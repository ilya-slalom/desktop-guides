# S22 PR 4 (Steam Community Guides) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In Find online, a reader picks **Steam** and can:
1. search the game's Steam Community guides with their own Steam Web API key;
2. or paste a guide's URL, which works without a key;
3. preview the guide in the in-app browser;
4. press **Save to library**.

The app then fetches the guide page and its images itself, and saves the guide to the game as an HTML guide. Saving again replaces the copy it saved before.

**Architecture:**
- **Core:**
  - `OnlineSources` gains Steam, behind `IncludeSteam`.
  - `SteamUrls` reads guide IDs and app IDs from URLs.
  - `ProviderCredentials` gains the Steam key.
- **Infrastructure:**
  - `SteamClient`: the three Web API calls, through `ProviderHttp`.
  - `SteamGuideAdapter`: reads a guide page.
  - `SteamGuideFetcher`: fetches the page, its details and images into a `CaptureDraft`. The existing `OnlineGuideSaver` saves that draft, so the offline copy, staging, replacement and failure safety are PR 3's.
  - `OnlineFixtureHandler`: answers the app's own HTTP requests from the online fixture manifest in test runs.
- **Production:**
  - Find online's Steam mode: the search box, the results list, the app ID prompt, and Save through the fetcher.
  - The Steam key on the Settings provider card.
  - "Not affiliated with Valve" in guide details.

**Tech Stack:** .NET 10, WinUI 3, WebView2, AngleSharp, xUnit, PowerShell UI Automation.

**Spec:** [the P3 technical design](../p3-technical-design.md), sections:
- 5.1, Find online: Steam search, the app ID and the results;
- 5.3, Steam fetched by the app;
- 5.4, "Not affiliated with Valve";
- 5.6, Steam credentials;
- 5.7, build switches;
- 5.9, tests.

Also [the source review](source-review.md), whose Steam section sets the terms and attribution.

**Markup:** recorded on 24 October 2026 from one public guide page and one `GetPublishedFileDetails` reply; nothing from them is copied into fixtures.
- the title is `div.workshopItemTitle`;
- the author is `div.guideAuthors`, reading "By {name}";
- the summary is `div.guideTopDescription`;
- the body is `div.guide.subSections`, in which each `div.subSection` holds a `div.subSectionTitle` and a `div.subSectionDesc`;
- images live on `images.steamusercontent.com`, and each sits in an `a.modalContentLink` that links to the full image.

## Global Constraints

- **Requests:**
  - The app's Steam requests go to `api.steampowered.com`, `steamcommunity.com` and `images.steamusercontent.com` only. They're added to `ProviderHttp.AllowedHosts`.
  - One request at a time; a fetch of a guide waits at least 1 s between requests.
  - The user agent is `DesktopGuides/{version} (+https://github.com/ilya-slalom/desktop-guides)`.
  - An HTTP 429 stops the search or save, with "Steam is limiting requests. Try again in a few minutes." Nothing is retried automatically.
- **The key:**
  - It's sent only to `api.steampowered.com`, as the `key` query parameter that Steam requires.
  - It never appears in logs, `ToString`, diagnostics, `unmatched.json` or exceptions. `OnlineFixtureHandler` records unmatched requests by host and path only.
- **Without a key:**
  - Search shows "Steam search needs your Steam Web API key." with an **Open Settings** link and a link to `https://steamcommunity.com/dev/apikey`.
  - Pasted guide URLs still work, since `GetPublishedFileDetails` needs no key.
- **Attribution:**
  - The saved copy's header names the author and links the guide.
  - Steam guides' guide details add "Not affiliated with Valve."
  - The source is `SourceSite.Steam`, and the item ID is the published file ID.
- **The in-app browser:** with Steam selected, it may show `steamcommunity.com` pages. Save on a guide page uses the app's own fetch, not browser capture. Other Steam pages get "This page isn't a guide Desktop Guides can save."
- **The game's app ID:**
  - Search needs it. A game without one asks once for its Steam store or guides URL, and keeps the ID that URL contains.
  - A guide pasted from another app saves to the current game; its app ID isn't stored.
- **Limits:**
  - the guide page is at most 4 MB;
  - each image is at most `ProviderHttp.MaxImageBytes`;
  - a guide has at most 200 images;
  - an image that fails becomes the existing placeholder.

## Review Focus

1. A 429, a timeout or a broken page in the middle of a save leaves the library unchanged. Saving again works, and the status names the step.
2. A wrong or revoked key gets 401 or 403 from Steam. That shows "Steam didn't accept the key. Check it in Settings." and never echoes the key.
3. A guide whose page has no `div.subSection`, because the layout changed or the guide is hidden, says it can't be read and saves nothing.
4. Saving a guide again by the same file ID replaces it and keeps its place. An unchanged guide is up to date.
5. Searching with no results, or for a game with no guides, says so, and the search box keeps its text.

## Host commands

`/tmp/s22d.sh <project> [filter]` syncs the branch to the host and runs a
test project. Installed runs use CI, or the scheduled-task route in
[p1/e2e-testing.md](../p1/e2e-testing.md).

### Task 1: Core: the source, URLs and the key

**Files:**
- Modify:
  - `Directory.Build.props` and `Directory.Build.targets`: `IncludeSteam`, which defines `SOURCE_STEAM`;
  - `src/DesktopGuides.Core/Online/OnlineSources.cs`;
  - `src/DesktopGuides.Core/Providers/ProviderContracts.cs`.
- Create: `src/DesktopGuides.Core/Online/SteamUrls.cs`.
- Test: `tests/DesktopGuides.Core.Tests/Online/SteamUrlsTests.cs` and `OnlineSourcesTests.cs`.

**Interfaces:**
- Produces:
  - `OnlineSource(SourceSite.Steam, "Steam", ["steamcommunity.com"], https://steamcommunity.com/)`;
  - `SteamUrls.GuideId(Uri) → string?`;
  - `SteamUrls.AppId(Uri) → string?`;
  - `SteamUrls.Guide(string fileId) → Uri`;
  - `ProviderCredentials(IgdbCredentials? Igdb, string? SteamGridDbKey, string? SteamWebApiKey = null)`.

- [ ] **Step 1: Write the failing tests:**
  - **`GuideId`** reads the file ID from:
    - `https://steamcommunity.com/sharedfiles/filedetails/?id=3285627728`;
    - the same with `&searchtext=x` added;
    - `http://` and upper-case hosts.
  - **`GuideId`** returns null for:
    - `/workshop/filedetails/?id=1`;
    - `?id=abc`;
    - `?id=`;
    - an ID of 21 or more digits;
    - another host.
  - **`AppId`** reads the app ID from:
    - `https://store.steampowered.com/app/620/Portal_2/`;
    - `https://steamcommunity.com/app/620/guides/`;
    - `https://steamcommunity.com/app/620`.
  - **`AppId`** returns null for `/app/abc/` and for other hosts.
  - **`OnlineSources`** lists GameFAQs, Neoseeker, then Steam, and `Find` matches `steamcommunity.com`.
  - **`ProviderCredentials.ToString()`** shows `SteamWebApiKey = saved` and never the value.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run Core tests; they pass.**
- [ ] **Step 5: Commit** `feat(p3): Steam as a source, its URLs and the key's place in the credentials`.

### Task 2: Infrastructure: the client, the adapter and the fetch

**Files:**
- Create in `src/DesktopGuides.Infrastructure/Online/`:
  - `SteamClient.cs`;
  - `SteamGuideAdapter.cs`;
  - `SteamGuideFetcher.cs`;
  - `OnlineFixtureHandler.cs`;
  - `Styles/steam.css`.
- Modify:
  - `src/DesktopGuides.Infrastructure/Providers/ProviderHttp.cs`: the hosts;
  - `ProviderCredentialBlob.cs` and `ProviderCredentialFile.cs`: the key, written as a new blob version that still reads the old one;
  - `OfflineCopyBuilder.cs`: Steam's header line and sheet.
- Fixtures: `tests/fixtures/p3/steam/`:
  - `guide.html`, synthetic, with three sections, two images, a `bb_h1` heading and an external link;
  - `guide-no-sections.html`;
  - `details.json`;
  - `query.json`;
  - `query-empty.json`;
  - `players.json`.
- Test: `tests/DesktopGuides.Infrastructure.Tests/Online/`:
  - `SteamClientTests.cs`;
  - `SteamGuideAdapterTests.cs`;
  - `SteamGuideFetcherTests.cs`;
  - `OnlineFixtureHandlerTests.cs`;
  - plus credential round-trip tests in the existing provider tests.

**Interfaces:**
- **`SteamClient(ProviderHttp http, Func<string?> key)`:**
  - `QueryGuidesAsync(string appId, string text, int page, CancellationToken) → SteamGuidePage(IReadOnlyList<SteamGuideSummary> Items, int Total)`;
  - `GetDetailsAsync(string fileId, CancellationToken) → SteamGuideSummary?`;
  - `TestKeyAsync(CancellationToken) → bool`, which makes one `QueryFiles` call.
  - Errors are `SteamException(SteamFailure Kind)`, where `Kind` is `NoKey`, `KeyRejected`, `RateLimited`, `Unavailable` or `Unreadable`.
- **`SteamGuideSummary`:** `(FileId, AppId, Title, Author?, Summary, PreviewUrl?, Updated)`.
  - The author's name comes from one `ISteamUser/GetPlayerSummaries` call per results page, when a key is set. Otherwise it's null.
- **`SteamGuideAdapter : IGuideSourceAdapter`:**
  - `Match` takes guide URLs;
  - `Read` returns one HTML page:
    - an `h2` for each section title, then the section's content;
    - images made absolute, with each `modalContentLink` unwrapped to its full image;
    - the summary as an intro paragraph;
    - the author taken from `div.guideAuthors`, without "By ".
- **`SteamGuideFetcher(SteamClient, ProviderHttp, CaptureStore, TimeProvider)`:**
  - `FetchAsync(Guid gameId, string fileId, IProgress<(int, int)>?, CancellationToken) → CaptureDraft`;
  - it throws `SteamException` on failure, and the draft is discarded.

- [ ] **Step 1: Write the failing tests.** The plan's Review Focus 1–5 are pinned here.
  - **Client:**
    - the query string carries `key`, `appid`, `search_text`, `filetype=10`, `return_details=true` and `numperpage=20`;
    - 401 and 403 give `KeyRejected`, 429 gives `RateLimited`, and with no key the result is `NoKey`, sent without a request;
    - the key appears in no exception message or `ToString`;
    - author names are joined from `players.json`.
  - **Adapter:**
    - the fixture reads its title, author, three `h2` sections, and two full-size image URLs;
    - external links stay absolute;
    - `guide-no-sections.html` reads as null.
  - **Fetcher:**
    - the page, then each image, at least 1 s apart, on a fake clock;
    - a 429 on the second image throws `RateLimited`, leaves no draft, and leaves the library untouched;
    - a page over 4 MB gives `Unreadable`.
  - **End to end:** saving through `OnlineGuideSaver`:
    - adds an HTML guide whose source is `(Steam, fileId)`;
    - shows "Not affiliated with Valve" in the copy header;
    - is up to date the second time, and replaced when a section changes.
  - **Credentials:** a v1 blob without the Steam key still reads, and the key round-trips.
  - **`OnlineFixtureHandler`:**
    - it answers a manifest route;
    - an unmatched request gets 599, logged as host and path with no query.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.** `steam.css` styles `h2` sections and the `bb_*` classes Steam's BBCode produces (`bb_h1`–`bb_h3`, `bb_ul`, `bb_table`, `bb_code`, `bb_spoiler`), in the Fluent direction of the PR 3b fixes.
- [ ] **Step 4: Run Infrastructure tests; they pass.**
- [ ] **Step 5: Commit** `feat(p3): Steam guides fetched and saved by the app`.

### Task 3: Production: the key in Settings and Steam in Find online

**Files:**
- Modify:
  - `ProviderSettingsCard.xaml(.cs)`;
  - `Providers/ProviderServices.cs`;
  - `ShellWindow.xaml`: the Steam panel in `OnlinePanel`;
  - `ShellWindow.Online.cs`;
  - the guide details view: the Valve notice;
  - `OnlineBrowser.cs`: test runs send app HTTP through `OnlineFixtureHandler`.
- Create: `ShellWindow.Steam.cs`.

**Visual direction:** ui-ux-pro-max at plan stage, within the app's existing Fluent styles.
- **Search:** a plain `TextBox` with a **Search** button. It runs on Enter only, with no suggestions while typing, as design 5.1 says.
- **Results:** a `ListView` whose rows show a 64 px preview, title, "By {author} · updated {date}", and a two-line summary. Rows are keyboard-selectable, with the row name as the accessible name.
- **The app ID prompt:** an `InfoBar` with a `TextBox` and **Use this game**.
- **No key:** an `InfoBar` carrying both links.
- **Saving:** progress uses the existing progress line, showing "image n of m".

- [ ] **Step 1: Write the failing installed checks** in the `online` group. Both use a seeded game.
  - `online-steam-search`, with a fake key from the fixture credentials file:
    1. pick Steam; the search box holds the game title;
    2. Enter shows two results from `query.json`;
    3. choosing the first opens it in the browser, from the fixture page;
    4. Save to library shows "Saved … to {game}.";
    5. the guide opens in the reader;
    6. guide details show "Not affiliated with Valve.";
    7. searching for "zzz" shows "No Steam guides match."
  - `online-steam-nokey`, with credentials cleared:
    1. Steam search shows the no-key bar;
    2. a pasted guide URL opens the guide and saves it;
    3. for a game without an app ID, the prompt takes `https://store.steampowered.com/app/620/x/`;
    4. after that, the no-key bar shows rather than the prompt.
  - `online-steam-limit`: the fixture answers 429 on an image during Save. The status names the limit, and `describe-online` shows no new guide.
- [ ] **Step 2: Run the `online` group in CI; the new modes fail.**
- [ ] **Step 3: Implement.**
  - The source picker shows Steam's panel in place of the browser until a guide is chosen.
  - `GoToOnlineAddress` routes a Steam guide URL to the browser on Steam.
  - Save on a Steam guide page calls the fetcher and then `SaveDraftAsync`, reusing the notice, format and keep-both dialogs.
  - The Settings card adds a "Steam Web API key" row with Test, Save and Clear, as SteamGridDB's.
- [ ] **Step 4: Run the full installed set in CI; it passes.** Take screenshots of search results, the no-key bar, and guide details with the Valve notice.
- [ ] **Step 5: Commit** `feat(p3): Steam in Find online, and its key in Settings`.

### Task 4: Records

- [ ] **Step 1:** Update the docs:
  - in `docs/p1/e2e-testing.md`, the Steam rows of the `online` group;
  - in the P3 design, a PR 4 verification record, plus PR 3b's, which was still pending;
  - in `source-review.md`, the date of the markup check.
- [ ] **Step 2:** Write the PR description: the target task (T22.2, PR 4), prerequisites (#148 and #149, merged), the intended outcome, the screenshots, and the live check for the user, with steps for a real key and a real guide.
