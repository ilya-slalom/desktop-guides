using CommunityToolkit.WinUI;
using DesktopGuides.Core.Navigation;
using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T16.1: Ctrl+F and Ctrl+O sit beside Alt+Left on ShellRoot. Each runs
    // only where KeyboardShortcuts places it, and never over typing.
    private void InitializeKeyboard()
    {
        AddShortcut(VirtualKey.F, ShortcutCommand.FocusLibrarySearch, () =>
            navigator.Current is ReaderRoute ? OpenFindFromKeyboard() : FocusLibrarySearch());
        AddShortcut(VirtualKey.O, ShortcutCommand.ImportGuide, ImportFromKeyboard);
        // The inner text box marks Esc handled, so this runs on the way down.
        LibrarySearchInput.PreviewKeyDown += LibrarySearchKeyDown;
        ShowShortcut(ImportGuideButton, "Import guide", ShortcutCommand.ImportGuide);
        ShowShortcut(LibrarySearchInput, "Search", ShortcutCommand.FocusLibrarySearch);
        ShellRoot.PreviewKeyDown += ShellPreviewKeyDown;
    }

    // The focused control handles a key before accelerators run, so the
    // toolbar would keep Alt+Left and the text list Ctrl+End. These keys are
    // taken on the way down instead; the accelerators stay as the fallback.
    private void ShellPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        VirtualKeyModifiers modifiers = CurrentModifiers();
        if (args.Key == VirtualKey.Left && modifiers == VirtualKeyModifiers.Menu)
        {
            if (!CanGoBackFromKeyboard()) return;
            args.Handled = true;
            _ = GoBackFromKeyboardAsync();
            return;
        }
        // T18.1: F3 and Shift+F3 step matches, even from the find box.
        if (navigator.Current is ReaderRoute && args.Key == VirtualKey.F3 && ReaderFind.Visibility == Visibility.Visible)
        {
            args.Handled = true;
            _ = FindStepAsync(modifiers == VirtualKeyModifiers.Shift ? FindDirection.Previous : FindDirection.Next);
            return;
        }
        if (navigator.Current is ReaderRoute && args.OriginalSource is not (TextBox or PasswordBox))
        {
            args.Handled = ReaderActions.TryRunKey(args.Key, modifiers);
        }
    }

    private bool OpenFindFromKeyboard()
    {
        _ = OpenFindAsync();
        return true;
    }

    private static void ShowShortcut(FrameworkElement control, string label, ShortcutCommand command)
    {
        ToolTipService.SetToolTip(control, KeyboardShortcuts.ToolTip(label, command));
        AutomationProperties.SetAcceleratorKey(control, KeyboardShortcuts.For(command).Keys);
    }

    private void AddShortcut(VirtualKey key, ShortcutCommand command, Func<bool> run)
    {
        KeyboardAccelerator accelerator = new() { Key = key, Modifiers = VirtualKeyModifiers.Control };
        accelerator.Invoked += (_, args) =>
        {
            // T18.1: in the reader, Ctrl+F is the guide search, not the library's.
            ShortcutCommand resolved = navigator.Current is ReaderRoute && command == ShortcutCommand.FocusLibrarySearch
                ? ShortcutCommand.FindInGuide
                : command;
            if (ShortcutRouting.Resolve(resolved, navigator.Current, TextInputFocused(), DialogOpen())
                == ShortcutDecision.Run)
            {
                args.Handled = run();
            }
        };
        ShellRoot.KeyboardAccelerators.Add(accelerator);
    }

    private bool TextInputFocused() =>
        ShellRoot.XamlRoot is XamlRoot root &&
        FocusManager.GetFocusedElement(root) is TextBox or PasswordBox or RichEditBox;

    private bool FocusLibrarySearch()
    {
        if (!LibrarySearchInput.IsEnabled || LibrarySearchInput.Visibility != Visibility.Visible)
        {
            return false;
        }
        LibrarySearchInput.Focus(FocusState.Keyboard);
        // Typing then replaces the old query.
        LibrarySearchInput.FindDescendant<TextBox>()?.SelectAll();
        return true;
    }

    // Runs the button's own handler, so its guard against a second picker applies.
    private bool ImportFromKeyboard()
    {
        if (!ImportGuideButton.IsEnabled || ImportGuideButton.Visibility != Visibility.Visible)
        {
            return false;
        }
        ImportGuideClicked(ImportGuideButton, new RoutedEventArgs());
        return true;
    }

    private void LibrarySearchKeyDown(object sender, KeyRoutedEventArgs args)
    {
        // An open suggestion list closes first, as the box itself does.
        if (args.Key != VirtualKey.Escape || LibrarySearchInput.IsSuggestionListOpen ||
            ShortcutRouting.Resolve(ShortcutCommand.ClearLibrarySearch, navigator.Current, true, DialogOpen())
                != ShortcutDecision.Run)
        {
            return;
        }
        args.Handled = true;
        switch (ShortcutRouting.LibrarySearchEsc(LibrarySearchInput.Text.Length > 0, GameList.Items.Count))
        {
            case LibrarySearchEscape.ClearText:
                LibrarySearchInput.Text = string.Empty;
                ApplyLibrarySearch(announce: true);
                break;
            case LibrarySearchEscape.FocusFirstGame:
                (GameList.ContainerFromIndex(0) as Control)?.Focus(FocusState.Keyboard);
                break;
            default:
                AddGameButton.Focus(FocusState.Keyboard);
                break;
        }
    }
}
