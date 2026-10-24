using System.Net;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Online;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class VgMapsIndexTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("dg-vgmaps-").FullName;
    private readonly Clock clock = new();
    private int fetches;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public void Dispose() => Directory.Delete(root, true);

    private string File => Path.Combine(root, "vgmaps-index.json");

    private Task<IReadOnlyList<VgMapsGame>> Fetch(CancellationToken token)
    {
        fetches++;
        return Task.FromResult(VgMapsIndex.Parse(VgMapsClient.Decode(P3Bytes.Read("vgmaps/index.htm"))));
    }

    private async Task<VgMapsIndex> LoadedAsync(bool refresh = false)
    {
        VgMapsIndex index = new(File, clock);
        await index.LoadAsync(Fetch, refresh, CancellationToken.None);
        return index;
    }

    [Fact]
    public void TheIndexListsGamesNotHeadings()
    {
        IReadOnlyList<VgMapsGame> games = VgMapsIndex.Parse(VgMapsClient.Decode(P3Bytes.Read("vgmaps/index.htm")));
        Assert.Equal(
            [
                ("Example Quest", "Example Quest (PSX)", "PSX/index.htm", "ExampleQuest"),
                ("Online Test Game", "Online Test Game (PSX)", "PSX/index.htm", "OnlineTestGame"),
                ("Online Test Game", "Online Test Game (NES)", "NES/index.htm", "OnlineTestGame"),
                ("Pac-Man", "Pac-Man (Arcade)", "Arcade/index.htm", "PacMan"),
                ("Pac-Man", "Pac-Man (Namco - NES)", "NES/index.htm", "PacManNamco"),
                ("Quest II", "Quest II (NES)", "NES/index.htm", "QuestII"),
                ("Symbol Quest™", "Symbol Quest™ (PSX)", "PSX/index.htm", "SymbolQuest"),
                ("Missing Game", "Missing Game (PSX)", "PSX/index.htm", "MissingGame"),
            ],
            games.Select(game => (game.Title, game.Label, game.SystemPath, game.Anchor)));
    }

    [Fact]
    public async Task SearchRanksAsSteamsDoes()
    {
        VgMapsIndex index = await LoadedAsync();
        Assert.Equal(["Online Test Game (PSX)", "Online Test Game (NES)"], index.Search("online test game").Select(game => game.Label));
        Assert.Equal(["Quest II (NES)", "Symbol Quest™ (PSX)", "Example Quest (PSX)"], index.Search("quest").Select(game => game.Label));
        Assert.Equal("SymbolQuest", Assert.Single(index.Search("symbol quest")).Anchor);
    }

    [Fact]
    public async Task AFreshIndexIsKeptAndAnOldOneFetchedAgain()
    {
        await LoadedAsync();
        clock.Now += TimeSpan.FromDays(29);
        await LoadedAsync();
        Assert.Equal(1, fetches);
        clock.Now += TimeSpan.FromDays(2);
        await LoadedAsync();
        Assert.Equal(2, fetches);
        await LoadedAsync(refresh: true);
        Assert.Equal(3, fetches);
    }

    [Fact]
    public async Task AFailedFetchKeepsNothing()
    {
        VgMapsIndex index = new(File, clock);
        await Assert.ThrowsAsync<ProviderException>(() => index.LoadAsync(
            _ => throw new ProviderException(ProviderErrorKind.RateLimited, "busy"), false, CancellationToken.None));
        Assert.False(System.IO.File.Exists(File));
        Assert.Empty(index.Search("online"));
    }

    [Fact]
    public async Task TheClientFetchesTheIndexAndDecodesItsCodePage()
    {
        SteamStub stub = new();
        stub.Routes["vgmaps.com/Atlas/index.htm"] = _ => (HttpStatusCode.OK, P3Bytes.Read("vgmaps/index.htm"));
        VgMapsClient client = new(new ProviderHttp(stub), (_, _) => Task.CompletedTask);
        IReadOnlyList<VgMapsGame> games = VgMapsIndex.Parse(await client.GetIndexAsync(CancellationToken.None));
        Assert.Contains(games, game => game.Title == "Symbol Quest™");
        Assert.StartsWith("DesktopGuides/", Assert.Single(stub.Requests).UserAgent);
    }

    [Fact]
    public async Task ALimitStopsTheClient()
    {
        SteamStub stub = new();
        stub.Status("vgmaps.com/Atlas/index.htm", HttpStatusCode.TooManyRequests);
        VgMapsClient client = new(new ProviderHttp(stub), (_, _) => Task.CompletedTask);
        Assert.Equal(ProviderErrorKind.RateLimited,
            (await Assert.ThrowsAsync<ProviderException>(() => client.GetIndexAsync(CancellationToken.None))).Kind);
    }
}
