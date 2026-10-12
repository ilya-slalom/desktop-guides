# T17.2 Release CI (Evidence and Workflow Lane) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- Every installed CI shard publishes a readable evidence checklist.
- A new `workflow` shard runs the whole user journey on the installed,
  signed package, and blocks release promotion when it fails.

**Architecture:**
- `tools/p1/write_shell_evidence.ps1` turns an installer report into
  `evidence.md` and `evidence.json`. CI runs it after every shard and
  appends the markdown to the step summary.
- `Run-ShellSmoke` records each call's timing in the report.
- The workflow lane is five new smoke modes inside the smoke's shared
  reader block, so they use its position helpers. A `workflow` installer
  group drives them, passing recorded places between stages in a small
  state file.

**Tech Stack:** PowerShell 5.1 and 7 (the scripts must run in both), UI
Automation, GitHub Actions, .NET 10 (unchanged app code).

**Spec:** `docs/p1/t17-2-release-ci-design.md`

**Target:** T17.2, pieces A and B, in two PRs.
**Prerequisites:** T04.3, T06.4, T07.3, T08.3, T09.3, T10.3, T12.3, T13.1,
T14.3, T15.4, T16.1 and T20.2, all merged.

| PR | Branch | Tasks |
|---|---|---|
| a: evidence checklist | `feat/p1-t17-2-release-ci` | 1–3 |
| b: workflow lane | `feat/p1-t17-2-workflow-lane` (from `main` after PR a merges) | 4–8 |

## Global Constraints

- PowerShell scripts stay ASCII-only. Each runs under Windows PowerShell
  5.1 (pcsx2-win) and PowerShell 7 (CI).
- The evidence never contains a credential, a provider key, library
  content, a machine name or a user name. Paths are rewritten to
  `%USERPROFILE%`, `%LOCALAPPDATA%` and `%TEMP%` forms.
- The writer never fails a run on its own: a missing report or file
  becomes a ✗ line. The writer itself is ASCII; it builds ✓ and ✗ from
  code points.
- The workflow lane uses the keyboard and visible controls for every step,
  never seeds.
- UI tests assert only what app code controls.
- No app code changes.
- Never print, copy or log provider credential values.

## Rulings against the spec

Task 3 records rulings 1–3 and Task 8 rulings 4–9 in the design.

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
   Task 2 adds `$report.webView2Version`, read from the EdgeUpdate client
   key that WebView2 registers.
3. **Scratch folders.** The existing groups leave no scratch folders, so
   the evidence prints the "scratch folders removed" line only when the
   report has `scratchFoldersRemaining` (workflow lane).
4. **The workflow modes live in the shared reader block** (the
   `elseif ($Mode -in @('txt-reader', 'keyboard-txt', ...))` block), as
   `keyboard-txt` does. That gives them `Get-TopRow`, `Wait-PdfPage`,
   `Get-PageTopLine`, `Wait-TopMark`, `Open-GuideFromGame` and
   `Invoke-ReaderCommand` without moving code.
5. **Imports use the shared `Choose-OpenPath`** with absolute paths. The
   `import-*` branch's `Choose-PickerFile` is limited to `tests/fixtures/p0`,
   and the lane picks from a scratch folder.
6. **State between stages.** Each stage runs in its own smoke process. The
   places recorded in `workflow-read` reach `workflow-resume` and
   `workflow-restore` through a JSON state file the installer passes as
   `-WorkflowStatePath`.
7. **Restoring into an empty library.** The lane removes the game before
   restoring, so it restores into an empty library. `restore-clean` and
   `restore-replace` always start from a seeded library and always confirm
   the Replace dialog, so no existing mode shows whether an empty library
   asks. The mode waits up to 5 s for `ReplaceLibraryDialog` and confirms
   it only if it appears.
8. **The TXT fixture is `p1/txt-numbered.txt`, not `p0/generated/txt-long.txt`.**
   Every row of `txt-long` reads `Area 001  |  Proceed north...`, so the
   place a resumed guide opens at can't be told apart by row.
   `txt-numbered` names each row `Line NNNN`, which `Get-TopLine` reads.
   The design's fixture list changes to match (Task 8).
9. **The HTML place is `MARK-0420`, through the page's own "Jump to
   MARK-0420" link,** not "about halfway". `html-long` has no other link,
   and T16.1 found that keys sent to the page don't reach it reliably. The
   fragment link is a visible control, and `Wait-TopMark` already checks
   that place in the progress smoke.

## Review Focus

1. **A run that failed before writing its report.** The evidence must
   still be published, with a ✗ line naming the missing report. Pinned by
   `test_write_shell_evidence.ps1`'s missing-report case (Task 1), and by
   the CI step's `if: always()` (Task 2).
2. **A path that hides a user name inside a longer string**, for example an
   error message quoting `C:\Users\runneradmin\AppData\Local\Temp\x`. It
   must be rewritten everywhere, not only in known fields. Pinned by the
   anonymisation case on a nested error string (Task 1).
3. **An import that stops for an answer.** The lane's TXT is plain ASCII
   and its PDF is unlocked, so the dialog shouldn't ask for an encoding or
   a password. The dialog offers no UTF-8 choice. So if it asks,
   `workflow-build` must fail at once, naming what it asked, rather than
   time out on a disabled Import. Pinned by the checks in
   `Import-WorkflowGuide` (Task 4).
4. **Saves not flushed before the restart.** `workflow-read` ends by going
   back to the Game page, and the installer closes the app gracefully, so
   the progress coordinator writes its saves. Pinned by `workflow-resume`
   reading each place after a fresh launch (Task 5).
5. **A restored library opening at the wrong places.** `workflow-restore`
   re-reads each guide's place from the state file after the swap (Task 6).

## Host commands

The Mac has no PowerShell or dotnet, so tests run on `pcsx2-win`:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t17-2) { Remove-Item -Recurse -Force E:\work\desktop-guides\t17-2 }; New-Item -ItemType Directory E:\work\desktop-guides\t17-2 | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t17-2'
}
```

- **Writer tests:** `stage && s 'cd /d E:\work\desktop-guides\t17-2 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_write_shell_evidence.ps1'`
- **Gate tests:** the same with `tools\p1\test_release_ci_gate.ps1`.
- **Parse check:** `[System.Management.Automation.Language.Parser]::ParseFile` on each changed `.ps1`, with 0 errors expected.
- **ASCII check:** `grep -nP '[^\x00-\x7F]' <file>` on the Mac, with no output expected.
- **Installed shards:** CI, the gate of record. `gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref <branch> -f shell-scope=<group> -f dev-fast=true`

---
# PR a: the evidence checklist

### Task 1: The evidence writer

**Files:**
- Create: `tools/p1/write_shell_evidence.ps1`
- Create: `tools/p1/test_write_shell_evidence.ps1`

**Interfaces:**
- Consumes: the installer report (`signed-install.json` or
  `portable-run.json`), including the fields Task 2 adds:
  - `smokeRuns`: entries of `name`, `mode`, `startedAt`, `seconds`,
    `passed`, `error`;
  - `webView2Version` and `finishedAt`;
  - the existing `applicationEventCount` and `applicationEvents`
    (`observedAt`, `provider`, `eventId`, `message`);
  - optionally `fixtures`, repo-relative under `tests/fixtures`, such as
    `p0/generated/txt-long.txt`;
  - optionally `scratchFoldersRemaining`.
- Produces:
  - `write_shell_evidence.ps1 -ReportPath <json> -FixtureRoot <tests/fixtures> -OutDirectory <dir> [-Commit <sha>] [-RunId <id>] [-Shard <name>]`;
  - `evidence.md` and `evidence.json` in `-OutDirectory`;
  - it returns the markdown path.

- [ ] **Step 1: Write the failing test**

```powershell
# tools/p1/test_write_shell_evidence.ps1
$ErrorActionPreference = 'Stop'
$writer = Join-Path $PSScriptRoot 'write_shell_evidence.ps1'
$pass = [string][char]0x2713
$fail = [string][char]0x2717

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Get-FolderHash([string] $root) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $names = [string[]] @(Get-ChildItem -LiteralPath $root -Recurse -File |
        ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') })
    [Array]::Sort($names, [StringComparer]::Ordinal)
    $text = ($names | ForEach-Object {
        "${_}:" + (Get-FileHash -LiteralPath (Join-Path $root $_) -Algorithm SHA256).Hash.ToLowerInvariant()
    }) -join "`n"
    return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))) -replace '-', '').ToLowerInvariant()
}

$root = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-evidence-$([Guid]::NewGuid().ToString('N'))"
try {
    $fixtures = Join-Path $root 'fixtures'
    New-Item -ItemType Directory -Force (Join-Path $fixtures 'p0') | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $fixtures 'p1\html-long\images') | Out-Null
    Set-Content -LiteralPath (Join-Path $fixtures 'p0\txt-a.txt') -Value 'a' -NoNewline -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $fixtures 'p1\html-long\guide.html') -Value '<p>x</p>' -NoNewline -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $fixtures 'p1\html-long\images\map.png') -Value 'png' -NoNewline -Encoding ASCII
    @{ schemaVersion = 1; fixtures = @(@{ id = 'txt-a'; path = 'txt-a.txt'; sha256 = 'ignored' }) } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fixtures 'p0\manifest.json') -Encoding UTF8

    $report = [ordered]@{
        runId = 'r1'; observedAt = '2026-10-12T00:00:00Z'; osBuild = '10.0.26100.0'
        cpuArchitecture = 'AMD64'; mode = 'signed-msix'; scenarioScope = 'design'
        scenarioGroupsRun = @('design'); success = $true; dotnetSdk = '10.0.401'
        windowsAppRuntime = @('Microsoft.WindowsAppRuntime.2 2.5.1.0'); webView2Version = '141.0.1'
        sourcePackageSha256 = 'AAA'; signedPackageSha256 = 'BBB'; certificateThumbprint = 'CCC'
        packageFamilyName = 'DesktopGuides.Preview_x'; packageFullName = 'DesktopGuides.Preview_0.1.0.0_x64__x'
        smokeRuns = @(
            [ordered]@{ name = 'design-system'; mode = 'design-language'; startedAt = '2026-10-12T00:00:01Z'; seconds = 12.5; passed = $true; error = $null },
            [ordered]@{ name = 'keyboard'; mode = 'keyboard'; startedAt = '2026-10-12T00:00:20Z'; seconds = 30.0; passed = $false
                error = "Expected focus. At C:\Users\someone\AppData\Local\Temp\x.ps1 and $($env:LOCALAPPDATA)\Packages\y" })
        packageStillInstalled = $false; certificateStillTrusted = $false
        processCleanupErrors = @(); finishedAt = '2026-10-12T00:05:00Z'
        applicationEventCount = 1
        applicationEvents = @([ordered]@{ observedAt = '2026-10-12T00:04:00Z'; provider = '.NET Runtime'; eventId = 1026
            message = "Application: DesktopGuides.Production.exe`nat C:\Users\someone\x" })
    }
    $reportPath = Join-Path $root 'signed-install.json'
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8

    # A full report: every section, the failed scenario, cleanup, all fixtures.
    $out = Join-Path $root 'out'
    $md = & $writer -ReportPath $reportPath -FixtureRoot $fixtures -OutDirectory $out -Commit 'abc123' -RunId '42' -Shard 'design'
    $text = Get-Content -LiteralPath $md -Raw -Encoding UTF8
    foreach ($heading in '### Run', '### Target', '### Package', '### Fixtures', '### Scenarios', '### Cleanup') {
        Assert-True ($text.Contains($heading)) "evidence.md lacks $heading."
    }
    Assert-True ($text.Contains("- $pass Commit ``abc123``")) 'The commit line is missing.'
    Assert-True ($text.Contains("- $pass Signed MSIX SHA-256 ``BBB``")) 'The signed hash line is missing.'
    Assert-True ($text.Contains("- $pass design-system (12.5 s)")) 'The passing scenario line is missing.'
    Assert-True ($text.Contains("- $fail keyboard (30 s)")) 'The failing scenario line is missing.'
    Assert-True ($text.Contains("- $pass Package removed")) 'The cleanup line is missing.'
    Assert-True ($text.Contains('Started 2026-10-12T00:00:00Z, finished 2026-10-12T00:05:00Z')) 'The run times are missing.'
    Assert-True ($text.Contains("- $fail Application event log entries: 1")) 'The event count is missing.'
    Assert-True ($text.Contains('  - 2026-10-12T00:04:00Z .NET Runtime 1026: Application: DesktopGuides.Production.exe')) 'The event was not listed.'
    Assert-True ($text.Contains("``tests/fixtures/p0/txt-a.txt`` (txt-a)")) 'The manifest fixture lacks its ID.'
    $folderHash = Get-FolderHash (Join-Path $fixtures 'p1\html-long')
    Assert-True ($text.Contains("``tests/fixtures/p1/html-long`` ``$folderHash``")) 'The folder fixture hash is wrong.'

    # Anonymisation reaches nested strings.
    $json = Get-Content -LiteralPath (Join-Path $out 'evidence.json') -Raw -Encoding UTF8
    Assert-True (-not $json.Contains('someone')) 'A user name leaked into evidence.json.'
    # JSON doubles each backslash.
    Assert-True (-not $json.Contains($env:LOCALAPPDATA.Replace('\', '\\'))) 'LOCALAPPDATA leaked into evidence.json.'
    Assert-True ($json.Contains('"seconds":  12.5') -or $json.Contains('"seconds": 12.5')) 'A number did not survive anonymisation.'
    Assert-True ($json.Contains('%USERPROFILE%') -and $json.Contains('%LOCALAPPDATA%')) 'Paths were not rewritten.'
    Assert-True (-not $text.Contains('someone')) 'A user name leaked into evidence.md.'

    # A named fixture that is missing is a failed line, not skipped.
    $report.fixtures = @('p1/html-long', 'p0/missing.txt')
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    $text = Get-Content -LiteralPath (& $writer -ReportPath $reportPath -FixtureRoot $fixtures -OutDirectory $out) -Raw -Encoding UTF8
    Assert-True ($text.Contains("- $fail ``tests/fixtures/p0/missing.txt`` not found")) 'A missing fixture was not reported.'
    Assert-True (-not $text.Contains('txt-a.txt')) 'The recorded fixture list was not preferred.'

    # No report at all still writes evidence.
    $none = Join-Path $root 'none.json'
    $text = Get-Content -LiteralPath (& $writer -ReportPath $none -FixtureRoot $fixtures -OutDirectory (Join-Path $root 'out2') -Shard 'pdf') -Raw -Encoding UTF8
    Assert-True ($text.Contains("- $fail No installer report (none.json)")) 'A missing report was not reported.'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'write_shell_evidence: all cases passed.'
```

- [ ] **Step 2: Run it to verify it fails**

Run: **Writer tests** (host commands).
Expected: FAIL, because `write_shell_evidence.ps1` doesn't exist.

- [ ] **Step 3: Write the writer**

```powershell
# tools/p1/write_shell_evidence.ps1
# T17.2: turns one installed-shell report into evidence.md and evidence.json.
# It never fails the run: missing data becomes a failed line.
param(
    [Parameter(Mandatory = $true)] [string] $ReportPath,
    [Parameter(Mandatory = $true)] [string] $FixtureRoot,
    [Parameter(Mandatory = $true)] [string] $OutDirectory,
    [string] $Commit = $env:GITHUB_SHA,
    [string] $RunId = $env:GITHUB_RUN_ID,
    [string] $Shard = ''
)
$ErrorActionPreference = 'Stop'
$pass = [string][char]0x2713
$fail = [string][char]0x2717

# Longest prefix first, so %TEMP% wins over %LOCALAPPDATA% and %USERPROFILE%.
function Hide-Paths([string] $text) {
    if ([string]::IsNullOrEmpty($text)) { return $text }
    $out = $text
    foreach ($pair in @(@($env:TEMP, '%TEMP%'), @($env:LOCALAPPDATA, '%LOCALAPPDATA%'), @($env:USERPROFILE, '%USERPROFILE%'))) {
        if ($pair[0]) { $out = [regex]::Replace($out, [regex]::Escape($pair[0]), $pair[1], 'IgnoreCase') }
    }
    # Any other profile, as in an error copied from another account.
    $out = [regex]::Replace($out, '(?i)[A-Z]:\\Users\\[^\\\s"'']+', '%USERPROFILE%')
    foreach ($pair in @(@($env:COMPUTERNAME, '%COMPUTERNAME%'), @($env:USERNAME, '%USERNAME%'))) {
        if ($pair[0] -and $pair[0].Length -ge 3) { $out = [regex]::Replace($out, "\b$([regex]::Escape($pair[0]))\b", $pair[1], 'IgnoreCase') }
    }
    return $out
}

# Every string inside a parsed JSON value, rewritten.
function Hide-Value($value) {
    if ($value -is [string]) { return Hide-Paths $value }
    if ($value -is [System.Management.Automation.PSCustomObject]) {
        $copy = [ordered]@{}
        foreach ($property in $value.PSObject.Properties) { $copy[$property.Name] = Hide-Value $property.Value }
        return $copy
    }
    # The leading comma keeps a one-item or empty array an array.
    if ($value -is [array]) { return , @($value | ForEach-Object { Hide-Value $_ }) }
    return $value
}

function Get-FixtureHash([string] $path) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    # A folder: one hash over "relative/path:sha256" lines in ordinal order.
    $root = (Resolve-Path -LiteralPath $path).Path.TrimEnd('\')
    $names = [string[]] @(Get-ChildItem -LiteralPath $root -Recurse -File |
        ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') })
    [Array]::Sort($names, [StringComparer]::Ordinal)
    $text = ($names | ForEach-Object {
        "${_}:" + (Get-FileHash -LiteralPath (Join-Path $root $_) -Algorithm SHA256).Hash.ToLowerInvariant()
    }) -join "`n"
    $sha = [System.Security.Cryptography.SHA256]::Create()
    return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))) -replace '-', '').ToLowerInvariant()
}

# Ruling 1: the recorded list when there is one, otherwise the whole set.
function Get-Fixtures($report) {
    $ids = @{}
    $manifest = Join-Path $FixtureRoot 'p0\manifest.json'
    if (Test-Path -LiteralPath $manifest) {
        foreach ($entry in (Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json).fixtures) { $ids["p0/$($entry.path)"] = $entry.id }
    }
    $paths = if ($report -and $report.PSObject.Properties['fixtures'] -and $report.fixtures) { @($report.fixtures) }
        else {
            @($ids.Keys) + @(Get-ChildItem -LiteralPath (Join-Path $FixtureRoot 'p1') -ErrorAction SilentlyContinue |
                ForEach-Object { "p1/$($_.Name)" })
        }
    $sorted = [string[]] @($paths)
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    foreach ($relative in $sorted) {
        $full = Join-Path $FixtureRoot ($relative -replace '/', '\')
        $found = Test-Path -LiteralPath $full
        [ordered]@{
            path = "tests/fixtures/$relative"; id = $ids[$relative]; found = $found
            sha256 = if ($found) { Get-FixtureHash $full } else { $null }
        }
    }
}

function Line([bool] $ok, [string] $text) { "- $(if ($ok) { $pass } else { $fail }) $text" }

function Field($report, [string] $name) {
    if ($report -and $report.PSObject.Properties[$name]) { return $report.$name } else { return $null }
}

New-Item -ItemType Directory -Force $OutDirectory | Out-Null
$reportName = Split-Path -Leaf $ReportPath
$report = $null
if (Test-Path -LiteralPath $ReportPath) {
    $report = Get-Content -LiteralPath $ReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("## Installed shell evidence$(if ($Shard) { ": $Shard" })")
$lines.Add('')
$lines.Add('### Run')
$lines.Add((Line ([bool] $Commit) "Commit ``$Commit``"))
$lines.Add((Line ([bool] $RunId) "Run ``$RunId``"))
if (-not $report) {
    $lines.Add((Line $false "No installer report ($reportName)"))
}
else {
    $lines.Add((Line $true "Groups ``$((@(Field $report 'scenarioGroupsRun')) -join ', ')`` (scope ``$(Field $report 'scenarioScope')``)"))
    $lines.Add((Line ([bool] (Field $report 'finishedAt')) "Started $(Field $report 'observedAt'), finished $(Field $report 'finishedAt')"))
    $lines.Add((Line ([bool] (Field $report 'success')) "Run result: $(if (Field $report 'success') { 'passed' } else { 'failed' })"))
    if (Field $report 'error') { $lines.Add((Line $false ("Error: " + (Hide-Paths ("$(Field $report 'error')" -split "`n")[0])))) }

    $lines.Add(''); $lines.Add('### Target')
    $lines.Add((Line ([bool] (Field $report 'osBuild')) "Windows build ``$(Field $report 'osBuild')``, CPU ``$(Field $report 'cpuArchitecture')``"))
    $lines.Add((Line ([bool] (Field $report 'dotnetSdk')) ".NET SDK ``$(Field $report 'dotnetSdk')``"))
    $lines.Add((Line ([bool] (Field $report 'windowsAppRuntime')) "Windows App Runtime ``$((@(Field $report 'windowsAppRuntime') | ForEach-Object { "$_" }) -join '; ')``"))
    $lines.Add((Line ([bool] (Field $report 'webView2Version')) "WebView2 Runtime ``$(Field $report 'webView2Version')``"))

    $lines.Add(''); $lines.Add('### Package')
    $lines.Add((Line $true "Mode ``$(Field $report 'mode')``"))
    if ((Field $report 'mode') -ne 'portable') {
        $lines.Add((Line ([bool] (Field $report 'packageFullName')) "Package ``$(Field $report 'packageFullName')``"))
        $lines.Add((Line ([bool] (Field $report 'sourcePackageSha256')) "Source MSIX SHA-256 ``$(Field $report 'sourcePackageSha256')``"))
        $lines.Add((Line ([bool] (Field $report 'signedPackageSha256')) "Signed MSIX SHA-256 ``$(Field $report 'signedPackageSha256')``"))
        $lines.Add((Line ([bool] (Field $report 'certificateThumbprint')) "Test certificate ``$(Field $report 'certificateThumbprint')``"))
    }
}

$fixtures = @(Get-Fixtures $report)
$lines.Add(''); $lines.Add('### Fixtures')
foreach ($fixture in $fixtures) {
    if ($fixture.found) {
        $id = if ($fixture.id) { " ($($fixture.id))" } else { '' }
        $lines.Add((Line $true "``$($fixture.path)``$id ``$($fixture.sha256)``"))
    }
    else { $lines.Add((Line $false "``$($fixture.path)`` not found")) }
}

$runs = @(Field $report 'smokeRuns')
$lines.Add(''); $lines.Add('### Scenarios')
if ($runs.Count -eq 0) { $lines.Add((Line $false 'No scenario timings recorded')) }
foreach ($run in $runs) {
    $text = "$($run.name) ($([Math]::Round([double] $run.seconds, 1)) s)"
    if (-not $run.passed -and $run.error) { $text += ': ' + (Hide-Paths ("$($run.error)" -split "`n")[0]) }
    $lines.Add((Line ([bool] $run.passed) $text))
}

if ($report) {
    $lines.Add(''); $lines.Add('### Cleanup')
    if ((Field $report 'mode') -eq 'portable') {
        $lines.Add((Line (-not (Field $report 'portableDataStillPresent')) 'Portable data folder removed'))
    }
    else {
        $lines.Add((Line (-not (Field $report 'packageStillInstalled')) 'Package removed'))
        $lines.Add((Line (-not (Field $report 'certificateStillTrusted')) 'Test certificate no longer trusted'))
    }
    $processErrors = @(Field $report 'processCleanupErrors')
    $lines.Add((Line ($processErrors.Count -eq 0) "No test-owned scheduled task or process left$(if ($processErrors.Count) { ': ' + (Hide-Paths ($processErrors -join '; ')) })"))
    if ($report.PSObject.Properties['scratchFoldersRemaining']) {
        $left = @(Field $report 'scratchFoldersRemaining')
        $lines.Add((Line ($left.Count -eq 0) "Scratch folders removed$(if ($left.Count) { ': ' + (Hide-Paths ($left -join '; ')) })"))
    }
    $events = [int] (Field $report 'applicationEventCount')
    $lines.Add((Line ($events -eq 0) "Application event log entries: $events"))
    # The installer keeps the first eight; each is listed by its first line.
    foreach ($entry in @(Field $report 'applicationEvents')) {
        $first = Hide-Paths ("$($entry.message)" -split "`r?`n")[0]
        $lines.Add("  - $($entry.observedAt) $($entry.provider) $($entry.eventId): $first")
    }
}

$markdown = Join-Path $OutDirectory 'evidence.md'
($lines -join "`n") + "`n" | Set-Content -LiteralPath $markdown -Encoding UTF8 -NoNewline
[ordered]@{
    shard = $Shard; commit = $Commit; runId = $RunId; reportFound = [bool] $report
    report = Hide-Value $report; fixtures = $fixtures
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutDirectory 'evidence.json') -Encoding UTF8
return $markdown
```

- [ ] **Step 4: Run it to verify it passes**

Run: **Writer tests**, then **Parse check** and **ASCII check** on both
files.
Expected: `write_shell_evidence: all cases passed.`, 0 parse errors, and
no non-ASCII lines.

Run it under both shells if you can: PowerShell 5.1 on the host, and
`pwsh` in `core-tests` (Task 2). `ConvertTo-Json` spacing differs between
them, which is why the number check accepts both forms.

- [ ] **Step 5: Commit**

```bash
git add tools/p1/write_shell_evidence.ps1 tools/p1/test_write_shell_evidence.ps1
git commit -m "feat(p1): write an evidence checklist from an installed-shell report"
```

### Task 2: Timings, the WebView2 version, and CI wiring

**Files:**
- Create: `tools/p1/windows_shell_run_record.ps1`
- Create: `tools/p1/test_windows_shell_run_record.ps1`
- Modify: `tools/p1/windows_shell_install.ps1`:
  - the `$report` literal (lines 135–144);
  - `Run-ShellSmoke` (lines 716–818);
  - the runtime facts at the top of the main `try` (lines 2587–2593).
- Modify: `.github/workflows/windows-ci.yml`:
  - `core-tests`, after "Check the release CI gate" (lines 161–163);
  - the `&production-shell-ui-steps` anchor (lines 461–546), which
    `dev-production-shell-ui` reuses.

**Interfaces:**
- Consumes: `write_shell_evidence.ps1` (Task 1).
- Produces:
  - `$report.finishedAt`, which Task 1's writer prints beside `observedAt`.
  - `Measure-SmokeRun([System.Collections.IList] $Runs, [string] $Name, [string] $Mode, [scriptblock] $Body)`
    runs `$Body` and returns its output. It appends
    `[ordered]@{ name; mode; startedAt; seconds; passed; error }` to `$Runs`
    whether `$Body` returns or throws, and rethrows the failure.
  - `Get-WebView2RuntimeVersion([string[]] $Keys = <the EdgeUpdate client keys>)`
    returns the first `pv` value that isn't empty or `0.0.0.0`, or `$null`.
  - `$report.smokeRuns` and `$report.webView2Version` in every installer
    report.

- [ ] **Step 1: Write the failing test**

```powershell
# tools/p1/test_windows_shell_run_record.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_run_record.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

# A passing body: its value comes back, and the run is recorded.
$runs = [System.Collections.Generic.List[object]]::new()
$value = Measure-SmokeRun $runs 'empty-again' 'empty' { [ordered]@{ success = $true; mode = 'empty' } }
Assert-True ($value.mode -eq 'empty') 'The body result was not returned.'
Assert-True ($runs.Count -eq 1) 'A passing run was not recorded.'
Assert-True ($runs[0].name -eq 'empty-again' -and $runs[0].mode -eq 'empty') 'The run name or mode is wrong.'
Assert-True ($runs[0].passed -and $null -eq $runs[0].error) 'A passing run was not marked passed.'
Assert-True ($runs[0].seconds -ge 0) 'The duration is missing.'
[void][DateTime]::Parse($runs[0].startedAt)

# A failing body: the failure is recorded, then rethrown unchanged.
$caught = $null
try { Measure-SmokeRun $runs 'keyboard' 'keyboard' { Start-Sleep -Milliseconds 120; throw 'Installed keyboard shell smoke failed: x' } }
catch { $caught = $_.Exception.Message }
Assert-True ($caught -eq 'Installed keyboard shell smoke failed: x') "The failure was not rethrown (got: $caught)."
Assert-True ($runs.Count -eq 2 -and -not $runs[1].passed) 'A failing run was not recorded as failed.'
Assert-True ($runs[1].error -eq $caught) 'The failing run lost its error.'
Assert-True ($runs[1].seconds -ge 0.1) "The failing run's duration is $($runs[1].seconds)."

# The WebView2 version: the first key with a real version wins.
$base = "HKCU:\Software\DesktopGuidesRunRecordTest-$([Guid]::NewGuid().ToString('N'))"
try {
    $missing = "$base\missing"
    $zero = "$base\zero"
    $real = "$base\real"
    New-Item -Path $zero -Force | Out-Null
    New-ItemProperty -Path $zero -Name pv -Value '0.0.0.0' | Out-Null
    New-Item -Path $real -Force | Out-Null
    New-ItemProperty -Path $real -Name pv -Value '141.0.3537.71' | Out-Null
    Assert-True ((Get-WebView2RuntimeVersion @($missing, $zero, $real)) -eq '141.0.3537.71') 'The real version was not found.'
    Assert-True ($null -eq (Get-WebView2RuntimeVersion @($missing, $zero))) 'A missing runtime did not read as null.'
}
finally {
    Remove-Item -Path $base -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'windows_shell_run_record: all cases passed.'
```

- [ ] **Step 2: Run it to verify it fails**

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-2 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_shell_run_record.ps1'`
Expected: FAIL, because `windows_shell_run_record.ps1` doesn't exist.

- [ ] **Step 3: Write the helpers**

```powershell
# tools/p1/windows_shell_run_record.ps1
# T17.2: facts the installer records for the evidence checklist.

# Runs one smoke and records its timing, whether it passes or throws.
# $Body reads its caller's variables through this function's scope, so the
# locals here carry a measure prefix that no caller uses.
function Measure-SmokeRun(
    [System.Collections.IList] $Runs,
    [string] $Name,
    [string] $Mode,
    [scriptblock] $Body) {
    $measureStarted = (Get-Date).ToUniversalTime()
    $measureWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $measurePassed = $false
    $measureFailure = $null
    try {
        $measureResult = & $Body
        $measurePassed = $true
        return $measureResult
    }
    catch {
        $measureFailure = $_.Exception.Message
        throw
    }
    finally {
        $Runs.Add([ordered]@{
            name = $Name; mode = $Mode; startedAt = $measureStarted.ToString('o')
            seconds = [Math]::Round($measureWatch.Elapsed.TotalSeconds, 1)
            passed = $measurePassed; error = $measureFailure
        })
    }
}

# WebView2 Runtime registers its version under the EdgeUpdate client key:
# machine-wide (32-bit view on x64, then native), or per user.
function Get-WebView2RuntimeVersion([string[]] $Keys = @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')) {
    foreach ($key in $Keys) {
        $value = (Get-ItemProperty -Path $key -Name pv -ErrorAction SilentlyContinue).pv
        if ($value -and $value -ne '0.0.0.0') { return [string] $value }
    }
    return $null
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: the Step 2 command, then **Parse check** and **ASCII check** on both
files.
Expected: `windows_shell_run_record: all cases passed.`

- [ ] **Step 5: Wire the installer**

In `windows_shell_install.ps1`:

1. Load the helpers next to the existing dot-source of
   `windows_shell_process.ps1` (line 150):

```powershell
. (Join-Path $PSScriptRoot 'windows_shell_process.ps1')
. (Join-Path $PSScriptRoot 'windows_shell_run_record.ps1')
```

2. Add the run list to the `$report` literal, after `scenarioGroupsRun`:

```powershell
    scenarioGroupsRun = [System.Collections.Generic.List[string]]::new()
    # T17.2: each Run-ShellSmoke call's timing, for the evidence checklist.
    smokeRuns = [System.Collections.Generic.List[object]]::new()
    success = $false
```

3. Wrap `Run-ShellSmoke`'s whole body, from
   `$resultPath = Join-Path $ResultDirectory "$ResultName.json"` to
   `return $result`, in `Measure-SmokeRun`, and indent it one level. The
   parameter list and every line inside stay as they are:

```powershell
    [string] $BackupPath = '') {
    return Measure-SmokeRun $report.smokeRuns $ResultName $mode {
        $resultPath = Join-Path $ResultDirectory "$ResultName.json"
        # ... the existing body, unchanged, one level deeper ...
        return $result
    }
}
```

   The script block is called from inside `Run-ShellSmoke`, so it reads
   the parameters and `$report` through PowerShell's dynamic scoping. The
   body uses no `$Runs`, `$Name`, `$Body` or `$measure*` variable, which
   would resolve to `Measure-SmokeRun`'s own. `$Mode` resolves there too,
   to the same value. Its `return $result` returns from the
   block, and `Measure-SmokeRun` passes that value on.

4. Record the WebView2 version next to the other runtime facts:

```powershell
    $report.dotnetSdk = (dotnet --version)
    $report.webView2Version = Get-WebView2RuntimeVersion
```

5. Record the end time just before the report is written (line 2935):

```powershell
    $report.finishedAt = (Get-Date).ToUniversalTime().ToString('o')
    $report | ConvertTo-Json -Depth 8 |
```

- [ ] **Step 6: Wire CI**

In `core-tests`, after "Check the release CI gate":

```yaml
      - name: Check the installed-shell evidence writer
        shell: pwsh
        run: ./tools/p1/test_write_shell_evidence.ps1
      - name: Check the installed-shell run record
        shell: pwsh
        run: ./tools/p1/test_windows_shell_run_record.ps1
```

In the `&production-shell-ui-steps` anchor, add this step between "Sign,
install, and test production shell" and "Upload installed shell
evidence":

```yaml
      - name: Write installed shell evidence
        if: always()
        shell: pwsh
        env:
          SHARD: ${{ matrix.shard }}
          # A PR run's GITHUB_SHA is the merge commit; name the PR's head.
          SOURCE_COMMIT: ${{ github.event.pull_request.head.sha || github.sha }}
        run: |
          $installed = 'artifacts/production-shell/installed'
          $markdown = & ./tools/p1/write_shell_evidence.ps1 `
            -ReportPath (Join-Path $installed 'signed-install.json') `
            -FixtureRoot tests/fixtures -OutDirectory $installed `
            -Commit $env:SOURCE_COMMIT -RunId $env:GITHUB_RUN_ID -Shard $env:SHARD
          Get-Content -LiteralPath $markdown -Raw | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
```

And add the markdown to the uploaded files. `evidence.json` already
matches `*.json`:

```yaml
          path: |
            artifacts/production-shell/installed/*.json
            artifacts/production-shell/installed/*.md
            artifacts/production-shell/installed/*.png
            artifacts/production-shell/installed/*.log
```

- [ ] **Step 7: Verify locally**

Run:
- **Writer tests**;
- the run-record test;
- **Parse check** on `windows_shell_install.ps1`;
- **ASCII check** on every changed file;
- the YAML load: `ruby -C /Users/ilya.lissoboi/work/desktop-guides -ryaml -e 'f = ".github/workflows/windows-ci.yml"; begin; YAML.load_file(f, aliases: true); rescue ArgumentError; YAML.load_file(f); end'`
  (the Mac's Ruby 2.6 takes no `aliases:` keyword and allows aliases anyway).

Expected: both tests pass, 0 parse errors, no non-ASCII lines, and the
YAML loads.

- [ ] **Step 8: Commit**

```bash
git add tools/p1/windows_shell_run_record.ps1 tools/p1/test_windows_shell_run_record.ps1 \
  tools/p1/windows_shell_install.ps1 .github/workflows/windows-ci.yml
git commit -m "feat(p1): record smoke timings and publish shell evidence in CI"
```

### Task 3: PR a docs, CI run and PR

**Files:**
- Modify: `docs/p1/e2e-testing.md` ("Gates of record", lines 247–274)
- Modify: `docs/p1/t17-2-release-ci-design.md` (Status line; a new
  "Rulings" section)
- Modify: `docs/p1/implementation-plan.md` (the status paragraph and the
  M6 notes)
- Modify: `docs/progress.md` (the "P1 first usable release" row)

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces: PR a, with a green PR run whose four shards each show
  evidence in their step summary.

- [ ] **Step 1: Push and dispatch one shard**

```bash
git push -u origin feat/p1-t17-2-release-ci
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-2-release-ci -f shell-scope=design -f dev-fast=true
```

Expected: the dispatch's `design` shard passes. Its step summary shows
"Installed shell evidence: design" with no ✗ line. Its artifact holds
`evidence.md` and `evidence.json`. Read `evidence.json` and check that it
has no runner user name (`runneradmin`) and no machine name.

- [ ] **Step 2: Update the docs**

In `e2e-testing.md` "Gates of record", after the sentence ending "and PRs
link to those artifacts.", add:

```markdown
After the run, each shard writes an evidence checklist with
`tools/p1/write_shell_evidence.ps1`, even when its scenarios failed. The
checklist covers:

- the run: commit, run ID and groups;
- the target: OS build, CPU, .NET SDK, Windows App Runtime and WebView2;
- the package: name, source and signed MSIX SHA-256, and certificate
  thumbprint;
- the fixtures, hashed from the files;
- each scenario with its duration;
- cleanup.

The checklist is appended to the job's step summary, and uploaded as
`evidence.md` and `evidence.json` in the shard's artifact. Paths are
rewritten to `%USERPROFILE%`, `%LOCALAPPDATA%` and `%TEMP%` forms, and the
checklist holds no machine or user name. Both package jobs depend on
`core-tests`, so a failing storage, security or script test stops
packaging. The P0 `packages` and native ARM64 jobs still run as the P0
regression lane.
```

In the design, set the Status line to:

```markdown
Status: approved. PR a (the evidence checklist) is in review; PR b (the
workflow lane) follows it.
```

Then add two sections before "Out of scope":
- "Rulings", holding plan rulings 1–3 word for word (fixtures per shard,
  WebView2 version, scratch folders);
- "T17.2 verification record", with the Step 1 dispatch run ID and a line
  for PR a's run.

In `implementation-plan.md`, add after the T16.1 note above the M5 table:

```markdown
T17.2 is in progress. PR a adds an evidence checklist to every installed
shard; PR b adds the workflow lane. A CI scheduled-task entry point, a
portable CI job and a fake-provider lane follow, each with its own design;
see the [T17.2 design](t17-2-release-ci-design.md).
```

In `docs/progress.md`, in the "P1 first usable release" row, change
"Open: T17.2–T17.3 release CI and candidate runs" to "Open: T17.2 release
CI (in progress: the evidence checklist, then the workflow lane) and T17.3
candidate runs".

- [ ] **Step 3: Commit, push and open the PR**

```bash
git add docs/p1/e2e-testing.md docs/p1/t17-2-release-ci-design.md docs/p1/implementation-plan.md docs/progress.md docs/p1/t17-2-release-ci-plan.md
git commit -m "docs(p1): record the T17.2 evidence checklist"
git push
```

Open PR a against `main`, titled "T17.2 PR a: installed-shell evidence
checklist". The body names:
- the task: T17.2, piece B;
- the prerequisites and their status: all merged, as in the plan header;
- the outcome: every shard publishes `evidence.md` and `evidence.json`,
  plus a step summary;
- the dispatch run's link, with an excerpt of one shard's summary.

No UI changes, so no screenshot.

- [ ] **Step 4: Wait for the PR run**

Expected: every job passes, and each of the four `production-shell-ui`
shards has an evidence summary. Record the run ID in the design's
verification record in a follow-up commit, as earlier tasks did. Then stop
for the user's merge decision.

---

# PR b: the workflow lane

Tasks 4–6 add test code: the five `workflow-*` smoke modes. They run only
against an installed package, so their red-green cycle is the `workflow`
shard's dispatch in Task 7. Until then, each task's gate is the parse
check, the ASCII check, and a read-through against the helpers it calls.

### Task 4: The smoke's workflow parameters and `workflow-build`

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`:
  - the `-Mode` ValidateSet (lines 3–34);
  - the parameter block's end (after `$BackupPath`, line 99);
  - the shared reader block's mode list and `$textGame` (lines 1514–1528);
  - a new last branch of the reader block's inner chain, after
    `elseif ($Mode -like 'text-size-*') { ... }` (it ends at line 4320,
    just before the `    }` that closes the reader block).

**Interfaces:**
- Consumes:
  - shared helpers: `Wait-Name`, `Wait-Status`, `Wait-EnabledById`,
    `Wait-VisibleById`, `Wait-HiddenById`, `Find-ById`, `Set-Text`,
    `Press-Enter`, `Invoke-Element`, `Click-Element`, `Select-Element`,
    `Resize-ShellWindow`, `Save-WindowScreenshot`, `Wait-SelectedGuide`,
    `Assert-RowHelp`, `Choose-OpenPath`;
  - reader-block helpers: `Open-TextGuide`, `Back-ToTextGame`.
- Produces:
  - smoke parameters `-WorkflowRoot <folder>` and
    `-WorkflowStatePath <json>`;
  - modes `workflow-build`, `workflow-read`, `workflow-resume`,
    `workflow-export` and `workflow-restore`, all in the reader block;
  - inside the `workflow-*` branch: `$workflowGuides` (keys `txt`, `html`,
    `pdf`, each with `title`, `file` and `format`), `Enter-WorkflowGame`,
    `Import-WorkflowGuide($guide)` and `Read-WorkflowState`. Tasks 5 and 6
    use them.
  - The game is `Workflow Game`. The guides are `Workflow Text Guide`,
    `Workflow Web Guide` and `Workflow PDF Guide`, from `txt-numbered.txt`,
    `html-long\guide.html` and `pdf-long.pdf` under `-WorkflowRoot`.

- [ ] **Step 1: Branch from main**

PR a must be merged first.

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides fetch origin
git -C /Users/ilya.lissoboi/work/desktop-guides checkout -b feat/p1-t17-2-workflow-lane origin/main
```

- [ ] **Step 2: Add the modes and parameters**

In the `-Mode` ValidateSet, after `'restore-cancel', 'restore-interrupted'`:

```powershell
        'restore-cancel', 'restore-interrupted',
        'workflow-build', 'workflow-read', 'workflow-resume', 'workflow-export', 'workflow-restore')]
```

After `[string] $BackupPath = ''`:

```powershell
    [string] $BackupPath = '',

    # T17.2: the scratch folder holding the workflow lane's originals, and
    # the file that carries recorded places from one stage to the next.
    [string] $WorkflowRoot = '',

    [string] $WorkflowStatePath = ''
)
```

In the reader block's `-in @(...)` list, after `'text-size-error-retry'`:

```powershell
        'text-size-pdf', 'text-size-error-prepare', 'text-size-error', 'text-size-error-retry',
        'workflow-build', 'workflow-read', 'workflow-resume', 'workflow-export', 'workflow-restore')) {
```

In `$textGame`, before the final `else`:

```powershell
            elseif ($Mode -like 'text-size-*') { 'Text Size Game' }
            elseif ($Mode -like 'workflow-*') { 'Workflow Game' }
            else { 'Text Reader Game' }
```

- [ ] **Step 3: Add the branch with `workflow-build`**

```powershell
        elseif ($Mode -like 'workflow-*') {
            # T17.2 A: one journey through visible controls, a stage per app
            # run. workflow-read records each guide's place in
            # -WorkflowStatePath for the later stages.
            $workflowGuides = [ordered]@{
                txt = @{ title = 'Workflow Text Guide'; file = 'txt-numbered.txt'; format = 'Text (TXT)' }
                html = @{ title = 'Workflow Web Guide'; file = 'html-long\guide.html'; format = 'Web page (HTML)' }
                pdf = @{ title = 'Workflow PDF Guide'; file = 'pdf-long.pdf'; format = 'PDF' }
            }

            function Enter-WorkflowGame {
                [void](Wait-Name 'LibraryHeading' 'Library')
                Resize-ShellWindow 1500 720
                Select-Element $textGame
                [void](Wait-Name 'GameHeading' $textGame)
                [void](Wait-Status 'Game ready.')
            }

            function Read-WorkflowState {
                if (-not $WorkflowStatePath -or -not (Test-Path -LiteralPath $WorkflowStatePath)) {
                    throw "$Mode needs the places workflow-read recorded (-WorkflowStatePath)."
                }
                return Get-Content -LiteralPath $WorkflowStatePath -Raw | ConvertFrom-Json
            }

            # The lane's files need no answers: plain ASCII text and an
            # unlocked PDF. A question fails at once instead of timing out on
            # a disabled Import; the dialog offers no UTF-8 choice to pick.
            function Import-WorkflowGuide($guide) {
                Click-Element (Wait-EnabledById 'ImportGuideButton')
                Choose-OpenPath (Join-Path $WorkflowRoot $guide.file)
                [void](Wait-VisibleById 'ImportGuideDialog')
                [void](Wait-Name 'ImportFormat' $guide.format)
                $deadline = (Get-Date).AddSeconds(30)
                $ready = $false
                do {
                    foreach ($question in @(@('ImportEncodingCp437', 'an encoding'), @('ImportPdfPasswordInput', 'a password'))) {
                        if (Find-ById $question[0]) {
                            [void](Save-WindowScreenshot 'workflow-import-question')
                            throw "Importing $($guide.file) asked for $($question[1])."
                        }
                    }
                    $primary = Find-ById 'PrimaryButton'
                    $ready = $primary -and $primary.Current.IsEnabled
                    if (-not $ready) { Start-Sleep -Milliseconds 200 }
                } while (-not $ready -and (Get-Date) -lt $deadline)
                if (-not $ready) { throw "Import stayed disabled for $($guide.file)." }
                Set-Text 'GuideTitleInput' $guide.title
                Invoke-Element (Wait-EnabledById 'PrimaryButton')
                [void](Wait-HiddenById 'ImportGuideDialog')
                [void](Wait-SelectedGuide $guide.title)
            }

            if ($Mode -eq 'workflow-build') {
                if (-not $WorkflowRoot) { throw 'workflow-build needs -WorkflowRoot.' }
                [void](Wait-Name 'LibraryEmpty' 'No games in your library.')
                Resize-ShellWindow 1500 720
                Invoke-Element (Wait-EnabledById 'AddGameButton')
                Set-Text 'GameTitleInput' $textGame
                Press-Enter (Wait-VisibleById 'GameTitleInput')
                [void](Wait-Name 'GameHeading' $textGame)
                $report.phases += 'workflow-game-added'

                foreach ($guide in $workflowGuides.Values) { Import-WorkflowGuide $guide }
                foreach ($guide in $workflowGuides.Values) {
                    [void](Assert-RowHelp 'GuideList' $guide.title "$($guide.format), Not started")
                }
                $report.workflowBuildScreenshot = Save-WindowScreenshot 'workflow-build'
                $report.phases += 'workflow-imported'
            }
        }
```

- [ ] **Step 4: Check**

Run: **Parse check** and **ASCII check** on the smoke.
Expected: 0 parse errors and no non-ASCII lines. Check by reading that
each helper named under Consumes is defined in the shared helpers or
earlier in the reader block, with the argument order used here.

- [ ] **Step 5: Commit**

```bash
git add tools/p1/windows_shell_ui_smoke.ps1
git commit -m "test(p1): add the workflow lane's build stage"
```

### Task 5: `workflow-read` and `workflow-resume`

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the `workflow-*` branch
  from Task 4)

**Interfaces:**
- Consumes:
  - from Task 4: `$workflowGuides`, `Enter-WorkflowGame` and
    `Read-WorkflowState`;
  - reader-block helpers: `Wait-FirstTextRow`, `Wait-TopLine`,
    `Wait-TopLineChange`, `Invoke-ReaderCommand`, `Wait-PdfPage`,
    `Invoke-NextPages`, `Wait-PageName`, `Wait-PageVisible` and
    `Wait-TopMark`;
  - shared helpers: `Wait-VisibleById`, `Wait-FocusedId` and
    `Assert-RowHelp`.
- Produces:
  - the state file, `{ "txtTopLine": <int>, "pdfPage": 3, "htmlMark": <int> }`;
  - `Assert-WorkflowPlaces($state, [string] $when)`, which Task 6 uses.
    It opens the HTML, then the TXT, then the PDF, and leaves the PDF as
    the selected guide.

- [ ] **Step 1: Add the place check and both modes**

Inside the `workflow-*` branch, after `Import-WorkflowGuide`:

```powershell
            # A different order from workflow-read's, ending on the PDF so
            # it is the Game page's selected guide afterwards.
            function Assert-WorkflowPlaces($state, [string] $when) {
                Open-TextGuide $workflowGuides.html.title
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PageName 'Long Web Guide')
                [void](Wait-TopMark ([int] $state.htmlMark) "$when (HTML)")
                Back-ToTextGame
                Open-TextGuide $workflowGuides.txt.title
                [void](Wait-Status 'Guide ready.')
                Wait-FirstTextRow
                Wait-TopLine ([int] $state.txtTopLine) "$when (TXT)"
                Back-ToTextGame
                Open-TextGuide $workflowGuides.pdf.title
                [void](Wait-Status 'Guide ready.')
                $page = [int] $state.pdfPage
                [void](Wait-PdfPage $page 200 "page $page of 200" 30)
                Back-ToTextGame
            }
```

After the `workflow-build` branch:

```powershell
            elseif ($Mode -eq 'workflow-read') {
                # The originals are deleted by now; the managed copies open.
                if (-not $WorkflowStatePath) { throw 'workflow-read needs -WorkflowStatePath.' }
                Enter-WorkflowGame
                Open-TextGuide $workflowGuides.txt.title
                [void](Wait-Status 'Guide ready.')
                Wait-FirstTextRow
                Wait-TopLine 1 'A first TXT open'
                $top = 1
                for ($i = 0; $i -lt 3; $i++) {
                    Invoke-ReaderCommand 'Next page'
                    $top = Wait-TopLineChange $top 'Next page'
                }
                Back-ToTextGame

                Open-TextGuide $workflowGuides.pdf.title
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PdfPage 1 200 'page 1 of 200')
                Invoke-NextPages 2
                [void](Wait-PdfPage 3 200 'page 3 of 200' 30)
                Back-ToTextGame

                # Plan ruling 9: the page's own link is the HTML place.
                Open-TextGuide $workflowGuides.html.title
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PageName 'Long Web Guide')
                Click-Element (Wait-PageVisible 'Jump to MARK-0420')
                $mark = Wait-TopMark 420 'the fragment link'
                # Back saves at once; the installer then closes the app.
                Back-ToTextGame

                $state = [ordered]@{ txtTopLine = $top; pdfPage = 3; htmlMark = $mark }
                $state | ConvertTo-Json | Set-Content -LiteralPath $WorkflowStatePath -Encoding UTF8
                $report.workflowPlaces = $state
                $report.phases += 'workflow-read'
            }
            elseif ($Mode -eq 'workflow-resume') {
                $state = Read-WorkflowState
                Enter-WorkflowGame
                Assert-WorkflowPlaces $state 'Reopening after a restart'
                $report.phases += 'workflow-resumed'

                # Mark the PDF complete from the Game page.
                $pdf = $workflowGuides.pdf.title
                $choice = Wait-VisibleById 'CompletionChoice'
                if ($choice.Current.Name -ne "Completion for $pdf") {
                    throw "The completion choice is '$($choice.Current.Name)'; expected the PDF's."
                }
                (Wait-VisibleById 'CompletionInProgress').SetFocus()
                Wait-FocusedId 'CompletionInProgress'
                [System.Windows.Forms.SendKeys]::SendWait('{RIGHT}')
                [void](Wait-Status "$pdf marked complete.")
                [void](Assert-RowHelp 'GuideList' $pdf '*Completed*')
                $report.workflowCompletedScreenshot = Save-WindowScreenshot 'workflow-completed'
                $report.phases += 'workflow-pdf-completed'
            }
```

- [ ] **Step 2: Check**

Run: **Parse check** and **ASCII check** on the smoke.
Expected: 0 parse errors and no non-ASCII lines.

- [ ] **Step 3: Commit**

```bash
git add tools/p1/windows_shell_ui_smoke.ps1
git commit -m "test(p1): add the workflow lane's read and resume stages"
```

### Task 6: `workflow-export` and `workflow-restore`

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the `workflow-*` branch)

**Interfaces:**
- Consumes:
  - from Tasks 4–5: `Enter-WorkflowGame`, `Read-WorkflowState`,
    `Assert-WorkflowPlaces` and `$workflowGuides`;
  - shared helpers: `Show-SettingsCard`, `Choose-SavePath`,
    `Choose-OpenPath`, `Wait-FocusedId`, `Wait-GuideRowCount`,
    `Wait-GameRow`, `Find-RawById` and `Assert-RowHelp`;
  - the existing `-BackupPath` parameter.
- Produces: the last two stages. The installer (Task 7) checks that the
  backup file exists between them.

- [ ] **Step 1: Add both modes**

After the `workflow-resume` branch:

```powershell
            elseif ($Mode -eq 'workflow-export') {
                if (-not $BackupPath) { throw 'workflow-export needs -BackupPath.' }
                [void](Wait-Name 'LibraryHeading' 'Library')
                Select-Element 'Settings'
                [void](Wait-Status 'Settings ready.')
                [void](Show-SettingsCard 'ExportSettingsCard')
                Invoke-Element (Wait-EnabledById 'ExportBackupButton')
                Choose-SavePath $BackupPath
                [void](Wait-Status ('Backup saved: ' + (Split-Path -Leaf $BackupPath) + ' (') -Prefix -Seconds 120)
                $report.phases += 'workflow-exported'
            }
            elseif ($Mode -eq 'workflow-restore') {
                if (-not $BackupPath) { throw 'workflow-restore needs -BackupPath.' }
                $state = Read-WorkflowState
                Enter-WorkflowGame
                [void](Wait-GuideRowCount 3)
                Invoke-Element (Wait-EnabledById 'RemoveGameButton')
                [void](Wait-VisibleById 'RemoveGameDialog')
                Invoke-Element (Wait-EnabledById 'PrimaryButton')
                [void](Wait-HiddenById 'RemoveGameDialog')
                [void](Wait-Name 'LibraryHeading' 'Library')
                [void](Wait-Status "Removed $textGame.")
                [void](Wait-Name 'LibraryEmpty' 'No games in your library.')
                $report.phases += 'workflow-game-removed'

                Select-Element 'Settings'
                [void](Wait-Status 'Settings ready.')
                [void](Show-SettingsCard 'RestoreSettingsExpander')
                Invoke-Element (Wait-EnabledById 'ChooseBackupButton')
                Choose-OpenPath $BackupPath
                Wait-FocusedId 'ReplaceLibraryButton'
                $holds = (Wait-VisibleById 'RestoreHoldsCard').Current.Name
                if ($holds -notlike 'Backup holds: 1 game, 3 guides*') {
                    throw "The backup summary was '$holds'; expected 1 game and 3 guides."
                }
                Invoke-Element (Wait-EnabledById 'ReplaceLibraryButton')
                # Plan ruling 7: an empty library may not ask; confirm only if
                # it does. A finished restore also ends the wait.
                $asked = $false
                $deadline = (Get-Date).AddSeconds(5)
                do {
                    $dialog = Find-ById 'ReplaceLibraryDialog'
                    if ($dialog -and -not $dialog.Current.IsOffscreen) { $asked = $true; break }
                    $probe = Find-RawById 'ShellContent'
                    if ($probe -and $probe.Current.ItemStatus -like '*|Library restored: *') { break }
                    Start-Sleep -Milliseconds 200
                } while ((Get-Date) -lt $deadline)
                $report.workflowRestoreAsked = $asked
                if ($asked) { Invoke-Element (Wait-EnabledById 'PrimaryButton') }
                [void](Wait-Status 'Library restored: ' -Prefix -Seconds 60)
                [void](Wait-Name 'LibraryHeading' 'Library')
                [void](Wait-GameRow $textGame)
                $report.phases += 'workflow-restored'

                Enter-WorkflowGame
                [void](Wait-GuideRowCount 3)
                Assert-WorkflowPlaces $state 'Reopening after the restore'
                [void](Assert-RowHelp 'GuideList' $workflowGuides.pdf.title '*Completed*')
                $report.workflowRestoredScreenshot = Save-WindowScreenshot 'workflow-restored'
                $report.phases += 'workflow-restored-places'
            }
```

- [ ] **Step 2: Check**

Run: **Parse check** and **ASCII check** on the smoke.
Expected: 0 parse errors and no non-ASCII lines.

- [ ] **Step 3: Commit**

```bash
git add tools/p1/windows_shell_ui_smoke.ps1
git commit -m "test(p1): add the workflow lane's export and restore stages"
```

### Task 7: The installer's `workflow` group, the CI shard and the gate test

**Files:**
- Modify: `tools/p1/windows_shell_install.ps1`:
  - the switches (lines 11–26) and `$scenarioGroups` (lines 50–76);
  - `Run-ShellSmoke`'s parameters, arguments and timeout;
  - a new `Run-WorkflowScenarios` after `Run-BackupScenarios`;
  - the group calls after `backup` (line 2768).
- Modify: `.github/workflows/windows-ci.yml`:
  - the `shell-scope` options (lines 17–34);
  - the shard matrix (line 459).
- Modify: `tools/p1/test_release_ci_gate.ps1`

**Interfaces:**
- Consumes:
  - the smoke modes from Tasks 4–6;
  - `Measure-SmokeRun` and `$report.smokeRuns` from PR a.
- Produces:
  - `-WorkflowOnly`, and `workflow` in `-Groups` and `shell-scope`;
  - `$report.fixtures`, `$report.scratchFoldersRemaining` and
    `$report.workflow*` results;
  - `workflow-state.json` in the result folder, uploaded with the shard's
    JSON.

- [ ] **Step 1: Write the failing gate test case**

In `test_release_ci_gate.ps1`, after the `'An unknown commit must be
rejected.'` case:

```powershell
        # T17.2: a run whose only failure is the workflow shard concludes
        # 'failure', and that blocks the commit it ran on.
        Assert-Rejected { & $gate -Commit $code -MainRef main -RunConclusion $red } `
            'A run that failed only its workflow shard must be rejected.' "concluded 'failure'"
```

After `Pop-Location`'s `finally` block, before `$global:LASTEXITCODE = 0`:

```powershell
    # The gate reads the run's conclusion, so the workflow shard counts only
    # if push runs include it and no job may fail without failing the run.
    $ci = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\..\.github\workflows\windows-ci.yml')
    Assert-True ($ci.Contains('{"shard":"workflow","groups":"workflow"}')) `
        'The workflow shard is missing from the push and pull_request matrix.'
    Assert-True (-not $ci.Contains('continue-on-error')) 'A windows-ci job may fail without failing the run.'
```

- [ ] **Step 2: Run it to verify it fails**

Run: **Gate tests**.
Expected: FAIL with "The workflow shard is missing from the push and
pull_request matrix." The new `Assert-Rejected` case passes already: the
gate needs no change, and the case pins that.

- [ ] **Step 3: Add the shard**

In the `shell-scope` options, after `- backup`:

```yaml
          - backup
          - workflow
```

In the matrix `include` string, append the fifth shard after the `html`
shard:

```
{"shard":"html","groups":"html,import,provider"},{"shard":"workflow","groups":"workflow"}]
```

Update the comment above it from "runners of about six minutes each" to
"runners of about six minutes each; `workflow` runs the whole journey on
its own".

- [ ] **Step 4: Run it to verify it passes**

Run: **Gate tests**.
Expected: `release_ci_gate: all cases passed.`

- [ ] **Step 5: Add the installer group**

1. Add the switch and the group:

```powershell
    [switch] $BackupOnly,
    [switch] $WorkflowOnly,
```

```powershell
    'backup' = $BackupOnly.IsPresent
    'workflow' = $WorkflowOnly.IsPresent
}
```

2. In `Run-ShellSmoke`, add the parameters after `$BackupPath`:

```powershell
    [string] $BackupPath = '',
    [string] $WorkflowRoot = '',
    [string] $WorkflowStatePath = '') {
```

   Then add the arguments after the `-BackupPath` line:

```powershell
        if ($BackupPath) { $arguments += ' -BackupPath "' + $BackupPath + '"' }
        if ($WorkflowRoot) { $arguments += ' -WorkflowRoot "' + $WorkflowRoot + '"' }
        if ($WorkflowStatePath) { $arguments += ' -WorkflowStatePath "' + $WorkflowStatePath + '"' }
```

   Then add `$mode -like 'workflow-*'` to the 240-second timeout list:

```powershell
        $timeoutSeconds = if ($mode -like 'provider-*' -or $mode -like 'pdf-*' -or $mode -like 'progress-*' -or $mode -like 'completion-*' -or $mode -like 'export-*' -or $mode -like 'restore-*' -or $mode -like 'workflow-*' -or $mode -eq 'html-position') { 240 }
```

3. Add the group runner after `Run-BackupScenarios`:

```powershell
function Run-WorkflowScenarios {
    # T17.2 A: the whole journey through visible controls, from an empty
    # library, with the app restarted between stages. The originals are
    # copies in a scratch folder, deleted once imported.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    $fixtures = @('p1/txt-numbered.txt', 'p1/html-long', 'p0/generated/pdf-long.pdf', 'p0/pdf-access.pdf')
    $report.fixtures = $fixtures
    $scratch = Join-Path ([Environment]::GetFolderPath('MyDocuments')) (
        'desktop-guides-workflow-' + [Guid]::NewGuid().ToString('N'))
    $originals = Join-Path $scratch 'originals'
    $state = Join-Path $ResultDirectory 'workflow-state.json'
    Remove-Item -LiteralPath $state -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $originals | Out-Null
    try {
        foreach ($fixture in $fixtures) {
            $source = Join-Path $fixtureRoot ($fixture -replace '/', '\')
            if (-not (Test-Path -LiteralPath $source)) {
                throw "The workflow fixture $fixture is missing; run tools/p0/make_fixtures.py first."
            }
            Copy-Item -LiteralPath $source -Destination $originals -Recurse
        }
        $hashOriginals = {
            @(Get-ChildItem -LiteralPath $originals -Recurse -File | Sort-Object FullName | ForEach-Object {
                $_.FullName.Substring($originals.Length) + '=' +
                    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }) -join ';'
        }
        $copied = & $hashOriginals

        Start-InstalledShell
        $report.workflowBuild = Run-ShellSmoke 'workflow-build' -WorkflowRoot $originals
        Close-InstalledShell
        if ((& $hashOriginals) -ne $copied) { throw 'Importing changed an original file.' }

        # The app must read its managed copies from here on.
        Remove-Item -LiteralPath (Join-Path $originals 'txt-numbered.txt'), (Join-Path $originals 'pdf-long.pdf') -Force
        Remove-Item -LiteralPath (Join-Path $originals 'html-long') -Recurse -Force

        Start-InstalledShell
        $report.workflowRead = Run-ShellSmoke 'workflow-read' -WorkflowStatePath $state
        Close-InstalledShell
        Start-InstalledShell
        $report.workflowResume = Run-ShellSmoke 'workflow-resume' -WorkflowStatePath $state
        Close-InstalledShell

        $backup = Join-Path $scratch 'workflow-backup.zip'
        Start-InstalledShell
        $report.workflowExport = Run-ShellSmoke 'workflow-export' -BackupPath $backup
        Close-InstalledShell
        if (-not (Test-Path -LiteralPath $backup)) { throw 'workflow-export saved no backup.' }

        Start-InstalledShell
        $report.workflowRestore = Run-ShellSmoke 'workflow-restore' -BackupPath $backup -WorkflowStatePath $state
        Close-InstalledShell

        # The control was never imported; nothing may have touched it.
        $control = (Get-FileHash -LiteralPath (Join-Path $originals 'pdf-access.pdf') -Algorithm SHA256).Hash
        if ($control -ne (Get-FileHash -LiteralPath (Join-Path $fixtureRoot 'p0\pdf-access.pdf') -Algorithm SHA256).Hash) {
            throw 'The untouched control original changed.'
        }
    }
    finally {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
        $report.scratchFoldersRemaining = @(if (Test-Path -LiteralPath $scratch) { $scratch })
    }
}
```

4. Call it after the backup group:

```powershell
    if (Enter-ScenarioGroup 'backup') { Run-BackupScenarios }
    if (Enter-ScenarioGroup 'workflow') { Run-WorkflowScenarios }
```

5. In the comment above the `*Only` switches, nothing changes. In the
   `-Groups` comment, nothing changes either: `workflow` is a group name
   like the others.

- [ ] **Step 6: Check locally**

Run:
- **Gate tests**;
- **Parse check** on the installer and the smoke;
- **ASCII check** on every changed file;
- the YAML load from Task 2 Step 7.

Expected: all pass.

- [ ] **Step 7: Commit and push**

```bash
git add tools/p1/windows_shell_install.ps1 tools/p1/test_release_ci_gate.ps1 .github/workflows/windows-ci.yml
git commit -m "feat(p1): run the installed workflow lane as a CI shard"
git push -u origin feat/p1-t17-2-workflow-lane
```

- [ ] **Step 8: Run the shard (the lane's red-green)**

```bash
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-2-workflow-lane -f shell-scope=workflow -f dev-fast=true
```

Expected: `dev-production-shell-ui (workflow)` passes. Its step summary
lists, in order and each with ✓:
- the five scenarios `workflow-build`, `workflow-read`,
  `workflow-resume`, `workflow-export` and `workflow-restore`;
- four fixtures;
- "Scratch folders removed".

When a stage fails, use superpowers:systematic-debugging:
1. Read its result JSON and screenshots from the artifact.
2. Find the cause in the smoke or the installer.
3. Fix it and dispatch again.

Ledger every change to a step this plan specifies as a ruling. Never
loosen a check to get a pass. Record `workflowRestoreAsked` from
`signed-install.json` in the ledger: Task 8 records it.

### Task 8: PR b docs, CI run and PR

**Files:**
- Modify: `docs/p1/e2e-testing.md` ("Gates of record")
- Modify: `docs/p1/t17-2-release-ci-design.md`:
  - "A: the workflow lane": the fixture list and the HTML place;
  - "Rulings";
  - the verification record.
- Modify: `docs/p1/implementation-plan.md` and `docs/progress.md`

**Interfaces:**
- Consumes: Tasks 4–7, and the Task 7 dispatch run.
- Produces: PR b, with a green PR run whose `workflow` shard passes.

- [ ] **Step 1: Update the docs**

In `e2e-testing.md` "Gates of record":
- Change "It runs as four parallel shards" to "five", and add `workflow`
  (the whole journey) to the shard list.
- Add `-WorkflowOnly` to the list of `*Only` switches.
- After the evidence paragraph from PR a, add:

```markdown
The `workflow` shard runs the whole journey on the signed install. It uses
visible controls and the keyboard only, never seeds, and restarts the app
between stages:

1. Add a game, and import a TXT, an HTML and a PDF guide from copies in a
   scratch folder.
2. Delete those copies.
3. Read each guide to a place.
4. After a restart, check each place, and mark the PDF complete.
5. Export a backup.
6. Remove the game, restore the backup, and check the guides, places and
   completion again.

Its evidence lists the five stages in that order. A failing stage fails
the `windows-ci` run, and `release_ci_gate.ps1` then refuses the release.
```

In the design's "A: the workflow lane":
- Replace `p0/generated/txt-long.txt` with `p1/txt-numbered.txt`.
- Replace "HTML scrolled about halfway" with "HTML at `MARK-0420`, through
  the page's own link".
- Add plan rulings 4–9 to "Rulings", word for word. Ruling 7 gets one more
  sentence: whether the empty library asked, from `workflowRestoreAsked`.
- Add a "T17.2 verification record" section with:
  - PR a's merge commit and run ID;
  - the Task 7 dispatch run ID;
  - PR b's run ID (filled in by Step 3's follow-up).

In `implementation-plan.md`, change the T17.2 note to say PR a and PR b
landed, and that E, C and D follow.

In `progress.md`, change the T17.2 wording in the "P1 first usable release"
row to match.

- [ ] **Step 2: Commit, push and open the PR**

```bash
git add docs/p1/e2e-testing.md docs/p1/t17-2-release-ci-design.md docs/p1/implementation-plan.md docs/progress.md
git commit -m "docs(p1): record the T17.2 workflow lane"
git push
```

Open PR b against `main`, titled "T17.2 PR b: installed workflow lane".
The body names:
- the task: T17.2, piece A;
- the prerequisites and their status: PR a merged, plus the plan header's
  list;
- the outcome: a fifth `workflow` shard runs the whole journey on the
  signed install and blocks release promotion when it fails;
- the dispatch run, and the shard's evidence summary;
- rulings 8 and 9, which change the design's fixture and HTML place.

The UI is unchanged, so no app screenshot is needed. Attach the lane's
`workflow-restored.png` from the artifact as evidence.

- [ ] **Step 3: Wait for the PR run**

Expected: every job passes, including `production-shell-ui (workflow)`,
and all five shards publish evidence. Record the run ID in the
verification record in a follow-up commit. Then stop for the user's merge
decision.

## Traceability

| Spec item | Task |
| --- | --- |
| A: the workflow group, fresh data root, scratch originals | 7 |
| A: stage 1, build through Add game and the real picker | 4 |
| A: stage 2, remove the originals | 7 |
| A: stages 3–4, read, restart, resume, mark complete | 5, 7 |
| A: stage 5, export through the Save dialog | 6 |
| A: stage 6, remove, restore, check guides, places and completion | 6 |
| A: stage 7, cleanup of scratch and backup | 7 |
| A: pass rule, per-stage result JSON and durations | 2, 7 |
| B: the writer, both files, every section | 1 |
| B: anonymisation | 1 |
| B: timings recorded by `Run-ShellSmoke` | 2 |
| B: CI step summary and artifact, even after a failure | 2 |
| B: the writer's tests in `core-tests` | 1, 2 |
| CI: the fifth shard and its `shell-scope` choice | 7 |
| Release gate: the `test_release_ci_gate.ps1` case | 7 |
| Docs: "Gates of record" and the status pages | 3, 8 |
| Verification: evidence on the PR runs; the shard on dispatch, then the PR | 3, 7, 8 |
