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

    [Fact]
    public void ABlockEdgeMatchesASpace()
    {
        // Two table cells, "Boss" and "Hydra", with no whitespace between.
        TextMatch match = Assert.Single(TextMatcher.FindAll("BossHydra", new FindQuery("boss hydra"), breaks: [4]));

        Assert.Equal(new TextMatch(0, 9), match);
    }

    [Fact]
    public void ABlockEdgeSplitsWords() =>
        Assert.Empty(TextMatcher.FindAll("BossHydra", new FindQuery("sshy"), breaks: [4]));

    [Fact]
    public void ABlockEdgeBesideWhitespaceIsOneSpace()
    {
        Assert.Single(TextMatcher.FindAll("Boss\nHydra", new FindQuery("boss hydra"), breaks: [4]));
        Assert.Single(TextMatcher.FindAll("Boss\nHydra", new FindQuery("boss hydra"), breaks: [5]));
        Assert.Single(TextMatcher.FindAll("Boss\nHydra", new FindQuery("boss hydra"), breaks: [4, 5]));
    }

    [Fact]
    public void BreaksAtTheEdgesChangeNothing() =>
        Assert.Equal(
            [new TextMatch(0, 4)],
            TextMatcher.FindAll("Boss", new FindQuery("boss"), breaks: [0, 4]));

    [Fact]
    public void MaskedTextNeverMatches() =>
        Assert.Empty(TextMatcher.FindAll("A\0\0\0\0\0\0B", new FindQuery("a b")));

    // Hidden HTML text is masked with NUL; it renders as nothing, so the
    // text either side of it joins.
    [Fact]
    public void MaskedTextIsSkippedSoTheTextAroundItJoins()
    {
        const string text = "the target line.\n\0\0\0\0\0\0MARK-0421  Step";
        TextMatch match = Assert.Single(TextMatcher.FindAll(text, new FindQuery("target line. mark-0421 step")));

        Assert.Equal(new TextMatch(4, text.Length - 4), match);
    }

    [Fact]
    public void WhitespaceEitherSideOfMaskedTextIsOneSpace() =>
        Assert.Single(TextMatcher.FindAll("open \0\0\0 door", new FindQuery("open door")));
}
