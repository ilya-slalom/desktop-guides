# P3 technical design: guides from supported online sources

Status: approved in conversation, 20 October 2026.
PR 2 (source fields and saving a guide again) in review (#146); see the
[PR 2 verification record](#pr-2-verification-record).
It covers S22 (T22.1, T22.2, TR22.1, TR22.2), the only story in P3.

Related documents:
- the [high-level design](initial-design.md), requirement 3;
- the [work breakdown](work-breakdown.md), which owns the story, task and
  TR IDs;
- the [T22.1 source review](p3/source-review.md), which records what each
  site's terms allow;
- P2 is complete, with v1.1.0 published (see the [P2 results](p2/results.md)).

## 1. Intent

A player who wants a walkthrough finds it on one of four sites today, and
reads it in a browser tab: online, with the site's layout, and with no
place kept. Desktop Guides already reads offline guides well, but getting
one in means saving files by hand and importing them.

P3 brings the guide in from the site, in as few steps as the site's terms
allow. The result is an ordinary guide in the library: offline, with its
place, bookmarks and completion, and credited to its author. When the
guide is updated online, saving it again replaces the copy and keeps the
reader's place.

## 2. Decisions (20 October 2026)

1. **Sources:** GameFAQs, Neoseeker, Steam Community Guides and VGMaps.
2. **How each is reached** follows the [source review](p3/source-review.md):

   | Source | Search in the app | Pasted URL | In-app browser and Save |
   |---|---|---|---|
   | GameFAQs | no | opens in the in-app browser | yes |
   | Neoseeker | no | opens in the in-app browser | yes |
   | Steam Community Guides | yes, with the user's Web API key | fetched by the app | yes |
   | VGMaps | yes, over the site's index | fetched by the app | yes |

3. **GameFAQs and Neoseeker:**
   - The user browses the real site in an in-app browser. Ads load and
     nothing is blocked.
   - **Save to library** captures the page being viewed. It then follows
     the guide's own pages, and only those, capturing each one and its
     images automatically.
   - The app never works around a Cloudflare check; the user solves it in
     the browser.
4. **Steam:** the user supplies their own Steam Web API key, entered and
   stored like the IGDB and SteamGridDB credentials.
5. **Entry point:** a **Find online** button on the game page, beside
   Import guide and Import maps. Saved guides and maps land in that game.
6. **Saving again replaces the existing copy where possible.** The guide
   keeps its ID, place, bookmarks and completion.
7. **The Microsoft Store question is deferred.** Whether the Store build
   includes GameFAQs and Neoseeker is decided at T17.5. P3 builds every
   source the sideloaded release includes.
8. **Restrictive notices:** when a guide's own notice forbids electronic
   copies, Save shows the notice and asks whether to save it for personal
   reading anyway or cancel.
9. **VGMaps ships without waiting** for its owner's reply. A courtesy note
   is optional and blocks nothing.

Calls made while writing this design, open to correction, are marked
"(design call)".

## 3. Goals, non-goals and anti-goals

- **Goals:**
  - Find, save and re-save guides from the four sources, from the game
    page.
  - A saved guide reads offline in the existing readers:
    - TXT for plain-text FAQs;
    - multi-page HTML for HTML guides;
    - image maps for VGMaps.
  - Each saved guide carries its source, URL, author, the author's notice
    and the date saved.
  - Network failures never damage the library (TR22.1).
  - Browsing online stays separate from reading offline (TR22.2).
- **Non-goals:**
  - Other sites. ZeldaDungeon.net and wikis can each come later with their
    own review.
  - Sharing, exporting or syncing saved guides beyond the existing local
    backup.
  - Background checks for updated guides. Re-saving is always the user's
    action.
  - Searching GameFAQs or Neoseeker from the app.
- **Anti-goals (a failure even if it works):**
  - Blocking or hiding ads in the in-app browser.
  - Getting around a challenge, a rate limit or robots.txt. This includes
    spoofing a browser for app-made requests, and replaying the in-app
    browser's cookies in a separate HTTP client.
  - Following links out of the saved guide, or crawling a site.
  - Losing or corrupting an existing guide, or its place, because a save
    or re-save failed.
  - Saved content that runs scripts or reaches the network when read.

## 4. Starting point

- **Imports:**
  - TXT, PDF, static HTML (single and multi-page) and image maps import
    through staging, validation and publication (T06, T07, T21.2).
  - Imported HTML is untrusted. The reader's WebView2 serves only managed
    files, from a synthetic origin, with networking dead (P1 technical
    design, T07.3).
  - Fingerprints and guide IDs exist so that a re-import can be told apart
    from an unchanged copy (TR06.3). Nothing replaces a guide yet.
- **Locators:** saved places restore `Exact`, by `Context`, or
  `Approximate`, and changed documents fall back visibly (T05, T12).
  Bookmarks use the same locators (T19.1).
- **Providers:** IGDB and SteamGridDB credentials live in
  `ProviderCredentialFile`, entered on the Settings provider card. Provider
  search is an explicit action: typing alone sends no requests (T04.4).

## 5. The design

### 5.1 Find online (Production)

**Find online** on the game page opens an online view for that game, as
its own screen. Closing it returns to the game page. It has:
- a source picker;
- for Steam and VGMaps, a search box pre-filled with the game's title.
  The search runs on Enter or the Search button, never while typing;
- one box that also accepts a pasted URL. A URL from one of the four
  sources selects that source. Any other URL is refused, with the list of
  supported sites;
- for GameFAQs and Neoseeker, the in-app browser, opened on the site's
  page for the game where the URL is known, else on its home page;
- **Save to library** when the current page is a guide the source's
  adapter recognises, and a progress line while saving. The progress line
  has page *n* of *m* and **Cancel**.

Steam search needs the game's Steam app ID (design call):
- IGDB already returns it for artwork. It's now stored on the game, and
  existing games get it on their next metadata refresh.
- A game without one asks once for its Steam store or guides URL, and
  keeps the ID it contains.

Steam search results list title, author, summary, preview image and
update time. Choosing one shows the guide in the in-app browser, with
**Save to library**. VGMaps results list the game's maps with area, size
and ripper. The user picks maps to save, or all of them.

The online view never opens a saved guide, and the reader never reaches
the network (TR22.2). A saved guide opens from the game page as any other
guide does.

### 5.2 The in-app browser (Production)

- **What it is:** a WebView2 in its own persistent profile under the
  cache root, apart from the reader's profiles. Normal browsing works:
  scripts, cookies, ads, challenges.
- **Navigation:** stays in the browser while it's on the chosen source's
  domains. Links elsewhere open in the system browser. Downloads and new
  windows are refused or sent to the system browser.
- **Permissions:** camera, microphone, location and notifications are
  denied. Web messages and host objects stay off.
- **Ads:** nothing is blocked. The user sees the site as the site serves it.
- **Settings:** "Clear online data" in Settings deletes the profile, which
  signs out of every source.

### 5.3 Capture (Production and Infrastructure)

A source adapter, one per source, behind a common interface, knows how to:
- tell whether a URL or a loaded page is a guide, and give the guide's
  stable identity, such as GameFAQs' FAQ ID or Steam's file ID;
- list the guide's own pages from the loaded page;
- find the guide's content in a page, and its title, author and notice;
- decide the format to save: TXT for a plain-text FAQ, multi-page HTML for
  an HTML guide, image maps for VGMaps.

**GameFAQs and Neoseeker** are captured through the in-app browser's own
session:
- The page being viewed is captured from its live DOM.
- Each further page of the guide is loaded in a hidden WebView2 on the
  same profile. Each is a normal page load, so ads, scripts and challenges
  behave as they would for the user.
- Images in the guide's content are taken from that page load's own
  responses, through WebView2's resource events, not fetched separately.
  So that every page, the first included, is captured the same way, the
  page being viewed is loaded again in the hidden WebView2 (design call).
  The hidden view scrolls through the page to trigger lazy-loaded images.
  An image still missing is loaded by navigating the hidden view to it, as
  a browser would. If that fails too, the copy shows a placeholder, and
  the save reports how many images are missing.
- Pages load one at a time, at most one every 2 seconds, with at most 200
  pages per guide.
- If a page shows a challenge, an error, or a 403 or 429 response, capture
  pauses. The user can open that page in the visible browser to solve it,
  then **Resume**. They can also **Save what's captured** or **Discard**.
- Only the guide's pages are captured: the adapter's page list for that
  guide's identity. No other link is followed.
- **Neoseeker** has two kinds of guide, and both are supported (design
  call):
  - text FAQs, owned by their authors;
  - wiki walkthroughs, owned by Neo Era Media. A walkthrough's own pages
    are the ones its contents navigation lists, and nothing else on the
    wiki.
- **Restrictive notices:** the adapter flags notice wording that forbids
  electronic copies or distribution. Save then shows the notice and asks
  before going on. A notice it can't classify is kept, with no prompt.

**Steam** guides are fetched by the app with an HTTP client:
- one guide page, plus the images in its content;
- metadata from `GetPublishedFileDetails`;
- a plain user agent naming Desktop Guides;
- one request at a time;
- a stop on HTTP 429.

**VGMaps:**
- **The index:** the app fetches `/Atlas/index.htm` when the user first
  searches, and keeps it for 30 days. **Refresh** fetches it again. Search
  runs locally.
- **System pages** are fetched only when the user opens a game in the
  results.
- **Map images** are fetched only for the maps the user picks, one at a
  time.
- **Formats:** PNG and JPEG save as they are. GIF is converted to PNG on
  save (design call), since image maps take PNG and JPEG only. SWF and
  other formats are listed as not saveable.

**The offline copy** is built from each page's guide content, not the
whole page:
- what's kept: the guide's text, its images and the author's notice, plus
  a header with the title, author, source, URL and date saved;
- what's dropped: site navigation, ads and scripts, which wouldn't work
  offline anyway. The copy is still ordinary static HTML;
- links between captured pages are rewritten to point at each other.
  Other links stay as external links, which the reader already opens in
  the system browser on request.

The result goes through the existing static HTML, TXT or image map
validation and staging unchanged: no scripts, local assets only, the size
limits. Captured HTML is as untrusted as any import.

### 5.4 Storing the source (Infrastructure)

A guide, or a map, gains optional source fields:
- the source;
- the guide's identity at the source;
- the canonical URL;
- the author;
- the author's notice, as captured;
- the time it was saved;
- the time it was last updated at the source, where the source gives one;
- the title at the source;
- for a partial save, the pages captured and the pages in the guide
  (design call).

The game also gains its Steam app ID (section 5.1).

This needs a schema migration. Backup and restore carry the fields; an
old backup restores with them empty.

The game page shows the source and author with each saved guide. Guide
details show the notice and a link to the original. Steam guides add
"Not affiliated with Valve" there. A partial save shows "pages 1–37 of 80"
in both places.

A Neoseeker text FAQ is credited to its author alone, as the site asks
(design call). Neoseeker appears only as the place it was saved from,
never as the owner, and the original link sits in guide details only.

### 5.5 Saving again (Infrastructure)

Saving a guide whose source and identity already exist in that game
replaces it:
- **If the new fingerprint matches,** nothing changes, and the status says
  the guide is up to date.
- **Otherwise,** the new copy is staged and validated in full, then
  published in one step. The guide's ID, place, bookmarks, completion,
  title and opened time stay. The managed files and source fields change.
  The library doesn't record whether a title was edited, so the title is
  always kept (design call); the source's current title is stored in the
  source fields.
- **A partial save** is replaced the same way. If the guide's capture is
  still in staging, Save offers to resume it first.
- **If anything fails** before publication, the existing guide is
  untouched, and its files and database rows stay as they were (TR22.1).
- **On the next open,** the place and bookmarks restore through the
  existing locator fallback: exact, by context, or approximate, with the
  existing visible notice when approximate.
- **If the format changes**, from TXT to HTML or the other way round, the
  place and bookmarks can't carry over. The save asks: replace the guide
  and lose its place, or keep both.

A VGMaps map saved again replaces the map with the same source URL in
that game.

### 5.6 Steam credentials (Production and Infrastructure)

- `ProviderCredentials` gains the Steam Web API key. It's entered, tested,
  saved and cleared on the Settings provider card, as the other keys are.
- The test calls `QueryFiles` once.
- The key is never logged and never sent anywhere except Steam's API.
- Without a key, Steam search explains where to get one and links to
  Settings. Pasted Steam URLs still work, since `GetPublishedFileDetails`
  needs no key.

### 5.7 Build switches

- Each source can be left out of a build by a build property, one per
  source. All four are on by default.
- A source that's off doesn't appear in the source picker. Its pasted URLs
  get the same refusal as any unsupported site.
- Guides already saved from it still read normally.

This keeps T17.5's Store decision a build setting, not a code change.

### 5.8 Failures and recovery (TR22.1)

- **What a failure is:** no connection, a timeout, a refusal (403, 429 or a
  challenge), an unrecognised page layout, oversized content, or a full
  disk.
- **Where a capture lives:** in a staging folder of its own until it
  publishes.
  - Closing the app leaves it in staging.
  - The next Find online for that game offers **Resume**, **Save what's
    captured** or **Discard**.
  - Startup removes captures older than 7 days.
- **What the user sees:** each failure names the step and offers one
  action: Retry, Open in browser, or Discard.
- **What it never touches:** the library's files and database.

### 5.9 Tests

- **No CI test reaches a live site.** Adapters take a base URL. A loopback
  fixture server serves synthetic pages, written for the tests, for each
  source's layouts:
  - a multi-page HTML guide;
  - a text FAQ;
  - a challenge page;
  - a 429 response;
  - a Steam guide, with API responses;
  - a VGMaps index and system page.

  No copied guide text goes in fixtures.
- **Unit tests:**
  - each adapter: recognising guides, listing pages, extracting content,
    choosing the format;
  - building the offline copy: rewritten links, dropped scripts and site
    chrome, the header and notice;
  - re-save: unchanged, changed, format change;
  - failure at every step of a capture and a re-save, with the library
    byte-for-byte unchanged;
  - the build switches;
  - the credential round trip.
- **Installed E2E** (a new `online` group, against the fixture server):
  - save a multi-page guide through the in-app browser;
  - pause on a challenge, then resume;
  - re-save, keeping the place;
  - a Steam search and save with a fake key;
  - VGMaps maps;
  - reading the saved guide with networking off.
- **Live checks:** done by hand on pcsx2-win with real guides from each
  source, before each release that changes an adapter, and recorded in
  the P3 results. Sites change their layouts, so a live check that fails
  is a finding to triage, not a CI failure.

## 6. Delivery

PRs in order, each with its own plan:

1. **T22.1:** the [source review](p3/source-review.md) and this design.
2. **Source fields and re-save** (Infrastructure): the migration, backup
   and restore, replacement with place kept, failure safety.
3. **Find online and the in-app browser**, with GameFAQs and Neoseeker
   capture, the fixture server and the `online` E2E group.
4. **Steam:** credentials, search, pasted URLs.
5. **VGMaps:** the index, search, map saving into image maps.
6. **Records:** results, live checks, the tested matrix, release notes.

## 7. Left to the builder

- The adapter interface, and how capture is split between Production and
  Infrastructure, within the rules above.
- The exact layout of the online view, within the game-page entry point
  and the parts listed in 5.1.
- How each adapter recognises guide pages and content, provided it fails
  safely when a site's layout changes.
- Capture timing below the stated limits, and the progress wording.

## 8. Traceability

| ID | Where |
|---|---|
| T22.1 | [source review](p3/source-review.md); section 2 |
| T22.2 | sections 5.1–5.7 |
| TR22.1 | sections 5.5 and 5.8; failure tests in 5.9 |
| TR22.2 | sections 5.1 and 5.2; the offline-read E2E in 5.9 |

## PR 2 verification record

[#146](https://github.com/ilya-slalom/desktop-guides/pull/146), following
[the PR 2 plan](p3/s22-source-fields-plan.md), checked on 20 October 2026:

- **Commits:**
  - `4fd38a2`: the Core source record;
  - `2479bd3`: schema 9 and source rows;
  - `05c1cf7`: saving again in place;
  - `9fd123d`: failure safety and startup recovery;
  - `347422e`: IGDB's Steam app ID;
  - `a4fe8c9`: the review fix, which refuses a replacement while another operation claims the guide.
- **Suites on pcsx2-win:** Core.Tests 1522/1522, Infrastructure.Tests 949/949. The Release x64 build adds no new warnings.
- **The plan's Review Focus tests:**
  1. `AnInterruptedReplacementRollsBackOnTheNextStart`, over six checkpoints, with `ACrashAfterTheCommitKeepsTheNewCopy` for the two after the commit;
  2. `APlaceOnARemovedPageDoesNotFailTheOpen`;
  3. `AVersionEightBackupRestores`;
  4. `RemovingAGuideOrGameRemovesItsSourceRows`;
  5. `TheSameItemInAnotherGameIsNotReplaced`.
- **Review:** a fresh whole-branch review found one Important issue. A second replacement while a first one's clean-up was still pending would leave two operations claiming the guide, and startup would then refuse the library. It's fixed and pinned by `ALeftoverOperationOnTheGuideRefusesASecondReplacement`. Six Minor findings are deferred, mostly test strength.
- **CI:** run [39399887408](https://github.com/ilya-slalom/desktop-guides/actions/runs/39399887408) passed every job, including all installed groups.
- **For PR 3:** an HTML place survives a re-save only if its page keeps the same document path, so captured pages need stable paths across saves.
