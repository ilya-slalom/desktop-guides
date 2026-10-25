# Architecture

How Desktop Guides is built today, and the rules a change must keep. It is
for developers and coding agents about to change the code. For how to
build, test and release, see [contributing.md](contributing.md),
[testing.md](testing.md) and [releasing.md](releasing.md). For per-site
rules, see [sources.md](sources.md).

Desktop Guides is a WinUI 3 app on .NET 10 for Windows 10 19041 and later,
x64 and ARM64. It keeps a local library of games and their guides (TXT,
HTML, PDF and image maps), reads them offline, and remembers each guide's
place. The reasoning behind past decisions is kept in the
`docs-history-2026-10-25` tag; this page describes only the current design.

## Invariants

Break none of these without a design change agreed first.

- **Originals stay untouched.** Import reads the picked file read-only and
  copies it into managed storage. Readers open only the managed copy.
- **Guides read offline.** No reader reaches the network. Only Find online
  and the metadata providers do, and only to allowed hosts.
- **Imported content is untrusted.** HTML, CSS, asset paths, PDF and image
  bytes, and captured pages are all treated as hostile input.
- **No partial records.** An import, replace, delete or restore either
  completes or leaves the library as it was. A crash is finished or undone
  at the next start.
- **Stable per-guide locators.** A guide's place is a versioned,
  format-specific locator tied to the content hash, not a percentage.
- **Completion is explicit.** A guide is complete only when the user marks
  it. Never infer it from the reading estimate or position.
- **One process owns the library.** A file lease serializes sessions, and
  the repository's write gate serializes writes.

## Solution layout

`DesktopGuides.sln` holds the projects under `src/` and `tests/`. The
`tools/` projects are built on their own by CI.

| Project | Target | What it is | References |
|---|---|---|---|
| `src/DesktopGuides.Core` | `net10.0` | Models, contracts, rules and user messages. No I/O beyond small helpers, no UI. | none |
| `src/DesktopGuides.Infrastructure` | `net10.0` | SQLite library, managed storage, import, backup and restore, loaders, online sources, providers. Uses AngleSharp, AngleSharp.Css, Microsoft.Data.Sqlite and PdfPig. | Core |
| `src/DesktopGuides.Production` | `net10.0-windows10.0.19041.0` | The shipped WinUI 3 app: shell, reader sessions, dialogs, styles. Windows App SDK, WebView2, CommunityToolkit controls. | Core, Infrastructure |
| `src/DesktopGuides.App` | `net10.0-windows10.0.19041.0` | The original reader spike app (`MainWindow`, `Probes/`, `Reading/IReaderAdapter.cs`). Built by CI; not shipped. | Core |
| `tests/DesktopGuides.Core.Tests` | `net10.0` | xUnit tests for Core. | Core |
| `tests/DesktopGuides.Infrastructure.Tests` | `net10.0` | xUnit tests for Infrastructure; sees its internals through `InternalsVisibleTo`. | Infrastructure |
| `tools/p1/DesktopGuides.ShellSeed` | `net10.0` | Console tool that seeds libraries and holds database locks for installed E2E runs. | Core, Infrastructure |
| `tools/p1/DesktopGuides.ReaderToolbarSmoke` | WinUI 3 | Test host that links the production `ReaderToolbar`, `MapsPane` and styles. | Core |

`tests/fixtures/` holds fixture guides and online-source fixtures.
`tools/p0` and `tools/p1` hold the scripts for fixtures, packaging, signing,
installed UI runs and release gates.

Keep platform-free logic in Core or Infrastructure, where the xUnit tests
run on any OS. Production is the only project that touches WinUI, WebView2,
`Windows.Data.Pdf` or WIC.

### Build switches

| Property | Default | Effect |
|---|---|---|
| `IncludeGameFaqs`, `IncludeNeoseeker`, `IncludeSteam`, `IncludeVgMaps` | `true` | `Directory.Build.targets` turns each into `SOURCE_GAMEFAQS` and the like. Only `OnlineSources.Available` reads them: a source left out isn't offered, and its pasted URLs are refused like any other site's. Its code still compiles, and guides already saved from it still read. |
| `PackageLane` | `Preview` | `Preview` or `Public`; anything else fails the build. `Public` swaps in `Package.Public.appxmanifest` and `app.public.manifest`, and defines `PUBLIC_LANE`. |
| `Portable` | unset | `true` builds the unpackaged, self-contained single-file exe. Outputs go under `artifacts/portable/`, so the MSIX and portable builds never glob each other's files. |

`PUBLIC_LANE` compiles out test-only paths: the provider fixture handler in
`ProviderServices`, the online fixture routes in `OnlineBrowser`, and DevTools
in the online browser. `AppLaneNames` (Core) and `AppLane` (Production) give
each lane its own single-instance key, named events, pipe names and window
title, so Preview and Public can be installed side by side.

## The app shell

`Program.Main` is a custom entry point (`DISABLE_XAML_GENERATED_MAIN`):
- It enforces a single instance per lane through
  `AppInstance.FindOrRegisterForKey`. The MSIX and portable builds use
  different keys, because they keep separate libraries.
- A second launch hands off to the first over `LaunchActivationPipe`, and
  the first activates its window.
- It installs a `DispatcherQueueSynchronizationContext` before `App` is
  created.

`App.OnLaunched` creates one `ShellWindow` and calls `InitializeAsync`.

### ShellWindow

`ShellWindow` is one window split into partial files by feature.
`ShellWindow.xaml.cs` holds startup (`InitializeCoreAsync`), routing,
rendering, the Library and Game pages, and the status line.

| Partial | Owns |
|---|---|
| `Startup` | The library-unavailable page, Retry, and the WebView2 runtime check |
| `Progress` | `ProgressCoordinator` wiring and test-only progress diagnostics |
| `Completion` | The only caller of `GuideCompletionService` |
| `TextView`, `TextSize`, `HtmlReader`, `PdfReader`, `MapReader` | Opening each reader and its per-guide settings |
| `Find`, `Contents`, `Bookmarks` | The reader's find bar, contents pane and bookmarks pane |
| `Maps`, `ImportMaps`, `MapCache` | The Maps pane, multi-file map import, and map tile caches |
| `Online`, `Steam`, `VgMaps` | Find online: the in-app browser and capture, Steam search and fetch, the VGMaps atlas |
| `Backup` | Export and restore in Settings |
| `Keyboard`, `Shortcuts` | Key routing |
| `Theme`, `Icons` | Theme, window material, and colourful icons |

Navigation:
- `ShellNavigator` (Core) holds the route and a back stack. The routes are
  `LibraryRoute`, `GameRoute`, `ReaderRoute`, `OnlineRoute` and
  `SettingsRoute`.
- Library and Game entries keep an anchor (the focused game, the selected
  guide), so Back restores focus.
- Opening a reader or Find online always puts its game page beneath it.
- `NavigationActionQueue` runs route changes and writes that must not
  interleave with them, such as completion, one at a time.
- Each render bumps `renderGeneration`, and every `await` in a render is
  followed by a generation check. A render that a newer one replaced
  returns without touching the UI. Keep this pattern in any new route code.

Settings live in the `Settings` table as `AppSettings`: theme, window
material, colourful icons, the contents pane state, the last active guide
and the export reminder. The Settings page also holds the provider
credentials card (`ProviderSettingsCard`), backup and restore, and "Clear
online data".

Startup order in `InitializeCoreAsync`:
1. Resolve the data root (`AppDataRoot`) and acquire `LibrarySessionLease`.
2. Run `LibraryRestoreRecovery`, then open `SqliteLibraryRepository`. This
   migrates or validates the database and reconciles file operations. If a
   just-restored library fails to open, roll back to the prior one.
3. Build the publishers, removers and loaders on the repository.
4. Resolve the cache root (`AppCacheRoot`), then sweep stale WebView2
   profiles and online captures older than 7 days.
5. Load provider credentials and settings, then render.

If the library can't be opened, `ShowLibraryUnavailable` shows a
`LibraryOpenMessages` page and the shell stays not `ready`, so nothing can
write.

## Readers

Each format has a reader session in Production behind
`IReaderSession` (`Core/Reading/ReaderContract.cs`):

| Format | Session | Engine | Loader (Infrastructure) |
|---|---|---|---|
| `Txt` | `TextReaderSession` with `TextReaderView` and `TextReflowView` | Native WinUI list of lines | `ManagedTextGuideLoader` |
| `Html` | `HtmlReaderSession` | WebView2, local origin per guide | `ManagedHtmlGuideLoader`, `ManagedHtmlAssetReader` |
| `Pdf` | `PdfReaderSession` with `PdfReaderView` | `Windows.Data.Pdf` page preview beside PdfPig page text | `ManagedPdfGuideLoader`, `PdfPageTextSource` |
| `Image` | `ImageReaderSession` | WinUI canvas; tiled pyramid for large maps | `MapTileStore`, `MapCacheQueue` |

### The reader contract

`IReaderSession` is the whole interface between the shell and a reader:
- `Capabilities` (`ReaderCapabilities` flags) decides which toolbar
  commands show. `ReaderCommandPolicy` maps commands to the capabilities
  they need, and refuses a command a session didn't declare.
- `ExecuteAsync(ReaderAction)` takes `ScrollAction`, `PageTurnAction`,
  `PageEdgeAction`, `PageJumpAction`, `ZoomAction`, `FitWidthAction`,
  `FitViewAction` and `TextSizeAction`.
- `GetLocationAsync` and `RestoreLocationAsync` read and restore the place.
  `RestoreOutcome` reports `Exact`, `Context`, `Approximate` or
  `Unavailable`, and the shell tells the user when the restore wasn't exact.
- `ApplyAppearanceAsync` takes a resolved `ReaderTheme` (Light, Dark or
  HighContrast) and a text scale. A reader never reads the Windows theme
  itself.
- `PageKeyRouting` gives TXT and PDF Page Up/Down and Ctrl+Home/End. A
  multi-page web guide leaves those keys to the page and uses
  Ctrl+Page Up/Down to move between pages.

Optional interfaces extend a session. Each one goes with a capability flag:
- `IFindableReader` with `Find`: in-guide search returning `FindResult`.
  TXT, HTML and PDF have it. A PDF scan reports partial totals and
  unsearchable (image-only) pages.
- `IOutlineReader` with `Outline`: contents from the document or detected
  sections (`OutlineSource`). Bookmarks share the `Outline` capability.
- `Reflow` (TXT only): Original or Reflow view, planned by
  `TextReflowPlanner` off the UI thread and saved per guide as `TextView`.
- `GuidePages` (multi-page HTML) and `FitView` (maps).

### Locators

`ReaderLocation` is `Format`, `SchemaVersion`, `ContentSha256`, a
format-specific `ReaderPosition` and an optional `EstimatedFraction`:

| Position | Fields |
|---|---|
| `TextPosition` | Character offset and a context snippet |
| `HtmlPosition` | Document path, element ID, text quote, text offset, scroll fraction |
| `PdfPosition` | Page index and fraction within the page |
| `ImagePosition` | Zoom (null means fitted) and centre as fractions |

Rules (`ReaderLocationCodec`):
- Serialized JSON is at most 4096 bytes, matching the database checks.
- The current version is 1. Another version decodes as `UnsupportedVersion`.
- A hash that differs from the guide's gives `ContentChanged`. The restore
  then falls back to context, then approximate (`RestoreCandidates`).
- An HTML position must name one of the guide's own documents.
- `EstimatedFraction` is display-only, bounded by `ProgressEstimate`. It
  never decides completion.

A new format or position field needs a codec change, a version decision and
tests for old locators.

### Saving the place and completion

- `ProgressCoordinator` saves the open guide's place 1 s after movement
  stops, or 4 s into continuous movement, and on flush or close. It records
  the open time when tracking starts. A save that fails shows a warning and
  never throws into the reader.
- Find holds `IProgressTracking.PauseAsync`, so stepping through matches
  never moves the saved place.
- A text size change is not movement and saves no place.
- Maps open fitted, with no saved place and no progress tracking.
- `GuideCompletionService` is the only writer of completion.
  `SetGuideCompletionAsync` changes only `CompletedUtcMs`, and a repeated
  completion keeps the first time. `SaveReadingLocationAsync` never touches
  completion.

### Text

TXT is decoded by the encoding chosen at import: UTF-8, or code page 437
or 1252 when the user picks one (`ManagedTextDecoder`). The code page is
stored on the guide. `TextSectionDetector` finds sections for the contents
pane and reflow.

### HTML

An HTML guide is static: one entry document, optional further pages in
reading order (`GuidePages`), and its stylesheets and images
(`GuideAssets`). The session follows these rules:
- **Origin:** each guide has its own origin,
  `https://g<guid>.guide.invalid/` (`GuideWebOrigin`). `.invalid` never
  resolves. Another guide's URL is cross-guide, never external.
- **Requests:** every request goes through `HtmlRequestPolicy.Decide`. It
  allows only GET, this guide's host, https on port 443, no user info, and a
  normalized path that is in the manifest. The response comes from the
  row's `RelativePath`, never the request path. `ManagedHtmlAssetReader`
  serves bytes only if they still match the recorded length and SHA-256.
- **Content Security Policy:** `HtmlRequestPolicy.ContentSecurityPolicy`
  allows no scripts, fonts, frames, forms or connections. Images may come
  from self and `data:`, and styles from self and inline.
- **Browser settings:** scripts, web messages, host objects, context menus,
  DevTools, the status bar, zoom control, autofill and browser accelerator
  keys are all off. Permissions are denied, downloads and new windows are
  cancelled, and external URI schemes never launch.
- **Network:** `HtmlBrowserEnvironment.Arguments` points Chromium at a dead
  proxy. Preconnect and prediction make sockets that neither the CSP nor
  `WebResourceRequested` sees, so the proxy is what stops them. Don't remove
  it.
- **Navigation:** `HtmlNavigationPolicy` cancels every navigation except the
  current document, its fragments, and a user's click on another imported
  page. A user's click on a website link raises the shell's external-link
  bar, and nothing opens without a second click. A page's own redirects and
  refreshes are denied.
- **Scripts the app runs:** they go through DevTools `Runtime.evaluate`
  with `userGesture: false`. `ExecuteScriptAsync` would add a user gesture,
  making page-started navigations look like clicks.
- **Profiles:** each session has a fresh WebView2 profile under
  `<cache>/WebView2/<guid>`. `WebView2ProfileSweeper` deletes leftovers at
  startup, and only folders whose names it generated.
- **Style:** `HtmlReaderStyle` is the app's theme and text-size style. It is
  built only from constants and a clamped scale, and has zero specificity,
  so the page's own rules still win.
- **Runtime missing:** the WebView2 runtime is checked at startup and on
  each open. Without it, the user gets a message with a download link.

`HtmlAssetPolicy` (Core) is the spike app's older single-origin policy. The
production reader uses `HtmlRequestPolicy`.

### PDF

- `PdfReaderSession` renders the current page with `Windows.Data.Pdf`, and
  shows PdfPig's text of the same page beside it, for selection and find.
- Page loads run one at a time and the latest wins (`LatestWinsScheduler`),
  so a fast page turn never pairs one page's text with another's preview.
  `PdfRasterBudget` and `PdfRenderCache` bound memory.
- PdfPig isn't thread-safe, so `PdfPageTextSource` serializes extraction.
  Find uses its own `PdfPageTextSource`, so a scan never competes with the
  shown page.
- A password is used once to open the document; it is never stored or
  echoed.

### Image maps

- `MapPyramid` (Core) decides levels and tiles. A map up to 4096 px on its
  longer side is shown whole from its file.
- A larger map gets an overview of at most 2048 px and 512 px tiles, built
  off the UI thread by `MapCacheQueue` through `WicMapTileBuilder`.
- `MapTileStore` keeps caches under the cache root, keyed by the map's
  hash. Each cache is built in a `.building` folder and moved into place
  only after its manifest is written. The store is capped at 2 GiB, evicts
  the least recently opened first, and is pruned of hashes no guide has.
  It is never backed up.
- No surface exceeds 4096 px. Tiles in memory stay within the PDF budget
  (`MapTileMemory`).
- Import limits are in `MapImageLimits`: 256 MiB, 32768 px a side, and
  268,435,456 pixels.

## Storage

### On disk

`AppDataRoot` resolves the data root. The MSIX build uses the package's
LocalState. The portable build uses `%LOCALAPPDATA%\DesktopGuides`. The
cache root (`AppCacheRoot`) is the package's LocalCache, or `Cache\` beside
the portable library.

```
<data root>/
  library.session.lock        LibrarySessionLease
  providers.bin               DPAPI-protected provider credentials
  restore.marker              only during a restore
  .restore-staging/<stage>/   a staged restore
  .recovery/                  database copies taken before migrations
  library/
    library.sqlite            the database (WAL)
    content/<guide>/          one guide's managed files
    artwork/<game>/<sha>.png|jpg|webp
    .staging/<op>/<guide>/    imports in progress
    .trash/<op>/<guide>/      deletes and replacements in progress
    .artwork-staging/
<cache root>/
  WebView2/<guid>/            reader profiles, one per session
  Online/Profile/             the in-app browser's persistent profile
  online-captures/            unfinished captures
  steam-apps.json, vgmaps-index.json   30-day indexes
  map tiles, diagnostics
```

`ManagedPathResolver` builds every managed path:
- Guide and operation IDs are required, never empty.
- Relative paths go through `ManagedRelativePath.Parse`. A resolved path
  must stay under its guide root.
- Every segment is checked for reparse points (`RejectFilesystemLinks`).
- The database and its `-wal`, `-shm` and `-journal` sidecars must not be
  links or have more than one hard link.

Build new paths through it, never with `Path.Combine` on user data.

### The database

`SqliteLibraryRepository` implements `ILibraryRepository`, which includes
`IReadingLocationStore`, `IGuideCompletionStore` and `IBookmarkStore`.

Tables (`LibrarySchema`, version 9):

| Table | Holds |
|---|---|
| `Games` | Title, platform, notes, and the optional IGDB link, metadata JSON and artwork path |
| `Guides` | Format (`Txt`, `Html`, `Pdf`, `Image`), managed root `content/<id>`, primary path, content SHA-256 and size, TXT code page, map pixel size |
| `ReadingStates` | Locator JSON, estimate, last opened time, completed time |
| `ReaderPreferences` | Text scale and TXT view |
| `Bookmarks` | Locator JSON and label per guide |
| `GuideAssets`, `GuidePages` | An HTML guide's files, and its pages in reading order |
| `GuideSources` | Where a saved guide came from (below) |
| `GameSteamApps` | A game's Steam app ID |
| `Settings` | Key-value app settings |
| `FileOperations` | The journal: `Import`, `DeleteGuide`, `DeleteGame`, `ReplaceGuide`, each `Prepared` or `Committed` |

Most rules are enforced by `CHECK` constraints and foreign keys with
`ON DELETE CASCADE`. Keep the C# validation (such as `GuideSource.Validate`)
in step with them.

Versioning:
- `PRAGMA user_version` is the schema version. `LibrarySchema.Migrations`
  is the ordered list of SQL steps, and each step sets its own version.
- On open, a newer version is refused (`LibraryOpenIssue.NewerVersion`).
  The current version is validated. An older one is validated at its own
  version, copied to `.recovery/`, then migrated in one transaction.
- Migrations run with foreign keys off, because a rebuild of a referenced
  table must not cascade its drop. `foreign_key_check` in validation is the
  gate.
- `ValidateDatabase` checks the version, `integrity_check`,
  `foreign_key_check`, and that `sqlite_master` exactly matches the schema
  the migrations build from empty. A hand-altered or partial schema is
  "damaged".

To change the schema:
1. Add a `VersionN` step and append it to `Migrations`.
2. Raise `CurrentVersion`.
3. SQLite can't alter a `CHECK`, so rebuild the table, as `Version7`,
   `Version8` and `Version9` do.
4. Update backup and restore if the new data references files.

Writes and opening:
- Every write goes through `WriteAsync`: one `SemaphoreSlim` write gate,
  with the work on the thread pool. Imports and exports hold the same gate
  for their whole run (`RunImportAsync`, `RunExportAsync`). Reads run on the
  thread pool without the gate.
- `LibraryOpenErrors` maps open failures to `LibraryOpenIssue` (`Damaged`,
  `Locked`, `NoAccess`, `DiskFull`, `Missing`, `MigrationFailed`,
  `NewerVersion`).
- A missing database is created only in an unused data folder. If content
  or artwork exists without a database, the open is `Missing`, so a restore
  can still succeed.
- At each open, the repository also sweeps unreferenced artwork and reports
  guides whose files are missing (`GuideFileHealth`).

### Import: inspect, stage, verify, publish

1. **Inspect.** `GuideImportValidator.InspectAsync` reads the picked file
   with shared read access and writes nothing. It returns `ImportReady`,
   `ImportNeedsTxtEncoding` or `ImportNeedsPdfPassword`. HTML goes through
   `StaticHtmlImportValidator` and `StaticHtmlDependencyScanner` (AngleSharp
   for HTML and CSS). They record every stylesheet and image reference,
   follow linked pages in the same folder (up to 200), and enforce
   `StaticHtmlScanLimits`. Other limits are in `GuideImportLimits`: TXT
   64 MiB, PDF 1 GiB, maps 256 MiB.
2. **Fingerprint.** A file's SHA-256, or for HTML, the SHA-256 of its sorted
   file list (`GuideFingerprint`).
3. **Publish.** `GuideImportPublisher.PublishAsync`, under the write gate:
   - commit a `Prepared` `Import` row;
   - copy into `.staging/<op>/<guide>` while hashing;
   - refuse if the bytes, size or write time differ from what was inspected;
   - verify the staged copy;
   - move it to `content/<guide>`;
   - insert the guide, assets, pages and source, and clear the journal row,
     in one transaction.
4. **Fail.** Any failure before the commit rolls back. If even that fails,
   the startup reconciler finishes it. Cancellation stops applying once
   `ImportProgress.Publishing` is reported.
5. **Duplicates.** The same content in the same game is refused
   (`ImportIssue.Duplicate`) unless the caller allows it.

### Saving a guide again

`GuideImportPublisher.ReplaceAsync`:
- **What stays and what changes:** the guide keeps its ID, title, place,
  bookmarks and completion. Its files, content columns, assets, pages and
  source change.
- **Up to date:** a matching fingerprint changes nothing and returns
  `UpToDate`.
- **Format change:** another format is refused (`FormatChanged`). A switch
  between TXT and HTML is allowed when the caller says so, and then the
  place, bookmarks and reader settings are dropped.
- **Sequence:** stage and verify, move the old copy to
  `.trash/<op>/<guide>`, move the new one in, commit `ReplaceGuide`, then
  delete the old copy.
- **Recovery:** at startup, a `Prepared` replacement is undone and a
  `Committed` one is finished.

### Deleting

- `GuideRemover` and `GameRemover` use `DeleteGuide` and `DeleteGame`
  journal rows. They move content to `.trash/<op>/`, delete the rows (which
  cascade), and then delete the trash.
- `FileOperationReconciler` runs at every open, before anything reads:
  - a leftover `Import` is removed;
  - a `Prepared` delete moves its content back;
  - a `Committed` delete finishes.
- It checks every row before moving anything, and refuses two operations
  claiming one guide.

### Backup and restore

- **Export.** `LibraryExporter` writes a `.zip`: a database snapshot taken
  with SQLite's backup API, plus every file it references, each checked
  against its recorded hash. `manifest.json` (`LibraryArchiveManifest`) is
  written and parsed without reflection. The write gate is held for the
  whole export, so the archive is one consistent state. The archive appears
  at its destination only after it has been verified.
  `BackupDestinationPolicy` refuses folders that uninstalling removes.
- **Restore, stage.** `LibraryRestorer.StageAsync` copies the archive into
  `.restore-staging/<stage>/` and checks every name, size, hash and database
  reference. It also checks for name collisions on a case-insensitive,
  Unicode-normalized disk, and for free space (the size plus a tenth). Then
  it extracts. Nothing under `library/` changes.
- **Restore, swap.** `ReplaceAsync` swaps the stage in, with the lease held
  and no repository open. It parks the prior library and records progress
  in `restore.marker`.
- **Restore, recovery.** `LibraryRestoreRecovery` runs at the next
  initialization. Only the session that swapped may keep the new library,
  and only if it opens. Any other start rolls it back. Every step is safe
  to repeat.

## Find online

Find online (`OnlineRoute`) lets the user save guides from GameFAQs,
Neoseeker, Steam and VGMaps. `OnlineSources` lists the sources a build
includes, with each one's hosts. A pasted URL from any other host is
refused. Per-site terms, pacing and rules for adding a source are in
[sources.md](sources.md).

### GameFAQs and Neoseeker: browse and capture

- **The browser.** `OnlineBrowser` is a WebView2 in its own persistent
  profile (`<cache>/Online/Profile`), separate from the reader. Sites load
  as they are served, scripts and ads included. It refuses navigations off
  the source hosts: a user's click opens in the system browser. It denies
  permissions and cancels downloads and external schemes. "Clear online
  data" deletes the profile.
- **Adapters.** `IGuideSourceAdapter` (`GameFaqsAdapter`,
  `NeoseekerAdapter`, and `SteamGuideAdapter` for Steam pages) matches a
  URL to a guide (`GuidePageMatch`: site, item ID, page key) and parses
  given HTML into a `GuidePageReading`. An adapter never fetches anything.
- **Capture.** `GuideCapture` steps the visible browser through the guide's
  pages through `WebViewPageLoader`, at least 2 s apart. Images come from
  the page loads themselves. A 403, 429, 503, timeout or unreadable page
  (such as a challenge) pauses the capture, and the user can resume, save
  what was captured, or discard.
- **Drafts.** `CaptureStore` keeps drafts under `<cache>/online-captures`,
  outside the library, capped at 200 pages. Drafts untouched for 7 days are
  swept at startup.
- **Notices.** `NoticeRules` classifies a guide's copyright notice. A
  restrictive one prompts before saving.

### Steam and VGMaps: fetched by the app

- `SteamClient` makes the Steam Web API calls with the user's key.
  `SteamAppIndex` matches a game to its Steam app by name; the index is
  kept 30 days. `SteamGuideFetcher` fetches a guide page and its images
  (at most 200), one request at a time and at least 1 s apart. Any failure
  but a missing image discards the draft.
- `VgMapsClient` fetches atlas pages at least 1 s apart. `VgMapsIndex` is
  kept 30 days and searched locally. `VgMapsSystemPage` parses a game's map
  table. `VgMapsSaver` saves chosen maps one at a time as image-map guides.
  GIF is converted to PNG through `IMapImageConverter`
  (`WinRtMapImageConverter`).

### Saving

- `OfflineCopyBuilder` writes captured pages as plain files the import
  already reads: `guide.txt`, or sibling HTML pages starting at
  `guide.html`. Each HTML page starts with a saved-from-URL comment. Links
  between pages are made relative and images local. Scripts, forms and
  handlers are dropped.
- Each copy gets `guide.css`, built from the embedded `Online/Styles/*.css`
  (`base.css` plus the site's sheet). The site's own CSS is never kept.
  These sheets must name no URL or font.
- `OnlineGuideSaver` builds the copy in a work folder and runs it through
  `GuideImportValidator`. It then publishes a new guide, or replaces the
  one saved before from the same site item (`FindSourcedGuideAsync`). The
  draft is discarded only after the save succeeds.
- Captured HTML is as untrusted as any import. It goes through the same
  scanner, limits and reader policy.

### Source fields

`GuideSource` is stored in `GuideSources`: site, item ID, URL, author,
notice, source title, saved time, source updated time, and captured and
total pages for a partial save. A saved item is found again by site and
item ID. Backups carry these fields.

## Metadata providers

- `ProviderGameImporter` (Core) adds a game from IGDB (`IgdbClient`, with
  tokens from `TwitchTokenSource`). It stores normalized metadata
  (`GameMetadataNormalizer`) and the Steam app ID IGDB returns.
- Artwork tries `SteamGridDbArtworkSource`, then `IgdbCoverArtworkSource`,
  through `FallbackArtworkSource`.
- `ManagedArtworkStore` validates artwork (PNG, JPEG or WebP, at most
  5 MB and 4096 px), stages it, and stores it as
  `artwork/<game>/<sha256>.<ext>`.
- `ProviderServices` (Production) builds one shared `ProviderHttp` and the
  clients on it.
- Credentials (IGDB client ID and secret, SteamGridDB key, Steam Web API
  key) are in `ProviderCredentials`. `ProviderCredentialFile` stores them
  as `providers.bin` in the data root, protected with DPAPI
  (`DataProtectionProvider("LOCAL=user")`) by
  `WindowsProviderCredentialStore`. They sit outside `library/`, so they
  are never backed up. `ToString()` masks them. Never log a credential or
  put one in an exception message.

### ProviderHttp

Every request the app itself makes goes through `ProviderHttp`:
- **Hosts:** https on the default port to `ProviderHttp.AllowedHosts` only.
  Add a host there, with a reason, when a source needs one.
- **Redirects:** at most 3, same host and https only. No cookies.
- **Concurrency:** one request at a time per instance.
- **Timeouts:** 15 s per request.
- **Rate limits:** a 429 is retried once if `Retry-After` is 2 s or less;
  otherwise it is `RateLimited`.
- **Body limits:** 1 MiB for JSON and 5 MiB for images by default.
- **Large downloads:** `DownloadAsync` streams to disk. The timeout covers
  the headers and each wait for more of the body, so a large map on a slow
  line still arrives. A failure deletes the partial file.
- **Errors:** `ProviderException` with a `ProviderErrorKind`, and messages
  that name the host, never a key.

The in-app browser is not covered by `ProviderHttp`. It reaches the
network as a browser does, limited by `OnlineSources` hosts.

## Cross-cutting

### Errors and user messages

- User-facing text is in Core, per area: `LibraryOpenMessages`,
  `TextGuideLoadMessages`, `HtmlGuideLoadMessages`, `PdfGuideLoadMessages`,
  `ProviderMessages`, `LibraryBackupMessages`, `ImportPresentation` and
  the `*Presentation` classes. They are unit tested. Add new messages there,
  not inline in XAML code-behind.
- Failures are typed: `GuideImportException` with `ImportIssue`,
  `LibraryOpenException` with `LibraryOpenIssue`, `LibraryRestoreException`,
  `ProviderException`, and the per-format load errors. The shell maps the
  issue to a message. It never shows a raw exception message that may hold
  a path or a key.
- The shell reports through one status `InfoBar` (`ShowStatus`,
  `ShowWarningStatus`, `ShowErrorStatus`, `ShowStatusAction`) and announces
  it to screen readers (`AnnounceStatus`). Route loads show
  `ShowRouteProgress`.
- Diagnostics written for installed tests record stages, types and
  HResults only, never locator text or messages.

### Progress and cancellation

Long work takes a `CancellationToken` and reports `IProgress<T>`
(`ImportProgress`, `LibraryRestoreProgress`, capture page *n* of *m*).
Once a publish or swap passes its point of no return, cancellation stops
applying, and the progress says so (`ImportProgress.Publishing`).

### Threading

- The UI thread owns all WinUI and WebView2 objects. Background results
  return to it through `DispatcherQueue.TryEnqueue`, or by awaiting on the
  UI synchronization context.
- File, hash, parse, database and decode work runs on the thread pool
  (`Task.Run`): repository calls, loaders, import, export and restore, TXT
  metrics and reflow planning, and map tile builds.
- Serialized on purpose: the repository write gate, `ProviderHttp`'s
  request gate, PdfPig extraction, PDF page loads (latest wins),
  `MapCacheQueue`, and `NavigationActionQueue`.

### Trimming and the portable build

- `Portable=true` publishes one self-contained exe. The Windows App SDK
  and .NET are bundled, extracted once per version to `%TEMP%\.net`,
  trimmed (`TrimMode=partial`), compressed and ReadyToRun.
- `EnableSingleFileAnalyzer` is on for both builds, so they restore the
  same packages and share `packages.lock.json`.
- Unused Windows App SDK components are excluded in the csproj. CI checks
  the bundle list (`assert_excluded_components.ps1`).
- **Trim-warning gate.** `tools/p1/assert_trim_warnings.ps1` fails the
  build on any trim or C#/WinRT warning not in
  `tools/p1/trim-warning-allowlist.txt`, and on any allow-list entry that
  no warning needs. The list allows only assembly-level `IL2104` entries
  for assemblies the project doesn't own. The project's own code gets no
  exceptions.
- JSON uses source-generated contexts (`CoreJsonContext`,
  `InfrastructureJsonContext`, `ProductionJsonContext`), so trimming keeps
  every member serialized. Add new payload types to the right context.

### Test hooks

Installed UI tests switch on optional behaviour (pauses, diagnostics
files, fixture providers) by creating a named event that `TestGate.IsOpen`
checks. Without the event the app behaves normally, and Public builds
compile the fixture paths out. Don't add test behaviour that runs without
a gate.
