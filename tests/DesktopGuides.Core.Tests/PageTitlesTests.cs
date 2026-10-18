using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PageTitlesTests
{
    [Fact]
    public void TheSuffixEveryPageSharesIsRemoved() =>
        Assert.Equal(["Sample Quest", "Chapter 1", "Chapter 2"], PageTitles.WithoutSharedSuffix(
            ["Sample Quest — Guides Example", "Chapter 1 — Guides Example", "Chapter 2 — Guides Example"]));

    [Fact]
    public void TheCutStartsAtASeparatorNotInsideAWord() =>
        Assert.Equal(["Maps", "Bosses"], PageTitles.WithoutSharedSuffix(
            ["Maps — StrategyWiki | Strategy guide", "Bosses — StrategyWiki | Strategy guide"]));

    [Fact]
    public void WithoutASharedSeparatorTitlesStayWhole() =>
        Assert.Equal(["Walkthrough", "Breakthrough"], PageTitles.WithoutSharedSuffix(["Walkthrough", "Breakthrough"]));

    [Fact]
    public void OnePageKeepsItsWholeTitle() =>
        Assert.Equal(["Sample Quest — Guides Example"], PageTitles.WithoutSharedSuffix(["Sample Quest — Guides Example"]));

    [Fact]
    public void ACutThatWouldEmptyATitleIsntMade() =>
        Assert.Equal([" | Site", "Map | Site"], PageTitles.WithoutSharedSuffix([" | Site", "Map | Site"]));
}
