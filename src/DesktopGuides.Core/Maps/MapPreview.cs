namespace DesktopGuides.Core.Maps;

// T21.2 PR c: the Maps pane's sizes. A preview is at most 2048 pixels on its
// longer side; the pane fills the space below and right of the Maps button,
// less the flyout's own padding, within a cap, and never past the window.
public static class MapPreview
{
    public const int MaxSide = 2048;
    public const double EdgeMargin = 48;
    public const double MaxPaneWidth = 1200, MaxPaneHeight = 900;

    // 0 keeps the natural size.
    public static int DecodeWidth(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int longer = Math.Max(width, height);
        return longer <= MaxSide ? 0 : (int)Math.Max(1, Math.Round(width * (double)MaxSide / longer));
    }

    public static (double Width, double Height) PaneSize(double availableWidth, double availableHeight) =>
        (Math.Clamp(availableWidth - EdgeMargin, 0, MaxPaneWidth),
         Math.Clamp(availableHeight - EdgeMargin, 0, MaxPaneHeight));

    // The map's aspect within the area, never enlarged.
    public static (double Width, double Height) ImageSize(double areaWidth, double areaHeight, int mapWidth, int mapHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapHeight);
        double scale = Math.Min(1, Math.Min(Math.Max(0, areaWidth) / mapWidth, Math.Max(0, areaHeight) / mapHeight));
        return (mapWidth * scale, mapHeight * scale);
    }
}
