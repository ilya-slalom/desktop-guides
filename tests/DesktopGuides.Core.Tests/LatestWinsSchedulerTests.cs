using System.Collections.Concurrent;
using DesktopGuides.Core.Pdf;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class LatestWinsSchedulerTests
{
    private sealed class Pages
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> pending = new();

        public Pages(Func<int, CancellationToken, Task<string>>? load = null) =>
            Scheduler = new(load ?? Load, (page, _) => Applied.Enqueue(page), (page, _) => Failed.Enqueue(page));

        public LatestWinsScheduler<string> Scheduler { get; }
        public ConcurrentQueue<int> Started { get; } = new();
        public ConcurrentQueue<int> Applied { get; } = new();
        public ConcurrentQueue<int> Failed { get; } = new();

        public TaskCompletionSource<string> Source(int page) =>
            pending.GetOrAdd(page, _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));

        private Task<string> Load(int page, CancellationToken token)
        {
            Started.Enqueue(page);
            TaskCompletionSource<string> source = Source(page);
            token.Register(() => source.TrySetCanceled(token));
            return source.Task;
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The scheduler didn't reach the expected state.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task FiftyRapidRequestsRunTwoLoadsAndApplyTheNewest()
    {
        Pages pages = new();
        for (int page = 0; page < 50; page++)
        {
            pages.Scheduler.Request(page);
        }
        Assert.Equal([0], pages.Started);

        pages.Source(0).SetResult("page 1");
        await Until(() => pages.Started.Count == 2);
        pages.Source(49).SetResult("page 50");
        await Until(() => !pages.Applied.IsEmpty);

        Assert.Equal([0, 49], pages.Started);
        Assert.Equal([49], pages.Applied);
        Assert.Equal(50, pages.Scheduler.Requests);
        Assert.Equal(2, pages.Scheduler.Loads);
        Assert.Equal(1, pages.Scheduler.StaleResults);
    }

    [Fact]
    public async Task ResultOfASupersededLoadIsNotApplied()
    {
        Pages pages = new();
        pages.Scheduler.Request(0);
        pages.Scheduler.Request(1);
        pages.Source(0).SetResult("page 1");
        await Until(() => pages.Started.Count == 2);

        pages.Scheduler.Request(2);
        pages.Source(1).SetResult("page 2");
        await Until(() => pages.Started.Count == 3);
        pages.Source(2).SetResult("page 3");
        await Until(() => !pages.Applied.IsEmpty);

        Assert.Equal([2], pages.Applied);
        Assert.Equal(2, pages.Scheduler.StaleResults);
    }

    [Fact]
    public async Task AResultWithNoNewerRequestIsAppliedAndALaterRequestLoadsAgain()
    {
        Pages pages = new();
        pages.Scheduler.Request(3);
        pages.Source(3).SetResult("page 4");
        await Until(() => pages.Applied.Count == 1);

        pages.Scheduler.Request(4);
        pages.Source(4).SetResult("page 5");
        await Until(() => pages.Applied.Count == 2);

        Assert.Equal([3, 4], pages.Applied);
        Assert.Equal(0, pages.Scheduler.StaleResults);
    }

    [Fact]
    public async Task AThrowingLoadIsReportedAndTheNextRequestStillRuns()
    {
        Pages pages = new();
        pages.Scheduler.Request(0);
        pages.Source(0).SetException(new InvalidDataException("bad page"));
        await Until(() => !pages.Failed.IsEmpty);

        pages.Scheduler.Request(1);
        pages.Source(1).SetResult("page 2");
        await Until(() => !pages.Applied.IsEmpty);

        Assert.Equal([0], pages.Failed);
        Assert.Equal([1], pages.Applied);
    }

    [Fact]
    public async Task ASupersededFailureIsNotReported()
    {
        Pages pages = new();
        pages.Scheduler.Request(0);
        pages.Scheduler.Request(1);
        pages.Source(0).SetException(new InvalidDataException("bad page"));
        await Until(() => pages.Started.Count == 2);
        pages.Source(1).SetResult("page 2");
        await Until(() => !pages.Applied.IsEmpty);

        Assert.Empty(pages.Failed);
        Assert.Equal(1, pages.Scheduler.StaleResults);
    }

    [Fact]
    public async Task CancelAsyncWaitsForTheRunningLoadAndStopsLaterOnes()
    {
        TaskCompletionSource<string> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool returned = false;
        Pages pages = new(async (page, _) =>
        {
            // Ignores the token, like a render that can't be interrupted.
            string result = await release.Task;
            returned = true;
            return result;
        });
        pages.Scheduler.Request(0);
        pages.Scheduler.Request(1);

        Task cancelling = pages.Scheduler.CancelAsync();
        Assert.False(cancelling.IsCompleted);
        release.SetResult("late");
        await cancelling;

        Assert.True(returned);
        Assert.Empty(pages.Applied);
        Assert.Empty(pages.Failed);
        pages.Scheduler.Request(2);
        Assert.Equal(1, pages.Scheduler.Loads);
    }

    [Fact]
    public async Task CancelAsyncCancelsTheRunningLoadsToken()
    {
        Pages pages = new();
        pages.Scheduler.Request(0);

        await pages.Scheduler.CancelAsync();

        Assert.True(pages.Source(0).Task.IsCanceled);
        Assert.Empty(pages.Failed);
        Assert.Empty(pages.Applied);
    }

    [Fact]
    public async Task CancelAsyncIsIdempotentAndWorksWithNothingRunning()
    {
        Pages pages = new();
        await pages.Scheduler.CancelAsync();
        await pages.Scheduler.CancelAsync();

        pages.Scheduler.Request(0);

        Assert.Empty(pages.Started);
    }
}
