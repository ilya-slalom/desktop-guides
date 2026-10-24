using System.ComponentModel;
using System.Globalization;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Online;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Online;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DesktopGuides.Production;

/// <summary>A game in the atlas's results.</summary>
public sealed partial class VgMapsGameItem(VgMapsGame game)
{
    public VgMapsGame Game { get; } = game;
    public string Label => Game.Label;
    public override string ToString() => Label;
}

/// <summary>A map in a game's list: its check box, and why it can't be saved when it can't.</summary>
public sealed partial class VgMapsMapItem(VgMapsMap map) : INotifyPropertyChanged
{
    private bool isChecked;

    public VgMapsMap Map { get; } = map;
    public bool IsSaveable => Map.Saveable is VgMapsSaveable.Yes or VgMapsSaveable.Convert;

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public string Name
    {
        get
        {
            string? size = Map is { Width: int width, Height: int height } ? $"{width} \u00D7 {height}" : null;
            string? file = Map.Bytes is long bytes
                ? bytes >= 1 << 20
                    ? (bytes / (double)(1 << 20)).ToString("0.##", CultureInfo.InvariantCulture) + " MB"
                    : (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " kB"
                : null;
            string?[] parts = Map.Saveable switch
            {
                VgMapsSaveable.NotSaveable => [$"Not saveable ({Map.Format})"],
                VgMapsSaveable.TooLarge => ["Too large to save", size, file],
                _ => [size, file, Map.Format.Length > 0 ? Map.Format : null, Map.Ripper is { } ripper ? $"by {ripper}" : null],
            };
            return $"{Map.Area}: {string.Join(" \u00B7 ", parts.OfType<string>())}";
        }
    }

    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;
}

// S22 PR 5: The Video Game Atlas. The app searches the atlas's index,
// reads a game's maps from its system page, and saves the chosen ones as
// image maps; the browser isn't used.
public sealed partial class ShellWindow
{
    private VgMapsClient? vgMapsClient;
    private VgMapsIndex? vgMapsIndex;
    private VgMapsGamePage? vgMapsPage;
    private readonly Dictionary<string, string> vgMapsSystemPages = new(StringComparer.Ordinal);

    private bool VgMapsSelected =>
        OnlineSource.SelectedIndex >= 0 && OnlineSources.Available[OnlineSource.SelectedIndex].Site == Core.Library.SourceSite.VgMaps;

    private VgMapsClient VgMapsApi() => vgMapsClient ??= new VgMapsClient(SteamHttp());

    private VgMapsIndex VgMapsAtlas => vgMapsIndex ??=
        new VgMapsIndex(Path.Combine(cacheRoot!, "vgmaps-index.json"), TimeProvider.System);

    private void ShowVgMaps(bool selected)
    {
        VgMapsPanel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        if (!selected)
        {
            VgMapsGames.Visibility = VgMapsMaps.Visibility = VgMapsGameRow.Visibility = Visibility.Collapsed;
            return;
        }
        OnlineBrowserHost.Visibility = Visibility.Collapsed;
        bool game = vgMapsPage is not null;
        VgMapsGames.Visibility = game ? Visibility.Collapsed : Visibility.Visible;
        VgMapsMaps.Visibility = VgMapsGameRow.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowVgMapsSearch()
    {
        if (VgMapsQuery.Text.Length == 0) VgMapsQuery.Text = onlineGameTitle;
        OnlineAddress.Text = "";
        OnlineSave.IsEnabled = false;
        OnlineStatus.Text = "";
        ShowVgMaps(selected: true);
        VgMapsQuery.Focus(FocusState.Programmatic);
    }

    private void VgMapsQueryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter) return;
        args.Handled = true;
        _ = SearchVgMapsAsync(refresh: false);
    }

    private void VgMapsSearchClicked(object sender, RoutedEventArgs args) => _ = SearchVgMapsAsync(refresh: false);

    private void VgMapsRefreshClicked(object sender, RoutedEventArgs args) => _ = SearchVgMapsAsync(refresh: true);

    private async Task SearchVgMapsAsync(bool refresh)
    {
        if (onlineCapture is not null) return;
        vgMapsPage = null;
        ShowVgMaps(selected: true);
        try
        {
            if (refresh || !VgMapsAtlas.IsFresh) OnlineStatus.Text = "Loading the atlas's index…";
            await VgMapsAtlas.LoadAsync(async token => VgMapsIndex.Parse(await VgMapsApi().GetIndexAsync(token)), refresh, CancellationToken.None);
            List<VgMapsGameItem> games = VgMapsAtlas.Search(VgMapsQuery.Text).Select(game => new VgMapsGameItem(game)).ToList();
            VgMapsGames.ItemsSource = games;
            OnlineStatus.Text = games.Count == 0 ? "No games in the atlas match."
                : games.Count == 1 ? "1 game in the atlas." : $"{games.Count} games in the atlas.";
        }
        catch (ProviderException error)
        {
            OnlineStatus.Text = $"{ProviderMessages.ForVgMaps(error.Kind)} The atlas's index couldn't be loaded.";
        }
        catch (Exception)
        {
            // Handlers that await this are async void: nothing may escape.
            OnlineStatus.Text = "The atlas's index couldn't be loaded. Try again.";
        }
        AnnounceStatus(OnlineStatus.Text);
    }

    private async void VgMapsGameChosen(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not VgMapsGameItem item || onlineCapture is not null) return;
        try
        {
            OnlineStatus.Text = $"Loading {item.Label}…";
            if (!vgMapsSystemPages.TryGetValue(item.Game.SystemPath, out string? html))
            {
                html = await VgMapsApi().GetSystemPageAsync(item.Game.SystemPath, CancellationToken.None);
                vgMapsSystemPages[item.Game.SystemPath] = html;
            }
            VgMapsGamePage? page = await Task.Run(() =>
                VgMapsSystemPage.Read(html, VgMapsClient.SystemPage(item.Game.SystemPath), item.Game));
            if (page is null || page.Maps.Count == 0)
            {
                OnlineStatus.Text = $"{item.Label}'s maps couldn't be found on its page.";
                return;
            }
            vgMapsPage = page;
            VgMapsGameHeading.Text = page.Copyright is { } copyright ? $"{item.Label} \u00B7 {copyright}" : item.Label;
            VgMapsMaps.ItemsSource = page.Maps.Select(map => new VgMapsMapItem(map)).ToList();
            ShowVgMaps(selected: true);
            int saveable = page.Maps.Count(map => map.Saveable is VgMapsSaveable.Yes or VgMapsSaveable.Convert);
            OnlineStatus.Text = $"{page.Maps.Count} maps, {saveable} of them saveable.";
            VgMapsMaps.Focus(FocusState.Programmatic);
        }
        catch (ProviderException error)
        {
            OnlineStatus.Text = $"{ProviderMessages.ForVgMaps(error.Kind)} {item.Label}'s page couldn't be loaded.";
        }
        catch (Exception)
        {
            OnlineStatus.Text = $"{item.Label}'s page couldn't be loaded. Try again.";
        }
        AnnounceStatus(OnlineStatus.Text);
    }

    private void VgMapsBackClicked(object sender, RoutedEventArgs args)
    {
        vgMapsPage = null;
        ShowVgMaps(selected: true);
        OnlineStatus.Text = "";
        VgMapsGames.Focus(FocusState.Programmatic);
    }

    private void VgMapsSaveSelectedClicked(object sender, RoutedEventArgs args) =>
        _ = SaveVgMapsAsync(item => item.IsChecked);

    private void VgMapsSaveAllClicked(object sender, RoutedEventArgs args) => _ = SaveVgMapsAsync(_ => true);

    private async Task SaveVgMapsAsync(Func<VgMapsMapItem, bool> chosen)
    {
        if (vgMapsPage is not { } page || onlineCapture is not null ||
            repository is not SqliteLibraryRepository library || guidePublisher is not { } publisher)
        {
            return;
        }
        VgMapsMap[] maps = (VgMapsMaps.ItemsSource as IEnumerable<VgMapsMapItem> ?? [])
            .Where(item => item.IsSaveable && chosen(item)).Select(item => item.Map).ToArray();
        if (maps.Length == 0)
        {
            OnlineStatus.Text = "Choose the maps to save.";
            return;
        }
        onlineCapture = new CancellationTokenSource();
        CancellationToken token = onlineCapture.Token;
        OnlineProgressRow.Visibility = Visibility.Visible;
        OnlineProgress.Value = 0;
        SetOnlineBrowsing(false);
        VgMapsSaveSelected.IsEnabled = VgMapsSaveAll.IsEnabled = false;
        OnlineStatus.Text = $"Saving map 1 of {maps.Length}…";
        // Progress reports arrive through the UI thread's queue, so the last can
        // land after the outcome; once the save has ended they're dropped.
        bool ended = false;
        try
        {
            VgMapsSaver saver = new(VgMapsApi(), library, publisher, importValidator, new WinRtMapImageConverter(),
                Path.Combine(cacheRoot!, "online-work"), TimeProvider.System);
            VgMapsSaveResult result = await saver.SaveAsync(onlineGameId, page, maps,
                new Progress<(int Done, int Total)>(step =>
                {
                    if (ended) return;
                    OnlineStatus.Text = $"Saving map {Math.Min(step.Done + 1, step.Total)} of {step.Total}…";
                    OnlineProgress.Maximum = step.Total;
                    OnlineProgress.Value = step.Done;
                }),
                token);
            ended = true;
            OnlineStatus.Text = Saved(result);
            ShowStatus(OnlineStatus.Text, InfoBarSeverity.Success, true, false);
            await RemindToExportAsync();
        }
        catch (OperationCanceledException)
        {
            ended = true;
            OnlineStatus.Text = "Saving stopped. The maps saved before it are kept.";
        }
        catch (VgMapsSaveException error)
        {
            ended = true;
            string why = error.Kind is { } kind ? ProviderMessages.ForVgMaps(kind)
                : error.InnerException is GuideImportException import ? import.Message
                : "A map couldn't be saved.";
            string kept = error.Saved == 1 ? "1 map before it is saved." : $"{error.Saved} maps before it are saved.";
            OnlineStatus.Text = $"{why} The save stopped at {error.Step}; {kept}";
        }
        catch (Exception)
        {
            ended = true;
            OnlineStatus.Text = "The maps couldn't be saved. Try again.";
        }
        finally
        {
            onlineCapture.Dispose();
            onlineCapture = null;
            OnlineProgressRow.Visibility = Visibility.Collapsed;
            SetOnlineBrowsing(true);
            VgMapsSaveSelected.IsEnabled = VgMapsSaveAll.IsEnabled = true;
            AnnounceStatus(OnlineStatus.Text);
        }
    }

    private string Saved(VgMapsSaveResult result)
    {
        static string Maps(int count) => count == 1 ? "1 map" : $"{count} maps";
        if (result.Added == 0 && result.Replaced == 0)
        {
            return result.UpToDate == 1 ? "1 map is up to date." : $"{result.UpToDate} maps are up to date.";
        }
        List<string> parts = [];
        if (result.Added > 0) parts.Add($"Saved {Maps(result.Added)} to {onlineGameTitle}");
        if (result.Replaced > 0) parts.Add(parts.Count == 0 ? $"Replaced {Maps(result.Replaced)}" : $"replaced {Maps(result.Replaced)}");
        if (result.UpToDate > 0) parts.Add($"{Maps(result.UpToDate)} up to date");
        return string.Join(", ", parts) + ".";
    }

    private void CloseVgMaps()
    {
        vgMapsPage = null;
        vgMapsClient = null;
        vgMapsSystemPages.Clear();
        VgMapsGames.ItemsSource = null;
        VgMapsMaps.ItemsSource = null;
        VgMapsQuery.Text = "";
        ShowVgMaps(selected: false);
    }
}
