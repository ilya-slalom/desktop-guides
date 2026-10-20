using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

// S22: schema 9's source rows and Steam app IDs.
public sealed class GuideSourceStorageTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;

    public async Task InitializeAsync() => library = await RemovalLibrary.CreateAsync();

    public async Task DisposeAsync() => await library.DisposeAsync();

    internal static GuideSource Source(string item = "13639") => new(
        SourceSite.GameFaqs, item,
        "https://gamefaqs.gamespot.com/ps/197341-final-fantasy-vii/faqs/" + item,
        "Author", "(c) Author", "Walkthrough",
        DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_000), null);

    [Fact]
    public async Task AnImportWithASourceStoresItAndReadsItBack()
    {
        Guid game = await library.AddGameAsync("Final Fantasy VII");
        GuideSource source = Source() with { CapturedPages = 3, TotalPages = 5 };
        Guid sourced = await library.AddGuideAsync(game, "Walkthrough", source);
        Guid plain = await library.AddGuideAsync(game, "Notes");

        Assert.Equal(source, await library.Repository.GetGuideSourceAsync(sourced));
        Assert.Null(await library.Repository.GetGuideSourceAsync(plain));
        IReadOnlyDictionary<Guid, GuideSource> sources = await library.Repository.ListGuideSourcesAsync(game);
        Assert.Equal(sourced, Assert.Single(sources).Key);
        Assert.Equal(sourced, (await library.Repository.FindSourcedGuideAsync(game, SourceSite.GameFaqs, "13639"))?.Id);
        Assert.Null(await library.Repository.FindSourcedGuideAsync(game, SourceSite.Neoseeker, "13639"));
    }

    [Fact]
    public async Task AnInvalidSourceFailsTheImportAndLeavesNoRows()
    {
        Guid game = await library.AddGameAsync("Game");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            library.AddGuideAsync(game, "Bad", Source() with { ItemId = "" }));
        Assert.Equal("0", library.Scalar("SELECT COUNT(*) FROM Guides"));
        Assert.Equal("0", library.Scalar("SELECT COUNT(*) FROM GuideSources"));
    }

    // Review Focus 4.
    [Fact]
    public async Task RemovingAGuideOrGameRemovesItsSourceRows()
    {
        Guid game = await library.AddGameAsync("Game");
        Guid guide = await library.AddGuideAsync(game, "Walkthrough", Source());
        Directory.CreateDirectory(library.Paths.GetGuideRoot(guide));
        await new GuideRemover(library.Repository, library.Paths).RemoveAsync(guide);
        Assert.Equal("0", library.Scalar("SELECT COUNT(*) FROM GuideSources"));

        await library.Repository.SetSteamAppIdAsync(game, "1091500");
        await new GameRemover(library.Repository, library.Paths, new ManagedArtworkStore(library.Paths))
            .RemoveAsync(game, 0);
        Assert.Equal("0", library.Scalar("SELECT COUNT(*) FROM GameSteamApps"));
    }

    [Fact]
    public async Task SteamAppIdsRoundTripAndRejectNonDigits()
    {
        Guid game = await library.AddGameAsync("Game");
        Assert.Null(await library.Repository.GetSteamAppIdAsync(game));
        await library.Repository.SetSteamAppIdAsync(game, "1091500");
        Assert.Equal("1091500", await library.Repository.GetSteamAppIdAsync(game));
        await library.Repository.SetSteamAppIdAsync(game, "570");
        Assert.Equal("570", await library.Repository.GetSteamAppIdAsync(game));
        await library.Repository.SetSteamAppIdAsync(game, null);
        Assert.Null(await library.Repository.GetSteamAppIdAsync(game));
        foreach (string bad in new[] { "12a", "", "1234567890123" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => library.Repository.SetSteamAppIdAsync(game, bad));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            library.Repository.SetSteamAppIdAsync(Guid.NewGuid(), "570"));
    }
}
