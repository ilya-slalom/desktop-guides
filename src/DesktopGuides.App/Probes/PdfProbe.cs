using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DesktopGuides.App.Probes;

public sealed class PdfProbe : IReaderProbe
{
    private const long MaxRasterBytes = 96L * 1024 * 1024;

    private readonly Grid view = new();
    private readonly ScrollViewer scroller = new();
    private readonly Image pageImage = new() { Stretch = Stretch.None };
    private readonly TextBlock pageStatus = new();
    private PdfDocument? document;
    private string? contentSha256;
    private int pageIndex;
    private int renderGeneration;
    private double scale = 1;
    private long currentRasterBytes;
    private long peakRasterBytes;
    private long openMilliseconds;
    private string restoreKind = "none";

    public PdfProbe()
    {
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        StackPanel toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
        Button previous = new() { Content = "Previous page" };
        Button next = new() { Content = "Next page" };
        previous.Click += async (_, _) => await TurnPageAsync(-1);
        next.Click += async (_, _) => await TurnPageAsync(1);
        toolbar.Children.Add(previous);
        toolbar.Children.Add(next);
        toolbar.Children.Add(pageStatus);
        view.Children.Add(toolbar);

        TextBlock accessibilityNote = new()
        {
            Text = "PDF pages are images in this probe. Document text is not available to screen readers.",
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(accessibilityNote, 1);
        view.Children.Add(accessibilityNote);

        Border divider = new() { Height = 8 };
        Grid.SetRow(divider, 2);
        view.Children.Add(divider);

        scroller.Content = pageImage;
        AutomationProperties.SetName(pageImage, "PDF page image");
        scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Grid.SetRow(scroller, 3);
        view.Children.Add(scroller);
    }

    public FrameworkElement View => view;

    public string Diagnostics =>
        $"page {pageIndex + 1}/{document?.PageCount ?? 0}; zoom {scale:0.00}; " +
        $"raster {currentRasterBytes / 1048576.0:0.0} MiB (peak {peakRasterBytes / 1048576.0:0.0}); " +
        $"open {openMilliseconds} ms; restore {restoreKind}.";

    public async Task OpenAsync(string path, int? codePage)
    {
        Stopwatch timer = Stopwatch.StartNew();
        contentSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        document = await PdfDocument.LoadFromFileAsync(file);
        if (document.PageCount == 0)
        {
            throw new InvalidDataException("PDF has no pages.");
        }
        await RenderPageAsync();
        timer.Stop();
        openMilliseconds = timer.ElapsedMilliseconds;
        UpdateStatus();
    }

    public Task<string> CaptureAsync()
    {
        if (document is null || contentSha256 is null)
        {
            throw new InvalidOperationException("Open a PDF guide first.");
        }
        double fraction = scroller.ScrollableHeight <= 0
            ? 0
            : Math.Clamp(scroller.VerticalOffset / Math.Max(1, pageImage.ActualHeight), 0, 1);
        return Task.FromResult(JsonSerializer.Serialize(
            new PdfLocation(1, contentSha256, pageIndex, fraction)));
    }

    public async Task RestoreAsync(string locationJson)
    {
        PdfDocument activeDocument = document ??
            throw new InvalidOperationException("Open a PDF guide first.");
        PdfLocation location = JsonSerializer.Deserialize<PdfLocation>(locationJson) ??
            throw new InvalidDataException("Invalid PDF location.");
        if (location.SchemaVersion != 1 || !double.IsFinite(location.PageFraction))
        {
            throw new InvalidDataException("Invalid PDF location.");
        }

        pageIndex = Math.Clamp(location.PageIndex, 0, (int)activeDocument.PageCount - 1);
        await RenderPageAsync();
        await Task.Yield();
        scroller.UpdateLayout();
        scroller.ChangeView(
            null,
            Math.Min(
                Math.Clamp(location.PageFraction, 0, 1) * pageImage.ActualHeight,
                scroller.ScrollableHeight),
            null,
            true);
        restoreKind = string.Equals(
            location.ContentSha256, contentSha256, StringComparison.OrdinalIgnoreCase)
            ? "exact page"
            : "approximate page";
        UpdateStatus();
    }

    public async Task ChangeScaleAsync(double factor)
    {
        scale = Math.Clamp(scale * factor, 0.5, 3);
        if (document is not null)
        {
            await RenderPageAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        renderGeneration++;
        pageImage.Source = null;
        document = null;
        currentRasterBytes = 0;
        return ValueTask.CompletedTask;
    }

    private async Task TurnPageAsync(int direction)
    {
        if (document is null)
        {
            return;
        }
        int nextIndex = Math.Clamp(pageIndex + direction, 0, (int)document.PageCount - 1);
        if (nextIndex == pageIndex)
        {
            return;
        }
        pageIndex = nextIndex;
        try
        {
            await RenderPageAsync();
            scroller.ChangeView(null, 0, null, true);
        }
        catch (Exception exception)
        {
            pageStatus.Text = $"Page error: {exception.Message}";
        }
    }

    private async Task RenderPageAsync()
    {
        PdfDocument activeDocument = document ??
            throw new InvalidOperationException("Open a PDF guide first.");
        int generation = ++renderGeneration;
        int targetIndex = pageIndex;
        using PdfPage page = activeDocument.GetPage((uint)targetIndex);
        double aspectRatio = page.Size.Height / page.Size.Width;
        uint rasterWidth = (uint)Math.Clamp(
            Math.Ceiling(page.Size.Width * scale * 1.5), 1, 3000);
        uint rasterHeight = (uint)Math.Ceiling(rasterWidth * aspectRatio);
        while ((long)rasterWidth * rasterHeight * 4 > MaxRasterBytes)
        {
            if (rasterWidth == 1)
            {
                throw new InvalidDataException("PDF page aspect ratio exceeds the raster limit.");
            }
            rasterWidth = Math.Max(1, rasterWidth / 2);
            rasterHeight = (uint)Math.Ceiling(rasterWidth * aspectRatio);
        }
        PdfPageRenderOptions options = new() { DestinationWidth = rasterWidth };
        using InMemoryRandomAccessStream stream = new();
        await page.RenderToStreamAsync(stream, options);
        if (generation != renderGeneration)
        {
            return;
        }

        stream.Seek(0);
        BitmapImage bitmap = new();
        await bitmap.SetSourceAsync(stream);
        if (generation != renderGeneration)
        {
            return;
        }
        pageImage.Source = null;
        pageImage.Width = page.Size.Width * scale;
        pageImage.Height = page.Size.Height * scale;
        pageImage.Source = bitmap;
        AutomationProperties.SetName(
            pageImage, $"Rendered image of PDF page {targetIndex + 1} of {activeDocument.PageCount}");
        currentRasterBytes = (long)rasterWidth * rasterHeight * 4;
        peakRasterBytes = Math.Max(peakRasterBytes, currentRasterBytes);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        pageStatus.Text = Diagnostics;
    }

    private sealed record PdfLocation(
        int SchemaVersion, string ContentSha256, int PageIndex, double PageFraction);
}
