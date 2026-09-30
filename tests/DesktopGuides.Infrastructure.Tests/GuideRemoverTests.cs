using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class GuideRemoverTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;
    private Guid gameId;
    private Guid guideId;
    private Guid siblingId;
    private Guid otherGameGuideId;

    public async Task InitializeAsync()
    {
        library = await RemovalLibrary.CreateAsync();
        gameId = await library.AddGameAsync("Removal Game");
        guideId = await library.AddGuideAsync(gameId, "Walkthrough");
        siblingId = await library.AddGuideAsync(gameId, "Sibling");
        otherGameGuideId = await library.AddGuideAsync(await library.AddGameAsync("Other Game"), "Other");
        RemovalLibrary.WriteFile(Content(guideId), "guide.txt", "walkthrough");
        RemovalLibrary.WriteFile(Content(guideId), "images/map.png", "map");
        RemovalLibrary.WriteFile(Content(guideId), "images/deep/key.png", "key");
        RemovalLibrary.WriteFile(Content(siblingId), "guide.txt", "sibling");
        RemovalLibrary.WriteFile(Content(otherGameGuideId), "guide.txt", "other");
    }

    public async Task DisposeAsync() => await library.DisposeAsync();

    private string Content(Guid id) => library.Paths.GetGuideRoot(id);

    private GuideRemover Remover(Action<RemovalCheckpoint>? checkpoint = null) =>
        new(library.Repository, library.Paths, checkpoint ?? (_ => { }));

    [Fact]
    public async Task DescribeNamesTheGuideAndCountsItsFiles()
    {
        GuideRemovalPreview? preview = await Remover().DescribeAsync(guideId);

        Assert.Equal(new GuideRemovalPreview(guideId, gameId, "Walkthrough", 3), preview);
    }

    [Fact]
    public async Task DescribeCountsZeroFilesWhenTheContentIsMissing()
    {
        Directory.Delete(Content(guideId), true);

        GuideRemovalPreview? preview = await Remover().DescribeAsync(guideId);

        Assert.Equal(0, preview?.FileCount);
    }

    [Fact]
    public async Task DescribeReturnsNullForAnUnknownGuide() =>
        Assert.Null(await Remover().DescribeAsync(Guid.NewGuid()));

    [Fact]
    public async Task DescribeRefusesALinkInsideTheTree()
    {
        if (!OperatingSystem.IsWindows()) return;
        string link = Path.Combine(Content(guideId), "link");
        RemovalLibrary.CreateJunction(link, Path.Combine(library.Root, "outside"));
        try
        {
            GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(
                () => Remover().DescribeAsync(guideId));

            Assert.Equal(GuideRemovalIssue.Unsafe, error.Issue);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task DescribeRefusesAFileInPlaceOfTheDirectory()
    {
        Directory.Delete(Content(guideId), true);
        File.WriteAllText(Content(guideId), "not a directory");

        GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(
            () => Remover().DescribeAsync(guideId));

        Assert.Equal(GuideRemovalIssue.Unsafe, error.Issue);
    }

    private string OperationCount() => library.Scalar("SELECT COUNT(*) FROM FileOperations");

    private static int TrashEntries(RemovalLibrary library) =>
        Directory.EnumerateFileSystemEntries(library.Paths.TrashRoot).Count();

    private void AssertGuideIntact()
    {
        Assert.Equal("1|1|1", library.RowsFor(guideId));
        Assert.Equal("walkthrough", File.ReadAllText(Path.Combine(Content(guideId), "guide.txt")));
        Assert.Equal("key", File.ReadAllText(Path.Combine(Content(guideId), "images/deep/key.png")));
        Assert.Equal("0", OperationCount());
        Assert.Equal(0, TrashEntries(library));
    }

    private void AssertNeighboursKept()
    {
        Assert.Equal("1|1|1", library.RowsFor(siblingId));
        Assert.Equal("1|1|1", library.RowsFor(otherGameGuideId));
        Assert.Equal("sibling", File.ReadAllText(Path.Combine(Content(siblingId), "guide.txt")));
        Assert.Equal("other", File.ReadAllText(Path.Combine(Content(otherGameGuideId), "guide.txt")));
    }

    [Fact]
    public async Task RemoveDeletesTheGuideItsStateAndItsFiles()
    {
        List<RemovalCheckpoint> seen = [];

        GuideRemovalResult result = await Remover(seen.Add).RemoveAsync(guideId);

        Assert.Equal(new GuideRemovalResult(GuideRemovalOutcome.Removed, false), result);
        Assert.Equal(
            [RemovalCheckpoint.Prepared, RemovalCheckpoint.Moved, RemovalCheckpoint.InCommit, RemovalCheckpoint.Committed],
            seen);
        Assert.Equal("0|0|0", library.RowsFor(guideId));
        Assert.False(Directory.Exists(Content(guideId)));
        Assert.Equal(0, TrashEntries(library));
        Assert.Equal("0", OperationCount());
        AssertNeighboursKept();
    }

    [Fact]
    public async Task RemoveOfABrokenGuideDeletesItsRows()
    {
        Directory.Delete(Content(guideId), true);

        GuideRemovalResult result = await Remover().RemoveAsync(guideId);

        Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
        Assert.Equal("0|0|0", library.RowsFor(guideId));
        Assert.Equal(0, TrashEntries(library));
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task RemoveOfAnUnknownGuideReturnsNotFound()
    {
        List<RemovalCheckpoint> seen = [];

        GuideRemovalResult result = await Remover(seen.Add).RemoveAsync(Guid.NewGuid());

        Assert.Equal(new GuideRemovalResult(GuideRemovalOutcome.NotFound, false), result);
        Assert.Empty(seen);
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task RemoveRefusesALinkBeforeJournaling()
    {
        if (!OperatingSystem.IsWindows()) return;
        string outside = Path.Combine(library.Root, "outside");
        string link = Path.Combine(Content(guideId), "link");
        RemovalLibrary.CreateJunction(link, outside);
        RemovalLibrary.WriteFile(outside, "keep.txt", "outside");
        List<RemovalCheckpoint> seen = [];
        try
        {
            GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(
                () => Remover(seen.Add).RemoveAsync(guideId));

            Assert.Equal(GuideRemovalIssue.Unsafe, error.Issue);
            Assert.Empty(seen);
            Assert.Equal("1|1|1", library.RowsFor(guideId));
            Assert.Equal("0", OperationCount());
            Assert.Equal("outside", File.ReadAllText(Path.Combine(outside, "keep.txt")));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task AFaultBeforeTheCommitRestoresTheGuide(string point)
    {
        RemovalCheckpoint fault = Enum.Parse<RemovalCheckpoint>(point);

        GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(() => Remover(reached =>
        {
            if (reached == fault) throw new InvalidOperationException("fault");
        }).RemoveAsync(guideId));

        Assert.Equal(GuideRemovalIssue.Failed, error.Issue);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        AssertGuideIntact();
        AssertNeighboursKept();
    }

    [Fact]
    public async Task ContentHeldOpenFailsAndKeepsTheGuide()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (new FileStream(Path.Combine(Content(guideId), "images/map.png"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(
                () => Remover().RemoveAsync(guideId));

            Assert.Equal(GuideRemovalIssue.Failed, error.Issue);
        }
        AssertGuideIntact();
    }

    [Fact]
    public async Task AFailedRestoreKeepsThePreparedRowForStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileStream? held = null;
        try
        {
            GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(() => Remover(reached =>
            {
                if (reached != RemovalCheckpoint.Moved) return;
                string trashed = Directory.EnumerateFiles(library.Paths.TrashRoot, "map.png", SearchOption.AllDirectories).Single();
                held = new FileStream(trashed, FileMode.Open, FileAccess.Read, FileShare.Read);
                throw new InvalidOperationException("fault");
            }).RemoveAsync(guideId));

            Assert.Equal(GuideRemovalIssue.RestoreFailed, error.Issue);
            Assert.Equal("DeleteGuide|Prepared", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
            Assert.Equal("1|1|1", library.RowsFor(guideId));
        }
        finally
        {
            held?.Dispose();
        }

        await library.RestartAsync();

        AssertGuideIntact();
    }

    [Fact]
    public async Task RemoveAfterAFailedRestoreLeavesTheGuideToStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileStream? held = null;
        try
        {
            await Assert.ThrowsAsync<GuideRemovalException>(() => Remover(reached =>
            {
                if (reached != RemovalCheckpoint.Moved) return;
                string trashed = Directory.EnumerateFiles(library.Paths.TrashRoot, "map.png", SearchOption.AllDirectories).Single();
                held = new FileStream(trashed, FileMode.Open, FileAccess.Read, FileShare.Read);
                throw new InvalidOperationException("fault");
            }).RemoveAsync(guideId));
        }
        finally
        {
            held?.Dispose();
        }

        GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(
            () => Remover().RemoveAsync(guideId));

        Assert.Equal(GuideRemovalIssue.RestoreFailed, error.Issue);
        Assert.Equal("DeleteGuide|Prepared", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
        Assert.Equal("1|1|1", library.RowsFor(guideId));

        await library.RestartAsync();

        AssertGuideIntact();
    }

    [Fact]
    public async Task AFailedCleanupStillRemovesTheGuide()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileStream? held = null;
        GuideRemovalResult result;
        try
        {
            result = await Remover(reached =>
            {
                if (reached != RemovalCheckpoint.Committed) return;
                string trashed = Directory.EnumerateFiles(library.Paths.TrashRoot, "map.png", SearchOption.AllDirectories).Single();
                held = new FileStream(trashed, FileMode.Open, FileAccess.Read, FileShare.Read);
            }).RemoveAsync(guideId);

            Assert.Equal(new GuideRemovalResult(GuideRemovalOutcome.Removed, true), result);
            Assert.Equal("0|0|0", library.RowsFor(guideId));
            Assert.Equal("DeleteGuide|Committed", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
            string operationRoot = Assert.Single(Directory.EnumerateFileSystemEntries(library.Paths.TrashRoot));
            Assert.Equal(guideId.ToString("N"), Path.GetFileName(Assert.Single(Directory.EnumerateFileSystemEntries(operationRoot))));
            AssertNeighboursKept();
        }
        finally
        {
            held?.Dispose();
        }

        await library.RestartAsync();

        Assert.Equal(0, TrashEntries(library));
        Assert.Equal("0", OperationCount());
        AssertNeighboursKept();
    }

    [Fact]
    public async Task RemoveWithACancelledTokenWritesNothing()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Remover().RemoveAsync(guideId, new CancellationToken(canceled: true)));

        AssertGuideIntact();
    }
}
