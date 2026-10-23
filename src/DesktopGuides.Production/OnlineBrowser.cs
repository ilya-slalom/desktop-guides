using DesktopGuides.Core.Online;
using DesktopGuides.Infrastructure.Online;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Streams;

namespace DesktopGuides.Production;

/// <summary>
/// S22: the in-app browser for Find online. Pages load as in any browser,
/// scripts and ads included, in a persistent profile apart from the
/// reader's. It stays on the included sources; anything else opens in the
/// system browser. Downloads, permissions and external schemes are refused.
/// </summary>
internal sealed class OnlineBrowser : IAsyncDisposable
{
    private readonly CoreWebView2Environment environment;
    private readonly OnlineFixtureRoutes? fixtures;
    private bool disposed;

    private OnlineBrowser(CoreWebView2Environment environment, WebView2 view, OnlineFixtureRoutes? fixtures)
    {
        this.environment = environment;
        this.fixtures = fixtures;
        View = view;
    }

    public WebView2 View { get; }

    public Uri? CurrentUrl => Uri.TryCreate(View.CoreWebView2?.Source, UriKind.Absolute, out Uri? url) ? url : null;

    /// <summary>A page finished loading in the visible browser.</summary>
    public event EventHandler? PageLoaded;

    /// <summary>A link left the sources; the caller opens it in the system browser.</summary>
    public event EventHandler<Uri>? ExternalLinkRequested;

    public static string ProfileFolder(string cacheRoot) => Path.Combine(cacheRoot, "Online", "Profile");

    private string? loadLog;

    public static async Task<OnlineBrowser> CreateAsync(string cacheRoot, OnlineFixtureRoutes? fixtures)
    {
        string profile = ProfileFolder(cacheRoot);
        Directory.CreateDirectory(profile);
        // No reader arguments: this browser reaches the network.
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateWithOptionsAsync(
            null, profile, new CoreWebView2EnvironmentOptions());
        WebView2 view = new();
        await view.EnsureCoreWebView2Async(environment);
        OnlineBrowser browser = new(environment, view, fixtures);
#if !PUBLIC_LANE
        if (fixtures is not null)
        {
            browser.loadLog = Path.Combine(cacheRoot, "diagnostics", $"online-loads-{Environment.ProcessId}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(browser.loadLog)!);
        }
#endif
        browser.Configure(view.CoreWebView2);
        view.CoreWebView2.NavigationCompleted += (_, _) => browser.PageLoaded?.Invoke(browser, EventArgs.Empty);
        return browser;
    }

    public void Navigate(Uri url)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        View.CoreWebView2.Navigate(url.AbsoluteUri);
    }

    public async Task<string> GetHtmlAsync()
    {
        string json = await View.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
        return System.Text.Json.JsonSerializer.Deserialize(json, ProductionJsonContext.Default.String) ?? "";
    }

    // The capture running in this browser, if any: its image loads may leave the sources.
    private WebViewPageLoader? capture;

    /// <summary>
    /// A loader that steps this browser through a guide's pages. While it
    /// is in use, the caller keeps the reader from browsing.
    /// </summary>
    public WebViewPageLoader CreateLoader()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        WebViewPageLoader loader = new(View.CoreWebView2, loadLog);
        capture = loader;
#if !PUBLIC_LANE
        FixtureServed += loader.RecordServed;
#endif
        return loader;
    }

    public void EndCapture(WebViewPageLoader loader)
    {
#if !PUBLIC_LANE
        FixtureServed -= loader.RecordServed;
#endif
        if (ReferenceEquals(capture, loader)) capture = null;
        loader.Dispose();
    }

    internal void Configure(CoreWebView2 core)
    {
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
#if PUBLIC_LANE
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.NavigationStarting += (_, args) => OnNavigationStarting(args);
        core.NewWindowRequested += (_, args) => OnNewWindowRequested(args);
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
#if !PUBLIC_LANE
        if (fixtures is not null)
        {
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) => AnswerFromFixtures(core, args);
        }
#endif
    }

    // Off the sources, a page never loads here: a link the user followed
    // opens in the system browser, and a navigation a script started (an
    // ad redirect, say) is only stopped. During a capture nothing launches,
    // and the browser leaves the sources only for the image it was asked to load.
    private void OnNavigationStarting(CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? url) || url.Scheme == "about" ||
            OnlineSources.Find(url) is not null)
        {
            return;
        }
        if (capture?.ImageTarget is { } image && image == url)
        {
            return;
        }
        args.Cancel = true;
        if (capture is null && args.IsUserInitiated && IsWeb(url))
        {
            ExternalLinkRequested?.Invoke(this, url);
        }
    }

    private void OnNewWindowRequested(CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        Uri? url = Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? parsed) ? parsed : null;
        switch (OnlineNavigation.ForNewWindow(url, capturing: capture is not null, args.IsUserInitiated))
        {
            case NewWindowAction.OpenHere:
                View.CoreWebView2.Navigate(url!.AbsoluteUri);
                break;
            case NewWindowAction.OpenExternally:
                ExternalLinkRequested?.Invoke(this, url!);
                break;
        }
    }

    private static bool IsWeb(Uri url) => url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp;

#if !PUBLIC_LANE
    // Test runs: every request is answered from the fixture manifest.
    internal void AnswerFromFixtures(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out Uri? url) || url.Scheme == "data") return;
        fixtures!.TryAnswer(url, out int status, out string contentType, out string? file, out string? location);
        // In memory, so no fixture file stays open in the browser.
        IRandomAccessStream? body = file is not null && File.Exists(file)
            ? new MemoryStream(File.ReadAllBytes(file)).AsRandomAccessStream() : null;
        string headers = $"Content-Type: {contentType}" + (location is null ? "" : $"\r\nLocation: {location}");
        args.Response = environment.CreateWebResourceResponse(
            body, status, status == 200 ? "OK" : "Fixture", headers);
        FixtureServed?.Invoke(url, file);
    }

    internal event Action<Uri, string?>? FixtureServed;
#endif

    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            View.Close();
        }
        return ValueTask.CompletedTask;
    }
}
