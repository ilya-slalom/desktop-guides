using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Reading;

// Failures counts failed saves; OpenFailures counts open times that weren't recorded.
public sealed record ProgressCounts(
    int Saves, int SkippedUnchanged, int Failures, int Opens, int OpenFailures = 0);

// Where the latest failure happened (open, turn, capture or write) and its
// exception type and HResult. Never the message, which may hold a path.
// Trigger says what started the save (timer, flush or close), and CaptureMs
// and WriteMs how long each stage ran before it ended, so a timed-out save
// shows where its time went (#94).
public sealed record ProgressFailure(
    string Stage, string ErrorType, int HResult,
    string Trigger = "", long CaptureMs = 0, long WriteMs = 0);

public sealed class ProgressSaveFailedEventArgs(Guid guideId, Exception error) : EventArgs
{
    public Guid GuideId { get; } = guideId;
    public Exception Error { get; } = error;
}

public interface IProgressTracking : IAsyncDisposable
{
    // Saves now if the position moved since the last save; never throws.
    Task FlushAsync(CancellationToken token);

    // Stops tracking without saving, for a session that has failed.
    void Abandon();

    // T18.1: saves a pending move, then saves nothing (not even on flush or
    // dispose) until the returned handle is disposed. Search holds it, so
    // moving to matches never moves the saved place.
    Task<IDisposable> PauseAsync(CancellationToken token);
}

// Saves the open guide's position 1 s after movement stops, or 4 s after the
// first unsaved movement during continuous movement, and on flush or dispose.
// Each tracking first records the open time.
// One guide is tracked at a time; a tracking that has ended never writes.
public sealed class ProgressCoordinator
{
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    private readonly IReadingLocationStore store;
    private readonly TimeProvider clock;
    private readonly object countsGate = new();
    private Tracking? current;
    private int saves;
    private int skippedUnchanged;
    private int failures;
    private int opens;
    private int openFailures;
    private ProgressFailure? lastFailure;

    public ProgressCoordinator(IReadingLocationStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        this.store = store;
        this.clock = clock;
    }

    public event EventHandler<ProgressSaveFailedEventArgs>? SaveFailed;
    public event EventHandler? CountsChanged;

    public ProgressCounts Counts
    {
        get
        {
            lock (countsGate) return new(saves, skippedUnchanged, failures, opens, openFailures);
        }
    }

    public ProgressFailure? LastFailure
    {
        get
        {
            lock (countsGate) return lastFailure;
        }
    }

    // restoredJson is the locator the guide was restored from, or the
    // position captured after opening, so an unmoved guide isn't rewritten.
    // T21.2: a map is tracked without recording an open, so it isn't
    // reading activity.
    public IProgressTracking Track(Guid guideId, IReaderSession session, string? restoredJson, bool recordOpen = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        Tracking tracking = new(this, guideId, session, restoredJson, SynchronizationContext.Current);
        Interlocked.Exchange(ref current, tracking)?.End();
        // The open is the tracking's first turn, so a save waits for it.
        if (recordOpen) _ = tracking.RecordOpenAsync(clock.GetUtcNow());
        return tracking;
    }

    private void Record(int saved, int skipped, int opened = 0)
    {
        lock (countsGate)
        {
            saves += saved;
            skippedUnchanged += skipped;
            opens += opened;
        }
        CountsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RecordFailure(
        string stage, Exception error, string trigger = "", long captureMs = 0, long writeMs = 0)
    {
        lock (countsGate)
        {
            if (stage == "open") openFailures++;
            else failures++;
            lastFailure = new(stage, error.GetType().Name, error.HResult, trigger, captureMs, writeMs);
        }
        CountsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Tracking : IProgressTracking
    {
        private readonly ProgressCoordinator owner;
        private readonly Guid guideId;
        private readonly IReaderSession session;
        private readonly SynchronizationContext? context;
        private readonly object gate = new();
        private readonly SemaphoreSlim turn = new(1, 1);
        private readonly ITimer quiet;
        private readonly ITimer deadline;
        private string? lastJson;
        private bool dirty;
        private bool deadlineArmed;
        private bool disposing;
        private bool ended;
        private bool failureReported;
        private int pauses;

        public Tracking(
            ProgressCoordinator owner, Guid guideId, IReaderSession session,
            string? restoredJson, SynchronizationContext? context)
        {
            this.owner = owner;
            this.guideId = guideId;
            this.session = session;
            this.context = context;
            lastJson = restoredJson;
            quiet = owner.clock.CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            deadline = owner.clock.CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            session.LocationChanged += OnLocationChanged;
        }

        public async Task FlushAsync(CancellationToken token)
        {
            lock (gate)
            {
                if (ended) return;
            }
            // Not dirty may mean a save is mid-capture: SaveAsync waits for it.
            await SaveAsync(token, "flush");
        }

        public async ValueTask DisposeAsync()
        {
            lock (gate)
            {
                if (ended || disposing) return;
                disposing = true;
                StopTimers();
            }
            session.LocationChanged -= OnLocationChanged;
            using (CancellationTokenSource timeout = new(FlushTimeout, owner.clock))
            {
                await FlushAsync(timeout.Token);
            }
            End();
        }

        public void Abandon() => End();

        public async Task<IDisposable> PauseAsync(CancellationToken token)
        {
            await FlushAsync(token);
            lock (gate)
            {
                pauses++;
                StopTimers();
            }
            return new Pause(this);
        }

        private void Resume()
        {
            bool rebaseline;
            lock (gate)
            {
                if (pauses > 0) pauses--;
                rebaseline = pauses == 0 && !ended && !disposing;
            }
            if (rebaseline) _ = RebaselineAsync();
        }

        // T18.1: the place shown when search ends becomes the baseline, so a
        // late event from the last match (closing the bar resizes the view)
        // isn't saved; only the user's next move is. Never throws.
        private async Task RebaselineAsync()
        {
            using CancellationTokenSource timeout = new(FlushTimeout, owner.clock);
            try
            {
                await turn.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                lock (gate)
                {
                    if (ended) return;
                }
                ReaderLocation location = await session.GetLocationAsync(timeout.Token)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                double? estimate = ProgressEstimate.Bound(location.EstimatedFraction);
                lastJson = ReaderLocationCodec.Serialize(location with { EstimatedFraction = estimate });
            }
            catch (Exception)
            {
                // Keep the old baseline: a later move still saves.
            }
            finally
            {
                turn.Release();
            }
        }

        private sealed class Pause(Tracking owner) : IDisposable
        {
            private int disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Resume();
            }
        }

        internal void End()
        {
            lock (gate)
            {
                if (ended) return;
                ended = true;
                StopTimers();
            }
            session.LocationChanged -= OnLocationChanged;
            quiet.Dispose();
            deadline.Dispose();
            Interlocked.CompareExchange(ref owner.current, null, this);
        }

        private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            lock (gate)
            {
                if (ended || disposing || pauses > 0) return;
                dirty = true;
                quiet.Change(Quiet, Timeout.InfiniteTimeSpan);
                ArmDeadline();
            }
        }

        private void Fire()
        {
            if (context is null) _ = SaveFromTimerAsync();
            else context.Post(_ => _ = SaveFromTimerAsync(), null);
        }

        // Never throws. A failure is counted as an open failure but not reported:
        // the guide did open, and the next open records it again.
        internal async Task RecordOpenAsync(DateTimeOffset openedUtc)
        {
            using CancellationTokenSource timeout = new(FlushTimeout, owner.clock);
            try
            {
                await turn.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                lock (gate)
                {
                    if (ended) return;
                }
                await owner.store.RecordGuideOpenedAsync(guideId, openedUtc, timeout.Token)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                owner.Record(0, 0, 1);
            }
            catch (ReadingStateMissingException)
            {
                // The guide was removed.
            }
            catch (Exception error)
            {
                owner.RecordFailure("open", error);
            }
            finally
            {
                turn.Release();
            }
        }

        private async Task SaveFromTimerAsync()
        {
            using CancellationTokenSource timeout = new(FlushTimeout, owner.clock);
            await SaveAsync(timeout.Token, "timer");
        }

        // Never throws. A capture or write that finishes after End is dropped.
        private async Task SaveAsync(CancellationToken token, string trigger)
        {
            lock (gate)
            {
                // Dispose flushes; that is the last save, on Back or close.
                if (disposing) trigger = "close";
            }
            try
            {
                await turn.WaitAsync(token);
            }
            catch (OperationCanceledException error)
            {
                // A flush with nothing to save that timed out behind a stuck save isn't a failure.
                lock (gate)
                {
                    if (!dirty) return;
                }
                Failed("turn", error, trigger);
                return;
            }
            string stage = "capture";
            DateTimeOffset captureStarted = owner.clock.GetUtcNow();
            DateTimeOffset? writeStarted = null;
            try
            {
                lock (gate)
                {
                    if (ended || !dirty || pauses > 0) return;
                    dirty = false;
                    StopTimers();
                }
                ReaderLocation location = await session.GetLocationAsync(token)
                    .WaitAsync(token).ConfigureAwait(false);
                double? estimate = ProgressEstimate.Bound(location.EstimatedFraction);
                string json = ReaderLocationCodec.Serialize(location with { EstimatedFraction = estimate });
                lock (gate)
                {
                    if (ended) return;
                }
                if (json == lastJson)
                {
                    owner.Record(0, 1);
                    return;
                }
                stage = "write";
                writeStarted = owner.clock.GetUtcNow();
                await owner.store.SaveReadingLocationAsync(guideId, json, estimate, token)
                    .WaitAsync(token).ConfigureAwait(false);
                lastJson = json;
                owner.Record(1, 0);
            }
            catch (ReadingStateMissingException)
            {
                // The guide was removed; there is nothing left to save.
            }
            catch (Exception error)
            {
                DateTimeOffset ended = owner.clock.GetUtcNow();
                long captureMs = Milliseconds(captureStarted, writeStarted ?? ended);
                long writeMs = writeStarted is DateTimeOffset started ? Milliseconds(started, ended) : 0;
                Failed(stage, error, trigger, captureMs, writeMs);
            }
            finally
            {
                turn.Release();
            }
        }

        private static long Milliseconds(DateTimeOffset from, DateTimeOffset to) =>
            (long)Math.Round((to - from).TotalMilliseconds);

        private void Failed(
            string stage, Exception error, string trigger, long captureMs = 0, long writeMs = 0)
        {
            bool report;
            lock (gate)
            {
                if (ended) return;
                dirty = true;
                if (!disposing) ArmDeadline();
                report = !failureReported;
                failureReported = true;
            }
            owner.RecordFailure(stage, error, trigger, captureMs, writeMs);
            if (report) owner.SaveFailed?.Invoke(owner, new ProgressSaveFailedEventArgs(guideId, error));
        }

        private void ArmDeadline()
        {
            if (deadlineArmed) return;
            deadlineArmed = true;
            deadline.Change(Deadline, Timeout.InfiniteTimeSpan);
        }

        private void StopTimers()
        {
            deadlineArmed = false;
            quiet.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            deadline.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }
}
