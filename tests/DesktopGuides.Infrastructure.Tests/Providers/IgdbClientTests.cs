using System.Net;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class IgdbClientTests
{
    private static readonly IgdbCredentials Credentials = new("client-id", "very-secret");

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Providers", "Fixtures", name));

    private static HttpResponseMessage Token(string token) =>
        FakeHandler.Json($$"""{"access_token":"{{token}}","expires_in":3600}""");

    private static (IgdbClient Client, FakeHandler Handler) Create(
        IgdbCredentials? credentials, params HttpResponseMessage[] responses)
    {
        FakeHandler handler = FakeHandler.Returning(responses);
        ProviderHttp http = new(handler);
        return (new IgdbClient(http, new TwitchTokenSource(http), _ => Task.FromResult(credentials)), handler);
    }

    [Fact]
    public void SearchBodyEscapesQuotesAndBackslashesAndKeepsUnicode()
    {
        Assert.Equal(
            "search \"Pokémon: \\\"Let's Go\\\" \\\\ Eevee\"; " +
            "fields name,first_release_date,game_type.type,version_parent,platforms.name,cover.image_id; limit 20;",
            IgdbClient.BuildSearchBody("  Pokémon: \"Let's Go\" \\ Eevee\n "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0007")]
    public async Task BlankQueryIsRejectedWithoutARequest(string query)
    {
        (IgdbClient client, FakeHandler handler) = Create(Credentials);
        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchAsync(query, default));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchAsync(new string('a', 101), default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MissingCredentialsAreNotConfiguredWithoutARequest()
    {
        (IgdbClient client, FakeHandler handler) = Create(null);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.SearchAsync("zelda", default));
        Assert.Equal(ProviderErrorKind.NotConfigured, error.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SearchSendsHeadersAndMapsValidResultsOnly()
    {
        (IgdbClient client, FakeHandler handler) = Create(Credentials, Token("tok"), FakeHandler.Json(Fixture("igdb-search.json")));

        IReadOnlyList<ProviderSearchResult> results = await client.SearchAsync("half-life", default);

        (HttpRequestMessage request, string? body) = handler.Requests[1];
        Assert.Equal("https://api.igdb.com/v4/games", request.RequestUri!.AbsoluteUri);
        Assert.Equal("client-id", Assert.Single(request.Headers.GetValues("Client-ID")));
        Assert.Equal("Bearer tok", request.Headers.Authorization!.ToString());
        Assert.EndsWith("limit 20;", body);
        Assert.Equal(2, results.Count);
        Assert.Equal(new ProviderSearchResult("231", "Half-Life", 1998,
            ["PC (Microsoft Windows)", "PlayStation 2"], GameTypeTag.MainGame,
            "https://images.igdb.com/igdb/image/upload/t_thumb/co1abc.jpg"),
            results[0], new SearchResultComparer());
        Assert.Equal(GameTypeTag.Edition, results[1].Type);
        Assert.Null(results[1].ThumbnailUrl);
    }

    [Fact]
    public async Task GetMapsSnapshotAndArtworkHints()
    {
        (IgdbClient client, FakeHandler handler) = Create(Credentials, Token("tok"), FakeHandler.Json(Fixture("igdb-game.json")));

        ProviderGameRecord record = await client.GetAsync("231", default);

        Assert.Contains("where id = 231;", handler.Requests[1].Body);
        Assert.Equal("231", record.ExternalId);
        Assert.Equal("Half-Life", record.Title);
        Assert.Equal("Gordon Freeman fights.\nAgain.", record.Snapshot.Summary);
        Assert.Equal(new DateOnly(1998, 11, 19), record.Snapshot.FirstReleaseDate);
        Assert.Equal(["Shooter"], record.Snapshot.Genres);
        Assert.Equal(["Valve"], record.Snapshot.Developers);
        Assert.Equal(["Sierra Entertainment"], record.Snapshot.Publishers);
        Assert.Equal("https://www.igdb.com/games/half-life", record.Snapshot.ProviderUrl);
        Assert.Equal(new ArtworkHints("Half-Life", "70", "co1abc"), record.Hints);
    }

    [Fact]
    public async Task Unauthorized401RefreshesTokenOnceThenSucceeds()
    {
        (IgdbClient client, FakeHandler handler) = Create(Credentials,
            Token("old"), new HttpResponseMessage(HttpStatusCode.Unauthorized),
            Token("new"), FakeHandler.Json("[]"));

        Assert.Empty(await client.SearchAsync("zelda", default));
        Assert.Equal("Bearer new", handler.Requests[3].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Second401IsInvalidCredentials()
    {
        (IgdbClient client, _) = Create(Credentials,
            Token("old"), new HttpResponseMessage(HttpStatusCode.Unauthorized),
            Token("new"), new HttpResponseMessage(HttpStatusCode.Unauthorized));

        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.SearchAsync("zelda", default));
        Assert.Equal(ProviderErrorKind.InvalidCredentials, error.Kind);
        Assert.DoesNotContain("very-secret", error.ToString());
        Assert.DoesNotContain("new", error.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("[{\"id\":\"231\",\"name\":\"x\"}]")]
    public async Task MalformedGetResponseIsMalformedData(string json)
    {
        (IgdbClient client, _) = Create(Credentials, Token("tok"), FakeHandler.Json(json));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.GetAsync("231", default));
        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
    }

    [Fact]
    public async Task EmptyGetResponseIsMalformedData()
    {
        (IgdbClient client, _) = Create(Credentials, Token("tok"), FakeHandler.Json("[]"));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.GetAsync("231", default));
        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
    }

    [Fact]
    public async Task ServerErrorIsUnavailable()
    {
        (IgdbClient client, _) = Create(Credentials, Token("tok"), new HttpResponseMessage(HttpStatusCode.BadGateway));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.SearchAsync("zelda", default));
        Assert.Equal(ProviderErrorKind.Unavailable, error.Kind);
    }

    [Fact]
    public async Task TestConnectionForcesAFreshTokenAndSendsOneLimitOneQuery()
    {
        (IgdbClient client, FakeHandler handler) = Create(Credentials,
            Token("t1"), FakeHandler.Json("[]"), Token("t2"), FakeHandler.Json("[]"));

        await client.TestConnectionAsync(default);
        await client.TestConnectionAsync(default);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("fields id; limit 1;", handler.Requests[1].Body);
        Assert.Equal("Bearer t2", handler.Requests[3].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task TestConnectionWithRejectedCredentialsHidesTheSecret()
    {
        (IgdbClient client, _) = Create(Credentials, new HttpResponseMessage(HttpStatusCode.BadRequest));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.TestConnectionAsync(default));
        Assert.Equal(ProviderErrorKind.InvalidCredentials, error.Kind);
        Assert.DoesNotContain("very-secret", error.ToString());
    }

    private sealed class SearchResultComparer : IEqualityComparer<ProviderSearchResult>
    {
        public bool Equals(ProviderSearchResult? x, ProviderSearchResult? y) =>
            x is not null && y is not null && x.ExternalId == y.ExternalId && x.Title == y.Title &&
            x.ReleaseYear == y.ReleaseYear && x.Platforms.SequenceEqual(y.Platforms) &&
            x.Type == y.Type && x.ThumbnailUrl == y.ThumbnailUrl;

        public int GetHashCode(ProviderSearchResult obj) => obj.ExternalId.GetHashCode();
    }
}
