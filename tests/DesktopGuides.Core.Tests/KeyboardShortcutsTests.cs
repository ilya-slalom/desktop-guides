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
                (ShortcutContext.Reader, "Ctrl+G", ShortcutCommand.GoToPage, "Go to page (PDF)"),
                (ShortcutContext.Reader, "Ctrl+Plus", ShortcutCommand.ZoomIn, "Zoom in (PDF) or larger text"),
                (ShortcutContext.Reader, "Ctrl+Minus", ShortcutCommand.ZoomOut, "Zoom out (PDF) or smaller text"),
                (ShortcutContext.Reader, "Ctrl+0", ShortcutCommand.ZoomReset, "Fit to width (PDF) or reset text size"),
                (ShortcutContext.Reader, "Esc", ShortcutCommand.LeaveGuidePage, "Leave the guide page (HTML)"),
                (ShortcutContext.Dialog, "Esc", ShortcutCommand.CloseDialog, "Close the dialog"),
            ],
            KeyboardShortcuts.All.Select(s => (s.Context, s.Keys, s.Command, s.Description)));
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
