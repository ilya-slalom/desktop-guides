using DesktopGuides.Core.Library;
using DesktopGuides.Core.Navigation;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow : Window
{
    private readonly ShellNavigator navigator = new();
    private readonly SemaphoreSlim navigationGate = new(1, 1);
    private SqliteLibraryRepository? repository;
    private Guid? resumeGuideId;
    private int renderGeneration;
    private bool ready;

    public ShellWindow()
    {
        InitializeComponent();
        Title = "Desktop Guides Preview";
        Navigation.SelectedItem = LibraryItem;
        Closed += async (_, _) =>
        {
            if (repository is not null)
            {
                await repository.DisposeAsync();
            }
        };
    }

    public async Task InitializeAsync()
    {
        try
        {
            string dataRoot = ApplicationData.Current.LocalFolder.Path;
            repository = new SqliteLibraryRepository(new ManagedPathResolver(dataRoot));
            await repository.InitializeAsync();
            ready = true;
            await RenderCurrentAsync();
        }
        catch (Exception error)
        {
            ready = false;
            ShellStatus.Text = $"Could not open the library: {error.Message}";
        }
    }

    private async void NavigationInvoked(
        NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        bool openSettings = args.IsSettingsInvoked;
        await RunNavigationAsync(async () =>
        {
            if (openSettings)
            {
                navigator.OpenSettings();
            }
            else
            {
                navigator.OpenLibrary();
            }
            await RenderCurrentAsync();
        });
    }

    private async void NavigationBackRequested(
        NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        await RunNavigationAsync(async () =>
        {
            if (navigator.GoBack())
            {
                await RenderCurrentAsync();
            }
        });
    }

    private async void GameSelected(object sender, SelectionChangedEventArgs args)
    {
        if (GameList.SelectedItem is Game game)
        {
            await RunNavigationAsync(() => OpenGameAsync(game.Id));
        }
    }

    private async void GuideSelected(object sender, SelectionChangedEventArgs args)
    {
        if (GuideList.SelectedItem is Guide guide)
        {
            await RunNavigationAsync(() => OpenGuideAsync(guide.Id));
        }
    }

    private async void ResumeClicked(object sender, RoutedEventArgs args)
    {
        if (resumeGuideId is Guid guideId)
        {
            await RunNavigationAsync(() => OpenGuideAsync(guideId));
        }
    }

    private async Task RunNavigationAsync(Func<Task> action)
    {
        // Finish route changes and last-guide writes in the order requested.
        await navigationGate.WaitAsync();
        try
        {
            if (ready)
            {
                await action();
            }
        }
        finally
        {
            navigationGate.Release();
        }
    }

    private async Task OpenGameAsync(Guid gameId)
    {
        try
        {
            if (await RequireRepository().GetGameAsync(gameId) is null)
            {
                ShellStatus.Text = "This game is no longer in your library.";
                return;
            }
            navigator.OpenGame(gameId);
            await RenderCurrentAsync();
        }
        catch (Exception error)
        {
            ShellStatus.Text = $"Could not open the game: {error.Message}";
        }
    }

    private async Task OpenGuideAsync(Guid guideId)
    {
        try
        {
            SqliteLibraryRepository library = RequireRepository();
            Guide? guide = await library.GetGuideAsync(guideId);
            if (guide is null || await library.GetGameAsync(guide.GameId) is null)
            {
                ShellStatus.Text = "This guide is no longer in your library.";
                return;
            }
            AppSettings settings = await library.GetSettingsAsync();
            await library.SaveSettingsAsync(settings with { LastActiveGuideId = guide.Id });
            navigator.OpenReader(guide.Id, guide.GameId);
            await RenderCurrentAsync();
        }
        catch (Exception error)
        {
            ShellStatus.Text = $"Could not open the guide: {error.Message}";
        }
    }

    private async Task RenderCurrentAsync()
    {
        int generation = ++renderGeneration;
        LibraryPanel.Visibility = Visibility.Collapsed;
        GamePanel.Visibility = Visibility.Collapsed;
        ReaderPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        Navigation.IsBackEnabled = navigator.CanGoBack;
        Navigation.SelectedItem = navigator.Current is SettingsRoute
            ? Navigation.SettingsItem
            : LibraryItem;

        SqliteLibraryRepository library = RequireRepository();
        try
        {
            switch (navigator.Current)
            {
                case LibraryRoute:
                    LibraryPanel.Visibility = Visibility.Visible;
                    ShellStatus.Text = "Loading library…";
                    IReadOnlyList<Game> games = await library.ListGamesAsync();
                    AppSettings settings = await library.GetSettingsAsync();
                    Guide? resume = settings.LastActiveGuideId is Guid lastId
                        ? await library.GetGuideAsync(lastId)
                        : null;
                    if (generation != renderGeneration)
                    {
                        return;
                    }
                    GameList.ItemsSource = games;
                    LibraryEmpty.Visibility =
                        games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    resumeGuideId = resume?.Id;
                    ResumeButton.Visibility =
                        resume is null ? Visibility.Collapsed : Visibility.Visible;
                    if (resume is not null)
                    {
                        ResumeButton.Content = $"Resume {resume.Title}";
                    }
                    ShellStatus.Text = "Library ready.";
                    break;

                case GameRoute gameRoute:
                    GamePanel.Visibility = Visibility.Visible;
                    ShellStatus.Text = "Loading game…";
                    Game? game = await library.GetGameAsync(gameRoute.GameId);
                    if (generation != renderGeneration)
                    {
                        return;
                    }
                    if (game is null)
                    {
                        navigator.ResetToLibrary();
                        await RenderCurrentAsync();
                        ShellStatus.Text = "This game is no longer in your library.";
                        return;
                    }
                    IReadOnlyList<Guide> guides =
                        await library.ListGuidesAsync(gameRoute.GameId);
                    if (generation != renderGeneration)
                    {
                        return;
                    }
                    GameHeading.Text = game.Title;
                    GamePlatform.Text = game.Platform ?? string.Empty;
                    GuideList.ItemsSource = guides;
                    GameEmpty.Visibility =
                        guides.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    ShellStatus.Text = "Game ready.";
                    break;

                case ReaderRoute readerRoute:
                    ReaderPanel.Visibility = Visibility.Visible;
                    ShellStatus.Text = "Loading guide…";
                    Guide? guide = await library.GetGuideAsync(readerRoute.GuideId);
                    if (generation != renderGeneration)
                    {
                        return;
                    }
                    if (guide is null || guide.GameId != readerRoute.GameId)
                    {
                        navigator.ResetToLibrary();
                        await RenderCurrentAsync();
                        ShellStatus.Text = "This guide is no longer in your library.";
                        return;
                    }
                    ReaderHeading.Text = guide.Title;
                    ReaderFormat.Text = guide.Format.ToString().ToUpperInvariant();
                    ShellStatus.Text = "Guide ready.";
                    break;

                case SettingsRoute:
                    SettingsPanel.Visibility = Visibility.Visible;
                    ShellStatus.Text = "Settings ready.";
                    break;
            }
        }
        catch (Exception error)
        {
            if (generation == renderGeneration)
            {
                ShellStatus.Text = $"Could not load this view: {error.Message}";
            }
        }
    }

    private SqliteLibraryRepository RequireRepository() =>
        repository ?? throw new InvalidOperationException("The library is not ready.");
}
