# P1 implementation results

Status: M0 merged into `main` through
[PR #3](https://github.com/ilya-slalom/desktop-guides/pull/3) on
26 September 2026, including the SQLite initialization review fix.
The P1 first usable release remains in progress. The
[dependency plan](implementation-plan.md) defines all 49 task exit gates;
this file records checks actually run.

## M0 task results

| Task | Implemented output | Verification and remaining scope |
| --- | --- | --- |
| T03.1 | Portable Game, Guide, ReadingState, ReaderPreferences, and Settings records; repository contract; SQLite v1 schema and repository. IDs are generated, timestamps use an injected UTC clock, foreign keys are enabled on each connection, and a stored guide root must match its ID. `TextCodePage` is stored only for TXT. | Windows integration tests reopen two guides under one game with independent locators, estimates, completion timestamps, and preferences; reject an orphan state and a mismatched guide root. An interrupted first initialization can retry when the version-0 database has no schema objects, passes integrity check, and has no other application ID. Existing unknown schema or another application ID is preserved and rejected. The v1→v2 migration and public guide publication belong to T03.2 and T06.3. |
| T03.3 | `ILibraryPaths`, strict forward-slash managed relative paths, and an injected-root resolver under generated guide IDs. | Windows tests accept a nested CSS asset and reject traversal, absolute/UNC paths, percent escapes, symlinks, and an NTFS junction. |
| T11.2 | Portable reader session, typed actions and capability policy, plus the WinUI view adapter contract. | Fake-reader tests show supported commands dispatch and unsupported commands stop at the policy. Production TXT/HTML/PDF adapters and shell command controls are later M3 work. |
| T12.1 | Versioned bounded JSON codecs for TXT, HTML, and PDF, with fingerprint checks and exact/context/approximate restore candidates. | Core tests cover round trips, changed bytes, future versions, invalid JSON/numbers, duplicate fields, oversized text, wrong format, and HTML entry-document mismatch. The codecs do not alter completion. Actual readers use them in M3. |
| T10.0 | [Native hybrid PDF decision](pdf-decision.md) with restricted WebView2 comparison, license check, fixture traces, keyboard/UIA selection, password retry, app-specific outbound-blocked run, and 23 repeated page turns. | The prototype passed its tested decision gates on Windows 11 x64. It is not a production PDF adapter. Narrator speech, complex reading order, installed offline use, and cache/endurance limits remain T10.1–T10.3, T16.3, and T17.3 gates. |

## Windows M0 verification

These checks were recorded for the initial M0 implementation on 25 September 2026.

- Host: Windows 11 x64, build `10.0.26200.0`; source staged under
  `E:\work\desktop-guides`; .NET SDK `10.0.401`. Locked restores passed for
  Core tests, Infrastructure tests, the WinUI app, and the PDF tool.
- Release headless tests: **61/61 Core and 10/10 Infrastructure passed**,
  including the two-guide completion-state assertion and Windows reserved
  device-name path cases.
- Unpackaged WinUI x64 publish and `PdfTextSpike` Release build passed, with
  zero PDF-tool build warnings or errors. An unsigned x64 MSIX build passed;
  its sole warning was that `mspdbcmf.exe` was unavailable, so no symbols
  package was generated. The
  [interactive native trace](evidence/pdf-ui-candidate.json) and
  [outbound-blocked trace](evidence/pdf-ui-candidate-offline.json) passed on
  the published assembly SHA-256
  `36ffc1af5c5d6ed4541f4dd292cc9ed8ed6773f2b60152f3c053a9a14c9452c9`.
  The [WebView2 comparison](evidence/pdf-web-candidate.json) did not expose
  tagged document text or a validated locator in its restricted prototype.
- After testing, no candidate firewall rule, scheduled task, or app process
  remained. The stale temporary directory from the earlier junction test
  was removed.

## Review follow-up — 26 September 2026

The review found that an interrupted first initialization could leave an
existing SQLite file at `user_version = 0`. The repository previously rejected
it solely because the file existed. Initialization now retries only if the
database has no schema objects, passes `integrity_check(1)`, and has
`application_id = 0`. Unknown schema objects or a different application ID
still require recovery instead of replacement.

On the Windows 11 x64 host under `E:\work\desktop-guides`, the three focused
tests first reproduced the issue against the old code (one failure, two
passes), then passed with the fix (three passes). The full locked Release run
passed **61/61 Core and 13/13 Infrastructure** tests. The PR checks for this
follow-up are tracked on [PR #3](https://github.com/ilya-slalom/desktop-guides/pull/3).

## PR CI evidence

[PR CI run 36157085242](https://github.com/ilya-slalom/desktop-guides/actions/runs/36157085242)
passed all five jobs for code head `6c6deb8527e271e4398d6d833d3bb77698776e22`
before the 26 September review follow-up:
locked Core/Infrastructure tests on Windows x64, native Windows 11 ARM64
Core/Infrastructure tests, x64 and ARM64 unsigned MSIX builds, and the
installed ARM64 P0 fixture regression. GitHub's `pull_request` checkout used
synthetic merge commit `c6b09d302d2c9a15bfb2417a6ecd1c674616dbd2`,
recorded by the [native environment](evidence/ci/native-arm64-environment.json).

The retained [x64 Core](evidence/ci/core-tests.trx) and
[Infrastructure](evidence/ci/infrastructure-tests.trx) results report
**61/61** and **10/10** passes. The [native ARM64 Core](evidence/ci/native-arm64-core-tests.trx)
and [Infrastructure](evidence/ci/native-arm64-infrastructure-tests.trx)
results report the same counts, with OS build `10.0.26200.0`, native `Arm64`
process, and SDK `10.0.401`.

The [installed ARM64 record](evidence/ci/native-arm64-signed-install.json)
reports **14/14** P0 fixture workflows passed in interactive session 2 and
confirms that its test package and trust certificate were removed. The
[suite traces](evidence/ci/native-arm64-ui-suite/suite.json) are retained.
This verifies the diagnostic P0 package on ARM64; the M0 PDF text candidate
was exercised interactively on Windows 11 x64, and the production P1 reader
has not been installed or tested.

[Final PR CI run 36206500420](https://github.com/ilya-slalom/desktop-guides/actions/runs/36206500420)
passed all five jobs for review-fix head
`39daad9f768249415bc7b434221ccad5c3cbad59`. Both x64 and native ARM64
ran **61/61 Core** and **13/13 Infrastructure** tests; both unsigned MSIX
builds and the installed native ARM64 **14/14** P0 fixture regression passed.

P0's 14-fixture installed result remains [P0 evidence](../p0/results.md);
these M0 prototype results do not claim that the production shell, import,
resume coordinator, PDF reader, or release package is complete.

## M1 T03.2 migration result — 26 September 2026

The first M1 change on `feat/p1-m1-migrations` applies schema v1 and v2 in
order for a new library and upgrades a populated v1 library to v2. Before
upgrading existing data, it creates a consistent SQLite backup under the
app-data `.recovery` directory. Upgrade scripts and the version change run
in one transaction. Initialization checks schema objects, database integrity,
and foreign keys; it rejects newer or incomplete schemas without replacing
their data. An injected failure after creating the v2 index rolls back to a
usable v1 database and retains the v1 recovery copy.

The tests first failed against the v1-only repository (two failures, five
passes). On the Windows 11 x64 host, build `10.0.26200.0`, under
`E:\work\desktop-guides` with .NET SDK `10.0.401`, the completed locked run
passed **61/61 Core** and **19/19 Infrastructure** tests. The Release x64
WinUI build passed with zero warnings and errors. Tests cover the populated
v1 backup, failure rollback and retry, newer-schema and orphaned-row
rejection, incomplete v2 detection, and linked recovery-root rejection.
Production installed-app migration remains a later M1 shell check.

The PR #4 review follow-up added three Windows regression tests. Before the
fix, all three failed: a linked `library.sqlite` upgraded an external v1
database, a changed v1 `CHECK` constraint reached backup and migration, and
a v2 database with `Games.Notes` renamed to `Memo` passed initialization.
The resolver now rejects a linked database before SQLite opens it, including
repository reads. Initialization compares app-owned table and index
definitions against the version's migration scripts before backup or use;
unrecognized definitions stop with their existing data intact.

With the fix on the Windows 11 x64 host under `E:\work\desktop-guides`, locked
restore and Release tests passed **61/61 Core** and **22/22 Infrastructure**.
The unsigned Release x64 WinUI MSIX build succeeded with zero errors. It
reported one host tooling warning: `mspdbcmf.exe` was unavailable, so no
symbols package was generated. The installed production-app migration gate
remains open.

A second PR #4 review found two more path cases. Disposable Windows 11 x64
probes confirmed that a linked `library.sqlite-shm` modified an outside file
and an NTFS hard link at `library.sqlite` let initialization upgrade an
outside v1 database. Two regression tests failed against that PR head. The
resolver now checks SQLite sidecar paths and rejects Windows files whose
link count is not one before opening SQLite. The two focused tests passed
after the change. Locked restore, **61/61 Core** and **24/24 Infrastructure**
tests passed on the same host, including the active-WAL-writer migration
test. The unsigned Release x64 WinUI MSIX build succeeded with zero errors
and the same host symbols-tool warning.

[Final M1 migration PR CI run 36211490767](https://github.com/ilya-slalom/desktop-guides/actions/runs/36211490767)
passed all five jobs for `26ad3c3e119c7d264bd80b345a4a3f25510fead3`
before merge through [PR #4](https://github.com/ilya-slalom/desktop-guides/pull/4).
Both x64 and native ARM64 passed **61/61 Core** and **24/24
Infrastructure** tests; both unsigned MSIX builds and the installed native
ARM64 **14/14** P0 fixture regression passed. The installed test record
confirms package and certificate cleanup. The production P1 app and its
startup recovery remain separate M1 gates.

## M1 T15.2 startup reconciliation — 26 September 2026

On `feat/p1-m1-reconciliation`, `InitializeAsync` validates or migrates SQLite
before preflighting every pending `FileOperations` row. The v1 manifest
contains canonical guide IDs and exact generated content/staging/trash paths.
Prepared imports without a Guide remove only their named staged and moved
content roots. Prepared deletions restore named trash roots while retaining
guide metadata; committed deletions remove named trash roots. Each journal
row is cleared after its filesystem work, allowing interrupted work to retry.
The startup report counts resolved operations and untracked content/staging/
trash entries for later `Review orphan` UI. Unknown entries are retained.

The first eight recovery tests failed against the merged T03.2 repository.
The focused review exposed a prepared-import collision: staging and content
both existed. Its regression failed against the first PR head; the preflight
now stops recovery and retains both directories and the journal row for
review. After that fix, **15/15 focused recovery tests** passed on the
Windows 11 x64 host, build `10.0.26200.0`, under `E:\work\desktop-guides`
with .NET SDK `10.0.401`. Locked restore and the full Release suites passed
**61/61 Core** and **39/39 Infrastructure**. Tests include stage-only and
moved imports, prepared and committed deletions, partial two-guide restore,
unknown directories, malformed and overlapping manifests, nested links,
an NTFS junction, conflicting paths, and retry after a locked-file deletion
failure. The unsigned Release x64 WinUI MSIX build passed with zero errors
and the host's existing `mspdbcmf.exe` symbols-package warning. Native
ARM64 headless CI on PR #5 passed **61/61 Core** and **39/39 Infrastructure**
tests at implementation head `7a54a58`; the x64 headless lane passed the
same counts. Both unsigned MSIX package jobs passed. The installed ARM64
P0 diagnostic regression passed **14/14** fixture workflows, and its record
shows package and certificate cleanup. Installed production-app recovery
remains a separate check; no M2 file mutation is wired yet.

A second review found that a schema-valid uppercase `Guides.Id` was missed by
the case-sensitive committed-guide lookup. A prepared import could then
remove that guide's content directory. The regression failed on the first
PR head; recovery now compares parsed GUIDs, rejects malformed or duplicate
logical Guide IDs before cleanup, and counts content orphans by GUID. Locked
Windows 11 x64 Infrastructure tests passed **41/41** after this fix. PR #5
checks record CI for its current head.

PR #5 merged into `main` on 26 September 2026 as
`47c9e906c01e85eb9ce9f9759f6359390d809e52`. Its final x64 and native
ARM64 headless lanes passed **61/61 Core** and **41/41 Infrastructure**
tests; both unsigned MSIX packages built. The installed ARM64 P0 fixture lane
passed **14/14** after a transient first-attempt probe-status lookup failure
on `pdf-short`; its successful artifact records package and certificate
cleanup.

## M1 T11.1 production shell — 26 September 2026

`feat/p1-m1-shell` adds a typed Library/Game/Reader/Settings route coordinator,
an ID-based Back stack, and a separate WinUI production project with a
provisional `DesktopGuides.Preview` identity. The packaged shell opens a
persistent library under its package local data, shows empty and populated
routes, offers an explicit Resume action for a valid last-guide ID, and
returns to Library when a current route has a stale ID. The Reader route is a
placeholder until M3 adds the format adapters. The P0 diagnostic project
stays available for its fixture regression lane.

On the Windows 11 x64 host (build `10.0.26200.0`, .NET SDK `10.0.401`,
`E:\work\desktop-guides`), locked Release tests passed **69/69 Core** and
**41/41 Infrastructure**. Unsigned production x64 and ARM64 MSIX builds
passed with zero errors; each reported the existing missing `mspdbcmf.exe`
symbols-tool warning. Package inspection found the production assembly and
no P0 diagnostic assembly, fixture files, or probe controls. The test-only
metadata seeder successfully set valid and stale last-guide IDs.

Two temporary self-signed x64 install attempts
([first record](evidence/production-shell-host-install-1.json),
[retry record](evidence/production-shell-host-install-2.json))
passed signature verification but `Add-AppxPackage` failed with `0x80070005`
while initializing Windows Process Lifetime Manager. The deployment log also
records access failures when Windows attempted cleanup under
`C:\Program Files\WindowsApps\Deleted` for unrelated WhatsApp and Clipchamp
packages. Both attempts removed the temporary certificate and left no
preview app installed. Neither attempt exercised the installed shell.

After the user retried the signed x64 copy from an interactive desktop,
Windows installed `DesktopGuides.Preview_0.1.0.0_x64` and the user reported
that it launched successfully. A [host check](evidence/production-shell-host-interactive-install.json)
confirmed the installed package reports `Status: Ok` and the same signed
copy has a valid signature. The app was closed when checked, so launch is a
user observation rather than an automated local UI trace. The interactive
result narrows the earlier failure to the install context or host state; it
does not establish why the two SSH-driven attempts failed at PLM. The
temporary development signer remains in the host's `TrustedPeople` store
while this manual install is being evaluated and must be removed afterward.
This test certificate expires on 27 September 2026.

A [controlled fresh-install retest](evidence/production-shell-host-ssh-reinstall.json)
on the same host used the same signed MSIX after verifying its signature and
publisher trust. The existing package was removed only after three package
data files were backed up on `E:\work\desktop-guides` and checked by SHA-256.
`Add-AppxPackage` from SSH session 0 again failed with `0x80070005`. This
deployment's log confirms successful signature validation before `Failed to
initialize PLM` and `PackagesInUseClosed` failure events. An interactive
scheduled task then installed the same MSIX in desktop session 1. The library
file was restored and matched its backup; the package reported `Status: Ok`
and launched in session 1. The app was closed after the check, all temporary
tasks were removed, and the backup remains on the host. This rules out an
already installed preview package and missing signer trust as sufficient
explanations for the SSH failures. The exact PLM access denial remains
undetermined; WindowsApps cleanup warnings are present in the deployment
log, but their relation to the failure has not been established.

Future local-host installed runs follow the
[interactive E2E procedure](e2e-testing.md).

An [earlier production-shell UI job](https://github.com/ilya-slalom/desktop-guides/actions/runs/36217602539)
passed on the PR #6 Windows 11 x64 runner, build `10.0.26100.0`, in
interactive session 2. The
[signed install record](evidence/ci/production-shell/signed-install.json)
shows unsigned input SHA-256
`E78CC2C24856A0DD094C78006B7B4CDBD5FC511B3B9F59CC27C9CE43BC57D6E4`
and installed signed MSIX SHA-256
`60ECD7C582B74F4974F4DF61BA68E8D1AC7E26AFBE0FEA73E7CF7E873C5C5A14`
and successful package/certificate cleanup. The
[empty-Library trace](evidence/ci/production-shell/empty.json) verifies
fresh startup before seeding. The
[normal UIA trace](evidence/ci/production-shell/normal.json) verifies
Library, explicit Resume, Reader → Game → Library, Settings → Library,
and Library → Game → Reader → Game. The
[stale-ID trace](evidence/ci/production-shell/stale.json) verifies that a
missing last-guide ID does not expose Resume after relaunch. The smoke test
also checks that the P0 fixture picker is absent. This verifies T11.1's
installed routing behavior; reading the guide remains M3 work.

The duplicate push workflow exposed a smoke-test race after Settings → Library:
the test selected a game while the Library list was still loading
([failure trace](evidence/ci/production-shell/smoke-race-before-fix.json)).
A route-ready wait alone did not resolve the race in the next two CI runs:
the smoke test still could not find the row immediately after the status
became ready. The smoke script now polls for a visible row that supports
selection. The combined empty → seeded → stale installed test passed with
this change. The expanded route suite also checks that a stale Game or
Reader route clears its Back history.

The M1 review follow-up added single-instance activation and a normal-close
drain check. An initial custom `async Task Main` build crashed inside WinUI
during the first installed UI Automation query. The entry point now uses a
synchronous STA `Main` and waits for duplicate activation redirection while
pumping COM. [PR CI run 36225502945](https://github.com/ilya-slalom/desktop-guides/actions/runs/36225502945)
passed the signed installed x64 shell job at code head `88bf060`. Its
[diagnostic install record](evidence/ci/production-shell/signed-install-single-instance.json)
shows a second launch redirecting to the original window process, a
responsive empty Library afterward, all seeded and stale route checks, a
normal window close, and removal of the test package and certificate. The
lifecycle trace in that record was used for diagnosis; the production code
no longer writes it.
