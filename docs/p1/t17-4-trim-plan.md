# T17.4 PR b1 (Trim-Safe Code and the Warning Gate) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The portable build's code is trim-safe, and CI fails on any new
trim or C#/WinRT warning. Shipping a trimmed exe is PR b2.

**Architecture:**
- **Gate:** a new CI job, `portable-trim-analysis`, publishes the portable
  build trimmed (`TrimMode=partial`, `CsWinRTAotWarningLevel=2`) into a
  scratch folder, purely to collect warnings. `assert_trim_warnings.ps1`
  fails the job on any warning that isn't on
  `tools/p1/trim-warning-allowlist.txt`, and on any allow-list entry that
  no warning needs.
- **Fixes:**
  - C#/WinRT: `partial` types and `AllowUnsafeBlocks`.
  - JSON: every `System.Text.Json` call moves to a source-generated
    context, one per assembly: `CoreJsonContext`,
    `InfrastructureJsonContext` and `ProductionJsonContext`.
  - Anonymous payloads become named records.
- **Third-party warnings:** the allow-list names only the assemblies the
  project doesn't own, each with its reason.

**Tech Stack:** C# / .NET 10, `System.Text.Json` source generation,
C#/WinRT, ILLink, xUnit, PowerShell 5.1 and 7, GitHub Actions.

**Spec:** `docs/p1/t17-4-size-pass-design.md`, PR b sections 1 and 2.
Sections 3 and 4 (the trimmed crash, and shipping a trimmed, compressed
exe) are PR b2, planned once b1 has merged.

**Target:** T17.4 PR b1, on `feat/p1-t17-4-trim`.
**Prerequisites:** T17.4 PR a (#106) and #105, both merged.

## Global Constraints

- **No behaviour change:** every JSON payload the app writes or reads stays
  byte-for-byte the same, including property names, order, nulls, number
  formats and escaping.
- **Stored data:** `GameMetadataJson` must read every snapshot it reads
  today, and reject what it rejects today.
- **Allow-list rules:** an entry names one code and one subject (an
  assembly for IL2104, or a repo-relative source path), with a `#` reason.
  Only assemblies the project doesn't own may appear.
- **The gate's input** is a trimmed publish log containing
  `Optimizing assemblies for size`. Any other log fails.
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.

## Rulings against the spec

1. **The gate reads the publish log, instead of using `WarningsAsErrors`.**
   The spec's allow-list needs "one code for one assembly". MSBuild can't
   suppress IL2104 for a single assembly, and `ILLinkTreatWarningsAsErrors`
   would fail on WinRT.Runtime's own warnings. A script that checks the
   log against an allow-list does what the spec asks.
2. **PR b splits in two.**
   - **b1 (this plan):** sections 1 and 2.
   - **b2:** sections 3 and 4. The crash's root cause decides b2's work,
     and it can't be planned before the fixes land.
3. **Production's JSON is pinned by the installed shards, not unit tests.**
   The Production project has no unit-test project. Its payloads are
   WebView2 script arguments and test-gate diagnostics files, and the
   `html`, `progress` and `workflow` groups read every one of them. Core
   and Infrastructure payloads get exact-string unit tests.
4. **Reflection JSON is switched off in the Core and Infrastructure test
   projects** (`JsonSerializerIsReflectionEnabledByDefault=false`), so a
   missed call fails a test. Test code that builds JSON on purpose uses an
   explicit reflection resolver.

## Review Focus

1. **A stored game's metadata no longer loads,** for example an enum name,
   a date, or a missing `artworkSource`. It must load as before. Pinned by
   Task 5's round-trip and exact-string tests over existing snapshot JSON.
2. **A WebView2 script argument changes,** such as a CSS string's escaping
   or a double's format. Reading position and text size must behave the
   same. Pinned by Task 4's `HtmlReaderStyle` and `FindArgs` tests, and by
   the installed `html` group.
3. **The gate passes vacuously:** a log without trimming, or an
   allow-list that hides a new warning in an assembly the project owns.
   Pinned by Task 1's cases.
4. **A diagnostics file the smoke reads gains or loses a field,** for
   example `hresult` becoming `hResult`. The smoke must still parse it.
   Pinned by Task 6's `[JsonPropertyName("hresult")]`, and by the installed
   `progress` and `html` groups.
5. **A credential blob's bytes change.** Stored credentials must still
   parse. Pinned by Task 5's exact-bytes test.

## Host commands

`pcsx2-win` runs the .NET tests, the publishes and the script tests:

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t17-4b) { Remove-Item -Recurse -Force E:\work\desktop-guides\t17-4b }; New-Item -ItemType Directory E:\work\desktop-guides\t17-4b | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t17-4b'
}
trimlog(){
  s 'cd /d E:\work\desktop-guides\t17-4b && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 -p:Portable=true -nologo && dotnet publish src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release --no-restore -p:Platform=x64 -p:Portable=true -p:PublishTrimmed=true -p:TrimMode=partial -p:CsWinRTAotWarningLevel=2 -p:PublishDir=E:\work\desktop-guides\t17-4b\artifacts\trim\ -nologo "-flp:logfile=E:\work\desktop-guides\t17-4b\artifacts\trim-analysis.log;verbosity=normal"'
}
gate(){ s 'cd /d E:\work\desktop-guides\t17-4b && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\assert_trim_warnings.ps1 -Log artifacts\trim-analysis.log'; }
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t17-4b && dotnet test tests\DesktopGuides.Core.Tests -nologo'`
- **Infrastructure tests:** the same, with `tests\DesktopGuides.Infrastructure.Tests`.
- **Script tests:** `s 'cd /d E:\work\desktop-guides\t17-4b && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\<test>.ps1'`
- **ASCII check:** `grep -nP '[^\x00-\x7F]' <file>`.
- **No `cd` in local shell commands:** use absolute paths.

---
### Task 1: The trim-warning gate

**Files:**
- Create: `tools/p1/assert_trim_warnings.ps1`
- Create: `tools/p1/trim-warning-allowlist.txt`
- Test: `tools/p1/test_assert_trim_warnings.ps1`
- Modify: `.github/workflows/windows-ci.yml` (`core-tests`, after "Check the
  distribution measurement")

**Interfaces:**
- Produces: `assert_trim_warnings.ps1 -Log <path> [-AllowList <path>]
  [-RepoRoot <path>]`. It throws on failure. On success it prints one line
  starting `Trim warnings: none new`. Warning keys are `IL2104 <assembly>`
  for assembly-level warnings, `<code> <repo-relative/path.cs>` for source
  warnings, and `<code> <message>` otherwise.

- [ ] **Step 1: Write the failing test**

`tools/p1/test_assert_trim_warnings.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$gate = Join-Path $PSScriptRoot 'assert_trim_warnings.ps1'

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

$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-trim-gate-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $root = 'D:\a\desktop-guides\desktop-guides'
    $trimmed = '  Optimizing assemblies for size. This process might take a while.'
    $winrt = "ILLink : Trim analysis warning IL2104: Assembly 'WinRT.Runtime' produced trim warnings. For more information see https://aka.ms/il2104 [$root\src\DesktopGuides.Production\DesktopGuides.Production.csproj]"
    $partial = "$root\src\DesktopGuides.Production\GuideRowItem.cs(8,22): warning CsWinRT1028: Class 'GuideRowItem' implements WinRT interfaces but it or a parent type isn't marked partial. [$root\src\DesktopGuides.Production\DesktopGuides.Production.csproj]"
    $json = "$root\src\DesktopGuides.Core\Pdf\PdfSessionDiagnostics.cs(21,33): warning IL2026: Using member 'System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, JsonSerializerOptions)' which has 'RequiresUnreferencedCodeAttribute'. [$root\src\DesktopGuides.Core\DesktopGuides.Core.csproj]"
    $other = "ILLink : Trim analysis warning IL2081: ABI.Windows.Foo.Bar(): 'T' generic argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicParameterlessConstructor'."

    function New-Log([string] $Name, [string[]] $Lines) {
        $path = Join-Path $scratch $Name
        Set-Content -LiteralPath $path -Value $Lines
        return $path
    }
    $allow = Join-Path $scratch 'allow.txt'
    Set-Content -LiteralPath $allow -Value @(
        '# Third-party assemblies only.',
        '',
        'IL2104 WinRT.Runtime  # C#/WinRT runtime; its warnings are its own')
    $common = @{ AllowList = $allow; RepoRoot = $root }

    # Only allowed warnings, each repeated as MSBuild's summary repeats it.
    $clean = New-Log 'clean.log' @($trimmed, $winrt, 'Build succeeded.', $winrt)
    $output = & $gate -Log $clean @common
    Assert-True ("$output" -like 'Trim warnings: none new*') "A log with only allowed warnings should pass, got: $output"

    # A warning in the project's own code fails, keyed by repo path.
    $dirty = New-Log 'dirty.log' @($trimmed, $winrt, $partial, $json)
    Assert-Rejected { & $gate -Log $dirty @common } 'CsWinRT1028 src/DesktopGuides.Production/GuideRowItem.cs' `
        'A C#/WinRT warning in app code should fail, naming its path.'
    Assert-Rejected { & $gate -Log $dirty @common } 'IL2026 src/DesktopGuides.Core/Pdf/PdfSessionDiagnostics.cs' `
        'A JSON trim warning in app code should fail, naming its path.'

    # A warning with neither assembly nor path fails with its text.
    $unkeyed = New-Log 'unkeyed.log' @($trimmed, $winrt, $other)
    Assert-Rejected { & $gate -Log $unkeyed @common } 'IL2081' 'A warning without a path should fail.'

    # A stale entry fails, so fixes shrink the list.
    $stale = New-Log 'stale.log' @($trimmed)
    Assert-Rejected { & $gate -Log $stale @common } 'IL2104 WinRT.Runtime' 'An unused allow-list entry should fail.'

    # A log without trimming isn't a trim-analysis publish.
    $plain = New-Log 'plain.log' @('Build succeeded.')
    Assert-Rejected { & $gate -Log $plain @common } 'shows no trimming' 'A log without trimming should fail.'

    # Entries need a reason, and only assembly-level entries are allowed.
    $noReason = Join-Path $scratch 'no-reason.txt'
    Set-Content -LiteralPath $noReason -Value @('IL2104 WinRT.Runtime')
    Assert-Rejected { & $gate -Log $clean -AllowList $noReason -RepoRoot $root } 'needs a # reason' `
        'An entry without a reason should fail.'
    $ownCode = Join-Path $scratch 'own-code.txt'
    Set-Content -LiteralPath $ownCode -Value @(
        'IL2104 WinRT.Runtime  # third party',
        'CsWinRT1028 src/DesktopGuides.Production/GuideRowItem.cs  # later')
    $ownLog = New-Log 'own.log' @($trimmed, $winrt, $partial)
    Assert-Rejected { & $gate -Log $ownLog -AllowList $ownCode -RepoRoot $root } 'only IL2104' `
        'An entry for the project''s own code should fail.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'assert_trim_warnings: all cases passed.'
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-4b && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_assert_trim_warnings.ps1'`
Expected: FAIL, because `assert_trim_warnings.ps1` doesn't exist.

- [ ] **Step 3: Write the gate and the empty allow-list**

`tools/p1/assert_trim_warnings.ps1`:

```powershell
# T17.4: fails a trim-analysis publish on any trim or C#/WinRT warning the
# allow-list doesn't name, and on any allow-list entry no warning needs.
# Only assembly-level IL2104 entries are allowed: the project's own code
# has no exceptions.
param(
    [Parameter(Mandatory = $true)]
    [string] $Log,
    [string] $AllowList = (Join-Path $PSScriptRoot 'trim-warning-allowlist.txt'),
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)
$ErrorActionPreference = 'Stop'
$lines = @(Get-Content -LiteralPath $Log)
if (-not ($lines | Where-Object { $_ -like '*Optimizing assemblies for size*' })) {
    throw "$Log shows no trimming; it isn't a trim-analysis publish."
}

$root = $RepoRoot.TrimEnd('\', '/') + '\'
$found = [Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::Ordinal)
foreach ($line in $lines) {
    $warning = [regex]::Match($line, 'warning (?<code>IL\d{4}|CsWinRT\d{4})\s*:\s*(?<text>.*)$')
    if (-not $warning.Success) { continue }
    $code = $warning.Groups['code'].Value
    $assembly = [regex]::Match($warning.Groups['text'].Value, "^Assembly '(?<name>[^']+)' produced")
    $source = [regex]::Match($line, '^\s*(?<path>[A-Za-z]:\\[^(]+\.cs)\(\d+')
    if ($code -eq 'IL2104' -and $assembly.Success) {
        $key = "IL2104 $($assembly.Groups['name'].Value)"
    }
    elseif ($source.Success) {
        $path = $source.Groups['path'].Value
        if ($path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            $path = $path.Substring($root.Length)
        }
        $key = "$code $($path -replace '\\', '/')"
    }
    else {
        $key = "$code $(($warning.Groups['text'].Value -replace '\s*\[[^\]]+\]\s*$', '').Trim())"
    }
    if (-not $found.ContainsKey($key)) { $found[$key] = $line.Trim() }
}

$allowed = @{}
foreach ($entry in @(Get-Content -LiteralPath $AllowList)) {
    $text = $entry.Trim()
    if (-not $text -or $text.StartsWith('#')) { continue }
    $parts = $text -split '#', 2
    if ($parts.Count -lt 2 -or -not $parts[1].Trim()) { throw "Allow-list entry '$text' needs a # reason." }
    $key = ($parts[0].Trim() -replace '\s+', ' ')
    if ($key -notmatch '^IL2104 \S+$') {
        throw "Allow-list entry '$key' isn't allowed: only IL2104 entries for assemblies the project doesn't own."
    }
    $allowed[$key] = $true
}

$new = @($found.Keys | Where-Object { -not $allowed.ContainsKey($_) })
$stale = @($allowed.Keys | Where-Object { -not $found.ContainsKey($_) } | Sort-Object)
$problems = @()
if ($new.Count -gt 0) { $problems += "New trim or C#/WinRT warnings:`n  " + ($new -join "`n  ") }
if ($stale.Count -gt 0) { $problems += "Allow-list entries no warning needs (remove them):`n  " + ($stale -join "`n  ") }
if ($problems.Count -gt 0) { throw ($problems -join "`n") }
Write-Output "Trim warnings: none new; $($found.Count) allowed."
```

`tools/p1/trim-warning-allowlist.txt`:

```text
# T17.4: trim warnings the portable build accepts, checked by
# assert_trim_warnings.ps1. One "IL2104 <assembly>  # reason" a line, for
# assemblies the project doesn't own. The project's own code gets no entries.
```

- [ ] **Step 4: Run the test to verify it passes**

Run: the Step 2 command.
Expected: `assert_trim_warnings: all cases passed.`

- [ ] **Step 5: Add the test to `core-tests` and check ASCII**

After "Check the distribution measurement":

```yaml
      - name: Check the trim-warning gate
        shell: pwsh
        run: ./tools/p1/test_assert_trim_warnings.ps1
```

Run: `grep -nP '[^\x00-\x7F]' tools/p1/assert_trim_warnings.ps1 tools/p1/test_assert_trim_warnings.ps1 tools/p1/trim-warning-allowlist.txt`
Expected: no output.

- [ ] **Step 6: Commit**

```bash
git add tools/p1/assert_trim_warnings.ps1 tools/p1/test_assert_trim_warnings.ps1 tools/p1/trim-warning-allowlist.txt .github/workflows/windows-ci.yml
git commit -m "test(p1): add the T17.4 trim-warning gate"
```

---

### Task 2: The trim-analysis CI job, and watching the gate fail

**Files:**
- Modify: `.github/workflows/windows-ci.yml` (a new `portable-trim-analysis`
  job after `portable-measure`)

**Interfaces:**
- Consumes: Task 1's `assert_trim_warnings.ps1` and allow-list.
- Produces: the CI job `portable-trim-analysis`, and its artifact
  `desktop-guides-trim-analysis-log`.

- [ ] **Step 1: Watch the gate fail on today's code, on the host**

```bash
stage && trimlog > /tmp/t17-4b-trim.log 2>&1; tail -3 /tmp/t17-4b-trim.log
gate 2>&1 | tail -60 > /tmp/t17-4b-gate-red.txt; cat /tmp/t17-4b-gate-red.txt
```

Expected:
- the publish succeeds;
- the gate FAILS with "New trim or C#/WinRT warnings:", listing:
  - `CsWinRT1028` for `AddGameDialog.xaml.cs`, `GuideRowItem.cs`,
    `LibraryGameItem.cs` and `Providers/ProviderServices.cs`;
  - `CsWinRT1030` for `AddGameDialog.xaml.cs`, `CatalogRowItem.cs`,
    `Providers/WindowsProviderCredentialStore.cs`, `ShellWindow.Backup.cs`,
    `ShellWindow.xaml.cs`, `Styles/Catalog.xaml.cs` and
    `TextReaderView.xaml.cs`;
  - `IL2026` for the 13 JSON files: 5 in Core (`FixtureCatalog`,
    `HtmlLocationRules`, `HtmlReaderStyle`, `HtmlSessionDiagnostics`,
    `PdfSessionDiagnostics`), 4 in Infrastructure (`FixtureProviderHandler`,
    `ProviderCredentialBlob`, `FileOperationManifest`, `GameMetadataJson`)
    and 4 in Production (`ExternalLinkLaunchers`, `HtmlPositionScripts`,
    `HtmlReaderSession`, `ShellWindow.Progress`);
  - assembly-level `IL2104` lines for `AngleSharp`,
    `Microsoft.Windows.SDK.NET` and `WinRT.Runtime`.

If the third-party warnings show as individual IL2081 or IL2075 lines
rather than IL2104, `TrimmerSingleWarn` is off for those packages. Then
add `-p:TrimmerSingleWarn=true` to `trimlog` and to Step 2, and ledger it.
Keep `/tmp/t17-4b-gate-red.txt` for the verification record.

- [ ] **Step 2: Add the CI job**

In `windows-ci.yml`, after the `portable-measure` job:

```yaml
  # T17.4: a trimmed publish of the portable build, only to collect trim and
  # C#/WinRT warnings; the shipped exe isn't trimmed yet (PR b2).
  portable-trim-analysis:
    needs: core-tests
    if: ${{ !(inputs.dev-fast && !inputs.verify-test-gate) }}
    runs-on: windows-2025
    steps:
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
      - name: Publish trimmed and check the warnings
        shell: pwsh
        run: |
          dotnet publish src/DesktopGuides.Production/DesktopGuides.Production.csproj `
            -c Release --no-restore -p:Platform=x64 -p:Portable=true `
            -p:PublishTrimmed=true -p:TrimMode=partial -p:CsWinRTAotWarningLevel=2 `
            -p:PublishDir=$PWD/artifacts/trim-analysis/ `
            "-flp:logfile=$PWD/artifacts/trim-analysis.log;verbosity=normal"
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          ./tools/p1/assert_trim_warnings.ps1 -Log artifacts/trim-analysis.log
      - name: Upload the trim-analysis log
        if: always()
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4.6.2
        with:
          name: desktop-guides-trim-analysis-log
          path: artifacts/trim-analysis.log
          if-no-files-found: warn
```

- [ ] **Step 3: Commit**

The gate now fails CI until Task 7. That's intended: no push before then.

```bash
git add .github/workflows/windows-ci.yml
git commit -m "ci(p1): run the trim-warning gate on a trimmed portable publish"
```

---

### Task 3: The C#/WinRT fixes

**Files:**
- Modify: `src/DesktopGuides.Production/AddGameDialog.xaml.cs` (`GameSearchItem`)
- Modify: `src/DesktopGuides.Production/GuideRowItem.cs`
- Modify: `src/DesktopGuides.Production/LibraryGameItem.cs`
- Modify: `src/DesktopGuides.Production/Providers/ProviderServices.cs`
- Modify: `src/DesktopGuides.Production/DesktopGuides.Production.csproj`
  (the first PropertyGroup)

**Interfaces:**
- Consumes: Task 2's `trimlog` and `gate`.

- [ ] **Step 1: Mark the four types `partial`**

In each file, add `partial` to the class declaration the warning names,
keeping every other modifier. For example, `GuideRowItem.cs` line 8:
`public sealed class GuideRowItem` becomes `public sealed partial class
GuideRowItem`. The same goes for `GameSearchItem` (`AddGameDialog.xaml.cs`
line 14), `LibraryGameItem` (`LibraryGameItem.cs` line 5) and
`ProviderServices` (`Providers/ProviderServices.cs` line 8). Read each line
first, and change only the modifier list.

- [ ] **Step 2: Let C#/WinRT generate its generic-interface code**

In `DesktopGuides.Production.csproj`, in the first PropertyGroup after
`<Nullable>enable</Nullable>`:

```xml
    <!-- T17.4: C#/WinRT generates the WinRT interface code for generic
         collections passed to WinUI (CsWinRT1030), which needs unsafe code. -->
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

- [ ] **Step 3: Check the C#/WinRT warnings are gone, and nothing else moved**

```bash
stage && trimlog > /tmp/t17-4b-trim.log 2>&1; tail -2 /tmp/t17-4b-trim.log
gate 2>&1 | grep -E "CsWinRT|IL2026|IL2104" | sort
```

Expected:
- the publish succeeds;
- no `CsWinRT` line remains;
- the `IL2026` and `IL2104` lines are those from Task 2 Step 1.

If a `CsWinRT1030` line remains, its type crosses into WinRT in a way
`AllowUnsafeBlocks` doesn't cover. Read the warning's full text in the
log, fix that one site, and ledger it.

Then check the MSIX build is unaffected:

```bash
s 'cd /d E:\work\desktop-guides\t17-4b && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release --no-restore -p:Platform=x64 -nologo' 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
```

Expected: `0 Error(s)`, with no new warning beyond the host's
`mspdbcmf.exe` one.

- [ ] **Step 4: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "fix(p1): make the WinRT-exposed types partial and allow C#/WinRT's unsafe code"
```

---

### Task 4: Core's JSON through a source-generated context

**Files:**
- Create: `src/DesktopGuides.Core/CoreJsonContext.cs`
- Modify: `src/DesktopGuides.Core/Fixtures/FixtureCatalog.cs` (lines 26-27, 83)
- Modify: `src/DesktopGuides.Core/Html/HtmlLocationRules.cs` (line 157)
- Modify: `src/DesktopGuides.Core/Html/HtmlReaderStyle.cs` (line 62)
- Modify: `src/DesktopGuides.Core/Html/HtmlSessionDiagnostics.cs` (`ToJson`)
- Modify: `src/DesktopGuides.Core/Pdf/PdfSessionDiagnostics.cs` (`ToJson`)
- Create: `tests/DesktopGuides.Core.Tests/TestJson.cs`
- Create: `tests/DesktopGuides.Core.Tests/CoreJsonPayloadTests.cs`
- Modify: `tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj`
- Modify: `tests/DesktopGuides.Core.Tests/HtmlReaderStyleTests.cs` (line 93),
  `tests/DesktopGuides.Core.Tests/HtmlLocationRulesTests.cs` (line 18)

**Interfaces:**
- Produces: `internal sealed partial class CoreJsonContext`, and the
  records `HtmlFindArgs`, `HtmlSessionDiagnosticsJson`, `HtmlDeniedJson`
  and `HtmlAppearanceJson`, all internal to Core.
- Produces (tests): `TestJson.Options`, a `JsonSerializerOptions` with an
  explicit reflection resolver.

- [ ] **Step 1: Pin today's JSON (characterization tests)**

`tests/DesktopGuides.Core.Tests/CoreJsonPayloadTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

// T17.4: the exact JSON each payload has today, so moving to source
// generation can't change a byte.
public sealed class CoreJsonPayloadTests
{
    private static readonly Guid Id = Guid.Parse("0123456789abcdef0123456789abcdef");

    [Fact]
    public void PdfDiagnosticsJsonIsUnchanged()
    {
        PdfSessionDiagnostics diagnostics = new(600, 41, 3, 90_000_000, 100_663_296, 17, 8, 280, true, 12, true);

        Assert.Equal(
            """{"requests":600,"loads":41,"staleResults":3,"peakCacheBytes":90000000,"maxCacheBytes":100663296,"cachedPagesAtClose":17,"peakTextPages":8,"peakTextCharacters":280,"disposedCleanly":true,"evictions":12,"abandonedExtraction":true}""",
            diagnostics.ToJson());
    }

    [Fact]
    public void EmptyHtmlDiagnosticsJsonIsUnchanged()
    {
        Assert.Equal(
            """{"guideId":"0123456789abcdef0123456789abcdef","served":[],"denied":[],"rejectedCaptures":0,"restores":{},"entryNavigations":0,"appearance":{"theme":null,"scale":null,"applications":0,"failures":0,"bodyBackground":null,"bodyColor":null,"rootZoom":null}}""",
            new HtmlSessionDiagnostics().ToJson(Id));
    }

    [Fact]
    public void HtmlDiagnosticsJsonIsUnchanged()
    {
        HtmlSessionDiagnostics diagnostics = new();
        diagnostics.RecordServed("/b.html");
        diagnostics.RecordServed("/a.html");
        diagnostics.RecordDenied(HtmlDenyReason.External, "Navigation");
        diagnostics.RecordDenied(HtmlDenyReason.External, "Navigation");
        diagnostics.RecordRejectedCapture();
        diagnostics.RecordRestore(RestoreKind.Exact);
        diagnostics.RecordEntryNavigation();
        diagnostics.RecordAppearance("dark", 1.5, new HtmlAppliedStyle("rgb(0, 0, 0)", "rgb(255, 255, 255)", "1.5"));

        Assert.Equal(
            """{"guideId":"0123456789abcdef0123456789abcdef","served":["/a.html","/b.html"],"denied":[{"reason":"External","context":"Navigation","count":2}],"rejectedCaptures":1,"restores":{"Exact":1},"entryNavigations":1,"appearance":{"theme":"dark","scale":1.5,"applications":1,"failures":0,"bodyBackground":"rgb(0, 0, 0)","bodyColor":"rgb(255, 255, 255)","rootZoom":"1.5"}}""",
            diagnostics.ToJson(Id));
    }

    [Fact]
    public void FindArgsJsonIsUnchangedAndEscapesMarkup()
    {
        HtmlPosition position = new("index.html", "intro", "Chapter \"1\" <b>", 42, 0.25);

        Assert.Equal(
            """{"offset":42,"quote":"Chapter \u00221\u0022 \u003Cb\u003E","id":"intro"}""",
            HtmlLocationRules.FindArgs(position));
    }

    [Fact]
    public void FindArgsJsonKeepsNulls()
    {
        HtmlPosition position = new("index.html", null, null, 0, 0);

        Assert.Equal("""{"offset":0,"quote":null,"id":null}""", HtmlLocationRules.FindArgs(position));
    }
}
```

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-4b && dotnet test tests\DesktopGuides.Core.Tests --filter CoreJsonPayloadTests -nologo'`
Expected: PASS, 5 of 5. These describe today's output. If one fails, its
expected string is wrong, not the code: correct the literal from the
actual output in the failure message, and ledger it.

- [ ] **Step 2: Switch reflection JSON off in the test project, and watch tests fail**

In `tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj`, in
the first PropertyGroup:

```xml
    <!-- T17.4: a product JSON call that misses the source-generated
         context throws here instead of passing on reflection. -->
    <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
```

`tests/DesktopGuides.Core.Tests/TestJson.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DesktopGuides.Core.Tests;

// Reflection JSON is off in this project; test code that builds JSON on
// purpose opts back in through this explicit resolver.
internal static class TestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
}
```

In `HtmlReaderStyleTests.cs` line 93, pass `TestJson.Options`:
`string literal = JsonSerializer.Serialize(HtmlReaderStyle.Css(ReaderTheme.Dark, 1.5), TestJson.Options);`.
In `HtmlLocationRulesTests.cs` line 18:
`private static string Str(string? value) => JsonSerializer.Serialize(value, TestJson.Options);`.

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-4b && dotnet test tests\DesktopGuides.Core.Tests -nologo' > /tmp/t17-4b-core-red.log 2>&1; grep -E "Failed!|Passed!|Reflection-based serialization" /tmp/t17-4b-core-red.log | sort | uniq -c | head`
Expected: FAIL. The failures include the five `CoreJsonPayloadTests`, and
the `HtmlSessionDiagnostics`, `PdfSessionDiagnostics`, `FixtureCatalog`,
`HtmlReaderStyle` and `HtmlLocationRules` tests that serialize. Each fails
with "Reflection-based serialization has been disabled".

- [ ] **Step 3: Add the context and move every Core call to it**

`src/DesktopGuides.Core/CoreJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;
using DesktopGuides.Core.Fixtures;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Pdf;

namespace DesktopGuides.Core;

// T17.4: source-generated JSON, so a trimmed build keeps every member it
// writes and reads. Names stay camelCase, as the anonymous payloads were.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(FixtureCatalog.FixtureManifest))]
[JsonSerializable(typeof(HtmlFindArgs))]
[JsonSerializable(typeof(HtmlSessionDiagnosticsJson))]
[JsonSerializable(typeof(PdfSessionDiagnostics))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
```

`FixtureCatalog.cs`:
- line 83, `private sealed record FixtureManifest(...)` becomes
  `internal sealed record FixtureManifest(int SchemaVersion, List<FixtureEntry> Fixtures);`;
- add the field
  `private static readonly CoreJsonContext ManifestJson = new(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });`;
- lines 26-27 become
  `FixtureManifest? manifest = JsonSerializer.Deserialize(manifestText, ManifestJson.FixtureManifest);`.

`HtmlLocationRules.cs` line 157 becomes:

```csharp
        return JsonSerializer.Serialize(
            new HtmlFindArgs(position.TextOffset, position.TextQuote, position.ElementId),
            CoreJsonContext.Default.HtmlFindArgs);
```

and at the end of the file:

```csharp
internal sealed record HtmlFindArgs(int Offset, string? Quote, string? Id);
```

`HtmlReaderStyle.cs` line 62: `JsonSerializer.Serialize(Css(theme, scale))`
becomes `JsonSerializer.Serialize(Css(theme, scale), CoreJsonContext.Default.String)`.

`HtmlSessionDiagnostics.cs`, the body of `ToJson`'s `lock`:

```csharp
            return JsonSerializer.Serialize(new HtmlSessionDiagnosticsJson(
                guideId.ToString("N"),
                served.ToArray(),
                denied
                    .OrderBy(pair => pair.Key.Reason.ToString(), StringComparer.Ordinal)
                    .ThenBy(pair => pair.Key.Context, StringComparer.Ordinal)
                    .Select(pair => new HtmlDeniedJson(pair.Key.Reason.ToString(), pair.Key.Context, pair.Value))
                    .ToArray(),
                rejectedCaptures,
                new Dictionary<string, int>(restores),
                entryNavigations,
                new HtmlAppearanceJson(
                    appearanceTheme,
                    appearanceScale,
                    appearanceApplications,
                    appearanceFailures,
                    appearanceComputed?.BodyBackground,
                    appearanceComputed?.BodyColor,
                    appearanceComputed?.RootZoom)),
                CoreJsonContext.Default.HtmlSessionDiagnosticsJson);
```

and at the end of the file:

```csharp
internal sealed record HtmlSessionDiagnosticsJson(
    string GuideId, string[] Served, HtmlDeniedJson[] Denied, int RejectedCaptures,
    Dictionary<string, int> Restores, int EntryNavigations, HtmlAppearanceJson Appearance);

internal sealed record HtmlDeniedJson(string Reason, string Context, int Count);

internal sealed record HtmlAppearanceJson(
    string? Theme, double? Scale, int Applications, int Failures,
    string? BodyBackground, string? BodyColor, string? RootZoom);
```

`PdfSessionDiagnostics.cs`: the record's own properties have the
payload's names and order, so `ToJson` serializes the record itself:

```csharp
    public string ToJson() => JsonSerializer.Serialize(this, CoreJsonContext.Default.PdfSessionDiagnostics);
```

- [ ] **Step 4: Run the Core tests, and watch them pass**

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-4b && dotnet test tests\DesktopGuides.Core.Tests -nologo' > /tmp/t17-4b-core.log 2>&1; tail -3 /tmp/t17-4b-core.log`
Expected: `Passed!`, with no failures. The count is the suite's previous
total plus 5.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core tests/DesktopGuides.Core.Tests
git commit -m "fix(p1): move Core's JSON to a source-generated context"
```

---

### Task 5: Infrastructure's JSON through a source-generated context

**Files:**
- Create: `src/DesktopGuides.Infrastructure/InfrastructureJsonContext.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/GameMetadataJson.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/FileOperationManifest.cs` (lines 30-35)
- Modify: `src/DesktopGuides.Infrastructure/Providers/ProviderCredentialBlob.cs` (line 38)
- Modify: `src/DesktopGuides.Infrastructure/Providers/FixtureProviderHandler.cs` (lines 16, 26, 83, 86, 89-95)
- Create: `tests/DesktopGuides.Infrastructure.Tests/TestJson.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/InfrastructureJsonPayloadTests.cs`
- Modify: `tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj`
- Modify: `tests/DesktopGuides.Infrastructure.Tests/FileOperationReconciliationTests.cs` (lines 89, 378),
  `tests/DesktopGuides.Infrastructure.Tests/LibraryExportMeasurement.cs` (line 64),
  `tests/DesktopGuides.Infrastructure.Tests/Providers/FixtureProviderHandlerTests.cs` (lines 20, 105)

**Interfaces:**
- Consumes: Task 4's Core context, through the Core code these tests call.
- Produces: `internal sealed partial class InfrastructureJsonContext`, and
  the record `FileOperationManifestJson`.

- [ ] **Step 1: Pin today's JSON (characterization tests)**

`tests/DesktopGuides.Infrastructure.Tests/InfrastructureJsonPayloadTests.cs`:

```csharp
using System.Text;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

// T17.4: the exact JSON each payload has today, so moving to source
// generation can't change a byte, and stored data still reads.
public sealed class InfrastructureJsonPayloadTests
{
    private const string Snapshot =
        """{"schemaVersion":1,"summary":"A \u003Cgame\u003E \u0026 more.","firstReleaseDate":"1998-11-19","genres":["Shooter"],"developers":["Valve"],"publishers":["Sierra"],"platforms":["PC"],"providerUrl":"https://www.igdb.com/games/half-life","type":"MainGame","artworkSource":"IGDB"}""";

    private static GameMetadataSnapshot Example() => new(
        1, "A <game> & more.", new DateOnly(1998, 11, 19), ["Shooter"], ["Valve"], ["Sierra"], ["PC"],
        "https://www.igdb.com/games/half-life", GameTypeTag.MainGame, "IGDB");

    [Fact]
    public void MetadataJsonIsUnchanged()
    {
        Assert.Equal(Snapshot, GameMetadataJson.Serialize(Example()));
    }

    [Fact]
    public void StoredMetadataStillReads()
    {
        GameMetadataSnapshot? read = GameMetadataJson.TryParse(Snapshot);

        Assert.NotNull(read);
        Assert.Equal(Example() with { Genres = read!.Genres, Developers = read.Developers, Publishers = read.Publishers, Platforms = read.Platforms }, read);
        Assert.Equal(["Shooter"], read.Genres);
        Assert.Equal(["PC"], read.Platforms);
    }

    [Fact]
    public void MetadataWithoutArtworkSourceStillReads()
    {
        GameMetadataSnapshot? read = GameMetadataJson.TryParse(Snapshot.Replace(""","artworkSource":"IGDB"}""", "}"));

        Assert.NotNull(read);
        Assert.Null(read!.ArtworkSource);
    }

    [Fact]
    public void MetadataWithANumericTypeIsRejected()
    {
        Assert.Null(GameMetadataJson.TryParse(Snapshot.Replace("\"MainGame\"", "0")));
    }

    [Fact]
    public void FileOperationManifestJsonIsUnchanged()
    {
        Guid operation = Guid.Parse("11111111111111111111111111111111");
        Guid guide = Guid.Parse("22222222222222222222222222222222");

        Assert.Equal(
            """{"schemaVersion":1,"guideIds":["22222222222222222222222222222222"],"ownedPaths":[".staging/11111111111111111111111111111111/22222222222222222222222222222222","content/22222222222222222222222222222222"]}""",
            FileOperationManifest.Create(FileOperationKind.Import, operation, [guide]));
    }

    [Fact]
    public void CredentialBlobBytesAreUnchanged()
    {
        byte[] blob = ProviderCredentialBlob.Format(new ProviderCredentials(new IgdbCredentials("id", "secret"), "key"));

        Assert.Equal(
            """{"v":1,"igdb":{"clientId":"id","clientSecret":"secret"},"steamGridDbKey":"key"}""",
            Encoding.UTF8.GetString(blob));
    }
}
```

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-4b && dotnet test tests\DesktopGuides.Infrastructure.Tests --filter InfrastructureJsonPayloadTests -nologo'`
Expected: PASS, 6 of 6. If a literal is wrong, correct it from the actual
output, and ledger it.

- [ ] **Step 2: Switch reflection JSON off in the test project, and watch tests fail**

In `DesktopGuides.Infrastructure.Tests.csproj`, in the first PropertyGroup:

```xml
    <!-- T17.4: a product JSON call that misses the source-generated
         context throws here instead of passing on reflection. -->
    <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
```

`tests/DesktopGuides.Infrastructure.Tests/TestJson.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DesktopGuides.Infrastructure.Tests;

// Reflection JSON is off in this project; test code that builds JSON on
// purpose opts back in through this explicit resolver.
internal static class TestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
}
```

Pass `TestJson.Options` as the last argument of each `JsonSerializer`
call in test code: `FileOperationReconciliationTests.cs` lines 89 and 378,
`LibraryExportMeasurement.cs` line 64, and
`Providers/FixtureProviderHandlerTests.cs` lines 20 and 105. For a call
that already passes options, set `TypeInfoResolver = new
DefaultJsonTypeInfoResolver()` on those options instead.

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-4b && dotnet test tests\DesktopGuides.Infrastructure.Tests -nologo' > /tmp/t17-4b-infra-red.log 2>&1; grep -E "Failed!|Passed!|Reflection-based serialization" /tmp/t17-4b-infra-red.log | sort | uniq -c | head`
Expected: FAIL. The failures include the `InfrastructureJsonPayloadTests`
and the metadata, file-operation, credential and fixture-provider tests,
each with "Reflection-based serialization has been disabled".

- [ ] **Step 3: Add the context and move every Infrastructure call to it**

`src/DesktopGuides.Infrastructure/InfrastructureJsonContext.cs`:

```csharp
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Infrastructure.Storage;

namespace DesktopGuides.Infrastructure;

// T17.4: source-generated JSON, so a trimmed build keeps every member it
// writes and reads. Callers that used web defaults pass those options in.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GameMetadataSnapshot))]
[JsonSerializable(typeof(FileOperationManifestJson))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(FixtureProviderHandler.FixtureManifest))]
[JsonSerializable(typeof(List<FixtureProviderHandler.UnmatchedRequest>))]
internal sealed partial class InfrastructureJsonContext : JsonSerializerContext;
```

`GameMetadataJson.cs`: replace the `Options` field with

```csharp
    private static readonly InfrastructureJsonContext Json = new(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<GameTypeTag>(allowIntegerValues: false) }
    });
```

Then `JsonSerializer.Serialize(snapshot, Options)` becomes
`JsonSerializer.Serialize(snapshot, Json.GameMetadataSnapshot)`, and
`JsonSerializer.Deserialize<GameMetadataSnapshot>(json, Options)` becomes
`JsonSerializer.Deserialize(json, Json.GameMetadataSnapshot)`.

`FileOperationManifest.cs` lines 30-35 become:

```csharp
        string json = JsonSerializer.Serialize(
            new FileOperationManifestJson(1, names, ExpectedPaths(kind, operationId, names)),
            InfrastructureJsonContext.Default.FileOperationManifestJson);
```

and at the end of the file:

```csharp
internal sealed record FileOperationManifestJson(int SchemaVersion, string[] GuideIds, string[] OwnedPaths);
```

`ProviderCredentialBlob.cs` line 38 becomes
`return JsonSerializer.SerializeToUtf8Bytes(root, InfrastructureJsonContext.Default.JsonObject);`.

`FixtureProviderHandler.cs`:
- line 16, `Options` becomes
  `private static readonly InfrastructureJsonContext Json = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));`;
- line 26 becomes
  `routes = JsonSerializer.Deserialize(manifest, Json.FixtureManifest)?.Routes ?? [];`;
- line 83 becomes
  `? JsonSerializer.Deserialize(File.ReadAllText(path), Json.ListUnmatchedRequest) ?? []`;
- line 86 becomes
  `File.WriteAllText(path, JsonSerializer.Serialize(entries, Json.ListUnmatchedRequest));`;
- the three nested records change from `private sealed record` to
  `internal sealed record`, so the context can see them.

- [ ] **Step 4: Run both suites, and watch them pass**

Run: `stage && s 'cd /d E:\work\desktop-guides\t17-4b && dotnet test tests\DesktopGuides.Infrastructure.Tests -nologo && dotnet test tests\DesktopGuides.Core.Tests -nologo' > /tmp/t17-4b-infra.log 2>&1; grep -E "Passed!|Failed!" /tmp/t17-4b-infra.log`
Expected: two `Passed!` lines, with no failures. Infrastructure's count is
its previous total plus 6.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Infrastructure tests/DesktopGuides.Infrastructure.Tests
git commit -m "fix(p1): move Infrastructure's JSON to a source-generated context"
```

---

### Task 6: Production's JSON through a source-generated context

**Files:**
- Create: `src/DesktopGuides.Production/ProductionJsonContext.cs`
- Modify: `src/DesktopGuides.Production/ExternalLinkLaunchers.cs` (lines 29, 33)
- Modify: `src/DesktopGuides.Production/HtmlPositionScripts.cs` (lines 179, 190)
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs` (lines 456, 897-906)
- Modify: `src/DesktopGuides.Production/ShellWindow.Progress.cs` (lines 24, 52-59, 66-81)

**Interfaces:**
- Produces: `internal sealed partial class ProductionJsonContext`, and the
  records `DevToolsEvaluateParams`, `HtmlPositionForTest`,
  `ProgressCountsForTest`, `ProgressFailureForTest` and
  `ProgressFailureContext`.

- [ ] **Step 1: Confirm the four files still warn (the red state)**

Run: `stage && trimlog > /tmp/t17-4b-trim.log 2>&1; gate 2>&1 | grep -E "IL2026 src/DesktopGuides.Production" | sort`
Expected: four lines, for `ExternalLinkLaunchers.cs`,
`HtmlPositionScripts.cs`, `HtmlReaderSession.cs` and
`ShellWindow.Progress.cs`. No Core or Infrastructure `IL2026` line is
left after Tasks 4 and 5.

- [ ] **Step 2: Add the context and the records**

`src/DesktopGuides.Production/ProductionJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;

namespace DesktopGuides.Production;

// T17.4: source-generated JSON, so a trimmed build keeps every member it
// writes and reads. The names match the anonymous payloads they replace:
// WebView2 DevTools parameters and the test gate's diagnostics files.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(DevToolsEvaluateParams))]
[JsonSerializable(typeof(HtmlPositionForTest))]
[JsonSerializable(typeof(ProgressCountsForTest))]
internal sealed partial class ProductionJsonContext : JsonSerializerContext;

internal sealed record DevToolsEvaluateParams(string Expression, bool ReturnByValue, bool UserGesture);

internal sealed record HtmlPositionForTest(
    string? Locator, string? Kind, string? Step, string? Reason,
    string? AppearanceKind, string? AppearanceStep, double? AppearanceScale, int AppearanceRestores);

internal sealed record ProgressCountsForTest(
    int Saves, int SkippedUnchanged, int Failures, int Opens, int OpenFailures,
    ProgressFailureForTest? LastFailure, ProgressFailureContext? FailureContext);

// "hresult", not camelCase's "hResult": the smoke reads the old name.
internal sealed record ProgressFailureForTest(
    string Stage, string Type, [property: JsonPropertyName("hresult")] int HResult,
    string Trigger, long CaptureMs, long WriteMs);

internal sealed record ProgressFailureContext(
    long? GateWaitMs, long? QueueWaitMs, long? WorkMs, int PoolThreads, long PoolPending);
```

- [ ] **Step 3: Move the four files' calls to it**

`ExternalLinkLaunchers.cs`:
- line 29 becomes
  `? JsonSerializer.Deserialize(await File.ReadAllTextAsync(path), ProductionJsonContext.Default.ListString) ?? []`;
- line 33 becomes
  `await File.WriteAllTextAsync(path, JsonSerializer.Serialize(launched, ProductionJsonContext.Default.ListString));`.

`HtmlPositionScripts.cs`:
- line 179, `JsonSerializer.Serialize(offset)` becomes
  `JsonSerializer.Serialize(offset, ProductionJsonContext.Default.Int32)`;
- line 190, `JsonSerializer.Serialize(fraction)` becomes
  `JsonSerializer.Serialize(fraction, ProductionJsonContext.Default.Double)`.

`HtmlReaderSession.cs`:
- line 456 becomes
  `JsonSerializer.Serialize(new DevToolsEvaluateParams(script, ReturnByValue: true, UserGesture: false), ProductionJsonContext.Default.DevToolsEvaluateParams);`;
- lines 897-906 (the anonymous object in `WritePositionForTest`) become:

```csharp
            string json = JsonSerializer.Serialize(
                new HtmlPositionForTest(
                    locator,
                    lastOutcome?.Kind.ToString(),
                    lastStep?.ToString(),
                    lastOutcome?.Reason,
                    appearanceOutcome?.Kind.ToString(),
                    appearanceStep?.ToString(),
                    appearanceScale,
                    appearanceRestores),
                ProductionJsonContext.Default.HtmlPositionForTest);
```

`ShellWindow.Progress.cs`:
- line 24 becomes `private ProgressFailureContext? failureContext;`;
- the anonymous `failureContext = new { ... };` (lines 52-59) becomes:

```csharp
                failureContext = new ProgressFailureContext(
                    write?.GateWaitMs,
                    write?.QueueWaitMs,
                    write?.WorkMs,
                    ThreadPool.ThreadCount,
                    ThreadPool.PendingWorkItemCount);
```

- the `JsonSerializer.Serialize(new { ... })` call (lines 66-81) becomes:

```csharp
                File.WriteAllText(temp, JsonSerializer.Serialize(
                    new ProgressCountsForTest(
                        counts.Saves,
                        counts.SkippedUnchanged,
                        counts.Failures,
                        counts.Opens,
                        counts.OpenFailures,
                        lastFailure is null ? null : new ProgressFailureForTest(
                            lastFailure.Stage,
                            lastFailure.ErrorType,
                            lastFailure.HResult,
                            lastFailure.Trigger,
                            lastFailure.CaptureMs,
                            lastFailure.WriteMs),
                        failureContext),
                    ProductionJsonContext.Default.ProgressCountsForTest));
```

Read each original block before replacing it, and keep its property order.
If the original has a field this plan doesn't list, add it to the record
in the same position, and ledger it.

- [ ] **Step 4: Check no JSON trim warning is left**

Run: `stage && trimlog > /tmp/t17-4b-trim.log 2>&1; tail -2 /tmp/t17-4b-trim.log; gate 2>&1 | grep -E "IL2026|CsWinRT" | sort`
Expected: the publish succeeds, and no `IL2026` or `CsWinRT` line names a
`src/` path.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "fix(p1): move Production's JSON to a source-generated context"
```

---

### Task 7: The third-party warnings, reviewed and allowed

**Files:**
- Modify: `tools/p1/trim-warning-allowlist.txt`
- Modify: `docs/p1/t17-4-size-pass-design.md` (a "PR b1 verification
  record" section, holding the AngleSharp review)

**Interfaces:**
- Consumes: Tasks 2-6.

- [ ] **Step 1: See what is left**

Run: `gate 2>&1 | tail -20`
Expected: the gate FAILS, and only assembly-level lines remain:
`IL2104 AngleSharp`, `IL2104 Microsoft.Windows.SDK.NET` and
`IL2104 WinRT.Runtime`, using the exact names the log shows. Any other
line is unfinished work from Tasks 3-6: go back and fix it there.

- [ ] **Step 2: Review AngleSharp**

```bash
grep -rn "AngleSharp" /Users/ilya.lissoboi/work/desktop-guides/src --include='*.cs' -l
grep -rn "CreateElement<\|\.ToDictionary(" /Users/ilya.lissoboi/work/desktop-guides/src/DesktopGuides.Infrastructure | grep -v "Linq" | head
s 'powershell -NoProfile -Command "$a = [Reflection.Assembly]::LoadFile((Get-ChildItem $env:USERPROFILE\.nuget\packages\anglesharp\1.8.2 -Recurse -Filter AngleSharp.dll | Where-Object FullName -match \"net8|net9|net10|netstandard2\" | Select-Object -First 1).FullName); $a.GetCustomAttributesData() | Where-Object { $_.AttributeType.Name -eq \"AssemblyMetadataAttribute\" } | ForEach-Object { $_.ConstructorArguments -join \"=\" }"'
```

Record in the design's PR b1 verification record:
- which project files use AngleSharp (expected: the static-HTML import
  scanner and validator, in Infrastructure);
- whether the app calls `CreateElement<T>` or AngleSharp's
  `ToDictionary` (the two reflection sites behind its warnings);
- whether AngleSharp declares `IsTrimmable`.

If it doesn't, `TrimMode=partial` keeps it whole, and its reflection
reaches only its own kept types, so the warning is allowed. If it does
declare `IsTrimmable=True`, record that PR b2 must root it with
`<TrimmerRootAssembly Include="AngleSharp" />`. Either way, the entry
below stays for b1.

- [ ] **Step 3: Allow the three assemblies**

Append to `tools/p1/trim-warning-allowlist.txt`, using the names from
Step 1:

```text
IL2104 AngleSharp  # HTML import parser; reflection over its own element and options types, reviewed in T17.4 (design, PR b1 record)
IL2104 Microsoft.Windows.SDK.NET  # Windows SDK projection; warnings are in its generated ABI marshalling for generic WinRT interfaces
IL2104 WinRT.Runtime  # C#/WinRT runtime; warnings are in its own marshalling helpers, outside the project's code
```

Run: `stage && trimlog > /tmp/t17-4b-trim.log 2>&1; gate`
Expected: `Trim warnings: none new; 3 allowed.`

- [ ] **Step 4: Run every suite and script test on the host**

```bash
s 'cd /d E:\work\desktop-guides\t17-4b && dotnet test tests\DesktopGuides.Core.Tests -nologo && dotnet test tests\DesktopGuides.Infrastructure.Tests -nologo' 2>&1 | grep -E "Passed!|Failed!"
s 'cd /d E:\work\desktop-guides\t17-4b && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_assert_trim_warnings.ps1 & powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_scripts_parse.ps1'
```

Expected: two `Passed!` lines; `assert_trim_warnings: all cases passed.`;
`windows_scripts_parse: all scripts passed.`

- [ ] **Step 5: Commit, push and dispatch**

```bash
git add tools/p1/trim-warning-allowlist.txt docs/p1/t17-4-size-pass-design.md
git commit -m "build(p1): allow only the third-party trim warnings, after an AngleSharp review"
git push -u origin feat/p1-t17-4-trim
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-4-trim -f shell-scope=html -f shell-lane=both
```

Expected:
- `core-tests` passes, with the new gate test, under pwsh 7;
- `portable-trim-analysis` passes with "Trim warnings: none new; 3 allowed.";
- the `html` shard passes on both lanes, which exercises the WebView2
  script arguments, the position-for-test file, external links and the
  provider fixtures.

Record the run ID.

---

### Task 8: Records, the full PR run and the PR

**Files:**
- Modify: `docs/p1/t17-4-size-pass-design.md` (Status, and the PR b1
  verification record)
- Modify: `docs/p1/implementation-plan.md` (the Status paragraph, lines 3-7)
- Modify: `docs/progress.md` (the "P1 first usable release" row)

- [ ] **Step 1: Write the records**

In the design:
- set Status to "PR a merged in #106 (merge commit `1c01e75`). PR b1
  (trim-safe code and the warning gate) is implemented on
  `feat/p1-t17-4-trim`; PR b2 (the trimmed, compressed exe) follows.";
- complete the PR b1 verification record:
  - the red gate list from Task 2;
  - each JSON task's red and green test counts;
  - the AngleSharp review;
  - the dispatch ID from Task 7;
  - every ledgered ruling.

In `implementation-plan.md`, the sentence "In M6, T17.2 (release CI) is
complete; T17.4 (size pass) and then T17.3 (release-candidate runs)
remain." becomes "In M6, T17.2 (release CI) is complete. T17.4 (size pass)
has PR a merged in #106 and PR b1 in review, with PR b2 to follow; T17.3
(release-candidate runs) comes after it."

In `progress.md`, in the "P1 first usable release" row, "with T17.4 and
T17.3 left" becomes "with T17.4 (size pass: PR a merged in #106, PR b in
progress) and T17.3 left".

- [ ] **Step 2: Commit and push**

```bash
git add docs
git commit -m "docs(p1): record T17.4 PR b1 and the status after PR a"
git push
```

- [ ] **Step 3: Open the PR, and wait for its run**

Title: "T17.4 PR b1: trim-safe code and a trim-warning gate". The body
names:
- the task: T17.4, PR b1 of b1 and b2;
- the prerequisites: PR a in #106, merged;
- the outcome: trim-safe code and the CI gate, with the exe not yet
  trimmed;
- the red and green evidence;
- the allow-list and the AngleSharp review;
- every ruling.

There are no UI changes, so no screenshot.

Expected: every job passes, including all 10 installed shards on both
lanes and `portable-trim-analysis`. Record the run in the PR description,
not with a push. Then stop for the user's merge decision.

## Traceability

| Spec item (PR b, sections 1 and 2) | Task |
| --- | --- |
| Gate: CI fails on a new trim or C#/WinRT warning | 1, 2 |
| Allow-list: one code for one assembly, with a reason | 1, 7 |
| Gate written first, failing on the known warnings | 2 |
| CsWinRT1028 `partial` types | 3 |
| CsWinRT1030 `AllowUnsafeBlocks` | 3 |
| IL2026: source-generated JSON, current payloads pinned first | 4, 5, 6 |
| AngleSharp trim review | 7 |
| Referenced projects (Core, Infrastructure) fixed the same way | 4, 5 |
| Sections 3 and 4 (crash, trimmed exe, launch limit) | PR b2 |
