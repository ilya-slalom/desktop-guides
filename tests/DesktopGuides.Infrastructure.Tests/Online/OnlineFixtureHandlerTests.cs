using System.Net;
using DesktopGuides.Infrastructure.Online;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class OnlineFixtureHandlerTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("dg-online-handler-").FullName;

    public void Dispose() => Directory.Delete(root, true);

    private HttpClient Client()
    {
        File.WriteAllText(Path.Combine(root, "details.json"), "{\"ok\":1}");
        File.WriteAllText(Path.Combine(root, "query.json"), "{\"q\":1}");
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {"routes":[
              {"url":"https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/?itemcount=1&publishedfileids%5B0%5D=4242","status":200,"file":"details.json","contentType":"application/json"},
              {"url":"https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/?appid=620&search_text=x","status":200,"file":"query.json","contentType":"application/json"}
            ]}
            """);
        return new HttpClient(new OnlineFixtureHandler(OnlineFixtureRoutes.Load(Path.Combine(root, "manifest.json")), root));
    }

    [Fact]
    public async Task AFormPostMatchesItsBodyAsTheQuery()
    {
        using HttpClient client = Client();
        HttpResponseMessage response = await client.PostAsync(
            "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
            new FormUrlEncodedContent([new("itemcount", "1"), new("publishedfileids[0]", "4242")]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"ok\":1}", await response.Content.ReadAsStringAsync());
    }

    // The fake key differs between runs, so a route never names it.
    [Fact]
    public async Task TheKeyIsLeftOutOfTheMatch()
    {
        using HttpClient client = Client();
        HttpResponseMessage response = await client.GetAsync(
            "https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/?key=SECRET1&appid=620&search_text=x");
        Assert.Equal("{\"q\":1}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUnmatchedRequestIsLoggedWithoutItsQuery()
    {
        using HttpClient client = Client();
        HttpResponseMessage response = await client.GetAsync(
            "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=SECRET2&steamids=1");
        Assert.Equal(599, (int)response.StatusCode);
        string log = File.ReadAllText(Path.Combine(root, OnlineFixtureHandler.UnmatchedName));
        Assert.Contains("api.steampowered.com", log);
        Assert.Contains("/ISteamUser/GetPlayerSummaries/v2/", log);
        Assert.DoesNotContain("SECRET2", log);
        Assert.DoesNotContain("steamids", log);
    }
}
