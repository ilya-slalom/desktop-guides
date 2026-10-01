# T05.3 Stable navigation design

Status: design approved in brainstorming on 1 October 2026;
implementation planned in
[t05-3-stable-navigation-plan.md](t05-3-stable-navigation-plan.md) and
verified in CI run
[36857845252](https://github.com/ilya-slalom/desktop-guides/actions/runs/36857845252)
(see the [verification record](#t053-verification-record)).
Prerequisites T04.2 (PR #25), T05.2 (PR #24), T06.3 (PR #19) and T15.3
(PR #22) are merged.

## Intent

Moving around the shell and changing the library keep the person's place:

- **Back to Library** returns focus to the game row the person opened, keeps
  the search query and its filtered rows, and never leaves focus on the
  disabled Back button.
- **Back to a Game** restores the guide that was selected there, whether the
  person returns from that guide's Reader, from Settings, or from another
  game.
- **Library and guide-list changes** (import, game rename, game removal,
  guide removal, metadata refresh) keep selection and focus on the same item
  by ID. If that item is gone, the nearest surviving row by its old position
  is used instead.
- **Late async work** (a startup render, a metadata refresh, a render
  overtaken by a newer navigation) never moves focus or selection to a stale
  row and never posts its status on another page.

Traces: the T05.3 row of [implementation-plan.md](implementation-plan.md)
(TR05.1), [work-breakdown.md](../work-breakdown.md) S05 and T05.3, and
[p1-technical-design.md](../p1-technical-design.md) (T05.3 verifies that the
Library query survives Library → Game → Reader → Game → Library).

Decisions made during brainstorming:

- **Approach A: navigator anchors.** Each back-stack entry carries an optional
  anchor ID next to its route. The shell reads the anchor when it renders the
  entry. Route records and their ID-only equality are unchanged.
- **One resolution rule.** A pure Core helper, `ListAnchor.Resolve`, picks the
  row to use after any list change. It replaces the index-based neighbour in
  guide removal.
- **Keyboard and mouse Back are left out.** Alt+Left and XButton1 are a
  recorded follow-up.
- **The query keeps living in `LibrarySearchInput`.** It already survives
  every route; T05.3 adds the tests that prove it.

## Already working (audit, kept as-is)

- The Library query survives every route, Resume, import, rename and removal.
- Reader → its Game restores the guide selection and focus through
  `pendingGuideFocus` and `TryRestoreGuideFocus`.
- Import selects the new guide; game rename keeps the selection.
- Switching games clears the previous game's guide list.

## Components

### Navigator anchors (Core/Navigation/ShellNavigator.cs)

Each entry in the back stack, and the current entry, is a route plus an
optional `Guid? Anchor`:

| Entry | Anchor means |
|---|---|
| `LibraryRoute` | The game whose row gets focus. |
| `GameRoute` | The guide that is selected. |
| `ReaderRoute`, `SettingsRoute` | No anchor; always `null`. |

New members:

```csharp
public Guid? CurrentAnchor { get; }
public void SetAnchor(Guid? anchorId); // throws InvalidOperationException on Reader or Settings
```

The navigator sets anchors itself on the moves it already knows about:

- `OpenGame(gameId)` from the Library sets the Library entry's anchor to
  `gameId` before pushing it.
- `OpenReader(guideId, gameId)` sets the anchor of the Game entry it leaves to
  `guideId`. When it pushes a Game entry for Resume, that entry's anchor is
  `guideId`, and the Library entry under it gets `gameId`.

Everything else goes through `SetAnchor`; the shell calls it whenever the
Game page's guide selection changes. `GoBack()` restores the previous entry
together with its anchor. `ResetToLibrary()` starts a clean Library entry
with no anchor. Opening the route that is already current is still a no-op
and keeps its anchor.

### ListAnchor (Core/Navigation/ListAnchor.cs)

```csharp
public static Guid? Resolve(
    IReadOnlyList<Guid> previousIds,
    IReadOnlyList<Guid> currentIds,
    Guid? anchorId);
```

IDs in each list are unique. The result is:

1. `anchorId`, if `currentIds` contains it (the order may have changed);
2. otherwise, if `previousIds` contains `anchorId`, the nearest ID that is in
   both lists, searching `previousIds` after the anchor's old position first,
   then before it;
3. otherwise `null`.

`null` tells the caller to use its own fallback.

### Library page (Production/ShellWindow.xaml.cs)

Selecting a Library row opens that game, so restoring the Library anchor
focuses the row and never sets `SelectedItem`.

- **After Back to the Library,** if `CurrentAnchor` is a visible row, the
  shell scrolls it into view and focuses it after layout through a new
  `libraryFocusPending` and `TryRestoreLibraryFocus`, which follow
  `TryRestoreGuideFocus`: an immediate attempt and one more through
  `DispatcherQueue`, both guarded by `renderGeneration` and the route.
  Other routes to the Library leave focus alone; after a game removal
  focus stays on Add game (T04.3).
- **If the anchor isn't visible** (the query hides it, or it was removed),
  focus goes to the first row. If no row is shown, it goes to
  `LibrarySearchInput`, or to Add game when search is disabled. Focus is
  never left on the disabled Back button.
- **No in-place reload.** Nothing re-renders the Library while it is
  showing (import, rename and removal happen on the Game page), so there is
  no in-place reload to handle.

### Game page (Production/ShellWindow.xaml.cs)

- **Selection comes from `navigator.CurrentAnchor`,** falling back to the
  list's own selection only when it still shows this game. The `GuideList` selection handler calls
  `SetAnchor` for changes made by the person (outside
  `settingGuideSelection`).
- **Reader → Game** keeps the existing `pendingGuideFocus` behaviour.
- **Guide removal:**
  1. Record the visible guide IDs.
  2. Set the anchor to the guide being removed.
  3. Reload the guides.
  4. Select `ListAnchor.Resolve(previous, current, removedId)` and set it as
     the anchor. If it returns `null`, clear the selection.
- **Metadata refresh** still re-renders only if the same game is current.
  Its status message is dropped if the person has left that game. The
  re-render keeps the anchored selection, and focuses the guide row again if
  focus was inside `GuideList` before.

### Async safety

- The startup render runs through `NavigationActionQueue` like every other
  render, so a quick first click can't be overwritten by it.
- Every focus or selection restore checks `renderGeneration` and the current
  route first.

## Error handling

- **Anchors are hints.** A missing anchor never raises an error; callers fall
  back as described above.
- **`SetAnchor` on a Reader or Settings entry throws.** It is a programming
  error, covered by unit tests.
- **Focus restore gives up quietly** when the container isn't realized after
  the second attempt, or when the generation or route changed. It never
  loops and never takes focus from a newer page.
- **Nothing is persisted.** No SQLite, settings or file changes; anchors live
  in memory like the back stack.

## Testing

### Core tests (xUnit, next to `ShellNavigatorTests`)

Navigator:

- `OpenGame` from the Library sets the Library anchor; `GoBack` returns it.
- `OpenReader` sets the Game entry's anchor; `GoBack` returns it.
- Resume from the Library pushes a Game entry anchored on the guide and a
  Library entry anchored on the game.
- `SetAnchor` replaces the current anchor; it throws on Reader and Settings.
- `ResetToLibrary` clears every anchor.
- Opening the current route again keeps its anchor.
- Route equality stays ID-only.

`ListAnchor.Resolve`:

- The anchor survives, including after a reorder.
- The anchor was removed: the next row.
- The last row was removed: the previous row.
- The anchor and its neighbours were removed: the nearest further survivor.
- The current list is empty: `null`.
- The anchor is `null`, or wasn't in the previous list: `null`.

### Installed smoke (CI `production-shell-ui` is the gate)

New or extended phases in `tools/p1/windows_shell_ui_smoke.ps1`:

- **Back to Library focus.** After Game → Back, the focused element is the
  game row that was opened. The workaround that refocuses `AddGameButton`
  and tabs to the list is removed.
- **Query retention.** With a query set, the search box text and filtered
  rows survive Library → Game → Reader → Back → Back, Resume, import, game
  rename and game removal.
- **Game → Settings → Back** keeps the selected guide.
- **Guide removal** selects the neighbouring guide and keeps the query.
- **Stale refresh.** CI has no provider keys, so a refresh fails at once
  and can't be ordered against a navigation. The guard is a Review Focus
  item backed by the route check in the queued refresh action and the
  generation checks; the verification record says so.
- **Guide order.** Nothing writes `LastOpenedUtcMs` yet, so opening a guide
  doesn't reorder the list and the smoke covers both next-row and
  previous-row removal.

UI tests assert only what app code controls: which element has focus, which
row is selected, the search text and the visible rows.

### Traceability

TR05.1 stays the governing requirement. The verification record lists each
smoke phase against the exit criteria: ID-based selection after
rename/import/removal, Reader → Game selection and focus, Back to Library
query, and no stale rows after async refresh.

## Out of scope (follow-ups)

- Keyboard and mouse Back (Alt+Left, XButton1).
- A limit on the back stack's size.
- Guide rename; there is no guide edit feature yet.
- Keeping the Library query across a relaunch.

## Documentation

- `docs/work-breakdown.md`: note the follow-ups under S05.
- `docs/progress.md` and `docs/p1/implementation-plan.md`: the T05.3 row and
  paragraph once verified.
- `docs/p1/e2e-testing.md`: the new smoke phases.
- `docs/p1-technical-design.md`: a note that the navigator carries per-entry
  anchors.

## PR outcome

Back and list changes keep the person's place by ID: Back to Library
focuses the opened game with the query intact, Back to a Game restores its
selected guide, removals select the nearest survivor, and late async work
can't move focus or selection to a stale row.

## T05.3 verification record

- **Unit tests.** On `pcsx2-win`, Core 301/301 and Infrastructure 429/429
  passed. The new tests are:
  - `ListAnchorTests`: the anchor kept, including after a reorder; the next
    row, the previous row, and the nearest further survivor when the
    anchor is removed; `null` for an empty list, a `null` anchor, or an
    anchor that wasn't in the previous list; null lists rejected;
  - `ShellNavigatorTests`: `OpenGame` and `OpenReader` set the anchors
    `GoBack` returns; Resume from the Library anchors the Game entry on the
    guide and the Library entry on the game; `SetAnchor` replaces the
    anchor and throws on Reader and Settings; `ResetToLibrary` clears every
    anchor; reopening the current route keeps its anchor; route equality
    stays ID-only.
- **Installed.** CI run
  [36857845252](https://github.com/ilya-slalom/desktop-guides/actions/runs/36857845252)
  on `7c1f467` passed every job, `production-shell-ui` included:
  - `stable-navigation`, light and dark, against each exit criterion:
    - Back to Library restores its query and the opened row:
      `resume-back-keeps-query-and-row` and `back-focuses-opened-row`;
    - Reader → Game keeps guide selection and focus:
      `reader-back-keeps-guide`, plus `settings-back-keeps-guide` and
      `other-game-back-keeps-guide`;
    - ID-based selection after removal: `remove-selects-next-guide`,
      `remove-last-selects-previous-guide` and `remove-keeps-query`;
    - after rename: `rename-keeps-query-and-falls-back`;
    - `navigation-no-provider-traffic`.
  - after import: `import-publish` (light) phase `query-kept-after-import`;
  - `catalog` (`catalog-keyboard`) and `library-search`
    (`search-kept-after-back`), light and dark, now assert the opened row
    has focus after Back.
- **Stale refresh.** No smoke phase: CI has no provider keys, so a refresh
  fails at once and can't be ordered against a navigation. The route check
  in the queued refresh action and the generation checks were covered in
  the final review.
- **Evidence.** Back to Library with the query kept and the opened row
  focused, not selected:
  [light](evidence/t05-3-stable-navigation/library-focus-restored-light.png)
  and [dark](evidence/t05-3-stable-navigation/library-focus-restored-dark.png).
- **Rulings.** Planning rulings 1–7 in the
  [plan](t05-3-stable-navigation-plan.md#rulings-carried-from-planning); the final review found no
  functional defects, and its two smoke races were fixed in `7c1f467`.
