using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Online;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class SteamAppIndexTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("dg-steam-apps-").FullName;
    private readonly Clock clock = new();
    private int fetches;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public void Dispose() => Directory.Delete(root, true);

    private string File => Path.Combine(root, "steam-apps.json");

    private static readonly IReadOnlyList<(string, string)> Apps =
    [
        ("620", "Portal 2"),
        ("1001", "Online Test Game II"),
        ("1002", "Online Test Game Remastered"),
        ("1003", "The Online Test Game Collection"),
        ("1004", "Online Test Game"),
        ("1005", "Online: Test-Game"),
        ("2525", "FINAL FANTASY VII REBIRTH™"),
        ("2526", "Final Fantasy VII Rebirth Soundtrack"),
    ];

    private Task<IReadOnlyList<(string, string)>> Fetch(CancellationToken token)
    {
        fetches++;
        return Task.FromResult(Apps);
    }

    private async Task<SteamAppIndex> LoadedAsync(bool refresh = false)
    {
        SteamAppIndex index = new(File, clock);
        await index.LoadAsync(Fetch, refresh, CancellationToken.None);
        return index;
    }

    [Fact]
    public async Task MatchesRankExactThenPrefixThenAllWords()
    {
        SteamAppIndex index = await LoadedAsync();
        Assert.Equal(
            ["1004", "1005", "1001", "1002", "1003"],
            index.Match("online test game").Select(app => app.AppId));
    }

    [Fact]
    public async Task MarksAndCaseDontMatter()
    {
        SteamAppIndex index = await LoadedAsync();
        Assert.Equal("2525", index.ExactSingle("Final Fantasy VII Rebirth")?.AppId);
        Assert.Equal("FINAL FANTASY VII REBIRTH™", index.NameOf("2525"));
    }

    [Fact]
    public async Task OnlyASingleExactMatchPicksItself()
    {
        SteamAppIndex index = await LoadedAsync();
        // "Online Test Game" and "Online: Test-Game" are both exact once punctuation goes.
        Assert.Null(index.ExactSingle("Online Test Game"));
        Assert.Null(index.ExactSingle("Online Test"));
        Assert.Equal("620", index.ExactSingle("portal 2")?.AppId);
    }

    [Fact]
    public async Task AtMostTwentyMatches()
    {
        SteamAppIndex index = new(File, clock);
        await index.LoadAsync(_ => Task.FromResult<IReadOnlyList<(string, string)>>(
            Enumerable.Range(1, 50).Select(n => (n.ToString(), $"Quest {n}")).ToArray()), false, CancellationToken.None);
        Assert.Equal(20, index.Match("quest").Count);
    }

    [Fact]
    public async Task AFreshListIsKeptAndAnOldOneFetchedAgain()
    {
        await LoadedAsync();
        Assert.True(System.IO.File.Exists(File));
        clock.Now += TimeSpan.FromDays(29);
        await LoadedAsync();
        Assert.Equal(1, fetches);
        clock.Now += TimeSpan.FromDays(2);
        await LoadedAsync();
        Assert.Equal(2, fetches);
        await LoadedAsync(refresh: true);
        Assert.Equal(3, fetches);
    }

    // Review Focus 1.
    [Fact]
    public async Task AFailedFetchLeavesNoList()
    {
        SteamAppIndex index = new(File, clock);
        await Assert.ThrowsAsync<ProviderException>(() => index.LoadAsync(
            _ => throw new ProviderException(ProviderErrorKind.RateLimited, "busy"), false, CancellationToken.None));
        Assert.False(System.IO.File.Exists(File));
        Assert.Empty(index.Match("portal"));
    }

    [Fact]
    public async Task AnUnreadableListIsFetchedAgain()
    {
        System.IO.File.WriteAllText(File, "{ not json");
        await LoadedAsync();
        Assert.Equal(1, fetches);
        Assert.Equal("620", (await LoadedAsync()).ExactSingle("Portal 2")?.AppId);
    }

    // A game whose app came from IGDB names it from a list already kept, never fetching one.
    [Fact]
    public async Task ACachedListLoadsWithoutFetching()
    {
        SteamAppIndex empty = new(File, clock);
        Assert.False(empty.LoadCached());
        await LoadedAsync();
        SteamAppIndex cached = new(File, clock);
        Assert.True(cached.LoadCached());
        Assert.Equal("Portal 2", cached.NameOf("620"));
        Assert.Equal(1, fetches);
    }
}
