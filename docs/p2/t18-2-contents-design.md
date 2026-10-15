# T18.2 table of contents: design

Status: approved in conversation, 15 October 2026. PR a (the contract, the
TXT detector, the pane and its keys) and PR b (HTML and PDF sources)
follow.

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

- **The interface:** `IOutlineReader { Task<Outline> GetOutlineAsync(CancellationToken token); }`.
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
       contents block, map to their later occurrence. The contents block
       itself is skipped.
    2. **Underlined headings.** A line followed by a line of `====` or
       `----` of about its own length.
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
  - **Core decides.** A run of three or more same-page links whose
    targets exist is the page's own contents list, and is used instead
    of the headings.
  - **Locations:** each entry's location is the HTML position the reader
    already uses (the offset, the quote and the id), so a restore is
    exact.
- **PDF (`Document`):**
  - **The outline:** the file's own outline, read with PdfPig's
    bookmarks from the search document.
  - **Locations:** entries that point into the document become page
    locations. Links to websites or other files are left out.
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
