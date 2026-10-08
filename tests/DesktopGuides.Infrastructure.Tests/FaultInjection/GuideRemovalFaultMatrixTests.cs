using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class GuideRemovalFaultMatrixTests : IAsyncLifetime
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

    private string[] RemovedKeys() =>
        before.KeysContaining(fixture.SubjectGuide).Append("db:Settings/LastActiveGuideId").ToArray();

    private void AssertRemoved() =>
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, fixture.Capture()), [], RemovedKeys());

    /// <summary>Removed, plus the guide's files under .trash/op and its Committed row.</summary>
    private void AssertCleanupPending()
    {
        LibrarySnapshot after = fixture.Capture();
        string row = after.Entries.Keys.Single(key => key.StartsWith("db:FileOperations/", StringComparison.Ordinal));
        string op = row["db:FileOperations/".Length..];
        Assert.Contains("Phase=Committed", after.Entries[row]);
        string guide = fixture.SubjectGuide.ToString("N");
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, after),
            [row, $"fs:library/.trash/{op}", $"fs:library/.trash/{op}/{guide}", $"fs:library/.trash/{op}/{guide}/guide.txt"],
            RemovedKeys());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("TrashCreated")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task ThrowBeforeTheCommitRestoresTheGuide(string checkpoint)
    {
        GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(() =>
            fixture.GuideRemover(FaultFixture.FaultAt(Point(checkpoint))).RemoveAsync(fixture.SubjectGuide));

        Assert.Equal(GuideRemovalIssue.Failed, error.Issue);
        Assert.IsType<InjectedFault>(error.InnerException);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("TrashCreated")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task CrashBeforeTheCommitIsRestoredAtStartup(string checkpoint)
    {
        await Assert.ThrowsAsync<GuideRemovalException>(() => fixture.GuideRemover(
            FaultFixture.FaultAt(Point(checkpoint)), rollBack: FaultFixture.SkipDeletionRollBack)
            .RemoveAsync(fixture.SubjectGuide));

        await fixture.RestartAsync();

        Assert.Equal(new StartupReconciliationReport(1, 0), fixture.Library.Repository.LastStartupReconciliation);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("TrashCreated")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task AFailedRollBackKeepsThePreparedRowForStartup(string checkpoint)
    {
        GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(() => fixture.GuideRemover(
            FaultFixture.FaultAt(Point(checkpoint)), rollBack: FaultFixture.FailDeletionRollBack)
            .RemoveAsync(fixture.SubjectGuide));
        Assert.Equal(GuideRemovalIssue.RestoreFailed, error.Issue);
        Assert.Equal("DeleteGuide|Prepared", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ThrowAtCommittedLeavesOnlyAKnownTrashEntry()
    {
        GuideRemovalResult result = await fixture.GuideRemover(FaultFixture.FaultAt(RemovalCheckpoint.Committed))
            .RemoveAsync(fixture.SubjectGuide);

        Assert.Equal(new GuideRemovalResult(GuideRemovalOutcome.Removed, true), result);
        AssertCleanupPending();
        await fixture.RestartAsync();
        AssertRemoved();
    }

    [Fact]
    public async Task CrashAfterTheCommitIsFinishedAtStartup()
    {
        await fixture.GuideRemover(finish: FaultFixture.SkipFinish).RemoveAsync(fixture.SubjectGuide);
        AssertCleanupPending();

        await fixture.RestartAsync();

        AssertRemoved();
    }

    [Fact]
    public async Task ARefusedDeleteRestoresTheGuide()
    {
        fixture.Library.Execute("""
            CREATE TRIGGER RefuseDelete BEFORE DELETE ON Guides
            BEGIN SELECT RAISE(ABORT, 'fault'); END
            """);

        GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(() =>
            fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide));

        Assert.Equal(GuideRemovalIssue.Failed, error.Issue);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ANormalRemovalRemovesExactlyTheGuide()
    {
        Assert.Equal(new GuideRemovalResult(GuideRemovalOutcome.Removed, false),
            await fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide));

        AssertRemoved();
    }

    [Fact]
    public async Task CancelBeforeTheCallChangesNothing()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide, cancel.Token));

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task CancelWhileAnotherOperationHoldsTheGateChangesNothing()
    {
        TaskCompletionSource release = new();
        Task<bool> holder = fixture.Library.Repository.RunExportAsync(async (_, _) =>
        {
            await release.Task;
            return true;
        }, CancellationToken.None);
        using CancellationTokenSource cancel = new();

        Task<GuideRemovalResult> waiting = fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide, cancel.Token);
        await Task.Delay(100);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.SetResult();
        await holder;
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task CancelAfterPreparedStillRemoves()
    {
        using CancellationTokenSource cancel = new();

        GuideRemovalResult result = await fixture.GuideRemover(FaultFixture.CancelAt(RemovalCheckpoint.Prepared, cancel))
            .RemoveAsync(fixture.SubjectGuide, cancel.Token);

        Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
        AssertRemoved();
    }
}
