using DesktopGuides.Core.Html;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Online;
using DesktopGuides.Infrastructure.Tests.FaultInjection;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class OnlineGuideSaverTests : IAsyncLifetime
{
    private const string SagaUrl = "https://gamefaqs.gamespot.com/ps5/100002-example-saga/faqs/78736/";
    private PublisherHarness harness = null!;
    private CaptureStore store = null!;
    private string work = null!;

    public async Task InitializeAsync()
    {
        harness = await PublisherHarness.CreateAsync();
        store = new CaptureStore(Path.Combine(harness.Sources.Root, "captures"), TimeProvider.System);
        work = Path.Combine(harness.Sources.Root, "work");
    }

    public async Task DisposeAsync() => await harness.DisposeAsync();

    private OnlineGuideSaver Saver(GuideImportPublisher? publisher = null) =>
        new(harness.Repository, publisher ?? harness.Publisher(), harness.Validator, work, TimeProvider.System);

    private static GuidePageReading Saga(string fixture, string slug) =>
        new GameFaqsAdapter().Read(new Uri(SagaUrl + slug), P3Fixtures.Read($"gamefaqs/html-slug-{fixture}.html"))!;

    private CaptureDraft Draft(params GuidePageReading[] pages)
    {
        CaptureDraft draft = store.Begin(harness.Game.Id, pages[0]);
        foreach (GuidePageReading page in pages) draft.Add(page, new Dictionary<Uri, string>());
        return draft;
    }

    private CaptureDraft AllThree(string cairnNote = "Notes for the Cairn page.") => Draft(
        Saga("intro", "introduction"),
        Saga("cairn", "cairn") with { Content = Saga("cairn", "cairn").Content.Replace("Notes for the Cairn page.", cairnNote) },
        Saga("fishing", "fishing"));

    private Task<OnlineSaveResult> SaveAsync(CaptureDraft draft, bool allowFormatChange = false, OnlineGuideSaver? saver = null) =>
        (saver ?? Saver()).SaveAsync(draft, allowFormatChange, null, CancellationToken.None);

    [Fact]
    public async Task AFirstSaveAddsAGuideWithItsSource()
    {
        if (!OperatingSystem.IsWindows()) return;
        CaptureDraft draft = AllThree();
        OnlineSaveResult.Added added = Assert.IsType<OnlineSaveResult.Added>(await SaveAsync(draft));

        Assert.Equal(3, (await harness.Repository.GetGuidePagesAsync(added.GuideId)).Count);
        GuideSource source = (await harness.Repository.GetGuideSourceAsync(added.GuideId))!;
        Assert.Equal((SourceSite.GameFaqs, "78736", SagaUrl + "introduction"), (source.Site, source.ItemId, source.Url));
        Assert.Equal(("Guide Writer", "Example's Saga Walkthrough"), (source.Author, source.SourceTitle));
        Assert.False(source.IsPartial);
        Assert.Equal("Example's Saga Walkthrough", (await harness.Repository.GetGuideAsync(added.GuideId))!.Title);
        Assert.Null(store.Find(harness.Game.Id, SourceSite.GameFaqs, "78736"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(work));
    }

    [Fact]
    public async Task ASecondSaveOfTheSameCopyIsUpToDate()
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid id = Assert.IsType<OnlineSaveResult.Added>(await SaveAsync(AllThree())).GuideId;
        Assert.Equal(new OnlineSaveResult.UpToDate(id), await SaveAsync(AllThree()));
    }

    // Review Focus 5.
    [Fact]
    public async Task ASecondSaveKeepsThePagePathsAndThePlace()
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid id = Assert.IsType<OnlineSaveResult.Added>(await SaveAsync(AllThree())).GuideId;
        string place = ReaderLocationCodec.Serialize(new ReaderLocation(
            GuideFormat.Html, ReaderLocationCodec.CurrentVersion, (await harness.Repository.GetGuideAsync(id))!.ContentSha256,
            new HtmlPosition("cairn.html", "cairn", "Notes for the", 0, 0.5), 0.5));

        Assert.Equal(new OnlineSaveResult.Replaced(id), await SaveAsync(AllThree("Notes for the Cairn page, revised.")));

        Guide replaced = (await harness.Repository.GetGuideAsync(id))!;
        string[] documents = (await harness.Repository.GetGuidePagesAsync(id)).Select(page => page.RequestPath).ToArray();
        Assert.Equal(["guide.html", "cairn.html", "fishing.html"], documents);
        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(place, GuideFormat.Html, replaced.ContentSha256, documents);
        Assert.Equal(LocationDecodeStatus.ContentChanged, decoded.Status);
        Assert.Equal(RestoreKind.Context, ReaderLocationCodec.RestoreCandidates(decoded)[0]);
    }

    [Fact]
    public async Task APartialSaveRecordsItsPages()
    {
        if (!OperatingSystem.IsWindows()) return;
        CaptureDraft draft = Draft(Saga("intro", "introduction"), Saga("cairn", "cairn"));
        Guid id = Assert.IsType<OnlineSaveResult.Added>(await SaveAsync(draft)).GuideId;
        GuideSource source = (await harness.Repository.GetGuideSourceAsync(id))!;
        Assert.Equal((2, 3), (source.CapturedPages, source.TotalPages));
    }

    [Fact]
    public async Task ATextToHtmlChangeAsksFirst()
    {
        if (!OperatingSystem.IsWindows()) return;
        GuidePageReading text = new GameFaqsAdapter().Read(new Uri(SagaUrl + "introduction"),
            P3Fixtures.Read("gamefaqs/text-faq.html"))!;
        Guid id = Assert.IsType<OnlineSaveResult.Added>(await SaveAsync(Draft(text))).GuideId;
        CaptureDraft html = AllThree();

        Assert.Equal(new OnlineSaveResult.NeedsFormatChange(id, GuideFormat.Txt, GuideFormat.Html), await SaveAsync(html));
        Assert.NotNull(store.Find(harness.Game.Id, SourceSite.GameFaqs, "78736"));
        Assert.Equal(new OnlineSaveResult.Replaced(id), await SaveAsync(html, allowFormatChange: true));
        Assert.Equal(GuideFormat.Html, (await harness.Repository.GetGuideAsync(id))!.Format);
    }

    // TR22.1.
    [Fact]
    public async Task AFailedSaveLeavesTheLibraryAndTheDraft()
    {
        if (!OperatingSystem.IsWindows()) return;
        CaptureDraft draft = AllThree();
        LibrarySnapshot before = LibrarySnapshot.Capture(harness.Paths);
        OnlineGuideSaver failing = Saver(harness.Publisher(createStagedFile: _ => throw new IOException("Injected.")));

        await Assert.ThrowsAsync<GuideImportException>(() => SaveAsync(draft, saver: failing));

        SnapshotAssert.Unchanged(before, LibrarySnapshot.Capture(harness.Paths));
        Assert.NotNull(store.Find(harness.Game.Id, SourceSite.GameFaqs, "78736"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(work));
    }
}
