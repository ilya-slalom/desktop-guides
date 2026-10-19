using DesktopGuides.Core.Library;
using DesktopGuides.Core.Maps;
using DesktopGuides.Core.Navigation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopGuides.Production;

// T21.2 PR c: the game's maps from any reader. The button shows once the
// game is known to have maps; the list is read again each time the pane
// opens, so a map imported or removed meanwhile is current. Showing a
// preview saves no place and records no open; only Open map opens a map,
// as any guide switch does.
public sealed partial class ShellWindow
{
    private CancellationTokenSource? mapsLoad;

    private void InitializeMaps()
    {
        ReaderActions.MapsOpening += (_, _) => _ = FillMapsPaneAsync();
        // Closing returns focus where it was (the flyout does this), so the
        // guide keeps its place: refocusing its list would scroll to the top.
        ReaderActions.Maps.OpenRequested += (_, mapId) => _ = OpenMapFromPaneAsync(mapId);
        ReaderActions.Maps.LoadPreview = LoadMapPreviewAsync;
    }

    // After a reader opens: does this game have maps?
    private async void RefreshMapsButton(Guid gameId, int generation)
    {
        mapsLoad?.Cancel();
        CancellationTokenSource cancel = new();
        mapsLoad = cancel;
        try
        {
            if (repository is null) return;
            IReadOnlyList<MapsMenuItem> items = MapsMenu.Items(await repository.ListGuideSummariesAsync(gameId, cancel.Token));
            if (cancel.IsCancellationRequested || generation != renderGeneration) return;
            ReaderActions.SetMaps(items.Count > 0);
        }
        catch (Exception)
        {
            // Without the list, the reader simply has no Maps button.
        }
    }

    private async Task FillMapsPaneAsync()
    {
        if (repository is null || navigator.Current is not ReaderRoute route) return;
        try
        {
            IReadOnlyList<MapsMenuItem> items = MapsMenu.Items(await repository.ListGuideSummariesAsync(route.GameId));
            if (navigator.Current != route) return;
            ReaderActions.Maps.Show(items, route.GuideId);
        }
        catch (Exception)
        {
            ReaderActions.CloseMaps();
        }
    }

    private async Task OpenMapFromPaneAsync(Guid mapId)
    {
        ReaderActions.CloseMaps();
        if (navigator.Current is ReaderRoute route && route.GuideId == mapId) return;
        await OpenGuideAsync(mapId);
    }

    // A large map's preview is its cache's overview; a small map's is its
    // file, decoded to at most 2048 pixels. The file is closed once read.
    private async Task<ImageSource> LoadMapPreviewAsync(MapsMenuItem item, CancellationToken token)
    {
        Guide map = await repository!.GetGuideAsync(item.Id, token)
            ?? throw new FileNotFoundException("The map is no longer in the library.");
        string path = libraryPaths!.ResolveExistingGuideFile(map.Id, map.PrimaryRelativePath);
        string source = path;
        int decodeWidth = MapPreview.DecodeWidth(item.PixelWidth, item.PixelHeight);
        if (!new MapPyramid(item.PixelWidth, item.PixelHeight).Small && mapCache is not null)
        {
            source = await mapCache.Ensure(map, path, front: true).Overview.WaitAsync(token);
            decodeWidth = 0;
        }
        BitmapImage bitmap = new() { DecodePixelWidth = decodeWidth };
        await using FileStream file = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await bitmap.SetSourceAsync(file.AsRandomAccessStream()).AsTask(token);
        return bitmap;
    }

    private bool ToggleMapsFromKeyboard()
    {
        if (navigator.Current is not ReaderRoute) return false;
        if (ReaderActions.MapsOpen)
        {
            ReaderActions.CloseMaps();
            return true;
        }
        ReaderActions.OpenMaps(fromKeyboard: true);
        return ReaderActions.MapsOpen;
    }
}
