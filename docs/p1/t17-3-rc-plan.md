# T17.3 PR 1 (Release-Candidate Harness) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The installed harness can run the whole journey on a
release-signed Public package, upgraded from the published release, with
an app-scoped offline relaunch. The release notes and a tested-matrix
skeleton are ready before the `v1.0.1` tag.

**Architecture:**
- **The lane:** a small helper, `windows_shell_lane.ps1`, holds the lane's
  names: package name, event prefix and profile filter. It also has the
  release-signer check. The installer, the smoke, the offline controller
  and the task entry point read the names from it instead of hard-coding
  Preview.
- **Release signing:** `-ReleaseSigned` swaps the installer's test
  re-signing for checking the pinned release signer and trusting it for
  the run.
- **The `rc` group:** it reuses the workflow lane's stages around an
  in-place upgrade and a new `workflow-offline` mode. The offline mode
  runs inside a firewall block shared with the provider group.

**Tech Stack:** PowerShell 5.1 and 7, AppX deployment cmdlets,
Authenticode, UI Automation, Windows Firewall cmdlets, GitHub Actions.

**Spec:** `docs/p1/t17-3-release-candidate-design.md`

**Target:** T17.3 PR 1 (of two), on `feat/p1-t17-3-rc` (spec commit
`70449b2`).
**Prerequisites:** T17.1, T17.2 and T17.4 (#106, #107, #108), all merged.

## Global Constraints

- **Preview stays the default:** every switch defaults to today's
  behaviour, so CI's Preview runs are byte-for-byte the same scenarios.
- **Public names:** the package is `IlyaLissoboi.DesktopGuides`, events are
  `Local\DesktopGuides.<Name>`, and the profile folder is
  `IlyaLissoboi.DesktopGuides_*`. Preview is `DesktopGuides.Preview`,
  `Local\DesktopGuides.Preview.<Name>` and `DesktopGuides.Preview_*`.
- **The release signer pin:** thumbprint
  `BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6`, from
  `docs/p1/release/release-certificate.json`. Trust is
  `docs/p1/release/desktop-guides-release.cer` in
  `Cert:\LocalMachine\TrustedPeople`, added only if absent and removed only
  if this run added it.
- **The release asset name:** `DesktopGuides-<a.b.c.d>-x64.msix`.
- **The `rc` group** requires `-Lane Public`, `-ReleaseSigned`,
  `-PackagePath` (the candidate) and `-PreviousPackage`, and runs alone.
- **The offline block:** only the package's executable, through the
  existing elevated controller, authorized by the user each time.
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.
  The smoke always runs under Windows PowerShell 5.1 (`powershell.exe`).
- **No `cd` in local shell commands.**

## Rulings against the spec

1. **The upgrade checks are rewritten in the installer.** The spec says
   to reuse `windows_package_upgrade.ps1`'s identity and data checks, but
   that script runs top-level code and depends on its own script variables,
   so it can't be dot-sourced. The installer gets `Invoke-RcUpgrade`. It
   applies the same three checks: the version increased, the family is
   unchanged, and `describe-library` is identical before and after.
2. **Offline is a new smoke mode, `workflow-offline`.** It reopens each
   guide at its recorded place, through `Assert-WorkflowPlaces`, and then
   checks for remote connections. It doesn't reuse `workflow-resume`, so
   the completion toggle isn't run twice.
3. **The provider group's firewall handshake becomes
   `Invoke-WithFirewallBlock`,** shared with the `rc` group. The provider
   behaviour is unchanged.

## Review Focus

1. **A Preview CI run changes behaviour** because an event name was
   rewritten wrongly, for example a missing `$`. Every Preview shard must
   pass unchanged. Pinned by Task 2's grep checks and Task 6's full dispatch.
2. **The run leaves the release certificate trusted, or removes a trust it
   didn't add.** Pinned by Task 3's cleanup branch and the rehearsal's
   cleanup lines.
3. **After the upgrade, the launch tasks still start the old
   executable.** The install location changes with the version. Pinned by
   Task 4's re-registration, and by the rehearsal's later stages, which
   would fail with "Interactive launch failed" otherwise.
4. **The offline stage passes without a block,** for example when the
   controller wasn't running. It must fail, not skip. Pinned by Task 4's
   `Invoke-WithFirewallBlock`, which throws when no `blocked.json`
   arrives, and by the `rc` pass rule.
5. **A package not signed with the pin is installed.** It must be refused
   before `Add-AppxPackage`. Pinned by Task 1's signer test and Task 3's
   check of both packages.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t17-3) { Remove-Item -Recurse -Force E:\work\desktop-guides\t17-3 }; New-Item -ItemType Directory E:\work\desktop-guides\t17-3 | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t17-3'
}
```

- **Script tests:** `s 'cd /d E:\work\desktop-guides\t17-3 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\<test>.ps1'`
- **ASCII check:** `grep -nP '[^\x00-\x7F]' <file>`.

---
### Task 1: The lane and release-signer helpers

**Files:**
- Create: `tools/p1/windows_shell_lane.ps1`
- Test: `tools/p1/test_windows_shell_lane.ps1`
- Modify: `tools/p1/windows_shell_profile.ps1` (`Assert-FreshPreviewProfile`
  becomes a wrapper)
- Modify: `tools/p1/test_windows_shell_profile.ps1` (a Public case)
- Modify: `.github/workflows/windows-ci.yml` (`core-tests`, after "Check
  the trim-warning gate")

**Interfaces:**
- Produces:
  - `Get-ShellLane([string] $Lane)` returns `{ Name; PackageName; EventPrefix }`;
  - `Get-ShellLanePackageNames` returns `string[]`;
  - `Test-InstalledLaneExecutable([string] $Path, [string] $ProgramFiles)`
    returns `bool`;
  - `Get-ReleasePin([string] $PinPath)` returns the thumbprint string;
  - `Assert-ReleaseSigner([string] $Path, [string] $Thumbprint)` throws, or
    returns nothing;
  - `Assert-FreshPackageProfile([string] $LocalAppDataPath, [string] $PackageName)`.

- [ ] **Step 1: Write the failing tests**

`tools/p1/test_windows_shell_lane.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_lane.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock] $Action, [string] $Expected, [string] $Message) {
    try { & $Action | Out-Null } catch {
        Assert-True ("$_" -like "*$Expected*") "$Message Got: $_"
        return
    }
    throw "$Message It was accepted."
}

$preview = Get-ShellLane 'Preview'
Assert-True ($preview.PackageName -eq 'DesktopGuides.Preview' -and $preview.EventPrefix -eq 'DesktopGuides.Preview') `
    "Unexpected Preview names: $($preview | ConvertTo-Json -Compress)"
$public = Get-ShellLane 'Public'
Assert-True ($public.PackageName -eq 'IlyaLissoboi.DesktopGuides' -and $public.EventPrefix -eq 'DesktopGuides') `
    "Unexpected Public names: $($public | ConvertTo-Json -Compress)"
Assert-True ((Get-ShellLane).Name -eq 'Preview') 'Preview is the default lane.'
Assert-Rejected { Get-ShellLane 'Store' } 'Store' 'An unknown lane should fail.'
Assert-True ((@(Get-ShellLanePackageNames) -join ',') -eq 'DesktopGuides.Preview,IlyaLissoboi.DesktopGuides') `
    'Both lanes'' packages are listed.'

$pf = 'C:\Program Files'
Assert-True (Test-InstalledLaneExecutable "$pf\WindowsApps\DesktopGuides.Preview_0.1.0.0_x64__en8r599z3n35j\DesktopGuides.Production.exe" $pf) `
    'The installed Preview exe is accepted.'
Assert-True (Test-InstalledLaneExecutable "$pf\WindowsApps\IlyaLissoboi.DesktopGuides_1.0.1.0_x64__8wekyb3d8bbwe\DesktopGuides.Production.exe" $pf) `
    'The installed Public exe is accepted.'
Assert-True (-not (Test-InstalledLaneExecutable "$pf\WindowsApps\Other.App_1.0.0.0_x64__x\DesktopGuides.Production.exe" $pf)) `
    'Another package''s exe is refused.'
Assert-True (-not (Test-InstalledLaneExecutable 'C:\Users\me\DesktopGuides.Production.exe' $pf)) `
    'A portable exe is refused.'

# The real pin, and a broken one.
$pinPath = Join-Path $PSScriptRoot '..\..\docs\p1\release\release-certificate.json'
Assert-True ((Get-ReleasePin $pinPath) -eq 'BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6') 'The release pin is read.'
$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-lane-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $badPin = Join-Path $scratch 'pin.json'
    Set-Content -LiteralPath $badPin -Value '{ "thumbprint": "not-a-thumbprint" }'
    Assert-Rejected { Get-ReleasePin $badPin } 'no valid thumbprint' 'A broken pin should fail.'

    # An unsigned file, and a file signed by someone else.
    $unsigned = Join-Path $scratch 'unsigned.msix'
    [IO.File]::WriteAllBytes($unsigned, [byte[]](1..64))
    Assert-Rejected { Assert-ReleaseSigner $unsigned 'BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6' } 'no signature' `
        'An unsigned package should fail.'
    $other = (Get-Process -Id $PID).Path
    Assert-Rejected { Assert-ReleaseSigner $other 'BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6' } 'not the release certificate' `
        'A package signed by another certificate should fail.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'windows_shell_lane: all cases passed.'
```

In `tools/p1/test_windows_shell_profile.ps1`, before the closing `}` of the
`try`, add:

```powershell
    # T17.3: the Public lane's profile is guarded the same way.
    Assert-FreshPackageProfile $scratch 'IlyaLissoboi.DesktopGuides'
    $publicProfile = Join-Path $packagesRoot 'IlyaLissoboi.DesktopGuides_test'
    New-Item -ItemType Directory -Path $publicProfile | Out-Null
    Assert-Rejected { Assert-FreshPackageProfile $scratch 'IlyaLissoboi.DesktopGuides' } `
        'An existing Public profile was accepted.'
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-3 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_shell_lane.ps1 & powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_shell_profile.ps1'`
Expected: FAIL on both, because `windows_shell_lane.ps1` and
`Assert-FreshPackageProfile` don't exist.

- [ ] **Step 3: Write the helpers**

`tools/p1/windows_shell_lane.ps1`:

```powershell
# T17.3: the names that differ between the Preview and Public lanes, and the
# release-signer check. The app's own names are in AppLaneNames (Core).

function Get-ShellLane([string] $Lane = 'Preview') {
    switch ($Lane) {
        'Preview' { return [pscustomobject]@{ Name = 'Preview'; PackageName = 'DesktopGuides.Preview'; EventPrefix = 'DesktopGuides.Preview' } }
        'Public' { return [pscustomobject]@{ Name = 'Public'; PackageName = 'IlyaLissoboi.DesktopGuides'; EventPrefix = 'DesktopGuides' } }
    }
    throw "Unknown lane '$Lane'; expected Preview or Public."
}

# Every lane's package, so a leftover check misses neither.
function Get-ShellLanePackageNames { return @('DesktopGuides.Preview', 'IlyaLissoboi.DesktopGuides') }

function Test-InstalledLaneExecutable([string] $Path, [string] $ProgramFiles = $env:ProgramFiles) {
    foreach ($name in Get-ShellLanePackageNames) {
        if ($Path -like "$ProgramFiles\WindowsApps\$($name)_*_x64__*\DesktopGuides.Production.exe") { return $true }
    }
    return $false
}

function Get-ReleasePin([string] $PinPath) {
    $pin = Get-Content -LiteralPath $PinPath -Raw | ConvertFrom-Json
    if ("$($pin.thumbprint)" -notmatch '^[0-9A-F]{40}$') { throw "The release pin $PinPath has no valid thumbprint." }
    return [string] $pin.thumbprint
}

# Refuses a package that isn't signed by the pinned release certificate.
# Trust isn't checked here: Add-AppxPackage checks it once the run has
# trusted the certificate. A changed package fails as HashMismatch.
function Assert-ReleaseSigner([string] $Path, [string] $Thumbprint) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if (-not $signature.SignerCertificate) {
        throw "$Path has no signature the harness can read (status $($signature.Status))."
    }
    if ($signature.SignerCertificate.Thumbprint -ne $Thumbprint) {
        throw "$Path is signed by $($signature.SignerCertificate.Thumbprint), not the release certificate $Thumbprint."
    }
    if ("$($signature.Status)" -in @('HashMismatch', 'NotSigned', 'NotSupportedFileFormat', 'Incompatible')) {
        throw "$Path has a broken signature (status $($signature.Status))."
    }
}
```

In `tools/p1/windows_shell_profile.ps1`, replace `Assert-FreshPreviewProfile`
with:

```powershell
function Assert-FreshPackageProfile([string] $LocalAppDataPath, [string] $PackageName) {
    if ([string]::IsNullOrWhiteSpace($LocalAppDataPath)) {
        throw 'Local app data path is unavailable.'
    }
    $packagesRoot = Join-Path $LocalAppDataPath 'Packages'
    if (-not (Test-Path -LiteralPath $packagesRoot)) {
        return
    }
    if (-not (Test-Path -LiteralPath $packagesRoot -PathType Container)) {
        throw 'Package profile root is not a directory.'
    }
    $profiles = @(Get-ChildItem -LiteralPath $packagesRoot -Directory `
        -Filter "$($PackageName)_*" -ErrorAction Stop)
    if ($profiles.Count -gt 0) {
        throw "A $PackageName profile already exists; refusing the fresh-profile install test."
    }
}

function Assert-FreshPreviewProfile([string] $LocalAppDataPath) {
    Assert-FreshPackageProfile $LocalAppDataPath 'DesktopGuides.Preview'
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command.
Expected: `windows_shell_lane: all cases passed.`, and the profile test's
own success line.

If `Assert-ReleaseSigner` on `powershell.exe` reports "no signature"
rather than "not the release certificate", the binary is catalog-signed on
this host. Then use `(Get-Command signtool.exe -ErrorAction
SilentlyContinue).Source`, or any Authenticode-signed `.exe` under
`C:\Program Files (x86)\Windows Kits\10\bin`, and ledger it.

- [ ] **Step 5: Add the test to `core-tests` and commit**

After "Check the trim-warning gate":

```yaml
      - name: Check the lane and release-signer helpers
        shell: pwsh
        run: ./tools/p1/test_windows_shell_lane.ps1
```

```bash
git add tools/p1/windows_shell_lane.ps1 tools/p1/test_windows_shell_lane.ps1 tools/p1/windows_shell_profile.ps1 tools/p1/test_windows_shell_profile.ps1 .github/workflows/windows-ci.yml
git commit -m "test(p1): add the lane and release-signer helpers for T17.3"
```

---

### Task 2: Make the harness lane-aware

**Files:**
- Modify: `tools/p1/windows_shell_install.ps1` (the param block; after
  line 53; the preflight at 116-127; 22 event names; `Get-AppxPackage` at
  2785, 2790, 3028 and 3046; profile checks at 2788; the message at 3056;
  `Run-ShellSmoke`'s arguments at 769-779)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the param block; lines
  2664 and 4291)
- Modify: `tools/p1/windows_provider_offline_controller.ps1` (line 46)
- Modify: `tools/p1/start_installed_task.ps1` (the param block; lines
  43-44; `Write-Leftovers` at 154-158)

**Interfaces:**
- Consumes: Task 1's `Get-ShellLane`, `Get-ShellLanePackageNames`,
  `Test-InstalledLaneExecutable` and `Assert-FreshPackageProfile`.
- Produces:
  - installer parameters `-Lane` (`Preview` or `Public`, default
    `Preview`), `-ReleaseSigned`, `-PreviousPackage <path>` and `-RcOnly`;
  - script variables `$lanePackage` (the identity name) and
    `$eventPrefix`;
  - the smoke parameter `-EventPrefix` (default `DesktopGuides.Preview`).

- [ ] **Step 1: Add the installer's new parameters, and watch the mirror test fail**

In `windows_shell_install.ps1`'s param block, after `[switch] $WorkflowOnly,`:

```powershell
    # T17.3: the release-candidate journey. Needs -Lane Public, -ReleaseSigned,
    # -PreviousPackage and -PackagePath (the candidate); runs alone.
    [switch] $RcOnly,
```

and after `[switch] $FakeProviders` (adding a comma after it):

```powershell
    # T17.3: Public drives the released identity: IlyaLissoboi.DesktopGuides,
    # with events under Local\DesktopGuides.*.
    [ValidateSet('Preview', 'Public')]
    [string] $Lane = 'Preview',

    # T17.3: install packages already signed with the release certificate,
    # checked against docs/p1/release/release-certificate.json; nothing is
    # re-signed.
    [switch] $ReleaseSigned,

    # T17.3: the published release the rc group upgrades from.
    [string] $PreviousPackage
```

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-3 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_start_installed_task.ps1'`
Expected: FAIL, because the parameter lists differ (the four new names).

- [ ] **Step 2: Mirror them in the task entry point**

In `start_installed_task.ps1`'s param block, after `[switch] $WorkflowOnly,`
add `[switch] $RcOnly,`. After `[switch] $FakeProviders,` add:

```powershell
    # T17.3: the lane, release-signed packages and the previous release.
    [ValidateSet('Preview', 'Public')]
    [string] $Lane,
    [switch] $ReleaseSigned,
    [string] $PreviousPackage,
```

`$Lane` has no default here, so only a bound value is forwarded and the
installer's default applies. In `$pathParameters` (lines 43-44), add
`'PreviousPackage'`.

`Write-Leftovers` checks both lanes. Dot-source the lane helper near the
top (after `$ErrorActionPreference = 'Stop'`):

```powershell
. (Join-Path $PSScriptRoot 'windows_shell_lane.ps1')
```

and replace the Preview `if` in `Write-Leftovers` with:

```powershell
    foreach ($name in Get-ShellLanePackageNames) {
        if (Get-AppxPackage -Name $name -ErrorAction SilentlyContinue) {
            $left += "the $name package is still installed"
        }
    }
```

Run: the Step 1 command.
Expected: PASS.

- [ ] **Step 3: Resolve the lane in the installer and use its names**

After `$ProgressPreference = 'SilentlyContinue'` (line 53):

```powershell
. (Join-Path $PSScriptRoot 'windows_shell_lane.ps1')
$lane = Get-ShellLane $Lane
$lanePackage = $lane.PackageName
$eventPrefix = $lane.EventPrefix
```

In the non-portable preflight (lines 116-127), use the lane's names:

```powershell
    if (Get-AppxPackage -Name $lanePackage) {
        throw "A $lanePackage package is already installed; refusing to replace it."
    }
    Assert-FreshPackageProfile $env:LOCALAPPDATA $lanePackage
```

Rewrite the 22 event names with two exact substitutions. Single-quoted
names become double-quoted, so the variable expands:

```bash
python3 - <<'EOF2'
import re
p = '/Users/ilya.lissoboi/work/desktop-guides/tools/p1/windows_shell_install.ps1'
s = open(p).read()
s, a = re.subn(r"'Local\\DesktopGuides\.Preview\.([A-Za-z0-9]+)'", r'"Local\\$eventPrefix.\1"', s)
s, b = re.subn(r'"Local\\DesktopGuides\.Preview\.', r'"Local\\$eventPrefix.', s)
open(p, 'w').write(s)
print(a + b)
EOF2
grep -n 'Local.DesktopGuides\.Preview' tools/p1/windows_shell_install.ps1
```

Expected: `22`, then no output. Read three rewritten lines
(`grep -n 'eventPrefix\.' tools/p1/windows_shell_install.ps1 | head -3`).
Each must read like `"Local\$eventPrefix.RedirectedActivation"`. If one
doesn't, fix it by hand, and ledger it.

The remaining Preview identity uses become the lane's:
- `Get-AppxPackage -Name DesktopGuides.Preview` (lines 2785, 2790, 3028,
  3046) becomes `Get-AppxPackage -Name $lanePackage`;
- `Assert-FreshPreviewProfile $env:LOCALAPPDATA` (line 2788) becomes
  `Assert-FreshPackageProfile $env:LOCALAPPDATA $lanePackage`;
- the message `'A Preview package not confirmed as test-owned remains untouched.'`
  (line 3056) becomes `"A $lanePackage package not confirmed as test-owned remains untouched."`.

Run: `grep -n 'DesktopGuides\.Preview' tools/p1/windows_shell_install.ps1`
Expected: no output.

Pass the prefix to the smoke. In `Run-ShellSmoke`, after the line
`' -ExpectedTopLine ' + $ExpectedTopLine`:

```powershell
        $arguments += ' -EventPrefix ' + $eventPrefix
```

- [ ] **Step 4: Use the prefix in the smoke and the controller**

In `windows_shell_ui_smoke.ps1`'s param block, after `[string] $WorkflowStatePath`
(adding a comma after it):

```powershell
    # T17.3: the lane's event prefix, from the installer.
    [string] $EventPrefix = 'DesktopGuides.Preview'
```

Then:
- line 2664: `"Local\DesktopGuides.Preview.HtmlAssetDelay.$ProcessId"` becomes
  `"Local\$EventPrefix.HtmlAssetDelay.$ProcessId"`;
- line 4291: `"Local\DesktopGuides.Preview.TextRemeasure.$($process.Id)"` becomes
  `"Local\$EventPrefix.TextRemeasure.$($process.Id)"`.

In `windows_provider_offline_controller.ps1`, after
`$ErrorActionPreference = 'Stop'`, add
`. (Join-Path $PSScriptRoot 'windows_shell_lane.ps1')`. Line 46's
Preview pattern becomes `-not (Test-InstalledLaneExecutable $request.program) -or`.

Run: `grep -n 'DesktopGuides\.Preview' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_provider_offline_controller.ps1`
Expected: only the smoke's new default (`= 'DesktopGuides.Preview'`).

- [ ] **Step 5: Refuse impossible combinations**

In the installer, just before the non-portable preflight (the `else {` at
line 116), add:

```powershell
if ($ReleaseSigned -and $portable) { throw '-ReleaseSigned applies only to an MSIX.' }
if ($ReleaseSigned -and $Lane -ne 'Public') { throw '-ReleaseSigned needs -Lane Public.' }
if (-not $portable -and $Lane -eq 'Public' -and -not $ReleaseSigned) {
    throw 'A Public MSIX can only be installed release-signed: pass -ReleaseSigned.'
}
if ($PreviousPackage -and -not $RcOnly) { throw '-PreviousPackage applies only to the rc group.' }
```

- [ ] **Step 6: Run the script tests on the host**

```bash
stage
s 'cd /d E:\work\desktop-guides\t17-3 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_start_installed_task.ps1 & powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_scripts_parse.ps1 & powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_shell_lane.ps1'
```

Expected: each prints its success line. The installed Preview behaviour
is checked by Task 6's CI dispatch.

- [ ] **Step 7: Commit**

```bash
git add tools/p1/windows_shell_install.ps1 tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_provider_offline_controller.ps1 tools/p1/start_installed_task.ps1
git commit -m "test(p1): make the installed harness lane-aware for the Public package"
```

---

### Task 3: Install release-signed packages without re-signing

**Files:**
- Modify: `tools/p1/windows_shell_install.ps1` (the preflight's name
  check at 119-122; the install block at 2757-2791; the cleanup at
  3037-3060)

**Interfaces:**
- Consumes: Task 1's `Get-ReleasePin` and `Assert-ReleaseSigner`, and
  Task 2's `$lanePackage`, `-ReleaseSigned`, `-RcOnly` and
  `-PreviousPackage`.
- Produces:
  - report fields `releaseSigned` (bool) and `certificateThumbprint` (the
    pin);
  - script variables `$releaseThumbprint` and `$releaseTrustAdded`;
  - with `-RcOnly`, the previous release is installed first.

The test for this task is the host rehearsal (Task 7), because only a real
release-signed package exercises it. Task 1's unit tests cover the signer
refusals. This task's change is to a run path no CI job takes, and that is
stated here.

- [ ] **Step 1: Accept the release asset name**

The preflight's name check (lines 119-122) becomes:

```powershell
    $packageName = Split-Path $PackagePath -Leaf
    # Release assets are DesktopGuides-<version>-x64.msix (release.yml).
    $expectedName = if ($ReleaseSigned) { '^DesktopGuides-[0-9]+(\.[0-9]+){3}-x64\.msix$' }
        else { '^DesktopGuides\.Production_[0-9]+(\.[0-9]+){3}_x64\.msix$' }
    if ($packageName -notmatch $expectedName) {
        throw "Expected an x64 production MSIX, got $packageName."
    }
    if ($PreviousPackage -and (Split-Path $PreviousPackage -Leaf) -notmatch $expectedName) {
        throw "Expected the previous release's MSIX, got $(Split-Path $PreviousPackage -Leaf)."
    }
```

- [ ] **Step 2: Check the release signer and trust it, instead of re-signing**

In the install block, keep `$report.sourcePackageSha256 = ...`. Wrap
everything from `Copy-Item $PackagePath $signed -Force` to
`$report.signedPackageSha256 = (Get-FileHash $signed -Algorithm SHA256).Hash`
in the `else` of:

```powershell
        if ($ReleaseSigned) {
            # T17.3: the bytes under test are the release's own; nothing is re-signed.
            $releaseRoot = Join-Path $PSScriptRoot '..\..\docs\p1\release'
            $releaseThumbprint = Get-ReleasePin (Join-Path $releaseRoot 'release-certificate.json')
            $releaseCertificate = Join-Path $releaseRoot 'desktop-guides-release.cer'
            if ((Get-PfxCertificate -FilePath $releaseCertificate).Thumbprint -ne $releaseThumbprint) {
                throw 'desktop-guides-release.cer does not match the pinned thumbprint.'
            }
            foreach ($package in @(@($PackagePath, $PreviousPackage) | Where-Object { $_ })) {
                Assert-ReleaseSigner $package $releaseThumbprint
            }
            $report.releaseSigned = $true
            $report.certificateThumbprint = $releaseThumbprint
            $signed = $PackagePath
            $report.signedPackageSha256 = $report.sourcePackageSha256
            if ($PreviousPackage) {
                $report.previousPackageSha256 = (Get-FileHash $PreviousPackage -Algorithm SHA256).Hash
            }
            # Trusted only for this run, and only if it wasn't already.
            if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$releaseThumbprint")) {
                Import-Certificate -FilePath $releaseCertificate `
                    -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
                $releaseTrustAdded = $true
            }
        }
        else {
            # ... the existing Copy-Item, New-SelfSignedCertificate, SignTool lines, unchanged ...
        }
```

Then the install line `Add-AppxPackage -Path $signed` becomes:

```powershell
        # The rc group installs the previous release first and upgrades later.
        Add-AppxPackage -Path $(if ($RcOnly) { $PreviousPackage } else { $signed })
```

- [ ] **Step 3: Remove only the trust this run added**

In the cleanup, after the `if ($certificate) { ... }` block:

```powershell
        if ($releaseTrustAdded) {
            Remove-Item "Cert:\LocalMachine\TrustedPeople\$releaseThumbprint" -ErrorAction SilentlyContinue
        }
```

and the `$report.certificateStillTrusted = ...` assignment becomes:

```powershell
        $report.certificateStillTrusted = if ($certificate) {
            Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
        } elseif ($releaseTrustAdded) {
            Test-Path "Cert:\LocalMachine\TrustedPeople\$releaseThumbprint"
        } else { $false }
```

- [ ] **Step 4: Check the scripts still parse, and commit**

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-3 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_scripts_parse.ps1'`
Expected: `windows_scripts_parse: all scripts passed.`

```bash
git add tools/p1/windows_shell_install.ps1
git commit -m "test(p1): install release-signed packages against the pinned signer"
```

---

### Task 4: The `rc` group

**Files:**
- Modify: `tools/p1/windows_shell_install.ps1`:
  - the group table (55-71) and its validation (73-94);
  - `Enter-ScenarioGroup` (2822-2829);
  - `Run-BlockedNetworkScenario` (2350-2375);
  - `Run-WorkflowScenarios` (2377-2461);
  - a new `Run-RcScenarios` and `Invoke-RcUpgrade`;
  - the dispatch after 2929;
  - the install block's report.
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the mode lists at 28 and
  1555; a `workflow-offline` branch after `workflow-resume`)

**Interfaces:**
- Consumes: Tasks 2 and 3.
- Produces:
  - `Invoke-WithFirewallBlock([Collections.IDictionary] $Record, [scriptblock] $Body)`
    fills `$Record.firewallRule` and `$Record.firewallRuleRemoved`;
  - `New-WorkflowOriginals` returns
    `{ Scratch; Originals; State; Hash; Copied; FixtureRoot }`;
  - `Remove-WorkflowOriginals`, `Assert-WorkflowControl` and
    `Assert-NoWorkflowCrashes`;
  - the report's `rc`:
    `{ previousVersion; candidateVersion; familyUnchanged; libraryIntact; offline }`;
  - the smoke mode `workflow-offline`.

- [ ] **Step 1: Register the group, explicit only**

At the end of `$scenarioGroups` (after `'workflow' = $WorkflowOnly.IsPresent`),
add `'rc' = $RcOnly.IsPresent`. After the existing group validation (line 94):

```powershell
# T17.3: rc runs only when asked for, on a release candidate.
if ($selectedGroups -contains 'rc' -and -not $RcOnly) { throw 'Run the rc group with -RcOnly.' }
if ($RcOnly -and -not ($Lane -eq 'Public' -and $ReleaseSigned -and $PackagePath -and $PreviousPackage)) {
    throw '-RcOnly needs -Lane Public, -ReleaseSigned, -PackagePath (the candidate) and -PreviousPackage.'
}
```

In `Enter-ScenarioGroup`, as its first line:

```powershell
        if ($name -eq 'rc' -and -not $RcOnly) { return $false }
```

Read lines 73-94 first: if the variable holding the selection isn't
`$selectedGroups`, use its real name, and ledger it.

- [ ] **Step 2: Share the firewall handshake**

Replace `Run-BlockedNetworkScenario` (2350-2375) with:

```powershell
# Asks the elevated offline controller to block the app's executable, runs
# $Body, then asks for the rule's removal. The block is required: no
# controller means a failure, never a skip.
function Invoke-WithFirewallBlock([Collections.IDictionary] $Record, [scriptblock] $Body) {
    $handshake = Join-Path $ResultDirectory 'offline-handshake'
    New-Item -ItemType Directory -Force $handshake | Out-Null
    [ordered]@{ runId = $runId; program = $expectedExecutablePath } |
        ConvertTo-Json | Set-Content (Join-Path $handshake 'request.json') -Encoding UTF8
    $blocked = Wait-HandshakeFile (Join-Path $handshake 'blocked.json') 120
    if (-not $blocked) {
        throw 'The offline controller did not confirm the firewall rule.'
    }
    $Record.firewallRule = $blocked.ruleName
    try {
        & $Body
    }
    finally {
        [ordered]@{ runId = $runId } | ConvertTo-Json |
            Set-Content (Join-Path $handshake 'done.json') -Encoding UTF8
        $restored = Wait-HandshakeFile (Join-Path $handshake 'restored.json') 60
        $Record.firewallRuleRemoved = [bool]($restored -and $restored.removed)
        if (-not $Record.firewallRuleRemoved) {
            $cleanupErrors.Add('The offline controller did not confirm that it removed the firewall rule.')
        }
    }
}

function Run-BlockedNetworkScenario {
    Invoke-WithFirewallBlock $report.providers {
        Start-InstalledShell
        $report.providers.blockedNetwork = Run-ShellSmoke 'provider-offline' `
            -ResultName 'provider-offline-blocked' -ExpectedProviderFailure 'Unavailable'
        Close-InstalledShell
    }
}
```

`$report.providers` must be a dictionary. Check how it's created (`grep -n
'providers = ' tools/p1/windows_shell_install.ps1`): if it's a
`[pscustomobject]`, change the parameter type to `[object]` and set the
two fields with `Add-Member -Force`, and ledger it.

- [ ] **Step 3: Split the workflow lane's setup and checks into helpers**

Above `Run-WorkflowScenarios`, add:

```powershell
# The workflow lane's four originals, copied to a scratch folder outside the repo.
function New-WorkflowOriginals {
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    $fixtures = @('p1/txt-numbered.txt', 'p1/html-long', 'p0/generated/pdf-long.pdf', 'p0/pdf-access.pdf')
    $report.fixtures = $fixtures
    $scratch = Join-Path ([Environment]::GetFolderPath('MyDocuments')) (
        'desktop-guides-workflow-' + [Guid]::NewGuid().ToString('N'))
    $originals = Join-Path $scratch 'originals'
    $state = Join-Path $ResultDirectory 'workflow-state.json'
    Remove-Item -LiteralPath $state -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $originals | Out-Null
    foreach ($fixture in $fixtures) {
        $source = Join-Path $fixtureRoot ($fixture -replace '/', '\')
        if (-not (Test-Path -LiteralPath $source)) {
            throw "The workflow fixture $fixture is missing; run tools/p0/make_fixtures.py first."
        }
        Copy-Item -LiteralPath $source -Destination $originals -Recurse
    }
    $hash = {
        @(Get-ChildItem -LiteralPath $originals -Recurse -File | Sort-Object FullName | ForEach-Object {
            $_.FullName.Substring($originals.Length) + '=' +
                (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }) -join ';'
    }.GetNewClosure()
    return [pscustomobject]@{
        Scratch = $scratch; Originals = $originals; State = $state
        Hash = $hash; Copied = (& $hash); FixtureRoot = $fixtureRoot
    }
}

# The app must read its managed copies from here on; pdf-access.pdf stays as a control.
function Remove-WorkflowOriginals([string] $Originals) {
    Remove-Item -LiteralPath (Join-Path $Originals 'txt-numbered.txt'), (Join-Path $Originals 'pdf-long.pdf') -Force
    Remove-Item -LiteralPath (Join-Path $Originals 'html-long') -Recurse -Force
}

# The control was never imported; nothing may have touched it.
function Assert-WorkflowControl($Workflow) {
    $control = (Get-FileHash -LiteralPath (Join-Path $Workflow.Originals 'pdf-access.pdf') -Algorithm SHA256).Hash
    if ($control -ne (Get-FileHash -LiteralPath (Join-Path $Workflow.FixtureRoot 'p0\pdf-access.pdf') -Algorithm SHA256).Hash) {
        throw 'The untouched control original changed.'
    }
}

# #93: the lane never kills the app, so any crash entry is a real crash.
function Assert-NoWorkflowCrashes([datetime] $Since) {
    # A crash entry can land a moment after the process has gone.
    Start-Sleep -Seconds 5
    $crashes = @(Get-AppCrashEvents $Since)
    $report.workflowCrashEntries = $crashes.Count
    if ($crashes.Count -gt 0) {
        $first = ("$($crashes[-1].Message)" -split "`r?`n" | Select-Object -First 4) -join ' | '
        throw "The app wrote $($crashes.Count) crash entries during the workflow lane (#93); the first: $first"
    }
}
```

Then rewrite `Run-WorkflowScenarios` to use them, with the same stages in
the same order:

```powershell
function Run-WorkflowScenarios {
    # T17.2 A: the whole journey through visible controls, from an empty
    # library, with the app restarted between stages. The originals are
    # copies in a scratch folder, deleted once imported.
    $crashWatchStart = Get-Date
    $workflow = New-WorkflowOriginals
    try {
        Start-InstalledShell
        $report.workflowBuild = Run-ShellSmoke 'workflow-build' -WorkflowRoot $workflow.Originals
        Close-InstalledShell
        if ((& $workflow.Hash) -ne $workflow.Copied) { throw 'Importing changed an original file.' }
        Remove-WorkflowOriginals $workflow.Originals

        Start-InstalledShell
        $report.workflowRead = Run-ShellSmoke 'workflow-read' -WorkflowStatePath $workflow.State
        Close-InstalledShell
        Start-InstalledShell
        $report.workflowResume = Run-ShellSmoke 'workflow-resume' -WorkflowStatePath $workflow.State
        Close-InstalledShell

        $backup = Join-Path $workflow.Scratch 'workflow-backup.zip'
        Start-InstalledShell
        $report.workflowExport = Run-ShellSmoke 'workflow-export' -BackupPath $backup
        Close-InstalledShell
        if (-not (Test-Path -LiteralPath $backup)) { throw 'workflow-export saved no backup.' }

        Start-InstalledShell
        $report.workflowRestore = Run-ShellSmoke 'workflow-restore' -BackupPath $backup -WorkflowStatePath $workflow.State
        Close-InstalledShell
        Assert-WorkflowControl $workflow

        # #93: Windows.Data.Pdf crashed the app at exit after a PDF session,
        # about half the time, so the lane closes after one five times.
        $report.workflowPdfExits = @()
        for ($round = 1; $round -le 5; $round++) {
            Start-InstalledShell
            $report.workflowPdfExits += Run-ShellSmoke 'workflow-pdf-exit' -ResultName "workflow-pdf-exit-$round"
            Close-InstalledShell
        }
        Assert-NoWorkflowCrashes $crashWatchStart
    }
    finally {
        Remove-Item -LiteralPath $workflow.Scratch -Recurse -Force -ErrorAction SilentlyContinue
        $report.scratchFoldersRemaining = @(if (Test-Path -LiteralPath $workflow.Scratch) { $workflow.Scratch })
    }
}
```

One change in ordering: the scratch folder is created before the `try`,
as `New-WorkflowOriginals`. A failed copy now throws before the `try`, so
the half-made folder stays behind. Make `New-WorkflowOriginals` remove
`$scratch` in a `catch` that rethrows:

```powershell
    New-Item -ItemType Directory -Path $originals | Out-Null
    try {
        # ... the foreach copying the fixtures ...
    }
    catch {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
        throw
    }
```

- [ ] **Step 4: Add the upgrade and the rc journey**

After `Run-WorkflowScenarios`:

```powershell
# T17.3: replaces the previous release with the candidate in place. The
# version must rise, the family stay, and the library read the same; the
# install location carries the version, so the launch task is rebuilt.
function Invoke-RcUpgrade {
    $before = Get-AppxPackage -Name $lanePackage
    $libraryBefore = Invoke-ShellSeed @('describe-library', $dataRoot)
    Add-AppxPackage -Path $PackagePath
    $after = Get-AppxPackage -Name $lanePackage
    if (-not $after) { throw 'The candidate did not install over the previous release.' }
    $report.rc.candidateVersion = $after.Version.ToString()
    $report.rc.familyUnchanged = $after.PackageFamilyName -eq $before.PackageFamilyName
    if (-not $report.rc.familyUnchanged) {
        throw "The upgrade changed the family from $($before.PackageFamilyName) to $($after.PackageFamilyName)."
    }
    if ([version] $after.Version -le [version] $before.Version) {
        throw "The upgrade went from $($before.Version) to $($after.Version)."
    }
    $report.rc.libraryIntact = (Invoke-ShellSeed @('describe-library', $dataRoot)) -eq $libraryBefore
    if (-not $report.rc.libraryIntact) { throw 'The library changed across the upgrade.' }
    $script:installed = $after
    $report.packageFullName = $after.PackageFullName
    $report.installLocation = $after.InstallLocation
    $script:expectedExecutablePath = Join-Path $after.InstallLocation 'DesktopGuides.Production.exe'
    $script:launchAction = New-ShellLaunchAction $launchResultPath
    $script:secondLaunchAction = New-ShellLaunchAction $secondLaunchResultPath
    Register-ScheduledTask -TaskName $launchTask -Action $script:launchAction `
        -Principal $principal -Force | Out-Null
}

# T17.3: the release-candidate journey. The previous release builds the
# library through the UI; the candidate then reads, resumes, relaunches
# offline, exports and restores it.
function Run-RcScenarios {
    $crashWatchStart = Get-Date
    $workflow = New-WorkflowOriginals
    try {
        Start-InstalledShell
        $report.workflowBuild = Run-ShellSmoke 'workflow-build' -WorkflowRoot $workflow.Originals
        Close-InstalledShell
        if ((& $workflow.Hash) -ne $workflow.Copied) { throw 'Importing changed an original file.' }
        Remove-WorkflowOriginals $workflow.Originals

        Invoke-RcUpgrade

        Start-InstalledShell
        $report.workflowRead = Run-ShellSmoke 'workflow-read' -WorkflowStatePath $workflow.State
        Close-InstalledShell
        Start-InstalledShell
        $report.workflowResume = Run-ShellSmoke 'workflow-resume' -WorkflowStatePath $workflow.State
        Close-InstalledShell

        $report.rc.offline = [ordered]@{}
        Invoke-WithFirewallBlock $report.rc.offline {
            Start-InstalledShell
            $report.workflowOffline = Run-ShellSmoke 'workflow-offline' -WorkflowStatePath $workflow.State
            Close-InstalledShell
        }

        $backup = Join-Path $workflow.Scratch 'workflow-backup.zip'
        Start-InstalledShell
        $report.workflowExport = Run-ShellSmoke 'workflow-export' -BackupPath $backup
        Close-InstalledShell
        if (-not (Test-Path -LiteralPath $backup)) { throw 'workflow-export saved no backup.' }

        Start-InstalledShell
        $report.workflowRestore = Run-ShellSmoke 'workflow-restore' -BackupPath $backup -WorkflowStatePath $workflow.State
        Close-InstalledShell
        Assert-WorkflowControl $workflow
        Assert-NoWorkflowCrashes $crashWatchStart
    }
    finally {
        Remove-Item -LiteralPath $workflow.Scratch -Recurse -Force -ErrorAction SilentlyContinue
        $report.scratchFoldersRemaining = @(if (Test-Path -LiteralPath $workflow.Scratch) { $workflow.Scratch })
    }
}
```

In the install block, after `$report.packageFullName = $installed.PackageFullName`:

```powershell
        if ($RcOnly) {
            $report.rc = [ordered]@{
                previousVersion = $installed.Version.ToString(); candidateVersion = $null
                familyUnchanged = $false; libraryIntact = $false; offline = $null
            }
        }
```

In the dispatch, after the workflow line (2929):

```powershell
    if (Enter-ScenarioGroup 'rc') { Run-RcScenarios }
```

Read how `$launchAction` and `$secondLaunchAction` are used
(`grep -n 'secondLaunchAction\|launchAction' tools/p1/windows_shell_install.ps1`).
If the second action is registered under its own task, re-register that
task in `Invoke-RcUpgrade` too, and ledger it.

- [ ] **Step 5: Add the `workflow-offline` smoke mode**

In `windows_shell_ui_smoke.ps1`, add `'workflow-offline'` after
`'workflow-resume'` in both mode lists (line 28 and line 1555). After the
`workflow-resume` branch (ending with
`$report.phases += 'workflow-pdf-completed'` and its `}`):

```powershell
            elseif ($Mode -eq 'workflow-offline') {
                # T17.3: with the app's network blocked, each managed copy
                # still opens at its place, and nothing reaches out.
                $state = Read-WorkflowState
                Enter-WorkflowGame
                Assert-WorkflowPlaces $state 'Relaunching offline'
                Assert-NoRemoteConnections 'offline relaunch'
                $report.phases += 'workflow-offline'
            }
```

Read the context first: `Assert-NoRemoteConnections` is defined inside a
branch at 1365. If it isn't visible from the workflow branches, move its
definition up to the smoke's shared helpers, and ledger it.

- [ ] **Step 6: Check, and commit**

```bash
stage
s 'cd /d E:\work\desktop-guides\t17-3 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_scripts_parse.ps1 & powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_start_installed_task.ps1'
grep -nP '[^\x00-\x7F]' tools/p1/windows_shell_install.ps1 tools/p1/windows_shell_ui_smoke.ps1
```

Expected: both success lines, and no non-ASCII output.

```bash
git add tools/p1/windows_shell_install.ps1 tools/p1/windows_shell_ui_smoke.ps1
git commit -m "test(p1): add the rc group: upgrade in place, then the journey with an offline relaunch"
```

---

### Task 5: The release-candidate evidence lines

**Files:**
- Modify: `tools/p1/write_shell_evidence.ps1` (the Package section,
  lines 148-159)
- Test: `tools/p1/test_write_shell_evidence.ps1`

**Interfaces:**
- Consumes: the report fields from Tasks 3 and 4: `releaseSigned`,
  `certificateThumbprint`, `previousPackageSha256`,
  `rc { previousVersion, candidateVersion, familyUnchanged, libraryIntact, offline { firewallRule, firewallRuleRemoved } }`
  and `workflowOffline.remoteConnections`.

- [ ] **Step 1: Write the failing test**

In `test_write_shell_evidence.ps1`, after the full-report asserts (after
the folder-hash assert), add a release-candidate report:

```powershell
    # T17.3: a release-candidate run shows the signer, the upgrade and offline.
    $rcReport = [ordered]@{}
    foreach ($key in $report.Keys) { $rcReport[$key] = $report[$key] }
    $rcReport.releaseSigned = $true
    $rcReport.certificateThumbprint = 'BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6'
    $rcReport.previousPackageSha256 = 'PPP'
    $rcReport.rc = [ordered]@{
        previousVersion = '1.0.0.0'; candidateVersion = '1.0.1.0'; familyUnchanged = $true; libraryIntact = $true
        offline = [ordered]@{ firewallRule = 'DesktopGuides-P1-ProviderOffline-r1'; firewallRuleRemoved = $true }
    }
    $rcReport.workflowOffline = [ordered]@{ remoteConnections = 0 }
    $rcPath = Join-Path $root 'rc-install.json'
    $rcReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $rcPath -Encoding UTF8
    $rcMd = & $writer -ReportPath $rcPath -FixtureRoot $fixtures -OutDirectory (Join-Path $root 'rc-out') -Commit 'abc123' -RunId '43' -Shard 'rc'
    $rcText = Get-Content -LiteralPath $rcMd -Raw -Encoding UTF8
    Assert-True ($rcText.Contains("- $pass Release signer ``BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6`` matches the pin")) 'The signer line is missing.'
    Assert-True ($rcText.Contains("- $pass Previous release SHA-256 ``PPP``")) 'The previous package line is missing.'
    Assert-True ($rcText.Contains('### Release candidate')) 'The release-candidate section is missing.'
    Assert-True ($rcText.Contains("- $pass Upgrade 1.0.0.0 -> 1.0.1.0: same family, library intact")) 'The upgrade line is missing.'
    Assert-True ($rcText.Contains("- $pass Offline relaunch: rule added, no connection, rule removed")) 'The offline line is missing.'
    Assert-True (-not $rcText.Contains('Test certificate')) 'A release-signed run named a test certificate.'

    # A changed library, a wrong signer and a rule left behind all show as failures.
    $rcReport.certificateThumbprint = 'OTHER'
    $rcReport.rc.libraryIntact = $false
    $rcReport.rc.offline.firewallRuleRemoved = $false
    $rcReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $rcPath -Encoding UTF8
    $badMd = & $writer -ReportPath $rcPath -FixtureRoot $fixtures -OutDirectory (Join-Path $root 'rc-bad') -Commit 'abc123' -RunId '44' -Shard 'rc'
    $badText = Get-Content -LiteralPath $badMd -Raw -Encoding UTF8
    Assert-True ($badText.Contains("- $fail Release signer ``OTHER`` matches the pin")) 'A wrong signer passed.'
    Assert-True ($badText.Contains("- $fail Upgrade 1.0.0.0 -> 1.0.1.0: same family, library intact")) 'A changed library passed.'
    Assert-True ($badText.Contains("- $fail Offline relaunch: rule added, no connection, rule removed")) 'A remaining rule passed.'

    # Without a release candidate, there is no such section.
    Assert-True (-not $text.Contains('### Release candidate')) 'An ordinary run showed a release-candidate section.'
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-3 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_write_shell_evidence.ps1'`
Expected: FAIL with "The signer line is missing."

- [ ] **Step 3: Write the lines**

In `write_shell_evidence.ps1`, after the `$ErrorActionPreference` line,
dot-source the lane helper:

```powershell
. (Join-Path $PSScriptRoot 'windows_shell_lane.ps1')
```

In the Package section, the `Test certificate` line becomes:

```powershell
        if (Field $report 'releaseSigned') {
            $pin = Get-ReleasePin (Join-Path $PSScriptRoot '..\..\docs\p1\release\release-certificate.json')
            $signer = Field $report 'certificateThumbprint'
            $lines.Add((Line ($signer -eq $pin) "Release signer ``$signer`` matches the pin"))
            if (Field $report 'previousPackageSha256') {
                $lines.Add((Line $true "Previous release SHA-256 ``$(Field $report 'previousPackageSha256')``"))
            }
        }
        else {
            $lines.Add((Line ([bool] (Field $report 'certificateThumbprint')) "Test certificate ``$(Field $report 'certificateThumbprint')``"))
        }
```

After the Package section's closing `}` (the end of the
`if (... -ne 'portable') { } else { }`), add:

```powershell
    $rc = Field $report 'rc'
    if ($rc) {
        $lines.Add(''); $lines.Add('### Release candidate')
        $upgraded = [bool] (Field $rc 'familyUnchanged') -and [bool] (Field $rc 'libraryIntact')
        $lines.Add((Line $upgraded "Upgrade $(Field $rc 'previousVersion') -> $(Field $rc 'candidateVersion'): same family, library intact"))
        $offline = Field $rc 'offline'
        $remote = Field (Field $report 'workflowOffline') 'remoteConnections'
        $offlineOk = [bool] (Field $offline 'firewallRule') -and [bool] (Field $offline 'firewallRuleRemoved') -and
            ($null -ne $remote) -and ([int] $remote -eq 0)
        $lines.Add((Line $offlineOk 'Offline relaunch: rule added, no connection, rule removed'))
    }
```

`Field` returns `$null` for a missing object, so a missing `workflowOffline`
reads as a failure. Check that `Field` handles a `$null` first argument
(lines 89-91). If it doesn't, guard with `if ($x) { Field $x 'name' }`,
and ledger it.

- [ ] **Step 4: Run the test to verify it passes**

Run: the Step 2 command.
Expected: the test's success line.

- [ ] **Step 5: Commit**

```bash
git add tools/p1/write_shell_evidence.ps1 tools/p1/test_write_shell_evidence.ps1
git commit -m "test(p1): show the release signer, the upgrade and the offline relaunch in the evidence"
```

---

### Task 6: The release-notes limits, the matrix skeleton, the procedure, and a CI check

**Files:**
- Modify: `docs/p1/release/release-notes-template.md` (the "Limits" list)
- Create: `docs/p1/release/tested-matrix.md`
- Modify: `docs/p1/e2e-testing.md` (the offline procedure step, the
  "Install and upgrade" and "Prerequisites and target matrix" rows, and a
  new `rc` group paragraph)

- [ ] **Step 1: The release notes**

In `release-notes-template.md`'s "Limits" list, the line "- Tested on
Windows 11. Clean-machine and Windows 10 support aren't claimed yet."
becomes:

```markdown
- Tested on Windows 11 x64; see the [tested matrix](https://github.com/ilya-slalom/desktop-guides/blob/main/docs/p1/release/tested-matrix.md). Clean-machine and Windows 10 support aren't claimed yet.
- Scanned (image-only) PDF pages show the page image only, marked "Image-only page; OCR is unavailable." They have no text to select, search or read aloud.
```

- [ ] **Step 2: The matrix skeleton**

`docs/p1/release/tested-matrix.md`:

```markdown
# Tested matrix

What each release was tested on before it was published (T17.3). A target
is supported only when it has a passing row here. The run's full evidence
stays with its run; this page holds the sanitized summary.

## How a row is made

The `rc` group of the installed harness runs on the release's draft
assets ([design](../t17-3-release-candidate-design.md)):
1. install the previous release;
2. build a library through the UI;
3. upgrade in place to the candidate;
4. read, resume and complete;
5. relaunch with the app's network blocked;
6. export, remove, and restore.

Every stage must pass.

## 1.0.1

Not run yet.

## Not tested

| Target | Why |
| --- | --- |
| A clean Windows 11 x64 machine without the Windows App Runtime | The runtime-free VM check is T17.8, in the final milestone. |
| Windows 10 x64 | Not advertised. |
| Windows 11 ARM64 | Not released; it needs its own native full-workflow result. |
| The portable build's own release gates | First run without installation, data location and replacing the exe are defined separately. Its scenario groups run in CI on every change. |
```

- [ ] **Step 3: The procedure**

In `e2e-testing.md`:
- In the "Install and upgrade" row of the scenario table, the text becomes:
  "Signed MSIX installs in an interactive session; an older version
  upgrades under the same identity without losing a populated library.
  Verify package version, launch, and data after restart. For a release,
  the `rc` group does this on the draft's own assets (T17.3)."
- In the "Prerequisites and target matrix" row, the text becomes:
  "On a disposable runtime-free Windows 11 x64 VM, observe missing Windows
  App Runtime/WebView2 behavior and offline-installer recovery (T17.8, in
  the final milestone). Repeat the complete installed workflow on each
  advertised OS/CPU target with the `rc` group, and record it in the
  [tested matrix](release/tested-matrix.md)."
- After the scenario table, add:

```markdown
### The release-candidate group (`rc`, T17.3)

`-RcOnly` runs the journey on a release's draft assets. It takes
`-Lane Public`, `-ReleaseSigned`, `-PackagePath <candidate>` and
`-PreviousPackage <previous release>`, and runs alone.

- **Before installing,** each package's signer must be the pinned release
  certificate. The certificate is trusted for the run only, and only if it
  wasn't already.
- **The stages:**
  1. the previous release installs and builds a library through the UI
     (`workflow-build`);
  2. the originals are removed;
  3. the candidate upgrades in place: the version rises, the family stays,
     and `describe-library` is unchanged;
  4. then `workflow-read`, `workflow-resume` and `workflow-offline`;
  5. then `workflow-export` and `workflow-restore`.
- **Offline:** `workflow-offline` relaunches the app while the elevated
  offline controller blocks the package's executable. Each guide must open
  at its place with no non-loopback connection.
- **The offline method:** T17.3 uses this app-scoped rule instead of the
  adapter disconnect in step 6 above. It proves TR17.1 for the app without
  cutting the host's network, and with it the SSH session coordinating the
  run.
- **Authorization:** the controller is started separately, elevated, with
  the user's authorization for each run. Without it, the stage fails: it
  is never skipped.
- **The record:** the sanitized result goes to the
  [tested matrix](release/tested-matrix.md).
```

- [ ] **Step 4: Commit, push, and check the Preview lanes in CI**

```bash
git add docs/p1/release/release-notes-template.md docs/p1/release/tested-matrix.md docs/p1/e2e-testing.md
git commit -m "docs(p1): add the tested matrix, the release-note limits and the rc procedure"
git push -u origin feat/p1-t17-3-rc
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-3-rc
```

Expected: every job passes: all 10 installed shards on both lanes, and
`core-tests` with the new lane test. This proves the lane-aware harness
is unchanged for Preview. Pay attention to the `html` shard, which uses
the most event names. Record the run ID.

---

### Task 7: A host rehearsal of the `rc` group

**Files:** none committed. This is verification.

The rehearsal uses a release dry run's artifact. It is a candidate built
the same way, but it isn't the one to be published.

- [ ] **Step 1: Get the two packages**

```bash
gh workflow run release.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-3-rc -f tag=v1.0.1
```

Once it passes:

```bash
mkdir -p /tmp/t17-3-rc && gh run download <dry-run-id> --repo ilya-slalom/desktop-guides -n release-1.0.1.0 -D /tmp/t17-3-rc/candidate
gh release download v1.0.0 --repo ilya-slalom/desktop-guides -p 'DesktopGuides-*-x64.msix' -p 'release-manifest.json' -D /tmp/t17-3-rc/previous
ls /tmp/t17-3-rc/candidate/release /tmp/t17-3-rc/previous
s 'powershell -NoProfile -Command "New-Item -ItemType Directory -Force E:\work\t17-3-rc | Out-Null"'
scp -q -o LogLevel=ERROR /tmp/t17-3-rc/candidate/release/DesktopGuides-1.0.1.0-x64.msix /tmp/t17-3-rc/previous/DesktopGuides-1.0.0.0-x64.msix pcsx2-win:E:/work/t17-3-rc/
```

Expected:
- `DesktopGuides-1.0.1.0-x64.msix` from the dry run, and
  `DesktopGuides-1.0.0.0-x64.msix` from v1.0.0, are on the host;
- each one's SHA-256 matches its `release-manifest.json`.

Check that with:

```bash
python3 -c "import json,hashlib,sys; [print(n, json.load(open(m,encoding='utf-8-sig'))['msix']['sha256'].lower() == hashlib.sha256(open(f,'rb').read()).hexdigest()) for n,m,f in [('candidate','/tmp/t17-3-rc/candidate/release/release-manifest.json','/tmp/t17-3-rc/candidate/release/DesktopGuides-1.0.1.0-x64.msix'),('previous','/tmp/t17-3-rc/previous/release-manifest.json','/tmp/t17-3-rc/previous/DesktopGuides-1.0.0.0-x64.msix')]]"
```

Expected: `candidate True` and `previous True`. Then delete `/tmp/t17-3-rc`.
The packages are public release assets, but they aren't kept locally.

- [ ] **Step 2: Stage the repo and the fixtures on the host**

```bash
stage
s 'cd /d E:\work\desktop-guides\t17-3 && python tools\p0\make_fixtures.py'
```

Expected: the fixtures are generated, including
`tests\fixtures\p0\generated\pdf-long.pdf`. If `python` isn't on the
host's PATH, generate them locally (`python3 tools/p0/make_fixtures.py`),
copy `tests/fixtures/p0/generated` over with `tar`, and ledger it.

- [ ] **Step 3: Ask the user to authorize the offline block**

Stop and ask the user. Name what happens: a Windows Firewall outbound
block rule for the installed `IlyaLissoboi.DesktopGuides` executable only.
It lasts at most 10 minutes, and the elevated controller removes it and
reports the removal. Continue only on an explicit yes.

- [ ] **Step 4: Run the controller and the group**

Start the controller, elevated through the SSH session, in the background:

```bash
s 'powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\t17-3\tools\p1\windows_provider_offline_controller.ps1 -ResultDirectory E:\work\desktop-guides\artifacts\p1-e2e\t17-3-rehearsal'
```

(run it with the Bash tool's `run_in_background`). Then the group:

```bash
s 'cd /d E:\work\desktop-guides\t17-3 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\start_installed_task.ps1 -PackagePath E:\work\t17-3-rc\DesktopGuides-1.0.1.0-x64.msix -PreviousPackage E:\work\t17-3-rc\DesktopGuides-1.0.0.0-x64.msix -Lane Public -ReleaseSigned -RcOnly -AllowOfflineFirewallRule -ResultDirectory E:\work\desktop-guides\artifacts\p1-e2e\t17-3-rehearsal -TimeoutMinutes 40' > /tmp/t17-3-rehearsal.log 2>&1; tail -20 /tmp/t17-3-rehearsal.log
```

Expected: exit 0. Then read the evidence:

```bash
s 'powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\t17-3\tools\p1\write_shell_evidence.ps1 -ReportPath E:\work\desktop-guides\artifacts\p1-e2e\t17-3-rehearsal\signed-install.json -FixtureRoot E:\work\desktop-guides\t17-3\tests\fixtures -OutDirectory E:\work\desktop-guides\artifacts\p1-e2e\t17-3-rehearsal -Commit <head> -RunId rehearsal -Shard rc' | tail -1
s 'type E:\work\desktop-guides\artifacts\p1-e2e\t17-3-rehearsal\evidence.md'
```

Expected:
- every line is ✓;
- "Release signer ... matches the pin";
- "Upgrade 1.0.0.0 -> 1.0.1.0: same family, library intact";
- "Offline relaunch: rule added, no connection, rule removed";
- the stages `workflow-build`, `workflow-read`, `workflow-resume`,
  `workflow-offline`, `workflow-export` and `workflow-restore`;
- cleanup: package removed, the certificate not left trusted (if the run
  added it), and no scratch folders.

**If `workflow-build` fails on v1.0.0** because its UI differs: stop.
Ledger the failure and what differs. Then apply the spec's fallback:
populate v1.0.0's library with the seed tool (`seed-navigation`) instead,
recording `rc.previousLibrary = 'seeded'`, and rerun. That is one more
task-sized change, so tell the user before making it.

Any other failure: use superpowers:systematic-debugging. Read the
evidence and the stage results before changing anything. Each rerun
needs the user's authorization for the offline block again.

- [ ] **Step 5: Confirm the host is clean**

```bash
s 'powershell -NoProfile -Command "Get-AppxPackage -Name IlyaLissoboi.DesktopGuides; Test-Path Cert:\LocalMachine\TrustedPeople\BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6; Get-NetFirewallRule -Name DesktopGuides-P1-* -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name"'
```

Expected: no package, `False`, and no rule. Delete `E:\work\t17-3-rc`. Keep
the rehearsal's evidence folder, which holds no user data, until the PR
merges.

---

### Task 8: Records, the full PR run and the PR

**Files:**
- Modify: `docs/p1/t17-3-release-candidate-design.md` (Status; a "PR 1
  verification record")

- [ ] **Step 1: Write the record**

- Set Status to "design approved; PR 1 (harness and docs) implemented on
  `feat/p1-t17-3-rc`; the candidate run (tag `v1.0.1`) follows."
- Add a "PR 1 verification record" section:
  - the script tests' red and green states;
  - the Task 6 dispatch ID;
  - the rehearsal's evidence lines and stage durations, as a sanitized
    summary with no paths;
  - every ledgered ruling.

- [ ] **Step 2: Commit, push, open the PR, and wait for its run**

```bash
git add docs
git commit -m "docs(p1): record T17.3 PR 1 and the rc rehearsal"
git push
```

Title: "T17.3 PR 1: the release-candidate harness, tested matrix and
release-note limits". The body names:
- the task, the prerequisites and the outcome;
- the rehearsal's evidence;
- the next steps: the `v1.0.1` tag with the user's OK, the candidate run
  with offline authorized, PR 2, and the user publishing the draft;
- every ruling.

There are no UI changes, so no screenshot.

Expected: every job passes. Record the run in the PR description, then
stop for the user's merge decision.

## Traceability

| Spec item | Task |
| --- | --- |
| `-Lane Public` (package name, event prefix) | 1, 2 |
| `-ReleaseSigned`: pinned signer, run-only trust | 1, 3 |
| `-PreviousPackage`, `-RcOnly`, explicit-only group | 2, 4 |
| `rc` stages: previous install, `workflow-build`, upgrade, read, resume, offline, export, restore | 3, 4 |
| Upgrade checks: version, family, library | 4 (ruling 1) |
| Offline: app-scoped rule, no connection, removal | 4, 7 |
| Evidence lines | 5 |
| Tested matrix skeleton, release-note limits | 6 |
| `e2e-testing.md` offline method and `rc` procedure | 6 |
| PR 1 verification: Preview CI unchanged, host rehearsal | 6, 7 |
| Tag, candidate run, PR 2, publish | after this plan |
