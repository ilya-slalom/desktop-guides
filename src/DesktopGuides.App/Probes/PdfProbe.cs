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
    private readonly PasswordBox passwordBox = new()
    {
        PlaceholderText = "PDF password",
        Visibility = Visibility.Collapsed,
        Width = 180
    };
    private readonly Button unlockButton = new()
    {
        Content = "Unlock PDF",
        Visibility = Visibility.Collapsed
    };
    private readonly Dictionary<CacheKey, LinkedListNode<CachedPage>> cache = [];
    private readonly LinkedList<CachedPage> recent = [];
    private PdfDocument? document;
    private StorageFile? storageFile;
    private CancellationTokenSource? resizeDelay;
    private string? contentSha256;
    private int pageIndex;
    private int renderGeneration;
    private double scale = 1;
    private long cachedRasterBytes;
    private long peakRasterBytes;
    private long openMilliseconds;
    private long lastRenderMilliseconds;
    private string restoreKind = "none";
    private bool needsPassword;

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
        unlockButton.Click += UnlockClicked;
        AutomationProperties.SetName(passwordBox, "PDF password");
        AutomationProperties.SetAutomationId(pageStatus, "PdfPageStatus");
        toolbar.Children.Add(previous);
        toolbar.Children.Add(next);
        toolbar.Children.Add(passwordBox);
        toolbar.Children.Add(unlockButton);
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
        AutomationProperties.SetAutomationId(scroller, "PdfScroller");
        AutomationProperties.SetName(pageImage, "PDF page image");
        scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scroller.SizeChanged += ScrollerSizeChanged;
        scroller.ViewChanged += (_, _) =>
        {
            if (document is not null)
            {
                UpdateStatus();
            }
        };
        Grid.SetRow(scroller, 3);
        view.Children.Add(scroller);
    }

    public FrameworkElement View => view;

    public string Diagnostics => needsPassword
        ? "Password required. Enter the PDF password and choose Unlock PDF."
        : $"page {pageIndex + 1}/{document?.PageCount ?? 0}; zoom {scale:0.00} fit width; " +
          $"fraction {PageFraction():0.00}; width {pageImage.Width:0}/{scroller.ActualWidth:0}; " +
          $"cache {cachedRasterBytes / 1048576.0:0.0} MiB in {cache.Count} pages " +
          $"(peak {peakRasterBytes / 1048576.0:0.0}); " +
          $"open {openMilliseconds} ms; render {lastRenderMilliseconds} ms; restore {restoreKind}.";

    public async Task OpenAsync(string path, int? codePage)
    {
        Stopwatch timer = Stopwatch.StartNew();
        contentSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        storageFile = await StorageFile.GetFileFromPathAsync(path);
        try
        {
            document = await PdfDocument.LoadFromFileAsync(storageFile);
        }
        catch (Exception)
        {
            // Windows.Data.Pdf reports password failures differently across builds.
            // Keep the probe open to let the user supply a password.
            needsPassword = true;
            passwordBox.Visibility = Visibility.Visible;
            unlockButton.Visibility = Visibility.Visible;
            UpdateStatus();
            return;
        }
        if (document.PageCount == 0)
        {
            throw new InvalidDataException("PDF has no pages.");
        }
        view.UpdateLayout();
        await RenderPageAsync();
        timer.Stop();
        openMilliseconds = timer.ElapsedMilliseconds;
        UpdateStatus();
    }

    public Task<string> CaptureAsync()
    {
        if (document is null || contentSha256 is null)
        {
            throw new InvalidOperationException("Unlock the PDF guide first.");
        }
        return Task.FromResult(JsonSerializer.Serialize(
            new PdfLocation(1, contentSha256, pageIndex, PageFraction())));
    }

    public async Task RestoreAsync(string locationJson)
    {
        PdfDocument activeDocument = document ??
            throw new InvalidOperationException("Unlock the PDF guide first.");
        PdfLocation location = JsonSerializer.Deserialize<PdfLocation>(locationJson) ??
            throw new InvalidDataException("Invalid PDF location.");
        if (location.SchemaVersion != 1 || !double.IsFinite(location.PageFraction))
        {
            throw new InvalidDataException("Invalid PDF location.");
        }

        pageIndex = Math.Clamp(location.PageIndex, 0, (int)activeDocument.PageCount - 1);
        await RenderPageAsync();
        await Task.Yield();
        ScrollToFraction(location.PageFraction);
        restoreKind = string.Equals(
            location.ContentSha256, contentSha256, StringComparison.OrdinalIgnoreCase)
            ? "exact page"
            : "approximate page";
        UpdateStatus();
    }

    public async Task ChangeScaleAsync(double factor)
    {
        double fraction = PageFraction();
        scale = Math.Clamp(scale * factor, 0.5, 3);
        if (document is not null)
        {
            await RenderPageAsync();
            ScrollToFraction(fraction);
        }
    }

    public ValueTask DisposeAsync()
    {
        renderGeneration++;
        resizeDelay?.Cancel();
        resizeDelay?.Dispose();
        pageImage.Source = null;
        document = null;
        storageFile = null;
        cache.Clear();
        recent.Clear();
        cachedRasterBytes = 0;
        return ValueTask.CompletedTask;
    }

    private async void UnlockClicked(object sender, RoutedEventArgs args)
    {
        if (storageFile is null)
        {
            return;
        }
        try
        {
            document = await PdfDocument.LoadFromFileAsync(storageFile, passwordBox.Password);
            if (document.PageCount == 0)
            {
                throw new InvalidDataException("PDF has no pages.");
            }
            needsPassword = false;
            passwordBox.Password = "";
            passwordBox.Visibility = Visibility.Collapsed;
            unlockButton.Visibility = Visibility.Collapsed;
            await RenderPageAsync();
            UpdateStatus();
        }
        catch (Exception)
        {
            pageStatus.Text = "Could not unlock PDF. Check the password or file, then try again.";
        }
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

    private async void ScrollerSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (document is null || Math.Abs(args.NewSize.Width - args.PreviousSize.Width) < 2)
        {
            return;
        }
        resizeDelay?.Cancel();
        resizeDelay?.Dispose();
        CancellationTokenSource delay = new();
        resizeDelay = delay;
        try
        {
            await Task.Delay(150, delay.Token);
            double fraction = PageFraction();
            await RenderPageAsync();
            if (!delay.IsCancellationRequested)
            {
                ScrollToFraction(fraction);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            pageStatus.Text = $"Resize error: {exception.Message}";
        }
    }

    private async Task RenderPageAsync()
    {
        PdfDocument activeDocument = document ??
            throw new InvalidOperationException("Open a PDF guide first.");
        int generation = ++renderGeneration;
        int targetIndex = pageIndex;
        using PdfPage page = activeDocument.GetPage((uint)targetIndex);
        double displayWidth = Math.Max(1, scroller.ActualWidth > 1
            ? scroller.ActualWidth * scale
            : page.Size.Width * scale);
        uint rasterWidth = RasterWidth(displayWidth, page.Size.Height / page.Size.Width);
        CacheKey key = new(targetIndex, rasterWidth);
        CachedPage? selected = FindCached(key);
        if (selected is null)
        {
            Stopwatch timer = Stopwatch.StartNew();
            selected = await RenderBitmapAsync(page, key);
            timer.Stop();
            if (generation != renderGeneration)
            {
                return;
            }
            lastRenderMilliseconds = timer.ElapsedMilliseconds;
            AddCached(selected);
        }
        else
        {
            lastRenderMilliseconds = 0;
        }
        if (generation != renderGeneration)
        {
            return;
        }
        pageImage.Source = null;
        pageImage.Width = displayWidth;
        pageImage.Height = displayWidth * page.Size.Height / page.Size.Width;
        pageImage.Source = selected.Bitmap;
        AutomationProperties.SetName(
            pageImage, $"Rendered image of PDF page {targetIndex + 1} of {activeDocument.PageCount}");
        UpdateStatus();

        int neighbor = targetIndex + 1 < activeDocument.PageCount
            ? targetIndex + 1 : targetIndex - 1;
        if (neighbor >= 0)
        {
            _ = PrefetchAsync(activeDocument, neighbor, displayWidth, generation);
        }
    }

    private async Task PrefetchAsync(
        PdfDocument activeDocument, int index, double displayWidth, int generation)
    {
        try
        {
            using PdfPage page = activeDocument.GetPage((uint)index);
            CacheKey key = new(index, RasterWidth(displayWidth, page.Size.Height / page.Size.Width));
            if (FindCached(key) is not null)
            {
                return;
            }
            CachedPage rendered = await RenderBitmapAsync(page, key);
            if (generation == renderGeneration && ReferenceEquals(document, activeDocument))
            {
                AddCached(rendered);
                UpdateStatus();
            }
        }
        catch (Exception)
        {
            // A failed speculative render must not interrupt the displayed page.
        }
    }

    private static async Task<CachedPage> RenderBitmapAsync(PdfPage page, CacheKey key)
    {
        PdfPageRenderOptions options = new() { DestinationWidth = key.RasterWidth };
        using InMemoryRandomAccessStream stream = new();
        await page.RenderToStreamAsync(stream, options);
        stream.Seek(0);
        BitmapImage bitmap = new();
        await bitmap.SetSourceAsync(stream);
        long bytes = (long)key.RasterWidth *
            (long)Math.Ceiling(key.RasterWidth * page.Size.Height / page.Size.Width) * 4;
        return new CachedPage(key, bitmap, bytes);
    }

    private static uint RasterWidth(double displayWidth, double aspectRatio)
    {
        uint width = (uint)Math.Clamp(Math.Ceiling(displayWidth * 1.5), 1, 3000);
        while ((long)width * Math.Ceiling(width * aspectRatio) * 4 > MaxRasterBytes)
        {
            if (width == 1)
            {
                throw new InvalidDataException("PDF page aspect ratio exceeds the raster limit.");
            }
            width = Math.Max(1, width / 2);
        }
        return width;
    }

    private CachedPage? FindCached(CacheKey key)
    {
        if (!cache.TryGetValue(key, out LinkedListNode<CachedPage>? node))
        {
            return null;
        }
        recent.Remove(node);
        recent.AddFirst(node);
        return node.Value;
    }

    private void AddCached(CachedPage page)
    {
        if (cache.ContainsKey(page.Key))
        {
            return;
        }
        LinkedListNode<CachedPage> node = recent.AddFirst(page);
        cache.Add(page.Key, node);
        cachedRasterBytes += page.Bytes;
        while (cachedRasterBytes > MaxRasterBytes && recent.Last is { } last)
        {
            recent.RemoveLast();
            cache.Remove(last.Value.Key);
            cachedRasterBytes -= last.Value.Bytes;
        }
        peakRasterBytes = Math.Max(peakRasterBytes, cachedRasterBytes);
    }

    private double PageFraction() => Math.Clamp(
        scroller.VerticalOffset / Math.Max(1, pageImage.ActualHeight > 0
            ? pageImage.ActualHeight : pageImage.Height), 0, 1);

    private void ScrollToFraction(double fraction)
    {
        scroller.UpdateLayout();
        scroller.ChangeView(
            null,
            Math.Min(
                Math.Clamp(fraction, 0, 1) * Math.Max(1, pageImage.ActualHeight),
                scroller.ScrollableHeight),
            null,
            true);
    }

    private void UpdateStatus() => pageStatus.Text = Diagnostics;

    private readonly record struct CacheKey(int PageIndex, uint RasterWidth);
    private sealed record CachedPage(CacheKey Key, BitmapImage Bitmap, long Bytes);
    private sealed record PdfLocation(
        int SchemaVersion, string ContentSha256, int PageIndex, double PageFraction);
}
