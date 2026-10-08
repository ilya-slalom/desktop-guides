using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class GameRemovalFaultMatrixTests : IAsyncLifetime
{
    private FaultFixture fixture = null!;
    private LibrarySnapshot before = null!;

    public async Task InitializeAsync()
    {
        fixture = await FaultFixture.CreateAsync();
        before = fixture.Capture();
    }

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private static RemovalCheckpoint Point(string name) => Enum.Parse<RemovalCheckpoint>(name);

    private Task<GameRemovalResult> Remove(GameRemover remover, CancellationToken token = default) =>
        remover.RemoveAsync(fixture.SubjectGame, 2, token);

    private bool IsArtwork(string key) =>
        key.StartsWith($"fs:library/artwork/{fixture.SubjectGame:N}", StringComparison.Ordinal);

    private string[] RemovedKeys() => before.KeysContaining(fixture.SubjectGame)
        .Concat(before.KeysContaining(fixture.SubjectGuide))
        .Concat(before.KeysContaining(fixture.SiblingGuide))
        .Append("db:Settings/LastActiveGuideId")
        .Distinct().ToArray();

    private void AssertRemoved() =>
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, fixture.Capture()), [], RemovedKeys());

    /// <summary>Removed, plus both guides' files under .trash/op and the Committed row.</summary>
    private void AssertCleanupPending()
    {
        LibrarySnapshot after = fixture.Capture();
        string row = after.Entries.Keys.Single(key => key.StartsWith("db:FileOperations/", StringComparison.Ordinal));
        string op = row["db:FileOperations/".Length..];
        Assert.Contains("Phase=Committed", after.Entries[row]);
        IEnumerable<string> trashed = before.Entries.Keys
            .Where(key => key.StartsWith("fs:library/content/", StringComparison.Ordinal) &&
                (key.Contains(fixture.SubjectGuide.ToString("N")) || key.Contains(fixture.SiblingGuide.ToString("N"))))
            .Select(key => $"fs:library/.trash/{op}/" + key["fs:library/content/".Length..]);
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, after),
            trashed.Append(row).Append($"fs:library/.trash/{op}"), RemovedKeys());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("TrashCreated")]
    [InlineData("MovedGuide")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task ThrowBeforeTheCommitRestoresTheGame(string checkpoint)
    {
        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() =>
            Remove(fixture.GameRemover(FaultFixture.FaultAt(Point(checkpoint)))));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        Assert.IsType<InjectedFault>(error.InnerException);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("TrashCreated")]
    [InlineData("MovedGuide")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task CrashBeforeTheCommitIsRestoredAtStartup(string checkpoint)
    {
        await Assert.ThrowsAsync<GameRemovalException>(() => Remove(fixture.GameRemover(
            FaultFixture.FaultAt(Point(checkpoint)), rollBack: FaultFixture.SkipDeletionRollBack)));

        await fixture.RestartAsync();
        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task AFailedRollBackKeepsThePreparedRowForStartup()
    {
        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() => Remove(fixture.GameRemover(
            FaultFixture.FaultAt(RemovalCheckpoint.Moved), rollBack: FaultFixture.FailDeletionRollBack)));
        Assert.Equal(GameRemovalIssue.RestoreFailed, error.Issue);
        Assert.Equal("DeleteGame|Prepared", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ThrowAtCommittedLeavesOnlyAKnownTrashEntry()
    {
        GameRemovalResult result = await Remove(fixture.GameRemover(FaultFixture.FaultAt(RemovalCheckpoint.Committed)));

        Assert.Equal((GameRemovalOutcome.Removed, true), (result.Outcome, result.CleanupPending));
        AssertCleanupPending();
        await fixture.RestartAsync();
        AssertRemoved();
    }

    [Fact]
    public async Task CrashAfterTheCommitIsFinishedAtStartup()
    {
        await Remove(fixture.GameRemover(finish: FaultFixture.SkipFinish));
        AssertCleanupPending();

        await fixture.RestartAsync();

        AssertRemoved();
    }

    [Fact]
    public async Task AFaultBeforeTheArtworkDeleteIsSweptAtStartup()
    {
        await Assert.ThrowsAsync<InjectedFault>(() =>
            Remove(fixture.GameRemover(FaultFixture.FaultAt(RemovalCheckpoint.BeforeArtworkDelete))));
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, fixture.Capture()), [],
            RemovedKeys().Where(key => !IsArtwork(key)));

        await fixture.RestartAsync();

        AssertRemoved();
    }

    [Theory]
    [InlineData("INSERT ON FileOperations")]
    [InlineData("DELETE ON Games")]
    public async Task ARefusedDatabaseWriteRestoresTheGame(string write)
    {
        fixture.Library.Execute($"""
            CREATE TRIGGER RefuseWrite BEFORE {write}
            BEGIN SELECT RAISE(ABORT, 'fault'); END
            """);

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() => Remove(fixture.GameRemover()));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        Assert.IsType<SqliteException>(error.InnerException);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ANormalRemovalRemovesExactlyTheGame()
    {
        GameRemovalResult result = await Remove(fixture.GameRemover());

        Assert.Equal(new GameRemovalResult(GameRemovalOutcome.Removed, false, null), result);
        AssertRemoved();
    }

    [Fact]
    public async Task CancelBeforeTheCallChangesNothing()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Remove(fixture.GameRemover(), cancel.Token));

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task CancelAfterPreparedStillRemoves()
    {
        using CancellationTokenSource cancel = new();

        GameRemovalResult result = await Remove(
            fixture.GameRemover(FaultFixture.CancelAt(RemovalCheckpoint.Prepared, cancel)), cancel.Token);

        Assert.Equal(GameRemovalOutcome.Removed, result.Outcome);
        AssertRemoved();
    }

    /// <summary>A linked game with artwork and no guides, added after the shared snapshot.</summary>
    private async Task<(Guid Game, LibrarySnapshot Before)> AddEmptyGameAsync()
    {
        Guid id = Guid.NewGuid();
        StoredArtwork cover = await fixture.Export.Store.StoreAsync(id, Artwork.TestImages.Png(3, 3), default);
        await fixture.Library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            id, "Empty Game", "PC", new ProviderGameLink(ProviderGameLink.Igdb, "900800", DateTimeOffset.UnixEpoch),
            GameMetadataJsonTests.Sample(), cover.RelativePath));
        return (id, fixture.Capture());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AGameWithoutGuidesSurvivesAFaultInItsCommit(bool restart)
    {
        (Guid game, LibrarySnapshot start) = await AddEmptyGameAsync();

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() =>
            fixture.GameRemover(FaultFixture.FaultAt(RemovalCheckpoint.InCommit)).RemoveAsync(game, 0));
        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        if (restart) await fixture.RestartAsync();

        SnapshotAssert.Unchanged(start, fixture.Capture());
    }

    [Fact]
    public async Task AGameWithoutGuidesHasItsArtworkSweptAfterAFault()
    {
        (Guid game, LibrarySnapshot start) = await AddEmptyGameAsync();

        await Assert.ThrowsAsync<InjectedFault>(() =>
            fixture.GameRemover(FaultFixture.FaultAt(RemovalCheckpoint.BeforeArtworkDelete)).RemoveAsync(game, 0));
        await fixture.RestartAsync();

        SnapshotAssert.Exactly(LibrarySnapshot.Diff(start, fixture.Capture()), [], start.KeysContaining(game));
    }

    [Fact]
    public async Task AGameWithoutGuidesIsRemovedExactly()
    {
        (Guid game, LibrarySnapshot start) = await AddEmptyGameAsync();

        await fixture.GameRemover().RemoveAsync(game, 0);

        SnapshotAssert.Exactly(LibrarySnapshot.Diff(start, fixture.Capture()), [], start.KeysContaining(game));
        (Guid again, _) = await AddEmptyGameAsync();
        Assert.NotEqual(game, again);
    }
}
