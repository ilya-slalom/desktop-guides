using DesktopGuides.Core.Maps;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class MapTileMemoryTests
{
    [Fact]
    public void TheBudgetIsPdfs() =>
        Assert.Equal(100_663_296, new MapTileMemory<object>().MaxBytes);

    [Fact]
    public void TheLeastRecentlyDrawnTileGoesFirst()
    {
        MapTileMemory<object> memory = new(maxBytes: 3 * 512 * 512 * 4);
        MapTileKey a = new(0, 0, 0), b = new(0, 1, 0), c = new(0, 2, 0), d = new(0, 3, 0);
        foreach (MapTileKey key in new[] { a, b, c }) memory.Add(key, new object(), 512, 512);
        memory.Touch(a);
        memory.Add(d, new object(), 512, 512);
        Assert.False(memory.TryGet(b, out _));
        Assert.True(memory.TryGet(a, out _));
        Assert.Equal(1, memory.Evictions);
        Assert.True(memory.PeakBytes <= memory.MaxBytes);
    }

    [Fact]
    public void ClearEmptiesIt()
    {
        MapTileMemory<object> memory = new();
        memory.Add(new MapTileKey(0, 0, 0), new object(), 512, 512);
        memory.Clear();
        Assert.Equal(0, memory.CachedBytes);
        Assert.False(memory.TryGet(new MapTileKey(0, 0, 0), out _));
    }
}
