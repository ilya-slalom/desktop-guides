using System.Net;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class ArtworkSourceTests
{
    private const string Grid = """
        {"success":true,"data":[{"id":1,"url":"https://cdn2.steamgriddb.com/grid/a.png",
          "thumb":"https://cdn2.steamgriddb.com/thumb/a.png","width":600,"height":900,"mime":"image/png"}]}
        """;

    private static (SteamGridDbArtworkSource Source, FakeHandler Handler) Create(string? key, params HttpResponseMessage[] responses)
    {
        FakeHandler handler = FakeHandler.Returning(responses);
        return (new SteamGridDbArtworkSource(new ProviderHttp(handler), _ => Task.FromResult(key)), handler);
    }

    [Fact]
    public async Task SteamIdLookupUsesBearerKeyAndReturnsFirstGrid()
    {
        (SteamGridDbArtworkSource source, FakeHandler handler) = Create("sgdb-key", FakeHandler.Json(Grid));

        ArtworkCandidate? candidate = await source.FindAsync(new ArtworkHints("Half-Life", "70", null), default);

        Assert.Equal(new ArtworkCandidate(new Uri("https://cdn2.steamgriddb.com/grid/a.png"), "SteamGridDB"), candidate);
        HttpRequestMessage request = Assert.Single(handler.Requests).Request;
        Assert.Equal("https://www.steamgriddb.com/api/v2/grids/steam/70?dimensions=600x900", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer sgdb-key", request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task MissingSteamMatchFallsBackToExactNameMatch()
    {
        (SteamGridDbArtworkSource source, FakeHandler handler) = Create("sgdb-key",
            FakeHandler.Json("""{"success":false,"errors":["Game not found"]}""", HttpStatusCode.NotFound),
            FakeHandler.Json("""{"success":true,"data":[{"id":5,"name":"Half-Life 2"},{"id":6,"name":"half-life"}]}"""),
            FakeHandler.Json(Grid));

        ArtworkCandidate? candidate = await source.FindAsync(new ArtworkHints("Half-Life", "70", null), default);

        Assert.NotNull(candidate);
        Assert.Equal("https://www.steamgriddb.com/api/v2/search/autocomplete/Half-Life", handler.Requests[1].Request.RequestUri!.AbsoluteUri);
        Assert.Equal("https://www.steamgriddb.com/api/v2/grids/game/6?dimensions=600x900", handler.Requests[2].Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task NoExactNameMatchReturnsNull()
    {
        (SteamGridDbArtworkSource source, FakeHandler handler) = Create("sgdb-key",
            FakeHandler.Json("""{"success":true,"data":[{"id":5,"name":"Half-Life 2"}]}"""));

        Assert.Null(await source.FindAsync(new ArtworkHints("Half-Life", null, null), default));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NoKeyReturnsNullWithoutARequest()
    {
        (SteamGridDbArtworkSource source, FakeHandler handler) = Create(null);
        Assert.Null(await source.FindAsync(new ArtworkHints("Half-Life", "70", null), default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EmptyGridAndOffListUrlsReturnNull()
    {
        (SteamGridDbArtworkSource source, _) = Create("sgdb-key",
            FakeHandler.Json("""{"success":true,"data":[{"id":1,"url":"https://evil.example/a.png"},{"id":2,"url":"http://cdn2.steamgriddb.com/a.png"}]}"""),
            FakeHandler.Json("""{"success":true,"data":[]}"""));
        Assert.Null(await source.FindAsync(new ArtworkHints("Half-Life", "70", null), default));
    }

    [Fact]
    public async Task RejectedKeyIsInvalidCredentialsWithoutTheKey()
    {
        (SteamGridDbArtworkSource source, _) = Create("sgdb-key", new HttpResponseMessage(HttpStatusCode.Unauthorized));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(
            () => source.FindAsync(new ArtworkHints("Half-Life", "70", null), default));
        Assert.Equal(ProviderErrorKind.InvalidCredentials, error.Kind);
        Assert.DoesNotContain("sgdb-key", error.ToString());
    }

    [Fact]
    public async Task IgdbCoverUsesCoverBigSize()
    {
        IgdbCoverArtworkSource source = new();
        Assert.Equal(new ArtworkCandidate(new Uri("https://images.igdb.com/igdb/image/upload/t_cover_big/co1abc.jpg"), "IGDB"),
            await source.FindAsync(new ArtworkHints("Half-Life", null, "co1abc"), default));
        Assert.Null(await source.FindAsync(new ArtworkHints("Half-Life", null, null), default));
    }
}
