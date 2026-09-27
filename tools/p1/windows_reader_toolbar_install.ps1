param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory,

    [switch] $SimulateSmokeTimeoutAfterInstall,

    [switch] $SimulateProcessExitDuringInspection
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ($SimulateProcessExitDuringInspection -and
    -not $SimulateSmokeTimeoutAfterInstall) {
    throw 'Process-exit simulation requires the installed timeout fixture.'
}
$packageName = Split-Path $PackagePath -Leaf
$packageMatch = [regex]::Match($packageName,
    '^DesktopGuides\.ReaderToolbarSmoke_(?<version>[0-9]+(?:\.[0-9]+){3})_x64\.msix$')
if (-not $packageMatch.Success) {
    throw "Expected an x64 toolbar test MSIX, got $packageName."
}
$expectedVersion = $packageMatch.Groups['version'].Value
if (-not @(Get-Process explorer -ErrorAction SilentlyContinue |
    Where-Object {
        $_.SessionId -eq [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    })) {
    throw 'Toolbar install test requires an interactive desktop session.'
}
if (Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke) {
    throw 'The toolbar test package is already installed.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Toolbar install test requires an elevated runner account.'
}
New-Item -ItemType Directory -Force $ResultDirectory | Out-Null
$ResultDirectory = (Resolve-Path $ResultDirectory).Path
$signed = Join-Path $ResultDirectory 'reader-toolbar-signed-x64.msix'
$public = Join-Path $ResultDirectory 'test-certificate.cer'
$smokeResult = Join-Path $ResultDirectory 'toolbar-ui.json'
$pauseMarker = Join-Path $ResultDirectory 'installed-before-timeout.txt'
$invocationId = [Guid]::NewGuid().ToString('N')
$taskName = 'DesktopGuides-P1-Toolbar-' + $invocationId
$report = [ordered]@{
    invocationId = $invocationId
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    sourcePackageSha256 = (Get-FileHash $PackagePath -Algorithm SHA256).Hash
    parentStoppedProcess = $false
    parentRemovedPackage = $false
    simulatedProcessExit = $false
    success = $false
}
$certificate = $null
$imported = $false
$registered = $false
. (Join-Path $PSScriptRoot 'windows_reader_toolbar_smoke_result.ps1')

try {
    Copy-Item -LiteralPath $PackagePath -Destination $signed -Force
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

    $signTool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' `
        -Recurse -Filter signtool.exe |
        Where-Object { $_.FullName -like '*\x64\signtool.exe' } |
        Sort-Object FullName |
        Select-Object -Last 1 -ExpandProperty FullName
    if (-not $signTool) { throw 'Windows SDK SignTool is unavailable.' }
    & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $signed
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE."
    }
    & $signTool verify /pa $signed
    if ($LASTEXITCODE -ne 0) {
        throw 'Signed toolbar MSIX verification failed.'
    }
    $report.signedPackageSha256 = (Get-FileHash $signed -Algorithm SHA256).Hash

    $script = Join-Path $PSScriptRoot 'windows_reader_toolbar_ui_smoke.ps1'
    $arguments = '-NoProfile -NonInteractive -Sta -WindowStyle Hidden ' +
        '-ExecutionPolicy Bypass -File "' + $script + '"' +
        ' -PackagePath "' + $signed + '"' +
        ' -ResultPath "' + $smokeResult + '"' +
        ' -InvocationId ' + $invocationId
    if ($SimulateSmokeTimeoutAfterInstall) {
        $arguments += ' -PauseAfterInstallPath "' + $pauseMarker + '"'
    }
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument $arguments -WorkingDirectory $PSScriptRoot
    $interactive = New-ScheduledTaskPrincipal -UserId $env:USERNAME `
        -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $taskName -Action $action `
        -Principal $interactive -Force | Out-Null
    $registered = $true
    Clear-ToolbarSmokeResult $smokeResult
    Remove-Item -LiteralPath $pauseMarker -ErrorAction SilentlyContinue
    Start-ScheduledTask -TaskName $taskName
    if ($SimulateSmokeTimeoutAfterInstall) {
        $deadline = (Get-Date).AddSeconds(30)
        do {
            if (Test-Path -LiteralPath $pauseMarker) { break }
            if (Test-Path -LiteralPath $smokeResult) {
                throw 'Toolbar smoke ended before the timeout fixture installed.'
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        if (-not (Test-Path -LiteralPath $pauseMarker)) {
            throw 'Toolbar timeout fixture did not finish installing.'
        }
        throw 'Simulated toolbar smoke timeout after installation.'
    }
    $deadline = (Get-Date).AddSeconds(180)
    do {
        if (Test-Path -LiteralPath $smokeResult) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    if (-not (Test-Path -LiteralPath $smokeResult)) {
        throw 'Interactive toolbar smoke timed out.'
    }
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $task = Get-ScheduledTask -TaskName $taskName
        if ($task.State -eq 'Ready') { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    if ($task.State -ne 'Ready') {
        throw 'Interactive toolbar smoke task did not finish.'
    }
    $smoke = Read-ToolbarSmokeResult $smokeResult $invocationId `
        ([System.Diagnostics.Process]::GetCurrentProcess().SessionId)
    if (-not $smoke.success) {
        throw "Interactive toolbar smoke failed: $($smoke.error)"
    }
    $report.phases = $smoke.phases
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
}
finally {
    $taskReady = $true
    if ($registered) {
        $task = Get-ScheduledTask -TaskName $taskName `
            -ErrorAction SilentlyContinue
        if ($task -and $task.State -ne 'Ready') {
            Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        }
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $task = Get-ScheduledTask -TaskName $taskName `
                -ErrorAction SilentlyContinue
            if (-not $task -or $task.State -eq 'Ready') { break }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        if ($task -and $task.State -ne 'Ready') {
            $taskReady = $false
            $report.taskCleanupError =
                'Interactive toolbar smoke task is still active.'
        }
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false `
            -ErrorAction SilentlyContinue
    }
    $installedNow = @(Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke)
    $ownedPackage = @($installedNow | Where-Object {
        $_.Version.ToString() -eq $expectedVersion -and
        $_.Architecture.ToString() -eq 'X64'
    })
    if ($installedNow.Count -gt 0 -and
        ($installedNow.Count -ne 1 -or $ownedPackage.Count -ne 1)) {
        $report.packageCleanupError =
            'A toolbar package not confirmed as test-owned remains untouched.'
    }
    elseif ($ownedPackage.Count -eq 1 -and $taskReady) {
        $expectedExecutable = Join-Path $ownedPackage[0].InstallLocation `
            'DesktopGuides.ReaderToolbarSmoke.exe'
        foreach ($process in @(Get-Process -Name DesktopGuides.ReaderToolbarSmoke `
            -ErrorAction SilentlyContinue)) {
            try {
                if (-not [string]::Equals($process.MainModule.FileName,
                    $expectedExecutable,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }
                if ($SimulateProcessExitDuringInspection -and
                    -not $report.simulatedProcessExit) {
                    $process.Kill()
                    if (-not $process.WaitForExit(10000)) {
                        throw "Toolbar process $($process.Id) did not exit."
                    }
                    $report.parentStoppedProcess = $true
                    $report.simulatedProcessExit = $true
                    throw [InvalidOperationException]::new(
                        'Simulated process exit during module inspection.')
                }
                if (-not $process.HasExited) {
                    $process.Kill()
                    if (-not $process.WaitForExit(10000)) {
                        throw "Toolbar process $($process.Id) did not exit."
                    }
                    $report.parentStoppedProcess = $true
                }
            }
            catch {
                $report.processCleanupError = $_ | Out-String
            }
        }
        try {
            Remove-AppxPackage -Package $ownedPackage[0].PackageFullName `
                -ErrorAction Stop
            $report.parentRemovedPackage = $true
        }
        catch {
            $report.packageCleanupError = $_ | Out-String
        }
    }
    if ($certificate) {
        if ($imported) {
            Remove-Item "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)" `
                -ErrorAction SilentlyContinue
        }
        Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)" `
            -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $public -ErrorAction SilentlyContinue
    $report.packageStillInstalled =
        [bool](Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke)
    $report.processStillRunning = [bool](
        Get-Process -Name DesktopGuides.ReaderToolbarSmoke `
            -ErrorAction SilentlyContinue)
    $report.certificateStillTrusted = if ($certificate) {
        Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
    } else { $false }
    $report.taskStillRegistered =
        [bool](Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)
    if ($report.packageStillInstalled -or $report.processStillRunning -or
        $report.certificateStillTrusted -or $report.taskStillRegistered -or
        -not $taskReady -or $report.packageCleanupError -or
        $report.processCleanupError) {
        $report.success = $false
    }
    $report | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $ResultDirectory 'signed-install.json') `
            -Encoding UTF8
}
if (-not $report.success) { exit 1 }
