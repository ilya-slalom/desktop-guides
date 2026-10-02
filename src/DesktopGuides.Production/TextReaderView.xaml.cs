using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace DesktopGuides.Production;

// One row per guide line, realized only while visible. Rows take the widest
// line's width and one measured Consolas line's height, so the scroll ranges
// are right from the first frame and don't change as wide lines appear.
public sealed partial class TextReaderView : UserControl
{
    private const int ProbeColumns = 64;
    private const double TestFontScale = 1.5;
    private readonly TextLineList lines;
    private readonly int maxColumns;
    private double rowWidth;
    private double rowHeight;
    private double baseFontSize;
    private double fontScale = 1;
    // The line to keep at the top through layout changes; it follows the
    // reader's scrolling except while the view applies its own scroll.
    private int anchorLine;
    private int reportedTopLine;
    private bool restoring;
    private ScrollViewer? viewer;
    private DispatcherQueue? dispatcher;
    private UISettings? uiSettings;
    private RegisteredWaitHandle? remeasureWait;
    private EventWaitHandle? remeasureSignal;

    public TextReaderView(TextLineList lines, int maxColumns)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(maxColumns);
        this.lines = lines;
        this.maxColumns = maxColumns;
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    public event EventHandler? TopLineChanged;

    public int LineCount => lines.Count;

    // Shows the line at the top; before the first layout it is kept and
    // applied once rows exist.
    public void ScrollToLine(int line)
    {
        anchorLine = Math.Clamp(line, 0, Math.Max(lines.Count - 1, 0));
        if (Lines.ItemsSource is null || lines.Count == 0)
        {
            return;
        }
        restoring = true;
        Lines.ScrollIntoView(lines[anchorLine], ScrollIntoViewAlignment.Leading);
        dispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () => restoring = false);
    }

    public void PageBy(int delta)
    {
        int rows = viewer is null || rowHeight <= 0
            ? 1
            : Math.Max(1, (int)Math.Floor(viewer.ViewportHeight / rowHeight));
        // Pages from the real top, not the anchor, which can sit below it after Go to end.
        long target = (long)FirstVisibleIndex + (long)delta * rows;
        ScrollToLine((int)Math.Clamp(target, 0, Math.Max(lines.Count - 1, 0)));
    }

    public void ScrollToEdge(ReaderEdge edge) =>
        ScrollToLine(edge == ReaderEdge.Start ? 0 : lines.Count - 1);

    public int FirstVisibleIndex =>
        Lines.ItemsPanelRoot is ItemsStackPanel panel ? Math.Max(panel.FirstVisibleIndex, 0) : 0;

    public void ScrollByViewport(double fraction)
    {
        if (!double.IsFinite(fraction))
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "A scroll must be a finite fraction.");
        }
        if (FindScrollViewer(Lines) is ScrollViewer viewer)
        {
            viewer.ChangeView(null, viewer.VerticalOffset + fraction * viewer.ViewportHeight, null, true);
        }
    }

    public void Clear()
    {
        Loaded -= OnLoaded;
        SizeChanged -= OnSizeChanged;
        if (viewer is not null)
        {
            viewer.ViewChanged -= OnViewChanged;
            viewer = null;
        }
        if (uiSettings is not null)
        {
            uiSettings.TextScaleFactorChanged -= OnTextScaleFactorChanged;
            uiSettings = null;
        }
        remeasureWait?.Unregister(null);
        remeasureWait = null;
        remeasureSignal?.Dispose();
        remeasureSignal = null;
        Lines.ItemsSource = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        dispatcher = DispatcherQueue;
        baseFontSize = CellProbe.FontSize;
        Measure();
        Lines.ItemsSource = lines;
        viewer = FindScrollViewer(Lines);
        if (viewer is not null)
        {
            viewer.ViewChanged += OnViewChanged;
        }
        uiSettings = new UISettings();
        uiSettings.TextScaleFactorChanged += OnTextScaleFactorChanged;
        OpenRemeasureHook();
        if (anchorLine > 0)
        {
            ScrollToLine(anchorLine);
        }
    }

    private void Measure()
    {
        CellProbe.FontSize = baseFontSize * fontScale;
        CellProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        rowWidth = Math.Ceiling(CellProbe.DesiredSize.Width / ProbeColumns * maxColumns);
        rowHeight = Math.Ceiling(CellProbe.DesiredSize.Height);
    }

    // Issue #29: the system text size changed, so rows measured at the old
    // size would clip. Re-measure, resize realized rows, and keep the top line.
    private void Remeasure()
    {
        if (Lines.ItemsSource is null)
        {
            return;
        }
        double oldWidth = rowWidth;
        Measure();
        if (Lines.ItemsPanelRoot is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is ListViewItem { ContentTemplateRoot: TextBlock text })
                {
                    ApplyRowSize(text);
                }
            }
        }
        if (viewer is not null && oldWidth > 0)
        {
            viewer.ChangeView(viewer.HorizontalOffset * rowWidth / oldWidth, null, null, true);
        }
        ScrollToLine(anchorLine);
    }

    private void ApplyRowSize(TextBlock text)
    {
        text.FontSize = baseFontSize * fontScale;
        // A minimum, so a fallback glyph wider than a Consolas cell isn't clipped.
        text.MinWidth = rowWidth;
        text.Height = rowHeight;
    }

    private void OnTextScaleFactorChanged(UISettings sender, object args) =>
        dispatcher?.TryEnqueue(Remeasure);

    // Installed tests can't change the system text size, so they signal this
    // event to re-measure at a larger test font size instead.
    private void OpenRemeasureHook()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(
                $@"Local\DesktopGuides.Preview.TextRemeasure.{Environment.ProcessId}",
                out EventWaitHandle? signal))
            {
                return;
            }
            remeasureSignal = signal;
            remeasureWait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) =>
                dispatcher?.TryEnqueue(() =>
                {
                    fontScale = TestFontScale;
                    Remeasure();
                }), null, Timeout.Infinite, executeOnlyOnce: false);
        }
        catch (UnauthorizedAccessException)
        {
            // Optional installed-test synchronization must not affect normal reading.
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (Lines.ItemsSource is not null)
        {
            ScrollToLine(anchorLine);
        }
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (args.IsIntermediate)
        {
            return;
        }
        int top = FirstVisibleIndex;
        if (!restoring)
        {
            anchorLine = top;
        }
        if (top != reportedTopLine)
        {
            reportedTopLine = top;
            TopLineChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void LineContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not TextLineItem line)
        {
            return;
        }
        if (args.ItemContainer.ContentTemplateRoot is TextBlock text)
        {
            text.Text = line.Text;
            ApplyRowSize(text);
        }
        AutomationProperties.SetName(
            args.ItemContainer, string.IsNullOrWhiteSpace(line.Text) ? "Blank line" : line.Text);
        args.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, index)) is ScrollViewer found)
            {
                return found;
            }
        }
        return null;
    }
}
