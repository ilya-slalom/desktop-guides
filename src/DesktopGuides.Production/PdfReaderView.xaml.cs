using DesktopGuides.Infrastructure.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production;

// The preview's vertical scroll state, in effective pixels.
public readonly record struct PdfPreviewLayout(double Offset, double ImageHeight, double ViewportHeight);

// One PDF page: its preview beside its text, or the text under the preview
// below 720 effective pixels. The session decides what to show and where on
// the page to scroll; a new page never moves keyboard focus.
public sealed partial class PdfReaderView : UserControl
{
    private static double NarrowWidth =>
        (double)Application.Current.Resources["DesktopGuidesPdfSplitBreakpoint"];
    private bool? narrow;

    public PdfReaderView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        PreviewScroller.SizeChanged += (_, _) =>
        {
            PreviewSizeChanged?.Invoke(this, EventArgs.Empty);
            PreviewLayoutChanged?.Invoke(this, EventArgs.Empty);
        };
        Preview.SizeChanged += (_, _) => PreviewLayoutChanged?.Invoke(this, EventArgs.Empty);
        PreviewScroller.ViewChanged += (_, args) =>
        {
            if (!args.IsIntermediate) PreviewScrolled?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? PreviewSizeChanged;
    // The page image or its viewport changed height.
    public event EventHandler? PreviewLayoutChanged;
    // A scroll of the preview, the user's or the app's, came to rest.
    public event EventHandler? PreviewScrolled;

    // The width a preview fills, in effective pixels; 0 before layout.
    public double PreviewWidth => PreviewScroller.ActualWidth;

    // The scroller holds only the page image, so its extent is the image
    // height. The image's ActualHeight lags: the scroller's clamp after a
    // shrink comes to rest before the image's SizeChanged.
    public PdfPreviewLayout PreviewLayout =>
        new(PreviewScroller.VerticalOffset, PreviewScroller.ExtentHeight, PreviewScroller.ViewportHeight);

    public void ScrollTo(double offset) =>
        PreviewScroller.ChangeView(null, offset, null, disableAnimation: true);

    public void ShowPage(int index, int count, string zoomLabel, ImageSource? image, PdfPageText? text)
    {
        string page = $"Page {index + 1} of {count}";
        ShowStatus($"{page} \u00B7 {zoomLabel}");
        Preview.Source = image;
        AutomationProperties.SetName(Preview, page + " preview");
        SetStatus(PreviewStatus, image is null ? "This page's preview couldn't be shown." : null);
        AutomationProperties.SetName(DocumentText, $"Page text, page {index + 1} of {count}");
        DocumentText.Text = text is { HasLetters: true } ? text.Text : string.Empty;
        SetStatus(TextStatus, text switch
        {
            null => "This page's text couldn't be read.",
            { HasLetters: false } => "Image-only page; OCR is unavailable.",
            { Truncated: true } => "Page text is shortened; it's too long to show in full.",
            _ => null,
        });
    }

    // Fit (NaN) fills the viewport with no horizontal scroll; a percent sets
    // the image's width and lets the page scroll sideways when it's wider.
    public void SetPageWidth(double width)
    {
        Preview.Width = width;
        PreviewScroller.HorizontalScrollBarVisibility =
            double.IsNaN(width) ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    public bool FocusPreview() => PreviewScroller.Focus(FocusState.Programmatic);

    public void Clear()
    {
        Preview.Source = null;
        DocumentText.Text = string.Empty;
        SetPageWidth(double.NaN);
    }

    // A polite live region announces page and zoom changes without moving
    // focus. It's raised only when the text changes, so a re-render of the
    // same page at the same zoom stays quiet.
    private void ShowStatus(string status)
    {
        if (PageStatus.Text == status) return;
        PageStatus.Text = status;
        FrameworkElementAutomationPeer.FromElement(PageStatus)
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static void SetStatus(TextBlock status, string? message)
    {
        status.Text = message ?? string.Empty;
        status.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool isNarrow = args.NewSize.Width < NarrowWidth;
        if (narrow == isNarrow) return;
        narrow = isNarrow;
        Grid.SetRow(TextPane, isNarrow ? 2 : 1);
        Grid.SetColumn(TextPane, isNarrow ? 0 : 1);
        TextPane.Margin = (Thickness)Application.Current.Resources[
            isNarrow ? "DesktopGuidesReaderGapAbove" : "DesktopGuidesPdfTextPaneMargin"];
        TextColumn.Width = isNarrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        TextRow.Height = isNarrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }
}
