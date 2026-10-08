using DesktopGuides.Core.Import;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class ImportFaultMatrixTests : IAsyncLifetime
{
    private FaultFixture fixture = null!;
    private LibrarySnapshot before = null!;

    public async Task InitializeAsync()
    {
        fixture = await FaultFixture.CreateAsync();
        before = fixture.Capture();
    }

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private static ImportCheckpoint Point(string name) => Enum.Parse<ImportCheckpoint>(name);

    /// <summary>The new guide's 7 rows and 7 file-tree entries were added, and nothing else changed.</summary>
    private void AssertPublished()
    {
        LibrarySnapshot after = fixture.Capture();
        string id = fixture.ImportGuideId.ToString("N");
        string[] added = after.KeysContaining(fixture.ImportGuideId).ToArray();
        Assert.Equal(
            new[]
            {
                $"db:GuideAssets/{id}/guide.html", $"db:GuideAssets/{id}/images/map.png",
                $"db:GuideAssets/{id}/styles/main.css", $"db:GuideAssets/{id}/styles/palette.css",
                $"db:Guides/{id}", $"db:ReaderPreferences/{id}", $"db:ReadingStates/{id}",
                $"fs:library/content/{id}", $"fs:library/content/{id}/guide.html",
                $"fs:library/content/{id}/images", $"fs:library/content/{id}/images/map.png",
                $"fs:library/content/{id}/styles", $"fs:library/content/{id}/styles/main.css",
                $"fs:library/content/{id}/styles/palette.css",
            },
            added.Order(StringComparer.Ordinal));
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, after), added, []);
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("Copied")]
    [InlineData("Verified")]
    [InlineData("MovedToContent")]
    [InlineData("Renamed")]
    [InlineData("InCommit")]
    public async Task ThrowBeforeTheCommitLeavesNoTrace(string checkpoint)
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() =>
            fixture.ImportAsync(fixture.Publisher(FaultFixture.FaultAt(Point(checkpoint)))));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("Copied")]
    [InlineData("Verified")]
    [InlineData("MovedToContent")]
    [InlineData("Renamed")]
    [InlineData("InCommit")]
    public async Task CrashBeforeTheCommitIsUndoneAtStartup(string checkpoint)
    {
        await Assert.ThrowsAsync<GuideImportException>(() => fixture.ImportAsync(
            fixture.Publisher(FaultFixture.FaultAt(Point(checkpoint)), rollBack: FaultFixture.SkipImportRollBack)));

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ThrowAfterTheCommitKeepsThePublishedGuide()
    {
        await Assert.ThrowsAsync<InjectedFault>(() =>
            fixture.ImportAsync(fixture.Publisher(FaultFixture.FaultAt(ImportCheckpoint.Published))));

        AssertPublished();
    }

    [Fact]
    public async Task CrashAfterTheCommitKeepsThePublishedGuideAtStartup()
    {
        await Assert.ThrowsAsync<InjectedFault>(() => fixture.ImportAsync(fixture.Publisher(
            FaultFixture.FaultAt(ImportCheckpoint.Published), rollBack: FaultFixture.SkipImportRollBack)));

        await fixture.RestartAsync();

        AssertPublished();
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("Copied")]
    [InlineData("Verified")]
    [InlineData("MovedToContent")]
    [InlineData("Renamed")]
    public async Task CancelBeforePublishingRollsBack(string checkpoint)
    {
        using CancellationTokenSource cancel = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.ImportAsync(
            fixture.Publisher(FaultFixture.CancelAt(Point(checkpoint), cancel)), cancel.Token));

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("InCommit")]
    [InlineData("Published")]
    public async Task CancelOncePublishingStartsIsIgnored(string checkpoint)
    {
        using CancellationTokenSource cancel = new();

        Guid id = await fixture.ImportAsync(
            fixture.Publisher(FaultFixture.CancelAt(Point(checkpoint), cancel)), cancel.Token);

        Assert.Equal(fixture.ImportGuideId, id);
        AssertPublished();
    }

    /// <summary>The first staged file writes normally; the second hits a full disk.</summary>
    private static Func<string, Stream> FullDiskOnSecondFile()
    {
        int calls = 0;
        return path => ++calls == 1
            ? new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true)
            : new DiskFullStream();
    }

    [Fact]
    public async Task ACopyFaultAfterTheFirstFileLeavesNoTrace()
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() =>
            fixture.ImportAsync(fixture.Publisher(createStagedFile: FullDiskOnSecondFile())));

        Assert.Equal(ImportIssue.NotEnoughSpace, error.Issue);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ACrashMidCopyIsUndoneAtStartup()
    {
        await Assert.ThrowsAsync<GuideImportException>(() => fixture.ImportAsync(fixture.Publisher(
            createStagedFile: FullDiskOnSecondFile(), rollBack: FaultFixture.SkipImportRollBack)));
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(fixture.Paths.StagingRoot));

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("FileOperations")]
    [InlineData("Guides")]
    public async Task ARefusedDatabaseInsertLeavesNoTrace(string table)
    {
        fixture.Library.Execute($"""
            CREATE TRIGGER RefuseInsert BEFORE INSERT ON {table}
            BEGIN SELECT RAISE(ABORT, 'fault'); END
            """);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() =>
            fixture.ImportAsync(fixture.Publisher()));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task AFailedRollBackKeepsTheRowAndStartupFinishesIt()
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() => fixture.ImportAsync(
            fixture.Publisher(FaultFixture.FaultAt(ImportCheckpoint.Copied), rollBack: (_, _) => throw new InjectedFault())));
        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        Assert.Equal("Import|Prepared", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }
}
