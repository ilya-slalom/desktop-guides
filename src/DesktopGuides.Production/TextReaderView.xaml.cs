using System.Globalization;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

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
    private readonly string? diagnosticsPath;
    private double cellWidth;
    // The font size is the base size times the test hook's multiplier
    // (Ruling 4) times the guide's text size.
    private double testScale = 1;
    private double textScale;
    // The line to keep at the top through layout changes; it follows the
    // reader's scrolling except while the view applies its own scroll.
    private int anchorLine;
    private int reportedTopLine;
    private bool restoring;
    private ScrollViewer? viewer;
    private DispatcherQueue? dispatcher;
    private RegisteredWaitHandle? remeasureWait;
    private EventWaitHandle? remeasureSignal;

    public TextReaderView(TextLineList lines, int maxColumns, double textScale, string? diagnosticsPath)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(maxColumns);
        this.lines = lines;
        this.maxColumns = maxColumns;
        this.textScale = textScale;
        this.diagnosticsPath = diagnosticsPath;
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    public event EventHandler? TopLineChanged;

    public int LineCount => lines.Count;

    // T14.1: the guide's text size. Rows re-measure at the new size, keep
    // Consolas, their measured width and no wrapping, and the top line stays.
    // Before the first layout the value is only kept.
    public double TextScale
    {
        get => textScale;
        set
        {
            if (value == textScale) return;
            textScale = value;
            Remeasure();
        }
    }

    private double CellFontSize => baseFontSize * testScale * textScale;

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
        CellProbe.SizeChanged -= OnProbeSizeChanged;
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
        CellProbe.SizeChanged += OnProbeSizeChanged;
        OpenRemeasureHook();
        if (anchorLine > 0)
        {
            ScrollToLine(anchorLine);
        }
    }

    private void Measure()
    {
        CellProbe.FontSize = CellFontSize;
        CellProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        cellWidth = CellProbe.DesiredSize.Width / ProbeColumns;
        rowWidth = Math.Ceiling(cellWidth * maxColumns);
        rowHeight = Math.Ceiling(CellProbe.DesiredSize.Height);
        WriteDiagnostics();
    }

    // Installed tests read the applied size and row measure (Ruling 7).
    private void WriteDiagnostics()
    {
        if (diagnosticsPath is null) return;
        string json = string.Create(CultureInfo.InvariantCulture,
            $"{{\"scale\":{textScale:R},\"rowWidth\":{rowWidth:R},\"cellWidth\":{cellWidth:R},\"rowHeight\":{rowHeight:R}}}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(diagnosticsPath)!);
            string temporary = diagnosticsPath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, diagnosticsPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Test diagnostics must not affect reading.
        }
    }

    // Issue #29: the text size changed, so rows measured at the old size
    // would clip. Re-measure, resize realized rows, and keep the top line.
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
        text.FontSize = CellFontSize;
        // A minimum, so a fallback glyph wider than a Consolas cell isn't clipped.
        text.MinWidth = rowWidth;
        text.Height = rowHeight;
    }

    // XAML re-lays the probe out at the new system text size, so its height
    // changes only after the new size is in effect.
    private void OnProbeSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (Math.Ceiling(args.NewSize.Height) != rowHeight)
        {
            Remeasure();
        }
    }

    // Installed tests can't change the system text size, so they signal this
    // event to grow the probe to a larger test font size instead.
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
                // Only the probe changes, as a system text size change would
                // change it; the view must notice on its own.
                dispatcher?.TryEnqueue(() =>
                {
                    testScale = TestFontScale;
                    CellProbe.FontSize = CellFontSize;
                }), null, Timeout.Infinite, executeOnlyOnce: false);
        }
        catch (UnauthorizedAccessException)
        {
            // Optional installed-test synchronization must not affect normal reading.
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Re-applying when the top line hasn't moved is harmless:
        // ScrollIntoView keeps the sideways scroll.
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
