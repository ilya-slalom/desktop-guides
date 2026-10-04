# T12.2 Progress Coordinator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Save each guide's reading position through a debounced, session-aware
`ProgressCoordinator` and restore it when the guide is reopened, with flushes on
leaving the Reader, window deactivation and window close.

**Architecture:** A pure Core `ProgressCoordinator` subscribes to an open
`IReaderSession`'s `LocationChanged`, runs a 1 s quiet timer and a 4 s deadline
timer from a `TimeProvider`, and writes through a narrow `IReadingLocationStore`
that `ILibraryRepository` extends. `ShellWindow` restores the stored locator once a
session is showing, reports inexact restores in the status InfoBar, then hands the
session to the coordinator; leaving the Reader, deactivation and closing flush it.
A new installed `progress` scenario group proves the timer, flush, burst and
two-guide behavior end to end.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), WebView2, xUnit, PowerShell 5.1 UI Automation smoke.

**Spec:** [t12-2-progress-coordinator-design.md](t12-2-progress-coordinator-design.md)

## Global Constraints

**Branch:** `feat/p1-t12-2-progress-coordinator` (already checked out; the spec is commit `831ec8d`).

**Tooling:**

- There is no local `dotnet` or `pwsh`. Every build and test runs in CI.
- The CI loop for branch `<b>`:
  1. Push, then `gh workflow run windows-ci.yml --ref <b>`.
  2. `gh run list --workflow windows-ci.yml --branch <b> --limit 1 --json databaseId,headSha -q '.[0]'`, and check that `headSha` matches `git rev-parse HEAD`.
  3. `gh run watch <id> --exit-status --interval 60`.
  4. On failure, `gh run view <id> --log-failed`. Smoke errors are in the `error`
     field of the `*shell*` artifact JSON (`gh run download <id> --pattern '*shell*'`;
     the JSON has a BOM).
- `core-tests` takes about 3 minutes and runs Core.Tests and Infrastructure.Tests.
  `production-shell-ui` takes about 20 minutes. A core-only step may `gh run cancel`
  the run once `core-tests` has finished.
- Pass counts: `gh api repos/ilya-slalom/desktop-guides/actions/jobs/<job-id>/logs | grep "Passed!"`.
- A RED run may be batched with the previous task's GREEN run only when they land
  in different jobs.
- ASCII check: `LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/*.ps1` (expect no output).
- `-f shell-scope=<group> -f dev-fast=true` is for iteration and is not PR evidence.

**Values (verbatim):**

- Quiet timer: `TimeSpan.FromSeconds(1)`. Deadline timer: `TimeSpan.FromSeconds(4)`.
  Flush timeout: `TimeSpan.FromSeconds(2)`.
- Written estimate: always `null`. `LastOpenedUtcMs` and `CompletedUtcMs` are never touched.
- Approximate status (Informational, closable, not auto-dismissed):
  `Opened near your last place. The guide changed since you were here.`
- Unavailable status (Warning): `Couldn't return to your last place, so the guide opened at the start.`
- Save failure status (Warning): `Couldn't save your place in this guide.`
- Override gate: `Local\DesktopGuides.Preview.ProgressOverride.<pid>`; file
  `<DataRoot>\test\restore-locator.json`, at most `ReaderLocationCodec.MaxBytes` (4096) bytes.
- Diagnostics gate: `Local\DesktopGuides.Preview.ProgressDiagnostics.<pid>`; file
  `<CacheRoot>\diagnostics\progress-<pid>.json` holding `{ "saves", "skippedUnchanged", "failures" }`.
- Scenario group `progress`, runner switch `-ProgressOnly`, smoke modes
  `progress-timer`, `progress-restored`, `progress-two-guides`.

**Rules:**

- Stored and override locators are untrusted and reach a session only through
  `ReaderLocationCodec.Deserialize`. Nothing logs locator text, guide text or paths.
- Originals stay untouched; no new network access; no change to T07.3's WebView2
  settings or CSP.
- PowerShell stays ASCII-only.
- UI tests assert what app code controls (status text, top line, page, saved
  counts), not WinUI rendering.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

**Spec refinements made while planning** (Task 6 records each in the design doc):

1. **Threading.** `Track` captures `SynchronizationContext.Current`; timer
   callbacks `Post` the save to it (inline when it is null) instead of the thread
   pool, so every `GetLocationAsync` starts on the thread that opened the session.
   The turn wait keeps the context; the capture and write awaits use
   `ConfigureAwait(false)` and `WaitAsync(token)`, so a stuck capture can't outlive
   its timeout.
2. **Baseline.** After an `Exact` restore the baseline is the stored JSON (as the
   spec says). After any other outcome, or with no locator, the shell captures the
   post-restore position and passes its serialized JSON as `restoredJson`, so an
   adapter that raises `LocationChanged` while settling (HTML after a resize, PDF
   after its first page) doesn't rewrite an unmoved guide. An `Unavailable` or
   `Approximate` restore still keeps the stored locator until the reader moves.
3. **`IProgressTracking.Abandon()`** ends a tracking without flushing; the shell
   calls it when a session fails (`html-crash`, PDF failure), where a capture
   can't succeed.
4. **`ReadingStateMissingException`** (Core/Library, derives from
   `InvalidOperationException`) replaces `RequireUpdated`'s generic exception for a
   missing `ReadingStates` row, so the coordinator recognises a removed guide
   without matching message text.
5. **`CountsChanged` and `Counts`** on the coordinator feed the diagnostics file.
6. **Restore-phase smoke.** The `html-position` restore phases use
   `test\restore-locator.json` through the shell's override; `restore-invalid` is
   decoded `Invalid` by the shell, so the session never restores and the phase no
   longer adds to `restoreKinds`. Phases that reopen a guide in the same pass now
   restore its saved place, so `unimported-link` and the final open expect
   `Exact`, and `txt-switch` expects the saved anchor line.
7. **`progress-messages`** is folded in: the `Approximate` and HTML `Unavailable`
   screenshots come from the `html-position` phases (light and dark), and the TXT
   `Unavailable` screenshot from `progress-two-guides`.
8. **Installed test layout.** Three smoke modes over three launches of one data
   root: `progress-timer` (then kill), `progress-restored` (covers `progress-flush`
   and `progress-burst`, then a normal close), `progress-two-guides`.
9. **Burst bound.** 20 Next page and 10 Previous page presses 100 ms apart; the
   allowed saves are `floor(elapsed / 4 s) + 1` (one per deadline that can fall
   inside the burst, plus the final quiet save). The phase fails a burst of 8 s or
   more, so the allowance never exceeds the spec's 2; UI Automation makes the
   presses take longer than the spec's 3 s, which is why the bound uses the
   measured time.
10. **Failure retry.** A failed save re-arms only the 4 s deadline (not during
    dispose), so a broken store is retried every 4 s, not on every movement.
11. **Restore-read failure.** If `GetReadingStateAsync` throws (other than
    cancellation), the restore counts as `Unavailable`.
12. **TXT with a changed file and no estimate.** T12.2 writes a null estimate, so a
    TXT guide whose content changed and whose context isn't found restores
    `Unavailable` (the codec offers `Approximate` only with an estimate) until
    T12.3 writes estimates.
13. **Deactivation flush** uses a real 2 s `CancellationTokenSource`; timer and
    dispose saves are bounded through the coordinator's `TimeProvider`.
14. **Clearing between passes.** A ShellSeed verb `clear-reading-locations`
    nulls every saved locator; the runner calls it between passes that share a
    data folder, so each pass starts every guide at its start as before.

## Review Focus

1. **Close save racing the quiet timer.** Leaving the Reader within 1 s of the last
   movement must still save that movement once, not zero or two times. Pinned by
   `DisposeSavesPendingMovementOnceBeforeQuietTimer` (Task 1) and `progress-flush`
   (Task 5).
2. **Deactivation during a burst.** A focus change mid-burst adds at most one save
   and never writes a stale position after a later one. Pinned by
   `FlushDuringBurstKeepsDeadlineAndWritesLatest` (Task 1).
3. **Settling movement after restore.** An adapter that raises `LocationChanged`
   right after a restore (PDF first page, HTML tracker tick, InfoBar height change)
   must not rewrite an unmoved guide. Pinned by
   `MovementBackToBaselineSkipsWrite` (Task 1) and the `progress-burst` save bound
   (Task 5).
4. **A late capture for the previous guide.** Opening guide B while A's capture is
   in flight must never write A's locator under B's ID. Pinned by
   `LateCaptureAfterNextTrackIsDiscarded` (Task 1).
5. **Reopening within a pass.** Smoke phases that reopen a guide now land on its
   saved place; any phase that assumed the start must be adapted or cleared.
   Pinned by the full GREEN run of every scenario group (Tasks 4 and 5), including
   the `txt` canary and ASCII phases.

## File map

| File | Change | Responsibility |
| --- | --- | --- |
| `src/DesktopGuides.Core/Reading/IReadingLocationStore.cs` | Create | Narrow save interface for the coordinator |
| `src/DesktopGuides.Core/Reading/ProgressCoordinator.cs` | Create | Debounce, flush, generation guard, failure reporting, counts |
| `src/DesktopGuides.Core/Library/ReadingStateMissingException.cs` | Create | Typed missing-row failure |
| `src/DesktopGuides.Core/Library/ILibraryRepository.cs` | Modify | Extend `IReadingLocationStore` |
| `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` | Modify | Throw `ReadingStateMissingException` |
| `tests/DesktopGuides.Core.Tests/ProgressCoordinatorTests.cs` | Create | Fake-clock coordinator tests |
| `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs` | Modify | Missing-row type; two-guide persistence through the coordinator |
| `src/DesktopGuides.Production/ShellWindow.Progress.cs` | Create | Restore on open, override, tracking lifetime, flush hooks, diagnostics |
| `src/DesktopGuides.Production/ShellWindow.xaml.cs` | Modify | Start the coordinator, TXT restore, close and leave hooks |
| `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs` | Modify | HTML restore and tracking; abandon on failure |
| `src/DesktopGuides.Production/ShellWindow.PdfReader.cs` | Modify | PDF restore and tracking; abandon on failure |
| `src/DesktopGuides.Production/HtmlReaderSession.cs` | Modify | Remove the test-only restore file |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | Modify | `seed-progress`, `clear-reading-locations` |
| `tools/p1/windows_shell_install.ps1` | Modify | `-ProgressOnly`, progress group, clears between passes |
| `tools/p1/windows_shell_ui_smoke.ps1` | Modify | Override helpers, restore-phase edits, progress modes |
| `.github/workflows/windows-ci.yml` | Modify | `progress` shell scope |
| `docs/p1/t12-2-progress-coordinator-design.md` and related docs | Modify | Status, notes, evidence |

---

### Task 1: Core progress coordinator

**Files:**
- Create: `src/DesktopGuides.Core/Reading/IReadingLocationStore.cs`
- Create: `src/DesktopGuides.Core/Library/ReadingStateMissingException.cs`
- Create: `src/DesktopGuides.Core/Reading/ProgressCoordinator.cs`
- Test: `tests/DesktopGuides.Core.Tests/ProgressCoordinatorTests.cs`

**Interfaces:**
- Consumes: `IReaderSession` (`LocationChanged`, `GetLocationAsync`),
  `ReaderLocationCodec.Serialize`, `ReaderLocation` (all existing).
- Produces:
  - `IReadingLocationStore.SaveReadingLocationAsync(Guid guideId, string locatorJson, double? estimatedFraction, CancellationToken token = default)`.
  - `ReadingStateMissingException(Guid guideId) : InvalidOperationException`, property `Guid GuideId`.
  - `ProgressCoordinator(IReadingLocationStore store, TimeProvider clock)` with
    `static TimeSpan Quiet`, `Deadline`, `FlushTimeout`;
    `IProgressTracking Track(Guid guideId, IReaderSession session, string? restoredJson)`;
    `ProgressCounts Counts`; events `SaveFailed` (`ProgressSaveFailedEventArgs`) and `CountsChanged` (`EventArgs`).
  - `IProgressTracking : IAsyncDisposable` with `Task FlushAsync(CancellationToken token)` and `void Abandon()`.
  - `sealed record ProgressCounts(int Saves, int SkippedUnchanged, int Failures)`.
  - `sealed class ProgressSaveFailedEventArgs(Guid guideId, Exception error) : EventArgs`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/ProgressCoordinatorTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

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
        Assert.Equal(new ProgressCounts(1, 0, 0), coordinator.Counts);
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
        Assert.Equal(new ProgressCounts(1, 1, 0), coordinator.Counts);
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
        Assert.Equal(new ProgressCounts(0, 1, 0), coordinator.Counts);
    }

    [Fact]
    public async Task NoMovementWritesNothing()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);

        clock.Advance(TimeSpan.FromSeconds(10));
        await tracking.FlushAsync(CancellationToken.None);
        await tracking.DisposeAsync();

        Assert.Equal(0, store.Attempts);
        Assert.Equal(0, session.Captures);
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
        first.Hold!.SetResult();
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
        hold.SetResult();
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
        Assert.Equal(new ProgressCounts(1, 0, 2), coordinator.Counts);
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
    public void WrittenEstimateIsAlwaysNull()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new() { Estimate = 0.5 };
        Track(coordinator, GuideA, session, null);

        session.Move();
        clock.Advance(TimeSpan.FromSeconds(1));

        (_, string json, double? estimate) = Assert.Single(store.Writes);
        Assert.Null(estimate);
        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            json, GuideFormat.Txt, FakeSession.Hash);
        Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
        Assert.Null(decoded.Location!.EstimatedFraction);
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

        public Task SaveReadingLocationAsync(
            Guid guideId, string locatorJson, double? estimatedFraction,
            CancellationToken token = default)
        {
            Attempts++;
            if (Failures.TryDequeue(out Exception? error)) return Task.FromException(error);
            Writes.Add((guideId, locatorJson, estimatedFraction));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSession : IReaderSession
    {
        public static readonly string Hash = new('a', 64);
        private EventHandler<LocationChangedEventArgs>? locationChanged;

        public int Offset { get; set; }
        public double? Estimate { get; init; }
        public TaskCompletionSource? Hold { get; set; }
        public bool NeverCompletes { get; init; }
        public int Captures { get; private set; }
        public int Subscribers => locationChanged?.GetInvocationList().Length ?? 0;

        public GuideFormat Format => GuideFormat.Txt;
        public ReaderCapabilities Capabilities => ReaderCapabilities.Scroll;
        public event EventHandler? CapabilitiesChanged { add { } remove { } }
        public event EventHandler<LocationChangedEventArgs>? LocationChanged
        {
            add => locationChanged += value;
            remove => locationChanged -= value;
        }

        public ReaderLocation Current => new(
            GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, Hash,
            new TextPosition(Offset, $"line {Offset}"), Estimate);

        public string CurrentJson =>
            ReaderLocationCodec.Serialize(Current with { EstimatedFraction = null });

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
            if (NeverCompletes) await new TaskCompletionSource().Task;
            if (Hold is TaskCompletionSource hold) await hold.Task;
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: commit the test file alone (`git add tests/DesktopGuides.Core.Tests/ProgressCoordinatorTests.cs && git commit -m "test(core): T12.2 progress coordinator tests (RED)"`), push, and start the CI loop. Cancel the run once `core-tests` finishes.
Expected: `core-tests` FAILS to build with `CS0246: The type or namespace name 'ProgressCoordinator' could not be found` (and the same for `IReadingLocationStore`, `IProgressTracking`, `ReadingStateMissingException`).

- [ ] **Step 3: Write the store interface and missing-row exception**

Create `src/DesktopGuides.Core/Reading/IReadingLocationStore.cs`:

```csharp
namespace DesktopGuides.Core.Reading;

// The one write the progress coordinator needs from the library.
public interface IReadingLocationStore
{
    Task SaveReadingLocationAsync(
        Guid guideId, string locatorJson, double? estimatedFraction,
        CancellationToken token = default);
}
```

Create `src/DesktopGuides.Core/Library/ReadingStateMissingException.cs`:

```csharp
namespace DesktopGuides.Core.Library;

// The guide's ReadingStates row is gone, usually because the guide was removed.
public sealed class ReadingStateMissingException(Guid guideId)
    : InvalidOperationException($"Guide {guideId} has no reading state.")
{
    public Guid GuideId { get; } = guideId;
}
```

- [ ] **Step 4: Write the coordinator**

Create `src/DesktopGuides.Core/Reading/ProgressCoordinator.cs`:

```csharp
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
                if (ended || !dirty) return;
            }
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
                string json = ReaderLocationCodec.Serialize(location with { EstimatedFraction = null });
                lock (gate)
                {
                    if (ended) return;
                }
                if (json == lastJson)
                {
                    owner.Record(0, 1, 0);
                    return;
                }
                await owner.store.SaveReadingLocationAsync(guideId, json, null, token)
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
```

Notes for the implementer:
- `owner.current` and `owner.clock` are private fields of the outer class; the
  nested class may use them.
- `Failed` during an ended tracking returns before counting, so an abandoned or
  replaced tracking reports nothing.
- `FlushTimeout` is shared by timer saves, flushes from dispose and the shell's
  deactivation flush (which uses its own real-time CTS).

- [ ] **Step 5: Run the tests to verify they pass**

Run: commit, push and start the CI loop; cancel once `core-tests` finishes.
Expected: `core-tests` PASSES; `Passed!` for Core.Tests includes 16 new `ProgressCoordinatorTests` cases.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Core/Reading/IReadingLocationStore.cs \
        src/DesktopGuides.Core/Reading/ProgressCoordinator.cs \
        src/DesktopGuides.Core/Library/ReadingStateMissingException.cs
git commit -m "feat(core): T12.2 ProgressCoordinator with quiet and deadline saves

- IReadingLocationStore and ReadingStateMissingException
- one tracking per guide; late captures after the next Track are dropped
- flush, dispose with a 2 s timeout, abandon; failures retried at the deadline

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Repository as the location store

**Files:**
- Modify: `src/DesktopGuides.Core/Library/ILibraryRepository.cs:5,26-28`
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs:461`
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 1's `IReadingLocationStore`, `ReadingStateMissingException`,
  `ProgressCoordinator`, `IProgressTracking`.
- Produces: `ILibraryRepository : IReadingLocationStore, IAsyncDisposable`; a
  missing `ReadingStates` row throws `ReadingStateMissingException`.

- [ ] **Step 1: Write the failing tests**

In `SqliteLibraryRepositoryTests.cs`, add `using DesktopGuides.Core.Reading;` after
`using DesktopGuides.Core.Library;`. In the validation test, change the missing-row
assertion (lines 86-87) to:

```csharp
        ReadingStateMissingException missing = await Assert.ThrowsAsync<ReadingStateMissingException>(() =>
            repository.SaveReadingLocationAsync(Guid.NewGuid(), "{}", 0.5));
        Assert.NotEqual(Guid.Empty, missing.GuideId);
```

Add this test after `PersistsTwoIndependentGuideStatesAndSettingsAcrossReopen`:

```csharp
    [Fact]
    public async Task ProgressCoordinatorSavesEachGuidesLocatorAcrossReopen()
    {
        using TestLibrary directory = new();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        StoreSession firstSession = new(120);
        StoreSession secondSession = new(9000);
        await using (SqliteLibraryRepository repository =
            new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await repository.InitializeAsync();
            Game game = await repository.AddGameAsync("Progress", null, null);
            InsertGuide(directory.Paths.DatabasePath, first, game.Id);
            InsertGuide(directory.Paths.DatabasePath, second, game.Id);
            ProgressCoordinator coordinator = new(repository, TimeProvider.System);

            await using (IProgressTracking tracking = coordinator.Track(first, firstSession, null))
            {
                firstSession.Move();
            }
            await using (IProgressTracking tracking = coordinator.Track(second, secondSession, null))
            {
                secondSession.Move();
            }
            Assert.Equal(new ProgressCounts(2, 0, 0), coordinator.Counts);
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        foreach ((Guid guideId, StoreSession session) in new[] { (first, firstSession), (second, secondSession) })
        {
            ReadingState? state = await reopened.GetReadingStateAsync(guideId);
            Assert.Null(state?.EstimatedFraction);
            LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
                state?.LocatorJson, GuideFormat.Txt, StoreSession.Hash);
            Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
            Assert.Equal(session.Current with { EstimatedFraction = null }, decoded.Location);
        }
    }
```

Add this helper class beside `FixedTimeProvider`:

```csharp
    private sealed class StoreSession(int offset) : IReaderSession
    {
        public static readonly string Hash = new('a', 64);
        private int offset = offset;

        public GuideFormat Format => GuideFormat.Txt;
        public ReaderCapabilities Capabilities => ReaderCapabilities.Scroll;
        public event EventHandler? CapabilitiesChanged { add { } remove { } }
        public event EventHandler<LocationChangedEventArgs>? LocationChanged;

        public ReaderLocation Current => new(
            GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, Hash,
            new TextPosition(offset, $"line {offset}"), 0.4);

        public void Move()
        {
            offset++;
            LocationChanged?.Invoke(this, new LocationChangedEventArgs());
        }

        public Task<ReaderLocation> GetLocationAsync(CancellationToken token) => Task.FromResult(Current);
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
```

The test drives saves through `DisposeAsync` (the leave-the-Reader flush), so it
does not wait on real timers.

- [ ] **Step 2: Run the tests to verify they fail**

Run: commit the test change alone (`git commit -m "test(infra): T12.2 store tests (RED)"`), push, start the CI loop and cancel once `core-tests` finishes.
Expected: `core-tests` FAILS to build Infrastructure.Tests with `CS1503: cannot convert from 'SqliteLibraryRepository' to 'IReadingLocationStore'` at `new(repository, TimeProvider.System)`.

- [ ] **Step 3: Make the repository the store**

In `ILibraryRepository.cs`, add `using DesktopGuides.Core.Reading;`, change the
declaration to `public interface ILibraryRepository : IReadingLocationStore, IAsyncDisposable`,
and delete the `SaveReadingLocationAsync` declaration (lines 26-28); the inherited
one has the same signature.

In `SqliteLibraryRepository.cs`, replace line 461:

```csharp
            RequireUpdated(command.ExecuteNonQuery(), "reading state");
```

with:

```csharp
            if (command.ExecuteNonQuery() != 1) throw new ReadingStateMissingException(guideId);
```

(`DesktopGuides.Core.Library` is already imported.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: commit, push and start the CI loop; cancel once `core-tests` finishes.
Expected: `core-tests` PASSES; Infrastructure.Tests' `Passed!` count is one higher than on `main`.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Library/ILibraryRepository.cs \
        src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs
git commit -m "feat(infra): T12.2 repository is the reading location store

- ILibraryRepository extends IReadingLocationStore
- a missing ReadingStates row throws ReadingStateMissingException

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Restore the saved place on open

**Files:**
- Create: `src/DesktopGuides.Production/ShellWindow.Progress.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs:1728-1731` (TXT open)
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs:40,71-73`
- Modify: `src/DesktopGuides.Production/ShellWindow.PdfReader.cs:66-68`
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs:55,62,65,74,124,455-473`
- Modify: `tools/p1/windows_shell_ui_smoke.ps1:1460-1488,1865-1890`
- Modify: `tools/p1/windows_shell_install.ps1:1166`
- Test: installed `html-position` mode (light and dark) in the `html` group

**Interfaces:**
- Consumes: `ILibraryRepository.GetReadingStateAsync`, `ReaderLocationCodec.Deserialize`,
  `IReaderSession.RestoreLocationAsync`, `TestGate.IsOpen`, `ShowStatus`,
  `ShowWarningStatus`, `ShowTransientStatus`, `renderGeneration`, `dataRoot`.
- Produces (used by Task 4):
  - `private async Task<bool> OpenAtSavedPlaceAsync(Guide guide, IReaderSession session, int generation, string contentSha256, string? htmlEntry, CancellationToken token)`,
    which ends `ShowRestoreStatus(kind); return true;` with locals `stored` (`string?`) and `kind` (`RestoreKind?`) in scope.
  - `private string? RestoreLocatorForTest()`.
  - Constants `ApproximateRestoreMessage`, `UnavailableRestoreMessage`.
  - `HtmlReaderSession(HtmlGuideLoaded loaded, string cacheRoot, HtmlSessionDiagnostics? diagnostics)`.
  - Smoke: `Set-RestoreLocator([string] $json)`, `Clear-RestoreLocator`,
    `Open-RestoredGuide([string] $guide, [string] $restore, [string] $status = 'Guide ready.')`.

- [ ] **Step 1: Point the restore-phase smoke at the shell's override (failing test)**

In `tools/p1/windows_shell_ui_smoke.ps1`, replace `Set-HtmlRestore`, `Clear-HtmlRestore`
and `Open-RestoredGuide` (lines 1460-1488) with:

```powershell
        # The shell's progress override replaces the guide's stored locator.
        function Set-RestoreLocator([string] $json) {
            $folder = Join-Path $AppDataRoot 'test'
            [void](New-Item -ItemType Directory -Force -Path $folder)
            [System.IO.File]::WriteAllText((Join-Path $folder 'restore-locator.json'), $json)
        }

        function Clear-RestoreLocator {
            Remove-Item -LiteralPath (Join-Path $AppDataRoot 'test\restore-locator.json') -Force -ErrorAction SilentlyContinue
        }
```

and, keeping `Clear-HtmlPosition` between them, `Open-RestoredGuide` becomes:

```powershell
        function Open-RestoredGuide([string] $guide, [string] $restore, [string] $status = 'Guide ready.') {
            Clear-HtmlPosition
            Set-RestoreLocator $restore
            try {
                Open-TextGuide $guide
                $report.sessionsOpened++
                [void](Wait-Status $status)
                return Wait-HtmlPosition { param($p) $p.kind } 'a restore outcome'
            }
            finally {
                Clear-RestoreLocator
            }
        }
```

In the `html-position` branch, change the `position-restore-changed` open to pass
the approximate status and add a screenshot after `Wait-TopMark 420 'a restore in changed bytes'`:

```powershell
            $changed = Open-RestoredGuide 'Changed Long Web Guide' $saved `
                'Opened near your last place. The guide changed since you were here.'
```

```powershell
            $report.progressApproximateScreenshot = Save-WindowScreenshot 'progress-approximate'
```

Replace the `position-restore-invalid` phase (from its comment through
`$report.phases += 'position-restore-invalid'`) with:

```powershell
            # position-restore-invalid: the shell can't decode a malformed
            # locator, so it says so and the page stays at its start. The
            # session never restores, so no restore kind is counted.
            Clear-HtmlPosition
            Set-RestoreLocator '{"format":"Html","schemaVersion":1,"payload":'
            try {
                Open-TextGuide 'Long Web Guide'
                $report.sessionsOpened++
                [void](Wait-Status "Couldn't return to your last place, so the guide opened at the start.")
                $invalid = Wait-HtmlPosition { param($p) $p.locator } 'a position after a malformed locator'
            }
            finally {
                Clear-RestoreLocator
            }
            if ([string] $invalid.quote -notlike 'Long Web Guide*') {
                throw "After a malformed locator the position was not the page's start."
            }
            $top = Get-PageTopLine
            if ($top -match '^MARK-') {
                throw "After a malformed locator the page's top line was $top."
            }
            $report.progressUnavailableHtmlScreenshot = Save-WindowScreenshot 'progress-unavailable-html'
            Back-ToTextGame
            $report.phases += 'position-restore-invalid'
```

In `tools/p1/windows_shell_install.ps1` `Invoke-HtmlPositionPass` (line 1166), change
the gate list to `@('HtmlDiagnostics', 'HtmlPosition', 'ProgressOverride')`.

Run the ASCII check: `LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/*.ps1`
Expected: no output.

- [ ] **Step 2: Run the html group to verify it fails**

Run: commit (`git commit -m "test(smoke): T12.2 restore phases use the shell override (RED)"`), push, then
`gh workflow run windows-ci.yml --ref feat/p1-t12-2-progress-coordinator -f shell-scope=html -f dev-fast=true`
and follow the CI loop.
Expected: `production-shell-ui` FAILS in `html-position-light` at `position-restore-exact`:
`Wait-Status` times out or the restore outcome is never written, because nothing reads
`restore-locator.json` yet. The previous phases (`position-fragment`, `position-resize`) pass.

- [ ] **Step 3: Remove the session's test-only restore**

In `src/DesktopGuides.Production/HtmlReaderSession.cs`:
- delete the field `private readonly string? restoreFileForTest;` (line 55);
- change the constructor to `public HtmlReaderSession(HtmlGuideLoaded loaded, string cacheRoot, HtmlSessionDiagnostics? diagnostics)` and delete `ArgumentException.ThrowIfNullOrEmpty(dataRoot);`;
- delete `restoreFileForTest = positionForTest ? Path.Combine(dataRoot, "test", "html-restore.json") : null;`;
- delete `if (RestoreRequestForTest() is LocationDecodeResult request) await RestoreAsync(request);` (line 124);
- delete `RestoreRequestForTest()` and its two comment lines (lines 453-473).

`RestoreLocationAsync` (line 234) stays as T09.3 built it: it waits on the entry load,
takes `restoreTurn`, writes the `html-position-<pid>.json` outcome and raises
`LocationChanged` if the page moved.

- [ ] **Step 4: Write the shell's restore**

Create `src/DesktopGuides.Production/ShellWindow.Progress.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    private const string ApproximateRestoreMessage =
        "Opened near your last place. The guide changed since you were here.";
    private const string UnavailableRestoreMessage =
        "Couldn't return to your last place, so the guide opened at the start.";

    // Once a session is showing, returns it to the guide's saved place and
    // says so when that place is approximate or lost. Returns false when a
    // newer render took over.
    private async Task<bool> OpenAtSavedPlaceAsync(
        Guide guide, IReaderSession session, int generation, string contentSha256,
        string? htmlEntry, CancellationToken token)
    {
        string? stored;
        RestoreKind? kind = null;
        try
        {
            stored = RestoreLocatorForTest() ??
                (await repository!.GetReadingStateAsync(guide.Id, token))?.LocatorJson;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            // An unreadable reading state is a lost place, not a failed open.
            stored = null;
            kind = RestoreKind.Unavailable;
        }
        if (generation != renderGeneration)
        {
            return false;
        }
        if (stored is not null)
        {
            // Stored and override locators are untrusted; only the codec's
            // result reaches the session.
            LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
                stored, guide.Format, contentSha256, htmlEntry);
            kind = RestoreKind.Unavailable;
            if ((decoded.Status is LocationDecodeStatus.Valid or LocationDecodeStatus.ContentChanged) &&
                decoded.Location is ReaderLocation location)
            {
                try
                {
                    kind = (await session.RestoreLocationAsync(location, token)).Kind;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception)
                {
                    kind = RestoreKind.Unavailable;
                }
                if (generation != renderGeneration)
                {
                    return false;
                }
            }
        }
        ShowRestoreStatus(kind);
        return true;
    }

    private void ShowRestoreStatus(RestoreKind? kind)
    {
        if (kind == RestoreKind.Approximate)
        {
            ShowStatus(ApproximateRestoreMessage, InfoBarSeverity.Informational, true, false);
        }
        else if (kind == RestoreKind.Unavailable)
        {
            ShowWarningStatus(UnavailableRestoreMessage);
        }
        else
        {
            ShowTransientStatus("Guide ready.");
        }
    }

    // Test gate only: replaces the stored locator. The file is untrusted and
    // goes through the codec like a stored locator.
    private string? RestoreLocatorForTest()
    {
        if (dataRoot is null ||
            !TestGate.IsOpen($@"Local\DesktopGuides.Preview.ProgressOverride.{Environment.ProcessId}"))
        {
            return null;
        }
        string path = Path.Combine(dataRoot, "test", "restore-locator.json");
        try
        {
            FileInfo file = new(path);
            if (!file.Exists) return null;
            // Longer than any locator: decodes as Invalid.
            if (file.Length > ReaderLocationCodec.MaxBytes) return string.Empty;
            return File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
```

Wire the three formats:

- `ShellWindow.HtmlReader.cs` line 40: `HtmlReaderSession session = new(loaded, cacheRoot!, HtmlReaderSession.DiagnosticsForTest());`.
  Replace lines 71-73 (`ReaderActions.SetSession(session); ShowTransientStatus("Guide ready."); return true;`) with:

```csharp
        ReaderActions.SetSession(session);
        return await OpenAtSavedPlaceAsync(
            guide, session, generation, guide.ContentSha256.ToLowerInvariant(),
            loaded.Policy.Entry.RequestPath, token);
```

- `ShellWindow.PdfReader.cs` lines 66-68 become:

```csharp
        ReaderActions.SetSession(session);
        return await OpenAtSavedPlaceAsync(
            guide, session, generation, guide.ContentSha256.ToLowerInvariant(), null, token);
```

- `ShellWindow.xaml.cs` TXT path: replace `ShowTransientStatus("Guide ready.");` and
  the `break;` after `ReaderActions.SetSession(session);` with:

```csharp
                    if (!await OpenAtSavedPlaceAsync(
                        guide, session, generation, document.ContentSha256.ToLowerInvariant(),
                        null, readerToken))
                    {
                        return false;
                    }
                    break;
```

TXT decodes against the loaded document's hash, so a changed file decodes as
`ContentChanged`; HTML and PDF sessions capture against `guide.ContentSha256`, so
they decode against it.

- [ ] **Step 5: Run the html group and core tests to verify they pass**

Run: commit, push, `gh workflow run windows-ci.yml --ref feat/p1-t12-2-progress-coordinator -f shell-scope=html -f dev-fast=true`, CI loop.
Expected: all jobs PASS. `html-position-light.json` and `-dark.json` list all 7 phases,
`restoreKinds` is `Exact, Exact, Approximate`, and `progress-approximate` and
`progress-unavailable-html` screenshots exist in the `*shell*` artifact.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Production/ShellWindow.Progress.cs \
        src/DesktopGuides.Production/ShellWindow.xaml.cs \
        src/DesktopGuides.Production/ShellWindow.HtmlReader.cs \
        src/DesktopGuides.Production/ShellWindow.PdfReader.cs \
        src/DesktopGuides.Production/HtmlReaderSession.cs
git commit -m "feat(shell): T12.2 restore the saved place when a guide opens

- the shell reads ReadingStates, decodes through the codec and restores
- Approximate and Unavailable restores are reported in the status bar
- the HTML test restore file moves to a format-independent shell override

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: Save points and flush hooks

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.Progress.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs:130` (Activated), InitializeCoreAsync,
  `CloseWhenIdleAsync`, `CloseReaderSessionAsync:1283`
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs` (`OnReaderSessionFailed`)
- Modify: `src/DesktopGuides.Production/ShellWindow.PdfReader.cs:79` (`OnPdfSessionFailed`)
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (new verb before the usage guard at line 575)
- Modify: `tools/p1/windows_shell_install.ps1:891,1228,1327` and `Run-HtmlReaderScenarios`
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (txt-switch, position-unimported-link)
- Test: installed `txt` and `html` groups, then a full run

**Interfaces:**
- Consumes: Task 1 `ProgressCoordinator(IReadingLocationStore, TimeProvider)`,
  `ProgressCoordinator.Track(Guid, IReaderSession, string?) : IProgressTracking`,
  `IProgressTracking.FlushAsync(CancellationToken)`, `IProgressTracking.Abandon()`,
  `ProgressCoordinator.SaveFailed`, `ProgressCoordinator.CountsChanged`,
  `ProgressCoordinator.Counts : ProgressCounts(int Saves, int SkippedUnchanged, int Failures)`.
  Task 2 `ILibraryRepository : IReadingLocationStore`. Task 3 `OpenAtSavedPlaceAsync`
  (locals `stored`, `kind`).
- Produces (used by Task 5):
  - `cacheRoot\diagnostics\progress-<pid>.json` = `{ "saves": n, "skippedUnchanged": n, "failures": n }`
    while the gate `Local\DesktopGuides.Preview.ProgressDiagnostics.<pid>` is open.
  - `SaveFailedMessage = "Couldn't save your place in this guide."`.
  - ShellSeed verb `clear-reading-locations <app-data-root>`.

Every installed group reopens guides, so once saving works every group that
expects a guide to reopen at its start sees its saved place instead. The runner
clears saved locators between passes that reuse one data folder, and the two
smoke phases that reopen a moved guide in the same pass now expect the saved place.

- [ ] **Step 1: Update the smoke phases that reopen a moved guide**

In `tools/p1/windows_shell_ui_smoke.ps1`, txt-switch (about line 2374). The
Numbered guide was last left at `$anchor` (set in txt-resize, confirmed after
txt-remeasure, before the ASCII Map guide opened); it now reopens there. Replace:

```powershell
            # txt-switch: a new guide starts at its first line with normal rows.
            Back-ToTextGame
            Open-TextGuide 'Numbered Lines Guide'
            [void](Wait-Status 'Guide ready.')
            Wait-StatusClosed
            Wait-FirstTextRow
            Wait-TopLine 1 'Reopening the Numbered guide'
```

with:

```powershell
            # txt-switch: a reopened guide returns to its saved line with normal rows.
            Back-ToTextGame
            Open-TextGuide 'Numbered Lines Guide'
            [void](Wait-Status 'Guide ready.')
            Wait-StatusClosed
            Wait-FirstTextRow
            Wait-TopLine $anchor 'Reopening the Numbered guide'
```

and `Wait-TopLine (1 + $pageStep) 'Next page after switching guides'` with
`Wait-TopLine ($anchor + $pageStep) 'Next page after switching guides'`.

In position-unimported-link (about line 1892), the guide's saved place is the
MARK-0420 line from `position-fragment`. Replace:

```powershell
            Open-TextGuide 'Long Web Guide'
            $report.sessionsOpened++
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Long Web Guide')
            Click-Element (Wait-PageVisible 'Jump to MARK-0420')
            $here = Wait-HtmlPosition { param($p) $p.quote -like 'MARK-0420 *' } 'the MARK-0420 line'
            [void](Wait-TopMark 420 'the fragment link')
```

with:

```powershell
            Clear-HtmlPosition
            Open-TextGuide 'Long Web Guide'
            $report.sessionsOpened++
            [void](Wait-Status 'Guide ready.')
            [void](Wait-HtmlPosition { param($p) $p.kind -eq 'Exact' } 'the saved place')
            $report.restoreKinds += 'Exact'
            [void](Wait-PageName 'Long Web Guide')
            $here = Wait-HtmlPosition { param($p) $p.quote -like 'MARK-0420 *' } 'the MARK-0420 line'
            [void](Wait-TopMark 420 'the saved place')
```

and in the final "A new session starts without the bar" block, after
`[void](Wait-Status 'Guide ready.')`, add:

```powershell
            [void](Wait-HtmlPosition { param($p) $p.kind -eq 'Exact' } 'the saved place')
            $report.restoreKinds += 'Exact'
```

`Assert-HtmlPositionPass` (runner line 1182) compares the diagnostics' restore
counts with the smoke's `restoreKinds`, so the expected list becomes `Exact, Exact,
Approximate, Exact, Exact` without a runner change.

- [ ] **Step 2: Run the txt group to verify it fails**

Run: commit the smoke edits (`test(shell): T12.2 expect the saved place on reopen`), push,
`gh workflow run windows-ci.yml --ref feat/p1-t12-2-progress-coordinator -f shell-scope=txt -f dev-fast=true`, CI loop.
Expected: production-shell-ui FAIL at `Reopening the Numbered guide`: the guide
reopens at line 1 because nothing saves yet.

- [ ] **Step 3: Add the ShellSeed verb**

In `tools/p1/DesktopGuides.ShellSeed/Program.cs`, before the usage guard (line 575):

```csharp
if (args.Length == 2 && args[0] == "clear-reading-locations")
{
    // Puts every guide back at its start between passes that share a data folder.
    ExecuteSql(
        new ManagedPathResolver(args[1]),
        "UPDATE ReadingStates SET LocatorJson = NULL, EstimatedFraction = NULL");
    return 0;
}
```

Add `clear-reading-locations <app-data-root>` to the usage message after the guard.

- [ ] **Step 4: Clear saved locators between passes**

In `tools/p1/windows_shell_install.ps1`, add
`Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null`:

- `Run-TxtReaderScenarios` (line 891): before `Set-AppThemePreference $false`, so the
  dark pass starts like the light one.
- `Run-HtmlPositionScenarios` (line 1228): after the `Remove-Item ... 'test'` line
  inside the pass loop.
- `Run-PdfReaderScenarios` (line 1327): before `Invoke-PdfReaderPass $pass.name`.
- `Run-HtmlReaderScenarios` (line 1078): before each `Invoke-HtmlReaderPass` call.


- [ ] **Step 5: Write the save points**

Add to `ShellWindow.Progress.cs` (usings: `System.Text.Json`, `Microsoft.UI.Xaml`):

```csharp
    private const string SaveFailedMessage = "Couldn't save your place in this guide.";

    private ProgressCoordinator? progress;
    private IProgressTracking? progressTracking;
    private readonly object progressCountsFile = new();

    private void StartProgress(IReadingLocationStore store)
    {
        progress = new ProgressCoordinator(store, TimeProvider.System);
        progress.SaveFailed += (_, _) =>
            DispatcherQueue.TryEnqueue(() => ShowWarningStatus(SaveFailedMessage));
        progress.CountsChanged += (_, _) => WriteProgressCountsForTest(progress.Counts);
    }

    // Test gate only: counts, never locator text.
    private void WriteProgressCountsForTest(ProgressCounts counts)
    {
        if (cacheRoot is null ||
            !TestGate.IsOpen($@"Local\DesktopGuides.Preview.ProgressDiagnostics.{Environment.ProcessId}"))
        {
            return;
        }
        string folder = Path.Combine(cacheRoot, "diagnostics");
        string path = Path.Combine(folder, $"progress-{Environment.ProcessId}.json");
        lock (progressCountsFile)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(new
                {
                    saves = counts.Saves,
                    skippedUnchanged = counts.SkippedUnchanged,
                    failures = counts.Failures,
                }));
                File.Move(temp, path, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // Saves an unsaved place while the session is still alive. Never throws.
    private async Task DisposeProgressTrackingAsync()
    {
        IProgressTracking? tracking = progressTracking;
        progressTracking = null;
        if (tracking is not null)
        {
            await tracking.DisposeAsync();
        }
    }

    private void WindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            FlushProgressAsync();
        }
    }

    private async void FlushProgressAsync()
    {
        if (progressTracking is not IProgressTracking tracking)
        {
            return;
        }
        using CancellationTokenSource timeout = new(ProgressCoordinator.FlushTimeout);
        try
        {
            await tracking.FlushAsync(timeout.Token);
        }
        catch (Exception)
        {
            // The coordinator reports failures through SaveFailed.
        }
    }

    // The unmoved place the coordinator compares captures against.
    private static async Task<string?> CaptureBaselineAsync(
        IReaderSession session, CancellationToken token)
    {
        try
        {
            ReaderLocation location = await session.GetLocationAsync(token);
            return ReaderLocationCodec.Serialize(location with { EstimatedFraction = null });
        }
        catch (Exception)
        {
            return null;
        }
    }
```

In `OpenAtSavedPlaceAsync`, replace the final `ShowRestoreStatus(kind); return true;` with:

```csharp
        ShowRestoreStatus(kind);
        // An exact restore is already at the stored place; any other outcome
        // compares against where the reader actually is, so an unmoved guide
        // keeps its stored locator.
        string? baseline = kind == RestoreKind.Exact
            ? stored
            : await CaptureBaselineAsync(session, token);
        if (generation != renderGeneration)
        {
            return false;
        }
        progressTracking = progress!.Track(guide.Id, session, baseline);
        return true;
```

- [ ] **Step 6: Wire the hooks**

`ShellWindow.xaml.cs`:

- After `AppWindow.Closing += WindowClosing;` (line 130): `Activated += WindowActivated;`.
- In `InitializeCoreAsync`, after `await repository.InitializeAsync();`: `StartProgress(repository);`.
- In `CloseWhenIdleAsync`, first line of the innermost `try`, before
  `if (repository is not null)`: `await DisposeProgressTrackingAsync();`.
- In `CloseReaderSessionAsync` (line 1283), first line, before `HideExternalLinkBar();`:
  `await DisposeProgressTrackingAsync();`.

`ShellWindow.HtmlReader.cs` `OnReaderSessionFailed` and `ShellWindow.PdfReader.cs`
`OnPdfSessionFailed`, right after the `ReferenceEquals` guard:

```csharp
        // A failed renderer can't report a place; leave the stored one.
        progressTracking?.Abandon();
        progressTracking = null;
```

- [ ] **Step 7: Build and run core and infra tests**

Run: commit, push, `gh workflow run windows-ci.yml --ref feat/p1-t12-2-progress-coordinator -f shell-scope=txt -f dev-fast=true`, CI loop.
Expected: build, core-tests and infra-tests PASS; production-shell-ui PASS, with
txt-switch reopening the Numbered guide at `$anchor`.

- [ ] **Step 8: Run every group**

Run: `gh workflow run windows-ci.yml --ref feat/p1-t12-2-progress-coordinator -f shell-scope=all`, CI loop.
Expected: all jobs PASS. `html-position-light.json` and `-dark.json` show
`restoreKinds` `Exact, Exact, Approximate, Exact, Exact`; the pdf, html-reader and
library groups pass unchanged. A group that fails because a guide reopened at its
saved place gets a `clear-reading-locations` call before that pass, not a smoke change,
unless the phase itself reopens a moved guide in the same pass (then expect the saved place
as in Step 1, and record it).

- [ ] **Step 9: Commit**

```bash
git add src/DesktopGuides.Production tools/p1/DesktopGuides.ShellSeed/Program.cs \
        tools/p1/windows_shell_install.ps1 tools/p1/windows_shell_ui_smoke.ps1
git commit -m "feat(shell): T12.2 save the reading place

- track the open session after its restore; save on quiet, deadline, Back,
  window deactivation and window close
- a failed renderer abandons its tracking without saving
- the save failure shows in the status bar; counts go to a gated diagnostics file
- the runner clears saved locators between passes that share a data folder

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: The installed `progress` scenario group

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (new verb before the usage guard)
- Modify: `tools/p1/windows_shell_install.ps1` (switch, group table, `Run-ShellSmoke`, new
  `Run-ProgressScenarios`, dispatch)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (parameter, modes, PDF helpers moved up)
- Modify: `.github/workflows/windows-ci.yml:14-26,367-372`
- Test: installed `progress` group

**Interfaces:**
- Consumes: Task 4 diagnostics file and gates, `SaveFailedMessage`; Task 3 smoke
  helpers `Set-RestoreLocator`, `Clear-RestoreLocator` and the constant
  `UnavailableRestoreMessage` text.
- Produces: `-ProgressOnly`, `shell-scope=progress`, `$report.progress`, screenshot
  `progress-unavailable-txt`.

TDD skip: this task adds an end-to-end check of behavior Tasks 1-4 already built and
tested, so there is no failing state to write first. The group's first run is its
verification; a phase that fails there is debugged as a defect, not loosened.

- [ ] **Step 1: Add the seed**

In `tools/p1/DesktopGuides.ShellSeed/Program.cs`, before the usage guard:

```csharp
if (args.Length == 3 && args[0] == "seed-progress")
{
    ManagedPathResolver progressPaths = new(args[1]);
    await using SqliteLibraryRepository progressRepository = new(progressPaths);
    await progressRepository.InitializeAsync();
    if ((await progressRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The progress seed needs an empty library.");
    }
    string fixtures = Path.GetFullPath(args[2]);
    Game progressGame = await progressRepository.AddGameAsync("Progress Game", null, null);
    GuideImportValidator progressValidator = new();
    GuideImportPublisher progressPublisher = new(progressRepository, progressPaths);
    async Task<Guid> PublishAsync(string relative, string title)
    {
        ImportInspection inspection = await progressValidator.InspectAsync(
            Path.Combine(fixtures, relative), CancellationToken.None);
        if (inspection is not ImportReady ready)
        {
            throw new InvalidOperationException($"The {relative} fixture failed the import preview: {inspection}.");
        }
        return await progressPublisher.PublishAsync(
            ready.Manifest, progressGame.Id, title, false, null, CancellationToken.None);
    }
    Guid numbered = Guid.NewGuid();
    await InsertTextGuideAsync(progressPaths, progressGame.Id, numbered, "Numbered Lines Guide",
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        File.ReadAllBytes(Path.Combine(fixtures, "p1", "txt-numbered.txt")));
    Guid web = await PublishAsync(Path.Combine("p1", "html-long", "guide.html"), "Long Web Guide");
    Guid pdf = await PublishAsync(Path.Combine("p0", "generated", "pdf-long.pdf"), "Long PDF Guide");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        numbered = numbered.ToString("N"),
        web = web.ToString("N"),
        pdf = pdf.ToString("N")
    }));
    return 0;
}
```

Add `seed-progress <app-data-root> <fixture-root>` to the usage message.

- [ ] **Step 2: Wire the group into CI and the runner**

`.github/workflows/windows-ci.yml`: add `- progress` after `- pdf` in the
`shell-scope` options, and `'progress' = 'ProgressOnly'` to `$groupSwitches`
after `'pdf' = 'PdfOnly'`.

`tools/p1/windows_shell_install.ps1`:

- Parameter block: `[switch] $ProgressOnly,` after `[switch] $PdfOnly,`.
- `$scenarioGroups`: `'progress' = $ProgressOnly.IsPresent` after `'pdf'`.
- Dispatch, after the pdf line: `if (Enter-ScenarioGroup 'progress') { Run-ProgressScenarios }`.
- `Run-ShellSmoke`: add `[int] $ExpectedTopLine = 0,` before `[string] $AppDataRoot`, pass
  it with `' -ExpectedTopLine ' + $ExpectedTopLine` after `-ExpectedScalePercent`, and
  give `progress-*` modes the 240 s timeout:
  `if ($mode -like 'provider-*' -or $mode -like 'pdf-*' -or $mode -like 'progress-*') { 240 }`.
- After `Run-PdfReaderScenarios`:

```powershell
function Invoke-ProgressPass([string] $mode, [int] $expectedTopLine, [switch] $Kill) {
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $gates = @(
        foreach ($name in @('ProgressDiagnostics', 'ProgressOverride')) {
            [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\DesktopGuides.Preview.$name.$processId")
        })
    try {
        $result = Run-ShellSmoke $mode -ExpectedTopLine $expectedTopLine `
            -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        if ($Kill) {
            # No flush: only a save the timer already made survives.
            Stop-Process -Id $processId -Force
            Wait-InstalledShellExit $processId
        }
        else {
            Close-InstalledShell
        }
        return $result
    }
    finally {
        foreach ($gate in $gates) { $gate.Dispose() }
    }
}

function Run-ProgressScenarios {
    # TR12.1-TR12.2: the quiet and deadline saves survive a killed process,
    # Back and closing save at once, a burst writes a bounded number of times,
    # and two guides keep their own places across restarts.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\pdf-long.pdf'))) {
        throw 'pdf-long.pdf is missing; run tools/p0/make_fixtures.py first.'
    }
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    $ids = Invoke-ShellSeed @('seed-progress', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $dataRoot 'test') -Recurse -Force -ErrorAction SilentlyContinue
    $report.progress = [ordered]@{ ids = $ids }
    try {
        $timer = Invoke-ProgressPass 'progress-timer' 0 -Kill
        $report.progress.timer = $timer
        $restored = Invoke-ProgressPass 'progress-restored' $timer.progressTopLine
        $report.progress.restored = $restored
        $report.progress.twoGuides = Invoke-ProgressPass 'progress-two-guides' $restored.progressTopLine
    }
    finally {
        Remove-Item -LiteralPath (Join-Path $dataRoot 'test') -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    }
}
```

- [ ] **Step 3: Move the PDF helpers to the reader block**

In `tools/p1/windows_shell_ui_smoke.ps1`, move `Get-PdfText`, `Get-PdfStatus`,
`Wait-PdfPage`, `Invoke-NextPages`, `Get-PdfScroll`, `Get-PdfFraction` and
`Wait-PdfFraction` (lines 1944-2030, inside the `pdf-reader` branch) unchanged to
reader-block level, after `Wait-TopMark` (line 1442), out-dented by four spaces.
The `pdf-reader` branch keeps calling them by the same names.

- [ ] **Step 4: Add the smoke modes**

Parameter block: `[int] $ExpectedTopLine = 0,` after `$ExpectedScalePercent`; add
`'progress-timer', 'progress-restored', 'progress-two-guides'` to the `$Mode` ValidateSet.

Reader block (line 1195): add the three modes to the `-in` list and the game name:

```powershell
            elseif ($Mode -like 'progress-*') { 'Progress Game' }
```

After the `pdf-reader` branch, add:

```powershell
        elseif ($Mode -like 'progress-*') {
            $saveFailed = "Couldn't save your place in this guide."

            function Read-ProgressCounts {
                $path = Join-Path $AppCacheRoot "diagnostics\progress-$ProcessId.json"
                $deadline = (Get-Date).AddSeconds(2)
                do {
                    try {
                        if (Test-Path -LiteralPath $path) {
                            return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
                        }
                    }
                    catch {
                        # Read during the atomic replace; try again.
                    }
                    Start-Sleep -Milliseconds 100
                } while ((Get-Date) -lt $deadline)
                return [pscustomobject] @{ saves = 0; skippedUnchanged = 0; failures = 0 }
            }

            function Assert-NoSaveFailure([string] $step) {
                $counts = Read-ProgressCounts
                if ($counts.failures -ne 0) { throw "$step had $($counts.failures) failed saves." }
                # The status probe holds the latest message as 'sequence|message'.
                $probe = Find-RawById 'ShellContent'
                if ($probe -and $probe.Current.ItemStatus -like "*|$saveFailed") {
                    throw "$step showed '$saveFailed'."
                }
            }

            function Open-NumberedAt([int] $line, [string] $step) {
                Open-TextGuide 'Numbered Lines Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                Wait-FirstTextRow
                Wait-TopLine $line $step
            }

            [void](Wait-Name 'LibraryHeading' 'Library')
            Resize-ShellWindow 1500 720
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')

            if ($Mode -eq 'progress-timer') {
                # progress-timer: the quiet timer saves within 5 s with no
                # flush; the runner then kills the process.
                Open-NumberedAt 1 'A first open'
                $top = 1
                for ($i = 0; $i -lt 3; $i++) {
                    Invoke-ReaderCommand 'Next page'
                    $top = Wait-TopLineChange $top 'Next page'
                }
                $report.progressTopLine = $top
                Start-Sleep -Seconds 5
                $counts = Read-ProgressCounts
                if ($counts.saves -lt 1) { throw "No save within 5 s of the last movement." }
                Assert-NoSaveFailure 'progress-timer'
                $report.progressTimerCounts = $counts
                $report.phases += 'progress-timer'
            }
            elseif ($Mode -eq 'progress-restored') {
                Open-NumberedAt $ExpectedTopLine 'Reopening after the process was killed'
                Back-ToTextGame

                # progress-flush: Back saves a PDF place at once.
                Open-TextGuide 'Long PDF Guide'
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PdfPage 1 200 'page 1 of 200')
                Invoke-NextPages 120
                [void](Wait-PdfPage 121 200 'page 121 of 200' 60)
                $scroll = Get-PdfScroll
                $room = 1 - $scroll.Current.VerticalViewSize / 100
                if (-not $scroll.Current.VerticallyScrollable -or $room -lt 0.3) {
                    throw "Page 121 scrolls only $([Math]::Round($room, 3)) of its height; progress-flush needs 0.3."
                }
                $scroll.SetScrollPercent(
                    [System.Windows.Automation.ScrollPattern]::NoScroll, 0.3 / $room * 100)
                Back-ToTextGame
                Open-TextGuide 'Long PDF Guide'
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PdfPage 121 200 'page 121 of 200')
                $report.progressPdfFraction = Wait-PdfFraction 0.3 'Reopening after Back'
                Back-ToTextGame
                $report.phases += 'progress-flush'

                # progress-burst: 30 page turns write at most once per 4 s
                # deadline plus the final quiet save.
                Open-NumberedAt $ExpectedTopLine 'Reopening for the burst'
                Start-Sleep -Seconds 2
                $before = Read-ProgressCounts
                $watch = [System.Diagnostics.Stopwatch]::StartNew()
                foreach ($command in @(@('Next page') * 20 + @('Previous page') * 10)) {
                    Invoke-ReaderCommand $command
                    Start-Sleep -Milliseconds 100
                }
                $elapsed = $watch.Elapsed.TotalSeconds
                Start-Sleep -Seconds 6
                if ($elapsed -ge 8) {
                    throw "The burst took $([Math]::Round($elapsed, 1)) s; the bound assumes under 8 s."
                }
                $after = Read-ProgressCounts
                $saves = $after.saves - $before.saves
                $allowed = [Math]::Floor($elapsed / 4) + 1
                if ($saves -lt 1 -or $saves -gt $allowed) {
                    throw "The burst made $saves saves in $([Math]::Round($elapsed, 1)) s; expected 1 to $allowed."
                }
                Assert-NoSaveFailure 'progress-burst'
                $report.progressBurst = [ordered]@{ seconds = $elapsed; saves = $saves; allowed = $allowed }
                $report.progressTopLine = Get-TopLine
                Back-ToTextGame
                $report.phases += 'progress-burst'

                # The HTML guide moves last and stays open: closing the window saves it.
                Open-TextGuide 'Long Web Guide'
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PageName 'Long Web Guide')
                Click-Element (Wait-PageVisible 'Jump to MARK-0420')
                [void](Wait-TopMark 420 'the fragment link')
                $report.phases += 'progress-restored'
            }
            else {
                # progress-two-guides: after a normal close both guides
                # reopen at their own places.
                Open-NumberedAt $ExpectedTopLine 'Reopening the TXT guide after a restart'
                Back-ToTextGame
                Open-TextGuide 'Long Web Guide'
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PageName 'Long Web Guide')
                [void](Wait-TopMark 420 'Reopening the HTML guide after a restart')
                Back-ToTextGame
                $report.phases += 'progress-two-guides'

                # progress-unavailable: a lost place opens at the start, says
                # so, and leaves the stored place for the next open.
                Set-RestoreLocator '{'
                try {
                    Open-TextGuide 'Numbered Lines Guide'
                    [void](Wait-Status "Couldn't return to your last place, so the guide opened at the start.")
                    Wait-FirstTextRow
                    Wait-TopLine 1 'An unreadable saved place'
                    $report.progressUnavailableTxtScreenshot = Save-WindowScreenshot 'progress-unavailable-txt'
                }
                finally {
                    Clear-RestoreLocator
                }
                Back-ToTextGame
                Open-NumberedAt $ExpectedTopLine 'Reopening after an unreadable place'
                Back-ToTextGame
                Assert-NoSaveFailure 'progress-two-guides'
                $report.phases += 'progress-unavailable'
            }
        }
```

`$report.progressTopLine` must be in the result JSON (the runner reads it); the
result writer serializes every `$report` key (`ConvertTo-Json -Depth 6`).

`Set-RestoreLocator` and `Clear-RestoreLocator` (Task 3) sit at reader-block level, so
these modes can call them.

- [ ] **Step 5: Run the progress group**

Run: commit, push, `gh workflow run windows-ci.yml --ref feat/p1-t12-2-progress-coordinator -f shell-scope=progress -f dev-fast=true`, CI loop.
Expected: all jobs PASS. The runner report's `progress` section shows
`timer.phases = [progress-timer]` with `saves >= 1`, `restored.phases` = `progress-flush`,
`progress-burst`, `progress-restored`, with `progressBurst.saves <= allowed`, and
`twoGuides.phases` = `progress-two-guides`, `progress-unavailable`. The
`progress-unavailable-txt` screenshot is in the `*shell*` artifact.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/windows-ci.yml tools/p1/DesktopGuides.ShellSeed/Program.cs \
        tools/p1/windows_shell_install.ps1 tools/p1/windows_shell_ui_smoke.ps1
git commit -m "test(shell): T12.2 installed progress scenario group

- progress-timer: a quiet save survives a killed process
- progress-flush, progress-burst and progress-restored in a second launch
- progress-two-guides and progress-unavailable after a normal close
- shell-scope=progress in CI

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 6: Docs and evidence

**Files:**
- Create: `docs/p1/evidence/t12-2-progress-coordinator/` (pass reports and screenshots)
- Modify: `docs/p1/t12-2-progress-coordinator-design.md` (status, implementation notes)
- Modify: `docs/p1/implementation-plan.md` (T12.2 status paragraph after T09.3's)
- Modify: `docs/work-breakdown.md` (S12 T12.2)
- Modify: `docs/p1-technical-design.md` (§8 S12 T12.2, and the T09.3 entry's "T12.2 persists" sentences)
- Modify: `docs/p1/e2e-testing.md` (`-ProgressOnly`, checklist row)
- Modify: `docs/p1/t09-3-html-locator-design.md:47-48,250-255`, `docs/p1/t08-3-txt-position-design.md`,
  `docs/p1/t10-3-pdf-locator-design.md`

**Interfaces:**
- Consumes: the green full CI run from Task 4 Step 8 re-run on the Task 5 commit
  (`<run>` below is its database ID; check its `headSha` is the final commit).
- Produces: nothing code reads.

This task is docs only: no test cycle of its own. Its check is that every number
written here is read from the run's artifacts.

- [ ] **Step 1: Run every group on the final commit**

Run: push, `gh workflow run windows-ci.yml --ref feat/p1-t12-2-progress-coordinator -f shell-scope=all`, CI loop.
Expected: all jobs PASS, including the `progress` group.

- [ ] **Step 2: Collect the evidence**

```bash
rm -rf /tmp/t12-2-run && gh run download <run> --pattern '*shell*' -D /tmp/t12-2-run
find /tmp/t12-2-run \( -name 'progress-*' -o -name 'html-position-*.json' -o -name 'html-position-*.progress-*.png' \) | sort
mkdir -p docs/p1/evidence/t12-2-progress-coordinator
```

Copy into `docs/p1/evidence/t12-2-progress-coordinator/`, renaming:

| From the artifact | To |
| --- | --- |
| `progress-timer.json`, `progress-restored.json`, `progress-two-guides.json` | same names |
| `html-position-light.json`, `html-position-dark.json` | same names |
| `html-position-light.progress-approximate.png`, `-dark.` | `approximate-light.png`, `approximate-dark.png` |
| `html-position-light.progress-unavailable-html.png`, `-dark.` | `unavailable-html-light.png`, `unavailable-html-dark.png` |
| `progress-two-guides.progress-unavailable-txt.png` | `unavailable-txt.png` |

Don't copy `html-position-<pid>.json` or `progress-<pid>.json` gate files. Strip the
BOM from the copied JSON (`sed -i '' '1s/^\xEF\xBB\xBF//' <file>`). Open each PNG and
check it shows its status message in the status bar.

- [ ] **Step 3: Update the design doc**

In `docs/p1/t12-2-progress-coordinator-design.md`, replace
`Status: design approved; not yet implemented.` with:

```markdown
Status: implemented; CI run <run> passed the installed `progress` group and
the `html-position` restore phases in light and dark.
```

Replace the paragraph under `## Docs` with `Done in the implementing branch.`
Append after `## Out of scope` a `## Implementation notes` section listing the
14 spec refinements from this plan's Global Constraints, one bullet each in the
same words, followed by the rulings made during execution (from the ledger), and a
`## Verification` section:

```markdown
## Verification

- `ProgressCoordinatorTests` (16 tests, fake clock) cover the quiet and
  deadline saves, the burst count, unchanged and baseline skips, flush and
  dispose, the late capture, failure retry and reporting, the missing row,
  the stuck capture, the null estimate, the tracking context and `Abandon`.
- `SqliteLibraryRepositoryTests` save two guides' locators through the
  coordinator and read them back after reopening the repository.
- CI run <run>:

  | Phase | Result |
  | --- | --- |
  | `progress-timer` | <saves> save(s) within 5 s; the killed app reopened at line <line> |
  | `progress-flush` | Back saved page 121 at fraction <fraction> |
  | `progress-burst` | <saves> saves in <seconds> s (allowed <allowed>) |
  | `progress-two-guides` | TXT line <line> and MARK-0420 after a normal close |
  | `progress-unavailable` | status shown, line 1, stored place kept |
  | `html-position` | restoreKinds `Exact, Exact, Approximate, Exact, Exact` |
```

Fill every `<...>` from the copied JSON.

- [ ] **Step 4: Update the plans and the T08.3, T09.3 and T10.3 designs**

- `docs/p1/implementation-plan.md`: after the T09.3 paragraph (line 836), add a T12.2
  paragraph in the same style: "T12.2 is implemented on
  `feat/p1-t12-2-progress-coordinator` (CI run <run>); see the
  [design and implementation notes](t12-2-progress-coordinator-design.md). A guide
  now reopens where it was left: ..." (one sentence each for the quiet and deadline
  saves, the Back, deactivation and close flushes, the status messages, and the
  locator-only write). In the T10.3 and T09.3 paragraphs, replace "Saving it and
  restoring on reopen remain T12.2." with "T12.2 saves it and restores it on reopen."
- `docs/work-breakdown.md` T12.2 (line 396): append
  "Implemented; see [p1/t12-2-progress-coordinator-design.md](p1/t12-2-progress-coordinator-design.md)."
  in the style of the T09.3 and T10.3 entries (lines 300, 328).
- `docs/p1-technical-design.md` §8 T12.2 (line 781): append a design link in the style
  of the T09.3 entry (line 694); in the T09.3 and T10.3 entries (lines 695, 730), change
  "T12.2 persists the envelope and restores" to the past tense.
- `docs/p1/t09-3-html-locator-design.md`: in the "In-session only" bullet (lines 44-48)
  drop "An HTML guide still reopens at its start until T12.2." and add
  "T12.2 does this through `RestoreLocationAsync`." In the `HtmlPosition` gate bullet
  (lines 250-255) replace the restore-file sentences with
  "The restore file moved to the shell's `ProgressOverride` gate in T12.2."
- `docs/p1/t08-3-txt-position-design.md` (line 37) and
  `docs/p1/t10-3-pdf-locator-design.md` (line 39): after the sentence naming T12.2's
  `ProgressCoordinator`, add "(implemented; see
  [t12-2-progress-coordinator-design.md](t12-2-progress-coordinator-design.md))."
  Leave sentences that describe T08.3's or T10.3's own scope unchanged.

- [ ] **Step 5: Update the E2E guide**

`docs/p1/e2e-testing.md`:

- The switch list (lines 231-232): add `-ProgressOnly` after `-PdfOnly`.
- The gate list: add `ProgressDiagnostics` (writes `progress-<pid>.json` counts to the
  cache's diagnostics folder after each save attempt; no locator text) and
  `ProgressOverride` (replaces the stored locator with `LocalState\test\restore-locator.json`
  when that file exists); remove the `HtmlPosition` gate's restore-file sentences.
- The checklist table: add a row after the PDF rows:

```markdown
| Reading progress | A moved TXT guide is saved within 5 s and reopens at its line after the app is killed. Back saves a PDF page and fraction at once. 30 page turns make at most `floor(seconds / 4) + 1` saves. After a normal close, a TXT and an HTML guide reopen at their own places. An unreadable saved place opens at the start with a warning and keeps the stored place; a changed HTML guide opens near its place with an informational message. | T12.2, TR12.1, TR12.2 |
```

- [ ] **Step 6: Commit and push**

```bash
git add docs
git commit -m "docs(p1): T12.2 implementation notes and evidence

- design status, implementation notes and verification from CI run <run>
- implementation plan, work breakdown and technical design status
- e2e guide: -ProgressOnly, progress gates and a reading-progress row
- T08.3, T09.3 and T10.3 designs no longer say guides reopen at the start

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push
```

## Implementation notes

Rulings made while executing this plan go in the ledger as
`Task <N>: Ruling: <finding> — <decision> — <cost if wrong>`, and Task 6 Step 3
copies them into the design doc's implementation notes. Expected decision points:

- A scenario group outside `txt` and `html` that reopens a moved guide in one pass
  (Task 4 Step 8): clear between passes, or expect the saved place if the phase is
  about reopening.
- A `progress-burst` count over the bound: debug it (`superpowers:systematic-debugging`)
  as a coordinator or adapter defect before touching the bound; the bound comes from
  the spec's "at most once every four seconds".
- An HTML or PDF adapter that raises `LocationChanged` while settling after the
  baseline capture: the coordinator's baseline check absorbs it if the position is
  unchanged; a real change is a save and is correct.

## Verification

Before the PR:

- `core-tests` and `infra-tests` green, including the 16 `ProgressCoordinatorTests`
  and `ProgressCoordinatorSavesEachGuidesLocatorAcrossReopen`.
- A full `shell-scope=all` CI run on the final commit with every group green,
  including `progress`, with the numbers in the design doc's verification table read
  from its artifacts.
- The PR names T12.2, its prerequisites (T03.2, T08.3, T09.3, T10.3, T11.1, all
  merged) and the intended outcome, and shows the approximate and unavailable
  status screenshots in light and dark.
