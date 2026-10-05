# T12.2 progress coordinator design

Status: implemented; CI run 37253787166 passed the installed `progress` group and
the `html-position` restore phases in light and dark.
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

Done in the implementing branch.

## Out of scope

- Estimated percentages, `LastOpenedUtcMs`, content-change handling beyond
  the decoder's `ContentChanged` (T12.3).
- Completion state (T13.x); T12.2 never touches `CompletedUtcMs`.
- Restoring across text-size and theme changes (T14.3).
- Reopening the last active guide at launch (unchanged: it isn't forced
  open).

## Implementation notes

Refinements made while planning:

- **Threading.** `Track` captures `SynchronizationContext.Current`; timer
  callbacks `Post` the save to it (inline when it is null) instead of the thread
  pool, so every `GetLocationAsync` starts on the thread that opened the session.
  The turn wait keeps the context; the capture and write awaits use
  `ConfigureAwait(false)` and `WaitAsync(token)`, so a stuck capture can't outlive
  its timeout.
- **Baseline.** After an `Exact` restore the baseline is the stored JSON (as the
  spec says). After any other outcome, or with no locator, the shell captures the
  post-restore position and passes its serialized JSON as `restoredJson`, so an
  adapter that raises `LocationChanged` while settling (HTML after a resize, PDF
  after its first page) doesn't rewrite an unmoved guide. An `Unavailable` or
  `Approximate` restore still keeps the stored locator until the reader moves.
- **`IProgressTracking.Abandon()`** ends a tracking without flushing; the shell
  calls it when a session fails (`html-crash`, PDF failure), where a capture
  can't succeed.
- **`ReadingStateMissingException`** (Core/Library, derives from
  `InvalidOperationException`) replaces `RequireUpdated`'s generic exception for a
  missing `ReadingStates` row, so the coordinator recognises a removed guide
  without matching message text.
- **`CountsChanged` and `Counts`** on the coordinator feed the diagnostics file.
- **Restore-phase smoke.** The `html-position` restore phases use
  `test\restore-locator.json` through the shell's override; `restore-invalid` is
  decoded `Invalid` by the shell, so the session never restores and the phase no
  longer adds to `restoreKinds`. Phases that reopen a guide in the same pass now
  restore its saved place, so `unimported-link` and the final open expect
  `Exact`, and `txt-switch` expects the saved anchor line.
- **`progress-messages`** is folded in: the `Approximate` and HTML `Unavailable`
  screenshots come from the `html-position` phases (light and dark), and the TXT
  `Unavailable` screenshot from `progress-two-guides`.
- **Installed test layout.** Three smoke modes over three launches of one data
  root: `progress-timer` (then kill), `progress-restored` (covers `progress-flush`
  and `progress-burst`, then a normal close), `progress-two-guides`.
- **Burst bound.** 20 Next page and 10 Previous page presses 100 ms apart; the
  allowed saves are `floor(elapsed / 4 s) + 1` (one per deadline that can fall
  inside the burst, plus the final quiet save). The phase fails a burst of 8 s or
  more, so the allowance never exceeds the spec's 2; UI Automation makes the
  presses take longer than the spec's 3 s, which is why the bound uses the
  measured time.
- **Failure retry.** A failed save re-arms only the 4 s deadline (not during
  dispose), so a broken store is retried every 4 s, not on every movement.
- **Restore-read failure.** If `GetReadingStateAsync` throws (other than
  cancellation), the restore counts as `Unavailable`.
- **TXT with a changed file and no estimate.** T12.2 writes a null estimate, so a
  TXT guide whose content changed and whose context isn't found restores
  `Unavailable` (the codec offers `Approximate` only with an estimate) until
  T12.3 writes estimates.
- **Deactivation flush** uses a real 2 s `CancellationTokenSource`; timer and
  dispose saves are bounded through the coordinator's `TimeProvider`.
- **Clearing between passes.** A ShellSeed verb `clear-reading-locations`
  nulls every saved locator; the runner calls it between passes that share a
  data folder, so each pass starts every guide at its start as before.

Rulings made during execution:

- The coordinator tests' fake session awaits its hold with
  `ConfigureAwait(false)`, so a held capture finishes before the test's next
  clock step rather than on xUnit's synchronization context.
- `StartProgress` subscribes through a local coordinator instead of the
  nullable field; the behavior is the same.
- `HtmlReaderSession.GetLocationAsync` writes the gated `html-position` file
  when it moves the current point, without raising `LocationChanged`. The
  shell's baseline capture (refinement 2) otherwise left the session's point
  equal to the tracker's first capture, so the file was never written.
- [e2e-testing.md](e2e-testing.md) has no gate list, so a sentence after the
  `-*Only` switch list describes the two progress gates.

## Verification

- `ProgressCoordinatorTests` (16 tests, fake clock) cover the quiet and
  deadline saves, the burst count, unchanged and baseline skips, flush and
  dispose, the late capture, failure retry and reporting, the missing row,
  the stuck capture, the null estimate, the tracking context and `Abandon`.
- `SqliteLibraryRepositoryTests` save two guides' locators through the
  coordinator and read them back after reopening the repository.
- CI run 37253787166 (reports and screenshots in
  [evidence/t12-2-progress-coordinator/](evidence/t12-2-progress-coordinator/)):

  | Phase | Result |
  | --- | --- |
  | `progress-timer` | 3 saves within 5 s; the killed app reopened at line 70 |
  | `progress-flush` | Back saved page 121 at fraction 0.3 |
  | `progress-burst` | 1 save in 3.6 s (allowed 1) |
  | `progress-two-guides` | TXT line 147 and MARK-0420 after a normal close |
  | `progress-unavailable` | status shown, line 1, stored place kept |
  | `html-position` | restoreKinds `Exact, Exact, Approximate, Exact, Exact` |
