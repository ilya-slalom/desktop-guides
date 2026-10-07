using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ProgressCoordinatorTests
{
    private static readonly Guid GuideA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid GuideB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private readonly ManualClock clock = new();
    private readonly FakeStore store = new();

    [Fact]
    public void OneMovementSavesAfterQuietNotBefore()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);

        session.Move();
        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Empty(store.Writes);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal([(GuideA, session.CurrentJson, (double?)null)], store.Writes);
        Assert.Equal(new ProgressCounts(1, 0, 0, 1), coordinator.Counts);
        GC.KeepAlive(tracking);
    }

    [Fact]
    public void BurstSavesAtDeadlinesThenAfterQuiet()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        Track(coordinator, GuideA, session, null);
        List<double> savedAt = [];
        coordinator.CountsChanged += (_, _) => savedAt.Add(clock.Elapsed.TotalSeconds);

        for (int i = 0; i < 50; i++)
        {
            session.Move();
            clock.Advance(TimeSpan.FromMilliseconds(200));
        }
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(3, store.Writes.Count);
        Assert.Equal([4.0, 8.0, 10.8], savedAt.Select(s => Math.Round(s, 1)));
        Assert.Equal(session.CurrentJson, store.Writes[^1].Json);
    }

    [Fact]
    public void UnchangedCaptureIsSkipped()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        Track(coordinator, GuideA, session, null);
        int changes = 0;
        coordinator.CountsChanged += (_, _) => changes++;

        session.Move();
        clock.Advance(TimeSpan.FromSeconds(1));
        session.Touch();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Single(store.Writes);
        Assert.Equal(new ProgressCounts(1, 1, 0, 1), coordinator.Counts);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task MovementBackToBaselineSkipsWrite()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, session.CurrentJson);

        session.Touch();
        await tracking.DisposeAsync();

        Assert.Empty(store.Writes);
        Assert.Equal(new ProgressCounts(0, 1, 0, 1), coordinator.Counts);
    }

    [Fact]
    public void TrackRecordsOneOpenAtTheClocksTimeBeforeTheFirstSave()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        DateTimeOffset opened = clock.GetUtcNow();
        Track(coordinator, GuideA, session, null);

        clock.Advance(TimeSpan.FromSeconds(3));
        session.Move();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([(GuideA, opened)], store.Opens);
        Assert.Equal(["open", "save"], store.Events);
        Assert.Equal(new ProgressCounts(1, 0, 0, 1), coordinator.Counts);
    }

    [Fact]
    public async Task UnmovedGuideWritesOnlyItsOpen()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);

        clock.Advance(TimeSpan.FromSeconds(10));
        await tracking.FlushAsync(CancellationToken.None);
        await tracking.DisposeAsync();

        Assert.Single(store.Opens);
        Assert.Equal(0, store.Attempts);
        Assert.Equal(0, session.Captures);
    }

    [Fact]
    public async Task FailedOpenCountsAFailureAndRaisesNothing()
    {
        ProgressCoordinator coordinator = new(store, clock);
        int failed = 0;
        coordinator.SaveFailed += (_, _) => failed++;
        store.OpenFailures.Enqueue(new IOException("disk"));
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);

        Assert.Equal(0, store.Attempts);
        session.Move();
        clock.Advance(TimeSpan.FromSeconds(1));
        await tracking.DisposeAsync();

        Assert.Empty(store.Opens);
        Assert.Single(store.Writes);
        Assert.Equal(0, failed);
        Assert.Equal(new ProgressCounts(1, 0, 1, 0), coordinator.Counts);
    }

    [Fact]
    public async Task MissingRowOnOpenRaisesNothing()
    {
        ProgressCoordinator coordinator = new(store, clock);
        int failed = 0;
        coordinator.SaveFailed += (_, _) => failed++;
        store.OpenFailures.Enqueue(new ReadingStateMissingException(GuideA));
        IProgressTracking tracking = Track(coordinator, GuideA, new FakeSession(), null);

        await tracking.DisposeAsync();

        Assert.Equal(0, failed);
        Assert.Equal(new ProgressCounts(0, 0, 0, 0), coordinator.Counts);
    }

    [Fact]
    public void HeldOpenThenNextTrackNeverWritesTheNextGuidesId()
    {
        ProgressCoordinator coordinator = new(store, clock);
        TaskCompletionSource hold = new();
        store.OpenHold = hold;
        FakeSession first = new();
        Track(coordinator, GuideA, first, null);
        store.OpenHold = null;

        Track(coordinator, GuideB, new FakeSession { Offset = 500 }, null);
        ReleaseInline(hold);
        first.Move();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([GuideB, GuideA], store.Opens.Select(open => open.Guide));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task DisposeDuringAPendingOpenWaitsForIt()
    {
        ProgressCoordinator coordinator = new(store, clock);
        TaskCompletionSource hold = new();
        store.OpenHold = hold;
        IProgressTracking tracking = Track(coordinator, GuideA, new FakeSession(), null);

        Task disposing = tracking.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);
        ReleaseInline(hold);
        await disposing;

        Assert.Equal([(GuideA, clock.GetUtcNow())], store.Opens);
        Assert.Equal(1, coordinator.Counts.Opens);
        Assert.Equal(0, coordinator.Counts.Failures);
    }

    [Fact]
    public async Task FlushSavesAtOnceWhenDirty()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);

        session.Move();
        await tracking.FlushAsync(CancellationToken.None);

        Assert.Single(store.Writes);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Single(store.Writes);
    }

    [Fact]
    public async Task DisposeSavesPendingMovementOnceBeforeQuietTimer()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);

        session.Move();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await tracking.DisposeAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        session.Move();
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Single(store.Writes);
        Assert.Equal(0, session.Subscribers);
    }

    [Fact]
    public async Task DisposeDuringTimerCaptureWaitsAndWritesIt()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new() { Hold = new TaskCompletionSource() };
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);
        session.Move();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, session.Captures);

        Task disposing = tracking.DisposeAsync().AsTask();
        ReleaseInline(session.Hold!);
        await disposing;

        Assert.Equal([(GuideA, session.CurrentJson, (double?)null)], store.Writes);
        Assert.Equal(0, coordinator.Counts.Failures);
    }

    [Fact]
    public async Task FlushDuringBurstKeepsDeadlineAndWritesLatest()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);

        session.Move();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await tracking.FlushAsync(CancellationToken.None);
        for (int i = 0; i < 30; i++)
        {
            session.Move();
            clock.Advance(TimeSpan.FromMilliseconds(200));
        }
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(3, store.Writes.Count);
        Assert.Equal(session.CurrentJson, store.Writes[^1].Json);
    }

    [Fact]
    public void LateCaptureAfterNextTrackIsDiscarded()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession first = new() { Hold = new TaskCompletionSource() };
        Track(coordinator, GuideA, first, null);
        first.Move();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, first.Captures);

        FakeSession second = new() { Offset = 500 };
        Track(coordinator, GuideB, second, null);
        ReleaseInline(first.Hold!);
        second.Move();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([(GuideB, second.CurrentJson, (double?)null)], store.Writes);
        Assert.Equal(0, first.Subscribers);
    }

    [Fact]
    public void MovementDuringCaptureSavesAgain()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new() { Hold = new TaskCompletionSource() };
        Track(coordinator, GuideA, session, null);

        session.Move();
        string captured = session.CurrentJson;
        clock.Advance(TimeSpan.FromSeconds(1));
        session.Move();
        TaskCompletionSource hold = session.Hold!;
        session.Hold = null;
        ReleaseInline(hold);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([captured, session.CurrentJson], store.Writes.Select(w => w.Json));
    }

    [Fact]
    public void StoreFailureKeepsDirtyRetriesAndReportsOnce()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        Track(coordinator, GuideA, session, null);
        List<ProgressSaveFailedEventArgs> failed = [];
        coordinator.SaveFailed += (_, e) => failed.Add(e);
        store.Failures.Enqueue(new IOException("disk"));
        store.Failures.Enqueue(new IOException("disk"));

        session.Move();
        clock.Advance(TimeSpan.FromSeconds(1));
        clock.Advance(ProgressCoordinator.Deadline);
        Assert.Empty(store.Writes);
        clock.Advance(ProgressCoordinator.Deadline);

        Assert.Single(store.Writes);
        Assert.Equal(3, store.Attempts);
        Assert.Equal(GuideA, Assert.Single(failed).GuideId);
        Assert.Equal(new ProgressCounts(1, 0, 2, 1), coordinator.Counts);
    }

    [Fact]
    public async Task MissingRowClearsDirtyAndRaisesNothing()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);
        int failed = 0;
        coordinator.SaveFailed += (_, _) => failed++;
        store.Failures.Enqueue(new ReadingStateMissingException(GuideA));

        session.Move();
        clock.Advance(TimeSpan.FromSeconds(10));
        await tracking.FlushAsync(CancellationToken.None);
        await tracking.DisposeAsync();

        Assert.Equal(1, store.Attempts);
        Assert.Equal(0, failed);
    }

    [Fact]
    public async Task StuckCaptureIsCancelledByDisposeTimeout()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new() { NeverCompletes = true };
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);
        int failed = 0;
        coordinator.SaveFailed += (_, _) => failed++;

        session.Move();
        ValueTask dispose = tracking.DisposeAsync();
        Assert.False(dispose.IsCompleted);
        clock.Advance(ProgressCoordinator.FlushTimeout);

        await dispose.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(store.Writes);
        Assert.Equal(1, failed);
        Assert.Equal(0, session.Subscribers);
    }

    [Fact]
    public void SaveWritesTheEstimateInTheLocatorAndTheColumn()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new() { Estimate = 0.25 };
        Track(coordinator, GuideA, session, null);

        session.Move();
        clock.Advance(TimeSpan.FromSeconds(1));

        (_, string json, double? estimate) = Assert.Single(store.Writes);
        Assert.Equal(0.25, estimate);
        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            json, GuideFormat.Txt, FakeSession.Hash);
        Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
        Assert.Equal(0.25, decoded.Location!.EstimatedFraction);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    [InlineData(double.NaN, null)]
    [InlineData(double.PositiveInfinity, null)]
    [InlineData(double.NegativeInfinity, null)]
    public void OutOfRangeEstimateIsBoundedBeforeItIsWritten(double estimate, double? expected)
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new() { Estimate = estimate };
        Track(coordinator, GuideA, session, null);

        session.Move();
        clock.Advance(TimeSpan.FromSeconds(1));

        (_, string json, double? written) = Assert.Single(store.Writes);
        Assert.Equal(expected, written);
        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            json, GuideFormat.Txt, FakeSession.Hash);
        Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
        Assert.Equal(expected, decoded.Location!.EstimatedFraction);
        Assert.Equal(0, coordinator.Counts.Failures);
    }

    // A row saved by a T12.2 build has the same position and no estimate.
    [Fact]
    public void NullEstimateBaselineIsRewrittenWithTheEstimate()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        string t122Json = session.CurrentJson;
        session.Estimate = 0.4;
        Track(coordinator, GuideA, session, t122Json);

        session.Touch();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([(GuideA, session.CurrentJson, (double?)0.4)], store.Writes);
    }

    [Fact]
    public void TimerSavesPostToTrackingContext()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        RecordingContext context = new();
        Track(coordinator, GuideA, session, null, context);

        session.Move();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, session.Captures);

        context.RunAll();
        Assert.Single(store.Writes);
    }

    [Fact]
    public async Task AbandonStopsWithoutSaving()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);

        session.Move();
        tracking.Abandon();
        clock.Advance(TimeSpan.FromSeconds(10));
        await tracking.FlushAsync(CancellationToken.None);
        await tracking.DisposeAsync();

        Assert.Equal(0, store.Attempts);
        Assert.Equal(0, session.Subscribers);
    }

    private static IProgressTracking Track(
        ProgressCoordinator coordinator, Guid guideId, FakeSession session,
        string? restoredJson, SynchronizationContext? context = null)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            return coordinator.Track(guideId, session, restoredJson);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    // xUnit's sync context stops awaits from resuming inline, so release a
    // held capture without one to finish it before the next clock advance.
    private static void ReleaseInline(TaskCompletionSource hold)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            hold.SetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> posts = new();

        public override void Post(SendOrPostCallback d, object? state) => posts.Enqueue((d, state));

        public void RunAll()
        {
            while (posts.TryDequeue(out var post)) post.Callback(post.State);
        }
    }

    private sealed class FakeStore : IReadingLocationStore
    {
        public List<(Guid Guide, string Json, double? Estimate)> Writes { get; } = [];
        public Queue<Exception> Failures { get; } = new();
        public int Attempts { get; private set; }
        public List<(Guid Guide, DateTimeOffset At)> Opens { get; } = [];
        public Queue<Exception> OpenFailures { get; } = new();
        public TaskCompletionSource? OpenHold { get; set; }
        public List<string> Events { get; } = [];

        public Task SaveReadingLocationAsync(
            Guid guideId, string locatorJson, double? estimatedFraction,
            CancellationToken token = default)
        {
            Attempts++;
            if (Failures.TryDequeue(out Exception? error)) return Task.FromException(error);
            Writes.Add((guideId, locatorJson, estimatedFraction));
            Events.Add("save");
            return Task.CompletedTask;
        }

        public async Task RecordGuideOpenedAsync(
            Guid guideId, DateTimeOffset openedUtc, CancellationToken token = default)
        {
            if (OpenHold is TaskCompletionSource hold) await hold.Task.ConfigureAwait(false);
            if (OpenFailures.TryDequeue(out Exception? error)) throw error;
            Opens.Add((guideId, openedUtc));
            Events.Add("open");
        }
    }

    private sealed class FakeSession : IReaderSession
    {
        public static readonly string Hash = new('a', 64);
        private EventHandler<LocationChangedEventArgs>? locationChanged;

        public int Offset { get; set; }
        public double? Estimate { get; set; }
        public TaskCompletionSource? Hold { get; set; }
        public bool NeverCompletes { get; init; }
        public int Captures { get; private set; }
        public int Subscribers => locationChanged?.GetInvocationList().Length ?? 0;

        public GuideFormat Format => GuideFormat.Txt;
        public ReaderCapabilities Capabilities => ReaderCapabilities.Scroll;
        public event EventHandler? CapabilitiesChanged { add { } remove { } }
        public event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored { add { } remove { } }
        public event EventHandler<LocationChangedEventArgs>? LocationChanged
        {
            add => locationChanged += value;
            remove => locationChanged -= value;
        }

        public ReaderLocation Current => new(
            GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, Hash,
            new TextPosition(Offset, $"line {Offset}"), Estimate);

        public string CurrentJson =>
            ReaderLocationCodec.Serialize(Current with { EstimatedFraction = ProgressEstimate.Bound(Estimate) });

        public void Move()
        {
            Offset++;
            Touch();
        }

        public void Touch() => locationChanged?.Invoke(this, new LocationChangedEventArgs());

        public async Task<ReaderLocation> GetLocationAsync(CancellationToken token)
        {
            Captures++;
            ReaderLocation snapshot = Current;
            if (NeverCompletes) await new TaskCompletionSource().Task.ConfigureAwait(false);
            if (Hold is TaskCompletionSource hold) await hold.Task.ConfigureAwait(false);
            return snapshot;
        }

        public Task OpenAsync(ManagedGuideSource source, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token) =>
            throw new NotSupportedException();
        public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) =>
            throw new NotSupportedException();
        public Task ExecuteAsync(ReaderAction action, CancellationToken token) =>
            throw new NotSupportedException();
        public ValueTask DisposeAsync() => default;
    }

    // Fires due timers in order on the calling thread. Elapsed is the time
    // since the clock was created.
    private sealed class ManualClock : TimeProvider
    {
        private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset now = Start;

        public TimeSpan Elapsed => now - Start;

        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ManualTimer timer = new(this, callback, state);
            lock (timers) timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            DateTimeOffset end = now + by;
            while (true)
            {
                ManualTimer? next;
                lock (timers)
                {
                    next = timers
                        .Where(timer => timer.Due is DateTimeOffset due && due <= end)
                        .OrderBy(timer => timer.Due)
                        .FirstOrDefault();
                }
                if (next is null) break;
                now = next.Due!.Value;
                next.Fire();
            }
            now = end;
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            private TimeSpan period = Timeout.InfiniteTimeSpan;

            public DateTimeOffset? Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime;
                this.period = period;
                return true;
            }

            public void Fire()
            {
                Due = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero
                    ? null
                    : Due + period;
                callback(state);
            }

            public void Dispose()
            {
                Due = null;
                lock (clock.timers) clock.timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }
        }
    }
}
