using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests.Providers;

public sealed class ProviderGameImporterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly ArtworkCandidate Grid = new(new Uri("https://cdn2.steamgriddb.com/grid/a.png"), "SteamGridDB");
    private static readonly ArtworkCandidate Cover = new(new Uri("https://images.igdb.com/igdb/image/upload/t_cover_big/co1.jpg"), "IGDB");

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ProviderGameRecord Record(params string[] platforms) => new("70", "Half-Life",
        new GameMetadataSnapshot(GameMetadataSnapshot.CurrentSchemaVersion, "Summary", new DateOnly(1998, 11, 19),
            ["Shooter"], ["Valve"], ["Sierra"], platforms, "https://www.igdb.com/games/half-life", GameTypeTag.MainGame),
        new ArtworkHints("Half-Life", "70", "co1"));

    private sealed class Harness
    {
        public FakeRepository Repository { get; } = new();
        public FakeStore Store { get; } = new();
        public FakeProvider Provider { get; init; } = new(Record("PC (Windows)"));
        public Dictionary<Uri, byte[]> Images { get; } = new() { [Grid.Url] = [1, 2, 3], [Cover.Url] = [4, 5, 6] };
        public List<Uri> Downloads { get; } = [];
        public Action? OnDownload { get; set; }
        public Clock Clock { get; } = new(Now);
        public ArtworkCandidate?[] Candidates { get; init; } = [Grid, Cover];

        public ProviderGameImporter Create() => new(Repository, Provider,
            new FallbackArtworkSource(Candidates.Select(c => (IArtworkSource)new FixedSource(c)).ToArray()),
            Store, (uri, token) =>
            {
                Downloads.Add(uri);
                OnDownload?.Invoke();
                token.ThrowIfCancellationRequested();
                return Images.TryGetValue(uri, out byte[]? bytes)
                    ? Task.FromResult(bytes)
                    : throw new ProviderException(ProviderErrorKind.Unavailable, "no image");
            }, Clock);
    }

    [Fact]
    public async Task AddCreatesOneLinkedGameWithArtworkAndItsOnlyPlatform()
    {
        Harness h = new();

        ProviderAddResult result = await h.Create().AddAsync("70", default);

        Game game = Assert.Single(h.Repository.Games.Values);
        Assert.Equal(game, result.Game);
        Assert.False(result.AlreadyInLibrary);
        Assert.False(result.ArtworkMissing);
        Assert.Equal(("Half-Life", "PC (Windows)"), (game.Title, game.Platform));
        Assert.Equal(new ProviderGameLink(ProviderGameLink.Igdb, "70", Now), game.Link);
        Assert.Equal("SteamGridDB", game.Metadata!.ArtworkSource);
        Assert.Equal(game.ArtworkRelativePath, Assert.Single(h.Store.Files.Keys));
        Assert.StartsWith($"artwork/{game.Id:N}/", game.ArtworkRelativePath);
    }

    [Fact]
    public async Task AddLeavesPlatformEmptyWhenSeveralAreListed()
    {
        Harness h = new() { Provider = new FakeProvider(Record("PC (Windows)", "PlayStation 2")) };
        Assert.Null((await h.Create().AddAsync("70", default)).Game.Platform);
    }

    [Fact]
    public async Task AddOfAnAlreadyLinkedIdOpensTheExistingGameWithoutFetching()
    {
        Harness h = new();
        Game existing = (await h.Create().AddAsync("70", default)).Game;

        ProviderAddResult again = await h.Create().AddAsync("70", default);

        Assert.True(again.AlreadyInLibrary);
        Assert.Equal(existing.Id, again.Game.Id);
        Assert.Equal(1, h.Provider.GetCalls);
        Assert.Single(h.Repository.Games);
    }

    [Fact]
    public async Task AddThatLosesAUniqueIndexRaceDeletesItsFileAndOpensTheWinner()
    {
        Harness h = new();
        Game winner = new(Guid.NewGuid(), "Half-Life", null, null, Now, Now, new ProviderGameLink("igdb", "70", Now));
        h.Repository.RaceWinner = winner.Id;
        h.OnDownload = () => h.Repository.Games[winner.Id] = winner;

        ProviderAddResult result = await h.Create().AddAsync("70", default);

        Assert.True(result.AlreadyInLibrary);
        Assert.Equal(winner, result.Game);
        Assert.Empty(h.Store.Files);
        Assert.Single(h.Store.Deleted);
    }

    [Fact]
    public async Task AddWithNoArtworkCandidateSavesGameWithNotice()
    {
        Harness h = new() { Candidates = [null, null] };

        ProviderAddResult result = await h.Create().AddAsync("70", default);

        Assert.True(result.ArtworkMissing);
        Assert.Null(result.Game.ArtworkRelativePath);
        Assert.Null(result.Game.Metadata!.ArtworkSource);
        Assert.Single(h.Repository.Games);
        Assert.Empty(h.Store.Files);
        Assert.Empty(h.Downloads);
    }

    [Fact]
    public async Task AddFallsBackToTheNextCandidateWhenADownloadOrImageFails()
    {
        Harness h = new();
        h.Images[Grid.Url] = [];

        ProviderAddResult result = await h.Create().AddAsync("70", default);

        Assert.Equal([Grid.Url, Cover.Url], h.Downloads);
        Assert.Equal("IGDB", result.Game.Metadata!.ArtworkSource);
        Assert.False(result.ArtworkMissing);

        Harness offline = new();
        offline.Images.Clear();
        ProviderAddResult none = await offline.Create().AddAsync("70", default);
        Assert.True(none.ArtworkMissing);
        Assert.Single(offline.Repository.Games);
    }

    [Fact]
    public async Task MalformedRecordStopsTheAddWithNothingCreated()
    {
        Harness h = new() { Provider = new FakeProvider { Failure = new(ProviderErrorKind.MalformedData, "bad") } };

        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => h.Create().AddAsync("70", default));

        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
        Assert.Empty(h.Repository.Games);
        Assert.Empty(h.Downloads);
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("artwork")]
    [InlineData("commit")]
    public async Task CancelLeavesNoRowAndNoFile(string stage)
    {
        using CancellationTokenSource cancel = new();
        Harness h = new();
        switch (stage)
        {
            case "fetch": cancel.Cancel(); break;
            case "artwork": h.OnDownload = cancel.Cancel; break;
            case "commit": h.Store.OnStored = cancel.Cancel; break;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Create().AddAsync("70", cancel.Token));

        Assert.Empty(h.Repository.Games);
        Assert.Empty(h.Store.Files);
    }

    private static async Task<(Harness H, Game Game)> LinkedWithLocalEdits()
    {
        Harness h = new();
        Game added = (await h.Create().AddAsync("70", default)).Game;
        Game edited = added with { Title = "My Half-Life", Platform = "PlayStation 2", Notes = "mine" };
        h.Repository.Games[added.Id] = edited;
        h.Clock.Now = Now.AddDays(1);
        return (h, edited);
    }

    [Fact]
    public async Task RefreshKeepsLocalFieldsAndDeletesTheOldFileAfterCommit()
    {
        (Harness h, Game before) = await LinkedWithLocalEdits();
        h.Images[Grid.Url] = [9, 9, 9];

        ProviderRefreshResult result = await h.Create().RefreshAsync(before.Id, default);

        Game after = h.Repository.Games[before.Id];
        Assert.Equal(after, result.Game);
        Assert.Equal(("My Half-Life", "PlayStation 2", "mine"), (after.Title, after.Platform, after.Notes));
        Assert.Equal(Now.AddDays(1), after.Link!.RetrievedUtc);
        Assert.NotEqual(before.ArtworkRelativePath, after.ArtworkRelativePath);
        Assert.Equal([before.ArtworkRelativePath!], h.Store.Deleted);
        Assert.Equal(after.ArtworkRelativePath, Assert.Single(h.Store.Files.Keys));
    }

    [Fact]
    public async Task RefreshWithIdenticalImageKeepsCurrentFile()
    {
        (Harness h, Game before) = await LinkedWithLocalEdits();

        ProviderRefreshResult result = await h.Create().RefreshAsync(before.Id, default);

        Assert.Equal(before.ArtworkRelativePath, result.Game.ArtworkRelativePath);
        Assert.Empty(h.Store.Deleted);
        Assert.True(h.Store.Files.ContainsKey(before.ArtworkRelativePath!));
    }

    [Fact]
    public async Task RefreshWithNoArtworkKeepsTheCurrentFileAndSource()
    {
        (Harness h, Game before) = await LinkedWithLocalEdits();
        h.Images.Clear();

        ProviderRefreshResult result = await h.Create().RefreshAsync(before.Id, default);

        Assert.False(result.ArtworkMissing);
        Assert.Equal(before.ArtworkRelativePath, result.Game.ArtworkRelativePath);
        Assert.Equal("SteamGridDB", result.Game.Metadata!.ArtworkSource);
        Assert.Empty(h.Store.Deleted);
    }

    [Fact]
    public async Task FailedRefreshKeepsTheRowAndRemovesOnlyTheNewFile()
    {
        (Harness h, Game before) = await LinkedWithLocalEdits();
        h.Images[Grid.Url] = [9, 9, 9];
        h.Repository.FailUpdates = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Create().RefreshAsync(before.Id, default));

        Assert.Equal(before, h.Repository.Games[before.Id]);
        Assert.Equal(before.ArtworkRelativePath, Assert.Single(h.Store.Files.Keys));
        Assert.DoesNotContain(before.ArtworkRelativePath!, h.Store.Deleted);
        Assert.Single(h.Store.Deleted);
    }

    [Fact]
    public async Task RefreshRejectsAnUnlinkedOrMissingGameWithoutARequest()
    {
        Harness h = new();
        Game manual = new(Guid.NewGuid(), "Manual", null, null, Now, Now);
        h.Repository.Games[manual.Id] = manual;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Create().RefreshAsync(manual.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => h.Create().RefreshAsync(Guid.NewGuid(), default));
        Assert.Equal(0, h.Provider.GetCalls);
    }
}
