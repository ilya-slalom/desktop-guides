using DesktopGuides.Core.Import;
using DesktopGuides.Core.Maps;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class MapBatchTests
{
    [Theory]
    [InlineData(0, "Import maps")]
    [InlineData(1, "Import 1 map")]
    [InlineData(3, "Import 3 maps")]
    public void TheImportButtonCountsReadyMaps(int ready, string label) =>
        Assert.Equal(label, MapBatch.ImportLabel(ready));

    [Fact]
    public void ASummaryNamesEachFailure()
    {
        Assert.Equal("Imported 2 maps.", MapBatch.Summary(2, []));
        Assert.Equal("Imported 1 map. 1 couldn't be imported: castle: The file changed after it was checked.",
            MapBatch.Summary(1, [("castle", "The file changed after it was checked.")]));
        Assert.Equal("No maps were imported. 2 couldn't be imported: a: x; b: y",
            MapBatch.Summary(0, [("a", "x"), ("b", "y")]));
    }

    [Fact]
    public void ARowShowsTypeSizeAndBytes() =>
        Assert.Equal($"PNG · 640 × 480 · {ImportPresentation.FormatSize(2048)}",
            MapBatch.Facts(new MapImportManifest(
                new ImportSource("C:\\m\\a.png", "a.png", 2048, DateTimeOffset.UnixEpoch),
                "a", MapImageKind.Png, 640, 480, new string('0', 64))));

    [Fact]
    public void ABatchHasAtMostAHundredFiles() => Assert.Equal(100, MapBatch.MaxFiles);

    // Importing waits until every picked file has been checked.
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void ImportWaitsForTheChecks(bool checking, bool importing, bool allowed) =>
        Assert.Equal(allowed, MapBatch.CanImport(checking, importing, ["castle floor 1"]));

    [Fact]
    public void ImportNeedsAMapAndEveryTitle()
    {
        Assert.False(MapBatch.CanImport(false, false, []));
        Assert.False(MapBatch.CanImport(false, false, ["castle floor 1", "  "]));
    }
}
