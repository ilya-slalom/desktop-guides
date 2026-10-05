# T13.2 Completion Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One service method marks a guide complete or back in progress. It
changes only `CompletedUtcMs`, in one SQLite transaction, and returns the
committed value.

**Architecture:**

- `GuideCompletionService` in Core reads a `TimeProvider` and calls a new
  `IGuideCompletionStore`.
- `ILibraryRepository` extends that store, as it already extends
  `IReadingLocationStore`.
- `SqliteLibraryRepository.SetGuideCompletionAsync` does one guarded
  `UPDATE` and a read-back inside one transaction under the existing
  `WriteAsync` gate.
- There is no UI and no installed scenario. T13.1 adds the actions.

**Tech Stack:** .NET 10, SQLite (Microsoft.Data.Sqlite), xUnit.

**Spec:** [t13-2-completion-service-design.md](t13-2-completion-service-design.md)

## Global Constraints

**Branch:** `feat/p1-t13-2-completion-service`, already checked out. The
spec is commit `32f1f6c`.

**Tooling:**

- There is no local `dotnet` or `pwsh`. Every build and test runs in CI.
- The CI loop for branch `<b>`:
  1. Push, then run
     `gh workflow run windows-ci.yml --ref <b> -f shell-scope=core -f dev-fast=true`.
  2. Run
     `gh run list --workflow windows-ci.yml --branch <b> --limit 1 --json databaseId,headSha -q '.[0]'`
     and check that `headSha` matches `git rev-parse HEAD`.
  3. Run `gh run watch <id> --exit-status --interval 60`.
  4. On failure, run `gh run view <id> --log-failed`.
- `core-tests` takes about 3 minutes and runs both Core.Tests and
  Infrastructure.Tests. A core-only step may `gh run cancel` the run once
  `core-tests` has finished.
- Pass counts:
  `gh api repos/ilya-slalom/desktop-guides/actions/jobs/<job-id>/logs | grep "Passed!"`.
  The baseline on `main` (`a36b058`) is Core.Tests 723 and
  Infrastructure.Tests 520.
- A build failure in `core-tests` counts as a valid RED only when the errors
  name the missing T13.2 members and nothing else.
- `dev-fast=true` runs are for iteration and are not PR evidence.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

**Values (verbatim from the spec):**

- `Task<DateTimeOffset?> SetGuideCompletionAsync(Guid guideId, DateTimeOffset? completedUtc, CancellationToken token = default)`.
  Null clears the completion time. A time is kept only when the guide isn't
  already complete. The method returns the committed completion time.
- `GuideCompletionService(IGuideCompletionStore store, TimeProvider clock)`
  has `MarkCompleteAsync(Guid guideId, CancellationToken token = default)`,
  which passes `clock.GetUtcNow()`, and
  `MarkInProgressAsync(Guid guideId, CancellationToken token = default)`,
  which passes null. Both return `Task<DateTimeOffset?>`.
- The SQL:

  ```sql
  SET CompletedUtcMs = CASE WHEN $completed IS NULL THEN NULL
                            ELSE COALESCE(CompletedUtcMs, $completed) END
  ```

- A row count other than 1 throws `ReadingStateMissingException(guideId)`.
- A time before the Unix epoch throws
  `ArgumentOutOfRangeException(nameof(completedUtc))` before any write.
- No column other than `CompletedUtcMs` is written. `LastOpenedUtcMs` and
  the row ordering don't change.
- Store exceptions reach the service's caller unchanged.
- Row facts are unchanged from T05.1: `Completed`, `~N%`.

## Review Focus

1. **A progress save, an open write and a completion arrive together** (the
   Reader flushing while the user clicks Complete). All three columns should
   end up set and none lost. Task 2 pins this with
   `ConcurrentWritesKeepEveryColumn`.
2. **The clock returns a local offset and sub-millisecond ticks** (for
   example `TimeProvider.System` on a +09:00 machine). The stored and
   returned value should be the same instant, truncated to milliseconds,
   with offset zero, so T13.1 shows exactly what was committed. Task 2 pins
   this with `CompletionTimeIsCommittedInUtcMilliseconds`.
3. **A canceled token reaches the write**, for example when the window
   closes mid-click. Expected: `OperationCanceledException` and nothing
   written. Task 2 pins this with `CanceledCompletionWritesNothing`.
4. **The guide was removed after its action was shown.** Expected:
   `ReadingStateMissingException` with that guide's ID, both for complete
   and for in progress. Task 2 pins this with
   `SetGuideCompletionRejectsAMissingRowAndANegativeTime`.
5. **Marking in progress a guide that was never complete.** Expected: it
   returns null, writes nothing visible, and raises no error. Task 2 pins
   this with `RepeatingAnActionKeepsTheCommittedState`, whose first call
   does exactly that.

---

## File map

| File | Change | Responsibility |
| --- | --- | --- |
| `src/DesktopGuides.Core/Reading/IGuideCompletionStore.cs` | Create | The completion write contract |
| `src/DesktopGuides.Core/Reading/GuideCompletionService.cs` | Create | Clock-stamped complete / in-progress |
| `tests/DesktopGuides.Core.Tests/GuideCompletionServiceTests.cs` | Create | Service rules with a fake clock and store |
| `src/DesktopGuides.Core/Library/ILibraryRepository.cs` | Modify line 6 | Extend `IGuideCompletionStore` |
| `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` | Modify after `RecordGuideOpenedAsync` (~line 481) | The transactional write |
| `tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs` | Modify after line 62 | Stub for the new member |
| `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs` | Modify after `RecordGuideOpenedRejectsAMissingRowAndANegativeTime` (~line 167) | SQLite toggle, repeat, restart, 100%, errors |
| `docs/p1/t13-2-completion-service-design.md`, `docs/p1/implementation-plan.md`, `docs/work-breakdown.md`, `docs/p1-technical-design.md` | Modify | Status and traceability |

## Task 1: Core completion service

**Files:**
- Create: `src/DesktopGuides.Core/Reading/IGuideCompletionStore.cs`
- Create: `src/DesktopGuides.Core/Reading/GuideCompletionService.cs`
- Test: `tests/DesktopGuides.Core.Tests/GuideCompletionServiceTests.cs`

**Interfaces:**
- Consumes: `ReadingStateMissingException(Guid)` from `DesktopGuides.Core.Library`.
- Produces:
  - `IGuideCompletionStore.SetGuideCompletionAsync(Guid, DateTimeOffset?, CancellationToken)`,
    which returns `Task<DateTimeOffset?>`;
  - `GuideCompletionService.MarkCompleteAsync(Guid, CancellationToken)` and
    `MarkInProgressAsync(Guid, CancellationToken)`, which return
    `Task<DateTimeOffset?>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/GuideCompletionServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Commit the tests and run them (RED)**

```bash
git add tests/DesktopGuides.Core.Tests/GuideCompletionServiceTests.cs
git commit -m "test(core): T13.2 completion service rules

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Run: push, then the CI loop with `shell-scope=core -f dev-fast=true`. Cancel
once `core-tests` has finished.
Expected: `core-tests` FAILS to build, and the only errors are `CS0246`
naming `IGuideCompletionStore` and `GuideCompletionService`.

- [ ] **Step 3: Write the implementation**

Create `src/DesktopGuides.Core/Reading/IGuideCompletionStore.cs`:

```csharp
namespace DesktopGuides.Core.Reading;

// The completion write the library provides.
public interface IGuideCompletionStore
{
    // Null clears the completion time. A time is kept only when the guide
    // isn't already complete. Returns the committed completion time.
    Task<DateTimeOffset?> SetGuideCompletionAsync(
        Guid guideId, DateTimeOffset? completedUtc, CancellationToken token = default);
}
```

Create `src/DesktopGuides.Core/Reading/GuideCompletionService.cs`:

```csharp
namespace DesktopGuides.Core.Reading;

// Marks a guide complete or back in progress by explicit action only.
// Completion never follows from the estimate or the reading position.
public sealed class GuideCompletionService
{
    private readonly IGuideCompletionStore store;
    private readonly TimeProvider clock;

    public GuideCompletionService(IGuideCompletionStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        this.store = store;
        this.clock = clock;
    }

    public Task<DateTimeOffset?> MarkCompleteAsync(Guid guideId, CancellationToken token = default) =>
        store.SetGuideCompletionAsync(guideId, clock.GetUtcNow(), token);

    public Task<DateTimeOffset?> MarkInProgressAsync(Guid guideId, CancellationToken token = default) =>
        store.SetGuideCompletionAsync(guideId, null, token);
}
```

- [ ] **Step 4: Commit and run (GREEN)**

```bash
git add src/DesktopGuides.Core/Reading/IGuideCompletionStore.cs src/DesktopGuides.Core/Reading/GuideCompletionService.cs
git commit -m "feat(core): T13.2 completion service over a narrow store

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Run: push, then the CI loop with `shell-scope=core -f dev-fast=true`.
Expected: `core-tests` PASSES, with Core.Tests at 728 (723 + 5) and
Infrastructure.Tests at 520.

- [ ] **Step 5: Ledger**

Run `task-done` with the GREEN run's `core-tests` result.

## Task 2: SQLite transactional completion write

**Files:**
- Modify: `src/DesktopGuides.Core/Library/ILibraryRepository.cs:6`
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (add the method after `RecordGuideOpenedAsync`, ~line 481)
- Modify: `tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs`
  (add after the `RecordGuideOpenedAsync` stub, line 62)
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`
  (add after `RecordGuideOpenedRejectsAMissingRowAndANegativeTime`, ~line 167)

**Interfaces:**
- Consumes: `IGuideCompletionStore` from Task 1.
- Consumes these existing test helpers in `SqliteLibraryRepositoryTests`:
  `TestLibrary`, `InsertGuide(databasePath, guideId, gameId)` (which inserts
  the `ReadingStates` row), `FixedTimeProvider`, and `Now`
  (`2026-09-25T00:00Z`).
- Produces: `ILibraryRepository : IReadingLocationStore, IGuideCompletionStore, IAsyncDisposable`
  and `SqliteLibraryRepository.SetGuideCompletionAsync`. T13.1 constructs
  `GuideCompletionService(repository, TimeProvider.System)`.

- [ ] **Step 1: Write the failing tests**

Add this to `SqliteLibraryRepositoryTests`, after
`RecordGuideOpenedRejectsAMissingRowAndANegativeTime`:

```csharp
    private const string PlaceJson = """{"place":1}""";

    [Fact]
    public async Task CompletionTogglesTwiceAndKeepsTheReadingPlace()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        await using SqliteLibraryRepository repository =
            new(directory.Paths, new FixedTimeProvider(Now));
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);
        await repository.SaveReadingLocationAsync(guide, PlaceJson, 0.4);
        await repository.RecordGuideOpenedAsync(guide, Now.AddHours(1));

        async Task AssertState(DateTimeOffset? completed)
        {
            ReadingState? state = await repository.GetReadingStateAsync(guide);
            Assert.Equal(PlaceJson, state?.LocatorJson);
            Assert.Equal(0.4, state?.EstimatedFraction);
            Assert.Equal(Now.AddHours(1), state?.LastOpenedUtc);
            Assert.Equal(completed, state?.CompletedUtc);
        }

        Assert.Equal(Now.AddHours(2), await repository.SetGuideCompletionAsync(guide, Now.AddHours(2)));
        await AssertState(Now.AddHours(2));
        Assert.Null(await repository.SetGuideCompletionAsync(guide, null));
        await AssertState(null);
        Assert.Equal(Now.AddHours(3), await repository.SetGuideCompletionAsync(guide, Now.AddHours(3)));
        await AssertState(Now.AddHours(3));
        Assert.Null(await repository.SetGuideCompletionAsync(guide, null));
        await AssertState(null);
    }

    [Fact]
    public async Task RepeatingAnActionKeepsTheCommittedState()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);

        Assert.Null(await repository.SetGuideCompletionAsync(guide, null));
        Assert.Equal(Now, await repository.SetGuideCompletionAsync(guide, Now));
        Assert.Equal(Now, await repository.SetGuideCompletionAsync(guide, Now.AddHours(5)));
        Assert.Equal(Now, (await repository.GetReadingStateAsync(guide))?.CompletedUtc);
        Assert.Null(await repository.SetGuideCompletionAsync(guide, null));
        Assert.Null(await repository.SetGuideCompletionAsync(guide, null));
        Assert.Null((await repository.GetReadingStateAsync(guide))?.CompletedUtc);
    }

    [Fact]
    public async Task CompletionSurvivesReopen()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        Guid gameId;
        FixedTimeProvider later = new(Now.AddHours(3));
        await using (SqliteLibraryRepository repository =
            new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await repository.InitializeAsync();
            Game game = await repository.AddGameAsync("Completion", null, null);
            gameId = game.Id;
            InsertGuide(directory.Paths.DatabasePath, guide, gameId);
            await repository.SaveReadingLocationAsync(guide, PlaceJson, 0.4);
            await repository.SetGuideCompletionAsync(guide, Now.AddHours(2));
        }

        await using (SqliteLibraryRepository reopened = new(directory.Paths))
        {
            await reopened.InitializeAsync();
            GuideSummary summary = Assert.Single(await reopened.ListGuideSummariesAsync(gameId));
            Assert.Equal(Now.AddHours(2), summary.State?.CompletedUtc);
            Assert.Equal("Completed", CatalogPresentation.GuideFacts(
                summary, later, CultureInfo.InvariantCulture)[1].Label);
            Assert.Null(await reopened.SetGuideCompletionAsync(guide, null));
        }

        await using SqliteLibraryRepository again = new(directory.Paths);
        await again.InitializeAsync();
        GuideSummary returned = Assert.Single(await again.ListGuideSummariesAsync(gameId));
        Assert.Null(returned.State?.CompletedUtc);
        Assert.Equal(PlaceJson, returned.State?.LocatorJson);
        Assert.Equal("~40%", CatalogPresentation.GuideFacts(
            returned, later, CultureInfo.InvariantCulture)[1].Label);
    }

    [Fact]
    public async Task AFullEstimateIsNotCompletion()
    {
        // TR13.1: reading to 100% never creates a completion time.
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        Guid gameId;
        await using (SqliteLibraryRepository repository =
            new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await repository.InitializeAsync();
            Game game = await repository.AddGameAsync("Completion", null, null);
            gameId = game.Id;
            InsertGuide(directory.Paths.DatabasePath, guide, gameId);
            await repository.SaveReadingLocationAsync(guide, PlaceJson, 1.0);
            await repository.RecordGuideOpenedAsync(guide, Now.AddHours(1));
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        GuideSummary summary = Assert.Single(await reopened.ListGuideSummariesAsync(gameId));
        Assert.Equal(1.0, summary.State?.EstimatedFraction);
        Assert.Null(summary.State?.CompletedUtc);
        Assert.Equal("~100%", CatalogPresentation.GuideFacts(
            summary, new FixedTimeProvider(Now.AddHours(2)), CultureInfo.InvariantCulture)[1].Label);
    }

    [Fact]
    public async Task SetGuideCompletionRejectsAMissingRowAndANegativeTime()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        Guid unknown = Guid.NewGuid();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);

        ReadingStateMissingException missing = await Assert.ThrowsAsync<ReadingStateMissingException>(() =>
            repository.SetGuideCompletionAsync(unknown, Now));
        Assert.Equal(unknown, missing.GuideId);
        missing = await Assert.ThrowsAsync<ReadingStateMissingException>(() =>
            repository.SetGuideCompletionAsync(unknown, null));
        Assert.Equal(unknown, missing.GuideId);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.SetGuideCompletionAsync(guide, DateTimeOffset.UnixEpoch.AddMilliseconds(-1)));
        Assert.Null((await repository.GetReadingStateAsync(guide))?.CompletedUtc);
    }

    [Fact]
    public async Task ConcurrentWritesKeepEveryColumn()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);

        await Task.WhenAll(
            repository.SaveReadingLocationAsync(guide, PlaceJson, 0.4),
            repository.SetGuideCompletionAsync(guide, Now.AddHours(2)),
            repository.RecordGuideOpenedAsync(guide, Now.AddHours(1)));

        ReadingState? state = await repository.GetReadingStateAsync(guide);
        Assert.Equal(PlaceJson, state?.LocatorJson);
        Assert.Equal(0.4, state?.EstimatedFraction);
        Assert.Equal(Now.AddHours(1), state?.LastOpenedUtc);
        Assert.Equal(Now.AddHours(2), state?.CompletedUtc);
    }

    [Fact]
    public async Task CompletionTimeIsCommittedInUtcMilliseconds()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);
        DateTimeOffset local = new(2026, 10, 5, 17, 0, 0, TimeSpan.FromHours(9));

        DateTimeOffset? committed = await repository.SetGuideCompletionAsync(guide, local.AddTicks(5));

        Assert.Equal(local, committed);
        Assert.Equal(TimeSpan.Zero, committed?.Offset);
        Assert.Equal(committed, (await repository.GetReadingStateAsync(guide))?.CompletedUtc);
    }

    [Fact]
    public async Task CanceledCompletionWritesNothing()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.SetGuideCompletionAsync(guide, Now, canceled.Token));

        Assert.Null((await repository.GetReadingStateAsync(guide))?.CompletedUtc);
    }
```

- [ ] **Step 2: Commit the tests and run them (RED)**

```bash
git add tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs
git commit -m "test(storage): T13.2 completion toggle, repeat, restart and 100% estimate

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Run: push, then the CI loop with `shell-scope=core -f dev-fast=true`. Cancel
once `core-tests` has finished.
Expected: `core-tests` FAILS to build, and the only errors are `CS1061`:
`SqliteLibraryRepository` doesn't contain a definition for
`SetGuideCompletionAsync`.

- [ ] **Step 3: Write the implementation**

In `src/DesktopGuides.Core/Library/ILibraryRepository.cs`, change line 6 to:

```csharp
public interface ILibraryRepository : IReadingLocationStore, IGuideCompletionStore, IAsyncDisposable
```

In `tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs`, add this
after the `RecordGuideOpenedAsync` stub:

```csharp
    public Task<DateTimeOffset?> SetGuideCompletionAsync(Guid guideId, DateTimeOffset? completedUtc, CancellationToken token = default) => throw new NotSupportedException();
```

In `SqliteLibraryRepository.cs`, add this after `RecordGuideOpenedAsync`:

```csharp
    // Changes only CompletedUtcMs. A repeated completion keeps the first time.
    public Task<DateTimeOffset?> SetGuideCompletionAsync(
        Guid guideId, DateTimeOffset? completedUtc, CancellationToken token = default)
    {
        long? completedMs = completedUtc?.ToUnixTimeMilliseconds();
        if (completedMs < 0) throw new ArgumentOutOfRangeException(nameof(completedUtc));
        return WriteAsync<DateTimeOffset?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE ReadingStates
                SET CompletedUtcMs = CASE WHEN $completed IS NULL THEN NULL
                                          ELSE COALESCE(CompletedUtcMs, $completed) END
                WHERE GuideId = $id
                """;
            update.Parameters.AddWithValue("$id", guideId.ToString("N"));
            update.Parameters.AddWithValue("$completed", (object?)completedMs ?? DBNull.Value);
            if (update.ExecuteNonQuery() != 1) throw new ReadingStateMissingException(guideId);
            using SqliteCommand read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT CompletedUtcMs FROM ReadingStates WHERE GuideId = $id";
            read.Parameters.AddWithValue("$id", guideId.ToString("N"));
            object? stored = read.ExecuteScalar();
            transaction.Commit();
            return stored is long ms ? FromUnixMilliseconds(ms) : null;
        }, token);
    }
```

If the missing-row branch throws, disposing the uncommitted transaction rolls
it back.

- [ ] **Step 4: Commit and run (GREEN)**

```bash
git add src/DesktopGuides.Core/Library/ILibraryRepository.cs src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs
git commit -m "feat(storage): T13.2 transactional completion write

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Run: push, then the CI loop with `shell-scope=core -f dev-fast=true`.
Expected: `core-tests` PASSES, with Core.Tests at 728 and
Infrastructure.Tests at 528 (520 + 8). `dev-production-packages` also
passes, which shows that Production still compiles against the extended
interface.

- [ ] **Step 5: Ledger**

Run `task-done` with the GREEN run's `core-tests` result.

## Task 3: Docs and full verification

**Files:**
- Modify: `docs/p1/t13-2-completion-service-design.md` (the status line; add
  Implementation notes and Verification)
- Modify: `docs/p1/implementation-plan.md` (add a T13.2 paragraph after the
  M4 table, before `## M5`)
- Modify: `docs/work-breakdown.md:424` (the T13.2 line)
- Modify: `docs/p1-technical-design.md:807` (§8 S13 T13.2)

**Interfaces:**
- Consumes: the CI run IDs and pass counts from Tasks 1 and 2, and the
  ledger's `Ruling:` lines.
- Produces: docs that match what shipped.

- [ ] **Step 1: Update the docs**

1. **Design:**
   - Set the status line to
     `Status: implemented; CI run <id> passed core-tests and the full matrix.`
   - Add `## Implementation notes`, with one bullet per ledgered ruling, or
     `None beyond the design.` if there are none.
   - Add `## Verification`. List the tests by file, name the run ID and give
     the pass counts.
2. **Implementation plan:** add this after the M4 table:

   > T13.2 is implemented; see the
   > [design and implementation notes](t13-2-completion-service-design.md).
   > `GuideCompletionService` marks a guide complete with the clock's time
   > or back in progress, and returns the committed state. The write changes
   > only `CompletedUtcMs`, in one SQLite transaction. A repeated completion
   > keeps the first time, and the locator, estimate, and open time are
   > untouched. A 100% estimate never creates a completion time. T13.1 adds
   > the actions.

3. **Work breakdown:** after the T13.2 line, add
   `  Implemented; see
   [p1/t13-2-completion-service-design.md](p1/t13-2-completion-service-design.md).`
4. **Technical design:** after the T13.2 bullet, add
   `  See the [T13.2 design](p1/t13-2-completion-service-design.md).`
   If a ruling changed shipped behavior, add one sentence saying so.

- [ ] **Step 2: Commit**

```bash
git add docs/
git commit -m "docs(p1): T13.2 record the implementation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 3: Full-matrix run**

Run: push, then run
`gh workflow run windows-ci.yml --ref feat/p1-t13-2-completion-service -f shell-scope=core`
without dev-fast, and follow the CI loop.
Expected: every job passes, including ARM64, packages and the installed
`core` group. Nothing changes the UI, so installed groups other than `core`
aren't needed. Debug any failure with superpowers:systematic-debugging
rather than re-running it. A known-flaky job that the branch doesn't touch
may be re-run once with `gh run rerun --failed`, and the run's notes record
that.

- [ ] **Step 4: Record the run**

Put the run ID into the design's status line and Verification section,
commit (`docs(p1): T13.2 record the full run`), and push.

- [ ] **Step 5: Ledger**

Run `task-done` with the full run's result.

---

## Self-review

**Spec coverage:**

| Spec requirement | Task |
| --- | --- |
| `IGuideCompletionStore` contract; `ILibraryRepository` extends it; `ImporterFakes` stub | 1, 2 |
| `GuideCompletionService`: clock time, null, committed value returned, errors pass through, null arguments rejected | 1 |
| One transaction, guarded `UPDATE`, read-back, missing row, negative time before any write | 2 |
| Only `CompletedUtcMs` written; locator, estimate and open time intact | 2 (`CompletionTogglesTwiceAndKeepsTheReadingPlace`, `ConcurrentWritesKeepEveryColumn`) |
| Repeat keeps the first time; repeated in-progress stays null | 2 (`RepeatingAnActionKeepsTheCommittedState`) |
| Restart: `Completed`, then `~N%` with locator intact | 2 (`CompletionSurvivesReopen`) |
| 100% estimate isn't completion (TR13.1) | 2 (`AFullEstimateIsNotCompletion`) |
| Docs: design, implementation-plan, work-breakdown, technical design; e2e-testing unchanged | 3 |

**Placeholder scan:** none. Run IDs are filled in from CI output in Task 3.

**Type consistency:** the signatures of `SetGuideCompletionAsync`,
`MarkCompleteAsync` and `MarkInProgressAsync` match across Tasks 1 and 2 and
the Global Constraints.

**Characterization note:** `AFullEstimateIsNotCompletion` would pass against
`main`'s storage logic on its own, because nothing writes `CompletedUtcMs`
there. It is kept as TR13.1's regression guard. Its RED in Task 2 comes only
from the file failing to compile, which is acceptable for a guard. It is not
evidence of new behavior.
