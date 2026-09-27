param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory,

    [switch] $SimulateSmokeTimeoutAfterInstall,

    [switch] $SimulateProcessExitDuringInspection,

    [string] $PauseTimeoutCleanupUntilPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ($SimulateProcessExitDuringInspection -and
    -not $SimulateSmokeTimeoutAfterInstall) {
    throw 'Process-exit simulation requires the installed timeout fixture.'
}
if ($PauseTimeoutCleanupUntilPath -and
    -not $SimulateSmokeTimeoutAfterInstall) {
    throw 'Cleanup pause requires the installed timeout fixture.'
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
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Toolbar install test requires an elevated runner account.'
}
$lockDirectory = Join-Path $env:LOCALAPPDATA 'DesktopGuides.ReaderToolbarSmoke'
New-Item -ItemType Directory -Force $lockDirectory | Out-Null
$lockPath = Join-Path $lockDirectory 'install.lock'
try {
    $toolbarInstallLock = [System.IO.File]::Open(
        $lockPath, [System.IO.FileMode]::OpenOrCreate,
        [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
}
catch {
    throw "Toolbar install lock is unavailable: $($_.Exception.Message)"
}
try {
if (Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke) {
    throw 'The toolbar test package is already installed.'
}
New-Item -ItemType Directory -Force $ResultDirectory | Out-Null
$ResultDirectory = (Resolve-Path $ResultDirectory).Path
$signed = Join-Path $ResultDirectory 'reader-toolbar-signed-x64.msix'
$public = Join-Path $ResultDirectory 'test-certificate.cer'
$smokeResult = Join-Path $ResultDirectory 'toolbar-ui.json'
$processHandoff = Join-Path $ResultDirectory 'toolbar-process.json'
$processHandoffAck = "$processHandoff.ack"
$pauseMarker = Join-Path $ResultDirectory 'installed-before-timeout.txt'
$invocationId = [Guid]::NewGuid().ToString('N')
$taskName = 'DesktopGuides-P1-Toolbar-' + $invocationId
$report = [ordered]@{
    invocationId = $invocationId
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    sourcePackageSha256 = (Get-FileHash $PackagePath -Algorithm SHA256).Hash
    parentStoppedProcess = $false
    parentRemovedPackage = $false
    processHandleAcquired = $false
    simulatedProcessExit = $false
    success = $false
}
$certificate = $null
$imported = $false
$registered = $false
$ownedProcess = $null
$handoff = $null
if (-not ('DesktopGuidesOwnedProcess' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'windows_owned_process.cs')
}
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
        ' -InvocationId ' + $invocationId +
        ' -ProcessHandoffPath "' + $processHandoff + '"'
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
    Remove-Item -LiteralPath $pauseMarker, $processHandoff,
        "$processHandoff.tmp", $processHandoffAck,
        "$processHandoffAck.tmp" -ErrorAction SilentlyContinue
    Start-ScheduledTask -TaskName $taskName
    $handoffDeadline = (Get-Date).AddSeconds(30)
    do {
        if (Test-Path -LiteralPath $processHandoff) { break }
        if (Test-Path -LiteralPath $smokeResult) {
            throw 'Interactive toolbar smoke ended before process handoff.'
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $handoffDeadline)
    if (-not (Test-Path -LiteralPath $processHandoff)) {
        throw 'Interactive toolbar smoke did not provide a process handoff.'
    }
    $handoffCandidate = Get-Content -LiteralPath $processHandoff -Raw |
        ConvertFrom-Json
    $installedForHandoff =
        @(Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke)
    if ($installedForHandoff.Count -ne 1 -or
        $installedForHandoff[0].Version.ToString() -ne $expectedVersion -or
        $installedForHandoff[0].Architecture.ToString() -ne 'X64') {
        throw 'Interactive toolbar handoff has no matching installed package.'
    }
    $expectedExecutable = Join-Path $installedForHandoff[0].InstallLocation `
        'DesktopGuides.ReaderToolbarSmoke.exe'
    if ($handoffCandidate.invocationId -ne $invocationId -or
        $handoffCandidate.handoffToken -notmatch '^[0-9a-f]{32}$' -or
        $handoffCandidate.packageFullName -ne
            $installedForHandoff[0].PackageFullName -or
        $handoffCandidate.sessionId -ne
            [System.Diagnostics.Process]::GetCurrentProcess().SessionId -or
        -not [string]::Equals([string]$handoffCandidate.executablePath,
            $expectedExecutable,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Interactive toolbar process handoff identity did not match.'
    }
    $ownedProcess = [DesktopGuidesOwnedProcess]::OpenVerified(
        [int]$handoffCandidate.processId, [datetime]$handoffCandidate.startedAt,
        [int]$handoffCandidate.sessionId, $expectedExecutable)
    if (-not $ownedProcess) {
        throw 'Toolbar app exited before its process handoff was accepted.'
    }
    $handoff = $handoffCandidate
    $report.processHandleAcquired = $true
    @{ handoffToken = $handoff.handoffToken } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath "$processHandoffAck.tmp" -Encoding UTF8
    Move-Item -LiteralPath "$processHandoffAck.tmp" `
        -Destination $processHandoffAck -Force
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
        if ($PauseTimeoutCleanupUntilPath) {
            $deadline = (Get-Date).AddSeconds(45)
            do {
                if (Test-Path -LiteralPath $PauseTimeoutCleanupUntilPath) {
                    break
                }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            if (-not (Test-Path -LiteralPath $PauseTimeoutCleanupUntilPath)) {
                throw 'Toolbar timeout cleanup hold was not released.'
            }
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
    if (-not $smoke.handoffAcknowledged -or
        $smoke.processId -ne $handoff.processId) {
        throw 'Interactive toolbar smoke did not retain the verified process.'
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
    if (-not $report.processHandleAcquired -and
        ($installedNow.Count -gt 0 -or
            (Test-Path -LiteralPath $pauseMarker))) {
        $report.processCleanupError =
            'Interactive toolbar smoke had no verified process handoff.'
    }
    if ($ownedProcess -and $taskReady) {
        try {
            if ($SimulateProcessExitDuringInspection -and
                -not $report.simulatedProcessExit) {
                if (-not $ownedProcess.TerminateAndWait(10000)) {
                    throw "Toolbar process $($handoff.processId) did not exit."
                }
                $report.parentStoppedProcess = $true
                $report.simulatedProcessExit = $true
                throw [InvalidOperationException]::new(
                    'Simulated process exit during module inspection.')
            }
            if (-not $ownedProcess.HasExited) {
                if (-not $ownedProcess.TerminateAndWait(10000)) {
                    throw "Toolbar process $($handoff.processId) did not exit."
                }
                $report.parentStoppedProcess = $true
            }
        }
        catch {
            $report.processCleanupError = $_ | Out-String
        }
    }
    if ($installedNow.Count -gt 0 -and
        ($installedNow.Count -ne 1 -or $ownedPackage.Count -ne 1)) {
        $report.packageCleanupError =
            'A toolbar package not confirmed as test-owned remains untouched.'
    }
    elseif ($ownedPackage.Count -eq 1 -and $taskReady) {
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
    try {
        $report.processStillRunning =
            [bool]($ownedProcess -and -not $ownedProcess.HasExited)
    }
    catch {
        $report.processStillRunning = $true
        $report.processCleanupError = $_ | Out-String
    }
    finally {
        if ($ownedProcess) { $ownedProcess.Dispose() }
    }
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
}
finally {
    $toolbarInstallLock.Dispose()
}
if (-not $report.success) { exit 1 }
