using DesktopGuides.Core.Import;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class ImportFaultMatrixTests : IAsyncLifetime
{
    private FaultFixture fixture = null!;
    private LibrarySnapshot before = null!;
    private IReadOnlyList<FileFingerprint> sources = null!;

    public async Task InitializeAsync()
    {
        fixture = await FaultFixture.CreateAsync();
        before = fixture.Capture();
        sources = FileFingerprint.Of(fixture.Sources.Root);
    }

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private static ImportCheckpoint Point(string name) => Enum.Parse<ImportCheckpoint>(name);

    /// <summary>Nothing in the library changed, and the source files were never touched.</summary>
    private void AssertNoTrace()
    {
        SnapshotAssert.Unchanged(before, fixture.Capture());
        Assert.Equal(sources, FileFingerprint.Of(fixture.Sources.Root));
    }

    private long FileOperationCount() =>
        Convert.ToInt64(fixture.Library.Scalar("SELECT COUNT(*) FROM FileOperations"));

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
        Assert.Equal(sources, FileFingerprint.Of(fixture.Sources.Root));
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
        AssertNoTrace();
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
        Assert.Equal(1, FileOperationCount());

        await fixture.RestartAsync();

        Assert.Equal(1, fixture.Library.Repository.LastStartupReconciliation!.ResolvedOperationCount);
        AssertNoTrace();
        await fixture.ImportAsync(fixture.Publisher());
        AssertPublished();
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

        AssertNoTrace();
    }

    [Theory]
    [InlineData("InCommit")]
    [InlineData("Published")]
    public async Task CancelOncePublishingStartsIsIgnored(string checkpoint)
    {
        using CancellationTokenSource cancel = new();
        List<ImportProgress> reports = [];

        Guid id = await fixture.ImportAsync(
            fixture.Publisher(FaultFixture.CancelAt(Point(checkpoint), cancel)), cancel.Token,
            new SyncProgress(reports.Add));

        Assert.Equal(fixture.ImportGuideId, id);
        AssertPublished();
        Assert.Equal(new ImportProgress(1, true), reports[^1]);
        Assert.All(reports.SkipLast(1), report => Assert.True(report is { Publishing: false, Fraction: < 1 }));
    }

    /// <summary>Staged files before <paramref name="failingFile"/> write normally; that one hits a full disk.</summary>
    private static Func<string, Stream> FullDiskOnFile(int failingFile)
    {
        int calls = 0;
        return path => ++calls < failingFile
            ? new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true)
            : new DiskFullStream();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AFullDiskLeavesNoTraceAndSaysSo(int failingFile)
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() =>
            fixture.ImportAsync(fixture.Publisher(createStagedFile: FullDiskOnFile(failingFile))));

        Assert.Equal(
            (ImportIssue.NotEnoughSpace, "There isn't enough free space to import this guide."),
            (error.Issue, error.Message));
        AssertNoTrace();
    }

    [Fact]
    public async Task ACrashMidCopyIsUndoneAtStartup()
    {
        await Assert.ThrowsAsync<GuideImportException>(() => fixture.ImportAsync(fixture.Publisher(
            createStagedFile: FullDiskOnFile(2), rollBack: FaultFixture.SkipImportRollBack)));
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(fixture.Paths.StagingRoot));

        await fixture.RestartAsync();

        AssertNoTrace();
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
        AssertNoTrace();
    }

    [Fact]
    public async Task AFailedRollBackKeepsTheRowAndStartupFinishesIt()
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() => fixture.ImportAsync(
            fixture.Publisher(FaultFixture.FaultAt(ImportCheckpoint.Copied), rollBack: (_, _) => throw new InjectedFault())));
        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        Assert.Equal("Import|Prepared", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));

        await fixture.RestartAsync();

        AssertNoTrace();
    }

    [Fact]
    public async Task AFailedRollBackAfterAFullDiskStillSaysNotEnoughSpace()
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() => fixture.ImportAsync(
            fixture.Publisher(createStagedFile: FullDiskOnFile(1), rollBack: (_, _) => throw new InjectedFault())));
        Assert.Equal(ImportIssue.NotEnoughSpace, error.Issue);
        Assert.Equal(1, FileOperationCount());

        await fixture.RestartAsync();

        AssertNoTrace();
    }

    private sealed class SyncProgress(Action<ImportProgress> report) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => report(value);
    }
}
