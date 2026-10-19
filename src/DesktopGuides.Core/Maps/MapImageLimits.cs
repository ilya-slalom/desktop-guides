namespace DesktopGuides.Core.Maps;

public enum MapLimit { Side, Pixels }

// T21.2: the largest map seen, 15456 × 10304, fits with room to spare.
public static class MapImageLimits
{
    public const long MaxBytes = 256L * 1024 * 1024;
    public const int MaxSide = 32768;
    public const long MaxPixels = 268_435_456;   // 16384²

    public static MapLimit? Check(MapImageSize size)
    {
        ArgumentNullException.ThrowIfNull(size);
        if (size.Width > MaxSide || size.Height > MaxSide) return MapLimit.Side;
        return (long)size.Width * size.Height > MaxPixels ? MapLimit.Pixels : null;
    }
}
