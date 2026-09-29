using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class GameMetadataJsonTests
{
    internal static GameMetadataSnapshot Sample() => new(
        GameMetadataSnapshot.CurrentSchemaVersion, "A summary.", new DateOnly(1998, 11, 19),
        ["Shooter"], ["Valve"], ["Sierra"], ["PC (Microsoft Windows)"],
        "https://www.igdb.com/games/half-life", GameTypeTag.MainGame);

    [Fact]
    public void RoundTripsEveryField()
    {
        GameMetadataSnapshot parsed = GameMetadataJson.TryParse(GameMetadataJson.Serialize(Sample()))!;
        Assert.Equal(Sample() with { Genres = parsed.Genres, Developers = parsed.Developers,
            Publishers = parsed.Publishers, Platforms = parsed.Platforms }, parsed);
        Assert.Equal(["Shooter"], parsed.Genres);
        Assert.Equal(["PC (Microsoft Windows)"], parsed.Platforms);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"schemaVersion":2,"genres":[],"developers":[],"publishers":[],"platforms":[],"type":"MainGame"}""")]
    [InlineData("""{"schemaVersion":1,"genres":null,"developers":[],"publishers":[],"platforms":[],"type":"MainGame"}""")]
    [InlineData("""{"schemaVersion":1,"genres":[],"developers":[],"publishers":[],"platforms":[],"type":"Nope"}""")]
    public void CorruptOrFutureJsonParsesAsNull(string? json) =>
        Assert.Null(GameMetadataJson.TryParse(json));

    [Fact]
    public void OversizedSnapshotIsRejectedOnSerialize() =>
        Assert.Throws<ArgumentException>(() => GameMetadataJson.Serialize(
            Sample() with { Summary = new string('©', 20000) }));
}
