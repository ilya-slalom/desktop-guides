# T15.3 guide deletion design

Status: designed on `feat/p1-t15-3-guide-deletion`. Prerequisites T06.3
(PR #19, merge commit `494cb02`) and T15.2 (PR #5) are merged.

## Intent

Someone can remove a guide from a game after confirming. The dialog names the
guide and says how many managed files go with it. Removal deletes the guide's
metadata, reading state, reader preferences and owned files. Cancel changes
nothing. The original file the person imported is never touched.

Removal is recoverable, not atomic across SQLite and NTFS. A crash or failure
at any point leaves either the whole guide or no guide, plus at most one
journaled trash entry that startup recovery finishes.

Traces: the T15.3 row of [implementation-plan.md](implementation-plan.md)
(TR15.1, TR15.2), [work-breakdown.md](../work-breakdown.md) S15 and T15.3, and
[p1-technical-design.md](../p1-technical-design.md) (the deletion protocol,
146–190, and T15.3, 798–802).

Decisions made during brainstorming:

- **Entry point.** A **Remove guide** button in the Game page's Guides header,
  shown when a guide is selected. The Reader route has no Remove action. A
  Delete-key shortcut is left to T16.1.
- **After removal.** The row that took the removed row's place is selected and
  focused, or the previous row when the last one was removed. With no guides
  left, the empty state shows and **Import guide** takes focus. An
  auto-hiding status message confirms the removal. Full stable-selection rules
  stay in T05.3.
- **Architecture.** A `GuideRemover` service over an internal deletion journal,
  mirroring T06.3's `GuideImportPublisher` and `IImportJournal`. T04.3 reuses
  the journal for `DeleteGame`.
- **Permanent.** There is no undo and no restore-from-trash UI.
- **Broken guides can be removed.** A guide whose `content/<id>` directory is
  missing shows 0 files and removes its metadata. This is S15's "remove a
  broken guide" acceptance.
- **No schema change.** `ON DELETE CASCADE` from `Guides` already removes
  `ReadingStates` and `ReaderPreferences`, and the v1 `DeleteGuide` manifest
  already exists. The schema stays at version 3.
- **Out of scope:** Game removal (T04.3); the fault-injection matrix across
  both protocols (T15.4); orphan review UI; keyboard shortcuts (T16.1).

## Components

### Core types (Core/Library)

```csharp
public sealed record GuideRemovalPreview(Guid GuideId, Guid GameId, string Title, int FileCount);

public enum GuideRemovalOutcome { Removed, NotFound }

public sealed record GuideRemovalResult(GuideRemovalOutcome Outcome, bool CleanupPending);

public enum GuideRemovalIssue { Unsafe, Failed, RestoreFailed }

public sealed class GuideRemovalException(GuideRemovalIssue issue, Exception? inner = null)
    : Exception(null, inner)
{
    public GuideRemovalIssue Issue { get; } = issue;
}
```

`CleanupPending` is true only for `Removed`.

### Owned tree (Infrastructure/Storage)

`OwnedGuideTree` gains `public int FileCount => files.Count;`. It already
refuses a link or a non-directory at the root and a link anywhere inside.

### Deletion journal (Infrastructure/Storage)

`SqliteLibraryRepository` gains:

```csharp
internal Task<T> RunDeletionAsync<T>(Func<IDeletionJournal, T> work, CancellationToken token);
```

It holds the write gate for the whole call and runs `work` on the thread pool,
like `RunImportAsync`. The token is checked only while waiting for the gate.

```csharp
/// <summary>SQL for one guide deletion. Callers already hold the repository's write gate.</summary>
internal interface IDeletionJournal
{
    Guide? GetGuide(Guid guideId);

    /// <summary>Commits a Prepared DeleteGuide row owning content/&lt;guide&gt; and .trash/&lt;op&gt;/&lt;guide&gt;.</summary>
    void Prepare(Guid operationId, Guid guideId);

    /// <summary>Deletes the Guide row (cascading its state rows) and marks the operation Committed, in one transaction.</summary>
    void Commit(Guid operationId, Guid guideId, Action beforeCommit);

    /// <summary>Moves a Prepared deletion's trash back to content, then removes its row.</summary>
    void RollBack(Guid operationId);

    /// <summary>Deletes a Committed deletion's trash, then removes its row.</summary>
    void Finish(Guid operationId);
}
```

- `Prepare` writes `FileOperationManifest.Create(FileOperationKind.DeleteGuide, operationId, [guideId])`.
- `Commit` requires exactly one deleted `Guides` row and exactly one updated
  `FileOperations` row (`Kind = 'DeleteGuide' AND Phase = 'Prepared'`), and
  throws `InvalidDataException` otherwise, so the transaction rolls back.
- `RollBack` and `Finish` call new `FileOperationReconciler.RollBackDeletion`
  and `FinishDeletion` methods. Like `RollBackImport`, they read the row, check
  its kind, phase and the Guide row's presence, capture the trees with
  `OwnedGuideTree`, act, remove an empty `.trash/<op>` directory, then remove
  the row. They share the startup reconciler's rules, so an in-process
  rollback and a startup rollback can't disagree.

### Remover (Infrastructure/Storage)

```csharp
public sealed class GuideRemover
{
    public GuideRemover(SqliteLibraryRepository repository, ILibraryPaths paths);
    internal GuideRemover(SqliteLibraryRepository repository, ILibraryPaths paths,
        Action<RemovalCheckpoint> checkpoint);

    public Task<GuideRemovalPreview?> DescribeAsync(Guid guideId, CancellationToken token = default);
    public Task<GuideRemovalResult> RemoveAsync(Guid guideId, CancellationToken token = default);
}

internal enum RemovalCheckpoint { Prepared, Moved, InCommit, Committed }
```

**`DescribeAsync`** reads without the gate. It returns null when the guide is
gone. Otherwise it captures `content/<id>` and returns its title and
`FileCount` (0 when the directory is missing). An unsafe tree throws
`GuideRemovalException(Unsafe)`.

**`RemoveAsync`** runs inside `RunDeletionAsync`:

1. `GetGuide`. If it's gone, return `NotFound` and write nothing.
2. Capture `content/<id>`. An unsafe tree throws `Unsafe` before any row
   exists.
3. `Prepare` with a new operation ID. Checkpoint `Prepared`.
4. If the content directory exists, create `.trash/<op>` and
   `Directory.Move(content/<id>, .trash/<op>/<id>)`. Checkpoint `Moved`.
5. `Commit`, with checkpoint `InCommit` as `beforeCommit`.
6. Checkpoint `Committed`, then `Finish`.

Failure handling:

- **Steps 3–5.** Any exception runs `RollBack`. If that succeeds, throw
  `GuideRemovalException(Failed, inner)`. If it also fails, throw
  `RestoreFailed`: the `Prepared` row stays, and the next startup moves the
  trash back.
- **Step 6.** An exception from `Finish` still returns
  `Removed` with `CleanupPending: true`. Only `.trash/<op>/<id>` (whatever part
  of it is left) and its `Committed` row remain, and the next startup's
  reconciler deletes them. No other path is touched.
- Cancellation stops only the wait for the gate. Once step 3 starts, the
  removal runs to an outcome.

The shell builds one `GuideRemover` next to `GuideImportPublisher`.

## Game page

### Header button

The Guides header gains a fourth control, **Remove guide**
(`RemoveSelectedGuide`), between **Open selected guide** and **Import
guide**. It's visible when a guide is selected and the list is enabled, with
the same rule as `UpdateOpenSelectedGuideAction`. Its automation name is
"Remove {title}".

### Flow

1. The button disables the list and header buttons and calls `DescribeAsync`.
   A null preview reloads the route with the "already removed" message.
2. A native `ContentDialog` (`RemoveGuideDialog`) opens:
   - **Title:** "Remove {title}?"
   - **Body:** "This removes the guide, its reading progress, and its {N}
     managed files from Desktop Guides. The original file you imported isn't
     affected." N reads "1 managed file" or "{N} managed files".
   - **Primary:** **Remove**. **Close:** **Cancel**, which is
     `DefaultButton`, so Enter and Escape both cancel. No custom danger style.
3. **Cancel** re-enables the page and returns focus to **Remove guide**.
4. **Remove** calls `RemoveAsync`. If the same Game route is still shown and
   no close was requested, the route reloads its guides and selects and
   focuses the neighbor, as in the brainstorming decision. The Library home
   and Resume rebuild from the repository when next shown.

### Messages

All use the existing `ShellStatusInfoBar`.

| Outcome | Severity | Message |
| --- | --- | --- |
| Removed | Informational, auto-hides | "Removed {title}." |
| Removed, cleanup pending | Informational, auto-hides | "Removed {title}. Leftover files will be cleaned up the next time Desktop Guides starts." |
| `NotFound` | Informational, auto-hides | "{title} was already removed." The list reloads. |
| `Unsafe` | Error, closable | "{title} can't be removed because its files were changed outside Desktop Guides." |
| `Failed` | Error, closable | "{title} couldn't be removed. The guide is unchanged. Try again." |
| `RestoreFailed` | Error, closable | "{title} couldn't be removed. Restart Desktop Guides to finish restoring it." |

Any other exception from `DescribeAsync` shows the `Failed` message.

## Testing

### Infrastructure tests (TDD, real SQLite and NTFS on `pcsx2-win`)

**Owned tree**
- `FileCount` counts files in a nested tree and is 0 for a missing root.

**Describe**
- Returns the title, game and file count for a guide with nested files.
- Returns 0 files for a guide whose content directory is missing.
- Returns null for an unknown guide.
- Throws `Unsafe` for a link inside the tree and for a file in place of the
  directory.

**Remove**
- The happy path removes the `Guides`, `ReadingStates` and `ReaderPreferences`
  rows, `content/<id>`, `.trash/<op>` and the `FileOperations` row. A second
  guide in the same game and a guide in another game keep their rows and
  bytes.
- A guide with a missing content directory removes its rows and leaves no
  trash or journal row.
- An unknown guide returns `NotFound` and writes nothing.
- A link inside the tree throws `Unsafe` before any journal row exists.
- A fault at `Prepared`, `Moved` or `InCommit` throws `Failed`. The content
  directory is back with the same bytes, all three rows are intact, and no
  journal row or trash remains.
- A failed restore: at `Moved`, the checkpoint opens a trashed file without
  `FileShare.Delete` and throws, so the move back fails. `RestoreFailed` is
  thrown and the `Prepared` row stays. After the handle closes, a fresh
  repository's `InitializeAsync` restores the directory and clears the row.
- A failed cleanup: at `Committed`, the checkpoint opens a trashed file without
  `FileShare.Delete`. The result is `Removed` with `CleanupPending`, the rows
  are gone, and only `.trash/<op>/<id>` and the `Committed` row remain. After
  the handle closes, `InitializeAsync` deletes both.
- The existing reconciler tests pass unchanged.

### Installed smoke

The removal group runs after the import group, which leaves two `txt-legacy`
guides in the seeded game.

| Run | Mode | Checks |
| --- | --- | --- |
| Dark | `remove-guide-cancel` | Select the first guide and invoke `RemoveSelectedGuide`. `RemoveGuideDialog` names the title and "1 managed file". Screenshot `remove-confirm-dark`. Cancel: the list still has 2 guides and focus is on `RemoveSelectedGuide`. |
| Light | `remove-guide` | Select the first guide, invoke `RemoveSelectedGuide`, screenshot `remove-confirm-light`, and press **Remove**. The list has 1 guide, selected and focused, and the status reads "Removed {title}.". Screenshot `removed-light`. |

`describe-import` in the shell seed tool also reports `TrashEntries`,
`ReadingStates` and `ReaderPreferences`. After the cancel run the state is
unchanged: 2 guides, 2 content directories, 2 reading states and 2
preferences rows. After the removal run it is 1 guide, 0 file operations, 0
staging entries, 1 content directory, 0 trash entries, 1 reading state and 1
preferences row.

### Traceability

| Requirement | Evidence |
| --- | --- |
| TR15.1: cleanup targets only app-owned paths | The unsafe-tree tests; the fault tests show only `content/<id>` and `.trash/<op>/<id>` change; the cleanup-pending test leaves only the known trash entry |
| TR15.2: deletion removes metadata, state, preferences and owned files; cancel leaves all four | The happy-path test; the `remove-guide` smoke state; the `remove-guide-cancel` smoke state |

## Documentation

When T15.3 is implemented, update these:

- this status line and a verification record;
- the T15.3 paragraph in [implementation-plan.md](implementation-plan.md);
- a T15.3 row in [progress.md](../progress.md);
- a new `Guide removal` row in [e2e-testing.md](e2e-testing.md);
- evidence under `docs/p1/evidence/t15-3-guide-deletion/`.

## PR outcome

- **Target task:** T15.3.
- **Prerequisites:** T06.3 (PR #19) and T15.2 (PR #5), both merged.
- **Outcome:** a guide can be removed from its game after a confirmation that
  names it and its file count. Removal deletes its metadata, state,
  preferences and managed files, and every failure leaves the guide intact or
  a journaled trash entry that startup finishes. The PR includes the dark
  and light confirmation screenshots and the light result.
