using DesktopGuides.Core.Library;
using DesktopGuides.Core.Navigation;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.System;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow : Window
{
    private readonly ShellNavigator navigator = new();
    private readonly NavigationActionQueue navigationQueue = new();
    private readonly CancellationTokenSource leaseWait = new();
    private Task initializationTask = Task.CompletedTask;
    private LibrarySessionLease? libraryLease;
    private SqliteLibraryRepository? repository;
    private Guid? resumeGuideId;
    private Guid? pendingGuideFocus;
    private int renderGeneration;
    private bool settingGuideSelection;
    private bool ready;
    private bool closeRequested;
    private bool allowClose;

    public ShellWindow()
    {
        InitializeComponent();
        Title = "Desktop Guides Preview";
        Navigation.SelectedItem = LibraryItem;
        GameList.AddHandler(
            UIElement.TappedEvent, new TappedEventHandler(GameTapped), true);
        GameList.AddHandler(
            UIElement.KeyDownEvent, new KeyEventHandler(GameKeyDown), true);
        GuideList.AddHandler(
            UIElement.TappedEvent, new TappedEventHandler(GuideTapped), true);
        GuideList.AddHandler(
            UIElement.KeyDownEvent, new KeyEventHandler(GuideKeyDown), true);
        Navigation.RegisterPropertyChangedCallback(
            NavigationView.IsPaneOpenProperty, (_, _) => UpdatePaneStatus());
        UpdatePaneStatus();
        ReaderActions.CommandFailed += message => ShellStatus.Text = message;
        AppWindow.Closing += WindowClosing;
    }

    private void UpdatePaneStatus() =>
        AutomationProperties.SetItemStatus(
            Navigation,
            Navigation.IsPaneOpen ? "Navigation pane open" : "Navigation pane closed");

    public Task InitializeAsync()
    {
        initializationTask = InitializeCoreAsync();
        return initializationTask;
    }

    internal bool IsClosing => closeRequested;

    private async Task InitializeCoreAsync()
    {
        try
        {
            string dataRoot = ApplicationData.Current.LocalFolder.Path;
            ShellStatus.Text = "Waiting for previous window...";
            libraryLease = await LibrarySessionLease.AcquireAsync(
                dataRoot, leaseWait.Token);
            ShellStatus.Text = "Loading library...";
            repository = new SqliteLibraryRepository(new ManagedPathResolver(dataRoot));
            await repository.InitializeAsync();
            ready = true;
            await RenderCurrentAsync();
        }
        catch (OperationCanceledException) when (closeRequested)
        {
            // The waiting window was closed before it acquired the library.
        }
        catch (Exception error)
        {
            ready = false;
            ShellStatus.Text = $"Could not open the library: {error.Message}";
        }
    }

    private void WindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowClose)
        {
            return;
        }
        args.Cancel = true;
        if (closeRequested)
        {
            return;
        }
        closeRequested = true;
        leaseWait.Cancel();
        Task pendingNavigation = navigationQueue.StopAndDrainAsync();
        Program.ReleaseInstanceKey();
        _ = CloseWhenIdleAsync(pendingNavigation);
    }

    private async Task CloseWhenIdleAsync(Task pendingNavigation)
    {
        // Close again after the first Closing event has returned.
        await Task.Yield();
        try
        {
            await Task.WhenAll(initializationTask, pendingNavigation);
        }
        finally
        {
            try
            {
                await navigationQueue.DisposeAsync();
            }
            finally
            {
                try
                {
                    if (repository is not null)
                    {
                        await repository.DisposeAsync();
                    }
                }
                finally
                {
                    try
                    {
                        if (libraryLease is not null)
                        {
                            await libraryLease.DisposeAsync();
                        }
                    }
                    finally
                    {
                        leaseWait.Dispose();
                        allowClose = true;
                        Close();
                    }
                }
            }
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
        await RunNavigationAsync(GoBackAsync);
    }

    private async void ReaderBackClicked(object sender, RoutedEventArgs args)
    {
        await RunNavigationAsync(GoBackAsync);
    }

    private async Task GoBackAsync()
    {
        ReaderRoute? reader = navigator.Current as ReaderRoute;
        if (!navigator.GoBack())
        {
            return;
        }
        if (reader is not null && navigator.Current is GameRoute game &&
            game.GameId == reader.GameId)
        {
            pendingGuideFocus = reader.GuideId;
        }
        await RenderCurrentAsync();
    }

    private async void GameSelected(object sender, SelectionChangedEventArgs args)
    {
        if (GameList.SelectedItem is Game game)
        {
            await RunNavigationAsync(() => OpenGameAsync(game.Id));
        }
    }

    private async void GameTapped(object sender, TappedRoutedEventArgs args)
    {
        if (GameFromRow(args.OriginalSource as DependencyObject) is Game game)
        {
            await RunNavigationAsync(() => OpenGameAsync(game.Id));
        }
    }

    private Game? GameFromRow(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, GameList))
        {
            if (source is ListViewItem row && row.Content is Game game)
            {
                return game;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private async void GameKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter &&
            GameFromRow(args.OriginalSource as DependencyObject) is Game game)
        {
            args.Handled = true;
            await RunNavigationAsync(() => OpenGameAsync(game.Id));
        }
    }

    private async void GuideSelected(object sender, SelectionChangedEventArgs args)
    {
        if (!settingGuideSelection && GuideList.SelectedItem is Guide guide)
        {
            await RunNavigationAsync(() => OpenGuideAsync(guide.Id));
        }
    }

    private async void GuideTapped(object sender, TappedRoutedEventArgs args)
    {
        if (GuideFromRow(args.OriginalSource as DependencyObject) is Guide guide)
        {
            await RunNavigationAsync(() => OpenGuideAsync(guide.Id));
        }
    }

    private Guide? GuideFromRow(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, GuideList))
        {
            if (source is ListViewItem row && row.Content is Guide guide)
            {
                return guide;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private async void GuideKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter &&
            GuideFromRow(args.OriginalSource as DependencyObject) is Guide guide)
        {
            args.Handled = true;
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

    private Task RunNavigationAsync(Func<Task> action) =>
        navigationQueue.RunAsync(async () =>
        {
            if (ready)
            {
                await action();
            }
        });

    private async Task OpenGameAsync(Guid gameId)
    {
        if (navigator.Current is GameRoute current && current.GameId == gameId)
        {
            return;
        }
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
        if (navigator.Current is ReaderRoute current && current.GuideId == guideId)
        {
            return;
        }
        ShellStatus.Text = "Opening guide...";
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
        if (navigator.Current is ReaderRoute)
        {
            Navigation.IsPaneOpen = false;
        }

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
                    Guid? selectedGuideId = pendingGuideFocus ??
                        (GuideList.SelectedItem as Guide)?.Id;
                    Guide? selectedGuide = selectedGuideId is Guid id
                        ? guides.FirstOrDefault(item => item.Id == id)
                        : null;
                    settingGuideSelection = true;
                    try
                    {
                        GuideList.ItemsSource = guides;
                        GuideList.SelectedItem = selectedGuide;
                    }
                    finally
                    {
                        settingGuideSelection = false;
                    }
                    GameEmpty.Visibility =
                        guides.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    if (pendingGuideFocus is not null && selectedGuide is not null)
                    {
                        GuideList.ScrollIntoView(selectedGuide);
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (navigator.Current is GameRoute current &&
                                current.GameId == gameRoute.GameId &&
                                (GuideList.SelectedItem as Guide)?.Id ==
                                selectedGuide.Id)
                            {
                                GuideList.UpdateLayout();
                                if (GuideList.ContainerFromItem(selectedGuide) is
                                    Control container)
                                {
                                    container.Focus(FocusState.Programmatic);
                                }
                                else
                                {
                                    GuideList.Focus(FocusState.Programmatic);
                                }
                            }
                        });
                    }
                    pendingGuideFocus = null;
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
                    Game? readerGame = await library.GetGameAsync(readerRoute.GameId);
                    if (generation != renderGeneration)
                    {
                        return;
                    }
                    if (readerGame is null)
                    {
                        navigator.ResetToLibrary();
                        await RenderCurrentAsync();
                        ShellStatus.Text = "This game is no longer in your library.";
                        return;
                    }
                    ReaderHeading.Text = guide.Title;
                    ReaderGameName.Text = readerGame.Title;
                    ReaderFormat.Text = guide.Format.ToString().ToUpperInvariant();
                    ShellStatus.Text = "Guide details ready.";
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
