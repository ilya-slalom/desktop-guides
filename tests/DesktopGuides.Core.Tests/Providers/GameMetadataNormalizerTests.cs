using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests.Providers;

public sealed class GameMetadataNormalizerTests
{
    [Theory]
    [InlineData("1942", "1942")]
    [InlineData(" 7 ", "7")]
    public void AcceptsCanonicalPositiveIds(string raw, string expected) =>
        Assert.Equal(expected, GameMetadataNormalizer.NormalizeExternalId(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("007")]
    [InlineData("12a")]
    [InlineData("123456789012345678901")]
    public void RejectsInvalidIds(string? raw)
    {
        ProviderException error = Assert.Throws<ProviderException>(
            () => GameMetadataNormalizer.NormalizeExternalId(raw));
        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
    }

    [Fact]
    public void TitleStripsControlCharactersAndIsCappedAt160()
    {
        string title = GameMetadataNormalizer.NormalizeTitle("Half\u0000-Life‮ " + new string('x', 300));
        Assert.StartsWith("Half-Life ", title);
        Assert.Equal(160, title.Length);
        Assert.Throws<ProviderException>(() => GameMetadataNormalizer.NormalizeTitle(" \u0007 "));
    }

    [Fact]
    public void TextKeepsNewlinesAndTruncatesToLimit()
    {
        string? text = GameMetadataNormalizer.NormalizeText("a\r\nb\u0001" + new string('c', 5000), 4000);
        Assert.StartsWith("a\nb", text);
        Assert.Equal(4000, text!.Length);
        Assert.Null(GameMetadataNormalizer.NormalizeText("  ", 4000));
    }

    [Fact]
    public void ListsDropBlanksAndDuplicatesAndCapCountAndLength()
    {
        IEnumerable<string?> raw = new[] { "PC", "pc", " ", null, new string('p', 90) }
            .Concat(Enumerable.Range(0, 30).Select(i => $"P{i}"));
        IReadOnlyList<string> list = GameMetadataNormalizer.NormalizeList(raw);
        Assert.Equal(16, list.Count);
        Assert.Equal("PC", list[0]);
        Assert.Equal(80, list[1].Length);
        Assert.DoesNotContain("pc", list);
    }

    [Theory]
    [InlineData("Main Game", false, GameTypeTag.MainGame)]
    [InlineData("Remaster", false, GameTypeTag.Remaster)]
    [InlineData("Remake", false, GameTypeTag.Remake)]
    [InlineData("Port", false, GameTypeTag.Port)]
    [InlineData("Expanded Game", false, GameTypeTag.Edition)]
    [InlineData("Main Game", true, GameTypeTag.Edition)]
    [InlineData("Expansion", false, GameTypeTag.Expansion)]
    [InlineData("Standalone Expansion", false, GameTypeTag.Expansion)]
    [InlineData("DLC", false, GameTypeTag.Expansion)]
    [InlineData("Bundle", false, GameTypeTag.Bundle)]
    [InlineData("Pack / Addon", false, GameTypeTag.Bundle)]
    [InlineData(null, false, GameTypeTag.Other)]
    [InlineData("Mod", false, GameTypeTag.Other)]
    public void MapsIgdbGameTypes(string? type, bool hasParent, GameTypeTag expected) =>
        Assert.Equal(expected, GameMetadataNormalizer.MapType(type, hasParent));

    [Theory]
    [InlineData("https://www.igdb.com/games/half-life", "https://www.igdb.com/games/half-life")]
    [InlineData("http://www.igdb.com/games/half-life", null)]
    [InlineData("https://evil.example/games/x", null)]
    [InlineData("https://www.igdb.com/companies/valve", null)]
    [InlineData("javascript:alert(1)", null)]
    public void ProviderUrlOnlyAllowsIgdbGamePages(string raw, string? expected) =>
        Assert.Equal(expected, GameMetadataNormalizer.NormalizeProviderUrl(raw));

    [Fact]
    public void ManualGamesHaveNoProviderFields()
    {
        Game game = new(Guid.NewGuid(), "T", null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Assert.Null(game.Link);
        Assert.Null(game.Metadata);
        Assert.Null(game.ArtworkRelativePath);
    }
}
