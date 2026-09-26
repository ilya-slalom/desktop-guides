using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace DesktopGuides.App.Probes;

// M0 evaluation surface only. No application data is served to this viewer.
public sealed class PdfWebCandidateProbe : IReaderProbe
{
    private const string Url = "https://pdf.invalid/guide.pdf";
    private readonly WebView2 browser = new();
    private CoreWebView2Environment? environment;
    private string? pdfPath;
    private string? profileRoot;
    private int allowedRequests;
    private int blockedRequests;

    public FrameworkElement View => browser;

    public string Diagnostics =>
        $"WebView2 PDF candidate: allowed {allowedRequests}, blocked {blockedRequests}";

    public async Task OpenAsync(string path, int? codePage)
    {
        pdfPath = path;
        profileRoot = Path.Combine(
            Path.GetTempPath(), "DesktopGuides-P1-WebPdf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profileRoot);
        environment = await CoreWebView2Environment.CreateWithOptionsAsync(
            null, profileRoot, new CoreWebView2EnvironmentOptions());
        await browser.EnsureCoreWebView2Async(environment);
        CoreWebView2 core = browser.CoreWebView2;
        core.Settings.IsScriptEnabled = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += ResourceRequested;
        core.NavigationStarting += (_, args) =>
        {
            if (args.Uri != Url)
            {
                args.Cancel = true;
            }
        };
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, args) => args.Cancel = true;

        TaskCompletionSource<bool> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            browser.NavigationCompleted -= Completed;
            completion.TrySetResult(args.IsSuccess);
        }
        browser.NavigationCompleted += Completed;
        core.Navigate(Url);
        if (!await completion.Task.WaitAsync(TimeSpan.FromSeconds(15)))
        {
            throw new InvalidDataException("The local PDF did not load in WebView2.");
        }
    }

    public Task<string> CaptureAsync() =>
        throw new NotSupportedException(
            "The WebView2 PDF candidate has no validated page/fraction locator API.");

    public Task RestoreAsync(string locationJson) =>
        throw new NotSupportedException(
            "The WebView2 PDF candidate has no validated page/fraction locator API.");

    public Task ChangeScaleAsync(double factor) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        browser.Close();
        if (profileRoot is not null)
        {
            try
            {
                Directory.Delete(profileRoot, true);
            }
            catch (IOException)
            {
                // The WebView2 process can keep its profile open briefly.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return ValueTask.CompletedTask;
    }

    private void ResourceRequested(
        CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (args.Request.Method == "GET" && args.Request.Uri == Url && pdfPath is not null)
        {
            allowedRequests++;
            args.Response = environment!.CreateWebResourceResponse(
                new MemoryStream(File.ReadAllBytes(pdfPath)).AsRandomAccessStream(),
                200, "OK",
                "Content-Type: application/pdf\r\n" +
                "X-Content-Type-Options: nosniff\r\n" +
                "Cache-Control: no-store");
            return;
        }
        blockedRequests++;
        args.Response = environment!.CreateWebResourceResponse(
            new MemoryStream().AsRandomAccessStream(), 403, "Forbidden",
            "Content-Type: text/plain\r\nCache-Control: no-store");
    }
}
