# S22 PR 3b live-check fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix what the user's live check of PR 3b (#148) on GameFAQs and
Neoseeker found, on 23 October 2026:
- a same-site link that opens a new tab leaves the app for the system browser;
- saved HTML guides have no styling;
- Neoseeker walkthroughs keep online links to the wiki articles they point at (side quests, activities, world intel);
- Neoseeker images link to the wiki's `File:` pages;
- a GameFAQs link that spells a page differently (`arts-plug-ins` for `arts-plugins`) stays online.

**Architecture:**
- **Core:** `OnlineNavigation.ForNewWindow`, a pure decision for the browser's new-window requests.
- **Infrastructure:**
  - `NeoseekerAdapter` lists a walkthrough page's linked wiki articles and reads them;
  - `CaptureDraft` and `GuideCapture` capture those articles one level deep;
  - `OfflineCopyBuilder` writes a built-in stylesheet per site, and resolves spelling-variant page links.
- **Production:** `OnlineBrowser.OnNewWindowRequested` follows the Core decision.

**Tech Stack:** .NET 10, WinUI 3, WebView2 1.0.4191.47, AngleSharp, xUnit.

**Spec:** [the P3 technical design](../p3-technical-design.md), section 5.3,
amended in Task 3 and Task 4 by the user's choices of 23 October 2026:
- **built-in styles:** each site gets a stylesheet written by Desktop Guides, chosen over capturing the site's CSS;
- **linked articles, one level:** a walkthrough's directly linked same-game wiki articles are saved too.

The branch is `feat/p3-s22-find-online` (PR #148). The fixes land there,
before its merge.

**Out of scope:** centering text guides in the reader. The user chose
"always centered" on 23 October 2026, as its own PR after #148:
- the normal view centers the block of rows, each as wide as the widest line, so columns keep their alignment;
- the reflow view caps prose at about 80 characters and centers it.

## Global Constraints

- **Capture:** the app still makes no HTTP request of its own to GameFAQs or Neoseeker. Articles load through the visible browser like any captured page: 2 s apart, with the same pause rules.
- **One level:** an article's own links are never followed. Only articles of the same game are followed.
- **Not articles:** `File:`, `Special:`, `Category:`, `Template:`, `User:`, `Talk:` and the walkthrough itself are excluded, as are links with a query and red links (class `new`).
- **The cap:** 200 pages per guide, articles included. Walkthrough pages come first, so the cap trims articles. A guide cut by the cap saves as partial, as today.
- **Styles:**
  - the stylesheet is a fixed embedded resource, so no page data enters it;
  - it contains no `url()`, `@import` or `@font-face`, so it can't make a request;
  - it uses selectors of low specificity, so the reader's dark theme still wins.
- **Copy:** the stylesheet is `guide.css`, beside `guide.html`, linked from every page's `<head>`. It goes through the static HTML import unchanged.
- **Drafts:** drafts saved before this change still load. `GuidePageLink.Linked` defaults to false, and `SchemaVersion` stays 1.

## Review Focus

1. A walkthrough page linking to the same article twice, or with different fragments, captures it once, and every link to it is rewritten.
2. An article link that loads as something else (a redirect to another game, a category page) is dropped from the draft, not a pause. The capture goes on, and the link stays online.
3. Resuming a paused capture with articles keeps the articles already found, and doesn't find them again.
4. Re-saving a guide whose articles changed replaces it. One whose pages are all unchanged is still up to date.
5. A new-tab link to the same site during a capture does nothing. One to another site opens externally only when the user clicked.

## Host commands

Tests run on the Windows host. `/tmp/s22d.sh <project> [filter]` syncs
the branch to `E:\work\desktop-guides\s22d` and runs one test project.

### Task 1: Same-site new tabs stay in the app

**Files:**
- Create: `src/DesktopGuides.Core/Online/OnlineNavigation.cs`
- Modify: `src/DesktopGuides.Production/OnlineBrowser.cs` (`OnNewWindowRequested`)
- Test: `tests/DesktopGuides.Core.Tests/OnlineNavigationTests.cs`

**Interfaces:**
- Produces: `public enum NewWindowAction { Ignore, OpenHere, OpenExternally }` and `public static NewWindowAction OnlineNavigation.ForNewWindow(Uri? url, bool capturing, bool userInitiated)`.

- [ ] **Step 1: Write the failing tests:**
  - `https://www.neoseeker.com/chrono-trigger/faqs/131223-o.html`, not capturing, clicked → `OpenHere`;
  - the same, not clicked → `OpenHere`, since a same-site page loads in the browser either way;
  - while capturing → `Ignore`;
  - `https://www.example.com/`, clicked → `OpenExternally`;
  - the same, not clicked → `Ignore`;
  - `http://www.neoseeker.com/x` → `OpenExternally` (not a source, since sources are https);
  - `mailto:a@b.c`, or a null URL → `Ignore`.
- [ ] **Step 2: Run them; they fail** because `OnlineNavigation` doesn't exist.
- [ ] **Step 3: Implement `ForNewWindow`** on `OnlineSources.Find`. Then in `OnNewWindowRequested`: `OpenHere` → `View.CoreWebView2.Navigate(url)`; `OpenExternally` → `ExternalLinkRequested`. `args.Handled = true` stays.
- [ ] **Step 4: Run Core tests; they pass.**
- [ ] **Step 5: Commit** `fix(p3): same-site links that open a new tab stay in Find online`.

### Task 2: Neoseeker image links and GameFAQs page spellings

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Online/NeoseekerAdapter.cs`, `OfflineCopyBuilder.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Online/NeoseekerAdapterTests.cs`, `OfflineCopyBuilderTests.cs`

- [ ] **Step 1: Write the failing tests:**
  - `ReadWalkthrough` on content with `<a class="image" href="/final-fantasy-vii-rebirth/File:FF7RB_OBJ1.png"><img …></a>` keeps the `img` and drops the anchor. Any anchor whose path has a `File:` segment is unwrapped;
  - `OfflineCopyBuilder.Build`, for a GameFAQs guide with page `arts-plugins`, rewrites a link to `…/faqs/81095/arts-plug-ins#fire` to `arts-plugins.html#fire`;
  - with pages `ab` and `a-b` both present, a link to `a-b` goes to `a-b.html`: the exact match wins.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.**
  - The adapter unwraps `File:` anchors after `Absolutize`.
  - The builder adds a second lookup keyed by the URL with `-` removed from its last path segment. It's used only when the exact key misses, and only for keys with a single page.
- [ ] **Step 4: Run Infrastructure tests; they pass.**
- [ ] **Step 5: Commit** `fix(p3): Neoseeker image links stay images; GameFAQs page spellings resolve`.

### Task 3: Built-in site styles

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Online/Styles/gamefaqs.css`, `neoseeker.css`, `base.css` (embedded resources)
- Modify: `src/DesktopGuides.Infrastructure/DesktopGuides.Infrastructure.csproj`, `OfflineCopyBuilder.cs`, `docs/p3-technical-design.md` (5.3, "The offline copy")
- Test: `tests/DesktopGuides.Infrastructure.Tests/Online/OfflineCopyBuilderTests.cs`, `OnlineGuideSaverTests.cs`

**Interfaces:**
- Produces: `internal static string OfflineCopyBuilder.StyleSheet(SourceSite site)`, which is `base.css` followed by the site's sheet. `<main class="dg-site-gamefaqs">` or `<main class="dg-site-neoseeker">`. Neoseeker content sits inside `<div class="mw-parser-output">`.

**Visual direction** (ui-ux-pro-max, plan stage; the user chose "Fluent,
app-native" on 23 October 2026 over "Paper, book-like" and "Site-faithful"):
- **one look for both sites,** matching the app's Fluent styles;
- **type:** `Segoe UI Variable Text, Segoe UI, sans-serif` at 15px, line height 1.6, lines at most 72ch. Headings are semibold: h1 28px, h2 20px, h3 17px, h4–h5 15px. `pre` and `code` use `Cascadia Mono, Consolas, monospace`;
- **colors:**
  - text `#1B1B1B` on `#FFFFFF`, secondary text `#5D5D5D`, borders `#E0E0E0`;
  - links `#005FB8`, the Windows light accent, at 6.1:1 against white;
- **tables:** 1px `#E0E0E0` borders, 4px corners, header row `#F3F3F3` semibold, even rows `#FAFAFA`, cell padding 6px 10px;
- **notes** (Neoseeker `.alert-*`): a `#F9F9F9` fill, a 4px left bar and 4px corners. The bar is `#005FB8` (primary, info), `#0F7B0F` (success), `#C42B1C` (danger), or `#8A8A8A` (secondary, dark). The kind isn't shown by color alone, since the alert's own text stays;
- **spoilers** (`.fspoiler`): a `#E0E0E0` block with the text `transparent` until `:hover` or `:focus`. The builder adds `tabindex="0"` so the keyboard can reveal them;
- **dark theme:** the reader's forced dark rules override these colors, and the layout, type and borders remain. In high contrast, Windows' forced colors apply.

The stylesheets' targets, taken from the user's live-check copies:
- **base:** the `dg-source` header, the `dg-pages` contents, and `dg-missing-image`; body width, line height, images at most 100% wide, and the table defaults.
- **GameFAQs:**
  - `table.ffaq` with borders and padding, and its header cells;
  - `.fspoiler` hidden until hover or focus, shown when printed;
  - `.imgnofloat` and `.bigresize`;
  - the h2–h5 scale, `ul` and `blockquote`.
- **Neoseeker:**
  - `.wikitable` and `.table-list`;
  - `.alert` and `.alert-primary`, `-secondary`, `-success`, `-info`, `-danger` and `-dark`, as bordered boxes with tinted fills;
  - `.center-div-container`, `.img-icon` (inline, 1.5em), `.floatnone` and `.pull-right`;
  - `.mw-headline`, `dl`/`dt`/`dd`, and `.hidden_text`.

- [ ] **Step 1: Write the failing tests:**
  - an HTML copy has `guide.css`, and every page's head has `<link rel="stylesheet" href="guide.css">`;
  - `main` carries the site class;
  - a GameFAQs `<span class="fspoiler">` gets `tabindex="0"`;
  - `StyleSheet(site)` for each site contains no `url(`, `@import` or `@font-face`;
  - a text copy writes no stylesheet;
  - through `OnlineGuideSaver.SaveAsync`, the published guide's assets include `guide.css` as a `StyleSheet`.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Write the sheets and wire them in.** Use `:where()` for anything below the site class, so the reader's dark rules (`body * { … !important }`) still apply. Then amend design 5.3: the offline copy gets a built-in stylesheet per site, the site's CSS isn't captured, and the user's reason.
- [ ] **Step 4: Run Infrastructure tests; they pass.**
- [ ] **Step 5: Commit** `feat(p3): saved HTML guides get a built-in stylesheet per site`.

### Task 4: Neoseeker linked articles, one level

**Files:**
- Modify:
  - `src/DesktopGuides.Infrastructure/Online/GuideSourceAdapter.cs`
  - `NeoseekerAdapter.cs`, `CaptureStore.cs`, `GuideCapture.cs`, `OfflineCopyBuilder.cs`
  - `docs/p3-technical-design.md` (5.3, "Only the guide's pages…")
- Test: `NeoseekerAdapterTests.cs`, `CaptureStoreTests.cs`, `GuideCaptureTests.cs`, `OfflineCopyBuilderTests.cs`
- Fixtures: `tests/fixtures/p3/neoseeker/wiki-episode-3.html` gains links to `/final-fantasy-vii-rebirth/Quests/Stuck_in_a_Rut`, `/World_Intel/Gongaga#Activation_Intel_2`, a `File:` link, a `Category:` link and another game's article. Add `wiki-article-stuck-in-a-rut.html` and `wiki-article-gongaga.html`, plus their manifest routes.

**Interfaces:**
- `GuidePageLink(string PageKey, Uri Url, string? Title, bool Linked = false)`.
- `GuidePageReading` gains `IReadOnlyList<GuidePageLink> Linked`, which is empty except on a walkthrough page.
- `IGuideSourceAdapter` gains `GuidePageReading? ReadLinked(GuidePageLink link, string html, GuidePageMatch item)`. GameFAQs returns null.
- Article keys are `article-` plus the path after the game, keyed like walkthrough pages: `Quests/Stuck_in_a_Rut` is `article-quests-stuck-in-a-rut`.
- `CaptureDraft.Extend(IEnumerable<GuidePageLink> links)` appends unseen keys up to 200 pages and counts every unseen key in `TotalPages`. `CaptureDraft.Drop(string pageKey)` removes a page that isn't an article.

- [ ] **Step 1: Write the failing tests:**
  - a walkthrough reading's `Linked` holds the two same-game articles once each, without fragments, and none of the excluded kinds;
  - `ReadLinked` on an article fixture reads its title from `#page-title h1`, its content and images, and gives `Linked` empty;
  - `ReadLinked` on a page without `#wiki-content` returns null;
  - `GuideCapture`: the walkthrough's pages are captured, then its articles, 2 s apart. The draft completes with all of them;
  - an article that loads 200 but isn't readable is dropped, and the capture completes;
  - an article returning 429 pauses there. Resuming continues with the articles still listed;
  - with 199 walkthrough pages and 3 articles, the draft holds 200 and `TotalPages` is 202, so the draft isn't complete;
  - a pre-change `capture.json` with no `Linked` field loads;
  - the builder rewrites links to articles, fragments kept, to `article-….html`. The contents list shows the walkthrough pages, then a "Linked articles" heading and the articles.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.**
  - `GuideCapture.RunAsync` loops while `Remaining` isn't empty. After each walkthrough page it calls `draft.Extend(reading.Linked)`. A `Linked` page goes through `adapter.ReadLinked`, and a 200 with a null reading means `Drop`.
  - Amend design 5.3: a walkthrough's pages, and the same game's wiki articles they link to directly; the user's choice of 23 October 2026.
- [ ] **Step 4: Run Infrastructure and Core tests; they pass.**
- [ ] **Step 5: Commit** `feat(p3): Neoseeker walkthroughs save the wiki articles they link to`.

### Task 5: Installed check and records

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (`online-save`), `docs/p3/evidence/ci/s22/` (screenshot), the PR #148 description.

- [ ] **Step 1:** In `online-save`, after saving the fixture walkthrough, `describe-online` lists `guide.css` and the `article-` pages. Checking the styles' appearance is left to the user's live check, since the CSS rendering itself is WebView2's.
- [ ] **Step 2:** Push, and get CI green. Copy the portable artifact to `E:\work\live-check\<sha>` for the user's second live check.
- [ ] **Step 3:** Add a styled saved guide's screenshot to the PR description, plus a summary of the fixes.
