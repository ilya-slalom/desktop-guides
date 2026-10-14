using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextMatcherTests
{
    [Fact]
    public void MatchesIgnoreCaseAndReturnSourceRanges()
    {
        IReadOnlyList<TextMatch> matches = TextMatcher.FindAll("The Boss key. the BOSS door.", new FindQuery("boss"));

        Assert.Equal([new TextMatch(4, 4), new TextMatch(18, 4)], matches);
    }

    [Fact]
    public void WhitespaceRunsMatchOneSpaceAcrossLines()
    {
        const string text = "Take the\n   silver   key now";
        IReadOnlyList<TextMatch> matches = TextMatcher.FindAll(text, new FindQuery("the  silver key"));

        TextMatch match = Assert.Single(matches);
        Assert.Equal("the\n   silver   key", text.Substring(match.Start, match.Length));
    }

    [Fact]
    public void OverlappingMatchesAreAllFound()
    {
        Assert.Equal(
            [new TextMatch(0, 3), new TextMatch(1, 3), new TextMatch(2, 3)],
            TextMatcher.FindAll("aaaaa", new FindQuery("aaa")));
    }

    [Fact]
    public void NoMatchIsEmpty()
    {
        Assert.Empty(TextMatcher.FindAll("nothing here", new FindQuery("dragon")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \t\n ")]
    public void AnEmptyQueryIsRefused(string query)
    {
        Assert.Throws<ArgumentException>(() => new FindQuery(query));
    }

    [Fact]
    public void TheQueryIsTrimmedAndCollapsed()
    {
        Assert.Equal("boss key", new FindQuery("  boss \t key \n").Text);
    }

    [Fact]
    public void MatchesStopAtTheCap()
    {
        Assert.Equal(10_000, TextMatcher.FindAll(new string('a', 50_000), new FindQuery("a")).Count);
        Assert.Equal(3, TextMatcher.FindAll("a a a a a", new FindQuery("a"), maxMatches: 3).Count);
    }

    [Fact]
    public void FirstStepFindsTheMatchAtOrAfterTheOffset()
    {
        TextMatch[] matches = [new(10, 2), new(50, 2), new(90, 2)];

        Assert.Equal(1, TextMatcher.Step(matches, current: null, fromOffset: 40, FindDirection.Next));
        Assert.Equal(0, TextMatcher.Step(matches, current: null, fromOffset: 40, FindDirection.Previous));
        Assert.Equal(0, TextMatcher.Step(matches, current: null, fromOffset: 95, FindDirection.Next));
    }

    [Fact]
    public void LaterStepsWrapAround()
    {
        TextMatch[] matches = [new(10, 2), new(50, 2), new(90, 2)];

        Assert.Equal(0, TextMatcher.Step(matches, current: 2, fromOffset: 0, FindDirection.Next));
        Assert.Equal(2, TextMatcher.Step(matches, current: 0, fromOffset: 0, FindDirection.Previous));
        Assert.Null(TextMatcher.Step([], current: null, fromOffset: 0, FindDirection.Next));
    }
}
