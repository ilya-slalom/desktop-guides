using DesktopGuides.Infrastructure.Online;
using Microsoft.Web.WebView2.Core;

namespace DesktopGuides.Production;

/// <summary>
/// S22: loads capture pages in a hidden browser on the in-app browser's
/// session, one at a time. Images come from the page load's own responses;
/// the page is scrolled to start lazy images. A load gives up after 30 s.
/// </summary>
internal sealed partial class WebViewPageLoader : IPageLoader, IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly CoreWebView2Controller controller;
    private readonly SemaphoreSlim one = new(1, 1);
    private Dictionary<Uri, byte[]>? images;

    /// <summary>The image being loaded on its own, which may sit on another host.</summary>
    internal Uri? ImageTarget { get; private set; }

    // Installed tests only: one line per load, for diagnosing a stalled capture.
    private readonly string? loadLog;

    internal WebViewPageLoader(CoreWebView2Controller controller, string? loadLog = null)
    {
        this.loadLog = loadLog;
        this.controller = controller;
        controller.CoreWebView2.WebResourceResponseReceived += OnResponseReceived;
    }

    // Test runs: an image the fixtures served, which WebView2 may not report
    // as a received response.
    internal void RecordServed(Uri url, string? file)
    {
        Dictionary<Uri, byte[]>? collecting = images;
        if (collecting is null || file is null ||
            Path.GetExtension(file).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp"))
        {
            return;
        }
        byte[] bytes = File.ReadAllBytes(file);
        lock (collecting) collecting[url] = bytes;
    }

    private CoreWebView2 Core => controller.CoreWebView2;

    public async Task<PageLoad> LoadPageAsync(Uri url, CancellationToken token)
    {
        await one.WaitAsync(token);
        try
        {
            images = [];
            int status = await NavigateAsync(url, token);
            if (status == 0) return new PageLoad(0, null, images);
            for (int step = 1; step <= 4; step++)
            {
                await Core.ExecuteScriptAsync($"window.scrollTo(0, document.documentElement.scrollHeight * {step} / 4)");
                await Task.Delay(250, token);
            }
            string json = await Core.ExecuteScriptAsync("document.documentElement.outerHTML");
            string? html = System.Text.Json.JsonSerializer.Deserialize(json, ProductionJsonContext.Default.String);
            return new PageLoad(status, html, images);
        }
        finally
        {
            images = null;
            one.Release();
        }
    }

    public async Task<byte[]?> LoadImageAsync(Uri url, CancellationToken token)
    {
        await one.WaitAsync(token);
        try
        {
            images = [];
            ImageTarget = url;
            int status = await NavigateAsync(url, token);
            return status == 200 && images.TryGetValue(url, out byte[]? bytes) ? bytes : null;
        }
        finally
        {
            ImageTarget = null;
            images = null;
            one.Release();
        }
    }

    // The status of the navigation, or 0 when it timed out.
    private async Task<int> NavigateAsync(Uri url, CancellationToken token)
    {
        TaskCompletionSource<int> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        long started = Environment.TickCount64;
        // Only this navigation's completion counts: a cancelled earlier one
        // can complete late, after a Cancel and a quick Resume.
        ulong? ours = null;
        // Kept as variables: C#/WinRT detaches a handler only by the same delegate instance.
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs> starting =
            (_, args) => ours ??= args.NavigationId;
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> completed = (_, args) =>
        {
            Log($"{url} completed id={args.NavigationId} ours={ours} http={args.HttpStatusCode} success={args.IsSuccess} " +
                $"error={args.WebErrorStatus} after={Environment.TickCount64 - started}ms");
            if (ours != args.NavigationId) return;
            done.TrySetResult(args.HttpStatusCode is > 0 ? args.HttpStatusCode : args.IsSuccess ? 200 : 0);
        };
        Core.NavigationStarting += starting;
        Core.NavigationCompleted += completed;
        try
        {
            Core.Navigate(url.AbsoluteUri);
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(Timeout);
            try
            {
                return await done.Task.WaitAsync(limit.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                Log($"{url} timed out after {Environment.TickCount64 - started}ms; source={Core.Source}");
                Core.Stop();
                return 0;
            }
            catch (OperationCanceledException)
            {
                // A cancelled capture leaves no navigation running behind it.
                Core.Stop();
                throw;
            }
        }
        finally
        {
            Core.NavigationStarting -= starting;
            Core.NavigationCompleted -= completed;
        }
    }

    private void Log(string line)
    {
        if (loadLog is null) return;
        try
        {
            File.AppendAllText(loadLog, $"{DateTimeOffset.UtcNow:O} {line}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }

    private async void OnResponseReceived(CoreWebView2 sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        Dictionary<Uri, byte[]>? collecting = images;
        if (collecting is null) return;
        // An async void handler must never throw, so every check is inside the try.
        try
        {
            if (args.Response.StatusCode != 200 ||
                !Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out Uri? url) ||
                !args.Response.Headers.Contains("Content-Type") ||
                !args.Response.Headers.GetHeader("Content-Type").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            using Windows.Storage.Streams.IRandomAccessStream? body = await args.Response.GetContentAsync();
            if (body is null) return;
            using Stream stream = body.AsStreamForRead();
            using MemoryStream copy = new();
            await stream.CopyToAsync(copy);
            lock (collecting) collecting[url] = copy.ToArray();
        }
        catch (Exception)
        {
            // An image whose body isn't available is loaded directly later.
        }
    }

    public void Dispose()
    {
        controller.CoreWebView2.WebResourceResponseReceived -= OnResponseReceived;
        controller.Close();
        one.Dispose();
    }
}
