namespace DesktopGuides.Core.Navigation;

public enum ShortcutContext { Everywhere, Library, LibrarySearch, Game, Reader, Dialog }

public enum ShortcutCommand
{
    Back, FocusLibrarySearch, ClearLibrarySearch, ImportGuide, PreviousPage, NextPage,
    PageStart, PageEnd, GoToPage, ZoomIn, ZoomOut, ZoomReset, CloseDialog
}

public sealed record KeyboardShortcut(
    ShortcutContext Context, string Keys, ShortcutCommand Command, string Description);

/// <summary>T16.1: every keyboard shortcut, for Settings, tooltips and tests.</summary>
public static class KeyboardShortcuts
{
    public const string SectionHeading = "Help";
    public const string ExpanderHeader = "Keyboard shortcuts";
    public const string ExpanderDescription = "Keys for the Library, Game pages, the reader and dialogs.";

    public static IReadOnlyList<KeyboardShortcut> All { get; } =
    [
        new(ShortcutContext.Everywhere, "Alt+Left", ShortcutCommand.Back, "Go back"),
        new(ShortcutContext.Library, "Ctrl+F", ShortcutCommand.FocusLibrarySearch, "Search the library"),
        new(ShortcutContext.LibrarySearch, "Esc", ShortcutCommand.ClearLibrarySearch, "Clear the search"),
        new(ShortcutContext.Game, "Ctrl+O", ShortcutCommand.ImportGuide, "Import a guide"),
        new(ShortcutContext.Reader, "Page Up", ShortcutCommand.PreviousPage, "Previous page (TXT, PDF)"),
        new(ShortcutContext.Reader, "Page Down", ShortcutCommand.NextPage, "Next page (TXT, PDF)"),
        new(ShortcutContext.Reader, "Ctrl+Home", ShortcutCommand.PageStart, "Go to start (TXT, PDF)"),
        new(ShortcutContext.Reader, "Ctrl+End", ShortcutCommand.PageEnd, "Go to end (TXT, PDF)"),
        new(ShortcutContext.Reader, "Ctrl+G", ShortcutCommand.GoToPage, "Go to page (PDF)"),
        new(ShortcutContext.Reader, "Ctrl+Plus", ShortcutCommand.ZoomIn, "Zoom in (PDF) or larger text"),
        new(ShortcutContext.Reader, "Ctrl+Minus", ShortcutCommand.ZoomOut, "Zoom out (PDF) or smaller text"),
        new(ShortcutContext.Reader, "Ctrl+0", ShortcutCommand.ZoomReset, "Fit to width (PDF) or reset text size"),
        new(ShortcutContext.Dialog, "Esc", ShortcutCommand.CloseDialog, "Close the dialog"),
    ];

    public static KeyboardShortcut For(ShortcutCommand command) => All.Single(s => s.Command == command);

    public static string ContextHeading(ShortcutContext context) => context switch
    {
        ShortcutContext.Everywhere => "Everywhere",
        ShortcutContext.Library => "Library",
        ShortcutContext.LibrarySearch => "Library search",
        ShortcutContext.Game => "Game page",
        ShortcutContext.Reader => "Reader",
        _ => "Dialogs"
    };

    public static string ToolTip(string label, ShortcutCommand command) => $"{label} ({For(command).Keys})";

    public static string AutomationName(KeyboardShortcut shortcut) => $"{shortcut.Description}, {shortcut.Keys}";
}
