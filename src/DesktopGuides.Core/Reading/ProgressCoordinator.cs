using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Reading;

public sealed record ProgressCounts(int Saves, int SkippedUnchanged, int Failures);

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
}

// Saves the open guide's position 1 s after movement stops, or 4 s after the
// first unsaved movement during continuous movement, and on flush or dispose.
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
            lock (countsGate) return new(saves, skippedUnchanged, failures);
        }
    }

    // restoredJson is the locator the guide was restored from, or the
    // position captured after opening, so an unmoved guide isn't rewritten.
    public IProgressTracking Track(Guid guideId, IReaderSession session, string? restoredJson)
    {
        ArgumentNullException.ThrowIfNull(session);
        Tracking tracking = new(this, guideId, session, restoredJson, SynchronizationContext.Current);
        Interlocked.Exchange(ref current, tracking)?.End();
        return tracking;
    }

    private void Record(int saved, int skipped, int failed)
    {
        lock (countsGate)
        {
            saves += saved;
            skippedUnchanged += skipped;
            failures += failed;
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
            await SaveAsync(token);
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
                if (ended || disposing) return;
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

        private async Task SaveFromTimerAsync()
        {
            using CancellationTokenSource timeout = new(FlushTimeout, owner.clock);
            await SaveAsync(timeout.Token);
        }

        // Never throws. A capture or write that finishes after End is dropped.
        private async Task SaveAsync(CancellationToken token)
        {
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
                Failed(error);
                return;
            }
            try
            {
                lock (gate)
                {
                    if (ended || !dirty) return;
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
                    owner.Record(0, 1, 0);
                    return;
                }
                await owner.store.SaveReadingLocationAsync(guideId, json, estimate, token)
                    .WaitAsync(token).ConfigureAwait(false);
                lastJson = json;
                owner.Record(1, 0, 0);
            }
            catch (ReadingStateMissingException)
            {
                // The guide was removed; there is nothing left to save.
            }
            catch (Exception error)
            {
                Failed(error);
            }
            finally
            {
                turn.Release();
            }
        }

        private void Failed(Exception error)
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
            owner.Record(0, 0, 1);
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
