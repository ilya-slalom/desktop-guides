using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GameRemovalPresentationTests
{
    [Theory]
    [InlineData(0, "Remove Halo?")]
    [InlineData(1, "Remove Halo and its guide?")]
    [InlineData(3, "Remove Halo and its 3 guides?")]
    public void TheTitleStatesTheGuideCount(int guides, string expected) =>
        Assert.Equal(expected, GameRemovalPresentation.DialogTitle("Halo", guides));

    [Fact]
    public void TheBodyForAGameWithoutGuidesIsT042sCopy() =>
        Assert.Equal(
            "This removes the game and its details from Desktop Guides.",
            GameRemovalPresentation.DialogBody(0, 0));

    [Theory]
    [InlineData(1, 1, "This removes the game, its guide with its reading progress, and its 1 managed file from Desktop Guides. The original file you imported isn't affected.")]
    [InlineData(1, 4, "This removes the game, its guide with its reading progress, and its 4 managed files from Desktop Guides. The original file you imported isn't affected.")]
    [InlineData(2, 1, "This removes the game, its 2 guides with their reading progress, and their 1 managed file from Desktop Guides. The original files you imported aren't affected.")]
    [InlineData(2, 3, "This removes the game, its 2 guides with their reading progress, and their 3 managed files from Desktop Guides. The original files you imported aren't affected.")]
    public void TheBodyStatesTheGuideAndFileCounts(int guides, int files, string expected) =>
        Assert.Equal(expected, GameRemovalPresentation.DialogBody(guides, files));

    [Fact]
    public void CountChangedStatesTheNewCount() =>
        Assert.Equal("The number of guides changed. It's now 3.", GameRemovalPresentation.CountChanged(3));

    [Fact]
    public void TheStatusLinesNameTheGame()
    {
        Assert.Equal("Removed Halo.", GameRemovalPresentation.Removed("Halo", false));
        Assert.Equal(
            "Removed Halo. Leftover files will be cleaned up the next time Desktop Guides starts.",
            GameRemovalPresentation.Removed("Halo", true));
        Assert.Equal("Halo was already removed.", GameRemovalPresentation.AlreadyRemoved("Halo"));
    }

    [Theory]
    [InlineData(GameRemovalIssue.Unsafe, "Halo can't be removed because a guide's files were changed outside Desktop Guides.")]
    [InlineData(GameRemovalIssue.Failed, "Halo couldn't be removed. The game is unchanged. Try again.")]
    [InlineData(GameRemovalIssue.RestoreFailed, "Halo couldn't be removed. Restart Desktop Guides to finish restoring it.")]
    public void EachIssueHasItsMessage(GameRemovalIssue issue, string expected) =>
        Assert.Equal(expected, GameRemovalPresentation.Error(issue, "Halo"));

    [Fact]
    public void AnUnknownIssueIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GameRemovalPresentation.Error((GameRemovalIssue)99, "Halo"));
}
