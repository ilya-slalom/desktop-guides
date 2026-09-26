# Desktop Guides: work breakdown

Status: P0 merged in [PR #1](https://github.com/ilya-slalom/desktop-guides/pull/1)
on 25 September 2026. The clean-VM prerequisite gate and Windows 10 check
remain deferred. P1 is designed in the [technical design](p1-technical-design.md)
and [implementation plan](p1/implementation-plan.md). Source:
[initial requirements and high-level design](initial-design.md). The backlog
defines the acceptance gates; [Windows results](p0/results.md) record
which checks have been performed. Windows 10 is deferred; native ARM64 CI
passed the installed reader fixtures. [P1 T10.0](p1/pdf-decision.md) selected
a text-capable PDF prototype; S10 remains open until the production adapter
passes its accessibility and installed-offline gates.

## How to use this backlog

- `R` IDs refer to the requirements in the high-level design. `S` is a user
  story or technical enabler, `T` is an implementation task, and `TR` is a
  testable technical requirement. IDs are stable when stories are reordered.
- **P0** is a reader and platform spike. **P1** is the first usable release.
  **P2** adds reader depth, backup, and formats. **P3** adds connected features.
- A story is ready for implementation when its dependencies and any spike
  decisions are resolved. It is done when its acceptance checks pass, its
  technical requirements have evidence, and relevant changes are reviewed.
- Keep user-visible behavior in the story acceptance checks. Record precise
  engineering constraints under `TR`; do not treat a task list as proof of
  completion.

### Planning choices

| Choice | Tradeoff | Decision |
| --- | --- | --- |
| Build all three readers directly in the MVP, or prove them with Windows samples first | A spike adds work up front; skipping it risks rewriting position handling, HTML isolation, or the PDF engine after UI integration. | Complete S01–S02 first and use their evidence to settle reader choices. |
| One flat implementation checklist, or stories with acceptance and dependency links | Linked stories take more planning to maintain but make scope and release readiness visible. | Use R → S → T → TR IDs and review the links when requirements change. |
| Treat reading percentage as completion, or track completion explicitly | A separate action adds one UI control and state field but avoids marking a partially used guide complete by accident. | Keep per-guide location, estimated percentage, and user-set completion separate. |

### Traceability to the high-level requirements

| Design requirement | Stories that deliver or validate it |
| --- | --- |
| R1 Game organization | S03, S04 |
| R2 Local TXT/HTML/PDF import | S02, S06, S07, S08, S09, S10 |
| R3 Library browsing and search | S05, S11 |
| R4 Offline reading | S02, S07, S08, S09, S10, S17 |
| R5 Per-guide resume | S02, S08, S09, S10, S12 |
| R6 Reading and completion state | S12, S13 |
| R7 Reader controls and appearance | S08, S09, S10, S11, S14 |
| R8 Safe import, removal, and recovery | S03, S04, S06, S07, S15, S20 |
| R9 Windows usability and accessibility | S01, S02, S10, S16, S17 |

## P0 — answer architectural questions

Implementation details and evidence gates for S01–S02 are in the
[P0 technical design](p0-technical-design.md).

### S01 — Establish the Windows application baseline

**Story:** As a maintainer, I need a buildable WinUI 3 application and a
repeatable Windows test setup so the reader prototypes can be evaluated on
their target platform. **Traces:** R9. **Depends on:** none.

**Acceptance:** A packaged sample launches on Windows 11 x64; the build and
installation steps are recorded. Windows 10 22H2 and ARM64 support are tested
before being advertised.

- **T01.1** Create the C#/.NET WinUI 3 solution, app project, core library, and
  test project with separate UI and domain boundaries.
- **T01.2** Set up a Windows CI job to restore, build, and run non-UI tests.
- **T01.3** Produce a test MSIX and document signing, Windows App SDK, and
  WebView2 Runtime prerequisites for a clean-machine check.
- **TR01.1** Application UI uses WinUI 3 on the Windows App SDK; no reader
  service requires a WinUI control to run unit tests.
- **TR01.2** Build outputs are architecture-specific and identify the tested OS
  and CPU combinations. Successful compilation alone is not counted as an
  installation test.

### S02 — Prove the three reader paths

**Story:** As a reader, I need TXT, static HTML, and PDF to display and resume
reliably so implementation can proceed without changing engines halfway
through the MVP. **Traces:** R2, R4, R5, R9. **Depends on:** S01.

**Acceptance:** A sample corpus demonstrates TXT whitespace and long-file
behavior, HTML with local images/CSS while offline, and PDF page display and
resume. Record a go/no-go decision for native PDF rendering, including text
accessibility.

- **T02.1** Collect distributable test samples: BOM and legacy-encoded TXT,
  ASCII diagrams, long TXT, HTML with relative assets and hostile links, and
  short/long PDFs.
- **T02.2** Prototype bounded TXT rendering and a stable normalized-character
  locator across resizing and text-size changes.
- **T02.3** Prototype WebView2 local content, blocked remote resources, disabled
  page scripts, and host-owned DOM position queries.
- **T02.4** Prototype `Windows.Data.Pdf` page rendering, cache bounds, page
  locator, and screen-reader behavior; record whether a text-capable PDF
  engine is needed.
- **TR02.1** Each prototype records its input sample, Windows build, observed
  behavior, and failures in a decision note.
- **TR02.2** HTML prototype emits no guide-originated network requests while
  rendering the offline sample, including CSS resources and attempted redirects.
- **TR02.3** No implementation story may claim PDF document-text accessibility
  from page-image rendering alone.

## P1 — first usable release

### S03 — Persist the local library

**Story:** As a reader, I need my games, guides, settings, and reading state to
survive app restarts. **Traces:** R1, R5, R8. **Depends on:** S01; can progress
alongside S02.

**Acceptance:** Restarting the app preserves records and settings; a schema
upgrade preserves a populated test library.

- **T03.1** Define SQLite tables and repository interfaces for `Game`, `Guide`,
  `ReadingState`, `ReaderPreferences`, and global settings.
- **T03.2** Implement versioned, tested migrations and foreign-key constraints.
- **T03.3** Create a per-user managed-content root and relative-path resolver.
- **TR03.1** IDs are stable, timestamps are stored in UTC, and `Guide.game_id`
  and per-guide state use enforced foreign keys.
- **TR03.2** The database stores managed relative paths; resolved paths cannot
  escape the library root.
- **TR03.3** Migration failure leaves the previous database usable or an
  actionable recovery copy; it never silently replaces it with an empty one.

### S04 — Manage games and their guides

**Story:** As a reader, I want to create and rename games and keep multiple
guides under each game so my library matches what I am playing. **Traces:** R1,
R8. **Depends on:** S03, S11.

**Acceptance:** Two guides can belong to one game and retain independent
reading state. Removing a game containing guides shows the number of affected
guides and requires confirmation.

- **T04.1** Build add/edit game dialogs and validate titles; support optional
  platform and notes without making either mandatory.
- **T04.2** Build game detail actions for renaming and removal.
- **T04.3** Implement confirmed cascade removal through the library service.
- **TR04.1** Game removal deletes its guide metadata, reading state, reader
  preferences, and managed content as one recoverable operation.
- **TR04.2** Canceling removal changes neither database records nor files.

### S05 — Browse and find library entries

**Story:** As a reader, I want to find a game or guide quickly and see my
reading state before opening it. **Traces:** R3. **Depends on:** S03, S11.

**Acceptance:** Library and game detail views show title, format, last opened
time, approximate percentage, and completion state. A title query finds
matching games and guides without requiring network access.

- **T05.1** Implement sorted game list/grid and guide cards or rows in WinUI 3.
- **T05.2** Add library title search and empty, loading, and no-result states.
- **T05.3** Keep selection and navigation stable when a game or guide is edited.
- **TR05.1** Search compares titles case-insensitively and does not open or
  parse guide content.
- **TR05.2** An unread guide has an explicit `Not started` display rather than
  fabricated 0% resume data.

### S06 — Import a guide without partial records

**Story:** As a reader, I want to add a local TXT, HTML, or PDF guide to a game
without changing the original file. **Traces:** R2, R8. **Depends on:** S03,
S04, S02.

**Acceptance:** Import preview shows title, format, and relevant encoding or
asset warnings. Canceling or failing import leaves no visible guide or managed
files. A successful guide opens after the original file is moved or deleted.

- **T06.1** Add file picker and import preview scoped to the selected game.
- **T06.2** Validate supported format and readability; provide a TXT encoding
  choice when automatic detection is uncertain.
- **T06.3** Copy into an isolated staging directory, fingerprint content, then
  publish managed files and database metadata.
- **T06.4** Handle repeated imports without silently overwriting an existing
  guide; show a concrete error for unsupported, missing, or encrypted files.
- **TR06.1** Import never writes to the selected original.
- **TR06.2** Database publication happens only after validation and file
  staging; failed publication removes newly created managed files.
- **TR06.3** Fingerprints and guide IDs are recorded so later re-import or
  replacement can be distinguished from an unchanged copy.

### S07 — Keep imported HTML static and offline

**Story:** As a reader, I want local HTML guides with their images and CSS to
remain usable offline without giving imported pages access to app data.
**Traces:** R2, R4. **Depends on:** S02, S06.

**Acceptance:** An HTML guide with relative images and CSS displays offline;
missing assets are reported during import. Scripts, remote images, remote CSS
imports, forms, new windows, and off-root file paths cannot initiate embedded
reader activity. Supported local nested CSS imports remain inside the guide.

- **T07.1** Resolve relative asset references against the selected import root
  and stage supported static images and CSS while preserving safe paths.
- **T07.2** Reject traversal and symlink escape; warn about missing or
  unsupported assets before import confirmation.
- **T07.3** Configure WebView2 navigation/resource restrictions and explicit
  external-link handling; test nested CSS URLs.
- **TR07.1** Only the guide's managed root can serve embedded content. Absolute
  paths and parent-directory escapes never resolve to user files.
- **TR07.2** Page JavaScript, host objects, web messages, and unsolicited new
  windows are disabled. The host may run fixed, validated DOM queries needed
  for reading location.
- **TR07.3** Remote resources are blocked at request time, including indirect
  loads from CSS and HTML attributes; an external link requires a user action
  to open the system browser.

### S08 — Read legacy TXT faithfully

**Story:** As a reader, I want long text walkthroughs to remain readable
without breaking maps, columns, or ASCII diagrams. **Traces:** R2, R4, R5, R7.
**Depends on:** S02, S06, S11.

**Acceptance:** UTF-8 and the selected legacy encoding render consistently.
Line breaks, spaces, and fixed-width content remain intact. Reopening after a
font-size or window-size change returns near the same text.

- **T08.1** Decode BOM/UTF-8 and explicit fallback encodings into a normalized
  text model while retaining the chosen encoding in guide metadata.
- **T08.2** Render using a bounded or virtualized native view with monospace
  preformatted layout by default.
- **T08.3** Implement navigation and character-offset/context location APIs.
- **TR08.1** Whitespace is never collapsed in the default TXT mode.
- **TR08.2** Rendering and progress storage do not create one persistent WinUI
  control per source line in a long guide.
- **TR08.3** Saved locations use normalized character offsets with context
  rather than pixel coordinates.

### S09 — Read imported HTML

**Story:** As a reader, I want an imported HTML walkthrough to open locally
with its layout and resume near the last text I saw. **Traces:** R2, R4, R5,
R7. **Depends on:** S02, S07, S11.

**Acceptance:** Fragment links within the imported entry document work;
links to other HTML documents are unavailable in P1, and remote links require
an explicit browser action. Resume works after reflow or theme/font changes,
with a visible approximate fallback if the document changed.

- **T09.1** Embed a restricted WebView2 reader for managed local content.
- **T09.2** Apply host-owned theme/font styling without requiring page scripts.
- **T09.3** Capture and restore document-relative path, visible text/element
  context, and scroll-ratio fallback.
- **TR09.1** DOM location results are treated as untrusted input and validated
  before persistence or navigation.
- **TR09.2** A guide cannot navigate into another guide's managed directory.
- **TR09.3** The reader works with the network disconnected after import.

### S10 — Read PDF manuals

**Story:** As a reader, I want a PDF manual to fit my window and reopen on the
same page and part of that page. **Traces:** R2, R4, R5, R7, R9. **Depends on:**
S02, S06, S11; the P0 raster-only result requires a P1 text-engine decision.

**Acceptance:** Page jump, previous/next page, zoom, and fit-to-width work on
short and long PDFs. Resume returns to the saved page and approximate vertical
point after resizing. A tagged PDF exposes usable document text to keyboard
selection and a screen reader; a scanned PDF is labeled image-only.

- **T10.0** Select and validate a distributable text-capable PDF path against
  tagged and scanned fixtures, offline behavior, page location, and licensing.
  [M0 prototype decision](p1/pdf-decision.md): native preview plus PdfPig text;
  production acceptance remains under T10.1–T10.3 and T16.3.
- **T10.1** Render and recycle pages with bounded cache/memory use.
- **T10.2** Add page count, page jump, fit-to-width, zoom, and keyboard commands.
- **T10.3** Persist zero-based page index and within-page fraction.
- **TR10.1** Page indexes are range-checked against the current document; an
  out-of-range saved value recovers without a crash.
- **TR10.2** PDF bytes are read from the managed copy and no online viewer is
  required.
- **TR10.3** A tested text-capable PDF path exposes document text from the tagged
  fixture through Windows UI Automation before this story or the release is
  complete. A raster-only image does not satisfy this requirement.

### S11 — Provide a consistent reading shell

**Story:** As a reader, I want the library, game details, reader, and settings
to feel like one Windows app. **Traces:** R3, R7. **Depends on:** S01, S02.

**Acceptance:** I can navigate Library → Game → Reader → Game without losing
context. The reader shows only controls supported by its format and remembers
the last active guide without forcing it open at launch.

- **T11.1** Build the `NavigationView` shell and library/game/reader/settings
  routes.
- **T11.2** Define reader adapter commands (`Open`, `GetLocation`,
  `RestoreLocation`, `GetEstimatedProgress`) and capability flags.
- **T11.3** Build the reader top bar, collapsible navigation area, and format
  command slots.
- **TR11.1** The shell does not inspect format-specific controls to read or
  save position.
- **TR11.2** Disabled or unsupported actions are absent or clearly unavailable,
  never silently ignored.

### S12 — Resume each guide independently

**Story:** As a reader, I want every guide to reopen near its own last visible
location after I leave it or restart the app. **Traces:** R5, R6. **Depends on:**
S03, S08, S09, S10, S11.

**Acceptance:** Switching between two guides and restarting preserves two
different positions. Text resizing keeps a text anchor where possible. When
content changes and only percentage can be restored, the app labels the
location approximate.

- **T12.1** Define versioned format-specific locator serialization and
  restoration order: exact/context anchor, then approximate percentage.
- **T12.2** Save after meaningful movement with throttling; flush on
  navigation away and app deactivation.
- **T12.3** Compute an estimated percentage per format and detect content
  fingerprint changes.
- **TR12.1** `ReadingState` is keyed by guide ID, never only by game or filename.
- **TR12.2** A normally running app persists a changed location within five
  seconds of the last movement; continuous scrolling does not trigger one
  database write per scroll event.
- **TR12.3** Restore clamps invalid positions, avoids crashes on old locator
  versions, and never changes the completion state.

### S13 — Track completion explicitly

**Story:** As a reader, I want to mark a guide complete or in progress without
losing my place. **Traces:** R6. **Depends on:** S03, S05, S12.

**Acceptance:** The completion action updates library and reader displays
immediately. Returning to in progress preserves the saved locator. Reaching
the final page does not silently mark the guide complete.

- **T13.1** Add completion actions to guide detail and reader views.
- **T13.2** Store completion time separately from reading location and
  percentage.
- **TR13.1** Completion status is derived from an explicit user action, not
  from estimated percentage.
- **TR13.2** State changes are persisted as one transaction and survive restart.

### S14 — Remember reader appearance

**Story:** As a reader, I want a comfortable text size for each guide and a
light or dark reading theme. **Traces:** R7. **Depends on:** S03, S08, S09,
S11.

**Acceptance:** Font size chosen in one TXT/HTML guide persists across restart
without changing another guide. Theme change applies to library and reader
without breaking TXT whitespace or HTML offline behavior.

- **T14.1** Add per-guide TXT/HTML font-size controls and saved preferences.
- **T14.2** Add global theme setting with a Windows theme default.
- **T14.3** Recheck location restoration after changing appearance.
- **TR14.1** Reader preferences are keyed by guide ID and have a bounded,
  accessible size range.
- **TR14.2** Theme CSS or assets do not require a remote resource.

### S15 — Recover from library and import errors

**Story:** As a reader, I want clear recovery paths if a managed file is
missing or an import is interrupted. **Traces:** R8. **Depends on:** S03, S04,
S06.

**Acceptance:** An interrupted import or missing guide file leaves the library
usable. The user can remove a broken guide after confirmation; existing,
unaffected guides still open.

- **T15.1** Handle missing/corrupt managed content and database exceptions at
  service boundaries with actionable messages.
- **T15.2** Clean orphaned staging directories and detect orphaned managed
  directories without deleting unknown user files.
- **T15.3** Implement guide removal with confirmation and recoverable deletion
  of its owned files and state.
- **T15.4** Test interrupted copy, failed database commit, and canceled and
  confirmed guide deletion.
- **TR15.1** Cleanup targets only app-owned staging or managed guide paths.
- **TR15.2** Guide deletion removes metadata, reading state, preferences, and
  owned files; canceled deletion leaves all four intact.

### S16 — Support Windows input and accessibility

**Story:** As a keyboard, touch, or screen-reader user, I want to reach and
understand the library and reader controls. **Traces:** R9. **Depends on:**
S05, S08, S09, S10, S11.

**Acceptance:** Core flows can be completed without a mouse. Focus stays
visible, controls have useful names, TXT/HTML text is readable through a screen
reader, and the UI remains usable with Windows scaling and high contrast.

- **T16.1** Map and document `Ctrl+O`, library `Ctrl+F`, `Esc`, and page
  navigation; keep every shortcut action in the visible UI.
- **T16.2** Audit keyboard order, focus restoration, AutomationProperties,
  touch target size, DPI scaling, and high contrast.
- **T16.3** Record PDF document-text access behavior from the chosen engine
  and provide an accurate user-facing limitation if needed.
- **TR16.1** No essential flow depends on hover or a keyboard-only gesture.
- **TR16.2** Reader overlays return focus to the invoking control when closed.

### S17 — Package and verify the MVP

**Story:** As a Windows user, I want an installable app whose imported guides
still work when I am offline. **Traces:** R4, R9. **Depends on:** S03–S16, S20.

**Acceptance:** A signed release candidate installs on the promised Windows
targets. Import, restart, offline read, resume, and removal pass on a clean
machine. Missing WebView2 Runtime produces an actionable setup message.

- **T17.1** Set MSIX identity, versioning, signing, and release output for
  tested CPU targets.
- **T17.2** Add automated unit/integration coverage for storage, import,
  locators, and security rules plus signed, installed Windows UI workflows.
  Start full P1 E2E runs through an interactive scheduled task when controlled
  remotely; retain per-scenario results and the manual accessibility checks.
- **T17.3** Run clean-install, upgrade, and offline scenarios in an interactive
  Windows session on each promised OS/architecture combination; publish the
  tested matrix and PDF limitations. Follow the
  [installed E2E procedure](p1/e2e-testing.md).
- **TR17.1** The final app needs no network access to display an imported TXT,
  HTML, or PDF guide after prerequisites are installed.
- **TR17.2** No OS or architecture is advertised without a recorded install and
  reader smoke result.

### S20 — Export and restore a local backup

**Story:** As a reader, I want a portable copy of my library so uninstall,
machine loss, or an unsuccessful upgrade does not leave my only guide copies
unrecoverable. **Traces:** R8. **Depends on:** S03, S15.

**Acceptance:** An archive saved outside app data restores games, guides,
preferences, and reading state into a clean installation. Restore validates
its integrity before replacing an existing library. Export and restore are
user initiated. After the first import, the app explains that uninstall
removes its live library and points to Export in Settings.

- **T20.1** Define a versioned manifest and archive of a consistent SQLite
  snapshot and managed guide files.
- **T20.2** Build export, validation, and restore flows with cancel/replace
  conflict handling.
- **TR20.1** Restore validates checksums and paths in staging before modifying
  the active library.
- **TR20.2** The export includes no credentials, transient WebView2 data, or
  unrelated user files.

## P2 — reader depth and local ownership

### S18 — Search and navigate within guides

**Story:** As a reader, I want to find a word or jump to a section inside a
guide. **Depends on:** S08, S09, S11, S12.

**Acceptance:** TXT/HTML search finds next and previous matches without
replacing the saved reading anchor; HTML headings or anchors and detected TXT
sections form a usable table of contents when available.

- **T18.1** Add TXT search over normalized text and HTML search over permitted
  DOM text.
- **T18.2** Add HTML heading/anchor collection and conservative TXT section
  heuristics with manual fallback when no TOC is found.
- **TR18.1** Search and TOC controls appear only where supported; failures to
  detect sections do not block ordinary scrolling.

### S19 — Add bookmarks and optional TXT reflow

**Story:** As a reader, I want to save important locations and optionally
reflow prose without damaging diagrams. **Depends on:** S08, S12.

**Acceptance:** Bookmarks reopen at a stable nearby location. Switching TXT
between preformatted and reflow views retains the reading position and offers
a clear way back to the original formatting.

- **T19.1** Add guide-scoped bookmark records and reader navigation.
- **T19.2** Build opt-in TXT paragraph detection and reflow with a persistent
  preformatted fallback.
- **TR19.1** Reflow never modifies imported source bytes or the canonical
  normalized-text locator.

### S21 — Add selected local material formats

**Story:** As a reader, I want maps, CBZ archives, and multi-page local HTML
beside written guides when their offline behavior is dependable. **Depends
on:** S06, S07, S11, S12, S20.

**Acceptance:** Each added format has a documented importer, reader controls,
locator, offline check, and failure behavior before it is marked supported.

- **T21.1** Prioritize image maps, multi-page HTML, CBZ, and other manual
  formats using real guide samples.
- **T21.2** Add one importer/reader adapter at a time and extend backup tests.
- **TR21.1** Multi-page HTML locators include document path and cross-page
  progress rules; archives cannot extract outside their staging root.

## P3 — connected and optional features

### S22 — Discover and save guides from supported sources

**Story:** As a reader, I want to find guides from approved sources and save
them for offline reading. **Depends on:** S06, S07, S15, S20.

**Acceptance:** Each supported source has documented terms, attribution,
capture behavior, and recovery for failed downloads. A saved guide remains
readable offline.

- **T22.1** Review one source at a time for API/usage terms and attribution.
- **T22.2** Build source adapter, preview, download/capture, and import through
  the existing staging pipeline.
- **TR22.1** Network failures cannot corrupt existing local library data.
- **TR22.2** Online browsing and previously imported offline reading remain
  separate capabilities.

### S23 — Sync library and progress across devices

**Story:** As a reader, I want optional sync without losing locally stored
guides or reading positions. **Depends on:** S20.

**Acceptance:** Sync can be disabled; offline edits remain available locally;
conflicting positions and guide revisions have a documented, testable
resolution rule.

- **T23.1** Choose transport and identity model; document privacy and storage
  costs before implementation.
- **T23.2** Define content IDs, change tracking, conflict UI/rules, and
  backup-based recovery tests.
- **TR23.1** Sync is opt-in and cannot silently replace a newer local guide
  or completion state with stale remote data.

### S24 — Expand desktop reading workflows

**Story:** As a reader, I want optional controller navigation, second-window
reading, and map annotations when they improve play alongside a game.
**Depends on:** S11, S12, S21.

**Acceptance:** Each feature preserves independent guide positions, remains
keyboard-accessible, and can be disabled without affecting the base reader.

- **T24.1** Prototype controller mapping and multiple-window state ownership.
- **T24.2** Prototype annotations as separate overlays stored apart from
  imported originals.
- **TR24.1** Two windows cannot overwrite one another's saved position without
  an explicit last-active-window rule.
- **TR24.2** Annotations never modify imported guide files.

## Release gates and ordering

1. Finish **S01–S02** and write down TXT, HTML, and PDF engine decisions.
   PDF accessibility can change S10, S16, and the release promise.
2. Build **S03** and **S11** as the core framework while validating T10.0.
   S04–S05 can then proceed together. Build **S06** after S04, followed by
   S07–S10 where dependencies allow.
3. Integrate **S12–S16** and **S20**, then run the clean-machine and offline
   gate in **S17**. Keep P2/P3 outside the first-release completion claim.
4. S20 moved into P1 because the managed library is the user's only copy
   after an original is removed, and MSIX local app data is removed on
   uninstall. Reassess archive size and restore usability during T20.1.

The first release is ready only when R1–R9 have passing evidence on a tested
Windows target and all P0 decisions affecting the MVP are resolved.
