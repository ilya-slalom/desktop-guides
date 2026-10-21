# S22 PR 3b (Find online: the screen, the in-app browser and capture) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A reader opens **Find online** from a game page, browses
GameFAQs or Neoseeker in an in-app browser with the sites' ads intact,
and presses **Save to library** on a guide. The app captures the guide's
own pages and images, one at a time, through the same browser session.
It pauses when a site refuses, and saves the guide to the game, or
replaces the copy saved before. An unfinished capture can be resumed,
saved as it is, or discarded.

**Architecture:**
- **Core:**
  - `OnlineRoute(Guid GameId)` in `ShellNavigator`;
  - `OnlineSources`: the list of sources, start URLs, which source a URL belongs to, and the build switches.
- **Infrastructure:**
  - `GuideCapture`, the capture loop, behind an `IPageLoader`, so pacing, pausing, missing images and cancellation are unit-tested without WebView2;
  - `OnlineFixtureRoutes`, the fixture manifest for test runs.
- **Production:**
  - `OnlineBrowser`, the visible WebView2 in a persistent profile;
  - `WebViewPageLoader`, which implements `IPageLoader` by stepping the visible browser itself through the guide's pages. It was first a hidden second WebView2; see the deviation note below;
  - `ShellWindow.Online.cs`, the screen and the save flow.

**Tech Stack:** .NET 10, WinUI 3, WebView2 1.0.4191.47 (pinned), xUnit,
PowerShell UI Automation.

**Spec:** [the P3 technical design](../p3-technical-design.md), sections
5.1–5.3, 5.7–5.9, built on PR 3a's [capture core](s22-capture-core-plan.md).

**Deviation, 21 October 2026, by the user's choice:** capture runs in the
visible browser, with no hidden second WebView2. In the installed runs the
hidden controller's navigations stalled now and then, and the app crashed,
and four targeted fixes didn't end it. The "hidden browser" wording below
describes the plan as written; the design's section 5.3 has the change.

**Visual direction** (ui-ux-pro-max, plan stage):
- the app's existing Fluent styles; no new styles;
- one primary action per state (Save to library, as an accent button);
- progress as text with a step count, plus a `ProgressBar`;
- pauses and errors in a `Severity="Warning"` or `"Error"` `InfoBar`, which Narrator announces, each with its recovery actions;
- the restrictive-notice prompt as a `ContentDialog` whose default button is Cancel;
- no state shown by colour alone.

## Global Constraints

- **Ads and challenges are untouched.**
  - The in-app browser blocks nothing, never filters ads, and never solves or skips a challenge.
  - The hidden browser loads pages as a browser would: scripts and ads run.
  - The app makes no HTTP request of its own to GameFAQs or Neoseeker.
- **The profile:**
  - `{cacheRoot}\Online\Profile`, a name that isn't a GUID, so `WebView2ProfileSweeper` leaves it;
  - one `CoreWebView2Environment` for both browsers, created without the reader's dead-proxy arguments;
  - **Clear online data** in Settings deletes the folder, after closing both browsers.
- **Navigation:**
  - the browser stays on the chosen source's hosts (`gamefaqs.gamespot.com`, `www.neoseeker.com`, and their `https` subresources);
  - a top-level navigation to any other host is cancelled and opened through `ExternalLinkLaunchers`;
  - new windows go to the system browser;
  - downloads, permissions and external URI schemes are refused, using the reader's handlers;
  - `IsWebMessageEnabled` and `AreHostObjectsAllowed` are false;
  - DevTools are off in the public lane.
- **Capture pacing:**
  - one page at a time;
  - at least 2 s from the start of one page load to the start of the next;
  - a page load times out after 30 s.
- **A page counts as refused** when it:
  - returns HTTP 403, 429 or 503;
  - times out;
  - or loads but its adapter `Read` returns null, which covers a challenge page or a changed layout.

  Capture then pauses at that page.
- **Images:**
  - taken from the hidden page load's own responses (`WebResourceResponseReceived`, `GetContentAsync`), for URLs in the reading's `Images`;
  - after the load, the page is scrolled in four steps, 250 ms apart, to trigger lazy images;
  - an image still missing is loaded by navigating the hidden view to it, with the same 2 s pacing;
  - after that it stays missing (a placeholder).
- **Copy:**
  - the button: "Find online";
  - the screen title: "Find online: {game}";
  - the source picker's items: "GameFAQs", "Neoseeker";
  - the address box: header "Address or guide URL", placeholder "Paste a guide URL";
  - Save: "Save to library", disabled with the helper text "This page isn't a guide Desktop Guides can save." when the adapter doesn't read it;
  - a refused URL: "Desktop Guides can save guides from GameFAQs and Neoseeker. Open other sites in your browser.";
  - progress: "Saving page {n} of {m}…", with Cancel;
  - the paused `InfoBar`:
    - title "Saving paused";
    - message "{site} didn't return page {n} of {m}. It may be asking you to confirm you're a person.";
    - actions: Show page, Resume, Save what's captured, Discard;
  - the restrictive-notice dialog:
    - title "This guide's notice limits copies";
    - the notice in a scrollable text block;
    - primary "Save for personal reading", close "Cancel" (the default);
  - the format-change dialog:
    - title "Replace the saved guide?";
    - body: the `FormatChanged` message from PR 2;
    - primary "Replace", secondary "Keep both", close "Cancel";
  - done: "Saved {title} to {game}.", "{title} is up to date." or "Replaced {title}. Your place is kept.", each with the action "Open guide";
  - a resumable draft on opening: an informational `InfoBar`, "An unfinished save of {title} is waiting ({captured} of {total} pages).", with Resume, Save what's captured, Discard;
  - Settings: the card "Online data", the button "Clear online data", and the confirmation "Sign out of every site and delete cookies the in-app browser stored?".
- **Keyboard:**
  - Back, Alt+Left or Esc (when focus isn't in the browser) returns to the game page;
  - Ctrl+L focuses the address box;
  - Ctrl+S runs Save to library when it's enabled;
  - on opening, focus goes to the address box.
- **Build switches:**
  - MSBuild properties `IncludeGameFaqs` and `IncludeNeoseeker`, default `true`, set in `Directory.Build.props`;
  - each adds `SOURCE_GAMEFAQS` or `SOURCE_NEOSEEKER` to `DefineConstants` in every project;
  - `OnlineSources.Available` is built from them.
- **Test fixtures:**
  - in the preview lane only (`#if !PUBLIC_LANE`), and only while the named test gate `OnlineFixtures` is open;
  - both browsers answer every request from `{data}\test\online\manifest.json` through `WebResourceRequested`, as `FixtureProviderHandler` does for providers;
  - unmatched requests get 599;
  - the public lane compiles none of it.
- **Live sites:** no automated test, script or harness run loads GameFAQs or Neoseeker. Live checks are done by hand, by the user, on pcsx2-win (Task 6).
- **Shell rules:** no `cd` in local shell commands. PowerShell stays ASCII-only and runs under 5.1 and 7.

## Review Focus

1. **A challenge on page 2 of 3** pauses at page 2. Show page displays it in the visible browser; after it's solved there, Resume captures pages 2 and 3, and the save is complete. Pinned by Task 2's `ARefusedPagePausesAndResumesThere` and Task 5's `online-pause` mode.
2. **Leaving the screen mid-capture** (Back, closing the window) cancels cleanly. The draft keeps the pages captured so far and is offered next time. Pinned by Task 2's `CancellingKeepsWhatWasCaptured` and Task 5's `online-resume`.
3. **A link off the source** (an ad, a forum link, another site) opens in the system browser and never in the in-app browser. Pinned by Task 5's `online-browse`.
4. **The saved guide reads with networking off.** Pinned by Task 5's `online-save`, which opens it in the reader. The reader's dead proxy and request filter apply, as for any HTML guide.
5. **A text FAQ** saves as a TXT guide and opens in the TXT reader. Pinned by Task 5's `online-save` (text FAQ step).

## Host commands

As in the [PR 2 plan](s22-source-fields-plan.md#host-commands), with the
staging folder `E:\work\desktop-guides\s22d` and the logs `E:\work\s22d-*.log`.
Installed runs:
`gh workflow run windows-ci.yml --ref feat/p3-s22-find-online -f shell-scope=online`.

---
### Task 1: Core: the route, the sources and the build switches

**Files:**
- Modify: `src/DesktopGuides.Core/Navigation/ShellNavigator.cs` (`OnlineRoute`, `OpenOnline`)
- Modify: `src/DesktopGuides.Core/Navigation/ShortcutRouting.cs` (Ctrl+L, Ctrl+S on the online route)
- Create: `src/DesktopGuides.Core/Online/OnlineSources.cs`
- Modify: `Directory.Build.props` (the two properties and their constants)
- Test: `tests/DesktopGuides.Core.Tests/ShellNavigatorTests.cs`, `ShortcutRoutingTests.cs` (or the file that tests it), `Online/OnlineSourcesTests.cs` (new)

**Interfaces:**
- Produces:
  - `public sealed record OnlineRoute(Guid GameId) : ShellRoute`;
  - `ShellNavigator.OpenOnline(Guid gameId)`. It pushes onto the game route, and Back returns to the game;
  - `public sealed record OnlineSource(SourceSite Site, string Name, IReadOnlyList<string> Hosts, Uri Home)`;
  - `OnlineSources.Available`, an `IReadOnlyList<OnlineSource>` holding GameFAQs, then Neoseeker, filtered by the build constants;
  - `OnlineSources.Find(Uri url) -> OnlineSource?`, true for `https` on one of a source's hosts;
  - `OnlineSources.StartUrl(OnlineSource source, string gameTitle) -> Uri`:
    - GameFAQs: `https://gamefaqs.gamespot.com/search?game={escaped title}`. This is a page a person opens; robots.txt is about crawlers, and the app never fetches it;
    - Neoseeker: `https://www.neoseeker.com/`.
  - new shortcut commands, `FocusAddress` and `SaveOnline`, valid only on `OnlineRoute`.

- [ ] **Step 1: Write the failing tests:**
  - `OpeningOnlineFromAGameReturnsToItOnBack`.
  - `OnlineSourcesListGameFaqsThenNeoseeker`.
  - `FindMatchesASourcesHostsOverHttpsOnly`: `https://gamefaqs.gamespot.com/x` gives GameFAQs; `http://…`, `https://example.com` and `https://neoseeker.com.evil.test` give null.
  - `TheGameFaqsStartUrlSearchesForTheTitle`: "Final Fantasy VII" gives `…/search?game=Final%20Fantasy%20VII`.
  - `CtrlLAndCtrlSRouteOnlyOnTheOnlineScreen`.
- [ ] **Step 2: Run them.** `t DesktopGuides.Core.Tests`. Expected: build errors, then FAIL.
- [ ] **Step 3: Implement** the Interfaces block. Any other `switch` over `ShellRoute` that must handle every case gets `OnlineRoute`.
- [ ] **Step 4: Run them.** Expected: PASS, the whole suite.
- [ ] **Step 5: Commit:** "feat(p3): the online route and sources".

### Task 2: Infrastructure: the capture loop and the fixture routes

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Online/GuideCapture.cs`
- Create: `src/DesktopGuides.Infrastructure/Online/OnlineFixtureRoutes.cs`
- Modify: `src/DesktopGuides.Infrastructure/Online/CaptureStore.cs` (`ListForGame`)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Online/GuideCaptureTests.cs`, `OnlineFixtureRoutesTests.cs`, `CaptureStoreTests.cs`

**Interfaces:**
- Consumes: PR 3a's adapters, `CaptureStore` and `CaptureDraft`.
- Produces:
  - `public sealed record PageLoad(int Status, string? Html, IReadOnlyDictionary<Uri, byte[]> Images)`. `Status` is 0 for a timeout;
  - `public interface IPageLoader`:
    - `Task<PageLoad> LoadPageAsync(Uri url, IReadOnlySet<Uri> wantedImages, CancellationToken token)`;
    - `Task<byte[]?> LoadImageAsync(Uri url, CancellationToken token)`;
  - `public abstract record CaptureOutcome`, with:
    - `Complete(CaptureDraft Draft)`;
    - `Paused(CaptureDraft Draft, GuidePageLink Page, int Number, int Total, int Status)`;
  - `GuideCapture(IGuideSourceAdapter adapter, IPageLoader loader, TimeProvider clock)` with `Task<CaptureOutcome> RunAsync(CaptureDraft draft, IProgress<(int Done, int Total)>? progress, CancellationToken token)`. It:
    - captures `draft.Remaining` in order;
    - for each page: loads it, `Read`s it, saves its images into the draft, then loads any wanted image still missing;
    - waits for the pacing (Global Constraints) through `clock`;
    - pauses on a refusal;
    - on cancellation throws `OperationCanceledException`, after keeping every page already added;
  - `CaptureStore.ListForGame(Guid gameId) -> IReadOnlyList<CaptureDraft>`, newest first;
  - `OnlineFixtureRoutes.Load(string manifestPath)`, with `TryAnswer(Uri url, out int status, out string contentType, out string? filePath)`. The manifest's routes are `{ "url": "…", "status": 200, "file": "gamefaqs/html-slug-cairn.html", "contentType": "text/html" }`, and the file paths are relative to the manifest.

- [ ] **Step 1: Write the failing tests** with a fake `IPageLoader` that serves PR 3a's fixtures by URL, and a fake clock:
  - `CapturesEveryPageAndItsImages`: the three slug pages, plus one image, complete.
  - **Review Focus 1:** `ARefusedPagePausesAndResumesThere`.
    1. Page 2 returns 429 the first time.
    2. The outcome is `Paused` at page 2 of 3, with 1 page captured.
    3. A second `RunAsync` on the same draft completes.
  - `AChallengePageIsARefusal`: a 200 whose HTML the adapter can't read pauses with status 200.
  - `PagesArePacedTwoSecondsApart`: the fake clock records the load start times, which are ≥ 2 s apart.
  - `AMissingImageIsLoadedDirectlyOnce`: an image absent from the page load is asked for once through `LoadImageAsync`.
  - **Review Focus 2:** `CancellingKeepsWhatWasCaptured`: cancel during page 3; page 1 and 2 stay in the draft.
  - `FixtureRoutesAnswerKnownUrlsAnd599Otherwise`.
  - `ListForGameReturnsItsDraftsNewestFirst`.
- [ ] **Step 2–4:** as before, with the filter `GuideCapture|OnlineFixtureRoutes|CaptureStore`, then the whole suite.
- [ ] **Step 5: Commit:** "feat(p3): the capture loop".

### Task 3: Production: the browsers

**Files:**
- Create: `src/DesktopGuides.Production/OnlineBrowser.cs` (the visible view, environment and policy)
- Create: `src/DesktopGuides.Production/WebViewPageLoader.cs` (`IPageLoader` over a hidden WebView2)
- Modify: `src/DesktopGuides.Production/DesktopGuides.Production.csproj` (nothing new expected; the constants come from `Directory.Build.props`)

**Interfaces:**
- Consumes: Task 1's `OnlineSources`; Task 2's `IPageLoader`, `PageLoad` and `OnlineFixtureRoutes`.
- Produces:
  - `sealed class OnlineBrowser : IAsyncDisposable`:
    - `static Task<OnlineBrowser> CreateAsync(string cacheRoot, OnlineSource source, IExternalLinkLauncher links, OnlineFixtureRoutes? fixtures)`;
    - `WebView2 View`;
    - `Uri? CurrentUrl`;
    - `event EventHandler? PageLoaded`;
    - `Task NavigateAsync(Uri url)`;
    - `Task<string> GetHtmlAsync()`, which runs `document.documentElement.outerHTML`;
    - `IPageLoader CreateLoader()`, a `WebViewPageLoader` on the same environment and profile.
  - The policy, events and copy follow the Global Constraints.

This task has no unit tests: WinUI and WebView2 code is checked by Task 5's installed modes, as the project's other WebView2 code is. Before going on, the step is a build with `-warnaserror` and a by-hand run on pcsx2-win against the fixture routes. **Never against the live sites.**

- [ ] **Step 1: Implement** `OnlineBrowser` and `WebViewPageLoader`.
  - Reuse the reader's handlers for new windows, permissions, downloads and URI schemes, extracting shared static helpers from `HtmlReaderSession` where they are private.
  - `WebViewPageLoader` serialises its loads, and for each one:
    - collects `WebResourceResponseReceived` bodies for the wanted image URLs;
    - waits for `NavigationCompleted`, reading the status from `HttpStatusCode`;
    - scrolls in four steps;
    - returns `GetHtmlAsync`.
- [ ] **Step 2: Build.** Run `build`. Expected: no new warnings.
- [ ] **Step 3: Commit:** "feat(p3): the in-app and capture browsers".

### Task 4: Production: the Find online screen and the save flow

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (`FindOnlineButton` in the Guides header, `OnlinePanel`)
- Create: `src/DesktopGuides.Production/ShellWindow.Online.cs`
- Create: `src/DesktopGuides.Production/OnlineNoticeDialog.xaml(.cs)`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`:
  - `RenderCurrentAsync` gains an `OnlineRoute` case;
  - startup calls `CaptureStore.Sweep`;
  - the online browsers are disposed on leaving the screen or closing the window.
- Modify: `src/DesktopGuides.Production/ShellWindow.Keyboard.cs` (Ctrl+L, Ctrl+S, Esc)
- Modify: the Settings XAML and code (the Online data card)

**Interfaces:**
- Consumes:
  - Tasks 1–3;
  - PR 3a's adapters, `NoticeRules`, `CaptureStore` and `OnlineGuideSaver`;
  - the existing `guidePublisher` and `importValidator`.
- Produces:
  - **The screen:**
    - Find online on the game page opens `OnlineRoute(gameId)`. Its parts, top to bottom:
      - the title;
      - a row holding the source `ComboBox`, the address `TextBox` with a Go button, and Save to library (accent);
      - the helper or progress line;
      - the `InfoBar`;
      - the browser.
    - Opening the screen shows the newest draft for the game, if there is one.
  - **The save flow:**
    1. `Read` the current page; if that gives nothing, Save is disabled.
    2. If `NoticeRules.Classify` is `Restrictive`, show the notice dialog.
    3. `Find` or `Begin` a draft.
    4. Run `GuideCapture`, with `IProgress` driving the progress line.
    5. On `Paused`, show the paused `InfoBar`:
       - Show page navigates the visible browser to that page;
       - Resume runs again;
       - Save what's captured saves now;
       - Discard deletes the draft.
    6. On `Complete`, save through `OnlineGuideSaver`, handling the four results. `NeedsFormatChange` shows the format dialog: Replace saves again with `allowFormatChange: true`; Keep both publishes as a new guide (a saver option, added here as `SaveAsNewAsync`).
    7. Show the done message with Open guide, then run the existing export reminder.
  - Leaving the route cancels a running capture.
  - Each AutomationId the Task 5 modes use: `FindOnlineButton`, `OnlineSource`, `OnlineAddress`, `OnlineGo`, `OnlineSave`, `OnlineStatus`, `OnlineInfo`, `OnlineBrowser`, and the InfoBar buttons by name.

- [ ] **Step 1: Implement** the Interfaces block, following the Global Constraints' copy and keyboard rules.
- [ ] **Step 2: Build** (`-warnaserror`), then run it by hand against the fixture routes on pcsx2-win.
- [ ] **Step 3: Commit:** "feat(p3): Find online and Save to library".

### Task 5: The `online` installed group

**Files:**
- Create: `tests/fixtures/p3/online/manifest.json` (routes over PR 3a's fixtures, plus a challenge page, a 429, an off-site link target, and image bytes)
- Modify: `tools/p1/windows_shell_install.ps1`:
  - the `online` group, which copies the fixtures to `{data}\test\online` and opens the `OnlineFixtures` gate;
  - `Run-OnlineScenarios`.
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the modes below)
- Modify: `.github/workflows/windows-ci.yml`:
  - `shell-scope` gains `online`;
  - the `html` shard runs `html,import,provider,online`.
- Modify: `docs/p1/e2e-testing.md` (the group)

**Modes** (each asserts only what app code controls):
- **`online-browse`:**
  - Find online opens with focus in the address box;
  - the source picker lists GameFAQs and Neoseeker;
  - a fixture text FAQ enables Save;
  - an unsupported fixture page disables it with the helper text;
  - Review Focus 3: an off-site link opens nothing in the in-app browser, and the external-launch record names its URL.
- **`online-save`:**
  - a three-page GameFAQs HTML guide saves with "Saved … to …";
  - the game lists it, and it opens with three pages;
  - Review Focus 4: it opens in the reader with networking dead;
  - Review Focus 5: a text FAQ saves and opens in the TXT reader.
- **`online-pause`** (Review Focus 1): page 2 answers with the challenge page.
  1. "Saving paused" appears.
  2. Show page shows it.
  3. The manifest switches page 2 to its real file. The harness rewrites the manifest; the routes are read on each request.
  4. Resume completes.
- **`online-resume`** (Review Focus 2):
  1. Start a save of the three-page guide, and go Back at page 2.
  2. Reopen Find online: the waiting draft shows; Resume completes.
- **`online-notice`:** the restrictive fixture shows the notice dialog. Cancel saves nothing; Save for personal reading saves it.
- **`online-resave`:**
  1. Save, open, and move to page 2.
  2. The manifest serves a changed page 2, and Save again replaces the guide ("Your place is kept").
  3. Reopening restores the place, by context.
- **`online-screenshot`:** a screenshot of the screen mid-save, for the PR.

- [ ] **Step 1: Write the modes and the group.**
- [ ] **Step 2: Run them.** `gh workflow run … -f shell-scope=online` on both lanes. Expected: PASS. Fix the app or the harness by the systematic-debugging rules, never by loosening an assertion.
- [ ] **Step 3: Commit:** "test(p3): the online installed group".

### Task 6: Records and the live check

- Modify: `docs/p3-technical-design.md` (a "PR 3b verification record"), `docs/progress.md`

- [ ] **Step 1: The live check, by hand, by the user**, on pcsx2-win, with a preview build:
  - one GameFAQs text FAQ;
  - one GameFAQs HTML guide;
  - one Neoseeker text FAQ;
  - one Neoseeker walkthrough.

  For each, record in the verification record:
  - whether Save was enabled;
  - the pages and images saved;
  - any pause;
  - whether the guide reads well.

  Hand the steps to the user. Don't drive the live sites from automation.
- [ ] **Step 2: The record:**
  - the commits;
  - the suite counts;
  - the installed runs;
  - each Review Focus test, by name;
  - the live-check results;
  - the PR screenshot.
- [ ] **Step 3: Commit:** "docs(p3): PR 3b records".
