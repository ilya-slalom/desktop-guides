using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class LibrarySearchTests
{
    [Theory]
    [InlineData("The Legend of Zelda", "zELDA")]
    [InlineData("The Legend of Zelda", "legend OF")]
    [InlineData("Pokémon Crystal", "pokemon")]
    [InlineData("Pokemon Crystal", "Pokémon")]
    [InlineData("Ōkami HD", "OKAMI")]
    [InlineData("FF Tactics", "ＦＦ")]
    [InlineData("ＦＦ Tactics", "ff")]
    [InlineData("ドラクエXI", "ドラクエ")]
    [InlineData("Ведьмак 3", "ВЕДЬМАК")]
    [InlineData("The Legend of Zelda", "  zelda  ")]
    public void MatchingIgnoresCaseAccentsAndWidth(string title, string query)
    {
        Assert.True(LibrarySearch.Matches(title, query));
    }

    [Theory]
    [InlineData("The Legend of Zelda", "Mario")]
    [InlineData("Pokémon Crystal", "pokemons")]
    [InlineData("ドラクエXI", "ドラゴン")]
    public void ADifferentTitleDoesNotMatch(string title, string query)
    {
        Assert.False(LibrarySearch.Matches(title, query));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankQueryMatchesEverything(string? query)
    {
        Assert.True(LibrarySearch.Matches("Anything", query));
    }

    [Fact]
    public void AGameTitleMatchNamesNoGuide()
    {
        LibraryGameSummary zelda = Summary("Zelda", "Zelda Walkthrough");

        LibrarySearchMatch match = Assert.Single(LibrarySearch.Filter([zelda], "zelda"));

        Assert.Same(zelda, match.Summary);
        Assert.Null(match.MatchedGuideTitle);
    }

    [Fact]
    public void AGuideOnlyMatchNamesTheFirstMatchingGuide()
    {
        LibraryGameSummary game = Summary("Zeta", "Achievements", "Main Walkthrough", "Side Walkthrough");

        LibrarySearchMatch match = Assert.Single(LibrarySearch.Filter([game], "walkthrough"));

        Assert.Equal("Main Walkthrough", match.MatchedGuideTitle);
    }

    [Fact]
    public void FilteringKeepsTheInputOrderAndDropsNonMatches()
    {
        LibraryGameSummary first = Summary("Zeta Quest");
        LibraryGameSummary other = Summary("Mario");
        LibraryGameSummary last = Summary("Alpha", "Quest Maps");

        IReadOnlyList<LibrarySearchMatch> matches = LibrarySearch.Filter([first, other, last], "quest");

        Assert.Equal([first, last], matches.Select(match => match.Summary));
    }

    [Fact]
    public void ABlankQueryKeepsEveryGameWithoutAGuide()
    {
        LibraryGameSummary first = Summary("Zeta", "Guide");
        LibraryGameSummary second = Summary("Alpha");

        IReadOnlyList<LibrarySearchMatch> matches = LibrarySearch.Filter([first, second], " ");

        Assert.Equal([first, second], matches.Select(match => match.Summary));
        Assert.All(matches, match => Assert.Null(match.MatchedGuideTitle));
    }

    [Fact]
    public void AnEmptyLibraryHasNoMatches()
    {
        Assert.Empty(LibrarySearch.Filter([], "zelda"));
    }

    private static LibraryGameSummary Summary(string title, params string[] guides) =>
        new(new Game(Guid.NewGuid(), title, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null),
            guides.Length, DateTimeOffset.UnixEpoch, guides);
}
