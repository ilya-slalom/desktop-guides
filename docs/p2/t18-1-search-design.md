# T18.1 in-guide search: design

Status: approved, 15 October 2026. PR a (the contract, TXT search and the
find bar) is implemented on `feat/p2-t18-1-search`. PR b (HTML) and PR c
(PDF) follow.

## Intent

TXT, HTML and PDF guides each find next and previous matches, with counts,
without changing the saved reading place. Search controls appear only for
guides that support it (TR18.1). This is the M7 exit in the
[P2 plan](implementation-plan.md#m7-in-guide-search).

## Starting point

Find is already partly built:
- **Already in the contract:** `ReaderCapabilities.Find`, `ReaderCommand.Find`,
  and `FindAction(Query)`.
- **Already in the toolbar:** a hidden "Find in guide" button that opens a
  prompt dialog.
- **Not yet declared:** no reader declares the capability.
- **Not yet possible:** nothing can mark a move as not-to-save. TXT raises
  `LocationChanged` on every top-line change, including programmatic
  scrolls.

## The design

### Contract and matching (Core)

- **`IFindableReader`:** `FindAsync(FindQuery, FindDirection, token)` returns
  `FindResult(Outcome, Index, Total, TotalIsPartial, UnsearchablePages)`,
  and there's a `ClearFind()`. Sessions that implement it declare
  `ReaderCapabilities.Find`. This replaces `FindAction` and the toolbar's
  prompt dialog.
- **`TextMatcher`:** case-insensitive (ordinal, culture-invariant), with
  whitespace runs collapsed on both sides. It returns match ranges as
  source offsets. TXT and PDF use it directly. HTML's script mirrors the
  same rules.

### Not saving the reading place (Core)

- **The pause:** `ProgressCoordinator` gains `PauseTracking()`, returning a
  token. While it's held, `LocationChanged` doesn't mark the guide dirty,
  and no deadline is armed.
- **Disposing the token** takes the current location as a new baseline
  without saving it, so the next real move saves.
- **The find bar** holds the token while it's open. "Back to my place"
  restores the location captured when the bar opened.

### The readers

- **TXT:**
  - **Searching:** over `TextGuideDocument.Text`.
  - **A match:** it scrolls to `LineAtOffset`.
  - **The highlight:** a `TextHighlighter` on the row's `TextBlock`,
    applied in `LineContainerChanging`. A new `TextLineView.ColumnOf(offset)`
    turns the source offset into a column, accounting for tab expansion.
- **HTML:**
  - **The script:** `HtmlFindScripts` reuses the Prelude's `walk()` through
    the existing DevTools `Runtime.evaluate`. It matches the page text,
    sets `CSS.highlights.set('dg-find', …)` without changing the DOM, and
    scrolls the match into view, returning `{index, total}`.
  - **The style:** `HtmlReaderStyle` gains a constant `::highlight(dg-find)`
    rule, with a dark-theme colour.
  - **While searching,** the 500 ms location tracker is paused.
- **PDF:**
  - **The scanner:** `PdfTextScanner` (Infrastructure) opens its own PdfPig
    document over the managed file. It scans pages in order with
    cancellation, recording each page's `HasLetters`. The reader's
    display text source, with its 8-page cache and the installed-test
    assertion on it, isn't touched.
  - **A match:** it goes to the page (`GoTo(page, 0)`), and selects the
    match in the `DocumentText` page-text pane.
  - **The count:** "N+" while the scan runs. Image-only pages are counted.
  - **Highlight rectangles over the page image** are a follow-up task. That
    is a deviation from the P2 design (see below).

### The shell

- **The find bar:** a `FindBar` control below `ReaderToolbar`, in the
  reader's toolbar `StackPanel`. It has a text box, Previous, Next, a
  count, "Back to my place" and Close.
- **Keys:** Enter or F3 for the next match; Shift+Enter or Shift+F3 for the
  previous one; Escape closes the bar.
- **Screen readers:** results are announced through `AnnounceStatus`.
- **The keyboard map:** T16.1's map (`KeyboardShortcuts.All`) gains Ctrl+F,
  F3 and Shift+F3 in the reader context. In the library, Ctrl+F still
  focuses the library search.
- **Unsupported guides:** Ctrl+F there says "Search isn't available for this
  guide."

### Tests

- **Core:**
  - `TextMatcher`: case, whitespace across lines, an empty query,
    overlapping matches, and Previous wrapping;
  - the coordinator's pause: no save while paused, and a new baseline after
    resuming;
  - `ColumnOf` with tabs;
  - the updated keyboard table.
- **Infrastructure:** `PdfTextScanner`:
  - the order;
  - an image-only page;
  - cancellation;
  - a damaged page;
  - that the display source is unaffected.
- **Installed:** a `navigation` group (seeded by `seed-progress`, plus
  `pdf-scan`) with these modes:
  - `find-txt`, `find-html` and `find-pdf`: the count, the match position,
    F3 and Shift+F3, and the stored locator unchanged after searching and
    after closing;
  - `find-unsupported`;
  - `find-back`.

  It runs as a `navigation` CI shard on both lanes, with a `shell-scope`
  choice for dispatches.

## Deviations from the P2 design

1. **PDF matches are shown in the page-text pane, not as rectangles over
   the page image** (the user's choice, 15 October 2026). The overlay
   needs PdfPig word boxes and a zoom-aware layer over the page view, and
   is planned as a follow-up.
2. **The existing `FindAction` command and its prompt dialog are replaced**
   by `IFindableReader` and the find bar, rather than kept alongside them.

## PR a verification record

See the [PR a plan](t18-1-search-plan.md).

- **Core,** each test failing first and then passing on the host:
  - `TextMatcherTests` (10);
  - three pause tests in `ProgressCoordinatorTests` (34 in all);
  - three `ColumnOf` tests;
  - the keyboard table, plus a routing test.

  The full Core suite passes, 1189/1189.
- **Production:** it builds with 0 errors and 0 warnings.
- **Installed:** dispatch
  [38600617803](https://github.com/ilya-slalom/desktop-guides/actions/runs/38600617803)
  passed the new `navigation` shard on both lanes (`find-txt`, `find-back`
  and `find-unsupported`).
  - **What failed:** `reader-toolbar-ui`. Its test harness still used the
    removed `FindAction`, and its smoke tested the old prompt dialog.
  - **The fix:** the harness now logs the toolbar's `FindRequested`, and
    the smoke expects it. The re-dispatch is recorded in the PR.
- **Rulings:**
  1. **No custom highlight brush.** A theme-aware brush can't be looked up
     reliably from code-behind, so `TextHighlighter`'s default system
     highlight colours are used. They follow the theme and high contrast.
  2. **Focus after closing the find bar** goes to the reader toolbar,
     where the reader keys route from.
  3. **The find modes sit in the smoke's shared reader block,** beside the
     progress modes. `find-txt` checks the progress save count directly,
     without a hand-off to the installer: unchanged after searching, and
     higher after Page Down. The installer checks the stored estimate.
  4. **The toolbar smoke follows the find-bar change:** it expects the
     find request, not the prompt.
