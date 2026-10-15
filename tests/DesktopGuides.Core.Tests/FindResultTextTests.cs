using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class FindResultTextTests
{
    private static FindResult With(int imageOnly, params int[] unreadable) =>
        new FindResult(FindOutcome.Found, 1, 3, false, imageOnly) { UnreadablePages = unreadable };

    [Fact]
    public void NothingToNoteIsNull() => Assert.Null(FindResultText.Note(With(0)));

    [Theory]
    [InlineData(1, "1 image-only page")]
    [InlineData(4, "4 image-only pages")]
    public void ImageOnlyPagesAreCounted(int pages, string expected) =>
        Assert.Equal(expected, FindResultText.Note(With(pages)));

    [Fact]
    public void OneUnreadablePageIsNamed() =>
        Assert.Equal("Couldn't search page 14", FindResultText.Note(With(0, 14)));

    [Fact]
    public void UpToThreeUnreadablePagesAreListed() =>
        Assert.Equal("Couldn't search pages 14, 20, 31", FindResultText.Note(With(0, 14, 20, 31)));

    [Fact]
    public void MoreThanThreeAreSummed() =>
        Assert.Equal("Couldn't search pages 14, 20, 31 and 2 more", FindResultText.Note(With(0, 14, 20, 31, 40, 41)));

    [Fact]
    public void BothNotesAreJoined() =>
        Assert.Equal("2 image-only pages \u00B7 Couldn't search page 9", FindResultText.Note(With(2, 9)));
}
