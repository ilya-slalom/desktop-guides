# T21.2 multi-page HTML: design

Status: approved in conversation, 18 October 2026. PR a (import, storage
and backup) is next, on `feat/p2-t21-2-multipage-import`.

## Intent

A reader can import a guide that spans many saved web pages as one guide.
It has one place, one completion state, working links between its pages,
and the pages' own stylesheets. A wiki walkthrough of 28–40 pages, or a
GameFAQs HTML guide split across pages, reads like one guide offline.

This is the first format from the [format survey](format-survey.md)
(rank 1, approved 17 October 2026). It meets TR21.1 and TR21.2 in the
[work breakdown](../work-breakdown.md#s21--add-selected-local-material-formats):
- page locators hold the page's path, with a rule for progress across
  pages;
- links between the imported pages open those pages;
- the pages keep the guide's own linked stylesheets, imported with it.
  The app doesn't substitute styles of its own.

## Starting point

- **Today's HTML import** takes one saved page:
  - it follows the page's linked stylesheets, their `@import`s, and the
    images in both;
  - it copies them byte for byte under `content/<guideId>`, with a hash
    for every file in `GuideAssets`;
  - the reader serves them from the guide's own origin, with scripts off.
- **A link to another page** of the guide's origin raises "this page
  wasn't imported". A link to a website offers to open it in the browser.
- **The HTML locator** already has a `documentPath` field. It's always
  the single page today.
- **What the saved samples showed** (17 October 2026, Edge and Chrome
  "Webpage, Complete", from StrategyWiki, Zeldapedia, IGN and Neoseeker):
  - **Stylesheets are saved locally,** and the pages link the local
    copies. MediaWiki's stylesheets are saved as `load.php`,
    `load(2).php` and so on. Both the browser, when it opens the page
    from disk, and today's importer, which takes only `.css` names,
    ignore them. That is why styling broke on the two MediaWiki sites
    and not on IGN or Neoseeker.
  - **Every link is rewritten to an absolute website URL.**
  - **Each page records its own URL,** in a `saved from url=` comment and
    in `<link rel="canonical">`.
  - **No font files are saved,** and scripts aren't imported.
  - **A page imports at 1–4 MB,** with its images and stylesheets.

## The design

### Import (Infrastructure)

- **Picking:** the reader picks one saved page, as today. Its original
  URL comes from the `saved from url=` comment, or from
  `<link rel="canonical">`. A page with neither imports as a one-page
  guide, exactly as today.
- **Finding the other pages:**
  - The importer indexes the other saved pages at the top level of the
    same folder by their own recorded URLs. Pages inside a `_files`
    folder aren't candidates.
  - From the first page, each link, in document order, whose URL
    matches an indexed page adds that page.
    - URLs are compared with the fragment removed, the query kept and
      the host case ignored.
    - Only pages on the first page's host qualify.
  - Each added page's links are followed the same way, breadth-first.
    The order pages are added in is the reading order.
- **Each page's files** are found with today's rules, per page. Today's
  missing, remote, unsafe, case-collision and hash checks apply to every
  page. The `%` name rule applies to every page, not only the first.
- **Stylesheets by role, not by name** (TR21.2):
  - A file linked by `<link rel="stylesheet">` or `@import` is a
    stylesheet whatever its extension.
  - It must decode as text, contain no NUL bytes and parse as CSS within
    today's rule limits. Otherwise it's skipped with a warning.
- **Limits:** today's per-file limits stay.
  - A guide has at most 200 pages.
  - A multi-page guide may total 1 GiB, like a PDF. A one-page guide
    keeps 256 MiB.
- **The preview** shows "N pages · M files · size", lists the pages in
  reading order by their titles, and shows today's warnings. It doesn't
  count linked pages that weren't saved: on IGN that would be thousands
  of site links.
- **For S22:** the importer's input is a set of pages, each with its
  original URL and its files. Browser saves supply that today. A
  downloader for an approved source would supply the same set later
  (the user's choice, 17 October 2026).

### Reading (Production)

- **Serving:** every page and its files are served from the guide's
  origin. Only the guide's recorded files are answered. Files are served
  by their stored kind, not their extension, so `load.php` is
  `text/css`. Scripts stay off, and the security policy is unchanged.
- **Link clicks:**

  | The link | What happens |
  | --- | --- |
  | Within the same page | As today |
  | To a guide page, by its original URL or a relative path | Opens the imported page, at the link's `#fragment` if it has one |
  | To any other website page, including unsaved pages of the guide's site | Today's bar, offering to open it in the browser |

- **Moving between pages:** the place is captured before a page is
  left. A page opened by a link starts at its fragment, or its top.
- **The toolbar:** a multi-page guide gains the page commands that TXT
  and PDF have:
  - Previous page and Next page move through the reading order;
  - Go to start and Go to end open the first and last pages.

  A one-page HTML guide's toolbar is unchanged.
- **Text size and theme** apply to every page.
- **Left out:** a browser-style "back" after a link. The contents pane
  and Previous page cover it, and Alt+Left stays the app's Back.

### Locators and progress (Core, TR21.1)

- **The locator** keeps its version. `documentPath` holds the page's
  path, and decoding accepts any page of the guide. Existing places and
  bookmarks stay valid.
- **Restoring** opens the locator's page, then uses today's steps
  (offset, context, fraction). A page the guide doesn't have is
  Unavailable, and the guide opens at its first page.
- **The saved place** is one per guide, as today.
- **Progress** is (the page's index + the fraction within the page) ÷
  the page count. Halfway through page 3 of 40 is 6.25%. It's an
  estimate only, shown where today's percentage is. Completion stays
  explicit.

### Contents, search and bookmarks

- **Contents:**
  - every page by its title, in reading order;
  - the open page expanded with its headings, built from the live page
    as today;
  - another page's entry opens it at its top.

  Titles come from `<title>`, with any suffix every page shares removed.
  A one-page guide's contents are unchanged.
- **Search, across the whole guide:**
  - **Counting:** when the find bar opens, every page's saved text is
    read off the UI thread, within a budget, and matches are counted
    per page.
  - **The open page** uses today's live search and highlighting.
  - **Crossing pages:** past the open page's last match, Next opens the
    next page with a match and highlights its first. Previous does the
    same backwards.
  - **The count** ("12 of 57") adds the earlier pages' counts to the
    position on the open page. A page whose live count differs from its
    saved-text count uses the live count once it's open.
  - **Out of time:** search covers the open page and says so.
  - **The saved place** never moves, as today.
- **Bookmarks:**
  - a bookmark's locator holds its page, and following it opens the
    page;
  - in a multi-page guide, the Bookmarks tab shows the page's title
    under each label;
  - one place, one bookmark, across pages.

### Storage and backup (Infrastructure)

- **Schema 7,** one forward migration:
  - `GuideAssets.Kind` gains `'Page'`, for every page after the first.
    The first stays `'EntryHtml'`, at the guide's `PrimaryRelativePath`.
  - The new `GuidePages(GuideId, PageOrder, RequestPath, OriginalUrl,
    Title)` table:
    - one row per page, with `PageOrder` running 0…n−1;
    - unique on `(GuideId, RequestPath)`;
    - each row names an `EntryHtml` or `Page` asset;
    - rows are deleted with their guide.
  - Every existing HTML guide gets one row for its entry page, so every
    HTML guide has at least one page.
- **Integrity:** today's hash check on every read is unchanged.
- **Backup:**
  - the snapshot carries `GuidePages`;
  - archive planning checks that page rows and page assets match one to
    one, that the order has no gaps, and that order 0 is the entry;
  - restore refuses a backup that breaks these rules;
  - a schema 6 backup restores and migrates.

### Tests

- **The fixture:** an authored `tests/fixtures/p2/html-multipage`, shaped
  like a browser save with made-up content and a made-up host
  (`guides.example`). It holds:
  - 4 saved pages, each with a `saved from url=` comment, a canonical
    link, absolute links to the others and its own `_files` folder;
  - a MediaWiki-style `load.php` stylesheet;
  - a link to a page that wasn't saved;
  - an unrelated saved page from another host;
  - a `%` in one name.

  No real page is copied. The 200-page and 1 GiB limits are generated in
  tests.
- **Core:**
  - discovery order, URL normalization and the host rule;
  - trimming the shared title suffix;
  - classifying link clicks;
  - locator decoding with the page set;
  - the progress formula;
  - stepping search across pages, and its count.
- **Infrastructure:**
  - stylesheets by role: `load.php` accepted; binary, NUL and HTML
    posing as CSS refused;
  - a multi-page import publishing end to end, and the limits;
  - the schema 6 to 7 migration from a populated library, and the
    `GuidePages` constraints;
  - backup and restore with pages, refusal of broken page rows, and a
    schema 6 backup.
- **Installed** (both lanes):
  - `html-pages-import`: the preview shows 4 pages in order;
  - `html-pages-links`: a link opens the imported page, and an unsaved
    one raises the website bar;
  - `html-pages-style`: the `load.php` stylesheet is served, from the
    reader's diagnostics, with a screenshot;
  - `html-pages-restart`: a place on page 3 returns after a restart,
    with its progress;
  - `html-pages-contents`;
  - `html-pages-find`: Next crosses pages, with the count;
  - `html-pages-bookmark`.

## Delivery

| PR | Content | Installed modes |
| --- | --- | --- |
| a | Discovery, stylesheets by role, the limits, the preview, schema 7 and backup. A multi-page guide opens at its first page; its links still go to the website. | `html-pages-import` |
| b | Serving every page, link clicks between pages, the page commands, locators and progress | `html-pages-links`, `html-pages-style`, `html-pages-restart` |
| c | Contents, search and bookmarks across pages | `html-pages-contents`, `html-pages-find`, `html-pages-bookmark` |

Multi-page HTML is listed as supported only after PR c.

## Deviations from the P2 design

1. **Three PRs for one format,** not one: the reader, locator and pane
   changes are too large for a single review.
2. **A multi-page guide may total 1 GiB,** not the 256 MiB HTML limit.
3. **Stylesheets are recognised by how a page uses them,** not by their
   extension.
4. **Downloading a guide from its URL** stays in S22. T21.2 only shapes
   its input for it.
