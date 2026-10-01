using DesktopGuides.Core.Library;
using DesktopGuides.Core.Tests.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GameRemoverTests
{
    private const string Artwork = "artwork/0123456789abcdef0123456789abcdef/cover.png";
    private readonly FakeRepository repository = new();
    private readonly FakeStore store = new();

    private GameRemover Remover() => new(repository, store);

    [Fact]
    public async Task ARemovedGameLosesItsArtwork()
    {
        Guid id = Guid.NewGuid();
        repository.NextRemoval = new(EmptyGameRemovalOutcome.Removed, Artwork);

        EmptyGameRemovalOutcome outcome = await Remover().RemoveAsync(id);

        Assert.Equal(EmptyGameRemovalOutcome.Removed, outcome);
        Assert.Equal([id], repository.RemoveCalls);
        Assert.Equal([Artwork], store.Deleted);
    }

    [Fact]
    public async Task ARemovedGameWithoutArtworkDeletesNothing()
    {
        repository.NextRemoval = new(EmptyGameRemovalOutcome.Removed, null);

        Assert.Equal(EmptyGameRemovalOutcome.Removed, await Remover().RemoveAsync(Guid.NewGuid()));
        Assert.Empty(store.Deleted);
    }

    [Theory]
    [InlineData(EmptyGameRemovalOutcome.NotFound)]
    [InlineData(EmptyGameRemovalOutcome.HasGuides)]
    public async Task AGameThatWasNotRemovedKeepsItsFiles(EmptyGameRemovalOutcome outcome)
    {
        repository.NextRemoval = new(outcome, Artwork);

        Assert.Equal(outcome, await Remover().RemoveAsync(Guid.NewGuid()));
        Assert.Empty(store.Deleted);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task AFailedArtworkDeleteStillReportsRemoved(Type failure)
    {
        repository.NextRemoval = new(EmptyGameRemovalOutcome.Removed, Artwork);
        store.DeleteFailure = (Exception)Activator.CreateInstance(failure)!;

        Assert.Equal(EmptyGameRemovalOutcome.Removed, await Remover().RemoveAsync(Guid.NewGuid()));
    }
}
