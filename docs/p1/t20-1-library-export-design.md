# T20.1 library export archive design

Status: merged through PR #59 on 8 October 2026, merge commit `47ccf3a`;
verified by CI run 37765505021. See the verification record.
Prerequisites T03.2 (PR #4), T04.4 (PR #14), T06.3 (PR #19) and T15.2
(PR #5) are merged.

## Intent

Someone can save a copy of their whole library to a single `.zip` outside app
data, so an uninstall, a lost machine or a failed upgrade doesn't leave their
only copies of imported guides unrecoverable. T20.1 builds the archive format
and the export engine. T20.2 adds the Settings rows, the save dialog, the
first-import reminder and restore.

The archive holds a consistent snapshot: the database and the files describe
one moment, with every file checked against its recorded SHA-256. It holds
nothing beyond the library: no credentials, cache, diagnostics or unrelated
files. A cancelled or failed export leaves no file at the destination.

Traces: the T20.1 row of [implementation-plan.md](implementation-plan.md)
(TR20.2), [work-breakdown.md](../work-breakdown.md) S20 and T20.1, and
[p1-technical-design.md](../p1-technical-design.md) (the live layout, 68–83;
migrations and backup, 200–215; and T20.1, 898–907).

Decisions made during brainstorming:

- **Damaged files fail the export.** If a referenced file is missing, is a
  link, or doesn't match its recorded size or hash, export fails, names every
  affected guide and game, and writes nothing to the destination. Every
  archive therefore restores in full; T20.2's restore validates every
  reference and would reject a gap anyway.
- **File type.** A plain `.zip`, for example
  `DesktopGuides-backup-2026-10-08.zip`. Any tool can open it, which helps when
  recovering one guide by hand. T20.2 picks the default name.
- **Consistency by holding the write gate.** Export holds the library write
  gate from the recovery check until the last entry is written. No import,
  removal or edit can interleave, so no pinning or extra on-disk state is
  needed. Library writes wait meanwhile, so T20.2 keeps library edits
  disabled during an export. The verification record measures how long the
  gate is held.
- **The whole database.** The snapshot includes reading states, per-guide
  preferences, provider metadata and the app Settings row (theme, window
  material, last guide). `Guides.SourceLabel` stays: it is the original file
  name, never a path.
- **No UI in T20.1.** It has no Production wiring and no installed smoke.

## Rulings against the spec

- **The live layout in the technical design is stale.** Section 2 still
  describes `LocalFolder/DesktopGuides/`, `games/<game-id>/` and
  `GameMetadataLinks`, and section 9 validates `GameMetadataLinks`
  references. The code has:
  - packaged data directly in `LocalFolder`;
  - `library/artwork/<game>/<sha256>.<ext>` referenced by
    `Games.ArtworkRelativePath`;
  - provider metadata in `Games` columns (`ProviderName`, `ProviderGameId`,
    `MetadataJson`, `MetadataRetrievedUtcMs`);
  - schema v4 with `GuideAssets`.

  T20.1 follows the code and corrects both sections.
- **Recovery mid-session.** The design says to "reconcile all known
  FileOperations". The reconciler normally runs only at startup. Under the
  write gate no operation can be active, so export runs it, then requires
  `FileOperations` to be empty. Anything left fails with `RecoveryIncomplete`.
- **One hashing pass.** Files are hashed while they're copied into the ZIP,
  not in a separate pass first. A quick existence, size and link pass fails
  early. A hash mismatch makes the export finish scanning, so it can name every
  damaged guide, before it deletes its temporary output.
- **The snapshot is written to `%TEMP%`.** `BackupDatabase` writes the snapshot
  to `%TEMP%\desktop-guides-export-<exportId>.sqlite` on a local disk, because
  SQLite misbehaves on network shares and the destination may be one. It is
  deleted with its sidecars whatever the outcome.
- **Protected roots come from the caller.** "Outside the package's app-data
  parent" depends on the build:
  - packaged: the parent of `LocalFolder`, which holds `LocalCache` and the
    other package folders;
  - portable: `%LOCALAPPDATA%\DesktopGuides`, which also holds its cache.

  The exporter takes the list. T20.2 supplies it.

## Archive format

Inside the ZIP, paths mirror the live library:

```
manifest.json                            first entry
library/library.sqlite                   BackupDatabase snapshot
library/content/<guideId>/<path>         every referenced guide file
library/artwork/<gameId>/<sha256>.<ext>  every referenced artwork file
```

`manifest.json` is UTF-8 JSON without a byte-order mark:

```json
{
  "format": "desktop-guides-library",
  "formatVersion": 1,
  "exportId": "0f3c9a1e5b7d4c2a8e6f1b3d5a7c9e0f",
  "createdUtc": "2026-10-08T07:12:00Z",
  "app": { "version": "1.0.0.0", "build": "msix" },
  "schemaVersion": 4,
  "counts": { "games": 12, "guides": 40, "files": 133, "bytes": 123456789 },
  "entries": [
    { "path": "library/library.sqlite", "bytes": 4096, "sha256": "…" }
  ]
}
```

- **Entries.** The archive holds exactly `manifest.json` and the listed
  entries:
  - sorted ordinally by path;
  - no directory entries and no duplicates;
  - non-ASCII names use the ZIP UTF-8 flag.

  `counts.files` and `counts.bytes` cover every entry except the manifest,
  database included. `games` and `guides` count the snapshot's rows.
- **Paths.** Each path has exactly one of three shapes:
  - `library/library.sqlite`;
  - `library/content/<32 lowercase hex>/<managed relative path>`, where the
    rest follows `ManagedRelativePath` rules;
  - `library/artwork/<32 lowercase hex>/<64 lowercase hex>.(png|jpg|webp)`.
- **Timestamps.** Every entry's last-write time is `createdUtc`, so the archive
  leaks no file times and two exports of the same library at the same clock
  time are byte-identical.
- **Compression.** Entries ending in `.pdf`, `.png`, `.jpg`, `.jpeg`, `.webp`
  or `.gif` (any case) are stored as they are, since they're already
  compressed. Every other entry is deflated at the optimal level: the
  database, text, HTML, CSS and SVG.
- **Excluded:**
  - `providers.bin` and `providers.bin.tmp`;
  - `.recovery/` and `library.session.lock`;
  - `.staging/`, `.trash/` and `.artwork-staging/`;
  - the database's `-wal`, `-shm` and `-journal` files;
  - the cache root (WebView2 profiles, diagnostics);
  - any file the database doesn't reference.
- **Versions.** `formatVersion` changes only when the layout or manifest shape
  changes. `schemaVersion` is the snapshot's `user_version` and tells restore
  which migrations to run.
- **Limits.** At most 250,000 entries and a 64 MiB manifest. That is about 120
  HTML guides at the 2,048-asset import maximum; typical guides have a few
  dozen files. A larger library fails with `LibraryTooLarge`.

## Components

### Core types (Core/Backup)

```csharp
public sealed record LibraryArchiveEntry(string Path, long Bytes, string Sha256);

public sealed record LibraryArchiveManifest(
    Guid ExportId, DateTimeOffset CreatedUtc, string AppVersion, string Build,
    int SchemaVersion, int Games, int Guides, IReadOnlyList<LibraryArchiveEntry> Entries)
{
    public const string Format = "desktop-guides-library";
    public const int FormatVersion = 1;
    public const string EntryName = "manifest.json";
    public const string DatabasePath = "library/library.sqlite";
    public static byte[] Write(LibraryArchiveManifest manifest);
    public static LibraryArchiveManifest Parse(ReadOnlySpan<byte> utf8Json);
}
```

`Write` and `Parse` use `Utf8JsonWriter` and `JsonDocument`, not
reflection-based serialization, so T17.4's trimming pass has nothing to change
here. `Write` sorts the entries and computes `counts`. `Parse` throws
`InvalidDataException` for:

- an unknown `format` or `formatVersion`, or a missing field;
- a path of the wrong shape, a duplicate path, or no database entry;
- a hash that isn't 64 lowercase hex digits, or a negative size;
- counts that don't match the entries;
- an entry count or manifest size over the limits.

```csharp
public enum LibraryExportPhase { Preparing, Writing, Verifying }

public readonly record struct LibraryExportProgress(
    LibraryExportPhase Phase, long BytesDone, long BytesTotal);

public sealed record LibraryExportResult(
    string Path, int Games, int Guides, int Files, long Bytes, string Sha256);

public enum LibraryExportIssue
{
    DestinationNotAllowed, DestinationUnavailable, DestinationExists,
    RecoveryIncomplete, DatabaseInvalid, ManagedFilesDamaged,
    LibraryTooLarge, WriteFailed, VerificationFailed
}

public sealed class LibraryExportException(
    LibraryExportIssue issue, IReadOnlyList<Guid>? guideIds = null,
    IReadOnlyList<Guid>? gameIds = null, Exception? inner = null) : Exception
```

`LibraryExportResult.Bytes` is the archive's size and `Sha256` its hash.
`ManagedFilesDamaged` carries the affected guide and game IDs.

### Repository (Infrastructure/Storage)

- `internal Task<T> RunExportAsync<T>(Func<SqliteConnection, CancellationToken, Task<T>> work, CancellationToken token)`
  takes the same `writeGate` as `RunImportAsync`. The token cancels the wait
  and is passed to the work.
- The `BackupDatabase` and validation steps of `CreateRecoveryCopy` move into
  one internal helper. The migration recovery copy and the export snapshot
  both use it: reserve the file with `FileMode.CreateNew`, back up, run
  `ValidateDatabase`, and delete the file on failure.

### Exporter (Infrastructure/Storage)

```csharp
public sealed record LibraryExportOptions(
    string AppVersion, string Build, IReadOnlyList<string> ProtectedRoots);

public sealed class LibraryExporter
{
    public LibraryExporter(SqliteLibraryRepository repository, ILibraryPaths paths,
        LibraryExportOptions options, TimeProvider? clock = null);
    public Task<LibraryExportResult> ExportAsync(string destination, bool overwrite,
        IProgress<LibraryExportProgress>? progress, CancellationToken token);
}
```

An internal constructor adds an `Action<ExportCheckpoint>` test hook, as the
importer has. The checkpoints are `GateHeld`, `Snapshotted`, `Planned`,
`EntryWritten`, `Written` and `BeforeRename`.

`ExportAsync`:

1. **Destination.** It must be an absolute path ending in `.zip`, with an
   existing parent folder. That folder's full path, or its resolved target if
   it is a link, must not be inside any protected root (ordinal,
   case-insensitive, by whole path segment). An existing file without
   `overwrite` fails with `DestinationExists`.
2. **Gate.** `RunExportAsync`.
3. **Under the gate:**
   1. *Recover:* `FileOperationReconciler.Run(connection)`, then
      `SELECT COUNT(*) FROM FileOperations` must be 0. An exception or a
      remaining row fails with `RecoveryIncomplete`.
   2. *Snapshot:* the shared backup helper writes to `%TEMP%`. A validation
      failure fails with `DatabaseInvalid`.
   3. *Plan:* `LibraryArchivePlan` reads the snapshot. A TXT or PDF guide
      contributes its primary file, with `ContentBytes` and `ContentSha256`.
      An HTML guide contributes each `GuideAssets` row, with `ByteCount` and
      `Sha256`; their sum must equal `ContentBytes`, and
      `GuideFingerprint.OfHtml` of them must equal `ContentSha256`. Each game
      with `ArtworkRelativePath` contributes that file, whose expected hash is
      its name. Guide files resolve through
      `ManagedPathResolver.ResolveExistingGuideFile`. Artwork resolves through
      `ManagedArtworkStore.ResolveFile` plus the link check, which becomes
      internal. Every missing file, wrong size, link, and HTML guide whose
      asset rows disagree with its `ContentBytes` or `ContentSha256` is
      collected; if any exist, export fails with `ManagedFilesDamaged`.
   4. *Write:* create `<destination>.<exportId>.tmp` with `FileMode.CreateNew`
      and `FileShare.None`. Write `manifest.json` from the recorded values,
      then each entry in manifest order, hashing as it streams. Mismatches are
      collected and the scan continues; if any exist, export fails with
      `ManagedFilesDamaged`. An I/O error such as a full disk fails with
      `WriteFailed`.
4. **Release the gate.**
5. **Verify.** `LibraryArchiveVerifier` re-opens the temporary ZIP. The first
   entry must be the manifest. `Parse` must succeed. The other entries must
   match the manifest's entries exactly, and every entry's length and SHA-256
   must match. Anything else fails with `VerificationFailed`.
6. **Rename.** `File.Move(temp, destination, overwrite)`. If the destination
   appeared after step 1 and `overwrite` is false, export fails with
   `DestinationExists`. Cancellation stops applying once the rename starts.

On cancellation (`OperationCanceledException`) or failure before the rename,
the temporary ZIP and the `%TEMP%` snapshot are deleted, the destination is
untouched, and the gate is released. Progress reports `Preparing` until the
plan is ready, then `Writing` and `Verifying` with byte totals, at most once
per entry or per MiB.

**Why holding the gate also covers artwork.** Artwork writes and deletions run
outside the gate, but they are ordered around gated database updates. A
refresh stores the new file, switches `ArtworkRelativePath` under the gate,
then deletes the old file. A game removal deletes its artwork after its gated
commit. While export holds the gate, no file referenced by its snapshot can
be deleted.

### Verifier (Infrastructure/Storage)

`internal static LibraryArchiveManifest LibraryArchiveVerifier.Verify(Stream zip, CancellationToken token)`
performs step 5 and returns the parsed manifest. T20.2's restore calls it
first, then adds its own staging checks.

## Testing

### Core tests (TDD)

`LibraryArchiveManifestTests`:

- `Write` then `Parse` round-trips a manifest. `Write` is deterministic:
  entries are sorted and the field order is fixed.
- `Parse` rejects each case listed under Core types: an unknown `format` or
  `formatVersion`, a missing field, each wrong path shape (`..`, absolute,
  drive, backslash, an empty segment, outside `library/`, an artwork name that
  isn't a hash), a duplicate path, no database entry, a bad hash, a negative
  size, mismatched counts, and a manifest over the limits.

### Infrastructure tests (TDD, real SQLite; NTFS cases on Windows)

The fixtures follow `RemovalLibrary`. The populated library has:

- two games, one with IGDB metadata and artwork;
- a TXT guide, an HTML guide that includes Guide B's non-ASCII asset, and a
  PDF guide;
- a reading state, per-guide preferences and Settings.

`LibraryExporterTests`:

- **Contents.** The archive holds exactly the expected entries, every hash
  matches, and the snapshot opens with the same rows as the live database.
- **Exclusions (TR20.2).** With `providers.bin`, `.recovery/`, the lock file,
  `.staging/`, `.trash/`, `.artwork-staging/`, the database sidecars, cache
  contents, and stray unreferenced files in `content/` and `artwork/` all
  present, none of them is in the archive.
- **Consistency.** At `GateHeld`, a game edit started on another task waits.
  It is absent from the snapshot and completes after the export.
- **Recovery.** A leftover `Prepared` import row is reconciled and the export
  succeeds. A conflicting row fails with `RecoveryIncomplete` and leaves no
  output.
- **Damaged files.** Each case fails with `ManagedFilesDamaged`, carries the
  right IDs, and leaves no output:
  - a missing guide file;
  - a same-size file with changed bytes;
  - an HTML asset whose hash doesn't match;
  - an artwork file whose hash doesn't match its name;
  - two damaged guides, both listed.
- **Links (Windows).** A guide or artwork file replaced by a symbolic link, or
  a guide folder replaced by a junction, fails with `ManagedFilesDamaged`.
- **Destination.**
  - A destination inside the data root, the cache root or a protected parent
    fails with `DestinationNotAllowed`.
  - A missing folder or a path without `.zip` fails with
    `DestinationUnavailable`.
  - An existing file without `overwrite` fails with `DestinationExists` and
    keeps its bytes. With `overwrite` it is replaced.
- **Cancellation** while waiting for the gate and at `Snapshotted`,
  `EntryWritten` and `BeforeRename`:
  - no `.tmp` file remains beside the destination and no snapshot remains in
    `%TEMP%`;
  - the destination is untouched;
  - a later write succeeds, which shows the gate was released.
- **Verification.** Corrupting the temporary ZIP at `Written` fails with
  `VerificationFailed` and cleans up.
- **Determinism.** With an injected clock, two exports of the same library are
  byte-identical.
- **The migration recovery copy** still passes its existing tests after the
  backup helper is extracted.

`LibraryArchiveVerifierTests`:

- It rejects:
  - a truncated ZIP;
  - an extra entry, or a missing entry;
  - a manifest that isn't the first entry;
  - an entry whose bytes don't match its hash.

### Measurement

On `pcsx2-win`, export a library seeded from the P1 fixtures plus one large
PDF. Record the archive size, the time the gate was held and the total time in
the verification record. This answers the work breakdown's "reassess archive
size" for T20.1.

### Traceability

- T20.1's exit in the implementation plan:
  - one write gate: the consistency test;
  - provider snapshots and managed artwork: the contents test;
  - clean cancellation: the cancellation tests;
  - checksums: the damaged-file and verification tests;
  - exclusions: the exclusion test.
- TR20.2: the exclusion test.
- TR20.1 (validation in staging before modifying the active library) stays
  with T20.2, which reuses `LibraryArchiveVerifier` and `Parse`.

## Out of scope

- The Settings rows, the save dialog, the first-import reminder and the
  progress UI (T20.2).
- Restore, staging validation and replacing the live library (T20.2).
- Merging two libraries (deferred past P1).
- Pinning files with hard links to shorten the gate; reconsider if the
  measured gate time is long.
- Encryption: the archive is as private as the folder it's saved in.

## Documentation

When T20.1 is implemented, update these:

- this status line and a verification record;
- [p1-technical-design.md](../p1-technical-design.md) section 2 (the live
  layout) and section 9 (T20.1 and the stale `GameMetadataLinks` text in
  T20.2) to match the code;
- the T20.1 row and a T20.1 paragraph in
  [implementation-plan.md](implementation-plan.md);
- a T20.1 row in [progress.md](../progress.md) and the merged-PR summary in
  [results.md](results.md).

## PR outcome

- **Target task:** T20.1.
- **Prerequisites:** T03.2 (PR #4), T04.4 (PR #14), T06.3 (PR #19) and T15.2
  (PR #5), all merged.
- **Outcome:** the library can be exported to a versioned, checksummed `.zip`
  holding a consistent database snapshot and every referenced guide and
  artwork file, and nothing else. Cancellation and failures leave no file
  behind. The archive format and verifier are ready for T20.2's restore.
  There is no UI change, so there is no screenshot.

## Risks

- **Long gate hold on large libraries.** Writes wait for the whole export.
  The measurement shows how long; T20.2 keeps edits disabled meanwhile.
- **Destination on a slow or removable drive.** Writing and verifying read
  the archive twice. A drive removed mid-export fails with `WriteFailed` or
  `VerificationFailed`, and the leftover `.tmp` file may stay on that drive.
- **Mid-session reconciliation.** It runs reconciler code outside its usual
  startup context. The recovery tests cover both a leftover row and a
  conflicting one.

## T20.1 verification record

- **Unit tests.** On `pcsx2-win` (Windows 11 build 26200, .NET SDK
  10.0.401): Core 932/932 (906 plus 26) and Infrastructure 593/593 (540 plus
  53), after the final-review fixes below. The new test classes are:
  - `LibraryArchiveManifestTests` (26): round trip, deterministic sorted
    output, counts, non-ASCII paths, and each rejection: format, version, a
    missing field, counts, a negative size, a malformed ID, five unsafe
    content paths, four wrong path shapes, an uppercase hash, a duplicate
    path, no database entry, non-JSON, an oversized manifest, sizes that
    overflow the total, and limits on `Write`;
  - `LibraryExportRepositoryTests` (6): the write gate is held, an open
    connection and the token are passed, a cancelled gate wait, a validated
    `BackupTo` copy, deletion on failed validation, and refusal of an
    existing file;
  - `LibraryArchiveVerifierTests` (8): a matching archive; a truncated
    archive; an entry missing from the manifest or from the archive; the
    manifest not first; changed bytes; wrong order; cancellation;
  - `LibraryArchivePlanTests` (12): every referenced file with its recorded
    size and hash; stray files left out; a missing file; a wrong size; HTML
    rows that disagree with the guide; missing artwork; several damaged
    guides; a shared relative path archived once; `LibraryTooLarge`; and,
    on Windows, a junctioned guide folder, a linked guide file and a linked
    artwork file;
  - `LibraryExporterTests` (26):
    - the archive's entries and verification;
    - the snapshot's rows: reading state, a preference, a setting and
      provider metadata;
    - exclusions (TR20.2): credentials, recovery copies, the lock file,
      staging, trash, artwork staging, the cache and stray files;
    - an edit during export waits and is absent from the snapshot;
    - a leftover `Prepared` import is reconciled first, and an unrecoverable
      row fails with `RecoveryIncomplete`;
    - a guide changed in place and mismatched artwork are damaged;
    - destination rules by whole path segment, a non-zip or missing folder,
      and an existing destination with and without `overwrite`;
    - cancellation at each of the six checkpoints and while waiting for the
      gate leaves nothing and releases the gate;
    - a corrupted temporary archive fails verification;
    - two exports with the same clock are byte-identical;
    - a stale `.tmp` from another export is left alone;
    - an empty library;
    - progress through each phase;
    - verification and the rename don't run on the caller's context;
    - a hard-linked live database fails as `DatabaseInvalid`;
    - another reader holding the temporary archive doesn't fail
      verification.
  - `LibraryExportMeasurement` (1): skipped unless `DG_EXPORT_MEASURE` is
    set.
- **Measurement.** [measurement.json](evidence/t20-1-library-export/measurement.json):
  - the library had 2 games and 32 guides (20 TXT, 5 HTML with 52 files
    each, PDFs of 20, 20, 20 and 100 MB, plus the fixture's 3), 175.0 MB in
    all;
  - the archive was 170.1 MB with 291 entries;
  - the write gate was held 0.48 s, and the whole export took 0.77 s.

  The PDFs and images are random bytes, stored uncompressed, so the archive
  is only a little smaller than the library. A gate this short doesn't
  justify hard-link pinning.
- **Production build.** The x64 Release build succeeded with no warnings.
  Production doesn't call the exporter yet.
- **Final review.** A fresh reviewer found no Critical issues. These four
  were fixed, each with a test that failed first:
  - `ExportAsync` now runs the destination check, verification and rename
    off the caller's context, so T20.2's UI won't freeze;
  - errors opening or reading the live library map to `DatabaseInvalid` or
    `WriteFailed`, never a raw exception;
  - verification shares reads, so an antivirus or indexer handle on the new
    file doesn't fail it;
  - `Parse` maps an overflowing size total to `InvalidDataException`.

  Deferred minors (by effect):
  - snapshot I/O errors read as `DatabaseInvalid`;
  - bad options surface late as `LibraryTooLarge`;
  - freelist pages can keep deleted rows in the archived database;
  - progress cadence;
  - case and Unicode-normalization duplicates pass `Parse` (T20.2 staging
    must catch them);
  - `Parse` accepts unknown JSON properties;
  - a crash leaves the `%TEMP%` snapshot behind;
  - ancestor links of the destination aren't resolved;
  - test gaps around the stale `.tmp` with `overwrite`, several write-stage
    mismatches, and a junctioned destination;
  - ZIP times have 2-second resolution.

  MSIX AppData write virtualization is handed to T20.2: a destination under
  `%LOCALAPPDATA%` or `%APPDATA%` outside `Packages` would be redirected into
  the package store and lost on uninstall.
- **Rulings:**
  - Host test staging adds the gitignored `tests/fixtures/p0/generated`
    fixtures, which the PDF tests read.
  - `ExportFixture.AddHtmlGuideAsync` iterates named tuple elements, because
    a lone `_` inside the `(journal, _)` lambda is a parameter, not a
    discard.
  - `LibraryExportMeasurement` gained `using DesktopGuides.Core.Backup;`.
  - Only the live layout and section 9 of `p1-technical-design.md` were
    corrected, as this design's Documentation section lists.
- **Follow-up.** Section 2 of `p1-technical-design.md` still describes the
  T04.4-era `GameMetadataLinks` table, an `AddGameMetadata` journal and
  `games/<game-id>` roots. The code stores provider metadata in `Games`
  columns and artwork under `library/artwork/`. Correct that text before
  T20.2's restore design relies on it.
- **Not run:** an installed smoke, because T20.1 has no UI.

