# T15.1 error recovery design

Status: design approved in brainstorming; awaiting written-spec review.
Prerequisites T03.2, T06.3, T09.1 and T10.1 are merged.

## Intent

When something in the library is broken, Desktop Guides says what is wrong,
protects what is still there, and offers one useful next step. A library
database that can't be opened stops the app before anything is written and
is never replaced by an empty one. A guide whose managed file is missing or
changed stays visible with its status and can be removed, and every other
guide still opens. A missing WebView2 Runtime is reported once at startup
without blocking text and PDF guides.

Traces: the T15.1 row of [implementation-plan.md](implementation-plan.md)
(TR15.1), [work-breakdown.md](../work-breakdown.md) T15.1, and
[p1-technical-design.md](../p1-technical-design.md) (startup recovery,
205–213; native InfoBar and ContentDialog, 401; T15.1, 868–873), and R9 in
[initial-design.md](../initial-design.md).

Decisions made during brainstorming:

- **One spec, three PRs.** (a) the library won't open, (b) guide health and
  the reader, (c) changed content and the startup runtime check. Each PR is
  independently shippable in that order; (c) uses the Remove action from (b).
- **Missing database.** A missing or 0-byte `library.sqlite` is a fresh start
  only when the data folder holds no guides, artwork or recovery copies.
  Otherwise it is an error and nothing is created.
- **Remove only.** A broken guide offers Remove through the existing T15.3
  dialog. There is no Replace-file or re-import-in-place action.
- **Health in memory.** Guide file status is detected at startup and on open
  and kept in memory. It is not stored in SQLite and needs no migration.
- **Changed content blocks.** A TXT or PDF managed copy whose size or SHA-256
  differs from its import record is not rendered, like HTML today.
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

Sources:

1. **Startup.** `StartupReconciliationReport` gains
   `IReadOnlyList<Guid> MissingGuideIds` (default empty). After the existing
   reconciliation and artwork sweep, `Initialize` checks each Guide's managed
   entry path with `File.Exists` through the existing path resolver. It hashes
   nothing and follows no links.
2. **Reader.** A failed open marks the guide. Missing marks Missing.
   Changed, PDF Damaged and TXT InvalidMetadata mark Damaged. Other errors
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
- Missing and Changed (all formats), PDF Damaged and TXT InvalidMetadata
  offer **Remove guide…**. It opens the existing `RemoveGuideDialog`. After
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
- Smoke: the existing `txt-missing`, `pdf-missing` and HTML missing or
  changed modes also assert the row's `GuideFileStatus` and the reader's
  Remove action. One mode completes the removal and asserts the row is gone
  and the other guide still opens.

## PR c: changed content and the startup runtime check

### TXT

`ManagedTextDecoder` already computes `ContentChanged`. When it is true the
decoder returns `TextGuideLoadFailed(TextGuideLoadError.Changed)` (a new
value) instead of the document, and `TextGuideLoaded` drops its
`ContentChanged` flag. Nothing renders and no progress is written.

### PDF

`ManagedPdfGuideLoader` already hashes the file. It compares the length and
hash with `Guide.ContentBytes` and `Guide.ContentSha256` and returns the
existing `PdfGuideLoadError.Changed` on a mismatch, before the document is
opened.

### Copy

All three formats use:

- Changed: "This guide's file changed after it was imported, so it can't be
  opened safely. Remove it, then import the original again."
- Missing: "This guide's file is missing from the library. Remove it, then
  import the original again."

Both offer **Remove guide…** and mark the guide's status. The existing
"Re-import it" wording is replaced.

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

- Core: a TXT copy with one changed byte (same length) → Changed; a changed
  length → Changed.
- Infrastructure: a PDF copy with one changed byte → Changed, and the PDF
  document is never opened.
- Smoke: `txt-changed` and `pdf-changed` modes tamper with the managed copy
  after the seed's import, then assert the error bar, the Remove action and
  the row status. `html-runtime-missing` gains a phase asserting the startup
  warning before any HTML guide is opened.

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
