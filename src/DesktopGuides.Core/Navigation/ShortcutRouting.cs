namespace DesktopGuides.Core.Navigation;

public enum ShortcutDecision { Run, Ignore }

public enum LibrarySearchEscape { ClearText, FocusFirstGame, FocusAddGame }

/// <summary>T16.1: whether a shortcut runs where the user is.</summary>
public static class ShortcutRouting
{
    // Keys a text field uses itself; the field wins while it has focus.
    private static readonly HashSet<ShortcutCommand> TypingKeys =
    [
        ShortcutCommand.FocusLibrarySearch, ShortcutCommand.ImportGuide,
        ShortcutCommand.PreviousPage, ShortcutCommand.NextPage,
        ShortcutCommand.PageStart, ShortcutCommand.PageEnd
    ];

    public static ShortcutDecision Resolve(
        ShortcutCommand command, ShellRoute route, bool textInputFocused, bool dialogOpen)
    {
        if (command == ShortcutCommand.CloseDialog)
        {
            return dialogOpen ? ShortcutDecision.Run : ShortcutDecision.Ignore;
        }
        if (dialogOpen || (textInputFocused && TypingKeys.Contains(command)))
        {
            return ShortcutDecision.Ignore;
        }
        bool inContext = KeyboardShortcuts.For(command).Context switch
        {
            ShortcutContext.Everywhere => true,
            ShortcutContext.Library => route is LibraryRoute,
            // Esc clears only while the search box itself has focus.
            ShortcutContext.LibrarySearch => route is LibraryRoute && textInputFocused,
            ShortcutContext.Game => route is GameRoute,
            ShortcutContext.Reader => route is ReaderRoute,
            ShortcutContext.Online => route is OnlineRoute,
            _ => false
        };
        return inContext ? ShortcutDecision.Run : ShortcutDecision.Ignore;
    }

    public static LibrarySearchEscape LibrarySearchEsc(bool hasText, int gameCount) =>
        hasText ? LibrarySearchEscape.ClearText
        : gameCount > 0 ? LibrarySearchEscape.FocusFirstGame
        : LibrarySearchEscape.FocusAddGame;
}
