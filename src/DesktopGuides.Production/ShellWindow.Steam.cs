using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using DesktopGuides.Core.Online;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Online;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.System;

namespace DesktopGuides.Production;

/// <summary>One Steam search result: what the row shows, and the guide it opens.</summary>
public sealed partial class SteamResultItem(SteamGuideSummary guide) : INotifyPropertyChanged
{
    private ImageSource? preview;

    public SteamGuideSummary Guide { get; } = guide;
    public string Title => Guide.Title;
    public string Summary => Guide.Summary;

    public string Byline =>
        string.Join(" · ", new[]
        {
            Guide.Author is { } author ? $"By {author}" : null,
            Guide.Updated is { } updated ? $"updated {updated.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture)}" : null,
        }.OfType<string>());

    public string AccessibleName => Byline.Length > 0 ? $"{Title}, {Byline}" : Title;

    // A list row's accessible name falls back to its item's text.
    public override string ToString() => AccessibleName;

    public ImageSource? Preview
    {
        get => preview;
        set
        {
            preview = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One of Steam's games in the picker.</summary>
public sealed partial class SteamAppItem(SteamApp app)
{
    public SteamApp App { get; } = app;
    public string Name => App.Name;

    // A list row's accessible name falls back to its item's text.
    public override string ToString() => Name;
}

// S22 PR 4: Steam Community guides. The reader searches the game's guides
// with their own Web API key, or pastes a guide's address, which needs
// none; a chosen guide shows in the in-app browser, and Save fetches it
// with the app's own client rather than capturing it through the browser.
public sealed partial class ShellWindow
{
    private ProviderHttp? steamFixtureHttp;
    private SteamAppIndex? steamApps;

    private SteamAppIndex SteamApps => steamApps ??=
        new SteamAppIndex(Path.Combine(cacheRoot!, "steam-apps.json"), TimeProvider.System);
    private CancellationTokenSource? steamSearch;

    private bool SteamSelected =>
        OnlineSource.SelectedIndex >= 0 && OnlineSources.Available[OnlineSource.SelectedIndex].Site == Core.Library.SourceSite.Steam;

    // Test runs answer the app's Steam requests from the same fixtures as the browser's.
    private ProviderHttp SteamHttp()
    {
        if (steamFixtureHttp is not null) return steamFixtureHttp;
        if (OnlineFixtures() is { } routes)
        {
            string log = Path.Combine(cacheRoot!, "diagnostics");
            Directory.CreateDirectory(log);
            return steamFixtureHttp = new ProviderHttp(new OnlineFixtureHandler(routes, log));
        }
        return providers!.Http;
    }

    private SteamClient SteamApi() => providers!.CreateSteam(
        async token => (await providers.Credentials.LoadAsync(token)).SteamWebApiKey, SteamHttp());

    // Steam's panel shows while Steam is the site; its results take the
    // browser's place until a guide is chosen.
    private void ShowSteam(bool selected, bool results)
    {
        SteamPanel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        SteamResults.Visibility = selected && results ? Visibility.Visible : Visibility.Collapsed;
        OnlineBrowserHost.Visibility = selected && results ? Visibility.Collapsed : Visibility.Visible;
        if (!selected)
        {
            // A search still running would write to another site's view.
            steamSearch?.Cancel();
            SteamKeyInfo.IsOpen = false;
            SteamAppInfo.IsOpen = false;
            SteamGameRow.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowSteamSearch()
    {
        if (SteamQuery.Text.Length == 0) SteamQuery.Text = onlineGameTitle;
        OnlineAddress.Text = "";
        OnlineSave.IsEnabled = false;
        OnlineStatus.Text = "";
        ShowSteam(selected: true, results: true);
        SteamQuery.Focus(FocusState.Programmatic);
    }

    private void SteamQueryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter) return;
        args.Handled = true;
        _ = SearchSteamAsync();
    }

    private void SteamSearchClicked(object sender, RoutedEventArgs args) => _ = SearchSteamAsync();

    private async Task SearchSteamAsync()
    {
        if (repository is not SqliteLibraryRepository library || providers is null || onlineCapture is not null) return;
        ShowSteam(selected: true, results: true);
        OnlineSave.IsEnabled = false;
        string text = SteamQuery.Text.Trim();
        steamSearch?.Cancel();
        steamSearch?.Dispose();
        CancellationTokenSource search = steamSearch = new CancellationTokenSource();
        try
        {
            SteamKeyInfo.IsOpen = (await providers.Credentials.LoadAsync(search.Token)).SteamWebApiKey is null;
            if (SteamKeyInfo.IsOpen)
            {
                SteamAppInfo.IsOpen = false;
                SteamResults.ItemsSource = null;
                OnlineStatus.Text = "";
                return;
            }
            string? appId = await library.GetSteamAppIdAsync(onlineGameId, search.Token);
            // PR 4b: a game with no Steam app yet is matched by name: one
            // exact match is used, anything else is the reader's to choose.
            if (appId is null)
            {
                await LoadSteamAppsAsync(refresh: false, search.Token);
                if (!ReferenceEquals(search, steamSearch)) return;
                if (SteamApps.ExactSingle(onlineGameTitle) is { } only)
                {
                    await library.SetSteamAppIdAsync(onlineGameId, only.AppId, search.Token);
                    appId = only.AppId;
                }
                else
                {
                    SteamResults.ItemsSource = null;
                    ShowSteamPicker(onlineGameTitle);
                    return;
                }
            }
            SteamAppInfo.IsOpen = false;
            ShowSteamGame(appId);
            OnlineStatus.Text = "Searching Steam…";
            SteamGuidePage page = await SteamApi().QueryGuidesAsync(appId, text, 1, search.Token);
            if (!ReferenceEquals(search, steamSearch)) return;
            List<SteamResultItem> items = page.Items.Select(item => new SteamResultItem(item)).ToList();
            SteamResults.ItemsSource = items;
            OnlineStatus.Text = items.Count == 0 ? "No Steam guides match."
                : page.Total > items.Count ? $"Showing {items.Count} of {page.Total} Steam guides."
                : items.Count == 1 ? "1 Steam guide." : $"{items.Count} Steam guides.";
            AnnounceStatus(OnlineStatus.Text);
            await LoadSteamPreviewsAsync(items, search.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ProviderException error)
        {
            if (ReferenceEquals(search, steamSearch)) OnlineStatus.Text = ProviderMessages.ForSteam(error.Kind);
        }
    }

    // Previews are a nicety: one at a time, and one that fails stays blank.
    private async Task LoadSteamPreviewsAsync(IReadOnlyList<SteamResultItem> items, CancellationToken token)
    {
        ProviderHttp http = SteamHttp();
        foreach (SteamResultItem item in items)
        {
            if (item.Guide.Preview is not { } preview) continue;
            // Steam's image server sizes its images; a row needs 64 px.
            Uri url = preview.Query.Length > 0 ? preview : new Uri(preview, "?imw=128&imh=128&ima=fit");
            try
            {
                ProviderResponse response = await http.SendAsync(
                    HttpMethod.Get, url, SteamClient.Identify, ProviderHttp.MaxImageBytes, token);
                if (response.StatusCode != System.Net.HttpStatusCode.OK) continue;
                using InMemoryRandomAccessStream stream = new();
                await stream.WriteAsync(response.Body.AsBuffer());
                stream.Seek(0);
                BitmapImage image = new() { DecodePixelWidth = 128 };
                await image.SetSourceAsync(stream);
                item.Preview = image;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ProviderException error) when (error.Kind == ProviderErrorKind.RateLimited)
            {
                return;
            }
            catch (Exception)
            {
                // An image WinUI can't decode, or one Steam didn't send.
            }
        }
    }

    private async Task LoadSteamAppsAsync(bool refresh, CancellationToken token)
    {
        if (!refresh && (SteamApps.IsLoaded || SteamApps.LoadCached())) return;
        OnlineStatus.Text = "Loading Steam's game list…";
        await SteamApps.LoadAsync(
            fetchToken => SteamApi().GetAppListAsync(
                new Progress<int>(page => OnlineStatus.Text = $"Loading Steam's game list… (part {page})"), fetchToken),
            refresh, token);
        OnlineStatus.Text = "";
    }

    private void ShowSteamGame(string appId)
    {
        if (!SteamApps.IsLoaded) SteamApps.LoadCached();
        string name = SteamApps.NameOf(appId) ?? $"app {appId}";
        SteamGame.Text = $"Steam game: {name}";
        AutomationProperties.SetName(SteamGame, SteamGame.Text);
        SteamGameRow.Visibility = Visibility.Visible;
    }

    private void ShowSteamPicker(string query)
    {
        SteamAppInfo.IsOpen = true;
        SteamAppQuery.Text = query;
        ShowSteamAppMatches();
        SteamAppQuery.Focus(FocusState.Programmatic);
    }

    private void ShowSteamAppMatches()
    {
        List<SteamAppItem> matches = SteamApps.Match(SteamAppQuery.Text).Select(app => new SteamAppItem(app)).ToList();
        SteamAppResults.ItemsSource = matches;
        OnlineStatus.Text = matches.Count == 0
            ? "No Steam games match. Try another name, or paste the game's store page."
            : "Choose the game on Steam.";
        AnnounceStatus(OnlineStatus.Text);
    }

    private void SteamAppQueryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter) return;
        args.Handled = true;
        _ = FindSteamAppsAsync(refresh: false);
    }

    private void SteamAppFindClicked(object sender, RoutedEventArgs args) => _ = FindSteamAppsAsync(refresh: false);

    private void SteamAppRefreshClicked(object sender, RoutedEventArgs args) => _ = FindSteamAppsAsync(refresh: true);

    private async Task FindSteamAppsAsync(bool refresh)
    {
        try
        {
            await LoadSteamAppsAsync(refresh, CancellationToken.None);
            ShowSteamAppMatches();
        }
        catch (ProviderException error)
        {
            OnlineStatus.Text = $"{ProviderMessages.ForSteam(error.Kind)} Steam's game list couldn't be loaded.";
        }
    }

    private async void SteamAppChosen(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not SteamAppItem item || repository is not SqliteLibraryRepository library) return;
        try
        {
            await library.SetSteamAppIdAsync(onlineGameId, item.App.AppId);
        }
        catch (Exception)
        {
            OnlineStatus.Text = "Couldn't remember the game's Steam app. Try again.";
            return;
        }
        SteamAppInfo.IsOpen = false;
        await SearchSteamAsync();
    }

    private async void SteamChangeGameClicked(object sender, RoutedEventArgs args)
    {
        try
        {
            await LoadSteamAppsAsync(refresh: false, CancellationToken.None);
        }
        catch (ProviderException error)
        {
            OnlineStatus.Text = $"{ProviderMessages.ForSteam(error.Kind)} Steam's game list couldn't be loaded.";
        }
        ShowSteamPicker(onlineGameTitle);
    }

    private async void SteamUseAppClicked(object sender, RoutedEventArgs args)
    {
        if (repository is not SqliteLibraryRepository library) return;
        if (!Uri.TryCreate(SteamAppUrl.Text.Trim(), UriKind.Absolute, out Uri? url) || SteamUrls.AppId(url) is not { } appId)
        {
            OnlineStatus.Text = "That isn't a Steam store or guides page address.";
            return;
        }
        try
        {
            await library.SetSteamAppIdAsync(onlineGameId, appId);
        }
        catch (Exception)
        {
            OnlineStatus.Text = "Couldn't remember the game's Steam page. Try again.";
            return;
        }
        SteamAppInfo.IsOpen = false;
        await SearchSteamAsync();
    }

    private async void SteamOpenSettingsClicked(object sender, RoutedEventArgs args) =>
        await RunNavigationAsync(OpenSettingsFromDialogAsync);

    private void SteamResultClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not SteamResultItem item || onlineBrowser is null) return;
        OpenSteamGuide(SteamUrls.Guide(item.Guide.FileId));
    }

    private void OpenSteamGuide(Uri url)
    {
        ShowSteam(selected: true, results: false);
        SteamKeyInfo.IsOpen = false;
        SteamAppInfo.IsOpen = false;
        onlineBrowser?.Navigate(url);
    }

    private async Task SaveSteamGuideAsync(string fileId)
    {
        if (providers is null || onlineCapture is not null) return;
        onlineCapture = new CancellationTokenSource();
        CancellationToken token = onlineCapture.Token;
        OnlineInfo.IsOpen = false;
        OnlineInfo.Visibility = Visibility.Collapsed;
        OnlineSave.IsEnabled = false;
        OnlineProgressRow.Visibility = Visibility.Visible;
        OnlineProgress.Value = 0;
        OnlineStatus.Text = "Saving the guide…";
        SetOnlineBrowsing(false);
        try
        {
            ProviderHttp http = SteamHttp();
            SteamGuideFetcher fetcher = new(SteamApi(), http, Captures, TimeProvider.System);
            CaptureDraft draft = await fetcher.FetchAsync(onlineGameId, fileId,
                new Progress<(int Done, int Total)>(step =>
                {
                    OnlineStatus.Text = $"Saving image {Math.Min(step.Done + 1, step.Total)} of {step.Total}…";
                    OnlineProgress.Maximum = Math.Max(step.Total, 1);
                    OnlineProgress.Value = step.Done;
                }),
                token);
            await SaveDraftAsync(draft, allowFormatChange: false);
        }
        catch (OperationCanceledException)
        {
            OnlineStatus.Text = "Saving stopped. Nothing was saved.";
        }
        catch (SteamFetchException error)
        {
            OnlineStatus.Text = $"{ProviderMessages.ForSteam(error.Kind)} The save stopped at {error.Step}. Nothing was saved.";
            AnnounceStatus(OnlineStatus.Text);
        }
        catch (Exception)
        {
            // An async void handler leads here, so nothing may escape: a full
            // disk or a locked file, say. The fetch discarded its draft.
            OnlineStatus.Text = "The guide couldn't be saved. Nothing was saved.";
            AnnounceStatus(OnlineStatus.Text);
        }
        finally
        {
            onlineCapture.Dispose();
            onlineCapture = null;
            OnlineProgressRow.Visibility = Visibility.Collapsed;
            SetOnlineBrowsing(true);
            _ = UpdateOnlineSaveAsync(keepStatus: true);
        }
    }

    private void CloseSteam()
    {
        steamSearch?.Cancel();
        steamSearch?.Dispose();
        steamSearch = null;
        steamFixtureHttp?.Dispose();
        steamFixtureHttp = null;
        SteamResults.ItemsSource = null;
        SteamQuery.Text = "";
        ShowSteam(selected: false, results: false);
    }
}
