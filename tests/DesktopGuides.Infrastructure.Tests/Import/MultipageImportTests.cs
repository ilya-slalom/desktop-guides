using DesktopGuides.Core.Html;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Import;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class MultipageImportTests
{
    private const string Entry = "Sample Quest - Guides Example.html";

    [Fact]
    public async Task ThePreviewListsFourPagesInReadingOrder()
    {
        if (!OperatingSystem.IsWindows()) return;
        using ImportTestDirectory files = new();
        P2Fixtures.CopyTree("html-multipage", files.Root);

        ImportInspection inspection = await new GuideImportValidator()
            .InspectAsync(Path.Combine(files.Root, Entry), CancellationToken.None);

        HtmlImportManifest manifest = Assert.IsType<HtmlImportManifest>(Assert.IsType<ImportReady>(inspection).Manifest);
        Assert.Equal(["Sample Quest", "Chapter 1", "Chapter 2", "Chapter 3"], manifest.PageTitles);
        Assert.Equal(4, manifest.AssetCount);
        Assert.DoesNotContain(manifest.Warnings, warning => warning.Message.Contains("readable CSS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishingStoresEveryPageWithItsOrderAndAddress()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        P2Fixtures.CopyTree("html-multipage", harness.Sources.Root);
        string entry = Path.Combine(harness.Sources.Root, Entry);

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));

        IReadOnlyList<GuideAsset> assets = await harness.Repository.GetGuideAssetsAsync(id);
        Assert.Equal(
            [("Chapter 1 - Guides Example.html", GuideAssetKind.Page),
                ("Chapter 1 - Guides Example_files/load.php", GuideAssetKind.StyleSheet),
                ("Chapter 2 - Guides Example.html", GuideAssetKind.Page),
                ("Chapter 2 - Guides Example_files/load.php", GuideAssetKind.StyleSheet),
                ("Chapter 3 - Guides Example.html", GuideAssetKind.Page),
                ("Chapter 3 - Guides Example_files/load.php", GuideAssetKind.StyleSheet),
                ("Sample Quest - Guides Example.html", GuideAssetKind.EntryHtml),
                ("Sample Quest - Guides Example_files/load.php", GuideAssetKind.StyleSheet)],
            assets.Select(asset => (asset.RequestPath, asset.Kind)));
        foreach (GuideAsset asset in assets)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(harness.Sources.Root, asset.RelativePath)),
                File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, asset.RelativePath)));
        }

        using SqliteConnection connection = new($"Data Source={harness.Paths.DatabasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using SqliteCommand pages = connection.CreateCommand();
        pages.CommandText = $"SELECT PageOrder, RequestPath, OriginalUrl, Title FROM GuidePages WHERE GuideId = '{id:N}' ORDER BY PageOrder";
        using SqliteDataReader reader = pages.ExecuteReader();
        List<(long, string, string?, string)> rows = [];
        while (reader.Read())
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
        }
        Assert.Equal(
            [(0L, "Sample Quest - Guides Example.html", "https://guides.example/wiki/Sample_Quest", "Sample Quest — Guides Example"),
                (1L, "Chapter 1 - Guides Example.html", "https://guides.example/wiki/Sample_Quest/Chapter_1", "Chapter 1 — Guides Example")],
            rows.Take(2));
        Assert.Equal(["Chapter 2 - Guides Example.html", "Chapter 3 - Guides Example.html"],
            rows.Skip(2).Select(row => row.Item2));
    }
}
