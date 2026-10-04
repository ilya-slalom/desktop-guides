# T12.2 progress coordinator design

Status: design approved; not yet implemented.
Prerequisites: T03.2 (repository), T11.1 (route coordinator), T08.3 (TXT
locator, PR #31), T09.3 (HTML locator, PR #39, merge commit `dee44e4`) and
T10.3 (PDF locator, PR #37) are merged.

## Intent

T08.3, T09.3 and T10.3 capture and restore a reading position inside an
open guide, but nothing saves it: every guide reopens at its start. T12.2
puts a `ProgressCoordinator` between the reader sessions and `ReadingStates`
so a guide reopens where it was left, after leaving the Reader or
restarting the app, without a database write per scroll event.

Success means:

- A changed position is saved within five seconds of the last movement in a
  normally running app, even if the process is then killed.
- Continuous scrolling writes at most once every four seconds.
- Leaving the Reader, the window losing focus and the window closing each
  save an unsaved position.
- Two guides keep independent positions across a restart.
- A late capture or write never reaches the next guide.
- A restore that isn't exact says so in the status bar.

Traces: the T12.2 row of [implementation-plan.md](implementation-plan.md)
(TR12.1, TR12.2), [work-breakdown.md](../work-breakdown.md) S12 T12.2, and
[p1-technical-design.md](../p1-technical-design.md) §3 (debounce and flush
rules) and §8 S12 T12.2.

Decisions made during brainstorming:

- **Locator only.** T12.2 writes `LocatorJson` with a null
  `EstimatedFraction` and leaves `LastOpenedUtcMs` alone. Library rows keep
  saying `Not started` until T12.3 adds validated per-format estimates and
  the open time, so T12.3's truthful-percentage check stays meaningful.
- **Status InfoBar for an inexact restore.** No new control: the shell's
  existing status bar reports `Approximate` and `Unavailable`.
- **A Core coordinator driven by `LocationChanged`.** The timing rules live
  in a pure Core class tested with a fake clock. A `DispatcherTimer` in
  `ShellWindow` was rejected because only installed runs could test it and
  the file is already about 2,000 lines. A fixed poll was rejected because
  it runs the HTML capture script while idle and ignores the movement
  signal the adapters already raise.

## Core: `DesktopGuides.Core/Reading/`

### `IReadingLocationStore` (new)

```csharp
public interface IReadingLocationStore
{
    Task SaveReadingLocationAsync(
        Guid guideId, string locatorJson, double? estimatedFraction,
        CancellationToken token = default);
}
```

`ILibraryRepository` extends it, so the shell passes the repository and
tests pass a fake. The repository's existing checks stay: a locator over
4 KiB is rejected and a missing `ReadingStates` row fails `RequireUpdated`.

### `ProgressCoordinator` (new)

```csharp
public sealed class ProgressCoordinator
{
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(4);

    public ProgressCoordinator(IReadingLocationStore store, TimeProvider clock);
    public event EventHandler<ProgressSaveFailedEventArgs>? SaveFailed;
    public IProgressTracking Track(Guid guideId, IReaderSession session, string? restoredJson);
}

public interface IProgressTracking : IAsyncDisposable
{
    Task FlushAsync(CancellationToken token);
}
```

- `Track` subscribes to the session's `LocationChanged` and starts a new
  generation. A coordinator tracks one guide at a time; `Track` ends the
  previous tracking's generation, though the shell disposes it first.
  `restoredJson` is the stored locator the shell restored from, so an
  unmoved guide whose capture serializes to the same JSON isn't rewritten.
- **Movement** marks the tracking dirty and restarts a 1 s quiet timer. The
  first movement after a save also starts a 4 s deadline timer, which is
  not restarted by later movement. Whichever fires first saves.
- **A save** calls `GetLocationAsync`, serializes the location with
  `ReaderLocationCodec.Serialize` after replacing `EstimatedFraction` with
  null, and writes only if the JSON differs from the last saved or restored
  JSON. The dirty flag clears when the capture starts; a movement during the
  capture or write sets it again and starts new timers.
- **One at a time.** Saves and flushes for a tracking are serialized by a
  `SemaphoreSlim`. A capture or write that completes after its generation
  ended is discarded; the write for the old guide may already be in flight,
  but nothing is ever written under the next guide's ID because each
  tracking holds its own guide ID and checks its generation before writing.
- **`FlushAsync`** saves now if the tracking is dirty, otherwise returns.
  `DisposeAsync` stops the timers, flushes with a 2 s timeout, unsubscribes
  and ends the generation. Dispose never throws.
- **Restore is not movement.** The shell calls `Track` only after the
  restore finishes. A guide opened and left without moving writes nothing,
  so an `Unavailable` or `Approximate` restore keeps the stored locator
  until the reader moves.
- **Failures.** `OperationCanceledException` from a flush timeout and any
  exception from `GetLocationAsync` or the store are caught. The tracking
  stays dirty, so the next timer or flush retries. The first failure in a
  tracking raises `SaveFailed` once; the shell shows "Couldn't save your
  place in this guide." A failure because the guide's row is gone
  (`InvalidOperationException` from `RequireUpdated` after a removal) clears
  dirty instead of retrying and raises nothing.
- Timers come from `TimeProvider.CreateTimer`, so tests drive time with a
  fake clock. Timer callbacks start a save on the thread pool; the
  coordinator never touches UI objects. Sessions already marshal
  `GetLocationAsync` to their UI thread where needed.

## Production

### Opening a guide (`ShellWindow`)

1. Once a session is showing (TXT after `ReaderActions.SetSession`, HTML
   after `OpenAsync` returns with its entry loaded, PDF after its first page),
   the shell reads `GetReadingStateAsync(guide.Id)`.
2. The locator source is the test override if its gate is open and its file
   exists (see below), else the stored `LocatorJson`.
3. With a locator, the shell decodes it with `ReaderLocationCodec.Deserialize`
   against the guide's format, `ContentSha256` and, for HTML, entry path.
   `Valid` and `ContentChanged` results go to `RestoreLocationAsync`.
   `UnsupportedVersion` and `Invalid` count as `Unavailable` without calling
   the session; the guide stays at its start.
4. After every await the shell checks `renderGeneration` and stops if the
   reader was left.
5. Status by outcome: no locator, `Exact` or `Context`: the transient
   "Guide ready." as today. `Approximate`: informational, closable, not
   auto-dismissed: "Opened near your last place. The guide changed since you
   were here." `Unavailable`: warning: "Couldn't return to your last place,
   so the guide opened at the start." The status InfoBar is already a live
   region, so screen readers announce it.
6. The shell then calls `coordinator.Track(guide.Id, session, json)` with
   the stored JSON when the restore was `Exact`, else null, and keeps the
   handle with the session.

HTML's restore today runs inside `OpenAsync` from the test file, before the
session's in-page tracker starts. After T12.2 every HTML restore goes through
the public `RestoreLocationAsync` on an open session, which T09.3 built for
this (it waits on the entry load and takes `restoreTurn`). The installed
`html-position` restore phases check that this path gives the same outcomes.

### Save points

- **Leaving the Reader.** `CloseReaderSessionAsync` disposes the tracking
  first, which flushes while the session is alive, then disposes the
  session. The 2 s flush timeout keeps a stuck renderer (the `html-crash`
  case) from holding up navigation.
- **Window deactivation.** A `Window.Activated` handler calls
  `FlushAsync` without awaiting when the state is
  `WindowActivationState.Deactivated`. Flushes are serialized, so this can't
  race a timer save.
- **Window closing.** `CloseWhenIdleAsync`, after the navigation queue
  drains and before the repository is disposed, disposes the current
  tracking. Windows App SDK desktop apps don't get a UWP suspend, so window
  activation and closing are the only lifecycle hooks (technical design §3).

### Test override

`HtmlReaderSession`'s test-only restore file moves into the shell as a
format-independent override behind the gate
`Local\DesktopGuides.Preview.ProgressOverride.<pid>`. When the gate is open
and `DataRoot\test\restore-locator.json` exists, its contents (at most
`ReaderLocationCodec.MaxBytes`, treated as untrusted) replace the stored
locator. The existing `html-position` restore phases switch to it and now
also see the shell's status messages.

A second gate, `Local\DesktopGuides.Preview.ProgressDiagnostics.<pid>`, makes
the coordinator write `diagnostics\progress-<pid>.json` under the cache root
after each save attempt: `{ saves, skippedUnchanged, failures }`. It holds no
locator text.

## Logging and untrusted input

Stored and override locators are untrusted and only reach a session through
`ReaderLocationCodec.Deserialize`. The coordinator logs nothing and the
diagnostics file holds counts only, so no guide text or path is written
outside `ReadingStates`.

## Testing

Core, with a fake `TimeProvider` that implements `CreateTimer` and a fake
store and session:

- One movement saves after 1 s, not before.
- Movement every 200 ms for 10 s saves at 4 s and 8 s, then 1 s after the
  last movement: 3 writes, not 50.
- An unchanged capture isn't written; an unmoved guide whose capture equals
  `restoredJson` writes nothing on dispose.
- No movement, no write, on timer or flush.
- `FlushAsync` saves at once when dirty; dispose flushes.
- A capture that completes after `Track` for the next guide is discarded,
  and the store never sees the old locator under the new guide ID.
- A store failure keeps dirty, the next timer retries, `SaveFailed` fires
  once.
- A missing-row failure clears dirty and raises nothing.
- A capture that never completes is cancelled by the dispose timeout and
  dispose returns.
- The written estimate is always null.

Infrastructure: two guides' locators saved through `SqliteLibraryRepository`
survive disposing and reopening the repository, each under its own guide ID.

Installed, a new `progress` scenario group (`-ProgressOnly`, wired into the
workflow's `shell-scope` input like the other groups):

- `progress-timer`: move a TXT guide, wait 6 s, kill the app process (no
  flush), relaunch, reopen: same top line.
- `progress-flush`: move a PDF guide, go Back at once, reopen: same page and
  fraction.
- `progress-burst`: about 30 PageDown presses over 3 s produce at most 2
  saves in the diagnostics file.
- `progress-two-guides`: positions in a TXT and an HTML guide, close the app
  normally, relaunch: both restored independently.
- `progress-messages`: through the override, an `Approximate` and an
  `Unavailable` restore show their status text; light and dark screenshots.

## Docs

Update this file's status and implementation notes,
[implementation-plan.md](implementation-plan.md) (T09.3 merged note, T12.2
status), [work-breakdown.md](../work-breakdown.md) T12.2,
[e2e-testing.md](e2e-testing.md) for the `progress` group, and remove the
"reopens at its start until T12.2" lines in the T08.3, T09.3 and T10.3
designs' notes where they describe current behavior.

## Out of scope

- Estimated percentages, `LastOpenedUtcMs`, content-change handling beyond
  the decoder's `ContentChanged` (T12.3).
- Completion state (T13.x); T12.2 never touches `CompletedUtcMs`.
- Restoring across text-size and theme changes (T14.3).
- Reopening the last active guide at launch (unchanged: it isn't forced
  open).
