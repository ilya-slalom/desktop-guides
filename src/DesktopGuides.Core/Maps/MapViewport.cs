using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Maps;

/// <summary>
/// T21.2: where a map is looked at, in physical pixels. Zoom is view pixels
/// per image pixel, from fit up to 4×; the centre is kept as a fraction of
/// the image so a saved place restores exactly. Every result is clamped:
/// an edge never comes inside the view, and a side smaller than the view
/// is centred.
/// </summary>
public sealed record MapViewport
{
    public const double Step = 1.25;
    public const double PanFraction = 0.1;
    // Within 1% of fit counts as fitted.
    private const double FitTolerance = 1.01;

    private readonly double fractionX;
    private readonly double fractionY;

    private MapViewport(int width, int height, double viewWidth, double viewHeight, double zoom, double fx, double fy)
    {
        ImageWidth = width;
        ImageHeight = height;
        ViewWidth = viewWidth;
        ViewHeight = viewHeight;
        Fit = FitFor(width, height, viewWidth, viewHeight);
        Zoom = zoom <= Fit * FitTolerance ? Fit : Math.Min(zoom, ImagePosition.MaxZoom);
        fractionX = ClampFraction(fx, width, viewWidth);
        fractionY = ClampFraction(fy, height, viewHeight);
    }

    public int ImageWidth { get; }
    public int ImageHeight { get; }
    public double ViewWidth { get; }
    public double ViewHeight { get; }
    public double Fit { get; }
    public double Zoom { get; }
    public double CenterX => fractionX * ImageWidth;
    public double CenterY => fractionY * ImageHeight;
    public bool IsFitted => Zoom == Fit;
    public bool CanZoomIn => Zoom < ImagePosition.MaxZoom;
    public bool CanZoomOut => !IsFitted;

    public static MapViewport Fitted(int imageWidth, int imageHeight, double viewWidth, double viewHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
        return new(imageWidth, imageHeight, Positive(viewWidth), Positive(viewHeight), 0, 0.5, 0.5);
    }

    public static MapViewport FromPosition(
        ImagePosition position, int imageWidth, int imageHeight, double viewWidth, double viewHeight)
    {
        ArgumentNullException.ThrowIfNull(position);
        MapViewport fitted = Fitted(imageWidth, imageHeight, viewWidth, viewHeight);
        return position.Zoom is double zoom
            ? fitted.With(zoom, position.CenterX, position.CenterY)
            : fitted;
    }

    public ImagePosition ToPosition() => new(IsFitted ? null : Zoom, fractionX, fractionY);

    // Keeps the image point under (viewX, viewY) where it is.
    public MapViewport ZoomBy(double factor, double viewX, double viewY)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        double pointX = CenterX + (viewX - ViewWidth / 2) / Zoom;
        double pointY = CenterY + (viewY - ViewHeight / 2) / Zoom;
        double zoom = Math.Clamp(Zoom * factor, Fit, ImagePosition.MaxZoom);
        return With(zoom,
            (pointX - (viewX - ViewWidth / 2) / zoom) / ImageWidth,
            (pointY - (viewY - ViewHeight / 2) / zoom) / ImageHeight);
    }

    public MapViewport ZoomIn() => ZoomBy(Step, ViewWidth / 2, ViewHeight / 2);

    public MapViewport ZoomOut() => ZoomBy(1 / Step, ViewWidth / 2, ViewHeight / 2);

    public MapViewport FitToView() => With(0, 0.5, 0.5);

    // Moves the map by (dx, dy) view pixels, as a drag does.
    public MapViewport PanBy(double dx, double dy) =>
        With(Zoom, (CenterX - dx / Zoom) / ImageWidth, (CenterY - dy / Zoom) / ImageHeight);

    public MapViewport PanStep(int columns, int rows) =>
        PanBy(-columns * ViewWidth * PanFraction, -rows * ViewHeight * PanFraction);

    // A fitted map stays fitted; otherwise the zoom and centre hold, clamped.
    public MapViewport Resize(double viewWidth, double viewHeight) =>
        new(ImageWidth, ImageHeight, Positive(viewWidth), Positive(viewHeight),
            IsFitted ? 0 : Zoom, fractionX, fractionY);

    // The most detailed level whose scale (2^-k) still holds the zoom's
    // detail; levelCount means the overview.
    public int Level(int levelCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(levelCount);
        int level = 0;
        while (level < levelCount && Math.Pow(2, -(level + 1)) >= Zoom * (1 - 1e-9))
        {
            level++;
        }
        return level;
    }

    // The visible area, in image pixels.
    public (double X, double Y, double Width, double Height) ViewRect()
    {
        double width = ViewWidth / Zoom, height = ViewHeight / Zoom;
        return (CenterX - width / 2, CenterY - height / 2, width, height);
    }

    // The tiles of a level that the view touches, with one more on each side.
    public (int FirstColumn, int FirstRow, int LastColumn, int LastRow) VisibleTiles(int level, int tileSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        double scale = Math.Pow(2, -level);
        int columns = (int)Math.Ceiling(Math.Ceiling(ImageWidth * scale) / tileSize);
        int rows = (int)Math.Ceiling(Math.Ceiling(ImageHeight * scale) / tileSize);
        (double x, double y, double width, double height) = ViewRect();
        return (
            Math.Max(0, (int)Math.Floor(x * scale / tileSize) - 1),
            Math.Max(0, (int)Math.Floor(y * scale / tileSize) - 1),
            Math.Min(columns - 1, (int)Math.Ceiling((x + width) * scale / tileSize)),
            Math.Min(rows - 1, (int)Math.Ceiling((y + height) * scale / tileSize)));
    }

    private MapViewport With(double zoom, double fx, double fy) =>
        new(ImageWidth, ImageHeight, ViewWidth, ViewHeight, zoom, fx, fy);

    private static double FitFor(int width, int height, double viewWidth, double viewHeight) =>
        Math.Min(Math.Min(viewWidth / width, viewHeight / height), ImagePosition.MaxZoom);

    // A side that fills the view keeps its edges outside it; a smaller one is centred.
    private double ClampFraction(double fraction, int size, double view)
    {
        double half = view / 2 / Zoom / size;
        if (!double.IsFinite(fraction) || half >= 0.5) return 0.5;
        return Math.Clamp(fraction, half, 1 - half);
    }

    private static double Positive(double value) =>
        double.IsFinite(value) && value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
