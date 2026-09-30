# T05.2 Library search design

Status: implemented on `feat/p1-t05-2-library-search`; verified by CI run
36726600007. Prerequisite T05.1 (PR #23) is merged.

## Intent

The Library gets a title search. Typing filters the game list to games whose
own title, or one of whose guide titles, contains the query. Matching ignores
case, accents and character width, so `pokemon` finds `Pokémon` and `ＦＦ`
finds `FF`. The list keeps its T05.1 activity order.

Search reads only the SQLite metadata already loaded for the Library. It
never opens guide files and never calls a provider. Debouncing exists only to
spare UI work.

The Library area shows exactly one of four views: loading, empty library, no
results, or the list. A visible **Clear search** button appears whenever a
query is active. Unread guides still say `Not started`.

Traces: the T05.2 row of [implementation-plan.md](implementation-plan.md)
(TR05.1, TR05.2), [work-breakdown.md](../work-breakdown.md) S05 and T05.2,
and [p1-technical-design.md](../p1-technical-design.md) (T05.2).

Decisions made during brainstorming:

- **Scope.** There is one Library search box. A game matches when its own
  title matches, or when any of its guides' titles match. The Game page gets
  no search.
- **Matching.** Substring match with the invariant culture, using
  `IgnoreCase | IgnoreNonSpace | IgnoreWidth`. The query is trimmed, and a
  blank query shows every game.
- **Data access.** Approach A: filter in Core, in memory, over the summaries
  the Library already loads. Neither a SQL function nor a folded search
  column is needed.
- **Guide-only matches.** When a game matches only through a guide, its row
  names that guide, so the user can see why the game is listed.

## Components

### Core types (Core/Library)

`LibraryGameSummary` gains the game's guide titles:

```csharp
public sealed record LibraryGameSummary(
    Game Game, int GuideCount, DateTimeOffset LastActivityUtc,
    IReadOnlyList<string> GuideTitles);
```

`GuideTitles` is in title order, then Guide ID. It is empty for a game with
no guides.

### Search (Core/Library/LibrarySearch.cs)

```csharp
public sealed record LibrarySearchMatch(
    LibraryGameSummary Summary, string? MatchedGuideTitle);

public static class LibrarySearch
{
    public static bool Matches(string title, string? query);
    public static IReadOnlyList<LibrarySearchMatch> Filter(
        IReadOnlyList<LibraryGameSummary> games, string? query);
}
```

- **`Matches`.** Trims the query. A null or blank query returns `true`.
  Otherwise the result is
  `CultureInfo.InvariantCulture.CompareInfo.IndexOf(title, query,
  CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace |
  CompareOptions.IgnoreWidth) >= 0`.
- **`Filter`.** Keeps the input order and handles each game in turn:
  - when the game's own title matches, keep it with
    `MatchedGuideTitle = null`;
  - otherwise, when a guide title matches, keep it with the first matching
    title in `GuideTitles` order;
  - otherwise, drop it.

  A blank query returns every game with `MatchedGuideTitle = null`.

### Presentation (Core/Library/CatalogPresentation.cs)

- **Visible facts.** `GameFacts` takes an optional matched guide title. When
  it is set, the facts end with `Guide: {title}`.
- **Spoken facts.** The accessible text appends the same fact, with the same
  wording.
- With no matched title, both outputs are unchanged from T05.1.

### Repository (Infrastructure/Storage/SqliteLibraryRepository.cs)

`ListGameSummariesAsync` runs two statements in one read transaction, so the
counts and titles come from the same snapshot:

1. The existing T05.1 summary statement, unchanged.
2. `SELECT GameId, Title FROM Guides ORDER BY GameId, Title, Id`.

The titles are grouped by game in C#. There is no schema change and no
guide-file access.

### Library UI (ShellWindow)

**Search row.** A new row between the Resume button and the list holds:

- an `AutoSuggestBox`:
  - `AutomationId="LibrarySearchInput"`, `AutomationProperties.Name="Search library"`;
  - `PlaceholderText="Search games and guides"`;
  - `QueryIcon="Find"`, with no suggestions;
- a **Clear search** button (`AutomationId="LibrarySearchClear"`), visible
  only when the trimmed query is not blank.

The whole row is disabled while the Library is loading or empty.

**Views.** A single `ShowLibraryState` helper makes exactly one of these
visible:

| View | When | Content |
| --- | --- | --- |
| `LibraryLoadingState` (new) | from render start until summaries arrive | ProgressRing and `Loading library…` |
| `LibraryEmptyState` (existing) | no games | unchanged copy |
| `LibraryNoResultsState` (new) | games exist, no match | `No games or guides match "{query}".` and `Check the spelling or clear the search.` |
| `GameList` | at least one match | filtered rows |

**Flow.**

- **Loading.** The Library render loads the summaries once and keeps them in
  a field. It then applies the box's current text; the render never clears
  the query. So after an import, a removal, or a return to the Library, the
  query is still applied.
- **Typing.** A `TextChanged` event with reason `UserInput` restarts a
  200 ms `DispatcherQueueTimer`. On tick, the filter runs over the cached
  summaries.
- **Enter.** `QuerySubmitted` applies the filter immediately.
- **Clear search.** Empties the box, applies the filter immediately, and
  focuses the box.
- **What the filter does:**
  1. calls `gameArtwork.CancelAll()`;
  2. rebuilds `LibraryGameItem`s from the matches;
  3. sets the view;
  4. shows the Clear button or hides it;
  5. announces the result through the transient status: `{n} of {total}
     games match.` or `No games match.`, and nothing for a blank query.

  None of these steps reads the database or the network.
- **Rows.** `LibraryGameItem` takes a `LibrarySearchMatch`.
- **Selection.** Replacing the items clears the selection without opening a
  game. Resume and Add game are unchanged.

## Testing

### Core tests (TDD)

**`LibrarySearchTests`**, for `Matches`:

- null, blank, whitespace and padded queries;
- mixed case: `zELDA` finds `The Legend of Zelda`;
- accents both ways: `pokemon` finds `Pokémon`, `Pokémon` finds `Pokemon`,
  and `OKAMI` finds `Ōkami`;
- width: `ＦＦ` finds `FF`;
- non-Latin scripts: `ドラクエ` finds `ドラクエXI`, and `ВЕДЬМАК` finds
  `Ведьмак`;
- a match in the middle of a title;
- no match.

**`LibrarySearchTests`**, for `Filter`:

- a game-title match has no matched guide;
- a guide-only match gives the first matching title in `GuideTitles` order;
- input order is preserved;
- a blank query keeps every game;
- an empty input.

**`CatalogPresentationTests`:**

- `Guide: {title}` appears in the visible facts and the spoken text;
- it is absent when the match was on the game's own title.

### Infrastructure tests (TDD, real SQLite on `pcsx2-win`)

`LibrarySummaryTests`:

- `GuideTitles` lists each game's own guides in title-then-ID order;
- `GuideTitles` is empty for a game with no guides;
- titles do not leak between games;
- a guide removed through `GuideRemover` drops out of `GuideTitles`;
- the existing deleted-`content/<id>` test also asserts `GuideTitles`.

### Installed smoke

The `production-shell-ui` job is the gate.

**`seed-search` / `library-search` (new).** The seed adds:

- `Pokémon Crystal`;
- `Ōkami HD`;
- `ドラゴンクエストXI`;
- `Zeta Archive Game`, with one unread guide, `Complete Walkthrough`.

In light and dark, the smoke checks:

1. `POKEMON` leaves one row, and Clear search is visible.
2. `okami` leaves one row.
3. `ドラゴン` leaves one row.
4. `walkthrough` leaves the Zeta row, whose HelpText ends with `Guide:
   Complete Walkthrough`.
5. `zzzz` shows `LibraryNoResultsState` with the query echoed, and hides
   `GameList`.
6. Clear search brings back every row, leaves the box empty and focused, and
   hides the Clear button.
7. With `walkthrough` typed, opening Zeta shows its guide row as
   `Not started`.
8. Back on the Library, the box still holds `walkthrough` and only the Zeta
   row is listed.
9. Screenshots of the results and no-results views.

**Offline.** The mode reuses the `catalog` no-provider-traffic check.

**Loading and empty views.**

- The existing empty-library smoke also asserts the search box is disabled.
- Every mode that waits for Library ready also asserts that
  `LibraryLoadingState` is collapsed. The spinner itself is too brief to
  catch reliably, so the smoke asserts only that it does not linger.

### Traceability

| Requirement | Evidence |
| --- | --- |
| T05.2 title search with loading, empty and no-results views | `LibrarySearchTests`; `library-search` |
| TR05.1 case-insensitive, never opens guide content | `LibrarySearchTests`; deleted-content summary test; `library-search` offline check |
| TR05.2 `Not started` for unread guides | `library-search` step 7; T05.1 `CatalogPresentationTests` |

## Out of scope

- `Ctrl+F` (T16.1).
- Restoring the query on Back navigation, and selection after refresh
  (T05.3).
- Search inside a guide.
- Fuzzy or multi-word matching.
- Search on the Game page.

## Documentation

- This spec gets a verification record.
- `implementation-plan.md` gets a T05.2 paragraph after the T05.1 paragraph.
- `e2e-testing.md` gets a Library search row.
- `progress.md`:
  - the T05.1 row shows merged (PR #23);
  - a T05.2 row is added.

## PR outcome

The PR names T05.2, its merged prerequisite T05.1 (PR #23), and the outcome.
Its body shows the results and no-results views in light and dark.

## T05.2 verification record

- **Unit tests.** On `pcsx2-win`, Infrastructure 396/396 and Core 258/258
  passed. The new tests are:
  - `LibrarySearchTests`: mixed case; accents both ways; width both ways;
    Japanese and Cyrillic titles; padded, blank and null queries;
    non-matches; game-title and guide-only matches, with the first matching
    guide title named; input order; and an empty input;
  - `CatalogPresentationTests`: `Guide: {title}` ends both the visible facts
    and the spoken text, and is absent when the game's own title matched;
  - `LibrarySummaryTests`: each game's own guide titles in title-then-ID
    order, none for a game without guides, a removed guide dropping out,
    and listing after a guide's content directory is deleted.
- **Installed.** CI run [36726600007](https://github.com/ilya-slalom/desktop-guides/actions/runs/36726600007)
  passed all nine jobs, including `production-shell-ui`:
  - `library-search`, light and dark: phases `search-case-accent`,
    `search-non-ascii`, `search-guide-title`, `search-no-results`,
    `search-clear`, `search-not-started`, `search-kept-after-back` and
    `search-no-provider-traffic` passed, with zero non-loopback
    connections;
  - `empty`: the search box was disabled and Clear search absent;
  - `catalog`, light and dark: the keyboard checks tabbed through the
    search box to the first game row;
  - every mode that waits for `Library ready.` found the loading view gone.
  - after the final review, CI run [36730472882](https://github.com/ilya-slalom/desktop-guides/actions/runs/36730472882)
    passed `library-search` again, in light and dark. This time the smoke
    focuses Clear search and presses Space, and focus lands in the search
    box. The earlier check invoked Clear while focus was already in the box,
    so it could not fail. `native-arm64-ui` failed on the P0 `pdf-short`
    fixture ("Probe status is unavailable"). The same failure hit the T15.3
    and T06.3 branches, and this branch does not touch P0 code.
- **Rulings.** Rulings 1–11 in the
  [plan](t05-2-library-search-plan.md#rulings-against-the-spec), plus two
  made during implementation:
  - the plan's seed loop variable `game` clashed with a top-level local in
    `Program.cs`, so it was renamed `searchGame`;
  - CI run [36722381641](https://github.com/ilya-slalom/desktop-guides/actions/runs/36722381641)
    failed because the AutoSuggestBox's own UIA element cannot take focus.
    The smoke now focuses its inner edit box, which is where Tab lands.
- **Evidence.**
  - [Results, light](evidence/t05-2-library-search/results-light.png)
  - [Results, dark](evidence/t05-2-library-search/results-dark.png)
  - [No results, light](evidence/t05-2-library-search/no-results-light.png)
  - [No results, dark](evidence/t05-2-library-search/no-results-dark.png)
