using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class KeyboardShortcutsTests
{
    [Fact]
    public void TheTableMatchesTheSpec()
    {
        Assert.Equal(
            [
                (ShortcutContext.Everywhere, "Alt+Left", ShortcutCommand.Back, "Go back"),
                (ShortcutContext.Library, "Ctrl+F", ShortcutCommand.FocusLibrarySearch, "Search the library"),
                (ShortcutContext.LibrarySearch, "Esc", ShortcutCommand.ClearLibrarySearch, "Clear the search"),
                (ShortcutContext.Game, "Ctrl+O", ShortcutCommand.ImportGuide, "Import a guide"),
                (ShortcutContext.Reader, "Page Up", ShortcutCommand.PreviousPage, "Previous page (TXT, PDF)"),
                (ShortcutContext.Reader, "Page Down", ShortcutCommand.NextPage, "Next page (TXT, PDF)"),
                (ShortcutContext.Reader, "Ctrl+Home", ShortcutCommand.PageStart, "Go to start (TXT, PDF)"),
                (ShortcutContext.Reader, "Ctrl+End", ShortcutCommand.PageEnd, "Go to end (TXT, PDF)"),
                (ShortcutContext.Reader, "Ctrl+Page Up", ShortcutCommand.PreviousGuidePage, "Previous page (web guides of several pages)"),
                (ShortcutContext.Reader, "Ctrl+Page Down", ShortcutCommand.NextGuidePage, "Next page (web guides of several pages)"),
                (ShortcutContext.Reader, "Ctrl+G", ShortcutCommand.GoToPage, "Go to page (PDF)"),
                (ShortcutContext.Reader, "Ctrl+Plus", ShortcutCommand.ZoomIn, "Zoom in (PDF) or larger text"),
                (ShortcutContext.Reader, "Ctrl+Minus", ShortcutCommand.ZoomOut, "Zoom out (PDF) or smaller text"),
                (ShortcutContext.Reader, "Ctrl+0", ShortcutCommand.ZoomReset, "Fit to width (PDF) or reset text size"),
                (ShortcutContext.Reader, "Ctrl+F", ShortcutCommand.FindInGuide, "Search in the guide"),
                (ShortcutContext.Reader, "F3", ShortcutCommand.FindNext, "Next match"),
                (ShortcutContext.Reader, "Shift+F3", ShortcutCommand.FindPrevious, "Previous match"),
                (ShortcutContext.Reader, "Ctrl+Shift+C", ShortcutCommand.ShowContents, "Show contents"),
                (ShortcutContext.Reader, "Ctrl+D", ShortcutCommand.AddBookmark, "Add bookmark"),
                (ShortcutContext.Reader, "Ctrl+Shift+B", ShortcutCommand.ShowBookmarks, "Show bookmarks"),
                (ShortcutContext.Dialog, "Esc", ShortcutCommand.CloseDialog, "Close the dialog"),
            ],
            KeyboardShortcuts.All.Select(s => (s.Context, s.Keys, s.Command, s.Description)));
    }

    // T19.1: two shortcuts that can run at the same moment never share keys.
    [Fact]
    public void NoTwoShortcutsShareKeysWhereBothCanRun()
    {
        static bool Overlap(ShortcutContext a, ShortcutContext b) =>
            a == b || a == ShortcutContext.Everywhere || b == ShortcutContext.Everywhere ||
            (a, b) is (ShortcutContext.Library, ShortcutContext.LibrarySearch)
                or (ShortcutContext.LibrarySearch, ShortcutContext.Library);

        string[] clashes =
        [
            .. from first in KeyboardShortcuts.All
               from second in KeyboardShortcuts.All
               where first.Command < second.Command && first.Keys == second.Keys &&
                     Overlap(first.Context, second.Context)
               select $"{first.Command} and {second.Command} on {first.Keys}"
        ];

        Assert.Empty(clashes);
    }

    [Fact]
    public void EveryCommandAppearsOnce()
    {
        Assert.Equal(
            Enum.GetValues<ShortcutCommand>().Order(),
            KeyboardShortcuts.All.Select(s => s.Command).Order());
    }

    [Fact]
    public void EveryContextHasAnEntry()
    {
        Assert.All(Enum.GetValues<ShortcutContext>(),
            context => Assert.Contains(KeyboardShortcuts.All, s => s.Context == context));
    }

    [Theory]
    [InlineData(ShortcutContext.Everywhere, "Everywhere")]
    [InlineData(ShortcutContext.Library, "Library")]
    [InlineData(ShortcutContext.LibrarySearch, "Library search")]
    [InlineData(ShortcutContext.Game, "Game page")]
    [InlineData(ShortcutContext.Reader, "Reader")]
    [InlineData(ShortcutContext.Dialog, "Dialogs")]
    public void EachContextHasAHeading(ShortcutContext context, string heading)
    {
        Assert.Equal(heading, KeyboardShortcuts.ContextHeading(context));
    }

    [Fact]
    public void TooltipsAndNamesUseTheKeys()
    {
        Assert.Equal("Import guide (Ctrl+O)", KeyboardShortcuts.ToolTip("Import guide", ShortcutCommand.ImportGuide));
        Assert.Equal("Search (Ctrl+F)", KeyboardShortcuts.ToolTip("Search", ShortcutCommand.FocusLibrarySearch));
        Assert.Equal("Import a guide, Ctrl+O",
            KeyboardShortcuts.AutomationName(KeyboardShortcuts.For(ShortcutCommand.ImportGuide)));
    }

    [Fact]
    public void TheSettingsCopyIsVerbatim()
    {
        Assert.Equal("Help", KeyboardShortcuts.SectionHeading);
        Assert.Equal("Keyboard shortcuts", KeyboardShortcuts.ExpanderHeader);
        Assert.Equal("Keys for the Library, Game pages, the reader and dialogs.",
            KeyboardShortcuts.ExpanderDescription);
    }
}
