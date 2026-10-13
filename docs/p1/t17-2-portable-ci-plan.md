# T17.2 Piece C (Portable CI Job) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** CI runs every installed shell shard against the portable
single-file exe as well as the MSIX, on every PR and push.

**Architecture:**
- A `portable-packages` job builds the Preview-lane portable zip with the
  release's packaging script.
- The `production-shell-ui` matrix gains a `lane` dimension
  (`msix`, `portable`) crossed with the existing five shard targets. The few
  lane-specific steps branch on `matrix.lane`.
- The evidence writer reports the portable exe and zip hashes.

**Tech Stack:** GitHub Actions, PowerShell 5.1 and 7, .NET 10 single-file
publish.

**Spec:** `docs/p1/t17-2-portable-ci-design.md`

**Target:** T17.2, piece C, in one PR on `feat/p1-t17-2-portable-ci` (spec
commit `3226d1f`).
**Prerequisites:** T17.2 pieces A, B and E (#90, #91, #95), #97 and #98,
all merged.

## Global Constraints

- The portable exe CI tests is built with the default **Preview** lane
  (no `-p:PackageLane`), published with
  `-c Release --no-restore -p:Platform=x64 -p:Portable=true`, and packaged
  by `tools/p1/package_portable_release.ps1 -Commit <sha>`.
- The shard targets are unchanged:
  - `core`: `core,txt,game-actions`;
  - `design`: `design,catalog,completion,backup`;
  - `pdf`: `pdf,progress,theme,text-size`;
  - `html`: `html,import,provider`;
  - `workflow`: `workflow`.
- Push and PR runs run both lanes and all five shards. `shell-lane`
  (`both`, `msix`, `portable`; default `both`) narrows only dispatches.
- Shard names are `production-shell-ui (<lane>, <shard>)`. Artifacts are
  `production-shell-ui-<lane>-<shard>`.
- PowerShell stays ASCII-only and runs under 5.1 and 7.
- No app code changes. A harness fix for a portable-only failure gets a
  ruling. An app defect found this way gets its own issue.

## Review Focus

1. **A dispatch that narrows the lane but not the scope, or the
   reverse.** `shell-lane=portable` with `shell-scope=all` must give five
   portable shards. `shell-lane=both` with `shell-scope=workflow` must give
   one shard per lane. Pinned by Task 2's two dispatches.
2. **The portable zip doesn't match its `.sha256`, or holds more than the
   exe.** The verify step must fail before any scenario runs. Pinned by the
   verify step's checks (Task 2), and by `package_portable_release.ps1`'s
   own single-exe refusal, which its tests already cover.
3. **A portable shard whose run never writes `portable-run.json`.** The
   evidence must still show a ✗ "No installer report" line, with the
   portable lane's report name. Pinned by Task 1's portable case, using
   the existing missing-report logic.
4. **The MSIX lane changing by accident.** Its shards must run exactly the
   steps they ran before, apart from the renamed jobs and artifacts.
   Pinned by Task 2's check of the MSIX shards in the dispatch, and by the
   full PR run (Task 4).
5. **A portable data folder left from a failed shard.** The installer
   refuses to start if `%LOCALAPPDATA%\DesktopGuides` exists. Each shard
   runs on a fresh runner, so this matters on the host only, and the
   existing check stays the guard.

## Host commands

Tests run on `pcsx2-win` when it's reachable:

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t17-2c) { Remove-Item -Recurse -Force E:\work\desktop-guides\t17-2c }; New-Item -ItemType Directory E:\work\desktop-guides\t17-2c | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t17-2c'
}
```

- **Script tests:** `stage && s 'cd /d E:\work\desktop-guides\t17-2c && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\<test>.ps1'`
- **When the host is unreachable:** use a `dev-fast` dispatch. Its
  `core-tests` job runs every script test under `pwsh`.
- **YAML load:** `ruby -C /Users/ilya.lissoboi/work/desktop-guides -ryaml -e 'f = ".github/workflows/windows-ci.yml"; begin; YAML.load_file(f, aliases: true); rescue ArgumentError; YAML.load_file(f); end'`
- **ASCII check:** `grep -nP '[^\x00-\x7F]' <file>`, with no output expected.

---

### Task 1: The evidence writer reports the portable hashes

**Files:**
- Modify: `tools/p1/write_shell_evidence.ps1` (the `param` block; the
  `### Package` section, lines 142–150; the `evidence.json` object)
- Modify: `tools/p1/test_write_shell_evidence.ps1` (the portable case,
  lines 102–107)

**Interfaces:**
- Consumes: `portable-run.json`'s existing `mode = 'portable'` and
  `portableExecutableSha256`.
- Produces:
  - `write_shell_evidence.ps1 -PortableZipSha256 <hash>`, optional,
    reported only;
  - Package lines `Portable exe SHA-256 `<hash>`` and
    `Portable zip SHA-256 `<hash>`` for portable reports;
  - `portableZipSha256` in `evidence.json`.

- [ ] **Step 1: Write the failing test**

Replace the portable case (lines 102–107) with:

```powershell
    # The portable exe carries its own Windows App Runtime, and the run
    # names both the exe and the zip it came from.
    $portable = [ordered]@{
        mode = 'portable'; windowsAppRuntime = @(); portableDataStillPresent = $false
        applicationEventCount = 0; portableExecutableSha256 = 'EEE'
    }
    $portablePath = Join-Path $root 'portable-run.json'
    $portable | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $portablePath -Encoding UTF8
    $text = Get-Content -LiteralPath (& $writer -ReportPath $portablePath -FixtureRoot $fixtures `
        -OutDirectory (Join-Path $root 'out4') -PortableZipSha256 'FFF') -Raw -Encoding UTF8
    Assert-True ($text.Contains("- $pass Windows App Runtime bundled in the portable exe")) 'The portable runtime line was wrong.'
    Assert-True ($text.Contains("- $pass Portable exe SHA-256 ``EEE``")) 'The portable exe hash is missing.'
    Assert-True ($text.Contains("- $pass Portable zip SHA-256 ``FFF``")) 'The portable zip hash is missing.'
    Assert-True (-not $text.Contains('MSIX')) 'A portable run listed MSIX lines.'
    $json = Get-Content -LiteralPath (Join-Path $root 'out4\evidence.json') -Raw -Encoding UTF8
    Assert-True ($json -match '"portableZipSha256":\s*"FFF"') 'evidence.json lacks the zip hash.'

    # Review Focus 3: a portable shard with no report still names it.
    $text = Get-Content -LiteralPath (& $writer -ReportPath (Join-Path $root 'absent\portable-run.json') `
        -FixtureRoot $fixtures -OutDirectory (Join-Path $root 'out5') -PortableZipSha256 'FFF') -Raw -Encoding UTF8
    Assert-True ($text.Contains("- $fail No installer report (portable-run.json)")) 'A missing portable report was not named.'
```

- [ ] **Step 2: Run it to verify it fails**

Run: **Script tests** with `test_write_shell_evidence.ps1`.
Expected: FAIL. `-PortableZipSha256` isn't a parameter yet, so the writer
call throws a parameter-binding error.

- [ ] **Step 3: Implement**

Add the parameter after `$Shard`:

```powershell
    [string] $Shard = '',
    # The portable zip's SHA-256 as the CI verify step checked it; reported only.
    [string] $PortableZipSha256 = ''
)
```

Give the `### Package` section a portable branch:

```powershell
    if ((Field $report 'mode') -ne 'portable') {
        $lines.Add((Line ([bool] (Field $report 'packageFullName')) "Package ``$(Field $report 'packageFullName')``"))
        $lines.Add((Line ([bool] (Field $report 'sourcePackageSha256')) "Source MSIX SHA-256 ``$(Field $report 'sourcePackageSha256')``"))
        $lines.Add((Line ([bool] (Field $report 'signedPackageSha256')) "Signed MSIX SHA-256 ``$(Field $report 'signedPackageSha256')``"))
        $lines.Add((Line ([bool] (Field $report 'certificateThumbprint')) "Test certificate ``$(Field $report 'certificateThumbprint')``"))
    }
    else {
        $lines.Add((Line ([bool] (Field $report 'portableExecutableSha256')) "Portable exe SHA-256 ``$(Field $report 'portableExecutableSha256')``"))
        $lines.Add((Line ([bool] $PortableZipSha256) "Portable zip SHA-256 ``$PortableZipSha256``"))
    }
```

Add `portableZipSha256` to the `evidence.json` object (line 198):

```powershell
    shard = $Shard; commit = $Commit; runId = $RunId; reportFound = [bool] $report
    portableZipSha256 = $PortableZipSha256
```

- [ ] **Step 4: Run it to verify it passes**

Run: **Script tests** with `test_write_shell_evidence.ps1`, plus the
**ASCII check** on both files.
Expected: `write_shell_evidence: all cases passed.`, and no non-ASCII
lines.

- [ ] **Step 5: Commit**

```bash
git add tools/p1/write_shell_evidence.ps1 tools/p1/test_write_shell_evidence.ps1
git commit -m "feat(p1): report the portable exe and zip hashes in the shell evidence"
```

### Task 2: Build the portable zip and add the lane dimension

**Files:**
- Modify: `tools/p1/test_release_ci_gate.ps1` (the CI-file checks, after
  the workflow-shard check)
- Modify: `.github/workflows/windows-ci.yml`:
  - the `workflow_dispatch` inputs (lines 11–51);
  - a new `portable-packages` job after `production-packages` (line 383);
  - `production-shell-ui` (lines 462–574);
  - `dev-production-shell-ui` (lines 585–590);
  - a new `dev-portable-packages` job after `dev-production-packages`.

**Interfaces:**
- Consumes: `write_shell_evidence.ps1 -PortableZipSha256` (Task 1);
  `start_installed_task.ps1 -PortableExecutable` (#95).
- Produces:
  - the artifact `desktop-guides-portable-x64`, holding
    `DesktopGuides-portable-x64-<short sha>.zip`, `.zip.sha256` and `.json`;
  - shard jobs `production-shell-ui (<lane>, <shard>)`, with artifacts
    `production-shell-ui-<lane>-<shard>`;
  - the dispatch input `shell-lane`.

- [ ] **Step 1: Write the failing gate check**

In `test_release_ci_gate.ps1`, after the
`'The workflow shard is missing from the push and pull_request matrix.'`
check:

```powershell
    # T17.2 C: push and PR runs test the portable exe too.
    Assert-True ($ci.Contains("|| '[""msix"",""portable""]'")) `
        'The portable lane is missing from the push and pull_request matrix.'
```

- [ ] **Step 2: Run it to verify it fails**

Run: **Script tests** with `test_release_ci_gate.ps1`.
Expected: FAIL with "The portable lane is missing from the push and
pull_request matrix."

- [ ] **Step 3: Add the dispatch input**

After the `shell-scope` input:

```yaml
      shell-lane:
        description: Installed production-shell lane to run (push and pull_request run both)
        type: choice
        default: both
        options:
          - both
          - msix
          - portable
```

- [ ] **Step 4: Add the portable build jobs**

After the `production-packages` job:

```yaml
  # T17.2 C: the portable single-file exe, built and zipped as release.yml
  # does but with the default Preview lane, whose event names the installed
  # UI tests use.
  portable-packages:
    needs: core-tests
    if: ${{ !(inputs.dev-fast && !inputs.verify-test-gate) }}
    runs-on: windows-2025
    steps: &portable-packages-steps
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4.3.1
        with:
          dotnet-version: 10.0.401
      - name: Cache NuGet packages
        uses: actions/cache@0057852bfaa89a56745cba8c7296529d2fc39830 # v4.3.0
        with:
          path: ~/.nuget/packages
          key: nuget-${{ runner.os }}-${{ runner.arch }}-portable-x64-${{ hashFiles('**/packages.lock.json') }}
          restore-keys: nuget-${{ runner.os }}-${{ runner.arch }}-portable-x64-
      - name: Restore locked portable dependencies
        run: dotnet restore src/DesktopGuides.Production/DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 -p:Portable=true
      - name: Build and zip the portable exe
        shell: pwsh
        run: |
          $publish = "$PWD/artifacts/portable-ci-publish"
          dotnet publish src/DesktopGuides.Production/DesktopGuides.Production.csproj `
            -c Release --no-restore -p:Platform=x64 -p:Portable=true `
            -p:PublishDir=$publish/
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          ./tools/p1/package_portable_release.ps1 -Commit $env:GITHUB_SHA `
            -PublishDirectory $publish -OutputDirectory artifacts/portable-ci
      - name: Upload the portable zip
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4.6.2
        with:
          name: desktop-guides-portable-x64
          path: artifacts/portable-ci/*
          if-no-files-found: error
```

After the `dev-production-packages` job:

```yaml
  dev-portable-packages:
    if: ${{ inputs.dev-fast && !inputs.verify-test-gate }}
    runs-on: windows-2025
    steps: *portable-packages-steps
```

- [ ] **Step 5: Add the lane dimension to the shard job**

Replace the head of `production-shell-ui`, through its `include:` line, with:

```yaml
  production-shell-ui:
    name: production-shell-ui (${{ matrix.lane }}, ${{ matrix.target.shard }})
    needs: [production-packages, portable-packages]
    runs-on: windows-2025
    strategy: &production-shell-ui-strategy
      fail-fast: false
      matrix:
        # T17.2 C: every shard runs against the signed MSIX and the portable
        # exe. A dispatch can narrow to one lane with shell-lane.
        lane: ${{ fromJSON(github.event_name == 'workflow_dispatch' && inputs.shell-lane != 'both' && format('["{0}"]', inputs.shell-lane) || '["msix","portable"]') }}
        # Scenario groups are independent, so they shard across runners of
        # about six minutes each; `workflow` runs the whole journey on its own.
        # A dispatch with one shell-scope runs only that group.
        target: ${{ fromJSON(github.event_name == 'workflow_dispatch' && inputs.shell-scope != 'all' && format('[{{"shard":"{0}","groups":"{0}"}}]', inputs.shell-scope) || '[{"shard":"core","groups":"core,txt,game-actions"},{"shard":"design","groups":"design,catalog,completion,backup"},{"shard":"pdf","groups":"pdf,progress,theme,text-size"},{"shard":"html","groups":"html,import,provider"},{"shard":"workflow","groups":"workflow"}]') }}
```

Replace the steps from the MSIX download through the verify step with:

```yaml
    steps: &production-shell-ui-steps
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4.3.1
        with:
          dotnet-version: 10.0.401
      - uses: actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4.3.0
        if: matrix.lane == 'msix'
        with:
          name: desktop-guides-production-x64
          path: artifacts/production-shell/download
      - uses: actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4.3.0
        if: matrix.lane == 'portable'
        with:
          name: desktop-guides-portable-x64
          path: artifacts/production-shell/portable-download
      - name: Verify interactive desktop
        shell: pwsh
        run: |
          $session = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
          $explorer = @(Get-Process explorer -ErrorAction SilentlyContinue |
            Where-Object { $_.SessionId -eq $session })
          if ($explorer.Count -eq 0) { throw 'No interactive desktop in the runner session.' }
          $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
          $principal = [Security.Principal.WindowsPrincipal]::new($identity)
          if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Installed runs need an elevated runner account.'
          }
      - name: Verify the MSIX
        if: matrix.lane == 'msix'
        shell: pwsh
        run: |
          $packages = @(Get-ChildItem artifacts/production-shell/download `
            -Filter 'DesktopGuides.Production_*_x64.msix')
          if ($packages.Count -ne 1) { throw 'Expected one x64 production package.' }
          $manifest = Get-Content artifacts/production-shell/download/manifest.json -Raw |
            ConvertFrom-Json
          if ($manifest.architecture -ne 'x64' -or
              $manifest.identityName -ne 'DesktopGuides.Preview' -or
              $manifest.publisher -ne 'CN=DesktopGuides Development' -or
              $manifest.packageVersion -ne '0.1.0.0' -or
              $manifest.windowsAppRuntimeMinimum -ne '2.5.1.0' -or
              $manifest.package -ne $packages[0].Name -or
              $manifest.packageSha256 -ne
                (Get-FileHash $packages[0].FullName -Algorithm SHA256).Hash) {
            throw 'Production package does not match its build manifest.'
          }
      - name: Verify and unzip the portable exe
        if: matrix.lane == 'portable'
        shell: pwsh
        run: |
          $download = 'artifacts/production-shell/portable-download'
          $zips = @(Get-ChildItem $download -Filter 'DesktopGuides-portable-x64-*.zip')
          if ($zips.Count -ne 1) { throw "Expected one portable zip, found $($zips.Count)." }
          $expected = ((Get-Content -LiteralPath "$($zips[0].FullName).sha256" -Raw).Trim() -split '\s+')[0]
          $actual = (Get-FileHash -LiteralPath $zips[0].FullName -Algorithm SHA256).Hash
          if ($actual -ne $expected) { throw "The portable zip's SHA-256 is $actual, not $expected." }
          $unzipped = Join-Path $PWD 'artifacts/production-shell/portable'
          Expand-Archive -LiteralPath $zips[0].FullName -DestinationPath $unzipped
          $files = @(Get-ChildItem -LiteralPath $unzipped -Recurse -File)
          if ($files.Count -ne 1 -or $files[0].Name -ne 'DesktopGuides.Production.exe') {
            throw "Expected only DesktopGuides.Production.exe in the zip; found $($files.Name -join ', ')."
          }
          "PORTABLE_EXE=$($files[0].FullName)" | Add-Content -LiteralPath $env:GITHUB_ENV
          "PORTABLE_ZIP_SHA256=$actual" | Add-Content -LiteralPath $env:GITHUB_ENV
```

The "Verify the MSIX" script is the existing verify step's package checks,
unchanged. Only the desktop and admin checks moved to "Verify interactive
desktop".

In "Sign, install, and test production shell", rename it to "Install or
unpack, and test production shell". Change `SHELL_GROUPS` to
`${{ matrix.target.groups }}`, add `LANE: ${{ matrix.lane }}` to its
`env:`, and replace the `$package = ...` lines and the entry-point call
with:

```yaml
          $target = if ($env:LANE -eq 'portable') {
            @{ PortableExecutable = $env:PORTABLE_EXE }
          } else {
            @{ PackagePath = (Get-ChildItem artifacts/production-shell/download `
              -Filter 'DesktopGuides.Production_*_x64.msix' | Select-Object -First 1).FullName }
          }
          $filter = @{}
          if ($env:SHELL_PASS) {
            $filter.PassFilter = @($env:SHELL_PASS -split ',' | ForEach-Object { $_.Trim() })
          }
          Write-Output "Lane: $env:LANE; scenario groups: $env:SHELL_GROUPS; passes: $(if ($env:SHELL_PASS) { $env:SHELL_PASS } else { 'all' })"
          & ./tools/p1/start_installed_task.ps1 @target `
            -ResultDirectory artifacts/production-shell/installed `
            -Groups @($env:SHELL_GROUPS -split ',') `
            -TimeoutMinutes 45 `
            @filter
          exit $LASTEXITCODE
```

In "Write installed shell evidence", set `SHARD: ${{ matrix.target.shard }}`
and add `LANE: ${{ matrix.lane }}` to its `env:`. Then replace its writer
call with:

```yaml
          $installed = 'artifacts/production-shell/installed'
          $report = if ($env:LANE -eq 'portable') { 'portable-run.json' } else { 'signed-install.json' }
          $markdown = & ./tools/p1/write_shell_evidence.ps1 `
            -ReportPath (Join-Path $installed $report) `
            -FixtureRoot tests/fixtures -OutDirectory $installed `
            -Commit $env:SOURCE_COMMIT -RunId $env:GITHUB_RUN_ID `
            -Shard "$env:LANE $env:SHARD" -PortableZipSha256 "$env:PORTABLE_ZIP_SHA256"
          Get-Content -LiteralPath $markdown -Raw | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
```

In "Upload installed shell evidence", set
`name: production-shell-ui-${{ matrix.lane }}-${{ matrix.target.shard }}`.

Change `dev-production-shell-ui`'s head to:

```yaml
  dev-production-shell-ui:
    name: dev-production-shell-ui (${{ matrix.lane }}, ${{ matrix.target.shard }})
    needs: [dev-production-packages, dev-portable-packages]
```

- [ ] **Step 6: Check locally**

Run:
- **Script tests** with `test_release_ci_gate.ps1`;
- the **YAML load**;
- the **ASCII check** on both files;
- `grep -n "matrix.shard\|matrix.groups" .github/workflows/windows-ci.yml`.

Expected: `release_ci_gate: all cases passed.`, the YAML loads, no
non-ASCII lines, and no `matrix.shard` or `matrix.groups` left.

- [ ] **Step 7: Commit, push and dispatch the portable workflow shard**

```bash
git add tools/p1/test_release_ci_gate.ps1 .github/workflows/windows-ci.yml
git commit -m "ci(p1): build the portable zip and run every installed shard against it"
git push -u origin feat/p1-t17-2-portable-ci
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-2-portable-ci -f shell-lane=portable -f shell-scope=workflow -f dev-fast=true
```

Expected:
- `dev-portable-packages` passes.
- One shard, `dev-production-shell-ui (portable, workflow)`, runs.
- Its evidence says "Installed shell evidence: portable workflow", with
  "Mode `portable`", both portable hashes and "Portable data folder
  removed".
- No MSIX shard runs.

Then dispatch `shell-lane=both` with `shell-scope=workflow`. Expected: one
MSIX and one portable shard. The MSIX shard's steps and evidence match the
`workflow` shard on `main`.

### Task 3: Run every group in portable mode and fix what breaks

**Files:**
- Modify, as failures require: `tools/p1/windows_shell_install.ps1` and
  `tools/p1/windows_shell_ui_smoke.ps1`.
- Create issues for app defects. Don't work around them in the harness.

**Interfaces:**
- Consumes: Task 2's lanes and dispatch input.
- Produces: a portable dispatch with `shell-scope=all` in which all five
  shards pass.

This task is the red-green of the portable lane itself. Most groups have
never run portable, and their failures can't be predicted from the spec,
so the steps define how each failure is handled instead of naming the
fixes.

- [ ] **Step 1: Dispatch every portable shard**

```bash
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-2-portable-ci -f shell-lane=portable -f shell-scope=all -f dev-fast=true
```

Expected: five `dev-production-shell-ui (portable, <shard>)` jobs. Some may
fail; record each failing scenario and its error in the ledger.

- [ ] **Step 2: Classify each failure**

For each failing scenario, use superpowers:systematic-debugging and read
its result JSON, screenshots and `installer.log`. Then decide which of
these it is:
- **A harness assumption about the package:** for example a package-only
  path, `Get-AppxPackage`, the package's `LocalState` folder, or the
  package family name. Fix the harness so the scenario checks the same
  user-visible outcome on both lanes, as `Run-BackupScenarios`'
  protected-folder branch already does with `if ($portable)`. Ledger a
  ruling per fix.
- **A check that can't apply to portable:** for example package identity
  or uninstall. Skip it in portable mode with an explicit `if ($portable)`
  branch, and record the skip in the report as
  `$report.portableSkipped += '<scenario>: <reason>'`. Ledger a ruling.
- **An app defect in the portable build:** file an issue with the
  evidence. Leave the scenario failing, and stop to ask the user how to
  proceed with this PR.

- [ ] **Step 3: Fix, check and redispatch**

After each fix:
- run the **ASCII check**, and `test_windows_scripts_parse.ps1` on the
  host or in `core-tests`;
- commit, with a message naming the scenario and the cause;
- dispatch the failing shard again with `shell-lane=portable` and its
  `shell-scope`.

Expected: the shard passes. Then dispatch `shell-lane=msix` for the same
scope. Expected: the MSIX shard still passes, which pins Review Focus 4.

- [ ] **Step 4: Confirm the whole portable lane**

Run Step 1's dispatch again.
Expected: all five portable shards pass. Each evidence summary shows both
portable hashes and "Portable data folder removed". Record the run ID in
the ledger.

### Task 4: Docs, the full PR run and the PR

**Files:**
- Modify: `docs/p1/e2e-testing.md` ("Gates of record" and "Portable
  build")
- Modify: `docs/p1/t17-2-portable-ci-design.md` (Status; "Rulings";
  "T17.2 piece C verification record")
- Modify: `docs/p1/implementation-plan.md` (the T17.2 note) and
  `docs/progress.md` (the T17.2 row and "Open:")

**Interfaces:**
- Consumes: Tasks 1–3, and their runs.
- Produces: the PR, with a full run in which all 10 shards pass.

- [ ] **Step 1: Update `e2e-testing.md`**

In "Gates of record":
- Change "It runs as five parallel shards" to "It runs five shards in
  each of two lanes".
- After the shard list, add:

```markdown
The `msix` lane installs the signed MSIX. The `portable` lane runs the
same shards against the portable single-file exe. `portable-packages`
builds it with the default Preview lane, whose event names the installed
UI tests use, and zips it with `package_portable_release.ps1`. Each
portable shard checks the zip against its `.sha256`, unzips it, and
requires the exe to be the only file. Shards are named
`production-shell-ui (<lane>, <shard>)`, and their artifacts
`production-shell-ui-<lane>-<shard>`. A dispatch's `shell-lane` input
(`both`, `msix` or `portable`) narrows it to one lane.
```

- Change "uploads its JSON results and screenshots as
  `production-shell-ui-<shard>`" to
  "`production-shell-ui-<lane>-<shard>`".

In "Portable build", after "the portable build's own release gates come
later under T17.3.", add:

```markdown
CI runs every scenario group against a Preview-lane portable zip on every
PR and push (T17.2 piece C). The released zip is built for the Public
lane, whose window title, instance key and test-event prefix differ; those
names aren't covered in CI.
```

- [ ] **Step 2: Update the design and status pages**

In the design:
- Set Status to "approved; implemented on `feat/p1-t17-2-portable-ci`".
- Add a "Rulings" section holding every Task 3 ruling from the ledger.
- Add a "T17.2 piece C verification record" section holding the Task 2
  and Task 3 dispatch IDs, and a line for the PR run.

In `implementation-plan.md`, append to the T17.2 note:

```markdown
Piece C runs every installed shard against the portable exe as well; see
the [piece C design](t17-2-portable-ci-design.md).
```

In `docs/progress.md`, change the T17.2 wording in "Open:" from "the
portable CI job and the fake-provider lane remain" to "the fake-provider
lane remains".

- [ ] **Step 3: Commit, push and open the PR**

```bash
git add docs/p1/e2e-testing.md docs/p1/t17-2-portable-ci-design.md docs/p1/t17-2-portable-ci-plan.md docs/p1/implementation-plan.md docs/progress.md
git commit -m "docs(p1): record the T17.2 portable CI job"
git push
```

Open the PR against `main`, titled "T17.2 piece C: run every installed
shard against the portable exe". The body names:
- the task: T17.2, piece C;
- the prerequisites and their status;
- the outcome: 10 shards, in two lanes;
- the dispatch runs;
- every Task 3 ruling;
- any app issue filed.

There are no UI changes, so no screenshot.

- [ ] **Step 4: Wait for the PR run**

Expected: every job passes, including all 10 `production-shell-ui` shards
and `portable-packages`. Record the run ID in the verification record in a
follow-up commit. Then stop for the user's merge decision.

## Traceability

| Spec item | Task |
| --- | --- |
| Building the zip: `portable-packages`, `dev-portable-packages` | 2 |
| The shard matrix: `lane` × `target`, names, `shell-lane` | 2 |
| What each lane does differently: download, verify, run, evidence, upload | 2 |
| Evidence: portable exe and zip hashes | 1 |
| Groups that have never run portable | 3 |
| Release gate: the CI-file check | 2 |
| Tests | 1, 2 |
| Docs | 4 |
| Delivery and verification | 2, 3, 4 |
