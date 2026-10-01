using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class EmptyGameRemovalTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;
    private ManagedArtworkStore store = null!;

    public async Task InitializeAsync()
    {
        library = await RemovalLibrary.CreateAsync();
        store = new ManagedArtworkStore(library.Paths);
    }

    public async Task DisposeAsync() => await library.DisposeAsync();

    private async Task<Game> AddLinkedAsync(string externalId)
    {
        Guid id = Guid.NewGuid();
        StoredArtwork cover = await store.StoreAsync(id, Artwork.TestImages.Png(4, 4), default);
        return await library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            id, "Empty Linked Game", "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, externalId, DateTimeOffset.UnixEpoch),
            GameMetadataJsonTests.Sample(), cover.RelativePath));
    }

    private GameRemover Remover() => new(library.Repository, store);

    [Fact]
    public async Task AManualGameWithoutGuidesIsRemoved()
    {
        Guid id = await library.AddGameAsync("Empty Manual Game");

        EmptyGameRemoval removal = await library.Repository.RemoveEmptyGameAsync(id);

        Assert.Equal(new EmptyGameRemoval(EmptyGameRemovalOutcome.Removed, null), removal);
        Assert.Null(await library.Repository.GetGameAsync(id));
    }

    [Fact]
    public async Task ALinkedGameIsRemovedWithItsArtworkPathAndCanBeAddedAgain()
    {
        Game game = await AddLinkedAsync("900200");

        EmptyGameRemoval removal = await library.Repository.RemoveEmptyGameAsync(game.Id);

        Assert.Equal(new EmptyGameRemoval(EmptyGameRemovalOutcome.Removed, game.ArtworkRelativePath), removal);
        Assert.Null(await library.Repository.FindLinkedGameAsync(ProviderGameLink.Igdb, "900200"));
        Game again = await AddLinkedAsync("900200");
        Assert.NotEqual(game.Id, again.Id);
    }

    [Fact]
    public async Task AGameWithAGuideIsKeptWithItsGuideAndState()
    {
        Guid id = await library.AddGameAsync("Guided Game");
        Guid guideId = await library.AddGuideAsync(id, "Walkthrough");
        await library.Repository.SaveReadingLocationAsync(guideId, "{\"offset\":3}", 0.45);

        EmptyGameRemoval removal = await library.Repository.RemoveEmptyGameAsync(id);

        Assert.Equal(new EmptyGameRemoval(EmptyGameRemovalOutcome.HasGuides, null), removal);
        Assert.NotNull(await library.Repository.GetGameAsync(id));
        Assert.Equal("1|1|1", library.RowsFor(guideId));
        Assert.Equal(0.45, (await library.Repository.GetReadingStateAsync(guideId))!.EstimatedFraction);
    }

    [Fact]
    public async Task AnUnknownGameIsNotFound() =>
        Assert.Equal(
            new EmptyGameRemoval(EmptyGameRemovalOutcome.NotFound, null),
            await library.Repository.RemoveEmptyGameAsync(Guid.NewGuid()));

    [Fact]
    public async Task AnEmptyIdIsRejected() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => library.Repository.RemoveEmptyGameAsync(Guid.Empty));

    [Fact]
    public async Task OtherGamesAreUntouched()
    {
        Guid removed = await library.AddGameAsync("Removed Game");
        Guid kept = await library.AddGameAsync("Kept Game");
        Guid keptGuide = await library.AddGuideAsync(kept, "Kept Guide");

        await library.Repository.RemoveEmptyGameAsync(removed);

        Assert.Equal([kept], (await library.Repository.ListGamesAsync()).Select(game => game.Id));
        Assert.Equal("1|1|1", library.RowsFor(keptGuide));
    }

    [Fact]
    public async Task TheRemoverDeletesTheArtworkFileAndItsFolder()
    {
        Game game = await AddLinkedAsync("900300");
        string file = store.ResolveFile(game.ArtworkRelativePath!)!;

        Assert.Equal(EmptyGameRemovalOutcome.Removed, await Remover().RemoveAsync(game.Id));

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
    }

    [Fact]
    public async Task ArtworkThatCannotBeDeletedIsSweptAtTheNextStart()
    {
        Game game = await AddLinkedAsync("900400");
        string file = store.ResolveFile(game.ArtworkRelativePath!)!;
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(EmptyGameRemovalOutcome.Removed, await Remover().RemoveAsync(game.Id));
            Assert.True(File.Exists(file));
        }

        await library.RestartAsync();

        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task AGuidePublishedIntoARemovedGameFailsAndLeavesNothing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        Assert.Equal(
            EmptyGameRemovalOutcome.Removed,
            (await harness.Repository.RemoveEmptyGameAsync(harness.Game.Id)).Outcome);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        harness.AssertNothingLeft();
    }
}
