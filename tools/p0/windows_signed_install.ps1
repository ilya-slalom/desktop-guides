param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory,

    [string] $OfflineNetworkAdapter
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$packageName = Split-Path $PackagePath -Leaf
if ($packageName -notmatch '^DesktopGuides\.App_[0-9]+(\.[0-9]+){3}_(x64|ARM64)\.msix$') {
    throw "Expected an architecture-specific Desktop Guides app MSIX, got $packageName."
}
$packageArchitecture = $Matches[2].ToLowerInvariant()
$report = [ordered]@{
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    osBuild = [Environment]::OSVersion.Version.ToString()
    cpuArchitecture = $env:PROCESSOR_ARCHITECTURE
    packageArchitecture = $packageArchitecture
    success = $false
}
$certificate = $null
$installed = $null
$imported = $false
$launchTask = 'DesktopGuides-P0-InstalledLaunch'
$suiteTask = 'DesktopGuides-P0-InstalledSuite'
$watchdogTask = 'DesktopGuides-P0-NetworkRestore'
$networkWasDisabled = $false
New-Item -ItemType Directory -Force $ResultDirectory | Out-Null
$ResultDirectory = (Resolve-Path $ResultDirectory).Path
$signed = Join-Path $ResultDirectory "desktop-guides-signed-$packageArchitecture.msix"
$public = Join-Path $ResultDirectory 'test-certificate.cer'
$suiteDirectory = Join-Path $ResultDirectory 'ui-suite'

if (Get-AppxPackage -Name DesktopGuides.ReaderSpike) {
    throw 'A Desktop Guides package is already installed; refusing to replace it.'
}

try {
    $report.windowsAppRuntime = @(
        Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2*' |
            Where-Object { $_.Architecture -eq $packageArchitecture } |
            Select-Object -ExpandProperty Version |
            ForEach-Object { $_.ToString() }
    )
    $runtimeFolder = 'C:\Program Files (x86)\Microsoft\EdgeWebView\Application'
    $report.webView2Versions = if (Test-Path $runtimeFolder) {
        @(Get-ChildItem $runtimeFolder -Directory | Select-Object -ExpandProperty Name)
    } else { @() }
    $report.dotnetSdk = (dotnet --version)
    $report.sourcePackageSha256 = (Get-FileHash $PackagePath -Algorithm SHA256).Hash
    Copy-Item $PackagePath $signed -Force

    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject 'CN=DesktopGuides Development' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyExportPolicy NonExportable `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddDays(1)
    $report.certificateThumbprint = $certificate.Thumbprint
    Export-Certificate -Cert $certificate -FilePath $public | Out-Null
    Import-Certificate -FilePath $public `
        -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    $imported = $true

    $signTools = @(Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' `
        -Recurse -Filter signtool.exe)
    $signTool = $signTools |
        Where-Object { $_.FullName -like "*\$packageArchitecture\signtool.exe" } |
        Sort-Object FullName |
        Select-Object -Last 1 -ExpandProperty FullName
    if (-not $signTool -and $packageArchitecture -eq 'arm64') {
        $signTool = $signTools |
            Where-Object { $_.FullName -like '*\x64\signtool.exe' } |
            Sort-Object FullName |
            Select-Object -Last 1 -ExpandProperty FullName
    }
    if (-not $signTool) {
        throw 'Windows SDK SignTool is unavailable.'
    }
    & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $signed
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE." }
    & $signTool verify /pa $signed
    if ($LASTEXITCODE -ne 0) { throw "Signature verification failed with exit code $LASTEXITCODE." }
    $report.signedPackageSha256 = (Get-FileHash $signed -Algorithm SHA256).Hash

    Add-AppxPackage -Path $signed
    $installed = Get-AppxPackage -Name DesktopGuides.ReaderSpike
    if (-not $installed) { throw 'Add-AppxPackage returned but the app is not installed.' }
    $report.packageFullName = $installed.PackageFullName
    $report.packageFamilyName = $installed.PackageFamilyName
    $report.installLocation = $installed.InstallLocation

    Get-CimInstance Win32_Process -Filter "Name = 'DesktopGuides.App.exe'" |
        Where-Object {
            $_.ExecutablePath -like 'E:\work\desktop-guides\artifacts\unpackaged\*'
        } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME `
        -LogonType Interactive -RunLevel Limited
    $appId = "shell:AppsFolder\$($installed.PackageFamilyName)!App"
    $launchAction = New-ScheduledTaskAction -Execute "$env:WINDIR\explorer.exe" `
        -Argument $appId
    Register-ScheduledTask -TaskName $launchTask -Action $launchAction `
        -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName $launchTask
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $appProcess = Get-CimInstance Win32_Process -Filter "Name = 'DesktopGuides.App.exe'" |
            Where-Object { $_.ExecutablePath -like "$($installed.InstallLocation)*" } |
            Select-Object -First 1
    } while (-not $appProcess -and (Get-Date) -lt $deadline)
    if (-not $appProcess) { throw "Installed app did not launch through $appId." }
    $report.launchedProcessId = $appProcess.ProcessId
    $report.launchedSessionId = $appProcess.SessionId
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        $windowProcess = Get-Process DesktopGuides.App -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -eq $appProcess.ProcessId -and $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1
    } while (-not $windowProcess -and (Get-Date) -lt $deadline)
    if (-not $windowProcess) { throw 'Installed app process has no interactive window.' }

    if ($OfflineNetworkAdapter) {
        $adapter = Get-NetAdapter -Name $OfflineNetworkAdapter -ErrorAction Stop
        if ($adapter.Status -ne 'Up') {
            throw "Network adapter '$OfflineNetworkAdapter' was not up before the offline test."
        }
        $watchdogScript = Join-Path $ResultDirectory 'restore-network.ps1'
        $escapedName = $OfflineNetworkAdapter.Replace("'", "''")
        @"
`$ErrorActionPreference = 'Stop'
Enable-NetAdapter -Name '$escapedName' -Confirm:`$false
"@ | Set-Content $watchdogScript -Encoding UTF8
        $watchdogAction = New-ScheduledTaskAction -Execute 'powershell.exe' `
            -Argument ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
                $watchdogScript + '"')
        $watchdogTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(5)
        $watchdogPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' `
            -LogonType ServiceAccount -RunLevel Highest
        Register-ScheduledTask -TaskName $watchdogTask -Action $watchdogAction `
            -Trigger $watchdogTrigger -Principal $watchdogPrincipal -Force | Out-Null

        Stop-Process -Id $appProcess.ProcessId -Force
        $networkWasDisabled = $true
        $report.offlineStartedAt = (Get-Date).ToUniversalTime().ToString('o')
        $report.offlineAdapter = $OfflineNetworkAdapter
        Disable-NetAdapter -Name $OfflineNetworkAdapter -Confirm:$false
        Start-ScheduledTask -TaskName $launchTask
        $deadline = (Get-Date).AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 500
            $offlineProcess = Get-CimInstance Win32_Process -Filter "Name = 'DesktopGuides.App.exe'" |
                Where-Object { $_.ExecutablePath -like "$($installed.InstallLocation)*" } |
                Select-Object -First 1
        } while (-not $offlineProcess -and (Get-Date) -lt $deadline)
        if (-not $offlineProcess) { throw 'Installed app did not relaunch offline.' }
        $report.offlineRelaunchedProcessId = $offlineProcess.ProcessId
    }

    $suiteScript = Join-Path $PSScriptRoot 'windows_ui_suite.ps1'
    $arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass ' +
        '-File "' + $suiteScript + '" -ResultDirectory "' + $suiteDirectory + '"'
    $suiteAction = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument $arguments -WorkingDirectory (Split-Path $suiteScript)
    Register-ScheduledTask -TaskName $suiteTask -Action $suiteAction `
        -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName $suiteTask
    $summary = Join-Path $suiteDirectory 'suite.json'
    $deadline = (Get-Date).AddMinutes(4)
    do {
        Start-Sleep -Seconds 2
    } while (-not (Test-Path $summary) -and (Get-Date) -lt $deadline)
    if (-not (Test-Path $summary)) { throw 'Installed UI suite timed out.' }
    $suiteResults = Get-Content $summary -Raw | ConvertFrom-Json
    $fixtureResults = [System.Collections.Generic.List[object]]::new()
    $failed = 0
    foreach ($entry in $suiteResults) {
        $fixtureResults.Add([ordered]@{
            fixture = $entry.fixture
            passed = [bool]$entry.passed
        })
        if (-not $entry.passed) { $failed++ }
    }
    $report.fixtureResults = $fixtureResults
    if ($fixtureResults.Count -ne 14 -or $failed -gt 0) {
        throw 'Installed UI suite reported a failing fixture.'
    }
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
}
finally {
    Stop-ScheduledTask -TaskName $suiteTask -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $suiteTask -Confirm:$false -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $launchTask -Confirm:$false -ErrorAction SilentlyContinue
    if ($installed) {
        Get-CimInstance Win32_Process -Filter "Name = 'DesktopGuides.App.exe'" |
            Where-Object { $_.ExecutablePath -like "$($installed.InstallLocation)*" } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
        Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction SilentlyContinue
    }
    if ($certificate) {
        if ($imported) {
            Remove-Item "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)" `
                -ErrorAction SilentlyContinue
        }
        Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)" `
            -ErrorAction SilentlyContinue
    }
    Remove-Item $public -ErrorAction SilentlyContinue
    if ($networkWasDisabled) {
        try {
            Enable-NetAdapter -Name $OfflineNetworkAdapter -Confirm:$false
            $deadline = (Get-Date).AddSeconds(60)
            do {
                Start-Sleep -Seconds 2
                $adapter = Get-NetAdapter -Name $OfflineNetworkAdapter
            } while ($adapter.Status -ne 'Up' -and (Get-Date) -lt $deadline)
            if ($adapter.Status -ne 'Up') {
                throw "Network adapter '$OfflineNetworkAdapter' did not return to Up."
            }
            $report.offlineEndedAt = (Get-Date).ToUniversalTime().ToString('o')
            Unregister-ScheduledTask -TaskName $watchdogTask -Confirm:$false `
                -ErrorAction SilentlyContinue
        }
        catch {
            $report.success = $false
            $report.cleanupError = 'Network restore failed; the scheduled watchdog remains enabled.'
        }
    }
    else {
        Unregister-ScheduledTask -TaskName $watchdogTask -Confirm:$false `
            -ErrorAction SilentlyContinue
    }
    $report.packageStillInstalled = [bool](Get-AppxPackage -Name DesktopGuides.ReaderSpike)
    $report.certificateStillTrusted = if ($certificate) {
        Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
    } else { $false }
    if ($report.packageStillInstalled -or $report.certificateStillTrusted) {
        $report.success = $false
        $report.cleanupError = 'Temporary package or certificate remains; remove it manually.'
    }
    $report | ConvertTo-Json -Depth 8 |
        Set-Content (Join-Path $ResultDirectory 'signed-install.json') -Encoding UTF8
}
if (-not $report.success) { exit 1 }
