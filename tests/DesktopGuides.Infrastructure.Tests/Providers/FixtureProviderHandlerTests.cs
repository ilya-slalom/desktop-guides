using System.Net;
using System.Text;
using System.Text.Json;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

// T17.2 D: the fake-provider lane's handler, driven through the real ProviderHttp.
public sealed class FixtureProviderHandlerTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "dg-fixtures-" + Guid.NewGuid().ToString("N"));

    public FixtureProviderHandlerTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private ProviderHttp Http(object manifest, params (string Name, string Text)[] files)
    {
        File.WriteAllText(Path.Combine(folder, FixtureProviderHandler.ManifestName), JsonSerializer.Serialize(manifest));
        foreach ((string name, string text) in files) File.WriteAllText(Path.Combine(folder, name), text);
        return new ProviderHttp(new FixtureProviderHandler(folder));
    }

    private static Task<ProviderResponse> Post(ProviderHttp http, string uri, string body) =>
        http.SendAsync(HttpMethod.Post, new Uri(uri),
            request => request.Content = new StringContent(body, Encoding.UTF8, "text/plain"),
            ProviderHttp.MaxJsonBytes, CancellationToken.None);

    [Fact]
    public async Task RoutesMatchOnMethodHostPathAndContainsAndTheFirstMatchWins()
    {
        using ProviderHttp http = Http(new
        {
            routes = new object[]
            {
                new { method = "POST", host = "api.igdb.com", path = "/v4/games", contains = "search \"Half-Life\"", status = 200, body = "search.json" },
                new { method = "POST", host = "api.igdb.com", path = "/v4/games", contains = "where id = ", status = 200, body = "game.json" },
                new { method = "POST", host = "api.igdb.com", path = "/v4/games", status = 200, body = "fallback.json" },
                new { method = "POST", host = "api.igdb.com", path = "/v4/games", status = 500 },
            },
        }, ("search.json", "[1]"), ("game.json", "[2]"), ("fallback.json", "[3]"));

        Assert.Equal("[1]", Encoding.UTF8.GetString((await Post(http, "https://api.igdb.com/v4/games", "search \"Half-Life\"; fields name;")).Body));
        Assert.Equal("[2]", Encoding.UTF8.GetString((await Post(http, "https://api.igdb.com/v4/games", "fields name; where id = 231;")).Body));
        ProviderResponse other = await Post(http, "https://api.igdb.com/v4/games", "fields id; limit 1;");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal("[3]", Encoding.UTF8.GetString(other.Body));
    }

    [Fact]
    public async Task AnUnmatchedRequestGets599AndIsRecordedWithoutQueryOrHeaders()
    {
        using ProviderHttp http = Http(new { routes = Array.Empty<object>() });

        ProviderResponse response = await http.SendAsync(HttpMethod.Get,
            new Uri("https://www.steamgriddb.com/api/v2/grids/game/9?secret=abc123"),
            request => request.Headers.Add("Authorization", "Bearer key-456"), ProviderHttp.MaxJsonBytes, CancellationToken.None);

        Assert.Equal((HttpStatusCode)599, response.StatusCode);
        string recorded = File.ReadAllText(Path.Combine(folder, FixtureProviderHandler.UnmatchedName));
        Assert.Contains("/api/v2/grids/game/9", recorded);
        Assert.Contains("www.steamgriddb.com", recorded);
        Assert.DoesNotContain("abc123", recorded);
        Assert.DoesNotContain("key-456", recorded);
    }

    [Fact]
    public async Task ARouteWithTimesAnswersOnlyThatOftenSoARetryReachesTheNext()
    {
        using ProviderHttp http = Http(new
        {
            routes = new object[]
            {
                new { method = "GET", host = "www.steamgriddb.com", path = "/api/v2/grids/steam/70", status = 429, retryAfterSeconds = 0, times = 1 },
                new { method = "GET", host = "www.steamgriddb.com", path = "/api/v2/grids/steam/70", status = 200, body = "grids.json" },
            },
        }, ("grids.json", "{\"success\":true,\"data\":[]}"));

        ProviderResponse response = await http.SendAsync(HttpMethod.Get,
            new Uri("https://www.steamgriddb.com/api/v2/grids/steam/70?dimensions=600x900"), _ => { },
            ProviderHttp.MaxJsonBytes, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"success\":true,\"data\":[]}", Encoding.UTF8.GetString(response.Body));
    }

    [Fact]
    public async Task AHostOutsideTheAllowListIsRefusedBeforeTheHandler()
    {
        using ProviderHttp http = Http(new
        {
            routes = new object[] { new { method = "GET", host = "example.com", path = "/", status = 200 } },
        });

        await Assert.ThrowsAnyAsync<Exception>(() => http.SendAsync(HttpMethod.Get, new Uri("https://example.com/"),
            _ => { }, ProviderHttp.MaxJsonBytes, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(folder, FixtureProviderHandler.UnmatchedName)));
    }

    // Review Focus 1: a body file outside the fixture folder is never read.
    [Fact]
    public void ABodyPathOutsideTheFolderIsRefused()
    {
        File.WriteAllText(Path.Combine(folder, FixtureProviderHandler.ManifestName), JsonSerializer.Serialize(new
        {
            routes = new object[] { new { method = "GET", host = "api.igdb.com", path = "/x", status = 200, body = "..\\outside.json" } },
        }));

        Assert.Throws<InvalidDataException>(() => new FixtureProviderHandler(folder));
    }

    [Fact]
    public async Task ADelayedRouteHonoursCancellation()
    {
        using ProviderHttp http = Http(new
        {
            routes = new object[] { new { method = "POST", host = "api.igdb.com", path = "/v4/games", status = 200, body = "a.json", delayMilliseconds = 30000 } },
        }, ("a.json", "[]"));
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.SendAsync(HttpMethod.Post,
            new Uri("https://api.igdb.com/v4/games"), request => request.Content = new StringContent("x"),
            ProviderHttp.MaxJsonBytes, cancel.Token));
    }
}
