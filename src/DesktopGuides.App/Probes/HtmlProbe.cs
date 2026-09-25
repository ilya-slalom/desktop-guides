using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopGuides.Core.Html;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace DesktopGuides.App.Probes;

public sealed class HtmlProbe : IReaderProbe
{
    private const string Origin = "https://guide.invalid";
    private const string ContentSecurityPolicy =
        "default-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
        "font-src 'self'; script-src 'none'; frame-src 'none'; form-action 'none'; " +
        "connect-src 'none'; object-src 'none'; base-uri 'none'";

    private readonly Grid view = new();
    private readonly WebView2 browser = new();
    private readonly TextBlock markerStatus = new();
    private readonly IReadOnlyList<string> verifiedPaths;
    private HtmlAssetPolicy? policy;
    private CoreWebView2Environment? environment;
    private string? contentSha256;
    private int allowedRequests;
    private int blockedRequests;
    private int blockedPopups;
    private long openMilliseconds;
    private double scale = 1;
    private string restoreKind = "none";

    public HtmlProbe(IEnumerable<string> verifiedPaths)
    {
        this.verifiedPaths = verifiedPaths.ToList();
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        StackPanel toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
        Button jump = new() { Content = "Jump to last marker" };
        Button top = new() { Content = "Back to top" };
        Button links = new() { Content = "Try fixture links" };
        jump.Click += JumpClicked;
        top.Click += TopClicked;
        links.Click += TryLinksClicked;
        toolbar.Children.Add(jump);
        toolbar.Children.Add(top);
        toolbar.Children.Add(links);
        toolbar.Children.Add(markerStatus);
        view.Children.Add(toolbar);
        Grid.SetRow(browser, 1);
        view.Children.Add(browser);
    }

    public FrameworkElement View => view;

    public string Diagnostics =>
        $"assets {allowedRequests}, blocked {blockedRequests}, popups {blockedPopups}; " +
        $"open {openMilliseconds} ms; " +
        $"zoom {scale:0.00}; restore {restoreKind}.";

    public async Task OpenAsync(string path, int? codePage)
    {
        Stopwatch timer = Stopwatch.StartNew();
        string root = Path.GetDirectoryName(path) ??
            throw new InvalidDataException("HTML fixture has no directory.");
        string rootPrefix = root + Path.DirectorySeparatorChar;
        policy = new HtmlAssetPolicy(
            root,
            verifiedPaths.Where(item => item.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)));
        contentSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));

        string userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopGuides", "P0-WebView");
        Directory.CreateDirectory(userData);
        environment = await CoreWebView2Environment.CreateWithOptionsAsync(
            null, userData, new CoreWebView2EnvironmentOptions());
        await browser.EnsureCoreWebView2Async(environment);
        CoreWebView2 core = browser.CoreWebView2;
        core.Settings.IsScriptEnabled = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += ResourceRequested;
        core.NavigationStarting += NavigationStarting;
        core.NewWindowRequested += (_, args) =>
        {
            blockedPopups++;
            args.Handled = true;
        };
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, args) => args.Cancel = true;

        string fileName = Path.GetFileName(path);
        string url = $"{Origin}/{Uri.EscapeDataString(fileName)}";
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            browser.NavigationCompleted -= Completed;
            completion.TrySetResult(args.IsSuccess);
        }
        browser.NavigationCompleted += Completed;
        core.Navigate(url);
        if (!await completion.Task.WaitAsync(TimeSpan.FromSeconds(15)))
        {
            throw new InvalidDataException("The local HTML page did not load.");
        }
        timer.Stop();
        openMilliseconds = timer.ElapsedMilliseconds;
    }

    public async Task<string> CaptureAsync()
    {
        CoreWebView2 core = browser.CoreWebView2 ??
            throw new InvalidOperationException("Open an HTML guide first.");
        string raw = await core.ExecuteScriptAsync(
            """
            (() => {
                const y = window.scrollY;
                let selected = null;
                let nearest = Infinity;
                for (const element of document.querySelectorAll('[id]')) {
                    const box = element.getBoundingClientRect();
                    if (box.bottom < 0 || box.top > window.innerHeight) continue;
                    const distance = Math.abs(box.top);
                    if (distance < nearest) {
                        selected = element;
                        nearest = distance;
                    }
                }
                return {
                    id: selected ? selected.id : null,
                    delta: selected ? -selected.getBoundingClientRect().top : 0,
                    fraction: y / Math.max(1, document.documentElement.scrollHeight - window.innerHeight)
                };
            })()
            """);
        using JsonDocument result = JsonDocument.Parse(raw);
        JsonElement data = result.RootElement;
        HtmlLocation location = new(
            1,
            contentSha256 ?? throw new InvalidOperationException("Missing content hash."),
            data.GetProperty("id").ValueKind == JsonValueKind.Null
                ? null
                : data.GetProperty("id").GetString(),
            data.GetProperty("delta").GetDouble(),
            Math.Clamp(data.GetProperty("fraction").GetDouble(), 0, 1));
        return JsonSerializer.Serialize(location);
    }

    public async Task RestoreAsync(string locationJson)
    {
        CoreWebView2 core = browser.CoreWebView2 ??
            throw new InvalidOperationException("Open an HTML guide first.");
        HtmlLocation location = JsonSerializer.Deserialize<HtmlLocation>(locationJson) ??
            throw new InvalidDataException("Invalid HTML location.");
        if (location.SchemaVersion != 1 || !string.Equals(
                location.ContentSha256, contentSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("This position belongs to a different HTML guide.");
        }

        string id = JsonSerializer.Serialize(location.ElementId);
        string delta = location.Delta.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string fraction = location.Fraction.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string raw = await core.ExecuteScriptAsync(
            $$"""
            (() => {
                const element = document.getElementById({{id}});
                const max = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
                const target = element
                    ? window.scrollY + element.getBoundingClientRect().top + {{delta}}
                    : max * {{fraction}};
                window.scrollTo(0, Math.max(0, Math.min(target, max)));
                return element ? 'element' : 'fraction';
            })()
            """);
        restoreKind = JsonSerializer.Deserialize<string>(raw) ?? "unknown";
    }

    public async Task ChangeScaleAsync(double factor)
    {
        scale = Math.Clamp(scale * factor, 0.6, 2.5);
        CoreWebView2 core = browser.CoreWebView2 ??
            throw new InvalidOperationException("Open an HTML guide first.");
        string value = scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        await core.ExecuteScriptAsync(
            $"document.documentElement.style.zoom = '{value}';");
    }

    public ValueTask DisposeAsync()
    {
        browser.Close();
        return ValueTask.CompletedTask;
    }

    private void ResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        string? file = policy?.Resolve(args.Request.Method, args.Request.Uri);
        if (file is null)
        {
            blockedRequests++;
            args.Response = environment!.CreateWebResourceResponse(
                new MemoryStream().AsRandomAccessStream(), 403, "Forbidden",
                "Content-Type: text/plain\r\nCache-Control: no-store");
            return;
        }

        allowedRequests++;
        string contentType = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
        string headers = $"Content-Type: {contentType}\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Cache-Control: no-store\r\n" +
            $"Content-Security-Policy: {ContentSecurityPolicy}";
        args.Response = environment!.CreateWebResourceResponse(
            new MemoryStream(File.ReadAllBytes(file)).AsRandomAccessStream(), 200, "OK", headers);
    }

    private void NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (policy?.Resolve("GET", args.Uri) is null)
        {
            blockedRequests++;
            args.Cancel = true;
        }
    }

    private async void JumpClicked(object sender, RoutedEventArgs args)
    {
        try
        {
            string raw = await browser.CoreWebView2.ExecuteScriptAsync(
                """
                (() => {
                    const markers = document.querySelectorAll('[id]');
                    const marker = markers[markers.length - 1];
                    if (marker) marker.scrollIntoView();
                    return marker ? marker.id : '';
                })()
                """);
            markerStatus.Text = $"Marker: {JsonSerializer.Deserialize<string>(raw)}";
        }
        catch (Exception exception)
        {
            markerStatus.Text = $"Jump failed: {exception.Message}";
        }
    }

    private async void TopClicked(object sender, RoutedEventArgs args)
    {
        try
        {
            await browser.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0, 0)");
            markerStatus.Text = "At top.";
        }
        catch (Exception exception)
        {
            markerStatus.Text = $"Jump failed: {exception.Message}";
        }
    }

    private async void TryLinksClicked(object sender, RoutedEventArgs args)
    {
        try
        {
            await browser.CoreWebView2.ExecuteScriptAsync(
                "Array.from(document.querySelectorAll('a[href]')).forEach(link => link.click())");
            markerStatus.Text = "Fixture links attempted.";
        }
        catch (Exception exception)
        {
            markerStatus.Text = $"Link test failed: {exception.Message}";
        }
    }

    private sealed record HtmlLocation(
        int SchemaVersion, string ContentSha256, string? ElementId, double Delta, double Fraction);
}
