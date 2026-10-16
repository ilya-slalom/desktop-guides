using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ShortcutRoutingTests
{
    private static readonly ShellRoute Library = new LibraryRoute();
    private static readonly ShellRoute Game = new GameRoute(Guid.NewGuid());
    private static readonly ShellRoute Reader = new ReaderRoute(Guid.NewGuid(), Guid.NewGuid());
    private static readonly ShellRoute Settings = new SettingsRoute();

    public static TheoryData<ShortcutCommand, ShellRoute> RightPlace() => new()
    {
        { ShortcutCommand.Back, Game },
        { ShortcutCommand.Back, Reader },
        { ShortcutCommand.Back, Settings },
        { ShortcutCommand.FocusLibrarySearch, Library },
        { ShortcutCommand.ImportGuide, Game },
        { ShortcutCommand.NextPage, Reader },
        { ShortcutCommand.GoToPage, Reader },
        { ShortcutCommand.ZoomIn, Reader },
    };

    public static TheoryData<ShortcutCommand, ShellRoute> WrongPlace() => new()
    {
        { ShortcutCommand.FocusLibrarySearch, Game },
        { ShortcutCommand.FocusLibrarySearch, Reader },
        { ShortcutCommand.FocusLibrarySearch, Settings },
        { ShortcutCommand.ImportGuide, Library },
        { ShortcutCommand.ImportGuide, Reader },
        { ShortcutCommand.ImportGuide, Settings },
        { ShortcutCommand.NextPage, Library },
        { ShortcutCommand.GoToPage, Game },
        { ShortcutCommand.ZoomIn, Settings },
        { ShortcutCommand.ClearLibrarySearch, Game },
    };

    [Theory]
    [MemberData(nameof(RightPlace))]
    public void ACommandRunsInItsContext(ShortcutCommand command, ShellRoute route)
    {
        Assert.Equal(ShortcutDecision.Run, ShortcutRouting.Resolve(command, route, false, false));
    }

    [Theory]
    [MemberData(nameof(WrongPlace))]
    public void ACommandIsIgnoredElsewhere(ShortcutCommand command, ShellRoute route)
    {
        Assert.Equal(ShortcutDecision.Ignore, ShortcutRouting.Resolve(command, route, false, false));
    }

    [Theory]
    [InlineData(ShortcutCommand.FocusLibrarySearch)]
    [InlineData(ShortcutCommand.ImportGuide)]
    [InlineData(ShortcutCommand.PreviousPage)]
    [InlineData(ShortcutCommand.NextPage)]
    [InlineData(ShortcutCommand.PageStart)]
    [InlineData(ShortcutCommand.PageEnd)]
    public void TypingWins(ShortcutCommand command)
    {
        ShellRoute route = command switch
        {
            ShortcutCommand.FocusLibrarySearch => Library,
            ShortcutCommand.ImportGuide => Game,
            _ => Reader
        };
        Assert.Equal(ShortcutDecision.Ignore, ShortcutRouting.Resolve(command, route, true, false));
    }

    [Theory]
    [InlineData(ShortcutCommand.Back)]
    [InlineData(ShortcutCommand.GoToPage)]
    [InlineData(ShortcutCommand.ZoomIn)]
    [InlineData(ShortcutCommand.ZoomReset)]
    public void TheseStillRunFromATextField(ShortcutCommand command)
    {
        ShellRoute route = command == ShortcutCommand.Back ? Game : Reader;
        Assert.Equal(ShortcutDecision.Run, ShortcutRouting.Resolve(command, route, true, false));
    }

    [Fact]
    public void ClearingTheSearchNeedsTheSearchBoxFocused()
    {
        Assert.Equal(ShortcutDecision.Run,
            ShortcutRouting.Resolve(ShortcutCommand.ClearLibrarySearch, Library, true, false));
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(ShortcutCommand.ClearLibrarySearch, Library, false, false));
    }

    [Theory]
    [InlineData(ShortcutCommand.Back)]
    [InlineData(ShortcutCommand.FocusLibrarySearch)]
    [InlineData(ShortcutCommand.ImportGuide)]
    [InlineData(ShortcutCommand.NextPage)]
    [InlineData(ShortcutCommand.GoToPage)]
    [InlineData(ShortcutCommand.ClearLibrarySearch)]
    public void AnOpenDialogBlocksEverythingElse(ShortcutCommand command)
    {
        foreach (ShellRoute route in new[] { Library, Game, Reader, Settings })
        {
            Assert.Equal(ShortcutDecision.Ignore, ShortcutRouting.Resolve(command, route, false, true));
        }
    }

    [Fact]
    public void CloseDialogRunsOnlyWithADialogOpen()
    {
        Assert.Equal(ShortcutDecision.Run, ShortcutRouting.Resolve(ShortcutCommand.CloseDialog, Game, false, true));
        Assert.Equal(ShortcutDecision.Ignore, ShortcutRouting.Resolve(ShortcutCommand.CloseDialog, Game, false, false));
    }

    [Theory]
    [InlineData(true, 0, LibrarySearchEscape.ClearText)]
    [InlineData(true, 3, LibrarySearchEscape.ClearText)]
    [InlineData(false, 3, LibrarySearchEscape.FocusFirstGame)]
    public void EscInTheSearchClearsThenLeaves(bool hasText, int games, LibrarySearchEscape expected)
    {
        Assert.Equal(expected, ShortcutRouting.LibrarySearchEsc(hasText, games));
    }

    [Fact]
    public void EscInAnEmptySearchOfAnEmptyLibraryTargetsAddGame()
    {
        Assert.Equal(LibrarySearchEscape.FocusAddGame, ShortcutRouting.LibrarySearchEsc(false, 0));
    }

    [Fact]
    public void SearchKeysRunInTheReaderEvenFromTheFindBox()
    {
        foreach (ShortcutCommand command in new[] { ShortcutCommand.FindInGuide, ShortcutCommand.FindNext, ShortcutCommand.FindPrevious })
        {
            Assert.Equal(ShortcutDecision.Run, ShortcutRouting.Resolve(command, Reader, textInputFocused: true, dialogOpen: false));
            Assert.Equal(ShortcutDecision.Ignore, ShortcutRouting.Resolve(command, Library, textInputFocused: false, dialogOpen: false));
        }
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(ShortcutCommand.FocusLibrarySearch, Reader, false, false));
    }

    [Fact]
    public void ShowContentsRunsOnlyInTheReader()
    {
        Assert.Equal(ShortcutDecision.Run,
            ShortcutRouting.Resolve(ShortcutCommand.ShowContents, Reader, textInputFocused: true, dialogOpen: false));
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(ShortcutCommand.ShowContents, Library, textInputFocused: false, dialogOpen: false));
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(ShortcutCommand.ShowContents, Reader, textInputFocused: false, dialogOpen: true));
    }

    [Theory]
    [InlineData(ShortcutCommand.AddBookmark)]
    [InlineData(ShortcutCommand.ShowBookmarks)]
    public void BookmarkKeysRunOnlyInTheReader(ShortcutCommand command)
    {
        Assert.Equal(ShortcutDecision.Run,
            ShortcutRouting.Resolve(command, Reader, textInputFocused: false, dialogOpen: false));
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(command, Library, textInputFocused: false, dialogOpen: false));
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(command, Reader, textInputFocused: false, dialogOpen: true));
    }
}
