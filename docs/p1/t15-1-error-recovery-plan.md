# T15.1 Error Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the library or a guide is broken, Desktop Guides says what is
wrong, changes nothing, and offers one useful next step. A library that
can't be opened stops on a "Library unavailable" page with **Try again** and
**Open data folder**, and is never replaced by an empty one. A guide whose
managed file is missing or damaged stays listed with its status and offers
**Remove guide**, while every other guide still opens. A missing WebView2
Runtime is reported once at startup without blocking TXT and PDF guides.

**Architecture:** Core gains `LibraryOpenException(LibraryOpenIssue)` and its
copy, `GuideFileHealth` and its presentation, and a shared `GuideLoadAction`
that replaces `HtmlGuideLoadAction` and adds `Remove`.
`SqliteLibraryRepository.Initialize` maps open failures through an internal
`LibraryOpenErrors.Map`. It refuses to create a database in a used data
folder and probes for write access before migrating. After the existing
reconciliation it reports missing guide files and unreadable guide rows. The
shell adds a `ShellWindow.Startup.cs` partial for the unavailable page, a
reader Remove handler that reuses T15.3's dialog, and a background runtime
probe.

**Tech Stack:** .NET 10, WinUI 3, Microsoft.Data.Sqlite, xUnit, the
PowerShell 5.1 UI Automation harness.

**Spec:** `docs/p1/t15-1-error-recovery-design.md`

**Target:** T15.1, in three PRs: a (Tasks 1–6), b (Tasks 7–12) and c
(Tasks 13–14). **Prerequisites:** T03.2, T06.3, T09.1 and T10.1, all merged.
T15.3 (guide removal) is merged and provides `GuideRemover` and
`RemoveGuideDialog`.

| PR | Branch | Tasks |
|---|---|---|
| a: the library won't open | `feat/p1-t15-1-error-recovery` | 1–6 |
| b: guide health and the reader | `feat/p1-t15-1-guide-health` (from `main` after PR a merges) | 7–12 |
| c: the startup runtime check | `feat/p1-t15-1-runtime-check` (from `main` after PR b merges) | 13–14 |

## Global Constraints

- No error path creates, truncates or overwrites `library.sqlite`, a
  recovery copy or a managed guide file. Remove goes through T15.3's
  `GuideRemover` only.
- No schema change. Guide file health lives in memory only.
- `LibraryOpenException` is raised only from `InitializeAsync`. Later reads
  and writes keep their current exceptions.
- No message contains exception text, SQL, or a path other than the data
  folder and the recovery copy.
- All copy is verbatim from this plan, with a straight ASCII apostrophe.
- New members go at the end of existing enums, so existing values keep
  their numbers.
- UI tests assert only what app code controls.
- PowerShell scripts stay ASCII-only.
- Never print, copy or log provider credential values.

## Rulings against the spec

Tasks 6, 12 and 14 record these rulings in the design's verification record.

1. **The mapping is narrower than the spec's list.** `Map` takes no
   `recoveryCopy` parameter. MigrationFailed is raised where the recovery
   copy is known, inside `MigrateOrValidate`. An unmapped exception (for
   example a reconciler `InvalidDataException`) is rethrown unchanged and
   keeps today's "Couldn't open the library" InfoBar.
2. **Technical detail moves to `InnerException`.** The exception's
   `Message` is the user-facing body. Existing tests that checked
   `error.Message` now check `error.InnerException!.Message`.
3. **Locked is unit-tested through `Map` only.** Microsoft.Data.Sqlite
   retries a busy database for its command timeout (30 s), so a held
   `BEGIN EXCLUSIVE` test would be slow and timing-dependent.
4. **A linked database or sidecar maps to Damaged.** `EnsureCreated` and
   `ValidateDatabasePath` reject links with `InvalidDataException` before
   any open.
5. **HTML NoManifest offers Remove and marks the guide Damaged.** Its copy
   ("Re-import this guide to read it.") is unchanged.
6. **A `-wal` file with no database counts as a used data folder** and
   raises Missing, because the WAL may hold committed data.
7. **Retry keeps the lease and disposes the failed repository** before it
   creates a new one.
8. **`HtmlGuideLoadAction` becomes `Core.Reading.GuideLoadAction`.** Its
   Remove label is "Remove guide", with no ellipsis, matching T15.3's header
   button.
9. **Changed TXT and PDF copies keep opening**, as the spec revision
   records. Only HTML Changed and PDF Changed (a folder or link at the
   managed path) are blocked.
10. **`Initialize` probes for write access before migrating.** SQLite opens
    a read-only file in read-only mode without an error, and reads the
    header lazily. The probe rolls back a `PRAGMA user_version` write, so
    NoAccess and Damaged are raised before anything is changed.
11. **A row's status is its first fact plus a caution glyph (`\uE7BA`).**
    There is no `GuideFileStatus` AutomationId. The row's accessible facts
    already carry the status, and an id per row would not be unique.
12. **`GuideFileHealth` stores each guide's game ID**, so the Library page
    counts guides that need attention without listing every game's guides.
13. **The reader's Remove handler mirrors `RemoveSelectedGuideClicked`.**
    After a removal it goes Back, as the Back button would. From the Game
    page that is the game, where T15.3's selection rules pick the survivor.
    From Library Resume it is the Library, whose Resume button no longer
    finds the guide.
14. **Attention counts include guides the list hides.** That can only
    happen to an unreadable row, and those are never marked.
15. **Every body says "Desktop Guides stopped before changing anything".**
    The spec says "ends by saying that nothing was changed". A shared
    sentence in the middle reads better before the next step.
16. **The data folder line has its own id, `LibraryUnavailableDataFolder`.**
    The spec lists no id for it.
17. **The unreadable-row fixture sets `UpdatedUtcMs = long.MaxValue`.** The
    spec suggests editing `Format`. But startup runs `PRAGMA
    integrity_check`, which checks CHECK constraints, so that edit would
    make the whole library Damaged. The `UpdatedUtcMs >= 0` CHECK passes,
    and `DateTimeOffset.FromUnixMilliseconds` throws
    `ArgumentOutOfRangeException`. A non-hex `Id` would break
    `FileOperationReconciler.ReadGuideIds`. `ImportedUtcMs` is aggregated by
    game summaries.
18. **Startup warnings show in this order:** the material fallback, then
    unreadable rows, then the runtime once its background probe returns.
    Each is announced and replaces the previous one in the single status
    bar, so the runtime warning, which matters most to someone with web
    guides, is the one left showing.
19. **The NoRuntime smoke holds `library.session.lock` across launch.**
    `Start-InstalledShell` learns the process ID only after launch, and the
    startup probe could run before the gate exists. Holding the lease keeps
    the app on "Waiting for previous window…" until the gate is created.
20. **The startup runtime warning's button is `ShellStatusAction`,** the
    status InfoBar's `ActionButton`. `ShowStatus` hides it for every other
    status.
21. **Production has no test project.** Tasks 4, 10 and 13's shell changes
    are gated by the Production build, by code review against the Review
    Focus, and by the installed smoke tasks. This is the plan's only TDD
    skip for code.
22. **`StartupReconciliationReport` compares `MissingGuides` by value.** The
    spec adds a list, but a record compares lists by reference, and existing
    tests assert whole reports with `Assert.Equal`. The record normalizes a
    null list to empty and overrides `Equals` and `GetHashCode`. An existing
    test whose seeded guide has no file now expects that guide in
    `MissingGuides`, instead of gaining a file it never needed.
23. **`MissingGuides` holds `MissingGuideFile(GuideId, GameId)`, not bare
    IDs.** The Library's attention count needs each guide's game without a
    second query (Ruling 12).
24. **A Remove dialog for a guide with no files left doesn't count files.**
    T15.3 pinned "its 0 managed files", which was rare until now. A missing
    TXT or PDF guide always has zero files, so `DialogBody(0)` drops the
    file clause: "This removes the guide and its reading progress from
    Desktop Guides. The original file you imported isn't affected."

## Review Focus

1. **The Library's Resume target is an unreadable row.** The Library must
   still load, with no Resume button. Test
   `UnreadableGuideRowIsHiddenAndCounted` (Task 9) pins that
   `GetGuideAsync` throws. The shell's catch at the Library Resume lookup,
   and only there, is checked in code review (Task 10). The Reader route's
   `GetGuideAsync` must keep throwing, because the existing
   `reader-render-error` smoke depends on it.
2. **The database file is read-only** (copied from a backup or a read-only
   share). Startup must say NoAccess and change nothing. Test
   `ReadOnlyDatabaseIsNoAccess` (Task 3).
3. **Only `library.sqlite-wal` remains** (a crash or partial restore).
   Startup must say Missing and must not create a database that would
   adopt or discard the WAL. Test `WalWithoutDatabaseIsMissing` (Task 3).
4. **The window closes while Try again runs.** Close must wait for the
   retry and dispose its repository, and the page must not reappear on a
   closing window. Checked in code review against `WindowClosing` and
   `CloseWhenIdleAsync` (Task 4).
5. **The guide is removed by another path while the reader shows its
   error.** Remove must report "already removed", go back to the game, and
   not throw. Checked in code review (Task 10).

## Host commands

The Mac has no dotnet, so every build and test runs on `pcsx2-win`:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t15-1) { Remove-Item -Recurse -Force E:\work\desktop-guides\t15-1 }; New-Item -ItemType Directory E:\work\desktop-guides\t15-1 | Out-Null"'
  COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t15-1'
}
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t15-1 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
- **Infrastructure tests:** `stage && s 'cd /d E:\work\desktop-guides\t15-1 && dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release'`
- **One class:** append `--filter "FullyQualifiedName~<Class>"` inside the
  quoted command.
- **Production build:** `stage && s 'cd /d E:\work\desktop-guides\t15-1 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'`
- **Seed build:** `stage && s 'cd /d E:\work\desktop-guides\t15-1 && dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release'`

---

# PR a: the library won't open

### Task 1: Core library-open contracts and copy

**Files:**
- Create: `src/DesktopGuides.Core/Library/LibraryOpen.cs`
- Test: `tests/DesktopGuides.Core.Tests/LibraryOpenMessagesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces, in namespace `DesktopGuides.Core.Library`:
  - `enum LibraryOpenIssue { Damaged, Missing, NewerVersion, MigrationFailed, Locked, NoAccess, DiskFull }`
  - `sealed class LibraryOpenException(LibraryOpenIssue issue, string? recoveryCopyPath = null, Exception? inner = null)`
    with `Issue`, `RecoveryCopyPath`, and `Message` equal to the body
  - `sealed record LibraryOpenMessage(string Title, string Body)`
  - `static class LibraryOpenMessages` with `For(LibraryOpenIssue)`,
    `DataFolder(string)` and `RecoveryCopy(string)`

- [ ] **Step 1: Write the failing tests**

`tests/DesktopGuides.Core.Tests/LibraryOpenMessagesTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class LibraryOpenMessagesTests
{
    [Theory]
    [InlineData(LibraryOpenIssue.Damaged, "Library can't be read",
        "Your library database can't be read. Desktop Guides stopped before changing anything. Your guide files are still in the data folder.")]
    [InlineData(LibraryOpenIssue.Missing, "Library database is missing",
        "Your library database is missing, but your guide files are still in the data folder. Desktop Guides stopped before changing anything. Restore library.sqlite, then try again.")]
    [InlineData(LibraryOpenIssue.NewerVersion, "Update Desktop Guides",
        "Your library was saved by a newer version of Desktop Guides. Desktop Guides stopped before changing anything. Update Desktop Guides, then try again.")]
    [InlineData(LibraryOpenIssue.MigrationFailed, "Library upgrade failed",
        "Your library couldn't be upgraded. Desktop Guides stopped before changing anything and kept a copy of your library from before the upgrade.")]
    [InlineData(LibraryOpenIssue.Locked, "Library is in use",
        "Another program may be using your library. Desktop Guides stopped before changing anything. Close that program, then try again.")]
    [InlineData(LibraryOpenIssue.NoAccess, "Library can't be accessed",
        "Desktop Guides doesn't have permission to read or change your library. Desktop Guides stopped before changing anything. Check the data folder's permissions, then try again.")]
    [InlineData(LibraryOpenIssue.DiskFull, "Not enough disk space",
        "There isn't enough free disk space to open your library. Desktop Guides stopped before changing anything. Free up some space, then try again.")]
    public void EachIssueHasItsTitleAndBody(LibraryOpenIssue issue, string title, string body) =>
        Assert.Equal(new LibraryOpenMessage(title, body), LibraryOpenMessages.For(issue));

    [Fact]
    public void EveryBodySaysNothingWasChanged()
    {
        foreach (LibraryOpenIssue issue in Enum.GetValues<LibraryOpenIssue>())
        {
            Assert.Contains(
                "Desktop Guides stopped before changing anything",
                LibraryOpenMessages.For(issue).Body);
        }
    }

    [Fact]
    public void AnUnknownIssueThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LibraryOpenMessages.For((LibraryOpenIssue)99));

    [Fact]
    public void DataFolderNamesThePath() =>
        Assert.Equal(@"Data folder: C:\Data", LibraryOpenMessages.DataFolder(@"C:\Data"));

    [Fact]
    public void RecoveryCopyNamesThePath() =>
        Assert.Equal(
            @"Copy from before the upgrade: C:\Data\.recovery\library.sqlite",
            LibraryOpenMessages.RecoveryCopy(@"C:\Data\.recovery\library.sqlite"));

    [Fact]
    public void ExceptionCarriesItsIssuePathAndCause()
    {
        IOException cause = new("injected");

        LibraryOpenException error = new(LibraryOpenIssue.MigrationFailed, @"C:\copy.sqlite", cause);

        Assert.Equal(LibraryOpenIssue.MigrationFailed, error.Issue);
        Assert.Equal(@"C:\copy.sqlite", error.RecoveryCopyPath);
        Assert.Same(cause, error.InnerException);
        Assert.Equal(LibraryOpenMessages.For(LibraryOpenIssue.MigrationFailed).Body, error.Message);
    }

    [Fact]
    public void ExceptionDefaultsHaveNoPathOrCause()
    {
        LibraryOpenException error = new(LibraryOpenIssue.Missing);

        Assert.Null(error.RecoveryCopyPath);
        Assert.Null(error.InnerException);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: Core tests with `--filter "FullyQualifiedName~LibraryOpenMessagesTests"`.
Expected: build fails with CS0246/CS0103 for `LibraryOpenIssue`,
`LibraryOpenMessage`, `LibraryOpenMessages` and `LibraryOpenException`.

- [ ] **Step 3: Implement**

`src/DesktopGuides.Core/Library/LibraryOpen.cs`:

```csharp
namespace DesktopGuides.Core.Library;

// Why InitializeAsync stopped. Every issue leaves the library unchanged.
public enum LibraryOpenIssue { Damaged, Missing, NewerVersion, MigrationFailed, Locked, NoAccess, DiskFull }

// The message is the user-facing body; technical detail stays in InnerException.
public sealed class LibraryOpenException(
    LibraryOpenIssue issue, string? recoveryCopyPath = null, Exception? inner = null)
    : Exception(LibraryOpenMessages.For(issue).Body, inner)
{
    public LibraryOpenIssue Issue { get; } = issue;
    public string? RecoveryCopyPath { get; } = recoveryCopyPath;
}

public sealed record LibraryOpenMessage(string Title, string Body);

public static class LibraryOpenMessages
{
    private const string Unchanged = "Desktop Guides stopped before changing anything";

    public static LibraryOpenMessage For(LibraryOpenIssue issue) => issue switch
    {
        LibraryOpenIssue.Damaged => new("Library can't be read",
            $"Your library database can't be read. {Unchanged}. Your guide files are still in the data folder."),
        LibraryOpenIssue.Missing => new("Library database is missing",
            $"Your library database is missing, but your guide files are still in the data folder. {Unchanged}. Restore library.sqlite, then try again."),
        LibraryOpenIssue.NewerVersion => new("Update Desktop Guides",
            $"Your library was saved by a newer version of Desktop Guides. {Unchanged}. Update Desktop Guides, then try again."),
        LibraryOpenIssue.MigrationFailed => new("Library upgrade failed",
            $"Your library couldn't be upgraded. {Unchanged} and kept a copy of your library from before the upgrade."),
        LibraryOpenIssue.Locked => new("Library is in use",
            $"Another program may be using your library. {Unchanged}. Close that program, then try again."),
        LibraryOpenIssue.NoAccess => new("Library can't be accessed",
            $"Desktop Guides doesn't have permission to read or change your library. {Unchanged}. Check the data folder's permissions, then try again."),
        LibraryOpenIssue.DiskFull => new("Not enough disk space",
            $"There isn't enough free disk space to open your library. {Unchanged}. Free up some space, then try again."),
        _ => throw new ArgumentOutOfRangeException(nameof(issue))
    };

    public static string DataFolder(string path) => $"Data folder: {path}";

    public static string RecoveryCopy(string path) => $"Copy from before the upgrade: {path}";
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: Core tests with `--filter "FullyQualifiedName~LibraryOpenMessagesTests"`.
Expected: 13 passed, 0 failed.

- [ ] **Step 5: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Core/Library/LibraryOpen.cs tests/DesktopGuides.Core.Tests/LibraryOpenMessagesTests.cs
git commit -m "feat(core): library-open issues and their copy" -m "LibraryOpenException carries the issue and the recovery copy path; its message is the user-facing body." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Map open failures to issues

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Storage/LibraryOpenErrors.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/LibraryOpenErrorsTests.cs`

**Interfaces:**
- Consumes: Task 1's `LibraryOpenException` and `LibraryOpenIssue`.
- Produces, in namespace `DesktopGuides.Infrastructure.Storage`:
  `internal static class LibraryOpenErrors` with
  `static LibraryOpenException? Map(Exception error)`. It returns null for
  an error that has no issue, and for one that is already a
  `LibraryOpenException`.

- [ ] **Step 1: Write the failing tests**

`tests/DesktopGuides.Infrastructure.Tests/LibraryOpenErrorsTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibraryOpenErrorsTests
{
    [Theory]
    [InlineData(26, LibraryOpenIssue.Damaged)]   // NOTADB
    [InlineData(11, LibraryOpenIssue.Damaged)]   // CORRUPT
    [InlineData(5, LibraryOpenIssue.Locked)]     // BUSY
    [InlineData(6, LibraryOpenIssue.Locked)]     // LOCKED
    [InlineData(261, LibraryOpenIssue.Locked)]   // BUSY_RECOVERY, an extended code
    [InlineData(14, LibraryOpenIssue.NoAccess)]  // CANTOPEN
    [InlineData(3, LibraryOpenIssue.NoAccess)]   // PERM
    [InlineData(8, LibraryOpenIssue.NoAccess)]   // READONLY
    [InlineData(23, LibraryOpenIssue.NoAccess)]  // AUTH
    [InlineData(13, LibraryOpenIssue.DiskFull)]  // FULL
    public void SqliteCodesMapByPrimaryCode(int code, LibraryOpenIssue issue)
    {
        SqliteException cause = new("injected", code);

        LibraryOpenException? mapped = LibraryOpenErrors.Map(cause);

        Assert.NotNull(mapped);
        Assert.Equal(issue, mapped.Issue);
        Assert.Same(cause, mapped.InnerException);
        Assert.Null(mapped.RecoveryCopyPath);
    }

    [Fact]
    public void AnUnlistedSqliteCodeIsNotMapped() =>
        Assert.Null(LibraryOpenErrors.Map(new SqliteException("injected", 1)));

    [Fact]
    public void InvalidDataIsDamaged() =>
        Assert.Equal(LibraryOpenIssue.Damaged,
            LibraryOpenErrors.Map(new InvalidDataException("schema is incomplete"))?.Issue);

    [Fact]
    public void UnauthorizedAccessIsNoAccess() =>
        Assert.Equal(LibraryOpenIssue.NoAccess,
            LibraryOpenErrors.Map(new UnauthorizedAccessException())?.Issue);

    [Theory]
    [InlineData(unchecked((int)0x80070070))] // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070027))] // ERROR_HANDLE_DISK_FULL
    public void DiskFullIOExceptionsAreDiskFull(int hresult) =>
        Assert.Equal(LibraryOpenIssue.DiskFull,
            LibraryOpenErrors.Map(new IOException("full", hresult))?.Issue);

    [Fact]
    public void OtherIOExceptionsAreNotMapped() =>
        Assert.Null(LibraryOpenErrors.Map(new IOException("sharing", unchecked((int)0x80070020))));

    [Fact]
    public void UnrelatedExceptionsAreNotMapped() =>
        Assert.Null(LibraryOpenErrors.Map(new InvalidOperationException()));

    [Fact]
    public void AnAlreadyMappedErrorIsNotWrappedAgain() =>
        Assert.Null(LibraryOpenErrors.Map(new LibraryOpenException(LibraryOpenIssue.Missing)));
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryOpenErrorsTests"`.
Expected: build fails with CS0103 for `LibraryOpenErrors`.

- [ ] **Step 3: Implement**

`src/DesktopGuides.Infrastructure/Storage/LibraryOpenErrors.cs`:

```csharp
using DesktopGuides.Core.Library;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Storage;

// Translates a failure raised while opening the library. Anything without an
// issue is rethrown unchanged by the caller and keeps the generic status.
internal static class LibraryOpenErrors
{
    private const int DiskFull = 112;
    private const int HandleDiskFull = 39;

    public static LibraryOpenException? Map(Exception error)
    {
        LibraryOpenIssue? issue = error switch
        {
            LibraryOpenException => null,
            SqliteException sqlite => (sqlite.SqliteErrorCode & 0xFF) switch
            {
                11 or 26 => LibraryOpenIssue.Damaged,
                5 or 6 => LibraryOpenIssue.Locked,
                3 or 8 or 14 or 23 => LibraryOpenIssue.NoAccess,
                13 => LibraryOpenIssue.DiskFull,
                _ => null
            },
            InvalidDataException => LibraryOpenIssue.Damaged,
            UnauthorizedAccessException => LibraryOpenIssue.NoAccess,
            IOException io when (io.HResult & 0xFFFF) is DiskFull or HandleDiskFull => LibraryOpenIssue.DiskFull,
            _ => null
        };
        return issue is { } mapped ? new LibraryOpenException(mapped, null, error) : null;
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryOpenErrorsTests"`.
Expected: 19 passed, 0 failed.

- [ ] **Step 5: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Infrastructure/Storage/LibraryOpenErrors.cs tests/DesktopGuides.Infrastructure.Tests/LibraryOpenErrorsTests.cs
git commit -m "feat(storage): map library open failures to issues" -m "SQLite primary codes, validation errors, access and disk-full errors map to a LibraryOpenIssue; everything else is left unmapped." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: `Initialize` stops with a typed issue and never replaces a used library

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (`Initialize` ~686, `MigrateOrValidate` ~707 and its migration catch)
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 1's `LibraryOpenException`, Task 2's `LibraryOpenErrors.Map`.
- Produces: `InitializeAsync` throws `LibraryOpenException` for every mapped
  failure, with `LastStartupReconciliation` left null. Errors raised by the
  reconciler or the artwork sweep stay unmapped (ruling 1).

- [ ] **Step 1: Update the existing tests to expect `LibraryOpenException`**

In `SqliteLibraryRepositoryTests`, change these `Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync())`
calls. Only the `InitializeAsync` asserts change. The second asserts at
~725 and ~760 (`GetGameAsync` → `InvalidDataException`) stay as they are.

| Line | Test | New assertion |
|---|---|---|
| ~488 | `PreservesPopulatedUnknownVersionZeroDatabase` | `Damaged` |
| ~508 | `PreservesEmptyVersionZeroDatabaseWithAnotherApplicationId` | `Damaged` |
| ~590 | `FailedMigrationRetainsVersionOneDataAndRecoveryCopy` | `MigrationFailed`; `RecoveryCopyPath` exists; `InnerException` is `IOException` |
| ~648 | `RejectsNewerSchemaWithoutReplacingItsData` | `NewerVersion`; `InnerException!.Message` contains "newer than this app supports" |
| ~677 | `RejectsOrphanedVersionOneRowsBeforeMigration` | `Damaged`; `InnerException!.Message` contains "orphaned records" |
| ~701 | `RejectsIncompleteCurrentSchemaWithoutReinitializing` | `Damaged`; `InnerException!.Message` contains "schema is incomplete" |
| ~724 | `RejectsLinkedDatabaseBeforeOpeningExternalVersionOneData` | `Damaged` |
| ~759 | `RejectsLinkedSharedMemoryFileBeforeModifyingItsTarget` | `Damaged` |
| ~805 | the next InitializeAsync assert | `Damaged` |
| ~838 | `RejectsAlteredVersionOneConstraintsBeforeBackup` | `Damaged` |
| ~863 | `RejectsRenamedVersionTwoColumnBeforeRepositoryReads` | `Damaged` |
| ~1005 | `FailedVersionThreeMigrationStaysAtVersionTwo` | `MigrationFailed` |
| ~1210 | `FailedVersionFourMigrationStaysAtVersionThree` | `MigrationFailed` |

The shape of each change, shown for ~590:

```csharp
LibraryOpenException error = await Assert.ThrowsAsync<LibraryOpenException>(
    () => failing.InitializeAsync());
Assert.Equal(LibraryOpenIssue.MigrationFailed, error.Issue);
Assert.True(File.Exists(error.RecoveryCopyPath));
Assert.IsType<IOException>(error.InnerException);
```

And for a one-line assert:

```csharp
LibraryOpenException error = await Assert.ThrowsAsync<LibraryOpenException>(() => repository.InitializeAsync());
Assert.Equal(LibraryOpenIssue.Damaged, error.Issue);
```

Leave `FileOperationReconciliationTests` unchanged. Its
`InvalidDataException` and `IOException` come from the reconciler, which
runs after the mapped block.

- [ ] **Step 2: Add the new startup tests**

Add these to `SqliteLibraryRepositoryTests`:

```csharp
[Fact]
public async Task NonDatabaseBytesAreDamagedAndUnchanged()
{
    using TestLibrary directory = new();
    directory.Paths.EnsureCreated();
    byte[] bytes = Enumerable.Repeat((byte)0x5A, 4096).ToArray();
    File.WriteAllBytes(directory.Paths.DatabasePath, bytes);

    await using SqliteLibraryRepository repository = new(directory.Paths);
    LibraryOpenException error = await Assert.ThrowsAsync<LibraryOpenException>(
        () => repository.InitializeAsync());

    Assert.Equal(LibraryOpenIssue.Damaged, error.Issue);
    Assert.IsType<SqliteException>(error.InnerException);
    Assert.Equal(bytes, File.ReadAllBytes(directory.Paths.DatabasePath));
    Assert.Null(repository.LastStartupReconciliation);
}

[Fact]
public async Task DeletedDatabaseWithGuideContentIsMissingAndNotRecreated()
{
    using TestLibrary directory = new();
    directory.Paths.EnsureCreated();
    string guideRoot = Path.Combine(directory.Paths.ContentRoot, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(guideRoot);
    File.WriteAllText(Path.Combine(guideRoot, "guide.txt"), "keep");

    await using SqliteLibraryRepository repository = new(directory.Paths);
    LibraryOpenException error = await Assert.ThrowsAsync<LibraryOpenException>(
        () => repository.InitializeAsync());

    Assert.Equal(LibraryOpenIssue.Missing, error.Issue);
    Assert.False(File.Exists(directory.Paths.DatabasePath));
}

[Fact]
public async Task ZeroByteDatabaseWithArtworkIsMissingAndStaysEmpty()
{
    using TestLibrary directory = new();
    directory.Paths.EnsureCreated();
    File.WriteAllBytes(Path.Combine(directory.Paths.ArtworkRoot, "cover.png"), [1, 2, 3]);
    File.WriteAllBytes(directory.Paths.DatabasePath, []);

    await using SqliteLibraryRepository repository = new(directory.Paths);
    LibraryOpenException error = await Assert.ThrowsAsync<LibraryOpenException>(
        () => repository.InitializeAsync());

    Assert.Equal(LibraryOpenIssue.Missing, error.Issue);
    Assert.Equal(0, new FileInfo(directory.Paths.DatabasePath).Length);
}

[Fact]
public async Task WalWithoutDatabaseIsMissing()
{
    using TestLibrary directory = new();
    directory.Paths.EnsureCreated();
    File.WriteAllBytes(directory.Paths.DatabasePath + "-wal", [1, 2, 3]);

    await using SqliteLibraryRepository repository = new(directory.Paths);
    LibraryOpenException error = await Assert.ThrowsAsync<LibraryOpenException>(
        () => repository.InitializeAsync());

    Assert.Equal(LibraryOpenIssue.Missing, error.Issue);
    Assert.False(File.Exists(directory.Paths.DatabasePath));
    Assert.Equal([1, 2, 3], File.ReadAllBytes(directory.Paths.DatabasePath + "-wal"));
}

// Guards the first run: it passes before and after the change.
[Fact]
public async Task EmptyDataFolderIsAFirstRun()
{
    using TestLibrary directory = new();

    await using SqliteLibraryRepository repository = new(directory.Paths);
    await repository.InitializeAsync();

    Assert.Equal(LibrarySchema.CurrentVersion, ReadUserVersion(directory.Paths.DatabasePath));
    Assert.NotNull(repository.LastStartupReconciliation);
}

[Fact]
public async Task ReadOnlyDatabaseIsNoAccess()
{
    using TestLibrary directory = new();
    await using (SqliteLibraryRepository first = new(directory.Paths))
    {
        await first.InitializeAsync();
    }
    byte[] before = File.ReadAllBytes(directory.Paths.DatabasePath);
    File.SetAttributes(directory.Paths.DatabasePath, FileAttributes.ReadOnly);
    try
    {
        await using SqliteLibraryRepository repository = new(directory.Paths);
        LibraryOpenException error = await Assert.ThrowsAsync<LibraryOpenException>(
            () => repository.InitializeAsync());

        Assert.Equal(LibraryOpenIssue.NoAccess, error.Issue);
        Assert.Equal(before, File.ReadAllBytes(directory.Paths.DatabasePath));
    }
    finally
    {
        File.SetAttributes(directory.Paths.DatabasePath, FileAttributes.Normal);
    }
}
```

If `LibrarySchema.CurrentVersion` is not visible to the test assembly, use
the literal that the other `ReadUserVersion` asserts in this file use.

- [ ] **Step 3: Run the tests to see them fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~SqliteLibraryRepositoryTests"`.
Expected: the 13 updated tests fail (an `InvalidDataException` was thrown
instead of a `LibraryOpenException`). `NonDatabaseBytesAreDamagedAndUnchanged`
fails with a raw `SqliteException`. The two Missing tests fail because
`InitializeAsync` succeeded and created the database. `WalWithoutDatabaseIsMissing`
fails the same way. `ReadOnlyDatabaseIsNoAccess` fails because
`InitializeAsync` succeeded. `EmptyDataFolderIsAFirstRun` passes.

- [ ] **Step 4: Implement**

Replace `Initialize`:

```csharp
private void Initialize()
{
    LastStartupReconciliation = null;
    SqliteConnection connection;
    try
    {
        paths.EnsureCreated();
        connection = OpenConnection(create: IsFirstRun());
        try
        {
            ProbeWrite(connection);
            MigrateOrValidate(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
    catch (Exception error) when (LibraryOpenErrors.Map(error) is { } mapped)
    {
        throw mapped;
    }
    using (connection)
    {
        StartupReconciliationReport report = new FileOperationReconciler(paths).Run(connection);
        int artworkReview = new ManagedArtworkStore(paths).Sweep(ReadArtworkReferences(connection));
        LastStartupReconciliation = report with { ArtworkReviewCount = artworkReview };
    }
}

// A missing or empty database is a first run only in an unused data folder.
// Otherwise nothing is created, so a restore can still succeed.
private bool IsFirstRun()
{
    FileInfo database = new(paths.DatabasePath);
    if (database.Exists && database.Length > 0)
    {
        return false;
    }
    if (HasEntries(paths.ContentRoot) || HasEntries(paths.ArtworkRoot) ||
        HasEntries(paths.RecoveryRoot) || File.Exists(paths.DatabasePath + "-wal"))
    {
        throw new LibraryOpenException(LibraryOpenIssue.Missing);
    }
    return true;
}

private static bool HasEntries(string directory) =>
    Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();

// An open can succeed read-only, and before the header is read, so a
// rolled-back write surfaces NoAccess or Damaged before anything changes.
private static void ProbeWrite(SqliteConnection connection)
{
    using SqliteCommand read = connection.CreateCommand();
    read.CommandText = "PRAGMA user_version";
    long version = (long)read.ExecuteScalar()!;
    using SqliteTransaction transaction = connection.BeginTransaction();
    using SqliteCommand write = connection.CreateCommand();
    write.Transaction = transaction;
    write.CommandText = $"PRAGMA user_version = {version}";
    write.ExecuteNonQuery();
    transaction.Rollback();
}
```

In `MigrateOrValidate`, change the newer-version throw to:

```csharp
throw new LibraryOpenException(
    LibraryOpenIssue.NewerVersion,
    inner: new InvalidDataException(
        $"Library schema version {currentVersion} is newer than this app supports."));
```

And change the migration catch to:

```csharp
catch (Exception error) when (recoveryCopy is not null)
{
    throw new LibraryOpenException(LibraryOpenIssue.MigrationFailed, recoveryCopy, error);
}
```

Add `using DesktopGuides.Core.Library;` if the file lacks it. `Map`
returns null for a `LibraryOpenException`, so these issues pass through the
outer catch unchanged.

- [ ] **Step 5: Run the tests to see them pass**

Run: Infrastructure tests (whole project).
Expected: all pass. In particular, `FileOperationReconciliationTests` still
sees its raw `InvalidDataException` and `IOException`.

- [ ] **Step 6: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs
git commit -m "feat(storage): stop startup with a typed library issue" -m "A missing or empty database in a used data folder is Missing and is never recreated; a write probe surfaces read-only and non-database files before migration; open and validation failures map to LibraryOpenException." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: The "Library unavailable" page

TDD skip (ruling 21): Production has no test project. The gates are the
Production build, review against Review Focus item 4, and the Task 5
installed smoke.

**Files:**
- Create: `src/DesktopGuides.Production/ShellWindow.Startup.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (a new panel in the
  `ShellContent` grid, row 1, beside `LibraryPanel` at ~88)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`
  (`InitializeCoreAsync` ~346–414, and the panel collapse block in
  `RenderCurrentAsync` at ~1600)

**Interfaces:**
- Consumes: Task 1's `LibraryOpenException`, `LibraryOpenMessages.For`,
  `DataFolder` and `RecoveryCopy`. Task 3's `InitializeAsync` behavior.
- Produces these AutomationIds, which Task 5's smoke uses:
  `LibraryUnavailable`, `LibraryUnavailableTitle`,
  `LibraryUnavailableBody`, `LibraryUnavailableDataFolder`,
  `LibraryUnavailableRecoveryPath`, `LibraryUnavailableRetry` (name
  "Try again") and `LibraryUnavailableOpenFolder` (name "Open data
  folder"). It also announces the title through the status probe.

- [ ] **Step 1: Add the panel**

In `ShellWindow.xaml`, add this inside the `ShellContent` grid as a sibling
of `LibraryPanel`, directly before it:

```xml
<local:AutomationGroup x:Name="LibraryUnavailablePanel"
                       Grid.Row="1"
                       Visibility="Collapsed"
                       AutomationProperties.AutomationId="LibraryUnavailable">
    <StackPanel Spacing="{StaticResource DesktopGuidesSpacing16}" MaxWidth="640" HorizontalAlignment="Left">
        <TextBlock x:Name="LibraryUnavailableTitle"
                   Style="{StaticResource DesktopGuidesPageTitleStyle}"
                   AutomationProperties.AutomationId="LibraryUnavailableTitle" />
        <TextBlock x:Name="LibraryUnavailableBody"
                   TextWrapping="Wrap"
                   Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                   AutomationProperties.AutomationId="LibraryUnavailableBody" />
        <TextBlock x:Name="LibraryUnavailableDataFolder"
                   TextWrapping="Wrap"
                   IsTextSelectionEnabled="True"
                   Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                   AutomationProperties.AutomationId="LibraryUnavailableDataFolder" />
        <TextBlock x:Name="LibraryUnavailableRecoveryPath"
                   TextWrapping="Wrap"
                   IsTextSelectionEnabled="True"
                   Visibility="Collapsed"
                   Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                   AutomationProperties.AutomationId="LibraryUnavailableRecoveryPath" />
        <StackPanel Orientation="Horizontal" Spacing="{StaticResource DesktopGuidesSpacing12}">
            <Button x:Name="LibraryUnavailableRetry"
                    Content="Try again"
                    Style="{StaticResource DesktopGuidesPrimaryActionButtonStyle}"
                    Click="LibraryUnavailableRetryClicked"
                    AutomationProperties.AutomationId="LibraryUnavailableRetry" />
            <Button x:Name="LibraryUnavailableOpenFolder"
                    Content="Open data folder"
                    Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"
                    Click="LibraryUnavailableOpenFolderClicked"
                    AutomationProperties.AutomationId="LibraryUnavailableOpenFolder" />
        </StackPanel>
    </StackPanel>
</local:AutomationGroup>
```

- [ ] **Step 2: Add the partial**

`src/DesktopGuides.Production/ShellWindow.Startup.cs`:

```csharp
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Windows.System;

namespace DesktopGuides.Production;

// The page shown when the library can't be opened. Nothing can write while
// it is up, because ready stays false.
public sealed partial class ShellWindow
{
    private void ShowLibraryUnavailable(LibraryOpenException error)
    {
        HideRouteProgress();
        HideStatus();
        LibraryPanel.Visibility = Visibility.Collapsed;
        GamePanel.Visibility = Visibility.Collapsed;
        ReaderPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        LibraryOpenMessage message = LibraryOpenMessages.For(error.Issue);
        LibraryUnavailableTitle.Text = message.Title;
        LibraryUnavailableBody.Text = message.Body;
        LibraryUnavailableDataFolder.Text = LibraryOpenMessages.DataFolder(dataRoot ?? "");
        LibraryUnavailableRecoveryPath.Text = error.RecoveryCopyPath is { } copy
            ? LibraryOpenMessages.RecoveryCopy(copy)
            : "";
        LibraryUnavailableRecoveryPath.Visibility = error.RecoveryCopyPath is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        LibraryUnavailablePanel.Visibility = Visibility.Visible;
        LibraryUnavailableRetry.IsEnabled = true;
        LibraryUnavailableOpenFolder.IsEnabled = dataRoot is not null;
        LibraryUnavailableRetry.Focus(FocusState.Programmatic);
        AnnounceStatus(message.Title);
    }

    // Retry runs as the initialization task, so closing waits for it.
    private async void LibraryUnavailableRetryClicked(object sender, RoutedEventArgs args)
    {
        if (ready || closeRequested || !initializationTask.IsCompleted)
        {
            return;
        }
        LibraryUnavailableRetry.IsEnabled = false;
        LibraryUnavailableOpenFolder.IsEnabled = false;
        try
        {
            initializationTask = InitializeCoreAsync();
            await initializationTask;
        }
        finally
        {
            if (!ready && !closeRequested)
            {
                LibraryUnavailableRetry.IsEnabled = true;
                LibraryUnavailableOpenFolder.IsEnabled = dataRoot is not null;
            }
        }
    }

    private async void LibraryUnavailableOpenFolderClicked(object sender, RoutedEventArgs args)
    {
        if (dataRoot is not string root)
        {
            return;
        }
        bool launched;
        try
        {
            launched = await Launcher.LaunchFolderPathAsync(root);
        }
        catch (Exception)
        {
            launched = false;
        }
        if (!launched && !closeRequested)
        {
            ShowWarningStatus("Couldn't open the data folder.");
        }
    }
}
```

- [ ] **Step 3: Make `InitializeCoreAsync` retryable**

In `ShellWindow.xaml.cs` `InitializeCoreAsync`:

1. Wrap the lease wait so a retry keeps the lease (ruling 7):

   ```csharp
   if (libraryLease is null)
   {
       ShowBusyStatus("Waiting for previous window…");
       libraryLease = await LibrarySessionLease.AcquireAsync(dataRoot, leaseWait.Token);
   }
   ```

2. Directly before `repository = new SqliteLibraryRepository(paths)`, add:

   ```csharp
   if (repository is not null)
   {
       await repository.DisposeAsync();
       repository = null;
   }
   ```

3. Directly after `await repository.InitializeAsync();`, hide the page:
   `LibraryUnavailablePanel.Visibility = Visibility.Collapsed;`

4. After the `catch (OperationCanceledException) when (closeRequested)`
   block, and before the generic catch, add:

   ```csharp
   catch (LibraryOpenException error)
   {
       ready = false;
       if (!closeRequested)
       {
           ShowLibraryUnavailable(error);
       }
   }
   ```

   The generic catch stays. A non-library failure during a retry leaves the
   page up and shows today's error status.

5. In `RenderCurrentAsync`'s collapse block (~1600), add
   `LibraryUnavailablePanel.Visibility = Visibility.Collapsed;` beside the
   other panels.

Add `using DesktopGuides.Core.Library;` if it is missing.

- [ ] **Step 4: Build**

Run: Production build.
Expected: build succeeded, 0 errors, and no new warnings in the changed files.

- [ ] **Step 5: Review against the Review Focus**

Check item 4 in code:
- `WindowClosing` sets `closeRequested` before `CloseWhenIdleAsync`, which
  awaits `initializationTask`. That is the retry's task, because the click
  handler assigns it before awaiting.
- A retry that finishes after close shows no page (`!closeRequested`), and
  its repository is disposed by `CloseWhenIdleAsync`.
- A click after close is ignored by the guard.

- [ ] **Step 6: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Production/ShellWindow.Startup.cs src/DesktopGuides.Production/ShellWindow.xaml src/DesktopGuides.Production/ShellWindow.xaml.cs
git commit -m "feat(shell): Library unavailable page with Try again" -m "A LibraryOpenException shows the issue, the data folder and any recovery copy instead of the library; Try again reuses the lease and disposes the failed repository." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: Installed smoke for a damaged and a missing library

TDD skip: harness and seed code only. The gates are the static checks
below and the installed run.

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (a new command before
  the usage check at ~836, plus one usage line)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the Mode `ValidateSet` at
  lines 3–22, plus new branches before the final `else` at ~3987)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-TxtReaderScenarios`
  ~944 and a new `Assert-LibraryRecovery`)

**Interfaces:**
- Consumes: Task 4's AutomationIds and its title announcement.
- Produces:
  - seed command `break-library <app-data-root> damaged|missing|restore|describe`,
    which prints one line `exists;length;sha256hex;sidecarCount`
  - smoke modes `library-damaged`, `library-missing` and `library-retry`
  - report keys `libraryDamaged`, `libraryDamagedRetry`, `libraryMissing`
    and `libraryMissingRetry`
  - screenshots `library-damaged.unavailable.png` and
    `library-missing.unavailable.png`

- [ ] **Step 1: Seed command**

Add before the final usage check in `Program.cs`:

```csharp
if (args.Length == 3 && args[0] == "break-library" &&
    args[2] is "damaged" or "missing" or "restore" or "describe")
{
    ManagedPathResolver fixturePaths = new(args[1]);
    string database = fixturePaths.DatabasePath;
    string backup = database + ".t15-1-backup";
    string[] sidecars = [database + "-wal", database + "-shm", database + "-journal"];
    if (args[2] is "damaged" or "missing")
    {
        if (File.Exists(backup))
        {
            throw new InvalidOperationException("The library is already broken; restore it first.");
        }
        // Folds the WAL into the file, so the backup is the whole library.
        using (SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString()))
        {
            connection.Open();
            using SqliteCommand checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            checkpoint.ExecuteNonQuery();
        }
        File.Move(database, backup);
        foreach (string sidecar in sidecars)
        {
            File.Delete(sidecar);
        }
        if (args[2] == "damaged")
        {
            File.WriteAllBytes(database, Enumerable.Repeat((byte)0x5A, 4096).ToArray());
        }
    }
    else if (args[2] == "restore")
    {
        File.Delete(database);
        foreach (string sidecar in sidecars)
        {
            File.Delete(sidecar);
        }
        File.Move(backup, database);
    }
    FileInfo file = new(database);
    string hash = file.Exists ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(database))) : "";
    Console.WriteLine(
        $"{file.Exists};{(file.Exists ? file.Length : 0)};{hash};{sidecars.Count(File.Exists)}");
    return 0;
}
```

Add `"or break-library <app-data-root> damaged|missing|restore|describe " +`
to the usage text, after the `corrupt-reader-guide` line.

- [ ] **Step 2: Smoke modes**

Add `'library-damaged', 'library-missing', 'library-retry'` to the Mode
`ValidateSet`. Add these branches to the top-level chain, before its final
`else`:

```powershell
elseif ($Mode -in @('library-damaged', 'library-missing')) {
    # T15.1: a library that can't be opened stops on its own page and
    # offers Try again and the data folder.
    $expected = if ($Mode -eq 'library-damaged') {
        @{ Title = "Library can't be read"
           Body = "Your library database can't be read. Desktop Guides stopped before changing anything. Your guide files are still in the data folder." }
    } else {
        @{ Title = 'Library database is missing'
           Body = "Your library database is missing, but your guide files are still in the data folder. Desktop Guides stopped before changing anything. Restore library.sqlite, then try again." }
    }
    [void](Wait-Status $expected.Title -AllowHidden)
    [void](Wait-VisibleById 'LibraryUnavailable')
    [void](Wait-Name 'LibraryUnavailableTitle' $expected.Title)
    [void](Wait-Name 'LibraryUnavailableBody' $expected.Body)
    $folder = Wait-VisibleById 'LibraryUnavailableDataFolder'
    if ($folder.Current.Name -notlike 'Data folder: *') {
        throw "Unexpected data folder line '$($folder.Current.Name)'."
    }
    Assert-Absent 'LibraryUnavailableRecoveryPath'
    Assert-Absent 'LibraryHeading'
    [void](Wait-EnabledById 'LibraryUnavailableRetry')
    [void](Wait-EnabledById 'LibraryUnavailableOpenFolder')
    [void](Wait-Name 'LibraryUnavailableRetry' 'Try again')
    [void](Wait-Name 'LibraryUnavailableOpenFolder' 'Open data folder')
    [void](Wait-FocusedId 'LibraryUnavailableRetry')
    $report.unavailableScreenshot = Save-WindowScreenshot 'unavailable'
    $report.phases += 'unavailable'
}
elseif ($Mode -eq 'library-retry') {
    Invoke-Element (Find-ById 'LibraryUnavailableRetry')
    [void](Wait-Status 'Library ready.')
    [void](Wait-Name 'LibraryHeading' 'Library')
    Wait-HiddenById 'LibraryUnavailable'
    [void](Wait-GameRow 'Text Reader Game')
    $report.phases += 'retried'
}
```

- [ ] **Step 3: Install wiring**

In `windows_shell_install.ps1`, add after `Assert-TxtBackDuringLoad`:

```powershell
function Assert-LibraryRecovery {
    # T15.1: the shell must leave a broken library byte-for-byte unchanged,
    # then open it after the seed restores it and Try again runs.
    foreach ($break in @('damaged', 'missing')) {
        $key = if ($break -eq 'damaged') { 'libraryDamaged' } else { 'libraryMissing' }
        $before = Invoke-ShellSeed @('break-library', $dataRoot, $break)
        try {
            Start-InstalledShell
            $report[$key] = Run-ShellSmoke "library-$break" -ResultName "library-$break"
            $after = Invoke-ShellSeed @('break-library', $dataRoot, 'describe')
            if ($after -ne $before) {
                throw "The $break library changed while the shell showed it: '$before' became '$after'."
            }
        }
        finally {
            Invoke-ShellSeed @('break-library', $dataRoot, 'restore') | Out-Null
        }
        $report["${key}Retry"] = Run-ShellSmoke 'library-retry' -ResultName "library-$break-retry"
        Close-InstalledShell
    }
}
```

In `Run-TxtReaderScenarios`, call `Assert-LibraryRecovery` after
`Assert-TxtBackDuringLoad`. The seeded TXT library has guide content, so the
missing case is a used data folder.

- [ ] **Step 4: Static checks**

Run: `perl -ne 'print "$ARGV:$.: non-ASCII\n" if /[^\x00-\x7F]/; close ARGV if eof' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1`
Expected: no output.

Run: `stage && s 'powershell -NoProfile -Command "foreach ($f in @(''E:\work\desktop-guides\t15-1\tools\p1\windows_shell_ui_smoke.ps1'',''E:\work\desktop-guides\t15-1\tools\p1\windows_shell_install.ps1'')) { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$e); if ($e) { $e; exit 1 } }; ''parsed''"'`
Expected: `parsed`.

Run: Seed build.
Expected: build succeeded, 0 errors.

- [ ] **Step 5: Commit** (show the message in chat first)

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): installed smoke for a damaged and a missing library" -m "break-library swaps the database for non-database bytes or removes it; the smoke checks the unavailable page, that the files are unchanged, and that Try again opens the restored library." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 6: Installed verification**

Push the branch (no approval needed) and let CI's `production-shell-ui` job
run. It is the gate of record. To run it on the host instead, use
`windows_shell_install.ps1 -PackagePath <msix> -ResultDirectory E:\work\desktop-guides\t15-1-results -TxtOnly`
through an interactive scheduled task, following
`docs/p1/e2e-testing.md`. Back up and restore the app data and
`%LOCALAPPDATA%\DesktopGuides\P0-WebView` as it requires.

Expected:
- `libraryDamaged.phases` and `libraryMissing.phases` contain `unavailable`.
- `libraryDamagedRetry.phases` and `libraryMissingRetry.phases` contain
  `retried`.
- No "changed while the shell showed it" error.
- Every existing TXT phase still passes.

Copy the screenshots for Task 6:

| Result file | Evidence |
|---|---|
| `library-damaged.unavailable.png` | `docs/p1/evidence/t15-1-error-recovery/library-damaged.png` |
| `library-missing.unavailable.png` | `docs/p1/evidence/t15-1-error-recovery/library-missing.png` |


### Task 6: PR a documentation and verification record

**Files:**
- Modify: `docs/p1/t15-1-error-recovery-design.md` (status line, a new verification record)
- Modify: `docs/p1/implementation-plan.md` (after the T15.4 paragraph that ends "#t154-verification-record).", ~line 975)
- Modify: `docs/progress.md` (the "Updated" line, a new T15.1 row after the T20.1 row)
- Modify: `docs/p1/e2e-testing.md` (a new `Library recovery` row after `Guide removal`, ~line 329)
- Create: `docs/p1/evidence/t15-1-error-recovery/` (the two PNGs from Task 5 Step 6)

**Interfaces:**
- Consumes:
  - the CI run ID and conclusion from Task 5 Step 6;
  - the Core and Infrastructure test totals from the last host runs;
  - the two evidence PNGs;
  - every `Ruling:` line in the executor's ledger for Tasks 1–5.

TDD skip: this task changes documentation only. The gate is the placeholder
grep, `git diff --check` and a read-through.

Fill every `<…>` below from the observed run before committing. Never
commit a placeholder.

- [ ] **Step 1: Spec**

In `t15-1-error-recovery-design.md`, replace the status paragraph's first
sentence with:
`Status: PR a implemented on \`feat/p1-t15-1-error-recovery\`; verified by CI run <run id>. PR b and PR c are planned.`
Keep the prerequisite sentence. Then append:

```markdown
## T15.1 verification record

### PR a: the library won't open

- **Unit tests.** On `pcsx2-win`, Infrastructure <n>/<n> and Core <n>/<n>
  passed. The new tests are:
  - `LibraryOpenMessagesTests`: every issue's title and body, and the shared
    "stopped before changing anything" sentence;
  - `LibraryOpenErrorsTests`: each SQLite code (including extended 261),
    `InvalidDataException`, `UnauthorizedAccessException`, a full disk, and
    the errors that stay unmapped;
  - `SqliteLibraryRepositoryTests`: non-database bytes, a deleted database
    with guide content, a 0-byte database with artwork, a `-wal` file with
    no database, an empty data folder, a read-only database, and the
    existing NewerVersion and migration-failure tests now expecting
    `LibraryOpenException`.
- **Installed.** CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>)
  passed `production-shell-ui`. In the TXT group:
  - `library-damaged` and `library-missing` showed the Library unavailable
    page with its title, body, data folder line, **Try again** and **Open
    data folder**, and the database bytes (or their absence) were unchanged;
  - after the seed restored the database, **Try again** opened the Library
    with Text Reader Game listed.
- **Rulings.** Rulings 1–24 in the [plan](t15-1-error-recovery-plan.md#rulings-against-the-spec),
  plus <the ledger rulings made during PR a, each on one line, or "none">.
- **Evidence.**
  - [Damaged library](evidence/t15-1-error-recovery/library-damaged.png)
  - [Missing library](evidence/t15-1-error-recovery/library-missing.png)
```

- [ ] **Step 2: Implementation plan and E2E catalogue**

In `docs/p1/implementation-plan.md`, after the T15.4 paragraph, add:

```markdown
T15.1 PR a is implemented on `feat/p1-t15-1-error-recovery`; see the
[design and verification record](t15-1-error-recovery-design.md#t151-verification-record).
A library that can't be opened stops on a Library unavailable page with
**Try again** and **Open data folder**. `Initialize` maps open failures to a
`LibraryOpenIssue`, never creates a database in a used data folder, and
probes for write access before migrating. CI run <run id> passed the
installed damaged and missing library runs.
```

In `docs/p1/e2e-testing.md`, after the `Guide removal` row, add:

```markdown
| Library recovery | `-TxtOnly`, after the TXT reader modes. With the shell closed, `break-library` replaces `library.sqlite` with non-database bytes (`library-damaged`) or removes it (`library-missing`). The shell shows the `LibraryUnavailable` page: its title and body for the issue, `LibraryUnavailableDataFolder`, and **Try again** focused beside **Open data folder**. The database bytes, or their absence, are unchanged afterwards. The seed restores the database, and **Try again** reads "Library ready." and lists Text Reader Game. | T15.1, TR15.1, TR11.3 |
```

- [ ] **Step 3: Progress**

In `docs/progress.md`:

- Set the "Updated" line to the commit date.
- After the T20.1 row, add:

```markdown
| P1 T15.1 error recovery | PR a implemented on `feat/p1-t15-1-error-recovery`; PR open. PR b and PR c are planned. | A library that can't be opened stops on a Library unavailable page with Try again and Open data folder, and is never replaced by an empty one. CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>) passed the installed damaged and missing library runs; see the [verification record](p1/t15-1-error-recovery-design.md#t151-verification-record). |
```

After the PR opens, change "PR open" to the linked PR number in a
follow-up commit on the same branch.

- [ ] **Step 4: Check and commit** (show the message in chat first)

```bash
grep -n '<run id>\|<n>/<n>\|<the ledger' docs/p1/t15-1-error-recovery-design.md docs/p1/implementation-plan.md docs/progress.md docs/p1/e2e-testing.md
git diff --check
```

Expected: no output from either command.

```bash
git add docs/p1/t15-1-error-recovery-design.md docs/p1/implementation-plan.md docs/progress.md docs/p1/e2e-testing.md docs/p1/evidence/t15-1-error-recovery
git commit -m "docs(p1): record T15.1 library recovery verification" \
  -m "Mark T15.1 PR a implemented and add its verification record: unit tests, CI run <run id>, rulings and two screenshots. Add the T15.1 paragraph to the implementation plan, a Library recovery row to the E2E catalogue and a T15.1 row to progress." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

# PR b: guide health and the reader

Start from `main` after PR a merges:
`git switch main && git pull && git switch -c feat/p1-t15-1-guide-health`.

### Task 7: Guide file health and its row facts

**Files:**
- Create: `src/DesktopGuides.Core/Library/GuideFileHealth.cs`
- Modify: `src/DesktopGuides.Core/Library/CatalogPresentation.cs` (`GameFacts`, `GuideFacts`)
- Test: `tests/DesktopGuides.Core.Tests/GuideFileHealthTests.cs` (new)
- Test: `tests/DesktopGuides.Core.Tests/CatalogPresentationTests.cs` (new tests at the end)

**Interfaces:**
- Consumes: `LibraryGameSummary`, `GuideSummary`, `CatalogFact` (unchanged).
- Produces:
  - `public enum GuideFileStatus { Ok, Missing, Damaged }`
  - `public sealed record MissingGuideFile(Guid GuideId, Guid GameId)`
  - `public sealed class GuideFileHealth` with
    `GuideFileStatus this[Guid guideId]`,
    `void Mark(Guid guideId, Guid gameId, GuideFileStatus status)`,
    `void Forget(Guid guideId)`, `int CountForGame(Guid gameId)` and
    `void Reset(IEnumerable<MissingGuideFile> missing)`
  - `public static class GuideFilePresentation` with
    `string StatusLabel(GuideFileStatus status)`,
    `string Attention(int count)` and `string Unreadable(int count)`
  - `CatalogPresentation.GameFacts(LibraryGameSummary summary, string? matchedGuideTitle = null, int attentionCount = 0)`
  - `CatalogPresentation.GuideFacts(GuideSummary summary, TimeProvider clock, CultureInfo culture, GuideFileStatus fileStatus = GuideFileStatus.Ok)`

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/GuideFileHealthTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideFileHealthTests
{
    private static readonly Guid GameA = Guid.NewGuid();
    private static readonly Guid GameB = Guid.NewGuid();

    [Fact]
    public void AnUnknownGuideIsOk()
    {
        Assert.Equal(GuideFileStatus.Ok, new GuideFileHealth()[Guid.NewGuid()]);
    }

    [Fact]
    public void MarkRecordsTheStatusAndCountsItForItsGame()
    {
        GuideFileHealth health = new();
        Guid missing = Guid.NewGuid();
        Guid damaged = Guid.NewGuid();
        health.Mark(missing, GameA, GuideFileStatus.Missing);
        health.Mark(damaged, GameA, GuideFileStatus.Damaged);
        health.Mark(Guid.NewGuid(), GameB, GuideFileStatus.Missing);

        Assert.Equal(GuideFileStatus.Missing, health[missing]);
        Assert.Equal(GuideFileStatus.Damaged, health[damaged]);
        Assert.Equal(2, health.CountForGame(GameA));
        Assert.Equal(1, health.CountForGame(GameB));
        Assert.Equal(0, health.CountForGame(Guid.NewGuid()));
    }

    [Fact]
    public void MarkingOkClearsTheStatus()
    {
        GuideFileHealth health = new();
        Guid guide = Guid.NewGuid();
        health.Mark(guide, GameA, GuideFileStatus.Damaged);
        health.Mark(guide, GameA, GuideFileStatus.Ok);

        Assert.Equal(GuideFileStatus.Ok, health[guide]);
        Assert.Equal(0, health.CountForGame(GameA));
    }

    [Fact]
    public void MarkingAgainReplacesTheStatus()
    {
        GuideFileHealth health = new();
        Guid guide = Guid.NewGuid();
        health.Mark(guide, GameA, GuideFileStatus.Missing);
        health.Mark(guide, GameA, GuideFileStatus.Damaged);

        Assert.Equal(GuideFileStatus.Damaged, health[guide]);
        Assert.Equal(1, health.CountForGame(GameA));
    }

    [Fact]
    public void ForgetRemovesTheGuide()
    {
        GuideFileHealth health = new();
        Guid guide = Guid.NewGuid();
        health.Mark(guide, GameA, GuideFileStatus.Missing);
        health.Forget(guide);
        health.Forget(Guid.NewGuid());

        Assert.Equal(GuideFileStatus.Ok, health[guide]);
        Assert.Equal(0, health.CountForGame(GameA));
    }

    [Fact]
    public void ResetReplacesEverythingWithTheMissingList()
    {
        GuideFileHealth health = new();
        Guid stale = Guid.NewGuid();
        Guid missing = Guid.NewGuid();
        health.Mark(stale, GameA, GuideFileStatus.Damaged);
        health.Reset([new MissingGuideFile(missing, GameB)]);

        Assert.Equal(GuideFileStatus.Ok, health[stale]);
        Assert.Equal(GuideFileStatus.Missing, health[missing]);
        Assert.Equal(0, health.CountForGame(GameA));
        Assert.Equal(1, health.CountForGame(GameB));
    }

    [Theory]
    [InlineData(GuideFileStatus.Missing, "File missing")]
    [InlineData(GuideFileStatus.Damaged, "File damaged")]
    public void StatusLabelCopy(GuideFileStatus status, string expected)
    {
        Assert.Equal(expected, GuideFilePresentation.StatusLabel(status));
    }

    [Fact]
    public void OkHasNoStatusLabel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GuideFilePresentation.StatusLabel(GuideFileStatus.Ok));
    }

    [Theory]
    [InlineData(1, "1 guide needs attention")]
    [InlineData(3, "3 guides need attention")]
    public void AttentionCopy(int count, string expected)
    {
        Assert.Equal(expected, GuideFilePresentation.Attention(count));
    }

    [Theory]
    [InlineData(1, "1 guide record couldn't be read and is hidden. Other guides open normally.")]
    [InlineData(2, "2 guide records couldn't be read and are hidden. Other guides open normally.")]
    public void UnreadableCopy(int count, string expected)
    {
        Assert.Equal(expected, GuideFilePresentation.Unreadable(count));
    }
}
```

Append to `CatalogPresentationTests` (before its closing brace):

```csharp
    [Fact]
    public void AttentionFollowsTheCountAndPrecedesTheMatchedGuide()
    {
        LibraryGameSummary summary = new(GameWith("PC"), 3, Now, []);

        Assert.Equal(
            ["PC", "Manual", "3 guides", "1 guide needs attention", "Guide: Maps"],
            Labels(CatalogPresentation.GameFacts(summary, "Maps", attentionCount: 1)));
        Assert.Equal(
            ["PC", "Manual", "3 guides", "2 guides need attention"],
            Labels(CatalogPresentation.GameFacts(summary, attentionCount: 2)));
    }

    [Fact]
    public void NoAttentionFactWhenEveryGuideIsOk()
    {
        LibraryGameSummary summary = new(GameWith("PC"), 3, Now, []);

        Assert.Equal(["PC", "Manual", "3 guides"], Labels(CatalogPresentation.GameFacts(summary, attentionCount: 0)));
    }

    [Theory]
    [InlineData(GuideFileStatus.Missing, "File missing")]
    [InlineData(GuideFileStatus.Damaged, "File damaged")]
    public void AFileStatusIsTheFirstGuideFact(GuideFileStatus status, string expected)
    {
        IReadOnlyList<CatalogFact> facts = CatalogPresentation.GuideFacts(
            new GuideSummary(GuideWith(GuideFormat.Txt), null), AtNow, EnGb, status);

        Assert.Equal([expected, "Text (TXT)", "Not started"], Labels(facts));
        Assert.Equal(expected, facts[0].AccessibleLabel);
    }

    [Fact]
    public void AnOkGuideHasNoStatusFact()
    {
        Assert.Equal(
            Labels(GuideFacts(null)),
            Labels(CatalogPresentation.GuideFacts(
                new GuideSummary(GuideWith(GuideFormat.Txt), null), AtNow, EnGb, GuideFileStatus.Ok)));
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: Core tests with `--filter "FullyQualifiedName~GuideFileHealthTests|FullyQualifiedName~CatalogPresentationTests"`.
Expected: build errors for `GuideFileHealth`, `GuideFileStatus`,
`MissingGuideFile`, `GuideFilePresentation` and the new parameters.

- [ ] **Step 3: Implement**

Create `src/DesktopGuides.Core/Library/GuideFileHealth.cs`:

```csharp
namespace DesktopGuides.Core.Library;

public enum GuideFileStatus
{
    Ok,
    Missing,
    Damaged
}

public sealed record MissingGuideFile(Guid GuideId, Guid GameId);

// What this session knows about each guide's managed file: startup reports
// the missing ones, and opening a guide marks it damaged or clears it. Each
// entry keeps its game so the Library can count guides that need attention.
public sealed class GuideFileHealth
{
    private readonly Dictionary<Guid, (Guid GameId, GuideFileStatus Status)> entries = [];

    public GuideFileStatus this[Guid guideId] =>
        entries.TryGetValue(guideId, out var entry) ? entry.Status : GuideFileStatus.Ok;

    public void Mark(Guid guideId, Guid gameId, GuideFileStatus status)
    {
        if (status == GuideFileStatus.Ok)
        {
            entries.Remove(guideId);
            return;
        }
        entries[guideId] = (gameId, status);
    }

    public void Forget(Guid guideId) => entries.Remove(guideId);

    public int CountForGame(Guid gameId) => entries.Values.Count(entry => entry.GameId == gameId);

    public void Reset(IEnumerable<MissingGuideFile> missing)
    {
        entries.Clear();
        foreach (MissingGuideFile file in missing)
        {
            entries[file.GuideId] = (file.GameId, GuideFileStatus.Missing);
        }
    }
}

public static class GuideFilePresentation
{
    public static string StatusLabel(GuideFileStatus status) => status switch
    {
        GuideFileStatus.Missing => "File missing",
        GuideFileStatus.Damaged => "File damaged",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static string Attention(int count) =>
        count == 1 ? "1 guide needs attention" : $"{count} guides need attention";

    public static string Unreadable(int count) => count == 1
        ? "1 guide record couldn't be read and is hidden. Other guides open normally."
        : $"{count} guide records couldn't be read and are hidden. Other guides open normally.";
}
```

In `CatalogPresentation.GameFacts`, add the parameter and the fact:

```csharp
    public static IReadOnlyList<CatalogFact> GameFacts(
        LibraryGameSummary summary, string? matchedGuideTitle = null, int attentionCount = 0)
    {
        // ...platform, source and count unchanged...
        if (attentionCount > 0)
        {
            labels.Add(GuideFilePresentation.Attention(attentionCount));
        }
        // A search that matched only a guide names it, so the row explains itself.
        if (matchedGuideTitle is not null)
```

In `CatalogPresentation.GuideFacts`:

```csharp
    public static IReadOnlyList<CatalogFact> GuideFacts(
        GuideSummary summary, TimeProvider clock, CultureInfo culture,
        GuideFileStatus fileStatus = GuideFileStatus.Ok)
    {
        string format = ImportPresentation.FormatLabel(summary.Guide.Format);
        List<CatalogFact> facts = [];
        // A broken file leads the row, before the facts that assume it opens.
        if (fileStatus != GuideFileStatus.Ok)
        {
            string status = GuideFilePresentation.StatusLabel(fileStatus);
            facts.Add(new(status, status));
        }
        facts.Add(new(format, format));
        facts.Add(ReadingStateFact(summary.State));
        if (summary.State?.LastOpenedUtc is DateTimeOffset opened)
        {
            facts.Add(OpenedFact(opened, clock, culture));
        }
        return facts;
    }
```

- [ ] **Step 4: Run the tests to see them pass**

Run: Core tests with `--filter "FullyQualifiedName~GuideFileHealthTests|FullyQualifiedName~CatalogPresentationTests"`.
Expected: 18 new tests pass (13 in `GuideFileHealthTests`, 5 new in
`CatalogPresentationTests`, counting each InlineData) and every existing
`CatalogPresentationTests` test still passes.

Run: Core tests.
Expected: 0 failed.

- [ ] **Step 5: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Core/Library/GuideFileHealth.cs src/DesktopGuides.Core/Library/CatalogPresentation.cs tests/DesktopGuides.Core.Tests/GuideFileHealthTests.cs tests/DesktopGuides.Core.Tests/CatalogPresentationTests.cs
git commit -m "feat(core): guide file health and its row facts" -m "GuideFileHealth tracks missing and damaged guide files per game. Guide rows lead with File missing or File damaged, and game rows count the guides that need attention." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 8: A shared `GuideLoadAction` with Remove, and the new copy

**Files:**
- Create: `src/DesktopGuides.Core/Reading/GuideLoadAction.cs`
- Modify: `src/DesktopGuides.Core/Html/HtmlGuideLoadMessages.cs` (drop the enum; copy, `ActionFor`, `ActionLabel`, a new `StatusFor`)
- Modify: `src/DesktopGuides.Core/Pdf/PdfGuideLoadMessages.cs` (copy, `ActionFor`, `ActionLabel`, a new `StatusFor`)
- Modify: `src/DesktopGuides.Core/Text/TextGuideLoadMessages.cs` (copy, new `ActionFor` and `StatusFor`)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (lines 57, 1377, 1383, 1385, 1388: the type name only)
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs` (lines 193, 196: the type name only)
- Modify: `src/DesktopGuides.Core/Library/GuideRemovalPresentation.cs` (`DialogBody`, ruling 24)
- Test: `tests/DesktopGuides.Core.Tests/HtmlGuideLoadMessagesTests.cs`, `PdfGuideLoadMessagesTests.cs`, `TextGuideLoadMessagesTests.cs`, `GuideRemovalPresentationTests.cs`

**Interfaces:**
- Consumes: `GuideFileStatus` (Task 7).
- Produces:
  - `namespace DesktopGuides.Core.Reading; public enum GuideLoadAction { None, GetRuntime, Reopen, Remove }`
  - `HtmlGuideLoadMessages.ActionFor(HtmlGuideLoadError) → GuideLoadAction` and
    `HtmlGuideLoadMessages.ActionLabel(GuideLoadAction) → string`
    (Remove → "Remove guide")
  - `PdfGuideLoadMessages.ActionFor(PdfGuideLoadError) → GuideLoadAction` and
    `PdfGuideLoadMessages.ActionLabel(GuideLoadAction)`
  - `TextGuideLoadMessages.ActionFor(TextGuideLoadError) → GuideLoadAction`
  - `HtmlGuideLoadMessages.StatusFor`, `PdfGuideLoadMessages.StatusFor` and
    `TextGuideLoadMessages.StatusFor`, each `→ GuideFileStatus?`: Missing →
    `Missing`; every other error that offers Remove → `Damaged`; anything
    else → `null` (no change)

No other file references `HtmlGuideLoadAction`
(`grep -rn HtmlGuideLoadAction src tests tools` lists only the files above).
The enum keeps its first three values in order, so no stored or logged
number changes.

- [ ] **Step 1: Update the tests**

In all three test files, add `using DesktopGuides.Core.Library;` and
`using DesktopGuides.Core.Reading;`, and replace every `HtmlGuideLoadAction`
with `GuideLoadAction`. Then change these cases.

`HtmlGuideLoadMessagesTests`:

```csharp
    [InlineData(HtmlGuideLoadError.Missing, "This guide's file is missing from the library. Remove it, then import the original again.")]
    [InlineData(HtmlGuideLoadError.NoManifest, "Re-import this guide to read it.")]
    [InlineData(HtmlGuideLoadError.Changed, "This guide's file changed after it was imported, so it can't be opened safely. Remove it, then import the original again.")]
```

```csharp
    [InlineData(HtmlGuideLoadError.Missing, GuideLoadAction.Remove)]
    [InlineData(HtmlGuideLoadError.NoManifest, GuideLoadAction.Remove)]
    [InlineData(HtmlGuideLoadError.Changed, GuideLoadAction.Remove)]
```

```csharp
    [InlineData(GuideLoadAction.GetRuntime, "Get WebView2 Runtime")]
    [InlineData(GuideLoadAction.Reopen, "Reopen")]
    [InlineData(GuideLoadAction.Remove, "Remove guide")]
    public void EachActionHasALabel(GuideLoadAction action, string label) =>
```

Add:

```csharp
    [Theory]
    [InlineData(HtmlGuideLoadError.Missing, GuideFileStatus.Missing)]
    [InlineData(HtmlGuideLoadError.NoManifest, GuideFileStatus.Damaged)]
    [InlineData(HtmlGuideLoadError.Changed, GuideFileStatus.Damaged)]
    [InlineData(HtmlGuideLoadError.RuntimeMissing, null)]
    [InlineData(HtmlGuideLoadError.RuntimeFailed, null)]
    [InlineData(HtmlGuideLoadError.Crashed, null)]
    public void EachErrorMarksTheGuideOrNot(HtmlGuideLoadError error, GuideFileStatus? status) =>
        Assert.Equal(status, HtmlGuideLoadMessages.StatusFor(error));
```

and add `HtmlGuideLoadMessages.StatusFor((HtmlGuideLoadError)99)` to
`UndefinedErrorsThrow`.

`PdfGuideLoadMessagesTests`:

```csharp
    [InlineData(PdfGuideLoadError.Missing, "This guide's file is missing from the library. Remove it, then import the original again.")]
    [InlineData(PdfGuideLoadError.Changed, "This guide's file changed after it was imported, so it can't be opened safely. Remove it, then import the original again.")]
```

```csharp
    [InlineData(PdfGuideLoadError.Missing, GuideLoadAction.Remove)]
    [InlineData(PdfGuideLoadError.Changed, GuideLoadAction.Remove)]
    [InlineData(PdfGuideLoadError.Damaged, GuideLoadAction.Remove)]
```

Add:

```csharp
    [Theory]
    [InlineData(PdfGuideLoadError.Missing, GuideFileStatus.Missing)]
    [InlineData(PdfGuideLoadError.Changed, GuideFileStatus.Damaged)]
    [InlineData(PdfGuideLoadError.Damaged, GuideFileStatus.Damaged)]
    [InlineData(PdfGuideLoadError.Unreadable, null)]
    [InlineData(PdfGuideLoadError.PasswordProtected, null)]
    [InlineData(PdfGuideLoadError.Failed, null)]
    [InlineData(PdfGuideLoadError.PasswordRequired, null)]
    [InlineData(PdfGuideLoadError.PasswordIncorrect, null)]
    public void EachErrorMarksTheGuideOrNot(PdfGuideLoadError error, GuideFileStatus? status) =>
        Assert.Equal(status, PdfGuideLoadMessages.StatusFor(error));

    [Fact]
    public void ChangedMatchesTheHtmlCopy() =>
        Assert.Equal(HtmlGuideLoadMessages.For(HtmlGuideLoadError.Changed),
            PdfGuideLoadMessages.For(PdfGuideLoadError.Changed));
```

Rename `ReopenHasALabelAndNoneHasNone` to
`ActionLabelsMatchTheHtmlLabels` with this body:

```csharp
    [Fact]
    public void ActionLabelsMatchTheHtmlLabels()
    {
        Assert.Equal("Reopen", PdfGuideLoadMessages.ActionLabel(GuideLoadAction.Reopen));
        Assert.Equal("Remove guide", PdfGuideLoadMessages.ActionLabel(GuideLoadAction.Remove));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfGuideLoadMessages.ActionLabel(GuideLoadAction.None));
    }
```

Add `PdfGuideLoadMessages.StatusFor((PdfGuideLoadError)99)` to
`UnknownErrorsThrow`, and `_ = PdfGuideLoadMessages.StatusFor(error);` to
`EveryErrorIsCovered`.

`TextGuideLoadMessagesTests`:

```csharp
    [InlineData(TextGuideLoadError.Missing, "This guide's file is missing from the library. Remove it, then import the original again.")]
```

Add:

```csharp
    [Theory]
    [InlineData(TextGuideLoadError.Missing, GuideLoadAction.Remove, GuideFileStatus.Missing)]
    [InlineData(TextGuideLoadError.InvalidMetadata, GuideLoadAction.Remove, GuideFileStatus.Damaged)]
    [InlineData(TextGuideLoadError.TooLarge, GuideLoadAction.None, null)]
    [InlineData(TextGuideLoadError.Unreadable, GuideLoadAction.None, null)]
    [InlineData(TextGuideLoadError.NotUtf8, GuideLoadAction.None, null)]
    [InlineData(TextGuideLoadError.Undecodable, GuideLoadAction.None, null)]
    public void EachErrorHasItsActionAndStatus(
        TextGuideLoadError error, GuideLoadAction action, GuideFileStatus? status)
    {
        Assert.Equal(action, TextGuideLoadMessages.ActionFor(error));
        Assert.Equal(status, TextGuideLoadMessages.StatusFor(error));
    }
```

and in `AnUnknownErrorThrows` add:

```csharp
        Assert.Throws<ArgumentOutOfRangeException>(() => TextGuideLoadMessages.ActionFor((TextGuideLoadError)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextGuideLoadMessages.StatusFor((TextGuideLoadError)99));
```

The existing `MissingMatchesTheTextGuideCopy` and
`MissingAndUnreadableUseTheTextWording` stay as they are and must still pass.

`GuideRemovalPresentationTests` (ruling 24): delete the
`[InlineData(0, "0 managed files")]` line and add:

```csharp
    [Fact]
    public void DialogBodyWithNoFilesLeftSkipsTheCount() =>
        Assert.Equal(
            "This removes the guide and its reading progress from Desktop Guides. The original file you imported isn't affected.",
            GuideRemovalPresentation.DialogBody(0));
```

- [ ] **Step 2: Run the tests to see them fail**

Run: Core tests with `--filter "FullyQualifiedName~GuideLoadMessagesTests"`.
Expected: build fails with CS0246 for `GuideLoadAction` and CS0117 for
`StatusFor` and `TextGuideLoadMessages.ActionFor`. (The build failure also
keeps `DialogBodyWithNoFilesLeftSkipsTheCount` from running; Step 4 runs it.)

- [ ] **Step 3: Implement**

Create `src/DesktopGuides.Core/Reading/GuideLoadAction.cs`:

```csharp
namespace DesktopGuides.Core.Reading;

// The one thing the Reader offers to do about an error, for every format.
// New members go at the end, so existing values keep their numbers.
public enum GuideLoadAction { None, GetRuntime, Reopen, Remove }
```

In `HtmlGuideLoadMessages.cs`, delete the `HtmlGuideLoadAction` enum and its
comment, add `using DesktopGuides.Core.Library;` and
`using DesktopGuides.Core.Reading;`, and change:

```csharp
        HtmlGuideLoadError.Missing =>
            "This guide's file is missing from the library. Remove it, then import the original again.",
        HtmlGuideLoadError.NoManifest => "Re-import this guide to read it.",
        HtmlGuideLoadError.Changed =>
            "This guide's file changed after it was imported, so it can't be opened safely. Remove it, then import the original again.",
```

```csharp
    public static GuideLoadAction ActionFor(HtmlGuideLoadError error) => error switch
    {
        HtmlGuideLoadError.RuntimeMissing => GuideLoadAction.GetRuntime,
        HtmlGuideLoadError.RuntimeFailed or HtmlGuideLoadError.Crashed => GuideLoadAction.Reopen,
        HtmlGuideLoadError.Missing or HtmlGuideLoadError.NoManifest or HtmlGuideLoadError.Changed =>
            GuideLoadAction.Remove,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    // What a failed open says about the guide's file; null leaves it as it was.
    public static GuideFileStatus? StatusFor(HtmlGuideLoadError error) => error switch
    {
        HtmlGuideLoadError.Missing => GuideFileStatus.Missing,
        HtmlGuideLoadError.NoManifest or HtmlGuideLoadError.Changed => GuideFileStatus.Damaged,
        HtmlGuideLoadError.RuntimeMissing or HtmlGuideLoadError.RuntimeFailed or HtmlGuideLoadError.Crashed => null,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static string ActionLabel(GuideLoadAction action) => action switch
    {
        GuideLoadAction.GetRuntime => "Get WebView2 Runtime",
        GuideLoadAction.Reopen => "Reopen",
        GuideLoadAction.Remove => "Remove guide",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
```

In `PdfGuideLoadMessages.cs`, add `using DesktopGuides.Core.Library;` and
`using DesktopGuides.Core.Reading;`, replace the comment above the class with
`// Action labels come from HtmlGuideLoadMessages so every format reads alike.`,
and change:

```csharp
        PdfGuideLoadError.Missing =>
            "This guide's file is missing from the library. Remove it, then import the original again.",
        PdfGuideLoadError.Changed =>
            "This guide's file changed after it was imported, so it can't be opened safely. Remove it, then import the original again.",
```

```csharp
    public static GuideLoadAction ActionFor(PdfGuideLoadError error) => error switch
    {
        PdfGuideLoadError.Unreadable or PdfGuideLoadError.Failed => GuideLoadAction.Reopen,
        PdfGuideLoadError.Missing or PdfGuideLoadError.Changed or PdfGuideLoadError.Damaged =>
            GuideLoadAction.Remove,
        PdfGuideLoadError.PasswordProtected or PdfGuideLoadError.PasswordRequired or
            PdfGuideLoadError.PasswordIncorrect => GuideLoadAction.None,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    // What a failed open says about the guide's file; null leaves it as it was.
    public static GuideFileStatus? StatusFor(PdfGuideLoadError error) => error switch
    {
        PdfGuideLoadError.Missing => GuideFileStatus.Missing,
        PdfGuideLoadError.Changed or PdfGuideLoadError.Damaged => GuideFileStatus.Damaged,
        PdfGuideLoadError.Unreadable or PdfGuideLoadError.PasswordProtected or PdfGuideLoadError.Failed or
            PdfGuideLoadError.PasswordRequired or PdfGuideLoadError.PasswordIncorrect => null,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static string ActionLabel(GuideLoadAction action) => HtmlGuideLoadMessages.ActionLabel(action);
```

Remove `using DesktopGuides.Core.Html;` only if nothing else uses it (it
still does: `ActionLabel` delegates to `HtmlGuideLoadMessages`).

In `TextGuideLoadMessages.cs`, add `using DesktopGuides.Core.Library;` and
`using DesktopGuides.Core.Reading;`, change the Missing line to:

```csharp
        TextGuideLoadError.Missing =>
            "This guide's file is missing from the library. Remove it, then import the original again.",
```

and add:

```csharp
    public static GuideLoadAction ActionFor(TextGuideLoadError error) => error switch
    {
        TextGuideLoadError.Missing or TextGuideLoadError.InvalidMetadata => GuideLoadAction.Remove,
        TextGuideLoadError.TooLarge or TextGuideLoadError.Unreadable or TextGuideLoadError.NotUtf8 or
            TextGuideLoadError.Undecodable => GuideLoadAction.None,
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };

    // What a failed open says about the guide's file; null leaves it as it was.
    public static GuideFileStatus? StatusFor(TextGuideLoadError error) => error switch
    {
        TextGuideLoadError.Missing => GuideFileStatus.Missing,
        TextGuideLoadError.InvalidMetadata => GuideFileStatus.Damaged,
        TextGuideLoadError.TooLarge or TextGuideLoadError.Unreadable or TextGuideLoadError.NotUtf8 or
            TextGuideLoadError.Undecodable => null,
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };
```

In `GuideRemovalPresentation.DialogBody`, return early for zero files:

```csharp
    public static string DialogBody(int fileCount)
    {
        // A guide whose file is missing has nothing left to count.
        if (fileCount == 0)
        {
            return "This removes the guide and its reading progress from Desktop Guides. " +
                "The original file you imported isn't affected.";
        }
        string files = fileCount == 1 ? "1 managed file" : $"{fileCount} managed files";
```

In Production, replace `HtmlGuideLoadAction` with `GuideLoadAction` at the
seven sites listed under Files, and add `using DesktopGuides.Core.Reading;`
to both files if it isn't there. Change nothing else in Production in this
task. The reader can't yet act on Remove (Task 10 adds the handler), and
until then `ShowReaderSurface` would show a button that does nothing. So
Task 10 lands in the same PR before any installed run, and no smoke runs
between Tasks 8 and 10.

- [ ] **Step 4: Run the tests to see them pass**

Run: Core tests with `--filter "FullyQualifiedName~GuideLoadMessagesTests|FullyQualifiedName~GuideRemovalPresentationTests"`.
Expected: 0 failed. Every new InlineData case runs, and
`DialogBodyWithNoFilesLeftSkipsTheCount` passes.

Run: Core tests.
Expected: 0 failed.

Run: Production build.
Expected: build succeeded, 0 errors.

Run: `grep -rn HtmlGuideLoadAction src tests tools`
Expected: no output.

- [ ] **Step 5: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Core/Reading/GuideLoadAction.cs src/DesktopGuides.Core/Html/HtmlGuideLoadMessages.cs src/DesktopGuides.Core/Pdf/PdfGuideLoadMessages.cs src/DesktopGuides.Core/Text/TextGuideLoadMessages.cs src/DesktopGuides.Core/Library/GuideRemovalPresentation.cs src/DesktopGuides.Production/ShellWindow.xaml.cs src/DesktopGuides.Production/ShellWindow.HtmlReader.cs tests/DesktopGuides.Core.Tests/HtmlGuideLoadMessagesTests.cs tests/DesktopGuides.Core.Tests/PdfGuideLoadMessagesTests.cs tests/DesktopGuides.Core.Tests/TextGuideLoadMessagesTests.cs tests/DesktopGuides.Core.Tests/GuideRemovalPresentationTests.cs
git commit -m "feat(core): offer Remove guide for missing and damaged guide files" -m "HtmlGuideLoadAction becomes the shared GuideLoadAction with a Remove member. Missing and changed guides say to remove and import again, and each format's StatusFor says whether a failed open marks the file missing or damaged. A removal dialog for a guide with no files left no longer counts zero files." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 9: Startup reports missing guide files and hides unreadable rows

**Files:**
- Modify: `src/DesktopGuides.Core/Library/LibraryModels.cs:65-68` (`StartupReconciliationReport`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (`ListGuidesAsync` ~250, `ListGuideSummariesAsync` ~339, `Initialize`, a new `TryReadGuide` and `ScanGuideFiles`)
- Test: `tests/DesktopGuides.Core.Tests/StartupReconciliationReportTests.cs` (new)
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs` (three new tests, one helper)
- Test: `tests/DesktopGuides.Infrastructure.Tests/FileOperationReconciliationTests.cs` (only the whole-report assertions that Step 5 finds)

**Interfaces:**
- Consumes: `MissingGuideFile` (Task 7); `ILibraryPaths.GetPlannedGuideFile(Guid guideId, string relativePath)`; Task 3's `Initialize`.
- Produces:
  - `StartupReconciliationReport(int ResolvedOperationCount, int ReviewOrphanCount, int ArtworkReviewCount = 0, IReadOnlyList<MissingGuideFile>? MissingGuides = null, int UnreadableGuideCount = 0)`.
    Its `MissingGuides` property is never null, and equality compares the
    list by value (Ruling 22).
  - `ListGuidesAsync` and `ListGuideSummariesAsync` skip a row that
    `ReadGuide` rejects. `GetGuideAsync` still throws for that row.

- [ ] **Step 1: Write the failing Core test**

Create `tests/DesktopGuides.Core.Tests/StartupReconciliationReportTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class StartupReconciliationReportTests
{
    private static readonly Guid Guide = Guid.NewGuid();
    private static readonly Guid Game = Guid.NewGuid();

    [Fact]
    public void NoMissingGuidesIsAnEmptyList()
    {
        StartupReconciliationReport report = new(0, 0);

        Assert.Empty(report.MissingGuides);
        Assert.Equal(report, new StartupReconciliationReport(0, 0, MissingGuides: []));
    }

    [Fact]
    public void ReportsWithTheSameMissingGuidesAreEqual()
    {
        StartupReconciliationReport first = new(1, 2, 3, [new MissingGuideFile(Guide, Game)], 4);
        StartupReconciliationReport second = new(1, 2, 3, [new MissingGuideFile(Guide, Game)], 4);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ADifferentMissingGuideOrUnreadableCountIsNotEqual()
    {
        StartupReconciliationReport report = new(0, 0, MissingGuides: [new MissingGuideFile(Guide, Game)]);

        Assert.NotEqual(report, new StartupReconciliationReport(0, 0));
        Assert.NotEqual(report, report with { MissingGuides = [new MissingGuideFile(Guid.NewGuid(), Game)] });
        Assert.NotEqual(report, report with { UnreadableGuideCount = 1 });
    }
}
```

- [ ] **Step 2: Write the failing Infrastructure tests**

Add this helper beside `InsertGuide` in `SqliteLibraryRepositoryTests`:

```csharp
    // InsertGuide's PrimaryRelativePath is guide.txt.
    private static void WriteGuideFile(ManagedPathResolver paths, Guid guideId)
    {
        string file = paths.GetPlannedGuideFile(guideId, "guide.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "guide");
    }
```

Add these tests:

```csharp
    [Fact]
    public async Task StartupReportsGuidesWhoseFileIsMissing()
    {
        using TestLibrary directory = new();
        Guid present = Guid.NewGuid();
        Guid missing = Guid.NewGuid();
        Game game;
        await using (SqliteLibraryRepository first = new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await first.InitializeAsync();
            game = await first.AddGameAsync("Health", null, null);
        }
        InsertGuide(directory.Paths.DatabasePath, present, game.Id);
        InsertGuide(directory.Paths.DatabasePath, missing, game.Id);
        WriteGuideFile(directory.Paths, present);

        await using SqliteLibraryRepository repository = new(directory.Paths, new FixedTimeProvider(Now));
        await repository.InitializeAsync();

        StartupReconciliationReport report = Assert.IsType<StartupReconciliationReport>(
            repository.LastStartupReconciliation);
        Assert.Equal([new MissingGuideFile(missing, game.Id)], report.MissingGuides);
        Assert.Equal(0, report.UnreadableGuideCount);
        Assert.Equal(2, (await repository.ListGuidesAsync(game.Id)).Count);
    }

    // A folder where the file should be isn't missing: the reader reports it
    // as changed when the guide is opened.
    [Fact]
    public async Task AFolderAtTheGuidePathIsNotMissing()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        Game game;
        await using (SqliteLibraryRepository first = new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await first.InitializeAsync();
            game = await first.AddGameAsync("Health", null, null);
        }
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);
        Directory.CreateDirectory(directory.Paths.GetPlannedGuideFile(guide, "guide.txt"));

        await using SqliteLibraryRepository repository = new(directory.Paths, new FixedTimeProvider(Now));
        await repository.InitializeAsync();

        Assert.Empty(repository.LastStartupReconciliation!.MissingGuides);
    }

    // Ruling 17: integrity_check validates CHECK constraints, so the fixture
    // breaks a value the schema allows but ReadGuide can't convert.
    [Fact]
    public async Task UnreadableGuideRowIsHiddenAndCounted()
    {
        using TestLibrary directory = new();
        Guid good = Guid.NewGuid();
        Guid bad = Guid.NewGuid();
        Game game;
        await using (SqliteLibraryRepository first = new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await first.InitializeAsync();
            game = await first.AddGameAsync("Health", null, null);
        }
        InsertGuide(directory.Paths.DatabasePath, good, game.Id);
        InsertGuide(directory.Paths.DatabasePath, bad, game.Id);
        WriteGuideFile(directory.Paths, good);
        WriteGuideFile(directory.Paths, bad);
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Guides SET UpdatedUtcMs = 9223372036854775807 WHERE Id = $id";
            command.Parameters.AddWithValue("$id", bad.ToString("N"));
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        await using SqliteLibraryRepository repository = new(directory.Paths, new FixedTimeProvider(Now));
        await repository.InitializeAsync();

        StartupReconciliationReport report = repository.LastStartupReconciliation!;
        Assert.Equal(1, report.UnreadableGuideCount);
        Assert.Empty(report.MissingGuides);
        Assert.Equal(good, Assert.Single(await repository.ListGuidesAsync(game.Id)).Id);
        Assert.Equal(good, Assert.Single(await repository.ListGuideSummariesAsync(game.Id)).Guide.Id);
        // Ruling 14: the game's count still includes the hidden row.
        Assert.Equal(2, Assert.Single(await repository.ListGameSummariesAsync()).GuideCount);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.GetGuideAsync(bad));
    }
```

- [ ] **Step 3: Run the tests to see them fail**

Run: Core tests with `--filter "FullyQualifiedName~StartupReconciliationReportTests"`.
Expected: build fails with CS1739 for the `MissingGuides` argument and
CS1061 for `MissingGuides` and `UnreadableGuideCount`.

Run: Infrastructure tests with `--filter "FullyQualifiedName~StartupReportsGuidesWhoseFileIsMissing|FullyQualifiedName~AFolderAtTheGuidePathIsNotMissing|FullyQualifiedName~UnreadableGuideRowIsHiddenAndCounted"`.
Expected: build fails for the same members.

- [ ] **Step 4: Implement**

In `LibraryModels.cs`, replace the record:

```csharp
// MissingGuides compares by value, so reports stay comparable in tests.
public sealed record StartupReconciliationReport(
    int ResolvedOperationCount,
    int ReviewOrphanCount,
    int ArtworkReviewCount = 0,
    IReadOnlyList<MissingGuideFile>? MissingGuides = null,
    int UnreadableGuideCount = 0)
{
    public IReadOnlyList<MissingGuideFile> MissingGuides { get; init; } = MissingGuides ?? [];

    public bool Equals(StartupReconciliationReport? other) =>
        other is not null &&
        ResolvedOperationCount == other.ResolvedOperationCount &&
        ReviewOrphanCount == other.ReviewOrphanCount &&
        ArtworkReviewCount == other.ArtworkReviewCount &&
        UnreadableGuideCount == other.UnreadableGuideCount &&
        MissingGuides.SequenceEqual(other.MissingGuides);

    public override int GetHashCode() => HashCode.Combine(
        ResolvedOperationCount, ReviewOrphanCount, ArtworkReviewCount,
        UnreadableGuideCount, MissingGuides.Count);
}
```

In `SqliteLibraryRepository.cs`, add beside `ReadGuide`:

```csharp
    // A row an external edit broke is skipped by the lists and counted at
    // startup; GetGuide still throws, so the Reader route reports it.
    private static Guide? TryReadGuide(SqliteDataReader reader)
    {
        try
        {
            return ReadGuide(reader);
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or
                                          ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    // Reads every guide row once. Only an absent entry path is missing: a
    // folder, a link or an unsafe path is left for the reader to report.
    // Nothing is hashed or followed.
    private (List<MissingGuideFile> Missing, int Unreadable) ScanGuideFiles(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, GameId, Title, Format, ManagedRelativeRoot,
                   PrimaryRelativePath, ContentSha256, ContentBytes,
                   SourceLabel, TextCodePage, ImportedUtcMs, UpdatedUtcMs
            FROM Guides ORDER BY Id
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        List<MissingGuideFile> missing = [];
        int unreadable = 0;
        while (reader.Read())
        {
            if (TryReadGuide(reader) is not Guide guide)
            {
                unreadable++;
                continue;
            }
            try
            {
                string planned = paths.GetPlannedGuideFile(guide.Id, guide.PrimaryRelativePath);
                if (!File.Exists(planned) && !Directory.Exists(planned))
                {
                    missing.Add(new MissingGuideFile(guide.Id, guide.GameId));
                }
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or
                                              IOException or UnauthorizedAccessException)
            {
            }
        }
        return (missing, unreadable);
    }
```

In `ListGuidesAsync`, replace `guides.Add(ReadGuide(reader));` with:

```csharp
                if (TryReadGuide(reader) is Guide guide)
                {
                    guides.Add(guide);
                }
```

In `ListGuideSummariesAsync`, replace the `summaries.Add(...)` statement with:

```csharp
                if (TryReadGuide(reader) is Guide guide)
                {
                    summaries.Add(new GuideSummary(
                        guide, reader.IsDBNull(12) ? null : ReadReadingState(reader, 12)));
                }
```

In Task 3's `Initialize`, change the `using (connection)` block to:

```csharp
    using (connection)
    {
        StartupReconciliationReport report = new FileOperationReconciler(paths).Run(connection);
        int artworkReview = new ManagedArtworkStore(paths).Sweep(ReadArtworkReferences(connection));
        (List<MissingGuideFile> missing, int unreadable) = ScanGuideFiles(connection);
        LastStartupReconciliation = report with
        {
            ArtworkReviewCount = artworkReview,
            MissingGuides = missing,
            UnreadableGuideCount = unreadable
        };
    }
```

The scan runs after reconciliation, so a guide whose import was just rolled
back isn't reported, and a guide whose deletion was just finished is gone.

- [ ] **Step 5: Run the tests to see them pass**

Run: Core tests with `--filter "FullyQualifiedName~StartupReconciliationReportTests"`.
Expected: 3 passed, 0 failed.

Run: Infrastructure tests with the Step 3 filter.
Expected: 3 passed, 0 failed.

Run: Infrastructure tests.
Expected: 0 failed. If a whole-report `Assert.Equal` fails only because
the actual report lists a guide that the test seeded without a file (for
example `PreparedImportRetainsCommittedGuideWithUppercaseDatabaseId`, which
writes a marker but no `guide.txt`), add that guide to the expected report,
`new StartupReconciliationReport(0, 0, MissingGuides: [new(guideId, game.Id)])`.
Don't add a file: it would change what the test seeds. Any other failure is
a regression; use systematic debugging.

Run: Core tests.
Expected: 0 failed.

- [ ] **Step 6: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Core/Library/LibraryModels.cs src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Core.Tests/StartupReconciliationReportTests.cs tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs tests/DesktopGuides.Infrastructure.Tests/FileOperationReconciliationTests.cs
git commit -m "feat(storage): report missing guide files and hide unreadable rows" -m "After startup reconciliation, Initialize lists guides whose managed entry file is absent and counts guide rows it can't read. The guide lists skip those rows, while GetGuideAsync still throws for them." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

If Step 5 changed no assertion in `FileOperationReconciliationTests.cs`,
leave it out of `git add`.

### Task 10: The shell marks broken guides and removes them from the reader

TDD skip (ruling 21): Production has no test project. The gates are the
Production build, review against Review Focus items 1 and 5, and the Task 11
installed smoke.

**Files:**
- Modify: `src/DesktopGuides.Production/GuideRowItem.cs`
- Modify: `src/DesktopGuides.Production/LibraryGameItem.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (fields ~57,
  `InitializeCoreAsync` after `InitializeAsync` and after the material
  warning, `ShowReaderSurface` ~1375, Library Resume ~1634, Game rows ~1702,
  the TXT failure and success ~1858–1890, `ApplyLibrarySearch` ~1966)
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs`
  (`OpenHtmlGuideAsync`, `ShowHtmlLoadError` ~87,
  `ReaderLoadErrorActionClicked` ~188)
- Modify: `src/DesktopGuides.Production/ShellWindow.PdfReader.cs`
  (`OpenPdfGuideAsync`, `ShowPdfLoadError` ~128)

**Interfaces:**
- Consumes:
  - Task 7: `GuideFileHealth`, `GuideFileStatus`,
    `GuideFilePresentation.Unreadable`, and the new `GameFacts` and
    `GuideFacts` parameters.
  - Task 8: `GuideLoadAction`, the three `StatusFor` methods,
    `TextGuideLoadMessages.ActionFor`, and `HtmlGuideLoadMessages.ActionLabel`
    with Remove.
  - Task 9: `LastStartupReconciliation.MissingGuides` and
    `UnreadableGuideCount`.
  - T15.3: `GuideRemover.DescribeAsync` and `RemoveAsync`,
    `RemoveGuideDialog.Create`, `GuideRemovalPresentation`, and
    `ShowRemovalError`.
- Produces, for Task 11's smoke:
  - The reader's `ReaderLoadErrorAction` button named "Remove guide" for
    each error that offers Remove. Choosing it opens the same Remove guide
    dialog as the Game page.
  - A broken guide's row whose first fact is "File missing" or "File
    damaged".
  - A game row with an "{n} guide(s) need(s) attention" fact.
  - The startup status for unreadable rows.

- [ ] **Step 1: Rows show a guide's file status**

`GuideRowItem.cs`:

```csharp
    internal GuideRowItem(
        GuideSummary summary, TimeProvider clock, CultureInfo culture,
        GuideFileStatus fileStatus = GuideFileStatus.Ok)
        : base(
            summary.Guide.Title,
            fileStatus == GuideFileStatus.Ok ? FormatGlyph(summary.Guide.Format) : "\uE7BA", // Warning
            CatalogPresentation.GuideFacts(summary, clock, culture, fileStatus))
```

`LibraryGameItem.cs`:

```csharp
    internal LibraryGameItem(LibrarySearchMatch match, int attentionCount = 0)
        : base(match.Summary.Game.Title, "\uE7FC",
            CatalogPresentation.GameFacts(match.Summary, match.MatchedGuideTitle, attentionCount))
```

In `ShellWindow.xaml.cs`, add a field beside `readerErrorGeneration`:

```csharp
    // Startup reports missing guide files; opening a guide updates its entry.
    private readonly GuideFileHealth guideHealth = new();
```

Then pass the status at both construction sites:

```csharp
                        .Select(summary => new GuideRowItem(
                            summary, TimeProvider.System, CultureInfo.CurrentCulture,
                            guideHealth[summary.Guide.Id]))
```

```csharp
        GameList.ItemsSource = matches
            .Select(match => new LibraryGameItem(match, guideHealth.CountForGame(match.Summary.Game.Id)))
            .ToList();
```

- [ ] **Step 2: Seed health at startup and report unreadable rows**

In `InitializeCoreAsync`, directly after
`LibraryUnavailablePanel.Visibility = Visibility.Collapsed;` (added by
Task 4 after `await repository.InitializeAsync();`):

```csharp
            StartupReconciliationReport? startup = repository.LastStartupReconciliation;
            guideHealth.Reset(startup?.MissingGuides ?? []);
```

This runs before the first render, so the first Library page already counts
missing files. A Try again rebuilds the entries from the new report.

After the material fallback warning (ruling 18):

```csharp
            if (startup is { UnreadableGuideCount: > 0 } report)
            {
                ShowWarningStatus(GuideFilePresentation.Unreadable(report.UnreadableGuideCount));
            }
```

- [ ] **Step 3: The reader action enum, and marking from the reader**

In `ShellWindow.xaml.cs`, change the `readerErrorAction` field to
`private GuideLoadAction readerErrorAction;`. Change `ShowReaderSurface`'s
parameter to `GuideLoadAction action = GuideLoadAction.None` and its three
`HtmlGuideLoadAction.None` comparisons to `GuideLoadAction.None` (Task 8
already renamed them; check nothing still says `HtmlGuideLoadAction`).
`HtmlGuideLoadMessages.ActionLabel` keeps labelling the button for every
format.

Add beside `CancelReaderLoad`:

```csharp
    // A failed or successful open is the newest word on the reader guide's
    // file; null leaves the entry as it was.
    private void MarkReaderGuide(GuideFileStatus? status)
    {
        if (status is GuideFileStatus known && navigator.Current is ReaderRoute route)
        {
            guideHealth.Mark(route.GuideId, route.GameId, known);
        }
    }
```

Call it at each place a reader shows its error or its view:

1. `ShowHtmlLoadError` (HtmlReader.cs), before `ShowReaderSurface`:
   `MarkReaderGuide(HtmlGuideLoadMessages.StatusFor(error));`. This covers
   the loader failure, the open failure and `OnReaderSessionFailed`.
2. `ShowPdfLoadError` (PdfReader.cs), before `ShowReaderSurface`:
   `MarkReaderGuide(PdfGuideLoadMessages.StatusFor(error));`.
3. The TXT failure in `RenderCurrentAsync` becomes:

   ```csharp
                    if (textLoad is TextGuideLoadFailed failed)
                    {
                        string message = TextGuideLoadMessages.For(failed.Error);
                        MarkReaderGuide(TextGuideLoadMessages.StatusFor(failed.Error));
                        ShowReaderSurface(
                            loading: false, error: message,
                            action: TextGuideLoadMessages.ActionFor(failed.Error));
                        ShowWarningStatus(message);
                        break;
                    }
   ```

4. `MarkReaderGuide(GuideFileStatus.Ok);` directly before each success
   `ShowReaderSurface(loading: false, view: session.View);`, in the TXT
   path, `OpenHtmlGuideAsync` and `OpenPdfGuideAsync`.

A PDF that asks for a password isn't marked: its file opened. A password
never reaches these calls.

- [ ] **Step 4: Remove from the reader**

In `ReaderLoadErrorActionClicked`, rename the two `HtmlGuideLoadAction`
cases to `GuideLoadAction` and add:

```csharp
            case GuideLoadAction.Remove:
                await RemoveReaderGuideAsync(generation);
                break;
```

Add to `ShellWindow.xaml.cs`, after `RemoveSelectedGuideClicked`:

```csharp
    // Ruling 13: the Game page's removal, offered on a reader whose guide
    // can't open. After a removal the reader goes Back.
    private async Task RemoveReaderGuideAsync(int generation)
    {
        if (removeRequested || closeRequested || navigator.Current is not ReaderRoute route)
        {
            return;
        }
        removeRequested = true;
        ReaderLoadErrorAction.IsEnabled = false;
        try
        {
            await RunNavigationAsync(async () =>
            {
                if (closeRequested || generation != renderGeneration || navigator.Current != route)
                {
                    return;
                }
                GuideRemover remover = guideRemover
                    ?? throw new InvalidOperationException("The library is not ready.");
                string title = ReaderHeading.Text;
                GuideRemovalPreview? preview;
                try
                {
                    preview = await remover.DescribeAsync(route.GuideId);
                }
                catch (Exception error)
                {
                    ShowRemovalError(error, title);
                    return;
                }
                if (closeRequested)
                {
                    return;
                }
                if (preview is null)
                {
                    guideHealth.Forget(route.GuideId);
                    await GoBackAsync();
                    ShowTransientStatus(GuideRemovalPresentation.AlreadyRemoved(title));
                    return;
                }
                title = preview.Title;
                ContentDialog dialog = RemoveGuideDialog.Create(preview, Navigation.XamlRoot);
                DialogSurface.Apply(dialog, EffectiveMaterial, DialogTheme);
                activeRemoveDialog = dialog;
                ContentDialogResult choice;
                try
                {
                    choice = await dialog.ShowAsync();
                }
                finally
                {
                    activeRemoveDialog = null;
                }
                // Cancel leaves the error and its button where they were.
                if (choice != ContentDialogResult.Primary || closeRequested)
                {
                    return;
                }
                GuideRemovalResult result;
                try
                {
                    result = await remover.RemoveAsync(route.GuideId);
                }
                catch (Exception error)
                {
                    ShowRemovalError(error, title);
                    return;
                }
                guideHealth.Forget(route.GuideId);
                if (!closeRequested && navigator.Current == route)
                {
                    CancelReaderLoad();
                    await GoBackAsync();
                }
                ShowTransientStatus(result.Outcome == GuideRemovalOutcome.NotFound
                    ? GuideRemovalPresentation.AlreadyRemoved(title)
                    : GuideRemovalPresentation.Removed(title, result.CleanupPending));
            });
        }
        finally
        {
            removeRequested = false;
            ReaderLoadErrorAction.IsEnabled = true;
        }
    }
```

`GoBackAsync` already sets `pendingGuideFocus` to the removed guide when Back
lands on its game, and the Game render resolves that to the nearest
survivor. `ReaderRoute` is a record, so `!=` compares the guide and game IDs.
The Reader is never the first route, so Back always has somewhere to go.

- [ ] **Step 5: Resume skips an unreadable guide**

In the Library render (~1634), replace the `resume` lookup:

```csharp
                    Guide? resume = null;
                    if (settings.LastActiveGuideId is Guid lastId)
                    {
                        try
                        {
                            resume = await library.GetGuideAsync(lastId);
                        }
                        catch (Exception error) when (error is InvalidDataException or FormatException or ArgumentOutOfRangeException)
                        {
                            // An unreadable row stays hidden, so Resume hides too (Review Focus 1).
                        }
                    }
```

- [ ] **Step 6: Build**

Run: Production build.
Expected: build succeeded, 0 errors, and no new warnings in the changed
files. `grep -rn HtmlGuideLoadAction src tests tools` prints nothing.

- [ ] **Step 7: Review against the Review Focus**

Check in code:
- Item 1: the Library render catches only the three read exceptions around
  `GetGuideAsync`. `ListGameSummariesAsync` already skips the row (Task 9),
  so the Library renders with Resume hidden.
- Item 5: a `DescribeAsync` that returns null forgets the entry, goes Back
  and says the guide was already removed. A `RemoveAsync` that returns
  NotFound does the same after the dialog.
- A second click while the dialog is up is ignored by `removeRequested`.
- A close during the dialog hides it through `activeRemoveDialog`, and
  `closeRequested` stops the handler before it removes anything.

- [ ] **Step 8: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Production/GuideRowItem.cs src/DesktopGuides.Production/LibraryGameItem.cs src/DesktopGuides.Production/ShellWindow.xaml.cs src/DesktopGuides.Production/ShellWindow.HtmlReader.cs src/DesktopGuides.Production/ShellWindow.PdfReader.cs
git commit -m "feat(shell): flag broken guide files and remove them from the reader" -m "Startup's missing-file report and each reader open feed GuideFileHealth. Guide rows lead with File missing or File damaged, game rows count guides that need attention, and a reader that can't open its guide offers Remove guide. Library Resume skips an unreadable row, and startup reports how many rows are hidden." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 11: Installed smoke for broken guide files

TDD skip: harness changes only. The gates are the static checks below and
the installed run.

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`:
  - a new `Assert-RowHelp` beside `Assert-RowFacts` (~683)
  - `$missingMessage` (~1341)
  - `html-missing-entry` (~2252)
  - `pdf-damaged` and `pdf-missing` (~2700–2718)
  - the `txt-reader` branch: its start (~3740) and `txt-missing` /
    `html-no-manifest` (~3818–3839)
- Modify: `docs/p1/e2e-testing.md` (the TXT reader row at ~316)

**Interfaces:**
- Consumes: Task 10's "Remove guide" action, row facts and attention count.
  Task 8's copy and ruling 24's zero-file dialog body. T15.3's
  `RemoveGuideDialog`, `RemoveGuideMessage`, `PrimaryButton` and
  `CloseButton`.
- Produces (in the `txt-reader-light` result):
  - phases `txt-library-attention`, `txt-missing-row`,
    `txt-missing-remove-cancelled`, `txt-missing-removed` and
    `html-no-manifest-row`
  - screenshots `txt-reader-light.txt-missing-row.png` and
    `txt-reader-light.txt-missing-remove.png`

The seed doesn't change. Its helpers always write a guide's file, so only
the guides it deletes on purpose are reported: Missing File Guide (TXT seed),
Missing PDF Guide (PDF seed), and Canary Guide B once the installer deletes
its entry before the runtime-missing pass. No mode asserts the facts of
those games' Library rows, apart from the new check below.

- [ ] **Step 1: A one-row help-text check**

After `Assert-RowFacts`, add:

```powershell
    # One realized row's help text, matched with -like.
    function Assert-RowHelp([string] $id, [string] $name, [string] $pattern) {
        $deadline = (Get-Date).AddSeconds(10)
        $help = 'no row'
        do {
            $row = @(Get-ListRows $id | Where-Object { $_.Current.Name -eq $name }) |
                Select-Object -First 1
            if ($row) {
                $help = $row.Current.HelpText
                if ($help -like $pattern) { return $help }
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "$id row '$name' help text was: $help"
    }
```

- [ ] **Step 2: The new missing copy and the Remove action**

Change `$missingMessage` to:

```powershell
        $missingMessage = "This guide's file is missing from the library. Remove it, then import the original again."
```

In `html-missing-entry`, change the comment to "a deleted entry still says
it is missing, and offers Remove guide", and replace
`Assert-Absent 'ReaderLoadErrorAction'` with
`[void](Wait-Name 'ReaderLoadErrorAction' 'Remove guide')`.

In `pdf-damaged` and in `pdf-missing`, make the same replacement. `$damaged`
keeps its copy (Task 8 leaves PDF Damaged unchanged).

- [ ] **Step 3: The TXT reader removes its missing guide**

At the start of the `txt-reader` branch (the final `else` of the reader
modes), replace `Select-Element $textGame` with:

```powershell
            # Startup found Missing File Guide's file gone (TR15.1).
            $report.txtLibraryRow = Assert-RowHelp 'GameList' $textGame '*5 guides, 1 guide needs attention'
            $report.phases += 'txt-library-attention'
            Select-Element $textGame
```

Replace the block from `# A missing managed file shows one sentence…`
through `$report.phases += 'html-no-manifest'` with:

```powershell
            # A missing managed file leads its row, shows one sentence and
            # no text or commands, and offers Remove guide.
            Show-TextGuide 'Missing File Guide'
            $report.missingRow = Assert-RowHelp 'GuideList' 'Missing File Guide' 'File missing, Text (TXT), *'
            $report.txtMissingRowScreenshot = Save-WindowScreenshot 'txt-missing-row'
            $report.phases += 'txt-missing-row'
            Open-TextGuide 'Missing File Guide'
            [void](Wait-Status $missingMessage)
            [void](Wait-Name 'ReaderLoadError' $missingMessage)
            Assert-Absent 'ReaderTextLines'
            Assert-Absent 'ReaderLoading'
            Assert-Absent 'RouteProgress'
            Assert-NoReaderCommands 'Missing File Guide'
            $removeAction = Wait-Name 'ReaderLoadErrorAction' 'Remove guide'
            $report.phases += 'txt-missing'

            # Cancel keeps the guide and its error (TR15.2).
            Invoke-Element $removeAction
            [void](Wait-VisibleById 'RemoveGuideDialog')
            [void](Wait-Name 'RemoveGuideMessage' ("This removes the guide and its reading progress " +
                "from Desktop Guides. The original file you imported isn't affected."))
            $report.txtMissingRemoveScreenshot = Save-WindowScreenshot 'txt-missing-remove'
            Invoke-Element (Wait-EnabledById 'CloseButton')
            [void](Wait-HiddenById 'RemoveGuideDialog')
            [void](Wait-Name 'ReaderHeading' 'Missing File Guide')
            [void](Wait-Name 'ReaderLoadError' $missingMessage)
            $report.phases += 'txt-missing-remove-cancelled'

            # Remove goes back to the game without the guide.
            Invoke-Element (Wait-Name 'ReaderLoadErrorAction' 'Remove guide')
            [void](Wait-VisibleById 'RemoveGuideDialog')
            Invoke-Element (Wait-EnabledById 'PrimaryButton')
            [void](Wait-HiddenById 'RemoveGuideDialog')
            [void](Wait-Status 'Removed Missing File Guide.')
            [void](Wait-Name 'GameHeading' $textGame)
            if ((Get-GuideRowNames) -contains 'Missing File Guide') {
                throw 'The removed Missing File Guide is still listed.'
            }
            $report.phases += 'txt-missing-removed'

            # A Web Page Guide with no saved asset rows (imported before
            # schema v4) asks to be re-imported, offers Remove guide, and
            # its row then says the file is damaged.
            Open-TextGuide 'Web Page Guide'
            [void](Wait-Status 'Re-import this guide to read it.')
            [void](Wait-Name 'ReaderLoadError' 'Re-import this guide to read it.')
            [void](Wait-Name 'ReaderLoadErrorAction' 'Remove guide')
            Assert-Absent 'ReaderTextLines'
            Assert-Absent 'ReaderLoading'
            Assert-Absent 'RouteProgress'
            Assert-NoReaderCommands 'Web Page Guide'
            $report.phases += 'html-no-manifest'
            Back-ToTextGame
            Show-TextGuide 'Web Page Guide'
            $report.webPageRow = Assert-RowHelp 'GuideList' 'Web Page Guide' 'File damaged, Web page (HTML), *'
            $report.phases += 'html-no-manifest-row'
```

The next step, `# Reopening reads the file again.`, starts with
`Back-ToTextGame`. Remove that line, because the step above already ends on
the Game page. ASCII Map Guide reopening after the removal is the check that
other guides still open.

The `txt-load-paused`, `txt-back-during-load` and `txt-load-released` modes
run later on the same data and open only ASCII Map Guide, so the removal
doesn't affect them.

- [ ] **Step 4: Static checks**

Run: `grep -n "Assert-Absent 'ReaderLoadErrorAction'" tools/p1/windows_shell_ui_smoke.ps1`
Expected: two lines remain: `html-crash` after Reopen succeeds (~2225),
and `html-runtime-missing-txt` (~2266). None remain in `html-missing-entry`,
`pdf-damaged` or `pdf-missing`.

Run: `grep -rn "file is missing from the library\.\"" tools/p1 docs/p1/e2e-testing.md`
Expected: no output. The old one-sentence copy is gone.

Run: `LC_ALL=C grep -n '[^ -~]' tools/p1/windows_shell_ui_smoke.ps1 | head`
Expected: no new lines. The script stays ASCII-only.

Run: `s 'powershell -NoProfile -Command "$null = [System.Management.Automation.Language.Parser]::ParseFile(''E:\work\desktop-guides\t15-1\tools\p1\windows_shell_ui_smoke.ps1'', [ref]$null, [ref]$e); $e.Count"'` after `stage`.
Expected: `0`.

- [ ] **Step 5: Update the E2E plan**

In `docs/p1/e2e-testing.md`:
- In the TXT reader row (~316), replace `Missing File shows \`This guide's file is missing from the library.\` and no text or commands; Web Page shows the placeholder; reopening ASCII shows its rows again.` with:

  ```text
  The Library row for Text Reader Game ends `5 guides, 1 guide needs attention`. Missing File's row leads with `File missing`; opening it shows `This guide's file is missing from the library. Remove it, then import the original again.`, no text or commands, and Remove guide. Its dialog says `This removes the guide and its reading progress from Desktop Guides. The original file you imported isn't affected.`; Cancel keeps the Reader and its error, and Remove shows the Game page with `Removed Missing File Guide.` and no Missing File row. Web Page shows the placeholder with Remove guide, and its row then leads with `File damaged`; reopening ASCII shows its rows again.
  ```

- Add `TR15.1, TR15.2` to that row's traceability cell.

The catalogue has no HTML or PDF reader error rows; the T09.1 and T10.1
records describe those modes and stay as they were.

Run: `grep -n "is missing from the library" docs/p1/e2e-testing.md`
Expected: one line, the TXT reader row, with the new two-sentence copy.

- [ ] **Step 6: Installed verification**

Commit first (Step 7), then push the branch (no approval needed) and let
CI's `production-shell-ui` job run. It is the gate of record and runs the
TXT, HTML and PDF groups. To run it on the host instead, use
`windows_shell_install.ps1 -PackagePath <msix> -ResultDirectory E:\work\desktop-guides\t15-1-results`
through an interactive scheduled task, following
`docs/p1/e2e-testing.md`. Back up and restore the app data and
`%LOCALAPPDATA%\DesktopGuides\P0-WebView` as it requires.

Expected:
- `txtReaderLight.phases` (result `txt-reader-light`) contains `txt-library-attention`,
  `txt-missing-row`, `txt-missing`, `txt-missing-remove-cancelled`,
  `txt-missing-removed`, `html-no-manifest`, `html-no-manifest-row` and
  `txt-reopen`.
- The HTML runtime-missing report contains `html-missing-entry`, and the PDF
  report contains `pdf-damaged` and `pdf-missing`.
- Every existing phase still passes.

Copy the screenshots for Task 12:

| Result file | Evidence |
|---|---|
| `txt-reader-light.txt-missing-row.png` | `docs/p1/evidence/t15-1-error-recovery/guide-missing-row.png` |
| `txt-reader-light.txt-missing-remove.png` | `docs/p1/evidence/t15-1-error-recovery/guide-missing-remove.png` |

Open both. The first shows the caution glyph on Missing File Guide's row,
and the second shows the Remove dialog over the reader error.

- [ ] **Step 7: Commit** (show the message in chat first)

```bash
git add tools/p1/windows_shell_ui_smoke.ps1 docs/p1/e2e-testing.md
git commit -m "test(e2e): broken guide files offer Remove in the installed smoke" -m "The TXT pass checks the Library attention count and the File missing row, cancels and then completes Remove guide on the missing guide, and sees Web Page Guide marked damaged. The HTML and PDF passes expect Remove guide on their missing and damaged guides." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 12: PR b documentation and verification record

**Files:**
- Modify: `docs/p1/t15-1-error-recovery-design.md` (status line, a `### PR b` section in the verification record)
- Modify: `docs/p1/implementation-plan.md` (the T15.1 paragraph added by Task 6)
- Modify: `docs/progress.md` (the "Updated" line and the T15.1 row)
- Create: `docs/p1/evidence/t15-1-error-recovery/guide-missing-row.png` and `guide-missing-remove.png` (from Task 11 Step 6)

**Interfaces:**
- Consumes:
  - the CI run ID and conclusion from Task 11 Step 6;
  - the Core and Infrastructure test totals from the last host runs;
  - the two evidence PNGs;
  - PR a's merged PR number;
  - every `Ruling:` line in the executor's ledger for Tasks 7–11.

TDD skip: this task changes documentation only. The gate is the placeholder
grep, `git diff --check` and a read-through.

Fill every `<…>` below from the observed run before committing. Never
commit a placeholder.

- [ ] **Step 1: Spec**

In `t15-1-error-recovery-design.md`, replace the status paragraph's first
sentence with:
`Status: PR a merged in #<PR a>; PR b implemented on \`feat/p1-t15-1-guide-health\`, verified by CI run <run id>. PR c is planned.`
Then append, after the `### PR a` section of the verification record:

```markdown
### PR b: guide health and the reader

- **Unit tests.** On `pcsx2-win`, Infrastructure <n>/<n> and Core <n>/<n>
  passed. The new tests are:
  - `GuideFileHealthTests`: marking, forgetting, the per-game count and
    a reset from the startup report;
  - `CatalogPresentationTests`: the `File missing` and `File damaged`
    facts lead a guide row, and a game row adds
    "1 guide needs attention" or "{n} guides need attention" after its count;
  - `HtmlGuideLoadMessagesTests`, `PdfGuideLoadMessagesTests` and
    `TextGuideLoadMessagesTests`: the new missing and changed copy, which
    errors offer **Remove guide**, and each error's file status;
  - `GuideRemovalPresentationTests`: a guide with no files left gets the
    dialog body without a file count;
  - `SqliteLibraryRepositoryTests` and `StartupReconciliationReportTests`:
    startup lists guides whose managed file is gone, counts unreadable guide
    rows, and the guide lists skip those rows.
- **Installed.** CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>)
  passed `production-shell-ui`. In the TXT group:
  - the Library row for Text Reader Game ended "1 guide needs attention",
    and Missing File Guide's row led with `File missing`;
  - its reader error offered **Remove guide**. Cancel kept the error;
    Remove returned to the game without the guide, and ASCII Map Guide
    still opened;
  - Web Page Guide offered **Remove guide**, and its row then led with
    `File damaged`.
  In the HTML and PDF groups, the missing entry, the missing PDF and the
  damaged PDF offered **Remove guide**.
- **Rulings.** <the ledger rulings made during PR b, each on one line, or "none">.
- **Evidence.**
  - [Missing guide row](evidence/t15-1-error-recovery/guide-missing-row.png)
  - [Remove from the reader](evidence/t15-1-error-recovery/guide-missing-remove.png)
```

- [ ] **Step 2: Implementation plan**

In `docs/p1/implementation-plan.md`, change the T15.1 paragraph's first
sentence to `T15.1 PR a merged in #<PR a>; PR b is implemented on
\`feat/p1-t15-1-guide-health\`; see the [design and verification
record](t15-1-error-recovery-design.md#t151-verification-record).` Then
append to the paragraph:

```markdown
Startup reports guides whose managed file is missing, and the Library and
game rows say which guides need attention. A guide whose file is missing or
damaged offers **Remove guide** in the Reader, through T15.3's removal.
Unreadable guide rows are hidden with one warning, and other guides open.
CI run <run id> passed the installed missing-file and removal runs.
```

- [ ] **Step 3: Progress**

In `docs/progress.md`:

- Set the "Updated" line to the commit date.
- In the T15.1 row, set the status cell to
  `PR a merged in #<PR a>. PR b implemented on \`feat/p1-t15-1-guide-health\`; PR open. PR c is planned.`
  and append to its evidence cell:
  `Broken guide files are marked in their rows and can be removed from the Reader; CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>).`

After the PR opens, change "PR open" to the linked PR number in a
follow-up commit on the same branch.

- [ ] **Step 4: Check and commit** (show the message in chat first)

```bash
grep -n '<run id>\|<n>/<n>\|<PR a>\|<the ledger' docs/p1/t15-1-error-recovery-design.md docs/p1/implementation-plan.md docs/progress.md
git diff --check
```

Expected: no output from either command.

```bash
git add docs/p1/t15-1-error-recovery-design.md docs/p1/implementation-plan.md docs/progress.md docs/p1/evidence/t15-1-error-recovery
git commit -m "docs(p1): record T15.1 guide health verification" \
  -m "Mark T15.1 PR b implemented and add its verification record: unit tests, CI run <run id>, rulings and two screenshots. Update the T15.1 paragraph in the implementation plan and the T15.1 progress row." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

# PR c: the startup runtime check

Start from `main` after PR b merges:
`git switch main && git pull && git switch -c feat/p1-t15-1-runtime-check`.

### Task 13: Warn at startup when the WebView2 Runtime is missing

**Files:**
- Modify: `src/DesktopGuides.Core/Html/HtmlGuideLoadMessages.cs` (a startup copy constant)
- Modify: `tests/DesktopGuides.Core.Tests/HtmlGuideLoadMessagesTests.cs`
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs` (`IsRuntimeAvailable`, used by `OpenEntryAsync` ~157–180)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (the `ShellStatusInfoBar` ActionButton, ~80)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (`ShowStatus` ~284, `InitializeCoreAsync`)
- Modify: `src/DesktopGuides.Production/ShellWindow.Startup.cs` (the probe and the button handler)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (`html-runtime-missing`, ~2234)
- Modify: `tools/p1/windows_shell_install.ps1` (`Invoke-HtmlReaderPass` ~1037, the runtime-missing launches ~1200)

**Interfaces:**
- Consumes:
  - Task 8's `GuideLoadAction` and `HtmlGuideLoadMessages.ActionLabel(GuideLoadAction)`;
  - Task 4's `ShellWindow.Startup.cs`;
  - Task 10's unreadable-row warning in `InitializeCoreAsync` (ruling 18
    puts the runtime warning after it);
  - the existing `LaunchExternalAsync(Uri)` and
    `HtmlGuideLoadMessages.RuntimeDownloadUrl`.
- Produces:
  - `HtmlGuideLoadMessages.RuntimeMissingAtStartup` (const string);
  - `public static bool HtmlReaderSession.IsRuntimeAvailable(string cacheRoot)`;
  - AutomationId `ShellStatusAction`;
  - smoke phase `html-runtime-startup` and screenshot
    `html-runtime-missing.html-runtime-startup.png`.

TDD applies to the copy only. The shell, session and harness changes are
ruling 21's skip: they are gated by the Production build, review and the
installed run.

- [ ] **Step 1: Write the failing test**

In `HtmlGuideLoadMessagesTests.cs`, add:

```csharp
    [Fact]
    public void StartupWarningSaysWhatStillOpens() =>
        Assert.Equal(
            "Web page guides need the Microsoft Edge WebView2 Runtime. Text and PDF guides still open.",
            HtmlGuideLoadMessages.RuntimeMissingAtStartup);
```

- [ ] **Step 2: Run it to make sure it fails**

Run: Core tests with `--filter "FullyQualifiedName~HtmlGuideLoadMessagesTests"`.
Expected: build error CS0117, `HtmlGuideLoadMessages` has no definition
for `RuntimeMissingAtStartup`.

- [ ] **Step 3: Add the copy**

In `HtmlGuideLoadMessages`, after `RuntimeDownloadUrl`:

```csharp
    // T15.1: the startup check, shown before any web page guide is opened.
    public const string RuntimeMissingAtStartup =
        "Web page guides need the Microsoft Edge WebView2 Runtime. Text and PDF guides still open.";
```

- [ ] **Step 4: Run it to make sure it passes**

Run: Core tests with `--filter "FullyQualifiedName~HtmlGuideLoadMessagesTests"`.
Expected: PASS, including the new Fact.

- [ ] **Step 5: One runtime probe for startup and each open**

In `HtmlReaderSession.cs`, after `MissingRuntimeFolderForTest`, add:

```csharp
    // T15.1: the startup check and each open ask the same question, so a
    // runtime installed later is found on the next open.
    public static bool IsRuntimeAvailable(string cacheRoot)
    {
        string? browserFolder = MissingRuntimeFolderForTest(cacheRoot);
        try
        {
            if (browserFolder is not null) Directory.CreateDirectory(browserFolder);
            return !string.IsNullOrEmpty(
                CoreWebView2Environment.GetAvailableBrowserVersionString(browserFolder));
        }
        catch (Exception)
        {
            return false;
        }
    }
```

In `OpenEntryAsync`, replace the block from
`string? browserFolder = MissingRuntimeFolderForTest(cacheRoot);` through
the `if (string.IsNullOrEmpty(version)) { … }` check with:

```csharp
        if (!IsRuntimeAvailable(cacheRoot))
        {
            throw new HtmlGuideLoadException(HtmlGuideLoadError.RuntimeMissing);
        }
        string? browserFolder = MissingRuntimeFolderForTest(cacheRoot);
```

`browserFolder` is still passed to `CreateWithOptionsAsync` below.
`GetAvailableBrowserVersionString` takes no token, so dropping the old
`when (error is not OperationCanceledException)` filter loses nothing.

- [ ] **Step 6: The status bar's action button**

In `ShellWindow.xaml`, give `ShellStatusInfoBar` a body (it is
self-closing today):

```xml
            <InfoBar x:Name="ShellStatusInfoBar"
                     Style="{StaticResource DesktopGuidesStatusInfoBarStyle}"
                     Margin="0,0,0,16"
                     IsOpen="False"
                     Severity="Informational"
                     AutomationProperties.AutomationId="ShellStatus"
                     AutomationProperties.LiveSetting="Polite">
                <InfoBar.ActionButton>
                    <Button x:Name="ShellStatusAction"
                            AutomationProperties.AutomationId="ShellStatusAction"
                            Visibility="Collapsed"
                            Click="ShellStatusActionClicked" />
                </InfoBar.ActionButton>
            </InfoBar>
```

In `ShellWindow.xaml.cs` `ShowStatus`, after `statusDismissTimer.Stop();`,
add `ShellStatusAction.Visibility = Visibility.Collapsed;` (ruling 20). Every
other status, including the next route's progress, drops the button.

- [ ] **Step 7: The startup probe**

In `ShellWindow.Startup.cs`, add:

```csharp
    // T15.1: one background probe once the library is ready. The warning
    // doesn't hide route progress, because a click may already be loading
    // a page.
    private async Task WarnIfRuntimeMissingAsync(string probedCacheRoot)
    {
        bool available = await Task.Run(
            () => HtmlReaderSession.IsRuntimeAvailable(probedCacheRoot));
        if (available || closeRequested)
        {
            return;
        }
        ShowStatus(HtmlGuideLoadMessages.RuntimeMissingAtStartup, InfoBarSeverity.Warning, true, false);
        ShellStatusAction.Content = HtmlGuideLoadMessages.ActionLabel(GuideLoadAction.GetRuntime);
        ShellStatusAction.Visibility = Visibility.Visible;
    }

    private async void ShellStatusActionClicked(object sender, RoutedEventArgs args) =>
        await LaunchExternalAsync(new Uri(HtmlGuideLoadMessages.RuntimeDownloadUrl));
```

Add `using DesktopGuides.Core.Html;`, `using DesktopGuides.Core.Reading;`
and `using Microsoft.UI.Xaml.Controls;` (for `InfoBarSeverity`). The project
has implicit usings, and Task 4 already imports `Microsoft.UI.Xaml`.

In `InitializeCoreAsync`, directly after Task 10's unreadable-row warning
(the last statement in the `try` block), add:

```csharp
            await WarnIfRuntimeMissingAsync(sweptRoot);
```

`sweptRoot` is the local copy of `cacheRoot` that the profile sweep already
uses. `IsRuntimeAvailable` never throws, so the `catch (Exception)` that
reports "Couldn't open the library" can't fire from here.

- [ ] **Step 8: Build**

Run: Production build.
Expected: build succeeded, 0 errors, and no new warnings in the changed files.

- [ ] **Step 9: Smoke phase**

In `windows_shell_ui_smoke.ps1`, in the `html-runtime-missing` branch,
between `[void](Wait-Name 'LibraryHeading' 'Library')` and
`Select-Element $textGame`, add:

```powershell
            # html-runtime-startup: before any web page guide is opened, the
            # startup check warns and offers Microsoft's page; the click goes
            # to the test launcher and leaves the warning showing.
            $startupRuntime = 'Web page guides need the Microsoft Edge WebView2 Runtime. Text and PDF guides still open.'
            [void](Wait-Name 'ShellStatus' $startupRuntime)
            $startupAction = Wait-Name 'ShellStatusAction' 'Get WebView2 Runtime'
            $report.htmlRuntimeStartupScreenshot = Save-WindowScreenshot 'html-runtime-startup'
            Invoke-Element $startupAction
            Start-Sleep -Seconds 1
            [void](Wait-Name 'ShellStatus' $startupRuntime)
            $report.phases += 'html-runtime-startup'
```

Selecting the game shows route progress, which closes the bar, so the
existing reader phases are unaffected.

- [ ] **Step 10: Hold the lease until the gate exists**

In `windows_shell_install.ps1`, change the start of `Invoke-HtmlReaderPass`
from `Start-InstalledShell` / `$processId = $report.launchedProcessId` to:

```powershell
    # Ruling 19 of the T15.1 plan: the startup runtime probe runs as soon as
    # the library opens, so the shell waits on the lease until the gate exists.
    $lease = $null
    if ($NoRuntime) {
        $lease = [System.IO.File]::Open(
            (Join-Path $dataRoot 'library.session.lock'),
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    }
    try {
        Start-InstalledShell
    }
    catch {
        if ($lease) { $lease.Dispose() }
        throw
    }
    $processId = $report.launchedProcessId
```

After the `$runtimeGate` block, before `try {`, add:

```powershell
    if ($lease) { $lease.Dispose() }
```

In the runtime-missing `Assert-HtmlReaderPass` call, the startup click
launches the runtime page too, so expect it twice:

```powershell
            @('https://developer.microsoft.com/microsoft-edge/webview2/',
              'https://developer.microsoft.com/microsoft-edge/webview2/') $logPath $baseline
```

- [ ] **Step 11: Static checks**

Run: `LC_ALL=C grep -n '[^ -~]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1 | head`
Expected: no new lines.

Run, after `stage`:
`s 'powershell -NoProfile -Command "foreach ($f in ''windows_shell_ui_smoke.ps1'',''windows_shell_install.ps1'') { $null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path ''E:\work\desktop-guides\t15-1\tools\p1'' $f), [ref]$null, [ref]$e); $e.Count }"'`
Expected: `0` twice.

- [ ] **Step 12: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Core/Html/HtmlGuideLoadMessages.cs tests/DesktopGuides.Core.Tests/HtmlGuideLoadMessagesTests.cs src/DesktopGuides.Production/HtmlReaderSession.cs src/DesktopGuides.Production/ShellWindow.xaml src/DesktopGuides.Production/ShellWindow.xaml.cs src/DesktopGuides.Production/ShellWindow.Startup.cs tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "feat(shell): warn at startup when the WebView2 Runtime is missing" -m "Once the library is ready, the shell checks for the runtime on a background thread with the same probe each HTML open uses. With no runtime it shows a dismissible warning with Get WebView2 Runtime. The installed runtime-missing pass holds the library lease until its gate exists and asserts the warning before any guide opens." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 13: Installed verification**

Push the branch (no approval needed) and let CI's `production-shell-ui` job
run. It is the gate of record. To run it on the host instead, use
`windows_shell_install.ps1 -PackagePath <msix> -ResultDirectory E:\work\desktop-guides\t15-1-results -HtmlOnly`
through an interactive scheduled task, following `docs/p1/e2e-testing.md`.

Expected:
- `htmlReader.html-runtime-missing.phases` contains `html-runtime-startup`
  before `html-runtime-missing`.
- The runtime-missing launches are the runtime page twice.
- Every other HTML pass and its launches are unchanged, because the probe
  finds the runtime and shows nothing.

Copy `html-runtime-missing.html-runtime-startup.png` to
`docs/p1/evidence/t15-1-error-recovery/runtime-missing-startup.png` for
Task 14, and open it: the Library with the warning and its button.

### Task 14: PR c documentation and verification record

**Files:**
- Modify: `docs/p1/t15-1-error-recovery-design.md` (status line, a `### PR c` section in the verification record)
- Modify: `docs/p1/implementation-plan.md` (the T15.1 paragraph, and the T15.1 row at ~979)
- Modify: `docs/work-breakdown.md` (the T15.1 bullet at ~487)
- Modify: `docs/progress.md` (the "Updated" line and the T15.1 row)
- Create: `docs/p1/evidence/t15-1-error-recovery/runtime-missing-startup.png` (from Task 13 Step 13)

**Interfaces:**
- Consumes:
  - the CI run ID and conclusion from Task 13 Step 13;
  - the Core test total from the last host run;
  - the evidence PNG;
  - PR a's and PR b's merged PR numbers;
  - every `Ruling:` line in the executor's ledger for Task 13.

TDD skip: this task changes documentation only. The gate is the placeholder
grep, `git diff --check` and a read-through.

Fill every `<…>` below from the observed run before committing. Never
commit a placeholder.

- [ ] **Step 1: Spec**

In `t15-1-error-recovery-design.md`, replace the status paragraph's first
sentence with:
`Status: PR a merged in #<PR a> and PR b in #<PR b>; PR c implemented on \`feat/p1-t15-1-runtime-check\`, verified by CI run <run id>.`
Then append, after the `### PR b` section:

```markdown
### PR c: the startup runtime check

- **Unit tests.** On `pcsx2-win`, Core <n>/<n> passed, including
  `HtmlGuideLoadMessagesTests.StartupWarningSaysWhatStillOpens`.
- **Installed.** CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>)
  passed `production-shell-ui`. In the runtime-missing pass, the Library
  showed "Web page guides need the Microsoft Edge WebView2 Runtime. Text and
  PDF guides still open." with **Get WebView2 Runtime** before any guide was
  opened, and the button reached the test launcher. The other HTML passes
  showed no warning.
- **Windows App SDK runtime.** The MSIX declares the framework dependency,
  so Windows installs it or refuses to start the app before any app code
  runs. No in-app check was added.
- **Rulings.** <the ledger rulings made during PR c, each on one line, or "none">.
- **Evidence.**
  - [Missing runtime at startup](evidence/t15-1-error-recovery/runtime-missing-startup.png)
```

- [ ] **Step 2: Implementation plan and work breakdown**

In `docs/p1/implementation-plan.md`:

- Change the T15.1 paragraph's first sentence to `T15.1 is implemented in
  #<PR a>, #<PR b> and PR c on \`feat/p1-t15-1-runtime-check\`; see the
  [design and verification record](t15-1-error-recovery-design.md#t151-verification-record).`
  and append: `A missing WebView2 Runtime is reported once at startup with
  **Get WebView2 Runtime**, and TXT and PDF guides still open. CI run <run id>
  passed the installed runtime-missing run.`
- In the T15.1 table row, append ` PRs: #<PR a>, #<PR b>, PR c.` to the
  description cell.

In `docs/work-breakdown.md`, append to the T15.1 bullet:

```markdown
  The Windows App SDK runtime is satisfied by the MSIX framework
  dependency, so the app adds no check of its own.
```

- [ ] **Step 3: Progress**

In `docs/progress.md`:

- Set the "Updated" line to the commit date.
- In the T15.1 row, set the status cell to
  `PR a merged in #<PR a> and PR b in #<PR b>. PR c implemented on \`feat/p1-t15-1-runtime-check\`; PR open.`
  and append to its evidence cell:
  `A missing WebView2 Runtime is reported at startup; CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>).`
- In the P1 summary row, remove "T15.1 error surfaces" from the Open list.
  Leave the merged-task count for the merge commit's follow-up, as earlier
  tasks did.

After the PR opens, change "PR open" and "PR c" to the linked PR number in
a follow-up commit on the same branch.

- [ ] **Step 4: Check and commit** (show the message in chat first)

```bash
grep -n '<run id>\|<n>/<n>\|<PR a>\|<PR b>\|<the ledger' docs/p1/t15-1-error-recovery-design.md docs/p1/implementation-plan.md docs/work-breakdown.md docs/progress.md
git diff --check
```

Expected: no output from either command.

```bash
git add docs/p1/t15-1-error-recovery-design.md docs/p1/implementation-plan.md docs/work-breakdown.md docs/progress.md docs/p1/evidence/t15-1-error-recovery
git commit -m "docs(p1): record T15.1 runtime check verification" \
  -m "Mark T15.1 PR c implemented and add its verification record: the startup copy test, CI run <run id>, rulings and a screenshot. Note in the work breakdown that the MSIX dependency covers the Windows App SDK runtime, and link the T15.1 PRs from the implementation plan." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Traceability

| Requirement | Evidence in this plan |
| --- | --- |
| T15.1: a corrupt or missing database stops with an actionable message and is never replaced by an empty one | Task 1's `LibraryOpenMessagesTests`; Task 2's `LibraryOpenErrorsTests`; Task 3's damaged, missing, 0-byte, WAL-only, read-only and first-run repository tests; Task 5's installed `library-damaged`, `library-missing` and Try again runs |
| T15.1: a missing or damaged guide is reported, and an unaffected guide still opens | Task 7's `GuideFileHealthTests` and row facts; Task 8's copy and action tests; Task 9's startup report and hidden unreadable rows; Task 11's `txt-missing`, `html-no-manifest`, `html-missing-entry`, `pdf-damaged`, `pdf-missing` and `txt-reopen` |
| T15.1: a missing runtime is reported with a next step | Task 13's `StartupWarningSaysWhatStillOpens` and the installed `html-runtime-startup` phase; the existing per-open `html-runtime-missing` phase |
| TR15.1: cleanup touches only app-owned paths | Task 3: an error path never creates, truncates or overwrites `library.sqlite`; Task 5 checks the bytes are unchanged; Task 10's Remove goes through T15.3's `GuideRemover` journal only |
| TR15.2: deletion removes all four kinds of state; Cancel leaves them intact | Task 11's `txt-missing-remove-cancelled` (the guide and its error stay) and `txt-missing-removed` (the row is gone) |
| TR11.3: shared resources and native controls | Task 4's page uses the shared text styles and spacing resources; Task 7 adds a caution glyph to the existing row template; Task 13 uses the status InfoBar's own `ActionButton` |

## PR outcome

### PR a

- **Target task:** T15.1, PR a of 3: the library won't open.
- **Prerequisites:** T03.2, T06.3 (PR #19), T09.1 (PR #35) and T10.1
  (PR #36), all merged.
- **Outcome:** a library that can't be opened stops on a Library
  unavailable page that names the problem and the data folder, with **Try
  again** and **Open data folder**. The database is never created over,
  truncated or replaced. The PR body shows the damaged and missing library
  pages.

### PR b

- **Target task:** T15.1, PR b of 3: guide health and the reader.
- **Prerequisites:** PR a merged; T15.3 (PR #22), merged.
- **Outcome:** a guide whose managed file is missing or damaged leads its
  row with `File missing` or `File damaged`, and the Library counts the
  guides that need attention. Its Reader error offers **Remove guide**
  through T15.3's dialog. Unreadable rows are hidden with one warning, and
  every other guide still opens. The PR body shows the marked row and the
  Remove dialog over the Reader.

### PR c

- **Target task:** T15.1, PR c of 3: the startup runtime check.
- **Prerequisites:** PR b merged.
- **Outcome:** with no WebView2 Runtime, the Library warns once at startup
  with **Get WebView2 Runtime**, and TXT and PDF guides still open. Each
  HTML open still checks, so a runtime installed later works without a
  restart. The PR body shows the startup warning.
