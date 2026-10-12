# T17.2 release CI: evidence checklist and workflow lane design

Status: approved. PR a (the evidence checklist) is in review; PR b (the
workflow lane) follows it.
Prerequisites in the plan's T17.2 row are merged, including T16.1 (#88).
T16.2 and T16.3 left T17.2's prerequisites when the accessibility audit moved
to the final milestone after P3 (#89).

## Intent

A release is promoted only when the whole user journey has passed on an
installed, signed package, and each CI run leaves a readable record of what
was tested, on what, and that it cleaned up after itself.

Traces: T17.2 in [implementation-plan.md](implementation-plan.md) (M6),
[work-breakdown.md](../work-breakdown.md) S17 and T17.2,
[p1-technical-design.md](../p1-technical-design.md) S17, and the
[installed E2E procedure](e2e-testing.md).

## Scope

T17.2 splits into five pieces, done in this order:

| | Piece | This design |
| --- | --- | --- |
| A | One end-to-end installed workflow lane | Yes |
| B | An evidence checklist for every installed run | Yes |
| E | A CI scheduled-task entry point | Later, own design |
| C | A portable CI job | Later, own design |
| D | A fake-provider CI lane | Later, own design |

Already in place, and recorded rather than rebuilt:

- **Storage and security tests stop packaging.** Both package jobs depend on
  `core-tests`, which runs the locked Core and Infrastructure tests and the
  script tests.
- **The P0 regression lane is kept.** The `packages` and native ARM64 jobs
  stay as they are.
- **A failing installed UI blocks release promotion.** `release_ci_gate.ps1`
  requires the `windows-ci` push run on `main` to have concluded `success`.

## A: the workflow lane

**Setup:**
- A new `workflow` scenario group runs on a fresh data root.
- Before it starts, the installer copies four fixtures to a scratch
  "originals" folder outside the repo:
  - `p0/generated/txt-long.txt`;
  - `p1/html-long/`;
  - `p0/generated/pdf-long.pdf`;
  - `p0/pdf-access.pdf` (the tagged PDF).
- Stage 2 later deletes the TXT, HTML and long PDF originals. The tagged PDF
  stays as an untouched control.

Each stage is its own smoke mode, and the app restarts between stages. Every
step uses the keyboard and visible controls, never seeds.

1. **`workflow-build`:**
   - Create a game with Add game, entering a title manually.
   - Import the TXT, HTML and long PDF through the real file picker and
     Import guide dialog.
   - Check that each appears in the guide list with its format.
2. **Remove the originals:** the installer deletes the three source files
   and folders. Nothing inside the app changes.
3. **`workflow-read`:**
   - Open each guide and move it to a distinct place: TXT a few pages down,
     the PDF to page 3, and HTML scrolled about halfway.
   - Then close the app.
4. **`workflow-resume`:**
   - After a restart, open each guide in a different order.
   - Check each resumes at its own place, using the checks the progress
     smoke already uses.
   - Mark the PDF complete from the Game page, and check that the completion
     fact appears.
5. **`workflow-export`:** export a backup to a scratch `.zip` through the
   Save dialog, and check the saved status.
6. **`workflow-restore`:**
   - Remove the game, which leaves an empty library.
   - Restore the backup through Settings, confirm it, and wait for the
     swapped library.
   - Check that the game, its three guides, each place and the completion
     state are back.
7. **Cleanup:** the installer deletes the scratch folder and the backup, and
   removes the package as the other groups do.

**Pass rule:**
- Every stage must pass.
- Each stage writes its result JSON as today.
- The installer records each stage's duration.

**Code:**
- The picker helpers (`Choose-PickerFile` and the helpers beside it) move
  from the `import-*` branch to the smoke's shared helpers, as T20.2 did for
  the Save dialog helpers.
- The installer gets a `workflow` group and a `-WorkflowOnly` switch.

## B: the evidence checklist

A new `tools/p1/write_shell_evidence.ps1` reads one shard's installer report
and the fixtures it used, and writes two files next to the report.

**`evidence.md`** has one line per item, each with ✓ or ✗:

- **Run:** source commit, run ID, shard and its groups, start and end times.
- **Target:** OS build, CPU architecture, .NET SDK, Windows App Runtime and
  WebView2 versions.
- **Package:** family name and version, the source and signed MSIX SHA-256,
  and the certificate thumbprint.
- **Fixtures:**
  - Each fixture the shard used, by repo-relative path, with a SHA-256 the
    writer computes from the file. A folder fixture (an HTML guide) is
    hashed over its files in path order.
  - The fixture's ID from `tests/fixtures/p0/manifest.json` is added where
    one exists; only P0 has a manifest.
  - A fixture the report names but the writer can't find is a ✗ line, not
    skipped.
- **Scenarios:**
  - Each `Run-ShellSmoke` result by name, pass or fail, with its duration.
  - For the workflow shard, the stages follow the journey order.
- **Cleanup:**
  - The package is removed and the test certificate is no longer trusted.
  - The scratch folders are removed.
  - No test-owned scheduled task or process is left.
  - App event-log entries are counted and listed.

**`evidence.json`** holds the same data in machine-readable form.

**Anonymisation:**
- User-profile and runner paths become `%USERPROFILE%`, `%LOCALAPPDATA%` and
  `%TEMP%` forms.
- Machine and user names are dropped.
- No credential, provider key or library content appears.

**Timings:** `Run-ShellSmoke` records each call's start and duration in the
report; today it records neither.

**In CI:**
- Every installed shell shard, including the new `workflow` shard, runs the
  writer after its run. It appends `evidence.md` to the job's step summary
  and adds both files to the shard's evidence artifact.
- The writer runs even when the scenarios failed. A missing report becomes a
  ✗ line, not a crash.

**Tests:** `tools/p1/test_write_shell_evidence.ps1`, in the style of the
existing `test_*.ps1` scripts.
- It feeds a sample report and manifest through the writer.
- It checks each section, the path anonymisation, folder hashing, and the
  missing-fixture and failed-scenario lines.
- It runs in `core-tests` with the other script tests.

## CI and release promotion

- **The new shard:**
  - `workflow` becomes a fifth `production-shell-ui` shard, running on every
    PR and every push to `main`.
  - It is also a `shell-scope` choice for `workflow_dispatch`.
- **Release gate:**
  - A failing journey blocks every release through the existing gate.
  - A `test_release_ci_gate.ps1` case pins it: a run that failed only its
    workflow job must block.
- **Docs:** `e2e-testing.md`'s "Gates of record" lists the workflow shard,
  the evidence checklist, the packaging dependency on `core-tests`, and the
  kept P0 lane.

## Delivery

Two PRs on this design:

| PR | Contents |
| --- | --- |
| a: evidence checklist | `write_shell_evidence.ps1` and its tests, `Run-ShellSmoke` timings, CI wiring for every existing shard. |
| b: workflow lane | Shared picker helpers, the five `workflow-*` modes, the installer group, the CI shard, the release-gate test case and the docs. |

## Verification

- **PR a:**
  - Its script tests pass in `core-tests`.
  - The evidence appears in each shard's step summary on the PR run.
- **PR b:**
  - The `workflow` shard passes on a `dev-fast` dispatch, then on the full
    PR run.
  - The workflow shard's evidence lists the journey stages in order.
- **No host run is needed:** CI is the gate of record
  ([e2e-testing.md](e2e-testing.md)).
- **Status pages:** `implementation-plan.md` and `progress.md` record T17.2
  as partly done until E, C and D merge.

## Rulings

From the [implementation plan](t17-2-release-ci-plan.md):

1. **Fixtures per shard.** The existing groups pass the whole
   `tests/fixtures` folder to the seed tool, which picks files itself, so
   the installer can't name the files a shard used without rewriting every
   group.
   - The evidence lists the full fixture set available to the run, hashed:
     every entry in `p0/manifest.json` and every top-level item under
     `tests/fixtures/p1`.
   - The workflow group records exactly the four fixtures it copies, in
     `$report.fixtures`. The writer prefers that list when present.
2. **WebView2 version.** The installer report doesn't record it today.
   PR a adds `$report.webView2Version`, read from the EdgeUpdate client
   key that WebView2 registers.
3. **Scratch folders.** The existing groups leave no scratch folders, so
   the evidence prints the "scratch folders removed" line only when the
   report has `scratchFoldersRemaining` (workflow lane).

## T17.2 verification record

- PR a dispatch (`shell-scope=design`, `dev-fast`):
  [38229333554](https://github.com/ilya-slalom/desktop-guides/actions/runs/38229333554).
- PR a run: [38230667053](https://github.com/ilya-slalom/desktop-guides/actions/runs/38230667053)
  passed. All four installed shards published their evidence. The `pdf`
  shard passed on a rerun of its failed job: the first attempt failed
  `progress-changed` with a `TaskCanceledException` on a save, as the
  `main` push run 38218463295 did before this change.

## Out of scope

- E (CI scheduled-task entry point), C (portable CI job) and D
  (fake-provider lane): later designs.
- Changing which scenarios the existing shards run.
- Any release or packaging change beyond the gate test.
