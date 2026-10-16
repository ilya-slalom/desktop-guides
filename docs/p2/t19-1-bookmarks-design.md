# T19.1 bookmarks: design

Status: approved in conversation, 16 October 2026. PR a (storage and
backup) is implemented on `feat/p2-t19-1-bookmarks`; PR b (the Bookmarks
tab) follows.

## Intent

A reader can mark places in any guide (TXT, HTML or PDF) and get back to
them. Bookmarks are named, listed in reading order, renamed, and deleted
with an undo. They travel through export and restore. They never change
the reading position or completion. A bookmark whose place moved or can't
be found says so and is kept; one whose saved place can't be read can
only be deleted. This is the M9 exit in the
[P2 plan](implementation-plan.md#m9-bookmarks), from
[section 5](../p2-technical-design.md#5-s19-bookmarks-t191) of the P2
design.

## Starting point

- **What exists:**
  - every reader saves and restores a `ReaderLocation`, with its outcomes
    (`Exact`, `Context`, `Approximate`, `Unavailable`);
  - every reader implements T18.2's `IOutlineReader`, so `OrderOf` gives
    any place's position in reading order, and `OutlineTree.EntryAt`
    finds the contents entry a place falls in;
  - T18.2's left pane, with its narrow-window overlay;
  - the schema at version 4, migrated forward and checked exactly on
    every open and restore;
  - export, which snapshots the whole SQLite database.
- **What's missing:** any stored bookmarks, and the pane's Bookmarks tab.

## The design

### Storage (Infrastructure)

- **The table:** schema version 5 adds `Bookmarks` in one forward
  migration, named like the existing tables:
  - `Id`: a 32-character GUID (`Guid.ToString("N")`);
  - `GuideId`: references `Guides(Id)` with `ON DELETE CASCADE`, so removing
    a guide removes its bookmarks without changing the removal code;
  - `LocatorJson`: the existing locator envelope, with
    `CHECK (length(LocatorJson) BETWEEN 1 AND 4096)`;
  - `Label`: `CHECK (length(Label) BETWEEN 1 AND 80)`;
  - `CreatedUtcMs` and `UpdatedUtcMs`, both at least 0;
  - an index on `GuideId`.
- **Exact limits in C#.** The SQL checks count characters. The repository
  also checks, before the write:
  - the locator is at most 4096 UTF-8 bytes, as `SaveReadingLocationAsync`
    does;
  - the label is 1 to 80 characters after trimming.
- **The repository:** new `ILibraryRepository` methods, under the existing
  write lock:
  - `ListBookmarksAsync(guideId)`;
  - `AddBookmarkAsync(guideId, locatorJson, label)`, returning the new
    `Bookmark`;
  - `RenameBookmarkAsync(id, label)`;
  - `DeleteBookmarkAsync(id)`;
  - `RestoreBookmarkAsync(bookmark)`, for undo: it puts the same row back,
    with its id and times.
- **The limit:** an add counts the guide's bookmarks in the same
  transaction, and a guide at 500 throws `BookmarkLimitException`. No
  trigger, so the checked schema stays plain tables and indexes.
- **No effect on other state:** bookmarks never touch `ReadingStates`,
  completion or `ReaderPreferences`.

### Backup

- **Export needs no change.** The snapshot already holds every table; the
  manifest's schema version becomes 5.
- **Restore** already refuses a newer schema, and checks integrity,
  foreign keys and the exact schema. With the constraints above, a backup
  with a dangling or oversized bookmark row is refused with the existing
  "invalid backup" outcome.
- **A version 4 backup** (from before P2) restores as today. The next
  library open migrates it to version 5 with no bookmarks, writing the
  usual recovery copy first.

### The pane and the keys (Production)

- **Tabs:** the left pane gets Contents and Bookmarks tabs, a Toolkit
  `Segmented` control, at the same 300 px width and with the same
  narrow-window overlay.
- **Opening it:**
  - Ctrl+Shift+C, or the Contents toolbar command, opens it on Contents;
  - Ctrl+Shift+B, or a new Bookmarks toolbar command, opens it on
    Bookmarks;
  - the command for the tab already showing closes the pane.
- **The Contents fallback**, "No sections found in this guide.", gains a
  "Bookmarks" link beside "Search in the guide".
- **Adding a bookmark:** Ctrl+D, or "Add bookmark" on the toolbar. The
  button is needed because keys pressed inside an HTML page don't reach
  the app (#115).
  - **The place** is the reader's current location.
  - **One place, one bookmark** (the user's choice, 16 October 2026): when
    a bookmark already has the same reading-order position, it is
    selected instead, with "This place is already bookmarked."
  - **The default label** is the contents entry the place falls in.
    Without one:
    - TXT: the first words of the text at the place;
    - HTML: the first words of the quote at the top of the view;
    - PDF: "Page 42".

    It's trimmed to whole words within 80 characters.
  - **Afterwards** the pane opens on Bookmarks with the new bookmark
    selected, and the status says "Bookmark added."
- **The list:**
  - **Order:** reading order, with ties going to the older bookmark;
    damaged bookmarks last.
  - **Screen readers** hear "label, location": "page 42" for PDF, and
    "37% through" for TXT and HTML, from the locator's estimate. A
    locator with no estimate gives the label alone.
  - **Enter or a click** goes to the bookmark, and the place is saved as a
    deliberate move:
    - `Exact` and `Context` go straight there;
    - `Approximate` shows "This bookmark's place may have shifted.";
    - `Unavailable` shows "This bookmark's place couldn't be found.", and
      the bookmark is kept.

    In a narrow window the overlay closes after the move.
  - **F2** edits the label in place: Enter saves, Escape cancels, and an
    empty label keeps the old one.
  - **Delete** removes the bookmark, with "Bookmark deleted." and an
    "Undo" action in the status line for 10 s.
  - **A damaged bookmark,** one whose locator doesn't decode, is listed as
    "Damaged bookmark" and offers only Delete.
- **Failures:**
  - a full guide: "This guide has 500 bookmarks. Delete one to add
    another.";
  - a failed write: "Couldn't save the bookmark.", and nothing changes.
- **The keyboard map:** T16.1's map gains Ctrl+D ("Add bookmark") and
  Ctrl+Shift+B ("Show bookmarks") in the Reader context, covered by its
  conflict tests. F2 and Delete act only inside the list.

### Tests

- **Infrastructure:**
  - **migration:** a populated version 4 database upgrades with an empty
    `Bookmarks` table, and a failed migration stays at version 4;
  - **repository:** add, list, rename, delete and restore; the cascade
    with the guide; the limit at 500; the byte and label limits;
  - **backup:** the snapshot holds bookmark rows; restore refuses a
    dangling or oversized row; a version 4 backup restores and then
    migrates;
  - tests that hard-code version 4 move to 5.
- **Core:** default labels, the reading-order sort, the duplicate check,
  and the restore outcome messages.
- **Installed** (the `navigation` group, both lanes):
  - `bookmark-txt`: add with Ctrl+D, rename with F2, follow, the saved
    place, and delete then undo;
  - `bookmark-html`: add from the toolbar, then follow;
  - `bookmark-pdf`: the "Page N" label;
  - `bookmark-restart`: bookmarks survive a restart;
  - `bookmark-changed`: a seeded bookmark for older content shows the
    `Unavailable` status, and a seeded damaged bookmark offers only
    Delete.

## Delivery

| PR | Content | Installed modes |
| --- | --- | --- |
| a | The table and migration, the repository and its limits, backup and restore coverage | None (no UI) |
| b | The Bookmarks tab, Ctrl+D and Ctrl+Shift+B, F2, Delete with undo, the restore statuses, damaged bookmarks | `bookmark-txt`, `bookmark-html`, `bookmark-pdf`, `bookmark-restart`, `bookmark-changed` |

## Deviations from the P2 design

1. **The archive verifier is unchanged.** It checks only the zip's
   structure and never opens the database. Foreign keys and sizes are
   checked at restore, by the existing database validation and the
   table's constraints.
2. **Names follow the schema:** `Bookmarks`, `GuideId`, `LocatorJson` and
   so on, not the design's `Bookmark` and `guideId`.
3. **One place, one bookmark:** adding at an already bookmarked place
   selects the existing bookmark (the user's choice).
4. **"Add bookmark" is also a toolbar command**, because of #115.

## PR a verification record

See the [PR a plan](t19-1-bookmarks-plan.md).

- **Core,** failing first and then passing on the host:
  `BookmarkRulesTests` (10). The full suite passes, 1275/1275.
- **Infrastructure,** on the host:
  - **the migration:** an upgrade from a populated version 4, and a
    failed version 5 migration that stays at 4 (2);
  - **the store** (10, including the review's double undo): list order
    and fields, rename, delete and undo, the limit at 500, an undo at the
    limit, a missing guide on add and on undo, the cascade with the
    reading state left alone, and limits refused before the write;
  - **backup** (4): the snapshot holds bookmarks; restore refuses a
    dangling and an oversized row; a version 4 backup restores, then
    migrates with no bookmarks. The two refusal tests passed when
    written, because they pin existing validation. A temporary change to
    valid rows made both fail.

  The full suite passes, 828/828.
- **Production:** it builds with 0 warnings, with warnings as errors.
- **Rulings:** the plan missed three bare `BackupTo(..., 4)` arguments in
  test fixtures. Each means the live database's version and now uses
  `LibrarySchema.CurrentVersion`.
- **Final review:** 0 Critical, 1 Important and 3 Minor.
  - **The Important finding is fixed,** with a failing test first: an
    undo run twice gave a raw primary-key error, or "This guide has 500
    bookmarks." at the limit. A restore now leaves a bookmark that is
    already stored as it is.
  - **The minors are deferred:** a hand-edited non-hex id, a hand-edited
    label of astral characters, and a test that could delete the real
    guide first.

