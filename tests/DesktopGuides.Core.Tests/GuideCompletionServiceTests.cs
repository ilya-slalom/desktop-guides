using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideCompletionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid GuideId = Guid.Parse("6f1d2c3b-4a59-4e6f-8a7b-9c0d1e2f3a4b");

    [Fact]
    public async Task MarkCompletePassesTheClockTime()
    {
        FakeStore store = new();
        GuideCompletionService service = new(store, new Clock(Now));

        await service.MarkCompleteAsync(GuideId);

        Assert.Equal((GuideId, (DateTimeOffset?)Now), Assert.Single(store.Calls));
    }

    [Fact]
    public async Task MarkInProgressPassesNull()
    {
        FakeStore store = new();
        GuideCompletionService service = new(store, new Clock(Now));

        await service.MarkInProgressAsync(GuideId);

        Assert.Equal((GuideId, (DateTimeOffset?)null), Assert.Single(store.Calls));
    }

    [Fact]
    public async Task EachActionReturnsTheCommittedValue()
    {
        // An already-complete guide keeps its first time, so the committed
        // value can differ from the clock.
        FakeStore store = new() { Committed = Now.AddDays(-1) };
        GuideCompletionService service = new(store, new Clock(Now));

        Assert.Equal(Now.AddDays(-1), await service.MarkCompleteAsync(GuideId));
        store.Committed = null;
        Assert.Null(await service.MarkInProgressAsync(GuideId));
    }

    [Fact]
    public async Task StoreErrorsReachTheCaller()
    {
        ReadingStateMissingException missing = new(GuideId);
        IOException storage = new("disk");
        FakeStore store = new() { Failure = missing };
        GuideCompletionService service = new(store, new Clock(Now));

        Assert.Same(missing, await Assert.ThrowsAsync<ReadingStateMissingException>(() =>
            service.MarkCompleteAsync(GuideId)));
        store.Failure = storage;
        Assert.Same(storage, await Assert.ThrowsAsync<IOException>(() =>
            service.MarkInProgressAsync(GuideId)));
    }

    [Fact]
    public void ANullStoreOrClockIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new GuideCompletionService(null!, new Clock(Now)));
        Assert.Throws<ArgumentNullException>(() => new GuideCompletionService(new FakeStore(), null!));
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore : IGuideCompletionStore
    {
        public List<(Guid GuideId, DateTimeOffset? CompletedUtc)> Calls { get; } = [];
        public DateTimeOffset? Committed { get; set; }
        public Exception? Failure { get; set; }

        public Task<DateTimeOffset?> SetGuideCompletionAsync(
            Guid guideId, DateTimeOffset? completedUtc, CancellationToken token = default)
        {
            Calls.Add((guideId, completedUtc));
            return Failure is null
                ? Task.FromResult(Committed)
                : Task.FromException<DateTimeOffset?>(Failure);
        }
    }
}
