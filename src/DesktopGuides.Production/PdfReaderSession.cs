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

// What the toolbar shows for zoom: the status label and which directions
// have a step left on the shown page.
public readonly record struct PdfZoomState(string Label, bool CanZoomIn, bool CanZoomOut);

// The Reader's session for one PDF guide: a Windows.Data.Pdf preview of the
// current page beside PdfPig's text of the same page. Loads run one at a
// time and the latest wins, so fast page turns never show one page's text
// beside another page's preview.
internal sealed class PdfReaderSession : IReaderSession, IFindableReader, IOutlineReader
{
    private const int FailuresBeforeStop = 3;
    // ERROR_WRONG_PASSWORD after PdfPig accepted the password (P12): an
    // engine split, reported as PasswordProtected.
    private const int WrongPassword = unchecked((int)0x8007052B);
    private static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(150);
    private static readonly PageResult BothFailed = new(null, null, 0, double.NaN, double.NaN);
    private readonly Guide guide;
    private readonly string filePath;
    private readonly PdfPageTextSource text;
    private readonly PdfPageTextSource search;
    // What a scan reads: the search source, slowed per page under a test gate.
    private readonly IPdfPageTextReader scanPages;
    private PdfPageText? shownText;
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
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(2);
    private PdfZoom zoom = PdfZoom.Fit;
    // The shown page's natural width in DIPs (P1); NaN before the first page.
    private double appliedNatural = double.NaN;
    // The page a zoom or resize re-renders. Its Apply isn't reader movement,
    // so it raises no LocationChanged and schedules no progress save.
    private int relayoutOf = -1;
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
        search = loaded.Search;
        scanPages = TestGate.IsOpen(AppLane.Current.LocalEvent("PdfScanDelay", Environment.ProcessId))
            ? new DelayedPages(search)
            : search;
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
    public ReaderCapabilities Capabilities =>
        ReaderCapabilities.PageNavigation | ReaderCapabilities.PageJump |
        ReaderCapabilities.FitWidth | ReaderCapabilities.Zoom | ReaderCapabilities.Find |
        ReaderCapabilities.Outline;
    public int PageCount => pageCount;
    public PdfZoomState ZoomState
    {
        get
        {
            double fit = FitPercent;
            bool known = double.IsFinite(fit) && fit > 0;
            return new(zoom.Label, known && zoom.CanZoomIn(fit), known && zoom.CanZoomOut(fit));
        }
    }
    public event EventHandler? ZoomChanged;

    // The shown page's fit width as a percent of its natural width; NaN
    // before layout or before a page is shown.
    private double FitPercent =>
        View.PreviewWidth >= 1 && double.IsFinite(appliedNatural) && appliedNatural > 0
            ? View.PreviewWidth / appliedNatural * 100
            : double.NaN;

    // Capabilities don't change during a PDF session.
    public event EventHandler? CapabilitiesChanged { add { } remove { } }
    // T14.3: their layout keeps its own place; nothing to report.
    public event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored { add { } remove { } }
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;
    // Raised once, with Failed, after three pages in a row fail both ways.
    public event EventHandler<PdfGuideLoadError>? Failed;

    public static bool DiagnosticsEnabledForTest() =>
        TestGate.IsOpen(AppLane.Current.LocalEvent("PdfDiagnostics", Environment.ProcessId));

    public Task OpenAsync(ManagedGuideSource source, CancellationToken token) =>
        OpenAsync(source, null, token);

    // The password is a parameter only: Windows.Data.Pdf uses it once, and
    // the session never stores it. PdfPig already opened with it (P3).
    public async Task OpenAsync(ManagedGuideSource source, string? password, CancellationToken token)
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
            document = await (password is null
                ? WinPdf.PdfDocument.LoadFromStreamAsync(stream)
                : WinPdf.PdfDocument.LoadFromStreamAsync(stream, password)).AsTask(token);
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
            // The count comes from the opened document, never the locator.
            case PageJumpAction jump:
                if (!PdfLocationRules.IsPageInRange(jump.PageNumber, pageCount))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(action), jump.PageNumber, PageEntry.RangeMessage(pageCount));
                }
                GoTo(jump.PageNumber - 1, 0);
                break;
            case FitWidthAction:
                SetZoom(PdfZoom.Fit);
                break;
            // Before the first page is laid out there's no fit percent, so a
            // zoom does nothing rather than guess.
            case ZoomAction step when double.IsFinite(FitPercent):
                SetZoom(step.Factor > 1 ? zoom.In(FitPercent) : step.Factor < 1 ? zoom.Out(FitPercent) : zoom);
                break;
            case ZoomAction:
                break;
            default:
                throw new NotSupportedException($"PDF guides don't support {action.Command}.");
        }
        return Task.CompletedTask;
    }

    // T18.1: the last query's scan, on the search source's own document.
    // A step waits only as far as the scan must go, then shows the match's
    // page and selects the match in the page text. Progress is paused
    // while the bar is open, so the page turns save nothing.
    private PdfTextScan? scan;
    private string? findText;
    private int? matchIndex;
    private PdfMatch? pendingMatch;

    public event EventHandler<FindResult>? ResultUpdated;

    public async Task<FindResult> FindAsync(FindQuery query, FindDirection direction, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (document is null || disposed) return FindResult.Failure;
        if (query.Text != findText)
        {
            ClearFind();
            PdfTextScan started = new(scanPages, query);
            started.Finished += OnScanFinished;
            scan = started;
            findText = query.Text;
        }
        PdfTextScan current = scan!;
        int? index = await current.StepAsync(matchIndex, target, direction, token);
        // A newer query, a Close or a dispose came in while the step waited.
        if (!ReferenceEquals(current, scan) || disposed) throw new OperationCanceledException(token);
        matchIndex = index;
        if (index is not int at)
        {
            pendingMatch = null;
            View.ClearMatch();
            return current.ResultFor(null);
        }
        ShowMatch(current.MatchAt(at));
        return current.ResultFor(at);
    }

    public void ClearFind()
    {
        if (scan is not null)
        {
            scan.Finished -= OnScanFinished;
            scan.Dispose();
            scan = null;
        }
        findText = null;
        matchIndex = null;
        pendingMatch = null;
        if (!disposed) View.ClearMatch();
    }

    // T18.2: the file's own outline, from the search document, so reading
    // never waits on it. Each entry opens its page from the top.
    public async Task<Outline> GetOutlineAsync(CancellationToken token)
    {
        if (document is null || disposed) return Outline.Empty;
        IReadOnlyList<PdfOutlineNode> nodes = await search.GetOutlineAsync(token);
        List<OutlineItem> items = [];
        void Add(IReadOnlyList<PdfOutlineNode> level, int depth)
        {
            foreach (PdfOutlineNode node in level)
            {
                items.Add(new OutlineItem(node.Title, depth,
                    PdfLocationRules.Capture(contentSha256, node.PageIndex, 0, pageCount), node.PageIndex));
                Add(node.Children, depth + 1);
            }
        }
        Add(nodes, 1);
        return OutlineTree.Build(OutlineSource.Document, items);
    }

    public long OrderOf(ReaderLocation location) =>
        location.Payload is PdfPosition position ? position.PageIndex : 0;

    // The shown page selects now; another page selects when its load shows.
    private void ShowMatch(PdfMatch match)
    {
        pendingMatch = match;
        if (match.PageIndex == target && match.PageIndex == position.Page) ApplyMatch(match, shownText);
        else GoTo(match.PageIndex, 0);
    }

    private void ApplyMatch(PdfMatch match, PdfPageText? page)
    {
        if (page is not { HasLetters: true }) return;
        (int start, int length) = TextBoxOffsets.Map(page.Text, new TextMatch(match.Start, match.Length));
        View.ShowMatch(start, length);
    }

    // On a worker: the total is final now, so the bar loses its "+".
    private void OnScanFinished(object? sender, EventArgs args) =>
        View.DispatcherQueue.TryEnqueue(() =>
        {
            if (disposed || !ReferenceEquals(sender, scan) || scan is null) return;
            try
            {
                ResultUpdated?.Invoke(this, scan.ResultFor(matchIndex));
            }
            catch (Exception)
            {
                // A handler must not escape into the dispatcher.
            }
        });

    // A new page applies the point when its load is shown. The page already
    // shown has no load coming, so it applies the point now.
    private void GoTo(long index, double fraction)
    {
        int page = (int)Math.Clamp(index, 0, pageCount - 1);
        position.Target(page, fraction);
        if (page != target)
        {
            target = page;
            relayoutOf = -1;
            scheduler.Request(page);
        }
        else if (page == position.Page)
        {
            position.Shown(page);
            ScrollToPoint();
            RaiseLocationChanged();
        }
    }

    private void SetZoom(PdfZoom next)
    {
        if (next == zoom) return;
        zoom = next;
        Relayout();
        RaiseZoomChanged();
    }

    // Sets the new width now, so the old bitmap stretches to it while the
    // sharper one renders; the layout guard then puts the point back.
    private void Relayout()
    {
        View.SetPageWidth(DisplayWidth(View.PreviewWidth, appliedNatural));
        relayoutOf = target == position.Page ? target : -1;
        scheduler.Request(target);
    }

    // NaN means fit: the view fills the viewport.
    private double DisplayWidth(double viewport, double natural) =>
        zoom.IsFit || !double.IsFinite(natural) || natural <= 0
            ? double.NaN
            : zoom.WidthFor(viewport >= 1 ? viewport : natural, natural);

    private void RaiseZoomChanged()
    {
        try
        {
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Like LocationChanged: a handler must not escape into a XAML event.
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
        return new PageResult(await textTask, raster.Image, raster.Width, raster.Aspect, raster.NaturalWidth);
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
        double natural = double.NaN;
        try
        {
            using WinPdf.PdfPage page = document!.GetPage((uint)index);
            aspect = page.Size.Height / page.Size.Width;
            natural = page.Size.Width;
            PdfRasterWidth raster = WidthFor(page.Size.Width, aspect);
            if (raster.IsTooLarge) return new Raster(null, 0, aspect, natural);
            width = raster.Width;
            PdfRenderKey key = new(contentSha256, index, width);
            if (cache.TryGet(key, out BitmapImage? hit)) return new Raster(hit, width, aspect, natural);
            using InMemoryRandomAccessStream png = new();
            await page.RenderToStreamAsync(
                png, new WinPdf.PdfPageRenderOptions { DestinationWidth = (uint)width }).AsTask(token);
            png.Seek(0);
            BitmapImage image = new();
            await image.SetSourceAsync(png).AsTask(token);
            // Measured from the decoded image, not from the requested width.
            cache.Add(key, image, image.PixelWidth, image.PixelHeight);
            return new Raster(image, width, aspect, natural);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A page the engine can't draw fails on its own; the next may work.
            return new Raster(null, width, aspect, natural);
        }
    }

    // Before layout the preview has no width, so the page's own width stands in.
    private PdfRasterWidth WidthFor(double pageWidth, double aspect)
    {
        double viewport = View.PreviewWidth >= 1 ? View.PreviewWidth : pageWidth;
        double display = zoom.IsFit ? viewport : zoom.WidthFor(viewport, pageWidth);
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
        // A failed page keeps the last known width, so a zoom still has a
        // fit percent to step from.
        if (double.IsFinite(result.NaturalWidth)) appliedNatural = result.NaturalWidth;
        bool relayout = index == relayoutOf;
        relayoutOf = -1;
        bool shown = true;
        try
        {
            // Pages differ in size, so a percent gives each its own width.
            View.SetPageWidth(DisplayWidth(View.PreviewWidth, appliedNatural));
            View.ShowPage(index, pageCount, zoom.Label, result.Image, result.Text);
            shownText = result.Text;
            // A pending match shows only on its own page.
            if (pendingMatch is PdfMatch match && match.PageIndex == index) ApplyMatch(match, result.Text);
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
        if (!relayout) RaiseLocationChanged();
        // The new page may allow a different step, so the toolbar refreshes.
        RaiseZoomChanged();
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
        if (disposed || document is null || View.PreviewWidth < 1 || !double.IsFinite(appliedAspect)
            || !double.IsFinite(appliedNatural))
        {
            return;
        }
        if (WidthFor(appliedNatural, appliedAspect).Width != appliedWidth)
        {
            Relayout();
        }
        // The fit percent follows the viewport, so a step may have opened or closed.
        RaiseZoomChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        LocationChanged = null;
        ResultUpdated = null;
        // The scan's page read honours its token; its document closes when free.
        scan?.Dispose();
        scan = null;
        Failed = null;
        ZoomChanged = null;
        View.PreviewSizeChanged -= OnPreviewSizeChanged;
        View.PreviewLayoutChanged -= OnPreviewLayoutChanged;
        View.PreviewScrolled -= OnPreviewScrolled;
        resizeDelay?.Cancel();
        resizeDelay?.Dispose();
        resizeDelay = null;
        bool clean = true;
        // A hung PdfPig page must not hold Back: wait at most CloseWait for
        // the scheduler, then at most CloseWait for the text source.
        Task cancelling = scheduler.CancelAsync();
        bool settled = await Task.WhenAny(cancelling, Task.Delay(CloseWait)) == cancelling;
        if (settled)
        {
            try
            {
                await cancelling;
            }
            catch (Exception)
            {
                // Recorded for the test diagnostics; closing goes on regardless.
                clean = false;
            }
        }
        else
        {
            clean = false;
        }
        bool closed;
        try
        {
            closed = await text.CloseAsync(settled ? CloseWait : TimeSpan.Zero);
        }
        catch (Exception)
        {
            // A faulted text source counts as not closed; the rest still runs.
            closed = false;
            clean = false;
        }
        try
        {
            // Never waits: a page still being read closes the document after.
            await search.CloseAsync(TimeSpan.Zero);
        }
        catch (Exception)
        {
            clean = false;
        }
        PdfSessionDiagnostics counts = new(
            scheduler.Requests, scheduler.Loads, scheduler.StaleResults,
            cache.PeakBytes, cache.MaxBytes, cache.Count,
            text.PeakPages, text.PeakCharacters, clean, cache.Evictions,
            AbandonedExtraction: !closed);
        View.Clear();
        cache.Clear();
        if (settled)
        {
            stream?.Dispose();
            file?.Dispose();
        }
        else
        {
            // The render may still be reading the stream; close it when the
            // load ends, and observe its fault so it isn't unobserved.
            IRandomAccessStream? heldStream = stream;
            FileStream? heldFile = file;
            _ = cancelling.ContinueWith(
                done =>
                {
                    _ = done.Exception;
                    heldStream?.Dispose();
                    heldFile?.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
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

    // Test gate only: each scanned page waits 40 ms, so a step's wait shows.
    private sealed class DelayedPages(IPdfPageTextReader pages) : IPdfPageTextReader
    {
        public int PageCount => pages.PageCount;

        public async Task<PdfPageText> GetPageTextAsync(int pageIndex, CancellationToken token)
        {
            await Task.Delay(40, token);
            return await pages.GetPageTextAsync(pageIndex, token);
        }
    }

    private sealed record Raster(BitmapImage? Image, int Width, double Aspect, double NaturalWidth);

    private sealed record PageResult(
        PdfPageText? Text, BitmapImage? Image, int RasterWidth, double Aspect, double NaturalWidth);
}
