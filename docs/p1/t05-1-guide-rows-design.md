# T05.1 Library and Game rows design

Status: implemented on `feat/p1-t05-1-guide-rows`; verified by CI run 36705946660. Prerequisites T03.2
(PR #4), T04.4 (PR #14), T05.4 (PR #16) and T11.1 (PR #6) are merged.

## Intent

The Library lists games most recently used first, and the Game page lists
guides the same way. Each row shows the game's
artwork, title and a short facts line: platform, source, and guide count.
Each guide row has a format tile and a facts line:
format, reading state, and when the guide was last opened. An unread guide
says `Not started`, never `0%`.

Listing reads only SQLite metadata. It never opens guide files and never
calls a provider. Both lists stay virtualized: a 500-game Library and a
97-guide game realize only a bounded set of rows.

Traces: the T05.1 row of [implementation-plan.md](implementation-plan.md)
(TR04.3, TR05.2, TR05.3), [work-breakdown.md](../work-breakdown.md) S05 and
T05.1, and [p1-technical-design.md](../p1-technical-design.md) (T05.1,
526–531).

Decisions made during brainstorming:

- **Game order.** A game's last activity is the latest of its creation time,
  any of its guides' import times, and any of its guides' last-opened times.
  Games sort by last activity (newest first), then title, then Game ID.
- **Guide order.** A guide's last activity is the later of its import time
  and its last-opened time. Guides sort by last activity (newest first),
  then title, then Guide ID. Until T12.3 records opens, this is newest
  import first. The T15.3 neighbour selection reads the displayed order, and
  Back and Resume match by Guide ID, so neither depends on title order. A
  guide that was just read moves to the top when the user returns to the
  Game page; T05.3 owns selection and focus after list changes.
- **Row facts.** Compact: game rows show platform · source · guide count;
  guide rows show format · reading state · last opened. Times are absolute
  and local, with only the time shown for today.
- **Data access.** One summary query per list (approach A). No per-row query
  and no denormalized activity column.

Reading state has no writer yet. T12.3 writes the estimate and
`LastOpenedUtcMs`, and T13.2 writes `CompletedUtcMs`. Until they land,
every real guide shows `Not started`; the tests seed reading states
directly.

## Components

### Core types (Core/Library)

```csharp
public sealed record LibraryGameSummary(
    Game Game, int GuideCount, DateTimeOffset LastActivityUtc);

public sealed record GuideSummary(Guide Guide, ReadingState? State);
```

`ILibraryRepository` gains:

```csharp
Task<IReadOnlyList<LibraryGameSummary>> ListGameSummariesAsync(
    CancellationToken token = default);
Task<IReadOnlyList<GuideSummary>> ListGuideSummariesAsync(
    Guid gameId, CancellationToken token = default);
```

`ListGamesAsync` and `ListGuidesAsync` stay; the import, removal and seed
paths still use them.

### Presentation (Core/Library/CatalogPresentation.cs)

A static helper, replacing `LibraryGamePresentation`. Its clock and time zone
are parameters, so tests pin them. Formatting uses the current culture.

- **Game facts:** the trimmed platform (omitted when blank), then `IGDB` when
  the game has a provider link or `Manual` otherwise, then `No guides`,
  `1 guide` or `N guides`.
- **Guide facts:** the format label from `ImportPresentation`
  (`Text (TXT)`, `Web page (HTML)`, `PDF`), then the reading state, then
  `Opened {when}` when `LastOpenedUtc` is set.
- **Reading state**, first match wins:
  1. `CompletedUtc` set → `Completed`;
  2. an estimate → `~N%`, where N is the estimate × 100 rounded half away
     from zero, so 0 → `~0%` and 1 → `~100%`;
  3. `LastOpenedUtc` set → `In progress`;
  4. otherwise → `Not started`. A missing reading-state row counts as unread.

  An estimate of 1 without `CompletedUtc` reads `~100%`, never `Completed`.
- **When:** convert to the given zone. Same local date as now → the
  culture's short time (`14:05`); the previous local date → `yesterday`;
  otherwise the culture's `d MMM yyyy`-style date (`28 Sep 2026`). A future
  time (clock skew) is shown as a date.
- **Accessible text:** the same facts joined with `, `, with `~N%` read as
  `about N percent` and `Opened 14:05` as `opened today at 14:05`.
- **Accessible name:** the title only, as today.

### Repository queries (Infrastructure/Storage/SqliteLibraryRepository.cs)

`ListGameSummariesAsync` is one statement:

```sql
SELECT g.<game columns>,
       COUNT(gu.Id) AS GuideCount,
       MAX(g.CreatedUtcMs,
           COALESCE(MAX(gu.ImportedUtcMs), 0),
           COALESCE(MAX(rs.LastOpenedUtcMs), 0)) AS LastActivityUtcMs
FROM Games g
LEFT JOIN Guides gu ON gu.GameId = g.Id
LEFT JOIN ReadingStates rs ON rs.GuideId = gu.Id
GROUP BY g.Id
ORDER BY LastActivityUtcMs DESC, g.Title, g.Id
```

It reuses the existing game-row mapping. `ListGuideSummariesAsync` is a
`LEFT JOIN` of `Guides` to `ReadingStates` for one game, reusing the
existing guide and reading-state mappings:

```sql
SELECT gu.<guide columns>, rs.<reading-state columns>
FROM Guides gu
LEFT JOIN ReadingStates rs ON rs.GuideId = gu.Id
WHERE gu.GameId = $gameId
ORDER BY MAX(gu.ImportedUtcMs, COALESCE(rs.LastOpenedUtcMs, 0)) DESC,
         gu.Title, gu.Id
```

Both run through the existing `ReadAsync` path.

### Rows (Production)

- **`DesktopGuidesCatalogRowTemplate`** in `Styles/Catalog.xaml`, bound to
  a new `CatalogRowItem : ArtworkItem`: the 45×60 tile, then the title and a
  `MetadataControl` facts line using `DesktopGuidesFactsStyle`. Title and
  facts trim with an ellipsis; row height still follows text scaling.
  Add game keeps `DesktopGuidesArtworkRowTemplate`.
  - This puts one Toolkit control in each row, which T05.4 avoided. T05.1
    asks for `MetadataControl` explicitly, and each recycled container
    still holds one fixed element tree. The realized-row checks below
    measure it.
- **Tile content:** game rows keep the artwork and placeholder. Guide rows
  show a format glyph instead of artwork: document for TXT, globe for HTML,
  PDF for PDF. The glyph uses `AccessibilityView="Raw"`.
- **`LibraryGameItem`** wraps a `LibraryGameSummary`, keeps `Game` and
  `ArtworkRelativePath`, and drops its platform `Summary` line, since the
  platform is now a fact.
- **`GuideRowItem`** wraps a `GuideSummary` and exposes `Guide`.
- **Accessibility:** `ArtworkListLoader`'s naming hook sets each container's
  `AutomationProperties.Name` to the title (unchanged) and its `HelpText` to
  the accessible facts. `MetadataControl` has no automation peer, so the
  facts aren't read twice.

### Shell (ShellWindow)

- The Library render calls `ListGameSummariesAsync`; the Game render calls
  `ListGuideSummariesAsync`.
- `GuideList` drops `DisplayMemberPath`, uses the catalog row template, and
  its `ItemsSource` becomes `GuideRowItem`s. It attaches the naming hook.
- The places that read `GuideList.SelectedItem is Guide`, `row.Content is
  Guide` or `GuideList.Items[i] as Guide` go through one `SelectedGuide`
  helper and a `GuideAt(object?)` helper. Selection, focus, Open, Remove,
  Resume and Back keep matching by `Guide.Id`, so their behaviour is
  unchanged.
- Empty states, status messages, the Resume button and the Open and Remove
  buttons are unchanged.

## Testing

### Core tests (TDD)

`CatalogPresentationTests`:

- Reading-state precedence, including a missing row, opened without an
  estimate, `~0%`, `~100%` without completion, and completion with an
  estimate.
- Game facts: blank and padded platforms, linked and manual games, 0, 1 and
  several guides.
- Dates with a fixed `TimeProvider` in a non-UTC zone: today, yesterday,
  older, both sides of local midnight, and a future time; en-US and one
  other culture.
- Accessible text has no `~` or `·` and says `about N percent`.

### Infrastructure tests (TDD, real SQLite on `pcsx2-win`)

`LibrarySummaryTests`:

- Game order by each activity source: a newer game, a newer import into an
  older game, and a newer open of an older game's guide each move that game
  to the top.
- A game with no guides and a guide with no reading-state row.
- Equal activity sorts by title, then ID.
- Guide counts, and removing a guide (through `GuideRemover`) updates both
  the count and the order.
- Guide summaries join each guide's own state, exclude other games' guides,
  and return a null state when no row exists.
- Guide order: a newer import sorts first, a newer open beats a newer
  import, and equal activity sorts by title, then ID.
- Both lists still return after the guide's `content/<id>` directory is
  deleted: listing never touches guide files.

### Installed smoke

The `production-shell-ui` job is the gate.

- **`seed-catalog`** gives all 500 games the same `CreatedUtcMs`, so the
  existing title-order and realized-row assertions stand unchanged.
- **`seed-facts` / `catalog-facts`** (new): the seed adds a game with four
  guides and writes their reading states directly:
  - not started;
  - opened today without an estimate;
  - ~45% opened yesterday;
  - completed with an estimate.

  The open times are chosen so that activity order differs from title
  order. It also adds an older game whose guide was opened most recently.
  The smoke checks:
  - that older game is the first Library row;
  - the guide rows appear in activity order;
  - each game row's Name and HelpText;
  - each guide row's Name and HelpText;
  - screenshots of the Library and the Game page, in light and dark.
- **`long-list`** gains the realized-row check for `GuideList` (97 guides),
  using the same `ListItem` count the `catalog` smoke uses for `GameList`.
  The smoke needs only that "ZZZ Focus Target Guide" is out of view before
  scrolling. Its seed gives all 97 guides one shared import time, newer than
  the base seed's guides, so the title tie-break puts it last among the
  long-list guides, still far below the fold. The seed must keep a shared
  timestamp. Once T12.3 records opens, the Resume step will move it to the
  top on return; that change owns the smoke update.
- The plan audits every other smoke for assumptions about game order,
  because a game's creation time now affects its position.

### Traceability

| Requirement | Evidence |
| --- | --- |
| T05.1 sorted lists and rows with facts | `LibrarySummaryTests` game and guide order; `catalog-facts` |
| TR05.2 `Not started` for unread guides | `CatalogPresentationTests`; `catalog-facts` |
| TR05.3 virtualized, missing artwork, long and localized text, no provider request | `catalog` (unchanged checks) and `long-list` realized rows |
| TR04.3 cached display offline | `catalog` no-provider-traffic phase |

## Out of scope

- Search and its states (T05.2).
- Selection and focus after rename, import or removal (T05.3).
- Writing reading state (T12.3) and completion (T13.1, T13.2).
- A game grid view, and relative times.
- New packages: `MetadataControl` is already locked.

## Documentation

- This spec gets a verification record.
- `implementation-plan.md` gets a T05.1 paragraph in M2.
- `e2e-testing.md` gets a Catalog facts row.
- `progress.md` gets a T05.1 row.

## PR outcome

The PR names T05.1, its merged prerequisites, and the outcome. Its body shows
the Library and Game page screenshots in light and dark.

## T05.1 verification record

- **Unit tests.** On `pcsx2-win`, Infrastructure 395/395 and Core 235/235
  passed. The new tests are:
  - `CatalogPresentationTests`: game facts for blank and padded platforms,
    linked and manual games and each guide count; reading-state precedence
    (missing row, opened only, an estimate without an open time, `~0%`,
    `~100%` without completion, midpoint rounding, completion with an
    estimate); today, yesterday, older, both sides of local midnight and a
    future time in UTC+9, in en-US and one other culture; and the spoken
    forms, which never contain `~` or `·`;
  - `LibrarySummaryTests`: game order by creation, import and open time;
    ties by title then ID; guide counts, including after `GuideRemover`
    removes a guide; guide order by import and open time; each guide's own
    reading state, or none; and listing after a guide's content directory
    is deleted.
- **Installed.** CI run [36705946660](https://github.com/ilya-slalom/desktop-guides/actions/runs/36705946660)
  passed `production-shell-ui`:
  - `catalog-facts`, light and dark: the Library listed Zeta Archive Game,
    Facts Test Game and Empty Test Game in activity order, and the Game
    page listed the four guides in activity order. Each row's Name was its
    title and its HelpText its spoken facts;
  - `catalog`, light and dark: the existing checks passed unchanged, and
    the long-title row and three recycled rows at the end had their own
    HelpText;
  - `long-list`: `GuideList` realized 8 of 99 rows.
- **Rulings.** Rulings 1–13 in the [plan](t05-1-guide-rows-plan.md#rulings-against-the-spec),
  plus one made during implementation: the plan's inline PowerShell
  parser check failed under SSH quoting, so the same `ParseFile` loop ran
  from a temporary script file, and both scripts parsed.
- **Evidence.**
  - [Library, light](evidence/t05-1-guide-rows/library-light.png)
  - [Library, dark](evidence/t05-1-guide-rows/library-dark.png)
  - [Game page, light](evidence/t05-1-guide-rows/game-light.png)
  - [Game page, dark](evidence/t05-1-guide-rows/game-dark.png)
