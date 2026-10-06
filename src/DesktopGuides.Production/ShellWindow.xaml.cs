using DesktopGuides.Core.Html;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Navigation;
using DesktopGuides.Core.Providers;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Production.Materials;
using DesktopGuides.Production.Providers;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using System.Globalization;
using System.Runtime.InteropServices;
using CommunityToolkit.WinUI.Controls;
using Windows.Storage;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow : Window
{
    private readonly ShellNavigator navigator = new();
    private readonly NavigationActionQueue navigationQueue = new();
    private readonly CancellationTokenSource leaseWait = new();
    private readonly DispatcherQueueTimer statusDismissTimer;
    private readonly DispatcherQueueTimer librarySearchTimer;
    private IReadOnlyList<LibraryGameSummary>? librarySummaries;
    private string appliedLibraryQuery = string.Empty;
    private Task initializationTask = Task.CompletedTask;
    private LibrarySessionLease? libraryLease;
    private SqliteLibraryRepository? repository;
    private ProviderServices? providers;
    private ManagedArtworkStore? artwork;
    private const int RowArtworkDecodeWidth = 90;
    private const int DetailCoverDecodeWidth = 240;
    private readonly ArtworkListLoader gameArtwork;
    private ProviderGameImporter? importer;
    private Guid? resumeGuideId;
    private Guid? pendingGuideFocus;
    private int guideFocusRenderGeneration = -1;
    // Set by Back to the Library; the next Library render focuses the anchored row.
    private bool libraryFocusPending;
    private int libraryFocusGeneration = -1;
    private int renderGeneration;
    private HtmlGuideLoadAction readerErrorAction;
    private int readerErrorGeneration;
    private long gameGuideIntentVersion;
    private long statusSequence;
    private bool settingGuideSelection;
    private bool ready;
    private bool closeRequested;
    private bool allowClose;
    private bool gameEditorRequested;
    private bool applyingMaterialSelection;
    private GameEditorDialog? activeGameEditor;
    private AddGameDialog? activeAddGameDialog;
    private readonly GuideImportValidator importValidator = new();
    private GuideImportPublisher? guidePublisher;
    private bool importRequested;
    private ImportGuideDialog? activeImportDialog;
    private GuideRemover? guideRemover;
    private bool removeRequested;
    private ContentDialog? activeRemoveDialog;
    private GameRemover? gameRemover;
    private ManagedTextGuideLoader? textLoader;
    private ManagedHtmlGuideLoader? htmlLoader;
    private ManagedPdfGuideLoader? pdfLoader;
    private string? cacheRoot;
    private string? dataRoot;
    private CancellationTokenSource? readerLoad;
    private IReaderSession? readerSession;
    private bool gameRemoveRequested;
    // Null while the Game page loads, so Remove game stays disabled until the count is known.
    private int? loadedGameGuideCount;
    // The game whose details the card last showed; another game opens them at the top.
    private Guid? detailsGameId;
    private CancellationTokenSource? refreshCancel;
    private Task refreshTask = Task.CompletedTask;

    internal WindowMaterial EffectiveMaterial { get; private set; } = WindowMaterial.Solid;

    public ShellWindow()
    {
        InitializeComponent();
        Title = "Desktop Guides Preview";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        statusDismissTimer = DispatcherQueue.CreateTimer();
        statusDismissTimer.Interval = TimeSpan.FromSeconds(3);
        statusDismissTimer.IsRepeating = false;
        statusDismissTimer.Tick += (_, _) => ShellStatusInfoBar.IsOpen = false;
        librarySearchTimer = DispatcherQueue.CreateTimer();
        librarySearchTimer.Interval = TimeSpan.FromMilliseconds(200);
        librarySearchTimer.IsRepeating = false;
        librarySearchTimer.Tick += (_, _) =>
        {
            if (navigator.Current is LibraryRoute)
            {
                ApplyLibrarySearch(announce: true);
            }
        };
        InitializeThemeChoice();
        ApplyWindowMaterial(WindowMaterial.Mica);
        ShowTheme(ThemePreference.System);
        Navigation.SelectedItem = LibraryItem;
        gameArtwork = ArtworkListLoader.Attach(GameList, LoadRowArtworkAsync);
        GameList.AddHandler(
            UIElement.TappedEvent, new TappedEventHandler(GameTapped), true);
        GameList.AddHandler(
            UIElement.KeyDownEvent, new KeyEventHandler(GameKeyDown), true);
        GuideList.AddHandler(
            UIElement.TappedEvent, new TappedEventHandler(GuideTapped), true);
        GuideList.AddHandler(
            UIElement.KeyDownEvent, new KeyEventHandler(GuideKeyDown), true);
        ArtworkListLoader.NameRows(GuideList);
        GuideList.LayoutUpdated += GuideListLayoutUpdated;
        Navigation.RegisterPropertyChangedCallback(
            NavigationView.IsPaneOpenProperty, (_, _) => UpdatePaneStatus());
        UpdatePaneStatus();
        ReaderActions.CommandFailed += ShowErrorStatus;
        // After Ctrl+G the Reader content takes focus back (P2).
        ReaderActions.ContentFocusRequested += (_, _) =>
            (readerSession as PdfReaderSession)?.View.FocusPreview();
        GameCompletionChoice.CompletionRequested += CompletionChoiceRequested;
        ReaderCompletionChoice.CompletionRequested += CompletionChoiceRequested;
        AppWindow.Closing += WindowClosing;
        Activated += WindowActivated;
    }

    private void UpdatePaneStatus() =>
        AutomationProperties.SetItemStatus(
            Navigation,
            Navigation.IsPaneOpen ? "Navigation pane open" : "Navigation pane closed");

    private static string MaterialFallbackMessage(WindowMaterial requested) =>
        $"{requested} isn't available on this device. Using Solid.";

    private void ApplyWindowMaterial(WindowMaterial requested)
    {
        EffectiveMaterial = WindowMaterials.Resolve(requested);
        SystemBackdrop = WindowMaterials.CreateBackdrop(EffectiveMaterial);
        SolidCanvas.Visibility = EffectiveMaterial == WindowMaterial.Solid
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReaderActions.DialogMaterial = EffectiveMaterial;
        applyingMaterialSelection = true;
        WindowMaterialSelector.SelectedIndex = (int)requested;
        applyingMaterialSelection = false;
        AutomationProperties.SetItemStatus(
            WindowMaterialSelector, EffectiveMaterial.ToString());
        // The card keeps the fallback visible after the transient status is replaced.
        string description = EffectiveMaterial == requested
            ? "Choose how much of your desktop shows behind the app."
            : MaterialFallbackMessage(requested);
        WindowMaterialSettingsCard.Description = description;
        AutomationProperties.SetName(
            WindowMaterialSettingsCard, $"Window background. {description}");
    }

    private async void WindowMaterialSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (applyingMaterialSelection ||
            repository is null ||
            WindowMaterialSelector.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse(tag, out WindowMaterial requested))
        {
            return;
        }
        WindowMaterial previous = e.RemovedItems.FirstOrDefault() is ComboBoxItem { Tag: string old } &&
            Enum.TryParse(old, out WindowMaterial parsed) ? parsed : WindowMaterial.Mica;
        ApplyWindowMaterial(requested);
        try
        {
            await repository.UpdateSettingsAsync(s => s with { WindowMaterial = requested });
            if (EffectiveMaterial == requested)
            {
                ShowTransientStatus($"Window background set to {requested}.");
            }
            else
            {
                ShowWarningStatus(MaterialFallbackMessage(requested));
            }
        }
        catch (Exception error)
        {
            ApplyWindowMaterial(previous);
            ShowErrorStatus($"Could not save the window background: {error.Message}");
        }
    }

    private void ShowBusyStatus(string message) =>
        ShowStatus(message, InfoBarSeverity.Informational, false, false);

    private void ShowTransientStatus(string message) =>
        ShowStatus(message, InfoBarSeverity.Informational, false, true);

    private void ShowWarningStatus(string message) =>
        ShowStatus(message, InfoBarSeverity.Warning, true, false);

    private void ShowErrorStatus(string message) =>
        ShowStatus(message, InfoBarSeverity.Error, true, false);

    // Drops the busy status when the panel on screen already says what is wrong.
    private void HideStatus()
    {
        statusDismissTimer.Stop();
        ShellStatusInfoBar.IsOpen = false;
    }

    private void ShowStatus(
        string message,
        InfoBarSeverity severity,
        bool isClosable,
        bool autoDismiss)
    {
        statusDismissTimer.Stop();
        ShellStatusInfoBar.Message = message;
        AutomationProperties.SetName(ShellStatusInfoBar, message);
        AutomationProperties.SetItemStatus(
            ShellContent, $"{++statusSequence}|{message}");
        ShellStatusInfoBar.Severity = severity;
        ShellStatusInfoBar.IsClosable = isClosable;
        ShellStatusInfoBar.IsOpen = true;
        if (autoDismiss)
        {
            statusDismissTimer.Start();
        }
    }

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

    // The details card scrolls inside whatever height the guide list doesn't need.
    private void GamePageSizeChanged(object sender, SizeChangedEventArgs args) =>
        GameMetadataSurface.MaxHeight = GamePageLayout.DetailsMaxHeight(
            GamePanel.ActualHeight,
            GameHeader.ActualHeight,
            GameGuidesHeader.ActualHeight,
            GamePanel.RowSpacing);

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
            string dataRoot = AppDataRoot.Resolve(
                AppDataRoot.HasPackageIdentity(),
                () => ApplicationData.Current.LocalFolder.Path,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            this.dataRoot = dataRoot;
            ShowBusyStatus("Waiting for previous window...");
            libraryLease = await LibrarySessionLease.AcquireAsync(
                dataRoot, leaseWait.Token);
            ShowBusyStatus("Loading library...");
            ManagedPathResolver paths = new(dataRoot);
            repository = new SqliteLibraryRepository(paths);
            artwork = new ManagedArtworkStore(paths);
            await repository.InitializeAsync();
            StartProgress(repository);
            guidePublisher = new GuideImportPublisher(repository, paths);
            guideRemover = new GuideRemover(repository, paths);
            completion = new GuideCompletionService(repository, TimeProvider.System);
            gameRemover = new GameRemover(repository, paths, artwork);
            textLoader = new ManagedTextGuideLoader(paths);
            htmlLoader = new ManagedHtmlGuideLoader(repository, paths);
            pdfLoader = new ManagedPdfGuideLoader(paths);
            cacheRoot = AppCacheRoot.Resolve(
                AppDataRoot.HasPackageIdentity(),
                () => ApplicationData.Current.LocalCacheFolder.Path,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            // The library lease is held, so no live session owns a profile here.
            string sweptRoot = cacheRoot;
            await Task.Run(() => WebView2ProfileSweeper.Sweep(sweptRoot));
            providers = new ProviderServices(dataRoot);
            await ProviderSettings.InitializeAsync(providers);
            importer = providers.CreateImporter(repository, artwork);
            AppSettings? settings = null;
            try
            {
                settings = await repository.GetSettingsAsync();
            }
            catch (InvalidDataException)
            {
                // An invalid setting; RenderCurrentAsync reports it.
            }
            WindowMaterial requestedMaterial = settings?.WindowMaterial ?? WindowMaterial.Mica;
            ShowTheme(settings?.Theme ?? ThemePreference.System);
            ApplyWindowMaterial(requestedMaterial);
            WindowMaterialSelector.IsEnabled = true;
            AppThemeChoice.IsEnabled = true;
            ready = true;
            // Queued like every other render, so a quick first click can't be overwritten.
            await RunNavigationAsync(() => RenderCurrentAsync());
            if (EffectiveMaterial != requestedMaterial)
            {
                ShowWarningStatus(MaterialFallbackMessage(requestedMaterial));
            }
        }
        catch (OperationCanceledException) when (closeRequested)
        {
            // The waiting window was closed before it acquired the library.
        }
        catch (Exception error)
        {
            ready = false;
            ShowErrorStatus($"Could not open the library: {error.Message}");
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
        statusDismissTimer.Stop();
        activeGameEditor?.Hide();
        activeAddGameDialog?.Hide();
        activeImportDialog?.Hide();
        activeRemoveDialog?.Hide();
        refreshCancel?.Cancel();
        ProviderSettings.Cancel();
        leaseWait.Cancel();
        CancelReaderLoad();
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
            await Task.WhenAll(initializationTask, pendingNavigation, refreshTask);
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
                    await DisposeProgressTrackingAsync();
                    if (repository is not null)
                    {
                        await repository.DisposeAsync();
                    }
                    providers?.Dispose();
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
        CancelReaderLoad();
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

    private async void TitleBarBackRequested(TitleBar sender, object args)
    {
        CancelReaderLoad();
        await RunNavigationAsync(GoBackAsync);
    }

    private void TitleBarPaneToggleRequested(TitleBar sender, object args) =>
        Navigation.IsPaneOpen = !Navigation.IsPaneOpen;

    private async void ReaderBackClicked(object sender, RoutedEventArgs args)
    {
        CancelReaderLoad();
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
        libraryFocusPending = navigator.Current is LibraryRoute;
        await RenderCurrentAsync();
    }

    private async void GameSelected(object sender, SelectionChangedEventArgs args)
    {
        if (GameList.SelectedItem is LibraryGameItem item)
        {
            await RunNavigationAsync(() => OpenGameAsync(item.Game.Id));
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
            if (source is ListViewItem row && row.Content is LibraryGameItem item)
            {
                return item.Game;
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
        if (navigator.Current is GameRoute)
        {
            navigator.SetAnchor(SelectedGuide?.Id);
        }
        UpdateOpenSelectedGuideAction();
        if (SelectedGuide is Guide guide)
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

    private Guide? SelectedGuide => GuideAt(GuideList.SelectedItem);

    private static Guide? GuideAt(object? item) => (item as GuideRowItem)?.Guide;

    private Guide? GuideFromRow(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, GuideList))
        {
            if (source is Button)
            {
                return null;
            }
            if (source is ListViewItem row && GuideAt(row.Content) is Guide guide)
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
        if (SelectedGuide is Guide guide)
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

    private void UpdateRemoveGameAction()
    {
        RemoveGameButton.IsEnabled = loadedGameGuideCount is not null && !closeRequested &&
            !importRequested && !gameEditorRequested && !removeRequested &&
            !gameRemoveRequested && refreshCancel is null;
    }

    private void UpdateOpenSelectedGuideAction()
    {
        if (GuideList.IsEnabled &&
            navigator.Current is GameRoute game &&
            SelectedGuide is Guide guide &&
            guide.GameId == game.GameId)
        {
            AutomationProperties.SetName(
                OpenSelectedGuideButton, $"Open {guide.Title}");
            AutomationProperties.SetName(
                RemoveSelectedGuideButton, $"Remove {guide.Title}");
            OpenSelectedGuideButton.Visibility = Visibility.Visible;
            RemoveSelectedGuideButton.Visibility = Visibility.Visible;
            if (GuideList.SelectedItem is GuideRowItem row)
            {
                GameCompletionChoice.Show(guide.Id, guide.Title,
                    GuideCompletionPresentation.IsComplete(row.CompletedUtc));
            }
        }
        else
        {
            OpenSelectedGuideButton.Visibility = Visibility.Collapsed;
            RemoveSelectedGuideButton.Visibility = Visibility.Collapsed;
            GameCompletionChoice.Hide();
        }
    }

    private void GuideListLayoutUpdated(object? sender, object args) =>
        TryRestoreGuideFocus();

    private void TryRestoreGuideFocus()
    {
        if (pendingGuideFocus is not Guid guideId ||
            guideFocusRenderGeneration != renderGeneration ||
            navigator.Current is not GameRoute game ||
            SelectedGuide is not Guide selected ||
            selected.Id != guideId ||
            selected.GameId != game.GameId ||
            GuideList.ContainerFromItem(GuideList.SelectedItem) is not Control container)
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

    private void RestoreLibraryFocus(int generation)
    {
        libraryFocusGeneration = generation;
        GameList.UpdateLayout();
        TryRestoreLibraryFocus();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (libraryFocusGeneration == generation)
            {
                GameList.UpdateLayout();
                TryRestoreLibraryFocus();
            }
        });
    }

    // Focuses the anchored row, or the first row when the query hides it.
    // Never sets SelectedItem: selecting a Library row opens the game.
    private void TryRestoreLibraryFocus()
    {
        if (libraryFocusGeneration != renderGeneration || navigator.Current is not LibraryRoute)
        {
            return;
        }
        List<LibraryGameItem> rows = [.. GameList.Items.OfType<LibraryGameItem>()];
        if (rows.Count == 0)
        {
            libraryFocusGeneration = -1;
            Control fallback = LibrarySearchInput.IsEnabled ? LibrarySearchInput : AddGameButton;
            fallback.Focus(FocusState.Programmatic);
            return;
        }
        LibraryGameItem target = rows.FirstOrDefault(row => row.Game.Id == navigator.CurrentAnchor) ?? rows[0];
        GameList.ScrollIntoView(target);
        GameList.UpdateLayout();
        // Focus can cause another layout pass. Suspend the callback while it runs.
        libraryFocusGeneration = -1;
        if (GameList.ContainerFromItem(target) is not Control container ||
            !container.Focus(FocusState.Programmatic))
        {
            libraryFocusGeneration = renderGeneration;
        }
    }

    private static bool FocusIsWithin(UIElement container)
    {
        if (container.XamlRoot is null)
        {
            return false;
        }
        for (DependencyObject? element = FocusManager.GetFocusedElement(container.XamlRoot) as DependencyObject;
            element is not null;
            element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, container))
            {
                return true;
            }
        }
        return false;
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
                if (importer is null || providers is null ||
                    !await providers.HasIgdbCredentialsAsync(CancellationToken.None))
                {
                    await ShowManualAddAsync(ProviderMessages.ForIgdb(ProviderErrorKind.NotConfigured));
                    return;
                }
                AddGameDialog search = new(providers.Igdb, importer, providers.Thumbnails)
                {
                    XamlRoot = Navigation.XamlRoot
                };
                DialogSurface.Apply(search, EffectiveMaterial);
                activeAddGameDialog = search;
                try
                {
                    await search.ShowAsync();
                }
                finally
                {
                    activeAddGameDialog = null;
                }
                if (closeRequested)
                {
                    return;
                }
                switch (search.Outcome)
                {
                    case AddGameOutcome.OpenGame when search.Added is { } added:
                        navigator.OpenGame(added.Game.Id);
                        await RenderCurrentAsync();
                        ImportGuideButton.Focus(FocusState.Programmatic);
                        if (added.AlreadyInLibrary)
                        {
                            ShowTransientStatus($"{added.Game.Title} is already in your library.");
                        }
                        else if (added.ArtworkMissing)
                        {
                            ShowWarningStatus("Game added. Its cover couldn't be downloaded.");
                        }
                        break;
                    case AddGameOutcome.AddManually:
                        await ShowManualAddAsync(null);
                        break;
                    case AddGameOutcome.OpenSettings:
                        await OpenSettingsFromDialogAsync();
                        break;
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

    private async Task ShowManualAddAsync(string? notice)
    {
        Game? created = null;
        GameEditorDialog editor = new(null, async details =>
        {
            created = await RequireRepository().AddGameAsync(
                details.Title, details.Platform, details.Notes);
        }, notice)
        {
            XamlRoot = Navigation.XamlRoot
        };
        DialogSurface.Apply(editor, EffectiveMaterial);
        activeGameEditor = editor;
        ContentDialogResult result;
        try
        {
            result = await editor.ShowAsync();
        }
        finally
        {
            activeGameEditor = null;
        }
        if (closeRequested)
        {
            return;
        }
        if (result == ContentDialogResult.Primary && created is not null)
        {
            navigator.OpenGame(created.Id);
            await RenderCurrentAsync();
            ImportGuideButton.Focus(FocusState.Programmatic);
        }
        else if (editor.OpenSettingsRequested)
        {
            await OpenSettingsFromDialogAsync();
        }
    }

    private async Task OpenSettingsFromDialogAsync()
    {
        navigator.OpenSettings();
        await RenderCurrentAsync();
        ProviderSettings.Expand();
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
                    ShowWarningStatus("This game is no longer in your library.");
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
                DialogSurface.Apply(editor, EffectiveMaterial);
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
            if (!closeRequested && navigator.Current is GameRoute shown)
            {
                EditGameButton.IsEnabled = true;
                // The button was disabled while the dialog was open, so WinUI could not return focus to it.
                if (shown.GameId == route.GameId)
                {
                    EditGameButton.Focus(FocusState.Programmatic);
                }
            }
            if (!closeRequested)
            {
                UpdateRemoveGameAction();
            }
        }
    }

    private async void ImportGuideClicked(object sender, RoutedEventArgs args)
    {
        if (importRequested || closeRequested || navigator.Current is not GameRoute route)
        {
            return;
        }
        importRequested = true;
        ImportGuideButton.IsEnabled = false;
        bool imported = false;
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
                    ShowWarningStatus("This game is no longer in your library.");
                    return;
                }
                string? path;
                try
                {
                    path = await PickGuideFileAsync();
                }
                catch (Exception)
                {
                    ShowWarningStatus(ImportGuideDialog.PickerFailedMessage);
                    return;
                }
                if (path is null || closeRequested)
                {
                    return;
                }
                GuideImportPublisher publisher = guidePublisher
                    ?? throw new InvalidOperationException("The library is not ready.");
                SqliteLibraryRepository library = RequireRepository();
                ImportGuideDialog dialog = new(
                    game.Title, path, importValidator, PickGuideFileAsync,
                    (manifest, token) => library.FindGuideByFingerprintAsync(
                        game.Id, manifest.Format, manifest.Fingerprint, token),
                    (manifest, title, allowDuplicate, progress, token) =>
                        publisher.PublishAsync(manifest, game.Id, title, allowDuplicate, progress, token))
                {
                    XamlRoot = Navigation.XamlRoot
                };
                DialogSurface.Apply(dialog, EffectiveMaterial);
                activeImportDialog = dialog;
                try
                {
                    await dialog.ShowAsync();
                }
                finally
                {
                    activeImportDialog = null;
                }
                if (!closeRequested && navigator.Current is GameRoute shown && shown.GameId == route.GameId)
                {
                    // An import that reached publication is kept even if the dialog was closed.
                    if (dialog.ImportedGuideId is Guid guideId)
                    {
                        imported = true;
                        pendingGuideFocus = guideId;
                        await RenderCurrentAsync();
                    }
                    else if (dialog.OpenGuideId is Guid existingId)
                    {
                        // Already inside the navigation queue, so open directly.
                        await OpenGuideAsync(existingId, route.GameId);
                    }
                }
            });
        }
        finally
        {
            importRequested = false;
            if (!closeRequested && navigator.Current is GameRoute)
            {
                ImportGuideButton.IsEnabled = true;
                if (!imported)
                {
                    ImportGuideButton.Focus(FocusState.Programmatic);
                }
            }
            if (!closeRequested)
            {
                UpdateRemoveGameAction();
            }
        }
    }

    private async void RemoveSelectedGuideClicked(object sender, RoutedEventArgs args)
    {
        if (removeRequested || closeRequested ||
            navigator.Current is not GameRoute route ||
            SelectedGuide is not Guide guide ||
            guide.GameId != route.GameId)
        {
            return;
        }
        removeRequested = true;
        GuideList.IsEnabled = false;
        OpenSelectedGuideButton.IsEnabled = false;
        RemoveSelectedGuideButton.IsEnabled = false;
        ImportGuideButton.IsEnabled = false;
        bool rendered = false;
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
                GuideRemover remover = guideRemover
                    ?? throw new InvalidOperationException("The library is not ready.");
                GuideRemovalPreview? preview;
                try
                {
                    preview = await remover.DescribeAsync(guide.Id);
                }
                catch (Exception error)
                {
                    ShowRemovalError(error, guide.Title);
                    return;
                }
                if (closeRequested)
                {
                    return;
                }
                if (preview is null)
                {
                    rendered = true;
                    // The render resolves the removed guide to its nearest survivor.
                    pendingGuideFocus = guide.Id;
                    await RenderCurrentAsync();
                    ShowTransientStatus(GuideRemovalPresentation.AlreadyRemoved(guide.Title));
                    return;
                }
                ContentDialog dialog = RemoveGuideDialog.Create(preview, Navigation.XamlRoot);
                DialogSurface.Apply(dialog, EffectiveMaterial);
                activeRemoveDialog = dialog;
                ContentDialogResult choice;
                try
                {
                    choice = await dialog.ShowAsync();
                }
                finally
                {
                    activeRemoveDialog = null;
                }
                if (choice != ContentDialogResult.Primary || closeRequested)
                {
                    return;
                }
                GuideRemovalResult result;
                try
                {
                    result = await remover.RemoveAsync(guide.Id);
                }
                catch (Exception error)
                {
                    ShowRemovalError(error, guide.Title);
                    return;
                }
                if (!closeRequested && navigator.Current is GameRoute shown && shown.GameId == route.GameId)
                {
                    rendered = true;
                    // The render resolves the removed guide to its nearest survivor.
                    pendingGuideFocus = guide.Id;
                    await RenderCurrentAsync();
                }
                ShowTransientStatus(result.Outcome == GuideRemovalOutcome.NotFound
                    ? GuideRemovalPresentation.AlreadyRemoved(guide.Title)
                    : GuideRemovalPresentation.Removed(guide.Title, result.CleanupPending));
            });
        }
        finally
        {
            removeRequested = false;
            OpenSelectedGuideButton.IsEnabled = true;
            RemoveSelectedGuideButton.IsEnabled = true;
            if (!closeRequested && navigator.Current is GameRoute shown && shown.GameId == route.GameId)
            {
                if (!rendered)
                {
                    GuideList.IsEnabled = true;
                    ImportGuideButton.IsEnabled = !importRequested;
                    UpdateOpenSelectedGuideAction();
                    RemoveSelectedGuideButton.Focus(FocusState.Programmatic);
                }
                else if (GuideList.Items.Count == 0)
                {
                    ImportGuideButton.Focus(FocusState.Programmatic);
                }
            }
            if (!closeRequested)
            {
                UpdateRemoveGameAction();
            }
        }
    }

    private void ShowRemovalError(Exception error, string title)
    {
        if (!closeRequested)
        {
            ShowErrorStatus(GuideRemovalPresentation.Error(
                error is GuideRemovalException removal ? removal.Issue : GuideRemovalIssue.Failed, title));
        }
    }

    private async void RemoveGameClicked(object sender, RoutedEventArgs args)
    {
        if (gameRemoveRequested || closeRequested || navigator.Current is not GameRoute route)
        {
            return;
        }
        gameRemoveRequested = true;
        string shownTitle = GameHeading.Text;
        EditGameButton.IsEnabled = false;
        RefreshMetadataButton.IsEnabled = false;
        ImportGuideButton.IsEnabled = false;
        RemoveGameButton.IsEnabled = false;
        bool rendered = false;
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
                GameRemover remover = gameRemover
                    ?? throw new InvalidOperationException("The library is not ready.");
                GameRemovalPreview? preview;
                try
                {
                    preview = await remover.DescribeAsync(route.GameId);
                }
                catch (Exception error)
                {
                    if (!closeRequested)
                    {
                        ShowErrorStatus(GameRemovalError(error, shownTitle));
                    }
                    return;
                }
                if (closeRequested)
                {
                    return;
                }
                // A null preview means it was removed elsewhere before the dialog (T04.2 ruling 14).
                string title = preview?.Title ?? shownTitle;
                GameRemovalResult? result = null;
                bool countChanged = false;
                while (preview is not null)
                {
                    title = preview.Title;
                    ContentDialog dialog = RemoveGameDialog.Create(preview, countChanged, Navigation.XamlRoot);
                    DialogSurface.Apply(dialog, EffectiveMaterial);
                    activeRemoveDialog = dialog;
                    ContentDialogResult choice;
                    try
                    {
                        choice = await dialog.ShowAsync();
                    }
                    finally
                    {
                        activeRemoveDialog = null;
                    }
                    if (choice != ContentDialogResult.Primary || closeRequested)
                    {
                        return;
                    }
                    try
                    {
                        result = await remover.RemoveAsync(preview.GameId, preview.GuideCount);
                    }
                    catch (Exception error)
                    {
                        // Nothing changed in the database, so the page still shows the game.
                        if (!closeRequested)
                        {
                            ShowErrorStatus(GameRemovalError(error, title));
                        }
                        return;
                    }
                    if (closeRequested)
                    {
                        return;
                    }
                    if (result.Outcome != GameRemovalOutcome.CountChanged)
                    {
                        break;
                    }
                    // Show the dialog again with the fresh counts.
                    preview = result.Current!;
                    countChanged = true;
                }
                rendered = true;
                // Clearing the back stack keeps Back from reaching the removed page.
                navigator.ResetToLibrary();
                await RenderCurrentAsync();
                ShowTransientStatus(result is { Outcome: GameRemovalOutcome.Removed }
                    ? GameRemovalPresentation.Removed(title, result.CleanupPending)
                    : GameRemovalPresentation.AlreadyRemoved(title));
                AddGameButton.Focus(FocusState.Programmatic);
            });
        }
        finally
        {
            gameRemoveRequested = false;
            if (!closeRequested)
            {
                bool restore = !rendered &&
                    navigator.Current is GameRoute shown && shown.GameId == route.GameId;
                if (restore)
                {
                    EditGameButton.IsEnabled = true;
                    ImportGuideButton.IsEnabled = !importRequested;
                    RefreshMetadataButton.IsEnabled = refreshCancel is null;
                }
                UpdateRemoveGameAction();
                if (restore)
                {
                    RemoveGameButton.Focus(FocusState.Programmatic);
                }
            }
        }
    }

    private static string GameRemovalError(Exception error, string title) =>
        GameRemovalPresentation.Error(
            error is GameRemovalException removal ? removal.Issue : GameRemovalIssue.Failed, title);

    private async Task<string?> PickGuideFileAsync()
    {
        FileOpenPicker picker = new(AppWindow.Id);
        foreach (string type in (string[])[".txt", ".html", ".htm", ".pdf"])
        {
            picker.FileTypeFilter.Add(type);
        }
        PickFileResult? result = await picker.PickSingleFileAsync();
        return result?.Path;
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
            ShowErrorStatus($"Could not open the game: {error.Message}");
        }
    }

    private bool IsSupersededGameGuideIntent(long? intentVersion) =>
        intentVersion is long version && version != gameGuideIntentVersion;

    private void ShowReaderSurface(
        bool placeholder, string? error = null, UIElement? view = null,
        HtmlGuideLoadAction action = HtmlGuideLoadAction.None)
    {
        ReaderPlaceholder.Visibility = placeholder ? Visibility.Visible : Visibility.Collapsed;
        ReaderLoadError.Text = error ?? string.Empty;
        ReaderLoadError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        readerErrorAction = error is null ? HtmlGuideLoadAction.None : action;
        readerErrorGeneration = renderGeneration;
        ReaderLoadErrorAction.Content = readerErrorAction == HtmlGuideLoadAction.None
            ? null
            : HtmlGuideLoadMessages.ActionLabel(readerErrorAction);
        ReaderLoadErrorAction.Visibility = readerErrorAction == HtmlGuideLoadAction.None
            ? Visibility.Collapsed
            : Visibility.Visible;
        PdfUnlockPanel.Visibility = Visibility.Collapsed;
        PdfUnlockError.Visibility = Visibility.Collapsed;
        PdfPasswordInput.Password = string.Empty;
        ReaderSurface.Content = view;
        ReaderSurface.Visibility = view is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // A render holds the navigation queue while its TXT guide loads, so a
    // request that leaves the Reader cancels the load before it queues.
    private void CancelReaderLoad() => readerLoad?.Cancel();

    // Every render closes the Reader: a load in flight is cancelled, and the
    // toolbar and surface drop the old session before it is disposed.
    private async Task CloseReaderSessionAsync()
    {
        await DisposeProgressTrackingAsync();
        HideExternalLinkBar();
        HideUnavailableLinkBar();
        readerLoad?.Cancel();
        readerLoad?.Dispose();
        readerLoad = null;
        ReaderActions.SetSession(null);
        ShowReaderSurface(placeholder: true);
        IReaderSession? closing = readerSession;
        readerSession = null;
        if (closing is not null)
        {
            await closing.DisposeAsync();
        }
    }

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

    // Holds a TXT load until the installed test continues it or the load is cancelled.
    private static async Task PauseTextLoadForTestAsync(CancellationToken token)
    {
        string prefix = $@"Local\DesktopGuides.Preview.TextLoad.{Environment.ProcessId}";
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
                    int signaled = await Task.Run(
                        () => WaitHandle.WaitAny([resume, token.WaitHandle], 30_000));
                    token.ThrowIfCancellationRequested();
                    if (signaled == WaitHandle.WaitTimeout)
                    {
                        throw new TimeoutException("TXT load test gate timed out.");
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
        ShowBusyStatus("Opening guide...");
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
                ShowWarningStatus("This guide is no longer in your library.");
                return;
            }
            Game? ownerGame = await library.GetGameAsync(guide.GameId);
            if (IsSupersededGameGuideIntent(intentVersion))
            {
                return;
            }
            if (ownerGame is null)
            {
                ShowWarningStatus("This guide is no longer in your library.");
                return;
            }
            await library.GetSettingsAsync();
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
                    await library.UpdateSettingsAsync(
                        s => s with { LastActiveGuideId = guide.Id });
                }
                catch (Exception error)
                {
                    ShowErrorStatus($"Could not save Resume: {error.Message}");
                }
            }
        }
        catch (Exception error)
        {
            ShowErrorStatus($"Could not open the guide: {error.Message}");
        }
    }

    private async Task<bool> RenderCurrentAsync()
    {
        int generation = ++renderGeneration;
        await CloseReaderSessionAsync();
        if (generation != renderGeneration)
        {
            return false;
        }
        guideFocusRenderGeneration = -1;
        bool restoreLibraryFocus = libraryFocusPending;
        libraryFocusPending = false;
        libraryFocusGeneration = -1;
        if (navigator.Current is not GameRoute)
        {
            pendingGuideFocus = null;
        }
        LibraryPanel.Visibility = Visibility.Collapsed;
        GamePanel.Visibility = Visibility.Collapsed;
        ReaderPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        OpenSelectedGuideButton.Visibility = Visibility.Collapsed;
        RemoveSelectedGuideButton.Visibility = Visibility.Collapsed;
        GameCompletionChoice.Hide();
        ReaderCompletionChoice.Hide();
        loadedGameGuideCount = null;
        UpdateRemoveGameAction();
        AppTitleBar.IsBackButtonEnabled = navigator.CanGoBack;
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
                    librarySearchTimer.Stop();
                    if (librarySummaries is null)
                    {
                        ShowLibraryView(LibraryView.Loading);
                    }
                    ShowBusyStatus("Loading library…");
                    IReadOnlyList<LibraryGameSummary> games = await library.ListGameSummariesAsync();
                    AppSettings settings = await library.GetSettingsAsync();
                    Guide? resume = settings.LastActiveGuideId is Guid lastId
                        ? await library.GetGuideAsync(lastId)
                        : null;
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    librarySummaries = games;
                    ApplyLibrarySearch(announce: false);
                    resumeGuideId = resume?.Id;
                    ResumeButton.Visibility =
                        resume is null ? Visibility.Collapsed : Visibility.Visible;
                    if (resume is not null)
                    {
                        ResumeButton.Content = $"Resume {resume.Title}";
                    }
                    if (restoreLibraryFocus)
                    {
                        RestoreLibraryFocus(generation);
                    }
                    ShowTransientStatus("Library ready.");
                    break;

                case GameRoute gameRoute:
                    GamePanel.Visibility = Visibility.Visible;
                    ShowBusyStatus("Loading game…");
                    // The rows shown before this render, if they belong to this game;
                    // ListAnchor.Resolve uses them to find a removed guide's survivor.
                    bool sameGameList = detailsGameId == gameRoute.GameId;
                    List<Guid> shownGuideIds = sameGameList
                        ? [.. GuideList.Items.OfType<GuideRowItem>().Select(item => item.Guide.Id)]
                        : [];
                    Guid? wantedGuideId = pendingGuideFocus ?? navigator.CurrentAnchor ??
                        (sameGameList ? SelectedGuide?.Id : null);
                    GameHeading.Text = "Loading game…";
                    GamePlatform.Text = string.Empty;
                    GameNotes.Text = string.Empty;
                    ClearProviderMetadata();
                    GameMetadataSurface.Visibility = Visibility.Collapsed;
                    EditGameButton.IsEnabled = false;
                    ImportGuideButton.IsEnabled = false;
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
                        ShowWarningStatus("This game is no longer in your library.");
                        return false;
                    }
                    IReadOnlyList<GuideRowItem> guides =
                        (await library.ListGuideSummariesAsync(gameRoute.GameId))
                        .Select(summary => new GuideRowItem(summary, TimeProvider.System, CultureInfo.CurrentCulture))
                        .ToList();
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    GameHeading.Text = game.Title;
                    GamePlatform.Text = game.Platform ?? string.Empty;
                    GamePlatform.Visibility = game.Platform is null ? Visibility.Collapsed : Visibility.Visible;
                    GameNotes.Text = game.Notes ?? string.Empty;
                    GameNotesScroll.Visibility = game.Notes is null ? Visibility.Collapsed : Visibility.Visible;
                    ImageSource? cover = await LoadCoverAsync(game.ArtworkRelativePath, DetailCoverDecodeWidth);
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    ShowProviderMetadata(game, cover);
                    GameMetadataSurface.Visibility =
                        game.Platform is null && game.Notes is null &&
                        game.Metadata is null && cover is null
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                    if (detailsGameId != game.Id)
                    {
                        GameMetadataScroll.ChangeView(null, 0, null, disableAnimation: true);
                        detailsGameId = game.Id;
                    }
                    EditGameButton.IsEnabled = true;
                    ImportGuideButton.IsEnabled = !importRequested;
                    loadedGameGuideCount = guides.Count;
                    UpdateRemoveGameAction();
                    Guid? selectedGuideId = ListAnchor.Resolve(
                        shownGuideIds, [.. guides.Select(item => item.Guide.Id)], wantedGuideId);
                    GuideRowItem? selectedGuide = selectedGuideId is Guid id
                        ? guides.First(item => item.Guide.Id == id)
                        : null;
                    if (pendingGuideFocus is not null)
                    {
                        // Focus follows the survivor when the wanted guide is gone.
                        pendingGuideFocus = selectedGuide?.Guide.Id;
                    }
                    if (navigator.Current == gameRoute)
                    {
                        navigator.SetAnchor(selectedGuide?.Guide.Id);
                    }
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
                    ShowTransientStatus("Game ready.");
                    break;

                case ReaderRoute readerRoute:
                    ReaderPanel.Visibility = Visibility.Visible;
                    ShowBusyStatus("Loading guide…");
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
                        ShowWarningStatus("This guide is no longer in your library.");
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
                        ShowWarningStatus("This game is no longer in your library.");
                        return false;
                    }
                    ReaderHeading.Text = guide.Title;
                    ReaderGameName.Text = readerGame.Title;
                    ReaderFormat.Text = guide.Format.ToString().ToUpperInvariant();
                    // Shown before the content opens, so it stays usable when a load fails.
                    ReadingState? readingState = await library.GetReadingStateAsync(guide.Id);
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    ReaderCompletionChoice.Show(guide.Id, guide.Title,
                        GuideCompletionPresentation.IsComplete(readingState?.CompletedUtc));
                    if (guide.Format == GuideFormat.Html)
                    {
                        if (!await OpenHtmlGuideAsync(guide, generation))
                        {
                            return false;
                        }
                        break;
                    }
                    if (guide.Format == GuideFormat.Pdf)
                    {
                        if (!await OpenPdfGuideAsync(guide, generation))
                        {
                            return false;
                        }
                        break;
                    }
                    ShowReaderSurface(placeholder: false);
                    readerLoad = new CancellationTokenSource();
                    CancellationToken readerToken = readerLoad.Token;
                    TextGuideLoad textLoad;
                    try
                    {
                        await PauseTextLoadForTestAsync(readerToken);
                        textLoad = await textLoader!.LoadAsync(guide, readerToken);
                    }
                    catch (OperationCanceledException) when (readerToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    if (textLoad is TextGuideLoadFailed failed)
                    {
                        string message = TextGuideLoadMessages.For(failed.Error);
                        ShowReaderSurface(placeholder: false, error: message);
                        ShowWarningStatus(message);
                        break;
                    }
                    // The restore compares the decoded document's hash; a changed file shows as it is.
                    TextGuideDocument document = ((TextGuideLoaded)textLoad).Document;
                    int maxColumns;
                    try
                    {
                        maxColumns = await Task.Run(
                            () => TextLineMetrics.MaxColumns(document, readerToken), readerToken);
                    }
                    catch (OperationCanceledException) when (readerToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    TextReaderSession session = new(document, maxColumns);
                    readerSession = session;
                    ShowReaderSurface(placeholder: false, view: session.View);
                    ReaderActions.SetSession(session);
                    if (!await OpenAtSavedPlaceAsync(
                        guide, session, generation, document.ContentSha256.ToLowerInvariant(),
                        null, readerToken))
                    {
                        return false;
                    }
                    break;

                case SettingsRoute:
                    SettingsPanel.Visibility = Visibility.Visible;
                    await ProviderSettings.ReloadIfUnreadableAsync();
                    ShowTransientStatus("Settings ready.");
                    break;
            }
            return true;
        }
        catch (Exception error)
        {
            if (generation == renderGeneration)
            {
                LibraryLoadingState.Visibility = Visibility.Collapsed;
                LibraryProgress.IsActive = false;
                ShowErrorStatus($"Could not load this view: {error.Message}");
            }
            return false;
        }
    }

    private enum LibraryView { Loading, Empty, NoResults, List }

    // Exactly one Library view is visible. Search is usable only when there
    // are games to search.
    private void ShowLibraryView(LibraryView view)
    {
        LibraryLoadingState.Visibility = view == LibraryView.Loading ? Visibility.Visible : Visibility.Collapsed;
        LibraryProgress.IsActive = view == LibraryView.Loading;
        LibraryEmptyState.Visibility = view == LibraryView.Empty ? Visibility.Visible : Visibility.Collapsed;
        LibraryNoResultsState.Visibility = view == LibraryView.NoResults ? Visibility.Visible : Visibility.Collapsed;
        GameList.Visibility = view == LibraryView.List ? Visibility.Visible : Visibility.Collapsed;
        bool searchable = view is LibraryView.NoResults or LibraryView.List;
        LibrarySearchInput.IsEnabled = searchable;
        LibrarySearchClear.Visibility = searchable && appliedLibraryQuery.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // Filters the cached summaries in memory; never reads SQLite or guide files.
    private void ApplyLibrarySearch(bool announce)
    {
        librarySearchTimer.Stop();
        if (librarySummaries is null)
        {
            return;
        }

        appliedLibraryQuery = LibrarySearchInput.Text.Trim();
        IReadOnlyList<LibrarySearchMatch> matches = LibrarySearch.Filter(librarySummaries, appliedLibraryQuery);
        gameArtwork.CancelAll();
        GameList.ItemsSource = matches.Select(match => new LibraryGameItem(match)).ToList();
        LibraryNoResults.Text = $"No games or guides match \"{appliedLibraryQuery}\".";
        ShowLibraryView(
            librarySummaries.Count == 0 ? LibraryView.Empty
            : matches.Count == 0 ? LibraryView.NoResults
            : LibraryView.List);
        if (announce && appliedLibraryQuery.Length > 0)
        {
            ShowTransientStatus(matches.Count == 0
                ? "No games match."
                : $"{matches.Count} of {librarySummaries.Count} games match.");
        }
    }

    // Every change restarts the debounce; an unchanged query is skipped.
    private void LibrarySearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        librarySearchTimer.Stop();
        if (sender.Text.Trim() != appliedLibraryQuery)
        {
            librarySearchTimer.Start();
        }
    }

    private void LibrarySearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        ApplyLibrarySearch(announce: true);

    private void LibrarySearchClearClicked(object sender, RoutedEventArgs e)
    {
        LibrarySearchInput.Text = string.Empty;
        ApplyLibrarySearch(announce: true);
        LibrarySearchInput.Focus(FocusState.Programmatic);
    }

    private void ClearProviderMetadata()
    {
        GameCover.Source = null;
        GameCoverFrame.Visibility = Visibility.Collapsed;
        GameProviderDetails.Visibility = Visibility.Collapsed;
        GameProviderLink.Visibility = Visibility.Collapsed;
        RefreshMetadataButton.Visibility = Visibility.Collapsed;
    }

    // Reads the managed file through a stream, so artwork never triggers a
    // network request and a damaged or missing file only leaves the placeholder.
    private async Task<ImageSource?> LoadCoverAsync(
        string? relativePath, int decodeWidth, CancellationToken token = default)
    {
        if (relativePath is null || artwork?.ResolveFile(relativePath) is not { } path)
        {
            return null;
        }
        try
        {
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            token.ThrowIfCancellationRequested();
            BitmapImage bitmap = new() { DecodePixelWidth = decodeWidth };
            await bitmap.SetSourceAsync(file.AsRandomAccessStream());
            token.ThrowIfCancellationRequested();
            return bitmap;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException)
        {
            return null;
        }
    }

    private Task<ImageSource?> LoadRowArtworkAsync(ArtworkItem item, CancellationToken token) =>
        item is LibraryGameItem game
            ? LoadCoverAsync(game.ArtworkRelativePath, RowArtworkDecodeWidth, token)
            : Task.FromResult<ImageSource?>(null);

    private void ShowProviderMetadata(Game game, ImageSource? cover)
    {
        GameCover.Source = cover;
        GameCoverFrame.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshMetadataButton.Visibility = game.Link is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshMetadataButton.IsEnabled = refreshCancel is null;
        if (game.Metadata is not { } metadata)
        {
            GameProviderDetails.Visibility = Visibility.Collapsed;
            return;
        }
        List<MetadataItem> facts = [];
        if (metadata.FirstReleaseDate is { } released)
        {
            string year = released.Year.ToString(CultureInfo.InvariantCulture);
            facts.Add(new MetadataItem { Label = year, AccessibleLabel = $"Released {year}" });
        }
        facts.Add(new MetadataItem { Label = GameMetadataPresentation.TypeLabel(metadata.Type) });
        if (GameMetadataPresentation.PlatformSummary(metadata.Platforms) is { } platforms)
        {
            facts.Add(new MetadataItem { Label = platforms, AccessibleLabel = $"Platforms: {platforms}" });
        }
        GameFacts.Items = facts;
        SetOptionalText(GameSummary, metadata.Summary);
        SetOptionalText(GameGenres,
            metadata.Genres.Count == 0 ? null : $"Genres: {string.Join(", ", metadata.Genres)}");
        SetOptionalText(GameCompanies, GameMetadataPresentation.Companies(metadata));
        GameAttribution.Text = string.Join(". ", GameMetadataPresentation.Attribution(metadata)) + ".";
        if (GameMetadataNormalizer.NormalizeProviderUrl(metadata.ProviderUrl) is { } url)
        {
            GameProviderLink.NavigateUri = new Uri(url);
            GameProviderLink.Visibility = Visibility.Visible;
        }
        else
        {
            GameProviderLink.Visibility = Visibility.Collapsed;
        }
        GameProviderDetails.Visibility = Visibility.Visible;
    }

    private static void SetOptionalText(TextBlock block, string? text)
    {
        block.Text = text ?? string.Empty;
        block.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshMetadataClicked(object sender, RoutedEventArgs args)
    {
        if (closeRequested || refreshCancel is not null || importer is null ||
            navigator.Current is not GameRoute route)
        {
            return;
        }
        refreshTask = RefreshMetadataAsync(route.GameId, importer);
    }

    private async Task RefreshMetadataAsync(Guid gameId, ProviderGameImporter refresher)
    {
        using CancellationTokenSource cancel = new();
        refreshCancel = cancel;
        RefreshMetadataButton.IsEnabled = false;
        UpdateRemoveGameAction();
        ShowBusyStatus("Refreshing metadata…");
        ProviderRefreshResult? result = null;
        string? error = null;
        try
        {
            result = await refresher.RefreshAsync(gameId, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (ProviderException failure)
        {
            error = ProviderMessages.ForIgdb(failure.Kind);
        }
        catch (KeyNotFoundException)
        {
            error = "This game is no longer in your library.";
        }
        catch (Exception failure)
        {
            error = $"Could not refresh metadata: {failure.Message}";
        }
        finally
        {
            refreshCancel = null;
            // Re-enable here: the user may have moved to another game, which won't re-render.
            if (!closeRequested)
            {
                RefreshMetadataButton.IsEnabled = true;
                UpdateRemoveGameAction();
            }
        }
        if (closeRequested)
        {
            return;
        }
        await RunNavigationAsync(async () =>
        {
            // The person left this game: no re-render, no status on another page.
            if (navigator.Current is not GameRoute current || current.GameId != gameId)
            {
                return;
            }
            if (FocusIsWithin(GuideList))
            {
                pendingGuideFocus = navigator.CurrentAnchor;
            }
            await RenderCurrentAsync();
            if (error is not null)
            {
                ShowErrorStatus(error);
            }
            else if (result!.ArtworkMissing)
            {
                ShowWarningStatus("Metadata refreshed. A new cover couldn't be downloaded.");
            }
            else
            {
                ShowTransientStatus("Metadata refreshed.");
            }
        });
    }

    private SqliteLibraryRepository RequireRepository() =>
        repository ?? throw new InvalidOperationException("The library is not ready.");
}
