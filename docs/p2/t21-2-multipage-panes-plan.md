# T21.2 PR c (Multi-page HTML: contents, search and bookmarks) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In a multi-page HTML guide, the contents pane lists every page, search covers the whole guide, and bookmarks name and open their page. After this PR, multi-page HTML is listed as supported.

**Architecture:**
- **Core:**
  - a cross-page reading order (`HtmlGuidePages.OrderOf`);
  - the pages' display titles;
  - `HtmlGuideOutline`, which nests the open page's outline under its page entry;
  - `GuideFind`, the cross-page search steps and count;
  - bookmark page titles.
- **Infrastructure:** `HtmlPageText` reads a saved page's text for counting.
- **Production:**
  - **The session:** it builds the cross-page outline and counts matches on every page off the UI thread, within a budget. It steps across pages and raises `PageChanged` after a move.
  - **The shell:** it rebuilds the contents on `PageChanged`.
  - **The Bookmarks tab:** it shows each bookmark's page.

**Tech Stack:** .NET 10, AngleSharp, WinUI 3, WebView2, xUnit, PowerShell UI Automation.

**Spec:** [t21-2-multipage-html-design.md](t21-2-multipage-html-design.md),
"Contents, search and bookmarks" (approved 18 October 2026; PR a #133 and
PR b #134 merged).

## Global Constraints

- **One-page guides are unchanged:** their contents, search, bookmarks and
  their accessible names stay exactly as today. Every rule below applies
  only to guides with more than one page.
- **Reading order across pages:** `Order = ((long)pageIndex << 32) | textOffset`.
  It's used by the contents' "current entry", bookmark ordering and
  "already bookmarked".
- **Contents:**
  - every page is a level-1 entry, in reading order;
  - its title is its display title (`PageTitles.WithoutSharedSuffix` over
    each page's `<title>`, or its file name without the extension);
  - its place is the page's top: offset 0, with `ScrollFraction` 0;
  - the open page's own outline nests under its page entry, one level
    down;
  - other pages have no children;
  - the outline is rebuilt after every page move.
- **Search:**
  - **Counting:** when the query changes, each other page's saved text is
    read and matched with `TextMatcher.FindAll` off the UI thread, within
    `GuideFind.Budget = 2 s`. The open page uses today's live search.
  - **Next:** after the open page's last match, it opens the next page
    with a match, at that page's first live match. It wraps from the last
    page to the first.
  - **Previous:** the same, backwards, at the page's last live match.
  - **No other page has matches:** the step wraps within the open page,
    as today.
  - **The count:** the index is the sum of earlier pages' counts plus the
    1-based position on the open page. The total is every other page's
    count plus the open page's live count, so the live count wins.
  - **Out of time:** the search covers the open page only, and the result
    has `OpenPageOnly = true`. The find bar's note reads
    "Searched this page only".
  - **The saved place:** a page move made by search raises no
    `LocationChanged`; search never moves the saved place.
- **Bookmarks:**
  - following a bookmark opens its page (PR b's restore does);
  - in a multi-page guide, the row shows the page's display title under
    the label;
  - the accessible name is "{label}, {page title}, {percent}% through";
  - "This place is already bookmarked." compares the cross-page order.
- **Copy:** "Searched this page only".
- **Shell rules:** no `cd` in local shell commands. PowerShell stays
  ASCII-only and runs under 5.1 and 7.

## Review Focus

1. **The same offset on two pages,** such as the top of two pages, is two
   places: both can be bookmarked. Pinned by Task 1's
   `TheSameOffsetOnTwoPagesIsTwoPlaces`.
2. **A query that matches only the open page** wraps within it, as today,
   and never opens another page. Pinned by Task 2's
   `NoOtherPageWrapsWithinTheOpenPage`.
3. **A page whose saved text can't be read** (a changed or missing file)
   counts as no matches; search goes on. Pinned by Task 3's
   `AnUnreadablePageCountsNothing`.
4. **Previous from the first match of the first page** wraps to the last
   page with a match, at its last match. Pinned by Task 2's
   `PreviousWrapsToTheLastPageWithAMatch`.
5. **A one-page guide's outline** is exactly the page's own outline, with
   no page entry. Pinned by Task 1's `AOnePageGuideKeepsItsOwnOutline`.

## Host commands

As in the PR b plan, with the staging folder `t21-2c`.

---

### Task 1: Core: the cross-page order, page titles, the outline and bookmark pages

**Files:**
- Modify: `src/DesktopGuides.Core/Html/HtmlGuidePages.cs` (`OrderOf`, `DisplayTitles`)
- Create: `src/DesktopGuides.Core/Html/HtmlGuideOutline.cs`
- Modify: `src/DesktopGuides.Core/Reading/BookmarkList.cs` (`AccessibleName` with a page)
- Test: `tests/DesktopGuides.Core.Tests/HtmlGuidePagesTests.cs`, `HtmlGuideOutlineTests.cs`, `BookmarkListTests.cs`

**Interfaces:**
- Produces:
  - `long HtmlGuidePages.OrderOf(int pageIndex, long textOffset)`, which
    returns `((long)pageIndex << 32) | textOffset`. It throws
    `ArgumentOutOfRangeException` for an offset outside
    `0..uint.MaxValue` or a page outside the guide.
  - `IReadOnlyList<string> HtmlGuidePages.DisplayTitles`: the titles, or
    the file names without extensions, with the shared suffix removed.
  - `static Outline HtmlGuideOutline.Build(HtmlGuidePages pages, int openPage, Outline openOutline, string contentSha256)`
  - `static string BookmarkList.AccessibleName(BookmarkItem item, string? pageTitle)`.
    The existing one-argument overload calls it with `null`.

- [ ] **Step 1: Write the failing tests.** In `HtmlGuidePagesTests`, which
  has `Pages()` with four pages:
  - `TheSameOffsetOnTwoPagesIsTwoPlaces`:
    - `OrderOf(0, 10) != OrderOf(1, 10)`;
    - `OrderOf(1, 0) > OrderOf(0, int.MaxValue)`;
    - `OrderOf(2, 5) == (2L << 32) + 5`.
  - `DisplayTitlesDropTheSharedSuffix`: pages titled
    "Sample Quest — Guides Example", "Chapter 1 — Guides Example" and so
    on, plus one with a null title and the request path "Extra.html".
    Expect `["Sample Quest", "Chapter 1", "Chapter 2", "Extra"]`. Build a
    local list for this; `Pages()` has trimmed titles already.

  In a new `HtmlGuideOutlineTests`:
  - `EveryPageIsAnEntryWithTheOpenPagesOutlineBeneath`:
    - the open page is 1, and its outline has two level-1 entries, "Ford"
      and "Road";
    - expect four top-level entries titled with the display titles;
    - expect entry 1's children to be "Ford" and "Road", at level 2;
    - expect the other entries to have no children;
    - each page entry's `Location` decodes (with `HtmlLocationRules.Decode`
      and `pages.DocumentPaths`) as `Valid`, with its page's document path
      and offset 0;
    - expect `Order == pages.OrderOf(index, 0)`.
  - `AnOpenPageWithNoOutlineStillListsThePages`: `Outline.Empty` for the
    open page gives four entries with no children.
  - `AOnePageGuideKeepsItsOwnOutline`: with one page, `Build` returns
    `openOutline` unchanged.

  In `BookmarkListTests`:
  - `AMultipageBookmarkNamesItsPage`: an HTML bookmark item labelled
    "Ford", with estimated fraction 0.375 and page title "Chapter 1",
    gives `"Ford, Chapter 1, 38% through"`.
  - The one-argument `AccessibleName` is unchanged.

- [ ] **Step 2: Run them and watch them fail**

Run: `stage && t DesktopGuides.Core.Tests "FullyQualifiedName~HtmlGuidePages|FullyQualifiedName~HtmlGuideOutline|FullyQualifiedName~BookmarkListTests"`
Expected: build errors (the new members don't exist).

- [ ] **Step 3: Implement.**
  - **`HtmlGuideOutline.Build`:**
    - a page entry's place is
      `HtmlLocationRules.Capture(sha, page.RequestPath, new HtmlCapture(0, null, null, 0), index, count)`;
    - the open page's entries are copied one level down, with their
      `Order` replaced by `pages.OrderOf(openPage, entry.Order)`;
    - the result's `Source` is `OutlineSource.Document`.
  - **`AccessibleName` with a page:** inserts `", {pageTitle}"` after the
    label, when the title is given.

- [ ] **Step 4: Run them, then the suite**

Run: the Step 2 command, then `t DesktopGuides.Core.Tests`. Expected: all pass.

- [ ] **Step 5: Commit**: `feat(p2): a guide's pages in its contents, and a reading order across pages`

---

### Task 2: Core: stepping search across pages

**Files:**
- Create: `src/DesktopGuides.Core/Reading/GuideFind.cs`
- Modify: `src/DesktopGuides.Core/Reading/FindContract.cs` (`FindResult.OpenPageOnly`)
- Modify: `src/DesktopGuides.Core/Reading/FindResultText.cs` (the note)
- Test: `tests/DesktopGuides.Core.Tests/GuideFindTests.cs`, `FindResultTextTests.cs`

**Interfaces:**
- Produces:
  - `static class GuideFind`, with:
    - `TimeSpan Budget = TimeSpan.FromSeconds(2)`;
    - `int? StepInPage(IReadOnlyList<TextMatch> matches, int? current, int fromOffset, FindDirection direction)`.
      This is `TextMatcher.Step` without wrapping: it returns `null` past
      either end.
    - `int? NextPage(IReadOnlyList<int> counts, int openPage, FindDirection direction)`:
      the nearest other page with a count above 0, wrapping, or `null`.
    - `(int Index, int Total) Count(IReadOnlyList<int> counts, int openPage, int liveCount, int indexInPage)`.
      `Index` is 1-based, and `counts[openPage]` is ignored in favour of
      `liveCount`.
  - `FindResult.OpenPageOnly` (`bool`, init, default `false`)
  - `FindResultText.Note` adds "Searched this page only" when
    `OpenPageOnly` is set. It joins with the existing parts, as today.

- [ ] **Step 1: Write the failing tests.** In a new `GuideFindTests`, using
  matches at starts 10, 20 and 30:
  - `StepInPage`:
    - with current 2 and Next, returns `null`;
    - with current 0 and Previous, returns `null`;
    - with no current, from offset 15 and Next, returns 1;
    - with no current, from offset 35 and Next, returns `null`;
    - with no current, from offset 5 and Previous, returns `null`.
  - `NextPage`, with counts `[1, 0, 2, 0]`:
    - from page 0 and Next, returns 2;
    - from page 2 and Next, returns 0 (it wraps);
    - from page 0 and Previous, returns 2;
    - `NoOtherPageWrapsWithinTheOpenPage`: counts `[0, 3, 0]` from page 1
      return `null` in both directions.
  - `PreviousWrapsToTheLastPageWithAMatch`: counts `[2, 0, 1, 4]` from
    page 0 and Previous return 3.
  - `Count`: counts `[1, 0, 2, 3]`, with the open page 2, live count 4 and
    index 1, give `(3, 8)`, that is 1 + 0 + 2 and 1 + 0 + 4 + 3.

  In `FindResultTextTests`:
  - `OpenPageOnlyIsNoted`: `FindResultText.Note(FindResult.None with { OpenPageOnly = true })`
    is "Searched this page only".

- [ ] **Step 2: Run them and watch them fail**

Run: `stage && t DesktopGuides.Core.Tests "FullyQualifiedName~GuideFind|FullyQualifiedName~FindResultText"`
Expected: build errors.

- [ ] **Step 3: Implement them.** The signatures and tests above determine
  the bodies.

- [ ] **Step 4: Run them, then the suite.** Expected: all pass.

- [ ] **Step 5: Commit**: `feat(p2): search steps and counts across a guide's pages`

---
### Task 3: Infrastructure: counting matches in the saved pages

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Reading/GuidePageCounter.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/GuidePageCounterTests.cs`

**Interfaces:**
- Consumes: `ManagedHtmlAssetReader`'s `HtmlAssetRead` (status and bytes) and `TextMatcher`.
- Produces:
  - `static string GuidePageCounter.TextOf(byte[] html)`: the page's
    text for matching. AngleSharp parses it, `script`, `style`,
    `noscript` and `template` are removed, and the result is
    `body.TextContent`, or the empty string with no body.
  - `static int[]? GuidePageCounter.Count(IReadOnlyList<GuideAsset> pages, Func<GuideAsset, HtmlAssetRead> read, FindQuery query, CancellationToken token)`:
    - one count per page, in order;
    - a read whose status isn't `Served` counts 0;
    - a cancelled token returns `null`, never partial counts;
    - each count is `TextMatcher.FindAll(text, query, TextMatcher.MaxMatches).Count`.

- [ ] **Step 1: Write the failing tests** in `GuidePageCounterTests`, with
  in-memory pages built as `GuideAsset` rows and a read function from a
  dictionary:
  - `PagesAreCountedInOrder`: three pages holding "ford ford", "road" and
    "Ford" give `[2, 0, 1]` for "ford", assuming case-insensitive matching
    as `TextMatcherTests` shows. Check that before writing the expected
    values.
  - `ScriptsAndStylesDontCount`: `<script>ford</script><style>.ford{}</style><p>ford</p>`
    counts 1.
  - `AnUnreadablePageCountsNothing` (Review Focus 3): a read returning
    `HtmlAssetReadStatus.Changed` for page 1 gives 0 there, and the others
    still count.
  - `ACancelledCountIsNoCount`: an already-cancelled token returns `null`.

- [ ] **Step 2: Run them and watch them fail**

Run: `stage && t DesktopGuides.Infrastructure.Tests "FullyQualifiedName~GuidePageCounterTests"`
Expected: build errors.

- [ ] **Step 3: Implement `GuidePageCounter`.** Check the token between
  pages, and use `TextMatcher.FindAll`'s existing overload. Match how
  `HtmlAssetRead` exposes bytes (`Bytes`) and status (`Status`).

- [ ] **Step 4: Run them, then the suite.** Expected: all pass.

- [ ] **Step 5: Commit**: `feat(p2): count search matches in a guide's saved pages`

---

### Task 4: Production: contents, search and bookmarks across pages

**Files:**
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs`
- Modify: `src/DesktopGuides.Core/Html/HtmlRequestPolicy.cs` (`GuideAsset? AssetFor(string requestPath)`)
- Modify: `src/DesktopGuides.Production/ShellWindow.Contents.cs`, `ShellWindow.HtmlReader.cs`, `ShellWindow.Bookmarks.cs`
- Modify: `src/DesktopGuides.Production/BookmarksPane.xaml` and `.xaml.cs` (the page line)
- Test: `tests/DesktopGuides.Core.Tests/HtmlRequestPolicyTests.cs` (`AssetFor`)

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces:
  - `event EventHandler? HtmlReaderSession.PageChanged`, raised after a
    page move;
  - `HtmlReaderSession.Pages` (`HtmlGuidePages`);
  - `BookmarkRow(BookmarkItem item, string? pageTitle)`, with
    `PageTitle` and `PageTitleVisibility`;
  - `BookmarksPane`'s row-building call gains a
    `Func<BookmarkItem, string?> pageTitleOf` parameter, with a default of
    none.

- [ ] **Step 1: `AssetFor`, test first.** In `HtmlRequestPolicyTests`,
  `AssetForFindsARecordedFile`:
  - `Policy().AssetFor("styles/main.css")` is the stylesheet row;
  - `AssetFor("nope.css")` is `null`.

  Run it and watch it fail, then implement it as a dictionary lookup.

- [ ] **Step 2: The session.**
  - **The order:** `OrderOf(location)` returns
    `pages.OrderOf(pages.IndexOfDocument(position.DocumentPath) ?? 0, position.TextOffset)`.
    For one page that equals today's offset, since page 0 shifts nothing.
  - **The outline:** `GetOutlineAsync` returns
    `HtmlGuideOutline.Build(pages, pageIndex, <today's open-page outline>, contentSha256)`.
  - **`PageChanged`:** raised at the end of a successful move.
  - **`GoToPageAsync`** gains `bool raiseLocation = true`. Search passes
    `false`, so a move made by search raises no `LocationChanged`.
  - **Search, in `FindAsync`, when `pages.Count > 1`:**
    - **A query change:** as today it reads the open page's live matches.
      Then, off the UI thread, run `GuidePageCounter.Count` over every
      page with `reader.Read`, under a `CancellationTokenSource` linked to
      `token` and cancelled after `GuideFind.Budget`.
    - **Keep `pageCounts`:** an `int[]`, or `null` when the count ran out
      of time.
    - **A step:** `GuideFind.StepInPage` first. When it returns `null` and
      `pageCounts` isn't `null`, `GuideFind.NextPage` picks the page.
      `GoToPageAsync(page, null, raiseLocation: false)` opens it, then the
      new page's live text is read, and its first match (Next) or last
      (Previous) is shown.
    - **When `NextPage` returns `null`,** or the counts timed out,
      `TextMatcher.Step` wraps in the open page, as today.
    - **The result:** `GuideFind.Count(pageCounts, pageIndex, matches.Count, index)`.
      `OpenPageOnly = pageCounts is null` when the guide has more than one
      page.
    - **The counts stay** until the query changes or `ClearFind`.
  - **The test position file** gains `"page": pageIndex`. The smoke reads
    the document path already, so this field is optional; leave it out if
    it isn't needed.
- [ ] **Step 3: The shell.**
  - **The contents:** subscribe to `PageChanged` for an HTML session. On
    it, cancel and replace `outlineCancel`, and run `BuildOutlineAsync`
    again. The pane stays open, and it selects the current entry as
    today.
  - **The bookmarks:** for an HTML session with more than one page, build
    rows with `pageTitleOf`, which maps an item's `HtmlPosition.DocumentPath`
    through `Pages.IndexOfDocument` to `Pages.DisplayTitles`. Otherwise
    pass `null`.
- [ ] **Step 4: The row.** In `BookmarksPane.xaml`'s template:
  - under the label, add a second `TextBlock` bound to `PageTitle`, in
    `DesktopGuidesMetadataStyle`, with `Visibility` bound to
    `PageTitleVisibility`;
  - `AccessibleName` uses `BookmarkList.AccessibleName(Item, pageTitle)`.
- [ ] **Step 5: Build and run the suites**

Run: `stage && build`, then both suites and the toolbar smoke build, as in PR b.
Expected: 0 warnings; all pass.

- [ ] **Step 6: Commit**: `feat(p2): contents, search and bookmarks across a web guide's pages`

---

### Task 5: The installed modes

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-NavigationScenarios`)

**Interfaces:**
- Consumes: Task 4's behavior, PR b's seeded "Pages Web Guide", and the fixture's text:
  - "start" appears once each on the first page ("Start here"), chapter 1
    and chapter 2 ("Back to the start");
  - "ford" appears twice, both in chapter 1.
- Produces three modes: `html-pages-contents`, `html-pages-find` and
  `html-pages-bookmark`. Each is added to both mode lists; the
  `html-pages-*` routing covers them.

- [ ] **Step 1: The modes.**
  - **`html-pages-contents`:**
    1. Open the guide and press Ctrl+Shift+C.
    2. The top-level contents names, in order, are "Sample Quest",
       "Chapter 1", "Chapter 2" and "Chapter 3". The open page's heading
       may appear beneath; assert the top level through the tree items'
       level or their order.
    3. Choose "Chapter 2": the position's document is chapter 2.
    4. The pane stays open, and after the rebuild "Chapter 2" is the
       selected entry.
  - **`html-pages-find`:**
    1. Open the guide, take a baseline of saves, press Ctrl+F and type
       "start".
    2. The count is "1 of 3", with the document on the first page.
    3. Next (Enter): "2 of 3", on chapter 1.
    4. Next: "3 of 3", on chapter 2.
    5. Next: "1 of 3", back on the first page.
    6. Shift+F3: "3 of 3", on chapter 2.
    7. Search for "ford" from the first page: "1 of 2", on chapter 1.
       Next: "2 of 2". Next: "1 of 2", wrapping in chapter 1.
    8. Saves equal the baseline.
    9. Close the bar.
  - **`html-pages-bookmark`:**
    1. Open the guide. Ctrl+D shows "Bookmark added." at the first page's
       top.
    2. Press Ctrl+Page Down twice, then Ctrl+D: "Bookmark added." again,
       not "This place is already bookmarked." This is Review Focus 1:
       the same offset, 0, on another page.
    3. Press Go to start and Ctrl+Shift+B.
    4. A row's name matches "*Chapter 2*".
    5. Activate it (Enter): the document is chapter 2.

  Reuse the helpers `bookmark-html` and `find-html` use, and match their
  names (`Wait-Name 'FindCount' ...`, `Read-Saves`, `Get-BookmarkRows`
  or its equivalent).
- [ ] **Step 2: The install side.** After the PR b passes, in
  `Run-NavigationScenarios`:
  - clear the reading locations;
  - run `html-pages-contents` and `html-pages-find` with `-HtmlPosition`;
  - check that no place was stored for "Pages Web Guide" after the find
    pass, through `& $estimateOf`;
  - run `html-pages-bookmark` with `-HtmlPosition`.
- [ ] **Step 3: Check:** the ASCII check and the parse check.
- [ ] **Step 4: Commit, push, and run the navigation group on both lanes.** Expected: both pass. Copy
  `html-pages-contents.png` and `html-pages-find.png` from the MSIX run to
  `docs/p2/evidence/ci/t21-2/`.

---

### Task 6: Records, the final review, the PR run and the PR

- [ ] **Step 1: The full suites and the builds.**
- [ ] **Step 2: The final review.** Run the whole-branch review first.
  Its fixes land before the records.
- [ ] **Step 3: The records.**
  - **The design's Status:** "PR b merged as #134. PR c (contents,
    search and bookmarks) implemented on `feat/p2-t21-2-multipage-panes`.
    Multi-page HTML is supported." Add the "PR c verification record".
  - **`results.md`:** #134's row: 2026-10-18, `07af06e`, T21.2 (PR b), run
    39119742284, Core 1373, Infra 872. The note: "Reading a multi-page
    guide. Passed on the first attempt."
  - **The status pages:** "T21.2 multi-page HTML complete pending this PR
    (#133, #134, PR c in review); image maps next." The `progress.md`
    "Next" becomes "T21.2 image maps design".
  - **Where formats are listed as supported:** `initial-design.md`'s more
    material line, and any README or Settings text. Add multi-page HTML
    ("several saved pages of one guide").
- [ ] **Step 4: The PR:** "T21.2 PR c: contents, search and bookmarks
  across pages", with the screenshots, the dispatches, rulings and minors.
  Wait for its run, then stop for the user's merge decision.

## Traceability

| Design item | Task |
| --- | --- |
| Contents: every page by title, the open page's headings beneath, another page opens at its top | 1, 4, 5 |
| Search across the whole guide: saved-text counts within a budget, crossing pages, the count across pages, the live count wins, out of time noted | 2, 3, 4, 5 |
| Search never moves the saved place | 4, 5 |
| Bookmarks open their page and show its title; one place, one bookmark across pages | 1, 4, 5 |
| Multi-page HTML listed as supported after PR c | 6 |
