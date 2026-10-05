# T13.2 completion service design

Status: designed; not yet implemented.
Prerequisites: T03.2 (the `ReadingStates` schema and repository) and T12.2
(the progress coordinator, PR #40) are merged. T12.3 (PR #41, merge commit
`a36b058`) stores estimates and open times, so the tests can show that a
100% estimate leaves completion alone.

## Intent

A guide is complete only when the user says so. T13.2 adds the one service
method that marks a guide complete or back in progress. T13.1 will call it
from both the Reader and the guide-detail actions. The write changes only
`CompletedUtcMs`, in one SQLite transaction, and keeps the guide's saved
place, estimate, and last open time.

Success means:

- Marking complete stores the clock's time, and marking in progress clears
  it. Both return the committed value, so the caller updates from what was
  stored.
- Repeating either action changes nothing. A repeated complete keeps the
  first time.
- Toggling twice leaves `LocatorJson`, `EstimatedFraction`, and
  `LastOpenedUtcMs` unchanged.
- The state survives reopening the repository (TR13.2).
- A guide saved at an estimate of 1.0 and opened again is not complete. Its
  row shows `~100%`, not `Completed` (TR13.1).

Traces: the T13.2 row of [implementation-plan.md](implementation-plan.md)
(TR13.1, TR13.2), [work-breakdown.md](../work-breakdown.md) S13 T13.2, and
[p1-technical-design.md](../p1-technical-design.md) §8 S13 T13.2.

Decisions made during brainstorming:

- **Headless verification only.** T13.2 has no UI caller. Core and
  Infrastructure tests run against a real SQLite file. Installed evidence
  comes with T13.1's actions. An installed check driven by a ShellSeed verb
  was rejected, because it would mostly re-test T05.1's row text, and T13.1
  needs its own installed run anyway.
- **A repeated complete keeps the first time.** A stale button or a second
  window can't rewrite when the guide was finished, and the repeat is
  idempotent as the technical design asks. Overwriting with the current
  time was rejected.
- **A Core service over a narrow store.** `GuideCompletionService` reads the
  clock and calls an `IGuideCompletionStore`, which `ILibraryRepository`
  extends, as it extends `IReadingLocationStore`. Two alternatives were
  rejected:
  - A repository-only method, where each T13.1 call site reads the clock
    itself. Its rules could only be tested through SQLite.
  - Folding completion into `ProgressCoordinator`. The coordinator exists
    only while the Reader has a guide open, the guide-detail action has no
    guide open, and it would couple completion to locator saves.

## Core: `DesktopGuides.Core/Reading/`

### `IGuideCompletionStore`

```csharp
// The completion write the library provides.
public interface IGuideCompletionStore
{
    // Null clears the completion time. A time is kept only when the guide
    // isn't already complete. Returns the committed completion time.
    Task<DateTimeOffset?> SetGuideCompletionAsync(
        Guid guideId, DateTimeOffset? completedUtc, CancellationToken token = default);
}
```

`ILibraryRepository` becomes
`ILibraryRepository : IReadingLocationStore, IGuideCompletionStore, IAsyncDisposable`.
The Core test fake in `Providers/ImporterFakes.cs` gains a stub that throws
`NotSupportedException`, like its other unused members.

### `GuideCompletionService`

```csharp
public sealed class GuideCompletionService(IGuideCompletionStore store, TimeProvider clock)
{
    public Task<DateTimeOffset?> MarkCompleteAsync(Guid guideId, CancellationToken token = default);
    public Task<DateTimeOffset?> MarkInProgressAsync(Guid guideId, CancellationToken token = default);
}
```

- The constructor rejects a null store or clock.
- `MarkCompleteAsync` passes `clock.GetUtcNow()`, and `MarkInProgressAsync`
  passes null.
- Both return the store's committed value.
- Store exceptions reach the caller unchanged: `ReadingStateMissingException`
  for a removed guide, and SQLite or I/O errors. T13.1 decides how to show
  them ("a disabled action explains a storage error instead of appearing to
  succeed").

## Infrastructure: `SqliteLibraryRepository.SetGuideCompletionAsync`

- A time earlier than the Unix epoch is rejected with
  `ArgumentOutOfRangeException` before any write, matching
  `RecordGuideOpenedAsync` and the schema's `CHECK`.
- The work runs inside `WriteAsync`, so it is serialized with every other
  repository write, including the coordinator's saves and open writes. It
  uses one connection and one `BeginTransaction()`:

  ```sql
  UPDATE ReadingStates
  SET CompletedUtcMs = CASE WHEN $completed IS NULL THEN NULL
                            ELSE COALESCE(CompletedUtcMs, $completed) END
  WHERE GuideId = $id;
  ```

  - A row count other than 1 throws `ReadingStateMissingException(guideId)`,
    and the transaction rolls back.
  - `SELECT CompletedUtcMs FROM ReadingStates WHERE GuideId = $id` then reads
    the committed value in the same transaction, the transaction commits,
    and the value is returned as a UTC `DateTimeOffset` from Unix
    milliseconds.
- No other column is written. The progress save and the open write never
  touch `CompletedUtcMs`, so concurrent writers can't lose each other's
  columns.
- Completion doesn't change `LastOpenedUtcMs`, so the Game page and Library
  ordering are unchanged.

## Display

`CatalogPresentation.ReadingStateFact` already puts `Completed` first and
shows `~N%` from the estimate otherwise. There is no presentation change.
After marking in progress, the row shows the saved estimate again.

## Untrusted input

The only input is a guide ID and the clock's time. Guide content can't
reach this write, so no estimate, locator, or reading position can set or
clear completion.

## Testing

Core (`GuideCompletionServiceTests`, a fake clock and store):

- Marking complete passes the clock's time.
- Marking in progress passes null.
- Each returns the store's committed value, not the time it passed.
- A store exception reaches the caller.
- A null store or clock is rejected.

Infrastructure (`SqliteLibraryRepositoryTests`, a real database file):

- **Toggle twice.** Seed a locator, an estimate, and an open time, then mark
  complete, in progress, complete, and in progress. Each call returns the
  expected value, and the locator, estimate, and open time are unchanged
  after every step.
- **Repeat.** A second complete at a later time returns and keeps the first
  time. A second in-progress stays null.
- **Restart.**
  - Mark complete, dispose, and reopen the repository.
    `ListGuideSummariesAsync` shows the completion time, and the row's fact
    is `Completed`.
  - Mark in progress, then reopen. The completion time is null, the locator
    is intact, and the fact is `~N%`.
- **100% isn't complete.** Save a locator with estimate 1.0, record an open,
  and reopen. `CompletedUtc` is null and the fact is `~100%`.
- **Errors.**
  - A guide without a row throws `ReadingStateMissingException`.
  - A time before the epoch throws `ArgumentOutOfRangeException` and
    writes nothing.

## Docs

In the implementing branch:

- the implementation notes and verification in this file;
- the T13.2 lines in [implementation-plan.md](implementation-plan.md),
  [work-breakdown.md](../work-breakdown.md), and §8 S13 of
  [p1-technical-design.md](../p1-technical-design.md).

[e2e-testing.md](e2e-testing.md) doesn't change, because T13.2 has no
installed scenario.

## Out of scope

- The completion actions, their announcements, and their storage-error
  display (T13.1).
- Installed or UI Automation evidence (T13.1).
- Any change to row ordering or presentation.
