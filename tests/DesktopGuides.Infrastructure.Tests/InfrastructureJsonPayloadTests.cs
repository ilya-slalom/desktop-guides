using System.Text;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

// T17.4: the exact JSON each payload has today, so moving to source
// generation can't change a byte, and stored data still reads.
public sealed class InfrastructureJsonPayloadTests
{
    private const string Snapshot =
        """{"schemaVersion":1,"summary":"A \u003Cgame\u003E \u0026 more.","firstReleaseDate":"1998-11-19","genres":["Shooter"],"developers":["Valve"],"publishers":["Sierra"],"platforms":["PC"],"providerUrl":"https://www.igdb.com/games/half-life","type":"MainGame","artworkSource":"IGDB"}""";

    private static GameMetadataSnapshot Example() => new(
        1, "A <game> & more.", new DateOnly(1998, 11, 19), ["Shooter"], ["Valve"], ["Sierra"], ["PC"],
        "https://www.igdb.com/games/half-life", GameTypeTag.MainGame, "IGDB");

    [Fact]
    public void MetadataJsonIsUnchanged()
    {
        Assert.Equal(Snapshot, GameMetadataJson.Serialize(Example()));
    }

    [Fact]
    public void StoredMetadataStillReads()
    {
        GameMetadataSnapshot? read = GameMetadataJson.TryParse(Snapshot);

        Assert.NotNull(read);
        Assert.Equal(Example() with { Genres = read!.Genres, Developers = read.Developers, Publishers = read.Publishers, Platforms = read.Platforms }, read);
        Assert.Equal(["Shooter"], read.Genres);
        Assert.Equal(["PC"], read.Platforms);
    }

    [Fact]
    public void MetadataWithoutArtworkSourceStillReads()
    {
        GameMetadataSnapshot? read = GameMetadataJson.TryParse(Snapshot.Replace(""","artworkSource":"IGDB"}""", "}"));

        Assert.NotNull(read);
        Assert.Null(read!.ArtworkSource);
    }

    [Fact]
    public void MetadataWithANumericTypeIsRejected()
    {
        Assert.Null(GameMetadataJson.TryParse(Snapshot.Replace("\"MainGame\"", "0")));
    }

    [Fact]
    public void FileOperationManifestJsonIsUnchanged()
    {
        Guid operation = Guid.Parse("11111111111111111111111111111111");
        Guid guide = Guid.Parse("22222222222222222222222222222222");

        Assert.Equal(
            """{"schemaVersion":1,"guideIds":["22222222222222222222222222222222"],"ownedPaths":[".staging/11111111111111111111111111111111/22222222222222222222222222222222","content/22222222222222222222222222222222"]}""",
            FileOperationManifest.Create(FileOperationKind.Import, operation, [guide]));
    }

    [Fact]
    public void CredentialBlobBytesAreUnchanged()
    {
        byte[] blob = ProviderCredentialBlob.Format(new ProviderCredentials(new IgdbCredentials("id", "secret"), "key"));

        Assert.Equal(
            """{"v":1,"igdb":{"clientId":"id","clientSecret":"secret"},"steamGridDbKey":"key"}""",
            Encoding.UTF8.GetString(blob));
    }
}
