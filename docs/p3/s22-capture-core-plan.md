# S22 PR 3a (capture core: adapters, the offline copy and saving) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Given the HTML of guide pages from GameFAQs or Neoseeker,
Infrastructure recognises the guide, lists its own pages, extracts each
page's content and the guide's title, author, notice and update date,
builds an offline copy, and saves it to a game as a new guide or as a
replacement of the one saved before. An unfinished capture is kept and can
be resumed. There's no UI and no network access: PR 3b's in-app browser
supplies the pages and images.

**Architecture:**
- **`Infrastructure/Online`** (new) holds everything:
  - `IGuideSourceAdapter`, with `GameFaqsAdapter` and `NeoseekerAdapter`, parsing with AngleSharp, which Infrastructure already references;
  - `NoticeRules`;
  - `OfflineCopyBuilder`;
  - `CaptureStore`;
  - `OnlineGuideSaver`.
- **The offline copy** is written as flat files that the existing validator already imports:
  - HTML pages as sibling files, each starting with a saved-from-URL comment, with relative links between them;
  - TXT as one UTF-8 file.
- **Saving:** `OnlineGuideSaver` inspects the copy through `GuideImportValidator`, then calls `PublishAsync` or `ReplaceAsync` (PR 2).

**Tech Stack:** .NET 10, AngleSharp 1.8.2 (already pinned), xUnit.

**Spec:** [the P3 technical design](../p3-technical-design.md), sections
5.3, 5.5 and 5.8, and the [source review](source-review.md).

**Deviation from the design's delivery list:** the design's PR 3 (Find
online, the in-app browser and capture) is split in two:
- **3a**, this plan, is the testable core, with no UI;
- **3b** is the Find online screen, the in-app browser, the hidden capture browser, pause and resume, the build switches, the fixture server and the `online` E2E group.

The design's Delivery section is updated in Task 6.

## Global Constraints

- **No network access in this PR.** Adapters parse HTML strings; images arrive as files the caller provides.
- **Identity** (`GuidePageMatch.ItemId`):
  - GameFAQs: the FAQ number from `/{platform}/{gameid}-{slug}/faqs/{faqid}`.
  - Neoseeker text FAQ:
    - the number in `link[rel=canonical]` when that is a Neoseeker FAQ URL, else the number in the page URL `/{game}/faqs/{id}-{slug}.html`;
    - so the copies of one FAQ for two platforms are one item (design call).
  - Neoseeker wiki walkthrough: `{game}/walkthrough`.
- **URL patterns** (case-insensitive host, `https` only; anything else is not a guide):
  - GameFAQs:
    - `^/(?<plat>[a-z0-9]+)/(?<gid>\d+)-(?<slug>[a-z0-9-]+)/faqs/(?<faq>\d+)(?:/(?<page>[a-z0-9-]+))?/?$`
    - plus an optional `?page=N`;
    - on `gamefaqs.gamespot.com`.
  - Neoseeker text FAQ: `^/(?<game>[a-z0-9-]+)/faqs/(?<id>\d+)-(?<slug>[^/.]+)\.html$` on `www.neoseeker.com`.
  - Neoseeker walkthrough:
    - `^/(?<game>[a-z0-9-]+)/walkthrough(?:/(?<page>[^/?#]+))?$`;
    - a URL with any query (for example `?edit`) is not a guide page.
- **Page keys**, which are also the offline copy's file names (`{key}.html`), must stay the same across saves (PR 2's verification record):
  - GameFAQs slug pages: the slug.
  - GameFAQs `?page=N`: `page-{N}`. A bare URL and `?page=0` are both `page-0`.
  - Neoseeker walkthrough: the hub is `index`. A page is its path segment, URL-decoded, lower-cased, with every run of characters other than `a-z0-9` replaced by `-`, and trimmed of `-`.
  - The first page in the guide's order is the entry, always saved as `guide.html`.
- **Extraction:**

  | Page | Content | Page list |
  |---|---|---|
  | GameFAQs text FAQ | the text of every `#faqtext > pre[id^=faqspan-]`, in order, **joined with nothing between** | none (one page) |
  | GameFAQs HTML guide | the children of `#faqwrap` after `div.ftoc` | `div.ftoc ol a[href]`, resolved against `<base href>` or the page URL, deduplicated by page key, in document order |
  | Neoseeker text FAQ | the text of `#faqtxt > pre` | none |
  | Neoseeker walkthrough | `article#wiki-content .mw-parser-output`, minus `.section-ad`, `.noprint` and comments | the hub, then `#wiki-navigation a[href]` whose path starts `/{game}/walkthrough/`, in document order |

- **Metadata:**
  - **GameFAQs** prefers the top-level JSON-LD `WebPage` (`headline`, `author.name`, `dateModified`, else `datePublished`). Its fallbacks:
    - title: `<title>` up to " - GameFAQs";
    - author: `h2.title a.contrib1`;
    - date: `Updated: MM/DD/YYYY`.
  - **Neoseeker FAQ:** title `#markupfaq > h1` trimmed, author `.author_area span.author a`, date `.author_area time[datetime]`.
  - **Neoseeker walkthrough:**
    - title "{the `strong` above `#page-title h1`} Walkthrough";
    - date: the latest `time[datetime]` after "Last edited by";
    - no author;
    - notice "© Neo Era Media Inc.".
- **Notices:**
  - GameFAQs and Neoseeker text FAQs: the notice is the first block of up to 30 lines of the text that contains "copyright", "(c)" or "©" (ignoring case), cut at 16,384 characters.
  - GameFAQs HTML guides: the same rule, over the text of the guide's pages.
  - `NoticeRules.Classify` returns `Permissive`, `Restrictive` or `Unknown`, in this order of precedence:
    1. **`Permissive`** if the notice mentions personal use: "personal use", "private use" or "personal, private".
    2. **`Restrictive`** if a forbidding phrase ("may not", "cannot", "must not", "not be", "prohibited", "forbidden") comes within 80 characters of "electronic", "any form", "in any way", "any means", "stored" or "saved".
    3. **`Unknown`** otherwise.
- **The offline copy (HTML):**
  - **Every page file starts with** `<!-- saved from url=(NNNN){url} -->`. NNNN is the URL's length as four digits; this is what the existing scanner reads.
  - **Its body** holds a `<header class="dg-source">` with:
    - the guide's title as `<h1>`;
    - "By {author}" when there is one;
    - "Saved from {site} on {date:d MMMM yyyy}" (invariant culture), where the site is GameFAQs or Neoseeker;
    - the source URL as a link.

    The page's content follows the header.
  - **Removed:** `script`, `noscript`, `iframe`, `object`, `embed`, `form`, `input`, `button`, `style` and `link` elements, and every `on*` attribute.
  - **Links:**
    - a link to another captured page of the guide becomes `{key}.html`, with any `#fragment` kept;
    - every other `href` stays absolute (resolved);
    - `javascript:` links lose their `href`.
  - **Images:**
    - an `img` whose resolved URL (`src`, or `data-src` when `src` is a `data:` URL) the caller supplied becomes `images/{n}.{ext}`;
    - an image the caller didn't supply becomes `<span class="dg-missing-image">Image not saved</span>`;
    - `srcset` and `data-srcset` are removed.
- **The offline copy (TXT):** `guide.txt`, UTF-8 with a BOM, the extracted text unchanged.
- **Captures:**
  - Kept under `{cacheRoot}/online-captures/{captureId:N}/`, with a `capture.json` and the pages captured so far.
  - `CaptureStore.Sweep` deletes captures older than 7 days.
  - A capture is found again by `(gameId, Site, ItemId)`.
- **Limits:** at most 200 pages per guide, the existing static HTML limit. A page list longer than that is cut to 200 and the save is partial.
- **Shell rules:** no `cd` in local shell commands.

## Review Focus

1. **A GameFAQs text FAQ split into several `pre` chunks mid-line** keeps its exact text: no added newline at the joins. Pinned by Task 1's `ATextFaqJoinsItsChunksExactly`.
2. **A Neoseeker walkthrough with lazy-loaded images** captures the real image URL, not the `data:` placeholder. Pinned by Task 2's `LazyImagesUseTheirDataSource` and Task 4's `ALazyImageIsSavedLocally`.
3. **A GameFAQs `?page=` guide** treats the bare URL and `?page=0` as one page, and so doesn't save it twice. Pinned by Task 1's `TheFirstPagedPageHasOneKey`.
4. **A wiki whose navigation also lists non-walkthrough pages** (Characters, Appendix) leaves them out. Pinned by Task 2's `OnlyTheWalkthroughsOwnPagesAreListed`.
5. **Saving a guide again keeps its page file names**, so a place on page 2 restores by context. Pinned by Task 6's `ASecondSaveKeepsThePagePathsAndThePlace`.

## Host commands

As in the [PR 2 plan](s22-source-fields-plan.md#host-commands), with the
staging folder `E:\work\desktop-guides\s22c` and the logs `E:\work\s22c-*.log`.

---
### Task 1: The adapter contract and the GameFAQs adapter

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Online/GuideSourceAdapter.cs` (the contract and records)
- Create: `src/DesktopGuides.Infrastructure/Online/GameFaqsAdapter.cs`
- Create: `tests/fixtures/p3/gamefaqs/` (synthetic pages, below)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Online/GameFaqsAdapterTests.cs` (new)

**Interfaces:**
- Produces:
  - `public enum CapturedFormat { Text, Html }`;
  - `public sealed record GuidePageMatch(SourceSite Site, string ItemId, string PageKey)`;
  - `public sealed record GuidePageLink(string PageKey, Uri Url, string? Title)`;
  - `public sealed record GuidePageReading(GuidePageMatch Match, Uri Url, CapturedFormat Format, string Title, string? Author, string? Notice, DateTimeOffset? SourceUpdated, IReadOnlyList<GuidePageLink> Pages, string Content, IReadOnlyList<Uri> Images)`:
    - `Content` is text for `Text`, and the content's HTML for `Html`;
    - `Pages` is empty for a one-page guide;
    - `Images` holds every image the content uses, resolved, in document order.
  - `public interface IGuideSourceAdapter`:
    - `SourceSite Site { get; }`;
    - `GuidePageMatch? Match(Uri url)`;
    - `GuidePageReading? Read(Uri url, string html)`, which is null when the page isn't a guide the adapter can read, for example a changed layout with no content element.
  - `GameFaqsAdapter : IGuideSourceAdapter`.

**Fixtures** are written for the tests, with structure copied from the archived pages the [research](#research-record) saw and invented filler text. No guide text is copied.
- **`text-faq.html`:** a text FAQ.
  - The text is in three `pre#faqspan-N`, with the second split mid-line ("The sword is in the cha" / "pel.").
  - JSON-LD gives a headline, an author and a `dateModified`.
  - The text has a notice block ("(c) Copyright 2003 Example Author. This guide may not be reproduced in any form…").
- **`html-slug-cairn.html`, `html-slug-intro.html`, `html-slug-fishing.html`:** a three-page HTML guide using slugs.
  - Each has `<base href=".../faqs/78736/cairn">` and a `div.ftoc ol` linking `introduction`, a group heading `<b>Walkthrough</b>`, then `cairn` and `fishing`.
  - The content after the `.ftoc` has an `h2`, a `p` with a link to `fishing#bait`, and an image `/a/faqs/36/78736-54.png`.
- **`html-paged-0.html`, `html-paged-1.html`:** an HTML guide using `?page=N`.
  - The `.ftoc` links `?page=0#Start`, `?page=1#Middle`, `?page=2#End`.
  - The page canonical is `?page=1`.

- [ ] **Step 1: Write the failing tests:**
  - **`MatchesGuideUrlsAndNothingElse`**, a theory:
    - `…/ps/197341-final-fantasy-vii/faqs/13639` → `("13639", "")`;
    - `…/faqs/78736/cairn` → `("78736", "cairn")`;
    - `…/faqs/74635?page=3` → `("74635", "page-3")`;
    - `…/faqs/74635?page=0` and `…/faqs/74635` → `("74635", "page-0")` when the page is paged. `Match` alone can't tell, so it returns `""` for the bare URL, and `Read` sets the key;
    - `http://` URLs, `/boards/…`, `/faqs` with no number, and other hosts → null.
  - **`ATextFaqJoinsItsChunksExactly`** (Review Focus 1):
    - `Format` is `Text`;
    - `Content` contains "The sword is in the chapel." and equals the three chunks' `TextContent` concatenated;
    - `Pages` is empty.
  - **`ATextFaqReadsItsMetadata`:** the title, author and `SourceUpdated` come from the JSON-LD, and `Notice` starts "(c) Copyright 2003 Example Author".
  - **`AnHtmlGuideListsItsPagesInOrder`:** reading `cairn` gives `Pages` keys `["introduction", "cairn", "fishing"]` with absolute URLs, and `Format` is `Html`.
  - **`AnHtmlPageExcludesItsTableOfContents`:** `Content` has the `h2`, but no `ftoc`; `Images` is `[https://gamefaqs.gamespot.com/a/faqs/36/78736-54.png]`.
  - **`TheFirstPagedPageHasOneKey`** (Review Focus 3): reading `html-paged-1.html` gives `Pages` keys `["page-0", "page-1", "page-2"]`, and its own key `page-1`.
  - **`AChangedLayoutIsNotAGuide`:** a page with no `#faqwrap` or `#faqtext` reads as null.
- [ ] **Step 2: Run them.** `t DesktopGuides.Infrastructure.Tests GameFaqsAdapter`. Expected: build errors.
- [ ] **Step 3: Implement** the Interfaces block, following the Global Constraints. Parse with AngleSharp's `HtmlParser`, as `StaticHtmlDependencyScanner` does.
- [ ] **Step 4: Run them.** Expected: PASS, then the whole Infrastructure suite.
- [ ] **Step 5: Commit:** "feat(p3): the GameFAQs source adapter".

### Task 2: The Neoseeker adapter

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Online/NeoseekerAdapter.cs`
- Create: `tests/fixtures/p3/neoseeker/`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Online/NeoseekerAdapterTests.cs` (new)

**Interfaces:**
- Consumes: Task 1's contract.
- Produces: `NeoseekerAdapter : IGuideSourceAdapter`.

**Fixtures** (synthetic, as in Task 1):
- **`text-faq.html`:**
  - a `#markupfaq` with an `h1` "Example Quest Game Script " (trailing space);
  - `.author_area` with an author link and `time datetime="2004-04-12T00:00:00Z"`;
  - a `div.alert` before the text;
  - `#faqtxt.text_only > pre`, containing an injected `<a>` game link and a notice block;
  - `link rel=canonical` to `…/faqs/74586-script.html`, while the page URL is `…/faqs/260078-script.html`.
- **`wiki-hub.html`, `wiki-a-golden-key.html`, `wiki-episode-3.html`:** a walkthrough.
  - `#page-title` has `<strong>Example Quest</strong>` and an `h1`.
  - `#wiki-navigation` has a "Characters" group first (`/example-quest/characters/Hero`), then walkthrough links `/example-quest/walkthrough/Episode_3` and `/example-quest/walkthrough/A_Golden_Key`, plus an `#toc` accordion toggle.
  - The content has a `.section-ad`, a `.noprint`, an HTML comment, one eager image `src="https://cdn.staticneo.com/ew/thumb/a.png"`, and one lazy image: `class="lazyload"`, a `data:image/svg+xml` `src`, `data-src="https://cdn.staticneo.com/ew/thumb/b.png"`.
  - "Last edited by Editor on `<time datetime="2024-03-19T14:52:48Z">`".

- [ ] **Step 1: Write the failing tests:**
  - **`MatchesFaqsAndWalkthroughPages`:**
    - `…/chrono-trigger/faqs/260078-script.html` → `("260078", "")`;
    - `…/example-quest/walkthrough/A_Golden_Key` → `("example-quest/walkthrough", "a-golden-key")`;
    - `…/example-quest/walkthrough` → `("example-quest/walkthrough", "index")`;
    - `…/walkthrough/X?edit`, `…/characters/Hero` and `http://` → null.
  - **`ATextFaqUsesItsCanonicalIdAndPlainText`:**
    - `ItemId` is `"74586"`;
    - `Content` is the `pre`'s text, including the game link's text but not its markup, and excludes the `.alert`;
    - the title is "Example Quest Game Script", with no trailing space;
    - the author and date are read.
  - **`OnlyTheWalkthroughsOwnPagesAreListed`** (Review Focus 4): `Pages` keys are `["index", "episode-3", "a-golden-key"]`, with no Characters page and no `#toc` link.
  - **`AWalkthroughPageDropsAdsCommentsAndNoPrint`:** `Content` has no `section-ad`, no `noprint`, and no `<!--`.
  - **`LazyImagesUseTheirDataSource`** (Review Focus 2): `Images` is `[…/a.png, …/b.png]`.
  - **`AWalkthroughReadsItsTitleDateAndNotice`:**
    - the title is "Example Quest Walkthrough";
    - `SourceUpdated` is 2024-03-19T14:52:48Z;
    - `Author` is null;
    - `Notice` is "© Neo Era Media Inc.".
- [ ] **Step 2–4:** as in Task 1, with the filter `NeoseekerAdapter`.
- [ ] **Step 5: Commit:** "feat(p3): the Neoseeker source adapter".

### Task 3: Notice rules

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Online/NoticeRules.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Online/NoticeRulesTests.cs` (new)

**Interfaces:**
- Produces:
  - `public enum NoticeKind { Unknown, Permissive, Restrictive }`;
  - `NoticeRules.Find(string text) -> string?`, the Global Constraints' notice block;
  - `NoticeRules.Classify(string? notice) -> NoticeKind`.

Tasks 1 and 2 call `NoticeRules.Find` for text guides. If Task 1 is done first, it uses a private copy of the same rule until this task replaces it.

- [ ] **Step 1: Write the failing tests:**
  - **`ClassifiesNotices`**, a theory:
    - "This guide cannot be distributed in printed or electronic media in any way" → `Restrictive`;
    - "You may print, view, or reproduce this guide for your personal use only" → `Permissive`;
    - "May not be reproduced in any form, except for personal, private use" → `Permissive` (permissive wins);
    - "(c) 2003 Example Author" → `Unknown`;
    - null → `Unknown`;
    - "This FAQ may not be posted on any other web site" → `Unknown`. It's about hosting elsewhere, not saving.
  - **`FindsTheFirstCopyrightBlock`:** a text with a contents list, then a "COPYRIGHT" section of 3 lines, then more text. `Find` returns those 3 lines. A text without the words returns null, and a block longer than 30 lines is cut at 30.
- [ ] **Step 2–4:** as in Task 1, with the filter `NoticeRules`.
- [ ] **Step 5: Commit:** "feat(p3): notice rules for saved guides".

### Task 4: The offline copy

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Online/OfflineCopyBuilder.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Online/OfflineCopyBuilderTests.cs` (new)

**Interfaces:**
- Consumes: Task 1's records.
- Produces:
  - `public sealed record CapturedPage(GuidePageReading Reading, IReadOnlyDictionary<Uri, string> ImageFiles)`. `ImageFiles` maps an image URL to a file the caller saved;
  - `public sealed record OfflineCopy(string EntryPath, CapturedFormat Format, int MissingImages)`;
  - `OfflineCopyBuilder.Build(IReadOnlyList<CapturedPage> pages, string folder, DateTimeOffset savedUtc) -> OfflineCopy`:
    - writes into an empty `folder`;
    - `pages` is in the guide's order, possibly fewer than `Pages` lists for a partial capture;
    - throws `ArgumentException` for no pages, or for pages of more than one item.

- [ ] **Step 1: Write the failing tests.** Each builds pages from Task 1's and Task 2's fixtures through the adapters, then runs `GuideImportValidator.InspectAsync` on `EntryPath`:
  - **`AMultiPageGuideImportsAsOneMultiPageHtmlGuide`:** the three slug pages.
    - The files are `guide.html` (introduction), `cairn.html` and `fishing.html`, each starting with `<!-- saved from url=(NNNN)` and the right URL.
    - The validator gives an `HtmlImportManifest`, and a preview through `PreviewHtmlAsync` has 3 pages in that order.
  - **`LinksBetweenCapturedPagesAreRelative`:**
    - `cairn.html`'s link to `fishing#bait` is `href="fishing.html#bait"`;
    - a link to an uncaptured page, or off the guide, stays absolute;
    - a `javascript:` link has no `href`.
  - **`ScriptsAndHandlersAreRemoved`:** a page whose content has a `script`, an `iframe`, an `onclick` and a `form` keeps none of them. The validator reports no unsupported references for them.
  - **`ALazyImageIsSavedLocally`** (Review Focus 2): with `ImageFiles` for `b.png`, the wiki page's lazy image becomes `images/2.png`, and that file is in the folder. The validator includes it as an asset.
  - **`AMissingImageIsAPlaceholder`:** with no file for `a.png`, the page has the "Image not saved" span, and `MissingImages` is 1.
  - **`TheHeaderNamesTheGuideAndItsSource`:** `guide.html`'s `header.dg-source` has the title, "By Example Author", "Saved from GameFAQs on 20 October 2026", and the source link.
  - **`ATextGuideIsItsTextUnchanged`:** the text FAQ gives `guide.txt` with a UTF-8 BOM, whose text equals `Content`. The validator gives a `TxtImportManifest` without asking for an encoding.
  - **`APartialCaptureLinksOnlyWhatItHas`:** with two of three pages, links to the missing page stay absolute.
- [ ] **Step 2–4:** as in Task 1, with the filter `OfflineCopyBuilder`.
- [ ] **Step 5: Commit:** "feat(p3): build an offline copy from captured pages".

### Task 5: Keeping an unfinished capture

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Online/CaptureStore.cs`
- Modify: `src/DesktopGuides.Infrastructure/InfrastructureJsonContext.cs` (the `capture.json` record)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Online/CaptureStoreTests.cs` (new)

**Interfaces:**
- Consumes: Tasks 1 and 4.
- Produces:
  - `public sealed class CaptureStore(string root, TimeProvider clock)`:
    - `CaptureDraft Begin(Guid gameId, GuidePageReading first)`;
    - `CaptureDraft? Find(Guid gameId, SourceSite site, string itemId)`;
    - `int Sweep()`, which deletes drafts not touched for 7 days and returns the count.
  - `public sealed class CaptureDraft`:
    - `Guid Id`, `Guid GameId`, `GuidePageMatch Item`;
    - `IReadOnlyList<GuidePageLink> Pages`, the guide's list, or the first page alone;
    - `IReadOnlyList<CapturedPage> Captured`, in the guide's order;
    - `IReadOnlyList<GuidePageLink> Remaining`;
    - `bool IsComplete`;
    - `string SaveImage(Uri url, ReadOnlySpan<byte> bytes)`, which returns the stored path;
    - `void Add(GuidePageReading page, IReadOnlyDictionary<Uri, string> imageFiles)`;
    - `void Discard()`.
  - `Add` and `SaveImage` write `capture.json` atomically, using `DiagnosticFile.Write`'s temp-then-move pattern. They touch its time.
  - A page list over 200 is cut to 200, and `IsComplete` is then false whatever is captured.

- [ ] **Step 1: Write the failing tests:**
  - `ADraftResumesAfterARestart`: `Begin` then `Add` one page, then make a new `CaptureStore` on the same root. `Find` returns the draft with one captured page and two remaining.
  - `ADraftForAnotherGameIsSeparate`.
  - `SweepRemovesOnlyStaleDrafts`: the clock moves 7 days and 1 minute ahead for one draft.
  - `ACorruptDraftIsIgnoredAndSwept`: a broken `capture.json` makes `Find` return null, and `Sweep` removes it.
  - `ALongPageListIsCutAt200`.
- [ ] **Step 2–4:** as in Task 1, with the filter `CaptureStore`.
- [ ] **Step 5: Commit:** "feat(p3): keep unfinished captures".

### Task 6: Saving to the library, and the records

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Online/OnlineGuideSaver.cs`
- Modify: `docs/p3-technical-design.md` (Delivery: PR 3 split into 3a and 3b; a "PR 3a verification record")
- Modify: `docs/progress.md`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Online/OnlineGuideSaverTests.cs` (new)

**Interfaces:**
- Consumes:
  - Tasks 3–5;
  - PR 2's `FindSourcedGuideAsync`, `PublishAsync(..., source)` and `ReplaceAsync`;
  - `GuideImportValidator`.
- Produces:
  - `public abstract record OnlineSaveResult`, with:
    - `Added(Guid GuideId)`;
    - `Replaced(Guid GuideId)`;
    - `UpToDate(Guid GuideId)`;
    - `NeedsFormatChange(Guid GuideId, GuideFormat Saved, GuideFormat New)`;
  - `OnlineGuideSaver(SqliteLibraryRepository repository, GuideImportPublisher publisher, GuideImportValidator validator, string workRoot, TimeProvider clock)`;
  - `Task<OnlineSaveResult> SaveAsync(CaptureDraft draft, bool allowFormatChange, IProgress<ImportProgress>? progress, CancellationToken token)`. The steps:
    1. Build the copy in a fresh folder under `workRoot`.
    2. Inspect it.
    3. Find the saved guide by `(draft.GameId, Site, ItemId)`.
    4. Replace it, or publish a new guide titled with the first page's `Title`.
    5. Use `GuideSource`:
       - `ItemId` and `Url` from the first page;
       - author, notice and `SourceUpdated` from the first page;
       - `SourceTitle` = the title;
       - `CapturedPages` and `TotalPages` when not `IsComplete`.
    6. Delete the work folder in all cases. On `Added`, `Replaced` or `UpToDate`, discard the draft.
  - `FormatChanged` from `ReplaceAsync` becomes `NeedsFormatChange`, and the draft is kept.

- [ ] **Step 1: Write the failing tests:**
  - `AFirstSaveAddsAGuideWithItsSource`: an HTML guide with three pages gives `Added`. The guide has three pages, and its source has no page counts. The draft is gone.
  - `ASecondSaveOfTheSameCopyIsUpToDate`.
  - **`ASecondSaveKeepsThePagePathsAndThePlace`** (Review Focus 5):
    1. Save; store a context place on `cairn.html`.
    2. Change `cairn`'s fixture text and save again: `Replaced`.
    3. The place decodes as `ContentChanged`, with `Context` first, against the new page list.
  - `APartialSaveRecordsItsPages`: two of three pages give `CapturedPages` 2 and `TotalPages` 3.
  - `ATextToHtmlChangeAsksFirst`: a saved TXT guide for the same item, then an HTML capture, gives `NeedsFormatChange`. The draft remains, and with `allowFormatChange` the result is `Replaced`.
  - **TR22.1:** `AFailedSaveLeavesTheLibraryAndTheDraft`. With a publisher whose staged-file factory throws, the library snapshot is unchanged, the draft remains, and the work folder is gone.
- [ ] **Step 2–4:** as in Task 1, with the filter `OnlineGuideSaver`, then both suites and `build`.
- [ ] **Step 5: Records.**
  - In the design's Delivery section, PR 3 becomes 3a, this plan, and 3b, as the deviation note above describes.
  - Add "PR 3a verification record" with the commits, the suite counts, the Review Focus tests by name, and the CI run.
  - Update the progress row: next is PR 3b.
- [ ] **Step 6: Commit:** "feat(p3): save captured guides to the library; PR 3a records".

## Research record

The fixtures' structure follows Wayback Machine captures (`id_` raw copies)
taken on 20 October 2026. No request went to the live sites.

- **GameFAQs text FAQs:**
  - `ps/197341-final-fantasy-vii/faqs/13639` (2025-03-08);
  - `…/faqs/2373` (2025-09-07, nine `faqspan` chunks).
- **GameFAQs HTML guides:**
  - slug pages: `ps5/289140-assassins-creed-valhalla/faqs/78736/cairn` (2025-11-13);
  - slug pages: `switch/189707-the-legend-of-zelda-breath-of-the-wild/faqs/74657/introduction` (2025-06-03);
  - `?page=N`: `…/faqs/74635?page=1` (2025-09-16, "Page 2 of 14").
- **Neoseeker text FAQs:**
  - `final-fantasy-vii/faqs/1733035-dynamixdj.html` (2024-02-11);
  - `chrono-trigger/faqs/260078-script.html` (2024-04-26), which has a canonical id other than its own.
- **Neoseeker walkthroughs:**
  - `final-fantasy-vii-rebirth/walkthrough/A_Golden_Key` (2024-04-26);
  - `elden-ring/walkthrough/Caria_Manor` (2025-02-16), which has lazy images and 55 walkthrough links among the wiki's navigation.
- **Gaps:**
  - only one `?page=` guide was seen;
  - text FAQs were never paginated in the sample;
  - the live markup may differ.

  PR 3b's live checks on pcsx2-win confirm or correct this.
