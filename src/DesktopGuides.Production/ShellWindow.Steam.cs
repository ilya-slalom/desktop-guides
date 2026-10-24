using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using DesktopGuides.Core.Online;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Online;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.UI.Xaml;
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

// S22 PR 4: Steam Community guides. The reader searches the game's guides
// with their own Web API key, or pastes a guide's address, which needs
// none; a chosen guide shows in the in-app browser, and Save fetches it
// with the app's own client rather than capturing it through the browser.
public sealed partial class ShellWindow
{
    private ProviderHttp? steamFixtureHttp;
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
            SteamKeyInfo.IsOpen = false;
            SteamAppInfo.IsOpen = false;
        }
    }

    private void ShowSteamSearch()
    {
        if (SteamQuery.Text.Length == 0) SteamQuery.Text = onlineGameTitle;
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
            SteamAppInfo.IsOpen = appId is null;
            if (appId is null)
            {
                SteamResults.ItemsSource = null;
                OnlineStatus.Text = "";
                SteamAppUrl.Focus(FocusState.Programmatic);
                return;
            }
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
            if (item.Guide.Preview is not { } url) continue;
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
            catch (Exception)
            {
                // An image WinUI can't decode, or one Steam didn't send.
            }
        }
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
        catch (ProviderException error)
        {
            OnlineStatus.Text = $"{ProviderMessages.ForSteam(error.Kind)} Nothing was saved.";
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
        steamSearch = null;
        steamFixtureHttp?.Dispose();
        steamFixtureHttp = null;
        SteamResults.ItemsSource = null;
        SteamQuery.Text = "";
        ShowSteam(selected: false, results: false);
    }
}
