using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Microsoft.Data.Sqlite;
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
        if (!OperatingSystem.IsWindows()) return;

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
        if (!OperatingSystem.IsWindows()) return;

        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        harness.Execute("DELETE FROM GuideAssets WHERE GuideId = $id", guide.Id);

        Assert.Equal(HtmlGuideLoadError.NoManifest, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task ChangedEntryIsChanged()
    {
        if (!OperatingSystem.IsWindows()) return;

        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        File.AppendAllText(harness.Paths.ResolveExistingGuideFile(guide.Id, "guide.html"), "<p>edit</p>");

        Assert.Equal(HtmlGuideLoadError.Changed, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task MissingEntryIsMissing()
    {
        if (!OperatingSystem.IsWindows()) return;

        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        File.Delete(harness.Paths.ResolveExistingGuideFile(guide.Id, "guide.html"));

        Assert.Equal(HtmlGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task DeletedGuideFolderIsMissing()
    {
        if (!OperatingSystem.IsWindows()) return;

        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        Directory.Delete(harness.Paths.GetGuideRoot(guide.Id), recursive: true);

        Assert.Equal(HtmlGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task EntryThatIsAFolderIsChanged()
    {
        if (!OperatingSystem.IsWindows()) return;

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
        if (!OperatingSystem.IsWindows()) return;

        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);

        Assert.Equal(HtmlGuideLoadError.Changed,
            await FailedAsync(harness, guide with { PrimaryRelativePath = "styles/main.css" }));
    }

    [Fact]
    public async Task LoadsAMultipageGuideWithItsPagesInOrder()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        P2Fixtures.CopyTree("html-multipage", harness.Sources.Root);
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(
            Path.Combine(harness.Sources.Root, "Sample Quest - Guides Example.html")));
        Guide guide = (await harness.Repository.GetGuideAsync(id))!;

        HtmlGuideLoaded loaded = Assert.IsType<HtmlGuideLoaded>(await LoadAsync(harness, guide));

        Assert.Equal(
            ["Sample Quest - Guides Example.html", "Chapter 1 - Guides Example.html",
                "Chapter 2 - Guides Example.html", "Chapter 3 - Guides Example.html"],
            loaded.Pages.DocumentPaths);
        Assert.IsType<HtmlServe>(loaded.Policy.Decide("GET", loaded.Pages.AddressOf(2).AbsoluteUri));
    }

    [Fact]
    public async Task AOnePageGuideHasItsEntryAsItsPage()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);

        HtmlGuideLoaded loaded = Assert.IsType<HtmlGuideLoaded>(await LoadAsync(harness, guide));

        Assert.Equal(["guide.html"], loaded.Pages.DocumentPaths);
    }

    [Fact]
    public async Task APageRowOnAStylesheetIsChanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        using (SqliteConnection connection = new($"Data Source={harness.Paths.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText =
                $"INSERT INTO GuidePages (GuideId, PageOrder, RequestPath) VALUES ('{guide.Id:N}', 1, 'styles/main.css')";
            insert.ExecuteNonQuery();
        }

        Assert.Equal(HtmlGuideLoadError.Changed, await FailedAsync(harness, guide));
    }
}
