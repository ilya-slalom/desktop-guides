using DesktopGuides.Core.Library;
using DesktopGuides.Core.Maps;
using DesktopGuides.Core.Reading;
using DesktopGuides.Production.Materials;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace DesktopGuides.Production;

public sealed partial class ReaderToolbar : UserControl
{
    private IReaderSession? session;
    private CancellationTokenSource sessionActions = new();

    // VK_OEM_PLUS and VK_OEM_MINUS: the = and - keys of the main keyboard.
    private const VirtualKey EqualsKey = (VirtualKey)187;
    private const VirtualKey MinusKey = (VirtualKey)189;
    private const VirtualKeyModifiers Ctrl = VirtualKeyModifiers.Control;

    private static readonly (VirtualKey Key, VirtualKeyModifiers Modifiers)[] KeyTable =
    [
        (VirtualKey.PageUp, VirtualKeyModifiers.None),
        (VirtualKey.PageDown, VirtualKeyModifiers.None),
        (VirtualKey.PageUp, Ctrl),
        (VirtualKey.PageDown, Ctrl),
        (VirtualKey.Home, Ctrl),
        (VirtualKey.End, Ctrl),
        (VirtualKey.G, Ctrl),
        (VirtualKey.Add, Ctrl),
        (EqualsKey, Ctrl),
        (EqualsKey, Ctrl | VirtualKeyModifiers.Shift),
        (VirtualKey.Subtract, Ctrl),
        (MinusKey, Ctrl),
        (VirtualKey.Number0, Ctrl),
        (VirtualKey.NumberPad0, Ctrl)
    ];

    private bool promptOpen;
    // T21.2: the page buttons' own tooltips, for sessions that page as TXT and PDF do.
    private readonly object?[] pageTips;
    // The open guide's text size; the shell sets it at open (Ruling 3).
    private double textScale = TextSizeSteps.Default;

    public ReaderToolbar()
    {
        InitializeComponent();
        pageTips = [.. new[] { PageStart, PreviousPage, NextPage, PageEnd }.Select(ToolTipService.GetToolTip)];
        // The tooltips name the keys, so WinUI's own key tips stay hidden.
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        foreach ((VirtualKey key, VirtualKeyModifiers modifiers) in KeyTable)
        {
            KeyboardAccelerator accelerator = new() { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (sender, args) =>
                args.Handled = TryRunKey(sender.Key, sender.Modifiers);
            KeyboardAccelerators.Add(accelerator);
        }
        // The event comes before the move; the shell reads HasOverflow after it.
        Commands.DynamicOverflowItemsChanging += (_, _) =>
            DispatcherQueue.TryEnqueue(() => OverflowChanged?.Invoke(this, EventArgs.Empty));
    }

    // T19.2: whether a shown command has moved into the overflow menu.
    public bool HasOverflow => Commands.PrimaryCommands.Any(command =>
        command is ICommandBarElement { IsInOverflow: true } and UIElement { Visibility: Visibility.Visible });

    public event EventHandler? OverflowChanged;

    // The open document's page count; 0 means the dialog has no range.
    public int PageCount { get; set; }

    // Each session's shell code turns keys on after SetSession resets them.
    public bool KeysEnabled { get; set; }

    // Raised after a dialog that a key opened closes, for the shell to focus content.
    public event EventHandler? ContentFocusRequested;

    public event Action<string>? CommandFailed;

    // Raised by a text size step before its action runs, so the shell's
    // scale is current for a theme refresh during the write (Ruling 3).
    public event Action<IReaderSession, double>? TextSizeChanged;

    internal WindowMaterial DialogMaterial { get; set; } = WindowMaterial.Mica;
    internal ElementTheme DialogTheme { get; set; } = ElementTheme.Default;

    public void SetSession(IReaderSession? value)
    {
        if (ReferenceEquals(session, value))
        {
            return;
        }

        if (session is not null)
        {
            session.CapabilitiesChanged -= CapabilitiesChanged;
        }
        sessionActions.Cancel();
        sessionActions.Dispose();
        sessionActions = new CancellationTokenSource();
        session = value;
        PageCount = 0;
        KeysEnabled = false;
        ZoomIn.IsEnabled = true;
        ZoomOut.IsEnabled = true;
        // T21.2 PR c: the shell shows Maps once it knows the game has some.
        mapsAvailable = false;
        ShowMaps.Visibility = Visibility.Collapsed;
        CloseMaps();
        ShowTextSize(TextSizeSteps.Default, announce: false);
        if (session is not null)
        {
            session.CapabilitiesChanged += CapabilitiesChanged;
        }
        RefreshCommands();
    }

    private void CapabilitiesChanged(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, session))
        {
            return;
        }
        if (DispatcherQueue.HasThreadAccess)
        {
            RefreshCommands();
        }
        else
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(sender, session))
                {
                    RefreshCommands();
                }
            });
        }
    }

    private void RefreshCommands()
    {
        HashSet<ReaderCommand> supported = session is null
            ? []
            : ReaderCommandPolicy.VisibleCommands(session).ToHashSet();
        bool pages = supported.Contains(ReaderCommand.PageTurn);
        bool edges = supported.Contains(ReaderCommand.PageEdge);
        bool textSize = supported.Contains(ReaderCommand.TextSize);
        bool zoom = supported.Contains(ReaderCommand.Zoom);
        bool pageJump = supported.Contains(ReaderCommand.PageJump);
        bool fitWidth = supported.Contains(ReaderCommand.FitWidth);
        bool fitView = supported.Contains(ReaderCommand.FitView);
        bool find = supported.Contains(ReaderCommand.Find);
        bool contents = supported.Contains(ReaderCommand.Contents);
        bool bookmarks = supported.Contains(ReaderCommand.Bookmarks);
        bool addBookmark = supported.Contains(ReaderCommand.AddBookmark);
        PageStart.Visibility = Show(edges);
        PreviousPage.Visibility = Show(pages);
        NextPage.Visibility = Show(pages);
        PageEnd.Visibility = Show(edges);
        // T21.2: a web guide of several pages names its own keys.
        bool guidePages = session is not null && (session.Capabilities & ReaderCapabilities.GuidePages) != 0;
        string?[] guideTips = ["Go to the first page", "Previous page (Ctrl+Page Up)", "Next page (Ctrl+Page Down)", "Go to the last page"];
        AppBarButton[] pageButtons = [PageStart, PreviousPage, NextPage, PageEnd];
        for (int index = 0; index < pageButtons.Length; index++)
        {
            ToolTipService.SetToolTip(pageButtons[index], guidePages ? guideTips[index] : pageTips[index]);
        }
        SmallerText.Visibility = Show(textSize);
        LargerText.Visibility = Show(textSize);
        TextSizeContainer.Visibility = Show(textSize);
        ResetTextSize.Visibility = Show(textSize);
        ZoomOut.Visibility = Show(zoom);
        ZoomIn.Visibility = Show(zoom);
        GoToPage.Visibility = Show(pageJump);
        FitToWidth.Visibility = Show(fitWidth);
        FitMap.Visibility = Show(fitView);
        FindInGuide.Visibility = Show(find);
        ShowContents.Visibility = Show(contents);
        ShowBookmarks.Visibility = Show(bookmarks);
        AddBookmark.Visibility = Show(addBookmark);
        GroupSeparator.Visibility = Show(ReaderCommandPolicy.ShowsGroupSeparator(supported));
        Commands.Visibility = Show(
            pages || edges || textSize || zoom || pageJump || fitWidth || fitView || find || contents || mapsAvailable ||
            bookmarks || addBookmark);
    }

    private static Visibility Show(bool visible) =>
        visible ? Visibility.Visible : Visibility.Collapsed;

    // P4 and P14: the accelerators and the PDF preview both come here.
    public bool TryRunKey(
        VirtualKey key, VirtualKeyModifiers modifiers, bool fromContent = false)
    {
        AppBarButton? command = CommandFor(key, modifiers);
        // T14.1 Ruling 1: the text size keys run even with KeysEnabled off.
        bool textKey = command == SmallerText || command == LargerText || command == ResetTextSize;
        if (command is null || !(KeysEnabled || textKey) || promptOpen || session is null ||
            XamlRoot is null || Commands.Visibility != Visibility.Visible ||
            command.Visibility != Visibility.Visible || !command.IsEnabled ||
            DialogOpen())
        {
            return false;
        }
        // A text box or scroll viewer keeps its own page keys. The preview
        // routes its keys here itself (fromContent), so they never run twice.
        bool pageKey = command == PreviousPage || command == NextPage ||
            command == PageStart || command == PageEnd;
        if (pageKey && !fromContent &&
            FocusManager.GetFocusedElement(XamlRoot) is TextBox or PasswordBox or ScrollViewer)
        {
            return false;
        }
        _ = RunAsync(command, fromKeyboard: true);
        return true;
    }

    // Any ContentDialog, including the shell's, owns the keyboard while open.
    private bool DialogOpen() =>
        VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
            .Any(popup => popup.Child is ContentDialog);

    private AppBarButton? CommandFor(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        // T21.2: which page keys page depends on the session.
        PageKey? page = key switch
        {
            VirtualKey.PageUp => PageKey.PageUp,
            VirtualKey.PageDown => PageKey.PageDown,
            VirtualKey.Home => PageKey.Home,
            VirtualKey.End => PageKey.End,
            _ => null
        };
        if (page is PageKey pageKey)
        {
            if (modifiers is not (VirtualKeyModifiers.None or Ctrl)) return null;
            return PageKeyRouting.For(session?.Capabilities ?? ReaderCapabilities.None, pageKey, modifiers == Ctrl) switch
            {
                PageKeyCommand.PreviousPage => PreviousPage,
                PageKeyCommand.NextPage => NextPage,
                PageKeyCommand.PageStart => PageStart,
                PageKeyCommand.PageEnd => PageEnd,
                _ => null
            };
        }
        return (key, modifiers) switch
        {
            (VirtualKey.G, Ctrl) => GoToPage,
            (VirtualKey.Add or EqualsKey, Ctrl) => ZoomOr(ZoomIn, LargerText),
            (EqualsKey, Ctrl | VirtualKeyModifiers.Shift) => ZoomOr(ZoomIn, LargerText),
            (VirtualKey.Subtract or MinusKey, Ctrl) => ZoomOr(ZoomOut, SmallerText),
            (VirtualKey.Number0 or VirtualKey.NumberPad0, Ctrl) =>
                FitMap.Visibility == Visibility.Visible ? FitMap : ZoomOr(FitToWidth, ResetTextSize),
            _ => null
        };
    }

    // Ruling 2: a session shows zoom or text size, not both. The zoom
    // commands win while visible, even disabled.
    private static AppBarButton ZoomOr(AppBarButton zoom, AppBarButton text) =>
        zoom.Visibility == Visibility.Visible ? zoom : text;

    // One place maps a keyed command to its action, for clicks and keys.
    private Task RunAsync(AppBarButton command, bool fromKeyboard = false)
    {
        if (command == GoToPage) return GoToPageAsync(fromKeyboard);
        if (command == PreviousPage) return ExecuteAsync(new PageTurnAction(-1), "turn to the previous page");
        if (command == NextPage) return ExecuteAsync(new PageTurnAction(1), "turn to the next page");
        if (command == PageStart) return ExecuteAsync(new PageEdgeAction(ReaderEdge.Start), "go to the start");
        if (command == PageEnd) return ExecuteAsync(new PageEdgeAction(ReaderEdge.End), "go to the end");
        if (command == ZoomOut) return ExecuteAsync(new ZoomAction(0.9), "zoom out");
        if (command == ZoomIn) return ExecuteAsync(new ZoomAction(1.1), "zoom in");
        if (command == FitToWidth) return ExecuteAsync(new FitWidthAction(), "fit to width");
        if (command == FitMap) return ExecuteAsync(new FitViewAction(), "fit the map");
        if (command == SmallerText) return StepTextAsync(TextSizeSteps.Smaller(textScale), "make the text smaller");
        if (command == LargerText) return StepTextAsync(TextSizeSteps.Larger(textScale), "make the text larger");
        if (command == ResetTextSize) return StepTextAsync(TextSizeSteps.Default, "reset the text size");
        throw new ArgumentException("This command has no key.", nameof(command));
    }

    // P5. Disabling the focused command would move focus, so it moves to the other one first.
    public void SetZoomAvailability(bool canZoomIn, bool canZoomOut)
    {
        if (!canZoomIn && canZoomOut && ZoomIn.FocusState != FocusState.Unfocused)
        {
            ZoomOut.Focus(ZoomIn.FocusState);
        }
        else if (!canZoomOut && canZoomIn && ZoomOut.FocusState != FocusState.Unfocused)
        {
            ZoomIn.Focus(ZoomOut.FocusState);
        }
        ZoomIn.IsEnabled = canZoomIn;
        ZoomOut.IsEnabled = canZoomOut;
    }

    // The shell shows the open guide's size, at open and after a failed save.
    public void SetTextSize(double scale) => ShowTextSize(scale, announce: false);

    private void ShowTextSize(double scale, bool announce)
    {
        textScale = scale;
        string label = TextSizeSteps.Label(scale);
        TextSizeValue.Text = label;
        AutomationProperties.SetName(TextSizeValue, $"Text size {label}");
        bool larger = TextSizeSteps.CanLarger(scale);
        bool smaller = TextSizeSteps.CanSmaller(scale);
        // P5. Disabling the focused command would move focus, so it moves to the other one first.
        if (!larger && smaller && LargerText.FocusState != FocusState.Unfocused)
        {
            SmallerText.Focus(LargerText.FocusState);
        }
        else if (!smaller && larger && SmallerText.FocusState != FocusState.Unfocused)
        {
            LargerText.Focus(SmallerText.FocusState);
        }
        LargerText.IsEnabled = larger;
        SmallerText.IsEnabled = smaller;
        ResetTextSize.IsEnabled = scale != TextSizeSteps.Default;
        if (announce)
        {
            FrameworkElementAutomationPeer.CreatePeerForElement(TextSizeValue)
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    // The toolbar steps (Ruling 3); the session only applies the target.
    private Task StepTextAsync(double target, string description)
    {
        IReaderSession? current = session;
        if (current is null || target == textScale)
        {
            return Task.CompletedTask;
        }
        ShowTextSize(target, announce: true);
        TextSizeChanged?.Invoke(current, target);
        return ExecuteAsync(new TextSizeAction(target), description);
    }

    private async Task ExecuteAsync(ReaderAction action, string description)
    {
        IReaderSession? current = session;
        if (current is null)
        {
            return;
        }
        CancellationToken token = sessionActions.Token;
        try
        {
            await ReaderCommandPolicy.ExecuteAsync(current, action, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A new guide replaced the reader while its command was running.
        }
        catch (Exception error)
        {
            if (ReferenceEquals(current, session))
            {
                CommandFailed?.Invoke($"Couldn't {description}: {error.Message}");
            }
        }
    }

    private async Task<string?> PromptAsync(
        string title, string label, string primaryButtonText,
        Control invokingControl, IReaderSession expectedSession,
        Func<string, string?>? validate = null, bool fromKeyboard = false)
    {
        TextBox input = new()
        {
            Header = label
        };
        AutomationProperties.SetAutomationId(input, "ReaderCommandInput");
        TextBlock error = new()
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetAutomationId(error, "ReaderCommandError");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        ContentDialog dialog = new()
        {
            Title = title,
            Content = new StackPanel { Spacing = 8, Children = { input, error } },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        DialogSurface.Apply(dialog, DialogMaterial, DialogTheme);
        dialog.Opened += (_, _) => input.Focus(FocusState.Programmatic);
        if (validate is not null)
        {
            // Go stays available, so a refusal explains itself (P2).
            dialog.PrimaryButtonClick += (_, args) =>
            {
                string? refusal = validate(input.Text);
                if (refusal is null)
                {
                    return;
                }
                args.Cancel = true;
                error.Text = refusal;
                error.Visibility = Visibility.Visible;
                FrameworkElementAutomationPeer.CreatePeerForElement(error)
                    ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                input.Focus(FocusState.Programmatic);
                input.SelectAll();
            };
        }
        promptOpen = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? input.Text.Trim()
                : null;
        }
        finally
        {
            promptOpen = false;
            if (fromKeyboard)
            {
                RequestContentFocus(expectedSession);
            }
            else
            {
                RestorePromptFocus(invokingControl, expectedSession);
            }
        }
    }

    // After Ctrl+G the reader content, not the overflow command, gets focus.
    private void RequestContentFocus(IReaderSession expectedSession) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(session, expectedSession))
            {
                ContentFocusRequested?.Invoke(this, EventArgs.Empty);
            }
        });

    private void RestorePromptFocus(
        Control invokingControl, IReaderSession expectedSession)
    {
        if (!ReferenceEquals(session, expectedSession) ||
            Commands.Visibility != Visibility.Visible)
        {
            return;
        }

        // Dialog commands live in overflow, which closes when they are invoked.
        Commands.IsOpen = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(session, expectedSession))
            {
                return;
            }
            if (invokingControl.Visibility == Visibility.Visible &&
                invokingControl.Focus(FocusState.Keyboard))
            {
                return;
            }
            foreach (Control command in new Control[]
            {
                PageStart, PreviousPage, NextPage, PageEnd, SmallerText, LargerText,
                ZoomOut, FitMap, ZoomIn, GoToPage, FitToWidth, ResetTextSize, ShowContents, ShowBookmarks, AddBookmark,
                FindInGuide
            })
            {
                if (command.Visibility == Visibility.Visible &&
                    command.Focus(FocusState.Keyboard))
                {
                    return;
                }
            }
        });
    }

    private async void PreviousPageClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(PreviousPage);

    private async void NextPageClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(NextPage);

    private async void PageStartClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(PageStart);

    private async void PageEndClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(PageEnd);

    private async void SmallerTextClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(SmallerText);

    private async void LargerTextClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(LargerText);

    private async void ResetTextSizeClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(ResetTextSize);

    private async void ZoomOutClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(ZoomOut);

    private async void ZoomInClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(ZoomIn);

    private async void FitToWidthClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(FitToWidth);

    private async void FitMapClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(FitMap);

    // T21.2 PR c: the Maps pane. The flyout closes itself on the press that
    // lands on Maps, so a click just after it closed means "close", not "open".
    private bool mapsAvailable;
    private bool focusMapsRow;
    private DateTimeOffset mapsClosedAt = DateTimeOffset.MinValue;

    public MapsPane Maps => MapsPaneView;

    public bool MapsOpen => MapsFlyout.IsOpen;

    // Raised before the pane shows, so the shell can load the game's maps.
    public event EventHandler? MapsOpening;

    public void SetMaps(bool any)
    {
        mapsAvailable = any;
        ShowMaps.Visibility = Show(any);
        if (!any) CloseMaps();
        RefreshCommands();
    }

    public void OpenMaps(bool fromKeyboard)
    {
        if (!mapsAvailable || ShowMaps.Visibility != Visibility.Visible || MapsFlyout.IsOpen) return;
        focusMapsRow = fromKeyboard;
        FlyoutBase.ShowAttachedFlyout(ShowMaps);
    }

    public void CloseMaps()
    {
        if (MapsFlyout.IsOpen) MapsFlyout.Hide();
    }

    private void ShowMapsClicked(object sender, RoutedEventArgs args)
    {
        if (MapsFlyout.IsOpen || DateTimeOffset.UtcNow - mapsClosedAt < TimeSpan.FromMilliseconds(300))
        {
            CloseMaps();
            return;
        }
        OpenMaps(fromKeyboard: false);
    }

    private void MapsFlyoutOpening(object? sender, object args)
    {
        if (XamlRoot is not null)
        {
            (double width, double height) = MapPreview.PaneSize(XamlRoot.Size.Width, XamlRoot.Size.Height);
            MapsPaneView.Width = width;
            MapsPaneView.Height = height;
        }
        MapsOpening?.Invoke(this, EventArgs.Empty);
    }

    private void MapsFlyoutOpened(object? sender, object args)
    {
        if (focusMapsRow) MapsPaneView.FocusFirstRow();
        focusMapsRow = false;
    }

    private void MapsFlyoutClosed(object? sender, object args)
    {
        mapsClosedAt = DateTimeOffset.UtcNow;
    }

    private async void GoToPageClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(GoToPage);

    private async Task GoToPageAsync(bool fromKeyboard)
    {
        IReaderSession? current = session;
        if (current is null)
        {
            return;
        }
        // P2: with a count the dialog refuses a bad entry itself.
        int count = PageCount;
        Func<string, string?>? validate = count > 0
            ? text => PageEntry.TryParse(text, count, out _) ? null : PageEntry.RangeMessage(count)
            : null;
        string? value = await PromptAsync(
            "Go to page", "Page number", "Go", GoToPage, current, validate, fromKeyboard);
        if (value is null || !ReferenceEquals(current, session))
        {
            return;
        }
        bool valid = count > 0
            ? PageEntry.TryParse(value, count, out int page)
            : int.TryParse(value, out page) && page >= 1;
        if (!valid)
        {
            CommandFailed?.Invoke("Enter a page number greater than zero.");
            return;
        }
        await ExecuteAsync(new PageJumpAction(page), "go to that page");
    }

    // T18.1: the shell opens the find bar; the old prompt dialog is gone.
    public event EventHandler? FindRequested;

    private void FindInGuideClicked(object sender, RoutedEventArgs args) =>
        FindRequested?.Invoke(this, EventArgs.Empty);

    // T18.2: the shell opens or closes the Contents pane.
    public event EventHandler? ContentsRequested;

    private void ShowContentsClicked(object sender, RoutedEventArgs args) =>
        RaiseAfterOverflow(() => ContentsRequested?.Invoke(this, EventArgs.Empty));

    // T19.1: the shell opens the Bookmarks tab, or adds a bookmark here.
    public event EventHandler? BookmarksRequested;
    public event EventHandler? AddBookmarkRequested;

    private void ShowBookmarksClicked(object sender, RoutedEventArgs args) =>
        RaiseAfterOverflow(() => BookmarksRequested?.Invoke(this, EventArgs.Empty));

    private void AddBookmarkClicked(object sender, RoutedEventArgs args) =>
        RaiseAfterOverflow(() => AddBookmarkRequested?.Invoke(this, EventArgs.Empty));

    // T19.1: a closing overflow menu hands focus back to "More". A command
    // that moves focus into the pane runs once the menu has closed, so the
    // menu doesn't take focus back from it.
    private void RaiseAfterOverflow(Action raise)
    {
        if (!Commands.IsOpen)
        {
            raise();
            return;
        }
        void Closed(object? sender, object args)
        {
            Commands.Closed -= Closed;
            raise();
        }
        Commands.Closed += Closed;
    }
}
