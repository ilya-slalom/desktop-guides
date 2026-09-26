# P1 installed end-to-end testing

Status: test procedure for T17.2 and T17.3. The M1 shell route smoke is
implemented; the complete P1 scenarios below become executable as their
features land. This procedure does not close a release gate by itself.

## Installation decision

Run P1 installed workflows against the signed production MSIX. When SSH or a
CI controller starts a run, it registers a Windows scheduled task for the
logged-in test user with `New-ScheduledTaskPrincipal -LogonType Interactive`
and starts that task. The task runs `Add-AppxPackage`, launches the app, and
drives Windows UI Automation in the same desktop session. SSH stages inputs
and reads result files; it does not perform `Add-AppxPackage` in session 0.

The [Windows 11 x64 host retest](evidence/production-shell-host-ssh-reinstall.json)
validated the choice: after a clean uninstall, the same trusted, signed
package failed from SSH session 0 at PLM (`0x80070005`) and installed from an
interactive scheduled task in session 1. An unpackaged WinUI run can help
debug a reader, but it cannot satisfy the signed install, identity, upgrade,
or package-data gates. Loose-file registration is not a substitute for the
signed MSIX scenario.

The existing
[`production-shell-ui` CI job](../../.github/workflows/windows-ci.yml)
runs the M1 route smoke in an already interactive runner session. It is
partial T11.1 evidence. New full P1 installed E2E lanes use the scheduled
task entry point on both the local host and CI so their session and result
checks match. The task runner and full scenario suite still need
implementation under T17.2.

## Runner contract

1. Stage source, fixtures, scripts, and artifacts under
   `E:\work\desktop-guides` on the local Windows host. Record the source
   commit, MSIX identity/version/architecture and SHA-256, fixture manifest
   hashes, signer thumbprint and validity, OS/CPU, Windows App Runtime, and
   WebView2 versions. Keep signing keys out of source and result files. A
   run that needs an upgrade retains the same package identity and records
   both package versions and hashes.
2. Use a disposable VM or dedicated test profile when available. For a
   shared profile, close the app and make a verified backup of package user
   data outside `%LOCALAPPDATA%\Packages` **before** any uninstall. Keep the
   signed recovery MSIX and an interactive recovery task ready. A fresh
   install verifies the package is absent before adding it; an upgrade
   verifies the expected older package is present and does not uninstall it.
   Record the initial package identity, version, signer, and data hashes so
   cleanup can restore that exact state after either outcome.
   Never run the current `tools/p1/windows_shell_install.ps1` cleanup
   against a user's working Preview installation.
3. Check for a logged-in, usable desktop. Run a short interactive-task
   probe before any destructive step and record its user, session ID, and
   access to the existing package profile, if any, or a writable
   `%LOCALAPPDATA%` parent for a first install. Require a nonzero session ID
   matching the desktop's Explorer session. After installation, verify
   access to the new package profile before driving the UI. If the probe
   fails or the desktop is locked, stop before uninstalling. Do not retry
   the install directly from SSH.
4. Register a uniquely named task for the test user with
   `New-ScheduledTaskPrincipal`, `-LogonType Interactive`, and
   `-RunLevel Limited`. Its action starts a versioned PowerShell runner from
   the staged workspace. The runner performs the scenario's install or
   upgrade, verifies `Get-AppxPackage` identity/status, launches the
   packaged app, and runs UI Automation in the interactive session. The
   SSH or CI launcher waits for a per-run result JSON with a bounded timeout;
   starting the task alone is not a pass. This limited task does not change
   network adapters.
5. On failure, save the PowerShell error and AppX deployment Activity ID,
   collect `Get-AppPackageLog`, and record which phase failed. After both
   successful and failed scenarios on a shared profile, use the prepared
   interactive recovery task to restore the original signed package,
   including its version, and restore and verify backed-up user data.
   Recovery protects the host; it does not turn a failed test into a pass.
   Retain the backup until package identity, health, data hashes, and
   interactive launch are checked.
6. With networking available, create a fresh, empty WebView2 user-data
   profile for the HTML hostile-input case. Independently probe a unique
   per-run canary URL, then record its server request count as the baseline.
   Run the guide case and verify no new guide-originated server requests;
   retain the app request trace and profile identifier. Then run offline
   scenarios entirely on the Windows host because SSH may disconnect. An
   elevated, host-side controller task records active adapters and routes,
   verifies that disabling the designated adapter leaves no other egress
   path, checks that a separate external probe endpoint is reachable, and
   arms a repeating `SYSTEM` network-restore watchdog before disconnection.
   Set its first restore after the bounded offline UI timeout. The limited
   interactive task relaunches and checks the imported guides while
   disconnected; it never changes the adapter. Record failed
   external-reachability probes before relaunch, between guide checks, and
   after the final check. Any successful probe or available egress route
   fails the offline scenario, even if reading succeeds. The controller
   restores networking after the offline result or timeout. Keep the
   watchdog armed until the adapter, gateway, and external probe endpoint
   are reachable and the SSH or CI launcher acknowledges restored
   connectivity through a separate per-run acknowledgment file. Then have
   the elevated controller remove the watchdog. If restoration cannot be
   confirmed, retain the watchdog and fail the run. Read the recorded
   results after connectivity returns.
7. Stop only test-owned processes and remove only test-owned tasks,
   packages, and temporary trust. Restore a shared profile to its recorded
   initial package and data state, or verify that a previously empty test
   profile has no candidate package left. Do not remove an existing user
   package or its signer as generic cleanup. Record the final package/data
   state and any retained recovery artifact. A missing result, unexpected
   session, failed scenario, unsuccessful restoration, or incomplete
   cleanup fails the run.

## Scenario checklist

Each row needs a named UI trace or result file from the signed, installed
production app. Add cases as the dependent P1 tasks complete.

| Scenario | Required observation | Task / requirement |
| --- | --- | --- |
| Shell smoke | Fresh empty Library, Library/Game/Reader/Settings routes, rapid Game/Guide → Settings selections and Back, stale Resume, no P0 fixture controls, a positive redirected-activation acknowledgment, and a new window while the old process drains a guide action blocked by a test-held SQLite write lock. Existing CI smoke covers these routes with seeded metadata; Reader is still a placeholder. | T11.1, TR11.1 |
| Install and upgrade | Signed MSIX installs in an interactive session; an older version upgrades under the same identity without losing a populated library. Verify package version, launch, and data after restart. | T17.1, T17.3, TR17.2 |
| Import and offline reading | Add a game and import TXT, static HTML with local assets, and PDF through the UI. Remove the originals; while online in a fresh WebView2 profile, verify a reachable HTML canary receives zero guide-originated requests. Then remove all egress, relaunch, and open all three managed copies while recording disconnected state through the final check. | T04–T10, T17.3, TR17.1 |
| Independent state | Move to different positions in two guides, restart, and verify their locators separately. Change layout/theme, check exact or labeled approximate restore, and toggle completion explicitly; reaching the end must not mark complete. | T12–T14, TR12.1–TR14.2 |
| Removal and recovery | Cancel and confirm guide/game removal, restart around an interrupted operation, and verify only owned records and files change. Export outside app data and restore into both clean and populated libraries. | T15.2–T15.4, T20.1–T20.2 |
| Accessibility and PDF limits | Drive import/read/complete/export with keyboard and UIA; record focus, high contrast, DPI, and Narrator checks. Tagged PDF text must be accessible; scanned, locked, and long PDFs get their stated checks and limits. | T10.2–T10.3, T16.1–T16.3 |
| Prerequisites and target matrix | On a disposable runtime-free Windows 11 x64 VM, observe missing Windows App Runtime/WebView2 behavior and offline-installer recovery. Repeat the complete installed workflow on each advertised OS/CPU target. | T17.3, TR17.2 |

The runtime-free VM gate is deferred. Windows 10 x64 and the complete P1
ARM64 workflow remain untested. The current Windows 11 x64 shell smoke
does not imply that the later import, reader, backup, or release scenarios
have passed.

## Evidence and exit

Write per-run JSON and UI traces outside package data, under
`E:\work\desktop-guides\artifacts\p1-e2e\<run-id>` on the local host or
the equivalent CI artifact directory. Record scenario ID, start/end time,
result, package and fixture hashes, OS/CPU/prerequisite versions, signer
thumbprint, interactive task/user/session, package status, AppX Activity ID
on failure, fresh WebView2 profile identifier, online canary reachability,
baseline/final server request counts, and app request trace, elevated offline
controller and watchdog status, offline adapter/route snapshots and failed
reachability probes through the final guide check, network restoration and
launcher acknowledgment, and verified final package/data state.
Keep user guide contents, private keys, and raw package data out of
repository evidence. Link the sanitized result from `docs/p1/results.md`.

T17.2 closes only when its required production UI flows pass in CI alongside
the locked headless/security gates. T17.3 closes per advertised target only
after the signed install, upgrade, physical offline, accessibility, backup,
and target-specific prerequisite results pass. Record deferred targets as
untested in the support matrix.
