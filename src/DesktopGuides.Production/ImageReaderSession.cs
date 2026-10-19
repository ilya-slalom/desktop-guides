using System.Diagnostics;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Maps;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Maps;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.System;

namespace DesktopGuides.Production;

// T21.2: a map, opened fitted, panned by drag, the arrow keys or the wheel,
// and zoomed from fit to 4×. A small map is one surface from its file. A
// large one draws its overview under the visible tiles of the level that
// holds the zoom's detail; tiles load one at a time, nearest the centre
// first, and memory keeps the recently drawn ones within PDF's budget. No
// surface exceeds 4096 pixels. The view place is a convenience, never
// progress.
internal sealed class ImageReaderSession : IReaderSession
{
    private readonly Guide guide;
    private readonly MapTileStore store;
    private readonly MapCacheQueue queue;
    private readonly string cacheRoot;
    private readonly bool diagnostics;
    private readonly MapPyramid pyramid;
    private readonly Canvas canvas = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly Image overview = new() { Stretch = Stretch.Fill };
    private readonly Dictionary<MapTileKey, Image> shown = [];
    private readonly HashSet<MapTileKey> failed = [];
    private readonly MapTileMemory<BitmapImage> memory = new();
    private readonly Stopwatch openClock = new();
    private MapViewport? viewport;
    private ImagePosition? pendingPosition;
    private IReadOnlyList<MapTileKey> wanted = [];
    private Point? dragFrom;
    private bool overviewReady;
    private bool tilesReady;
    private bool fromCache;
    private bool loadingTiles;
    private bool disposed;
    private long openMs = -1;
    private int maxSurfaceSide;
    private int level;

    public ImageReaderSession(Guide guide, MapTileStore store, MapCacheQueue queue, string cacheRoot, bool diagnostics)
    {
        ArgumentNullException.ThrowIfNull(guide);
        this.guide = guide;
        this.store = store;
        this.queue = queue;
        this.cacheRoot = cacheRoot;
        this.diagnostics = diagnostics;
        pyramid = new MapPyramid(guide.PixelWidth ?? 1, guide.PixelHeight ?? 1);
        canvas.Children.Add(overview);
        View = new Grid
        {
            Children = { canvas },
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        AutomationProperties.SetAutomationId(View, "MapImageView");
        AutomationProperties.SetName(View, guide.Title);
        View.SizeChanged += (_, _) => Resized();
        View.Loaded += (_, _) =>
        {
            if (View.XamlRoot is not null) View.XamlRoot.Changed += (_, _) => Resized();
        };
        View.PointerPressed += OnPointerPressed;
        View.PointerMoved += OnPointerMoved;
        View.PointerReleased += OnPointerEnded;
        View.PointerCaptureLost += OnPointerEnded;
        View.PointerWheelChanged += OnPointerWheelChanged;
        View.KeyDown += OnKeyDown;
    }

    public Grid View { get; }

    public GuideFormat Format => GuideFormat.Image;

    public ReaderCapabilities Capabilities => ReaderCapabilities.Zoom | ReaderCapabilities.FitView;

    public bool CanZoomIn => viewport?.CanZoomIn ?? true;

    public bool CanZoomOut => viewport?.CanZoomOut ?? false;

    // True while an uncached map waits for its overview.
    public bool Preparing { get; private set; }

#pragma warning disable CS0067 // A map's capabilities never change.
    public event EventHandler? CapabilitiesChanged;
    public event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored;
#pragma warning restore CS0067
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;
    public event EventHandler? ZoomChanged;
    public event EventHandler? Prepared;

    public async Task OpenAsync(ManagedGuideSource source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        openClock.Start();
        if (pyramid.Small)
        {
            Show(await LoadAsync(source.PrimaryFilePath, token));
            return;
        }
        MapCacheBuild build = queue.Ensure(guide, source.PrimaryFilePath, front: true);
        queue.OpenHash = guide.ContentSha256;
        fromCache = build.Complete.IsCompletedSuccessfully;
        if (fromCache) store.Touch(guide.ContentSha256);
        Preparing = !build.Overview.IsCompleted;
        string overviewPath = await build.Overview.WaitAsync(token);
        Show(await LoadAsync(overviewPath, token));
        if (Preparing)
        {
            Preparing = false;
            Prepared?.Invoke(this, EventArgs.Empty);
        }
        _ = WaitForTilesAsync(build.Complete);
    }

    public Task<ReaderLocation> GetLocationAsync(CancellationToken token) =>
        Task.FromResult(new ReaderLocation(
            GuideFormat.Image, ReaderLocationCodec.CurrentVersion, guide.ContentSha256,
            viewport?.ToPosition() ?? pendingPosition ?? new ImagePosition(null, 0.5, 0.5), null));

    public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.Payload is not ImagePosition position)
        {
            Apply(viewport?.FitToView(), raise: false);
            return Task.FromResult(new RestoreOutcome(RestoreKind.Unavailable));
        }
        if (viewport is null)
        {
            pendingPosition = position;
        }
        else
        {
            Apply(MapViewport.FromPosition(position, pyramid.Width, pyramid.Height, viewport.ViewWidth, viewport.ViewHeight), raise: false);
        }
        return Task.FromResult(new RestoreOutcome(RestoreKind.Exact));
    }

    public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) => Task.CompletedTask;

    public Task ExecuteAsync(ReaderAction action, CancellationToken token)
    {
        if (viewport is null) return Task.CompletedTask;
        MapViewport next = action switch
        {
            ZoomAction { Factor: > 1 } => viewport.ZoomIn(),
            ZoomAction => viewport.ZoomOut(),
            FitViewAction => viewport.FitToView(),
            _ => throw new NotSupportedException($"A map has no {action.Command} command."),
        };
        Apply(next, raise: true);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        disposed = true;
        if (queue.OpenHash == guide.ContentSha256) queue.OpenHash = null;
        canvas.Children.Clear();
        shown.Clear();
        overview.Source = null;
        memory.Clear();
        return ValueTask.CompletedTask;
    }

    private async Task WaitForTilesAsync(Task complete)
    {
        try
        {
            await complete;
        }
        catch (Exception)
        {
            // The overview stays; detail comes on the next open.
            return;
        }
        if (disposed) return;
        tilesReady = true;
        Render();
        WriteDiagnostics();
    }

    private void Show(BitmapImage bitmap)
    {
        maxSurfaceSide = Math.Max(maxSurfaceSide, Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        overview.Source = bitmap;
        overviewReady = true;
        Resized();
    }

    private void Resized()
    {
        if (disposed || View.XamlRoot is null || View.ActualWidth <= 0 || View.ActualHeight <= 0) return;
        double scale = View.XamlRoot.RasterizationScale;
        double width = View.ActualWidth * scale, height = View.ActualHeight * scale;
        canvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, View.ActualWidth, View.ActualHeight) };
        if (viewport is null)
        {
            MapViewport fitted = MapViewport.Fitted(pyramid.Width, pyramid.Height, width, height);
            Apply(pendingPosition is ImagePosition position
                ? MapViewport.FromPosition(position, pyramid.Width, pyramid.Height, width, height)
                : fitted, raise: false);
            pendingPosition = null;
            return;
        }
        if (viewport.ViewWidth != width || viewport.ViewHeight != height)
        {
            Apply(viewport.Resize(width, height), raise: true);
        }
    }

    private void Apply(MapViewport? next, bool raise)
    {
        if (next is null || disposed) return;
        bool zoomIn = CanZoomIn, zoomOut = CanZoomOut;
        viewport = next;
        Render();
        if (zoomIn != CanZoomIn || zoomOut != CanZoomOut) ZoomChanged?.Invoke(this, EventArgs.Empty);
        if (raise) Settled();
    }

    // A change has ended: the coordinator saves it after its quiet second.
    private void Settled()
    {
        LocationChanged?.Invoke(this, new LocationChangedEventArgs());
        WriteDiagnostics();
    }

    private void Render()
    {
        if (viewport is not MapViewport view || View.XamlRoot is null) return;
        double scale = View.XamlRoot.RasterizationScale;
        Place(overview, 0, 0, pyramid.Width, pyramid.Height, view, scale);
        if (overviewReady && openMs < 0)
        {
            openMs = openClock.ElapsedMilliseconds;
            WriteDiagnostics();
        }
        level = view.Level(pyramid.Small ? 0 : pyramid.Levels);
        HashSet<MapTileKey> visible = [];
        if (!pyramid.Small && tilesReady && level < pyramid.Levels)
        {
            (int firstColumn, int firstRow, int lastColumn, int lastRow) = view.VisibleTiles(level, pyramid.TileSize);
            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    visible.Add(new MapTileKey(level, column, row));
                }
            }
        }
        foreach (MapTileKey key in shown.Keys.Where(key => !visible.Contains(key)).ToList())
        {
            canvas.Children.Remove(shown[key]);
            shown.Remove(key);
        }
        int span = pyramid.TileSize << level;
        foreach (MapTileKey key in visible)
        {
            if (!memory.TryGet(key, out BitmapImage? bitmap) || bitmap is null) continue;
            if (!shown.TryGetValue(key, out Image? image))
            {
                image = new Image { Stretch = Stretch.Fill, Source = bitmap };
                shown[key] = image;
                canvas.Children.Add(image);
            }
            Place(image, key.Column * span, key.Row * span,
                Math.Min(span, pyramid.Width - key.Column * span), Math.Min(span, pyramid.Height - key.Row * span), view, scale);
        }
        double centerColumn = view.CenterX / span, centerRow = view.CenterY / span;
        wanted = [.. visible.Where(key => !failed.Contains(key) && !memory.TryGet(key, out _))
            .OrderBy(key => Math.Abs(key.Column + 0.5 - centerColumn) + Math.Abs(key.Row + 0.5 - centerRow))];
        foreach (MapTileKey key in visible) memory.Touch(key);
        if (wanted.Count > 0) LoadTiles();
    }

    // Image pixels (x, y, width, height) to the canvas, in DIPs.
    private static void Place(FrameworkElement element, double x, double y, double width, double height, MapViewport view, double scale)
    {
        Canvas.SetLeft(element, ((x - view.CenterX) * view.Zoom + view.ViewWidth / 2) / scale);
        Canvas.SetTop(element, ((y - view.CenterY) * view.Zoom + view.ViewHeight / 2) / scale);
        element.Width = width * view.Zoom / scale;
        element.Height = height * view.Zoom / scale;
    }

    // One tile at a time; each pass takes what the latest render wants.
    private async void LoadTiles()
    {
        if (loadingTiles) return;
        loadingTiles = true;
        try
        {
            while (!disposed && NextWanted() is MapTileKey key)
            {
                try
                {
                    BitmapImage bitmap = await LoadAsync(store.TilePath(guide.ContentSha256, key), CancellationToken.None);
                    if (disposed) return;
                    maxSurfaceSide = Math.Max(maxSurfaceSide, Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
                    memory.Add(key, bitmap, Math.Max(1, bitmap.PixelWidth), Math.Max(1, bitmap.PixelHeight));
                }
                catch (Exception)
                {
                    // The overview stays under a tile that won't load.
                    failed.Add(key);
                }
                Render();
            }
        }
        finally
        {
            loadingTiles = false;
        }
        WriteDiagnostics();
    }

    private MapTileKey? NextWanted()
    {
        foreach (MapTileKey key in wanted)
        {
            if (!failed.Contains(key) && !memory.TryGet(key, out _)) return key;
        }
        return null;
    }

    private static async Task<BitmapImage> LoadAsync(string path, CancellationToken token)
    {
        BitmapImage bitmap = new();
        await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await bitmap.SetSourceAsync(file.AsRandomAccessStream()).AsTask(token);
        return bitmap;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        View.Focus(FocusState.Pointer);
        if (viewport is null || !View.CapturePointer(args.Pointer)) return;
        dragFrom = args.GetCurrentPoint(View).Position;
        args.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (dragFrom is not Point from || viewport is null || View.XamlRoot is null) return;
        Point to = args.GetCurrentPoint(View).Position;
        double scale = View.XamlRoot.RasterizationScale;
        dragFrom = to;
        Apply(viewport.PanBy((to.X - from.X) * scale, (to.Y - from.Y) * scale), raise: false);
        args.Handled = true;
    }

    private void OnPointerEnded(object sender, PointerRoutedEventArgs args)
    {
        if (dragFrom is null) return;
        dragFrom = null;
        View.ReleasePointerCapture(args.Pointer);
        Settled();
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        if (viewport is null || View.XamlRoot is null) return;
        PointerPoint point = args.GetCurrentPoint(View);
        double scale = View.XamlRoot.RasterizationScale;
        int delta = point.Properties.MouseWheelDelta;
        double notches = delta / 120.0;
        VirtualKeyModifiers modifiers = args.KeyModifiers;
        MapViewport next;
        if ((modifiers & VirtualKeyModifiers.Control) != 0)
        {
            next = viewport.ZoomBy(Math.Pow(MapViewport.Step, notches), point.Position.X * scale, point.Position.Y * scale);
        }
        else if ((modifiers & VirtualKeyModifiers.Shift) != 0 || point.Properties.IsHorizontalMouseWheel)
        {
            double sign = point.Properties.IsHorizontalMouseWheel ? -1 : 1;
            next = viewport.PanBy(sign * notches * viewport.ViewWidth * MapViewport.PanFraction, 0);
        }
        else
        {
            next = viewport.PanBy(0, notches * viewport.ViewHeight * MapViewport.PanFraction);
        }
        Apply(next, raise: true);
        args.Handled = true;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (viewport is null) return;
        (int columns, int rows)? step = args.Key switch
        {
            VirtualKey.Left => (-1, 0),
            VirtualKey.Right => (1, 0),
            VirtualKey.Up => (0, -1),
            VirtualKey.Down => (0, 1),
            _ => null,
        };
        if (step is not (int columns, int rows)) return;
        Apply(viewport.PanStep(columns, rows), raise: true);
        args.Handled = true;
    }

    private void WriteDiagnostics()
    {
        if (!diagnostics || viewport is not MapViewport view) return;
        try
        {
            string folder = Path.Combine(cacheRoot, "diagnostics");
            Directory.CreateDirectory(folder);
            MapSessionDiagnostics facts = new(
                openMs, fromCache, pyramid.Small, tilesReady, level, shown.Count, maxSurfaceSide,
                memory.PeakBytes, memory.MaxBytes, view.Zoom, view.Fit, view.CenterX, view.CenterY);
            File.WriteAllText(Path.Combine(folder, $"map-session-{Environment.ProcessId}.json"), facts.ToJson());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Diagnostics never fail the reader.
        }
    }

    public static bool DiagnosticsEnabledForTest() =>
        TestGate.IsOpen(AppLane.Current.LocalEvent("MapDiagnostics", Environment.ProcessId));
}
