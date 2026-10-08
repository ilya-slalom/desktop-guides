# T15.4 fault-injection matrix design

Status: implemented on `feat/p1-t15-4-fault-injection`; see the verification record.
Prerequisites T04.3 (PR #26), T06.3 (PR #19), T15.2 (PR #5) and T15.3
(PR #22) are merged.

## Intent

Every import and removal either completes or leaves the library exactly as it
was, or exactly as startup recovery finishes it. That must hold when the
operation fails in process, crashes, is cancelled, or fails during its own
rollback. T15.4 proves it with a headless matrix: each cell injects one fault
at one phase and compares every database row and every app-owned file before
and after.

Traces: the T15.4 row of [implementation-plan.md](implementation-plan.md)
(TR04.1, TR04.2, TR06.2, TR15.1, TR15.2), [work-breakdown.md](../work-breakdown.md)
S15 and T15.4, and [p1-technical-design.md](../p1-technical-design.md) section
8 (T15.4: copy failure, crash after stage, crash after rename, failed
database commit, failed trash move, cancelled removal, committed removal and
startup recovery).

Decisions made during brainstorming:

- **A dedicated matrix suite.** A snapshot helper, plus one theory-driven
  matrix class per protocol. Existing tests stay.
- **Defects.** Fix small, local defects in this PR, test first. Anything that
  would change a protocol's steps or the journal format gets a tracking issue
  and a note in the verification record.
- **Headless only.** The suite runs on Windows (`pcsx2-win` and CI), with no
  UI. T15.1 owns error copy.
- **Provider artwork has no journal.** Its startup sweep is covered through
  game removal; artwork writes themselves aren't journaled operations.

## Rulings against the spec

- **The protocols in the technical design are stale.** Section 2 still
  describes an `AddGameMetadata` operation, `games/<game-id>` roots and a
  version 2 manifest that were never built. The three journaled protocols
  are `Import`, `DeleteGuide` and `DeleteGame`. The matrix follows the code;
  correcting the design text is a separate follow-up.
- **"Failed trash move"** is covered by throws at `Prepared` and the new
  `TrashCreated` checkpoint, and by a content file held open on Windows,
  which the existing tests already exercise.

## Assertion model

`LibrarySnapshot` (a test helper) captures:

- every row of `Games`, `Guides`, `GuideAssets`, `ReadingStates`,
  `ReaderPreferences`, `Settings` and `FileOperations`, with every column,
  ordered by key;
- every entry under the data root, as relative path and kind (folder, file
  or link), with size and SHA-256 for files. Links are recorded and not
  followed. `library/library.sqlite` and its `-wal`, `-shm` and `-journal`
  sidecars are skipped, because their content is compared as rows.

`LibrarySnapshot.Diff(before, after)` returns the added, removed and changed
keys, for example `Guides/<id>` or `library/content/<id>/guide.txt`. Each
cell asserts that the diff is exactly what its outcome should produce:

| Outcome | Expected diff |
| --- | --- |
| Failure before commit, after rollback or recovery | empty |
| Import published | the new guide's rows (`Guides`, `GuideAssets`, `ReadingStates`, `ReaderPreferences`) and its `content/<id>/…` files, nothing else |
| Guide removed | that guide's rows and `content/<id>` files gone, and `Settings` `LastActiveGuideId` cleared when it pointed there |
| Game removed | the game's row, its guides' rows and files, and its artwork file gone |
| Cleanup pending | the removed state, plus one `.trash/<op>/…` entry and that operation's `Committed` row |

Every fixture also holds a bystander game and guide, with artwork, reading
state and preferences. No bystander key may ever appear in a diff.

## Failure modes

- **Throw.** The checkpoint throws `InjectedFault`, and the protocol's own
  rollback runs.
- **Crash.** The checkpoint throws, and a test hook skips the in-process
  rollback or finish. A fresh repository then starts on the same files, and
  its startup recovery must reach the cell's final state.
- **Rollback fails.** The rollback hook throws. The result is `RestoreFailed`
  for a removal, or the original issue for an import, and the journal row is
  kept. After a restart, recovery reaches the empty diff.
- **Cancel.** The token is cancelled at the checkpoint. Import honours it
  until `Renamed` and ignores it from `InCommit` on. Removal ignores it once
  the operation is journaled.

## Seams and new checkpoints

These are internal and test-only, and they match the importer's existing
`rollBack` hook. None changes a protocol's steps.

- `ImportCheckpoint` gains `MovedToContent`, fired between
  `Directory.Move(staged, content)` and deleting `.staging/<op>`, and
  `Published`, fired after `Publish` commits.
- `RemovalCheckpoint` gains `TrashCreated`, fired after `.trash/<op>` is
  created and before the first move, and `BeforeArtworkDelete`, fired in
  `GameRemover` after the commit and before the artwork delete.
- `GuideRemover` and `GameRemover` internal constructors gain
  `Action<IDeletionJournal, Guid>? rollBack` and
  `Action<IDeletionJournal, Guid>? finish`. They default to the journal's
  `RollBack` and `Finish`.

## Matrix

### Import (`GuideImportPublisher`)

The subject is a three-file HTML guide (entry, stylesheet and image), so a
copy spans several files.

| Checkpoint or fault | Throw | Crash, then restart | Cancel |
| --- | --- | --- | --- |
| `Prepared` | empty | empty | empty |
| `Copied` | empty | empty | empty |
| `Verified` | empty | empty | empty |
| `MovedToContent` (new) | empty | empty | empty |
| `Renamed` | empty | empty | empty |
| `InCommit` | empty | empty | published |
| `Published` (new) | — | published | published |
| Copy fault after the first file (a source stream that throws) | empty | empty | — |
| `Prepare` row insert fails (a trigger) | `SaveFailed`, empty | — | — |
| `Publish` insert fails (a trigger) | empty | — | — |

Rollback fails: the rollback hook throws at `Copied`. The original issue is
reported and the `Prepared` row is kept; after a restart, the diff is empty.

### Guide removal (`GuideRemover`)

| Checkpoint or fault | Throw | Crash, then restart | Rollback fails |
| --- | --- | --- | --- |
| `Prepared` | `Failed`, empty | empty | `RestoreFailed`, row kept; after restart, empty |
| `TrashCreated` (new) | `Failed`, empty | empty | `RestoreFailed`, row kept; after restart, empty |
| `Moved` | `Failed`, empty | empty | `RestoreFailed`, row kept; after restart, empty |
| `InCommit` | `Failed`, empty | empty | `RestoreFailed`, row kept; after restart, empty |
| `Committed` | `Removed`, cleanup pending | removed | — |
| `Guides` delete fails (a trigger) | `Failed`, empty | — | — |

Also:

- a normal removal gives exactly the removed state;
- a token cancelled before the call, or while another operation holds the
  gate, throws `OperationCanceledException` and leaves an empty diff;
- a token cancelled at `Prepared` still returns `Removed` with the removed
  state.

### Game removal (`GameRemover`)

The subject game has two guides and artwork.

| Checkpoint or fault | Throw | Crash, then restart |
| --- | --- | --- |
| `Prepared` | `Failed`, empty | empty |
| `TrashCreated` (new) | `Failed`, empty | empty |
| first `MovedGuide` (one guide moved, one not) | `Failed`, empty | empty |
| `Moved` | `Failed`, empty | empty |
| `InCommit` | `Failed`, empty | empty |
| `Committed` | `Removed`, cleanup pending | removed |
| `BeforeArtworkDelete` (new) | removed, but the artwork file is still present; after restart, the sweep deletes it | removed |
| commit fails (the existing trigger) | `Failed`, empty | — |

Also:

- rollback fails at `Moved`: `RestoreFailed`, row kept; after a restart, the
  diff is empty;
- cancellation, as for guide removal;
- a game with no guides: throw and crash at `InCommit` and at
  `BeforeArtworkDelete`, and the normal path.

## NTFS cells (Windows only)

Each cell puts a sentinel file behind every link; the sentinel must keep its
bytes.

| Case | Expected |
| --- | --- |
| `.staging/<op>` is a junction, with a `Prepared` import row | startup recovery throws `InvalidDataException`; the row is kept and the sentinel is intact |
| `.trash/<op>` is a junction, with a `Prepared` deletion row | the same |
| `.trash/<op>/<guide>` is a junction, with a `Committed` deletion row | the same |
| `content/<guide>` is itself a junction, and that guide is removed | `GuideRemovalException` with `Unsafe`; nothing moved |
| a file in a removed guide is hard-linked to an outside file | `Removed`; the outside file keeps its bytes |
| a removed guide holds `CON` or `x.` (made through `\\?\`) | `Removed`, or `Removed` with cleanup pending and one known trash entry; no raw exception |
| `.trash` holds a non-GUID folder or an uppercase-GUID folder | kept, and counted in the startup report for review |
| `artwork/<game>` is a junction, and that game is removed | the outside artwork file keeps its bytes |

## Defects

Fix in T15.4, each with a cell that fails first:

1. `GuideRemover` and `GameRemover` call `paths.GetGuideRoot` outside their
   `Capture` try, so a junctioned `content/<guide>` throws a raw
   `InvalidDataException`. Move the call inside the try, so it maps to
   `Unsafe`.
2. `ManagedArtworkStore.Delete` resolves and deletes without a link check. If
   the junctioned-artwork cell confirms it deletes the outside file, call
   `ManagedPathResolver.RejectFilesystemLinks` first and leave the file.
3. In `GameRemovalTests`, the held-content test exercises its partial-move
   rollback only when the held guide sorts second by ID. Hold the guide that
   moves first. This is a test-only fix.

Record with a tracking issue:

- The T06.3 follow-up: if deleting the empty `.staging/<op>` fails after the
  rename, the whole import is rolled back. Making that cleanup best-effort
  changes the import protocol's failure handling. The `MovedToContent` cells
  pin today's behavior.
- Any further defect the matrix finds that would change a protocol step or
  the journal format.

## Components

### Production (`src/DesktopGuides.Infrastructure`)

- `Import/GuideImportPublisher.cs`: the `MovedToContent` and `Published`
  checkpoints.
- `Storage/GuideRemover.cs`:
  - the `TrashCreated` and `BeforeArtworkDelete` values on
    `RemovalCheckpoint`;
  - the `rollBack` and `finish` hooks;
  - fix 1.
- `Storage/GameRemover.cs`: the same hooks and checkpoints, plus fix 1.
- `Artwork/ManagedArtworkStore.cs`: fix 2, if confirmed.

### Tests (`tests/DesktopGuides.Infrastructure.Tests/FaultInjection/`)

- `LibrarySnapshot.cs`: `Capture(ManagedPathResolver)` and
  `Diff(LibrarySnapshot before, LibrarySnapshot after)`, with an assertion
  that lists every unexpected key.
- `FaultFixture.cs`: a populated library with the subject game (two guides
  and artwork) and the bystander game and guide. It provides the
  remover and publisher factories with hooks, and `RestartAsync`.
- `ImportFaultMatrixTests.cs`, `GuideRemovalFaultMatrixTests.cs` and
  `GameRemovalFaultMatrixTests.cs`: the matrix, as theories whose
  `InlineData` names the checkpoint and mode.
- `NtfsFaultTests.cs`: the NTFS cells, which return early off Windows.

## Testing

- **Fails first.** Cells that need a new checkpoint, hook or fix are written
  first and fail. A cell that passes on today's code is recorded as
  characterization in the verification record.
- **Suites.** Core and Infrastructure on `pcsx2-win`, then CI on the PR.
- **Not run.** An installed smoke, because there is no UI.

### Traceability

- TR04.1 and TR04.2 (game removal): the game-removal matrix and the
  junctioned-artwork cell.
- TR06.2 (import publishes all or nothing): the import matrix.
- TR15.1 (cleanup targets only app-owned paths): the NTFS cells and the
  bystander check in every cell.
- TR15.2 (guide deletion removes all four; cancellation keeps them): the
  guide-removal matrix and its cancellation cells.

## Out of scope

- Changing any protocol's steps or the journal format.
- UI error copy and focus (T15.1).
- Correcting the stale protocol text in `p1-technical-design.md` section 2.
- Provider artwork writes, which aren't journaled.

## Documentation

When T15.4 is implemented, update these:

- this status line and a verification record with the cell count, results,
  fixes and tracking issues;
- the T15.4 note in [implementation-plan.md](implementation-plan.md).

## PR outcome

- **Target task:** T15.4.
- **Prerequisites:** T04.3 (PR #26), T06.3 (PR #19), T15.2 (PR #5) and T15.3
  (PR #22), all merged.
- **Outcome:** every import and removal phase is covered by a fault-injection
  cell that compares exact database rows and app-owned files, for in-process
  failure, crash and restart, cancellation and failed rollback, plus the NTFS
  link and name cases. Small defects found are fixed; others are tracked.
  There is no UI change, so there is no screenshot.

## T15.4 verification record

- **Unit tests.** On `pcsx2-win`, Core 932/932 (unchanged) and
  Infrastructure 678/678 passed. Infrastructure was 593 before T15.4, and
  the 85 new cells are:
  - `LibrarySnapshotTests`, 6;
  - `CheckpointOrderTests`, 4;
  - `ImportFaultMatrixTests`, 26;
  - `GuideRemovalFaultMatrixTests`, 19;
  - `GameRemovalFaultMatrixTests`, 22;
  - `NtfsFaultTests`, 8. These cells run only on Windows.
- **Characterization.** These cells passed on the code as it was before
  their task:
  - all 26 import cells;
  - all 19 guide-removal cells;
  - all 22 game-removal cells;
  - 5 of the 8 NTFS cells: the junctioned `.staging/<op>`, `.trash/<op>` and
    `.trash/<op>/<guide>` cells; the hard-linked guide file; and the unknown
    trash folders.
- **Fixes.** Each fix has a cell that failed first.
  - **A junctioned `content/<guide>`** now maps to `Unsafe` in both removers.
    Before, it escaped as a raw `InvalidDataException`. Cell:
    `AJunctionedGuideFolderIsUnsafeToRemove`.
  - **`ManagedArtworkStore.Delete`** no longer deletes through a filesystem
    link. The junctioned-artwork cell showed that it deleted the outside
    file, so the suspected defect was real. Cell:
    `AJunctionedArtworkFolderKeepsTheOutsideFile`.
  - **A guide holding `CON` or `x.`** can now be removed. Before, removal
    was refused as `Failed`, because a plain Win32 path turns `x.` into
    `x`. `OwnedGuideTree` now reads and deletes through `\\?\` paths. This
    defect was found by the matrix and was not in the list above. Supported
    imports can't create these names. Cell:
    `ReservedNamesInARemovedGuideReachAConsistentOutcome`.
  - **`ContentHeldOpenFailsAndRestoresEveryGuide`** now always holds the
    guide that moves second, and asserts one `MovedGuide`. This changes
    only the test, so no product code failed first.
- **Tracking issues.**
  [#61](https://github.com/ilya-slalom/desktop-guides/issues/61): an
  import rolls back when its empty staging folder can't be deleted.
- **Rulings.**
  - **The existing remover order tests now include the new checkpoints.**
    The plan expected them to stay unchanged. In fact
    `RemoveDeletesTheGuideItsStateAndItsFiles` and
    `RemoveDeletesTheGameItsGuidesAndTheirFiles` assert the full checkpoint
    sequence. Both now include `TrashCreated`, and the game test also
    includes `BeforeArtworkDelete`, because that game has artwork.
  - **The reserved-name defect was fixed in T15.4 instead of tracked.** The
    fix touches one file and changes no protocol step or journal format.
- **Not run.** No installed smoke, because T15.4 has no UI.
