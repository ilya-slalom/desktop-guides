using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.WinUI;
using DesktopGuides.Core.Reading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace DesktopGuides.Production;

// T19.1: one row of the Bookmarks tab. Only the editor's visibility changes;
// a rename reloads the list from the library.
public sealed partial class BookmarkRow(BookmarkItem item) : INotifyPropertyChanged
{
    private bool editing;

    public BookmarkItem Item { get; } = item;
    public string Label => BookmarkList.DisplayLabel(Item);
    public string AccessibleName => BookmarkList.AccessibleName(Item);
    public Visibility LabelVisibility => editing ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EditorVisibility => editing ? Visibility.Visible : Visibility.Collapsed;

    public bool IsEditing
    {
        get => editing;
        set
        {
            if (editing == value) return;
            editing = value;
            PropertyChanged?.Invoke(this, new(nameof(LabelVisibility)));
            PropertyChanged?.Invoke(this, new(nameof(EditorVisibility)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

// T19.1: the Bookmarks tab. It lists and reports; the shell stores and moves.
public sealed partial class BookmarksPane : UserControl
{
    private readonly ObservableCollection<BookmarkRow> rows = [];

    public BookmarksPane()
    {
        InitializeComponent();
        List.ItemsSource = rows;
    }

    public event EventHandler? AddRequested;
    public event EventHandler<BookmarkItem>? ItemInvoked;
    public event EventHandler<(BookmarkItem Item, string Label)>? RenameRequested;
    public event EventHandler<BookmarkItem>? DeleteRequested;

    // The bookmark to select, or the first.
    public void ShowItems(IReadOnlyList<BookmarkItem> items, Guid? select)
    {
        ArgumentNullException.ThrowIfNull(items);
        rows.Clear();
        foreach (BookmarkItem item in items) rows.Add(new BookmarkRow(item));
        Empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        List.SelectedItem = rows.FirstOrDefault(row => row.Item.Bookmark.Id == select) ?? rows.FirstOrDefault();
    }

    public void Clear()
    {
        rows.Clear();
        Empty.Visibility = Visibility.Collapsed;
        List.Visibility = Visibility.Collapsed;
    }

    public void FocusContent() => _ = FocusContentAsync();

    // After layout, so the selected item's container exists. Completes once
    // focus is set, so a status announced after it finds the list ready.
    public Task FocusContentAsync()
    {
        TaskCompletionSource done = new();
        bool queued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!(List.SelectedItem is BookmarkRow row && List.ContainerFromItem(row) is ListViewItem item &&
                item.Focus(FocusState.Programmatic)))
            {
                AddButton.Focus(FocusState.Programmatic);
            }
            done.TrySetResult();
        });
        if (!queued) done.TrySetResult();
        return done.Task;
    }

    private void AddClicked(object sender, RoutedEventArgs args) => AddRequested?.Invoke(this, EventArgs.Empty);

    // Enter, Space or a click. A damaged bookmark has no place to go to.
    private void ItemClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is BookmarkRow { IsEditing: false } row && !row.Item.IsDamaged)
        {
            ItemInvoked?.Invoke(this, row.Item);
        }
    }

    private void ListKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (List.SelectedItem is not BookmarkRow row || row.IsEditing) return;
        if (args.Key == VirtualKey.F2 && !row.Item.IsDamaged)
        {
            args.Handled = true;
            BeginRename(row);
        }
        else if (args.Key == VirtualKey.Delete)
        {
            args.Handled = true;
            DeleteRequested?.Invoke(this, row.Item);
        }
    }

    private void ListContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is BookmarkRow row) AutomationProperties.SetName(args.ItemContainer, row.AccessibleName);
    }

    private void BeginRename(BookmarkRow row)
    {
        row.IsEditing = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (List.ContainerFromItem(row) is ListViewItem item && item.FindDescendant<TextBox>() is TextBox editor)
            {
                editor.Text = row.Item.Bookmark.Label;
                editor.SelectAll();
                editor.Focus(FocusState.Programmatic);
            }
        });
    }

    // Escape is handled here, so the pane doesn't take it as "close".
    private void EditorKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (sender is not TextBox { DataContext: BookmarkRow row } editor) return;
        if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            EndRename(row, editor.Text);
        }
        else if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            EndRename(row, null);
        }
    }

    private void EditorLostFocus(object sender, RoutedEventArgs args)
    {
        if (sender is TextBox { DataContext: BookmarkRow { IsEditing: true } row }) EndRename(row, null);
    }

    // An empty or unchanged label keeps the old one.
    private void EndRename(BookmarkRow row, string? label)
    {
        row.IsEditing = false;
        if (List.ContainerFromItem(row) is ListViewItem item) item.Focus(FocusState.Programmatic);
        if (label?.Trim() is { Length: > 0 } trimmed && trimmed != row.Item.Bookmark.Label)
        {
            RenameRequested?.Invoke(this, (row.Item, trimmed));
        }
    }
}
