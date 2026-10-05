# T12.3 Progress Estimates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Library rows show a truthful reading state: a bounded per-format
estimate, the last open time, and an `Approximate` restore with its status
message when a TXT or PDF guide's managed bytes changed.

**Architecture:** `ProgressCoordinator` keeps each capture's estimate, bounded
by a new `ProgressEstimate.Bound`, in both the locator JSON and the
`EstimatedFraction` column, and records the open time through a new
`IReadingLocationStore.RecordGuideOpenedAsync` as each tracking's first turn.
`ManagedPdfGuideLoader` hashes the managed copy it opens, and `PdfReaderSession`
compares and caches against that hash instead of the stored record. The
existing presentation (`CatalogPresentation.ReadingStateFact`) needs no change;
installed `progress-row` and `progress-changed` modes prove it end to end.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), WebView2, PdfPig, SQLite,
xUnit, PowerShell 5.1 UI Automation smoke.

**Spec:** [t12-3-progress-estimates-design.md](t12-3-progress-estimates-design.md)

## Global Constraints

**Branch:** `feat/p1-t12-3-progress-estimates` (already checked out; the spec is commit `5f9f8b1`).

**Tooling:**

- There is no local `dotnet` or `pwsh`. Every build and test runs in CI.
- The CI loop for branch `<b>`:
  1. Push, then `gh workflow run windows-ci.yml --ref <b> -f shell-scope=<group> -f dev-fast=true`.
  2. `gh run list --workflow windows-ci.yml --branch <b> --limit 1 --json databaseId,headSha -q '.[0]'`, and check that `headSha` matches `git rev-parse HEAD`.
  3. `gh run watch <id> --exit-status --interval 60`.
  4. On failure, `gh run view <id> --log-failed`. Smoke errors are in the `error`
     field of the `production-shell-ui` artifact JSON (`gh run download <id> --pattern '*shell*'`;
     the JSON has a BOM, read it with `utf-8-sig`).
- `core-tests` takes about 3 minutes and runs Core.Tests and Infrastructure.Tests.
  A core-only step may `gh run cancel` the run once `core-tests` has finished.
- Pass counts: `gh api repos/ilya-slalom/desktop-guides/actions/jobs/<job-id>/logs | grep "Passed!"`.
- A RED run may be batched with the previous task's GREEN run only when they land
  in different jobs.
- ASCII check: `LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/*.ps1` (expect no output).
- `dev-fast=true` runs are for iteration and are not PR evidence.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

**Values (verbatim):**

- `ProgressEstimate.Bound`: a finite value is clamped to `[0,1]`; NaN, `+∞` and `-∞` become `null`.
- The bounded estimate is the one serialized into the locator JSON **and** passed as the store's `estimatedFraction`.
- `RecordGuideOpenedAsync` sets only `LastOpenedUtcMs` (Unix milliseconds); a negative time throws `ArgumentOutOfRangeException(nameof(openedUtc))`; a missing row throws `ReadingStateMissingException`.
- `ProgressCounts(int Saves, int SkippedUnchanged, int Failures, int Opens)`; the diagnostics file holds `{ "saves", "skippedUnchanged", "failures", "opens" }`.
- A failed open counts in `Failures` and never raises `SaveFailed`; a missing row on open is ignored.
- `PdfGuideLoaded.ContentSha256`: 64 lowercase hex characters; hash buffer 1 MiB.
- Approximate status (unchanged from T12.2): `Opened near your last place. The guide changed since you were here.`
- Row facts (unchanged from T05.1): `~N%` / `about N percent`, `In progress`, `Not started`, `Completed`, `opened today at {time}`.
- Nothing in T12.3 reads or writes `CompletedUtcMs`; an estimate of 100% never means complete.
- Imported content and paths are untrusted; originals are never touched.

## Review Focus

1. Recording opens changes `LastOpenedUtcMs`, which orders Game rows and Library
   games. Installed groups other than `progress` that assume import order could
   reorder. Expected: they still pass; Task 5's `shell-scope=all` run checks it.
2. An HTML guide restored `Exact` whose scroll fraction drifts as images load
   rewrites its locator once, with the new estimate. Expected: one write, and the
   row shows the newer estimate. Covered by the coordinator's unchanged-skip; no
   new test.
3. A PDF managed copy that can be opened but not read while hashing (an I/O
   error mid-file). Expected: `Unreadable`, the file disposed, no crash. Task 3
   pins it with `HashReadFailureIsUnreadable`.
4. A guide opened just before local midnight shows `opened today at …` until the
   Game page reloads. Expected: the next visit says `opened yesterday`. T05.1's
   presentation tests already cover the date rules; no new test.
5. A row saved by a T12.2 build (locator, null estimate, no open time). Expected:
   `Not started` until reopened, then `In progress` and the open time, then `~N%`
   after the first movement. Task 1's `NullEstimateBaselineIsRewrittenWithTheEstimate`
   pins the rewrite.

---

## File map

| File | Change |
| --- | --- |
| `src/DesktopGuides.Core/Reading/ProgressEstimate.cs` | Create: `Bound` |
| `src/DesktopGuides.Core/Reading/IReadingLocationStore.cs` | Add `RecordGuideOpenedAsync` |
| `src/DesktopGuides.Core/Reading/ProgressCoordinator.cs` | Bounded estimate in saves; open as first turn; `Opens` count |
| `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` | Implement `RecordGuideOpenedAsync` |
| `src/DesktopGuides.Infrastructure/Reading/ManagedPdfGuideLoader.cs` | Hash the managed copy; `PdfGuideLoaded.ContentSha256` |
| `src/DesktopGuides.Production/PdfReaderSession.cs` | Use the loaded hash for capture, restore and render key |
| `src/DesktopGuides.Production/ShellWindow.PdfReader.cs` | Pass the loaded hash to `OpenAtSavedPlaceAsync` |
| `src/DesktopGuides.Production/ShellWindow.Progress.cs` | Baseline keeps the bounded estimate; diagnostics add `opens` |
| `src/DesktopGuides.Production/ShellWindow.xaml.cs` | Replace the `ContentChanged is T12.3's` comment |
| `tests/DesktopGuides.Core.Tests/ProgressEstimateTests.cs` | Create |
| `tests/DesktopGuides.Core.Tests/ProgressCoordinatorTests.cs` | Estimate and open tests; fake store gains opens |
| `tests/DesktopGuides.Core.Tests/TextLocatorTests.cs` | Changed-TXT characterization |
| `tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs` | `FakeRepository` stub |
| `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs` | Estimate, open and completion tests |
| `tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedPdfGuideLoaderTests.cs` | Hash tests |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | `Unopened Guide`; `describe-progress`; `change-progress-copies` |
| `tools/p1/windows_shell_ui_smoke.ps1` | `progress-row`, `progress-changed` modes |
| `tools/p1/windows_shell_install.ps1` | Run the new modes in the `progress` group |
| docs (Task 5) | Design notes, implementation plan, work breakdown, technical design, e2e testing |

---

## Task 1: Bounded estimates in every save

**Files:**
- Create: `src/DesktopGuides.Core/Reading/ProgressEstimate.cs`
- Create: `tests/DesktopGuides.Core.Tests/ProgressEstimateTests.cs`
- Modify: `src/DesktopGuides.Core/Reading/ProgressCoordinator.cs` (`SaveAsync`, lines 202 and 212)
- Modify: `src/DesktopGuides.Production/ShellWindow.Progress.cs:103` (`CaptureBaselineAsync`)
- Modify: `tests/DesktopGuides.Core.Tests/ProgressCoordinatorTests.cs`
- Modify: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs` (`ProgressCoordinatorSavesEachGuidesLocatorAcrossReopen`)

**Interfaces:**
- Consumes: `ReaderLocation.EstimatedFraction` (`double?`), `ReaderLocationCodec.Serialize`.
- Produces: `public static class ProgressEstimate { public static double? Bound(double? estimate); }` in namespace `DesktopGuides.Core.Reading`. Task 2 and Task 4 rely on saves writing the bounded estimate to `EstimatedFraction`.

- [ ] **Step 1: Write the failing tests**

`tests/DesktopGuides.Core.Tests/ProgressEstimateTests.cs`:

```csharp
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ProgressEstimateTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(1.0, 1.0)]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    public void FiniteEstimateIsClampedToTheUnitRange(double estimate, double expected) =>
        Assert.Equal(expected, ProgressEstimate.Bound(estimate));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteEstimateIsDropped(double estimate) =>
        Assert.Null(ProgressEstimate.Bound(estimate));

    [Fact]
    public void MissingEstimateStaysMissing() => Assert.Null(ProgressEstimate.Bound(null));
}
```

In `ProgressCoordinatorTests.cs`:

1. Change `FakeSession.Estimate` from `{ get; init; }` to `{ get; set; }`, and
   `CurrentJson` to:

   ```csharp
   public string CurrentJson =>
       ReaderLocationCodec.Serialize(Current with { EstimatedFraction = ProgressEstimate.Bound(Estimate) });
   ```

2. Replace `WrittenEstimateIsAlwaysNull` with:

   ```csharp
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
   ```

   `NullEstimateBaselineIsRewrittenWithTheEstimate` models a row a T12.2 build
   saved (same position, no estimate): the first movement rewrites it once.

3. Every existing `(double?)null` in an expected write stays `null`: those
   sessions have no `Estimate`.

In `SqliteLibraryRepositoryTests.ProgressCoordinatorSavesEachGuidesLocatorAcrossReopen`,
replace the assertions that the written estimate is null and that the decoded
location equals `session.Current with { EstimatedFraction = null }` with:

```csharp
Assert.Equal(0.4, state.EstimatedFraction);
Assert.Equal(session.Current, decoded.Location);
```

(Use the test's existing local names for the reading state, decoded result and
session; `StoreSession`'s estimate is already 0.4.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: commit the test changes alone (`git commit -m "test(core): T12.3 saves keep the bounded estimate (RED)"`), push, start the CI loop with `-f shell-scope=core -f dev-fast=true`, and cancel once `core-tests` finishes.
Expected: `core-tests` FAILS to build Core.Tests with `CS0103: The name 'ProgressEstimate' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

`src/DesktopGuides.Core/Reading/ProgressEstimate.cs`:

```csharp
namespace DesktopGuides.Core.Reading;

// Session estimates come from guide-driven layout, so they are bounded before
// they are serialized or stored.
public static class ProgressEstimate
{
    public static double? Bound(double? estimate) =>
        estimate is double value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : null;
}
```

In `ProgressCoordinator.Tracking.SaveAsync`, replace

```csharp
string json = ReaderLocationCodec.Serialize(location with { EstimatedFraction = null });
```

with

```csharp
double? estimate = ProgressEstimate.Bound(location.EstimatedFraction);
string json = ReaderLocationCodec.Serialize(location with { EstimatedFraction = estimate });
```

and the store call with

```csharp
await owner.store.SaveReadingLocationAsync(guideId, json, estimate, token)
    .WaitAsync(token).ConfigureAwait(false);
```

In `ShellWindow.Progress.cs` `CaptureBaselineAsync`, replace
`ReaderLocationCodec.Serialize(location with { EstimatedFraction = null })` with

```csharp
ReaderLocationCodec.Serialize(location with
{
    EstimatedFraction = ProgressEstimate.Bound(location.EstimatedFraction),
})
```

so an unmoved guide's capture still matches its baseline.

- [ ] **Step 4: Run the tests to verify they pass**

Run: commit (`git commit -m "feat(core): T12.3 saves write the bounded estimate"`), push, CI loop with `-f shell-scope=core -f dev-fast=true`.
Expected: `core-tests` PASSES; Core.Tests' `Passed!` count is 15 higher than on `main` (9 `ProgressEstimateTests` cases, plus 7 new coordinator cases, minus `WrittenEstimateIsAlwaysNull`), and the x64 shell smoke PASSES.

- [ ] **Step 5: Ledger**

The commits are already made in Steps 2 and 4; run `task-done` with the CI run's result.

---

## Task 2: The coordinator records each open

**Files:**
- Modify: `src/DesktopGuides.Core/Reading/IReadingLocationStore.cs`
- Modify: `src/DesktopGuides.Core/Reading/ProgressCoordinator.cs` (`ProgressCounts`, `Counts`, `Record`, `Track`, `Tracking`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (after `SaveReadingLocationAsync`)
- Modify: `src/DesktopGuides.Production/ShellWindow.Progress.cs` (`WriteProgressCountsForTest`)
- Modify: `tests/DesktopGuides.Core.Tests/ProgressCoordinatorTests.cs`
- Modify: `tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs:61`
- Modify: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 1's bounded saves.
- Produces:
  - `Task RecordGuideOpenedAsync(Guid guideId, DateTimeOffset openedUtc, CancellationToken token = default);` on `IReadingLocationStore` (so on `ILibraryRepository` too).
  - `public sealed record ProgressCounts(int Saves, int SkippedUnchanged, int Failures, int Opens);`
  - The diagnostics JSON gains `opens`; Task 4's smoke reads it.

- [ ] **Step 1: Write the failing Core tests**

In `ProgressCoordinatorTests.FakeStore`, add the open members and record save
events:

```csharp
public List<(Guid Guide, DateTimeOffset At)> Opens { get; } = [];
public Queue<Exception> OpenFailures { get; } = new();
public TaskCompletionSource? OpenHold { get; set; }
public List<string> Events { get; } = [];

public async Task RecordGuideOpenedAsync(
    Guid guideId, DateTimeOffset openedUtc, CancellationToken token = default)
{
    if (OpenHold is TaskCompletionSource hold) await hold.Task.ConfigureAwait(false);
    if (OpenFailures.TryDequeue(out Exception? error)) throw error;
    Opens.Add((guideId, openedUtc));
    Events.Add("open");
}
```

and in `SaveReadingLocationAsync`, after `Writes.Add(...)`, add `Events.Add("save");`.
Without a hold the method completes synchronously, so an open finishes inside
`Track`, before any test subscribes to `CountsChanged`.

Update the expected counts:

| Test | Before | After |
| --- | --- | --- |
| `OneMovementSavesAfterQuietNotBefore` | `(1, 0, 0)` | `(1, 0, 0, 1)` |
| `UnchangedCaptureIsSkipped` | `(1, 1, 0)` | `(1, 1, 0, 1)` (`changes` stays 2) |
| `MovementBackToBaselineSkipsWrite` | `(0, 1, 0)` | `(0, 1, 0, 1)` |
| `StoreFailureKeepsDirtyRetriesAndReportsOnce` | `(1, 0, 2)` | `(1, 0, 2, 1)` |

Replace `NoMovementWritesNothing` and add the open tests:

```csharp
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
```

In `ImporterFakes.FakeRepository`, after the `SaveReadingLocationAsync` stub, add:

```csharp
public Task RecordGuideOpenedAsync(Guid guideId, DateTimeOffset openedUtc, CancellationToken token = default) => throw new NotSupportedException();
```

- [ ] **Step 2: Write the failing Infrastructure tests**

In `ProgressCoordinatorSavesEachGuidesLocatorAcrossReopen`:

- Construct the coordinator with `new FixedTimeProvider(Now.AddHours(2))` instead of `TimeProvider.System`.
- Expect `new ProgressCounts(2, 0, 0, 2)`.
- In the reopened loop, also assert `Assert.Equal(Now.AddHours(2), state?.LastOpenedUtc);` and `Assert.Null(state?.CompletedUtc);`.
- After the loop:

  ```csharp
  Game game = Assert.Single(await reopened.ListGamesAsync());
  IReadOnlyList<GuideSummary> summaries = await reopened.ListGuideSummariesAsync(game.Id);
  Assert.All(summaries, summary => Assert.Equal(0.4, summary.State?.EstimatedFraction));
  Assert.All(summaries, summary => Assert.Equal(Now.AddHours(2), summary.State?.LastOpenedUtc));
  ```

Add two tests after it:

```csharp
[Fact]
public async Task SaveAndOpenKeepACompletedGuideCompleted()
{
    using TestLibrary directory = new();
    Guid guide = Guid.NewGuid();
    StoreSession session = new(120);
    await using SqliteLibraryRepository repository =
        new(directory.Paths, new FixedTimeProvider(Now));
    await repository.InitializeAsync();
    Game game = await repository.AddGameAsync("Progress", null, null);
    InsertGuide(directory.Paths.DatabasePath, guide, game.Id);
    SetCompleted(directory.Paths.DatabasePath, guide);
    ProgressCoordinator coordinator = new(repository, new FixedTimeProvider(Now.AddHours(2)));

    await using (IProgressTracking tracking = coordinator.Track(guide, session, null))
    {
        session.Move();
    }

    GuideSummary summary = Assert.Single(await repository.ListGuideSummariesAsync(game.Id));
    Assert.Equal(Now, summary.State?.CompletedUtc);
    Assert.Equal(0.4, summary.State?.EstimatedFraction);
    Assert.Equal(Now.AddHours(2), summary.State?.LastOpenedUtc);
    IReadOnlyList<CatalogFact> facts = CatalogPresentation.GuideFacts(
        summary, new FixedTimeProvider(Now.AddHours(3)), CultureInfo.InvariantCulture);
    Assert.Equal("Completed", facts[1].Label);
}

[Fact]
public async Task RecordGuideOpenedRejectsAMissingRowAndANegativeTime()
{
    using TestLibrary directory = new();
    await using SqliteLibraryRepository repository = new(directory.Paths);
    await repository.InitializeAsync();
    Guid unknown = Guid.NewGuid();

    ReadingStateMissingException missing = await Assert.ThrowsAsync<ReadingStateMissingException>(() =>
        repository.RecordGuideOpenedAsync(unknown, Now));
    Assert.Equal(unknown, missing.GuideId);
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
        repository.RecordGuideOpenedAsync(unknown, DateTimeOffset.UnixEpoch.AddMilliseconds(-1)));
}
```

Add `using System.Globalization;` if the file lacks it. If `InsertGuide` or the
other helpers take different arguments than shown, match the existing call in
`ProgressCoordinatorSavesEachGuidesLocatorAcrossReopen`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: commit the test changes alone (`git commit -m "test(core): T12.3 the coordinator records each open (RED)"`), push, CI loop with `-f shell-scope=core -f dev-fast=true`, cancel once `core-tests` finishes.
Expected: `core-tests` FAILS to build Core.Tests with `CS1729: 'ProgressCounts' does not contain a constructor that takes 4 arguments` and `CS1061: 'ProgressCounts' does not contain a definition for 'Opens'`. (The fakes' extra `RecordGuideOpenedAsync` methods compile; they just aren't interface members yet.)

- [ ] **Step 4: Write the implementation**

`IReadingLocationStore`:

```csharp
public interface IReadingLocationStore
{
    Task SaveReadingLocationAsync(
        Guid guideId, string locatorJson, double? estimatedFraction,
        CancellationToken token = default);

    // Sets only the guide's last open time.
    Task RecordGuideOpenedAsync(
        Guid guideId, DateTimeOffset openedUtc, CancellationToken token = default);
}
```

`SqliteLibraryRepository`, after `SaveReadingLocationAsync`:

```csharp
public Task RecordGuideOpenedAsync(
    Guid guideId, DateTimeOffset openedUtc, CancellationToken token = default)
{
    long openedMs = openedUtc.ToUnixTimeMilliseconds();
    if (openedMs < 0) throw new ArgumentOutOfRangeException(nameof(openedUtc));
    return WriteAsync(() =>
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ReadingStates SET LastOpenedUtcMs = $opened WHERE GuideId = $id
            """;
        command.Parameters.AddWithValue("$id", guideId.ToString("N"));
        command.Parameters.AddWithValue("$opened", openedMs);
        if (command.ExecuteNonQuery() != 1) throw new ReadingStateMissingException(guideId);
    }, token);
}
```

`ProgressCoordinator`:

- `public sealed record ProgressCounts(int Saves, int SkippedUnchanged, int Failures, int Opens);`
- Add `private int opens;`; `Counts` returns `new(saves, skippedUnchanged, failures, opens)`.
- `Record(int saved, int skipped, int failed, int opened = 0)` adds `opens += opened;`.
  Existing calls keep their three arguments.
- Update the class comment: `// ... and on flush or dispose. Each tracking first records the open time.`
- In `Track`, after the `Interlocked.Exchange(...)?.End();` line:

  ```csharp
  // The open is the tracking's first turn, so a save waits for it.
  _ = tracking.RecordOpenAsync(clock.GetUtcNow());
  ```

- In `Tracking`, before `FlushAsync`:

  ```csharp
  // Never throws. A failure is counted but not reported: the guide did open,
  // and the next open records it again.
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
          owner.Record(0, 0, 0, 1);
      }
      catch (ReadingStateMissingException)
      {
          // The guide was removed.
      }
      catch (Exception)
      {
          owner.Record(0, 0, 1);
      }
      finally
      {
          turn.Release();
      }
  }
  ```

  `Track` runs on the shell's UI thread, so the open starts on the tracking's
  context like a timer save. It never sets `dirty` and never calls `Failed`.

`ShellWindow.Progress.cs` `WriteProgressCountsForTest`: add `opens = counts.Opens`
to the anonymous object after `failures`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: commit (`git commit -m "feat(core): T12.3 the coordinator records each open"`), push, CI loop with `-f shell-scope=progress -f dev-fast=true`.
Expected: `core-tests` PASSES; Core.Tests' `Passed!` count is 5 higher than after Task 1 (6 new open tests minus `NoMovementWritesNothing`), Infrastructure.Tests' is 2 higher. The `progress` group still PASSES: its rows now record opens, but no T12.2 phase reads the row facts.

- [ ] **Step 6: Ledger**

Run `task-done` with the CI run's result.

---

## Task 3: Changed managed copies restore approximately

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Reading/ManagedPdfGuideLoader.cs` (`PdfGuideLoaded`, `Open`, new `HashOrNull`)
- Modify: `src/DesktopGuides.Production/PdfReaderSession.cs` (constructor, lines 128, 136, 224)
- Modify: `src/DesktopGuides.Production/ShellWindow.PdfReader.cs:68`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs:1714` (comment only)
- Modify: `tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedPdfGuideLoaderTests.cs`
- Modify: `tests/DesktopGuides.Core.Tests/TextLocatorTests.cs`

**Interfaces:**
- Consumes: Task 1's stored estimates (the TXT characterization serializes with `ProgressEstimate.Bound`).
- Produces: `public sealed record PdfGuideLoaded(string FilePath, PdfPageTextSource Text, string ContentSha256) : PdfGuideLoad;` (64 lowercase hex). Task 4's `progress-changed` mode relies on a changed PDF copy restoring `Approximate`.

- [ ] **Step 1: Write the failing loader tests**

In `ManagedPdfGuideLoaderTests`, add `using System.Security.Cryptography;` and:

```csharp
[Fact]
public async Task LoadedHashIsTheManagedCopysSha256()
{
    await using PublisherHarness harness = await PublisherHarness.CreateAsync();
    (Guide guide, _) = await PublishAsync(harness);

    PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(await LoadAsync(harness, guide));
    using PdfPageTextSource text = loaded.Text;

    string expected = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(ManagedFile(harness, guide))));
    Assert.Equal(expected, loaded.ContentSha256);
    Assert.Equal(guide.ContentSha256, loaded.ContentSha256, ignoreCase: true);
}

[Fact]
public async Task BytesAppendedPastEofChangeTheHashButStillLoad()
{
    await using PublisherHarness harness = await PublisherHarness.CreateAsync();
    (Guide guide, _) = await PublishAsync(harness);
    string managed = ManagedFile(harness, guide);
    File.SetAttributes(managed, FileAttributes.Normal);
    File.AppendAllText(managed, "\n% changed\n");

    PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(await LoadAsync(harness, guide));
    using PdfPageTextSource text = loaded.Text;

    Assert.NotEqual(guide.ContentSha256, loaded.ContentSha256, StringComparer.OrdinalIgnoreCase);
    Assert.Equal(1, text.PageCount);
}

[Fact]
public async Task HashReadFailureIsUnreadable()
{
    await using PublisherHarness harness = await PublisherHarness.CreateAsync();
    (Guide guide, _) = await PublishAsync(harness);
    string managed = ManagedFile(harness, guide);
    using FileStream locker = new(managed, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    // A byte-range lock past the header makes the hash's read fail with an
    // IOException after the header check has passed.
    locker.Lock(locker.Length - 1, 1);

    Assert.Equal(PdfGuideLoadError.Unreadable, await FailedAsync(harness, guide));
}
```

`FailedAsync(harness, guide)` is the file's existing helper that asserts a
`PdfGuideLoadFailed` and returns its `Error`.

- [ ] **Step 2: Write the TXT characterization test**

In `TextLocatorTests`:

```csharp
[Fact]
public void ChangedGuideWithoutItsContextRestoresNearTheStoredEstimate()
{
    static string Lines(string word) => string.Concat(
        Enumerable.Range(1, 400).Select(i => $"Line {i:0000} | {word} guide text.\n"));
    TextGuideDocument numbered = Doc(Lines("Numbered"));
    TextGuideDocument edited = Doc(Lines("Edited"));
    ReaderLocation captured = TextLocator.Capture(numbered, 150);
    string json = ReaderLocationCodec.Serialize(captured with
    {
        EstimatedFraction = ProgressEstimate.Bound(captured.EstimatedFraction),
    });

    LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
        json, GuideFormat.Txt, edited.ContentSha256);
    Assert.Equal(LocationDecodeStatus.ContentChanged, decoded.Status);
    TextRestore restore = TextLocator.Restore(edited, decoded.Location!);

    Assert.Equal(new TextRestore(150,
        new RestoreOutcome(RestoreKind.Approximate, TextLocator.ApproximateReason)), restore);
}
```

This is a characterization test: `TextLocator` already falls back to the
estimate, so it passes before any production change. It pins the end-to-end
path T12.3 enables (a stored estimate, a changed hash, no context match) and
the estimate's round trip through the codec. Ledger it as a characterization,
not a RED.

- [ ] **Step 3: Run the tests to verify the loader tests fail**

Run: commit the test changes alone (`git commit -m "test(infra): T12.3 the PDF loader hashes its managed copy (RED)"`), push, CI loop with `-f shell-scope=core -f dev-fast=true`, cancel once `core-tests` finishes.
Expected: `core-tests` FAILS to build Infrastructure.Tests with `CS1061: 'PdfGuideLoaded' does not contain a definition for 'ContentSha256'`. Core.Tests builds, and `ChangedGuideWithoutItsContextRestoresNearTheStoredEstimate` PASSES.

- [ ] **Step 4: Write the implementation**

`ManagedPdfGuideLoader.cs`: add `using System.Security.Cryptography;`, change the record to

```csharp
public sealed record PdfGuideLoaded(string FilePath, PdfPageTextSource Text, string ContentSha256) : PdfGuideLoad;
```

and in `Open`, replace

```csharp
file.Position = 0;
PdfPageTextSource text = PdfPageTextSource.Open(file, token);
```

with

```csharp
// The managed copy can change after import, so restores compare against
// the bytes actually opened.
file.Position = 0;
string? contentSha256 = HashOrNull(file, token);
if (contentSha256 is null)
{
    file.Dispose();
    return Failed(PdfGuideLoadError.Unreadable);
}
file.Position = 0;
PdfPageTextSource text = PdfPageTextSource.Open(file, token);
```

and `return new PdfGuideLoaded(path, text);` with `return new PdfGuideLoaded(path, text, contentSha256);`.
Add, before `Failed`:

```csharp
private static string? HashOrNull(FileStream file, CancellationToken token)
{
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    byte[] buffer = new byte[1024 * 1024];
    try
    {
        int read;
        while ((read = file.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
        return null;
    }
    return Convert.ToHexStringLower(hash.GetHashAndReset());
}
```

Cancellation propagates to `Open`'s existing `OperationCanceledException` catch,
which disposes the stream and rethrows.

`PdfReaderSession.cs`: add `private readonly string contentSha256;`, set
`contentSha256 = loaded.ContentSha256;` in the constructor after `text = loaded.Text;`,
and replace `guide.ContentSha256` with `contentSha256` at lines 128 (`Capture`),
136 (`Restore`) and 224 (`PdfRenderKey`), so pages cached from the old bytes
aren't reused.

`ShellWindow.PdfReader.cs:68`: replace `guide.ContentSha256.ToLowerInvariant()`
with `loaded.ContentSha256`.

`ShellWindow.xaml.cs:1714`: replace
`// ContentChanged is T12.3's; T08.2 shows the file as it is.` with
`// The restore compares the decoded document's hash; a changed file shows as it is.`

- [ ] **Step 5: Run the tests to verify they pass**

Run: commit (`git commit -m "feat(pdf): T12.3 PDF restores compare the opened copy's hash"`), push, CI loop with `-f shell-scope=pdf -f dev-fast=true`.
Expected: `core-tests` PASSES with Infrastructure.Tests 3 higher and Core.Tests 1 higher than after Task 2; the `pdf` group PASSES (unchanged copies hash to the stored value, so restores stay `Exact`).

- [ ] **Step 6: Ledger**

Run `task-done` with the CI run's result.

---

## Task 4: Installed row and changed-copy phases

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`seed-progress`; new `describe-progress`, `change-progress-copies`; usage text)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (Mode `ValidateSet` at line 17, the progress `-in` list at line 1200, the progress block)
- Modify: `tools/p1/windows_shell_install.ps1` (`Invoke-ProgressPass`, `Run-ProgressScenarios`, new `Assert-ProgressRows`)

**Interfaces:**
- Consumes: Task 1's stored estimates, Task 2's `LastOpenedUtcMs` writes and `opens` count, Task 3's PDF hash.
- Produces: smoke modes `progress-row` and `progress-changed`; report fields
  `progressRows` (array of `{ Name, HelpText }`), `progress.rows` and
  `progress.changed` in the runner report; screenshots `progress-row-light`,
  `progress-row-dark`, `progress-changed-txt`, `progress-changed-pdf`.

This task is acceptance, not RED/GREEN: the behavior was driven by Tasks 1–3's
failing tests, and these phases prove it in an installed app. A phase that
passes on its first run is expected; one that fails is debugged with
superpowers:systematic-debugging.

- [ ] **Step 1: ShellSeed verbs**

In `seed-progress`, after the `pdf` publish, add a guide that is never opened
(the same bytes as the Numbered guide; only `ManagedRelativeRoot` is unique):

```csharp
Guid unopened = Guid.NewGuid();
await InsertTextGuideAsync(progressPaths, progressGame.Id, unopened, "Unopened Guide",
    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    File.ReadAllBytes(Path.Combine(fixtures, "p1", "txt-numbered.txt")));
```

and add `unopened = unopened.ToString("N")` to the printed JSON.

After the `clear-reading-locations` block, add:

```csharp
if (args.Length == 2 && args[0] == "describe-progress")
{
    // The stored reading state behind each Progress Game row, newest first.
    ManagedPathResolver describePaths = new(args[1]);
    await using SqliteLibraryRepository describeRepository = new(describePaths);
    await describeRepository.InitializeAsync();
    Game describeGame = (await describeRepository.ListGamesAsync())
        .Single(game => game.Title == "Progress Game");
    var describeRows = (await describeRepository.ListGuideSummariesAsync(describeGame.Id))
        .Select(summary => new
        {
            title = summary.Guide.Title,
            estimate = summary.State?.EstimatedFraction,
            openedUtcMs = summary.State?.LastOpenedUtc?.ToUnixTimeMilliseconds(),
            completedUtcMs = summary.State?.CompletedUtc?.ToUnixTimeMilliseconds(),
        });
    Console.WriteLine(JsonSerializer.Serialize(describeRows));
    return 0;
}

if (args.Length == 2 && args[0] == "change-progress-copies")
{
    // Edits the managed copies, never the fixtures: every TXT line loses its
    // saved context, and the PDF gains bytes past %%EOF.
    ManagedPathResolver changePaths = new(args[1]);
    await using SqliteLibraryRepository changeRepository = new(changePaths);
    await changeRepository.InitializeAsync();
    Game changeGame = (await changeRepository.ListGamesAsync())
        .Single(game => game.Title == "Progress Game");
    IReadOnlyList<GuideSummary> changeGuides =
        await changeRepository.ListGuideSummariesAsync(changeGame.Id);
    string ManagedCopy(string title)
    {
        Guide guide = changeGuides.Single(summary => summary.Guide.Title == title).Guide;
        string copy = changePaths.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);
        File.SetAttributes(copy, FileAttributes.Normal);
        return copy;
    }
    string numberedCopy = ManagedCopy("Numbered Lines Guide");
    string numberedText = File.ReadAllText(numberedCopy);
    if (!numberedText.Contains("Numbered guide text.", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The Numbered guide copy has no line to edit.");
    }
    File.WriteAllText(numberedCopy,
        numberedText.Replace("Numbered guide text.", "Edited guide text.", StringComparison.Ordinal),
        new UTF8Encoding(false));
    File.AppendAllText(ManagedCopy("Long PDF Guide"), "\n% changed\n");
    return 0;
}
```

Add `using System.Text;` if the file lacks it. In the usage text, add
`describe-progress` to the `seed-linked-game|describe-providers|...` line and a
line `"or change-progress-copies <app-data-root> " +` after
`clear-reading-locations`.

- [ ] **Step 2: Smoke modes**

Add `'progress-row', 'progress-changed'` after `'progress-two-guides'` in the Mode
`ValidateSet` (line 17) and in the progress `-in` list (line 1200).

In the progress block, insert before the final `else {` (the `progress-two-guides`
branch):

```powershell
            elseif ($Mode -eq 'progress-row') {
                # progress-row: after a normal close and relaunch, moved guides
                # show their estimate and open time; an unopened guide does not.
                $report.progressRows = Assert-RowFacts 'GuideList' @(
                    @('Numbered Lines Guide', 'Text (TXT), about * percent, opened today at *'),
                    @('Long Web Guide', 'Web page (HTML), about * percent, opened today at *'),
                    @('Long PDF Guide', 'PDF, about * percent, opened today at *'),
                    @('Unopened Guide', 'Text (TXT), Not started'))
                Wait-HiddenById 'ShellStatus'
                $report.progressRowScreenshot = Save-WindowScreenshot 'progress-row'
                $report.phases += 'progress-row'
            }
            elseif ($Mode -eq 'progress-changed') {
                # progress-changed: changed managed copies restore near the
                # saved place by its estimate, and say so.
                $approximate = 'Opened near your last place. The guide changed since you were here.'
                Open-TextGuide 'Numbered Lines Guide'
                [void](Wait-Status $approximate)
                Wait-FirstTextRow
                Wait-TopLine $ExpectedTopLine 'Reopening a changed TXT guide'
                $report.progressChangedTxtScreenshot = Save-WindowScreenshot 'progress-changed-txt'
                Back-ToTextGame
                Open-TextGuide 'Long PDF Guide'
                [void](Wait-Status $approximate)
                [void](Wait-PdfPage 121 200 'page 121 of 200')
                $report.progressPdfFraction = Wait-PdfFraction 0.3 'Reopening a changed PDF guide'
                $report.progressChangedPdfScreenshot = Save-WindowScreenshot 'progress-changed-pdf'
                Back-ToTextGame
                Assert-NoSaveFailure 'progress-changed'
                $report.phases += 'progress-changed'
            }
```

Every line of `txt-numbered.txt` has the same length, and so does every edited
line, so the estimate lands on the saved line exactly (Task 3's
characterization test checks the same arithmetic).

- [ ] **Step 3: Runner**

In `Invoke-ProgressPass`, add a parameter and pass it through:

```powershell
function Invoke-ProgressPass([string] $mode, [int] $expectedTopLine, [switch] $Kill, [string] $resultName = $mode) {
```

and `Run-ShellSmoke $mode -ResultName $resultName -ExpectedTopLine $expectedTopLine ...`.

Add after `Invoke-ProgressPass`:

```powershell
function Assert-ProgressRows($rows, $stored) {
    # Each row's percentage is the stored estimate's; no estimate, no percentage.
    foreach ($row in $rows) {
        $state = @($stored | Where-Object { $_.title -eq $row.Name })
        if ($state.Count -ne 1) { throw "No stored state for row '$($row.Name)'." }
        if ($null -ne $state[0].completedUtcMs) { throw "Row '$($row.Name)' is completed." }
        if ($row.HelpText -match 'about (\d+) percent') {
            if ($null -eq $state[0].estimate) {
                throw "Row '$($row.Name)' shows $($Matches[1]) percent with no stored estimate."
            }
            $expected = [Math]::Round([double]$state[0].estimate * 100, [MidpointRounding]::AwayFromZero)
            if ([Math]::Abs([int]$Matches[1] - $expected) -gt 1) {
                throw "Row '$($row.Name)' shows $($Matches[1]) percent; the stored estimate is $($state[0].estimate)."
            }
        }
        elseif ($null -ne $state[0].estimate) {
            throw "Row '$($row.Name)' shows no percentage; the stored estimate is $($state[0].estimate)."
        }
    }
}
```

In `Run-ProgressScenarios`, after the `twoGuides` line and inside the `try`
(no `clear-reading-locations` call in between: the rows read the places the
earlier passes saved):

```powershell
        $stored = Invoke-ShellSeed @('describe-progress', $dataRoot) | ConvertFrom-Json
        $report.progress.stored = $stored
        $originalTheme = Get-AppThemePreference
        try {
            Set-AppThemePreference $true
            $light = Invoke-ProgressPass 'progress-row' 0 -resultName 'progress-row-light'
            Assert-ProgressRows $light.progressRows $stored
            Set-AppThemePreference $false
            $dark = Invoke-ProgressPass 'progress-row' 0 -resultName 'progress-row-dark'
            Assert-ProgressRows $dark.progressRows $stored
            $report.progress.rows = [ordered]@{ light = $light; dark = $dark }
        }
        finally {
            Restore-AppThemePreference $originalTheme
        }

        # The changed copies are managed; the fixtures they came from stay untouched.
        $originals = @('p1\txt-numbered.txt', 'p0\generated\pdf-long.pdf') | ForEach-Object {
            Join-Path $fixtureRoot $_ }
        $before = @($originals | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
        Invoke-ShellSeed @('change-progress-copies', $dataRoot) | Out-Null
        $report.progress.changed = Invoke-ProgressPass 'progress-changed' $restored.progressTopLine
        $after = @($originals | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
        if (($before -join ',') -ne ($after -join ',')) { throw 'A progress fixture original changed.' }
```

Update the function's comment to add: `TR12.3 and TR05.2: rows show the stored
estimate and open time, an unopened guide stays Not started, and changed TXT and
PDF copies restore approximately.`

- [ ] **Step 4: ASCII check**

Run: `LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/*.ps1`
Expected: no output.

- [ ] **Step 5: Run the installed group**

Run: commit (`git commit -m "test(shell): T12.3 installed row and changed-copy phases"`), push, CI loop with `-f shell-scope=progress -f dev-fast=true`.
Expected: all jobs PASS. In the `production-shell-ui` artifact:
- `progress-row-light.json` and `progress-row-dark.json` list `progressRows` in the order Numbered, Web, PDF, Unopened, the first three with `about N percent, opened today at …` and Unopened with `Not started`;
- the runner report's `progress.stored` has a non-null `estimate` and `openedUtcMs` for the first three titles, null for `Unopened Guide`, and null `completedUtcMs` for all four;
- `progress-changed.json` lists phase `progress-changed` with `progressPdfFraction` within 0.02 of 0.3;
- the `progress-row-*` and `progress-changed-*` screenshots exist.

Copy the four screenshots and the three JSON reports into
`docs/p1/evidence/t12-3-progress-estimates/` and commit them
(`git commit -m "docs(p1): T12.3 installed evidence"`).

- [ ] **Step 6: Ledger**

Run `task-done` with the CI run's result.

---

## Task 5: Docs and full verification

**Files:**
- Modify: `docs/p1/t12-3-progress-estimates-design.md` (status line, Implementation notes, Verification)
- Modify: `docs/p1/implementation-plan.md` (the T12.2 paragraph ending `remain T12.3.` at ~line 858; add a T12.3 paragraph after it)
- Modify: `docs/work-breakdown.md:400` (T12.3 line)
- Modify: `docs/p1-technical-design.md:789` (§8 S12 T12.3)
- Modify: `docs/p1/e2e-testing.md` (progress gates at ~line 233; the Reading progress row at ~line 269)

**Interfaces:**
- Consumes: the CI run IDs and results from Tasks 1–4, the ledger's `Ruling:` lines.
- Produces: docs that match the shipped behavior.

- [ ] **Step 1: Update the docs**

1. Design: set `Status: implemented; CI run <id> passed the installed progress group.`
   Add `## Implementation notes` (one bullet per ledgered ruling, plus: the open
   runs inline from `Track` on the UI thread; `change-progress-copies` rewrites
   every Numbered line because appending can't remove the saved context) and
   `## Verification` (the tests by file, and a table of the `progress-row` and
   `progress-changed` results with the run ID).
2. Implementation plan: change `T12.2 writes only the locator: estimates and the open time remain T12.3.`
   to `T12.2 wrote only the locator; T12.3 adds estimates and the open time.` and add:

   > T12.3 is implemented; see the
   > [design and implementation notes](t12-3-progress-estimates-design.md).
   > Library rows show the saved estimate (`~N%`) and when a guide was last
   > opened; a guide never opened stays `Not started`. Estimates are clamped to
   > `[0,1]` and non-finite values are dropped before they are stored. A TXT or
   > PDF guide whose managed copy changed reopens near its place by context,
   > else by percentage with the `Approximate` message. Completion is never
   > inferred from the estimate.

3. Work breakdown: after the T12.3 line add `  Implemented; see
   [p1/t12-3-progress-estimates-design.md](p1/t12-3-progress-estimates-design.md).`
4. Technical design: after the T12.3 bullet add
   `  See the [T12.3 design](p1/t12-3-progress-estimates-design.md).` and, if the
   bullet's wording differs from what shipped (PDF hashes the opened copy;
   HTML denies a changed entry file), add one sentence saying so.
5. E2E testing: change `counts (saves, unchanged skips, failures; no locator text)`
   to `counts (saves, unchanged skips, failures, opens; no locator text)`, and
   append to the Reading progress row: `After a restart, moved guides' rows show
   their stored estimate and today's open time, and an unopened guide shows Not
   started, in light and dark. Changed TXT and PDF managed copies reopen at the
   saved line or page with the approximate message, and the fixture originals
   are unchanged.` with traces `T12.2, T12.3, TR12.1, TR12.2, TR12.3, TR05.2`.

- [ ] **Step 2: Commit**

```bash
git add docs/
git commit -m "docs(p1): T12.3 record the implementation"
```

- [ ] **Step 3: Full installed run**

Run: push, `gh workflow run windows-ci.yml --ref feat/p1-t12-3-progress-estimates -f shell-scope=all`, CI loop.
Expected: all jobs PASS, including every installed group. Opens now order Game
rows and Library games by `LastOpenedUtcMs`, so a group that assumed import
order would fail here; debug any such failure with
superpowers:systematic-debugging rather than re-running it.

- [ ] **Step 4: Record the run**

Put the run ID in the design's status line and Verification table, commit
(`git commit -m "docs(p1): T12.3 record the full run"`), push.

- [ ] **Step 5: Ledger**

Run `task-done` with the full run's result.

---

## Self-review

**Spec coverage:**

| Spec requirement | Task |
| --- | --- |
| Top-of-view estimates kept, bounded by `ProgressEstimate.Bound`, in JSON and column | 1 |
| Unchanged-skip compares JSON with the estimate; baseline keeps the bounded estimate | 1 |
| `RecordGuideOpenedAsync` (only `LastOpenedUtcMs`, missing row, negative time) | 2 |
| Open as the tracking's first turn at `clock.GetUtcNow()`; a save waits for it | 2 |
| Open failure counted, no `SaveFailed`; missing row ignored; late write harmless | 2 |
| Dispose and flush wait for the open | 2 |
| `ProgressCounts.Opens`; diagnostics `opens` | 2 |
| PDF hash in the loader (1 MiB chunks, token checks, `Unreadable` on read failure) | 3 |
| `PdfReaderSession` uses the loaded hash for capture, restore and render key | 3 |
| TXT: no code change; changed bytes without context restore `Approximate` | 3 |
| HTML unchanged | — (no task, by design) |
| Display: no presentation change; `Not started` for never opened | 4 (installed check) |
| Completed stays `Completed` after save and open | 2 |
| Installed `progress-row` (light/dark, within 1 percent) and `progress-changed` (originals untouched) | 4 |
| Docs | 5 |

**Type consistency:** `ProgressEstimate.Bound(double?) → double?` (Tasks 1, 3);
`ProgressCounts(Saves, SkippedUnchanged, Failures, Opens)` (Tasks 2, 4 via
`opens`); `RecordGuideOpenedAsync(Guid, DateTimeOffset, CancellationToken)`
(Task 2 interface, repository, both fakes); `PdfGuideLoaded(FilePath, Text,
ContentSha256)` (Task 3 loader, session, shell).

**Review Focus:** items 3 and 5 have tests in Tasks 3 and 1; items 1, 2 and 4
are checked by Task 5's full run or existing T05.1 tests, as each line says.
