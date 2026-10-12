# T17.2 piece E: the scheduled-task entry point design

Status: approved; implemented on `feat/p1-t17-2-task-entry`.
Pieces A (the workflow lane) and B (the evidence checklist) merged in #90
and #91; see the [T17.2 design](t17-2-release-ci-design.md).

## Intent

Installed shell runs start the same way on CI and on the Windows host:
through an elevated, interactive scheduled task for the logged-in user. CI
then exercises the path that SSH runs on the host depend on, and a host run
needs one command instead of a hand-built task.

Traces: T17.2 in [implementation-plan.md](implementation-plan.md) ("signed
installed production UI workflow CI started through an interactive
scheduled task") and the [installed E2E procedure](e2e-testing.md):

> New full P1 installed E2E lanes use the scheduled task entry point on both
> the local host and CI so their session and result checks match.

## Today

- **CI** calls `tools/p1/windows_shell_install.ps1` directly in the
  runner's interactive session. The installer already starts the app and
  each UI smoke through its own scheduled tasks.
- **Host runs over SSH** need the installer itself to run in an
  interactive task. Earlier sessions used an uncommitted prototype for
  that, adapted by hand each time.

## Scope

- **In:** every run of `windows_shell_install.ps1`, for the signed MSIX and
  the portable build: all installed CI shards and host runs.
- **Out:**
  - the `public-upgrade` gate (`windows_package_upgrade.ps1`) and the
    reader-toolbar install, which keep their current launch;
  - pieces C (portable CI job) and D (fake-provider lane), which get their
    own designs.
- **Unchanged:** the installer itself.

## Parts

### `tools/p1/start_installed_task.ps1`: the entry point

CI or SSH runs this script.

1. **Parameters.** It takes the installer's parameters as they are today:
   - `-PackagePath` or `-PortableExecutable`, and `-ResultDirectory`;
   - any `*Only` switch, `-Groups` and `-PassFilter`;
   - the credential-file paths and `-AllowOfflineFirewallRule`.

   It adds two of its own:
   - `-TimeoutMinutes`, default 45;
   - `-Installer`, the script to run, default `windows_shell_install.ps1`.
     This is the only test hook.
2. **Refusals.** It exits 2 without starting anything when:
   - the current user has no logged-in desktop, meaning no `explorer`
     process of theirs in a session other than 0;
   - a `DesktopGuides-P1-Installed-*` task is already registered, because
     an earlier run may still be going.
3. **Hand-over file.** It writes `task-input.json` to the result folder:
   - the installer parameters;
   - the caller's `PATH` and `DOTNET_ROOT`.

   It holds paths and switches only, never credential values. The
   environment matters because a task starts with the user's stored
   environment, not the CI job's. CI's `setup-dotnet` puts SDK 10.0.401 on
   `PATH` for the job only, and the installer calls `dotnet` throughout.
4. **The task.** It registers `DesktopGuides-P1-Installed-<id>`:
   - for the current user, `-LogonType Interactive`, `-RunLevel Highest`;
     elevation is needed because the installer trusts its test
     certificate in `LocalMachine`;
   - its action runs the inner script with the path of `task-input.json`.

   Then it starts the task.
5. **Waiting.** It prints each new line of `installer.log` as it appears,
   so the CI log streams live. It waits until `task-exit.json` exists, the
   task ends without writing it, or the timeout passes. After the result
   arrives, it prints the log's remaining lines.
6. **Exit.** It exits with the code in the table below. A `finally` block
   always stops and unregisters the task.

### `tools/p1/run_installed_task_inner.ps1`: inside the task

1. It reads `task-input.json`, and sets `PATH` and `DOTNET_ROOT` to the
   caller's values.
2. It runs the installer in the same process with the recorded
   parameters, copying all its output, including `dotnet build`, to
   `installer.log`.
3. It writes `task-exit.json` as `{ "exitCode": <int>, "error": <string or null> }`.
   An exception that escapes the installer is recorded as exit code 98,
   with its text.

The installer then starts its own launch and smoke tasks, as it does today.

## Exit codes

| Code | Meaning |
| --- | --- |
| The installer's | The installer ran to the end: 0 when it passed, 1 when it failed. |
| 98 | The installer threw before exiting. The error is in `task-exit.json` and the log. |
| 97 | The task never started, or ended without writing `task-exit.json`. |
| 124 | The run reached `-TimeoutMinutes`. |
| 2 | Refused: no logged-in desktop, or an earlier `DesktopGuides-P1-Installed-*` task is still registered. |

## Timeout

- **Ending the run.** The entry point stops the task, which ends the
  installer. The installer's own cleanup then doesn't run.
- **Leftovers.** The entry point checks for and prints what may be left:
  whether `DesktopGuides.Preview` is still installed, and whether any
  `DesktopGuides-P1-*` task remains.
- **No removal.** It reports leftovers but doesn't remove the package. On
  the host that could take a package the user relies on, and
  [e2e-testing.md](e2e-testing.md)'s cleanup rules ask first. CI runners
  are discarded after the job.
- **Evidence.** A killed installer writes no `signed-install.json`. The
  evidence checklist already reports that as ✗ "No installer report".

## CI

- **The install step.** "Sign, install, and test production shell" calls
  the entry point instead of the installer, with the same parameters and
  `-TimeoutMinutes 45`. The step fails when the entry point exits non-zero,
  as it does today.
- **Shards.** Every `production-shell-ui` and `dev-production-shell-ui`
  shard changes, because they share one set of steps through the YAML
  anchor.
- **Uploads.** `installer.log`, `task-input.json` and `task-exit.json` are
  uploaded through the existing `*.log` and `*.json` patterns.
- **Evidence.** The evidence and upload steps don't change.

## Host

`e2e-testing.md` replaces the manual "start the installer from an
interactive task with `-RunLevel Highest`" instructions with one command
over SSH, for example:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\start_installed_task.ps1 -PackagePath <msix> -ResultDirectory <dir> -ProviderOnly
```

The other host steps stay as they are:
- staging the sources;
- restoring `DesktopGuides.ShellSeed`;
- building the MSIX on the host;
- asking before touching the user's data folders.

## Tests

`tools/p1/test_start_installed_task.ps1` runs the entry point with fake
installer scripts. It runs in `core-tests`, whose runner has an interactive
desktop, as `test_windows_shell_task_cleanup.ps1` already relies on.

1. **Arguments arrive intact.** A path with spaces, an array (`-Groups a,b`)
   and a switch reach the fake installer unchanged.
2. **The exit code comes back.** A fake that exits 3 makes the entry point
   exit 3.
3. **A throw is reported.** A fake that throws gives 98, with its message in
   `task-exit.json` and the output.
4. **Output streams.** Lines the fake prints appear in the entry point's
   output and in `installer.log`.
5. **The environment carries over.** The `PATH` and `DOTNET_ROOT` the fake
   sees match the caller's.
6. **The timeout ends the run.** A fake that sleeps, with `-TimeoutMinutes 1`,
   gives 124.
7. **No task is left.** After every case, no `DesktopGuides-P1-Installed-*`
   task is registered.
8. **A stale task refuses the run.** A registered task with that prefix
   makes the entry point exit 2 and leaves that task alone.

The "no logged-in desktop" refusal can't be simulated on a runner that has
a desktop, so a reviewer checks that branch by reading.

## Rulings

From the [implementation plan](t17-2-task-entry-plan.md), and from the
first dispatches:

1. **The task runs the caller's PowerShell.** CI runs the installer under
   `pwsh` today, and the host under `powershell.exe` 5.1. The task action
   uses the entry point's own executable (`(Get-Process -Id $PID).Path`),
   so each keeps the shell it is tested with. Running every CI shard under
   5.1 would change what CI tests.
2. **The entry point mirrors the installer's parameters explicitly** and
   forwards only the ones the caller bound, so the installer's own
   defaults, such as the credential-file paths, still apply. A test
   compares the two parameter lists, so a new installer switch can't be
   forgotten here.
3. **Relative paths are resolved before the hand-over.** The task starts
   in `tools\p1`, not in the caller's folder, and CI passes
   `artifacts/production-shell/installed`. The entry point turns
   `-ResultDirectory`, `-PackagePath`, `-PortableExecutable` and the
   credential-file paths into absolute paths.
4. **`-TimeoutMinutes` is a `[double]`**, so the timeout test can use 0.25
   minutes instead of a full minute.
5. **On timeout the entry point also ends the inner process tree.** The
   inner script records its process ID in `task-started.json`. After
   `Stop-ScheduledTask`, the entry point runs `taskkill /PID <id> /T /F`, so
   no test-owned `powershell` or `dotnet` child keeps running. The app
   itself is started through the installer's own launch task, not as a
   child; it is reported as a leftover, not killed.
6. **The installer runs in a child PowerShell, not in the inner script's
   process.** The first dispatch kept only 10 lines in `installer.log`:
   `Out-Host`, which the installer uses for `dotnet build`, bypasses the
   output streams that in-process capture reads. A child with redirected
   output prints `Out-Host` and `Write-Host` to stdout. A transcript would
   also capture them, but on Windows PowerShell 5.1 it writes a header
   naming the machine and user into an uploaded file.
7. **`pwsh` puts its own folder first in `PATH` when it starts**, so under
   PowerShell 7 the installer's `PATH` is `$PSHOME` followed by the
   caller's whole `PATH`. The test allows exactly that and nothing else.
8. **The task's console is hidden** (`-WindowStyle Hidden`), as the
   installer's own launch and smoke tasks are. With a visible console on
   the test desktop, the `design` shard failed twice in a row on focus:
   `completion-restart-after` ("Web page guides couldn't start.") and then
   `keyboard` (`SetFocus` threw). The test now requires the installer's
   console window to be hidden.

## Delivery and verification

One PR:

- **Tests:** `test_start_installed_task.ps1` passes on the host and in
  `core-tests`.
- **One shard:** a `dev-fast` dispatch of one shard passes through the
  entry point, with the installer's output in the job log.
- **Full run:** the PR run passes, with all five shards.
- **Status pages:** `implementation-plan.md` and `progress.md` record
  piece E; T17.2 stays open until C and D merge.

## T17.2 piece E verification record

- First `workflow` dispatch through the entry point:
  [38254964955](https://github.com/ilya-slalom/desktop-guides/actions/runs/38254964955)
  passed. The task ran the installer with SDK 10.0.401 from the CI job and
  exit code 0, but `installer.log` lacked the `Out-Host` output (ruling 6).
- After ruling 6:
  [38256243225](https://github.com/ilya-slalom/desktop-guides/actions/runs/38256243225).
  The `workflow` shard passed, with the `dotnet build` output in
  `installer.log`. `core-tests` failed on the `PATH` assertion under
  PowerShell 7 (ruling 7).
- PR run 38258342552: `core-tests` and four shards passed. The `design`
  shard failed, and failed again on a rerun of the job, each time on a
  different focus-dependent scenario. Cause: the task's visible console
  window (ruling 8).
- PR run after ruling 8:
  [38264219275](https://github.com/ilya-slalom/desktop-guides/actions/runs/38264219275)
  passed on the first attempt, with `core-tests` and all five installed
  shards started through the entry point.
- In every run, the `workflow` shard's evidence lists the #93 crash on
  close after `workflow-read`; the stages still pass.
