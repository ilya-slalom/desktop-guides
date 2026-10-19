namespace DesktopGuides.Core.Maps;

public readonly record struct MapTileKey(int Level, int Column, int Row);

/// <summary>
/// T21.2: a large map's levels. Level k is the map halved k times, rounded
/// up; halving stops at the overview, whose longer side is at most 2048.
/// Levels 0 to Levels - 1 are cut into square tiles. A map whose longer
/// side is at most 4096 is small: it's shown from its file whole.
/// </summary>
public sealed record MapPyramid
{
    public const int OverviewMaxSide = 2048;
    public const int SmallMaxSide = 4096;
    public const int DefaultTileSize = 512;

    public MapPyramid(int width, int height, int tileSize = DefaultTileSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        Width = width;
        Height = height;
        TileSize = tileSize;
        int levels = 0;
        while (Math.Max(Halved(width, levels), Halved(height, levels)) > OverviewMaxSide) levels++;
        Levels = levels;
    }

    public int Width { get; }
    public int Height { get; }
    public int TileSize { get; }
    public int Levels { get; }
    public bool Small => Math.Max(Width, Height) <= SmallMaxSide;
    public (int Width, int Height) OverviewSize => LevelSize(Levels);

    public (int Width, int Height) LevelSize(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        return (Halved(Width, level), Halved(Height, level));
    }

    public int Columns(int level) => (LevelSize(level).Width + TileSize - 1) / TileSize;

    public int Rows(int level) => (LevelSize(level).Height + TileSize - 1) / TileSize;

    private static int Halved(int size, int times) =>
        times >= 31 ? 1 : (int)((size + (1L << times) - 1) >> times);
}
