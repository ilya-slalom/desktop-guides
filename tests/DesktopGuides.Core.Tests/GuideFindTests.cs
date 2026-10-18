using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideFindTests
{
    private static readonly TextMatch[] Matches = [new(10, 3), new(20, 3), new(30, 3)];

    [Theory]
    [InlineData(2, 0, FindDirection.Next, null)]
    [InlineData(0, 0, FindDirection.Previous, null)]
    [InlineData(1, 0, FindDirection.Next, 2)]
    [InlineData(null, 15, FindDirection.Next, 1)]
    [InlineData(null, 35, FindDirection.Next, null)]
    [InlineData(null, 5, FindDirection.Previous, null)]
    [InlineData(null, 25, FindDirection.Previous, 1)]
    public void StepInPageNeverWraps(int? current, int from, FindDirection direction, int? expected) =>
        Assert.Equal(expected, GuideFind.StepInPage(Matches, current, from, direction));

    [Theory]
    [InlineData(0, FindDirection.Next, 2)]
    [InlineData(2, FindDirection.Next, 0)]
    [InlineData(0, FindDirection.Previous, 2)]
    public void NextPageIsTheNearestOtherPageWithAMatch(int open, FindDirection direction, int expected) =>
        Assert.Equal(expected, GuideFind.NextPage([1, 0, 2, 0], open, direction));

    // Review Focus 2.
    [Fact]
    public void NoOtherPageWrapsWithinTheOpenPage()
    {
        Assert.Null(GuideFind.NextPage([0, 3, 0], 1, FindDirection.Next));
        Assert.Null(GuideFind.NextPage([0, 3, 0], 1, FindDirection.Previous));
    }

    // Review Focus 4.
    [Fact]
    public void PreviousWrapsToTheLastPageWithAMatch() =>
        Assert.Equal(3, GuideFind.NextPage([2, 0, 1, 4], 0, FindDirection.Previous));

    [Fact]
    public void TheCountSpansPagesAndTheLiveCountWins() =>
        Assert.Equal((3, 8), GuideFind.Count([1, 0, 2, 3], 2, 4, 1));
}
