# T04.3 Game removal design

Status: designed on `feat/p1-t04-3-game-removal` on 1 October 2026.
Prerequisites T04.2 (PR #25), T04.4 (PR #14) and T15.3 (PR #22) are merged.

## Intent

**Remove game** works for every game. Before the confirmation, the app counts
the game's guides and their managed files, and the dialog states both. One
explicit **Remove** then deletes the game, its guides' metadata, reading state
and reader preferences, its provider link and snapshot, its managed artwork
and its guides' managed content, and touches nothing else. **Cancel** changes
neither database records nor files. The original files the person imported
are never touched, and there is no undo.

Removal is recoverable, not atomic across SQLite and NTFS. A crash or failure
at any point leaves either the whole game or no game, plus at most one
journaled trash entry that startup recovery finishes. An artwork file left
behind after the commit is removed by the existing startup artwork sweep.

Traces: the T04.3 row of [implementation-plan.md](implementation-plan.md)
(TR04.1, TR04.2), [work-breakdown.md](../work-breakdown.md) S04 and T04.3,
and [p1-technical-design.md](../p1-technical-design.md) (the deletion
protocol, and T04.3).

Decisions made during brainstorming:

- **Confirmation.** The same native dialog as T04.2, with **Cancel** as the
  default. For a game with guides, the title and body state the guide count
  and the managed-file count. There is no checkbox and no typed title.
- **Approach A.** A game-level remover over T15.3's deletion journal, writing
  the existing v1 `DeleteGame` manifest. The game's managed artwork is
  deleted best effort after the commit, as in T04.2, rather than moved into
  the trash. This differs from the technical design's sketch, which named the
  `games/<game-id>` root in the operation; see ruling 1.
- **One remove flow.** A game with no guides goes through the same remover
  without a journal row. T04.2's dialog copy for that case is unchanged.
- **Changed count.** The service gets the Game ID plus the guide count the
  dialog showed. If the count differs, nothing is removed and the dialog
  shows again with the fresh counts.
- **No schema change.** `DeleteGame` is already allowed by the
  `FileOperations` check, the manifest accepts up to 10,000 guide IDs for it,
  and the startup reconciler already restores or finishes a multi-guide
  deletion. The schema stays at version 3.

## Rulings against the spec

1. **Artwork stays outside the journal.** The v1 `DeleteGame` manifest has no
   Game ID or artwork path, and adding them needs manifest v2 and an artwork
   branch in every reconciler path. The artwork file is deleted best effort
   after the commit; if that fails or the app stops first, the row no longer
   references it and the startup sweep deletes it. Cost if wrong: an orphaned
   artwork file can exist between a crash and the next start. The game is
   already gone, so nothing shows it.
2. **The changed-count re-prompt is not exercised in the installed smoke.**
   Adding a guide between the dialog and the click would need a test hook in
   the production app. Infrastructure tests cover `CountChanged` and Core
   tests cover its copy. Cost if wrong: a defect in the shell's re-prompt
   loop would be caught only by review.

## Components

### Core types (Core/Library)

`GameRemovalContracts.cs` replaces T04.2's `EmptyGameRemoval` and
`EmptyGameRemovalOutcome`:

```csharp
/// <summary>What the confirmation states: the game, its guide count and its managed-file count.</summary>
public sealed record GameRemovalPreview(Guid GameId, string Title, int GuideCount, int FileCount);

public enum GameRemovalOutcome { Removed, NotFound, CountChanged }

/// <summary>
/// CleanupPending is true only for Removed: startup deletes the leftover trash.
/// Current is the fresh preview, set only for CountChanged.
/// </summary>
public sealed record GameRemovalResult(
    GameRemovalOutcome Outcome, bool CleanupPending, GameRemovalPreview? Current);

public enum GameRemovalIssue { Unsafe, Failed, RestoreFailed }

public sealed class GameRemovalException(GameRemovalIssue issue, Exception? inner = null)
    : Exception(null, inner)
{
    public GameRemovalIssue Issue { get; } = issue;
}
```

T04.2's Core `GameRemover` is deleted; its artwork handling moves into the
Infrastructure remover. `RemoveEmptyGameAsync` leaves `ILibraryRepository`
and its test fake.

### Deletion journal (Infrastructure/Storage)

`IDeletionJournal` gains three members. The existing `RollBack` and `Finish`
are reused unchanged, because `FileOperationReconciler.RollBackDeletion` and
`FinishDeletion` already handle every guide a manifest names.

```csharp
/// <summary>The game's title, artwork path and guide IDs, or null when it's gone.</summary>
GameForRemoval? GetGameForRemoval(Guid gameId);

/// <summary>Commits a Prepared DeleteGame row owning each content/&lt;guide&gt; and .trash/&lt;op&gt;/&lt;guide&gt;.</summary>
void PrepareGame(Guid operationId, IReadOnlyList<Guid> guideIds);

/// <summary>
/// Deletes the game (cascading its guides, their state rows and its provider
/// link), clears a LastActiveGuideId that named one of its guides, and marks
/// the operation Committed, in one transaction. With no guides, there is no
/// operation and only the game is deleted.
/// </summary>
void CommitGame(Guid? operationId, Guid gameId, IReadOnlyList<Guid> guideIds, Action beforeCommit);
```

`internal sealed record GameForRemoval(string Title, string? ArtworkRelativePath, IReadOnlyList<Guid> GuideIds);`

`CommitGame` reads the game's guide IDs inside its transaction and throws
`InvalidDataException`, rolling back, unless they equal `guideIds` exactly.
It also requires exactly one deleted `Games` row and, with an operation,
exactly one updated `FileOperations` row
(`Kind = 'DeleteGame' AND Phase = 'Prepared'`). Because the remover holds the
write gate throughout, the guard is defensive: it keeps the delete exact if
that ever changes.

### Remover (Infrastructure/Storage)

```csharp
public sealed class GameRemover
{
    public GameRemover(SqliteLibraryRepository repository, ILibraryPaths paths, IArtworkStore artwork);
    internal GameRemover(SqliteLibraryRepository repository, ILibraryPaths paths, IArtworkStore artwork,
        Action<RemovalCheckpoint> checkpoint);

    public Task<GameRemovalPreview?> DescribeAsync(Guid gameId, CancellationToken token = default);
    public Task<GameRemovalResult> RemoveAsync(Guid gameId, int expectedGuideCount, CancellationToken token = default);
}
```

`RemovalCheckpoint` gains `MovedGuide`, raised after each guide's directory
moves to the trash. `GuideRemover` doesn't raise it.

**`DescribeAsync`** reads without the gate. It returns null when the game is
gone. Otherwise it captures each guide's `content/<guide>` with
`OwnedGuideTree` and returns the title, the guide count, and the total
`FileCount`; a missing directory counts 0 files. An unsafe tree throws
`GameRemovalException(Unsafe)`. `Guid.Empty` throws `ArgumentException`
here and in `RemoveAsync`.

**`RemoveAsync`** runs inside `RunDeletionAsync`:

1. `GetGameForRemoval`. If the game is gone, return `NotFound` and write
   nothing.
2. Capture every guide's content tree. An unsafe tree throws `Unsafe` before
   anything is written.
3. If the guide count isn't `expectedGuideCount`, return `CountChanged` with
   a fresh preview from those trees. Nothing is written.
4. **No guides:** `CommitGame(null, …)`, then delete the artwork best effort
   and return `Removed`. A failure throws `Failed`; nothing changed.
5. If an unfinished file operation already claims any of the guides, throw
   `RestoreFailed`, as `GuideRemover` does. A second deletion would strand the
   first one's row.
6. `PrepareGame` with a new operation ID naming every guide. Checkpoint
   `Prepared`. A failure here throws `Failed`; nothing changed.
7. For each guide whose content directory exists, create `.trash/<op>` and
   `Directory.Move(content/<guide>, .trash/<op>/<guide>)`, then checkpoint
   `MovedGuide`. After all of them, checkpoint `Moved`.
8. `CommitGame`, with checkpoint `InCommit` as `beforeCommit`.
9. Checkpoint `Committed`, then `Finish`.
10. Delete the artwork best effort. On `IOException` or
    `UnauthorizedAccessException`, the startup sweep deletes it.

Failure handling, as in T15.3:

- **Steps 6–8.** Any exception runs `RollBack`, which moves every trashed
  guide back to `content` and removes the row. If that succeeds, throw
  `Failed`. If it also fails, throw `RestoreFailed`: the `Prepared` row
  stays, and the next startup moves the trash back.
- **Step 9.** An exception from `Finish` still returns `Removed` with
  `CleanupPending: true`. Only the leftover `.trash/<op>/…` and its
  `Committed` row remain, and the next startup's reconciler deletes them.
- **Cancellation** stops only the wait for the gate. Once step 6 starts, the
  removal runs to an outcome.

The shell builds one `GameRemover` next to `GuideRemover`.

### Presentation (Core/Library/GameRemovalPresentation.cs)

`GuidesFirst` and `HasGuides` are removed. The copy is:

| Member | Text |
| --- | --- |
| `DialogTitle(title, 0)` | "Remove {title}?" (T04.2) |
| `DialogTitle(title, 1)` | "Remove {title} and its guide?" |
| `DialogTitle(title, N)` | "Remove {title} and its {N} guides?" |
| `DialogBody(0, _)` | "This removes the game and its details from Desktop Guides." (T04.2) |
| `DialogBody(1, F)` | "This removes the game, its guide with its reading progress, and its {F} from Desktop Guides. The original file you imported isn't affected." |
| `DialogBody(N, F)` | "This removes the game, its {N} guides with their reading progress, and their {F} from Desktop Guides. The original files you imported aren't affected." |
| `CountChanged(N)` | "The number of guides changed. It's now {N}." |

{F} reads "1 managed file" or "{F} managed files". The status messages are
in the table under [Messages](#messages).

## Game page

### Button

`UpdateRemoveGameAction` enables **Remove game** once the guide list has
loaded (`loadedGameGuideCount is not null`) and no close, import, game edit,
guide removal, game removal or metadata refresh is running. `RemoveGameHint`
is removed from the XAML and code, and the button's HelpText is cleared.

### Flow

1. The button disables the page actions, as in T04.2, and calls
   `DescribeAsync`. A null preview goes to the "already removed" path (T04.2
   ruling 14). A `GameRemovalException` shows its issue's message; any other
   exception shows the `Failed` message.
2. `RemoveGameDialog.Create(preview, changed, root)` opens a native
   `ContentDialog`: the title and body from the presentation table, **Remove**
   as the primary button and **Cancel** as the close button and
   `DefaultButton`, so Enter and Escape both cancel. No custom danger style.
   When `changed` is true, a `RemoveGameCountChanged` line above the body
   shows `CountChanged(N)`; otherwise it is collapsed.
3. **Cancel** restores the page and returns focus to **Remove game**.
4. **Remove** calls `RemoveAsync(gameId, preview.GuideCount)`:
   - `Removed`: reset the navigator to the Library, render it, show the
     status message and focus **Add game**, as in T04.2.
   - `CountChanged`: open the dialog again with `result.Current` and
     `changed: true`. This repeats until the counts match or the person
     cancels. A count of 0 uses the no-guides copy.
   - `NotFound`: the "already removed" path.
   - `GameRemovalException`: its issue's message, as for `DescribeAsync`.
     The page re-renders from the database, so after `Failed` or
     `RestoreFailed` it still shows the game and its guides.

### Changes to T04.2

- **Kept:** the button, its place and its AutomationId; the native dialog
  with Cancel as the default; the no-guides copy; the re-read before the
  dialog; the reset to the Library and focus on Add game after removal; the
  "already removed" path; best-effort artwork deletion with the startup
  sweep behind it; every existing empty-game smoke phase.
- **Changed:** the button is enabled with guides; the dialog states the
  counts; the remover moves from Core to Infrastructure and goes through
  the deletion journal; the empty-game delete's SQL moves from
  `ILibraryRepository.RemoveEmptyGameAsync` to `CommitGame`, keeping its
  single transaction.
- **Retired:** `RemoveGameHint`, the `HasGuides` outcome and message,
  `GuidesFirst`, `EmptyGameRemoval`, `EmptyGameRemovalOutcome`, the Core
  `GameRemover` and `RemoveEmptyGameAsync`. The `remove-disabled-with-guides`
  phase becomes `remove-enabled-with-guides`.

### Messages

All use the existing `ShellStatusInfoBar`.

| Outcome | Severity | Message |
| --- | --- | --- |
| Removed | Informational, auto-hides | "Removed {title}." |
| Removed, cleanup pending | Informational, auto-hides | "Removed {title}. Leftover files will be cleaned up the next time Desktop Guides starts." |
| `NotFound` | Informational, auto-hides | "{title} was already removed." |
| `Unsafe` | Error, closable | "{title} can't be removed because a guide's files were changed outside Desktop Guides." |
| `Failed` | Error, closable | "{title} couldn't be removed. The game is unchanged. Try again." |
| `RestoreFailed` | Error, closable | "{title} couldn't be removed. Restart Desktop Guides to finish restoring it." |

### UI Automation

`RemoveGameButton`, `RemoveGameDialog` and `RemoveGameMessage` keep their
IDs. `RemoveGameCountChanged` is new. `RemoveGameHint` is removed.

## Testing

### Core tests

- `GameRemovalPresentationTests`: the dialog title and body for 0, 1 and N
  guides, including the singular forms; `CountChanged`; every status message,
  including cleanup pending. The `GuidesFirst` and `HasGuides` asserts are
  removed.
- T04.2's Core `GameRemoverTests` are removed with the class; their cases
  move to `GameRemovalTests`.

### Infrastructure tests (TDD, real SQLite and NTFS on `pcsx2-win`)

`GameRemovalTests` replaces `EmptyGameRemovalTests`.

**Describe**
- Returns the guide count and total file count across nested trees, counting
  a missing content directory as 0.
- Returns null for an unknown game, and throws `ArgumentException` for
  `Guid.Empty`.
- Throws `Unsafe` for a link in any guide's tree.

**Remove**
- The happy path with two guides, one nested, deletes the `Games`,
  `GameMetadataLinks`, `Guides`, `ReadingStates` and `ReaderPreferences` rows,
  each `content/<guide>`, `.trash/<op>`, the `FileOperations` row and the
  artwork file. Another game's rows, content and artwork are unchanged byte
  for byte.
- `LastActiveGuideId` is cleared when it named one of the game's guides and
  kept when it named another game's guide.
- No guides (T04.2's cases): manual and linked games are removed, the
  provider game can be added again, the artwork file and folder are deleted,
  and a locked artwork file is swept at the next start. No journal row is
  written.
- `CountChanged`: an expected count of 1 against 2 guides, and of 2 against 1,
  returns the fresh preview and writes or moves nothing.
- A guide with a missing content directory is removed, with no trash left.
- An unknown game returns `NotFound` and writes nothing.
- A link inside a tree throws `Unsafe` before any journal row exists.
- A guide claimed by an unfinished file operation throws `RestoreFailed`, and
  nothing changes.
- A guide published into the game after it was removed leaves nothing, as in
  T04.2.

**Crash points**
- A fault at `Prepared`, at the first `MovedGuide` of two, at `Moved` or at
  `InCommit` throws `Failed`. Every guide is back in `content` with the same
  bytes, every row is intact, and no journal row or trash remains.
- A failed restore: at `Moved`, the checkpoint opens a trashed file without
  `FileShare.Delete` and throws, so the move back fails. `RestoreFailed` is
  thrown and the `Prepared` row stays. After the handle closes, a fresh
  repository's `InitializeAsync` restores every guide and clears the row.
- A failed cleanup: at `Committed`, the checkpoint opens a trashed file
  without `FileShare.Delete`. The result is `Removed` with `CleanupPending`,
  the rows are gone, and only `.trash/<op>/…` and the `Committed` row remain.
  After the handle closes, `InitializeAsync` deletes both.
- The commit guard: at `Moved`, the checkpoint inserts a guide row for the
  game directly. `CommitGame` throws, the transaction rolls back, and the
  files are restored.
- The existing reconciler, `GuideRemover` and import tests pass unchanged.

### Installed smoke

The `production-shell-ui` job is the gate.

**`seed-actions`** gains `Guided Remove Game`: provider-linked with artwork,
with two guides that have managed content, a TXT guide and a nested HTML guide
with an asset. One of them has a reading state and is the Resume guide.

**`describe-actions`** also reports, per game, whether each guide's content
directory exists, plus the trash entries, file operations and
`LastActiveGuideId`.

**`game-actions`**, in light and dark, each on a fresh seed:

| Phase | Checks |
| --- | --- |
| `remove-enabled-with-guides` (was `remove-disabled-with-guides`) | On Linked Rename Game, Remove game is enabled and `RemoveGameHint` is absent. |
| `guide-list-keeps-a-row`, rename phases, `next-game-details-at-top` | Unchanged. |
| `remove-with-guides-cancel` (new) | Open Guided Remove Game and press Remove game. `RemoveGameDialog` reads `Remove Guided Remove Game and its 2 guides?`, the body names the file count, and `RemoveGameCountChanged` is absent. Screenshot. Press Escape: both guides are listed and focus is on Remove game. |
| `remove-with-guides` (new) | Press Remove game, then Remove. The Library is shown, the status reads `Removed Guided Remove Game.`, no row has that title, Resume doesn't name a removed guide, and focus is on Add game. |
| Empty-game phases | Unchanged. |

After each run, `describe-actions` checks: Linked Rename Game is intact; no
Guided Remove Game rows, content directories or artwork folder; no trash
entries or file operations; and `LastActiveGuideId` names no removed guide.

**`game-actions-persisted`** is unchanged, and also checks that Guided Remove
Game hasn't come back.

**`design-language`** drops its `RemoveGameHint` asserts.

### Traceability

| Requirement | Evidence |
| --- | --- |
| T04.3 count-confirmed game removal through the trash protocol | `GameRemovalTests`; `remove-with-guides-cancel`; `remove-with-guides` |
| TR04.1 one recoverable removal of metadata, state, preferences, provider link, artwork and content | `GameRemovalTests` happy path and crash points; `describe-actions` after `game-actions` |
| TR04.2 Cancel changes neither records nor files | `remove-with-guides-cancel`; `CountChanged` tests |
| Changed count refreshes the dialog rather than deleting | `CountChanged` tests; `GameRemovalPresentationTests` (ruling 2) |
| No unrelated game directory is touched | `GameRemovalTests` happy path |

## Out of scope

- The fault-injection matrix across the import and delete protocols (T15.4).
- Selection and focus rules after removal beyond moving focus to Add game
  (T05.3).
- Undo, restore from trash, and a Remove action on Library rows.
- T04.2's deferred minors: the card scrollers as tab stops, the 48 px floor
  including padding, and Import and Edit refreshing the Remove button only in
  their `finally`.

## Documentation

- This spec gets a verification record.
- `implementation-plan.md` gets a T04.3 paragraph in M2.
- `e2e-testing.md` updates the Game actions row.
- `progress.md` gets a T04.3 row.
- The T04.2 design stays as shipped, with a note at its top that this spec
  supersedes its guides-first rule and `RemoveGameHint`.

## PR outcome

The PR names T04.3, its merged prerequisites, and the outcome. Its body shows
the counted removal dialog in light and dark.
