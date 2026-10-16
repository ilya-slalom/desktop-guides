# T19.2 TXT reflow: design

Status: approved in conversation, 16 October 2026. PR a (the planner and
the view preference) is implemented on `feat/p2-t19-2-reflow`; PR b (the
reflow view) follows.

## Intent

A TXT guide can be read in a Reflow view. Its hard-wrapped prose
paragraphs wrap to the window in the reading font. Everything that isn't
clearly prose (maps, ASCII art, tables, lists and headings) keeps its
monospace layout and scrolls sideways as a block.

The view is a display mode only. The imported bytes, the normalized text
and every stored locator stay as they are (TR19.1). Search, contents and
bookmarks work in both views, and switching keeps the reader's place. A
guide with no prose says so and stays on Original.

This is the M10 exit in the
[P2 plan](implementation-plan.md#m10-txt-reflow), from
[section 6](../p2-technical-design.md#6-s19-txt-reflow-t192) of the P2
design.

## Starting point

- **What exists:**
  - **The TXT reader:** a virtualized list with one row per normalized
    line, in monospace. Its top-line tracking, capture, restore, paging
    and search highlights all work in line indices.
  - **The session contract is offset-based.** Saved places, contents
    entries and bookmarks all hold a `TextPosition.CharacterOffset`.
  - **T18.2's `TextSectionDetector`** gives heading offsets, under a 2 s
    budget enforced with `CancelAfter`.
  - **`ReaderPreferences`** holds each guide's `TextScale`.
  - **`BoundedChoice`** is the app's wrapper around the Toolkit's
    `Segmented` control.
- **What's missing:**
  - prose detection;
  - a view that wraps;
  - an offset-based capture and scroll for TXT;
  - a per-guide view preference.

## The design

### `TextReflowPlanner` (Core)

- **Input:** the `TextGuideDocument` (normalized text and line starts) and
  the section detector's headings. It runs under the same 2 s budget
  pattern (`CancelAfter`), and never returns a partial plan.
- **Output:** a `ReflowPlan`, which is a list of blocks, or `None`.
  - **The blocks split the whole text into consecutive pieces.** Each
    block covers the source range from its first line up to the next
    block's first line, so blank lines belong to the block before them.
    Every offset maps to exactly one block.
  - **Each block** has a kind (`Prose` or `Preserved`), its source range,
    and its display text.
  - **A prose block also keeps a segment map** from display characters to
    source offsets, so highlights and locators convert exactly between
    the two.
- **Classification** works per paragraph, a run of non-blank lines:
  - **Prose:**
    - most lines are mostly letters and spaces;
    - they have no interior runs of two or more spaces;
    - they aren't indented;
    - they sit near the guide's typical wrap width, though the last line
      may be short;
    - the paragraph isn't one of the detector's headings.
  - **Preserved:** anything else, including:
    - symbol-dense lines (`| - + = # / \ _` and box drawing);
    - aligned columns;
    - indented blocks;
    - runs of short lines, such as lists;
    - headings;
    - anything doubtful.
- **Joining prose:** trailing spaces are dropped and lines are joined with
  one space. A line-end hyphen is kept.
- **`None`:** planning found no prose, failed, or ran out of time. The app
  then says "This guide has no prose to reflow."
- **TR19.1:** planning only reads. The document's text, its hash and every
  locator stay unchanged.

### Storage (Infrastructure)

- **Schema 6:** one forward migration adds `ReaderPreferences.TextView`
  with `CHECK (TextView IS NULL OR TextView IN ('Original', 'Reflow'))`.
  `NULL` reads as Original.
- **The model:** `ReaderPreferences` gains a `TextView` value.
- **The repository:** a separate `SaveTextViewAsync(guideId, view)`, so a
  text-size save and a view save never overwrite each other.
- **Backup:** the snapshot already carries the column. Restore's existing
  validation refuses a value outside the `CHECK`. A version 5 database or
  backup migrates to Original.

### The views (Production)

- **The session:** `TextReaderSession` holds today's line view and a
  `TextReflowView`, which it creates the first time it's needed. A
  `ViewChanged` event tells the shell to swap the reading surface.
- **`TextReflowView`** is a virtualized list with one item per block:
  - **prose** wraps to the window in the reading font;
  - **preserved** blocks keep their monospace layout, in their own
    horizontal scroll.

  Text size applies to both. Page Up/Down and Ctrl+Home/End scroll by the
  viewport.
- **Places are offsets, kept to the block** (the user's choice, 16 October
  2026):
  - the reflow view's first visible place is its first visible block's
    start offset;
  - going to an offset shows the block that holds it.
- **`TextLocator`** gains an offset-based `Capture`, beside the line-based
  one.
- **Both views share one contract:**
  - **places and bookmarks:** saving and restoring the place, and
    bookmarks, use offsets in either view;
  - **search** steps from the first visible offset, and highlights a match
    inside its block through the segment map;
  - **contents** entries go to their block.

### The choice and switching (Production)

- **The control:** an "Original | Reflow" `BoundedChoice` in the reader
  toolbar, beside text size (the user's choice). It's shown only for TXT,
  through a new `ReaderCapabilities.Reflow` flag that only the TXT session
  declares. There's no keyboard shortcut; Tab and the arrow keys reach
  it, like the completion choice.
- **Switching:**
  - take the first visible offset;
  - plan, if there's no plan yet;
  - open the other view at that offset;
  - save the choice for the guide.

  "Reflow view." or "Original view." is announced without the status bar.
- **Opening a guide saved as Reflow** plans before showing the guide. If
  planning gives `None`, the guide opens in Original.
- **A guide with no prose:** "This guide has no prose to reflow." The
  choice returns to Original, and nothing is saved.

### Tests

- **Core:**
  - `TextReflowPlanner`, over a new authored fixture, `p2/txt-reflow.txt`,
    which holds hard-wrapped prose, ASCII art, a map, a table, a list and
    headings;
  - an offset round trip for every block (display index to source offset
    and back);
  - the guide with no prose, and the budget;
  - text and hash unchanged;
  - the offset-based `TextLocator.Capture`.
- **Infrastructure:**
  - the migration from a populated version 5;
  - the column's round trip and its limits;
  - backup and restore with a `TextView` value, and a version 5 backup
    migrating.
- **Installed** (the `navigation` group, both lanes; the seed adds a
  "Reflow Guide" from `p2/txt-reflow.txt`):
  - `reflow-switch`: Original to Reflow shows the block holding the top
    line; back to Original returns to that block's first line;
  - `reflow-restart`: a new launch reopens in Reflow;
  - `reflow-none`: Numbered Lines Guide gives the message and stays on
    Original;
  - `reflow-find`: a search in Reflow highlights its match and moves to
    it;
  - `reflow-bookmark`: a bookmark added in Reflow is followed exactly in
    Original.

## Delivery

| PR | Content | Installed modes |
| --- | --- | --- |
| a | `TextReflowPlanner` and its fixture; the offset-based `TextLocator.Capture`; the `TextView` column (schema 6), with backup coverage | None (no UI) |
| b | `TextReflowView`, the toolbar choice, switching, and search, contents and bookmarks in both views | `reflow-switch`, `reflow-restart`, `reflow-none`, `reflow-find`, `reflow-bookmark` |

## Deviations from the P2 design

1. **Switching keeps the place to the block,** not to the character (the
   user's choice). Search, contents and bookmarks still land on their
   exact block.
2. **A `ReaderCapabilities.Reflow` flag** gates the choice. TXT is the only
   format with it.
3. **The view is saved in a `TextView` column**, through its own save
   method, so it never races the text-size save.
4. **Prose tolerates the way real guides are written** (final review, 16
   October 2026):
   - **Two spaces after a sentence** don't count as a column gap. Three or
     more spaces, or two anywhere else, still do.
   - **Symbols such as HP/MP, 50%, +10 or [WLK3]** may make up a tenth of
     a line. Layout characters (`|`, `\`, `_`, box drawing, and runs of
     three or more of one symbol) are never prose.
   - **Typographic quotes, dashes and the ellipsis** count as plain
     punctuation.
   - **A paragraph with list markers after its first line** stays
     preserved.

## PR a verification record

See the [PR a plan](t19-2-reflow-plan.md).

- **Core,** each test failing first and then passing on the host:
  - `TextLocatorTests` gains 4 tests, for `CaptureAt`;
  - `TextReflowPlannerTests` (18, including the review's three) runs over
    the authored `p2/txt-reflow.txt`. It covers block kinds, joining, the
    offset round trip for every display index, blocks that cover the text,
    no prose, an empty guide, cancellation, and TR19.1 (text, hash and
    locators unchanged).

  The full suite passes, 1317/1317.
- **Infrastructure:** the full suite passes, 837/837. New tests:
  - the schema 5 to 6 migration, and a failed one;
  - the view and the text size saving apart;
  - a missing guide or an unknown view refused, and the column's `CHECK`;
  - backups: the snapshot holds the view, restore refuses an unknown one,
    and a schema 5 backup restores, keeps its text sizes and opens as
    Original.

  The refusal test passed when written. A temporary valid value made it
  fail.
- **Production:** it builds with 0 warnings, with warnings as errors.
- **Rulings:**
  1. **T19.1 version 4 backup test:** it now rebuilds `ReaderPreferences`
     with version 1's exact statement, so its schema stays version 4.
  2. **T19.1 upgrade check:** a literal `5L` in T19.1's upgrade test is
     now `LibrarySchema.CurrentVersion`.
  3. **The two-space rule changed:** see deviation 4.
- **Final review:** 0 Critical, 3 Important and 4 Minor. All three
  Important findings were about prose rules that real guides would break,
  and each is fixed with a failing test first:
  1. one symbol (HP/MP, 50%, [WLK3]) or a typographic quote made a
     paragraph preserved;
  2. two spaces after a sentence did;
  3. a list of long items was joined into prose.

  **The minors are deferred:**
  - the heading check's cost and cancellation;
  - `Budget` having no caller until PR b;
  - `CaptureAt` at a low surrogate;
  - a form feed in prose text.

