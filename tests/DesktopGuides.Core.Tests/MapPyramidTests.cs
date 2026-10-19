using DesktopGuides.Core.Maps;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class MapPyramidTests
{
    [Theory]
    [InlineData(4096, 300, true, 0)]
    [InlineData(4097, 300, false, 2)]      // 4097 → 2049 → 1025 (overview)
    [InlineData(15456, 10304, false, 3)]   // 15456 → 7728 → 3864 → 1932 (overview)
    [InlineData(20000, 400, false, 4)]     // 20000 → … → 1250 (overview)
    public void LevelsStopAtTheOverview(int w, int h, bool small, int levels)
    {
        MapPyramid pyramid = new(w, h);
        Assert.Equal(small, pyramid.Small);
        if (!small) Assert.Equal(levels, pyramid.Levels);
        Assert.True(Math.Max(pyramid.OverviewSize.Width, pyramid.OverviewSize.Height) <= MapPyramid.OverviewMaxSide);
    }

    [Fact]
    public void ALevelRoundsUp()
    {
        MapPyramid pyramid = new(15456, 10303);
        Assert.Equal((7728, 5152), pyramid.LevelSize(1));
        Assert.Equal((1932, 1288), pyramid.OverviewSize);
        Assert.Equal(31, pyramid.Columns(0));
        Assert.Equal(21, pyramid.Rows(0));
    }

    // Review Focus 5.
    [Fact]
    public void AStripHasOneTileRowPerLevel()
    {
        MapPyramid strip = new(20000, 400);
        Assert.All(Enumerable.Range(0, strip.Levels), level => Assert.Equal(1, strip.Rows(level)));
        Assert.Equal(40, strip.Columns(0));
    }

    [Fact]
    public void EveryTileIsEmittedOnceWithItsSize()
    {
        List<(MapTileKey Key, int W, int H)> tiles = [];
        MapPyramidBuilder builder = new(10, 6, tileSize: 4, levels: 2, (key, w, h, _) => tiles.Add((key, w, h)));
        byte[] rows = new byte[10 * 4 * 3];
        builder.AddBand(rows, 3);
        builder.AddBand(rows, 3);
        builder.Complete();
        Assert.Equal(tiles.Count, tiles.Select(t => t.Key).Distinct().Count());
        Assert.Equal(3 * 2 + 2 * 1, tiles.Count);                 // 10 × 6 and 5 × 3 in tiles of 4
        Assert.Contains((new MapTileKey(0, 2, 1), 2, 2), tiles);   // the corner tile of 10 × 6
        Assert.Contains((new MapTileKey(1, 1, 0), 1, 3), tiles);   // level 1 is 5 × 3
    }

    [Fact]
    public void ALevelAveragesTwoByTwoBlocks()
    {
        // A 2 × 2 image of 0, 40, 80 and 120 in every channel averages to 60.
        byte[]? top = null;
        MapPyramidBuilder builder = new(2, 2, tileSize: 1, levels: 2, (key, _, _, bgra) => { if (key.Level == 1) top = bgra; });
        builder.AddBand([0, 0, 0, 0, 40, 40, 40, 40, 80, 80, 80, 80, 120, 120, 120, 120], 2);
        builder.Complete();
        Assert.Equal([60, 60, 60, 60], top);
    }

    [Fact]
    public void AnOddEdgeAveragesWhatExists()
    {
        // A 3 × 1 row: the last level-1 pixel is the lone third pixel.
        Dictionary<MapTileKey, byte[]> tiles = [];
        MapPyramidBuilder builder = new(3, 1, tileSize: 2, levels: 2, (key, _, _, bgra) => tiles[key] = bgra);
        builder.AddBand([10, 10, 10, 10, 30, 30, 30, 30, 200, 200, 200, 200], 1);
        builder.Complete();
        Assert.Equal([20, 20, 20, 20, 200, 200, 200, 200], tiles[new MapTileKey(1, 0, 0)]);
    }

    [Fact]
    public void ATileCopiesItsPixelsInRowOrder()
    {
        // 2 × 2 in tiles of 1: the bottom-right tile is the fourth pixel.
        Dictionary<MapTileKey, byte[]> tiles = [];
        MapPyramidBuilder builder = new(2, 2, tileSize: 1, levels: 1, (key, _, _, bgra) => tiles[key] = bgra);
        builder.AddBand([1, 1, 1, 1, 2, 2, 2, 2], 1);
        builder.AddBand([3, 3, 3, 3, 4, 4, 4, 4], 1);
        builder.Complete();
        Assert.Equal([4, 4, 4, 4], tiles[new MapTileKey(0, 1, 1)]);
        Assert.Equal([2, 2, 2, 2], tiles[new MapTileKey(0, 1, 0)]);
    }

    [Fact]
    public void TooManyRowsAreRefused()
    {
        MapPyramidBuilder builder = new(2, 2, tileSize: 1, levels: 1, (_, _, _, _) => { });
        builder.AddBand(new byte[16], 2);
        Assert.Throws<InvalidOperationException>(() => builder.AddBand(new byte[8], 1));
    }
}
