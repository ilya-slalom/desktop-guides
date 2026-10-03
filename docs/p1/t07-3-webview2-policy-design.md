# T07.3 WebView2 manifest responder and deny rules design

Status: implemented in PR #34; CI run 37058200440 passed
`production-shell-ui`.
Prerequisites: T06.3 is merged (PR #19, merge commit `494cb02`); T07.2 is
merged (PR #10, merge commit `72f4378`); T11.2 is merged (M0).

## Intent

An imported static HTML guide opens in the production Reader inside a
WebView2 that can load only that guide's own approved files, makes no network
request of its own, and leaves the app only through an explicit user action
(TR07.1–TR07.3):

- **Manifest responder.** Each guide gets its own synthetic HTTPS origin.
  `WebResourceRequested` serves only the files the import preview approved,
  through their stored managed paths, and denies everything else.
- **Deny rules.** Page scripts, host objects and web messages are off.
  Navigation away from the entry document, new windows, frames, permissions
  and downloads are denied. Fragment links inside the document still work.
- **External links.** A link to a website is cancelled and shown in an
  app-owned bar above the guide; only its **Open in browser** button
  launches the system browser.
- **Canary.** Installed CI runs, with a loopback canary listening and with it
  stopped, record zero guide-originated connections or requests, and show
  that a guide can't load another guide's files.

Traces: the T07.3 row of [implementation-plan.md](implementation-plan.md)
(TR07.1–TR07.3), [work-breakdown.md](../work-breakdown.md) S07 and T07.3,
[p1-technical-design.md](../p1-technical-design.md) §5–§6, the P0
`HtmlProbe` and `HtmlAssetPolicy`, and the T07.1–T07.2
[static asset](t07-static-assets-results.md) and
[boundary](t07-static-boundary-results.md) results.

Decisions made during brainstorming:

- **A minimal production view, not a harness.** T07.3 replaces the HTML
  placeholder in the installed shell with a bare `HtmlReaderSession`, so the
  canary exercises the real open path. T09.1 keeps the rest of the adapter:
  the cache-profile sweep after a crash, the refined missing-runtime and
  mismatch messages, and the "no source or network fallback" checks.
  T09.2/T09.3 add commands, appearance and position.
- **The allowlist is saved in the library.** A schema v4 `GuideAssets` table
  holds the preview manifest's rows, written in the publication transaction.
- **The external-link prompt is a Reader InfoBar.** It doesn't block
  reading, and it is separate from the auto-dismissing shell status bar.

Rejected approaches:

- **A separate harness app** (like `DesktopGuides.ReaderToolbarSmoke`): it
  keeps the production Reader untouched, but adds a packaged app and CI job
  and doesn't exercise the shell's open path.
- **Merging T07.3 with T09.1:** fewer intermediate steps, but one large
  security-sensitive PR.
- **Re-scanning the managed copy on open:** no new table, but it re-parses
  HTML on every open, a fingerprint mismatch can't say which file changed,
  and the original `_files` folder name would still need storing.
- **A manifest file beside the guide:** it would sit inside the folder the
  WebView serves from and need its own exclusion and hash.
- **A confirmation dialog or the shell status bar for external links:** the
  dialog interrupts reading on link-heavy guides; the status bar
  auto-dismisses and is shared with unrelated messages.

## Library: `GuideAssets` (schema v4)

```sql
CREATE TABLE GuideAssets (
    GuideId TEXT NOT NULL REFERENCES Guides(Id) ON DELETE CASCADE,
    RequestPath TEXT NOT NULL,
    RelativePath TEXT NOT NULL,
    Kind TEXT NOT NULL,
    ByteCount INTEGER NOT NULL CHECK (ByteCount >= 0),
    Sha256 TEXT NOT NULL,
    PRIMARY KEY (GuideId, RequestPath)
);
PRAGMA user_version = 4;
```

- `RequestPath` is the decoded URL path from `StaticAsset.RequestRelativePath`
  and is only ever a lookup key. `RelativePath` is the managed path under
  `content/<GuideId>/`. `Kind` is the `StaticAssetKind` name (`EntryHtml`,
  `StyleSheet` or `Image`).
- The v3→v4 migration only creates the table.
- `NewImportedGuide` gains the HTML asset list. `journal.Publish` inserts
  the rows in the same transaction as the `Guides` row, so a published HTML
  guide always has them and a rolled-back import leaves none.
- The repository gains `GetGuideAssetsAsync(guideId)`. Guide deletion
  removes the rows through the cascade.
- An HTML guide with no rows (imported before v4) doesn't open; the Reader
  shows "Re-import this guide to read it."
- ShellSeed writes rows for every HTML guide it seeds.

## Core: origin and policies

These types are pure and use no WebView2 types, so the Core suite covers
them.

### `GuideWebOrigin`

`https://g<GuideId:N>.guide.invalid`, for example
`https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c1.guide.invalid`. The 33-character
label fits DNS limits and `.invalid` never resolves. Every guide has a
different origin, so a URL into another guide's origin is cross-guide and
denied, never external. The entry URL is the origin plus the entry row's
`RequestPath`, each segment escaped.

### `HtmlRequestPolicy.Decide(method, uri)`

Returns `Serve(asset)` or `Deny`. `Serve` requires all of:

- method `GET`;
- scheme `https`, the exact host, port 443, no user info, no query;
- the path decoded **once**, then no `\`, NUL, empty or `.`/`..` segment,
  and no `%` left after decoding (a sign of double encoding);
- an ordinal match of the decoded path against a `RequestPath` row.

`Serve` carries the row, and the file is opened through the row's
`RelativePath` under the guide's managed root, never through the request
path. Serving reuses the P0 `HtmlAssetPolicy` checks: the managed root must
not be a reparse point, the resolved file must stay under it, and its bytes
are re-hashed and compared with `FixedTimeEquals` before they are returned.
A mismatch or missing file is denied.

### `HtmlNavigationPolicy.Classify(uri, currentDocument)`

- `Entry`: the entry URL, the first time it loads.
- `SameDocument`: the current document with a fragment. A later navigation
  to the entry URL without a fragment (a reload) is `Deny`.
- `External(uri)`: `http` or `https` to any host not ending in
  `.guide.invalid`.
- `Deny`: everything else, including other guide origins, other HTML files
  in the same guide (not supported in P1), and `file:`, `javascript:`,
  `data:`, `blob:`, `mailto:` and custom schemes.

### Response headers

Every served file gets a `Content-Type` from its managed file's extension
(`.html`/`.htm`, `.css`, `.png`, `.jpg`/`.jpeg`, `.gif`, `.webp`; anything
else is denied),
`X-Content-Type-Options: nosniff`, `Cache-Control: no-store` and the P0 CSP
with fonts removed:

```text
default-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline';
font-src 'none'; script-src 'none'; frame-src 'none'; form-action 'none';
connect-src 'none'; object-src 'none'; base-uri 'none'
```

Denied requests get a 403 with an empty body.

## Production: session, bar and shell

### `HtmlReaderSession : IReaderSession`

- **Profile.** Each open creates a WebView2 environment with a user data
  folder at `LocalCacheFolder\WebView2\<session guid>`. `DisposeAsync`
  closes the view and deletes the folder when it can; T09.1 sweeps folders
  left after a crash.
- **Settings.** `IsScriptEnabled`, `IsWebMessageEnabled`,
  `AreHostObjectsAllowed`, `AreDefaultContextMenusEnabled`,
  `AreDevToolsEnabled`, `IsStatusBarEnabled`, `IsZoomControlEnabled`,
  `IsGeneralAutofillEnabled`, `IsPasswordAutosaveEnabled` and
  `AreBrowserAcceleratorKeysEnabled` are all false. The last one blocks
  reload, print and DevTools keys; T09.2 can bring back the ones it needs.
- **Requests.** One `AddWebResourceRequestedFilter("*", All)`. The handler
  answers every request from `HtmlRequestPolicy`, so nothing reaches the
  network stack as a page request.
- **Navigation.** `NavigationStarting` allows `Entry` and `SameDocument`,
  cancels `External` and raises it to the bar, and cancels `Deny` silently.
  `FrameNavigationStarting` cancels everything. `NewWindowRequested` sets
  `Handled` and raises `External` targets to the bar. `PermissionRequested`
  denies, and `DownloadStarting` cancels.
- **Contract.** `Capabilities` is `Scroll` only, so the TXT toolbar commands
  hide themselves. `GetLocationAsync`, `RestoreLocationAsync`,
  `ApplyAppearanceAsync` and `ExecuteAsync` are minimal stubs until
  T09.2/T09.3.
- **External link event.** The session raises `ExternalLinkRequested(Uri)`
  instead of launching anything itself.

### External-link bar

An `InfoBar` in the Reader, between the toolbar and the guide surface:

- Title "This link leaves Desktop Guides", the full URL as the message, an
  **Open in browser** button and a **Dismiss** button. The InfoBar's close
  button behaves like Dismiss.
- A newer external link replaces the URL. The bar closes when the guide
  changes or the Reader closes.
- **Open in browser** calls `IExternalLinkLauncher.LaunchAsync(uri)`, whose
  production implementation is `Launcher.LaunchUriAsync`. When a named-event
  gate `Local\DesktopGuides.Preview.ExternalLaunch.<pid>` exists, the shell
  uses a recording launcher instead. It writes the URL to
  `LocalCacheFolder\diagnostics\external-launches.json`, so the CI runner never
  opens a browser.
- AutomationIds: `ReaderExternalLinkBar`, `ReaderExternalLinkUrl`,
  `ReaderExternalLinkOpen`, `ReaderExternalLinkDismiss`.

### Shell wiring

- `GuideFormat.Html` opens through `HtmlReaderSession`; the placeholder
  remains only for PDF. TXT is unchanged.
- Failures show in the same place as TXT load errors (`ReaderLoadError`) and
  as a warning status:
  - no WebView2 runtime: "Web page guides need the Microsoft Edge WebView2
    Runtime.";
  - no asset rows: "Re-import this guide to read it.";
  - the entry file is missing or its hash differs: "This guide's files
    have changed. Re-import it to read it."

  T09.1 refines this copy and adds the actionable runtime message.

### Test diagnostics

The session counts its decisions: served request paths, and denied requests
by category (cross-guide, external, other scheme, malformed, not in the
manifest, hash mismatch) and by WebView2 resource context. It writes these
only when a named-event gate
`Local\DesktopGuides.Preview.HtmlDiagnostics.<pid>` exists, following the
existing `...ForTest` gates. It writes a JSON file to
`LocalCacheFolder\diagnostics\html-session-<n>.json` when the session is
disposed. Without the gate nothing is counted or written. The file never
contains external URLs or guide text.

## Testing

### Core xUnit

- `GuideWebOrigin` format, uniqueness and parsing.
- `HtmlRequestPolicy` tables: an allowed path; a non-GET method; `http`;
  a different port; user info; a query; a lookalike host such as
  `g<id>.guide.invalid.example` or another guide's origin; single- and
  double-encoded traversal (`..`, `%2e%2e`, `%252e`); `\`, `%5c` and NUL;
  case differences; a trailing slash; companion folders whose request path
  differs from their managed path.
- `HtmlNavigationPolicy` tables for each classification, including
  `javascript:`, `data:`, `mailto:`, another guide's origin, another HTML
  file in the same guide, and an uppercase scheme.

### Infrastructure xUnit

- v3→v4 migration on an existing v3 library.
- Publication writes the HTML rows with the guide; a commit failure leaves
  neither; TXT and PDF publish no rows.
- `GetGuideAssetsAsync` round-trip; deleting a guide removes its rows.
- Serving opens the row's managed path and denies a changed or missing
  file.

### Installed smoke (`production-shell-ui`)

ShellSeed adds two canary HTML guides whose files reference
`tools/p0/http_canary.py` on loopback through:

- `img` `src` and `srcset`, `picture`/`source`, `input type=image`,
  `video poster`, the `background` attribute and inline `style` URLs;
- `link` `stylesheet`, `icon`, `preconnect`, `dns-prefetch`, `prefetch`
  and `preload`;
- `@import`, nested `url()` and `image-set()` in local CSS;
- `meta http-equiv=refresh`, a `base href`, `object`, `embed` and `iframe`;
- an external link with `ping` and a `target=_blank` link;
- an image whose URL is in the other canary guide's origin.

The canary changes so that it counts accepted connections as well as
requests. A preconnect or DNS prefetch can open a connection without sending
a request, and that still counts as guide-originated.

For each canary guide, the smoke opens it once with the canary listening and
once with the canary stopped, and each time checks that:

- the guide's text appears in WebView2's UI Automation tree;
- the canary recorded zero connections and zero requests;
- the diagnostics show every local asset served and the cross-guide image
  denied.

It then clicks the external link and checks that `ReaderExternalLinkUrl`
shows its URL. It clicks **Open in browser** and checks that the recording launcher's
file contains that URL. It clicks the `target=_blank` link, checks the
bar again, then clicks **Dismiss** and checks the bar is gone. It then
checks the canary log is still empty.

The existing Web Page Guide check changes from the placeholder text to the
loaded guide.

## Docs

- This design, and `t07-3-webview2-policy-plan.md`.
- `work-breakdown.md` and `implementation-plan.md` for T07.3, including the
  stale T08.3 "in review" status (PR #31 and its PR #33 follow-ups are
  merged).
- [p1-technical-design.md](../p1-technical-design.md): any engine behavior
  that differs from the starting approach, such as how fragment navigation
  is reported.

## Out of scope

- Position capture and restore, text scale, theme and Find (T09.2, T09.3).
- The crash-leftover profile sweep and the actionable missing-runtime
  experience (T09.1).
- Links between HTML files in one guide; SVG, scripts, frames, fonts and
  media (unsupported in P1).
- PDF reading (T10).

## Planning rulings

Rulings from [the plan](t07-3-webview2-policy-plan.md) that refine the spec.
R4 and R19 change spec wording.

- **R1.** Queries are ignored for lookup, not denied; a query never changes which file is served.
- **R2.** No "no `%` after decoding" rule; a single decode of `%252e` matches no row.
- **R3.** Portable cache root is `%LOCALAPPDATA%\DesktopGuides\Cache`; packaged uses `LocalCacheFolder`.
- **R4.** The seeded "Web Page Guide" stays rowless as a pre-v4 import, phase `html-no-manifest`. **Changes spec wording:** replaces "ShellSeed writes rows for every HTML guide".
- **R5.** Canary guides are seeded through the real `GuideImportPublisher` (`seed-html-reader`).
- **R6.** Serving uses `ManagedPathResolver.ResolveExistingGuideFile`; the P0 `HtmlAssetPolicy` is unchanged.
- **R7.** `GuideAssets` adds CHECK constraints on `Kind` and `length(Sha256)`.
- **R8.** `GetGuideAssetsAsync` lives only on `SqliteLibraryRepository`.
- **R9.** External navigation must be user-initiated; otherwise it is denied silently.
- **R10.** External URLs with user info are denied.
- **R11.** `IsGuideHost` also matches the bare P0 host `guide.invalid`.
- **R12.** `FileMissing` is a separate deny reason from `HashMismatch`.
- **R13.** Fragments are ignored by `Decide`.
- **R14.** `System.Uri` normalization is accepted; `%2e%2e` and `%5c` stay encoded until the single decode and are then rejected.
- **R15.** One smoke mode `html-reader` covers canary A, canary B and the external-link checks, as separate phases.
- **R16.** Diagnostics files are named `html-session-<pid>-<n>.json` so online and offline launches don't collide.
- **R17.** "This link couldn't be opened." is new warning copy for a launcher that returns false or throws.
- **R18.** Load failures inside `OpenAsync` surface as `HtmlGuideLoadException` and map through `HtmlGuideLoadMessages.For`.
- **R19.** The installed check proves cross-guide isolation by served sets, not deny counts. **Changes spec wording:** the spec's "diagnostics show the cross-guide image denied" no longer holds, because the CSP stops it first.

Execution rulings:

- **Task 5.** The CI gate for Task 5 is Task 6's `production-shell-ui` run; the shell has no unit-test project.
- **Task 6.** Route every Chromium-made connection, loopback included, to a dead proxy (`--proxy-server=http://0.0.0.0:9 --proxy-bypass-list=<-loopback>`) to stop preconnect and predictor sockets. Cost if wrong: one extra browser argument. The fallback is feature disables.
- **Task 6.** Accept reading page UI Automation from the `msedgewebview2` browser window. Cost: the smoke depends on Chromium window class names.
- **Task 6.** `Mark-CanaryLines` stays as a permanent non-asserting diagnostic (per-step canary counts in the report).

## Implementation notes

What CI showed in run 37058200440 (attempt 2; the online and offline
launches matched). The WebView2 runtime on the runner was 153.0.4234.48.

- **Fragment link.** It scrolled in-page to `#details`, opened no bar and
  recorded no denial. `HtmlReaderSession` lets a `SameDocument` navigation
  through if `NavigationStarting` is raised for it, but no diagnostic records
  that event, so whether WebView2 raises it was not observed directly.
- **`rel=icon`, `prefetch`, `preload`.** None reached `WebResourceRequested`
  (no `Image` or `Stylesheet` denials) and the canary logged no GET, so the
  CSP stopped them first. The same holds for every loopback subresource:
  `img` `src`/`srcset`, `picture` `source`, `input type=image`, `video
  poster`, `object`, `embed`, `iframe` and the CSS `url()` references.
- **`ping`.** Not sent (no `GET /ping`).
- **`IsUserInitiated`.** A pointer click on the external anchor and on the
  `target=_blank` anchor opened the bar, so both were user-initiated. The
  `meta refresh` to `http://127.0.0.1:8765/redirect` was cancelled with no
  bar (R9). In the code, a user click on an external anchor goes through
  `NavigationStarting` and `target=_blank` through `NewWindowRequested`.
- **Canary A, by layer.**
  - Loopback references and the cross-guide image: stopped by the CSP. No
    handler entry and no canary line.
  - The cross-guide anchor (a link into guide B's origin): cancelled by
    `NavigationStarting` as `Deny`, with no bar. The diagnostics also hold a
    `CrossGuide`/`Document` x1 entry. Only the handler writes diagnostics, so
    that request reached it too; the order of the two events was not
    observed.
  - `External`/`Document` x2 in the handler: the `meta refresh` and the
    external link click. Navigation then cancelled the refresh silently and
    showed the click in the bar.
- **`base href`.** Ignored under `base-uri 'none'`; local files were served at
  the guide's own origin.
- **Served sets.** Guide A served `guide.html`, `images/a.png` and
  `style.css`. Guide B (before its title-derived rename, below) served
  `guide.html`, `images/b.png` and `style.css`,
  with nothing denied. `external-launches.json` held only
  `https://example.com/desktop-guides-canary`; the `target=_blank` link to
  `example.org` was dismissed.
- **Page UI Automation.** The WebView2 page's tree is not in the shell
  window's tree, which shows an empty `Microsoft.UI.Xaml.Controls.WebView2`
  pane. It is in a top-level `Chrome_WidgetWin_1` /
  `Chrome_RenderWidgetHostHWND` window owned by the `msedgewebview2` browser
  process, a child of the app. The smoke reads it from there.
- **Preconnect and the network predictor (differs from the starting
  approach).** Before the fix, run 37049094122 showed the canary's accepted
  connections at 1 (before A), 3 (A loaded), 5 (after refresh) and 5 to the
  end. These four bare TCP connections carried no request line. They came
  from `preconnect`/`dns-prefetch` hints and from the `meta refresh` target
  before `NavigationStarting` cancelled it. Neither the CSP nor
  `WebResourceRequested` sees them. Every HTML session's environment now sets
  `AdditionalBrowserArguments` to `HtmlBrowserEnvironment.Arguments`
  (`--proxy-server=http://0.0.0.0:9 --proxy-bypass-list=<-loopback>`), so
  Chromium's own connections, loopback included, go to a dead proxy. Content
  served by `WebResourceRequested` never reaches the network, so it is
  unaffected. After the fix every step counts 1, the install script's health
  check. The canary now mostly guards against losing the proxy, and the
  served sets are the main isolation evidence (R19). Preconnect and
  dns-prefetch hints are common in saved pages from major guide sites
  (Fandom, IGN, GameFAQs), so without the proxy real imports would likely
  have leaked TCP connects when online. This is an expectation, not a
  measurement of those sites.
- **Title-derived names.** Canary guide B is now a Save Page As layout named
  after its page title: `Canary Guide B (PS1) - Walkthrough's 100% Café – v2.html`
  beside its `_files` companion. The `%` makes the import alias the
  entry to `guide.html`; the companion keeps its source name. Navigation
  matches the entry by decoded path (scheme, host, port and query unchanged),
  because Chromium can report `( ) '` literal where `EntryUri` escapes them.
  Run 37081967040 passed; both passes served B's two `_files` assets and
  `guide.html`, and the canary log stayed at 1.

## Verification

Run 37058200440 (merge-base `e447597`), attempt 2, passed every job. In both
`html-reader-online` (light, canary listening) and `html-reader-offline`
(dark, canary stopped) the phases `html-canary-a`, `html-external-links` and
`html-canary-b` passed. `html-canary.log` holds one `ACCEPT` line, the
install script's health check, and nothing after the baseline in either
pass.

`production-shell-ui` has two known flakes, each cleared by one rerun and
unrelated to T07.3: "Interactive launch did not write launch.json" and the
TXT "no rows" check in `txt-reader-light`.
