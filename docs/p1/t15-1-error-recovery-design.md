# T15.1 error recovery design

Status: PR a merged in #74 and PR b in #75; PR c implemented on `feat/p1-t15-1-runtime-check`, verified by CI run 37948919282.
Prerequisites T03.2, T06.3, T09.1 and T10.1 are merged.

## Intent

When something in the library is broken, Desktop Guides says what is wrong,
protects what is still there, and offers one useful next step. A library
database that can't be opened stops the app before anything is written and
is never replaced by an empty one. A guide whose managed file is missing or
damaged stays visible with its status and can be removed, and every other
guide still opens. A TXT or PDF copy that changed after import still opens
with the approximate-restore notice; a changed HTML guide stays blocked. A
missing WebView2 Runtime is reported once at startup
without blocking text and PDF guides.

Traces: the T15.1 row of [implementation-plan.md](implementation-plan.md)
(TR15.1), [work-breakdown.md](../work-breakdown.md) T15.1, and
[p1-technical-design.md](../p1-technical-design.md) (startup recovery,
205–213; native InfoBar and ContentDialog, 401; T15.1, 868–873), and R9 in
[initial-design.md](../initial-design.md).

Decisions made during brainstorming:

- **One spec, three PRs.** (a) the library won't open, (b) guide health and
  the reader, (c) the startup runtime check. Each PR is independently
  shippable in that order.
- **Missing database.** A missing or 0-byte `library.sqlite` is a fresh start
  only when the data folder holds no guides, artwork or recovery copies.
  Otherwise it is an error and nothing is created.
- **Remove only.** A broken guide offers Remove through the existing T15.3
  dialog. There is no Replace-file or re-import-in-place action.
- **Health in memory.** Guide file status is detected at startup and on open
  and kept in memory. It is not stored in SQLite and needs no migration.
- **Changed content.** A TXT or PDF managed copy whose size or SHA-256
  differs from its import record keeps opening with today's approximate-
  restore notice. HTML Changed stays blocked, as today, and offers Remove.
- **Approach A.** A typed `LibraryOpenException(LibraryOpenIssue)` and a
  dedicated "Library unavailable" page, following the codebase's
  exception-plus-issue convention.
- **Out of scope:** Replace-file or re-import-in-place; an orphan or artwork
  review UI; a lease-wait timeout for a hung previous window; a startup check
  for the Windows App SDK runtime (see Runtime); `.resw` localization.

## PR a: the library won't open

### Core types (Core/Library)

```csharp
public enum LibraryOpenIssue
{
    Damaged,          // not a database, malformed, integrity or schema check failed
    Missing,          // library.sqlite missing or 0 bytes in a used data folder
    NewerVersion,     // user_version above LibrarySchema.CurrentVersion
    MigrationFailed,  // a migration threw; the recovery copy is kept
    Locked,           // SQLITE_BUSY or SQLITE_LOCKED after Microsoft.Data.Sqlite's busy retries
    NoAccess,         // CANTOPEN, PERM, READONLY, UnauthorizedAccessException
    DiskFull,         // SQLITE_FULL or a disk-full IOException
}

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
    public static LibraryOpenMessage For(LibraryOpenIssue issue);
}
```

Every message ends by saying that nothing was changed. Example (Damaged):
"Your library database can't be read. Desktop Guides stopped before changing
anything. Your guide files are still in the data folder." NewerVersion says to
update Desktop Guides. MigrationFailed names the recovery copy. Locked says
another program may be using the library and to try again. Messages never
include exception text, paths from user-chosen sources, or SQL.

### Infrastructure (SqliteLibraryRepository)

`Initialize` is the only method that changes:

1. **First run.** If `library.sqlite` is missing or 0 bytes, it is a first run
   only when `content/`, `artwork/` and `.recovery/` are absent or empty.
   Otherwise `Initialize` throws `LibraryOpenException(Missing)` without
   creating, truncating or opening the file for write. The first-run open keeps
   `SqliteOpenMode.ReadWriteCreate`; every other open uses `ReadWrite`.
2. **Mapping.** A new internal `LibraryOpenErrors.Map(Exception, string?
   recoveryCopy)` translates failures raised inside `Initialize`:
   - `SqliteException` primary code 26 (NOTADB), 11 (CORRUPT) → Damaged
   - 5 (BUSY), 6 (LOCKED) → Locked
   - 14 (CANTOPEN), 3 (PERM), 8 (READONLY), 23 (AUTH) → NoAccess
   - 13 (FULL) → DiskFull
   - the existing "newer than this app supports" `InvalidDataException` →
     NewerVersion; other `InvalidDataException` from validation → Damaged
   - any exception after `CreateRecoveryCopy` succeeded → MigrationFailed
     with that path
   - `UnauthorizedAccessException` → NoAccess; `IOException` with
     ERROR_DISK_FULL or ERROR_HANDLE_DISK_FULL → DiskFull
   - anything else is rethrown unchanged.
3. The existing recovery-copy and rollback behaviour, and the
   `RecoverableEmptyDatabase` rule for a zero-schema file, are unchanged.

`LibraryOpenException` is only raised from `InitializeAsync`. Later reads and
writes keep their current exceptions.

### Shell (new ShellWindow.Startup.cs partial)

`InitializeAsync`'s catch handles `LibraryOpenException` separately from other
exceptions:

- It shows a `LibraryUnavailable` panel in place of the navigation content
  and keeps `ready = false`, so no command or navigation can write.
- The panel shows the title and body, the data folder path, and the recovery
  copy path when there is one.
- **Open data folder** launches Explorer on the data folder.
- **Try again** disposes the failed repository, creates and initializes a
  new one, and on success hides the panel and continues the normal startup
  path. The library lease is kept across retries.
- AutomationIds: `LibraryUnavailable`, `LibraryUnavailableTitle`,
  `LibraryUnavailableBody`, `LibraryUnavailableRecoveryPath`,
  `LibraryUnavailableOpenFolder`, `LibraryUnavailableRetry`.

Other startup exceptions keep today's status InfoBar.

### Tests

Infrastructure (`SqliteLibraryRepositoryTests` or a new
`LibraryOpenErrorTests`):

- A file of random bytes → Damaged, and its bytes are unchanged.
- A deleted `library.sqlite` with a guide in `content/` → Missing, and no
  file is created.
- A 0-byte file with an artwork file → Missing, and the file stays 0 bytes.
- A first run (empty data folder) creates and migrates the database.
- `user_version` above current → NewerVersion.
- A migration that throws through `migrationCheckpoint` → MigrationFailed
  whose `RecoveryCopyPath` exists.
- A database held with `BEGIN EXCLUSIVE` by another connection → Locked.

Core: `LibraryOpenMessages.For` returns a non-empty title and body for every
issue, and no body contains exception text.

Smoke (`ShellSeed`): `library-damaged` and `library-missing`. Each asserts the
panel and its AutomationIds, that the database bytes (or its absence) are
unchanged, then repairs the seed, clicks **Try again** and asserts the
Library appears.

## PR b: guide health and the reader

### Health status (Core/Library)

```csharp
public enum GuideFileStatus { Ok, Missing, Damaged }

public sealed class GuideFileHealth
{
    public GuideFileStatus this[Guid guideId] { get; }   // Ok when unknown
    public void Mark(Guid guideId, GuideFileStatus status);
    public void Reset(IEnumerable<Guid> missingGuideIds); // at startup
}
```

The shell owns one instance. Messages: "File missing", "File damaged".

### Copy

- Missing (all formats): "This guide's file is missing from the library.
  Remove it, then import the original again."
- Changed (HTML, and PDF when its managed path is a folder or crosses a
  link): "This guide's file changed after it was imported, so it
  can't be opened safely. Remove it, then import the original again."

The existing "Re-import it" wording for these cases is replaced. A TXT or
PDF copy whose bytes changed still opens and never produces Changed.

Sources:

1. **Startup.** `StartupReconciliationReport` gains
   `IReadOnlyList<Guid> MissingGuideIds` (default empty). After the existing
   reconciliation and artwork sweep, `Initialize` checks each Guide's managed
   entry path with `File.Exists` through the existing path resolver. It hashes
   nothing and follows no links.
2. **Reader.** A failed open marks the guide. Missing marks Missing.
   Changed, HTML NoManifest, PDF Damaged and TXT InvalidMetadata mark
   Damaged. Other errors
   (TooLarge, NotUtf8, password errors, runtime errors) don't change status.
3. **Recovery.** A successful open marks the guide Ok, so a file restored by
   hand clears its status.

### Rows

- `GuideRowItem` gains `FileStatus`. A non-Ok row shows a caution glyph and
  the status as its first fact, with AutomationId `GuideFileStatus`.
- Opening a non-Ok row still opens the reader, which shows the specific error.
- A Library game row whose guides include any non-Ok status shows
  "1 guide needs attention" or "{n} guides need attention".

### Reader error bar

- `HtmlGuideLoadAction` is renamed `GuideLoadAction` (TXT and PDF already
  reuse it) and gains `Remove`.
- Missing (all formats), Changed (HTML and PDF), HTML NoManifest, PDF
  Damaged and TXT InvalidMetadata offer **Remove guide…**. It opens the existing `RemoveGuideDialog`. After
  a removal the shell returns to the Game page with T15.3's selection rules.
  Cancel leaves the reader on the error.

### Bad stored rows

`ListGuidesAsync` and `ListGuideSummariesAsync` catch `InvalidDataException`
and `FormatException` per row and skip that row; their signatures don't
change. `GetGuideAsync` keeps throwing. At startup, `Initialize` reads every
Guide row once with the same reader and reports the number that fail as
`StartupReconciliationReport.UnreadableGuideCount`. When it is above zero the
shell shows a warning status once: "1 guide record couldn't be read and is
hidden. Other guides open normally." ("{n} guide records…" when plural).

Assumption: schema constraints make such rows rare (only an external edit
produces one), so they are hidden rather than shown as removable rows.

### Tests

- Infrastructure: startup reports exactly the guides whose entry file was
  deleted; with one row whose `Format` is edited to an unknown value, both
  list reads return the other guides and the startup report's
  `UnreadableGuideCount` is 1.
- Core: `GuideFileHealth` transitions (unknown → Ok, Mark, Reset).
- Smoke: the existing `txt-missing`, `pdf-missing` and HTML missing-entry
  phases also assert the row's `GuideFileStatus` and the reader's
  Remove action. One mode completes the removal and asserts the row is gone
  and the other guide still opens.

## PR c: the startup runtime check

### Runtime

- After the library is ready, the shell calls
  `CoreWebView2Environment.GetAvailableBrowserVersionString` once on a
  background thread, with the same browser-folder logic as
  `HtmlReaderSession`.
- With no runtime, it shows a dismissible Warning status: "Web page guides
  need the Microsoft Edge WebView2 Runtime. Text and PDF guides still open."
  with **Get WebView2 Runtime** (`HtmlGuideLoadMessages.RuntimeDownloadUrl`).
- The per-open check is unchanged, so installing the runtime later works
  without a restart.
- **Windows App SDK runtime.** The MSIX declares the framework dependency, so
  Windows installs it or refuses to start the app before any app code runs.
  This is recorded as satisfied by packaging; no in-app check is added.

### Tests

- Smoke: `html-runtime-missing` gains a phase asserting the startup warning
  before any HTML guide is opened. The existing `progress-changed` mode keeps
  asserting that a changed TXT copy opens with the approximate notice.

## Rules that hold across all three PRs

- No error path creates, truncates or overwrites `library.sqlite`, a recovery
  copy, or a managed guide file. Remove goes through T15.3's journal only.
- No error message contains exception text, SQL, or paths other than the
  data folder and recovery copy.
- An unaffected guide still opens in every scenario above, and every smoke
  mode that breaks one guide also opens a second one.
- UI tests assert only what app code controls (AutomationIds, text, enabled
  state, navigation), not WinUI rendering.

## Docs to update

- [implementation-plan.md](implementation-plan.md): the T15.1 row gets its
  PR links as each PR merges.
- [work-breakdown.md](../work-breakdown.md): T15.1 notes that the Windows App
  SDK runtime is satisfied by the MSIX dependency.

## T15.1 verification record

### PR a: the library won't open

- **Unit tests.** On `pcsx2-win`, Infrastructure 672/672 and Core 952/952
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
- **Installed.** CI run [37928644369](https://github.com/ilya-slalom/desktop-guides/actions/runs/37928644369)
  passed `production-shell-ui` on its second attempt. The first attempt
  stopped in `core-tests` at the unrelated process-cleanup check, which timed
  out waiting for its helper; its re-run passed. In the TXT group:
  - `library-damaged` and `library-missing` showed the Library unavailable
    page with its title, body, data folder line, **Try again** and **Open
    data folder**, and the database bytes (or their absence) were unchanged;
  - after the seed restored the database, **Try again** opened the Library
    with Text Reader Game listed.
- **Rulings.** Rulings 1–24 in the [plan](t15-1-error-recovery-plan.md#rulings-against-the-spec),
  plus:
  - `StartupLeavesAnEmptyGameFolderItCannotDelete` now creates the database
    before seeding its artwork folder, because an artwork folder with no
    database is now a missing library.
- **Evidence.**
  - [Damaged library](evidence/t15-1-error-recovery/library-damaged.png)
  - [Missing library](evidence/t15-1-error-recovery/library-missing.png)

### PR b: guide health and the reader

- **Unit tests.** On `pcsx2-win`, Infrastructure 687/687 and Core 995/995
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
- **Installed.** CI run [37944327625](https://github.com/ilya-slalom/desktop-guides/actions/runs/37944327625)
  passed `production-shell-ui`. In the TXT group:
  - the Library row for Text Reader Game ended "1 guide needs attention",
    and Missing File Guide's row led with `File missing`;
  - its reader error offered **Remove guide**. Cancel kept the error and
    returned focus to Remove guide; Remove returned to the game without the
    guide, with Remove game enabled, and ASCII Map Guide still opened;
  - Web Page Guide offered **Remove guide**, and its row then led with
    `File damaged`.
  In the HTML and PDF groups, the missing entry, the missing PDF and the
  damaged PDF offered **Remove guide**.
- **Rulings.** Rulings 1–24 in the [plan](t15-1-error-recovery-plan.md#rulings-against-the-spec),
  plus:
  - removing a guide from the Game page also clears its file status, so the
    Library attention count doesn't stay stale until the next start;
  - the reader-action rename touched eight Production sites, not seven.
- **Evidence.**
  - [Missing guide row](evidence/t15-1-error-recovery/guide-missing-row.png)
  - [Remove from the reader](evidence/t15-1-error-recovery/guide-missing-remove.png)

### PR c: the startup runtime check

- **Unit tests.** On `pcsx2-win`, Core 996/996 passed, including
  `HtmlGuideLoadMessagesTests.StartupWarningSaysWhatStillOpens`.
- **Installed.** CI run [37948919282](https://github.com/ilya-slalom/desktop-guides/actions/runs/37948919282)
  passed `production-shell-ui`. In the runtime-missing pass, the Library
  showed "Web page guides need the Microsoft Edge WebView2 Runtime. Text and
  PDF guides still open." with **Get WebView2 Runtime** before any guide was
  opened, and the button reached the test launcher. The other HTML passes
  showed no warning.
- **Windows App SDK runtime.** The MSIX declares the framework dependency,
  so Windows installs it or refuses to start the app before any app code
  runs. No in-app check was added.
- **Rulings.** None.
- **Evidence.**
  - [Missing runtime at startup](evidence/t15-1-error-recovery/runtime-missing-startup.png)
