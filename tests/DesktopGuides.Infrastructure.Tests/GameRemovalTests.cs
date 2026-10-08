using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Infrastructure.Tests.Import;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class GameRemovalTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;
    private ManagedArtworkStore store = null!;
    private Game game = null!;
    private Guid walkthroughId;
    private Guid mapsId;
    private Guid otherGuideId;
    private string artworkFile = null!;
    private string otherArtworkFile = null!;
    private byte[] otherArtwork = null!;

    public async Task InitializeAsync()
    {
        library = await RemovalLibrary.CreateAsync();
        store = new ManagedArtworkStore(library.Paths);
        game = await AddLinkedAsync("Guided Game", "900500");
        walkthroughId = await library.AddGuideAsync(game.Id, "Walkthrough");
        mapsId = await library.AddGuideAsync(game.Id, "Maps");
        Game other = await AddLinkedAsync("Other Game", "900600");
        otherGuideId = await library.AddGuideAsync(other.Id, "Other");
        RemovalLibrary.WriteFile(Content(walkthroughId), "guide.txt", "walkthrough");
        RemovalLibrary.WriteFile(Content(mapsId), "index.html", "maps");
        RemovalLibrary.WriteFile(Content(mapsId), "images/map.png", "map");
        RemovalLibrary.WriteFile(Content(mapsId), "images/deep/key.png", "key");
        RemovalLibrary.WriteFile(Content(otherGuideId), "guide.txt", "other");
        artworkFile = store.ResolveFile(game.ArtworkRelativePath!)!;
        otherArtworkFile = store.ResolveFile(other.ArtworkRelativePath!)!;
        otherArtwork = File.ReadAllBytes(otherArtworkFile);
    }

    public async Task DisposeAsync() => await library.DisposeAsync();

    private async Task<Game> AddLinkedAsync(string title, string externalId)
    {
        Guid id = Guid.NewGuid();
        StoredArtwork cover = await store.StoreAsync(id, Artwork.TestImages.Png(4, 4), default);
        return await library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            id, title, "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, externalId, DateTimeOffset.UnixEpoch),
            GameMetadataJsonTests.Sample(), cover.RelativePath));
    }

    private string Content(Guid id) => library.Paths.GetGuideRoot(id);

    private GameRemover Remover(Action<RemovalCheckpoint>? checkpoint = null) =>
        new(library.Repository, library.Paths, store, checkpoint ?? (_ => { }));

    private string GameRows(Guid id) => library.Scalar($"SELECT COUNT(*) FROM Games WHERE Id = '{id:N}'");

    private string OperationCount() => library.Scalar("SELECT COUNT(*) FROM FileOperations");

    private int TrashEntries() =>
        Directory.EnumerateFileSystemEntries(library.Paths.TrashRoot).Count();

    private static Action<RemovalCheckpoint> FaultAt(RemovalCheckpoint fault) => reached =>
    {
        if (reached == fault) throw new InvalidOperationException("fault");
    };

    private void HoldTrashedMap(out FileStream held)
    {
        string trashed = Directory.EnumerateFiles(library.Paths.TrashRoot, "map.png", SearchOption.AllDirectories).Single();
        held = new FileStream(trashed, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private void AssertGameIntact()
    {
        Assert.Equal("1", GameRows(game.Id));
        Assert.Equal("1|1|1", library.RowsFor(walkthroughId));
        Assert.Equal("1|1|1", library.RowsFor(mapsId));
        Assert.Equal("walkthrough", File.ReadAllText(Path.Combine(Content(walkthroughId), "guide.txt")));
        Assert.Equal("maps", File.ReadAllText(Path.Combine(Content(mapsId), "index.html")));
        Assert.Equal("key", File.ReadAllText(Path.Combine(Content(mapsId), "images/deep/key.png")));
        Assert.True(File.Exists(artworkFile));
        Assert.Equal("0", OperationCount());
        Assert.Equal(0, TrashEntries());
    }

    private async Task AssertOtherGameKeptAsync()
    {
        Assert.Equal("1|1|1", library.RowsFor(otherGuideId));
        Assert.Equal("other", File.ReadAllText(Path.Combine(Content(otherGuideId), "guide.txt")));
        Assert.Equal(otherArtwork, File.ReadAllBytes(otherArtworkFile));
        Assert.NotNull(await library.Repository.FindLinkedGameAsync(ProviderGameLink.Igdb, "900600"));
    }

    // Describe

    [Fact]
    public async Task DescribeCountsTheGuidesAndTheirFiles() =>
        Assert.Equal(
            new GameRemovalPreview(game.Id, "Guided Game", 2, 4),
            await Remover().DescribeAsync(game.Id));

    [Fact]
    public async Task DescribeCountsZeroFilesForMissingContent()
    {
        Directory.Delete(Content(mapsId), true);

        Assert.Equal(
            new GameRemovalPreview(game.Id, "Guided Game", 2, 1),
            await Remover().DescribeAsync(game.Id));
    }

    [Fact]
    public async Task DescribeReturnsNullForAnUnknownGame() =>
        Assert.Null(await Remover().DescribeAsync(Guid.NewGuid()));

    [Fact]
    public async Task InvalidArgumentsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Remover().DescribeAsync(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => Remover().RemoveAsync(Guid.Empty, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Remover().RemoveAsync(game.Id, -1));
        AssertGameIntact();
    }

    [Fact]
    public async Task DescribeRefusesALinkInAnyGuidesTree()
    {
        if (!OperatingSystem.IsWindows()) return;
        string link = Path.Combine(Content(mapsId), "link");
        RemovalLibrary.CreateJunction(link, Path.Combine(library.Root, "outside"));
        try
        {
            GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
                () => Remover().DescribeAsync(game.Id));

            Assert.Equal(GameRemovalIssue.Unsafe, error.Issue);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    // Remove

    [Fact]
    public async Task RemoveDeletesTheGameItsGuidesAndTheirFiles()
    {
        List<RemovalCheckpoint> seen = [];

        GameRemovalResult result = await Remover(seen.Add).RemoveAsync(game.Id, 2);

        Assert.Equal(new GameRemovalResult(GameRemovalOutcome.Removed, false, null), result);
        Assert.Equal(
            [RemovalCheckpoint.Prepared, RemovalCheckpoint.TrashCreated, RemovalCheckpoint.MovedGuide,
             RemovalCheckpoint.MovedGuide, RemovalCheckpoint.Moved, RemovalCheckpoint.InCommit,
             RemovalCheckpoint.Committed, RemovalCheckpoint.BeforeArtworkDelete],
            seen);
        Assert.Equal("0", GameRows(game.Id));
        Assert.Null(await library.Repository.FindLinkedGameAsync(ProviderGameLink.Igdb, "900500"));
        Assert.Equal("0|0|0", library.RowsFor(walkthroughId));
        Assert.Equal("0|0|0", library.RowsFor(mapsId));
        Assert.False(Directory.Exists(Content(walkthroughId)));
        Assert.False(Directory.Exists(Content(mapsId)));
        Assert.False(File.Exists(artworkFile));
        Assert.False(Directory.Exists(Path.GetDirectoryName(artworkFile)));
        Assert.Equal(0, TrashEntries());
        Assert.Equal("0", OperationCount());
        await AssertOtherGameKeptAsync();
    }

    [Fact]
    public async Task RemoveClearsAResumeGuideThatWasTheGames()
    {
        await library.Repository.SaveSettingsAsync(
            await library.Repository.GetSettingsAsync() with { LastActiveGuideId = mapsId });

        await Remover().RemoveAsync(game.Id, 2);

        Assert.Null((await library.Repository.GetSettingsAsync()).LastActiveGuideId);
    }

    [Fact]
    public async Task RemoveKeepsAResumeGuideFromAnotherGame()
    {
        await library.Repository.SaveSettingsAsync(
            await library.Repository.GetSettingsAsync() with { LastActiveGuideId = otherGuideId });

        await Remover().RemoveAsync(game.Id, 2);

        Assert.Equal(otherGuideId, (await library.Repository.GetSettingsAsync()).LastActiveGuideId);
    }

    [Fact]
    public async Task AManualGameWithoutGuidesIsRemovedWithoutAnOperation()
    {
        Guid id = await library.AddGameAsync("Empty Manual Game");
        List<RemovalCheckpoint> seen = [];

        GameRemovalResult result = await Remover(seen.Add).RemoveAsync(id, 0);

        Assert.Equal(new GameRemovalResult(GameRemovalOutcome.Removed, false, null), result);
        Assert.Equal([RemovalCheckpoint.InCommit], seen);
        Assert.Null(await library.Repository.GetGameAsync(id));
        AssertGameIntact();
        await AssertOtherGameKeptAsync();
    }

    [Fact]
    public async Task ALinkedGameWithoutGuidesLosesItsArtworkAndCanBeAddedAgain()
    {
        Game empty = await AddLinkedAsync("Empty Linked Game", "900700");
        string file = store.ResolveFile(empty.ArtworkRelativePath!)!;

        Assert.Equal(GameRemovalOutcome.Removed, (await Remover().RemoveAsync(empty.Id, 0)).Outcome);

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
        Assert.Null(await library.Repository.FindLinkedGameAsync(ProviderGameLink.Igdb, "900700"));
        Game again = await AddLinkedAsync("Empty Linked Game", "900700");
        Assert.NotEqual(empty.Id, again.Id);
    }

    [Fact]
    public async Task ArtworkThatCannotBeDeletedIsSweptAtTheNextStart()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (new FileStream(artworkFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(
                new GameRemovalResult(GameRemovalOutcome.Removed, false, null),
                await Remover().RemoveAsync(game.Id, 2));
            Assert.True(File.Exists(artworkFile));
        }

        await library.RestartAsync();

        Assert.False(File.Exists(artworkFile));
        await AssertOtherGameKeptAsync();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task AChangedCountReturnsTheFreshPreviewAndRemovesNothing(int expected)
    {
        GameRemovalResult result = await Remover().RemoveAsync(game.Id, expected);

        Assert.Equal(
            new GameRemovalResult(GameRemovalOutcome.CountChanged, false,
                new GameRemovalPreview(game.Id, "Guided Game", 2, 4)),
            result);
        AssertGameIntact();
    }

    [Fact]
    public async Task ACountChangedByAGuideRemovalIsReportedThenRemoved()
    {
        await new GuideRemover(library.Repository, library.Paths).RemoveAsync(mapsId);

        Assert.Equal(
            new GameRemovalResult(GameRemovalOutcome.CountChanged, false,
                new GameRemovalPreview(game.Id, "Guided Game", 1, 1)),
            await Remover().RemoveAsync(game.Id, 2));
        Assert.Equal(GameRemovalOutcome.Removed, (await Remover().RemoveAsync(game.Id, 1)).Outcome);
        Assert.Equal("0", GameRows(game.Id));
    }

    [Fact]
    public async Task AGuideWithMissingContentIsRemoved()
    {
        Directory.Delete(Content(mapsId), true);
        List<RemovalCheckpoint> seen = [];

        Assert.Equal(GameRemovalOutcome.Removed, (await Remover(seen.Add).RemoveAsync(game.Id, 2)).Outcome);

        Assert.Single(seen, point => point == RemovalCheckpoint.MovedGuide);
        Assert.Equal("0|0|0", library.RowsFor(mapsId));
        Assert.Equal(0, TrashEntries());
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task RemoveOfAnUnknownGameReturnsNotFound()
    {
        Assert.Equal(
            new GameRemovalResult(GameRemovalOutcome.NotFound, false, null),
            await Remover().RemoveAsync(Guid.NewGuid(), 0));

        AssertGameIntact();
    }

    [Fact]
    public async Task RemoveRefusesALinkBeforeJournaling()
    {
        if (!OperatingSystem.IsWindows()) return;
        string link = Path.Combine(Content(mapsId), "link");
        RemovalLibrary.CreateJunction(link, Path.Combine(library.Root, "outside"));
        try
        {
            GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
                () => Remover().RemoveAsync(game.Id, 2));

            Assert.Equal(GameRemovalIssue.Unsafe, error.Issue);
            Assert.Equal("0", OperationCount());
        }
        finally
        {
            Directory.Delete(link);
        }
        AssertGameIntact();
    }

    [Fact]
    public async Task AGuideClaimedByAnUnfinishedOperationIsLeftAlone()
    {
        await library.RunDeletion(journal => journal.Prepare(Guid.NewGuid(), mapsId));

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
            () => Remover().RemoveAsync(game.Id, 2));

        Assert.Equal(GameRemovalIssue.RestoreFailed, error.Issue);
        Assert.Equal("1", GameRows(game.Id));
        Assert.Equal("1|1|1", library.RowsFor(walkthroughId));
        Assert.Equal("1|1|1", library.RowsFor(mapsId));
        Assert.Equal("1", OperationCount());
        Assert.True(Directory.Exists(Content(walkthroughId)));
    }

    [Fact]
    public async Task AGuidePublishedIntoARemovedGameFailsAndLeavesNothing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GameRemover remover = new(harness.Repository, harness.Paths, new ManagedArtworkStore(harness.Paths));
        Assert.Equal(GameRemovalOutcome.Removed, (await remover.RemoveAsync(harness.Game.Id, 0)).Outcome);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task RemoveWithACancelledTokenWritesNothing()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Remover().RemoveAsync(game.Id, 2, new CancellationToken(canceled: true)));

        AssertGameIntact();
    }

    // Crash points

    [Theory]
    [InlineData("Prepared")]
    [InlineData("MovedGuide")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task AFaultBeforeTheCommitRestoresEveryGuide(string point)
    {
        // MovedGuide faults at the first of the two moves.
        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
            () => Remover(FaultAt(Enum.Parse<RemovalCheckpoint>(point))).RemoveAsync(game.Id, 2));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        AssertGameIntact();
        await AssertOtherGameKeptAsync();
    }

    [Fact]
    public async Task APrepareFailureChangesNothing()
    {
        library.Execute("""
            CREATE TRIGGER RefuseOperations BEFORE INSERT ON FileOperations
            BEGIN SELECT RAISE(ABORT, 'fault'); END
            """);

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
            () => Remover().RemoveAsync(game.Id, 2));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        Assert.IsType<SqliteException>(error.InnerException);
        AssertGameIntact();
    }

    [Fact]
    public async Task ContentHeldOpenFailsAndRestoresEveryGuide()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (new FileStream(Path.Combine(Content(mapsId), "images/map.png"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
                () => Remover().RemoveAsync(game.Id, 2));

            Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        }
        AssertGameIntact();
    }

    [Fact]
    public async Task AFailedRestoreKeepsThePreparedRowForStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileStream? held = null;
        try
        {
            GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() => Remover(reached =>
            {
                if (reached != RemovalCheckpoint.Moved) return;
                HoldTrashedMap(out FileStream stream);
                held = stream;
                throw new InvalidOperationException("fault");
            }).RemoveAsync(game.Id, 2));

            Assert.Equal(GameRemovalIssue.RestoreFailed, error.Issue);
            Assert.Equal("DeleteGame|Prepared", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
            Assert.Equal("1", GameRows(game.Id));
        }
        finally
        {
            held?.Dispose();
        }

        await library.RestartAsync();

        AssertGameIntact();
    }

    [Fact]
    public async Task RemoveAfterAFailedRestoreLeavesTheGameToStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileStream? held = null;
        try
        {
            await Assert.ThrowsAsync<GameRemovalException>(() => Remover(reached =>
            {
                if (reached != RemovalCheckpoint.Moved) return;
                HoldTrashedMap(out FileStream stream);
                held = stream;
                throw new InvalidOperationException("fault");
            }).RemoveAsync(game.Id, 2));
        }
        finally
        {
            held?.Dispose();
        }

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
            () => Remover().RemoveAsync(game.Id, 2));

        Assert.Equal(GameRemovalIssue.RestoreFailed, error.Issue);
        Assert.Equal("DeleteGame|Prepared", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
        Assert.Equal("1", GameRows(game.Id));

        await library.RestartAsync();

        AssertGameIntact();
    }

    [Fact]
    public async Task AFailedCleanupStillRemovesTheGame()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileStream? held = null;
        try
        {
            GameRemovalResult result = await Remover(reached =>
            {
                if (reached != RemovalCheckpoint.Committed) return;
                HoldTrashedMap(out FileStream stream);
                held = stream;
            }).RemoveAsync(game.Id, 2);

            Assert.Equal(new GameRemovalResult(GameRemovalOutcome.Removed, true, null), result);
            Assert.Equal("0", GameRows(game.Id));
            Assert.Equal("0|0|0", library.RowsFor(mapsId));
            Assert.Equal("DeleteGame|Committed", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
            Assert.Single(Directory.EnumerateFileSystemEntries(library.Paths.TrashRoot));
            Assert.False(File.Exists(artworkFile));
        }
        finally
        {
            held?.Dispose();
        }

        await library.RestartAsync();

        Assert.Equal(0, TrashEntries());
        Assert.Equal("0", OperationCount());
        await AssertOtherGameKeptAsync();
    }

    [Fact]
    public async Task TheCommitGuardKeepsAGameWhoseGuidesChanged()
    {
        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() => Remover(reached =>
        {
            if (reached == RemovalCheckpoint.Moved)
            {
                library.Execute($"UPDATE Guides SET GameId = '{game.Id:N}' WHERE Id = '{otherGuideId:N}'");
            }
        }).RemoveAsync(game.Id, 2));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        Assert.IsType<InvalidDataException>(error.InnerException);
        AssertGameIntact();
        Assert.Equal("1|1|1", library.RowsFor(otherGuideId));
    }

    [Fact]
    public async Task AFaultWhileCommittingAGameWithoutGuidesKeepsIt()
    {
        Guid id = await library.AddGameAsync("Empty Manual Game");

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
            () => Remover(FaultAt(RemovalCheckpoint.InCommit)).RemoveAsync(id, 0));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        Assert.NotNull(await library.Repository.GetGameAsync(id));
    }
}
