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
        runId = 'r1'; observedAt = '2026-10-12T00:00:00.5000000Z'; osBuild = '10.0.26100.0'
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
        # No processCleanupErrors: the installer sets it only after a cleanup error.
        finishedAt = '2026-10-12T00:05:00.2500000Z'
        applicationEventCount = 1
        applicationEvents = @([ordered]@{ observedAt = '2026-10-12T00:04:00.1000000Z'; provider = '.NET Runtime'; eventId = 1026
            message = "Application: DesktopGuides.Production.exe`nat C:\Users\someone\x" })
        providers = [ordered]@{ source = 'fixtures' }
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
    Assert-True ($text.Contains("- $pass Providers: fixtures")) 'The providers line is missing.'
    Assert-True ($text.Contains("- $pass No test-owned scheduled task or process left")) 'A report without cleanup errors showed one.'
    Assert-True ($text.Contains('Started 2026-10-12T00:00:00Z, finished 2026-10-12T00:05:00Z')) 'The run times are missing.'
    Assert-True ($text.Contains("- $fail Application event log entries: 1")) 'The event count is missing.'
    Assert-True ($text.Contains('  - 2026-10-12T00:04:00Z .NET Runtime 1026: Application: DesktopGuides.Production.exe')) 'The event was not listed.'
    Assert-True ($text.Contains("``tests/fixtures/p0/txt-a.txt`` (txt-a)")) 'The manifest fixture lacks its ID.'
    $folderHash = Get-FolderHash (Join-Path $fixtures 'p1\html-long')
    Assert-True ($text.Contains("``tests/fixtures/p1/html-long`` ``$folderHash``")) 'The folder fixture hash is wrong.'

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
    Assert-True ($rcText.Contains("- $pass Release certificate not left trusted by the run")) 'The release trust cleanup line is missing.'

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

    # Anonymisation reaches nested strings.
    $json = Get-Content -LiteralPath (Join-Path $out 'evidence.json') -Raw -Encoding UTF8
    Assert-True (-not $json.Contains('someone')) 'A user name leaked into evidence.json.'
    # JSON doubles each backslash.
    Assert-True (-not $json.Contains($env:LOCALAPPDATA.Replace('\', '\\'))) 'LOCALAPPDATA leaked into evidence.json.'
    Assert-True ($json.Contains('"seconds":  12.5') -or $json.Contains('"seconds": 12.5')) 'A number did not survive anonymisation.'
    Assert-True ($json.Contains('%USERPROFILE%') -and $json.Contains('%LOCALAPPDATA%')) 'Paths were not rewritten.'
    Assert-True (-not $text.Contains('someone')) 'A user name leaked into evidence.md.'
    Assert-True ($json -match '"observedAt":\s*"2026-10-12T00:00:00\.5') 'evidence.json lost the ISO start time.'

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
    Assert-True ($text.Contains("- $fail No scenario timings recorded")) 'A missing report printed a blank scenario.'

    # A report cut off mid-write is a failed line, not a failed step.
    $cut = Join-Path $root 'cut.json'
    Set-Content -LiteralPath $cut -Value '{"runId": "r1", "smokeRuns": [' -Encoding UTF8
    $text = Get-Content -LiteralPath (& $writer -ReportPath $cut -FixtureRoot $fixtures -OutDirectory (Join-Path $root 'out3')) -Raw -Encoding UTF8
    Assert-True ($text.Contains("- $fail Unreadable installer report (cut.json)")) 'A truncated report was not reported.'

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
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'write_shell_evidence: all cases passed.'
