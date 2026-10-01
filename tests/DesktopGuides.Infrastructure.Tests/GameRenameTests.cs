using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class GameRenameTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;
    private Game before = null!;
    private Guid alphaId;
    private Guid betaId;

    public async Task InitializeAsync()
    {
        library = await RemovalLibrary.CreateAsync();
        Guid id = Guid.NewGuid();
        StoredArtwork cover = await new ManagedArtworkStore(library.Paths)
            .StoreAsync(id, Artwork.TestImages.Png(4, 4), default);
        await library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            id, "Linked Rename Game", "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, "900100", DateTimeOffset.UnixEpoch.AddDays(1)),
            GameMetadataJsonTests.Sample(), cover.RelativePath));
        alphaId = await library.AddGuideAsync(id, "Alpha Route Guide");
        betaId = await library.AddGuideAsync(id, "Beta Route Guide");
        await library.Repository.SaveReadingLocationAsync(alphaId, "{\"offset\":12}", 0.45);
        await library.Repository.UpdateSettingsAsync(s => s with { LastActiveGuideId = betaId });
        // Read back, so the comparison uses stored precision.
        before = (await library.Repository.GetGameAsync(id))!;
    }

    public async Task DisposeAsync() => await library.DisposeAsync();

    [Fact]
    public async Task RenameKeepsTheGameBoundToItsId()
    {
        Game renamed = await library.Repository.UpdateGameAsync(
            before.Id, "Renamed Linked Game", before.Platform, before.Notes);

        Assert.Equal(before.Id, renamed.Id);
        await AssertRenamedAndKeptAsync();
    }

    [Fact]
    public async Task RenameSurvivesARestart()
    {
        await library.Repository.UpdateGameAsync(
            before.Id, "Renamed Linked Game", before.Platform, before.Notes);

        await library.RestartAsync();

        await AssertRenamedAndKeptAsync();
    }

    private async Task AssertRenamedAndKeptAsync()
    {
        Game game = (await library.Repository.GetGameAsync(before.Id))!;
        Assert.Equal("Renamed Linked Game", game.Title);
        Assert.Equal(before.Link, game.Link);
        Assert.Equal(GameMetadataJson.Serialize(before.Metadata!), GameMetadataJson.Serialize(game.Metadata!));
        Assert.Equal(before.ArtworkRelativePath, game.ArtworkRelativePath);
        Assert.NotNull(new ManagedArtworkStore(library.Paths).ResolveFile(game.ArtworkRelativePath!));
        Assert.Equal(before.CreatedUtc, game.CreatedUtc);

        Assert.Equal(
            new[] { alphaId, betaId }.Order(),
            (await library.Repository.ListGuidesAsync(before.Id)).Select(guide => guide.Id).Order());
        ReadingState alpha = (await library.Repository.GetReadingStateAsync(alphaId))!;
        Assert.Equal("{\"offset\":12}", alpha.LocatorJson);
        Assert.Equal(0.45, alpha.EstimatedFraction);
        Assert.Equal(betaId, (await library.Repository.GetSettingsAsync()).LastActiveGuideId);

        LibraryGameSummary summary = Assert.Single(await library.Repository.ListGameSummariesAsync());
        Assert.Equal((before.Id, "Renamed Linked Game", 2), (summary.Game.Id, summary.Game.Title, summary.GuideCount));
    }
}
