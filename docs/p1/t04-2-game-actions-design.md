# T04.2 Game rename and remove actions design

Status: implemented on `feat/p1-t04-2-game-actions`; verified by CI run
<run-id>. Prerequisites T04.1 (PR #11), T04.4 (PR #14), T05.4 (PR #16) and
T11.1 (PR #6) are merged.

## Intent

The Game page's actions stay bound to the Game ID. Renaming a game through
**Edit game** keeps everything else attached to it: its provider link and
snapshot, artwork, Guide IDs, reading state, Resume, and the selected guide,
both after the page refreshes and after the app restarts.

The Game page also gets **Remove game**. In this task it removes only a game
with no guides. A game with guides shows the button disabled, with a visible
line saying the guides must be removed first. T04.3 later enables it for every
game, behind a confirmation that counts the guides, using the trash journal.

Traces: the T04.2 row of [implementation-plan.md](implementation-plan.md)
(TR03.1, TR04.3), [work-breakdown.md](../work-breakdown.md) S04 and T04.2,
and [p1-technical-design.md](../p1-technical-design.md) (T04.2).

Decisions made during brainstorming:

- **Scope.** Rename gets proof, not new UI: T04.1's **Edit game** dialog
  already changes only the title, platform and notes. Removal covers games
  without guides; games with guides wait for T04.3.
- **Remove on a game with guides.** The button is shown and disabled, with
  the visible hint `Remove this game's guides first.`. A disabled button
  cannot take focus, so the visible line carries the explanation.
- **Data access.** Approach A: one repository method deletes the game row in
  a write transaction, and only when the game has no guides. Artwork is
  deleted afterwards, best effort; the existing startup artwork sweep removes
  anything left behind. The trash journal is not used.

## Components

### Core types (Core/Library)

```csharp
public enum EmptyGameRemovalOutcome { Removed, NotFound, HasGuides }

public sealed record EmptyGameRemoval(
    EmptyGameRemovalOutcome Outcome, string? ArtworkRelativePath);
```

`ArtworkRelativePath` is set only for `Removed`, and only when the game had
artwork.

`ILibraryRepository` gains:

```csharp
Task<EmptyGameRemoval> RemoveEmptyGameAsync(
    Guid gameId, CancellationToken token = default);
```

### Remover (Core/Library/GameRemover.cs)

```csharp
public sealed class GameRemover(ILibraryRepository repository, IArtworkStore artwork)
{
    public Task<EmptyGameRemovalOutcome> RemoveAsync(
        Guid gameId, CancellationToken token = default);
}
```

- Calls `RemoveEmptyGameAsync`.
- On `Removed` with an artwork path, calls `IArtworkStore.Delete(path)`. An
  `IOException` or `UnauthorizedAccessException` from the delete is ignored:
  the row is already gone, and the next startup sweep deletes unreferenced
  artwork.
- Returns the outcome. `NotFound` and `HasGuides` touch no files.

### Presentation (Core/Library/GameRemovalPresentation.cs)

| Member | Text |
| --- | --- |
| `DialogTitle(title)` | `Remove {title}?` |
| `DialogBody` | `This removes the game and its details from Desktop Guides.` |
| `GuidesFirst` | `Remove this game's guides first.` |
| `Removed(title)` | `Removed {title}.` |
| `AlreadyRemoved(title)` | `{title} was already removed.` |
| `HasGuides(title)` | `{title} has guides now, so it wasn't removed.` |
| `Failed(title)` | `{title} couldn't be removed. The game is unchanged. Try again.` |

### Repository (Infrastructure/Storage/SqliteLibraryRepository.cs)

`RemoveEmptyGameAsync` throws `ArgumentException` for `Guid.Empty`. Otherwise
it runs through the existing `WriteAsync` path in one transaction:

1. `SELECT ArtworkRelativePath, (SELECT COUNT(*) FROM Guides WHERE GameId =
   $id) FROM Games WHERE Id = $id`.
2. No row → `NotFound`. A guide count above zero → `HasGuides`.
3. Otherwise `DELETE FROM Games WHERE Id = $id AND NOT EXISTS (SELECT 1 FROM
   Guides WHERE GameId = $id)`, commit, and return `Removed` with the
   artwork path.

The provider link, snapshot and artwork path are columns of the `Games` row,
so the delete removes them, and the unique provider index no longer blocks
adding the same provider game again. There is no schema change.

A guide import into the removed game fails at publication on the
`Guides.GameId` foreign key, so it never lists a partial guide; T06.3's
failure path already removes the staged files.

### Game page UI (ShellWindow)

**Header.** A **Remove game** button (`AutomationId="RemoveGameButton"`,
`DesktopGuidesSecondaryActionButtonStyle`) follows **Edit game**. A hint
(`AutomationId="RemoveGameHint"`, `DesktopGuidesSecondaryBodyStyle`) sits
under the header row and reads `GameRemovalPresentation.GuidesFirst`.

**State.** One `UpdateRemoveGameAction` helper sets:

- the button enabled only when the Game page has finished loading, the game
  has no guides, and no import, game edit, metadata refresh, guide removal or
  game removal is running;
- the hint visible only when the loaded game has guides; the button's
  `AutomationProperties.HelpText` carries the same text while it is visible.

**Confirmation.** `RemoveGameDialog`, in the pattern of `RemoveGuideDialog`:
title `DialogTitle(title)`, body `DialogBody` (`AutomationId=
"RemoveGameMessage"`), primary **Remove**, close **Cancel**, and Cancel as
the default button, so Enter and Escape both cancel. The dialog's
`AutomationId` is `RemoveGameDialog`.

**Flow.** `RemoveGameClicked` guards with a `gameRemoveRequested` flag,
disables the header actions, and runs on the navigation queue:

| Case | Result |
| --- | --- |
| Cancel | Nothing changes; focus returns to Remove game. |
| `Removed` | `navigator.ResetToLibrary()`, so Back cannot return to the removed page; the Library renders and keeps its search query; status `Removed {title}.`; focus moves to Add game. |
| `NotFound` | As `Removed`, with status `{title} was already removed.` |
| `HasGuides` | The Game page re-renders, which disables the button and shows the hint; warning status `HasGuides(title)`. |
| Exception | The Game page stays; error status `Failed(title)`; focus returns to Remove game. |

Closing the window while the dialog is open closes it as Cancel, following
the guide-removal flow.

### Rename

No product change is planned. `UpdateGameAsync` writes only `Title`,
`Platform`, `Notes` and `UpdatedUtcMs`, and the Game page render already
reselects the previous guide by ID. The tests below pin this behavior; a
failure found while writing them is fixed in this task.

## Testing

### Core tests (TDD)

**`GameRemoverTests`**, with fakes:

- `Removed` with an artwork path deletes that path;
- `Removed` without artwork deletes nothing;
- `HasGuides` and `NotFound` delete nothing;
- an `IOException` from the artwork delete still returns `Removed`.

**`GameRemovalPresentationTests`:** the dialog title and body, the hint, and
each status line.

### Infrastructure tests (TDD, real SQLite on `pcsx2-win`)

**`GameRenameTests`.** Rename a provider-linked game with artwork, two
guides, one reading state and `LastActiveGuideId` set, then check:

- the provider link, snapshot, artwork path and `CreatedUtc` are unchanged;
- the Guide IDs, the reading state and `LastActiveGuideId` are unchanged;
- the game summary shows the new title under the same Game ID;
- all of the above after reopening the repository on the same data root.

**`EmptyGameRemovalTests`:**

- a manual game without guides → `Removed`, and the game is gone;
- a linked game without guides → `Removed` with its artwork path, and the
  same provider game ID can be added again;
- a game with a guide → `HasGuides`; the game, the guide and its reading
  state are intact;
- an unknown ID → `NotFound`; `Guid.Empty` throws;
- other games are untouched;
- `GameRemover` with the real `ManagedArtworkStore` deletes the artwork file
  and its game folder;
- with the artwork file held open so it cannot be deleted, `GameRemover`
  still returns `Removed`, and the next `InitializeAsync` sweeps the file;
- publishing a guide into a game removed after the preview fails, leaving no
  guide row, no staged entry and no content entry.

### Installed smoke

The `production-shell-ui` job is the gate.

**`seed-actions` (new).** On an empty data root:

- `Linked Rename Game`: provider-linked with artwork, and two guides.
  `Alpha Route Guide` has a reading state of about 45% opened yesterday;
  `Beta Route Guide` is the Resume guide.
- `Empty Linked Game`: provider-linked with artwork, and no guides.

**`describe-actions` (new)** prints, as JSON: the game count, each game's ID,
title, provider game ID and artwork path, whether each game's artwork folder
exists, the Guide IDs, and the reading-state rows. It prints no credential.

**`game-actions` (new)**, in light and in dark, each on a fresh seed:

1. On the Library, read Linked Rename Game's HelpText, then open it. Remove
   game is disabled, and `RemoveGameHint` reads `Remove this game's guides
   first.`.
2. Select Beta Route Guide, press Edit game, rename the game to `Renamed
   Linked Game` and save. The heading shows the new title, Beta Route Guide
   is still selected, focus is on Edit game, and the provider summary is
   still shown.
3. Go Back. The Library row is named `Renamed Linked Game`, and its HelpText
   facts equal the ones read before the rename.
4. Open Empty Linked Game. Remove game is enabled and the hint is collapsed.
   Press Remove game: `RemoveGameDialog` shows `Remove Empty Linked Game?`
   and the body. Press Escape: the dialog closes, the game stays, and focus
   is on Remove game.
5. Press Remove game again, then Remove. The Library is shown, the status
   reads `Removed Empty Linked Game.`, no row has that name, and focus is on
   Add game.
6. Screenshots: the Game page with the hint, and the confirmation dialog.

The install script then runs `describe-actions` and checks: one game, with
Linked Rename Game's original ID, provider game ID and artwork; no artwork
folder for the removed game; the seeded Guide IDs; and the Alpha reading
state.

**`game-actions-persisted` (new)**, after the light run, in a relaunched app:

1. The Library lists `Renamed Linked Game`, and Resume names Beta Route
   Guide.
2. Resume opens Beta Route Guide. Back shows the Game page headed `Renamed
   Linked Game`, with Beta Route Guide selected.
3. Alpha Route Guide's HelpText still says about 45 percent, and the provider
   summary is shown.
4. The `catalog` no-provider-traffic check reports no non-loopback
   connections.

### Traceability

| Requirement | Evidence |
| --- | --- |
| T04.2 ID-bound rename and remove actions | `GameRenameTests`; `EmptyGameRemovalTests`; `game-actions` |
| TR03.1 stable IDs | `GameRenameTests`; `describe-actions` after `game-actions` |
| TR04.3 the provider link and snapshot survive a local rename and work offline | `GameRenameTests`; `game-actions-persisted` |
| Rename keeps view selection after refresh and restart | `game-actions` step 2; `game-actions-persisted` step 2 |

## Out of scope

- Removing a game with guides, and its count confirmation (T04.3).
- Selection and focus rules after removal beyond moving focus to Add game
  (T05.3).
- Undo after removal.
- A Remove action on Library rows.

## Documentation

- This spec gets a verification record.
- `implementation-plan.md` gets a T04.2 paragraph in M2.
- `e2e-testing.md` gets a Game actions row.
- `progress.md` gets a T04.2 row.

## PR outcome

The PR names T04.2, its merged prerequisites, and the outcome. Its body shows
the Game page with the hint and the removal dialog, in light and dark.

## T04.2 verification record

- **Unit tests.** On `pcsx2-win`, Infrastructure 407/407 and Core 268/268
  passed. The new tests are:
  - `GameRemoverTests`: artwork deleted only on `Removed`, nothing deleted
    for `HasGuides`, `NotFound` or a game without artwork, and `Removed`
    kept when the artwork delete throws;
  - `GameRemovalPresentationTests`: the dialog title and body, the hint,
    and each status line;
  - `GameRenameTests`: a rename keeps the provider link, snapshot, artwork
    path, `CreatedUtc`, Guide IDs, reading state and `LastActiveGuideId`,
    and the summary shows the new title under the same Game ID, before and
    after reopening the repository;
  - `EmptyGameRemovalTests`: manual and linked games removed, the provider
    game addable again, `HasGuides` leaving the game, guide and reading
    state intact, `NotFound`, `Guid.Empty`, other games untouched, the
    artwork file and folder deleted, a locked artwork file swept at the
    next start, and a publication into a removed game leaving nothing.
- **Installed.** CI run [<run-id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run-id>)
  passed `production-shell-ui`:
  - `game-actions`, light and dark: phases `remove-disabled-with-guides`,
    `rename-keeps-selection`, `rename-library-row`, `remove-confirm`,
    `remove-escape-cancels`, `remove-enter-cancels` and `removed`;
  - `game-actions-persisted`: phases `persisted-resume`,
    `persisted-selection`, `persisted-facts` and
    `persisted-no-provider-traffic`, with zero non-loopback connections;
  - `describe-actions` after each run: one game with the original ID,
    provider game ID and artwork; no artwork folder for the removed game;
    the seeded Guide IDs, Alpha's reading state and Beta as Resume;
  - `design-language`: Remove game and its hint inside the window at both
    widths.
- **Rulings.** Rulings 1–21 in the
  [plan](t04-2-game-actions-plan.md#rulings-against-the-spec), each with
  what it costs if wrong. One was needed during implementation: CI run
  [36810227220](https://github.com/ilya-slalom/desktop-guides/actions/runs/36810227220)
  failed because the plan's `game-actions` block waited again for
  `Library ready.`, which the smoke's shared prelude had already consumed.
  The block now checks only the Library heading, as `catalog-facts` does.
- **Evidence.**
  - [Guides-first hint, light](evidence/t04-2-game-actions/remove-game-hint-light.png)
  - [Guides-first hint, dark](evidence/t04-2-game-actions/remove-game-hint-dark.png)
  - [Remove confirmation, light](evidence/t04-2-game-actions/remove-game-confirm-light.png)
  - [Remove confirmation, dark](evidence/t04-2-game-actions/remove-game-confirm-dark.png)
