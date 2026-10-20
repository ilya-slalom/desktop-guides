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
    private readonly nint window;
    private bool disposed;

    private OnlineBrowser(CoreWebView2Environment environment, WebView2 view, nint window, OnlineFixtureRoutes? fixtures)
    {
        this.environment = environment;
        this.window = window;
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

    public static async Task<OnlineBrowser> CreateAsync(string cacheRoot, nint window, OnlineFixtureRoutes? fixtures)
    {
        string profile = ProfileFolder(cacheRoot);
        Directory.CreateDirectory(profile);
        // No reader arguments: this browser reaches the network.
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateWithOptionsAsync(
            null, profile, new CoreWebView2EnvironmentOptions());
        WebView2 view = new();
        await view.EnsureCoreWebView2Async(environment);
        OnlineBrowser browser = new(environment, view, window, fixtures);
        browser.Configure(view.CoreWebView2, visible: true);
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

    /// <summary>A hidden browser on the same session, for capturing pages.</summary>
    public async Task<WebViewPageLoader> CreateLoaderAsync()
    {
        CoreWebView2Controller controller = await environment.CreateCoreWebView2ControllerAsync(
            CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)window));
        controller.IsVisible = false;
        controller.Bounds = new Windows.Foundation.Rect(0, 0, 1280, 2000);
        Configure(controller.CoreWebView2, visible: false);
        WebViewPageLoader loader = new(controller);
#if !PUBLIC_LANE
        FixtureServed += loader.RecordServed;
#endif
        return loader;
    }

    internal void Configure(CoreWebView2 core, bool visible)
    {
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = visible;
#if PUBLIC_LANE
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
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

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? url) || url.Scheme == "about")
        {
            return;
        }
        if (OnlineSources.Find(url) is null)
        {
            args.Cancel = true;
            if (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            {
                ExternalLinkRequested?.Invoke(this, url);
            }
        }
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? url) &&
            (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp))
        {
            ExternalLinkRequested?.Invoke(this, url);
        }
    }

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
