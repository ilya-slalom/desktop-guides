using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;
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
    }

    public WebView2 View { get; }
    public GuideFormat Format => GuideFormat.Html;
    public ReaderCapabilities Capabilities => ReaderCapabilities.Scroll;

    // Capabilities don't change during an HTML session.
    public event EventHandler? CapabilitiesChanged { add { } remove { } }
    // T09.3 adds position tracking.
    public event EventHandler<LocationChangedEventArgs>? LocationChanged { add { } remove { } }
    public event EventHandler<Uri>? ExternalLinkRequested;

    public static HtmlSessionDiagnostics? DiagnosticsForTest() =>
        TestGate.IsOpen($@"Local\DesktopGuides.Preview.HtmlDiagnostics.{Environment.ProcessId}")
            ? new HtmlSessionDiagnostics()
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
        try
        {
            Directory.CreateDirectory(profile);
            environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                null, profile, new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = HtmlBrowserEnvironment.Arguments,
                });
            await View.EnsureCoreWebView2Async(environment);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new HtmlGuideLoadException(HtmlGuideLoadError.RuntimeMissing);
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
            core.NavigationCompleted += Completed;
            core.Navigate(policy.EntryUri.AbsoluteUri);
            success = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A timeout or a COM failure leaves the guide unreadable, not the app.
            throw new HtmlGuideLoadException(HtmlGuideLoadError.Changed);
        }
        finally
        {
            // A closed view raises nothing more, and touching it could throw.
            if (!disposed) core.NavigationCompleted -= Completed;
        }
        if (!success || !entryServed)
        {
            throw new HtmlGuideLoadException(HtmlGuideLoadError.Changed);
        }
    }

    public Task<ReaderLocation> GetLocationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // T09.3 captures the real position; until then the entry's top.
        return Task.FromResult(new ReaderLocation(
            GuideFormat.Html, 1, contentSha256 ?? string.Empty,
            new HtmlPosition(policy.Entry.RequestPath, null, null, 0, 0), 0));
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

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        ExternalLinkRequested = null;
        WriteDiagnostics();
        if (core is not null)
        {
            core.WebResourceRequested -= OnWebResourceRequested;
            core.NavigationStarting -= OnNavigationStarting;
            core.FrameNavigationStarting -= OnFrameNavigationStarting;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.PermissionRequested -= OnPermissionRequested;
            core.DownloadStarting -= OnDownloadStarting;
            core.LaunchingExternalUriScheme -= OnLaunchingExternalUriScheme;
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
        // T09.1 sweeps profiles a crash or a slow exit leaves behind.
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
}
