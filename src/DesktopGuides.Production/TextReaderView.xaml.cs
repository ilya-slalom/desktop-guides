using DesktopGuides.Core.Text;
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
    private readonly TextLineList lines;
    private readonly int maxColumns;
    private double rowWidth;
    private double rowHeight;

    public TextReaderView(TextLineList lines, int maxColumns)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(maxColumns);
        this.lines = lines;
        this.maxColumns = maxColumns;
        InitializeComponent();
        Loaded += OnLoaded;
    }

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
        Lines.ItemsSource = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        CellProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        rowWidth = Math.Ceiling(CellProbe.DesiredSize.Width / ProbeColumns * maxColumns);
        rowHeight = Math.Ceiling(CellProbe.DesiredSize.Height);
        Lines.ItemsSource = lines;
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
            // A minimum, so a fallback glyph wider than a Consolas cell isn't clipped.
            text.MinWidth = rowWidth;
            text.Height = rowHeight;
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
