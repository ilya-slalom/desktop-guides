using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class AppIconCatalogTests
{
    public static TheoryData<AppIcon> Icons()
    {
        TheoryData<AppIcon> data = [];
        foreach (AppIcon icon in Enum.GetValues<AppIcon>()) data.Add(icon);
        return data;
    }

    [Fact]
    public void TheCatalogListsTenPlaces()
    {
        Assert.Equal(10, AppIconCatalog.All.Count);
        Assert.Equal(Enum.GetValues<AppIcon>(), AppIconCatalog.All);
    }

    [Theory]
    [MemberData(nameof(Icons))]
    public void ByDefaultEveryIconIsItsGlyph(AppIcon icon)
    {
        AppIconChoice choice = AppIconCatalog.For(icon, colorful: false, highContrast: false);

        Assert.False(choice.IsAsset);
        Assert.False(string.IsNullOrEmpty(choice.Glyph));
        Assert.Null(choice.AssetFile);
    }

    [Theory]
    [MemberData(nameof(Icons))]
    public void ColorfulIconsUseTheirColorAsset(AppIcon icon)
    {
        AppIconChoice choice = AppIconCatalog.For(icon, colorful: true, highContrast: false);

        Assert.True(choice.IsAsset);
        Assert.Null(choice.Glyph);
        Assert.Matches("^ic_fluent_[a-z_]+_24_color\\.(svg|png)$", choice.AssetFile);
    }

    [Theory]
    [MemberData(nameof(Icons))]
    public void ForEveryIconHighContrastWinsOverColorful(AppIcon icon)
    {
        Assert.Equal(
            AppIconCatalog.For(icon, colorful: false, highContrast: false),
            AppIconCatalog.For(icon, colorful: true, highContrast: true));
        Assert.Equal(
            AppIconCatalog.For(icon, colorful: false, highContrast: false),
            AppIconCatalog.For(icon, colorful: false, highContrast: true));
    }

    [Theory]
    [InlineData(AppIcon.NavLibrary, "\uE80F", "ic_fluent_library_24_color.svg")]
    [InlineData(AppIcon.NavSettings, "\uE713", "ic_fluent_settings_24_color.svg")]
    [InlineData(AppIcon.TitleBar, "\uE82D", "ic_fluent_library_24_color.svg")]
    [InlineData(AppIcon.EmptyLibrary, "\uE82D", "ic_fluent_book_open_24_color.svg")]
    [InlineData(AppIcon.EmptySearch, "\uE721", "ic_fluent_search_sparkle_24_color.svg")]
    [InlineData(AppIcon.ThemeCard, "\uE771", "ic_fluent_paint_brush_24_color.svg")]
    [InlineData(AppIcon.MaterialCard, "\uE790", "ic_fluent_image_24_color.svg")]
    [InlineData(AppIcon.BackupCard, "\uE896", "ic_fluent_document_folder_24_color.svg")]
    [InlineData(AppIcon.RestoreCard, "\uE777", "ic_fluent_history_24_color.svg")]
    [InlineData(AppIcon.ProvidersCard, "\uE721", "ic_fluent_globe_24_color.svg")]
    public void EachPlaceKeepsItsGlyphAndGetsItsAsset(AppIcon icon, string glyph, string asset)
    {
        Assert.Equal(glyph, AppIconCatalog.For(icon, false, false).Glyph);
        Assert.Equal(asset, AppIconCatalog.For(icon, true, false).AssetFile);
    }

    [Fact]
    public void AssetFilesAreTheNineDistinctFiles()
    {
        Assert.Equal(9, AppIconCatalog.AssetFiles.Count);
        Assert.Equal(
            AppIconCatalog.All.Select(icon => AppIconCatalog.For(icon, true, false).AssetFile!)
                .Distinct().Order(StringComparer.Ordinal),
            AppIconCatalog.AssetFiles);
    }

    // Ruling 9: the catalog and the vendored folder agree, so no icon goes missing.
    [Fact]
    public void EveryAssetFileIsVendoredAndNothingElseIs()
    {
        string folder = Path.Combine(
            RepoRoot(), "src", "DesktopGuides.Production",
            AppIconCatalog.AssetFolder.Replace('/', Path.DirectorySeparatorChar));
        string[] vendored = Directory.GetFiles(folder)
            .Select(Path.GetFileName)
            .Where(name => name!.EndsWith(".svg", StringComparison.Ordinal) ||
                           name.EndsWith(".png", StringComparison.Ordinal))
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AppIconCatalog.AssetFiles, vendored);
        Assert.True(File.Exists(Path.Combine(folder, "SOURCE.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "THIRD-PARTY-NOTICES.txt")));
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DesktopGuides.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("DesktopGuides.sln not found.");
    }
}
