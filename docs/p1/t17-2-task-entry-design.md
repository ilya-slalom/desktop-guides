# T17.2 piece E: the scheduled-task entry point design

Status: design approved in brainstorming; spec awaiting review.
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

## Delivery and verification

One PR:

- **Tests:** `test_start_installed_task.ps1` passes on the host and in
  `core-tests`.
- **One shard:** a `dev-fast` dispatch of one shard passes through the
  entry point, with the installer's output in the job log.
- **Full run:** the PR run passes, with all five shards.
- **Status pages:** `implementation-plan.md` and `progress.md` record
  piece E; T17.2 stays open until C and D merge.
