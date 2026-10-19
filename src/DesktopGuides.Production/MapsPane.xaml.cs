using DesktopGuides.Core.Maps;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace DesktopGuides.Production;

// T21.2 PR c: the reader's Maps pane. A row shows its map when the pointer
// enters it, when it gets keyboard focus, or when it's clicked; nothing
// clears it, so the last map stays when the pointer moves on. Enter and
// Open map open the map. The last few previews are kept for the pane's life.
public sealed partial class MapsPane : UserControl
{
    private const int KeptPreviews = 8;
    private const string PointMessage = "Point to a map to see it.";
    private const string PreparingMessage = "Preparing the map…";
    private const string FailedMessage = "This map can't be shown.";
    private readonly LinkedList<(Guid Id, ImageSource Source)> previews = new();
    private CancellationTokenSource? loading;
    private MapsMenuItem? shown;

    public MapsPane()
    {
        InitializeComponent();
    }

    public event EventHandler<Guid>? OpenRequested;

    public Func<MapsMenuItem, CancellationToken, Task<ImageSource>>? LoadPreview { get; set; }

    public void Show(IReadOnlyList<MapsMenuItem> items, Guid? openGuideId)
    {
        ArgumentNullException.ThrowIfNull(items);
        MapsList.ItemsSource = items;
        FirstFocus = items.Count == 0 ? -1 : MapsMenu.FirstFocus(items, openGuideId);
        if (shown is MapsMenuItem current && !items.Any(item => item.Id == current.Id))
        {
            Clear();
        }
    }

    // The row the keyboard starts on.
    public int FirstFocus { get; private set; } = -1;

    // Alt+M: the keyboard lands on a row, and its map shows.
    public void FocusFirstRow()
    {
        if (FirstFocus < 0) return;
        MapsList.SelectedIndex = FirstFocus;
        MapsList.ScrollIntoView(MapsList.SelectedItem);
        MapsList.UpdateLayout();
        (MapsList.ContainerFromIndex(FirstFocus) as ListViewItem)?.Focus(FocusState.Keyboard);
    }

    public void Forget()
    {
        loading?.Cancel();
        previews.Clear();
        Clear();
    }

    private void Clear()
    {
        shown = null;
        PreviewImage.Source = null;
        PreviewTitle.Text = string.Empty;
        PreviewFacts.Text = string.Empty;
        AutomationProperties.SetName(PreviewImage, string.Empty);
        ShowMessage(PointMessage);
        OpenMap.IsEnabled = false;
    }

    private void MapRowChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not MapsMenuItem item) return;
        AutomationProperties.SetName(args.ItemContainer, item.Title);
        if (args.ItemContainer.Tag is null)
        {
            args.ItemContainer.Tag = true;
            args.ItemContainer.PointerEntered += (container, _) => PreviewRow(container);
            args.ItemContainer.GotFocus += (container, _) =>
            {
                // A flyout's own first focus is programmatic; only the keyboard shows a map.
                if (container is ListViewItem { FocusState: FocusState.Keyboard }) PreviewRow(container);
            };
        }
    }

    private void PreviewRow(object container)
    {
        if (container is ListViewItem { Content: MapsMenuItem item }) _ = PreviewAsync(item);
    }

    private void MapClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is MapsMenuItem item) _ = PreviewAsync(item);
    }

    private void MapsListPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter) return;
        if (FocusManager.GetFocusedElement(XamlRoot) is ListViewItem { Content: MapsMenuItem item })
        {
            args.Handled = true;
            OpenRequested?.Invoke(this, item.Id);
        }
    }

    private void OpenMapClicked(object sender, RoutedEventArgs args)
    {
        if (shown is MapsMenuItem item) OpenRequested?.Invoke(this, item.Id);
    }

    private async Task PreviewAsync(MapsMenuItem item)
    {
        if (shown?.Id == item.Id && PreviewImage.Source is not null) return;
        loading?.Cancel();
        CancellationTokenSource cancel = new();
        loading = cancel;
        shown = item;
        PreviewTitle.Text = item.Title;
        PreviewFacts.Text = item.Facts;
        AutomationProperties.SetName(PreviewImage, item.Title);
        OpenMap.IsEnabled = true;
        ImageSource? source = Kept(item.Id);
        if (source is null)
        {
            PreviewImage.Source = null;
            ShowMessage(PreparingMessage);
            try
            {
                source = LoadPreview is null ? null : await LoadPreview(item, cancel.Token);
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
                source = null;
            }
            catch (Exception)
            {
                return;
            }
            // A newer row took over while this one loaded.
            if (!ReferenceEquals(loading, cancel)) return;
            if (source is null)
            {
                ShowMessage(FailedMessage);
                return;
            }
            Keep(item.Id, source);
        }
        PreviewImage.Source = source;
        ShowMessage(null);
        SizePreview();
    }

    private ImageSource? Kept(Guid id)
    {
        for (LinkedListNode<(Guid Id, ImageSource Source)>? node = previews.First; node is not null; node = node.Next)
        {
            if (node.Value.Id != id) continue;
            previews.Remove(node);
            previews.AddFirst(node);
            return node.Value.Source;
        }
        return null;
    }

    private void Keep(Guid id, ImageSource source)
    {
        previews.AddFirst((id, source));
        while (previews.Count > KeptPreviews) previews.RemoveLast();
    }

    private void ShowMessage(string? message)
    {
        PreviewMessage.Text = message ?? string.Empty;
        PreviewMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        PreviewImage.Visibility = message is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PreviewAreaSizeChanged(object sender, SizeChangedEventArgs args) => SizePreview();

    // Shrunk to fit the area, never enlarged.
    private void SizePreview()
    {
        if (shown is not MapsMenuItem item || PreviewArea.ActualWidth <= 0 || PreviewArea.ActualHeight <= 0) return;
        double scale = XamlRoot?.RasterizationScale ?? 1;
        (double width, double height) = MapPreview.ImageSize(
            PreviewArea.ActualWidth * scale, PreviewArea.ActualHeight * scale, item.PixelWidth, item.PixelHeight);
        PreviewImage.Width = width / scale;
        PreviewImage.Height = height / scale;
    }
}
