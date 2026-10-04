using System.Text.Json;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace DesktopGuides.Production;

internal sealed class HtmlGuideLoadException(HtmlGuideLoadError error)
    : Exception(HtmlGuideLoadMessages.For(error))
{
    public HtmlGuideLoadError Error { get; } = error;
}

// The Reader's session for one HTML guide. WebView2 sees only the guide's
// own origin: every request is answered from the manifest rows, every
// navigation away from the entry document is cancelled, and a click on a
// website link is handed to the shell's bar instead of being followed.
internal sealed class HtmlReaderSession : IReaderSession
{
    private static int sessionCounter;
    private readonly HtmlRequestPolicy policy;
    private readonly ManagedHtmlAssetReader reader;
    private readonly string cacheRoot;
    private readonly HtmlSessionDiagnostics? diagnostics;
    private readonly string profile;
    private CoreWebView2Environment? environment;
    private CoreWebView2? core;
    private string? contentSha256;
    private bool entryNavigated;
    private bool entryServed;
    private bool disposed;
    private TaskCompletionSource<bool>? pendingNavigation;
    private bool failed;
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(5);
    private static readonly HtmlCapture Start = new(0, null, null, 0);
    private readonly bool positionForTest;
    private readonly DispatcherQueueTimer tracker;
    private HtmlCapture? current;
    private HtmlScroll? lastScroll;
    private bool ticking;
    private static readonly TimeSpan ResizeSettle = TimeSpan.FromMilliseconds(300);
    private CancellationTokenSource? resizeDelay;
    private bool opened;
    private bool resizing;
    // Bumped by every size change: a capture or baseline taken across one
    // is from a page mid-reflow and is dropped.
    private int generation;

    public HtmlReaderSession(HtmlGuideLoaded loaded, string cacheRoot, HtmlSessionDiagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);
        policy = loaded.Policy;
        reader = loaded.Reader;
        this.cacheRoot = cacheRoot;
        this.diagnostics = diagnostics;
        profile = Path.Combine(cacheRoot, "WebView2", Guid.NewGuid().ToString("N"));
        View = new WebView2();
        positionForTest = TestGate.IsOpen($@"Local\DesktopGuides.Preview.HtmlPosition.{Environment.ProcessId}");
        tracker = View.DispatcherQueue.CreateTimer();
        tracker.Interval = TimeSpan.FromMilliseconds(500);
        tracker.IsRepeating = true;
        tracker.Tick += OnTrackerTick;
        View.SizeChanged += OnViewSizeChanged;
    }

    public WebView2 View { get; }
    public GuideFormat Format => GuideFormat.Html;
    public ReaderCapabilities Capabilities => ReaderCapabilities.Scroll;

    // Capabilities don't change during an HTML session.
    public event EventHandler? CapabilitiesChanged { add { } remove { } }
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;
    public event EventHandler<Uri>? ExternalLinkRequested;
    // Raised once, with Crashed, when the browser or the page's renderer
    // dies after the guide opened.
    public event EventHandler<HtmlGuideLoadError>? Failed;

    public static HtmlSessionDiagnostics? DiagnosticsForTest() =>
        TestGate.IsOpen($@"Local\DesktopGuides.Preview.HtmlDiagnostics.{Environment.ProcessId}")
            ? new HtmlSessionDiagnostics()
            : null;

    // The installed smoke points the probe at an empty folder, so it fails
    // the way it does on a PC without WebView2.
    private static string? MissingRuntimeFolderForTest(string cacheRoot) =>
        TestGate.IsOpen($@"Local\DesktopGuides.Preview.WebView2Missing.{Environment.ProcessId}")
            ? Path.Combine(cacheRoot, "missing-runtime-test")
            : null;

    public async Task OpenAsync(ManagedGuideSource source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Guide.Id != policy.GuideId)
        {
            throw new ArgumentException("The source is a different guide.", nameof(source));
        }
        ObjectDisposedException.ThrowIf(disposed, this);
        contentSha256 = source.Guide.ContentSha256;
        string? browserFolder = MissingRuntimeFolderForTest(cacheRoot);
        string? version;
        try
        {
            if (browserFolder is not null) Directory.CreateDirectory(browserFolder);
            version = CoreWebView2Environment.GetAvailableBrowserVersionString(browserFolder);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new HtmlGuideLoadException(HtmlGuideLoadError.RuntimeMissing);
        }
        if (string.IsNullOrEmpty(version))
        {
            throw new HtmlGuideLoadException(HtmlGuideLoadError.RuntimeMissing);
        }
        try
        {
            Directory.CreateDirectory(profile);
            environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                browserFolder, profile, new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = HtmlBrowserEnvironment.Arguments,
                });
            await View.EnsureCoreWebView2Async(environment);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The runtime is installed but didn't start.
            throw new HtmlGuideLoadException(HtmlGuideLoadError.RuntimeFailed);
        }
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);

        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args) =>
            completion.TrySetResult(args.IsSuccess);
        core = View.CoreWebView2;
        bool success;
        try
        {
            CoreWebView2Settings settings = core.Settings;
            settings.IsScriptEnabled = false;
            settings.IsWebMessageEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
            core.NavigationStarting += OnNavigationStarting;
            core.FrameNavigationStarting += OnFrameNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.PermissionRequested += OnPermissionRequested;
            core.DownloadStarting += OnDownloadStarting;
            core.LaunchingExternalUriScheme += OnLaunchingExternalUriScheme;
            core.ProcessFailed += OnProcessFailed;
            core.NavigationCompleted += Completed;
            pendingNavigation = completion;
            core.Navigate(policy.EntryUri.AbsoluteUri);
            success = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        }
        catch (Exception error) when (error is not OperationCanceledException and not HtmlGuideLoadException)
        {
            // A timeout or a COM failure leaves the guide unreadable, not the app.
            throw new HtmlGuideLoadException(HtmlGuideLoadError.Changed);
        }
        finally
        {
            pendingNavigation = null;
            // A closed or crashed view raises nothing more, and touching it could throw.
            if (!disposed && !failed) core.NavigationCompleted -= Completed;
        }
        if (!success || !entryServed)
        {
            // A dying renderer can fail the navigation before ProcessFailed
            // reaches the completion source.
            throw new HtmlGuideLoadException(failed ? HtmlGuideLoadError.Crashed : HtmlGuideLoadError.Changed);
        }
        opened = true;
        tracker.Start();
    }

    // A capture that fails or is rejected keeps the last good point.
    public async Task<ReaderLocation> GetLocationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Mid-reflow, the page's top isn't the reader's point.
        if (!resizing && await CaptureAsync() is HtmlCapture capture) current = capture;
        token.ThrowIfCancellationRequested();
        return HtmlLocationRules.Capture(contentSha256 ?? string.Empty, policy.Entry.RequestPath, current ?? Start);
    }

    public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(location);
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new RestoreOutcome(
            RestoreKind.Unavailable, "HTML positions are restored in a later preview."));
    }

    public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) => Task.CompletedTask;

    public Task ExecuteAsync(ReaderAction action, CancellationToken token) =>
        throw new NotSupportedException("HTML guides have no reader commands yet.");

    // Runs a fixed host script in the entry document only. Any failure,
    // including a page that navigated away or a renderer that died, is null.
    private async Task<string?> RunScriptAsync(string script)
    {
        if (disposed || failed || core is null) return null;
        try
        {
            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out Uri? source) ||
                !HtmlNavigationPolicy.IsEntryDocument(source, policy.EntryUri))
            {
                return null;
            }
            string reply = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", EvaluateParams(script))
                .AsTask().WaitAsync(ScriptTimeout);
            return EvaluateValue(reply);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ExecuteScriptAsync runs with a user gesture, which makes the page's
    // own refreshes and redirects look like a person's click to the
    // navigation policy. Runtime.evaluate without one keeps them page-initiated.
    private static string EvaluateParams(string script) =>
        JsonSerializer.Serialize(new { expression = script, returnByValue = true, userGesture = false });

    // The value as JSON, as ExecuteScriptAsync would give it; null when the
    // script threw.
    private static string? EvaluateValue(string reply)
    {
        using JsonDocument json = JsonDocument.Parse(reply);
        if (json.RootElement.TryGetProperty("exceptionDetails", out _)) return null;
        return json.RootElement.GetProperty("result").TryGetProperty("value", out JsonElement value)
            ? value.GetRawText()
            : "null";
    }

    private async Task<HtmlCapture?> CaptureAsync()
    {
        string? reply = await RunScriptAsync(HtmlPositionScripts.Capture);
        if (disposed) return null;
        HtmlCapture? capture = HtmlLocationRules.ParseCapture(reply, policy.EntryUri);
        if (capture is null) diagnostics?.RecordRejectedCapture();
        return capture;
    }

    // A cheap scroll read every tick; a capture only after a move.
    private async void OnTrackerTick(DispatcherQueueTimer sender, object args)
    {
        if (ticking || disposed) return;
        ticking = true;
        try
        {
            int started = generation;
            HtmlScroll? scroll = HtmlLocationRules.ParseScroll(await RunScriptAsync(HtmlPositionScripts.ReadScroll));
            if (scroll is null || disposed || started != generation ||
                !HtmlLocationRules.Moved(lastScroll, scroll))
            {
                return;
            }
            HtmlCapture? capture = await CaptureAsync();
            if (capture is null || disposed || started != generation) return;
            lastScroll = scroll;
            if (capture == current) return;
            current = capture;
            WritePositionForTest();
            RaiseLocationChanged();
        }
        catch (Exception)
        {
            // A tick must never take down the app.
        }
        finally
        {
            ticking = false;
        }
    }

    // Tracking pauses while the size changes; 300 ms after the last change
    // the current point goes back to the top and the scroll it leaves is the
    // new baseline. Chromium's own scroll change during the reflow is never
    // taken as the reader's move.
    private async void OnViewSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (!opened || disposed) return;
        generation++;
        resizing = true;
        tracker.Stop();
        resizeDelay?.Cancel();
        resizeDelay?.Dispose();
        CancellationTokenSource delay = new();
        resizeDelay = delay;
        try
        {
            await Task.Delay(ResizeSettle, delay.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            await ReapplyAsync();
        }
        catch (Exception)
        {
            // A failed re-apply leaves the page where the reflow put it.
        }
        finally
        {
            if (resizeDelay == delay && !disposed)
            {
                resizeDelay = null;
                delay.Dispose();
                resizing = false;
                tracker.Start();
            }
        }
    }

    private async Task ReapplyAsync()
    {
        int started = generation;
        if (current is HtmlCapture point)
        {
            // A page with no text has only its fraction.
            HtmlRestoreStep step = point.Quote is null ? HtmlRestoreStep.Fraction : HtmlRestoreStep.Exact;
            await ScrollToTargetAsync(new HtmlRestoreTarget(step, point.Offset, point.Fraction));
        }
        HtmlScroll? scroll = HtmlLocationRules.ParseScroll(await RunScriptAsync(HtmlPositionScripts.ReadScroll));
        if (scroll is not null && started == generation && !disposed) lastScroll = scroll;
    }

    // Puts the target character's box at the viewport top. An offset whose
    // text has no box (hidden since capture) falls back to the fraction, and
    // the returned target says so. Null when no script replied.
    private async Task<(HtmlRestoreTarget Target, int Pending)?> ScrollToTargetAsync(HtmlRestoreTarget target)
    {
        if (target.Step != HtmlRestoreStep.Fraction &&
            HtmlLocationRules.ParsePending(await RunScriptAsync(HtmlPositionScripts.ScrollToOffset(target.Offset))) is int pending)
        {
            return (target, pending);
        }
        HtmlRestoreTarget fraction = target with { Step = HtmlRestoreStep.Fraction };
        return HtmlLocationRules.ParsePending(await RunScriptAsync(HtmlPositionScripts.ScrollToFraction(fraction.Fraction))) is int left
            ? (fraction, left)
            : null;
    }

    private void RaiseLocationChanged()
    {
        try
        {
            LocationChanged?.Invoke(this, new LocationChangedEventArgs());
        }
        catch (Exception)
        {
            // A throwing handler must not escape into the timer.
        }
    }

    private async void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        Windows.Foundation.Deferral deferral = args.GetDeferral();
        string context = string.Empty;
        try
        {
            context = args.ResourceContext.ToString();
            HtmlRequestDecision decision = policy.Decide(args.Request.Method, args.Request.Uri);
            if (decision is HtmlServe serve)
            {
                HtmlAssetRead read = await Task.Run(() => reader.Read(serve.Asset));
                if (read.Status == HtmlAssetReadStatus.Served && !disposed && environment is not null)
                {
                    diagnostics?.RecordServed(serve.Asset.RequestPath);
                    if (serve.Asset.Kind == GuideAssetKind.EntryHtml) entryServed = true;
                    args.Response = environment.CreateWebResourceResponse(
                        new MemoryStream(read.Bytes!).AsRandomAccessStream(), 200, "OK",
                        HtmlRequestPolicy.ServedHeaders(serve.ContentType));
                    return;
                }
                Deny(args, read.Status == HtmlAssetReadStatus.Changed
                    ? HtmlDenyReason.HashMismatch
                    : HtmlDenyReason.FileMissing, context);
                return;
            }
            Deny(args, ((HtmlDeny)decision).Reason, context);
        }
        catch (Exception)
        {
            // An unset response would send the request to the network: deny it instead.
            try
            {
                Deny(args, HtmlDenyReason.Malformed, context);
            }
            catch (Exception)
            {
                // The view closed while a request was in flight.
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    // Answers 403 even after dispose; only a live session counts it.
    private void Deny(CoreWebView2WebResourceRequestedEventArgs args, HtmlDenyReason reason, string context)
    {
        if (environment is null) return;
        if (!disposed) diagnostics?.RecordDenied(reason, context);
        args.Response = environment.CreateWebResourceResponse(
            new MemoryStream().AsRandomAccessStream(), 403, "Forbidden", HtmlRequestPolicy.DeniedHeaders);
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        HtmlNavigation navigation = HtmlNavigationPolicy.Classify(
            args.Uri, policy.EntryUri, entryNavigated, args.IsUserInitiated);
        switch (navigation.Kind)
        {
            case HtmlNavigationKind.Entry:
                entryNavigated = true;
                break;
            case HtmlNavigationKind.SameDocument:
                break;
            case HtmlNavigationKind.External:
                args.Cancel = true;
                ExternalLinkRequested?.Invoke(this, navigation.ExternalUri!);
                break;
            default:
                args.Cancel = true;
                break;
        }
    }

    private static void OnFrameNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args) =>
        args.Cancel = true;

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        HtmlNavigation navigation = HtmlNavigationPolicy.Classify(
            args.Uri, policy.EntryUri, entryNavigated: true, args.IsUserInitiated);
        if (navigation.Kind == HtmlNavigationKind.External)
        {
            ExternalLinkRequested?.Invoke(this, navigation.ExternalUri!);
        }
    }

    private static void OnPermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args) =>
        args.State = CoreWebView2PermissionState.Deny;

    private static void OnDownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args) =>
        args.Cancel = true;

    // Defense in depth: NavigationStarting already cancels custom schemes.
    private static void OnLaunchingExternalUriScheme(
        CoreWebView2 sender, CoreWebView2LaunchingExternalUriSchemeEventArgs args) => args.Cancel = true;

    // Chromium restarts GPU, utility and frame processes by itself; only a
    // lost browser or page renderer leaves the guide unreadable.
    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        if (disposed || failed || args.ProcessFailedKind is not (
                CoreWebView2ProcessFailedKind.BrowserProcessExited or
                CoreWebView2ProcessFailedKind.RenderProcessExited or
                CoreWebView2ProcessFailedKind.RenderProcessUnresponsive))
        {
            return;
        }
        failed = true;
        if (pendingNavigation?.TrySetException(new HtmlGuideLoadException(HtmlGuideLoadError.Crashed)) == true)
        {
            return;
        }
        Failed?.Invoke(this, HtmlGuideLoadError.Crashed);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        tracker.Stop();
        tracker.Tick -= OnTrackerTick;
        LocationChanged = null;
        View.SizeChanged -= OnViewSizeChanged;
        resizeDelay?.Cancel();
        resizeDelay?.Dispose();
        resizeDelay = null;
        ExternalLinkRequested = null;
        Failed = null;
        WriteDiagnostics();
        if (core is not null)
        {
            try
            {
                core.WebResourceRequested -= OnWebResourceRequested;
                core.NavigationStarting -= OnNavigationStarting;
                core.FrameNavigationStarting -= OnFrameNavigationStarting;
                core.NewWindowRequested -= OnNewWindowRequested;
                core.PermissionRequested -= OnPermissionRequested;
                core.DownloadStarting -= OnDownloadStarting;
                core.LaunchingExternalUriScheme -= OnLaunchingExternalUriScheme;
                core.ProcessFailed -= OnProcessFailed;
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or
                                              InvalidOperationException)
            {
                // A crashed browser leaves a view that can't be touched; the
                // handlers check disposed anyway.
            }
        }
        if (environment is not null)
        {
            // The profile folder is locked until the browser process exits.
            TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
            environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            View.Close();
            await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        else
        {
            View.Close();
        }
        // A profile still locked here is removed by the next startup's sweep.
        try
        {
            if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void WriteDiagnostics()
    {
        if (diagnostics is null) return;
        try
        {
            string folder = Path.Combine(cacheRoot, "diagnostics");
            Directory.CreateDirectory(folder);
            int number = Interlocked.Increment(ref sessionCounter);
            File.WriteAllText(
                Path.Combine(folder, $"html-session-{Environment.ProcessId}-{number}.json"),
                diagnostics.ToJson(policy.GuideId));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Test diagnostics must never affect closing a guide.
        }
    }

    // Test gate only: the current point as the codec writes it. Written to
    // a temporary file and moved, so the smoke never reads half a file.
    private void WritePositionForTest()
    {
        if (!positionForTest || disposed) return;
        try
        {
            string? locator = current is null ? null : ReaderLocationCodec.Serialize(
                HtmlLocationRules.Capture(contentSha256 ?? string.Empty, policy.Entry.RequestPath, current));
            string json = JsonSerializer.Serialize(new
            {
                locator,
                kind = (string?)null,
                step = (string?)null,
                reason = (string?)null
            });
            string folder = Path.Combine(cacheRoot, "diagnostics");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"html-position-{Environment.ProcessId}.json");
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Test diagnostics must never affect reading.
        }
    }
}
