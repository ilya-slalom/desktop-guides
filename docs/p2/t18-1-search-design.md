# T18.1 in-guide search: design

Status: approved, 15 October 2026. PR a (the contract, TXT search and the
find bar) merged as #113, and PR b (HTML) as #116. PR c (PDF) is
implemented on `feat/p2-t18-1-pdf-search`.

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
- **HTML** (as built; see deviations 3 and 4):
  - **Reading the text:** `HtmlFindScripts.ReadText` reuses the Prelude's
    `walk()` through the existing DevTools `Runtime.evaluate`. It returns
    the walk's text, the ranges of hidden text (no box, or not
    `visibility: visible`), and the offsets where a new block or a `<br>`
    starts. Core's `HtmlFindText` masks the hidden text, and `TextMatcher`
    skips it and reads each block edge as a space.
  - **Showing a match:** `HtmlFindScripts.Show` sets
    `CSS.highlights.set('dg-find', …)` without changing the DOM, and
    scrolls the match a quarter of the way down, inside any scrolling box
    first.
  - **The style:** `HtmlReaderStyle` gains a `::highlight(dg-find)` rule for
    each theme: `Mark` in Light, the Dark palette's mark colour in Dark,
    and the system `Highlight` colours in High Contrast.
  - **While a step runs,** it holds the session's restore turn, so the
    500 ms tracker and the resize re-apply leave the page to it. The
    captured point follows the match, and a step stops when the bar
    closes.
- **PDF** (as built; see deviation 5):
  - **Its own document:** the loader opens a second `PdfPageTextSource`
    ("Search", with a one-page cache) beside the display source, with the
    same password used once. The display source, with its 8-page cache
    and the installed-test assertion on it, isn't touched.
  - **The scan:** `PdfTextScan` (Infrastructure) reads pages in order on a
    worker through `IPdfPageTextReader`, with cancellation, and matches
    each page with `TextMatcher`. A page with no letters is image-only,
    and a page that can't be read is recorded; either way the scan goes
    on. A step waits only as far as the scan must go to answer it.
  - **A match:** it goes to the page (`GoTo(page, 0)`), and selects the
    match in the `DocumentText` page-text pane, mapped through
    `TextBoxOffsets` and scrolled into view.
  - **The count:** "Searching…" while a step waits, "N+" while the scan
    runs, and the final total through `ResultUpdated`. A note under the bar
    names image-only and unreadable pages.
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
3. **HTML matching runs in Core, not in the page script** (approved 15
   October 2026). The script returns the walk's text with its block edges
   and hidden ranges, so TXT and HTML share one matcher and its tests.
4. **The tracker isn't paused for the whole time the bar is open**
   (approved 15 October 2026). Each find step holds the restore turn
   instead, so the point follows the match, and the re-apply after the
   bar closes (a resize) keeps the match in view.
5. **The PDF search document is opened by the loader, not the scanner.**
   A locked PDF's password is never stored, so both documents open at
   load with it, once.

## PR a verification record

See the [PR a plan](t18-1-search-plan.md).

- **Core,** each test failing first and then passing on the host:
  - `TextMatcherTests` (10);
  - three pause tests in `ProgressCoordinatorTests` (34 in all);
  - three `ColumnOf` tests;
  - the keyboard table, plus a routing test.

  The full Core suite passes, 1190/1190, with the review fix's test
  `ResumingTakesTheCurrentPlaceAsTheBaseline`.
- **Production:** it builds with 0 errors and 0 warnings.
- **Installed:** dispatch
  [38600617803](https://github.com/ilya-slalom/desktop-guides/actions/runs/38600617803)
  passed the new `navigation` shard on both lanes (`find-txt`, `find-back`
  and `find-unsupported`), but failed `reader-toolbar-ui`: its harness
  still used the removed `FindAction`, and its smoke tested the old prompt
  dialog. The harness now logs the toolbar's `FindRequested`, and the smoke
  expects it. The re-dispatch,
  [38615503420](https://github.com/ilya-slalom/desktop-guides/actions/runs/38615503420),
  passed every job. The [find-txt screenshot](evidence/ci/t18-1/find-txt.png)
  is from it.
- **Final review:** two Important findings, both fixed:
  1. closing the bar saved the match's place on the next tick; resuming
     now takes the place shown as the baseline, without saving it;
  2. two quick Ctrl+F presses could open the bar twice and leak a pause;
     opening is now guarded, and needs the guide's tracking to have started.
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
  5. **The open-twice fix has no failing test first.** The race needs two
     key presses inside the pause's await, which the installed smoke can't
     time reliably.

## PR b verification record

See the [PR b plan](t18-1-html-search-plan.md).

- **Core,** each test failing first and then passing on the host:
  - `TextMatcherTests`: block edges as spaces, masked text skipped, and
    whitespace either side of masked text as one space (17 in all);
  - `HtmlFindTextTests` (13);
  - `HtmlReaderStyleTests`: the find rule for each theme.

  The full Core suite passes, 1210/1210.
- **Production:** it builds with 0 errors and 0 warnings, with warnings as
  errors.
- **Installed:** the `navigation` shard passes on both lanes, in dispatch
  [38676788492](https://github.com/ilya-slalom/desktop-guides/actions/runs/38676788492)
  and again after the review fix. Its modes are `find-txt`, `find-back`,
  `find-html`, `find-html-text`, `find-html-close` and `find-unsupported`.
  Screenshots: [find-html](evidence/ci/t18-1/find-html.png) and
  [find-html-text](evidence/ci/t18-1/find-html-text.png).
- **Found on the way:**
  1. **Keys typed inside an HTML page never reach the shell.** This covers
     Ctrl+F, F3, Escape and Alt+Left, and has been true since P1. Three
     spike runs logged no key events at all at `ShellRoot`. It's tracked
     as [#115](https://github.com/ilya-slalom/desktop-guides/issues/115).
     `find-html` opens the bar from the toolbar's *Find in guide*, and
     `find-html-text` uses Ctrl+F with the keyboard outside the page.
  2. **Hidden text blocked a match across it.** `html-long` has a hidden
     panel between two lines, so the matcher now skips masked text. This
     was reproduced offline by running `ReadText` in headless Chrome with
     scripts disabled, and feeding its reply to the C# matcher.
- **Final review:** one Important finding, fixed. A find step still
  running when the bar closed re-drew the highlight, could scroll the
  page, and announced a stale result. `find-html-close`, with a test gate
  holding the step 2 s, failed in
  [38681152873](https://github.com/ilya-slalom/desktop-guides/actions/runs/38681152873)
  and passes after the fix. Eleven minors are deferred and listed in the
  PR.
- **Rulings:**
  1. **The click before the real scroll.** The find-html click into the page goes
     through its left padding, because the top line's centre can sit under
     the toolbar.
  2. **No HTML-only matching rules.** `<noscript>` text, CSS-generated text
     and a body that is itself the scroller follow the position walk's
     existing limits.

## PR c verification record

See the [PR c plan](t18-1-pdf-search-plan.md).

- **Core,** each test failing first and then passing on the host:
  `TextBoxOffsetsTests` (4) and `FindResultTextTests` (7). The full Core
  suite passes, 1221/1221.
- **Infrastructure:** `PdfTextScanTests` (12: order, image-only and
  unreadable pages, waiting, wrapping, disposing, cancelling, the cap, a
  real guide on its own source) and the loader's own-search-source and
  locked-guide checks. The full suite passes, 803/803.
- **Production:** it builds with 0 warnings, with warnings as errors.
- **Installed:** the `navigation` shard passes on both lanes, with
  `find-pdf`, `find-pdf-scan`, `find-pdf-wait` and `find-pdf-lines`
  beside PR a's and PR b's modes. Dispatches:
  [38691334117](https://github.com/ilya-slalom/desktop-guides/actions/runs/38691334117)
  and, after the review,
  [38698633489](https://github.com/ilya-slalom/desktop-guides/actions/runs/38698633489).
  Screenshots: [find-pdf](evidence/ci/t18-1/find-pdf.png),
  [find-pdf-scan](evidence/ci/t18-1/find-pdf-scan.png) and
  [find-pdf-lines](evidence/ci/t18-1/find-pdf-lines.png).
- **`find-unsupported` retired.** Every format now supports search, so
  the toolbar smoke's `no-find-without-the-capability` keeps TR18.1. Its
  `TextControls` set no longer includes Find.
- **Final review:** 0 Critical, 3 Important, 9 Minor.
  1. **Fixed:** a step that waited on the scan showed the last query's
     count. The bar now says "Searching…". `find-pdf-wait`, with a test gate
     slowing each page by 40 ms, failed in
     [38696720812](https://github.com/ilya-slalom/desktop-guides/actions/runs/38696720812)
     and passes after the fix.
  2. **Covered:** the installed test couldn't see line-break or scrolling
     faults. `find-pdf-lines`, on a new generated `pdf-lines` fixture with
     the target on line 78, checks the exact selection and that it is on
     screen. It passed without a code change, so the suspected faults
     don't reproduce.
  3. **Measured, not a fault:** PdfPig might keep a scanned document's
     objects. A full scan of a 200 MiB, 200-page image PDF left managed
     memory at 11 MiB and the working set at about 122 MiB.

  The minors are deferred and listed in the PR.
