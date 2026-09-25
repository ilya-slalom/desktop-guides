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
    private string? relativeDocument;
    private string? stagingRoot;
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
        string[] originals = verifiedPaths
            .Where(item => item.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _ = new HtmlAssetPolicy(root, originals);
        contentSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        relativeDocument = Path.GetFileName(root) + "/" + Path.GetFileName(path);
        stagingRoot = Path.Combine(Path.GetTempPath(), "DesktopGuides-P0", Guid.NewGuid().ToString("N"));
        List<string> staged = [];
        foreach (string original in originals)
        {
            string destination = Path.Combine(stagingRoot, Path.GetRelativePath(root, original));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(original, destination);
            staged.Add(destination);
        }
        policy = new HtmlAssetPolicy(stagingRoot, staged);

        string userData = Path.Combine(stagingRoot, "profile");
        Directory.CreateDirectory(userData);
        try
        {
            environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                null, userData, new CoreWebView2EnvironmentOptions());
            await browser.EnsureCoreWebView2Async(environment);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "HTML requires Microsoft Edge WebView2 Runtime. Install or repair the runtime and retry.",
                exception);
        }
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
                for (const element of document.querySelectorAll('h1,h2,h3,h4,p,li,pre,blockquote')) {
                    const box = element.getBoundingClientRect();
                    if (box.bottom < 0 || box.top > window.innerHeight) continue;
                    const distance = Math.abs(box.top);
                    if (distance < nearest) {
                        selected = element;
                        nearest = distance;
                    }
                }
                return {
                    id: selected && selected.id ? selected.id : null,
                    quote: selected ? selected.textContent.trim().replace(/\s+/g, ' ').slice(0, 96) : null,
                    delta: selected ? -selected.getBoundingClientRect().top : 0,
                    fraction: y / Math.max(1, document.documentElement.scrollHeight - window.innerHeight)
                };
            })()
            """);
        using JsonDocument result = JsonDocument.Parse(raw);
        JsonElement data = result.RootElement;
        HtmlLocation location = new(
            2,
            relativeDocument ?? throw new InvalidOperationException("Missing document path."),
            contentSha256 ?? throw new InvalidOperationException("Missing content hash."),
            data.GetProperty("id").ValueKind == JsonValueKind.Null
                ? null
                : data.GetProperty("id").GetString(),
            data.GetProperty("quote").ValueKind == JsonValueKind.Null
                ? null
                : data.GetProperty("quote").GetString(),
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
        if (location.SchemaVersion != 2 ||
            !string.Equals(location.RelativeDocument, relativeDocument, StringComparison.Ordinal) ||
            location.ElementId?.Length > 128 || location.TextContext?.Length > 96 ||
            !double.IsFinite(location.Delta) || !double.IsFinite(location.Fraction))
        {
            throw new InvalidDataException("Invalid position or different HTML guide.");
        }

        string id = JsonSerializer.Serialize(location.ElementId);
        string quote = JsonSerializer.Serialize(location.TextContext);
        string delta = location.Delta.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string fraction = location.Fraction.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string restoreScript =
            $$"""
            (() => {
                let element = document.getElementById({{id}});
                let kind = element ? 'element' : 'fraction';
                const quote = {{quote}};
                if (!element && quote) {
                    element = Array.from(document.querySelectorAll('h1,h2,h3,h4,p,li,pre,blockquote'))
                        .find(candidate => candidate.textContent.trim().replace(/\s+/g, ' ').includes(quote));
                    if (element) kind = 'text context';
                }
                const max = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
                const target = element
                    ? window.scrollY + element.getBoundingClientRect().top + {{delta}}
                    : max * {{fraction}};
                window.scrollTo(0, Math.max(0, Math.min(target, max)));
                return kind;
            })()
            """;
        string raw = await core.ExecuteScriptAsync(restoreScript);
        await core.ExecuteScriptAsync(
            """
            Promise.race([
                Promise.all(Array.from(document.images).map(image => image.complete
                    ? Promise.resolve()
                    : new Promise(resolve => {
                        image.addEventListener('load', resolve, {once: true});
                        image.addEventListener('error', resolve, {once: true});
                    }))),
                new Promise(resolve => setTimeout(resolve, 1500))
            ])
            """);
        raw = await core.ExecuteScriptAsync(restoreScript);
        restoreKind = JsonSerializer.Deserialize<string>(raw) ?? "unknown";
        if (!string.Equals(location.ContentSha256, contentSha256, StringComparison.OrdinalIgnoreCase))
        {
            restoreKind = "approximate " + restoreKind;
        }
        markerStatus.Text = $"Restored {restoreKind}: {location.ElementId ?? location.TextContext ?? "page"}";
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
        if (stagingRoot is not null)
        {
            try
            {
                Directory.Delete(stagingRoot, true);
            }
            catch (IOException)
            {
                // WebView2 can keep its profile open briefly after the control closes.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
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
        int SchemaVersion, string RelativeDocument, string ContentSha256,
        string? ElementId, string? TextContext, double Delta, double Fraction);
}
