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

    [Fact]
    public async Task DetailsNeedNoKey()
    {
        stub.Json(Details, "details.json");
        SteamGuideSummary details = (await Client(key: null).GetDetailsAsync("4242", CancellationToken.None))!;

        Assert.Equal(HttpMethod.Post, stub.Requests[0].Method);
        Assert.Equal("itemcount=1&publishedfileids%5B0%5D=4242", stub.Requests[0].Body);
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
        Assert.StartsWith("DesktopGuides/", Assert.Single(stub.Requests).UserAgent);
    }
}
