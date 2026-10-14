# T17.2: turns one installed-shell report into evidence.md and evidence.json.
# It never fails the run: missing data becomes a failed line.
param(
    [Parameter(Mandatory = $true)] [string] $ReportPath,
    [Parameter(Mandatory = $true)] [string] $FixtureRoot,
    [Parameter(Mandatory = $true)] [string] $OutDirectory,
    [string] $Commit = $env:GITHUB_SHA,
    [string] $RunId = $env:GITHUB_RUN_ID,
    [string] $Shard = '',
    # The portable zip's SHA-256 as the CI verify step checked it; reported only.
    [string] $PortableZipSha256 = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_lane.ps1')
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
    # PowerShell 7's ConvertFrom-Json turns ISO times into DateTime values.
    if ($value -is [datetime]) { return $value.ToUniversalTime().ToString('o') }
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

# A list field the report may leave out; missing reads as empty, not one null.
function Items($report, [string] $name) {
    return @(Field $report $name | Where-Object { $null -ne $_ })
}

# One form for times on both shells, to the second.
function Format-Time($value) {
    if ($null -eq $value -or '' -eq "$value") { return '' }
    $time = if ($value -is [datetime]) { $value } else {
        [DateTime]::Parse("$value", [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind)
    }
    return $time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
}

New-Item -ItemType Directory -Force $OutDirectory | Out-Null
$reportName = Split-Path -Leaf $ReportPath
$report = $null
$reportUnreadable = $false
if (Test-Path -LiteralPath $ReportPath) {
    # A job killed mid-write leaves a cut-off file; that is a failed line too.
    try { $report = Get-Content -LiteralPath $ReportPath -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { $reportUnreadable = $true }
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("## Installed shell evidence$(if ($Shard) { ": $Shard" })")
$lines.Add('')
$lines.Add('### Run')
$lines.Add((Line ([bool] $Commit) "Commit ``$Commit``"))
$lines.Add((Line ([bool] $RunId) "Run ``$RunId``"))
if (-not $report) {
    $lines.Add((Line $false "$(if ($reportUnreadable) { 'Unreadable' } else { 'No' }) installer report ($reportName)"))
}
else {
    $lines.Add((Line $true "Groups ``$((@(Field $report 'scenarioGroupsRun')) -join ', ')`` (scope ``$(Field $report 'scenarioScope')``)"))
    $lines.Add((Line ([bool] (Field $report 'finishedAt')) "Started $(Format-Time (Field $report 'observedAt')), finished $(Format-Time (Field $report 'finishedAt'))"))
    $lines.Add((Line ([bool] (Field $report 'success')) "Run result: $(if (Field $report 'success') { 'passed' } else { 'failed' })"))
    $providers = Field $report 'providers'
    if ($providers -and $providers.PSObject.Properties['source']) {
        $lines.Add((Line $true "Providers: $($providers.source)"))
    }
    if (Field $report 'error') { $lines.Add((Line $false ("Error: " + (Hide-Paths ("$(Field $report 'error')" -split "`n")[0])))) }

    $lines.Add(''); $lines.Add('### Target')
    $lines.Add((Line ([bool] (Field $report 'osBuild')) "Windows build ``$(Field $report 'osBuild')``, CPU ``$(Field $report 'cpuArchitecture')``"))
    $lines.Add((Line ([bool] (Field $report 'dotnetSdk')) ".NET SDK ``$(Field $report 'dotnetSdk')``"))
    if ((Field $report 'mode') -eq 'portable') {
        $lines.Add((Line $true 'Windows App Runtime bundled in the portable exe'))
    }
    else {
        $lines.Add((Line ([bool] (Field $report 'windowsAppRuntime')) "Windows App Runtime ``$((@(Field $report 'windowsAppRuntime') | ForEach-Object { "$_" }) -join '; ')``"))
    }
    $lines.Add((Line ([bool] (Field $report 'webView2Version')) "WebView2 Runtime ``$(Field $report 'webView2Version')``"))

    $lines.Add(''); $lines.Add('### Package')
    $lines.Add((Line $true "Mode ``$(Field $report 'mode')``"))
    if ((Field $report 'mode') -ne 'portable') {
        $lines.Add((Line ([bool] (Field $report 'packageFullName')) "Package ``$(Field $report 'packageFullName')``"))
        $lines.Add((Line ([bool] (Field $report 'sourcePackageSha256')) "Source MSIX SHA-256 ``$(Field $report 'sourcePackageSha256')``"))
        $lines.Add((Line ([bool] (Field $report 'signedPackageSha256')) "Signed MSIX SHA-256 ``$(Field $report 'signedPackageSha256')``"))
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
    }
    else {
        $lines.Add((Line ([bool] (Field $report 'portableExecutableSha256')) "Portable exe SHA-256 ``$(Field $report 'portableExecutableSha256')``"))
        $lines.Add((Line ([bool] $PortableZipSha256) "Portable zip SHA-256 ``$PortableZipSha256``"))
    }
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

$runs = Items $report 'smokeRuns'
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
        $trustLabel = if (Field $report 'releaseSigned') { 'Release certificate not left trusted by the run' } else { 'Test certificate no longer trusted' }
        $lines.Add((Line (-not (Field $report 'certificateStillTrusted')) $trustLabel))
    }
    $processErrors = Items $report 'processCleanupErrors'
    $lines.Add((Line ($processErrors.Count -eq 0) "No test-owned scheduled task or process left$(if ($processErrors.Count) { ': ' + (Hide-Paths ($processErrors -join '; ')) })"))
    if ($report.PSObject.Properties['scratchFoldersRemaining']) {
        $left = Items $report 'scratchFoldersRemaining'
        $lines.Add((Line ($left.Count -eq 0) "Scratch folders removed$(if ($left.Count) { ': ' + (Hide-Paths ($left -join '; ')) })"))
    }
    $events = [int] (Field $report 'applicationEventCount')
    $lines.Add((Line ($events -eq 0) "Application event log entries: $events"))
    # The installer keeps the first eight; each is listed by its first line.
    foreach ($entry in Items $report 'applicationEvents') {
        $first = Hide-Paths ("$($entry.message)" -split "`r?`n")[0]
        $lines.Add("  - $(Format-Time $entry.observedAt) $($entry.provider) $($entry.eventId): $first")
    }
}

$markdown = Join-Path $OutDirectory 'evidence.md'
($lines -join "`n") + "`n" | Set-Content -LiteralPath $markdown -Encoding UTF8 -NoNewline
[ordered]@{
    shard = $Shard; commit = $Commit; runId = $RunId; reportFound = [bool] $report
    portableZipSha256 = $PortableZipSha256
    report = Hide-Value $report; fixtures = $fixtures
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutDirectory 'evidence.json') -Encoding UTF8
return $markdown
