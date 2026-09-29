using DesktopGuides.Core.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests.Providers;

public sealed class GameMetadataPresentationTests
{
    [Theory]
    [InlineData(GameTypeTag.MainGame, "Main game")]
    [InlineData(GameTypeTag.Remaster, "Remaster")]
    [InlineData(GameTypeTag.Remake, "Remake")]
    [InlineData(GameTypeTag.Port, "Port")]
    [InlineData(GameTypeTag.Edition, "Edition")]
    [InlineData(GameTypeTag.Expansion, "Expansion")]
    [InlineData(GameTypeTag.Bundle, "Bundle")]
    [InlineData(GameTypeTag.Other, "Other")]
    public void TypeLabelsAreSentenceCase(GameTypeTag type, string expected) =>
        Assert.Equal(expected, GameMetadataPresentation.TypeLabel(type));

    [Fact]
    public void ResultSummaryShowsTypeAndYearWhenKnown()
    {
        ProviderSearchResult withYear = new("70", "Half-Life", 1998, ["PC (Windows)"], GameTypeTag.MainGame, null);
        Assert.Equal("Main game, 1998", GameMetadataPresentation.ResultSummary(withYear));
        Assert.Equal("Edition", GameMetadataPresentation.ResultSummary(withYear with { ReleaseYear = null, Type = GameTypeTag.Edition }));
    }

    [Fact]
    public void PlatformSummaryShowsThreeThenACount()
    {
        Assert.Null(GameMetadataPresentation.PlatformSummary([]));
        Assert.Equal("PC, PS2", GameMetadataPresentation.PlatformSummary(["PC", "PS2"]));
        Assert.Equal("PC, PS2, Xbox and 2 more",
            GameMetadataPresentation.PlatformSummary(["PC", "PS2", "Xbox", "Mac", "Linux"]));
    }

    [Fact]
    public void AttributionNamesEachSourceUsed()
    {
        GameMetadataSnapshot snapshot = new(1, null, null, [], [], [], [], null, GameTypeTag.MainGame);
        Assert.Equal(["Metadata from IGDB"], GameMetadataPresentation.Attribution(snapshot));
        Assert.Equal(["Metadata from IGDB", "Artwork from SteamGridDB"],
            GameMetadataPresentation.Attribution(snapshot with { ArtworkSource = "SteamGridDB" }));
        Assert.Equal(["Metadata and artwork from IGDB"],
            GameMetadataPresentation.Attribution(snapshot with { ArtworkSource = "IGDB" }));
    }

    [Fact]
    public void CompaniesNameDevelopersThenPublishers()
    {
        GameMetadataSnapshot snapshot = new(1, null, null, [], [], [], [], null, GameTypeTag.MainGame);
        Assert.Null(GameMetadataPresentation.Companies(snapshot));
        Assert.Equal("Developed by Valve. Published by Sierra, Valve.",
            GameMetadataPresentation.Companies(snapshot with { Developers = ["Valve"], Publishers = ["Sierra", "Valve"] }));
        Assert.Equal("Published by Sierra.",
            GameMetadataPresentation.Companies(snapshot with { Publishers = ["Sierra"] }));
    }
}
