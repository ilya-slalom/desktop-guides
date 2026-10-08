using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class CheckpointOrderTests : IAsyncLifetime
{
    private FaultFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await FaultFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Fact]
    public async Task ImportFiresEveryCheckpointInOrder()
    {
        List<ImportCheckpoint> seen = [];

        await fixture.ImportAsync(fixture.Publisher(seen.Add));

        Assert.Equal(
            new[]
            {
                ImportCheckpoint.Prepared, ImportCheckpoint.Copied, ImportCheckpoint.Verified,
                ImportCheckpoint.MovedToContent, ImportCheckpoint.Renamed, ImportCheckpoint.InCommit,
                ImportCheckpoint.Published,
            },
            seen);
    }

    [Fact]
    public async Task GuideRemovalFiresEveryCheckpointInOrder()
    {
        List<RemovalCheckpoint> seen = [];

        await fixture.GuideRemover(seen.Add).RemoveAsync(fixture.SubjectGuide);

        Assert.Equal(
            new[]
            {
                RemovalCheckpoint.Prepared, RemovalCheckpoint.TrashCreated, RemovalCheckpoint.Moved,
                RemovalCheckpoint.InCommit, RemovalCheckpoint.Committed,
            },
            seen);
    }

    [Fact]
    public async Task GameRemovalFiresEveryCheckpointInOrder()
    {
        List<RemovalCheckpoint> seen = [];

        await fixture.GameRemover(seen.Add).RemoveAsync(fixture.SubjectGame, 2);

        Assert.Equal(
            new[]
            {
                RemovalCheckpoint.Prepared, RemovalCheckpoint.TrashCreated, RemovalCheckpoint.MovedGuide,
                RemovalCheckpoint.MovedGuide, RemovalCheckpoint.Moved, RemovalCheckpoint.InCommit,
                RemovalCheckpoint.Committed, RemovalCheckpoint.BeforeArtworkDelete,
            },
            seen);
    }

    [Fact]
    public async Task RemoversCallTheirRollBackAndFinishHooks()
    {
        List<Guid> rolledBack = [];
        await Assert.ThrowsAsync<GuideRemovalException>(() => fixture.GuideRemover(
            FaultFixture.FaultAt(RemovalCheckpoint.Moved), rollBack: (_, operation) => rolledBack.Add(operation))
            .RemoveAsync(fixture.SubjectGuide));
        Assert.Single(rolledBack);
        Assert.Equal($"{rolledBack[0]:N}|Prepared",
            fixture.Library.Scalar("SELECT Id || '|' || Phase FROM FileOperations"));

        await fixture.RestartAsync();
        List<Guid> finished = [];
        GuideRemovalResult result = await fixture.GuideRemover(finish: (_, operation) => finished.Add(operation))
            .RemoveAsync(fixture.SubjectGuide);
        Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
        Assert.Equal($"{finished.Single():N}|Committed",
            fixture.Library.Scalar("SELECT Id || '|' || Phase FROM FileOperations"));
    }
}
