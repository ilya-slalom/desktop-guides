using CommunityToolkit.WinUI;
using DesktopGuides.Core.Navigation;
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
        AddShortcut(VirtualKey.F, ShortcutCommand.FocusLibrarySearch, FocusLibrarySearch);
        AddShortcut(VirtualKey.O, ShortcutCommand.ImportGuide, ImportFromKeyboard);
        // The inner text box marks Esc handled, so this runs on the way down.
        LibrarySearchInput.PreviewKeyDown += LibrarySearchKeyDown;
        ShowShortcut(ImportGuideButton, "Import guide", ShortcutCommand.ImportGuide);
        ShowShortcut(LibrarySearchInput, "Search", ShortcutCommand.FocusLibrarySearch);
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
            if (ShortcutRouting.Resolve(command, navigator.Current, TextInputFocused(), DialogOpen())
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
