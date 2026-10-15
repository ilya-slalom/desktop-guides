# P2 technical design: reader depth and local ownership

Status: design approved in conversation, 15 October 2026. It covers S18
(search and contents), S19 (bookmarks and TXT reflow), and S21's first
task (the format survey).

Related documents:
- the [high-level design](initial-design.md) defines R1–R9;
- the [work breakdown](work-breakdown.md) owns the story, task and TR IDs;
- the [P2 implementation plan](p2/implementation-plan.md) orders the work;
- P1 is complete, with v1.0.1 published (see the [P1 results](p1/results.md)).

## 1. Scope and decisions

- **In scope:**
  - S18: in-guide search and a table of contents, for TXT, HTML and PDF;
  - S19: bookmarks for all three formats, and optional reflow for TXT;
  - S21's T21.1: a survey of real guides that ranks candidate formats.
- **Out of scope:**
  - S21's formats themselves. Each gets its own design under T21.2 after
    the survey.
  - P3 (S22–S24), and the final milestone.
- **Decided 15 October 2026:**
  1. **The shape of P2:** S18 and S19 are designed in full now. S21 starts
     with the survey, and each format follows with its own design.
  2. **PDF search is included.** P1 extracts PDF page text through PdfPig
     for its page-text view, so the original "TXT/HTML only" limit no
     longer applies. Image-only pages are reported as unsearchable.
  3. **Search and contents use the open reader session (approach A).**
     Nothing new is stored and there is no index, so guides imported
     before P2 work unchanged. Bookmarks are the only new stored data.
- **P1 rules that still hold:**
  - guides read offline;
  - originals stay untouched;
  - imported HTML is untrusted;
  - per-guide locators stay stable;
  - completion is explicit;
  - adapters never write SQLite directly.

## 2. Reader contract extensions

The P1 contract (`IReaderAdapter`, `ReaderCapabilities`, `ReaderLocation`,
`RestoreOutcome`) is extended, not replaced. These capability flags are
added or used: `Find` (already present), `Outline` and `Reflow`.

```csharp
interface IFindableReader
{
    Task<FindResult> FindAsync(FindQuery query, FindDirection direction,
                               CancellationToken token);
    void ClearFind();
}

record FindQuery(string Text);   // case-insensitive, whitespace-normalized
enum FindDirection { Next, Previous }
record FindResult(FindOutcome Outcome, int? Index, int? Total,
                  bool TotalIsPartial, int UnsearchablePages);
enum FindOutcome { Found, NotFound, Cancelled, Failed }

interface IOutlineReader
{
    Task<Outline> GetOutlineAsync(CancellationToken token);
}

record Outline(OutlineSource Source, IReadOnlyList<OutlineEntry> Entries);
enum OutlineSource { Document, Detected, None }
record OutlineEntry(string Title, int Level, ReaderLocation Location,
                    IReadOnlyList<OutlineEntry> Children);
```

- **The shell composes commands from the flags.** A guide without a
  capability shows no control for it, and its shortcut announces that the
  feature isn't available for this guide (TR18.1).
- **Locations:** outline entries and bookmarks are ordinary
  `ReaderLocation` envelopes, so going to one reuses each adapter's
  `RestoreLocationAsync` and its outcomes.

## 3. S18: search (T18.1)

- **Matching:** case-insensitive (ordinal, culture-invariant), with runs of
  whitespace in the guide and in the query treated as one space, so a
  match can cross a line break. There's no whole-word or regex matching.
- **TXT:** searches the in-memory normalized text, the text locator
  offsets refer to. The highlight is drawn in the line view.
- **HTML:**
  - **Searching:** an injected script walks the permitted text nodes, as
    `HtmlPositionScripts` already does.
  - **Highlighting:** with the CSS Custom Highlight API, so the DOM is
    never changed and locators stay valid.
  - **Results:** they return through the existing session messaging.
- **PDF:**
  - **The scan:** pages are scanned in reading order on a background task
    through `PdfPageTextSource`, with cancellation. While the scan runs,
    the total is shown as partial ("12+").
  - **Image-only pages** are counted, and reported in the find bar.
  - **A match** jumps to its page, with highlight rectangles drawn over
    the rendered page from PdfPig's word boxes.
- **The UI:**
  - **Opening and the bar:** in the reader, Ctrl+F opens a find bar under
    the reader toolbar. It has a text box, Previous, Next, a count ("3 of
    17", "No matches"), "Back to my place" and Close.
  - **Keys:** Enter or F3 for the next match; Shift+Enter or Shift+F3 for
    the previous one; Escape closes the bar.
  - **Screen readers:** each new match is announced.
  - **The library:** Ctrl+F there stays the library search.
- **The saved reading place** (the S18 acceptance):
  - **While the bar is open:** jumps to matches never save a reading place.
  - **"Back to my place":** returns to where the bar was opened.
  - **After closing:** the view stays at the match, but the saved place
    moves only once the user scrolls or turns a page.
- **Failure:**
  - **A script error, or a PDF page that can't be read,** is reported in
    the bar ("Couldn't search page 14"), and the scan continues.
  - **Search never blocks reading.**

## 4. S18: table of contents (T18.2)

- **HTML (`Document`):**
  - a script collects `h1` to `h6`, nested by level;
  - if the page has its own in-page contents list (a run of links to
    `#id` anchors), that list is used instead;
  - titles are trimmed to 120 characters, and entries locate by element
    ID.
- **PDF (`Document`):** the file's own outline, through PdfPig, with
  entries pointing to their pages. Without an outline there is no table
  of contents, and nothing is detected.
- **TXT (`Detected`):** a Core-side `TextSectionDetector`.
  - **Search codes:** codes listed in the guide's own contents block, such
    as `[WLK01]`, map to their later occurrence. This is the strongest
    signal.
  - **Underlined and numbered headings:** headings underlined with `====`
    or `----`, and numbered headings (`1.0`, `1.1`, `2.`), nested by
    depth.
  - **The threshold:** it accepts three or more sections, spread through
    the text. Otherwise it returns `None`.
  - **The time budget** is 2 s.
- **The UI:**
  - **Opening it:** the Contents tab of the left pane, from the reader
    toolbar or Ctrl+Shift+C, shows a native `TreeView`. The pane has a
    fixed width and can't be resized, so there's no `GridSplitter`.
  - **Following an entry:** Enter goes to it and moves focus to the guide.
    This is a deliberate move, so the reading place is saved.
  - **When it opens,** the entry containing the current place is
    selected.
  - **When nothing is found:** with no entries, the pane says "No sections
    found in this guide", with links to Search and Bookmarks. That's the
    manual fallback in T18.2.
- **Failure:** the outline builds in the background after the guide
  opens. A failure or timeout gives "No sections found", and never delays
  opening or scrolling.

## 5. S19: bookmarks (T19.1)

- **Storage:**
  - **The table:** a `Bookmark` table: `id` (GUID), `guideId` (a foreign
    key, deleted with its guide), `locatorJson` (the existing locator
    envelope, at most 4 KiB), `label` (1 to 80 characters), and
    `createdUtcMs` and `updatedUtcMs`.
  - **Limit:** at most 500 bookmarks per guide.
  - **The migration:** one forward migration bumps the schema version.
  - **No effect on other state:** bookmarks never touch `ReadingState` or
    completion.
- **Backup (S20):**
  - **What changes:** export includes the bookmark rows, and the archive
    verifier checks their foreign keys and locator sizes.
  - **Older and newer backups:** a backup from before P2 restores with no
    bookmarks. An older app refuses a newer backup under the existing
    schema-version rule.
  - **The tests:** the existing export, restore and recovery tests gain
    bookmark rows.
- **The UI:**
  - **Adding a bookmark:** Ctrl+D, or "Add bookmark", saves the current
    place. The default label is the nearest outline entry, or the first
    words of the visible line ("Page 42" for PDF). The label can be
    edited inline.
  - **The Bookmarks tab** (Ctrl+Shift+B) lists bookmarks in reading order.
  - **Actions:** Enter goes to a bookmark, and saves the place; F2 renames
    it; Delete removes it, with "Undo" in the status line for 10 s.
  - **Screen readers:** items read as "label, location".
- **Restoring a bookmark** uses the locator's existing outcomes:
  - **`Exact` and `Context`** go straight there;
  - **`Approximate`** shows "This bookmark's place may have shifted.";
  - **`Unavailable`** shows "This bookmark's place couldn't be found.", and
    the bookmark is kept.
- **Failure:**
  - **A failed write** shows a status message and changes nothing, with
    the existing write lock and timeouts.
  - **A bookmark whose locator doesn't parse** is listed as "Damaged
    bookmark", with only Delete offered.

## 6. S19: TXT reflow (T19.2)

- **What it is:** a display mode only. The imported bytes, the normalized
  text and the offset locator never change (TR19.1).
- **`TextReflowPlanner`** (Core) splits the normalized text into prose
  and preserved blocks. Each block records the range of normalized text
  it covers, so every position maps exactly to its source offset.
  - **Prose:** a run of lines separated by blank lines, where most lines:
    - are mostly letters and spaces;
    - are near the guide's typical wrap width;
    - have no runs of two or more interior spaces.

    Prose lines are joined with single spaces. Line-end hyphens are kept.
  - **Preserved:** anything else, including:
    - lines dense in `| - + = # / \ _` or box-drawing characters;
    - aligned columns;
    - indented blocks;
    - runs of short lines;
    - headings from the section detector.

    Doubtful blocks are preserved.
  - **The time budget** is 2 s.
- **Rendering:** prose wraps to the window in the reading font. Preserved
  blocks keep their original monospace layout, with horizontal scrolling.
  Text size applies to both.
- **The UI:**
  - **The choice:** a Toolkit `Segmented` control, `Original` or `Reflow`,
    shown only for TXT.
  - **Per guide:** saved in the guide's preferences row (a new column);
    the default is `Original`.
  - **Switching** restores the first visible character's source offset in
    the other view.
  - **Search, contents and bookmarks** work in both views.
- **Failure:** if planning fails or finds no prose, the status is "This
  guide has no prose to reflow", and the view stays on Original.

## 7. S21: the format survey (T21.1)

- **The sample:** representative guides from major public game-guide
  sites (GameFAQs, IGN, Neoseeker, StrategyWiki and large fan sites),
  across genres and eras.
- **What it records for each candidate format** (image maps, multi-page
  HTML, CBZ, EPUB, Markdown, and others found):
  - frequency;
  - whether it can be imported and read offline;
  - import-security risk;
  - locator needs;
  - the likely reader.
- **The output:** a ranking in `docs/p2/format-survey.md`, with its
  uncertainty stated. No sample is committed.
- **What follows:** T21.2 then designs and adds one format at a time,
  each extending the backup tests and meeting TR21.1.

## 8. Shared UI, accessibility and testing

- **The left pane** has two tabs, Contents and Bookmarks, toggled from the
  reader toolbar. Its width is fixed.
- **Keyboard:** Ctrl+F, F3 and Shift+F3, Ctrl+Shift+C, Ctrl+D and
  Ctrl+Shift+B are added to T16.1's keyboard map. The map's tests guard
  against conflicts.
- **Accessibility:** every new control has a name, focus order and
  announcements, and they are asserted as in T16.1. The full audit stays
  in the final milestone.
- **Testing, TDD throughout:**
  - **Core unit tests,** with authored fixtures only:
    - matching;
    - `TextSectionDetector`, including the cases where it must find no
      contents;
    - `TextReflowPlanner`: diagrams, maps, tables and lists preserved,
      and the offset round trip;
    - outline mapping.
  - **Infrastructure tests:**
    - the bookmark repository, its limits and the migration;
    - backup export, verification and restore with bookmarks;
    - the PDF outline and page scanning.
  - **Installed UI tests:** smoke modes `find-*`, `contents-*`,
    `bookmark-*` and `reflow-*`, in a new `navigation` scenario group and
    CI shard, on both lanes. They assert only what the app controls:
    - match counts and positions;
    - the saved place unchanged by search;
    - focus and statuses;
    - offsets preserved across a view switch.

## 9. Delivery

| Milestone | Tasks | Notes |
| --- | --- | --- |
| M7 | T18.1 search | |
| M8 | T18.2 contents | The section detector is reused by reflow. |
| M9 | T19.1 bookmarks | |
| M10 | T19.2 reflow | |
| any time | T21.1 survey | Research; can run alongside. |
| after the survey | T21.2 formats | One design and PR per format. |

- **Per task:** each task gets a design note and its own PR, as in P1. The
  [implementation plan](p2/implementation-plan.md) holds the dependency
  table and exits.
- **Optional M7 follow-ups,** reader polish from open issues: #45 (Ctrl+wheel
  PDF zoom) and #50 (Dark style changes image and SVG colours).

## 10. Traceability

P2's features are the initial design's
[later candidates](initial-design.md#later-candidates-in-priority-order):
1 (navigation) and 2 (more material). R4 (read offline), R5 (resume
accurately) and R9 (Windows usability) apply to them as constraints.

| Requirement | Design section | Verified by |
| --- | --- | --- |
| Later candidate 1: search and contents (S18) | 3, 4 | Core matching and detector tests; `find-*` and `contents-*` modes |
| R5: search doesn't replace the saved place | 3 | `find-*` asserts the stored locator is unchanged |
| TR18.1 | 2, 4 | Controls present only with capability flags; detection failure doesn't block scrolling |
| S19 bookmarks | 5 | Repository, migration and backup tests; `bookmark-*` modes |
| S19 reflow and TR19.1 | 6 | Planner offset round-trip tests; `reflow-*` modes; source bytes unchanged |
| S21 and TR21.1 | 7 | The survey document, then per-format designs |
