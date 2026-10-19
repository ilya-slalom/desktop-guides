namespace DesktopGuides.Core.Maps;

// T21.2 PR c: the Maps pane's sizes. A preview is at most 2048 pixels on its
// longer side; the pane is as large as the window allows, within a cap.
public static class MapPreview
{
    public const int MaxSide = 2048;
    public const double WindowMargin = 48;
    public const double MaxPaneWidth = 1200, MaxPaneHeight = 900;
    public const double MinPaneWidth = 520, MinPaneHeight = 320;

    // 0 keeps the natural size.
    public static int DecodeWidth(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int longer = Math.Max(width, height);
        return longer <= MaxSide ? 0 : (int)Math.Max(1, Math.Round(width * (double)MaxSide / longer));
    }

    public static (double Width, double Height) PaneSize(double windowWidth, double windowHeight) =>
        (Math.Clamp(windowWidth - 2 * WindowMargin, MinPaneWidth, MaxPaneWidth),
         Math.Clamp(windowHeight - 2 * WindowMargin, MinPaneHeight, MaxPaneHeight));

    // The map's aspect within the area, never enlarged.
    public static (double Width, double Height) ImageSize(double areaWidth, double areaHeight, int mapWidth, int mapHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapHeight);
        double scale = Math.Min(1, Math.Min(Math.Max(0, areaWidth) / mapWidth, Math.Max(0, areaHeight) / mapHeight));
        return (mapWidth * scale, mapHeight * scale);
    }
}
