# Installed end-to-end testing

The installed harness installs the production app, or unpacks the portable
exe, and drives it through Windows UI Automation in a real desktop session.
CI runs it on every pull request. This page covers how it works, how to run
it on a Windows host, and how to extend it. Building and unit tests are in
[contributing.md](contributing.md).

## What UI tests assert

UI tests check what the app's code controls, not how Windows or WinUI
render it.

- Assert what the app reports through UI Automation: names, states, focus,
  the status text, the stored data, the effective material or theme.
- Don't measure Windows rendering (Mica, Acrylic, blur, tint, fonts) against
  fixed values, and don't change host conditions to steady it.
- Use a pixel check only where it is the one way to see an app effect, and
  then tolerate any Windows rendering. For example, Acrylic fails only if it
  repeats the Solid fill within one level on every channel.
- High contrast isn't run: enabling it makes Windows rewrite the active
  theme, which the harness can't restore.

## How a run is put together

| Piece | Role |
| --- | --- |
| `tools/p1/start_installed_task.ps1` | The entry point, on CI and on a host. Registers an elevated, interactive scheduled task for the logged-in user and waits for it. |
| `tools/p1/run_installed_task_inner.ps1` | Runs inside that task. Starts the installer in a child PowerShell and copies its output to `installer.log`. |
| `tools/p1/windows_shell_install.ps1` | The installer. Signs and installs the MSIX (or checks the portable exe), runs the scenario groups, and cleans up. |
| `tools/p1/windows_shell_ui_smoke.ps1` | One UI Automation mode per call, run in a limited interactive task. Writes `<mode>.json`. |
| `tools/p1/DesktopGuides.ShellSeed` | A console tool the installer builds once. Its commands (`seed-*`, `describe-*`, `hold-write-lock`, …) seed the library and read it back. |
| `tools/p1/windows_provider_offline_controller.ps1` | Run separately and elevated. Blocks the installed app's network access for one scenario. |

### The entry point

`start_installed_task.ps1` takes the installer's parameters, plus
`-TimeoutMinutes` (default 45).

- The parameters cross into the task in `task-input.json` with the caller's
  `PATH` and `DOTNET_ROOT`. The file holds paths and switches, never
  credential values.
- The task runs the same PowerShell as the caller: `pwsh` on CI, Windows
  PowerShell 5.1 on a host. Its console runs headless, so no window takes
  focus from the app.
- Everything the installer prints, including `dotnet build` output, appears
  as it runs and is kept as `installer.log`.
- It exits with the installer's exit code. Its own codes are:
  - `2`: refused. There is no logged-in desktop, or an earlier
    `DesktopGuides-P1-Installed-*` task is still registered. Nothing starts.
  - `97`: the task ended without a result.
  - `98`: the installer threw.
  - `124`: timeout. It stops the run and lists what may be left, such as
    the installed package, but removes nothing.
- Its parameter list mirrors the installer's.
  `tools/p1/test_start_installed_task.ps1` fails if the two drift apart.

### Test gates

Test-only behavior is switched on by named events the harness creates before
it drives the window, such as `Local\DesktopGuides.Preview.ProviderFixtures`
or `Local\DesktopGuides.Preview.OnlineFixtures.<pid>`. Without the event the
app behaves normally. Gates that answer requests from fixtures are compiled
out of the Public lane (`#if !PUBLIC_LANE`), and CI checks that the Public
package doesn't contain them.

## Lanes

| Lane | Target | Notes |
| --- | --- | --- |
| `msix` | `-PackagePath <DesktopGuides.Production_<version>_x64.msix>` | Signs a copy with a one-day, nonexportable certificate, trusts it in `LocalMachine\TrustedPeople` for the run, and installs it. Writes `signed-install.json`. |
| `portable` | `-PortableExecutable <folder>\DesktopGuides.Production.exe` | Skips certificate, signing, install and uninstall, so the run itself needs no elevation. Writes `portable-run.json` with `mode: "portable"`. |

- Pass exactly one of `-PackagePath` and `-PortableExecutable`.
- A portable run isn't evidence for any signed-install, identity, upgrade or
  package-data check.
- A portable run refuses to start if `%LOCALAPPDATA%\DesktopGuides` exists.
  It deletes only that folder, and only if it created it. It leaves the
  `%TEMP%\.net` extraction in place, and rejects
  `-AllowOfflineFirewallRule`.
- CI tests the Preview lane. The released Public lane's window title,
  instance key and event prefix differ; only the `rc` group drives it.
- An unpackaged or loose-file run can help debug a reader. It doesn't
  replace the signed MSIX for install, identity, upgrade or package-data
  checks.

## Scenario groups

Groups run in this order. Each group after the first starts from an empty
data root, so a group run alone sees what the full run gives it.

| Group | Switch | Covers |
| --- | --- | --- |
| `core` | `-CoreOnly` | Library, Game, Reader and Settings routes on an empty and a seeded library; the game editor; single instance and second launch; closing during held reads; Resume rules and stale Resume; a long guide list; switching games while loading. |
| `design` | `-DesignOnly` | The four routes at wide and narrow widths in system, light and dark; window materials (Mica, Solid, Acrylic); reader layouts; keyboard focus; colorful icons. Restores the stored material and the Windows app theme. |
| `catalog` | `-CatalogOnly` | A 500-game library's virtualization and row text; catalog facts; library search; focus and selection on Back. |
| `txt` | `-TxtOnly` | The TXT reader: wide and long files, paging, missing and damaged guide files, Back during a held load; a damaged or missing library database. |
| `html` | `-HtmlOnly` | The HTML reader with a loopback canary on and off, and with WebView2 missing; place restore across resizes and text size; the page theme. |
| `pdf` | `-PdfOnly` | Go to page, zoom, keys, a locked PDF, and an offline relaunch after the originals are deleted. |
| `progress` | `-ProgressOnly` | Place saving and its rate, restore after a kill and a restart, approximate restore of changed guides. |
| `completion` | `-CompletionOnly` | The explicit completion choice on the Game page and in the Reader; reaching the end doesn't complete; a failed save. |
| `theme` | `-ThemeOnly` | The app theme setting, relaunch, following Windows, a failed save. |
| `text-size` | `-TextSizeOnly` | Text size steps and limits, restore per guide, PDF exclusion, a failed save. |
| `import` | `-ImportOnly` | Import preview, publication, duplicates, guide removal, multipage HTML and map imports. |
| `game-actions` | `-GameActionsOnly` | Renaming and removing games, with their guides and artwork. |
| `provider` | `-ProviderOnly` | Provider settings, search and add, refresh keeping local edits, credential removal, credential leak scans. |
| `backup` | `-BackupOnly` | The first-import reminder, export, cancel, a refused folder, restore into clean and populated libraries, an interrupted restore. |
| `workflow` | `-WorkflowOnly` | The whole journey through visible controls and the keyboard; see below. |
| `navigation` | `-NavigationOnly` | Find in guide, table of contents, bookmarks, reflow, multipage HTML guides. |
| `maps` | `-MapsOnly` | The map reader's zoom, pan and reopen, a strip wider than 16384 px, the Maps pane, removal clearing the cache. |
| `online` | `-OnlineOnly` | Find online against fixtures: browse, save, resave, pause and resume, Steam, VGMaps. |
| `rc` | `-RcOnly` | The release-candidate journey; see below. Runs only when asked for. |

### Choosing what runs

- No switch runs every group except `rc`.
- One `*Only` switch runs one group against a fresh install.
- `-Groups core,txt` runs several, still in the order above. Don't combine
  it with an `*Only` switch.
- `-PassFilter` takes wildcards and works with the `pdf` group alone, for
  example `-PdfOnly -PassFilter pdf-zoom-*,pdf-locked-light`. Pass names
  are `pdf-reader-light`, `pdf-zoom-light`, `pdf-keys-light`,
  `pdf-jump-light`, `pdf-locked-light` and `pdf-offline`. Other groups carry
  state from one mode to the next, so they take no filter.

### The workflow group

It never seeds, and it restarts the app between stages:

1. Add a game, and import a TXT, an HTML and a PDF guide from copies in a
   scratch folder.
2. Delete those copies.
3. Read each guide to a place.
4. After a restart, check each place, and mark the PDF complete.
5. Export a backup.
6. Remove the game, restore the backup, and check the guides, places and
   completion again.

Then it opens the PDF guide and closes the app five more times
(`workflow-pdf-exit-1` to `-5`). The group never kills the app, so any crash
or error-report entry for the app in the Application log fails it.

## CI shards

`production-shell-ui` runs 6 shards in each lane, each a fresh install on its
own runner. A failing shard doesn't cancel the others.

| Shard | Groups |
| --- | --- |
| `core` | core, txt, game-actions |
| `design` | design, catalog, completion, backup |
| `pdf` | pdf, progress, theme, text-size |
| `html` | html, import, provider, online |
| `workflow` | workflow |
| `navigation` | navigation, maps |

- Jobs are named `production-shell-ui (<lane>, <shard>)`. Each uploads
  `production-shell-ui-<lane>-<shard>` with its JSON results, screenshots,
  logs and evidence checklist.
- The `msix` lane checks the MSIX against its build manifest. The `portable`
  lane checks the zip against its `.sha256` and requires the exe to be the
  only file.
- CI passes `-FakeProviders`, so the provider group uses fixtures.
- The dispatch inputs `shell-scope`, `shell-lane` and `shell-pass` narrow
  the run; see [contributing.md](contributing.md#manual-dispatch).

## Providers: fixtures or real keys

- **Fixtures.** With `-FakeProviders` and no real IGDB credential file, the
  installer copies `tests/fixtures/p1/providers` into the app's
  `test\providers` folder, opens the `ProviderFixtures` gate, and types the
  fixtures' fake credentials. A Preview build then answers every provider
  request from those files, with no network. A request the fixtures don't
  cover fails the group and is listed in `provider-unmatched.json`.
- **Real keys.** Real credential files win over `-FakeProviders`. The
  defaults are `E:\work\igdb_credentials.txt` and
  `E:\work\steamgriddb_credentials.txt`; pass `-IgdbCredentialFile` and
  `-SteamGridDbCredentialFile` to use others. Each file holds `label: value`
  lines: `igdb client id`, `igdb client secret`, and `steamgriddb api key`.
- The fixtures don't follow changes in the real APIs. A host run of
  `-ProviderOnly` with real keys is the check for API drift.
- Credential values are read in memory and typed through UI Automation.
  They are never passed as arguments or written to results. After each
  scenario the harness scans the result and package folders for them in
  UTF-8 and UTF-16LE, and records only file names.
- With real keys, a missing credential file is recorded as `skipped`, not
  passed. With fixtures, a missing file is an error.
- The blocked-network provider scenario runs only with
  `-AllowOfflineFirewallRule` and the offline controller. Without the switch
  it is recorded as not run.

## Online fixtures

The `online` group never loads a live site.

- `tests/fixtures/p3` is copied to the app's `test\online` folder, and the
  `OnlineFixtures` and `ExternalLaunch` gates are opened.
- Both in-app browsers answer every request from
  `tests/fixtures/p3/manifest.json`, a list of URL, status, file and content
  type routes.
- The app's own requests (Steam's API, pages and images) go through
  `OnlineFixtureHandler` in `src/DesktopGuides.Infrastructure/Online`. It
  answers from the same manifest and leaves the API key out of the match.
  An unmatched request gets status 599 and a method, host and path line in
  `unmatched.json`, never its query or body.
- To cover a new page, add the file under `tests/fixtures/p3` and its route
  to the manifest. Live sites are checked by hand, never by automation; see
  [sources.md](sources.md).

## The release-candidate group

`-RcOnly` runs the journey on a release's draft assets. It needs
`-Lane Public`, `-ReleaseSigned`, `-PackagePath <candidate>` and
`-PreviousPackage <previous release>`, and runs alone. Both packages must be
named `DesktopGuides-<version>-x64.msix`, as the release assets are.

- Before installing, each package's signer must be the certificate pinned in
  [release/release-certificate.json](release/release-certificate.json). The
  certificate is trusted for the run only, and only if it wasn't already.
- The previous release installs and builds a library through the UI
  (`workflow-build`), and the originals are removed.
- The candidate upgrades in place: the version must rise, the package family
  stay, and `describe-library` read the same.
- Then `workflow-read`, `workflow-resume` and `workflow-offline`, then
  `workflow-export` and `workflow-restore`.
- `workflow-offline` relaunches the app while the offline controller blocks
  the package's executable. Each guide must open at its place with no
  non-loopback connection.
- The controller needs the user's authorization for each run. Without it the
  stage fails; it is never skipped.
- The sanitized result goes to
  [release/tested-matrix.md](release/tested-matrix.md). The release steps
  are in [releasing.md](releasing.md).

### The offline controller

The controller adds a Windows Firewall outbound block rule for the installed
app's executable only. It doesn't touch network adapters, so the host and
the SSH session stay connected.

- Start it elevated, with the same `-ResultDirectory` as the run, before or
  alongside the run. Ask the user first, every time.
- It waits up to `-TimeoutMinutes` (default 45) for the installer's request
  in `offline-handshake\`, and refuses any program that isn't an installed
  Desktop Guides executable.
- The rule lasts until the installer signals it is done, or at most
  `-WatchdogMinutes` (default 10, at most 15). It then removes the rule and
  confirms the removal in `restored.json`.
- It writes `offline-controller.json`. An unconfirmed removal fails the run.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\windows_provider_offline_controller.ps1 `
  -ResultDirectory <the run's result folder>
```

## Running on a Windows host

Use a host where a test user is logged in to a desktop. Installation and UI
Automation must run in that desktop session.

- **Over SSH, always go through `start_installed_task.ps1`.** An SSH session
  is session 0. Run there directly, the installer stops with "Shell install
  test requires an interactive desktop session", and `Add-AppxPackage` from
  session 0 fails Windows app lifecycle initialization (`0x80070005`). Don't
  retry the install from SSH.
- The task is elevated, because the MSIX lane trusts its temporary
  certificate in `LocalMachine`.

### Preparing a host copy

Copy sources as a zip and unpack them with `Expand-Archive`. Windows `tar`
reads the UTF-8 names in a tar stream with the OEM code page, which renames a
non-ASCII fixture folder and breaks the html group.

```sh
git archive --format=zip -o desktop-guides.zip HEAD
```

A tree from `git archive` has no generated fixtures and no restored
packages. Prepare it as CI does, or the run fails before installing:

```powershell
Expand-Archive desktop-guides.zip -DestinationPath E:\work\desktop-guides\<tree>
Set-Location E:\work\desktop-guides\<tree>
dotnet restore tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj --locked-mode
python tools\p0\make_fixtures.py
python tools\p0\verify_fixtures.py
```

- The installer builds the seed tool with `--no-restore`, so it needs that
  restore.
- The seed tool and the app must come from the same commit. The schema SQL
  is a raw string literal, so its stored text follows the source's line
  endings, and the app rejects a library whose schema text differs from its
  own. `.gitattributes` keeps `*.cs` at LF in every checkout; don't change
  that.
- Use a CI-built MSIX of the same commit, or build one in the tree; see
  [contributing.md](contributing.md#the-production-app-msix).

### Starting a run

```sh
ssh <host> "powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\<tree>\tools\p1\start_installed_task.ps1 -PackagePath <msix> -ResultDirectory E:\work\desktop-guides\<tree>\artifacts\e2e\<run-id> -HtmlOnly"
```

- With `-File`, an array argument such as `-Groups core,txt` arrives as one
  string. Use one `*Only` switch per run instead.
- Use the host for `-ProviderOnly` with real keys, the `rc` group, and to
  debug a CI failure. CI is the record for the other groups.

## Data and cleanup rules

The harness is built not to touch a user's own installation. Keep it that
way.

- **A fresh profile is required.** The MSIX lane refuses to start if the
  lane's package is installed, or if any `%LOCALAPPDATA%\Packages\<package
  name>_*` folder exists, even with no package registered. The portable lane
  refuses an existing `%LOCALAPPDATA%\DesktopGuides`.
- **Never run the harness against a user's working Preview installation.**
  Use a disposable VM or a dedicated test profile.
- **On a shared profile, back up first.** Close the app and make a verified
  backup of the package's user data outside `%LOCALAPPDATA%\Packages`
  before any uninstall. Record the initial package identity, version, signer
  and data hashes. Keep the signed recovery MSIX and an interactive recovery
  task ready.
- **After the run, restore the shared profile** to its recorded package,
  version and data, and verify the hashes and an interactive launch before
  deleting the backup. Recovery protects the host; it doesn't turn a failed
  run into a pass.
- **Remove only what the test owns.** Cleanup stops only verified test
  processes, removes only its own scheduled tasks, the package it installed
  and the temporary trust. A package it can't confirm as its own is left
  untouched and the run fails.
- **Incomplete cleanup fails the run.** A missing result, a wrong session,
  a remaining task, process, package or certificate, or an unremoved
  firewall rule all fail it.
- After a timeout (exit `124`), read the listed leftovers and remove them
  by hand. Check for `DesktopGuides-P1-*` scheduled tasks before the next
  run; a stale one makes the entry point refuse.

## Evidence

Each run writes to its `-ResultDirectory`:

- `signed-install.json` or `portable-run.json`: the run ID, OS build, CPU,
  mode, groups run, every scenario's result and duration, package identity
  and hashes, certificate thumbprint, Windows App Runtime, cleanup state,
  and any app crash entries from the Application log;
- `<mode>.json` for each UI Automation mode, and its screenshots as `.png`;
- `installer.log`, `task-input.json`, `task-started.json` and
  `task-exit.json` from the entry point;
- `offline-controller.json` and `offline-handshake\` when the controller
  ran; `provider-unmatched.json` when a fixture route was missing.

On CI, `tools/p1/write_shell_evidence.ps1` then writes `evidence.md` and
`evidence.json`, even when scenarios failed. They list the commit, run and
groups; the OS, CPU, .NET SDK, Windows App Runtime and WebView2; the package
or portable exe hashes and certificate; fixture hashes; each scenario with
its duration; and cleanup. Paths are rewritten to `%USERPROFILE%`,
`%LOCALAPPDATA%` and `%TEMP%` forms, with no machine or user name. The
checklist is also added to the job summary.

Keep result folders under `artifacts\`, which is gitignored. Never commit:

- user guide contents or raw package data;
- private keys or certificates with keys (`*.pfx` is gitignored);
- credential values, or files that might hold them;
- machine names, user names or unsanitized paths.

Link CI artifacts from the PR. For a release, record the sanitized result in
[release/tested-matrix.md](release/tested-matrix.md).

## Extending the harness

- **A new check in an existing group.** Add the mode name to the
  `ValidateSet` of `windows_shell_ui_smoke.ps1` and implement it there.
  Call it from the group's `Run-*Scenarios` function in
  `windows_shell_install.ps1` with `Run-ShellSmoke '<mode>'`. Seed data with
  a new `ShellSeed` command where the UI isn't what's under test.
- **A new group.** Add its `*Only` switch and its entry in the installer's
  ordered `$scenarioGroups`, mirror the switch in `start_installed_task.ps1`,
  add the name to the workflow's `shell-scope` options, and put it in a CI
  shard. A shard should take about six minutes.
- **A new installer parameter.** Mirror it in `start_installed_task.ps1`.
  Pass only paths and switches, never secrets.
- **Test-only app behavior.** Put it behind a named-event gate, and compile
  anything that answers from fixtures out of the Public lane.
- **Harness logic.** Pure helpers in `tools/p1` have `test_*.ps1` scripts
  that CI runs in `core-tests`. Add or update the matching test.
- Each result must carry the current invocation ID, process ID and session
  ID. Keep that check when adding result types; an old or foreign result
  must fail.
