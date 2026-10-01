using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GamePageLayoutTests
{
    [Fact]
    public void TallPageLeavesTheGuideListItsMinimum() =>
        // 900 - 80 - 32 - 3 * 16 = 740 for details and list.
        Assert.Equal(
            740 - GamePageLayout.MinGuideListHeight,
            GamePageLayout.DetailsMaxHeight(900, 80, 32, 16));

    [Fact]
    public void ShortPageShrinksTheDetailsInsteadOfTheGuideList() =>
        // The CI window: a wrapped title and the hint leave 190 for both.
        Assert.Equal(
            190 - GamePageLayout.MinGuideListHeight,
            GamePageLayout.DetailsMaxHeight(370, 100, 32, 16));

    [Fact]
    public void TinyPageKeepsTheDetailsMinimum() =>
        Assert.Equal(
            GamePageLayout.MinDetailsHeight,
            GamePageLayout.DetailsMaxHeight(200, 100, 32, 16));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void UnmeasuredPageDoesNotCapTheDetails(double panelHeight) =>
        Assert.Equal(
            double.PositiveInfinity,
            GamePageLayout.DetailsMaxHeight(panelHeight, 80, 32, 16));

    [Fact]
    public void GuideListMinimumHoldsOneRow() =>
        // A catalog row is a 60 px tile with 8 px padding above and below.
        Assert.True(GamePageLayout.MinGuideListHeight >= 76);
}
