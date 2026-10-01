using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GameRemovalPresentationTests
{
    [Fact]
    public void DialogTitleNamesTheGame() =>
        Assert.Equal("Remove Zelda?", GameRemovalPresentation.DialogTitle("Zelda"));

    [Fact]
    public void DialogBodyDescribesTheRemoval() =>
        Assert.Equal(
            "This removes the game and its details from Desktop Guides.",
            GameRemovalPresentation.DialogBody);

    [Fact]
    public void GuidesFirstExplainsTheDisabledAction() =>
        Assert.Equal("Remove this game's guides first.", GameRemovalPresentation.GuidesFirst);

    [Fact]
    public void StatusLinesNameTheGame()
    {
        Assert.Equal("Removed Zelda.", GameRemovalPresentation.Removed("Zelda"));
        Assert.Equal("Zelda was already removed.", GameRemovalPresentation.AlreadyRemoved("Zelda"));
        Assert.Equal(
            "Zelda has guides now, so it wasn't removed.", GameRemovalPresentation.HasGuides("Zelda"));
        Assert.Equal(
            "Zelda couldn't be removed. The game is unchanged. Try again.",
            GameRemovalPresentation.Failed("Zelda"));
    }
}
