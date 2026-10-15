using DesktopGuides.Core.Reading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace DesktopGuides.Production;

// T18.2: the reader's Contents pane. It shows an outline and reports what
// the user picked; the shell does the moving.
public sealed partial class ContentsPane : UserControl
{
    private readonly Dictionary<TreeViewNode, OutlineEntry> entries = [];

    public ContentsPane()
    {
        InitializeComponent();
        KeyDown += PaneKeyDown;
    }

    public event EventHandler<OutlineEntry>? EntryInvoked;
    public event EventHandler? SearchRequested;
    public event EventHandler? CloseRequested;

    public void ShowBuilding()
    {
        Clear();
        Building.Visibility = Visibility.Visible;
    }

    public void ShowOutline(Outline outline, bool canSearch)
    {
        ArgumentNullException.ThrowIfNull(outline);
        Clear();
        if (outline.Entries.Count == 0)
        {
            Empty.Visibility = Visibility.Visible;
            SearchLink.Visibility = canSearch ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        foreach (OutlineEntry entry in outline.Entries) Tree.RootNodes.Add(NodeFor(entry));
        Tree.Visibility = Visibility.Visible;
    }

    // The entry that holds the place, or the first when the place comes
    // before every entry.
    public void Select(OutlineEntry? entry)
    {
        TreeViewNode? node = entry is null
            ? Tree.RootNodes.FirstOrDefault()
            : entries.FirstOrDefault(pair => ReferenceEquals(pair.Value, entry)).Key;
        if (node is not null) Tree.SelectedNode = node;
    }

    // After layout, so the selected item's container exists.
    public void FocusContent() =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (Tree.Visibility == Visibility.Visible &&
                Tree.SelectedNode is TreeViewNode node && Tree.ContainerFromNode(node) is Control item &&
                item.Focus(FocusState.Programmatic))
            {
                return;
            }
            if (SearchLink.Visibility == Visibility.Visible && SearchLink.Focus(FocusState.Programmatic)) return;
            CloseButton.Focus(FocusState.Programmatic);
        });

    public void Clear()
    {
        Tree.RootNodes.Clear();
        entries.Clear();
        Tree.Visibility = Visibility.Collapsed;
        Building.Visibility = Visibility.Collapsed;
        Empty.Visibility = Visibility.Collapsed;
        SearchLink.Visibility = Visibility.Collapsed;
    }

    private TreeViewNode NodeFor(OutlineEntry entry)
    {
        TreeViewNode node = new() { Content = entry.Title, IsExpanded = true };
        entries[node] = entry;
        foreach (OutlineEntry child in entry.Children) node.Children.Add(NodeFor(child));
        return node;
    }

    // Enter, Space or a click.
    private void TreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node && entries.TryGetValue(node, out OutlineEntry? entry))
        {
            args.Handled = true;
            EntryInvoked?.Invoke(this, entry);
        }
    }

    private void PaneKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape) return;
        args.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SearchClicked(object sender, RoutedEventArgs args) => SearchRequested?.Invoke(this, EventArgs.Empty);

    private void CloseClicked(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
