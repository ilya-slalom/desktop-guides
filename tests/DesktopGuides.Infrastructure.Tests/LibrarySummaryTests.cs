using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibrarySummaryTests : IAsyncLifetime
{
    private const long Day = 86_400_000;
    private const long Base = 1_790_000_000_000;
    private RemovalLibrary library = null!;

    public async Task InitializeAsync() => library = await RemovalLibrary.CreateAsync();

    public async Task DisposeAsync() => await library.DisposeAsync();

    private static string N(Guid id) => id.ToString("N");

    private async Task<Guid> GameAt(string title, long created)
    {
        Guid id = await library.AddGameAsync(title);
        library.Execute($"UPDATE Games SET CreatedUtcMs = {created}, UpdatedUtcMs = {created} WHERE Id = '{N(id)}'");
        return id;
    }

    private async Task<Guid> GuideAt(Guid gameId, string title, long imported)
    {
        Guid id = await library.AddGuideAsync(gameId, title);
        library.Execute($"UPDATE Guides SET ImportedUtcMs = {imported}, UpdatedUtcMs = {imported} WHERE Id = '{N(id)}'");
        return id;
    }

    private void Opened(Guid guideId, long opened) =>
        library.Execute($"UPDATE ReadingStates SET LastOpenedUtcMs = {opened} WHERE GuideId = '{N(guideId)}'");

    private async Task<string[]> GameTitles() =>
        (await library.Repository.ListGameSummariesAsync()).Select(summary => summary.Game.Title).ToArray();

    private async Task<string[]> GuideTitles(Guid gameId) =>
        (await library.Repository.ListGuideSummariesAsync(gameId)).Select(summary => summary.Guide.Title).ToArray();

    [Fact]
    public async Task GamesSortByCreationWhenTheyHaveNoGuides()
    {
        await GameAt("Older", Base);
        await GameAt("Newer", Base + Day);

        Assert.Equal(["Newer", "Older"], await GameTitles());
    }

    [Fact]
    public async Task ANewerImportMovesAnOlderGameFirst()
    {
        Guid older = await GameAt("Older", Base);
        await GameAt("Newer", Base + Day);
        await GuideAt(older, "Fresh", Base + 2 * Day);

        Assert.Equal(["Older", "Newer"], await GameTitles());
    }

    [Fact]
    public async Task ANewerOpenMovesAnOlderGameFirst()
    {
        Guid older = await GameAt("Older", Base);
        Guid newer = await GameAt("Newer", Base + 2 * Day);
        Guid guide = await GuideAt(older, "Read", Base);
        await GuideAt(newer, "Unread", Base + 2 * Day);
        Opened(guide, Base + 3 * Day);

        IReadOnlyList<LibraryGameSummary> summaries = await library.Repository.ListGameSummariesAsync();

        Assert.Equal(["Older", "Newer"], summaries.Select(summary => summary.Game.Title));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(Base + 3 * Day), summaries[0].LastActivityUtc);
    }

    [Fact]
    public async Task AGuideWithoutAReadingStateRowStillCounts()
    {
        Guid game = await GameAt("Game", Base);
        Guid guide = await GuideAt(game, "Guide", Base + Day);
        library.Execute($"DELETE FROM ReadingStates WHERE GuideId = '{N(guide)}'");

        LibraryGameSummary summary = Assert.Single(await library.Repository.ListGameSummariesAsync());

        Assert.Equal(1, summary.GuideCount);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(Base + Day), summary.LastActivityUtc);
    }

    [Fact]
    public async Task GuideCountsAreNotInflatedByReadingStates()
    {
        Guid game = await GameAt("Game", Base);
        foreach (string title in new[] { "A", "B", "C" })
        {
            Opened(await GuideAt(game, title, Base), Base + Day);
        }
        await GameAt("Empty", Base);

        IReadOnlyList<LibraryGameSummary> summaries = await library.Repository.ListGameSummariesAsync();

        Assert.Equal([("Game", 3), ("Empty", 0)], summaries.Select(summary => (summary.Game.Title, summary.GuideCount)));
    }

    [Fact]
    public async Task EqualGameActivitySortsByTitleThenId()
    {
        Guid first = await GameAt("Twin", Base);
        Guid second = await GameAt("Twin", Base);
        await GameAt("Alpha", Base);
        Guid[] twins = [.. new[] { first, second }.OrderBy(N, StringComparer.Ordinal)];

        IReadOnlyList<LibraryGameSummary> summaries = await library.Repository.ListGameSummariesAsync();

        Assert.Equal(["Alpha", "Twin", "Twin"], summaries.Select(summary => summary.Game.Title));
        Assert.Equal(twins, summaries.Skip(1).Select(summary => summary.Game.Id));
    }

    [Fact]
    public async Task RemovingAGuideUpdatesTheCountAndTheOrder()
    {
        Guid older = await GameAt("Older", Base);
        await GameAt("Newer", Base + Day);
        Guid fresh = await GuideAt(older, "Fresh", Base + 2 * Day);
        await GuideAt(older, "Kept", Base);
        RemovalLibrary.WriteFile(library.Paths.GetGuideRoot(fresh), "guide.txt", "x");
        Assert.Equal(["Older", "Newer"], await GameTitles());

        GuideRemovalResult result = await new GuideRemover(library.Repository, library.Paths).RemoveAsync(fresh);

        Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
        IReadOnlyList<LibraryGameSummary> summaries = await library.Repository.ListGameSummariesAsync();
        Assert.Equal([("Newer", 0), ("Older", 1)], summaries.Select(summary => (summary.Game.Title, summary.GuideCount)));
    }

    [Fact]
    public async Task GuideSummariesJoinTheirOwnStateAndExcludeOtherGames()
    {
        Guid game = await GameAt("Game", Base);
        Guid other = await GameAt("Other", Base);
        Guid read = await GuideAt(game, "Read", Base);
        Guid unread = await GuideAt(game, "Unread", Base);
        Guid missing = await GuideAt(game, "Missing", Base);
        Guid elsewhere = await GuideAt(other, "Elsewhere", Base);
        library.Execute($"""
            UPDATE ReadingStates SET EstimatedFraction = 0.45, LastOpenedUtcMs = {Base + Day},
                CompletedUtcMs = {Base + Day} WHERE GuideId = '{N(read)}';
            UPDATE ReadingStates SET EstimatedFraction = 0.9 WHERE GuideId = '{N(elsewhere)}';
            DELETE FROM ReadingStates WHERE GuideId = '{N(missing)}';
            """);

        IReadOnlyList<GuideSummary> summaries = await library.Repository.ListGuideSummariesAsync(game);

        Assert.Equal([read, missing, unread], summaries.Select(summary => summary.Guide.Id));
        Assert.Equal(
            new ReadingState(read, null, 0.45,
                DateTimeOffset.FromUnixTimeMilliseconds(Base + Day),
                DateTimeOffset.FromUnixTimeMilliseconds(Base + Day)),
            summaries[0].State);
        Assert.Null(summaries[1].State);
        Assert.Equal(new ReadingState(unread, null, null, null, null), summaries[2].State);
        Assert.Equal(await library.Repository.GetGuideAsync(read), summaries[0].Guide);
    }

    [Fact]
    public async Task ANewerImportSortsFirst()
    {
        Guid game = await GameAt("Game", Base);
        await GuideAt(game, "Alpha", Base);
        await GuideAt(game, "Beta", Base + Day);

        Assert.Equal(["Beta", "Alpha"], await GuideTitles(game));
    }

    [Fact]
    public async Task ANewerOpenBeatsANewerImport()
    {
        Guid game = await GameAt("Game", Base);
        Guid alpha = await GuideAt(game, "Alpha", Base);
        await GuideAt(game, "Beta", Base + Day);
        Opened(alpha, Base + 2 * Day);

        Assert.Equal(["Alpha", "Beta"], await GuideTitles(game));
    }

    [Fact]
    public async Task EqualGuideActivitySortsByTitleThenId()
    {
        Guid game = await GameAt("Game", Base);
        Guid first = await GuideAt(game, "Twin", Base);
        Guid second = await GuideAt(game, "Twin", Base);
        await GuideAt(game, "Alpha", Base);
        Guid[] twins = [.. new[] { first, second }.OrderBy(N, StringComparer.Ordinal)];

        IReadOnlyList<GuideSummary> summaries = await library.Repository.ListGuideSummariesAsync(game);

        Assert.Equal(["Alpha", "Twin", "Twin"], summaries.Select(summary => summary.Guide.Title));
        Assert.Equal(twins, summaries.Skip(1).Select(summary => summary.Guide.Id));
    }

    [Fact]
    public async Task ListingNeverReadsGuideContent()
    {
        Guid game = await GameAt("Game", Base);
        Guid guide = await GuideAt(game, "Guide", Base);
        RemovalLibrary.WriteFile(library.Paths.GetGuideRoot(guide), "guide.txt", "x");
        Directory.Delete(library.Paths.GetGuideRoot(guide), true);

        Assert.Equal(1, Assert.Single(await library.Repository.ListGameSummariesAsync()).GuideCount);
        Assert.Equal(guide, Assert.Single(await library.Repository.ListGuideSummariesAsync(game)).Guide.Id);
    }
}
