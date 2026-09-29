using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production;

// Names each realized row for UIA and, when a loader is supplied, decodes its
// artwork in phase 1 of ContainerContentChanging. Recycled rows cancel their
// load and drop their thumbnail, so a reused container never shows stale art.
internal sealed class ArtworkListLoader
{
    private readonly Func<ArtworkItem, CancellationToken, Task<ImageSource?>>? load;
    private readonly ArtworkLoadTickets<SelectorItem> tickets = new();

    private ArtworkListLoader(Func<ArtworkItem, CancellationToken, Task<ImageSource?>>? load) =>
        this.load = load;

    // Add game uses naming only: its thumbnails come from the provider loader.
    public static void NameRows(ListViewBase list) =>
        list.ContainerContentChanging += new ArtworkListLoader(null).ContainerContentChanging;

    public static ArtworkListLoader Attach(
        ListViewBase list, Func<ArtworkItem, CancellationToken, Task<ImageSource?>> load)
    {
        ArtworkListLoader loader = new(load);
        list.ContainerContentChanging += loader.ContainerContentChanging;
        return loader;
    }

    // Call before replacing ItemsSource, so loads in flight can't apply to new rows.
    public void CancelAll() => tickets.ReleaseAll();

    private void ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not SelectorItem container) return;
        if (args.InRecycleQueue)
        {
            tickets.Release(container);
            if (args.Item is ArtworkItem recycled && load is not null) recycled.Thumbnail = null;
            return;
        }
        if (args.Item is not ArtworkItem item) return;
        if (args.Phase == 0)
        {
            AutomationProperties.SetName(container, item.AccessibleName);
            if (load is not null) args.RegisterUpdateCallback(1, ContainerContentChanging);
            return;
        }
        if (args.Phase == 1 && load is not null) _ = LoadAsync(container, item);
    }

    private async Task LoadAsync(SelectorItem container, ArtworkItem item)
    {
        ArtworkLoadTicket ticket = tickets.Issue(container);
        ImageSource? image;
        try
        {
            image = await load!(item, ticket.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (tickets.IsCurrent(container, ticket)) item.Thumbnail = image;
    }
}
