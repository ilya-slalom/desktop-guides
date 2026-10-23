using DesktopGuides.Core.Library;
using System.Text;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Import;
using DesktopGuides.Infrastructure.Online;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class OfflineCopyBuilderTests : IAsyncLifetime
{
    private const string SagaUrl = "https://gamefaqs.gamespot.com/ps5/100002-example-saga/faqs/78736/";
    private static readonly DateTimeOffset Saved = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private PublisherHarness harness = null!;
    private string folder = null!;

    public async Task InitializeAsync()
    {
        harness = await PublisherHarness.CreateAsync();
        folder = Path.Combine(harness.Sources.Root, "copy");
        Directory.CreateDirectory(folder);
    }

    public async Task DisposeAsync() => await harness.DisposeAsync();

    private static GuidePageReading Saga(string fixture, string slug) =>
        new GameFaqsAdapter().Read(new Uri(SagaUrl + slug), P3Fixtures.Read($"gamefaqs/html-slug-{fixture}.html"))!;

    private static GuidePageReading Wiki(string fixture, string path) =>
        new NeoseekerAdapter().Read(new Uri("https://www.neoseeker.com/example-quest/walkthrough" + path),
            P3Fixtures.Read($"neoseeker/{fixture}.html"))!;

    private string Image(string name)
    {
        string path = Path.Combine(harness.Sources.Root, name);
        File.WriteAllBytes(path, MapBytes.WholePng(4, 4));
        return path;
    }

    private static CapturedPage Page(GuidePageReading reading, IReadOnlyDictionary<Uri, string>? images = null) =>
        new(reading, images ?? new Dictionary<Uri, string>());

    private CapturedPage[] ThreeSagaPages() =>
    [
        Page(Saga("intro", "introduction")),
        Page(Saga("cairn", "cairn")),
        Page(Saga("fishing", "fishing")),
    ];

    private string Text(string name) => File.ReadAllText(Path.Combine(folder, name));

    [Fact]
    public async Task AMultiPageGuideImportsAsOneMultiPageHtmlGuide()
    {
        if (!OperatingSystem.IsWindows()) return;
        OfflineCopy copy = OfflineCopyBuilder.Build(ThreeSagaPages(), folder, Saved);

        Assert.Equal(Path.Combine(folder, "guide.html"), copy.EntryPath);
        Assert.StartsWith($"<!-- saved from url=({(SagaUrl + "cairn").Length:D4}){SagaUrl}cairn -->", Text("cairn.html"));
        ImportManifest manifest = await harness.InspectAsync(copy.EntryPath);
        Assert.IsType<HtmlImportManifest>(manifest);
        Guid id = await harness.PublishAsync(harness.Publisher(), manifest);
        Assert.Equal(["guide.html", "cairn.html", "fishing.html"],
            (await harness.Repository.GetGuidePagesAsync(id)).Select(page => page.RequestPath));
    }

    [Fact]
    public void LinksBetweenCapturedPagesAreRelative()
    {
        OfflineCopyBuilder.Build(ThreeSagaPages(), folder, Saved);
        string cairn = Text("cairn.html");
        Assert.Contains("href=\"fishing.html#bait\"", cairn);
        Assert.Contains("href=\"https://gamefaqs.gamespot.com/boards/100002\"", cairn);
        Assert.DoesNotContain("javascript:", cairn);
    }

    [Fact]
    public void ScriptsAndHandlersAreRemoved()
    {
        GuidePageReading page = Saga("cairn", "cairn") with
        {
            Content = "<p onclick=\"steal()\">Text</p><script>steal()</script><iframe src=\"https://ads.example\"></iframe>" +
                      "<form action=\"/x\"><input name=\"q\"><button>Go</button></form><style>p{}</style>",
        };
        OfflineCopyBuilder.Build([Page(page)], folder, Saved);
        string html = Text("guide.html");
        foreach (string gone in new[] { "onclick", "<script", "<iframe", "<form", "<input", "<button", "<style" })
        {
            Assert.DoesNotContain(gone, html);
        }
        Assert.Contains("<p>Text</p>", html);
    }

    // Review Focus 2.
    [Fact]
    public async Task ALazyImageIsSavedLocally()
    {
        GuidePageReading hub = Wiki("wiki-hub", "");
        string b = Image("b.png");
        OfflineCopy copy = OfflineCopyBuilder.Build(
            [Page(hub, new Dictionary<Uri, string> { [new Uri("https://cdn.staticneo.com/ew/thumb/b.png")] = b })],
            folder, Saved);
        string html = Text("guide.html");
        Assert.Contains("src=\"images/1.png\"", html);
        Assert.DoesNotContain("data-src", html);
        Assert.DoesNotContain("srcset", html);
        Assert.True(File.Exists(Path.Combine(folder, "images", "1.png")));
        Assert.Equal(1, copy.MissingImages);
        if (!OperatingSystem.IsWindows()) return;
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(copy.EntryPath));
        Assert.Contains(await harness.Repository.GetGuideAssetsAsync(id),
            asset => asset.RequestPath == "images/1.png" && asset.Kind == GuideAssetKind.Image);
    }

    [Fact]
    public void AMissingImageIsAPlaceholder()
    {
        OfflineCopy copy = OfflineCopyBuilder.Build([Page(Saga("cairn", "cairn"))], folder, Saved);
        Assert.Contains("<span class=\"dg-missing-image\">Image not saved</span>", Text("guide.html"));
        Assert.DoesNotContain("<img", Text("guide.html"));
        Assert.Equal(1, copy.MissingImages);
    }

    [Fact]
    public void TheHeaderNamesTheGuideAndItsSource()
    {
        OfflineCopyBuilder.Build(ThreeSagaPages(), folder, Saved);
        string entry = Text("guide.html");
        Assert.Contains("<header class=\"dg-source\">", entry);
        Assert.Contains("<h1>Example's Saga Walkthrough</h1>", entry);
        Assert.Contains("By Guide Writer", entry);
        Assert.Contains("Saved from GameFAQs on 20 October 2026", entry);
        Assert.Contains($"href=\"{SagaUrl}introduction\"", entry);
    }

    [Fact]
    public async Task ATextGuideIsItsTextUnchanged()
    {
        GuidePageReading text = new GameFaqsAdapter().Read(
            new Uri("https://gamefaqs.gamespot.com/ps/100001-example-quest/faqs/13639"), P3Fixtures.Read("gamefaqs/text-faq.html"))!;
        OfflineCopy copy = OfflineCopyBuilder.Build([Page(text)], folder, Saved);
        Assert.Equal(Path.Combine(folder, "guide.txt"), copy.EntryPath);
        byte[] bytes = File.ReadAllBytes(copy.EntryPath);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(text.Content, Encoding.UTF8.GetString(bytes[3..]));
        Assert.IsType<TxtImportManifest>(await harness.InspectAsync(copy.EntryPath));
    }

    [Fact]
    public void APartialCaptureLinksOnlyWhatItHas()
    {
        OfflineCopyBuilder.Build([Page(Saga("intro", "introduction")), Page(Saga("cairn", "cairn"))], folder, Saved);
        Assert.Contains($"href=\"{SagaUrl}fishing#bait\"", Text("cairn.html"));
        Assert.False(File.Exists(Path.Combine(folder, "fishing.html")));
    }

    [Fact]
    public void PagesFromTwoItemsAreRefused() =>
        Assert.Throws<ArgumentException>(() => OfflineCopyBuilder.Build(
            [Page(Saga("cairn", "cairn")), Page(Wiki("wiki-hub", ""))], folder, Saved));

    private static GuidePageReading SagaPage(string slug, string content) =>
        Saga("cairn", "cairn") with
        {
            Url = new Uri(SagaUrl + slug),
            Match = Saga("cairn", "cairn").Match with { PageKey = slug },
            Pages = [],
            Content = content,
        };

    [Fact]
    public void ALinkSpellingAPageWithOtherHyphensReachesIt()
    {
        OfflineCopyBuilder.Build(
            [Page(SagaPage("arts", $"<a href=\"{SagaUrl}arts-plug-ins#fire\">Fire</a>")), Page(SagaPage("arts-plugins", "<p>Plug-ins</p>"))],
            folder, Saved);
        Assert.Contains("href=\"arts-plugins.html#fire\"", Text("guide.html"));
    }

    [Fact]
    public void AnExactPageMatchWinsOverASpellingVariant()
    {
        OfflineCopyBuilder.Build(
            [Page(SagaPage("first", $"<a href=\"{SagaUrl}a-b\">A-B</a> <a href=\"{SagaUrl}ab\">AB</a>")),
             Page(SagaPage("ab", "<p>ab</p>")), Page(SagaPage("a-b", "<p>a-b</p>"))],
            folder, Saved);
        string html = Text("guide.html");
        Assert.Contains("href=\"a-b.html\"", html);
        Assert.Contains("href=\"ab.html\"", html);
    }

    [Fact]
    public void EveryHtmlPageLinksTheSitesStyleSheet()
    {
        OfflineCopyBuilder.Build(ThreeSagaPages(), folder, Saved);
        Assert.Equal(OfflineCopyBuilder.StyleSheet(SourceSite.GameFaqs), Text("guide.css"));
        foreach (string name in new[] { "guide.html", "cairn.html", "fishing.html" })
        {
            string html = Text(name);
            Assert.Contains("<head><meta charset=\"utf-8\"><link rel=\"stylesheet\" href=\"guide.css\">", html);
            Assert.Contains("<main class=\"dg-site-gamefaqs\">", html);
        }
    }

    [Fact]
    public void NeoseekerContentSitsInItsWikiWrapper()
    {
        OfflineCopyBuilder.Build([Page(Wiki("wiki-hub", ""))], folder, Saved);
        Assert.Contains("<main class=\"dg-site-neoseeker\"><div class=\"mw-parser-output\">", Text("guide.html"));
        Assert.Equal(OfflineCopyBuilder.StyleSheet(SourceSite.Neoseeker), Text("guide.css"));
    }

    [Theory]
    [InlineData(SourceSite.GameFaqs)]
    [InlineData(SourceSite.Neoseeker)]
    public void AStyleSheetCanMakeNoRequest(SourceSite site)
    {
        string css = OfflineCopyBuilder.StyleSheet(site);
        Assert.Contains(".dg-source", css);
        foreach (string request in new[] { "url(", "@import", "@font-face" })
        {
            Assert.DoesNotContain(request, css, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheSitesStyleSheetsDiffer() =>
        Assert.NotEqual(OfflineCopyBuilder.StyleSheet(SourceSite.GameFaqs), OfflineCopyBuilder.StyleSheet(SourceSite.Neoseeker));

    [Fact]
    public void ASpoilerCanBeRevealedFromTheKeyboard()
    {
        OfflineCopyBuilder.Build([Page(SagaPage("cairn", "<p>Boss: <span class=\"fspoiler\">Gwyn</span></p>"))], folder, Saved);
        Assert.Contains("<span class=\"fspoiler\" tabindex=\"0\">Gwyn</span>", Text("guide.html"));
    }

    [Fact]
    public void ATextCopyHasNoStyleSheet()
    {
        GuidePageReading text = new GameFaqsAdapter().Read(
            new Uri("https://gamefaqs.gamespot.com/ps5/100002-example-saga/faqs/78735"), P3Fixtures.Read("gamefaqs/text-faq.html"))!;
        OfflineCopyBuilder.Build([Page(text)], folder, Saved);
        Assert.False(File.Exists(Path.Combine(folder, "guide.css")));
    }
}
