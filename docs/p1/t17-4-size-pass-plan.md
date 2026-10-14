# T17.4 PR a (Component Exclusion and Measurement) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Both builds drop the unused Windows App SDK AI, ML, Search,
Widgets and DWrite components, CI fails if one comes back, and every
release records its sizes and portable launch times.

**Architecture:**
- **Exclusion:** direct `ExcludeAssets="all"` references to the six unused
  packages, with the `Microsoft.WindowsAppSDK` metapackage kept.
- **Component check:** `assert_excluded_components.ps1` derives the
  forbidden file names from those packages in the NuGet cache, at the
  versions `packages.lock.json` resolves. It checks a portable bundle list
  (written by a new MSBuild target, since the publish folder holds only
  the exe) or an MSIX, whose `Microsoft.WindowsAppRuntime.2` dependency it
  also checks.
- **Measurement:** `measure_distribution.ps1` runs through
  `start_installed_task.ps1 -Installer` on the interactive desktop. It
  times launches to the library view, and writes `measurements.json`,
  which CI uploads and `release.yml` puts in the release manifest.

**Tech Stack:** MSBuild / NuGet central package management, PowerShell 5.1
and 7, UI Automation, GitHub Actions.

**Spec:** `docs/p1/t17-4-size-pass-design.md` (PR a only; PR b gets its
own plan).

**Target:** T17.4 PR a, on `feat/p1-t17-4-size-pass` (spec commit
`5640ee3`).
**Prerequisites:** T17.1 and T17.2, merged (T17.2's last piece in #104).
#105 (docs: T17.2 complete) is open and independent of this PR.

## Global Constraints

- **Keep the metapackage:** `Microsoft.WindowsAppSDK` 2.5.1 stays a direct
  reference.
- **The six excluded packages, at these versions:**
  `Microsoft.WindowsAppSDK.AI` 2.5.5, `Microsoft.WindowsAppSDK.ML` 2.1.94,
  `Microsoft.WindowsAppSDK.Search` 2.5.5, `Microsoft.WindowsAppSDK.Widgets`
  2.0.5, `Microsoft.WindowsAppSDK.DWrite` 2.1.0 and
  `Microsoft.Windows.AI.MachineLearning` 2.1.74, each with
  `ExcludeAssets="all"`.
- **The MSIX keeps its runtime:** it declares `Microsoft.WindowsAppRuntime.2`
  with a `MinVersion` of 2.5.1.0 or later.
- **No other version changes:** regenerating the lock file changes only
  the six packages' `type` (Transitive to Direct) and their
  `requested` ranges.
- **Ready means:** the main window exists, and UI Automation finds
  `LibraryNavigation` and no `LibraryUnavailable`. (Superseded in the
  final review: ready is `LibraryEmpty`; see the design's ruling 6.)
- **Launch runs:** one untimed first launch, then 3 cold launches (each
  with a new, empty extraction folder) and 3 warm launches (reusing the
  last cold extraction). Each figure is the median.
- **The user's data:** the measurement refuses to run when
  `%LOCALAPPDATA%\DesktopGuides` exists, and removes only the folder it
  created. Extraction goes to a scratch folder through
  `DOTNET_BUNDLE_EXTRACT_BASE_DIR`, never the user's `%TEMP%\.net`.
- **No time limit in PR a:** times are recorded, not checked.
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.

## Rulings against the spec

1. **The component check reads the bundle list, not the publish folder.**
   The spec says the "published folder" must lack the files, but a
   single-file publish holds only the exe. A target runs after the SDK's
   `PrepareForBundle` and writes `@(FilesToBundle)`, the exact bundle
   input, to the path in `BundleFileListPath`, when that property is set.
2. **The forbidden names come from the packages themselves.** Instead of a
   hand-kept list, the check takes every `.dll` and `.winmd` in the six
   packages' NuGet folders. This is exact, and it follows version bumps.
3. **The MSIX payload is checked too.** The ML package's
   `onnxruntime.dll`, `DirectML.dll` and the components' `*.Projection.dll`
   files sit in `lib\` and `runtimes\`, which framework-dependent builds
   also copy. So the MSIX check covers the payload as well as the
   dependency.
4. **Measurement runs in its own CI job,** `portable-measure`, after
   `portable-packages`. That keeps it off the installed shards' critical
   path.

## Review Focus

1. **A component the app needs at runtime turns out to be excluded,** for
   example in text rendering or images. Every scenario group must still
   pass on both builds. Pinned by Task 6's full PR run, all 10 shards in
   both lanes.
2. **The lock file drifts other versions.** Only the six packages may
   change. Pinned by Task 3's lock comparison.
3. **The check passes vacuously:** an empty or wrong list, or a missing
   package folder. It must fail, not report "none found". Pinned by
   Task 1's positive-control, empty-list and missing-folder cases.
4. **A host run with the user's portable data present.** The measurement
   must refuse and leave the folder untouched. Pinned by Task 4's refusal
   test.
5. **The library fails to open in a timed launch.** The measurement must
   fail, not record the time an error page took. Pinned by Task 4's
   `LibraryUnavailable` check, and by Task 5's CI run.

## Host commands

`pcsx2-win` runs the script tests and builds:

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t17-4a) { Remove-Item -Recurse -Force E:\work\desktop-guides\t17-4a }; New-Item -ItemType Directory E:\work\desktop-guides\t17-4a | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t17-4a'
}
```

- **Script tests:** `stage && s 'cd /d E:\work\desktop-guides\t17-4a && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\<test>.ps1'`
- **When the host is unreachable:** use a `dev-fast` dispatch, whose
  `core-tests` runs the same script tests.
- **ASCII check:** `grep -nP '[^\x00-\x7F]' <file>`.
- **No measurement on the host in PR a:** the host has the user's portable
  data folder, so the measurement would refuse. CI measures. PR b's host
  measurement needs the user's go-ahead first.

---
### Task 1: The excluded-component check

**Files:**
- Create: `tools/p1/assert_excluded_components.ps1`
- Test: `tools/p1/test_assert_excluded_components.ps1`
- Modify: `.github/workflows/windows-ci.yml` (`core-tests`, after "Check
  portable release packaging")

**Interfaces:**
- Produces: `assert_excluded_components.ps1 -FileList <path>` or
  `-Msix <path>`, with optional `-LockFile`, `-PackageRoot` and
  `-MinimumRuntime` (default `2.5.1.0`). It throws on failure. On success
  it prints one line starting `Excluded components: none`.

- [ ] **Step 1: Write the failing test**

`tools/p1/test_assert_excluded_components.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'assert_excluded_components.ps1'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

# Returns the error text, or fails when the action succeeds.
function Assert-Rejected([scriptblock] $Action, [string] $Expected, [string] $Message) {
    try { & $Action | Out-Null } catch {
        Assert-True ("$_" -like "*$Expected*") "$Message Got: $_"
        return
    }
    throw "$Message It was accepted."
}

$excluded = [ordered]@{
    'Microsoft.WindowsAppSDK.AI' = '2.5.5'
    'Microsoft.WindowsAppSDK.ML' = '2.1.94'
    'Microsoft.WindowsAppSDK.Search' = '2.5.5'
    'Microsoft.WindowsAppSDK.Widgets' = '2.0.5'
    'Microsoft.WindowsAppSDK.DWrite' = '2.1.0'
    'Microsoft.Windows.AI.MachineLearning' = '2.1.74'
}

function New-Lock([string] $Path, [string[]] $Skip = @()) {
    $framework = [ordered]@{
        'Microsoft.WindowsAppSDK' = [ordered]@{ type = 'Direct'; resolved = '2.5.1' }
    }
    foreach ($id in $excluded.Keys) {
        if ($id -in $Skip) { continue }
        $framework[$id] = [ordered]@{ type = 'Transitive'; resolved = $excluded[$id] }
    }
    $lock = [ordered]@{ version = 1; dependencies = [ordered]@{
        'net10.0-windows10.0.19041' = $framework
        'net10.0-windows10.0.19041/win-x64' = [ordered]@{}
    } }
    [IO.File]::WriteAllText($Path, ($lock | ConvertTo-Json -Depth 5))
}

function New-PackageFile([string] $Root, [string] $Id, [string] $Relative) {
    $path = Join-Path $Root (Join-Path $Id.ToLowerInvariant() (Join-Path $excluded[$Id] $Relative))
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    [IO.File]::WriteAllBytes($path, [byte[]](1, 2, 3))
}

function New-Msix([string] $Path, [string[]] $Files, [string] $Dependencies) {
    $manifest = '<?xml version="1.0" encoding="utf-8"?>' +
        '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">' +
        "<Dependencies>$Dependencies</Dependencies></Package>"
    $zip = [IO.Compression.ZipFile]::Open($Path, 'Create')
    try {
        foreach ($name in @('AppxManifest.xml') + $Files) {
            $writer = [IO.StreamWriter]::new($zip.CreateEntry($name).Open())
            try { $writer.Write($(if ($name -eq 'AppxManifest.xml') { $manifest } else { 'x' })) }
            finally { $writer.Dispose() }
        }
    }
    finally { $zip.Dispose() }
}

$runtime = '<PackageDependency Name="Microsoft.WindowsAppRuntime.2" MinVersion="2.5.1.0" Publisher="CN=Microsoft Corporation" />'
$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-components-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $packages = Join-Path $scratch 'packages'
    New-PackageFile $packages 'Microsoft.WindowsAppSDK.AI' 'lib\net6.0-windows10.0.17763.0\Microsoft.Windows.AI.Projection.dll'
    New-PackageFile $packages 'Microsoft.WindowsAppSDK.AI' 'runtimes-framework\win-x64\native\Microsoft.Windows.Workloads.dll'
    New-PackageFile $packages 'Microsoft.WindowsAppSDK.Search' 'metadata\Microsoft.Windows.Search.winmd'
    New-PackageFile $packages 'Microsoft.WindowsAppSDK.Widgets' 'runtimes-framework\win-x64\native\Microsoft.Windows.Widgets.dll'
    New-PackageFile $packages 'Microsoft.WindowsAppSDK.DWrite' 'runtimes-framework\win-x64\native\DWriteCore.dll'
    New-PackageFile $packages 'Microsoft.Windows.AI.MachineLearning' 'runtimes\win-x64\native\onnxruntime.dll'
    New-PackageFile $packages 'Microsoft.Windows.AI.MachineLearning' 'build\readme.txt'
    # The ML package holds build files only; its folder must still exist.
    New-Item -ItemType Directory -Force -Path (Join-Path $packages 'microsoft.windowsappsdk.ml\2.1.94\build') | Out-Null
    $lock = Join-Path $scratch 'packages.lock.json'
    New-Lock $lock
    $common = @{ LockFile = $lock; PackageRoot = $packages }

    # A portable bundle list: clean, then with excluded files.
    $clean = Join-Path $scratch 'clean.txt'
    Set-Content -LiteralPath $clean -Value @('Microsoft.WinUI.dll', 'Microsoft.ui.xaml.dll', 'DesktopGuides.Production.dll')
    $output = & $checker -FileList $clean @common
    Assert-True ("$output" -like 'Excluded components: none*') "A clean list should pass, got: $output"

    $dirty = Join-Path $scratch 'dirty.txt'
    Set-Content -LiteralPath $dirty -Value @('Microsoft.WinUI.dll', 'DWriteCore.dll', 'runtimes\win-x64\native\ONNXRUNTIME.dll')
    Assert-Rejected { & $checker -FileList $dirty @common } 'DWriteCore.dll (Microsoft.WindowsAppSDK.DWrite)' `
        'A list holding DWriteCore.dll should fail, naming the file and its package.'
    Assert-Rejected { & $checker -FileList $dirty @common } 'ONNXRUNTIME.dll (Microsoft.Windows.AI.MachineLearning)' `
        'Names should match without regard to case or folder.'

    # Positive control: a list without WinUI isn't a build of this app.
    $empty = Join-Path $scratch 'empty.txt'
    Set-Content -LiteralPath $empty -Value @('DesktopGuides.Production.dll')
    Assert-Rejected { & $checker -FileList $empty @common } 'Microsoft.WinUI.dll' `
        'A list without Microsoft.WinUI.dll should fail.'
    $blank = Join-Path $scratch 'blank.txt'
    Set-Content -LiteralPath $blank -Value @('')
    Assert-Rejected { & $checker -FileList $blank @common } 'lists no files' 'An empty list should fail.'

    # The lock file and the package cache must both know every package.
    $partial = Join-Path $scratch 'partial.lock.json'
    New-Lock $partial -Skip 'Microsoft.WindowsAppSDK.Widgets'
    Assert-Rejected { & $checker -FileList $clean -LockFile $partial -PackageRoot $packages } `
        'has no Microsoft.WindowsAppSDK.Widgets' 'A lock file missing a package should fail.'
    Assert-Rejected { & $checker -FileList $clean -LockFile $lock -PackageRoot (Join-Path $scratch 'none') } `
        'Package folder not found' 'A missing package folder should fail.'

    # An MSIX: its payload and its runtime dependency.
    $good = Join-Path $scratch 'good.msix'
    New-Msix $good @('Microsoft.WinUI.dll', 'DesktopGuides.Production.dll') $runtime
    $output = & $checker -Msix $good @common
    Assert-True ("$output" -like 'Excluded components: none*') "A clean MSIX should pass, got: $output"

    $payload = Join-Path $scratch 'payload.msix'
    New-Msix $payload @('Microsoft.WinUI.dll', 'Microsoft.Windows.AI.Projection.dll') $runtime
    Assert-Rejected { & $checker -Msix $payload @common } 'Microsoft.Windows.AI.Projection.dll' `
        'An MSIX holding a projection of an excluded component should fail.'

    $old = Join-Path $scratch 'old.msix'
    New-Msix $old @('Microsoft.WinUI.dll') $runtime.Replace('2.5.1.0', '2.4.9.0')
    Assert-Rejected { & $checker -Msix $old @common } 'below 2.5.1.0' 'An older runtime minimum should fail.'

    $none = Join-Path $scratch 'none.msix'
    New-Msix $none @('Microsoft.WinUI.dll') ''
    Assert-Rejected { & $checker -Msix $none @common } 'Microsoft.WindowsAppRuntime.2' `
        'An MSIX without the runtime dependency should fail.'

    Assert-Rejected { & $checker @common } 'either -FileList or -Msix' 'No input should fail.'
    Assert-Rejected { & $checker -FileList $clean -Msix $good @common } 'either -FileList or -Msix' `
        'Two inputs should fail.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'assert_excluded_components: all cases passed.'
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-4a && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_assert_excluded_components.ps1'`
Expected: FAIL, because `assert_excluded_components.ps1` doesn't exist
("is not recognized" or "not found").

- [ ] **Step 3: Write the check**

`tools/p1/assert_excluded_components.ps1`:

```powershell
# T17.4: fails when a build carries a Windows App SDK component the app
# doesn't use. The forbidden file names are the excluded packages' own
# .dll and .winmd files, at the versions packages.lock.json resolves.
param(
    # A portable bundle list (BundleFileListPath), one relative path a line.
    [string] $FileList,
    [string] $Msix,
    [string] $LockFile = (Join-Path $PSScriptRoot '..\..\src\DesktopGuides.Production\packages.lock.json'),
    [string] $PackageRoot,
    [string] $MinimumRuntime = '2.5.1.0'
)
$ErrorActionPreference = 'Stop'
$excluded = @('Microsoft.WindowsAppSDK.AI', 'Microsoft.WindowsAppSDK.ML',
    'Microsoft.WindowsAppSDK.Search', 'Microsoft.WindowsAppSDK.Widgets',
    'Microsoft.WindowsAppSDK.DWrite', 'Microsoft.Windows.AI.MachineLearning')
# Every build of the app carries the WinUI projection; a list without it
# isn't this app's, and would pass for the wrong reason.
$control = 'microsoft.winui.dll'

if ([bool] $FileList -eq [bool] $Msix) { throw 'Pass either -FileList or -Msix.' }
if (-not $PackageRoot) {
    $PackageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES }
        else { Join-Path $env:USERPROFILE '.nuget\packages' }
}

# The framework section (no RID) holds every package's resolved version.
$lock = Get-Content -LiteralPath $LockFile -Raw | ConvertFrom-Json
$framework = @($lock.dependencies.PSObject.Properties | Where-Object { $_.Name -notmatch '/' })[0].Value
$forbidden = @{}
foreach ($id in $excluded) {
    $entry = $framework.PSObject.Properties[$id]
    if (-not $entry) { throw "packages.lock.json has no $id; the check would prove nothing." }
    $folder = Join-Path $PackageRoot (Join-Path $id.ToLowerInvariant() $entry.Value.resolved)
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
        throw "Package folder not found: $folder. Restore first."
    }
    Get-ChildItem -LiteralPath $folder -Recurse -File |
        Where-Object { $_.Extension -in @('.dll', '.winmd') } |
        ForEach-Object { $forbidden[$_.Name.ToLowerInvariant()] = $id }
}
if ($forbidden.Count -eq 0) { throw 'The excluded packages hold no .dll or .winmd files.' }

if ($FileList) {
    $names = @(Get-Content -LiteralPath $FileList | ForEach-Object { $_.Trim() } |
        Where-Object { $_ } | ForEach-Object { Split-Path $_ -Leaf })
    if ($names.Count -eq 0) { throw "$FileList lists no files." }
}
else {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Msix).Path)
    try {
        $names = @($zip.Entries | Where-Object { $_.Name } |
            ForEach-Object { [Uri]::UnescapeDataString($_.Name) })
        $entry = $zip.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw "$Msix has no AppxManifest.xml." }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
    $ns = @{ m = 'http://schemas.microsoft.com/appx/manifest/foundation/windows10' }
    $runtime = @(Select-Xml -Xml $manifest -Namespace $ns `
        -XPath "//m:Dependencies/m:PackageDependency[@Name='Microsoft.WindowsAppRuntime.2']")
    if ($runtime.Count -ne 1) {
        throw "Expected one Microsoft.WindowsAppRuntime.2 dependency in $Msix, found $($runtime.Count)."
    }
    $minimum = $runtime[0].Node.GetAttribute('MinVersion')
    if ([version] $minimum -lt [version] $MinimumRuntime) {
        throw "The Microsoft.WindowsAppRuntime.2 minimum $minimum is below $MinimumRuntime."
    }
}

if (-not ($names | Where-Object { $_.ToLowerInvariant() -eq $control })) {
    throw "The build lists no Microsoft.WinUI.dll; it isn't a build of this app."
}
$found = @($names | Where-Object { $forbidden.ContainsKey($_.ToLowerInvariant()) } | Sort-Object -Unique)
if ($found.Count -gt 0) {
    $list = ($found | ForEach-Object { "$_ ($($forbidden[$_.ToLowerInvariant()]))" }) -join ', '
    throw "Excluded Windows App SDK components are in the build: $list"
}
Write-Output "Excluded components: none of $($forbidden.Count) file names among $($names.Count) entries."
```

- [ ] **Step 4: Run the test to verify it passes**

Run: the Step 2 command.
Expected: `assert_excluded_components: all cases passed.` Run it under
`pwsh` too (the same command with `pwsh` in place of `powershell`).
Expected: the same line.

- [ ] **Step 5: Add the test to `core-tests` and check ASCII**

In `.github/workflows/windows-ci.yml`, after the "Check portable release
packaging" step:

```yaml
      - name: Check the excluded-component check
        shell: pwsh
        run: ./tools/p1/test_assert_excluded_components.ps1
```

Run: `grep -nP '[^\x00-\x7F]' tools/p1/assert_excluded_components.ps1 tools/p1/test_assert_excluded_components.ps1`
Expected: no output.

- [ ] **Step 6: Commit**

```bash
git add tools/p1/assert_excluded_components.ps1 tools/p1/test_assert_excluded_components.ps1 .github/workflows/windows-ci.yml
git commit -m "test(p1): add the T17.4 excluded-component check"
```

---

### Task 2: Run the check on every build, and watch it fail

**Files:**
- Modify: `src/DesktopGuides.Production/DesktopGuides.Production.csproj`
  (a target after the Portable PropertyGroup)
- Modify: `.github/workflows/windows-ci.yml` (`production-packages`'
  "Build unsigned production package" and "Build and verify the public
  lane" steps; `portable-packages`' "Build and zip the
  portable exe")
- Modify: `.github/workflows/release.yml` ("Build the portable release",
  plus a new step after it)

**Interfaces:**
- Consumes: Task 1's `assert_excluded_components.ps1`.
- Produces: the MSBuild property `BundleFileListPath`. When it's set, a
  portable publish writes one bundled file's relative path a line to that
  path.

- [ ] **Step 1: Add the bundle-list target**

In `DesktopGuides.Production.csproj`, after the Portable PropertyGroup:

```xml
  <!-- T17.4: the publish folder holds only the exe, so the excluded-component
       check reads the bundle's own input list, written when asked for. -->
  <Target Name="WriteBundleFileList" AfterTargets="PrepareForBundle"
          Condition="'$(BundleFileListPath)' != ''">
    <WriteLinesToFile File="$(BundleFileListPath)"
                      Lines="@(FilesToBundle->'%(RelativePath)')"
                      Overwrite="true" />
  </Target>
```

- [ ] **Step 2: Watch the check fail on today's builds, on the host**

```bash
stage
s 'cd /d E:\work\desktop-guides\t17-4a && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 -p:Portable=true && dotnet publish src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release --no-restore -p:Platform=x64 -p:Portable=true -p:PublishDir=E:\work\desktop-guides\t17-4a\artifacts\p\ -p:BundleFileListPath=E:\work\desktop-guides\t17-4a\artifacts\bundle-files.txt -nologo'
s 'cd /d E:\work\desktop-guides\t17-4a && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\assert_excluded_components.ps1 -FileList artifacts\bundle-files.txt'
```

Expected:
- the publish succeeds, and `artifacts\bundle-files.txt` lists about 551
  files, including `Microsoft.WinUI.dll`;
- the check FAILS with "Excluded Windows App SDK components are in the
  build:", naming at least `DWriteCore.dll`, `onnxruntime.dll` and
  `DirectML.dll`.

If the list is empty or lacks the Windows App SDK's native files,
`FilesToBundle` isn't the full bundle input on this SDK. Read
`Microsoft.NET.Publish.targets` (the `PrepareForBundle` target), use the
item it bundles, and ledger the ruling.

Then the MSIX:

```bash
s 'cd /d E:\work\desktop-guides\t17-4a && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release --no-restore -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -nologo'
s 'cd /d E:\work\desktop-guides\t17-4a && powershell -NoProfile -Command "$m = Get-ChildItem src\DesktopGuides.Production\AppPackages -Recurse -Filter DesktopGuides.Production_*_x64.msix | Select-Object -First 1; & .\tools\p1\assert_excluded_components.ps1 -Msix $m.FullName"'
```

Expected: FAIL, naming the projection DLLs (for example
`Microsoft.Windows.AI.Projection.dll`) and `onnxruntime.dll`. If the MSIX
check passes here, the MSIX payload never held the components. Ledger
that, and keep the MSIX check as a guard: its dependency check still
applies.

Record both failure lines for the verification record.

- [ ] **Step 3: Run the check in CI**

In `windows-ci.yml` `production-packages`, in "Build unsigned production
package", after the `verify_production_package.py` call and
its `$LASTEXITCODE` check:

```powershell
          ./tools/p1/assert_excluded_components.ps1 -Msix $packages[0].FullName
```

In "Build and verify the public lane", replace the final
`exit $LASTEXITCODE` with:

```powershell
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          ./tools/p1/assert_excluded_components.ps1 -Msix $packages[0].FullName
```

In `portable-packages`' "Build and zip the portable exe", add the list
property to the publish, and run the check before zipping:

```powershell
          $publish = "$PWD/artifacts/portable-ci-publish"
          dotnet publish src/DesktopGuides.Production/DesktopGuides.Production.csproj `
            -c Release --no-restore -p:Platform=x64 -p:Portable=true `
            -p:PublishDir=$publish/ -p:BundleFileListPath=$PWD/artifacts/portable-bundle-files.txt
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          ./tools/p1/assert_excluded_components.ps1 -FileList artifacts/portable-bundle-files.txt
          ./tools/p1/package_portable_release.ps1 -Commit $env:GITHUB_SHA `
            -PublishDirectory $publish -OutputDirectory artifacts/portable-ci
```

`dev-portable-packages` reuses these steps through the
`&portable-packages-steps` anchor, so it gets the check too.

- [ ] **Step 4: Run the check in the release**

In `release.yml` "Build the portable release", add
`-p:BundleFileListPath=$PWD/artifacts/portable-release-bundle-files.txt`
to the `dotnet publish` call. Then add a step after it:

```yaml
      - name: Check the release builds for excluded components
        shell: pwsh
        run: |
          ./tools/p1/assert_excluded_components.ps1 -FileList artifacts/portable-release-bundle-files.txt
          ./tools/p1/assert_excluded_components.ps1 -Msix "artifacts/release/DesktopGuides-$env:VERSION-x64.msix"
```

- [ ] **Step 5: Commit**

The check now fails CI until Task 3 lands. That's intended: Task 3
follows before any push.

```bash
git add src/DesktopGuides.Production/DesktopGuides.Production.csproj .github/workflows/windows-ci.yml .github/workflows/release.yml
git commit -m "build(p1): check every build for excluded Windows App SDK components"
```

---

### Task 3: Exclude the unused components

**Files:**
- Modify: `Directory.Packages.props` (after the `Microsoft.WindowsAppSDK`
  line)
- Modify: `src/DesktopGuides.Production/DesktopGuides.Production.csproj`
  (the PackageReference ItemGroup)
- Modify: `src/DesktopGuides.Production/packages.lock.json` (regenerated)

**Interfaces:**
- Consumes: Task 2's `BundleFileListPath` and the Task 1 check.
- Produces: builds without the six components, which Tasks 5 and 6
  measure and test.

- [ ] **Step 1: Keep the current lock file for the comparison**

Run: `cp src/DesktopGuides.Production/packages.lock.json /tmp/t17-4-lock-before.json`

- [ ] **Step 2: Add the central versions**

In `Directory.Packages.props`, after the `Microsoft.WindowsAppSDK` line:

```xml
    <!-- T17.4: excluded from the app (ExcludeAssets="all"); the metapackage
         still brings them, and the Community Toolkit needs the metapackage. -->
    <PackageVersion Include="Microsoft.WindowsAppSDK.AI" Version="2.5.5" />
    <PackageVersion Include="Microsoft.WindowsAppSDK.ML" Version="2.1.94" />
    <PackageVersion Include="Microsoft.WindowsAppSDK.Search" Version="2.5.5" />
    <PackageVersion Include="Microsoft.WindowsAppSDK.Widgets" Version="2.0.5" />
    <PackageVersion Include="Microsoft.WindowsAppSDK.DWrite" Version="2.1.0" />
    <PackageVersion Include="Microsoft.Windows.AI.MachineLearning" Version="2.1.74" />
```

- [ ] **Step 3: Add the excluding references**

In `DesktopGuides.Production.csproj`, after
`<PackageReference Include="Microsoft.WindowsAppSDK" />`:

```xml
    <!-- T17.4: the app uses only Foundation, WinUI and WebView2, and the
         system DWrite.dll. Referencing only those components made NuGet
         resolve the Toolkit's metapackage at 1.6, which collides with the
         2.x components, so the unused ones are excluded instead. -->
    <PackageReference Include="Microsoft.WindowsAppSDK.AI" ExcludeAssets="all" />
    <PackageReference Include="Microsoft.WindowsAppSDK.ML" ExcludeAssets="all" />
    <PackageReference Include="Microsoft.WindowsAppSDK.Search" ExcludeAssets="all" />
    <PackageReference Include="Microsoft.WindowsAppSDK.Widgets" ExcludeAssets="all" />
    <PackageReference Include="Microsoft.WindowsAppSDK.DWrite" ExcludeAssets="all" />
    <PackageReference Include="Microsoft.Windows.AI.MachineLearning" ExcludeAssets="all" />
```

- [ ] **Step 4: Regenerate the lock file on the host and copy it back**

```bash
stage
s 'cd /d E:\work\desktop-guides\t17-4a && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --force-evaluate -p:Platform=x64 -p:Portable=true -nologo'
s 'type E:\work\desktop-guides\t17-4a\src\DesktopGuides.Production\packages.lock.json' > src/DesktopGuides.Production/packages.lock.json
```

Expected: the restore succeeds. Check the local file has the host's line
endings converted to match the repo (`git diff --stat` shows only the
lock file's real changes). If `type` output has CRLF and the repo stores
LF, run `perl -pi -e 's/\r$//' src/DesktopGuides.Production/packages.lock.json`.

- [ ] **Step 5: Compare the lock files**

```bash
python3 - <<'EOF'
import json
before = json.load(open('/tmp/t17-4-lock-before.json'))['dependencies']
after = json.load(open('src/DesktopGuides.Production/packages.lock.json'))['dependencies']
six = {'Microsoft.WindowsAppSDK.AI', 'Microsoft.WindowsAppSDK.ML', 'Microsoft.WindowsAppSDK.Search',
       'Microsoft.WindowsAppSDK.Widgets', 'Microsoft.WindowsAppSDK.DWrite', 'Microsoft.Windows.AI.MachineLearning'}
bad = []
for tfm in before.keys() | after.keys():
    b, a = before.get(tfm, {}), after.get(tfm, {})
    for name in b.keys() | a.keys():
        rb, ra = b.get(name, {}).get('resolved'), a.get(name, {}).get('resolved')
        if rb != ra:
            bad.append(f'{tfm} {name}: {rb} -> {ra}')
        elif name not in six and b.get(name) != a.get(name):
            bad.append(f'{tfm} {name}: entry changed')
print('\n'.join(bad) or 'only the six packages changed, and no resolved version moved')
EOF
```

Expected: `only the six packages changed, and no resolved version moved`.
Anything else is a finding: stop and investigate before going on.

- [ ] **Step 6: Run the check on both builds again, and watch it pass**

```bash
stage
s 'cd /d E:\work\desktop-guides\t17-4a && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 -p:Portable=true && dotnet publish src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release --no-restore -p:Platform=x64 -p:Portable=true -p:PublishDir=E:\work\desktop-guides\t17-4a\artifacts\p\ -p:BundleFileListPath=E:\work\desktop-guides\t17-4a\artifacts\bundle-files.txt -nologo'
s 'cd /d E:\work\desktop-guides\t17-4a && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\assert_excluded_components.ps1 -FileList artifacts\bundle-files.txt'
s 'cd /d E:\work\desktop-guides\t17-4a && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release --no-restore -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -nologo'
s 'cd /d E:\work\desktop-guides\t17-4a && powershell -NoProfile -Command "$m = Get-ChildItem src\DesktopGuides.Production\AppPackages -Recurse -Filter DesktopGuides.Production_*_x64.msix | Select-Object -First 1; & .\tools\p1\assert_excluded_components.ps1 -Msix $m.FullName; $m.Length"'
```

Expected:
- both locked restores succeed, so the MSIX and portable builds share the
  regenerated lock file;
- the publish and the build report `0 Warning(s)`;
- the bundle list has about 485 files;
- both checks print `Excluded components: none ...`;
- the MSIX size prints.

Record the exe size (`artifacts\p\DesktopGuides.Production.exe`), the
bundle file count and the MSIX size for the verification record.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props src/DesktopGuides.Production/DesktopGuides.Production.csproj src/DesktopGuides.Production/packages.lock.json
git commit -m "build(p1): exclude the unused Windows App SDK AI, ML, Search, Widgets and DWrite components"
```

---

### Task 4: The measurement scripts

**Files:**
- Create: `tools/p1/windows_distribution_measure.ps1` (helpers)
- Create: `tools/p1/measure_distribution.ps1` (the driver)
- Test: `tools/p1/test_windows_distribution_measure.ps1`
- Modify: `.github/workflows/windows-ci.yml` (`core-tests`, after Task 1's
  step)

**Interfaces:**
- Produces:
  - `Get-MedianMilliseconds([double[]] $Values)` returns `[long]`;
  - `Get-FolderFootprint([string] $Path)` returns `[ordered]@{ files; bytes }`;
  - `New-LaunchSummary([double[]] $Runs)` returns `[ordered]@{ medianMs; runsMs }`;
  - `measure_distribution.ps1 -PortableExecutable <exe> -ResultDirectory <dir>
    [-Runs 3] [-ReadyTimeoutSeconds 60]` writes
    `<dir>\measurements.json`, holding `executable`, `executableBytes`,
    `extraction { files, bytes }`, `coldLaunch { medianMs, runsMs }`,
    `warmLaunch { medianMs, runsMs }`, `readyWhen`, `osBuild`,
    `processor` and `measuredAt`.

- [ ] **Step 1: Write the failing test**

`tools/p1/test_windows_distribution_measure.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_distribution_measure.ps1')
$driver = Join-Path $PSScriptRoot 'measure_distribution.ps1'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

Assert-True ((Get-MedianMilliseconds @(900, 100, 500)) -eq 500) 'The median of three is the middle value.'
Assert-True ((Get-MedianMilliseconds @(400, 100, 300, 200)) -eq 250) 'The median of four averages the middle two.'
Assert-True ((Get-MedianMilliseconds @(1499.6)) -eq 1500) 'Medians round to whole milliseconds.'
$rejected = $false
try { Get-MedianMilliseconds @() | Out-Null } catch { $rejected = $true }
Assert-True $rejected 'No launch times should fail.'

$summary = New-LaunchSummary @(1210.4, 1190.6, 1500.2)
Assert-True ($summary.medianMs -eq 1210) "Unexpected median: $($summary.medianMs)"
Assert-True ((@($summary.runsMs) -join ',') -eq '1210,1191,1500') "Runs keep their order: $(@($summary.runsMs) -join ',')"

$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-measure-test-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path (Join-Path $scratch 'tree\sub') | Out-Null
$saved = $env:LOCALAPPDATA
try {
    [IO.File]::WriteAllBytes((Join-Path $scratch 'tree\a.bin'), [byte[]](1..10))
    [IO.File]::WriteAllBytes((Join-Path $scratch 'tree\sub\b.bin'), [byte[]](1..5))
    $footprint = Get-FolderFootprint (Join-Path $scratch 'tree')
    Assert-True ($footprint.files -eq 2 -and $footprint.bytes -eq 15) "Unexpected footprint: $($footprint.files) files, $($footprint.bytes) bytes"
    $rejected = $false
    try { Get-FolderFootprint (Join-Path $scratch 'absent') | Out-Null } catch { $rejected = $true }
    Assert-True $rejected 'A missing extraction folder should fail.'

    # The driver must refuse, and touch nothing, when portable data exists.
    $local = Join-Path $scratch 'local'
    $data = Join-Path $local 'DesktopGuides'
    New-Item -ItemType Directory -Path $data | Out-Null
    [IO.File]::WriteAllText((Join-Path $data 'library.db'), 'user data')
    $exe = Join-Path $scratch 'DesktopGuides.Production.exe'
    [IO.File]::WriteAllBytes($exe, [byte[]](1..3))
    $env:LOCALAPPDATA = $local
    $refusal = $null
    try { & $driver -PortableExecutable $exe -ResultDirectory (Join-Path $scratch 'out') | Out-Null }
    catch { $refusal = "$_" }
    Assert-True ($refusal -like '*portable DesktopGuides data folder already exists*') "Expected a refusal, got: $refusal"
    Assert-True ((Get-Content -LiteralPath (Join-Path $data 'library.db') -Raw) -eq 'user data') 'The user data must stay untouched.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $scratch 'out\measurements.json'))) 'A refused run writes no measurements.'
}
finally {
    $env:LOCALAPPDATA = $saved
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'windows_distribution_measure: all cases passed.'
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-4a && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_distribution_measure.ps1'`
Expected: FAIL, because `windows_distribution_measure.ps1` doesn't exist.

- [ ] **Step 3: Write the helpers**

`tools/p1/windows_distribution_measure.ps1`:

```powershell
# T17.4: the arithmetic behind measure_distribution.ps1.

function Get-MedianMilliseconds([double[]] $Values) {
    if (-not $Values -or $Values.Count -eq 0) { throw 'No launch times to summarise.' }
    $sorted = @($Values | Sort-Object)
    $middle = [int] [Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2 -eq 1) { $sorted[$middle] }
        else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    return [long] [Math]::Round($median, [MidpointRounding]::AwayFromZero)
}

function Get-FolderFootprint([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Extraction folder not found: $Path"
    }
    $files = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force)
    $bytes = 0L
    foreach ($file in $files) { $bytes += $file.Length }
    return [ordered]@{ files = $files.Count; bytes = $bytes }
}

function New-LaunchSummary([double[]] $Runs) {
    return [ordered]@{
        medianMs = Get-MedianMilliseconds $Runs
        runsMs = @($Runs | ForEach-Object { [long] [Math]::Round($_, [MidpointRounding]::AwayFromZero) })
    }
}
```

- [ ] **Step 4: Write the driver**

`tools/p1/measure_distribution.ps1`:

```powershell
# T17.4: measures the portable exe: its size, what its first launch extracts,
# and cold and warm launch times to the library view. Run it through
# start_installed_task.ps1 -Installer, which gives it the interactive desktop.
param(
    [Parameter(Mandatory = $true)]
    [string] $PortableExecutable,
    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory,
    [ValidateRange(1, 9)]
    [int] $Runs = 3,
    [int] $ReadyTimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_profile.ps1')
. (Join-Path $PSScriptRoot 'windows_distribution_measure.ps1')

$PortableExecutable = (Resolve-Path -LiteralPath $PortableExecutable).Path
# The run owns the data folder only if it starts absent.
Assert-FreshPortableProfile $env:LOCALAPPDATA
$sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
if ($sessionId -eq 0 -or -not @(Get-Process explorer -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $sessionId })) {
    throw 'Measuring launches requires an interactive desktop session.'
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$automation = [System.Windows.Automation.AutomationElement]
$descendants = [System.Windows.Automation.TreeScope]::Descendants
$library = [System.Windows.Automation.PropertyCondition]::new($automation::AutomationIdProperty, 'LibraryNavigation')
$unavailable = [System.Windows.Automation.PropertyCondition]::new($automation::AutomationIdProperty, 'LibraryUnavailable')

function Stop-MeasuredApp([Diagnostics.Process] $Process) {
    if ($Process.HasExited) { return }
    [void] $Process.CloseMainWindow()
    if (-not $Process.WaitForExit(20000)) {
        $Process.Kill()
        [void] $Process.WaitForExit(10000)
        throw 'The app did not close within 20 s, so it was killed.'
    }
}

# Milliseconds from start until the library view shows.
function Measure-Launch([string] $ExtractBase) {
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $ExtractBase
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $PortableExecutable -PassThru
    try {
        while ($true) {
            if ($process.HasExited) { throw "The app exited during launch, with code $($process.ExitCode)." }
            $process.Refresh()
            if ($process.MainWindowHandle -ne 0) {
                $root = $null
                try { $root = $automation::FromHandle($process.MainWindowHandle) } catch { }
                if ($root -and $root.FindFirst($descendants, $library)) {
                    $elapsed = $clock.Elapsed.TotalMilliseconds
                    if ($root.FindFirst($descendants, $unavailable)) {
                        throw 'The library could not open, so the launch time means nothing.'
                    }
                    return $elapsed
                }
            }
            if ($clock.Elapsed.TotalSeconds -gt $ReadyTimeoutSeconds) {
                throw "The library view did not appear within $ReadyTimeoutSeconds s."
            }
            Start-Sleep -Milliseconds 50
        }
    }
    finally {
        Stop-MeasuredApp $process
        Remove-Item Env:\DOTNET_BUNDLE_EXTRACT_BASE_DIR -ErrorAction SilentlyContinue
    }
}

New-Item -ItemType Directory -Force -Path $ResultDirectory | Out-Null
$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-measure-$([Guid]::NewGuid().ToString('N'))"
$dataFolder = Join-Path $env:LOCALAPPDATA 'DesktopGuides'
try {
    # The untimed first launch creates the data folder, so every timed
    # launch opens the same empty library.
    $first = Join-Path $scratch 'first'
    [void] (Measure-Launch $first)
    $extraction = Get-FolderFootprint $first
    $cold = @(for ($i = 1; $i -le $Runs; $i++) { Measure-Launch (Join-Path $scratch "cold-$i") })
    $warmBase = Join-Path $scratch "cold-$Runs"
    $warm = @(for ($i = 1; $i -le $Runs; $i++) { Measure-Launch $warmBase })
    $exe = Get-Item -LiteralPath $PortableExecutable
    $result = [ordered]@{
        executable = $exe.Name
        executableBytes = $exe.Length
        extraction = $extraction
        coldLaunch = New-LaunchSummary $cold
        warmLaunch = New-LaunchSummary $warm
        readyWhen = 'the main window shows and UI Automation finds LibraryNavigation'
        osBuild = [Environment]::OSVersion.Version.ToString()
        processor = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
        measuredAt = (Get-Date).ToUniversalTime().ToString('o')
    }
    [IO.File]::WriteAllText((Join-Path $ResultDirectory 'measurements.json'),
        ($result | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
    Write-Output ("Measured {0}: {1} bytes; extraction {2} files, {3} bytes; cold {4} ms; warm {5} ms" -f
        $exe.Name, $exe.Length, $extraction.files, $extraction.bytes,
        $result.coldLaunch.medianMs, $result.warmLaunch.medianMs)
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    # Absent before the first launch, so it is this run's own. WebView2 may
    # hold it for a moment after the app exits.
    for ($try = 0; $try -lt 20 -and (Test-Path -LiteralPath $dataFolder); $try++) {
        Remove-Item -LiteralPath $dataFolder -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $dataFolder) { Start-Sleep -Milliseconds 500 }
    }
    if (Test-Path -LiteralPath $dataFolder) {
        Write-Output "measure_distribution: could not remove $dataFolder; remove it by hand."
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: the Step 2 command, then the same with `pwsh`.
Expected: `windows_distribution_measure: all cases passed.` both times.

Also run the parse check: `s 'cd /d E:\work\desktop-guides\t17-4a && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_scripts_parse.ps1'`
Expected: `windows_scripts_parse: all scripts passed.`

- [ ] **Step 6: Add the test to `core-tests` and check ASCII**

After Task 1's `core-tests` step:

```yaml
      - name: Check the distribution measurement
        shell: pwsh
        run: ./tools/p1/test_windows_distribution_measure.ps1
```

Run: `grep -nP '[^\x00-\x7F]' tools/p1/windows_distribution_measure.ps1 tools/p1/measure_distribution.ps1 tools/p1/test_windows_distribution_measure.ps1`
Expected: no output.

- [ ] **Step 7: Commit**

```bash
git add tools/p1/windows_distribution_measure.ps1 tools/p1/measure_distribution.ps1 tools/p1/test_windows_distribution_measure.ps1 .github/workflows/windows-ci.yml
git commit -m "test(p1): add the T17.4 portable size and launch measurement"
```

---

### Task 5: Measure in CI and in every release

**Files:**
- Modify: `.github/workflows/windows-ci.yml` (a new `portable-measure` job
  after `portable-packages`)
- Modify: `.github/workflows/release.yml` (a measurement step, and the
  manifest)
- Modify: `docs/p1/release-packaging.md` (the manifest description)

**Interfaces:**
- Consumes: Task 4's `measure_distribution.ps1` and its
  `measurements.json`; the `desktop-guides-portable-x64` artifact.
- Produces: the CI artifact `desktop-guides-portable-measurements`, and
  the release manifest's
  `measurements = { msixBytes, portable = <measurements.json> }`.

- [ ] **Step 1: Add the `portable-measure` job**

In `windows-ci.yml`, after the `portable-packages` job (before
`public-upgrade`):

```yaml
  # T17.4: the portable exe's size, extraction and launch times. A job of
  # its own, so the installed shards don't wait for it.
  portable-measure:
    needs: portable-packages
    runs-on: windows-2025
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
      - uses: actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4.3.0
        with:
          name: desktop-guides-portable-x64
          path: artifacts/portable-measure/download
      - name: Measure the portable exe
        shell: pwsh
        run: |
          $zips = @(Get-ChildItem artifacts/portable-measure/download -Filter 'DesktopGuides-portable-x64-*.zip')
          if ($zips.Count -ne 1) { throw "Expected one portable zip, found $($zips.Count)." }
          Expand-Archive -LiteralPath $zips[0].FullName -DestinationPath artifacts/portable-measure/exe
          ./tools/p1/start_installed_task.ps1 `
            -PortableExecutable artifacts/portable-measure/exe/DesktopGuides.Production.exe `
            -ResultDirectory artifacts/portable-measure/result `
            -Installer ./tools/p1/measure_distribution.ps1 -TimeoutMinutes 10
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          Get-Content artifacts/portable-measure/result/measurements.json
      - name: Upload the portable measurements
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4.6.2
        with:
          name: desktop-guides-portable-measurements
          path: artifacts/portable-measure/result/measurements.json
          if-no-files-found: error
```

`portable-packages` already skips on a plain `dev-fast` dispatch, and
`needs` skips this job with it.

- [ ] **Step 2: Check the job in CI with a narrow dispatch**

```bash
git push -u origin feat/p1-t17-4-size-pass
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-4-size-pass -f shell-scope=core -f shell-lane=portable
```

Expected:
- `core-tests` passes, with both new script tests;
- `production-packages` and `portable-packages` pass, and their logs show
  `Excluded components: none ...` for each MSIX and the bundle list;
- `portable-measure` passes and prints `measurements.json`: an exe of
  about 180 MB, an extraction of about 485 files, and cold and warm
  medians;
- the `core` portable shard passes.

If `portable-measure` fails, use superpowers:systematic-debugging; read
the job log with `gh api repos/ilya-slalom/desktop-guides/actions/jobs/<id>/logs`.
Record the run ID for the verification record.

- [ ] **Step 3: Measure in the release, and record it in the manifest**

In `release.yml`, after Task 2's "Check the release builds for excluded
components" step:

```yaml
      - name: Measure the portable release
        shell: pwsh
        run: |
          # The public exe, before zipping; the zip holds the same bytes.
          ./tools/p1/start_installed_task.ps1 `
            -PortableExecutable artifacts/portable-release-publish/DesktopGuides.Production.exe `
            -ResultDirectory artifacts/measure -Installer ./tools/p1/measure_distribution.ps1 -TimeoutMinutes 10
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

In "Write the release manifest and notes", add after the `portable =` line:

```powershell
            measurements = [ordered]@{
              msixBytes = $msix.Length
              portable = Get-Content artifacts/measure/measurements.json -Raw | ConvertFrom-Json
            }
```

and change `ConvertTo-Json -Depth 4` to `ConvertTo-Json -Depth 6` on that
manifest.

- [ ] **Step 4: Describe the manifest's new block**

In `docs/p1/release-packaging.md`, after the paragraph ending "these are
not attached to the release.", add:

```markdown
`release-manifest.json` also records `measurements` (T17.4): the MSIX size,
and for the portable exe its size, what its first launch extracts (files
and bytes), and the median cold and warm launch times on the release
runner, from start until the library view shows. Each cold launch starts
with an empty extraction folder; warm launches reuse one. Runner times are
noisier than a desktop's, so compare them with earlier releases, not with
host measurements.
```

- [ ] **Step 5: Commit, push, and run a release dry run**

```bash
git add .github/workflows/windows-ci.yml .github/workflows/release.yml docs/p1/release-packaging.md
git commit -m "ci(p1): measure the portable exe in CI and record sizes and launch times in each release"
git push
gh workflow run release.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-4-size-pass -f tag=v1.0.1
```

Expected: the dry run passes (it builds and signs, and publishes
nothing). Its `release-1.0.1.0` artifact's `release-manifest.json` has a
`measurements` block with `msixBytes` and `portable.coldLaunch.medianMs`.
Check that with:

```bash
gh run download <run-id> --repo ilya-slalom/desktop-guides -n release-1.0.1.0 -D /tmp/t17-4-release
python3 -c "import json; m = json.load(open('/tmp/t17-4-release/release-manifest.json', encoding='utf-8-sig'))['measurements']; print(m['msixBytes'], m['portable']['executableBytes'], m['portable']['extraction'], m['portable']['coldLaunch'], m['portable']['warmLaunch'])"
rm -rf /tmp/t17-4-release
```

The artifact holds a signed package and is public for a day; delete only
the local copy. Record the run ID.

---

### Task 6: Records, the full PR run and the PR

**Files:**
- Modify: `docs/p1/t17-4-size-pass-design.md` (Status; a "PR a
  verification record" section)
- Modify: `docs/p1/results.md` (a "T17.4 PR a" section after the portable
  size spike)
- Modify: `docs/p1/implementation-plan.md` (the status note) and
  `docs/progress.md` (the T17.4 line in "Open:")

- [ ] **Step 1: Write the records**

In the design:
- Set Status to "PR a implemented on `feat/p1-t17-4-size-pass`; PR b
  follows."
- Add a "PR a verification record" section with:
  - Task 2's red lines (portable and MSIX);
  - Task 3's lock comparison result, and its green checks;
  - the Task 5 dispatch and release dry-run IDs;
  - every ledgered ruling.

In `results.md`, after the portable size spike section, add
"## T17.4 PR a: components excluded — <date>". Give it this table, using
the figures from Task 3 (host build) and Task 5 (CI runner):

```markdown
| Build | Exe or MSIX | Extraction | Cold / warm launch | Measured on |
| --- | --- | --- | --- | --- |
| Spike `80d2647` (portable) | 244.0 MB | 551 files, 233.3 MB | 2.2 s / 0.5 s | host |
| Spike V1 (portable) | 181.8 MB | 485 files, 171.2 MB | 1.5 s / 0.5 s | host |
| PR a portable | <exe MB> | <files>, <MB> | <cold> s / <warm> s | CI runner, run <id> |
| PR a MSIX (Preview, x64) | <MSIX MB> | — | — | host build |
```

Under it, say that runner times are a new baseline and aren't comparable
with the host's, and that PR b's launch limit is checked on the host.
Fill each `<...>` from the recorded values; none may stay in the
committed file.

In `implementation-plan.md` and `progress.md`, say T17.4 PR a is in
review and PR b (trimming) follows.

- [ ] **Step 2: Commit and push**

```bash
git add docs
git commit -m "docs(p1): record T17.4 PR a: components excluded, sizes and launch times"
git push
```

- [ ] **Step 3: Open the PR, and wait for its run**

Title: "T17.4 PR a: exclude the unused Windows App SDK components, and
measure each release". The body names:
- the task: T17.4, PR a of two;
- the prerequisites: T17.1 and T17.2, merged;
- the outcome: smaller builds, a component check, and the measurements;
- the size table;
- the red and green check runs and the dry run;
- every ruling.

There are no UI changes, so no screenshot.

Expected: every job passes, including all 10 installed shards on both
lanes (Review Focus 1) and `portable-measure`. Record the run in the PR
description, not with a push: a docs push to an open PR starts a full run.
Then stop for the user's merge decision.

## Traceability

| Spec item (PR a) | Task |
| --- | --- |
| The six `ExcludeAssets="all"` references; the metapackage kept | 3 |
| Lock files regenerated, with no other version change | 3 |
| Component check: portable output, written first | 1, 2 |
| Component check: the MSIX keeps `Microsoft.WindowsAppRuntime.2` 2.5.1.0+ | 1, 2 |
| `measure_distribution.ps1`: sizes, extraction, cold and warm medians of 3 | 4 |
| Launches through the interactive task; a scratch data and extraction folder | 4, 5 |
| `release.yml` writes `measurements` into the manifest | 5 |
| No time limit in PR a | 4, 5 |
| All 10 shards on both lanes; a results.md entry | 6 |
