using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class ManagedHtmlGuideLoaderTests
{
    private static async Task<Guide> PublishAsync(PublisherHarness harness)
    {
        foreach (string file in new[] { "images/map.png", "styles/main.css", "styles/palette.css" })
        {
            harness.Sources.Copy("html-static/" + file, file);
        }
        string entry = harness.Sources.Copy("html-static/guide.html", "guide.html");
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));
        return (await harness.Repository.GetGuideAsync(id))!;
    }

    private static Task<HtmlGuideLoad> LoadAsync(PublisherHarness harness, Guide guide) =>
        new ManagedHtmlGuideLoader(harness.Repository, harness.Paths).LoadAsync(guide, CancellationToken.None);

    private static async Task<HtmlGuideLoadError> FailedAsync(PublisherHarness harness, Guide guide) =>
        Assert.IsType<HtmlGuideLoadFailed>(await LoadAsync(harness, guide)).Error;

    [Fact]
    public async Task LoadsAPublishedGuideAtItsOwnOrigin()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);

        HtmlGuideLoaded loaded = Assert.IsType<HtmlGuideLoaded>(await LoadAsync(harness, guide));

        Assert.Equal(GuideWebOrigin.EntryUri(guide.Id, "guide.html"), loaded.Policy.EntryUri);
        Assert.IsType<HtmlServe>(loaded.Policy.Decide("GET", loaded.Policy.EntryUri.AbsoluteUri));
        Assert.Equal(harness.Paths.ResolveExistingGuideFile(guide.Id, "guide.html"), loaded.EntryFilePath);
    }

    [Fact]
    public async Task GuideWithoutRowsNeedsReimport()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        harness.Execute("DELETE FROM GuideAssets WHERE GuideId = $id", guide.Id);

        Assert.Equal(HtmlGuideLoadError.NoManifest, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task ChangedEntryIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        File.AppendAllText(harness.Paths.ResolveExistingGuideFile(guide.Id, "guide.html"), "<p>edit</p>");

        Assert.Equal(HtmlGuideLoadError.Changed, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task MissingEntryIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        File.Delete(harness.Paths.ResolveExistingGuideFile(guide.Id, "guide.html"));

        Assert.Equal(HtmlGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task DeletedGuideFolderIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        Directory.Delete(harness.Paths.GetGuideRoot(guide.Id), recursive: true);

        Assert.Equal(HtmlGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task EntryThatIsAFolderIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        string entry = harness.Paths.ResolveExistingGuideFile(guide.Id, "guide.html");
        File.Delete(entry);
        Directory.CreateDirectory(entry);

        Assert.Equal(HtmlGuideLoadError.Changed, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task GuideFolderThatIsALinkIsChanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        string root = harness.Paths.GetGuideRoot(guide.Id);
        string elsewhere = Path.Combine(Path.GetTempPath(), "desktop-guides-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.Move(root, elsewhere);
            RemovalLibrary.CreateJunction(root, elsewhere);

            Assert.Equal(HtmlGuideLoadError.Changed, await FailedAsync(harness, guide));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root);
            if (Directory.Exists(elsewhere)) Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public async Task EntryRowThatIsNotThePrimaryFileIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);

        Assert.Equal(HtmlGuideLoadError.Changed,
            await FailedAsync(harness, guide with { PrimaryRelativePath = "styles/main.css" }));
    }
}
