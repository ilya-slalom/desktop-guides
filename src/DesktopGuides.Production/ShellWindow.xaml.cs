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
    private int guideFocusRenderGeneration = -1;
    private int renderGeneration;
    private long gameGuideIntentVersion;
    private bool settingGuideSelection;
    private bool ready;
    private bool closeRequested;
    private bool allowClose;
    private bool gameEditorRequested;
    private GameEditorDialog? activeGameEditor;

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
        GuideList.LayoutUpdated += GuideListLayoutUpdated;
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

    private void ShellContentSizeChanged(object sender, SizeChangedEventArgs args)
    {
        ResourceDictionary resources = Application.Current.Resources;
        double narrowBreakpoint =
            (double)resources["DesktopGuidesNarrowBreakpoint"];
        double wideBreakpoint =
            (double)resources["DesktopGuidesWideBreakpoint"];
        string paddingKey = args.NewSize.Width <= narrowBreakpoint
            ? "DesktopGuidesPagePaddingNarrow"
            : args.NewSize.Width >= wideBreakpoint
                ? "DesktopGuidesPagePaddingWide"
                : "DesktopGuidesPagePadding";
        ShellContent.Padding = (Thickness)resources[paddingKey];
    }

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
        activeGameEditor?.Hide();
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
        if (settingGuideSelection)
        {
            return;
        }
        UpdateOpenSelectedGuideAction();
        if (GuideList.SelectedItem is Guide guide)
        {
            await OpenGuideFromGameAsync(guide);
        }
    }

    private async void GuideTapped(object sender, TappedRoutedEventArgs args)
    {
        if (GuideFromRow(args.OriginalSource as DependencyObject) is Guide guide)
        {
            await OpenGuideFromGameAsync(guide);
        }
    }

    private Guide? GuideFromRow(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, GuideList))
        {
            if (source is Button)
            {
                return null;
            }
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
            await OpenGuideFromGameAsync(guide);
        }
    }

    private async void OpenSelectedGuideClicked(object sender, RoutedEventArgs args)
    {
        if (GuideList.SelectedItem is Guide guide)
        {
            await OpenGuideFromGameAsync(guide);
        }
    }

    private Task OpenGuideFromGameAsync(Guide guide)
    {
        if (closeRequested)
        {
            return Task.CompletedTask;
        }
        long intentVersion = ++gameGuideIntentVersion;
        return RunNavigationAsync(
            () => OpenGuideAsync(guide.Id, guide.GameId, intentVersion));
    }

    private void UpdateOpenSelectedGuideAction()
    {
        if (GuideList.IsEnabled &&
            navigator.Current is GameRoute game &&
            GuideList.SelectedItem is Guide guide &&
            guide.GameId == game.GameId)
        {
            AutomationProperties.SetName(
                OpenSelectedGuideButton, $"Open {guide.Title}");
            OpenSelectedGuideButton.Visibility = Visibility.Visible;
        }
        else
        {
            OpenSelectedGuideButton.Visibility = Visibility.Collapsed;
        }
    }

    private void GuideListLayoutUpdated(object? sender, object args) =>
        TryRestoreGuideFocus();

    private void TryRestoreGuideFocus()
    {
        if (pendingGuideFocus is not Guid guideId ||
            guideFocusRenderGeneration != renderGeneration ||
            navigator.Current is not GameRoute game ||
            GuideList.SelectedItem is not Guide selected ||
            selected.Id != guideId ||
            selected.GameId != game.GameId ||
            GuideList.ContainerFromItem(selected) is not Control container)
        {
            return;
        }
        // Focus can cause another layout pass. Suspend the callback while it runs.
        guideFocusRenderGeneration = -1;
        if (container.Focus(FocusState.Programmatic))
        {
            pendingGuideFocus = null;
        }
        else
        {
            guideFocusRenderGeneration = renderGeneration;
        }
    }

    private async void ResumeClicked(object sender, RoutedEventArgs args)
    {
        if (resumeGuideId is Guid guideId)
        {
            await RunNavigationAsync(() => OpenGuideAsync(guideId));
        }
    }

    private async void AddGameClicked(object sender, RoutedEventArgs args)
    {
        if (gameEditorRequested || closeRequested)
        {
            return;
        }
        gameEditorRequested = true;
        AddGameButton.IsEnabled = false;
        try
        {
            await RunNavigationAsync(async () =>
            {
                if (closeRequested || navigator.Current is not LibraryRoute)
                {
                    return;
                }
                Game? created = null;
                GameEditorDialog editor = new(null, async details =>
                {
                    created = await RequireRepository().AddGameAsync(
                        details.Title, details.Platform, details.Notes);
                })
                {
                    XamlRoot = Navigation.XamlRoot
                };
                activeGameEditor = editor;
                try
                {
                    if (await editor.ShowAsync() == ContentDialogResult.Primary &&
                        created is not null && !closeRequested)
                    {
                        navigator.OpenGame(created.Id);
                        await RenderCurrentAsync();
                    }
                }
                finally
                {
                    activeGameEditor = null;
                }
            });
        }
        finally
        {
            gameEditorRequested = false;
            if (!closeRequested)
            {
                AddGameButton.IsEnabled = true;
            }
        }
    }

    private async void EditGameClicked(object sender, RoutedEventArgs args)
    {
        if (gameEditorRequested || closeRequested ||
            navigator.Current is not GameRoute route)
        {
            return;
        }
        gameEditorRequested = true;
        EditGameButton.IsEnabled = false;
        try
        {
            await RunNavigationAsync(async () =>
            {
                if (closeRequested ||
                    navigator.Current is not GameRoute current ||
                    current.GameId != route.GameId)
                {
                    return;
                }
                Game? game = await RequireRepository().GetGameAsync(route.GameId);
                if (closeRequested)
                {
                    return;
                }
                if (game is null)
                {
                    ShellStatus.Text = "This game is no longer in your library.";
                    return;
                }
                GameEditorDialog editor = new(game, async details =>
                {
                    await RequireRepository().UpdateGameAsync(
                        game.Id, details.Title, details.Platform, details.Notes);
                })
                {
                    XamlRoot = Navigation.XamlRoot
                };
                activeGameEditor = editor;
                try
                {
                    if (await editor.ShowAsync() == ContentDialogResult.Primary &&
                        !closeRequested)
                    {
                        await RenderCurrentAsync();
                    }
                }
                finally
                {
                    activeGameEditor = null;
                }
            });
        }
        finally
        {
            gameEditorRequested = false;
            if (!closeRequested && navigator.Current is GameRoute)
            {
                EditGameButton.IsEnabled = true;
            }
        }
    }

    private Task RunNavigationAsync(Func<Task> action) =>
        navigationQueue.RunAsync(async () =>
        {
            if (ready && !closeRequested)
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
            navigator.OpenGame(gameId);
            await RenderCurrentAsync();
        }
        catch (Exception error)
        {
            ShellStatus.Text = $"Could not open the game: {error.Message}";
        }
    }

    private bool IsSupersededGameGuideIntent(long? intentVersion) =>
        intentVersion is long version && version != gameGuideIntentVersion;

    private static async Task PauseReaderMetadataReadForTestAsync()
    {
        string prefix = $@"Local\DesktopGuides.Preview.ReaderLoad.{Environment.ProcessId}";
        try
        {
            if (!EventWaitHandle.TryOpenExisting(
                $"{prefix}.Reached", out EventWaitHandle? reached))
            {
                return;
            }
            using (reached)
            {
                if (!EventWaitHandle.TryOpenExisting(
                    $"{prefix}.Continue", out EventWaitHandle? resume))
                {
                    return;
                }
                using (resume)
                {
                    reached.Set();
                    if (!await Task.Run(() => resume.WaitOne(30_000)))
                    {
                        throw new TimeoutException("Reader metadata test gate timed out.");
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Optional installed-test synchronization must not affect normal reading.
        }
    }

    private async Task OpenGuideAsync(
        Guid guideId, Guid? sourceGameId = null, long? intentVersion = null)
    {
        if (IsSupersededGameGuideIntent(intentVersion) ||
            sourceGameId is Guid expectedGameId &&
            (navigator.Current is not GameRoute game ||
             game.GameId != expectedGameId ||
             !GuideList.IsEnabled))
        {
            return;
        }
        if (navigator.Current is ReaderRoute current && current.GuideId == guideId)
        {
            return;
        }
        ShellStatus.Text = "Opening guide...";
        try
        {
            SqliteLibraryRepository library = RequireRepository();
            Guide? guide = await library.GetGuideAsync(guideId);
            if (IsSupersededGameGuideIntent(intentVersion))
            {
                return;
            }
            if (guide is null ||
                (sourceGameId is Guid source && guide.GameId != source))
            {
                ShellStatus.Text = "This guide is no longer in your library.";
                return;
            }
            Game? ownerGame = await library.GetGameAsync(guide.GameId);
            if (IsSupersededGameGuideIntent(intentVersion))
            {
                return;
            }
            if (ownerGame is null)
            {
                ShellStatus.Text = "This guide is no longer in your library.";
                return;
            }
            AppSettings settings = await library.GetSettingsAsync();
            if (IsSupersededGameGuideIntent(intentVersion))
            {
                return;
            }
            navigator.OpenReader(guide.Id, guide.GameId);
            bool rendered = await RenderCurrentAsync();
            if (rendered &&
                navigator.Current is ReaderRoute opened &&
                opened.GuideId == guide.Id &&
                opened.GameId == guide.GameId)
            {
                try
                {
                    await library.SaveSettingsAsync(
                        settings with { LastActiveGuideId = guide.Id });
                }
                catch (Exception error)
                {
                    ShellStatus.Text = $"Could not save Resume: {error.Message}";
                }
            }
        }
        catch (Exception error)
        {
            ShellStatus.Text = $"Could not open the guide: {error.Message}";
        }
    }

    private async Task<bool> RenderCurrentAsync()
    {
        int generation = ++renderGeneration;
        guideFocusRenderGeneration = -1;
        if (navigator.Current is not GameRoute)
        {
            pendingGuideFocus = null;
        }
        LibraryPanel.Visibility = Visibility.Collapsed;
        GamePanel.Visibility = Visibility.Collapsed;
        ReaderPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        OpenSelectedGuideButton.Visibility = Visibility.Collapsed;
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
                        return false;
                    }
                    GameList.ItemsSource = games;
                    LibraryEmptyState.Visibility =
                        games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    GameList.Visibility =
                        games.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
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
                    Guid? selectedGuideId = pendingGuideFocus ??
                        (GuideList.SelectedItem as Guide)?.Id;
                    GameHeading.Text = "Loading game…";
                    GamePlatform.Text = string.Empty;
                    GameNotes.Text = string.Empty;
                    GameMetadataSurface.Visibility = Visibility.Collapsed;
                    EditGameButton.IsEnabled = false;
                    GameEmptyState.Visibility = Visibility.Collapsed;
                    GuideList.Visibility = Visibility.Collapsed;
                    GuideList.IsEnabled = false;
                    settingGuideSelection = true;
                    try
                    {
                        GuideList.ItemsSource = null;
                        GuideList.SelectedItem = null;
                    }
                    finally
                    {
                        settingGuideSelection = false;
                    }
                    Game? game = await library.GetGameAsync(gameRoute.GameId);
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    if (game is null)
                    {
                        navigator.ResetToLibrary();
                        await RenderCurrentAsync();
                        ShellStatus.Text = "This game is no longer in your library.";
                        return false;
                    }
                    IReadOnlyList<Guide> guides =
                        await library.ListGuidesAsync(gameRoute.GameId);
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    GameHeading.Text = game.Title;
                    GamePlatform.Text = game.Platform ?? string.Empty;
                    GameNotes.Text = game.Notes ?? string.Empty;
                    GameMetadataSurface.Visibility =
                        game.Platform is null && game.Notes is null
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                    EditGameButton.IsEnabled = true;
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
                    GuideList.IsEnabled = true;
                    GameEmptyState.Visibility =
                        guides.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    GuideList.Visibility =
                        guides.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                    UpdateOpenSelectedGuideAction();
                    if (pendingGuideFocus is not null && selectedGuide is not null)
                    {
                        guideFocusRenderGeneration = generation;
                        GuideList.UpdateLayout();
                        GuideList.ScrollIntoView(selectedGuide);
                        GuideList.UpdateLayout();
                        TryRestoreGuideFocus();
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (guideFocusRenderGeneration == generation)
                            {
                                GuideList.UpdateLayout();
                                TryRestoreGuideFocus();
                            }
                        });
                    }
                    else
                    {
                        pendingGuideFocus = null;
                    }
                    ShellStatus.Text = "Game ready.";
                    break;

                case ReaderRoute readerRoute:
                    ReaderPanel.Visibility = Visibility.Visible;
                    ShellStatus.Text = "Loading guide…";
                    await PauseReaderMetadataReadForTestAsync();
                    Guide? guide = await library.GetGuideAsync(readerRoute.GuideId);
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    if (guide is null || guide.GameId != readerRoute.GameId)
                    {
                        navigator.ResetToLibrary();
                        await RenderCurrentAsync();
                        ShellStatus.Text = "This guide is no longer in your library.";
                        return false;
                    }
                    Game? readerGame = await library.GetGameAsync(readerRoute.GameId);
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    if (readerGame is null)
                    {
                        navigator.ResetToLibrary();
                        await RenderCurrentAsync();
                        ShellStatus.Text = "This game is no longer in your library.";
                        return false;
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
            return true;
        }
        catch (Exception error)
        {
            if (generation == renderGeneration)
            {
                ShellStatus.Text = $"Could not load this view: {error.Message}";
            }
            return false;
        }
    }

    private SqliteLibraryRepository RequireRepository() =>
        repository ?? throw new InvalidOperationException("The library is not ready.");
}
