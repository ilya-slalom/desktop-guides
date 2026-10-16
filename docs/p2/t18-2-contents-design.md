# T18.2 table of contents: design

Status: approved in conversation, 15 October 2026. PR a (the contract, the
TXT detector, the pane and its keys) merged as #118, and PR b (HTML and PDF
sources) as #119. T18.2 and M8 are complete.

## Intent

HTML headings and in-page contents lists, PDF outlines, and detected TXT
sections each form a contents tree that can be followed. A guide with no
sections shows the fallback. Failing to detect sections never blocks
opening or scrolling (TR18.1). This is the M8 exit in the
[P2 plan](implementation-plan.md#m8-table-of-contents), from
[section 4](../p2-technical-design.md#4-s18-table-of-contents-t182) of the
P2 design.

## Starting point

- **What exists:**
  - every reader saves and restores a `ReaderLocation`, with its outcomes;
  - HTML's position walk gives walk offsets, quotes and element ids;
  - PR c of T18.1 opens a second PdfPig document for search;
  - T16.1's keyboard map and its conflict tests.
- **What's missing:**
  - an `Outline` capability;
  - a left pane;
  - any section detection.

## The design

### The contract (Core)

- **The interface:** `IOutlineReader { Task<Outline> GetOutlineAsync(CancellationToken token); long OrderOf(ReaderLocation location); }`.
  `OrderOf` gives the current place's `Order`, so the pane can select the
  entry it falls in.
  A session that implements it declares a new
  `ReaderCapabilities.Outline` flag.
- **The data:**
  - `Outline(OutlineSource Source, IReadOnlyList<OutlineEntry> Entries)`,
    where `OutlineSource` is `Document`, `Detected` or `None`;
  - `OutlineEntry(Title, Level, ReaderLocation Location, long Order, Children)`.

  Titles are trimmed to 120 characters. `Order` is the entry's place in
  reading order: the TXT offset, the HTML walk offset, or the PDF page
  and fraction.
- **`OutlineTree`:**
  - **`Build`** turns a flat list of (title, level, location, order) into
    the nested tree. A jump in levels, such as an `h1` followed by an
    `h4`, nests one level down and never into empty levels.
  - **`EntryAt(outline, order)`** returns the last entry at or before a
    place.
- **Following an entry** calls the reader's existing
  `RestoreLocationAsync`, with its existing outcomes.

### The three sources

- **TXT (`Detected`):** Core's `TextSectionDetector`, over the normalized
  text.
  - **Its signals, strongest first:**
    1. **Search codes.** Codes such as `[WLK01]`, listed in the guide's own
       contents block (the 60-line window holding the most distinct codes,
       at least three), map to a later heading. A heading is underlined or
       boxed, or has its code first or last on a short line. A code with no
       heading, and a code the contents block doesn't list (button names
       such as `[L1]`), gives no entry.
    2. **Underlined and boxed headings.** A line followed by a rule of
       `====` or `----` of about its own length, or a line between two
       rules. It must follow a blank line or the file's start, be at most 50
       characters, and not be column-aligned, so table rows and a
       paragraph's last line over a rule aren't headings.
    3. **Numbered headings.** `1.0`, `1.1`, `2.` and similar, at the start
       of a short line, nested by depth.
  - **When it finds nothing:** it returns `None` with fewer than three
    sections, or when they all sit in the first tenth of the guide.
  - **The budget:** it stops at 2 s and returns `None`, never a partial
    tree.
- **HTML (`Document`):**
  - **The script:** a fixed script, like T18.1's find scripts, returns
    each `h1` to `h6` and each same-page `#id` link. For each it gives the
    walk offset and the text, and for a link, its target's offset.
  - **Core decides.** A container holding three or more same-page links,
    each to a different place further down the page, is the page's own
    contents list, and is used instead of the headings. A references or
    "Back to top" list points back up, or to one place, so it never
    counts.
  - **Locations:** each entry's location is the HTML position the reader
    already uses (the offset, the quote and the id), so a restore is
    exact.
- **PDF (`Document`):**
  - **The outline:** the file's own outline, read with PdfPig's
    bookmarks from the search document.
  - **Locations:** entries that point into the document become page
    locations. Links to websites or other files are left out, and their
    children move up. A grouping entry with no page of its own takes its
    first child's page.
  - **Without an outline:** the result is `None`, with no detection.
- **Building:** the outline builds in the background once the guide is
  open. A failure or a timeout gives `None`, and never delays opening or
  scrolling.

### The pane and the keys (Production)

- **The pane:** a fixed-width left pane (`ReaderContentsPane`, 300 px)
  beside the reading surface. It has a "Contents" heading and a native
  `TreeView` (`ReaderContentsTree`).
- **Narrow windows** (below the 720 px reader breakpoint): the pane covers
  the surface, and closes itself after an entry is followed (the user's
  choice, 15 October 2026).
- **Opening it:**
  - a "Contents" toggle in the reader toolbar, shown only with the
    `Outline` capability;
  - Ctrl+Shift+C in the reader.

  Opening moves focus to the tree. The pane's open state lasts for the
  app session, across guides, and isn't stored.
- **The current entry** is selected when the pane opens, using
  `OutlineTree.EntryAt`.
- **Following an entry:**
  - **Enter or a click** restores the entry's location and moves focus to
    the guide. This is a deliberate move, so the place is saved.
  - **An `Approximate` restore** shows the existing "Your place may have
    shifted." notice.
  - **Escape** in the pane closes it, and focus returns to the guide.
- **Status in the pane:**
  - **while building:** "Finding sections…";
  - **with no sections:** "No sections found in this guide."
    (`ReaderContentsEmpty`), with a "Search in the guide" link that opens
    the find bar. Bookmarks join the fallback in T19.1, which also gives
    the pane its Bookmarks tab.
- **The keyboard map:** T16.1's map gains Ctrl+Shift+C ("Show contents")
  in the Reader context, covered by its conflict tests.

### Tests

- **Core:**
  - `OutlineTree`: nesting, level jumps, the trim, and `EntryAt`.
  - `TextSectionDetector`, over authored fixtures:
    - **what it finds:** search codes, underlined headings, and numbered
      and nested headings;
    - **where it must find nothing:** ASCII art, a short guide, prose
      without headings, and a contents block with no body;
    - **the 2 s budget,** with an injected clock.
  - The HTML contents-list rule, and parsing the script's reply.
- **Infrastructure:** the PDF outline mapping, over a generated PDF with
  a nested outline and an external link.
- **Installed** (the `navigation` group, both lanes):
  - **`contents-txt`, `contents-html` and `contents-pdf`:** the tree,
    following an entry, the saved place, focus, and the current entry on
    reopening;
  - **`contents-none`:** the fallback, on Numbered Lines Guide;
  - **a narrow-window pass:** the overlay closes after an entry is
    followed.

## Delivery

| PR | Content | Installed modes |
| --- | --- | --- |
| a | The contract, `OutlineTree`, `TextSectionDetector`, the pane, the toolbar toggle and Ctrl+Shift+C; TXT contents | `contents-txt`, `contents-none`, the narrow pass |
| b | HTML and PDF sources | `contents-html`, `contents-pdf` |

## Deviations from the P2 design

1. **The fallback links only to Search** until T19.1 adds bookmarks.
2. **Narrow windows show the pane as an overlay** (the user's choice). The
   P2 design describes only the fixed side pane.

## PR a verification record

See the [PR a plan](t18-2-contents-plan.md).

- **Core,** each test failing first and then passing on the host:
  - `OutlineTreeTests` (7);
  - `TextSectionDetectorTests` (20, including the installed fixture and the
    six review cases);
  - the Contents capability in `ReaderContractTests`;
  - the Ctrl+Shift+C table row and its routing test.

  The full Core suite passes, 1250/1250.
- **Production:** it builds with 0 warnings, with warnings as errors.
- **Installed:** the `navigation` shard passes on both lanes, with
  `contents-txt`, `contents-none` and `contents-narrow` beside the find
  modes, and `reader-toolbar-ui` checks the Contents command. Dispatches:
  [38730009630](https://github.com/ilya-slalom/desktop-guides/actions/runs/38730009630)
  and, after the review,
  [38733785829](https://github.com/ilya-slalom/desktop-guides/actions/runs/38733785829).
  Screenshots: [contents-txt](evidence/ci/t18-2/contents-txt.png),
  [contents-none](evidence/ci/t18-2/contents-none.png) and
  [contents-narrow](evidence/ci/t18-2/contents-narrow.png).
- **Found on the way:**
  1. **The narrow overlay was see-through.** The elevated card fill is
     translucent, so the guide's text showed through the entries. The
     `contents-narrow` screenshot showed it, although the bounds check
     passed. The pane now uses the reading card's opaque fill.
  2. **Two test setup faults, fixed in the tests:**
     - in a wide window the pane stays open after an entry is followed,
       so the reopen step first closes it;
     - `contents-narrow` clears the place `contents-txt` saved.
- **Final review:** 0 Critical, 3 Important and 16 Minor. All three
  Important findings were in the detector, and all are fixed with failing
  tests first:
  1. bracketed button names became search codes;
  2. cross-references took entries;
  3. table rows and a paragraph's last line over a rule became headings.

  The minors are deferred and listed in the PR.

## PR b verification record

See the [PR b plan](t18-2-contents-html-pdf-plan.md).

- **Core,** each test failing first and then passing on the host:
  `HtmlOutlineRulesTests` (15, including the review's three). The full
  Core suite passes, 1265/1265.
- **Infrastructure:** the PDF outline tests (3), over a generated
  `pdf-outline.pdf`. That fixture has a nested outline, a website link
  and an other-file link, and a grouping entry with no page. The full
  suite passes, 806/806.
- **Production:** it builds with 0 warnings, with warnings as errors.
- **Installed:** the `navigation` shard passes on both lanes, with
  `contents-html`, `contents-html-headings` and `contents-pdf` beside PR
  a's modes. Dispatches:
  [38749883671](https://github.com/ilya-slalom/desktop-guides/actions/runs/38749883671)
  and, after the review,
  [38755884810](https://github.com/ilya-slalom/desktop-guides/actions/runs/38755884810).
  Screenshots: [contents-html](evidence/ci/t18-2/contents-html.png) and
  [contents-pdf](evidence/ci/t18-2/contents-pdf.png).
- **Found on the way:** PdfPig's `TryGetBookmarks` takes
  `allowContainerNode`, which defaults to `false` and then drops grouping
  entries with their children. A fixture grouping entry failed the test
  with the default and passes with `true`.
- **Final review:** 0 Critical, 3 Important and 6 Minor. All three
  Important findings are fixed:
  1. **A references, footnote or back-to-top list beat the real
     contents.** Only forward links count now, each target once.
  2. **A quote cut through a surrogate pair discarded the whole
     outline.** The entry is now kept.
  3. **The empty-anchor lookup was quadratic.** It was measured in
     headless Chrome with scripts disabled. On a 30,000-paragraph page
     with 1,000 empty anchors, the old script was cut off after 120 s;
     the new one finishes in 1.6 s.

  The minors are deferred and listed in the PR.
