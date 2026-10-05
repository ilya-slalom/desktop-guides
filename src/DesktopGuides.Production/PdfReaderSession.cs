using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;

namespace DesktopGuides.Production;

internal sealed class PdfGuideLoadException(PdfGuideLoadError error)
    : Exception(PdfGuideLoadMessages.For(error))
{
    public PdfGuideLoadError Error { get; } = error;
}

// The Reader's session for one PDF guide: a Windows.Data.Pdf preview of the
// current page beside PdfPig's text of the same page. Loads run one at a
// time and the latest wins, so fast page turns never show one page's text
// beside another page's preview.
internal sealed class PdfReaderSession : IReaderSession
{
    private const int FailuresBeforeStop = 3;
    // ERROR_WRONG_PASSWORD. Import rejects encrypted PDFs, so this is a
    // managed copy replaced after import.
    private const int WrongPassword = unchecked((int)0x8007052B);
    private static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(150);
    private static readonly PageResult BothFailed = new(null, null, 0, double.NaN);
    private readonly Guide guide;
    private readonly string filePath;
    private readonly PdfPageTextSource text;
    private readonly string contentSha256;
    private readonly string cacheRoot;
    private readonly bool writeDiagnostics;
    private readonly PdfRenderCache<BitmapImage> cache = new(PdfRasterBudget.MaxBytes);
    private readonly LatestWinsScheduler<PageResult> scheduler;
    private FileStream? file;
    private IRandomAccessStream? stream;
    private WinPdf.PdfDocument? document;
    private int pageCount;
    private CancellationTokenSource? resizeDelay;
    private int target;
    private readonly PdfPagePosition position = new();
    private int appliedWidth;
    private double appliedAspect = double.NaN;
    private int failedPages;
    private bool failed;
    private bool disposed;

    public PdfReaderSession(Guide guide, PdfGuideLoaded loaded, string cacheRoot, bool writeDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(guide);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);
        this.guide = guide;
        filePath = loaded.FilePath;
        text = loaded.Text;
        contentSha256 = loaded.ContentSha256;
        this.cacheRoot = cacheRoot;
        this.writeDiagnostics = writeDiagnostics;
        scheduler = new LatestWinsScheduler<PageResult>(
            LoadPageAsync, Apply, (index, _) => Apply(index, BothFailed));
        View = new PdfReaderView();
        View.PreviewSizeChanged += OnPreviewSizeChanged;
        View.PreviewLayoutChanged += OnPreviewLayoutChanged;
        View.PreviewScrolled += OnPreviewScrolled;
    }

    public PdfReaderView View { get; }
    public GuideFormat Format => GuideFormat.Pdf;
    public ReaderCapabilities Capabilities => ReaderCapabilities.PageNavigation;

    // Capabilities don't change during a PDF session.
    public event EventHandler? CapabilitiesChanged { add { } remove { } }
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;
    // Raised once, with Failed, after three pages in a row fail both ways.
    public event EventHandler<PdfGuideLoadError>? Failed;

    public static bool DiagnosticsEnabledForTest() =>
        TestGate.IsOpen($@"Local\DesktopGuides.Preview.PdfDiagnostics.{Environment.ProcessId}");

    public async Task OpenAsync(ManagedGuideSource source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Guide.Id != guide.Id || source.PrimaryFilePath != filePath)
        {
            throw new ArgumentException("The source is a different guide.", nameof(source));
        }
        ObjectDisposedException.ThrowIf(disposed, this);
        if (document is not null)
        {
            throw new InvalidOperationException("The guide is already open.");
        }
        // The loader already resolved and checked this path; it isn't
        // resolved again, and no StorageFile is used.
        try
        {
            file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream = file.AsRandomAccessStream();
            document = await WinPdf.PdfDocument.LoadFromStreamAsync(stream).AsTask(token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new PdfGuideLoadException(OpenError(error));
        }
        if (document.PageCount != text.PageCount)
        {
            throw new PdfGuideLoadException(PdfGuideLoadError.Damaged);
        }
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        pageCount = (int)document.PageCount;
        target = 0;
        scheduler.Request(0);
    }

    private static PdfGuideLoadError OpenError(Exception error) => error switch
    {
        FileNotFoundException or DirectoryNotFoundException => PdfGuideLoadError.Missing,
        IOException or UnauthorizedAccessException => PdfGuideLoadError.Unreadable,
        _ when error.HResult == WrongPassword => PdfGuideLoadError.PasswordProtected,
        _ => PdfGuideLoadError.Damaged,
    };

    public Task<ReaderLocation> GetLocationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (document is null) throw new InvalidOperationException("The guide isn't open.");
        return Task.FromResult(
            PdfLocationRules.Capture(contentSha256, position.Page, position.Fraction, pageCount));
    }

    public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(location);
        token.ThrowIfCancellationRequested();
        if (document is null) throw new InvalidOperationException("The guide isn't open.");
        PdfRestore restore = PdfLocationRules.Restore(location, contentSha256, pageCount);
        if (!disposed && restore.Outcome.Kind != RestoreKind.Unavailable)
        {
            GoTo(restore.PageIndex, restore.PageFraction);
        }
        return Task.FromResult(restore.Outcome);
    }

    // T10.2 adds fit-width and zoom; the preview has no theme of its own.
    public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) => Task.CompletedTask;

    public Task ExecuteAsync(ReaderAction action, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(action);
        token.ThrowIfCancellationRequested();
        if (disposed) return Task.CompletedTask;
        if (document is null) throw new InvalidOperationException("The guide isn't open.");
        switch (action)
        {
            // Turns build on the wanted page, not the shown one, so ten fast
            // Next clicks move ten pages. A turn past either end does nothing.
            case PageTurnAction turn:
                long next = Math.Clamp((long)target + turn.Delta, 0, pageCount - 1);
                if (next != target) GoTo(next, 0);
                break;
            case PageEdgeAction edge:
                GoTo(edge.Edge == ReaderEdge.Start ? 0 : pageCount - 1, 0);
                break;
            default:
                throw new NotSupportedException($"PDF guides don't support {action.Command}.");
        }
        return Task.CompletedTask;
    }

    // A new page applies the point when its load is shown. The page already
    // shown has no load coming, so it applies the point now.
    private void GoTo(long index, double fraction)
    {
        int page = (int)Math.Clamp(index, 0, pageCount - 1);
        position.Target(page, fraction);
        if (page != target)
        {
            target = page;
            scheduler.Request(page);
        }
        else if (page == position.Page)
        {
            position.Shown(page);
            ScrollToPoint();
            RaiseLocationChanged();
        }
    }

    // Text extraction runs on a worker while the raster renders on the UI
    // thread; both finish before the result is offered, so a cancellation
    // surfaces only once neither is still using the document.
    private async Task<PageResult> LoadPageAsync(int index, CancellationToken token)
    {
        Task<PdfPageText?> textTask = ReadTextAsync(index, token);
        Task<Raster> rasterTask = RenderAsync(index, token);
        await Task.WhenAll(textTask, rasterTask);
        Raster raster = await rasterTask;
        return new PageResult(await textTask, raster.Image, raster.Width, raster.Aspect);
    }

    private async Task<PdfPageText?> ReadTextAsync(int index, CancellationToken token)
    {
        try
        {
            return await text.GetPageTextAsync(index, token);
        }
        catch (PdfPageTextException)
        {
            return null;
        }
    }

    private async Task<Raster> RenderAsync(int index, CancellationToken token)
    {
        int width = 0;
        double aspect = double.NaN;
        try
        {
            using WinPdf.PdfPage page = document!.GetPage((uint)index);
            aspect = page.Size.Height / page.Size.Width;
            PdfRasterWidth raster = WidthFor(page.Size.Width, aspect);
            if (raster.IsTooLarge) return new Raster(null, 0, aspect);
            width = raster.Width;
            PdfRenderKey key = new(contentSha256, index, width);
            if (cache.TryGet(key, out BitmapImage? hit)) return new Raster(hit, width, aspect);
            using InMemoryRandomAccessStream png = new();
            await page.RenderToStreamAsync(
                png, new WinPdf.PdfPageRenderOptions { DestinationWidth = (uint)width }).AsTask(token);
            png.Seek(0);
            BitmapImage image = new();
            await image.SetSourceAsync(png).AsTask(token);
            // Measured from the decoded image, not from the requested width.
            cache.Add(key, image, image.PixelWidth, image.PixelHeight);
            return new Raster(image, width, aspect);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A page the engine can't draw fails on its own; the next may work.
            return new Raster(null, width, aspect);
        }
    }

    // Before layout the preview has no width, so the page's own width stands in.
    private PdfRasterWidth WidthFor(double pageWidth, double aspect)
    {
        double display = View.PreviewWidth >= 1 ? View.PreviewWidth : pageWidth;
        double scale = View.XamlRoot?.RasterizationScale ?? 1;
        return PdfRasterBudget.WidthFor(display * scale, aspect, PdfRasterBudget.MaxBytes);
    }

    // Runs on the UI thread, only for the newest request.
    private void Apply(int index, PageResult result)
    {
        if (disposed || failed) return;
        position.Shown(index);
        appliedWidth = result.RasterWidth;
        appliedAspect = result.Aspect;
        bool shown = true;
        try
        {
            View.ShowPage(index, pageCount, result.Image, result.Text);
            // A same-height image raises no SizeChanged, so apply the point
            // here; a new height re-applies it from OnPreviewLayoutChanged.
            ScrollToPoint();
        }
        catch (Exception)
        {
            // Apply must never throw: the scheduler would stall. A page that
            // can't be shown counts as failed both ways.
            shown = false;
        }
        failedPages = !shown || (result.Image is null && result.Text is null) ? failedPages + 1 : 0;
        RaiseLocationChanged();
        if (failedPages < FailuresBeforeStop) return;
        failed = true;
        // Queued, so the shell disposes the session after this load returns.
        View.DispatcherQueue.TryEnqueue(() =>
        {
            if (!disposed) Failed?.Invoke(this, PdfGuideLoadError.Failed);
        });
    }

    // The image or the viewport changed height: put the point back.
    private void OnPreviewLayoutChanged(object? sender, EventArgs args)
    {
        if (!disposed) ScrollToPoint();
    }

    // Only a user's scroll moves the point; PdfPagePosition ignores the
    // app's own scrolls and the scroller's clamps.
    private void OnPreviewScrolled(object? sender, EventArgs args)
    {
        if (disposed) return;
        PdfPreviewLayout layout = View.PreviewLayout;
        if (position.Scrolled(layout.Offset, layout.ImageHeight, layout.ViewportHeight))
        {
            RaiseLocationChanged();
        }
    }

    private void ScrollToPoint()
    {
        PdfPreviewLayout layout = View.PreviewLayout;
        View.ScrollTo(position.OffsetFor(layout.ImageHeight, layout.ViewportHeight));
    }

    private void RaiseLocationChanged()
    {
        try
        {
            LocationChanged?.Invoke(this, new LocationChangedEventArgs());
        }
        catch (Exception)
        {
            // A throwing handler must not escape into the scheduler or a
            // XAML event.
        }
    }

    // Re-renders the current page only when the raster width changes.
    private async void OnPreviewSizeChanged(object? sender, EventArgs args)
    {
        resizeDelay?.Cancel();
        resizeDelay?.Dispose();
        CancellationTokenSource delay = new();
        resizeDelay = delay;
        try
        {
            await Task.Delay(ResizeDelay, delay.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (disposed || document is null || View.PreviewWidth < 1 || !double.IsFinite(appliedAspect))
        {
            return;
        }
        if (WidthFor(View.PreviewWidth, appliedAspect).Width != appliedWidth)
        {
            scheduler.Request(target);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        LocationChanged = null;
        Failed = null;
        View.PreviewSizeChanged -= OnPreviewSizeChanged;
        View.PreviewLayoutChanged -= OnPreviewLayoutChanged;
        View.PreviewScrolled -= OnPreviewScrolled;
        resizeDelay?.Cancel();
        resizeDelay?.Dispose();
        resizeDelay = null;
        bool clean = true;
        try
        {
            await scheduler.CancelAsync();
        }
        catch (Exception)
        {
            // Recorded for the test diagnostics; closing goes on regardless.
            clean = false;
        }
        PdfSessionDiagnostics counts = new(
            scheduler.Requests, scheduler.Loads, scheduler.StaleResults,
            cache.PeakBytes, cache.MaxBytes, cache.Count,
            text.PeakPages, text.PeakCharacters, clean, cache.Evictions);
        View.Clear();
        cache.Clear();
        text.Dispose();
        stream?.Dispose();
        file?.Dispose();
        WriteDiagnostics(counts);
    }

    private void WriteDiagnostics(PdfSessionDiagnostics counts)
    {
        if (!writeDiagnostics) return;
        try
        {
            string folder = Path.Combine(cacheRoot, "diagnostics");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"pdf-{guide.Id:N}.json"), counts.ToJson());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Test diagnostics must never affect closing a guide.
        }
    }

    private sealed record Raster(BitmapImage? Image, int Width, double Aspect);

    private sealed record PageResult(PdfPageText? Text, BitmapImage? Image, int RasterWidth, double Aspect);
}
