using System.Net;
using System.Web;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Online;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class SteamClientTests
{
    private const string Key = "FAKEKEY0123456789";
    private const string Query = "api.steampowered.com/IPublishedFileService/QueryFiles/v1/";
    private const string Details = "api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
    private const string Players = "api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/";
    private readonly SteamStub stub = new();

    private SteamClient Client(string? key = Key) => new(new ProviderHttp(stub), _ => Task.FromResult(key));

    [Fact]
    public async Task ASearchAsksForTheAppsGuidesWithTheKey()
    {
        stub.Json(Query, "query.json");
        stub.Json(Players, "players.json");

        SteamGuidePage page = await Client().QueryGuidesAsync("620", "field notes", 1, CancellationToken.None);

        var query = HttpUtility.ParseQueryString(stub.Requests[0].Uri.Query);
        Assert.Equal((Key, "620", "field notes", "10", "true", "20", "1"),
            (query["key"], query["appid"], query["search_text"], query["filetype"], query["return_details"], query["numperpage"], query["page"]));
        Assert.Equal(2, page.Total);
        Assert.Equal(
            [("4242", "Example Quest Field Notes", "Test Author"), ("4343", "Example Quest Speedrun Route", "Second Author")],
            page.Items.Select(item => (item.FileId, item.Title, item.Author)));
        Assert.Equal(new Uri("https://images.steamusercontent.com/ugc/1/PREVIEW/"), page.Items[0].Preview);
        Assert.Null(page.Items[1].Preview);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1720543880), page.Items[0].Updated);
        Assert.Equal("76561190000000001,76561190000000002", HttpUtility.ParseQueryString(stub.Requests[1].Uri.Query)["steamids"]);
    }

    [Fact]
    public async Task NoResultsIsAnEmptyPage()
    {
        stub.Json(Query, "query-empty.json");
        SteamGuidePage page = await Client().QueryGuidesAsync("620", "zzz", 1, CancellationToken.None);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task WithoutAKeyASearchSendsNothing()
    {
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(
            () => Client(key: null).QueryGuidesAsync("620", "x", 1, CancellationToken.None));
        Assert.Equal(ProviderErrorKind.NotConfigured, error.Kind);
        Assert.Empty(stub.Requests);
    }

    // Review Focus 2.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderErrorKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.Forbidden, ProviderErrorKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderErrorKind.Unavailable)]
    public async Task ARefusedSearchSaysWhyAndNeverShowsTheKey(HttpStatusCode status, ProviderErrorKind kind)
    {
        stub.Status(Query, status);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(
            () => Client().QueryGuidesAsync("620", "x", 1, CancellationToken.None));
        Assert.Equal(kind, error.Kind);
        Assert.DoesNotContain(Key, error.ToString());
    }

    // Review: without a key Steam answers guides with result 9, so details ask with the key.
    [Fact]
    public async Task DetailsAskWithTheKey()
    {
        stub.Json(Details, "details.json");
        SteamGuideSummary details = (await Client().GetDetailsAsync("4242", CancellationToken.None))!;

        Assert.Equal(HttpMethod.Post, stub.Requests[0].Method);
        Assert.Equal($"key={Key}&itemcount=1&publishedfileids%5B0%5D=4242", stub.Requests[0].Body);
        Assert.DoesNotContain("key=", stub.Requests[0].Uri.Query);
        Assert.Equal(("4242", "620", "Example Quest Field Notes", null), (details.FileId, details.AppId, details.Title, details.Author));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1720543880), details.Updated);
    }

    [Fact]
    public async Task AMissingGuideHasNoDetails()
    {
        stub.Json(Details, "details-missing.json");
        Assert.Null(await Client().GetDetailsAsync("9", CancellationToken.None));
    }

    [Fact]
    public async Task TheKeyTestIsOneQuery()
    {
        stub.Json(Query, "query-empty.json");
        await Client().TestKeyAsync(CancellationToken.None);
        Assert.Single(stub.Requests);
        stub.Status(Query, HttpStatusCode.Forbidden);
        Assert.Equal(ProviderErrorKind.InvalidCredentials,
            (await Assert.ThrowsAsync<ProviderException>(() => Client().TestKeyAsync(CancellationToken.None))).Kind);
    }

    [Fact]
    public async Task EveryRequestNamesTheApp()
    {
        stub.Json(Details, "details.json");
        await Client().GetDetailsAsync("4242", CancellationToken.None);
        string agent = Assert.Single(stub.Requests).UserAgent;
        Assert.StartsWith("DesktopGuides/", agent);
        Assert.EndsWith("(+https://github.com/ilyalissoboi/desktop-guides)", agent);
    }

    [Fact]
    public async Task WithoutAKeyDetailsAreNotAsked()
    {
        Assert.Null(await Client(key: null).GetDetailsAsync("4242", CancellationToken.None));
        Assert.Empty(stub.Requests);
    }

    // Review: a retry after a short limit resent a disposed form.
    [Fact]
    public async Task ARetriedDetailsRequestSendsItsFormAgain()
    {
        int calls = 0;
        stub.Routes[Details] = _ => ++calls == 1
            ? (HttpStatusCode.TooManyRequests, [])
            : (HttpStatusCode.OK, System.Text.Encoding.UTF8.GetBytes(P3Fixtures.Read("steam/details.json")));
        stub.RetryAfter = TimeSpan.FromMilliseconds(1);
        Assert.NotNull(await Client().GetDetailsAsync("4242", CancellationToken.None));
        Assert.All(stub.Requests, request => Assert.Contains("publishedfileids", request.Body));
    }

    private const string AppList = "api.steampowered.com/IStoreService/GetAppList/v1/";

    [Fact]
    public async Task TheGameListPagesThroughWithTheKey()
    {
        stub.Routes[AppList] = request => (HttpStatusCode.OK, System.Text.Encoding.UTF8.GetBytes(P3Fixtures.Read(
            HttpUtility.ParseQueryString(request.RequestUri!.Query)["last_appid"] == "1002" ? "steam/applist-2.json" : "steam/applist-1.json")));

        IReadOnlyList<(string AppId, string Name)> apps = await Client().GetAppListAsync(null, CancellationToken.None);

        Assert.Equal(["620", "1001", "1002", "1003", "2525"], apps.Select(app => app.AppId));
        var first = HttpUtility.ParseQueryString(stub.Requests[0].Uri.Query);
        Assert.Equal((Key, "true", "false", "false", "false", "false", "10000", "0"),
            (first["key"], first["include_games"], first["include_dlc"], first["include_software"], first["include_videos"],
             first["include_hardware"], first["max_results"], first["last_appid"]));
        // Review: a page must arrive within one request's timeout, so pages are small.
        Assert.Equal("10000", first["max_results"]);
        Assert.Equal("1002", HttpUtility.ParseQueryString(stub.Requests[1].Uri.Query)["last_appid"]);
    }

    [Fact]
    public async Task ALimitPartWayThroughTheListStopsIt()
    {
        stub.Routes[AppList] = request => HttpUtility.ParseQueryString(request.RequestUri!.Query)["last_appid"] == "1002"
            ? (HttpStatusCode.TooManyRequests, [])
            : (HttpStatusCode.OK, System.Text.Encoding.UTF8.GetBytes(P3Fixtures.Read("steam/applist-1.json")));
        Assert.Equal(ProviderErrorKind.RateLimited, (await Assert.ThrowsAsync<ProviderException>(
            () => Client().GetAppListAsync(null, CancellationToken.None))).Kind);
    }

    // Review: a list cut off by the page cap isn't kept as if whole.
    [Fact]
    public async Task AListLongerThanThePageCapIsRefused()
    {
        int page = 0;
        stub.Routes[AppList] = _ => (HttpStatusCode.OK, System.Text.Encoding.UTF8.GetBytes(
            $"{{\"response\":{{\"apps\":[{{\"appid\":{++page},\"name\":\"G{page}\"}}],\"have_more_results\":true,\"last_appid\":{page}}}}}"));
        SteamClient client = new(new ProviderHttp(stub), _ => Task.FromResult<string?>(Key), (_, _) => Task.CompletedTask);
        Assert.Equal(ProviderErrorKind.MalformedData, (await Assert.ThrowsAsync<ProviderException>(
            () => client.GetAppListAsync(null, CancellationToken.None))).Kind);
    }
}
