# P1 technical design: local library and first usable reader

Status: design for S03–S17 and S20, updated 28 September 2026. The M0
contracts and PDF decision are implemented for review; see
[P1 results](p1/results.md).
The [high-level design](initial-design.md) defines R1–R9;
the [work breakdown](work-breakdown.md) owns story, task, and TR IDs; the
[implementation plan](p1/implementation-plan.md) orders the work. P0 was
merged in [PR #1](https://github.com/ilya-slalom/desktop-guides/pull/1).
The [P0 results](p0/results.md) and [reader decisions](p0/reader-decisions.md)
are inputs, including the PDF text-accessibility blocker.

## 1. Scope, assumptions, and decisions

- The initial release imports local TXT, one static HTML entry document with
  local image/CSS assets, and PDF. Multi-document HTML, OCR, in-guide search,
  accounts, and sync stay in P2/P3. An in-document HTML fragment link works;
  an unimported HTML target is reported as unavailable.
- P1 targets Windows 11 x64 first. Native Windows 11 ARM64 is a candidate
  because P0 ran Core and reader fixtures there; the full P1 app still needs
  its own install and workflow check. Windows 10 is deferred and is not an
  advertised target. The user deferred P0's clean-VM prerequisite check;
  T17.3 remains a release gate before claiming clean-install support.
- One interactive app process owns the library. No background sync or
  cross-process writer is designed. Imports copy files; readers never depend
  on the selected original after import. Completion is user controlled.
- Package identity must stay stable after the first public package.
  P0's `ReaderSpike` identity and bundled fixture picker are test assets,
  not the production identity or library UI.

| Decision | Practical alternatives | P1 choice and reason |
| --- | --- | --- |
| App structure | Grow the P0 fixture window, or introduce a shell and application services | Introduce a WinUI shell and retain P0 probes as test-only adapters. Fixture-specific controls and paths cannot become library dependencies. |
| Metadata | JSON files, or SQLite with constraints and transactions | SQLite through `Microsoft.Data.Sqlite`, behind portable contracts. Foreign keys, explicit migrations, and one write coordinator support independent guide state and recovery. |
| Files plus database | Assume one SQLite transaction covers files, or journal cross-boundary operations | Stage and rename app-owned directories, record pending file operations in SQLite, then reconcile on startup. A database transaction cannot roll back a filesystem rename. |
| Content ownership | Package local data alone, or add user export/restore | Use package `LocalFolder` for the live library and promote S20 to P1. Package local data persists through updates but is removed on uninstall; an archive saved outside app data provides recovery when the user exports it. |
| HTML | Reuse a broad virtual-host folder, or explicitly serve verified assets | Extend the P0 per-request allowlist and use a unique synthetic origin per guide. Preview discovers missing local assets before publication; every served byte comes from the managed copy. |
| PDF | Ship the P0 raster viewer with a note, or validate a text path first | The [T10.0 decision](p1/pdf-decision.md) selected a native `Windows.Data.Pdf` preview plus PdfPig text path after the tagged fixture appeared in Windows UI Automation and keyboard selection. T10.1–T10.3 must implement and verify the production adapter before S10/S17 pass. |
| Search | SQLite `LOWER`/`LIKE`, or compare title metadata in Core | Filter loaded title metadata using invariant, case-insensitive comparison, then sort and virtualize the view. No guide bytes are opened for library search. |

The new portable `DesktopGuides.Core` types describe games, guides, locators,
operations, and validation. A new `DesktopGuides.Infrastructure` project owns
SQLite, managed files, import, and archive I/O. `DesktopGuides.App` owns WinUI
views, pickers, WebView2, PDF presentation, and accessibility peers. Headless
integration tests reference Core and Infrastructure; UI automation runs on
Windows. Pin new package versions in `Directory.Packages.props` and lock files
when implementation begins. `Microsoft.Data.Sqlite` executes its async ADO.NET
methods synchronously, so run short database commands on a worker, never on
the UI thread. Use WAL for readers and one serialized writer.

```mermaid
flowchart LR
    Shell["WinUI shell<br/>Library • Game • Reader • Settings"]
    Services["Application services<br/>library • import • progress • backup"]
    Core["Core contracts<br/>models • locators • policies"]
    Infra["Infrastructure<br/>SQLite • managed files • operation journal"]
    Adapters["Reader adapters<br/>TXT • static HTML • PDF"]
    Shell --> Services
    Shell --> Adapters
    Services --> Core
    Services --> Infra
    Adapters --> Core
    Adapters --> Infra
```

## 2. Storage, identity, and recoverable file operations

### Live layout and path rule

For the packaged app, use
`ApplicationData.Current.LocalFolder.Path/DesktopGuides/` as an app-data
parent. Its live `library/` child contains `library.sqlite`,
`games/<game-id>/`, `content/<guide-id>/`, `.staging/<operation-id>/`, and
`.trash/<operation-id>/`. Sibling `.restore/`, `.recovery/`, and
`restore-state.json` support whole-library replacement without renaming a
directory into itself. Put transient WebView2 profiles under
`LocalCacheFolder`, outside backup scope. Tests inject a temporary parent.
Only a generated `Guid` in `"N"` form names a guide directory. Database paths
are forward-slash relative paths within that directory; no original absolute
path is stored. Keep a sanitized basename only as an optional source label.
After the first successful import, show a dismissible reminder that uninstall
removes the live library and that Export in Settings saves a user-owned copy.
Reject archive destinations inside the package's app-data parent.

Resolve a stored path by checking each segment for empty, `.`, `..`, colon,
backslash, NUL, drive or UNC syntax, and reparse points. Canonicalize under
the expected guide root, require the result to stay inside it, and repeat the
link check immediately before opening. The attacker model covers hostile
imported paths and static assets; a separate local process racing filesystem
renames is outside P1. Never recurse into unknown directories during cleanup.

### Initial schema and invariants

Schema v1 establishes the original tables below; schema v2 adds the
last-opened-time index. T04.4 adds schema v3 with `GameMetadataLinks` and the
`AddGameMetadata` journal kind. New databases apply all available versions in
order and, after T04.4 lands, finish with `PRAGMA user_version=3`. Use `Guid`
text IDs, UTC Unix-millisecond integers, parameterized SQL, `CHECK`
constraints for bounded strings and fractions, and
`PRAGMA foreign_keys=ON` on **every** connection. `ON DELETE CASCADE` handles
dependent rows inside the database; only application services may invoke
deletes because files need separate coordination.

| Table | Required fields and constraints |
| --- | --- |
| `Games` | `Id` primary key, trimmed editable `Title` (1–160), optional editable `Platform` (up to 80) and `Notes` (up to 2,000), `CreatedUtcMs`, `UpdatedUtcMs`. |
| `GameMetadataLinks` | `GameId` primary/foreign key, bounded `Provider` and `ExternalGameId` with a unique pair, normalized canonical title/platform/release/developer/publisher snapshot, optional managed artwork relative path, and `FetchedUtcMs`. Provider-specific payloads are not persisted without a separately bounded, versioned contract. |
| `Guides` | `Id` primary key, `GameId` foreign key to Games, title (1–200), format enum, unique `ManagedRelativeRoot`, `PrimaryRelativePath`, `ContentSha256`, `ContentBytes`, optional source label, nullable TXT-only `TextCodePage` (437 or 1252), `ImportedUtcMs`, `UpdatedUtcMs`. |
| `ReadingStates` | `GuideId` primary/foreign key, nullable versioned locator JSON, nullable estimated fraction in `[0,1]`, nullable `LastOpenedUtcMs`, nullable `CompletedUtcMs`. A new guide has no estimated fraction. |
| `ReaderPreferences` | `GuideId` primary/foreign key, nullable TXT/HTML font scale within the permitted range. |
| `Settings` | Key/value rows for system/light/dark theme and last active guide ID; a stale guide ID is ignored at launch. |
| `FileOperations` | Operation ID, `Import`/`AddGameMetadata`/`DeleteGuide`/`DeleteGame`, `Prepared`/`Committed` phase, validated JSON manifest of generated game or guide IDs and app-owned paths, creation time. Stable for startup recovery. |

Create `Guides(GameId)` in v1, `ReadingStates(LastOpenedUtcMs)` in v2, and the
provider/external-ID unique constraint with `GameMetadataLinks` in v3. Do not
duplicate a global reading position on Game or filename.
Invariant/culture-independent title comparison is performed on metadata in
Core so non-ASCII case behavior does not depend on SQLite's built-in
`NOCASE`; paginate **after** filtering. Reassess query strategy only if
measured libraries make this too slow. For TXT/PDF, `ContentSha256` is the
copied file hash. For HTML, it is SHA-256 of a canonical sorted list of
managed relative paths and each file's SHA-256, so an asset change also
changes the guide fingerprint.

### Cross-boundary protocol

All mutating operations acquire a single library write gate. An **import**
commits a `Prepared` FileOperations row, copies and hashes source bytes under
`.staging`, validates them, renames the staged guide directory into `content`
on the same volume, then inserts Guide and empty state rows and removes the
operation row in one SQLite transaction. An exception rolls back owned files.
At startup, a prepared import with no Guide row removes only its named staged
or newly moved directory; a committed Guide row is never removed by a janitor.
The UI lists only committed Guide rows.

A provider-backed game addition uses the same publication boundary. It writes
a prepared `AddGameMetadata` operation for one generated Game ID, downloads
optional artwork only into its named staging root, validates and decodes the
bounded image, and renames the generated game root into `games/<game-id>` on
the same volume. One SQLite transaction then inserts `Games` and
`GameMetadataLinks` and removes the operation row. If there is no artwork,
the manifest has no owned path and publication is database-only. At startup,
a prepared operation with no Game row removes only its exact staging or final
game root; a committed Game row is retained. Cancellation cleans up the
prepared row and its exact owned paths before returning control to the dialog.

A **guide or game deletion** first writes a `Prepared` operation naming all
owned guide IDs and, for a game, its exact generated `games/<game-id>` root,
moves their directories to `.trash/<operation-id>`, then deletes metadata and
marks the operation `Committed` in one transaction.
If move or commit fails, restore any moved directories and keep metadata.
At startup, `Prepared` deletion with remaining database rows restores files;
`Committed` deletion removes its exact trash paths and then its operation
row. A missing file does not justify deleting other files. This is
recoverable, not a claim of one atomic transaction across SQLite and NTFS.
Cancel occurs before the operation is written and changes nothing.

For T15.2, `ManifestJson` v1 contains `schemaVersion`, canonical `"N"` guide
IDs, and the expected library-relative `ownedPaths`. An import names one
`.staging/<operation-id>/<guide-id>` and `content/<guide-id>` pair. A
`DeleteGuide` names one `content/<guide-id>` and
`.trash/<operation-id>/<guide-id>` pair; a v1 `DeleteGame` names one or more
guide pairs.

T04.4 adds manifest v2 without reinterpreting v1. V2 retains `guideIds` and
adds canonical `"N"` `gameIds`. `AddGameMetadata` requires exactly one Game
ID, no Guide IDs, and, when artwork is present, one
`.staging/<operation-id>/<game-id>` and `games/<game-id>` pair. A v2
`DeleteGame` requires exactly one Game ID, its current Guide IDs, their guide
pairs, and the generated game root when present. The parser continues to
accept pending v1 operations after migration. Recovery derives paths from IDs
and requires the manifest paths to match exactly. It rejects unknown versions,
duplicate IDs or claims, unsafe paths, incompatible row phases, and
conflicting directory states before changing files. A nested filesystem link
blocks that operation without being followed.

Recovery compares committed `Guides.Id` and `Games.Id` values as parsed GUIDs,
since the SQLite schema permits uppercase spelling of a generated ID. An
unparseable or duplicate logical ID stops cleanup before any owned tree
changes. Orphan counts use the same GUID identity for content and game
directory names.

The startup reconciler preflights every journal row, then resolves rows in
creation order. It removes only the named roots for a prepared import with
no committed Guide; removes only the named roots for a prepared metadata add
with no committed Game; restores named trash roots for a prepared deletion
whose Games or Guides still exist; and removes named trash roots for a
committed deletion whose target rows are absent. It clears each row after its
filesystem work, so a crash can be retried. It leaves untracked entries under
`games`, `content`, `.staging`, and `.trash` untouched and reports their count
for a later `Review orphan` UI. A collision or malformed row stops startup
recovery while preserving the journal and files for repair.

On startup, run schema validation/migration, reconcile known operations,
then detect untracked directories under `games` and `content` without
deleting them. Surface missing/corrupt managed artwork and guides as
repairable rows. If the database is corrupt, stop publication and offer the
recovery copy; never initialize an empty replacement over the existing file.

### Migrations and backup

Before a schema upgrade, create a consistent recovery database with
`SqliteConnection.BackupDatabase`; a plain copy of a live WAL database is
insufficient. Apply each forward migration and `user_version` change in one
transaction, then run both `integrity_check` and `foreign_key_check`. Failure
rolls back and leaves the old file/copy for recovery. A populated v1 fixture
must upgrade to v2 with the last-opened index and all rows intact. T04.4 adds
a populated v2-to-v3 fixture that proves existing games and guides survive,
the metadata-link table and unique provider key are present, and a failed v3
migration preserves the v2 database or recovery copy.
Reject filesystem links at `library.sqlite`, its `-wal`, `-shm`, and
`-journal` sidecars, and their parent paths before each database open. On
Windows, also reject any of these files with multiple hard links, using a
file handle to read the NTFS link count; the reparse-point attribute alone
does not identify hard links. For each supported version, compare app-owned
table and index definitions with a reference database built from that
version's migration scripts. This checks columns, constraints, and index
definitions that object-name checks miss. Exact matching deliberately
rejects manual schema alterations while preserving their data for recovery;
SQLite's internal `sqlite_*` objects are excluded.
Do not execute SQLite I/O on the WinUI dispatcher.
Keep named migration recovery copies under the app-data `.recovery/` sibling
of the live library, so a later whole-library replacement cannot remove them.

## 3. Reader contract, locators, and UI state

Use a typed reader interface in App because `FrameworkElement` is WinUI
specific. Its model arguments and results live in Core:

```csharp
interface IReaderAdapter : IAsyncDisposable
{
    GuideFormat Format { get; }
    ReaderCapabilities Capabilities { get; }
    FrameworkElement View { get; }
    event EventHandler<LocationChangedEventArgs> LocationChanged;
    Task OpenAsync(ManagedGuideSource source, CancellationToken token);
    Task<ReaderLocation> GetLocationAsync(CancellationToken token);
    Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location,
                                              CancellationToken token);
    Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token);
}
```

`ManagedGuideSource` is created only by the resolver after a committed Guide
lookup; it is never supplied by a web page. Capability flags cover text size,
page jump, fit width, find, and selectable text. The shell composes commands
from flags; unsupported commands are hidden or announced unavailable.
Adapters send movement notifications, but never write SQLite directly.
Opening a new guide increments a reader-session generation; obsolete
captures/renders from an old generation cannot overwrite new guide state.

`ReaderLocation` is a bounded JSON envelope with `format`, `schemaVersion`,
`contentSha256`, `payload`, and `estimatedFraction`. Store at most 4 KiB of
locator JSON; reject non-finite numbers, unknown path segments, wrong format,
and oversized quotes. TXT payload is normalized UTF-16 offset plus context
quote; HTML is relative document, ID/text context, delta and scroll fraction;
PDF is zero-based page plus within-page fraction. `RestoreOutcome` is
`Exact`, `Context`, `Approximate`, or `Unavailable` with an optional user-facing
reason. Unknown future versions yield `Unavailable` and leave the guide open
at a safe start. Invalid values are clamped or rejected without changing
completion.

Debounce meaningful movement for roughly one second and guarantee that a
normally running app writes the latest changed location within five seconds
of the last movement. Serialize captures and writes by guide/session; flush
on navigation, window deactivation, and closing. Windows App SDK desktop
apps do not use UWP automatic suspension, so use window activation and
`AppWindow.Closing` rather than a UWP suspend hook. Never write on every
scroll event. An empty ReadingState is displayed as `Not started`, not 0%.

## 4. WinUI shell and interaction direction

The visual direction is a quiet reading workspace: a compact `NavigationView`
for Library/Settings, a clear Game title and guide list, and a reader surface
that uses most of the window. Favor native WinUI typography, spacing, theme
brushes, and visible focus states. Avoid fixture terminology in production
copy. At narrow widths collapse the navigation pane before squeezing text;
the reader toolbar wraps or moves low-priority commands into an overflow menu.
High contrast uses system brushes, never hard-coded HTML colors without a
matching local style. A screen-reader announcement distinguishes approximate
restore, missing managed content, and completion changes.

Use the native WinUI `TitleBar` as the window drag region and host the Back and
pane-toggle actions there. Continue using `AppWindow` for close coordination
and window lifecycle. Apply a Mica system backdrop to the long-lived main
window and keep route backgrounds transparent so the material is visible;
high contrast replaces it with the system window color. Acrylic is reserved
for transient flyouts or light-dismiss overlays rather than persistent reader
or catalog surfaces.

The shell and each route fill the available width after `NavigationView` and
responsive page padding. Do not apply a global content maximum that creates
unused vertical gutters on wide windows. A format adapter may constrain an
individual prose column when line length requires it, while metadata surfaces,
catalog rows, toolbars, and reader hosts still occupy the route width.

Present loading states and actionable feedback with a native `InfoBar` above
the active route. Loading remains visible until replaced. Routine ready
messages close after three seconds; warning and error messages remain
dismissible. Preserve the last status through the shell automation state so
installed tests can synchronize without keeping diagnostics visible. Keep
that retained marker in the raw UI Automation view, outside normal assistive
technology views, and include a monotonically increasing sequence so a test
cannot accept an older completion state.

The design language is a quiet field guide for game reference material.
Catalog views let cached game artwork carry the strongest color and visual
identity. Guide lists use denser flat rows, restrained separators, and a clear
reading-state marker. Reader chrome stays visually subordinate to the guide.
Use Segoe UI Variable with the system UI font fallback for shell text and the
selected fixed-width system font only for preformatted TXT. Headings, labels,
and actions remain left aligned.

Implement semantic XAML resources for canvas, surface, elevated surface,
primary/secondary text, accent, selection, success, warning, and destructive
states; spacing and corner-radius steps; and title, subtitle, body, caption,
and reader-command styles. Light references start with canvas `#F8F9FB`,
surface `#FFFFFF`, primary text `#1C1C1C`, secondary text `#616161`, and the
system accent with `#0067C0` as a design reference. Runtime values bind to
WinUI theme and high-contrast resources rather than fixing those light values
in page XAML. Cards identify artwork or meaningful groups; dense guide data
uses rows rather than a uniform card treatment.

Use the [WinUI Gallery](https://github.com/microsoft/WinUI-Gallery) as an
interactive catalog and source reference, not as an application dependency or
a shell to copy. Review examples against the project's locked Windows App SDK
version before adopting them. Prefer built-in WinUI controls and resource
keys where they provide the required behavior. A stable Windows Community
Toolkit control is appropriate when it supplies a complete accessible pattern
that the app would otherwise rebuild. Each Toolkit package requires a recorded
need, license/version review, central version lock, and installed Windows
check. Copy only the minimal pattern needed and adapt automation names, focus
behavior, theme resources, copy, and layout to Desktop Guides.

Adoption occurs with the feature that first needs the pattern:

| Stage | Task | Gallery-informed UI work |
| --- | --- | --- |
| Foundation | T11.4 | Semantic resources, type/spacing scales, full-width responsive routes, native `TitleBar`, Mica window backdrop, transient `InfoBar`, control-state rules, and representative Library/Game/Reader/Settings screenshots. |
| Provider addition | T04.4 | Search entry, cancelable progress, edition result rows, artwork fallback, `InfoBar`/validation feedback, manual fallback, and dialog focus restoration. |
| Catalog and import | T05.4, then T05.1/T06.1 | Reusable artwork and metadata templates, virtualized rows, empty/loading/error states, import preview groups, progress, and confirmation. |
| Reader and Settings | T14.4 | Theme-aware command presentation, appearance settings, teaching/status surfaces, and Settings groups after functional controls exist. |
| Release audit | T16.2 | Keyboard, UIA, touch target, localization/overflow, theme, high contrast, DPI, and screenshot review of the complete flow. |

The application remains on stable Windows App SDK `2.5.1`. Toolkit packages
use stable `8.2.251219`; preview `8.3` and Gallery experimental-SDK controls
are excluded from P1. T11.4 introduces
`CommunityToolkit.WinUI.Controls.SettingsControls` for `SettingsCard`. Other
packages are added only with the task that first uses them:

| Task(s) | Toolkit component | Package | Use and boundary |
| --- | --- | --- | --- |
| T11.4, T14.4, T20.2 | `SettingsCard`, `SettingsExpander` | `CommunityToolkit.WinUI.Controls.SettingsControls` | Use cards for actionable or informative Settings rows and expanders for optional advanced groups. Do not turn catalog rows or reader content into settings cards. |
| T04.4, T05.1, T05.4 | `MetadataControl` | `CommunityToolkit.WinUI.Controls.MetadataControl` | Flatten short platform, edition, provider, format, and reading-state facts into accessible text. Keep artwork, title, selection, and virtualization in the owning data template. |
| T06.1 | `HeaderedContentControl` | `CommunityToolkit.WinUI.Controls.HeaderedControls` | Associate repeated import-preview groups with visible headings. Use native headings when only one group exists. |
| T13.1, T14.2, T19.2 | `Segmented` | `CommunityToolkit.WinUI.Controls.Segmented` | Present two to five bounded, mutually exclusive states. Verify selected-state UIA and retain native radio-button behavior as the fallback. |
| T18.2 | `GridSplitter` | `CommunityToolkit.WinUI.Controls.Sizers` | Make the optional table-of-contents pane resizable without changing its collapsed default. Do not place a splitter in the primary reading surface before the pane exists. |
| T22.2 | `RichSuggestBox` candidate | `CommunityToolkit.WinUI.Controls.RichSuggestBox` | Use only when a selected source supports cancellable incremental suggestions within its rate and credential model. T04.4 provider search remains explicit-submit. |

Native WinUI remains the chosen implementation for `NavigationView`,
`TitleBar`, `CommandBar`, `AutoSuggestBox`, `InfoBar`, `ContentDialog`,
`TreeView`, `NumberBox`, progress, file pickers, and keyboard accelerators.
The main window uses Mica; default or explicit Acrylic remains limited to
transient UI. Toolkit
animation, converter, primitive, media, color, token, and tabbed-command
packages have no current requirement and are not added speculatively.

The recorded UI tasks were reviewed as one set:

| UI task(s) | Component decision |
| --- | --- |
| T04.1–T04.3 | Keep the native form controls, `MenuFlyout`, and `ContentDialog`; Toolkit settings controls would misrepresent editing and destructive actions. |
| T04.4 | Add Toolkit `MetadataControl` for bounded edition/provider facts. Keep provider search as an explicit native search action so typing does not produce network requests. |
| T05.1–T05.4 | Reuse `MetadataControl` inside virtualized data templates. Native list/grid, search, selection, and state surfaces remain responsible for browsing behavior. |
| T06.1–T06.4, T07.2 | Add `HeaderedContentControl` only for repeated import-preview groups. Keep file picking, validation `InfoBar`, progress, encoding choice, duplicate choice, and confirmation native. |
| T07.3, T09.1–T09.3 | Keep WebView2 security, external-link actions, and host-owned appearance in the reader adapter; no Toolkit control changes the trust boundary. |
| T08.2–T08.3 | Keep the virtualized TXT surface and reader commands native so long-guide realization and stable locators remain under app control. |
| T10.2 | Keep PDF movement, `NumberBox` page entry, zoom, fit, and `CommandBar` overflow native. |
| T11.1–T11.4 | Keep `NavigationView` and `CommandBar`; move shell Back and pane-toggle actions into native `TitleBar`, use Mica behind transparent route backgrounds, fill the available width, and auto-hide routine native `InfoBar` messages. |
| T11.4 | Use `SettingsCard` for the local-storage row and retain semantic app resources around it. |
| T13.1 | Use `Segmented` for the two explicit completion states if UIA selection passes; keep a native radio fallback. |
| T14.1–T14.4 | Use `Segmented` for System/Light/Dark and `SettingsCard`/`SettingsExpander` for Settings. Keep reader text-size commands native. |
| T15.1, T15.3 | Keep actionable `InfoBar` and destructive `ContentDialog` behavior native. |
| T16.1–T16.2 | Audit Toolkit controls together with native controls; keyboard accelerators and focus restoration stay app-owned. |
| T20.2 | Put Export and Restore in `SettingsCard` rows, with optional details in `SettingsExpander` and native replacement confirmation. |
| T18.1–T18.2 | Keep find UI and TOC `TreeView` native; add `GridSplitter` if the TOC pane becomes user-resizable. |
| T19.1–T19.2 | Keep bookmark list/navigation native and use `Segmented` for Original/Reflow. |
| T21.2 | Select a Toolkit media control only after a chosen format proves a need; no generic media package is preselected. |
| T22.2 | Evaluate `RichSuggestBox` for a provider that supports safe incremental suggestions; otherwise reuse T04.4 explicit search. |
| T23.2, T24.1–T24.2 | Keep conflict, window, controller, and annotation UI unassigned until those interaction models are designed. |

`NavigationView` does not manage a back stack automatically. A shell
navigation coordinator owns `Library`, `Game(gameId)`, `Reader(guideId)`, and
`Settings`, keeps selected IDs stable across edits, and returns Reader to its
own Game. Launch opens Library and may show a `Resume last guide` action; it
does not auto-open that guide. A dialog or overlay returns focus to its
invoking control. Every shortcut has a visible matching command.

`AppInstance` registration in the production entry point selects one process
to own the library. The provisional package registers launch activation only.
Use a synchronous `[STAThread]` entry point so WinUI creates its window on an
STA thread. A duplicate launch connects to a same-user named pipe owned by
that process. The UI callback activates the window and writes acceptance to
that launch's connection before it can process a later close. A failed or
closed connection causes the secondary to retry owner selection; a received
acceptance ends the secondary launch even if the user then closes the window.
File and protocol activation will need explicit payload forwarding when those
extensions are added. Verify launch behavior on the supported targets.

On window close, stop accepting navigation, reject pending launch connections,
and unregister the instance key so a new launch can own it. Await
initialization and queued actions, then dispose the repository and close.
This may briefly defer closing while a database write finishes; the old and
new processes can overlap during this handoff. The installed smoke checks
acceptance, both close boundaries, and a new window opening while an old guide
action is blocked by a test-held write lock.

The P0 fixture picker and test assets remain available only in a separate
CI/development probe mode. A production MSIX does not bundle the P0 corpus
or expose fixture controls. CI must keep one diagnostic package lane for P0
reader regressions and add a production-mode library/import workflow lane.
For T11.1 the existing `DesktopGuides.App` project is the diagnostic package,
and a separate WinUI production project owns the new shell. The production
project uses a provisional package identity until T17.1 sets the public one.

## 5. Security, failure, and acceptance budgets

- Import uses cancellation-aware streaming copies and SHA-256. The starting
  validation limits are TXT 64 MiB, PDF 1 GiB, HTML entry 16 MiB, 2,048 static
  assets, 32 MiB per asset, and 256 MiB total HTML tree. These are explicit
  P1 safety bounds, shown in preview errors and revisited with real samples.
  They are not claims about every guide on the internet.
- Accepted static HTML support is the entry `.html`/`.htm` document, local
  `.css`, and PNG/JPEG/GIF/WebP images. SVG, scripts, frames, fonts, media,
  forms, external assets, and additional HTML documents are unsupported in
  P1; import warns about references and the reader blocks them. Use an HTML
  and CSS parser, not regex alone, to discover `src`, `srcset`, CSS `url()`,
  and nested `@import`. Pin and license-review parsers before T07.1 is done.
- WebView2 loads a synthetic HTTPS origin unique to one managed guide. The
  P0 request-handler allowlist, hash check, navigation/popup blocking, denied
  permissions/downloads, disabled document scripts/host objects/web messages,
  and fresh-profile canary tests remain mandatory. Fixed host DOM scripts
  check the current origin and validate returned JSON before storage.
- Service errors have stable codes (`UnsupportedFormat`, `MissingSource`,
  `EncodingChoiceRequired`, `UnsafeAsset`, `MissingManagedFile`,
  `StorageUnavailable`, `PasswordRequired`, `PdfTextUnavailable`). UI copy
  gives one action, such as Retry, Choose encoding, Remove broken guide,
  Repair WebView2, or Export recovery copy. Logs omit guide text, passwords,
  original absolute paths, and WebView2 profile contents.
- [T10.0](p1/pdf-decision.md) compares a WebView2 PDF surface and a separately licensed text
  extraction/rendering path against `pdf-access`, `pdf-scan`, `pdf-locked`,
  `pdf-long`, offline use, page-fraction restore, selection, Narrator/UIA text,
  and redistribution. A hybrid P0 raster preview plus a selectable native
  text view is eligible only if the tagged fixture has credible reading
  order. A scanned fixture needs an honest image-only label; OCR is P2.
  Record the chosen engine and a failed-candidate matrix before T10.1.
  If no candidate passes, S10 and S17 stay blocked; PDF is not silently
  removed from R2.

## 6. Task design: persistence, library, and import

### S03 — Persist the local library

- **T03.1** Add Core records and repository interfaces for Game, Guide,
  ReadingState, ReaderPreferences, and Settings; implement the schema above
  in Infrastructure. Use generated IDs and UTC timestamps from an injected
  clock. The repository exposes guide-scoped state and transactional
  service methods, not public `DeleteGame` SQL. Tests insert two guides under
  one game and show that their locators and completion fields cannot collide.
- **T03.2** Add ordered migration scripts keyed by `user_version`, backup
  before migration, transaction rollback, and integrity/foreign-key checks.
  Keep a populated schema-v1 fixture and upgrade it to v2; inject a migration
  failure midway and verify the original data or recovery copy still opens.
  Unknown newer schema versions fail with a clear "update the app" message
  instead of downgrading or creating an empty database.
- **T03.3** Add `ILibraryPaths` and `ManagedPathResolver` with an injectable
  test root. It creates only app-owned directories and returns paths beneath
  the generated guide ID. Test `..`, absolute, UNC, mixed slash, encoded
  separator, symlink/junction, missing segment, and a normal nested asset on
  Windows. The resolver is reused by import, readers, backup, and deletion.

### S04 — Manage games and their guides

- **T04.1** Add `Add game` and `Edit game` WinUI dialogs with trimmed title,
  optional platform/notes, length feedback, and Save disabled until valid.
  Duplicate titles are allowed because distinct editions/platforms can share
  a name; stable IDs disambiguate them. Cancel closes without a repository
  write. Test keyboard submission, errors, and Unicode names. Use this dialog
  as T04.4's manual/offline fallback and local-override editor.
- **T04.2** Show game details and rename/remove actions bound to the game ID.
  A rename updates `UpdatedUtcMs`, keeps guide IDs and reading state intact,
  refreshes Library/Game breadcrumbs, and preserves list selection. An
  interrupted update shows a retryable message instead of returning to a
  stale view.
- **T04.3** Count affected guides before a removal dialog and require the
  user's explicit Remove action. Pass game ID plus expected count to the
  service; if the count changed, refresh the dialog rather than deleting an
  unexpected set. Use the multi-guide trash protocol in section 2. Verify
  confirmed, canceled, failed-move, failed-commit, and startup-recovery
  cases; remove the exact provider snapshot and managed artwork root, and
  touch no unrelated game directory.
- **T04.4** Record a provider decision covering catalog/edition coverage,
  public-client authentication, licensing, attribution, rate limits,
  availability, and artwork terms. A provider requiring a confidential
  credential must use an approved service boundary or be rejected; never
  ship that secret in the desktop package. Add the schema-v3 migration,
  preserve pending manifest-v1 operations, and introduce manifest v2 with
  explicit Game IDs. Add a provider-neutral Core contract with bounded,
  cancellation-aware search and detail/artwork retrieval. The Add game dialog searches online,
  distinguishes editions by platform/release data, and offers
  `Create manually` at all times. Selecting a result allocates a local Game
  ID, stages and validates bounded artwork, then publishes the Game, unique
  provider link, normalized snapshot, and managed artwork through the
  recovery journal. Store no remote URL as an offline display dependency.
  Explicit refresh updates the source snapshot while preserving editable
  local title/platform/notes. Test v2 migration and rollback, duplicate
  provider IDs, cancellation, timeout, rate limiting, malformed/oversized
  metadata and images, provider outage, offline fallback, crash recovery,
  refresh overrides, and relaunch without networking.

### S05 — Browse and find library entries

- **T05.1** Render a virtualized Game collection ordered by last activity,
  then title and stable ID. Game detail rows show guide title, format,
  last-opened local display time, nullable estimated percentage, and explicit
  completion state. Use data-bound view models, not guide-file parsing during
  listing. Verify large synthetic metadata lists keep bounded realized UI
  items.
- **T05.2** Filter game and guide titles with Core's invariant
  case-insensitive comparison; debounce input only for UI work, not network
  calls. Provide distinct empty-library, loading, and no-results views and
  a visible Clear search action. Unread rows say `Not started`; `0%` appears
  only after a real saved estimate. Test mixed-case and non-ASCII titles.
- **T05.3** Route by IDs rather than row indices. After rename, deletion, or
  import, preserve the selected game/guide when it still exists; otherwise
  move focus to the nearest safe list row or heading. Restore the prior
  Library query on Back and prevent a stale async refresh from replacing a
  newer selection.
- **T05.4** Extract the provider result's artwork, metadata, progress, status,
  and error presentations into shared styles and data templates after T04.4
  proves their real content. Reuse them in Library, Game, and import surfaces.
  Keep item containers virtualizable, provide a deterministic missing-artwork
  state, and test long titles, missing metadata, localization expansion, and
  disconnected rendering. Do not create custom control wrappers for a style
  or template that standard WinUI controls already support.

### S06 — Import without partial records

- **T06.1** Use a packaged-desktop file picker owned by the current window.
  Open a preview scoped to a selected Game ID, with a suggested title,
  detected format, source basename, size, TXT encoding choice, and HTML
  asset warnings. The picker or preview may be canceled without creating a
  FileOperation or managed directory. A progress view offers Cancel during
  copy and validation.
- **T06.2** Validate extension **and** readable format. TXT tries BOM and
  strict UTF-8, then requires an explicit CP437/Windows-1252 choice when
  decode fails; store the code page. HTML runs the static dependency scan
  from S07. PDF checks readable pages and whether a password is required;
  a password is never persisted. If the selected PDF engine cannot handle
  an encrypted file, reject it before publication with a concrete reason.
  Produce a typed validated ImportManifest rather than passing raw picker
  paths into reader code.
- **T06.3** Allocate Guide and Operation IDs, commit the prepared journal row,
  stream-copy into `.staging`, compute SHA-256 and byte counts, validate again
  after copy, rename to the final generated directory, then publish all
  metadata in one transaction. A successful import opens after the original
  is moved or deleted. Inject cancellation, disk-full/copy error, hash
  mismatch, rename error, and failed SQL commit; no half-created Guide is
  listed and all app-owned residuals are reconciled.
- **T06.4** Compare fingerprint plus format within the selected game before
  publishing. Show `Open existing` or `Import another copy` for an unchanged
  duplicate; never overwrite in place. A second copy gets a new Guide ID,
  so its reading state is independent. Missing, unsupported, encrypted, and
  unreadable sources have distinct error codes. Replacement of an existing
  guide's bytes is a later explicit workflow, not a side effect of import.

### S07 — Keep imported HTML static and offline

- **T07.1** Parse the selected entry HTML and reachable local CSS into a
  bounded dependency graph. Enumerate relevant `src`, `srcset`, stylesheet
  links, inline CSS `url()`, and nested `@import`; normalize fragment/query
  handling before resolving a path. Hash supported local CSS/images in a
  preview manifest with safe relative names; T06.3 stages the verified bytes
  after T07.2 implements the filesystem boundary. Cycles terminate
  through a visited set; excess depth/count/bytes yields a preview error.
  Pin and license-review the chosen HTML/CSS parser and test nested CSS and
  image variants.
- **T07.2** Resolve every referenced path against the selected HTML root,
  reject `..`, absolute paths, encoded escapes, symlink/junction traversal,
  unsupported type, and case-colliding destinations. Report missing files
  and blocked remote references in preview before Confirm. Revalidate
  staged paths and hashes before publishing; a source asset changed during
  preview cannot silently change the imported result. For a selected entry
  with `%` in its filename, map only its matching `_files` companion folder
  to a safe managed directory. Retain each included asset's validated URL
  request path separately from its managed-file path.
- **T07.3** Serve only a per-guide manifest allowlist from WebView2's
  `WebResourceRequested` handler at the unique synthetic origin. Validate
  and decode request paths once, then map any allowed companion URL to its
  staged managed path; never use a request path for disk access. Deny all
  other requests, navigation, new windows, permissions, and downloads;
  disable document scripts, host objects, and web messages. Internal
  fragments remain in the view. An external URL is canceled and shown in an
  app-owned confirmation bar; only its explicit Open action invokes the
  system browser. A fresh-profile canary test covers HTML attributes,
  CSS imports, redirects, and attempted cross-guide URLs with zero
  guide-originated network requests.

## 7. Task design: reader adapters and shell

### S08 — Read legacy TXT faithfully

- **T08.1** Extract P0 decoding and `TextGuideDocument` into the managed-guide
  reader path. Normalize CRLF/CR to LF after decode, preserve spaces/tabs,
  use UTF-16 offsets, and persist the chosen encoding with Guide metadata.
  A BOM wins over a stored fallback code page; a contradictory metadata
  value is treated as a recoverable validation error. Test mixed newlines,
  CP437, Windows-1252, BOM, invalid UTF-8, and file truncation.
- **T08.2** Keep a native virtualized list and default monospace,
  no-wrap presentation with horizontal scrolling for diagrams. Retain one
  normalized text buffer and line-start index; provide visible line slices
  lazily so a 10 MiB guide does not duplicate one string per source line.
  Compare first-text time, realized controls, and window-response samples
  with P0 on the Windows 11 x64 reference host. A size-limit rejection is
  explicit; the reader never silently drops trailing text.
- **T08.3** Expose page up/down, start/end, and a normalized offset/context
  capture/restore path through `IReaderAdapter`. Exact unchanged-file
  restore returns within one visible logical line after width/font change;
  changed bytes try nearest matching context, then a labeled approximate
  fraction. Test repeated quotes, invalid offsets, and guide switching.

### S09 — Read imported HTML

- **T09.1** Bind the P0 restricted WebView2 surface to one committed managed
  Guide ID and its allowlist. Remove fixture staging and use an isolated
  cache profile that is disposed and later swept if WebView2 holds a lock.
  Refuse a missing/mismatched managed asset instead of falling back to a
  source path or network URL. TXT and PDF still open if WebView2 is missing.
- **T09.2** Apply a fixed app-owned local style for system/light/dark theme
  and bounded per-guide text scale; never download fonts or theme assets.
  Preserve the source's static structure where possible and use high
  contrast system choices. Test that changing theme/font while offline does
  not allow a CSS resource outside the guide root.
- **T09.3** Capture a validated, document-relative visible ID/text quote and
  delta with bounded scroll-fraction fallback. Restore after navigation and
  image settling, prefer element/context, then percentage; report
  `Approximate` when bytes or document path changed. Reject non-finite,
  oversized, wrong-guide, and cross-directory DOM results. Test wide/narrow
  reflow, delayed image layout, fragment navigation, and an unimported HTML
  link. P1 supports only the imported entry document and its fragments;
  S21 owns multi-document HTML.

### S10 — Read PDF manuals

- **T10.0** Run the decision matrix in section 5 before building the final
  adapter. Record the candidate/version/license, tested PDF hashes,
  UIA/Narrator text behavior, keyboard selection, offline status, page
  locator control, memory, and failure handling in `docs/p1/pdf-decision.md`.
  A tagged paragraph must be readable as document text; a scanned page
  must not falsely claim text. This is a blocking gate, not a release note.
- **T10.1** Implement the selected engine behind a `PdfReaderAdapter`.
  Reuse P0 page-cache logic only if compatible with the text path. Bound
  rendered images by measured bytes (initial cap 96 MiB), cache by document
  fingerprint/page/render width, and ignore stale render generations after
  rapid navigation or zoom. Dispose page/text handles on close. Test 200-page
  turns and a long session without retaining every page.
- **T10.2** Provide page count, validated page-number entry, Previous/Next,
  fit-width, zoom, and visible matching keyboard commands. Keep toolbar
  focus and reading focus separate so a page turn does not strand Narrator.
  Wrong password shows a retryable message; clear the attempted password
  after each attempt and never write it to storage/logs.
- **T10.3** Persist zero-based page index, within-page fraction, document
  fingerprint, and locator version through the common reader envelope.
  Clamp old/out-of-range pages to valid bounds and return `Approximate` on
  changed bytes. Restore page exactly and vertical fraction within 0.1
  page after resize/zoom on `pdf-long`; confirm the text view refers to the
  same selected page. OCR for `pdf-scan` remains out of scope.

### S11 — Provide a consistent reading shell

- **T11.1** Replace the fixture window with the `NavigationView` route
  coordinator in section 4. Library, Game, Reader, and Settings share one
  window and stable ID-based back navigation. Start at Library, show an
  optional Resume action, and handle missing last-guide IDs. Serialize user
  route intents, including last-guide persistence, so a delayed lookup or
  write cannot override a later selection. Forward duplicate launch requests
  before the library opens and drain pending navigation on close. Keep a
  build-time diagnostic probe mode for P0 CI without shipping fixtures in
  the production package.
- **T11.2** Define the typed reader adapter/capabilities in section 3 and
  test command dispatch with fake adapters. Only the active adapter knows
  its control tree; the shell receives commands, locations, progress, errors,
  and capability changes as typed values. The production TXT/HTML/PDF
  adapters implement this contract in M3; P0 diagnostic probes retain their
  existing interface until T11.1 separates the production shell.
- **T11.3** Build a compact reader title/back bar, collapsible navigation
  area, reader content host, status/approximate-restore announcement, and
  capability-based command slots. Narrow windows move secondary commands
  to overflow; essential Back and reader movement remain available. Verify
  Library → Game → Reader → Game by keyboard and pointer without losing
  selection or guide state. T05.3 verifies the Library query survives the
  Library → Game → Reader → Game → Library route after T05.2 adds search.
- **T11.4** Add app-level design resource dictionaries and document the
  semantic color, typography, spacing, shape, icon, motion, layout, and copy
  rules described in section 4. Build representative responsive states for
  Library, Game, Reader, and Settings with realistic guide metadata. Review
  WinUI Gallery patterns against Windows App SDK 2.5.1 and record built-in,
  adapted, rejected, and Toolkit-dependent candidates. Pin stable Toolkit
  `SettingsControls` and use `SettingsCard` for the representative local
  storage setting. Verify theme resource lookup, keyboard focus, the
  Settings-card UIA name and bounds, 200% display scaling, long text, narrow
  width, and high contrast on installed Windows. Capture screenshots for the
  implementation PR. For the initial T11.4 implementation, the user deferred
  the 200% display-scaling run to T16.2; the other checks still run now and
  the deferred gate must remain explicit.

## 8. Task design: tracking, preferences, and recovery

### S12 — Resume each guide independently

- **T12.1** Define the versioned locator envelope and format payloads in
  section 3 in Core, plus per-format parsers and `RestoreOutcome`. Exact
  fingerprint and context matches precede approximate fraction fallback.
  Tests round-trip all three formats and reject old/unknown versions,
  malformed JSON, out-of-range values, and wrong-guide document paths
  without touching completion.
- **T12.2** Put a `ProgressCoordinator` between adapters and ReadingStates.
  It observes movement, deduplicates unchanged anchors, debounces capture,
  and writes within five seconds after the final movement. Flush during
  Reader exit, `Window.Activated` loss, and window closing; a generation
  token prevents delayed writes to the next Guide ID. An injected clock and
  fake repository prove continuous scroll does not produce one write per
  event and two guides retain independent positions after restart.
- **T12.3** Estimate TXT fraction from normalized offset, HTML fraction
  from validated document/scroll information, and PDF fraction from
  `(pageIndex + pageFraction) / pageCount`, clamped to `[0,1]`.
  Store the content fingerprint with the locator. On changed bytes, ask
  the adapter for context restoration, then label a percentage-only result
  `Approximate`. `LastOpenedUtcMs` updates only on a successful open.

### S13 — Track completion explicitly

- **T13.1** Put `Mark complete` / `Mark in progress` in the Reader and
  guide-detail actions, using the same Guide ID service method. Update the
  library row and reader command from the committed state; announce the
  result to screen readers. Reaching the last line/page never calls this
  method. A disabled action explains a storage error instead of appearing
  to succeed.
- **T13.2** Change only `CompletedUtcMs` in one SQLite transaction and leave
  `LocatorJson`, estimate, and last-opened time intact. Marking in progress
  clears the completion timestamp; repeating the same action is idempotent.
  Tests toggle twice, restart, and verify that 100% estimated progress does
  not create a completion timestamp.

### S14 — Remember reader appearance

- **T14.1** Store per-Guide TXT/HTML text scale (initially 0.75–2.0 of the
  16-unit default, equivalent to 12–32 WinUI font units) in
  ReaderPreferences. Show Smaller/Larger and a current value with bounded
  steps; PDF uses its separate zoom value and is not written into text
  preferences. Test that changing one guide leaves another unchanged after
  restart and never collapses TXT whitespace.
- **T14.2** Store global `System`, `Light`, or `Dark` in Settings; System is
  the default. Bind WinUI brushes and fixed local HTML CSS to the effective
  theme; Windows high contrast takes precedence over the preference.
  Do not reference remote fonts, images, or styles. Test launch, theme
  change, and restart while disconnected.
- **T14.3** Capture a guide locator before a size/theme transition, apply
  appearance, wait for layout, and restore through the same adapter.
  TXT should remain within one logical line; HTML should return to the
  same context when it exists; PDF page/fraction behavior is unaffected.
  Record an approximate notice when a stable anchor cannot be found.
- **T14.4** Apply the design resources and reviewed Gallery patterns to the
  production Reader and Settings controls after T08–T10 and T14.2 establish
  their behavior. Keep content dominant, retain capability-based command
  overflow, and use settings/status/teaching patterns only where they clarify
  an action. Verify all reader formats plus Settings under system, light,
  dark, and high-contrast themes; test keyboard focus and supported text and
  display scales. Record any Toolkit dependency decision and capture
  installed-app screenshots.

### S15 — Recover from library and import errors

- **T15.1** Catch database, missing-file, invalid-content, and runtime
  failures at the service boundary and map them to stable error codes and
  actions. A missing managed file keeps its Guide row visible with `File
  missing` status and offers Remove; other guides still open. A bad
  database/migration shows recovery-file location and stops writes until
  repaired, instead of generating a new empty library.
- **T15.2** Reconcile only FileOperations-owned staging/trash paths on
  startup. Enumerate untracked directories for a diagnostic `Review
  orphan` count but do not auto-delete them. WebView2 cache/profile cleanup
  is separate and never follows links into content. Test interrupted import
  at each protocol phase and malformed/unowned directory names.
- **T15.3** Confirm guide removal with title and owned-file count, then use
  the single-guide trash protocol. Delete Guide, ReadingState, and
  ReaderPreferences in one database transaction; preserve unrelated Game
  and Guide rows. A failure before commit restores the directory, and a
  post-commit cleanup failure retains only a known trash entry for retry.
- **T15.4** Add fault-injection integration tests for copy failure, crash
  after stage, crash after rename, failed database commit, failed trash move,
  canceled removal, committed removal, and startup recovery. Verify both
  SQLite rows and exact app-owned paths after each scenario. Keep these
  tests headless; a smaller WinUI check validates error copy and focus.

### S16 — Support Windows input and accessibility

- **T16.1** Map `Ctrl+O` to Import in a Game context, `Ctrl+F` to Library
  title search, `Esc` to the active dialog/reader overlay, and Page Up/Down
  to the active reader's supported movement. Other contexts leave a
  shortcut unavailable without hijacking typed text. Document shortcuts
  in Settings/menus and test each with visible command parity.
- **T16.2** Audit keyboard tab order, visible focus, return focus after
  dialogs/overlays, AutomationProperties names/states, touch hit targets,
  Windows display scaling, high contrast, and Narrator on the real WinUI
  interface. TXT and static HTML document text must be reachable and read,
  not merely their toolbar labels. Record keyboard-only and screen-reader
  traces for Add game → Import → Read → Mark complete → Export.
- **T16.3** Re-run tagged and scanned PDF document-text checks on the
  selected T10.0 engine, including page changes and an error/password
  state. Record UIA text, selection, Narrator reading, and any honest
  scanned-image limitation in the user-facing help/release note. An
  inaccessible tagged PDF blocks S10/S17 even if the bitmap looks correct.

## 9. Task design: backup and release

### S20 — Export and restore a local backup

- **T20.1** Define a versioned ZIP manifest with archive version,
  application/schema version, export time, IDs, each relative managed path,
  byte length, and SHA-256. Hold the library write gate and reconcile all
  known FileOperations before taking a `BackupDatabase` snapshot and copying
  immutable managed files. If recovery is incomplete, fail export. The
  database and content must describe one state. Do not include original source
  paths, passwords, diagnostic logs, temp directories, or WebView2 profile
  data. Stream the archive into a temporary sibling of the user-selected
  destination and rename only after full checksum verification; cancellation
  removes that temporary output.
- **T20.2** Validate archive version, supported schema version, entry count,
  expanded size, duplicate and escaping names, hashes, database integrity,
  foreign keys, all managed-guide references, and every
  `GameMetadataLinks` artwork reference and manifest entry in a staging root
  before touching the live library. Canonical export destinations must be
  outside the package's app-data parent;
  the Settings flow and first-import reminder explain why. P1 choices are
  Cancel or Replace after a count summary; merging two libraries is deferred.
  For Replace, close database/readers, write a restore marker outside the
  live root recording whether a prior library exists, rename that root to
  `.recovery` if present, promote staging, reopen and verify, then remove the
  marker. On failure or interrupted startup, quarantine an unverified
  promoted root and restore the prior root, or an empty library if none
  existed. Test a clean restore, populated replacement, cancel,
  corrupt/truncated ZIP, zip traversal, oversized expansion, and a forced
  swap failure.

### S17 — Package and verify the MVP

- **T17.1** Choose the final MSIX identity/publisher before a public
  package, set four-part versioning and tested x64 output, and specify how
  Windows App Runtime and Evergreen WebView2 prerequisites are delivered
  online/offline. Use a protected signing service or secret-backed release
  job; keep private signing material out of source and logs. ARM64 output
  may be offered only after the complete P1 workflow passes natively.
  Preserve one package identity across upgrades and test version increase.
  The [release packaging procedure](p1/release-packaging.md) records the
  development-lane checks, prerequisite delivery, and remaining public
  identity decision.
- **T17.2** Keep locked restore, Core and Infrastructure headless tests,
  architecture-labeled package builds, and the P0 diagnostic fixture suite.
  Add a production-mode Windows UI workflow: create game, import each
  format, remove originals, restart, resume independently, toggle
  completion, export, restore, and remove. CI failing storage/security tests
  must block packaging; a failing UI workflow must block release promotion.
  Save anonymized fixture IDs, hashes, package and OS/CPU versions, timing,
  accessibility, and cleanup evidence. Trigger installed E2E work through
  an interactive scheduled task, including when SSH coordinates the local
  Windows host; installation and UI Automation run in its desktop session.
- **T17.3** On every promised target, install a signed release candidate,
  upgrade it, import guides, disconnect network, restart, resume, and
  restore a backup into a clean installation. Observe actual missing
  Windows App Runtime and WebView2 behavior and offline-installer recovery
  on a disposable runtime-free Windows 11 x64 VM before a clean-install
  claim; the user deferred that P0 check, so this lane remains pending
  until such a target exists. Start with Windows 11 x64. Windows 10 stays
  unadvertised; ARM64 is added only with its own native full-workflow
  result. Publish a tested matrix and the scanned-PDF/OCR limitation.
  Follow the [installed E2E procedure](p1/e2e-testing.md) for interactive
  task preflight, data protection, physical offline recovery, and evidence.

## 10. Verification gates and design references

Every P1 task closes with a code or documentation artifact, a named test or
manual trace, and a reviewable result. The [implementation plan](p1/implementation-plan.md)
lists dependencies and specific exits for all 53 tasks. Core/Infrastructure
tests run on the locked Windows CI toolchain; critical file-boundary and
symlink tests also run on Windows NTFS. UI Automation, Narrator, high
contrast, DPI, installed-package, and offline checks run on target Windows
sessions. `docs/p1/results.md` will record actual outcomes during
implementation; this design makes no unrun compatibility claim.

Primary references used for these decisions:

- [Microsoft: ApplicationData local data lifetime](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/store-and-retrieve-app-data)
  and [app data after uninstall](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes).
- [Microsoft: Microsoft.Data.Sqlite asynchronous limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async),
  [connection strings and foreign keys](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings),
  and [SQLite backup API](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup).
- [SQLite: foreign keys](https://www.sqlite.org/foreignkeys.html),
  [transaction atomicity](https://www.sqlite.org/atomiccommit.html), and
  [online backup](https://www.sqlite.org/backup.html).
- [Microsoft: desktop FileOpenPicker](https://learn.microsoft.com/en-us/windows/apps/develop/files/using-file-folder-pickers),
  [NavigationView](https://learn.microsoft.com/en-us/windows/apps/design/controls/navigationview),
  and [desktop lifecycle](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle).
- [Microsoft: WebView2 security](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security),
  [local content](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content),
  and [PDF toolbar options](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2pdftoolbaritems?view=webview2-winrt-1.0.4022.49).
