# T12.3 progress estimates design

Status: implemented; CI run 37270721376 passed the installed `progress` group,
including the `progress-row` phases in light and dark and `progress-changed`.
Prerequisites: T12.2 (progress coordinator, PR #40, merge commit `17bdf48`)
is merged. T05.1's row presentation and T12.1's codec are in place.

## Intent

T12.2 saves each guide's place but writes a null estimate and never records
an open, so every library row still says `Not started`. T12.3 makes the row
show a truthful reading state:

- an estimated percentage, bounded per format;
- when the guide was last opened;
- an `Approximate` restore, and its status message, when the guide's bytes
  changed and only the percentage could place the reader.

Completion stays an explicit state: nothing in T12.3 reads or writes
`CompletedUtcMs`, and an estimate of 100% never means complete.

Success means:

- After a guide is moved and the app restarts, its row shows `~N%`, with N
  from the saved estimate, and `Opened …`.
- A guide that was never opened shows `Not started`, never `0%` (TR05.2).
- A TXT or PDF guide whose managed copy changed restores by context, else
  by percentage with the `Approximate` message, never as `Exact`.
- An out-of-range or non-finite estimate from a session is clamped or
  dropped and never reaches storage or breaks a save (TR12.3).
- A completed guide stays `Completed` after saves and opens.

Traces: the T12.3 row of [implementation-plan.md](implementation-plan.md)
(TR12.3, TR05.2), [work-breakdown.md](../work-breakdown.md) S12 T12.3, and
[p1-technical-design.md](../p1-technical-design.md) §8 S12 T12.3.

Decisions made during brainstorming:

- **Top-of-view estimates.** Each format keeps the estimate its capture
  already computes: TXT `offset / length` of the top line, HTML the scroll
  fraction, PDF `(pageIndex + pageFraction) / pageCount`. The value is the
  same one TXT's approximate restore uses, so display and restore can't
  drift. A long guide read to the end shows about 97–100%. A display-only
  end-of-view value and a scroll-range value for every format were
  rejected: the first needs a second value per format and writes on resize,
  and the second shows 0% for a guide that fits on screen.
- **PDF hashes its managed copy at open.** TXT already compares the hash of
  the bytes it decoded, and HTML refuses to serve a changed file. PDF
  compared against the stored record, so a changed copy restored `Exact`.
  Real users rarely meet a changed copy, which comes from corruption or an
  outside edit (re-importing new bytes makes a new guide). The hash runs on
  the loader's background thread over a stream it already opens.
- **The coordinator owns every `ReadingStates` write.** It records the open
  time as well as the locator, so the rules are tested in Core with a fake
  clock, and the generation check keeps a late open write off the next
  guide. A shell-side open write was rejected because only installed runs
  could test it. Stamping the open time with the first save was rejected
  because a guide opened and left unmoved would never record an open.

## Core: `DesktopGuides.Core/Reading/`

### `IReadingLocationStore`

```csharp
public interface IReadingLocationStore
{
    Task SaveReadingLocationAsync(
        Guid guideId, string locatorJson, double? estimatedFraction,
        CancellationToken token = default);

    Task RecordGuideOpenedAsync(
        Guid guideId, DateTimeOffset openedUtc, CancellationToken token = default);
}
```

`SqliteLibraryRepository.RecordGuideOpenedAsync` sets only
`LastOpenedUtcMs` (Unix milliseconds) and throws
`ReadingStateMissingException` when the row is gone, like
`SaveReadingLocationAsync`. A negative time is rejected with
`ArgumentOutOfRangeException`.

### `ProgressCoordinator`

- **Estimates.** A save keeps the captured `EstimatedFraction`, passed
  through `ProgressEstimate.Bound`: a finite value is clamped to `[0,1]`,
  and NaN or an infinity becomes null. The bounded value is serialized into
  the locator JSON and passed as the store's `estimatedFraction`, so the
  two never disagree. The unchanged-skip compares the JSON with the estimate
  in it. TXT and PDF estimates follow the position, and HTML's equals the
  payload's scroll fraction, so this adds no writes.
- **The open.** `Track` queues the open write as the tracking's first turn,
  using `clock.GetUtcNow()` at the time of `Track`. A save waits for it.
  The shell calls `Track` only once the guide's content shows, so a load
  failure, or a session that fails before that, records no open. The write
  runs on the tracking's synchronization context like a timer save.
  Dispose and flush wait for it.
- **Open failures.** A missing row is ignored. Any other failure counts in
  `Failures` but doesn't raise `SaveFailed`: the guide did open, and the
  next open records it again. A write that completes after the generation
  ended is harmless, because it is for its own guide ID. An open write still
  waiting for its turn when the generation ends is skipped.
- `ProgressCounts` gains `Opens`, and the diagnostics file gains `opens`.

`ProgressEstimate` is a new static class in `Core/Reading`:

```csharp
public static class ProgressEstimate
{
    public static double? Bound(double? estimate);
}
```

## Production and Infrastructure

### Baseline

`CaptureBaselineAsync` serializes the captured location with its bounded
estimate instead of null, so an unmoved guide still matches its baseline.
After an `Exact` restore, the baseline is still the stored JSON. A row
saved by a T12.2 build has a null estimate in its JSON, so the first
movement rewrites it with an estimate.

### PDF content hash

- `PdfGuideLoaded` gains `string ContentSha256` (64 lowercase hex).
- `ManagedPdfGuideLoader.Open` hashes the opened `FileStream` with
  `IncrementalHash` in 1 MiB chunks, checking the token between chunks,
  after the header check and before `PdfPageTextSource.Open`, then rewinds
  the stream. A read failure while hashing is `Unreadable`. Cancellation
  disposes the stream and rethrows.
- `PdfReaderSession` uses the loaded hash, not `guide.ContentSha256`, for
  `Capture`, `Restore` and `PdfRenderKey`, so pages cached from the old
  bytes aren't reused. `OpenPdfGuideAsync` passes the loaded hash to
  `OpenAtSavedPlaceAsync`.
- A mismatch is not a load error. The guide shows as it is, like TXT, and
  `PdfLocationRules.Restore` already reports a changed hash as
  `Approximate`, keeping the page clamped and the point within it.

### TXT

There is no code change beyond the stored estimate. `TextLocator.Restore`
already compares against the hash of the decoded document and tries
context, then estimate. With estimates stored, a changed guide whose
context isn't found restores `Approximate` instead of T12.2's
`Unavailable`. The `ContentChanged is T12.3's` comment in `ShellWindow`
is replaced by a note that the restore compares hashes. A context match on
changed bytes stays `Context`, as the technical design orders.

### HTML

Unchanged. A changed entry file is denied (`HashMismatch`) and the guide
doesn't open, so there is no restore. A stored locator for other bytes
already decodes `ContentChanged` and restores through the existing plan.

### Display

`CatalogPresentation.ReadingStateFact` already shows, first match winning:
`Completed`, `~N%` from the estimate, `In progress` when opened without an
estimate, and `Not started`. The Game page reloads its rows on every visit,
after the Reader's dispose flush. There is no presentation change. A guide
opened and left without moving shows `In progress` and its open time,
not `~0%`.

Rows saved by T12.2 builds have a locator and no estimate or open time, so
they show `Not started` until the guide is next opened. Only development
builds wrote them, so no migration is added.

## Untrusted input

Session estimates come from guide-driven layout (HTML's page script, PDF
page sizes), so they are bounded in Core before they are serialized or
stored. The repository and the schema's `CHECK` still reject out-of-range
values. Stored locators keep going through `ReaderLocationCodec.Deserialize`,
which already decodes an out-of-range `estimatedFraction` as `Invalid`.

## Testing

Core (`ProgressCoordinatorTests`, fake clock and store):

- A save writes the session's estimate in both the JSON and the column
  (this replaces `WrittenEstimateIsAlwaysNull`).
- Estimates of -0.5, 1.5, NaN and +∞ are written as 0, 1, null and null.
- `Track` records one open at the clock's time before the first save, and
  `NoMovementWritesNothing` becomes "an unmoved guide writes only its open".
- A failed open write counts a failure and raises no `SaveFailed`. A
  missing row raises nothing.
- An open write held behind a slow store, then `Track` for the next guide:
  the store never sees the next guide's ID from the old tracking.
- Dispose during a pending open write waits for it.

Core (`TextLocatorTests`): a changed TXT document, with a context that
isn't found and a stored estimate, restores `Approximate` near the
estimate. A locator from the coordinator round-trips its estimate.

Infrastructure:

- `RecordGuideOpenedAsync` and an estimate saved through the coordinator
  survive reopening the repository and come back from
  `ListGuideSummariesAsync`.
- With `CompletedUtcMs` seeded by SQL, a save and an open leave it
  unchanged, and the row's fact stays `Completed`.
- A missing row throws `ReadingStateMissingException`.
- `ManagedPdfGuideLoader` returns the file's SHA-256, and a different hash
  after bytes are appended past `%%EOF`.

Installed, in the `progress` group:

- `progress-row`: after `progress-two-guides`' normal close and relaunch,
  the Game page rows show `~N%` (N within 1 of the saved estimate) and
  `Opened today at …` for the moved guides. A guide never opened in that
  data root still shows `Not started`. Light and dark screenshots.
- `progress-changed`: ShellSeed appends bytes to a TXT managed copy (lines
  that remove the saved context) and past a PDF's `%%EOF`. Reopening each
  shows the `Approximate` status message, near the saved line or page, and
  the original source files are untouched.

The existing T05.1 seeded-row assertions (`~45%`, `In progress`,
`Not started`) keep covering the presentation rules.

## Docs

In the implementing branch: implementation notes and verification in this
file, the T12.3 paragraph in [implementation-plan.md](implementation-plan.md),
the T12.3 line in [work-breakdown.md](../work-breakdown.md) and §8 S12 of
[p1-technical-design.md](../p1-technical-design.md), and the progress
scenarios in [e2e-testing.md](e2e-testing.md).

## Out of scope

- Completion actions and the completion timestamp service (T13.x).
- Restoring across text-size and theme changes (T14.3).
- Migrating rows saved by T12.2 builds.
- End-of-view or scroll-range estimates.

## Implementation notes

- **The open runs from `Track`.** `Track` starts the open write at once on
  the shell's UI thread, so it holds the tracking's first turn before any
  timer save can post. It never marks the tracking dirty and never raises
  `SaveFailed`.
- **`change-progress-copies` rewrites every Numbered line.** Appending lines
  can't remove the saved context, so the verb replaces `Numbered guide text.`
  with `Edited guide text.` on every line. Each line keeps a uniform length,
  so the stored estimate lands on the saved line exactly.
- **An open moves its guide to the top of the Game page.** Rows sort by
  `MAX(ImportedUtcMs, LastOpenedUtcMs)` (T05.1), and T12.3 is the first
  runtime write of `LastOpenedUtcMs`. Selecting a guide opens it, so the
  selected guide is the top row after Back, and two older smoke
  expectations changed:
  - `stable-navigation` opens Atlas Second, so its rows become Second,
    Third, First. Each removal now selects the row after the removed one
    (`remove-selects-next-guide`, `remove-again-selects-next-guide`).
    Removing the last row can't happen there any more, and
    `ListAnchorTests` keep covering the previous-row fallback.
  - `long-list` reopens the tail guide, which comes back as the first row.
    `virtualized-guide-back-focus` became `opened-guide-back-focus-first-row`
    and checks that order; returning focus to a still-virtualized row is
    no longer reachable from the Reader.
- **Test corrections made while implementing:**
  - The Infrastructure reopen test declared `game` twice; the reopened game
    is now `listed`.
  - The TXT characterization decodes against the lowercase document hash,
    as the shell does (`ShellWindow.xaml.cs`). The codec decodes an
    uppercase expected hash as `Invalid`.
  - `HashReadFailureIsUnreadable` pads the managed copy 64 KiB past `%%EOF`
    before it locks the last byte. In the 1 KiB fixture, the header check's
    buffered first read reached the locked byte, so `Open`'s generic catch
    returned `Damaged`. An I/O failure during the header check still maps to
    `Damaged`, as before T12.3.

## Verification

- `ProgressEstimateTests` cover clamping, NaN and infinities.
- `ProgressCoordinatorTests` cover:
  - the estimate in both the JSON and the column;
  - bounded out-of-range estimates;
  - rewriting a baseline whose estimate is null;
  - one open at the clock's time before the first save;
  - an unmoved guide writing only its open;
  - an open failure (counted, not raised) and a missing row (ignored);
  - a held open never writing the next guide's ID;
  - dispose waiting for a pending open.
- `TextLocatorTests` restore a changed TXT guide without its context at the
  stored estimate, `Approximate`.
- `SqliteLibraryRepositoryTests` read back estimates and open times through
  `ListGuideSummariesAsync` after reopening. They also check that a completed
  guide stays completed after a save and an open, and that a missing row or a
  negative time is rejected.
- `ManagedPdfGuideLoaderTests` check the loaded hash, a changed hash after
  bytes past `%%EOF`, and `Unreadable` when the hash's read fails.
- CI run 37270721376 (reports and screenshots in
  [evidence/t12-3-progress-estimates/](evidence/t12-3-progress-estimates/)):

  | Phase | Result |
  | --- | --- |
  | `progress-row` (light, dark) | Numbered `~37%` (stored 0.365), Web `~16%` (0.165), PDF `~60%` (0.6015), each opened today; Unopened `Not started` with no estimate or open time; no row completed |
  | `progress-changed` | TXT reopened at the saved line, PDF at page 121 fraction 0.300, both with the approximate message; fixture originals unchanged |
