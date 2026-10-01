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

For focused design-language iteration, pass `-DesignOnly` to
`tools/p1/windows_shell_install.ps1`. It still signs and installs the
production MSIX interactively, seeds realistic game metadata, checks all four
representative routes at wide and narrow widths in system/light/dark, then
runs the material passes: light and dark × Solid, Acrylic, and Mica, plus a
switch-and-relaunch check. The harness puts the stored material back to Mica,
restores the user's app theme, and removes the package and temporary trust.
The full run repeats these checks after the existing shell regression.

For provider E2E without the full shell regression, pass `-ProviderOnly` with
`-IgdbCredentialFile` and `-SteamGridDbCredentialFile` paths. Credential
values are read in memory and typed through UI Automation; they are never
passed as arguments or written to result files. Harness checks cover provider
settings, offline metadata display, live search and add, refresh preserving
local edits, and credential removal. After each scenario, the harness scans
result and package directories for credential values in UTF-8 and UTF-16LE;
file names are recorded, but the scan never outputs a value. A missing
credential file is recorded as `skipped`, not passed. With
`-AllowOfflineFirewallRule`, a separate elevated controller adds a temporary
Windows Firewall outbound block rule for the app's executable while a scenario
with saved credentials runs. The rule blocks only the test executable, lasts
at most 10 minutes, and requires the user's explicit authorization each time.
The elevated controller removes the rule and reports its removal. Without the
switch, the blocked-network scenario is recorded as not run. The full run adds
the provider pass after the material passes, starting from an empty profile.

Installed UI checks test what the app code controls, not Windows or WinUI.
The material passes check the effective material the app reports through UI
Automation, that no layer of ours hides the backdrop or leaves a seam, that
the Acrylic dialog style is applied, and that the chosen material persists.
Windows and WinUI render Mica and Acrylic, including blur, tint, the
transparency setting, and fallbacks, so the harness neither measures that
output against fixed values nor changes host conditions to steady it. A pixel
check is used only where it is the one way to see an app effect, and then it
tolerates any Windows rendering: Acrylic fails only if it repeats the Solid
fill within one level on every channel. Mica is recorded but not checked this
way, because with transparency off Windows draws it in the Solid fill color.

High contrast runs only in T16.2. Enabling it makes Windows rewrite the active
theme to `Custom.theme`, which this harness cannot restore.

## Portable build

Releases also ship a portable, self-contained build. Build it with
`dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 -p:Portable=true`
and then `dotnet publish` with the same project, `-c Release --no-restore
-p:Platform=x64 -p:Portable=true`. The output goes to
`artifacts\portable\bin\DesktopGuides.Production\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish\`,
kept apart from the MSIX build's `bin` and `obj` folders. Pass
`-PortableExecutable <publish folder>\DesktopGuides.Production.exe` instead of
`-PackagePath` to run the same scenarios against it. That mode skips the
certificate, signing, install, and uninstall steps, so the interactive
scheduled task needs no elevation. It refuses to start if
`%LOCALAPPDATA%\DesktopGuides` exists, deletes only the folder it created, and
rejects `-AllowOfflineFirewallRule`. It writes `portable-run.json` with
`mode: "portable"`. A portable run is not evidence for any signed install,
identity, upgrade, or package-data gate; the portable build's own release
gates come later under T17.3.

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

The M1 shell runner requires a fresh Preview package profile and refuses an
existing `DesktopGuides.Preview_*` directory even when no package is
registered. Use the full E2E backup/restore procedure above for a shared
profile. Each interactive launch helper writes a per-launch result with a
random handoff token, then keeps its original child handle until the
installer acknowledges ownership or the child exits. The installer opens
its own process handle and checks the exact creation time, desktop session,
and executable image through that handle before writing the matching
acknowledgment. If result publication or handoff fails, the helper stops
its child through the original handle. Cleanup terminates verified children
through retained handles and waits for exit; it drains both launch tasks
before package removal. A task that cannot drain or an unverified remaining
process fails cleanup. Clear old result and acknowledgment files before
each task start.

The installed shell UI smoke runner may publish a result before its task
exits. Wait for the registered task to become idle before reusing its name,
including after a failed result. The signed shell gate includes a delayed
task exit followed immediately by another smoke scenario. Each result must
carry the current invocation ID, process ID, and session ID. Fail on an
uncleared old result or a result with the wrong identity. Confirm that the
runner's scheduled tasks were removed before reporting cleanup success.

For the Reader render-failure fixture, open a guide in a disposable
installed profile and pause its second metadata read with process-scoped
named events. After the Reader route is visible, set that guide's database
format to an invalid value and clear Resume, then release the read. Require
the Reader loading error, restore the valid format, return to Library, and
verify Resume remains empty. Always release the event and restore the
fixture if the scenario fails.

The toolbar installed gate also runs a simulated timeout after install and
process exit during cleanup inspection. Require the parent to remove the
confirmed test-owned package despite the inspection error, verify no test
process, trust, or task remains, then perform a normal install on the same
runner. Keep the per-user install lock open from the package-absent
preflight through cleanup. While the first timeout run holds its installed
package and process, start a second run for the same identity. It must
fail before setup and leave the first package and process intact; after
releasing the first run, require its cleanup before the normal install.
The toolbar task writes a fresh process handoff with its invocation ID, PID,
start time, session, package, and executable path. Parent timeout cleanup
stops only that verified process. The child retains its launch handle until
the parent opens its own verified handle and acknowledges a fresh handoff
token. Start an unrelated same-name process before the first install in
the overlap fixture; it must survive and must not count as a leaked test
process. The interactive child publishes an invocation-bound package
receipt after installation. Parent cleanup removes a toolbar test package
only when that receipt matches the installed package; otherwise the gate
fails and preserves it. Wait separately for installation and process
handoff, and bound the overlap contender so a stalled child cannot hold
the fixture open. The signed normal toolbar run includes an artificial
31-second pre-install delay to verify the installation wait exceeds the
former 30-second cutoff.

### Gates of record

CI `production-shell-ui` runs the full installed harness on every PR and is the
gate of record for the shell, design-language, material and catalog scenarios.
It uploads the JSON results and screenshots, and PRs link to that artifact.
Provider live scenarios skip on CI, because the runner has no credential files.
Run them on the Windows host with `-ProviderOnly`, using the user's keys and
normal provider requests. Use the host for other scenarios only to debug a CI
failure; `-CatalogOnly`, `-ImportOnly` and `-DesignOnly` run one scenario group
against a fresh install. A portable CI job, a fake-provider CI lane and a CI
scheduled-task entry point are T17.2 work.

When a host run installs a CI-built MSIX, stage the source with Windows line
endings before building `DesktopGuides.ShellSeed`. The schema SQL is a raw
string literal, so its stored text follows the source line endings, and the app
rejects a library whose `sqlite_master` text differs from its own build.

## Scenario checklist

Each row needs a named UI trace or result file from the signed, installed
production app. Add cases as the dependent P1 tasks complete.

| Scenario | Required observation | Task / requirement |
| --- | --- | --- |
| Shell smoke | Fresh empty Library, Library/Game/Reader/Settings routes, rapid Game/Guide → Settings selections and Back, stale Resume, no P0 fixture controls, and a positive UI-accepted second launch that brings a background window to the foreground. Verify normalized boundary-length game input, a bounded 2,000-character note, and close during a blocked Edit metadata read without a late dialog or stalled shutdown. While guide A's lookup is held by a fixture read lock, select guide B and verify B's Reader, Back selection/focus, and persisted Resume. During another held lookup, request Close and select a later guide; the accepted earlier guide must still become Resume after drain. In a disposable fixture, remove a still-displayed later guide and clear Resume before the held lookup; its failed open must leave Resume empty. Also corrupt a guide after its Reader route opens but before its second metadata read; the render error must leave Resume empty after the format is restored and the user returns to Library. Hold the second game's metadata read and verify the previous guide row and selected-guide action are unavailable during loading, then check the second game's Reader after release. Launch a new window while an old guide write is blocked; verify the new window waits for the library lease and then shows the distinct guide saved by the old window. Pause a second launch after it selects the old instance, and pause a callback after it reaches the UI queue; close the old window in each case and verify the launch takes over. After a second launch receives UI acceptance, close the old window before the second process exits and verify it does not reopen. Retain verified process handles from the interactive launch handoff and wait for handle-confirmed exit before seeding or package cleanup. Reader is still a placeholder. | T04.1, T11.1, T11.3, TR11.1 |
| Design language | With realistic game metadata, capture Library, Game, Reader, and Settings at wide and narrow widths. Verify semantic headings, named controls, keyboard focus, native `TitleBar` Back/pane actions, the Toolkit `SettingsCard` name and bounds, full-width route layout, automatic dismissal of routine `InfoBar` status, no title-bar/content overlap, long text, and system/light/dark resources in each window material. Solid strips across the pane/content boundary show no seam, Acrylic does not repeat the Solid fill, and the Acrylic edit-game dialog differs from the Solid one. Restore the stored material to Mica and the original Windows app theme exactly. | T11.4, TR11.3 |
| Library catalog | Seed 500 games with valid, corrupt and missing managed artwork, a 160-character title, and German, Arabic and Japanese titles. In light and dark: fewer than 80 realized `GameList` rows at the top and after scrolling to the last game; the long-title row no taller than a short one; Tab into the list without opening a game, Ctrl+Down then Enter opens the focused game, and End opens the last; rows stay inside `GameList` at 600 px; no status, no non-loopback TCP connection and no credential blob. The long-title row's HelpText is `PC, IGDB, No guides`, and after scrolling to the end the last row and two recycled rows describe their own games. | T05.4, T05.1, TR05.3, TR11.3 |
| Catalog facts | Seed three games whose activity order differs from title order and four guides in each reading state. In light and dark: the Library lists Zeta Archive Game, Facts Test Game and Empty Test Game in that order with HelpText `Windows, Manual, 1 guide`, `PC, IGDB, 4 guides` and `Manual, No guides`; the Game page lists Main Story Walkthrough, Collectibles Map, Weapon Upgrade Guide and Achievement Checklist, each Name its title and each HelpText its format, reading state and last opened, with `Not started` for the unread guide. In `long-list`, a 99-guide game realizes fewer than 60 `GuideList` rows. | T05.1, TR05.2, TR05.3, TR11.3 |
| Library search | Seed Zeta Archive Game with the unread guide Complete Walkthrough, Pokémon Crystal, Ōkami HD and ドラゴンクエストXI. In light and dark: typed `POKEMON`, then `okami`, `ドラゴン` and fullwidth `ＸＩ`, each leave one row and announce `1 of 4 games match.`; `walkthrough` leaves Zeta with HelpText `Manual, 1 guide, Guide: Complete Walkthrough`; `zzzz` shows `No games or guides match "zzzz".` and hides `GameList`; Clear search restores all four rows, empties and focuses the box and hides itself; Zeta's guide reads `Not started`, and Back keeps `walkthrough` applied; no non-loopback TCP connection. `empty` shows search disabled, and every Library-ready wait asserts the loading view is gone. | T05.2, TR05.1, TR05.2, TR11.3 |
| Game actions | Seed Linked Rename Game, provider-linked with artwork and the guides Alpha Route Guide (about 45%, opened yesterday) and Beta Route Guide, Empty Linked Game, provider-linked with artwork and no guides, and Guided Remove Game, provider-linked with artwork and two guides (Guided Walkthrough, TXT and the Resume guide; Guided Map Guide, HTML with an image). In light and dark: Guided Remove Game's Remove game opens `Remove Guided Remove Game and its 2 guides?` stating 3 managed files; Escape keeps both guides with focus on Remove game; Remove shows the Library with `Removed Guided Remove Game.`, no Resume and focus on Add game. Linked Rename Game shows Remove game enabled; renaming it to Renamed Linked Game keeps Beta selected, focus on Edit game and the summary, and its Library facts are unchanged. With the Library filtered to `Empty`, Empty Linked Game's Remove game opens `Remove Empty Linked Game?`; Escape and Enter cancel with focus back on Remove game; Remove shows the Library with `Removed Empty Linked Game.`, the `Empty` query kept and its no-results view, focus on Add game and Back disabled. `describe-actions` finds one game with its original ID, artwork, Guide IDs, reading state and Resume, and no artwork folder for the removed game, and no rows, content, artwork, trash entries or file operations for Guided Remove Game. After a relaunch, Guided Remove Game is still gone, Resume opens Beta under the new name, Back keeps Beta selected, Alpha still reads about 45 percent, and no non-loopback TCP connection is made. | T04.2, T04.3, TR03.1, TR04.1, TR04.2, TR04.3, TR11.3 |
| Import preview | From a seeded game, Import guide opens the system Open dialog; the smoke enters each fixture path in its File name box and presses Open. In light and dark: `txt-legacy` shows its title, format, file name and both encodings, and CP437 shows its sample; `html-static` shows details under a native heading; `html-hostile` shows details and warnings in headered groups; `pdf-locked` shows the password-protected message; Close and picker Cancel return focus to Import guide. Afterwards no guide, file operation, or staged or managed file exists. | T06.1, T06.2, TR06.1, TR11.3 |
| Import publication | From a seeded game, pick `txt-legacy` and check Import stays disabled until CP437 is chosen. Enter a unique title and press Import. In light, the dialog closes and the Game page lists the new guide selected and focused. Afterwards the library has one CP437 guide, one content directory per guide, and no file operation or staging entry. | T06.3, TR06.2, TR06.3, TR11.3 |
| Duplicate import | After Import publication, pick `txt-legacy` again in the same game and choose CP437. The preview's `ImportDuplicate` InfoBar names the existing guide, and the primary button reads Import another copy. In dark, import a copy under a new title; it is selected and focused. In light, choose Open existing; the dialog closes and the Reader shows the first guide. Afterwards the library has two CP437 guides, two content directories, and no file operation or staging entry. | T06.4, TR06.3, TR11.3 |
| Guide removal | After Duplicate import, open the imported guide, go back and invoke Remove guide. The `RemoveGuideDialog` names the guide and 1 managed file. In dark, Cancel returns focus to Remove guide and the library is unchanged. In light, Remove closes the dialog, the status reads "Removed {title}.", and the remaining guide is selected and focused. Afterwards the library has one guide, one content directory, one reading state, one preferences row, and no trash entry, file operation or staging entry. | T15.3, TR15.1, TR15.2, TR11.3 |
| Install and upgrade | Signed MSIX installs in an interactive session; an older version upgrades under the same identity without losing a populated library. Verify package version, launch, and data after restart. | T17.1, T17.3, TR17.2 |
| Provider-backed game addition | Search the selected provider, distinguish editions, add one result, and verify its provider provenance, normalized metadata, and validated artwork. Disconnect and relaunch to confirm the cached display remains usable. Exercise duplicate selection, cancellation, malformed/oversized data, unavailable service, and `Create manually`; failed attempts leave no game or managed artwork. Refresh source data and verify local title, platform, and notes remain unchanged. | T04.4, TR04.3, TR04.4 |
| Import and offline reading | Add a game and import TXT, static HTML with local assets, and PDF through the UI. Remove the originals; while online in a fresh WebView2 profile, verify a reachable HTML canary receives zero guide-originated requests. Then remove all egress, relaunch, and open all three managed copies while recording disconnected state through the final check. | T04–T10, T17.3, TR17.1 |
| Independent state | Move to different positions in two guides, restart, and verify their locators separately. Change layout/theme, check exact or labeled approximate restore, and toggle completion explicitly; reaching the end must not mark complete. | T12–T14, TR12.1–TR14.2 |
| Removal and recovery | Cancel and confirm guide/game removal, restart around an interrupted operation, and verify only owned records and files change. Export outside app data and restore into both clean and populated libraries. | T15.2–T15.4, T20.1–T20.2 |
| Accessibility and PDF limits | Drive import/read/complete/export with keyboard and UIA; record focus, high contrast, DPI, and Narrator checks. Tagged PDF text must be accessible; scanned, locked, and long PDFs get their stated checks and limits. | T10.2–T10.3, T16.1–T16.3 |
| Prerequisites and target matrix | On a disposable runtime-free Windows 11 x64 VM, observe missing Windows App Runtime/WebView2 behavior and offline-installer recovery. Repeat the complete installed workflow on each advertised OS/CPU target. | T17.3, TR17.2 |

The runtime-free VM gate is deferred. Windows 10 x64, 200% display scaling,
and the complete P1 ARM64 workflow remain untested. The current Windows 11
x64 shell smoke does not imply that the later import, reader, backup, or
release scenarios have passed.

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
