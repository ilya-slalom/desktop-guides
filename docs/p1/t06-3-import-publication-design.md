# T06.3 import publication design

Status: implemented and verified 30 September 2026 on `feat/p1-t06-3-import-publication`;
see the [verification record](#t063-verification-record).
Prerequisites T03.2, T03.3, T15.2 (M0 and M1, PRs #3–#5) and T06.2 (PR #17)
are merged.

## Intent

A person who has a valid import preview presses **Import**, and the guide
becomes part of the library. Its bytes are copied into app-owned storage,
fingerprinted, validated again and published with its metadata, so the
guide stays readable after the original is moved or deleted. The original
is never written. A cancelled, failed or interrupted import leaves no listed
guide and no app-owned residue: failures roll back immediately, and a crash
is rolled back by the T15.2 startup reconciler.

Traces: the T06.3 row of [implementation-plan.md](implementation-plan.md)
(TR06.1–TR06.3), [work-breakdown.md](../work-breakdown.md) T06.3, and
[p1-technical-design.md](../p1-technical-design.md) (fingerprints, 119–122;
cross-boundary protocol, 124–133; T06.3, 566–572).

Decisions made during brainstorming:

- **After success** the dialog closes and the Game page re-renders with the
  new guide selected and focused. Opening managed guides in a reader is M3.
- **Write gate:** the import holds the single library write gate for its
  whole duration, as the technical design states. Other writes, including
  reading-position saves, wait until the import finishes. This is acceptable
  while the import dialog is modal; revisit it if imports move to the
  background or T12.2 measures a missed five-second save.
- **Cancellation** is honoured until the publish transaction starts.
- **Duplicates** import as a second copy until T06.4 adds the
  `Open existing` / `Import another copy` choice.
- **No schema change.** `Guides.ContentSha256` is the fingerprint; the
  schema stays at version 3.
- **Out of scope:** duplicate detection (T06.4), cloud reparse tags
  ([#18](https://github.com/ilya-slalom/desktop-guides/issues/18)), and
  opening the imported guide in a reader (T08–T10).

## Components

### `GuideImportPublisher` (Infrastructure/Import)

```csharp
public Task<Guid> PublishAsync(
    ImportManifest manifest, Guid gameId, string title,
    IProgress<ImportProgress>? progress, CancellationToken token);
```

Returns the new Guide ID or throws `GuideImportException` with a typed
`ImportIssue`, or `OperationCanceledException`. The publisher is public
because Production can't see Infrastructure internals. The title is checked
with `GuideTitle.Create`. `ImportProgress(double Fraction, bool Publishing)`
is a Core record; `Publishing` tells the dialog that cancellation no longer
applies. An internal constructor takes the test seams listed under
[Testing](#testing).

### Repository entry point

The repository's write gate is private and not re-entrant, so
`SqliteLibraryRepository` gains one internal method that takes the gate,
runs the work and releases it:

```csharp
internal Task<T> RunImportAsync<T>(
    Func<IImportJournal, CancellationToken, Task<T>> work,
    CancellationToken token);
```

`IImportJournal` is synchronous, since the whole import already runs on the
thread pool under the gate. It never takes the gate itself:

- `Prepare(Guid operationId, Guid guideId)` commits a `Prepared`
  Import row whose manifest comes from `FileOperationManifest.Create`.
- `Publish(NewImportedGuide guide, Action beforeCommit)` inserts `Guides`,
  an empty `ReadingStates` row and an empty `ReaderPreferences` row, and
  deletes the operation row, in one transaction.
- `RollBack(Guid operationId)` removes the import's owned directories, then
  its row.

All SQL stays in the repository, so `ValidateDatabase` and the existing
transaction pattern (`AddLinkedGameAsync`) cover it.

### Rollback

`FileOperationReconciler.RollBackImport` handles an in-process failure. It
deletes the staged tree through `OwnedGuideTree` when it exists, and
otherwise deletes `content/<guide>`. `Directory.Move` is atomic, so while
the stage exists, `content/<guide>` isn't this import's, and a pre-created
content directory survives. It then removes an empty `.staging/<op>` parent
and deletes the row. A crash is resolved by the unchanged startup `Run`.

## Flow

Everything below runs inside `RunImportAsync`.

1. **Re-check the source.** Its size and last-write time must match
   `ImportSource`; otherwise `Changed`. A missing file is `Missing`. HTML
   runs `StaticHtmlImportValidator.PreviewAsync` again to get the asset
   list, since `HtmlImportManifest` carries only counts.
2. **Prepare.** Allocate the Operation and Guide IDs and commit the
   `Prepared` row. From here the staged and content directories are owned.
3. **Copy and hash.** Stream-copy each file into
   `.staging/<op>/<guide>`, hashing with SHA-256 as it is written. Each
   staged file is created with `FileMode.CreateNew` and flushed with
   `Flush(flushToDisk: true)`. Progress reports bytes copied over the total.
4. **Validate the staged copy.**
   - The source is re-checked first, as in step 1, which catches a change
     made during the copy and reports it as `Changed` rather than a decode
     error.
   - TXT decodes strictly with the chosen code page, or strict UTF-8 when
     it is null.
   - PDF re-opens with `ReadPdf` and has the preview's page count.
   - HTML runs `VerifyStagedAsync` against the staged root.
5. **Rename** `.staging/<op>/<guide>` to `content/<guide>` with
   `Directory.Move`. Both are under `LibraryRoot`; an existing destination
   is an error. The now-empty `.staging/<op>` folder is then deleted.
6. **Publish** with `IImportJournal.Publish`.

Any exception from step 2 up to the end of step 6 runs the rollback and
rethrows the mapped issue. The source is only ever opened with
`FileAccess.Read` and `FileShare.Read`.

## Managed layout

Under `library/content/<guide>/`:

| Format | Files |
| --- | --- |
| TXT | `guide.txt` |
| PDF | `guide.pdf` |
| HTML | The scanner's managed names (`StaticAsset.RelativePath`): the entry keeps its validated name, or becomes `guide.html` when it contains `%`, with its companion folder as `__desktop_guides_files/`. |

Every destination goes through `ILibraryPaths.GetPlannedGuideFile`. Only
files listed by the scanner are copied; anything else in the source folder
is ignored. The original file name is recorded only as `SourceLabel`.

## Fingerprint

`ContentSha256` is lowercase hex.

- **TXT and PDF:** SHA-256 of the copied bytes.
- **HTML:** SHA-256 of the UTF-8 text formed by one line per file,
  including the entry, sorted by ordinal comparison of the managed relative
  path:

  ```text
  <managed relative path>\t<lowercase hex SHA-256 of the file>\n
  ```

  Each staged file's hash must equal the scanner's `StaticAsset.Sha256`;
  a mismatch is `Changed`.

## Metadata

| Column | Value |
| --- | --- |
| `Id` | New Guide ID |
| `GameId` | The selected game |
| `Title` | The validated dialog title |
| `Format` | `Txt`, `Html` or `Pdf` |
| `ManagedRelativeRoot` | `content/<id>` |
| `PrimaryRelativePath` | `guide.txt`, `guide.pdf` or the HTML entry |
| `ContentSha256` | The fingerprint |
| `ContentBytes` | Total bytes copied |
| `SourceLabel` | The original file name, not truncated (NTFS caps names at 255 UTF-16 units) |
| `TextCodePage` | 437 or 1252 for a legacy TXT choice; NULL for UTF-8 and other formats |
| `ImportedUtcMs`, `UpdatedUtcMs` | The repository clock |

The source path, any PDF password and the preview warnings are not stored.

## Errors

`ImportIssue` gains `NotEnoughSpace` and `SaveFailed`.

| Situation | Issue | Message |
| --- | --- | --- |
| Source gone before or during the copy | `Missing` | "The file is no longer there. Choose it again." |
| Size, last-write time or an HTML asset hash changed | `Changed` | "The file changed after it was checked. Choose it again to see the new version." |
| Read denied, sharing violation, or a cloud file that can't be downloaded | `Unreadable` | Existing wording |
| Staged copy fails re-validation | `Unreadable` | Existing per-format wording |
| `ERROR_DISK_FULL` or `ERROR_HANDLE_DISK_FULL` | `NotEnoughSpace` | "There isn't enough free space to import this guide." |
| Any other managed write, rename or commit failure, including a foreign-key failure if the game is gone | `SaveFailed` | "The guide couldn't be saved to your library. Nothing was changed." |

Messages never include the source path or the underlying exception. The
app has no logging; any future logging carries only the issue and the
operation ID. If the rollback itself fails,
the original issue still surfaces and the `Prepared` row stays for the
startup reconciler.

## Dialog

- `ImportGuideDialog` gains a primary **Import** button, enabled only when
  the title is valid, a manifest exists and any required encoding has been
  chosen.
- While importing, the existing busy panel shows a determinate progress bar
  and **Cancel**. **Import** and **Choose another file** are disabled.
  Cancel is disabled once publication starts.
- Cancelling returns to the preview with the title and encoding unchanged
  and no error bar.
- A failure shows the issue message in the existing Error `InfoBar` and
  keeps the preview.
- Closing the window cancels the import and waits for it to stop, through
  the existing closing deferral.
- On success the dialog sets `ImportedGuideId` and hides. `Hide()` makes
  `ShowAsync` return `None`, so `ShellWindow` checks the ID, then sets `pendingGuideFocus` to the new ID and calls
  `RenderCurrentAsync`.

## Testing

Test seams, internal and injected through the publisher's constructor:

- `Func<Guid>` allocates IDs, so a test knows the paths in advance.
- `Func<string, Stream>` creates staged files, so a test can inject a
  disk-full stream.
- `Action<ImportCheckpoint>` fires at `Prepared`, `Copied` (after every file is copied, before
  verification), `Verified`,
  `Renamed` and `InCommit` (inside the transaction, before `Commit()`).
- The rollback delegate defaults to the reconciler's rollback.

### Infrastructure tests (TDD, real SQLite and NTFS on `pcsx2-win`)

1. **Happy path** for TXT (UTF-8 and 437), PDF, and HTML with nested CSS
   and a `%`-named entry: correct `Guides` row, empty state rows, managed
   bytes and hash, a pinned HTML fingerprint, the original unchanged
   (`FileFingerprint`), no operation row and no `.staging` residue.
2. **Survives the original:** after the source is deleted,
   `ResolveExistingGuideFile` finds the managed file and its bytes hash to
   `ContentSha256`.
3. **Source changes:** removed before copy is `Missing`; grown between
   preview and Confirm is `Changed`; rewritten at `Copied` is `Changed`;
   an HTML asset edited after the preview is `Changed`; a source locked
   with `FileShare.None` is `Unreadable`.
4. **Cancellation** at each checkpoint before `InCommit`:
   `OperationCanceledException`, rolled back, nothing listed, original
   unchanged.
5. **Injected faults:** a disk-full stream is `NotEnoughSpace`; a
   pre-created `content/<id>` is `SaveFailed`; a throw at `InCommit` is
   `SaveFailed` with the SQL transaction rolled back; deleting the game at
   `Renamed` is `SaveFailed`. Each ends fully rolled back.
6. **Crash points:** with a no-op rollback, throw at each checkpoint, then
   run the real `FileOperationReconciler`. No guide, no staged or content
   directory and no operation row remain; the report has one resolved
   operation; a later import succeeds.
7. **Rollback failure:** a failing rollback still surfaces the original
   issue and leaves the row for startup.
8. **Write gate:** `SaveReadingLocationAsync` issued while an import is
   paused at `Copied` completes only after the import releases the gate.

### Installed smoke mode `import-publish`

Extends `tools/p1/windows_shell_ui_smoke.ps1`. The scenario picks the TXT
fixture and presses **Import**, then asserts that the dialog closes, the
Game page lists the new guide selected and focused, and `signed-install.json`
records one guide and no file operations. It captures light and dark
screenshots for the PR, and asserts only what app code controls.

### Traceability

- **TR06.1** (the original is never written): tests 1, 3 and 4.
- **TR06.2** (publication only after validation and staging; failures
  remove managed files): tests 4–7.
- **TR06.3** (fingerprints and IDs recorded): tests 1 and 2.

## Documentation

On completion, update [progress.md](../progress.md), the T06.3 row status
in [implementation-plan.md](implementation-plan.md) and a verification
record in this file.

## PR outcome

The PR names T06.3 as the target, lists the four prerequisites as merged,
and states the outcome: pressing **Import** publishes a TXT, HTML or PDF
guide that survives removal of the original, with no partial guide after
cancellation, failure or crash. It includes light and dark screenshots of
the dialog's Import state and the Game page after import.

## T06.3 verification record

- **Unit tests.** `core-tests` in CI run [36669062806](https://github.com/ilya-slalom/desktop-guides/actions/runs/36669062806): 198 Core and 333
  Infrastructure passes. They include:
  - `GuideImportPublisherTests`: TXT, PDF and HTML publication, source
    changes, cancellation, injected faults, crash points, rollback failure
    and the write gate.
  - `ImportJournalTests`.
  - `GuideFingerprintTests`.

  The same suites pass on `pcsx2-win`.
- **Installed import scenario.** `production-shell-ui` in the same run,
  light and dark:
  - Import is disabled until CP437 is chosen for `txt-legacy`.
  - Import closes the dialog.
  - The Game page lists the new guide selected and focused.

  The preview runs still end with no guide or file operation. After both
  publish runs, `importState` shows 2 guides, 0 file operations, 0 staging
  entries, 2 content directories and 2 guides with code page 437.
- **Rulings.** From the plan, with the details in its Rulings section:
  - `PublishAsync` is public and takes a `string` title.
  - Progress is `IProgress<ImportProgress>`.
  - The journal is synchronous, and `RollBack` replaces `AbandonAsync`.
  - In-process rollback keeps a content directory the import doesn't own.
  - The `Copied` checkpoint replaces `MidCopy`.
  - The source is re-checked before format verification.
  - The dialog reports success through `ImportedGuideId`.
  - There is no logging.
  - The smoke publishes two guides.
  - `SourceLabel` isn't truncated.
  - The smoke shares its helpers by branching.

  From the execution ledger:
  - The fingerprint test uses the file's `Manifest<T>` helper.
  - Theory tests take checkpoint names as strings, because the enum is internal.
  - The publisher deletes the empty `.staging/<op>` folder after the move.
  - TXT and PDF re-checks use size and last-write time, not a re-hash.
- **Open follow-ups.** From the final review, all minor:
  - The smoke doesn't edit the title during an import. That case (Review Focus 1) was checked by code review only.
  - A failed delete of the empty `.staging/<op>` folder rolls back an import that otherwise succeeded.
  - A read error before `Prepare` shows the generic message.
  - The dialog's validation handlers have no `importing` guard.
- **Evidence.** [import-ready (light)](evidence/t06-3-import-publication/import-ready-light.png),
  [import-published (light)](evidence/t06-3-import-publication/import-published-light.png),
  [import-ready (dark)](evidence/t06-3-import-publication/import-ready-dark.png)
  and [import-published (dark)](evidence/t06-3-import-publication/import-published-dark.png).
