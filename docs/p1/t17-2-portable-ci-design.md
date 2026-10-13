# T17.2 piece C: the portable CI job design

Status: approved; implemented on `feat/p1-t17-2-portable-ci`.
Pieces A, B and E merged in #90, #91 and #95; see the
[T17.2 design](t17-2-release-ci-design.md) and the
[piece E design](t17-2-task-entry-design.md).

## Intent

Releases ship a portable single-file build alongside the MSIX, but CI tests
only the MSIX. Piece C runs every installed shell scenario group against the
portable exe in CI, on every PR and push. A portable regression then fails
the run and blocks a release through the existing gate, instead of surfacing
only on the host.

Traces: T17.2 in [implementation-plan.md](implementation-plan.md) and the
[installed E2E procedure](e2e-testing.md) ("A portable CI job ... [is] T17.2
work").

## Today

- **Building it:** `release.yml` builds the portable exe with
  `dotnet publish -p:Portable=true -p:PackageLane=Public` and
  `tools/p1/package_portable_release.ps1`. `windows-ci` only runs that
  script's own tests.
- **Testing it:** the installer runs every scenario group against a
  portable exe with `-PortableExecutable`. The scheduled-task entry point
  forwards that parameter. The portable lane has run `core`, `html` and
  `pdf` on the host (#56); the other groups have never run portable.
- **Evidence:** the CI evidence step reads only `signed-install.json`.

## Decisions

1. **Scope:** all five shards (`core`, `design`, `pdf`, `html`,
   `workflow`) on every PR and push, as for the MSIX.
2. **Wiring:** one shard job with a `lane` dimension, not a second job, so
   the two lanes share one set of steps.
3. **Build lane:** the portable exe CI tests is built with the default
   **Preview** lane. The installed UI tests reach the app through named
   events prefixed `DesktopGuides.Preview.` (23 places in `tools/p1`), which
   only a Preview-lane build answers. The code is identical to the
   Public-lane release; only the lane's window title, instance key and
   event prefix differ. Testing the Public lane's names would need a
   lane-aware harness and is out of scope.

## Building the zip

A new `portable-packages` job:

- runs after `core-tests`, like `production-packages`, so a failing test
  stops packaging;
- restores and publishes `src/DesktopGuides.Production` with
  `-p:Platform=x64 -p:Portable=true`, using `release.yml`'s commands
  except that it keeps the default lane;
- packages the publish folder with
  `package_portable_release.ps1 -Commit <sha>`. That's the release's script
  and checks: the publish folder may hold only the exe, and the script
  writes the zip, `.zip.sha256` and the build manifest;
- uploads them as the `desktop-guides-portable-x64` artifact.

Its steps sit behind a YAML anchor, and a `dev-portable-packages` job reuses
them for `dev-fast` dispatches, as `dev-production-packages` does for the
MSIX.

## The shard matrix

- **Dimensions:** `production-shell-ui` gets two:
  - `lane: [msix, portable]`;
  - `target`: the existing five `{shard, groups}` entries, unchanged.

  GitHub crosses them into 10 jobs, named
  `production-shell-ui (<lane>, <shard>)`.
- **Dependencies:** the job needs `production-packages` and
  `portable-packages`. `dev-production-shell-ui` uses the same matrix and
  steps through the anchors.
- **Dispatches:** a new `workflow_dispatch` input, `shell-lane` (`both`,
  `msix` or `portable`; default `both`), narrows a dispatch to one lane.
  `shell-scope` still picks the group. Push and PR runs always run both
  lanes and all five shards.

## What each lane does differently

The shared steps are checkout, the .NET SDK, fixtures, the seed tool,
evidence and upload. These depend on `matrix.lane`:

| Step | MSIX | Portable |
| --- | --- | --- |
| Download | `desktop-guides-production-x64` | `desktop-guides-portable-x64` |
| Verify | interactive desktop, admin, MSIX against its build manifest (as today) | interactive desktop; zip SHA-256 against `.zip.sha256`; unzip; exactly one `DesktopGuides.Production.exe` |
| Run | `start_installed_task.ps1 -PackagePath <msix>` | `start_installed_task.ps1 -PortableExecutable <exe>` |
| Evidence | `-ReportPath signed-install.json` | `-ReportPath portable-run.json -PortableZipSha256 <hash>` |
| Upload | `production-shell-ui-msix-<shard>` | `production-shell-ui-portable-<shard>` |

**The installer's portable mode is unchanged.** It skips the certificate,
signing and install steps, refuses to start if `%LOCALAPPDATA%\DesktopGuides`
exists, deletes only the data folder it created, and writes
`portable-run.json`. The entry point still runs it in the elevated
interactive task, so both lanes start the same way.

**Evidence checklist:**
- For a portable run, the Package section adds "Portable exe SHA-256",
  from the report's `portableExecutableSha256`. It also adds "Portable zip
  SHA-256", from the new writer parameter `-PortableZipSha256`, which the
  writer reports only.
- The existing Cleanup line reports whether the portable data folder was
  removed.
- The `%TEMP%\.net` extraction stays in place by design.

## Groups that have never run portable

`design`, `catalog`, `txt`, `progress`, `completion`, `theme`, `text-size`,
`import`, `game-actions`, `backup` and `workflow` run portable for the first
time here. Some scenarios may assume a package; for example, the backup
group's protected-folder check already has a portable branch, and others may
not. Each such failure is fixed in the harness within this piece, with a
ruling recorded. An app defect found this way gets its own issue instead of
a harness workaround.

## Release gate

A failing portable shard fails the `windows-ci` push run on `main`, so
`release_ci_gate.ps1` refuses the release; the gate itself is unchanged.
`test_release_ci_gate.ps1`'s CI-file checks also require the push and PR
matrix's `lane` list to include `portable`, so the lane can't be dropped
silently.

## Tests

- **Release gate:** the new `test_release_ci_gate.ps1` check.
- **Evidence writer:** a portable case in `test_write_shell_evidence.ps1`.
  A sample `portable-run.json` with `portableExecutableSha256`, plus
  `-PortableZipSha256`, gives both hash lines and no MSIX package lines.
- **Order:** each check is written first and must fail first.

## Docs

- **"Gates of record":** `e2e-testing.md` describes the two lanes, the
  portable zip job and the `shell-lane` input.
- **"Portable build":** it says CI runs the full group set against the
  Preview-lane portable zip.

## Delivery and verification

One PR:

1. **Pipeline:** a `dev-fast` dispatch with `shell-lane=portable` and
   `shell-scope=workflow` checks the build, unzip and evidence pipeline on
   one shard.
2. **All groups:** a dispatch with `shell-lane=portable` and
   `shell-scope=all` runs every group in portable mode for the first time.
   Each failure is diagnosed and fixed as described above.
3. **Full run:** the full PR run passes with all 10 shards.

## Rulings

1. **Script tests ran in CI only.** The Windows host was unreachable over
   SSH, so the evidence-writer and release-gate tests ran under `pwsh` in
   `core-tests`, through `dev-fast` dispatches. They had no PowerShell 5.1
   run.
2. **No harness changes were needed.** Every scenario group passed in
   portable mode once its run wasn't stopped by an intermittent failure.
   No `if ($portable)` fix or skip was added, so the report has no
   `portableSkipped` list.

## T17.2 piece C verification record

- **Pipeline:** dispatch
  [38355366505](https://github.com/ilya-slalom/desktop-guides/actions/runs/38355366505)
  ran the portable `workflow` shard, which passed with both portable
  hashes in its evidence and the portable data folder removed.
- **Both lanes:** dispatch
  [38355995908](https://github.com/ilya-slalom/desktop-guides/actions/runs/38355995908)
  ran the `workflow` shard in both lanes, and both passed. Its `core-tests`
  failed once on #95's hidden-console check. That check is intermittent
  and isn't part of this piece.
- **First full portable run:** dispatch
  [38356005318](https://github.com/ilya-slalom/desktop-guides/actions/runs/38356005318)
  passed `core`, `design`, `html` and `workflow`. The `pdf` shard stopped
  in `progress-restored`, where an HTML guide stayed on "Loading guide…".
  Three portable `progress` reruns passed that scenario. One of them
  failed `progress-changed`, which is #94, also seen on the MSIX lane
  (timed save, 0 ms capture, 2,001 ms write).
- **Confirmation:** dispatch
  [38379616531](https://github.com/ilya-slalom/desktop-guides/actions/runs/38379616531)
  passed all five portable shards, with no ✗ line in any evidence
  checklist.
- **PR run:** [38383088085](https://github.com/ilya-slalom/desktop-guides/actions/runs/38383088085)
  passed on the first attempt, with all 10 shards. Merged in #100 (merge
  commit `77af7a5`).

## Out of scope

- The Public lane's own names (a lane-aware harness).
- The ARM64 portable build.
- Piece D, the fake-provider lane.
