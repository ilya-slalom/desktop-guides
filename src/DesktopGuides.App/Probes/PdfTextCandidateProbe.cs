using System.Security.Cryptography;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using RasterPdfDocument = Windows.Data.Pdf.PdfDocument;
using TextPdfDocument = UglyToad.PdfPig.PdfDocument;

namespace DesktopGuides.App.Probes;

// M0 evaluation surface only. The production PDF adapter is selected by T10.0.
public sealed class PdfTextCandidateProbe : IReaderProbe
{
    private readonly Grid view = new();
    private readonly ScrollViewer previewScroller = new();
    private readonly Image previewImage = new() { Stretch = Stretch.Uniform };
    private readonly TextBox pageText = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinWidth = 250
    };
    private readonly TextBlock pageStatus = new();
    private readonly TextBlock textStatus = new();
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
    private RasterPdfDocument? raster;
    private TextPdfDocument? textDocument;
    private FileStream? textStream;
    private string? sourcePath;
    private string? sha256;
    private int pageIndex;
    private double zoom = 1;
    private bool loading;

    public PdfTextCandidateProbe()
    {
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });

        StackPanel toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
        Button previous = new() { Content = "Previous page" };
        Button next = new() { Content = "Next page" };
        previous.Click += async (_, _) => await TurnAsync(-1);
        next.Click += async (_, _) => await TurnAsync(1);
        unlockButton.Click += UnlockClicked;
        toolbar.Children.Add(previous);
        toolbar.Children.Add(next);
        toolbar.Children.Add(passwordBox);
        toolbar.Children.Add(unlockButton);
        toolbar.Children.Add(pageStatus);
        toolbar.Children.Add(textStatus);
        view.Children.Add(toolbar);

        Grid columns = new() { ColumnSpacing = 12 };
        columns.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        columns.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        previewScroller.Content = previewImage;
        previewScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        previewScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        AutomationProperties.SetName(previewImage, "PDF page preview");
        Grid.SetColumn(pageText, 1);
        columns.Children.Add(previewScroller);
        columns.Children.Add(pageText);
        Grid.SetRow(columns, 1);
        view.Children.Add(columns);
        AutomationProperties.SetAutomationId(pageText, "PdfDocumentText");
        AutomationProperties.SetName(pageText, "PDF document text");
        AutomationProperties.SetAutomationId(pageStatus, "PdfPageStatus");
        AutomationProperties.SetAutomationId(textStatus, "PdfTextStatus");
        AutomationProperties.SetName(passwordBox, "PDF password");
    }

    public FrameworkElement View => view;

    public string Diagnostics => raster is null
        ? "Password required; enter the PDF password and choose Unlock PDF"
        : $"text candidate page {pageIndex + 1}/{raster.PageCount}; " +
          $"selectable characters {pageText.Text.Length}; zoom {zoom:0.00}";

    public async Task OpenAsync(string path, int? codePage)
    {
        sourcePath = path;
        await using (FileStream source = File.OpenRead(path))
        {
            sha256 = Convert.ToHexString(await SHA256.HashDataAsync(source))
                .ToLowerInvariant();
        }
        try
        {
            await LoadAsync(null);
        }
        catch (PdfDocumentEncryptedException)
        {
            passwordBox.Visibility = Visibility.Visible;
            unlockButton.Visibility = Visibility.Visible;
            pageStatus.Text = "Password required";
            return;
        }
        await ShowPageAsync(0);
    }

    private async Task LoadAsync(string? password)
    {
        string path = sourcePath ??
            throw new InvalidOperationException("Choose a PDF before unlocking it.");
        FileStream candidateStream = File.OpenRead(path);
        TextPdfDocument candidateText;
        try
        {
            candidateText = password is null
                ? TextPdfDocument.Open(candidateStream)
                : TextPdfDocument.Open(
                    candidateStream, new ParsingOptions { Password = password });
        }
        catch
        {
            candidateStream.Dispose();
            throw;
        }
        RasterPdfDocument candidateRaster;
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            candidateRaster = password is null
                ? await RasterPdfDocument.LoadFromFileAsync(file)
                : await RasterPdfDocument.LoadFromFileAsync(file, password);
        }
        catch
        {
            candidateText.Dispose();
            candidateStream.Dispose();
            throw;
        }
        if (candidateRaster.PageCount == 0 ||
            candidateText.NumberOfPages != candidateRaster.PageCount)
        {
            candidateText.Dispose();
            candidateStream.Dispose();
            throw new InvalidDataException("The PDF text and preview page counts disagree.");
        }
        textDocument = candidateText;
        textStream = candidateStream;
        raster = candidateRaster;
    }

    public Task<string> CaptureAsync()
    {
        if (raster is null || sha256 is null)
        {
            throw new InvalidOperationException("Open a PDF first.");
        }
        ReaderLocation location = new(
            GuideFormat.Pdf, ReaderLocationCodec.CurrentVersion, sha256,
            new PdfPosition(pageIndex, PageFraction()),
            (pageIndex + PageFraction()) / raster.PageCount);
        return Task.FromResult(ReaderLocationCodec.Serialize(location));
    }

    public async Task RestoreAsync(string locationJson)
    {
        if (raster is null || sha256 is null)
        {
            throw new InvalidOperationException("Open a PDF first.");
        }
        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            locationJson, GuideFormat.Pdf, sha256);
        if (decoded.Location?.Payload is not PdfPosition position)
        {
            throw new InvalidDataException("The PDF position is invalid.");
        }
        await ShowPageAsync(Math.Clamp(position.PageIndex, 0, (int)raster.PageCount - 1));
        previewScroller.ChangeView(
            null, previewScroller.ScrollableHeight * position.PageFraction, null);
    }

    public async Task ChangeScaleAsync(double factor)
    {
        zoom = Math.Clamp(zoom * factor, 0.5, 3);
        if (raster is not null)
        {
            double fraction = PageFraction();
            await ShowPageAsync(pageIndex);
            previewScroller.ChangeView(null, previewScroller.ScrollableHeight * fraction, null);
        }
    }

    public ValueTask DisposeAsync()
    {
        previewImage.Source = null;
        textDocument?.Dispose();
        textDocument = null;
        textStream?.Dispose();
        textStream = null;
        raster = null;
        return ValueTask.CompletedTask;
    }

    private async void UnlockClicked(object sender, RoutedEventArgs args)
    {
        if (!unlockButton.IsEnabled)
        {
            return;
        }
        string attempt = passwordBox.Password;
        passwordBox.Password = "";
        unlockButton.IsEnabled = false;
        try
        {
            await LoadAsync(attempt);
            passwordBox.Visibility = Visibility.Collapsed;
            unlockButton.Visibility = Visibility.Collapsed;
            await ShowPageAsync(0);
        }
        catch (PdfDocumentEncryptedException)
        {
            pageStatus.Text = "Incorrect password; try again";
        }
        catch (Exception)
        {
            pageStatus.Text = "Could not open this PDF; try again";
        }
        finally
        {
            attempt = "";
            unlockButton.IsEnabled = true;
        }
    }

    private async Task TurnAsync(int delta)
    {
        if (raster is null || loading)
        {
            return;
        }
        await ShowPageAsync(Math.Clamp(pageIndex + delta, 0, (int)raster.PageCount - 1));
    }

    private async Task ShowPageAsync(int index)
    {
        if (raster is null || textDocument is null || loading)
        {
            return;
        }
        loading = true;
        try
        {
            pageIndex = index;
            pageText.Text = await Task.Run(() =>
                ContentOrderTextExtractor.GetText(textDocument.GetPage(index + 1)));
            textStatus.Text = pageText.Text.Length > 0
                ? "Selectable document text"
                : "Image-only page; OCR is unavailable";
            AutomationProperties.SetName(
                pageText, $"PDF document text, page {index + 1} of {raster.PageCount}");
            using PdfPage page = raster.GetPage((uint)index);
            uint width = (uint)Math.Clamp(
                Math.Ceiling(Math.Max(400, previewScroller.ActualWidth) * zoom), 1, 2000);
            using InMemoryRandomAccessStream stream = new();
            await page.RenderToStreamAsync(stream,
                new PdfPageRenderOptions { DestinationWidth = width });
            stream.Seek(0);
            BitmapImage image = new();
            await image.SetSourceAsync(stream);
            previewImage.Source = image;
            previewImage.Width = width;
            previewScroller.ChangeView(null, 0, null);
            pageStatus.Text = $"Page {index + 1} of {raster.PageCount}";
        }
        finally
        {
            loading = false;
        }
    }

    private double PageFraction() =>
        previewScroller.ScrollableHeight <= 0
            ? 0
            : Math.Clamp(previewScroller.VerticalOffset / previewScroller.ScrollableHeight, 0, 1);
}
