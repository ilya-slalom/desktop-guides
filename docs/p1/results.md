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
remaining on the STA. [PR CI run 36225502945](https://github.com/ilya-slalom/desktop-guides/actions/runs/36225502945)
passed the signed installed x64 shell job at code head `88bf060`. Its
[diagnostic install record](evidence/ci/production-shell/signed-install-single-instance.json)
shows a second launch redirecting to the original window process, a
responsive empty Library afterward, all seeded and stale route checks, a
normal window close, and removal of the test package and certificate. The
lifecycle trace in that record was used for diagnosis; the production code
no longer writes it.

The next review pass tightened the installed gate and package cleanup. The
running window now acknowledges a redirected launch through an optional
test-created event; starting the scheduled task alone cannot pass the gate.
The runner removes only the package identity it recorded after its install
and uses per-run task names. Closing unregisters the instance key before
draining navigation, so another launch can open a new window.
[PR CI run 36227524454](https://github.com/ilya-slalom/desktop-guides/actions/runs/36227524454)
passed the signed installed x64 shell job at code head `ade1a59`. Its
[install record](evidence/ci/production-shell/signed-install-blocked-handoff.json)
shows the redirect acknowledgment and a new window while the closing
process remained alive. The
[queued-guide trace](evidence/ci/production-shell/queue-guide.json)
confirms that the old window began a guide action while a separate test
process held a SQLite write lock. After lock release, the old process exited,
the new window passed the seeded route smoke, and the test package and
certificate were removed. This is M1 shell evidence; the complete P1
installed workflow remains T17.2 work.

The next review follow-up at code head `464f7c8` holds an exclusive
package-local library lease through repository disposal. A new window can
open during close, but waits for the old session before loading the library.
The activation path retries if it selected an instance that starts closing.
The installed test now binds UI checks and process cleanup to recorded
processes in the interactive session, and queues a different guide from the
seeded Resume guide.

[PR CI run 36229239568](https://github.com/ilya-slalom/desktop-guides/actions/runs/36229239568)
passed the signed Windows 11 x64 installed-shell job. Its
[install record](evidence/ci/production-shell/review-followup/signed-install.json)
shows two different window processes during close; the
[waiting-window trace](evidence/ci/production-shell/review-followup/waiting-handoff.json)
observed the new window waiting for the library lease while the old one was
still alive. After the test released the SQLite write lock, the old window
exited and the new Library resumed with the guide selected in the
[pending-guide trace](evidence/ci/production-shell/review-followup/queue-guide.json).
The same run paused another launch after target selection, closed that
target, and verified that the launch opened a new window. The package and
temporary certificate were removed. The Windows 11 x64 host separately
passed 42/42 Infrastructure tests, the unsigned x64 production MSIX build,
and the shell-seed build. Both the push and
[PR workflow](https://github.com/ilya-slalom/desktop-guides/actions/runs/36229242647)
passed 8/8 jobs, including the native ARM64 P0 fixture regression. These
checks remain M1 shell evidence.

The close-boundary follow-up at code head `1581bde` makes a redirected
secondary wait for the first window's UI callback to accept activation.
The installed runner now launches the packaged executable through an
interactive task helper and records its returned process ID and start time;
UI checks and process cleanup use that specific launch record. Process
exit during cleanup no longer stops package and certificate cleanup.

[PR CI run 36230900475](https://github.com/ilya-slalom/desktop-guides/actions/runs/36230900475)
passed the signed Windows 11 x64 installed shell job. The
[install record](evidence/ci/production-shell/activation-ack/signed-install.json)
shows accepted redirection, the library lease wait during the blocked
guide write, and a new window after both a pre-redirection target close
and a close while activation was queued on the UI thread. The
[launch trace](evidence/ci/production-shell/activation-ack/launch.json)
records the interactive task's package executable, process ID, and session.
The package and temporary signer were removed. On the Windows 11 x64 host,
PowerShell parsing, 42/42 Infrastructure tests, the unsigned production MSIX,
and the shell-seed build passed. Both the push and
[PR workflow](https://github.com/ilya-slalom/desktop-guides/actions/runs/36230903529)
passed 8/8 jobs, including the native ARM64 P0 fixture regression. The
complete P1 installed workflow remains T17.2 work.

The next review fix at code head `625d3c3` replaces the process-wide
activation acknowledgment with a same-user pipe connection for each launch.
An accepted reply stays accepted after the old window closes; a delayed
reply on an abandoned connection cannot acknowledge a later launch. The
installed runner now waits for each verified test-owned process to exit
before seeding metadata or removing the MSIX, and retains ownership if
that wait times out.

The signed Windows 11 x64
[install record](evidence/ci/production-shell/activation-pipe/signed-install.json)
from [push run 36233298067](https://github.com/ilya-slalom/desktop-guides/actions/runs/36233298067)
shows acceptance before close without reopening, a new window when close
precedes acceptance, and the queued-activation and library-lease handoffs.
It also records removal of the temporary package and signer. The
[launch trace](evidence/ci/production-shell/activation-pipe/launch.json)
identifies the installed executable and interactive process. On the
Windows 11 x64 host, PowerShell parsing, 45/45 Infrastructure tests, the
delayed/already-exited/timed-out cleanup checks, and the unsigned x64
production MSIX build passed. Both the push and
[PR workflow](https://github.com/ilya-slalom/desktop-guides/actions/runs/36233300630)
passed 8/8 jobs, including the native ARM64 P0 fixture regression. The
complete P1 installed workflow remains T17.2 work.

The installed-runner handle follow-up at code head `12d21c2` makes the
interactive launch helper retain its child until the installer verifies a
second handle and acknowledges a per-launch token. A failed result write or
handoff stops the child through the helper's original handle. The installer
checks exact creation time, session, and image through its retained handle,
and cleanup terminates and waits through that handle after draining both
launch tasks. On the Windows 11 x64 host, harmless child-process checks passed
for rejected identity, an unrelated decoy PID, already-exited and timed-out
cleanup, failed result publication, accepted handoff, and stale
acknowledgment. PowerShell 5.1 parsing of the installer also passed.

The signed Windows 11 x64
[install record](evidence/ci/production-shell/handle-handoff/signed-install.json)
from [push run 36235737097](https://github.com/ilya-slalom/desktop-guides/actions/runs/36235737097)
shows the redirect and close handoffs, no process cleanup error, and removal
of the temporary package and certificate. The
[launch trace](evidence/ci/production-shell/handle-handoff/launch.json)
records the interactive session and handoff token. Both the push and
[PR workflow](https://github.com/ilya-slalom/desktop-guides/actions/runs/36235738707)
passed 8/8 jobs, including the native ARM64 P0 UI regression. These remain
M1 shell checks; the complete P1 installed workflow is still T17.2 work.

The next review follow-up at code head `92011f9` rejects an existing
`DesktopGuides.Preview_*` profile before the M1 shell test prepares or
installs a package, even if the package was previously unregistered. The
handoff test retains its launched child's handle for failure cleanup, and
activation waits for a reply through the remaining deadline. On the Windows
11 x64 host, PowerShell parsing, fresh-profile and process-handoff checks,
and 47/47 locked Infrastructure tests passed.

The signed Windows 11 x64
[install record](evidence/ci/production-shell/profile-boundary/signed-install.json)
from [push run 36237540783](https://github.com/ilya-slalom/desktop-guides/actions/runs/36237540783)
shows successful activation and removal of the temporary package and
certificate without a process cleanup error. Both the push and
[PR workflow](https://github.com/ilya-slalom/desktop-guides/actions/runs/36237542985)
passed 8/8 jobs, including the native ARM64 P0 UI regression. This closes
the M1 review findings; the complete P1 installed workflow remains T17.2
work.

The next review fix at code head `b5e8aec` waits for the registered
installed UI smoke task to reach `Ready` after its result is written,
before another scenario reuses the task name. The first empty-library
smoke keeps its helper alive for two seconds after publishing its result;
the next empty-library smoke starts immediately after the task becomes
ready. Both changed scripts passed Windows PowerShell 5.1 parsing on the
Windows 11 x64 host.

The signed Windows 11 x64
[install record](evidence/ci/production-shell/smoke-idle/signed-install.json)
from [push run 36238917270](https://github.com/ilya-slalom/desktop-guides/actions/runs/36238917270)
records successful delayed and follow-up smokes, with the test package
and temporary certificate removed. The complete P1 installed workflow
remains T17.2 work.

The next review follow-up at code head `a1f7ee5` transfers foreground
permission from a second user launch to the existing window, rejects
stale smoke results by invocation and process identity, and checks that
all test-owned scheduled tasks are removed. On the Windows 11 x64 host,
PowerShell 5.1 parsing, a read-only result check, a retained-task check,
an unsigned x64 production MSIX build, and production-package inspection
passed. The signed Windows 11 x64
[install record](evidence/ci/production-shell/foreground-result-cleanup/signed-install.json)
from [push run 36240550121](https://github.com/ilya-slalom/desktop-guides/actions/runs/36240550121)
shows a separate test window in the foreground before a duplicate
launch and the original shell in the foreground afterward. The
[focus trace](evidence/ci/production-shell/foreground-result-cleanup/second-launch.json.foreground.json)
records the two window handles and the duplicate's exit. The installed
run passed with no test package or temporary certificate left behind.
The complete P1 installed workflow remains T17.2 work.

The parallel
[PR run 36240553671](https://github.com/ilya-slalom/desktop-guides/actions/runs/36240553671)
for `a1f7ee5` exposed a duplicate-process exit between its image-path query
and the handle's exit signal. Code head `5bd2821` waits up to 500 ms for
that exact handle only after a process-information query fails; a still-live
process remains an error. The Windows 11 x64 host passed the handle and
launch-handoff checks, including a post-exit ownership check. The signed
Windows 11 x64
[install record](evidence/ci/production-shell/foreground-exit-transition/signed-install.json)
from [push run 36241173720](https://github.com/ilya-slalom/desktop-guides/actions/runs/36241173720)
passed the background-window activation and reports package, certificate,
and process cleanup. The
[focus trace](evidence/ci/production-shell/foreground-exit-transition/second-launch.json.foreground.json)
again records the test window before launch and the original shell after
the duplicate exited.

## M1 T11.3 reader shell — 26 September 2026

The `feat/p1-m1-reader-shell` branch adds a guide title/game/format header,
in-reader Back, a reading-surface host, and a capability-driven CommandBar.
The preview shell has no reader adapter yet, so it offers no reader
commands. Returning to Game retains the selected guide and restores focus
to its row; activating the selected row can reopen it.

On the Windows 11 x64 host, build `10.0.26200.0`, with source staged under
`E:\work\desktop-guides`, locked production restore and the unsigned Release
x64 MSIX build passed. The MSIX SHA-256 is
`1b20ada10646f124dd6e2f716df9fcf6ee8c84a7d9e411bbd95569fb2be6e293`.
The build reported only the existing missing-`mspdbcmf.exe` symbols warning.
The updated installed-smoke script passed Windows PowerShell 5.1 parsing.
The host retains an installed Preview package, so its fresh-profile M1
installer was not run there.

The signed installed Windows 11 x64
[push run 36244519302](https://github.com/ilya-slalom/desktop-guides/actions/runs/36244519302)
passed its shell job for code head `2187a0492aa0c1aa5218510289ffc8a8458d292f`.
The [install record](evidence/ci/production-shell/reader-shell/signed-install.json)
reports Windows build `10.0.26100.0`, signed MSIX SHA-256
`11f6bff9f38bccdf62cc5a3c911d972dbcdce37ece9b96ef207b4e5fb5d45827`,
all normal and close-handoff route smokes, stale Resume, and graceful exit.
The [normal trace](evidence/ci/production-shell/reader-shell/normal.json)
includes Reader → Game with selected-row focus, Enter to reopen the same
guide, and Settings round trips. The
[Reader screenshot](evidence/ci/production-shell/reader-shell/normal.reader.png)
shows the compact title/game/format header, collapsed pane, and honest
unavailable-reading message. The install record confirms that the temporary
package and signer were removed. Both x64 and ARM64 production package
builds, Core/Infrastructure tests, and the native ARM64 P0 installed
fixture regression passed in the eight-job run. The ARM64 result is a
diagnostic P0 check, not the complete P1 installed workflow.

The installed checks exposed two smoke assumptions that were corrected
before the passing run: reselecting an already selected guide does not
activate it, and the blocked-write handoff temporarily changes the Resume
guide. The final smoke activates a selected row with Enter and explicitly
returns to the route guide before later relaunches. Dynamic toolbar actions
remain an M3 adapter integration check; this M1 preview has no adapter.

The T11.3 review follow-up at code head
`920324342d0947bc3c04237f783dbfee479f2339` passed the signed installed
Windows 11 x64 shell job in [PR run 36247566880](https://github.com/ilya-slalom/desktop-guides/actions/runs/36247566880)
and [push run 36247564409](https://github.com/ilya-slalom/desktop-guides/actions/runs/36247564409).
The [install record](evidence/ci/production-shell/reader-shell/review-followup/signed-install.json)
reports Windows build `10.0.26100.0`, AMD64, success, and removal of the
temporary package and certificate. The [normal trace](evidence/ci/production-shell/reader-shell/review-followup/normal.json)
checks that Reader closes the navigation pane, its toggle opens and closes
it, pointer Back restores guide selection and focus, pointer input reopens
the selected row, and keyboard Back and Enter reopen it again. Screenshot
capture now fails the smoke if it cannot save a nonempty image; the retained
[Reader screenshot](evidence/ci/production-shell/reader-shell/review-followup/normal.reader.png)
shows the closed pane and unavailable-reading preview. The first review
follow-up run failed because WinUI's pane toggle did not expose a UI
Automation clickable point; using its visible bounds for the physical click
passed the installed rerun.

### T11.3 focused-row review follow-up — 26 September 2026

At code head `e81db1a81a23ac6bf13562a8daabfe9415b3a49d`, Enter now opens
the guide row that received the key even when another row remains selected.
The Windows 11 x64 host build under `E:\work\desktop-guides` passed the
unsigned Release MSIX build, and Windows PowerShell 5.1 parsed the updated
smoke script. The signed installed shell jobs passed in
[push run 36249362118](https://github.com/ilya-slalom/desktop-guides/actions/runs/36249362118)
and [PR run 36249364872](https://github.com/ilya-slalom/desktop-guides/actions/runs/36249364872).
The retained [install record](evidence/ci/production-shell/reader-shell/focused-enter/signed-install.json)
reports Windows build `10.0.26100.0`, AMD64, success, and removal of the
temporary package and signer. Its four normal route scenarios all include
the [focused-row trace](evidence/ci/production-shell/reader-shell/focused-enter/normal.json):
Ctrl+Arrow moves focus to the other guide without changing selection, Enter
opens that focused guide, and the test restores the original Resume guide
before later route checks. The [Reader screenshot](evidence/ci/production-shell/reader-shell/focused-enter/normal.reader.png)
was retained.

The T11.3 exit check now covers guide selection and focus. Library query
retention remains required and is assigned to T05.3 after T05.2 adds search.
In the PR run, the first native ARM64 P0 diagnostic attempt failed when
`pdf-short` could not read its startup status; the unchanged P0 lane passed
all 14 fixtures in the push run and in [PR attempt 2](https://github.com/ilya-slalom/desktop-guides/actions/runs/36249364872/attempts/2).

### T11.3 queued guide and loading-state review follow-up — 27 September 2026

At code head `ce6e823c879781325b53f4c41a3751d975a18a37`, Game-view guide
actions retain the later selection when an earlier settings write is delayed.
The [queued-action trace](evidence/ci/production-shell/reader-shell/queued-guide-loading/queue-later-guide.json)
selects guide B while A is still opening. The
[result trace](evidence/ci/production-shell/reader-shell/queued-guide-loading/later-guide-result.json)
confirms B opens, Back selects and focuses B, and Library offers Resume B.

The [preparation trace](evidence/ci/production-shell/reader-shell/queued-guide-loading/switch-game-prepare.json)
leaves the previous game's selected guide in the shell. With the next
game's read held by an exclusive SQLite lock, the
[loading trace](evidence/ci/production-shell/reader-shell/queued-guide-loading/switch-game-loading.json)
confirms the previous guide row and selected-guide action are unavailable
while the guide list is disabled. After release, the
[two-game trace](evidence/ci/production-shell/reader-shell/queued-guide-loading/switch-game.json)
confirms the second game's guide and Reader context.

The signed installed Windows 11 x64
[PR run 36286711186](https://github.com/ilya-slalom/desktop-guides/actions/runs/36286711186)
passed the production-shell UI job. Its
[install record](evidence/ci/production-shell/reader-shell/queued-guide-loading/signed-install.json)
reports OS build `10.0.26100.0`, AMD64, success, and removal of the
temporary package and certificate. On the local Windows 11 x64 host,
Windows PowerShell 5.1 parsed both changed scripts, the locked fixture
restore and Release fixture build passed, and the unsigned Release x64
production MSIX built. A separate disposable database probe confirmed
that the fixture's exclusive read lock blocks another read and releases
normally. The host's existing Preview package and profile precluded a
fresh-profile installed run there.

The first installed attempt at `aad96a9` stopped in the smoke runner:
its later-guide continuation started from Reader but still applied the
Library precheck. The `ce6e823` test correction removed that precheck,
and the installed scenario passed.

### T11.3 close admission and Resume review follow-up — 27 September 2026

At code head `8839d15a4a47d03866914ea1b203a921c032fde3`, Game-view guide
actions stop registering a new selection once Close has stopped the
navigation queue. Resume is saved after the selected Reader route renders,
and a failed settings save leaves that Reader visible with an error. The
installed selection test now holds the first guide's database read until
the later guide is selected; it no longer relies on a 300 ms delay.

The signed installed Windows 11 x64
[PR run 36289417891](https://github.com/ilya-slalom/desktop-guides/actions/runs/36289417891)
passed all nine jobs, including the signed shell job and the retained native
ARM64 P0 regression. The
[late-close traces](evidence/ci/production-shell/reader-shell/close-resume-review/late-guide-after-close.json)
select a second guide after Close is requested, while the first accepted
guide is held by a read lock; the
[install record](evidence/ci/production-shell/reader-shell/close-resume-review/signed-install.json)
includes the successful relaunch check that expects the first guide as
Resume after drain. The
[later-selection trace](evidence/ci/production-shell/reader-shell/close-resume-review/queue-later-guide.json)
and [result](evidence/ci/production-shell/reader-shell/close-resume-review/later-guide-result.json)
confirm the later guide's Reader, Back focus, and persisted Resume. The
[failed-later-guide trace](evidence/ci/production-shell/reader-shell/close-resume-review/later-guide-failed-result.json)
confirms that a removed later guide opens no Reader and leaves Resume
empty instead of saving the superseded guide. The existing close/relaunch
scenario now waits for the Reader to appear while its Resume write is held,
as recorded in the
[write-lock trace](evidence/ci/production-shell/reader-shell/close-resume-review/queue-guide-write.json).

The install record reports OS build `10.0.26100.0`, AMD64, success, and
removal of the temporary package and certificate. The Windows 11 x64 host
under `E:\work\desktop-guides` passed Windows PowerShell 5.1 parsing,
the locked ShellSeed restore and Release build, an unsigned Release x64
production MSIX build, and an isolated invalid-guide fixture probe. Its
existing Preview package and profile prevented a fresh-profile installed
run there. An earlier CI attempt at `2f3c740` used the old `"Opening
guide..."` expectation for the held-write scenario; it was updated to
observe the rendered Reader before the passing run.

### T11.3 render, cleanup, and focus review follow-up — 27 September 2026

At code head `9c4457f`, `RenderCurrentAsync` reports whether its requested
route finished loading. `OpenGuideAsync` saves Resume only when that result
is successful and the matching Reader remains current. The toolbar
installer now stops a timed-out test task, waits for it to leave `Running`,
then stops the test-owned app process and removes the test-owned MSIX
before checking for leftover package, process, task, and certificate.
Go to page and Find in guide reopen the CommandBar overflow after their
dialogs and restore keyboard focus to the command when it is still
available.

The Windows 11 x64 host, build `10.0.26200.0`, under
`E:\work\desktop-guides` passed Windows PowerShell 5.1 parsing, locked
restores, and unsigned Release x64 builds for the production shell and
linked toolbar test app. Each build reported only the host's existing
missing-`mspdbcmf.exe` symbols warning. The
[interactive host record](evidence/host/reader-toolbar-render-cleanup/review-result.json)
ran in desktop session 1. Its
[forced-timeout record](evidence/host/reader-toolbar-render-cleanup/timeout-install.json)
confirms that the parent stopped the still-running toolbar app and
removed its package after stopping the scheduled task. The following
[normal install](evidence/host/reader-toolbar-render-cleanup/normal-install.json)
passed with no test package, process, task, or certificate left behind.
The [UI trace](evidence/host/reader-toolbar-render-cleanup/toolbar-ui.json)
includes keyboard focus returning to both overflow dialog commands.
An initial host attempt exposed a smoke assumption: WinUI names the open
overflow toggle `Less app bar`; the passing test closes it by its
automation ID. The Reader render-error path was checked in code and built
on Windows; the host fixture did not inject a second-read exception.

The review-fix evidence head `fdffd08` passed all nine jobs in
[push run 36291334420](https://github.com/ilya-slalom/desktop-guides/actions/runs/36291334420)
and [PR run 36291336239](https://github.com/ilya-slalom/desktop-guides/actions/runs/36291336239).
The [CI timeout record](evidence/ci/reader-toolbar-render-cleanup/timeout-install.json)
confirms that the parent stopped the running test app and removed its
package; the following
[signed toolbar install](evidence/ci/reader-toolbar-render-cleanup/signed-install.json)
and [focus trace](evidence/ci/reader-toolbar-render-cleanup/toolbar-ui.json)
passed with cleanup. The
[signed production shell](evidence/ci/production-shell/reader-shell/render-cleanup-review/signed-install.json)
passed its route and Resume checks and removed its package and
certificate. Both architecture package builds, x64 and native ARM64
headless tests, and the retained native ARM64 P0 installed regression
passed. The native ARM64 installed result remains a P0 diagnostic check.

### T11.3 Reader render fault and toolbar inspection cleanup — 27 September 2026

At code head `41511d3`, the installed shell fixture pauses after the Reader
route opens and before its second guide metadata read. The disposable
fixture clears Resume and stores an invalid guide format, then releases the
read. The shell smoke requires the Reader loading error and, after restoring
the guide format and returning to Library, no Resume action. This checks
the render-success gate through the repository's actual read error. The
toolbar timeout fixture simulates a process exit during cleanup inspection
and requires removal of the confirmed test-owned package despite the
inspection error, followed by a successful normal install.

The Windows 11 x64 host, build `10.0.26200.0`, under
`E:\work\desktop-guides` passed PowerShell 5.1 parsing, locked restores,
and unsigned Release x64 builds of Production and the toolbar test app,
plus a Release ShellSeed build. Each MSIX build had only the existing
missing-`mspdbcmf.exe` symbols warning. In a separate disposable
database, ShellSeed changed the route guide from TXT to invalid format,
cleared Resume, and restored TXT while keeping Resume empty. The
[interactive host record](evidence/host/reader-render-fault-inspection/review-result.json)
reports session 1 and a passing timeout-to-reinstall sequence. Its
[timeout record](evidence/host/reader-render-fault-inspection/timeout-install.json)
shows the simulated inspection error, process stop, package removal, and
no remaining package, process, certificate, or task. The subsequent
[normal install](evidence/host/reader-render-fault-inspection/normal-install.json)
passed and cleaned up. The host's existing Preview package and profile
precluded a fresh-profile installed Reader run there.

The signed installed Windows 11 x64 shell in
[push run 36292903300](https://github.com/ilya-slalom/desktop-guides/actions/runs/36292903300)
passed all nine jobs at `41511d3`. Its
[route-open trace](evidence/ci/reader-render-fault-inspection/queue-reader-render-error.json),
[render-error trace](evidence/ci/reader-render-fault-inspection/reader-render-error-observed.json),
and [Resume result](evidence/ci/reader-render-fault-inspection/reader-render-error-result.json)
confirm the second-read error left Reader visible and did not save
Resume. The [production install record](evidence/ci/reader-render-fault-inspection/production-signed-install.json)
reports package and certificate cleanup. The
[toolbar timeout record](evidence/ci/reader-render-fault-inspection/toolbar-timeout-install.json)
reports the simulated inspection error and successful package removal;
the subsequent
[normal toolbar install](evidence/ci/reader-render-fault-inspection/toolbar-signed-install.json)
and [UI trace](evidence/ci/reader-render-fault-inspection/toolbar-ui.json)
passed. The parallel PR run `36292904629` passed the new toolbar gate but
stopped in the older later-guide Back check because a name-only UIA search
selected an element without an Invoke pattern. The smoke helper now
prefers the back button automation ID and requires an Invoke pattern.

At test-fix head `c94f54d`, all nine jobs passed in
[push run 36293452654](https://github.com/ilya-slalom/desktop-guides/actions/runs/36293452654)
and [PR run 36293454522](https://github.com/ilya-slalom/desktop-guides/actions/runs/36293454522).
The final PR
[later-guide trace](evidence/ci/reader-render-fault-inspection/final/later-guide-result.json)
passed Reader → Game → Library with guide selection, keyboard focus,
and Resume restored. Its
[Reader route](evidence/ci/reader-render-fault-inspection/final/queue-reader-render-error.json),
[read-error](evidence/ci/reader-render-fault-inspection/final/reader-render-error-observed.json),
and [empty-Resume](evidence/ci/reader-render-fault-inspection/final/reader-render-error-result.json)
traces repeated the new regression. The
[signed production install](evidence/ci/reader-render-fault-inspection/final/production-signed-install.json)
passed and removed its temporary package and certificate. The signed
toolbar job and retained native ARM64 P0 installed suite passed in both
runs. Windows 10 and the complete P1 ARM64 workflow remain deferred.

### T11.3 toolbar install ownership review follow-up — 27 September 2026

The toolbar installer now holds an exclusive per-user file lock before
its package-absent preflight and through all cleanup. A concurrent
invocation exits before creating a task, certificate, or result directory
and cannot classify the first run's package as its own. The installed
timeout fixture holds the first package and process while invoking a
second installer, then releases the first and runs a normal install.

The Windows 11 x64 host under `E:\work\desktop-guides` parsed the
PowerShell scripts and passed locked restores and unsigned Release x64
production and toolbar MSIX builds. The
[interactive result](evidence/host/reader-toolbar-exclusive-install/review-result.json)
reports desktop session 1 and a passing overlap-to-reinstall sequence.
Its [overlap result](evidence/host/reader-toolbar-exclusive-install/overlap-result.json)
confirms the second invocation was rejected while the first package and
process remained present. The
[first timeout cleanup](evidence/host/reader-toolbar-exclusive-install/timeout-install.json)
removed the package after the simulated process-inspection error, and
the [normal install](evidence/host/reader-toolbar-exclusive-install/normal-install.json)
passed with no test package, process, task, or certificate left behind.
An initial host attempt exposed that asynchronous `Start-Process` in
Windows PowerShell 5.1 returned no exit-code property; the helper now
uses the first installer's signed report to verify its expected timeout
and cleanup.

At code head `8b6008a`, all nine jobs passed in
[push run 36300190195](https://github.com/ilya-slalom/desktop-guides/actions/runs/36300190195)
and [PR run 36300193536](https://github.com/ilya-slalom/desktop-guides/actions/runs/36300193536).
The signed PR
[overlap trace](evidence/ci/reader-toolbar-exclusive-install/overlap-result.json)
records a rejected second installer, the first package and process
still present, and verified first-run cleanup. Its
[timeout install record](evidence/ci/reader-toolbar-exclusive-install/timeout-install.json)
shows removal after the simulated inspection error. The
[normal install](evidence/ci/reader-toolbar-exclusive-install/normal-install.json)
and [toolbar UI trace](evidence/ci/reader-toolbar-exclusive-install/toolbar-ui.json)
passed with no test package, process, task, or certificate remaining.
The signed production shell and retained native ARM64 P0 installed
suite also passed.

The first CI overlap attempt launched Windows PowerShell 5.1 from the
`pwsh` CI controller and exited before setup because `Get-FileHash`
could not be resolved, as recorded in its
[diagnostic trace](evidence/ci/reader-toolbar-exclusive-install/ci-startup-failure.json).
The controller now starts child installers with its own PowerShell
executable, matching both the CI and local-host environments.

### T11.3 toolbar process-scope review follow-up — 27 September 2026

The toolbar test task now writes a fresh process handoff after launching
its app. Parent cleanup validates the invocation, package, PID, creation
time, session, and executable before opening an owned process handle.
Only that handle can be terminated or counted as a leftover. The installed
overlap fixture keeps an unrelated process with the same name alive through
timeout cleanup and requires it to survive.

The Windows 11 x64 host under `E:\work\desktop-guides` parsed all three
changed PowerShell scripts. Its
[interactive result](evidence/host/reader-toolbar-process-scope/review-result.json)
reports desktop session 1, matching timeout and normal handoffs, and a
passing timeout-to-reinstall sequence. The
[overlap trace](evidence/host/reader-toolbar-process-scope/overlap-result.json)
records that the contender was rejected while the first app survived,
and that the unrelated same-name process survived the first cleanup.
The [timeout handoff](evidence/host/reader-toolbar-process-scope/timeout-process.json)
identifies the stopped process. The
[timeout cleanup](evidence/host/reader-toolbar-process-scope/timeout-install.json)
removed its package, task, and temporary trust entry after the simulated
inspection error. The
[normal install](evidence/host/reader-toolbar-process-scope/normal-install.json)
and [toolbar UI trace](evidence/host/reader-toolbar-process-scope/toolbar-ui.json)
passed with no test-owned package or process left; its
[handoff](evidence/host/reader-toolbar-process-scope/normal-process.json)
matches the install invocation.

At code head `c6a5aa3`, all nine jobs passed in
[PR run 36301957589](https://github.com/ilya-slalom/desktop-guides/actions/runs/36301957589)
and in [push run 36301955579, attempt 2](https://github.com/ilya-slalom/desktop-guides/actions/runs/36301955579).
The signed PR
[overlap trace](evidence/ci/reader-toolbar-process-scope/overlap-result.json)
records the rejected contender and surviving unrelated process. Its
[timeout handoff](evidence/ci/reader-toolbar-process-scope/timeout-process.json)
matches the process stopped in the
[timeout install record](evidence/ci/reader-toolbar-process-scope/timeout-install.json).
The [normal install](evidence/ci/reader-toolbar-process-scope/normal-install.json)
and [toolbar UI trace](evidence/ci/reader-toolbar-process-scope/toolbar-ui.json)
passed, and the [normal handoff](evidence/ci/reader-toolbar-process-scope/normal-process.json)
matches the install invocation. The signed production shell and retained
native ARM64 P0 suite passed.

Push attempt 1 failed in the unchanged production-shell UI smoke while
waiting for `ReaderHeading` during the rapid Settings path. The same path
passed in the parallel PR run and push attempt 2. No shell source changed
in this follow-up.
