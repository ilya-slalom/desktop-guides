# T20.2 export and restore design

Status: PR a merged in #78; PR b (restore engine) in review; PR c planned.
Prerequisites T14.4 (PR #54), T15.1 (PRs #74–#76), T15.4 (PR #62) and T20.1
(PR #59) are merged.

## Intent

Someone can save their whole library to a `.zip` outside app data from
Settings, and later replace the library with that backup, on this machine or
a clean installation. The app tells them once, after their first import, that
uninstalling removes the live library and where Export is. Restore checks the
whole archive in a staging folder before it touches the live library, asks
before replacing anything, and never leaves the user with neither library: an
interrupted or failed swap puts the previous library back.

Traces: the T20.2 row of [implementation-plan.md](implementation-plan.md)
(TR20.1, TR20.2), [work-breakdown.md](../work-breakdown.md) S20 and T20.2,
[p1-technical-design.md](../p1-technical-design.md) (Toolkit settings
controls, 370 and 403; T20.2, 930–945), and R8 in
[initial-design.md](../initial-design.md). The archive format, exporter and
verifier are T20.1's; see the
[T20.1 design](t20-1-library-export-design.md).

Decisions made during brainstorming:

- **One spec, three PRs.** (a) the export UI and the first-import reminder,
  (b) the headless restore engine: staging, validation, swap and startup
  recovery, (c) the restore UI and the in-session swap. Each PR ships in that
  order; (a) lets users make backups before restore exists.
- **Reminder.** A one-time status InfoBar after the first successful import,
  with a **Go to Export** action. A Settings key records that it was shown.
- **Inline restore.** Restore lives in a `SettingsExpander` in Settings, not
  a dialog. Only the destructive Replace uses a native `ContentDialog`.
- **In-session swap.** Replace closes the reader and the repository, swaps
  the roots, and re-runs the shell's initialization, the path T15.1's
  **Try again** already uses. No relaunch.
- **The previous library is deleted once the restored one verifies.** It is
  parked in `.recovery/` only for the duration of the swap. The confirmation
  dialog suggests exporting first.
- **Restore replaces everything in `library/`:** the database, with games,
  provider metadata, guides, reading state, per-guide preferences and the
  app Settings row, plus every guide and artwork file. Provider keys
  (`providers.bin`) and the cache root are not touched.
- **Older schemas migrate after the swap** through the normal startup
  `MigrateOrValidate`, which also writes its usual recovery copy. A newer
  schema is refused before anything is staged further.
- **Merging two libraries is out of scope** for P1, as the technical design
  says.

## Rulings against the spec

- **Packaged protected roots cover all of `%LOCALAPPDATA%` and `%APPDATA%`.**
  The technical design says only "outside the package's app-data parent".
  MSIX redirects a packaged app's writes under those folders, outside
  `Packages`, into the package store, so a backup there is lost on
  uninstall (handed over by T20.1's verification record). Rejecting both
  folders whole is simpler and honest; Documents, Desktop, other drives and
  USB drives remain allowed.
- **The prior root is parked under `.recovery/restore-<stageId>/library`,**
  not renamed to `.recovery` itself, which already holds migration recovery
  copies.
- **"Quarantine" renames and then deletes.** An unverified promoted root is
  renamed to `.recovery/restore-<stageId>-unverified/` so the prior root can
  move back at once, then deleted on a best-effort basis. The user still has
  the `.zip`; keeping the copy would silently double disk use. A copy that
  can't be deleted is removed at the next startup.
- **Only the session that swapped may confirm the restore.** A later startup
  that finds a promoted root and a marker treats it as an interrupted
  startup and rolls back, as the spec says, instead of trying to verify it.
- **Case and Unicode-normalization duplicates** that T20.1's `Parse` accepts
  are rejected in staging, as T20.1's deferred minors require.

## PR a: export UI and the first-import reminder

### Destination policy (Core/Backup)

```csharp
public static class BackupDestinationPolicy
{
    public static IReadOnlyList<string> ProtectedRoots(
        bool packaged, string dataRoot, string localAppData, string roamingAppData);
}
```

- **Packaged:** the parent of `LocalFolder` (the package folder), plus
  `%LOCALAPPDATA%` and `%APPDATA%`.
- **Portable:** `%LOCALAPPDATA%\DesktopGuides`, which holds both the data and
  the cache root.

The Production host computes the list and passes it to
`LibraryExportOptions.ProtectedRoots`. The exporter's existing check matches
by whole path segment, case-insensitively, and resolves a linked parent
folder; a match fails with `DestinationNotAllowed`.

### Messages (Core/Backup)

`LibraryBackupMessages.ForExport(LibraryExportIssue, …)` maps every issue to
one plain sentence, so a test can check each issue has text:

| Issue | Message |
| --- | --- |
| `DestinationNotAllowed` | Choose a folder outside the app's data, such as Documents or a USB drive. Backups saved in app data are removed when the app is uninstalled. |
| `DestinationUnavailable` | That folder isn't available. Choose another folder. |
| `DestinationExists` | A file with that name appeared while saving. Try again. |
| `RecoveryIncomplete` | An earlier change to the library didn't finish. Restart the app, then try again. |
| `DatabaseInvalid` | The library database couldn't be copied. Restart the app, then try again. |
| `ManagedFilesDamaged` | Some guide files are missing or damaged: *A*, *B*, *C* and N more. Remove or re-import them, then try again. |
| `LibraryTooLarge` | The library is too large to back up in one file. |
| `WriteFailed` | The backup couldn't be written. Check the drive has free space, then try again. |
| `VerificationFailed` | The saved backup didn't match the library, so it was deleted. Try again, or choose another drive. |

`ManagedFilesDamaged` names up to three guide or game titles, looked up from
the IDs the exception carries.

### Settings (Infrastructure)

`AppSettings` gains `bool ExportReminderShown`, stored as the
`ExportReminderShown` key in the `Settings` table by `ReadSettings` and
`WriteSettings`. The table is key/value, so there is no migration; a missing
key reads as false.

### Shell (new `ShellWindow.Backup.cs` partial)

- **Export card.** The static `LibraryStorageSettingsCard` becomes
  `ExportSettingsCard`: header **Back up library**, description "Save your
  games, guides and reading progress to a .zip file. Uninstalling the app
  removes your library, so keep the backup outside app data." and an
  **Export…** button (`ExportBackupButton`).
- **Picker.** `Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id)`
  with a `.zip` choice and the suggested name
  `DesktopGuides-backup-YYYY-MM-DD.zip` (local date). The system dialog asks
  before replacing a file, so a picked existing file exports with
  `overwrite: true`. A picker failure shows a status error, as import does.
- **Running.** The card shows `ExportProgress`, a determinate `ProgressBar`
  driven by `LibraryExportProgress` (Preparing is indeterminate), a phase
  label and **Cancel** (`ExportCancelButton`). While it runs, the navigation
  pane and the library commands are disabled (`libraryBusy`): the export
  holds the write gate, so any write would only stall. Closing the window
  cancels the export and waits for it before `CloseWhenIdleAsync` releases
  the lease.
- **Result.** Success shows the status InfoBar: "Backup saved:
  *name* (12 games, 40 guides, 310 MB)." Cancellation shows "Export
  canceled." Failure shows the table's message as an error.
- **Reminder.** After `ImportGuideClicked` succeeds, if
  `ExportReminderShown` is false, the shell saves it as true, then shows the
  status InfoBar: "Your library lives in app data, and uninstalling the app
  removes it. Export a backup from Settings." with the `ShellStatusAction`
  button **Go to Export**, which opens Settings and focuses
  `ExportBackupButton`. If the save fails the reminder still shows; it may
  show again after the next import.

### Tests

- **Core:** `ProtectedRoots` for both builds; every `LibraryExportIssue` has
  a message; `ManagedFilesDamaged` lists three titles and "N more".
- **Infrastructure:** `ExportReminderShown` round-trips and defaults to
  false; the exporter rejects a destination under each packaged root.
- **Installed smoke** (`windows_shell_ui_smoke.ps1`), driving the real save
  dialog through UIA as the import smokes drive the open dialog:
  - `export-backup`: export to a temp folder; the file exists and the seed
    tool's verify command accepts it;
  - `export-cancel`: Cancel leaves no file and no `.tmp`;
  - `export-protected`: a path under app data is refused with the message;
  - `import-reminder`: the first import shows the reminder and **Go to
    Export** focuses the button; a second import doesn't show it.

## PR b: the restore engine

### Contracts (Core/Backup)

```csharp
public enum LibraryRestorePhase { Copying, Checking, Extracting }

public readonly record struct LibraryRestoreProgress(
    LibraryRestorePhase Phase, long BytesDone, long BytesTotal);

public enum LibraryRestoreIssue
{
    SourceUnavailable, ArchiveInvalid, ArchiveUnsafe, NotEnoughSpace,
    NewerVersion, DatabaseInvalid, ReferencesInvalid, SwapFailed
}

public sealed class LibraryRestoreException(
    LibraryRestoreIssue issue, IReadOnlyList<Guid>? guideIds = null,
    IReadOnlyList<Guid>? gameIds = null, Exception? inner = null) : Exception

public sealed record LibraryRestoreStage(
    Guid StageId, DateTimeOffset CreatedUtc, string AppVersion,
    int SchemaVersion, int Games, int Guides, long Bytes);
```

`LibraryOpenIssue` gains `RestoreIncomplete`.

### Restorer (Infrastructure/Storage)

```csharp
public sealed class LibraryRestorer
{
    public LibraryRestorer(ILibraryPaths paths, string dataRoot);
    public Task<LibraryRestoreStage> StageAsync(string zipPath,
        IProgress<LibraryRestoreProgress>? progress, CancellationToken token);
    public Task ReplaceAsync(LibraryRestoreStage stage, CancellationToken token);
    public void DiscardStage(LibraryRestoreStage stage);
}
```

An internal constructor adds an `Action<RestoreCheckpoint>` test hook, as the
importer and exporter have.

### Staging

The stage folder is `<data>/.restore-staging/<stageId>/`, a sibling of
`library/` on the same volume, so the swap is a rename. Each check below
fails with the named issue; on any failure or cancellation the stage folder
is deleted and `library/` is untouched.

1. **Copy** (`SourceUnavailable`, `NotEnoughSpace`). The data volume needs
   the `.zip`'s size free plus 10%. Copy it to `archive.zip` in the stage
   folder, so later checks read a local file the user can't change
   mid-check. A read error fails with `SourceUnavailable`.
2. **Archive** (`ArchiveInvalid`). `LibraryArchiveVerifier.Verify`: the
   manifest is first, `Parse` succeeds, the entry set matches the manifest,
   and every length and SHA-256 matches. This also enforces the
   250,000-entry and 64 MiB manifest caps.
3. **Names** (`ArchiveUnsafe`). Every entry passes
   `LibraryArchiveManifest.ValidatePath` again; no two entries are equal
   ignoring case after NFC normalization; every full target path stays under
   the stage's `library/`.
4. **Space** (`NotEnoughSpace`). The expanded size is the manifest's
   `counts.bytes`; the volume needs that plus 10% free. There is no byte cap
   beyond free space: T20.1's entry and manifest caps already bound the
   archive's shape, and a declared size can't be exceeded (step 5).
5. **Extract.** Each entry streams to its target with `FileMode.CreateNew`,
   writing at most its declared length (a longer stream fails
   `ArchiveInvalid`) and hashing as it writes. No reparse point is created
   or followed.
6. **Database** (`DatabaseInvalid`, `NewerVersion`). Open the staged
   `library.sqlite`. `user_version` above `LibrarySchema.CurrentVersion`
   fails with `NewerVersion`. Otherwise `ValidateDatabase` at its own version
   (`integrity_check`, `foreign_key_check`, exact schema), which becomes
   `internal static`; and `FileOperations` must be empty.
7. **References** (`ReferencesInvalid`, with IDs). `LibraryArchivePlan.Read`
   on the staged database must produce exactly the manifest's entries: each
   TXT or PDF guide's primary file, each HTML guide's `GuideAssets` rows
   (sizes and hashes summing to `ContentBytes` and fingerprinting to
   `ContentSha256`), and each `Games.ArtworkRelativePath` file. A reference
   with no entry, such as missing artwork, or an entry with no reference,
   fails with the affected guide and game IDs. This is TR20.1.

`StageAsync` returns the stage with the manifest's counts and creation time.
`DiscardStage` deletes the folder.

### Swap

`ReplaceAsync` uses only paths. The caller guarantees that no repository is
open and that the process holds the session lease.

The marker is `<data>/restore.marker`: UTF-8 JSON holding `stageId`,
`priorExists` and `priorPath` (`<data>/.recovery/restore-<stageId>/library`),
written to a temporary file, flushed and renamed into place.

1. Write the marker. (`MarkerWritten`)
2. If `library/` exists, move it to `priorPath`. (`PriorMoved`)
3. Move the stage's `library/` to `library/`. (`Promoted`)

A failure at step 2 deletes the marker and fails with `SwapFailed`. A failure
at step 3 moves the prior root back, deletes the marker and fails with
`SwapFailed`. The marker stays after step 3: startup confirms the restore.

### Startup recovery

`LibraryRestoreRecovery.Run(dataRoot, paths, verifyRestore)` runs in
`InitializeCoreAsync` right after the lease is acquired and before the
repository opens. It decides from what is on disk, so every step is
idempotent:

- **No marker:** delete any `.restore-staging/*` and any `.recovery/restore-*`
  folder. Done.
- **Marker, and the stage's `library/` still exists** (the swap didn't
  promote): move `priorPath` back to `library/` if it is there, delete the
  marker and the stage. Done.
- **Marker, promoted root in place, `verifyRestore` false** (an interrupted
  startup): roll back.
- **Marker, promoted root in place, `verifyRestore` true** (the session that
  swapped): return `PendingVerify`. The shell opens the repository with the
  normal `InitializeAsync`. On success it calls `Complete`, which deletes
  `priorPath` and the stage, then the marker last. On failure it calls
  `RollBack` and initializes again.
- **Roll back:** rename the promoted root to
  `.recovery/restore-<stageId>-unverified/`, move `priorPath` back if
  `priorExists`, delete the marker, then delete the unverified copy on a
  best-effort basis. The shell shows: "The backup couldn't be opened, so your
  previous library was kept."
- **Can't proceed**, for example `priorPath` can't be moved back: throw
  `LibraryOpenException(RestoreIncomplete)`, which shows T15.1's Library
  unavailable page with **Open data folder** and **Try again**, naming the
  `.recovery` folder.

`IsFirstRun`'s "content but no database" check ignores `.restore-staging`
and the marker.

### Tests

TDD, real SQLite, under `tests/DesktopGuides.Infrastructure.Tests/Restore/`.

- **Round trip:** `ExportFixture` → export → stage → replace → recovery with
  `verifyRestore` → `InitializeAsync` → `Complete`; the library's
  `LibrarySnapshot` equals the exported one, and no marker, parked root or
  stage folder remains. Both a clean and a populated target.
- **Staging cells**, one per issue, from a `HostileArchive` test builder:
  truncated ZIP; flipped byte; an entry missing from the manifest; an extra
  entry; `..\` and absolute paths; case and NFC duplicates; a stream longer
  than its declared length; a newer schema; a corrupt database page; a
  dangling foreign key; a leftover `FileOperations` row; missing artwork; an
  HTML asset-sum mismatch; not enough space (injected free-space probe).
  Each leaves no stage folder and an unchanged live snapshot.
- **Cancellation** during copy and during extraction leaves no stage folder.
- **Swap cells**, T15.4 style: for each of `MarkerWritten`, `PriorMoved` and
  `Promoted`, a populated and a clean target, with an in-process throw and a
  simulated crash followed by recovery with `verifyRestore` false. Each ends
  with the prior snapshot exactly, or no library if there was none.
- **Verification:** a promoted database that fails `InitializeAsync` rolls
  back to the prior snapshot.
- **Rollback idempotency:** a crash at each rollback step, then recovery
  again, reaches the same state.
- **`RestoreIncomplete`:** a `priorPath` that can't be moved back.
- **NTFS cells (Windows):** a junction at `priorPath`, at the stage's
  `library/` or at `library/` is refused.

## PR c: restore UI and the in-session swap

### Restore expander

Below the Export card in the Library section, `RestoreSettingsExpander`:

- **Idle:** header **Restore from backup**, description "Replace this library
  with one from a backup file." and **Choose backup…**
  (`ChooseBackupButton`), which opens a `FileOpenPicker` filtered to `.zip`.
- **Staging:** the expander opens with a determinate progress bar and phase
  label (Copying, Checking, Extracting) and **Cancel**. `libraryBusy` is set
  as for export. Closing the window cancels and discards the stage.
- **Staged:** one `SettingsCard` per detail:
  - "Backup made: 8 October 2026, 16:12, by version 1.0.0.0"
  - "Backup holds: 12 games, 40 guides, 310 MB"
  - "This library has: 9 games, 31 guides"

  and **Replace library…** (`ReplaceLibraryButton`) and **Discard**.
  Leaving Settings keeps the stage; closing the window or a later startup
  discards it.
- **Errors** show in the expander's InfoBar, mapped by
  `LibraryBackupMessages.ForRestore`:

| Issue | Message |
| --- | --- |
| `SourceUnavailable` | The backup file couldn't be read. Check it's still there, then try again. |
| `ArchiveInvalid` | This file isn't a Desktop Guides backup, or it's damaged. |
| `ArchiveUnsafe` | This backup contains file names that aren't allowed, so it wasn't opened. |
| `NotEnoughSpace` | There isn't enough free space to restore this backup. It needs *N* GB. |
| `NewerVersion` | This backup is from a newer version of Desktop Guides. Update the app, then try again. |
| `DatabaseInvalid` | The library database in this backup is damaged. |
| `ReferencesInvalid` | This backup is missing files for: *A*, *B*, *C* and N more. |
| `SwapFailed` | The library couldn't be replaced. Close other programs that might be using it, then try again. |

`ReferencesInvalid` titles come from the staged database.

### Confirmation

`ReplaceLibraryDialog.Create(current, staged, XamlRoot)`, a `ContentDialog`
like `RemoveGuideDialog`, with `DialogSurface.Apply`:

- Title: "Replace your library?"
- Body: "Your 9 games and 31 guides, with their reading progress, will be
  replaced by the backup's 12 games and 40 guides. This can't be undone. To
  keep the current library, export it first." For an empty library: "The
  backup's 12 games and 40 guides will be restored."
- **Replace** (primary) and **Cancel** (close, default).

### In-session swap

After **Replace**, inside `RunNavigationAsync`:

1. `ready = false`; close the reader session and progress tracking.
2. Await the navigation queue's other work and the refresh tasks.
3. Dispose the repository and the services built on it.
4. `ReplaceAsync(stage)`. `SwapFailed` reopens the prior library with the
   error.
5. `InitializeCoreAsync(verifyRestore: true)`, which runs startup recovery,
   opens the restored library, and calls `Complete` or `RollBack`.

Success opens the Library route and shows "Library restored: 12 games, 40
guides." The restored database's theme and window material apply, since
initialization reads them. Rollback shows the kept-library message.
`RestoreIncomplete` shows the Library unavailable page.

### Tests

- **Core:** every `LibraryRestoreIssue` has a message; the dialog text for
  empty and populated libraries.
- **Installed smoke**, with backups built by the seed tool from fixtures:
  - `restore-clean`: restore into an empty install; the games appear and a
    guide opens at its saved position;
  - `restore-replace`: Cancel in the confirmation changes nothing; Replace
    replaces a populated library and the counts match;
  - `restore-corrupt`: a truncated ZIP shows its message, the library is
    unchanged and no stage folder remains;
  - `restore-missing-artwork`: the message names the game;
  - `restore-cancel`: Cancel during staging leaves no stage folder;
  - `restore-interrupted`: the seed tool leaves a promoted root and a marker
    as a crash after the swap would; on launch the prior library is back.

## Rules that hold across all three PRs

- Imported files are untrusted, and so is a backup: every name, size, hash
  and database row is checked in staging before `library/` changes.
- The live library is never left missing: every swap failure or interruption
  restores the prior root, or no library if there was none.
- Export and restore are user initiated; nothing runs automatically.
- No credential, cache or diagnostics file is exported or restored (TR20.2).
- UI tests assert only what app code controls.
- PowerShell scripts stay ASCII-only.

## Out of scope

- Merging two libraries; restoring a single guide.
- Scheduled or automatic backups.
- Encryption: the archive is as private as the folder it's saved in.

## Docs to update

Each PR updates this status line and its verification record, the T20.2 row
and paragraph in [implementation-plan.md](implementation-plan.md),
[progress.md](../progress.md), and [results.md](results.md). PR c also
corrects the stale section 2 of
[p1-technical-design.md](../p1-technical-design.md) (`GameMetadataLinks`,
`games/<game-id>` roots), which T20.1 flagged, and attaches screenshots of
the Export card, the staged Restore expander and the confirmation dialog to
its PR. PR a's PR includes a screenshot of the Export card and the reminder.

## T20.2 verification record

### PR a: export UI and the first-import reminder

- **Unit tests.** On `pcsx2-win`, Core 1018/1018 and Infrastructure 694/694
  passed. The new tests are:
  - `BackupDestinationPolicyTests`: the protected roots, including the
    packaged app-data root, and what each refusal says;
  - `LibraryBackupMessagesTests`: the export copy, plurals, sizes and
    damaged-guide titles;
  - `ExportReminderSettingTests`: `ExportReminderShown` defaults to false
    and round-trips through the settings table;
  - `LibraryExporterTests`: characterization of the packaged roots.
- **Installed.** The `backup` group passed in CI run
  [38027063797](https://github.com/ilya-slalom/desktop-guides/actions/runs/38027063797)
  (head `11a60ea`), with these phases:
  - `export-reminder-shown`, `export-reminder-opens-settings` and
    `export-reminder-once`: the first import shows the reminder, **Go to
    Export** opens Settings at the Export card, and a second import
    doesn't repeat it (a 3 s polling watch of the status bar and the
    content status);
  - `export-saved`: Backup saved, with the counts;
  - `export-cancel-no-file` and `export-protected-refused`: Cancel and a
    protected folder leave no file;
  - `describe-backup`: `{"verified":true,"games":1,"guides":2,"files":3,"credentials":false}`.
- **Final-review fixes.** The `backup` group passed again in
  [38028956542](https://github.com/ilya-slalom/desktop-guides/actions/runs/38028956542)
  (head `896a95d`) and
  [38030077234](https://github.com/ilya-slalom/desktop-guides/actions/runs/38030077234)
  (head `570e0dd`), with the same phases. The exporter is now built when
  Export is clicked, so startup no longer resolves Roaming AppData. The
  packaged `export-protected-refused` now saves to a fresh folder under
  `%LOCALAPPDATA%`, outside `Packages` and the portable `DesktopGuides`
  folder (T20.1 already refused the app's own data folder); the portable
  run keeps the app data path. `export-saved` scrolls Settings to its end
  so the shot shows the Export card.
- **Full run.** [38025953925](https://github.com/ilya-slalom/desktop-guides/actions/runs/38025953925)
  (`shell-scope=all`, head `1c9c342`) passed every job; the design shard ran
  `design, catalog, completion, backup`, and `design-light` and
  `design-dark` passed `settings-narrow` and `settings-wide` with
  `ExportSettingsCard`.
- **CI history.**
  - [38020372769](https://github.com/ilya-slalom/desktop-guides/actions/runs/38020372769):
    the harness alone, red as intended; `import-reminder` waited for a
    reminder that didn't exist yet.
  - 38021693034: focus fell to the title bar after **Go to Export**.
  - 38022839444 and 38023775682: the Save dialog saved under its suggested
    name.
  - 38024576095: a diagnostic run for that dialog.
  - [38025373384](https://github.com/ilya-slalom/desktop-guides/actions/runs/38025373384):
    the first green `backup` group, before the fix-round history was
    squashed (head `c41d318`, same tree as `1c9c342`).
- **Rulings.** Rulings 5, 6, 8, 10 and 11 in the
  [plan](t20-2-export-restore-plan.md#rulings-against-the-spec), plus:
  - Commit trailers name the implementing model (Sonnet 5.5).
  - `GoToExportAsync` calls `SettingsPanel.UpdateLayout()` and retries
    `Focus` once on the dispatcher, because Settings is collapsed until the
    render and focus otherwise fell to the title bar.
  - `windows_shell_foreground_probe.cs` gains `GetText` and `TypeText`
    (WM_CHAR per character) because the Save dialog ignores WM_SETTEXT for
    the name it returns; `Choose-SavePath` reads the box back before
    pressing Save.
  - `export-*` modes use the 240 s smoke tier, since the picker wait and
    the Backup saved wait exceed the default 60 s.
  - `export-reminder-once` is a 3 s polling watch, not the plan's
    pattern-match check, which could pass without reading the UI.
- **Evidence.**
  - [Reminder](evidence/t20-2-export-restore/export-reminder.png), from
    run 38027063797.
  - [Backup saved](evidence/t20-2-export-restore/export-saved.png), with the
    Export card, from run 38030077234.
- **Not run.** The reminder check has only been seen passing, never failing
  on a build that shows the reminder twice. `export-cancel` and
  `export-protected` don't record whether the picker created the empty
  file. Restore (PRs b and c) is not implemented.

### PR b: the restore engine

- **Unit tests.** On `pcsx2-win`, Core 1027/1027 (after Task 6) and
  Infrastructure 758/758 (after Task 10) passed. Commits: `baed5b3` (restore
  contracts and copy), `b9c0076` (staging), `7901284` and `a812aa9`
  (validation and the set-equality fix), `da3f21b` and `50625fc` (swap and
  its fix round), `f93157e` (startup recovery).
- **Cells.**
  - Staging: 19 (`LibraryRestorerStageTests`).
  - Validation: 11 (`LibraryRestorerValidationTests`).
  - Swap: 10, plus 3 added in the fix round (a failed rollback keeps the
    marker; an existing marker refuses a second swap).
  - Recovery: 19 from the plan, plus 2 implementer guards
    (`AMissingParkedLibraryBehindAPromotedRestoreIsIncomplete` and
    `AMissingParkedLibraryBeforePromotionIsIncomplete`).
- **NTFS.** All cells ran on Windows. The two swap cells
  `AJunctionedRecoveryFolderFailsTheSwap` and `AJunctionedStageFailsTheSwap`
  create real junctions and delete them in a `finally`.
- **Rulings.** Rulings 1-4, 7, 9 and 13 in the
  [plan](t20-2-export-restore-plan.md#rulings-against-the-spec), plus:
  - `RestoreIncomplete` is excluded from the "stopped before changing
    anything" copy test: a failed restore is the one issue where the library
    may have changed, so that sentence would be false.
  - Theories over the internal `RestoreCheckpoint` take the name as a string
    and `Enum.Parse` it (a public xUnit theory can't take an internal enum:
    CS0051, xUnit1000, xUnit1010).
  - The reference check requires the staged files to equal the referenced
    set. It is defence in depth: a duplicate artwork reference is unreachable
    because the `Games.ArtworkRelativePath` CHECK ties the path to the row's
    Id (bypassing it fails `integrity_check` with `DatabaseInvalid`), so no
    test reproduces it.
  - `Replace` refuses when a marker already exists (a second swap would
    overwrite the pending marker and could orphan the original library under
    `.recovery`), and keeps the marker and throws `SwapFailed` when its
    rollback fails; startup recovery then returns the parked prior root.
  - Two tamper guards in `LibraryRestoreRecovery`: a `Swapping` marker with a
    prior library whose parked copy is missing, and an unpromoted swap whose
    prior and live roots are both gone, each return `RestoreIncomplete`
    and keep the marker. The brief's code would report `RolledBack` over the
    restored library or drop the marker.
- **Wiring.** Nothing is wired into the app yet (startup recovery and the
  in-session swap land in PR c, ruling 7), so there is no installed smoke and
  no screenshot.
- **CI.** PR run: CI_RUN_PLACEHOLDER.
- **Not run.** No separate RED was captured for Task 6 (exact-string tests
  were written with the code). The extraction byte cap is defence in depth
  with no direct test, because verification refuses an oversized entry first.
