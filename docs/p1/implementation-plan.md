# P1 implementation plan and exit gates

Status: M0 merged; M1 in progress; M2–M6 planned, 26 September 2026.
P0 was merged into `main` through
[PR #1](https://github.com/ilya-slalom/desktop-guides/pull/1).
This plan orders all **16 P1 stories and 49 tasks** in the
[work breakdown](../work-breakdown.md). The [technical design](../p1-technical-design.md)
defines the architecture, data contracts, failure protocols, and reader
behavior. [Implementation results](results.md) and the
[PDF decision](pdf-decision.md) record tested output.

## Delivery rules

1. Work in small reviewed changes with a passing locked restore and relevant
   tests. Add meaningful tests for schema, paths, recovery, security, and
   locators before or alongside implementation. Inspect changed files and run
   applicable build, test, and UI checks on Windows before closing a task.
2. On the Windows 11 x64 development host, stage source under
   `E:\work\desktop-guides`. Use the repository on macOS as the source checkout.
   Keep package builds and UI automation in Windows CI or an interactive
   Windows session; Core and Infrastructure tests should be headless. For
   installed P1 E2E runs, have SSH or CI trigger an interactive scheduled
   task that installs the signed MSIX and drives the UI. Use the
   [E2E procedure](e2e-testing.md) for profile isolation, recovery, offline
   work, and result checks.
3. A task is done only when its listed output exists and its exit check has a
   recorded result. A passing unit test alone does not close a task that
   requires installed WinUI, offline, keyboard, or UI Automation evidence.
   Keep `docs/p1/results.md` current with commit, fixture, OS/CPU, and outcome.
4. Maintain the P0 diagnostic reader lane while building the production
   shell. Original guide files remain untouched, and the production package
   contains no P0 fixture picker or corpus.

Task prerequisites below are **task IDs** unless marked P0. Tasks in the same
milestone may run in parallel once their prerequisites close. `TR` IDs are the
testable requirements in the work breakdown; a range means every listed TR.

## M0 — contracts, path safety, and PDF decision

Exit: portable models and path rules are reviewed; a failing tagged-PDF
candidate cannot be treated as a completed reader. The PDF experiment may run
in parallel with storage contracts.

T03.1, T03.3, T11.2, T12.1, and the T10.0 candidate decision were merged into
`main` through [PR #3](https://github.com/ilya-slalom/desktop-guides/pull/3)
on 26 September 2026. [M0 results](results.md) record Windows x64 and native
ARM64 CI evidence, including the SQLite initialization review fix.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T03.1 | P0 Core | Game, Guide, ReadingState, preferences, Settings contracts and SQLite v1 schema. Two guides under one game keep distinct state; IDs, UTC values, and foreign keys are checked. | TR03.1 |
| T03.3 | P0 file probes | Injected app-data root and one managed relative-path resolver. NTFS tests reject traversal, absolute/UNC paths, and links while allowing a nested static asset. | TR03.2 |
| T11.2 | P0 reader probes | Typed adapter and capability contract with fake adapters. Shell commands appear only for supported capabilities; no shell control-tree inspection. | TR11.1, TR11.2 |
| T12.1 | T03.1, T11.2 | Versioned TXT/HTML/PDF locator codecs and restore outcomes. Round-trip, malformed, future-version, wrong-guide, and changed-fingerprint tests pass without changing completion. | TR12.1, TR12.3 |
| T10.0 | P0 PDF results | `docs/p1/pdf-decision.md` compares distributable PDF text paths on tagged, scanned, locked, and long fixtures. Select only a path that proves tagged document text in UIA, keyboard selection, offline use, and acceptable licensing. A failed matrix blocks T10.1 and S17. | TR10.2, TR10.3 |

## M1 — persistent foundation and production shell

Exit: the app opens a persistent empty library, can upgrade a populated
database safely, and navigates production routes without fixture controls.
Set the public package identity before producing a public candidate.

T03.2 was merged through [PR #4](https://github.com/ilya-slalom/desktop-guides/pull/4)
with transactional v1→v2 upgrade and a consistent recovery copy under
`DataRoot/.recovery`. T15.2 was merged through
[PR #5](https://github.com/ilya-slalom/desktop-guides/pull/5), merge commit
`47c9e906c01e85eb9ce9f9759f6359390d809e52`. Startup reconciliation
now precedes M2 file mutation. T11.1 is implemented for review in
[PR #6](https://github.com/ilya-slalom/desktop-guides/pull/6); reader
controls and package identity remain separate gates.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T03.2 | T03.1, T03.3 | Versioned migration runner, pre-upgrade SQLite backup, integrity checks, and a populated v1→v2 fixture. Injected failure retains usable prior data or an actionable recovery copy; newer schema fails clearly. | TR03.1, TR03.3 |
| T15.2 | T03.1, T03.2, T03.3 | FileOperations startup reconciler and exact owned-path janitor, installed before mutating file operations. Phase and malformed-path tests retain unknown directories and never follow links. | TR15.1 |
| T11.1 | T03.2, T11.2 | Library/Game/Reader/Settings route coordinator and separate diagnostic build mode. Launch, Back, stale-ID, and route tests pass on installed WinUI; production package has no fixtures. | TR11.1 |
| T11.3 | T11.1, T11.2 | Capability-based reader bar, navigation pane, overflow, and status/focus behavior. Keyboard and pointer trace returns Reader → its Game with query and selection retained. | TR11.1, TR11.2 |
| T17.1 | T11.1 | Production MSIX identity/version/signing and prerequisite delivery plan, plus tested x64 packaging configuration. Install and upgrade use the same identity; credentials stay outside source/logs. Final signing and architecture claims remain gated by M6. | TR17.2 |

### T15.2 implementation sequence

| Step | Dependency | Output and check |
| --- | --- | --- |
| Journal contract | T03.2 | Define a bounded v1 manifest of canonical guide IDs and expected owned paths. Reject unknown fields, versions, duplicate IDs, traversal, and paths inconsistent with operation kind or ID before filesystem work. |
| Path and janitor safety | Journal contract, T03.3 | Resolve exact content/staging/trash guide roots under generated IDs. Preflight trees without following links; remove only a preflighted owned root and retain unknown siblings. |
| Startup reconciliation | Path and janitor safety | Run after schema validation in `InitializeAsync`. Apply the prepared-import, prepared-delete, and committed-delete phase matrix; clear each row only after filesystem work. Expose resolved-operation and review-orphan counts. |
| Windows exit | Startup reconciliation | Tests cover stage-only and moved imports, partial deletion restore, committed trash cleanup, restart retry, malformed and overlapping manifests, nested links, and unknown directories. Locked Core/Infrastructure tests and the Release x64 package build pass on Windows 11; native ARM64 headless and package checks run in CI. |

### T11.1 implementation sequence

The existing `DesktopGuides.App` project remains the P0 diagnostic package.
A separate WinUI production project keeps its fixture picker, probe classes,
and fixture corpus out of the production MSIX. A single conditional project
would share more build logic, but it would make accidental fixture inclusion
harder to detect. The public package identity is finalized in T17.1.

| Step | Dependency | Output and check |
| --- | --- | --- |
| Route coordinator | T03.2 | Typed Library, Game, Reader, and Settings routes with an ID-based back stack. Test launch, Reader-to-own-Game, stale Game/Guide IDs, Back, and optional last-guide Resume without auto-open. |
| Production WinUI shell | Route coordinator | A `NavigationView` window loads `SqliteLibraryRepository` under packaged `ApplicationData.LocalFolder`, renders empty Library/Game/Reader/Settings routes, and handles loading/errors without fixture controls. T04/T05 and M3 later fill in catalog actions and reader adapters. |
| Package separation | Production shell | Build distinct production and diagnostic MSIX packages. Inspect production package contents for fixture/probe strings and files; the diagnostic P0 workflow remains available. |
| Installed Windows exit | Package separation | On Windows 11 x64 install production MSIX, verify an empty Library on first launch, then exercise Library → Game → Reader → Game, Settings and Back through UI Automation with seeded local metadata. Verify a stale last-guide ID is ignored and fixture controls are absent. Run locked Core/Infrastructure tests and both package builds in CI; retain installed ARM64 P0 regression. |

## M2 — catalog, static-asset validation, import, and removal

Exit: two independent managed guides can be added under one game, retain
their content after originals are removed, be listed and searched offline,
and be removed through the recoverable trash protocol. Every failed or
canceled import leaves no visible partial guide. Establish crash recovery
before first publication. Implement T07.1–T07.2 before import validation;
complete S07's WebView2 policy in M3.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T04.1 | T03.2, T11.1 | Add/Edit game dialogs and validation. Unicode, duplicate-title, optional-field, cancel, and keyboard cases pass without unintended writes. | TR03.1 |
| T04.2 | T04.1, T11.1 | ID-bound Game detail rename/remove actions. Rename preserves Guide IDs, state, and view selection after refresh or restart. | TR03.1 |
| T05.1 | T03.2, T11.1 | Virtualized Library and Game guide rows with metadata-driven format, last-opened, estimate, and completion displays. Large synthetic lists keep bounded realized UI items. | TR05.2 |
| T05.2 | T05.1 | Case-insensitive metadata title search with empty/loading/no-results states. Mixed-case and non-ASCII tests pass offline without reading guide bytes; unread rows say `Not started`. | TR05.1, TR05.2 |
| T06.1 | T04.1, T11.1 | Window-owned file picker, game-scoped import preview, warnings, and cancelable progress UI. Cancel before Confirm creates neither a guide nor staged files. | TR06.1 |
| T07.1 | T03.3 | Bounded HTML/CSS dependency parser and static-asset manifest with pinned, license-reviewed parser dependencies. Nested local CSS, `srcset`, cycles, and over-budget fixture tests pass. | TR07.1, TR07.3 |
| T07.2 | T03.3, T07.1 | Preview warnings and path checks for missing, unsupported, remote, escaping, and changed assets. NTFS junction/case-collision tests and post-copy revalidation pass. | TR07.1, TR07.3 |
| T06.2 | T06.1, T07.1, T07.2, T10.0 | Typed import validation for TXT encoding, one static HTML entry, and readable PDF/password cases. Unsupported, encrypted-unreadable, and size-limit inputs produce distinct errors before publication. | TR06.1, TR06.2 |
| T06.3 | T03.2, T03.3, T06.2, T15.2 | Staged streaming copy, fingerprints, prepared journal, same-volume rename, and transactional metadata publication. Original-removal, cancellation, crash-point, disk/copy, and failed-commit tests show no partial listed guide. | TR06.1–TR06.3 |
| T06.4 | T06.3 | Duplicate fingerprint choice (`Open existing` / `Import another copy`) and typed errors. Repeated import never overwrites; a second copy has its own Guide ID and state. | TR06.3 |
| T15.3 | T06.3, T15.2 | Confirmed guide deletion via trash journal. Cancel, move failure, commit failure, and startup recovery tests preserve or remove exactly the intended metadata and owned bytes. | TR15.1, TR15.2 |
| T04.3 | T04.2, T15.3 | Count-confirmed multi-guide Game removal through the same trash protocol. Cancel and changed-count cases leave records/files intact; commit removes only that Game's guides and state. | TR04.1, TR04.2 |
| T05.3 | T04.2, T05.2, T06.3, T15.3 | Stable ID-based selection and Back behavior after rename/import/removal. Async refresh and keyboard focus tests do not jump to stale rows. | TR05.1 |

## M3 — managed reader adapters

Exit: imported TXT/HTML/PDF open through the typed shell, work offline, and
provide format-correct navigation and restoration. Tagged PDF text access is
observed again in the actual adapter, beyond the T10.0 prototype.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T14.2 | T03.2, T11.1 | Persisted System/Light/Dark setting and local theme styles. Restart and disconnected-session checks pass; high contrast keeps system colors. | TR14.2 |
| T07.3 | T06.3, T07.2, T11.2 | Per-guide WebView2 manifest responder, navigation/popup/resource deny rules, and explicit external-link action. Fresh-profile online/offline canary tests record zero guide-originated network requests and block cross-guide loads. | TR07.1–TR07.3 |
| T08.1 | T06.3, T11.2 | Managed TXT decoding and stored encoding choice. BOM, strict UTF-8, CP437, Windows-1252, newline, and truncation tests pass with untouched originals. | TR08.1 |
| T08.2 | T08.1 | Virtualized monospace no-wrap TXT view. ASCII diagrams survive; 10 MiB response and realized-item measures meet the P0 reference checks without a persistent control per line. | TR08.1, TR08.2 |
| T08.3 | T08.2, T12.1 | TXT commands and offset/context capture/restore. Resize, font change, repeat quote, and changed-content tests return the right line or a labeled approximation. | TR08.3 |
| T09.1 | T06.3, T07.3 | Managed-guide HTML adapter with isolated transient profile and no source/network fallback. Missing WebView2 leaves TXT/PDF usable with an actionable message. | TR09.2, TR09.3 |
| T09.2 | T09.1, T14.2 | Fixed local style for theme/font and high contrast. Offline style changes preserve static assets and do not permit a new network or cross-guide request. | TR09.3, TR14.2 |
| T09.3 | T09.1, T12.1 | Bounded HTML context/fragment capture and restore with approximate fallback. Reflow, delayed image, changed content, invalid DOM, and unimported-link cases pass. | TR09.1, TR09.2 |
| T10.1 | T06.3, T10.0, T11.2 | Selected text-capable PDF adapter with bounded render cache and disposal. Long-document/rapid-turn tests show current page, accessible text, and bounded memory. | TR10.2, TR10.3 |
| T10.2 | T10.1 | Page controls, fit-width, zoom, keyboard parity, and password retry. Range, focus, password-clearing, and offline installed-app cases pass. | TR10.1–TR10.3 |
| T10.3 | T10.1, T12.1 | Versioned page/fraction locator and restore. `pdf-long` returns to the same page within 0.1 page after resize; invalid or changed locators recover safely. | TR10.1, TR10.3 |

## M4 — progress, completion, and appearance

Exit: two guides retain independent positions across restart; completion
changes only by explicit action; per-guide appearance and global theme survive
restart. Theme/font changes preserve or visibly approximate the location.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T12.2 | T03.2, T08.3, T09.3, T10.3, T11.1 | Session-aware ProgressCoordinator and flush hooks. Fake-clock and installed-app tests show a changed position saved within five seconds, no write per scroll event, and no cross-guide stale write. | TR12.1, TR12.2 |
| T12.3 | T12.2 | Per-format bounded estimate, content-hash comparison, and approximate status. Restart, changed-byte, invalid-value, and unread tests preserve completion and display truthful percentages. | TR12.3, TR05.2 |
| T13.2 | T03.2, T12.2 | Transactional completion timestamp service, independent of locator and estimate. Repeat/toggle/restart tests prove 100% reading never implies complete. | TR13.1, TR13.2 |
| T13.1 | T05.1, T11.3, T13.2 | Guide and Reader completion actions bound to one service. UIA and keyboard checks show immediate committed state and announce changes; final-page reading leaves state unchanged. | TR13.1, TR13.2 |
| T14.1 | T03.2, T08.2, T09.1, T11.3 | Bounded per-guide TXT/HTML text-size controls and persisted preferences. Two-guide restart test preserves separate sizes and TXT fixed-width layout. | TR14.1 |
| T14.3 | T08.3, T09.3, T10.3, T14.1, T14.2 | Pre-change capture and post-layout restore across text/theme changes. TXT returns within one line, HTML to matching context where present, PDF to page/fraction; fallback is announced. | TR14.1, TR14.2 |

## M5 — errors, accessibility, and portable backup

Exit: interrupted operations recover without damaging unrelated content;
Settings can export outside app data and restore a validated library; complete
flows work by keyboard and with the recorded accessibility checks.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T15.1 | T03.2, T06.3, T09.1, T10.1 | Stable service errors and actionable UI for corrupt DB, missing guide, invalid content, and missing runtime. An unaffected guide still opens; a corrupt DB is never replaced by an empty one. | TR15.1 |
| T15.4 | T04.3, T06.3, T15.2, T15.3 | Headless fault-injection matrix across each import/delete protocol phase plus canceled operations. Assert exact DB rows and owned paths; Windows NTFS runs cover links and malformed names. | TR04.1, TR04.2, TR06.2, TR15.1, TR15.2 |
| T20.1 | T03.2, T06.3, T15.2 | Versioned ZIP manifest and consistent SQLite/files snapshot under one write gate. Export is canceled cleanly, verified by checksums, and excludes source paths, credentials, and transient profiles. | TR20.2 |
| T20.2 | T15.1, T15.4, T20.1 | Settings Export/Restore, out-of-app-data destination check, first-import export reminder, full staged archive validation, Cancel/Replace, and rollback marker. Clean and populated restore, corrupt/unsafe ZIP, cancel, and interrupted swap tests pass. | TR20.1, TR20.2 |
| T16.1 | T05.2, T06.1, T08.3, T10.2, T11.3 | Visible menu/toolbar parity and documented shortcuts. Keyboard trace checks context, text-field handling, dialogs, page movement, and Escape behavior. | TR16.1, TR16.2 |
| T16.2 | T07.3, T08.3, T09.3, T10.2, T13.1, T16.1, T20.2 | Real WinUI audit of tab/focus, touch, high contrast, DPI, AutomationProperties, and Narrator through Add game → Import → Read → Complete → Export. TXT/HTML document text is read, and overlays return focus. | TR16.1, TR16.2 |
| T16.3 | T10.0, T10.2, T10.3 | Tagged/scanned/locked PDF UIA, selection, keyboard, and Narrator record for the selected production engine. Tagged document text passes; scanned image-only limitation is stated accurately. | TR10.3, TR16.1 |

## M6 — release evidence and tested support matrix

Exit: all P1 TRs pass on a signed installed Windows 11 x64 release candidate
through the [interactive E2E procedure](e2e-testing.md), with prerequisites,
restart, physical offline reading, upgrade, and restore evidence. Publish
only targets with complete target-specific results.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T17.2 | T04.3, T06.4, T07.3, T08.3, T09.3, T10.3, T12.3, T13.1, T14.3, T15.4, T16.2, T16.3, T20.2 | Locked Core/Infrastructure and signed installed production UI workflow CI started through an interactive scheduled task, retained P0 regression lane, and reviewable fixture/evidence checklist. Failing storage/security tests stop packaging; failing installed UI blocks release promotion. | TR17.1, TR17.2 |
| T17.3 | T17.1, T17.2 | Interactive-task signed install/upgrade, three-format import after original removal, offline relaunch, independent resume, deletion, and backup restore on each promised target. Record OS/CPU/package/prerequisite versions, hashes, and scanned-PDF limit. A runtime-free Windows 11 x64 VM must prove missing-prerequisite failure and offline-installer recovery before a clean-install claim. | TR17.1, TR17.2 |

The existing `production-shell-ui` CI job verifies M1 routes in an already
interactive runner session. It does not execute the complete P1 scenario
checklist or close T17.2. The full E2E runner will use the scheduled-task
entry point and preserve the per-scenario evidence listed in the
[procedure](e2e-testing.md).

## Windows verification and deferred environments

| Lane | Required result | Current status |
| --- | --- | --- |
| Headless Core/Infrastructure | Schema/migration, locator, path, transaction recovery, archive, import-security, and fault-injection tests on locked Windows CI; NTFS link/junction checks on Windows. | T03.2's merged [PR CI](results.md) passed 61 Core and 24 Infrastructure tests on x64 and native ARM64. T15.2's locked Windows 11 x64 run passed 61 Core and 41 Infrastructure tests after the uppercase-ID review fix, including NTFS junction, prepared-import collision, and retry cases. Earlier PR #5 CI passed 61 Core and 39 Infrastructure tests on x64 and native ARM64; current-head results are in PR checks. Later-task suites are pending. |
| Windows 11 x64 installed app | Production UI workflow, keyboard, UIA/Narrator, high contrast/DPI, signed upgrade, and physically disconnected relaunch on `E:\work\desktop-guides` source. | T11.1 installed shell routes passed on a Windows 11 x64 CI runner. A [controlled local retest](evidence/production-shell-host-ssh-reinstall.json) reproduced `0x80070005` from SSH session 0 after uninstall and succeeded through an interactive scheduled task in desktop session 1; the package, backup, and launch were verified. Full P1 flow and release gates remain open. |
| Runtime-free Windows 11 x64 VM | Actual absent Windows App Runtime and WebView2 failures, prerequisite setup, recovery, and clean restore. | Deferred by user until a disposable VM is available. Do not claim clean-machine support before this lane passes. |
| Windows 11 ARM64 | Native complete P1 installed workflow, backup, accessibility, and offline evidence before advertising ARM64. | P1 pending; P0 native Core/UI fixtures passed. |
| Windows 10 x64 | Equivalent signed install and reader workflow before advertising Windows 10. | Deferred by user. |

Review schema and file-operation changes before starting production imports;
review HTML/PDF engine and package identity changes before a release
candidate. The release report should link exact traces and state any
unavailable lane as untested. The PDF gate, backup restore, and target-specific
install checks are blocking release requirements, not items to infer from P0.
