using CommunityToolkit.WinUI;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace DesktopGuides.Production;

// T19.2: a TXT guide's Reflow view, one list item per planned block. Its
// place is the first visible block's start offset (the user's choice:
// switching keeps the place to the block).
public sealed partial class TextReflowView : UserControl
{
    private readonly ReflowPlan plan;
    private double textScale;
    private double baseFontSize = 14;
    private int anchorBlock;
    private int reportedTop = -1;
    private bool restoring;
    private ScrollViewer? viewer;
    private DispatcherQueue? dispatcher;
    private IReadOnlyDictionary<int, (int Start, int Length)> highlights = new Dictionary<int, (int, int)>();

    public TextReflowView(ReflowPlan plan, double textScale)
    {
        ArgumentNullException.ThrowIfNull(plan);
        this.plan = plan;
        this.textScale = textScale;
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    public event EventHandler? TopBlockChanged;

    public int FirstVisibleIndex =>
        Blocks.ItemsPanelRoot is ItemsStackPanel panel ? Math.Max(panel.FirstVisibleIndex, 0) : anchorBlock;

    public int FirstVisibleOffset => plan.Blocks[Math.Clamp(FirstVisibleIndex, 0, plan.Blocks.Count - 1)].Start;

    public double TextScale
    {
        get => textScale;
        set
        {
            textScale = value;
            RefreshRealized();
            ScrollToBlock(anchorBlock);
        }
    }

    // The block holding the offset goes to the top; before the first layout
    // it is kept and applied once items exist.
    public void ScrollToOffset(int offset) => ScrollToBlock(plan.BlockAt(offset));

    public void ScrollToEdge(ReaderEdge edge) => ScrollToBlock(edge == ReaderEdge.Start ? 0 : plan.Blocks.Count - 1);

    // A page is a viewport: blocks have no common height.
    public void PageBy(int delta) => ScrollByViewport(delta);

    public void ScrollByViewport(double fraction)
    {
        if (!double.IsFinite(fraction))
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "A scroll must be a finite fraction.");
        }
        viewer?.ChangeView(null, viewer.VerticalOffset + fraction * viewer.ViewportHeight, null, true);
    }

    public void ShowMatch(IReadOnlyList<(int Block, int Start, int Length)> ranges)
    {
        highlights = ranges.ToDictionary(range => range.Block, range => (range.Start, range.Length));
        RefreshRealized();
    }

    public void ClearMatch()
    {
        highlights = new Dictionary<int, (int, int)>();
        RefreshRealized();
    }

    public bool FocusBlocks() => Blocks.Focus(FocusState.Programmatic);

    public void Clear()
    {
        Loaded -= OnLoaded;
        SizeChanged -= OnSizeChanged;
        if (viewer is not null) viewer.ViewChanged -= OnViewChanged;
        viewer = null;
        Blocks.ItemsSource = null;
    }

    private void ScrollToBlock(int index)
    {
        anchorBlock = Math.Clamp(index, 0, plan.Blocks.Count - 1);
        if (Blocks.ItemsSource is null) return;
        restoring = true;
        Blocks.ScrollIntoView(plan.Blocks[anchorBlock], ScrollIntoViewAlignment.Leading);
        dispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () => restoring = false);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        dispatcher = DispatcherQueue;
        if (Application.Current.Resources.TryGetValue("ControlContentThemeFontSize", out object size) && size is double font)
        {
            baseFontSize = font;
        }
        Blocks.ItemsSource = plan.Blocks;
        viewer = Blocks.FindDescendant<ScrollViewer>();
        if (viewer is not null) viewer.ViewChanged += OnViewChanged;
        ScrollToBlock(anchorBlock);
    }

    // A wider or narrower window re-wraps prose; the top block stays.
    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ScrollToBlock(anchorBlock);

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (args.IsIntermediate) return;
        int top = FirstVisibleIndex;
        if (!restoring) anchorBlock = top;
        if (top != reportedTop)
        {
            reportedTop = top;
            TopBlockChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshRealized()
    {
        for (int index = 0; index < plan.Blocks.Count; index++)
        {
            if (Blocks.ContainerFromIndex(index) is ListViewItem item) Apply(item, index);
        }
    }

    private void BlockContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer is not ListViewItem item) return;
        Apply(item, args.ItemIndex);
    }

    private void Apply(ListViewItem item, int index)
    {
        if (item.ContentTemplateRoot is not Grid { Children: [TextBlock prose, ScrollViewer { Content: TextBlock preserved } scroll] })
        {
            return;
        }
        ReflowBlock block = plan.Blocks[index];
        bool isProse = block.Kind == ReflowKind.Prose;
        TextBlock text = isProse ? prose : preserved;
        prose.Visibility = isProse ? Visibility.Visible : Visibility.Collapsed;
        scroll.Visibility = isProse ? Visibility.Collapsed : Visibility.Visible;
        text.Text = block.Text;
        text.FontSize = baseFontSize * textScale;
        text.TextHighlighters.Clear();
        if (highlights.TryGetValue(index, out (int Start, int Length) range) && range.Length > 0)
        {
            TextHighlighter highlighter = new();
            highlighter.Ranges.Add(new TextRange { StartIndex = range.Start, Length = range.Length });
            text.TextHighlighters.Add(highlighter);
        }
        AutomationProperties.SetName(item, block.Text.Length == 0 ? "Blank block" : block.Text);
    }
}
