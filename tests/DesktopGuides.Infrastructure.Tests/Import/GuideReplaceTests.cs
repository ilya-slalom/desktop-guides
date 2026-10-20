using DesktopGuides.Core.Html;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Tests.FaultInjection;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

// S22: saving a guide again replaces its content in place.
public sealed class GuideReplaceTests : IAsyncLifetime
{
    private const string Entry = "Sample Quest - Guides Example.html";
    private PublisherHarness harness = null!;

    public async Task InitializeAsync() => harness = await PublisherHarness.CreateAsync();

    public async Task DisposeAsync() => await harness.DisposeAsync();

    private static GuideSource Source(string item = "13639") => new(
        SourceSite.GameFaqs, item, "https://gamefaqs.gamespot.com/faqs/" + item,
        "Author", "(c) Author", "Walkthrough",
        DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_000), null);

    private string WriteText(string name, string text)
    {
        string path = Path.Combine(harness.Sources.Root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private async Task<Guid> SaveTextAsync(string text, Guid? game = null, string title = "My walkthrough")
    {
        ImportManifest manifest = await harness.InspectAsync(WriteText($"{Guid.NewGuid():N}.txt", text), 437);
        return await harness.Publisher().PublishAsync(
            manifest, game ?? harness.Game.Id, title, false, null, CancellationToken.None, Source());
    }

    private async Task<GuideReplaceOutcome> ReplaceAsync(
        Guid guideId, ImportManifest manifest, GuideSource? source = null, bool allowFormatChange = false,
        GuideImportPublisher? publisher = null, CancellationToken token = default) =>
        await (publisher ?? harness.Publisher()).ReplaceAsync(
            manifest, guideId, source ?? Source(), allowFormatChange, null, token);

    private Task<ImportManifest> TextManifest(string text) =>
        harness.InspectAsync(WriteText($"{Guid.NewGuid():N}.txt", text), 437);

    [Fact]
    public async Task SavingTheSameCopyIsUpToDateAndChangesNothing()
    {
        Guid id = await SaveTextAsync("Chapter 1\nThe same text.\n");
        LibrarySnapshot before = LibrarySnapshot.Capture(harness.Paths);

        GuideReplaceOutcome outcome = await ReplaceAsync(id, await TextManifest("Chapter 1\nThe same text.\n"),
            Source() with { SourceTitle = "Renamed at the source" });

        Assert.Equal(GuideReplaceOutcome.UpToDate, outcome);
        SnapshotAssert.Unchanged(before, LibrarySnapshot.Capture(harness.Paths));
    }

    [Fact]
    public async Task ANewCopyReplacesTheContentAndKeepsTheReadersState()
    {
        Guid id = await SaveTextAsync("Chapter 1\nOld text.\n");
        Guide old = (await harness.Repository.GetGuideAsync(id))!;
        string locator = ReaderLocationCodec.Serialize(new ReaderLocation(
            GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, old.ContentSha256, new TextPosition(10, "Old text."), 0.4));
        await harness.Repository.SaveReadingLocationAsync(id, locator, 0.4);
        await harness.Repository.RecordGuideOpenedAsync(id, DateTimeOffset.FromUnixTimeMilliseconds(1_000));
        await harness.Repository.SetGuideCompletionAsync(id, DateTimeOffset.FromUnixTimeMilliseconds(2_000));
        await harness.Repository.AddBookmarkAsync(id, locator, "Boss");
        await harness.Repository.AddBookmarkAsync(id, locator, "Chest");
        await harness.Repository.SaveReaderPreferencesAsync(id, 1.25);
        string stateBefore = harness.Rows($"SELECT * FROM ReadingStates WHERE GuideId = '{id:N}'");
        string bookmarksBefore = harness.Rows($"SELECT * FROM Bookmarks WHERE GuideId = '{id:N}' ORDER BY Id");
        string preferencesBefore = harness.Rows($"SELECT * FROM ReaderPreferences WHERE GuideId = '{id:N}'");
        GuideSource newer = Source() with { SourceTitle = "Walkthrough v2", CapturedPages = 3, TotalPages = 5 };

        GuideReplaceOutcome outcome = await ReplaceAsync(id, await TextManifest("Chapter 1\nNew text, longer.\n"), newer);

        Assert.Equal(GuideReplaceOutcome.Replaced, outcome);
        Guide replaced = (await harness.Repository.GetGuideAsync(id))!;
        Assert.Equal("My walkthrough", replaced.Title);
        Assert.Equal(old.ImportedUtc, replaced.ImportedUtc);
        Assert.NotEqual(old.ContentSha256, replaced.ContentSha256);
        Assert.Equal("Chapter 1\nNew text, longer.\n",
            File.ReadAllText(harness.Paths.GetPlannedGuideFile(id, replaced.PrimaryRelativePath)));
        Assert.Equal(stateBefore, harness.Rows($"SELECT * FROM ReadingStates WHERE GuideId = '{id:N}'"));
        Assert.Equal(bookmarksBefore, harness.Rows($"SELECT * FROM Bookmarks WHERE GuideId = '{id:N}' ORDER BY Id"));
        Assert.Equal(preferencesBefore, harness.Rows($"SELECT * FROM ReaderPreferences WHERE GuideId = '{id:N}'"));
        Assert.Equal(newer, await harness.Repository.GetGuideSourceAsync(id));
        Assert.Equal(0, harness.Count("FileOperations"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness.Paths.StagingRoot));
        Assert.False(Directory.Exists(harness.Paths.TrashRoot) &&
                     Directory.EnumerateFileSystemEntries(harness.Paths.TrashRoot).Any());
    }

    private async Task<(string Full, string Short)> MultipageCopiesAsync()
    {
        string full = Path.Combine(harness.Sources.Root, "full");
        string shorter = Path.Combine(harness.Sources.Root, "short");
        P2Fixtures.CopyTree("html-multipage", full);
        P2Fixtures.CopyTree("html-multipage", shorter);
        File.Delete(Path.Combine(shorter, "Chapter 3 - Guides Example.html"));
        Directory.Delete(Path.Combine(shorter, "Chapter 3 - Guides Example_files"), recursive: true);
        return (Path.Combine(full, Entry), Path.Combine(shorter, Entry));
    }

    [Fact]
    public async Task AnHtmlGuideReplacesItsPagesAndAssets()
    {
        if (!OperatingSystem.IsWindows()) return;
        (string full, string shorter) = await MultipageCopiesAsync();
        Guid id = await harness.Publisher().PublishAsync(
            await harness.InspectAsync(full), harness.Game.Id, "Quest", false, null, CancellationToken.None, Source());
        Guid other = (await harness.Repository.AddGameAsync("Other", null, null)).Id;
        Guid fresh = await harness.Publisher().PublishAsync(
            await harness.InspectAsync(shorter), other, "Quest", false, null, CancellationToken.None);

        Assert.Equal(GuideReplaceOutcome.Replaced, await ReplaceAsync(id, await harness.InspectAsync(shorter)));

        Assert.Equal(
            (await harness.Repository.GetGuidePagesAsync(fresh)).Select(page => (page.Order, page.RequestPath)),
            (await harness.Repository.GetGuidePagesAsync(id)).Select(page => (page.Order, page.RequestPath)));
        Assert.Equal(
            (await harness.Repository.GetGuideAssetsAsync(fresh)).Select(asset => (asset.RequestPath, asset.Sha256)),
            (await harness.Repository.GetGuideAssetsAsync(id)).Select(asset => (asset.RequestPath, asset.Sha256)));
        Assert.Equal((await harness.Repository.GetGuideAsync(fresh))!.ContentSha256,
            (await harness.Repository.GetGuideAsync(id))!.ContentSha256);
    }

    // Review Focus 2.
    [Fact]
    public async Task APlaceOnARemovedPageDoesNotFailTheOpen()
    {
        if (!OperatingSystem.IsWindows()) return;
        (string full, string shorter) = await MultipageCopiesAsync();
        Guid id = await harness.Publisher().PublishAsync(
            await harness.InspectAsync(full), harness.Game.Id, "Quest", false, null, CancellationToken.None, Source());
        Guide old = (await harness.Repository.GetGuideAsync(id))!;
        IReadOnlyList<GuidePage> oldPages = await harness.Repository.GetGuidePagesAsync(id);
        string kept = oldPages.Single(page => page.RequestPath.StartsWith("Chapter 1", StringComparison.Ordinal)).RequestPath;
        string removed = oldPages.Single(page => page.RequestPath.StartsWith("Chapter 3", StringComparison.Ordinal)).RequestPath;
        string Place(string document) => ReaderLocationCodec.Serialize(new ReaderLocation(
            GuideFormat.Html, ReaderLocationCodec.CurrentVersion, old.ContentSha256,
            new HtmlPosition(document, "ending", null, 0, 0.5), 0.5));

        await ReplaceAsync(id, await harness.InspectAsync(shorter));

        Guide replaced = (await harness.Repository.GetGuideAsync(id))!;
        string[] documents = (await harness.Repository.GetGuidePagesAsync(id)).Select(page => page.RequestPath).ToArray();
        LocationDecodeResult onKept = ReaderLocationCodec.Deserialize(Place(kept), GuideFormat.Html, replaced.ContentSha256, documents);
        Assert.Equal(LocationDecodeStatus.ContentChanged, onKept.Status);
        Assert.Equal(RestoreKind.Context, ReaderLocationCodec.RestoreCandidates(onKept)[0]);
        Assert.Equal(LocationDecodeStatus.Invalid,
            ReaderLocationCodec.Deserialize(Place(removed), GuideFormat.Html, replaced.ContentSha256, documents).Status);
    }

    [Fact]
    public async Task AFormatChangeIsRefusedUnlessAllowed()
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid id = await SaveTextAsync("Chapter 1\nText.\n");
        string locator = ReaderLocationCodec.Serialize(new ReaderLocation(
            GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, (await harness.Repository.GetGuideAsync(id))!.ContentSha256,
            new TextPosition(0, "Chapter 1"), 0.1));
        await harness.Repository.SaveReadingLocationAsync(id, locator, 0.1);
        await harness.Repository.AddBookmarkAsync(id, locator, "Start");
        await harness.Repository.SaveReaderPreferencesAsync(id, 1.5);
        await harness.Repository.SetGuideCompletionAsync(id, DateTimeOffset.FromUnixTimeMilliseconds(5_000));
        (string full, _) = await MultipageCopiesAsync();
        ImportManifest html = await harness.InspectAsync(full);
        LibrarySnapshot before = LibrarySnapshot.Capture(harness.Paths);

        GuideImportException refused = await Assert.ThrowsAsync<GuideImportException>(() => ReplaceAsync(id, html));
        Assert.Equal(ImportIssue.FormatChanged, refused.Issue);
        SnapshotAssert.Unchanged(before, LibrarySnapshot.Capture(harness.Paths));

        Assert.Equal(GuideReplaceOutcome.Replaced, await ReplaceAsync(id, html, allowFormatChange: true));
        Assert.Equal(GuideFormat.Html, (await harness.Repository.GetGuideAsync(id))!.Format);
        Assert.Equal("null|null|5000", harness.Rows(
            $"SELECT IFNULL(LocatorJson, 'null') || '|' || IFNULL(EstimatedFraction, 'null') || '|' || CompletedUtcMs FROM ReadingStates WHERE GuideId = '{id:N}'"));
        Assert.Equal(0, harness.Count($"Bookmarks WHERE GuideId = '{id:N}'"));
        Assert.Equal("null|null", harness.Rows(
            $"SELECT IFNULL(TextScale, 'null') || '|' || IFNULL(TextView, 'null') FROM ReaderPreferences WHERE GuideId = '{id:N}'"));

        ImportManifest pdf = await harness.InspectAsync(harness.Sources.Copy("pdf-short.pdf", "short.pdf"));
        Assert.Equal(ImportIssue.FormatChanged,
            (await Assert.ThrowsAsync<GuideImportException>(() => ReplaceAsync(id, pdf, allowFormatChange: true))).Issue);
    }

    [Fact]
    public async Task AMapIsReplacedWithItsNewSize()
    {
        string first = Path.Combine(harness.Sources.Root, "map.png");
        File.WriteAllBytes(first, MapBytes.WholePng(640, 480));
        GuideSource map = Source("/Atlas/NES/Zelda-Hyrule.png") with { Site = SourceSite.VgMaps };
        Guid id = await harness.Publisher().PublishAsync(
            await harness.InspectAsync(first), harness.Game.Id, "Hyrule", false, null, CancellationToken.None, map);
        string second = Path.Combine(harness.Sources.Root, "map2.png");
        File.WriteAllBytes(second, MapBytes.WholePng(1024, 768));

        Assert.Equal(GuideReplaceOutcome.Replaced, await ReplaceAsync(id, await harness.InspectAsync(second), map));

        Guide replaced = (await harness.Repository.GetGuideAsync(id))!;
        Assert.Equal((1024, 768), (replaced.PixelWidth, replaced.PixelHeight));

        ImportManifest text = await TextManifest("Not a map.\n");
        Assert.Equal(ImportIssue.FormatChanged,
            (await Assert.ThrowsAsync<GuideImportException>(() => ReplaceAsync(id, text, map, allowFormatChange: true))).Issue);
    }

    // Review Focus 5.
    [Fact]
    public async Task TheSameItemInAnotherGameIsNotReplaced()
    {
        Guid other = (await harness.Repository.AddGameAsync("Other", null, null)).Id;
        Guid first = await SaveTextAsync("Chapter 1\nOld.\n");
        Guid second = await SaveTextAsync("Chapter 1\nOld.\n", other);
        IEnumerable<KeyValuePair<string, string>> SecondEntries() =>
            LibrarySnapshot.Capture(harness.Paths).Entries.Where(entry => entry.Key.Contains(second.ToString("N")));
        KeyValuePair<string, string>[] before = SecondEntries().ToArray();

        await ReplaceAsync(first, await TextManifest("Chapter 1\nNew.\n"));

        Assert.Equal(before, SecondEntries().ToArray());
        Assert.Equal(second, (await harness.Repository.FindSourcedGuideAsync(other, SourceSite.GameFaqs, "13639"))?.Id);
    }

    [Fact]
    public async Task CancellingBeforePublishingChangesNothing()
    {
        Guid id = await SaveTextAsync("Chapter 1\nOld.\n");
        ImportManifest manifest = await TextManifest("Chapter 1\nNew.\n");
        LibrarySnapshot before = LibrarySnapshot.Capture(harness.Paths);
        using CancellationTokenSource cancel = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReplaceAsync(id, manifest,
            publisher: harness.Publisher(replaceCheckpoint: FaultFixture.CancelAt(ReplaceCheckpoint.Copied, cancel)),
            token: cancel.Token));

        SnapshotAssert.Unchanged(before, LibrarySnapshot.Capture(harness.Paths));
    }

    private void AssertNoOperationLeft()
    {
        Assert.Equal(0, harness.Count("FileOperations"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness.Paths.StagingRoot));
        Assert.False(Directory.Exists(harness.Paths.TrashRoot) &&
                     Directory.EnumerateFileSystemEntries(harness.Paths.TrashRoot).Any());
    }

    // Review Focus 1: a crash before the commit, with no rollback in the
    // process, is undone by the next start.
    [Theory]
    [InlineData("Prepared")]
    [InlineData("Copied")]
    [InlineData("Verified")]
    [InlineData("OldMovedAside")]
    [InlineData("NewMovedIn")]
    [InlineData("InCommit")]
    public async Task AnInterruptedReplacementRollsBackOnTheNextStart(string checkpoint)
    {
        ReplaceCheckpoint point = Enum.Parse<ReplaceCheckpoint>(checkpoint);
        Guid id = await SaveTextAsync("Chapter 1\nOld.\n");
        ImportManifest manifest = await TextManifest("Chapter 1\nNew.\n");
        LibrarySnapshot before = LibrarySnapshot.Capture(harness.Paths);

        await Assert.ThrowsAnyAsync<Exception>(() => ReplaceAsync(id, manifest, publisher: harness.Publisher(
            replaceCheckpoint: FaultFixture.FaultAt(point), replaceRollBack: FaultFixture.SkipImportRollBack)));
        await harness.RestartAsync();

        SnapshotAssert.Unchanged(before, LibrarySnapshot.Capture(harness.Paths));
        AssertNoOperationLeft();
    }

    // A crash after the commit keeps the new copy; the next start removes the old one.
    [Theory]
    [InlineData("Committed")]
    [InlineData("Finished")]
    public async Task ACrashAfterTheCommitKeepsTheNewCopy(string checkpoint)
    {
        ReplaceCheckpoint point = Enum.Parse<ReplaceCheckpoint>(checkpoint);
        Guid id = await SaveTextAsync("Chapter 1\nOld.\n");
        string state = harness.Rows($"SELECT * FROM ReadingStates WHERE GuideId = '{id:N}'");
        ImportManifest manifest = await TextManifest("Chapter 1\nNew.\n");

        Assert.Equal(GuideReplaceOutcome.Replaced, await ReplaceAsync(id, manifest, publisher: harness.Publisher(
            replaceCheckpoint: FaultFixture.FaultAt(point))));
        await harness.RestartAsync();

        Guide replaced = (await harness.Repository.GetGuideAsync(id))!;
        Assert.Equal(manifest.Fingerprint, replaced.ContentSha256);
        Assert.Equal("Chapter 1\nNew.\n", File.ReadAllText(harness.Paths.GetPlannedGuideFile(id, replaced.PrimaryRelativePath)));
        Assert.Equal(state, harness.Rows($"SELECT * FROM ReadingStates WHERE GuideId = '{id:N}'"));
        AssertNoOperationLeft();
    }

    [Fact]
    public async Task AFailedReplacementRollsBackAtOnce()
    {
        Guid id = await SaveTextAsync("Chapter 1\nOld.\n");
        ImportManifest manifest = await TextManifest("Chapter 1\nNew.\n");
        LibrarySnapshot before = LibrarySnapshot.Capture(harness.Paths);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() => ReplaceAsync(id, manifest,
            publisher: harness.Publisher(replaceCheckpoint: FaultFixture.FaultAt(ReplaceCheckpoint.NewMovedIn))));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        SnapshotAssert.Unchanged(before, LibrarySnapshot.Capture(harness.Paths));
    }

    [Fact]
    public async Task AFullDiskDuringTheCopyChangesNothing()
    {
        Guid id = await SaveTextAsync("Chapter 1\nOld.\n");
        ImportManifest manifest = await TextManifest("Chapter 1\nNew.\n");
        LibrarySnapshot before = LibrarySnapshot.Capture(harness.Paths);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() => ReplaceAsync(id, manifest,
            publisher: harness.Publisher(createStagedFile: _ => new DiskFullStream())));

        Assert.Equal(ImportIssue.NotEnoughSpace, error.Issue);
        SnapshotAssert.Unchanged(before, LibrarySnapshot.Capture(harness.Paths));
    }
}
