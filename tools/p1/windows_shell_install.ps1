param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory,

    [switch] $DesignOnly
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$targetSessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
if ($targetSessionId -eq 0 -or
    -not @(Get-Process explorer -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $targetSessionId })) {
    throw 'Shell install test requires an interactive desktop session.'
}
$packageName = Split-Path $PackagePath -Leaf
if ($packageName -notmatch '^DesktopGuides\.Production_[0-9]+(\.[0-9]+){3}_x64\.msix$') {
    throw "Expected an x64 production MSIX, got $packageName."
}
if (Get-AppxPackage -Name DesktopGuides.Preview) {
    throw 'A production preview package is already installed; refusing to replace it.'
}
. (Join-Path $PSScriptRoot 'windows_shell_profile.ps1')
Assert-FreshPreviewProfile $env:LOCALAPPDATA
. (Join-Path $PSScriptRoot 'windows_shell_smoke_result.ps1')
. (Join-Path $PSScriptRoot 'windows_shell_task_cleanup.ps1')
. (Join-Path $PSScriptRoot 'windows_shell_screenshot_stats.ps1')

New-Item -ItemType Directory -Force $ResultDirectory | Out-Null
$ResultDirectory = (Resolve-Path $ResultDirectory).Path
$signed = Join-Path $ResultDirectory 'desktop-guides-production-signed-x64.msix'
$public = Join-Path $ResultDirectory 'test-certificate.cer'
$runId = [Guid]::NewGuid().ToString('N')
$report = [ordered]@{
    runId = $runId
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    osBuild = [Environment]::OSVersion.Version.ToString()
    cpuArchitecture = $env:PROCESSOR_ARCHITECTURE
    success = $false
}
$certificate = $null
$installed = $null
$expectedExecutablePath = $null
$imported = $false
. (Join-Path $PSScriptRoot 'windows_shell_process.ps1')
$ownedProcesses = [System.Collections.Generic.Dictionary[int,DesktopGuidesOwnedProcess]]::new()
$cleanupErrors = [System.Collections.Generic.List[string]]::new()
$launchTask = "DesktopGuides-P1-ShellLaunch-$runId"
$secondLaunchTask = "DesktopGuides-P1-ShellSecondLaunch-$runId"
$smokeTask = "DesktopGuides-P1-ShellSmoke-$runId"
$launchResultPath = Join-Path $ResultDirectory 'launch.json'
$secondLaunchResultPath = Join-Path $ResultDirectory 'second-launch.json'

function Get-InstalledShellProcesses {
    if (-not $installed) { return @() }
    Get-CimInstance Win32_Process -Filter "Name = 'DesktopGuides.Production.exe'" |
        Where-Object {
            $_.SessionId -eq $targetSessionId -and
            [string]::Equals($_.ExecutablePath, $expectedExecutablePath,
                [StringComparison]::OrdinalIgnoreCase)
        }
}

function Wait-ScheduledTaskIdle(
    [string] $TaskName,
    [int] $TimeoutSeconds,
    [switch] $RequireRegistered) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $task = Get-ScheduledTask -TaskName $TaskName `
            -ErrorAction SilentlyContinue
        if (($task -and $task.State -eq 'Ready') -or
            (-not $task -and -not $RequireRegistered)) {
            return $true
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Clear-LaunchResult([string] $ResultPath) {
    Remove-Item -LiteralPath $ResultPath, "$ResultPath.tmp",
        "$ResultPath.ack", "$ResultPath.ack.tmp" `
        -ErrorAction SilentlyContinue
}

function Wait-LaunchResult(
    [string] $ResultPath,
    [string] $TaskName) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
    } while (-not (Test-Path $ResultPath) -and (Get-Date) -lt $deadline)
    if (-not (Test-Path $ResultPath)) {
        throw "Interactive launch did not write $ResultPath."
    }
    $launch = Get-Content $ResultPath -Raw | ConvertFrom-Json
    if (-not $launch.success) {
        throw "Interactive launch failed: $($launch.error)"
    }
    if ($launch.sessionId -ne $targetSessionId -or
        -not [string]::Equals($launch.executablePath, $expectedExecutablePath,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Interactive launch returned an unexpected session or executable.'
    }
    $token = [Guid]::Empty
    if (-not [Guid]::TryParseExact(
        [string]$launch.handoffToken, 'N', [ref]$token)) {
        throw 'Interactive launch returned an invalid handoff token.'
    }
    $owned = [DesktopGuidesOwnedProcess]::OpenVerified(
        [int]$launch.processId, [datetime]$launch.startedAt,
        $targetSessionId, $expectedExecutablePath)
    if ($owned) {
        if ($ownedProcesses.ContainsKey([int]$launch.processId)) {
            $previous = $ownedProcesses[[int]$launch.processId]
            if (-not $previous.HasExited) {
                $owned.Dispose()
                throw 'Interactive launch reused the ID of a live test process.'
            }
            $previous.Dispose()
            [void]$ownedProcesses.Remove([int]$launch.processId)
        }
        $ownedProcesses[[int]$launch.processId] = $owned
    }
    $ack = @{ handoffToken = $token.ToString('N') }
    $ack | ConvertTo-Json -Compress |
        Set-Content -LiteralPath "$ResultPath.ack.tmp" -Encoding UTF8
    Move-Item -LiteralPath "$ResultPath.ack.tmp" `
        -Destination "$ResultPath.ack" -Force
    if (-not (Wait-ScheduledTaskIdle $TaskName 10 -RequireRegistered)) {
        throw "Interactive launch task $TaskName did not finish after handoff."
    }
    return $launch
}

function Wait-InstalledShellWindow([int] $ShellProcessId) {
    if (-not $ownedProcesses.ContainsKey($ShellProcessId)) {
        throw "Test-launched shell process $ShellProcessId exited before ownership handoff."
    }
    $deadline = (Get-Date).AddSeconds(30)
    do {
        if ($ownedProcesses[$ShellProcessId].HasExited) {
            throw "Test-launched shell process $ShellProcessId exited before opening a window."
        }
        $appProcess = Get-InstalledShellProcesses |
            Where-Object { $_.ProcessId -eq $ShellProcessId } |
            Select-Object -First 1
        if ($appProcess) {
            $windowProcess = Get-Process -Id $ShellProcessId `
                -ErrorAction SilentlyContinue
            if ($windowProcess -and $windowProcess.MainWindowHandle -ne 0) {
                break
            }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if (-not $appProcess -or -not $windowProcess -or
        $windowProcess.MainWindowHandle -eq 0) {
        throw "Test-launched shell process $ShellProcessId did not open a window."
    }
    $report.launchedProcessId = $appProcess.ProcessId
    $report.launchedSessionId = $appProcess.SessionId
}

function Start-InstalledShell {
    Clear-LaunchResult $launchResultPath
    Start-ScheduledTask -TaskName $launchTask
    $launch = Wait-LaunchResult $launchResultPath $launchTask
    Wait-InstalledShellWindow ([int]$launch.processId)
}

function New-ShellLaunchAction(
    [string] $ResultPath,
    [int] $ForegroundTargetProcessId = 0) {
    $script = Join-Path $PSScriptRoot 'windows_shell_launch.ps1'
    $arguments = '-NoProfile -NonInteractive -Sta -WindowStyle Hidden -ExecutionPolicy Bypass ' +
        '-File "' + $script + '"' +
        ' -ExecutablePath "' + $expectedExecutablePath + '"' +
        ' -ResultPath "' + $ResultPath + '"'
    if ($ForegroundTargetProcessId -gt 0) {
        $arguments += ' -ForegroundTargetProcessId ' +
            $ForegroundTargetProcessId
    }
    New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument $arguments -WorkingDirectory $PSScriptRoot
}

function Assert-SingleInstance {
    $firstProcessId = $report.launchedProcessId
    $foregroundAction = New-ShellLaunchAction `
        $secondLaunchResultPath $firstProcessId
    $focusResultPath = "$secondLaunchResultPath.foreground.json"
    $activationEvent = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::AutoReset,
        'Local\DesktopGuides.Preview.RedirectedActivation')
    try {
        $activationEvent.Reset() | Out-Null
        Register-ScheduledTask -TaskName $secondLaunchTask -Action $foregroundAction `
            -Principal $principal -Force | Out-Null
        Clear-LaunchResult $secondLaunchResultPath
        if (Test-Path -LiteralPath $focusResultPath) {
            Remove-Item -LiteralPath $focusResultPath -ErrorAction Stop
        }
        Start-ScheduledTask -TaskName $secondLaunchTask
        $secondLaunch = Wait-LaunchResult $secondLaunchResultPath $secondLaunchTask
        $report.secondLaunchProcessId = $secondLaunch.processId
        if (-not (Test-Path -LiteralPath $focusResultPath)) {
            throw 'Foreground launch did not record a focus result.'
        }
        $focusResult = Get-Content -LiteralPath $focusResultPath -Raw |
            ConvertFrom-Json
        if ($focusResult.handoffToken -ne $secondLaunch.handoffToken -or
            $focusResult.foregroundTargetProcessId -ne $firstProcessId -or
            -not $focusResult.success) {
            throw "Second launch did not bring the background shell forward: $(
                $focusResult | ConvertTo-Json -Compress)."
        }
        $report.backgroundActivation = $focusResult
        if (-not $activationEvent.WaitOne(15000)) {
            throw 'Original shell did not acknowledge redirected activation.'
        }
        $report.redirectedActivationObserved = $true
    }
    finally {
        $activationEvent.Dispose()
    }
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $processes = @(Get-InstalledShellProcesses)
        if ($processes.Count -eq 1 -and
            $processes[0].ProcessId -eq $firstProcessId) {
            break
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    $report.secondLaunchProcessIds = @(
        $processes | ForEach-Object { $_.ProcessId })
    if ($processes.Count -ne 1 -or $processes[0].ProcessId -ne $firstProcessId) {
        throw "Second launch did not settle on original process $firstProcessId; found $($report.secondLaunchProcessIds -join ', ')."
    }
    $report.singleInstanceProcessId = $processes[0].ProcessId
    Register-ScheduledTask -TaskName $secondLaunchTask -Action $secondLaunchAction `
        -Principal $principal -Force | Out-Null
}

function Request-InstalledShellClose([int] $ShellProcessId) {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    if (-not $ownedProcesses.ContainsKey($ShellProcessId) -or
        $ownedProcesses[$ShellProcessId].HasExited) {
        throw "Test-launched shell process $ShellProcessId has already exited."
    }
    $process = Get-Process -Id $ShellProcessId -ErrorAction Stop
    if ($process.MainWindowHandle -eq 0) {
        throw 'Installed production shell has no window to close.'
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle(
        $process.MainWindowHandle)
    $pattern = $window.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern)
    $pattern.Close()
}

function Wait-InstalledShellExit([int] $ShellProcessId) {
    if (-not $ownedProcesses.ContainsKey($ShellProcessId)) {
        throw "No test-owned handle for shell process $ShellProcessId."
    }
    $deadline = (Get-Date).AddSeconds(20)
    do {
        if ($ownedProcesses[$ShellProcessId].HasExited) {
            return
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if (-not $ownedProcesses[$ShellProcessId].HasExited) {
        throw 'Production shell did not exit after a normal window close.'
    }
}

function Close-InstalledShell {
    Request-InstalledShellClose $report.launchedProcessId
    Wait-InstalledShellExit $report.launchedProcessId
    $report.closedGracefully = $true
}

function Start-ShellDatabaseLock(
    [string] $Mode,
    [string] $ReadyPath,
    [string] $ReleasePath) {
    if ($Mode -notin @('hold-write-lock', 'hold-read-lock')) {
        throw "Unsupported shell database lock mode $Mode."
    }
    $seedDll = Join-Path $PSScriptRoot `
        'DesktopGuides.ShellSeed\bin\Release\net10.0\DesktopGuides.ShellSeed.dll'
    if (-not (Test-Path $seedDll)) {
        throw 'Shell seed executable is unavailable for the database lock.'
    }
    $lockArguments = @(
        ('"{0}"' -f $seedDll), $Mode, ('"{0}"' -f $dataRoot),
        ('"{0}"' -f $ReadyPath), ('"{0}"' -f $ReleasePath)
    )
    $lockProcess = Start-Process -FilePath 'dotnet.exe' `
        -ArgumentList $lockArguments -PassThru -WindowStyle Hidden
    try {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            if ($lockProcess.HasExited) {
                throw "$Mode helper exited early: $($lockProcess.ExitCode)."
            }
            if (Test-Path -LiteralPath $ReadyPath) {
                return $lockProcess
            }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        throw "$Mode helper did not acquire the database."
    }
    catch {
        New-Item -ItemType File -Force $ReleasePath | Out-Null
        if (-not $lockProcess.WaitForExit(10000)) {
            [void][DesktopGuidesOwnedProcess]::TerminateAndWait(
                $lockProcess.Handle, 10000)
        }
        throw
    }
}

function Release-ShellDatabaseLock(
    [System.Diagnostics.Process] $LockProcess,
    [string] $ReleasePath) {
    New-Item -ItemType File -Force $ReleasePath | Out-Null
    if (-not $LockProcess.WaitForExit(10000)) {
        if (-not [DesktopGuidesOwnedProcess]::TerminateAndWait(
            $LockProcess.Handle, 10000)) {
            throw 'Database-lock helper did not exit after handle termination.'
        }
    }
    if ($LockProcess.ExitCode -ne 0) {
        throw "Database-lock helper failed: $($LockProcess.ExitCode)."
    }
}

function Assert-RelaunchDuringClose {
    $closingProcessId = $report.launchedProcessId
    $lockReady = Join-Path $ResultDirectory "write-lock-ready-$runId"
    $lockRelease = Join-Path $ResultDirectory "write-lock-release-$runId"
    $lockProcess = Start-ShellDatabaseLock `
        'hold-write-lock' $lockReady $lockRelease
    try {
        $report.pendingGuide = Run-ShellSmoke 'queue-guide-write'
        Request-InstalledShellClose $closingProcessId
        Start-Sleep -Milliseconds 300
        if (-not (Get-Process -Id $closingProcessId -ErrorAction SilentlyContinue)) {
            throw 'Closing shell exited before pending guide action drained.'
        }
        Start-InstalledShell
        $report.closeHandoffOverlapObserved = [bool](
            Get-Process -Id $closingProcessId -ErrorAction SilentlyContinue)
        if (-not $report.closeHandoffOverlapObserved) {
            throw 'New shell opened only after the old shell exited.'
        }
        $report.waitingDuringClose = Run-ShellSmoke 'waiting-handoff'
    }
    finally {
        Release-ShellDatabaseLock $lockProcess $lockRelease
    }
    Wait-InstalledShellExit $closingProcessId
    $report.relaunchDuringCloseProcessId = $report.launchedProcessId
    $report.normalAfterCloseRelaunch = Run-ShellSmoke 'normal' 'Blocked Write Guide'
}

function Assert-GameEditorDoesNotOpenDuringClose {
    $closingProcessId = $report.launchedProcessId
    $lockReady = Join-Path $ResultDirectory "editor-close-lock-ready-$runId"
    $lockRelease = Join-Path $ResultDirectory "editor-close-lock-release-$runId"
    $lockProcess = Start-ShellDatabaseLock `
        'hold-read-lock' $lockReady $lockRelease
    try {
        $report.gameEditorQueuedBeforeClose = Run-ShellSmoke 'queue-game-editor'
        Request-InstalledShellClose $closingProcessId
    }
    finally {
        Release-ShellDatabaseLock $lockProcess $lockRelease
    }
    Wait-InstalledShellExit $closingProcessId
    Start-InstalledShell
}

function Assert-LaterGuideWins {
    $lockReady = Join-Path $ResultDirectory "later-guide-lock-ready-$runId"
    $lockRelease = Join-Path $ResultDirectory "later-guide-lock-release-$runId"
    $lockProcess = Start-ShellDatabaseLock `
        'hold-read-lock' $lockReady $lockRelease
    try {
        $report.queuedLaterGuide = Run-ShellSmoke 'queue-later-guide'
    }
    finally {
        Release-ShellDatabaseLock $lockProcess $lockRelease
    }
    $report.laterGuideResult = Run-ShellSmoke 'later-guide-result'
}

function Assert-LateGuideAfterClose {
    $closingProcessId = $report.launchedProcessId
    $lockReady = Join-Path $ResultDirectory "late-close-lock-ready-$runId"
    $lockRelease = Join-Path $ResultDirectory "late-close-lock-release-$runId"
    $lockProcess = Start-ShellDatabaseLock `
        'hold-read-lock' $lockReady $lockRelease
    try {
        $report.pendingBeforeLateClose = Run-ShellSmoke 'queue-guide'
        Request-InstalledShellClose $closingProcessId
        $report.lateGuideAfterClose = Run-ShellSmoke 'late-guide-after-close'
    }
    finally {
        Release-ShellDatabaseLock $lockProcess $lockRelease
    }
    Wait-InstalledShellExit $closingProcessId
    Start-InstalledShell
    $report.normalAfterLateClose = Run-ShellSmoke 'normal' 'Blocked Write Guide'
}

function Assert-FailedLaterGuideDoesNotSaveEarlier {
    Start-InstalledShell
    $report.normalBeforeFailedGuide = Run-ShellSmoke 'normal' 'Blocked Write Guide'
    $seedProject = Join-Path $PSScriptRoot `
        'DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj'
    dotnet run --project $seedProject -c Release --no-restore -- `
        invalidate-blocked-guide $dataRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not invalidate the displayed guide for the failure case.'
    }
    $lockReady = Join-Path $ResultDirectory "failed-guide-lock-ready-$runId"
    $lockRelease = Join-Path $ResultDirectory "failed-guide-lock-release-$runId"
    $lockProcess = Start-ShellDatabaseLock `
        'hold-read-lock' $lockReady $lockRelease
    try {
        $report.queuedBeforeFailedGuide = Run-ShellSmoke 'queue-later-guide'
    }
    finally {
        Release-ShellDatabaseLock $lockProcess $lockRelease
    }
    $report.failedLaterGuideResult = Run-ShellSmoke 'later-guide-failed-result'
    Close-InstalledShell
}

function Assert-ReaderRenderErrorDoesNotSaveResume {
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $reached = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::AutoReset,
        "Local\DesktopGuides.Preview.ReaderLoad.$($processId).Reached")
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\DesktopGuides.Preview.ReaderLoad.$($processId).Continue")
    $seedProject = Join-Path $PSScriptRoot `
        'DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj'
    $fixtureCorrupted = $false
    try {
        $report.queuedBeforeRenderError = Run-ShellSmoke 'queue-reader-render-error'
        if (-not $reached.WaitOne(15000)) {
            throw 'Reader route did not reach its second metadata read.'
        }
        dotnet run --project $seedProject -c Release --no-restore -- `
            corrupt-reader-guide $dataRoot
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not corrupt the disposable Reader metadata fixture.'
        }
        $fixtureCorrupted = $true
        $resume.Set() | Out-Null
        $report.readerRenderError = Run-ShellSmoke 'reader-render-error-observed'
        dotnet run --project $seedProject -c Release --no-restore -- `
            restore-reader-guide $dataRoot
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not restore the Reader metadata fixture.'
        }
        $fixtureCorrupted = $false
        $report.readerRenderRecovery = Run-ShellSmoke 'reader-render-error-result'
        Close-InstalledShell
    }
    finally {
        try {
            $resume.Set() | Out-Null
            if ($fixtureCorrupted) {
                dotnet run --project $seedProject -c Release --no-restore -- `
                    restore-reader-guide $dataRoot
                if ($LASTEXITCODE -ne 0) {
                    throw 'Reader metadata fixture recovery failed during cleanup.'
                }
            }
        }
        finally {
            $resume.Dispose()
            $reached.Dispose()
        }
    }
}

function Assert-GameSwitchClearsWhileLoading {
    $report.switchGamePreparation = Run-ShellSmoke 'switch-game-prepare'
    $lockReady = Join-Path $ResultDirectory "read-lock-ready-$runId"
    $lockRelease = Join-Path $ResultDirectory "read-lock-release-$runId"
    $lockProcess = Start-ShellDatabaseLock `
        'hold-read-lock' $lockReady $lockRelease
    try {
        $report.switchGameLoading = Run-ShellSmoke 'switch-game-loading'
    }
    finally {
        Release-ShellDatabaseLock $lockProcess $lockRelease
    }
    $report.switchGame = Run-ShellSmoke 'switch-game'
}

function Assert-ClosingTargetRedirect {
    $closingProcessId = $report.launchedProcessId
    $selected = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::AutoReset,
        'Local\DesktopGuides.Preview.RedirectSelected')
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        'Local\DesktopGuides.Preview.RedirectContinue')
    try {
        $selected.Reset() | Out-Null
        $resume.Reset() | Out-Null
        Clear-LaunchResult $secondLaunchResultPath
        Start-ScheduledTask -TaskName $secondLaunchTask
        $secondLaunch = Wait-LaunchResult $secondLaunchResultPath $secondLaunchTask
        if (-not $selected.WaitOne(15000)) {
            throw 'Second launch did not select the closing target.'
        }
        $report.closingTargetSelected = $true
        Request-InstalledShellClose $closingProcessId
        Wait-InstalledShellExit $closingProcessId
        $resume.Set() | Out-Null
        Wait-InstalledShellWindow ([int]$secondLaunch.processId)
        $report.closingTargetRelaunchProcessId = $report.launchedProcessId
        $report.normalAfterClosingTarget = Run-ShellSmoke 'normal'
    }
    finally {
        $resume.Set() | Out-Null
        $resume.Dispose()
        $selected.Dispose()
    }
}

function Assert-QueuedActivationClose {
    $closingProcessId = $report.launchedProcessId
    $queued = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::AutoReset,
        'Local\DesktopGuides.Preview.ActivationQueued')
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        'Local\DesktopGuides.Preview.ActivationContinue')
    try {
        $queued.Reset() | Out-Null
        $resume.Reset() | Out-Null
        Clear-LaunchResult $secondLaunchResultPath
        Start-ScheduledTask -TaskName $secondLaunchTask
        $secondLaunch = Wait-LaunchResult $secondLaunchResultPath $secondLaunchTask
        if (-not $queued.WaitOne(15000)) {
            throw 'Original shell did not queue redirected activation.'
        }
        $report.activationQueuedBeforeClose = $true
        Request-InstalledShellClose $closingProcessId
        Wait-InstalledShellExit $closingProcessId
        $resume.Set() | Out-Null
        Wait-InstalledShellWindow ([int]$secondLaunch.processId)
        $report.queuedActivationRelaunchProcessId = $report.launchedProcessId
        $report.normalAfterQueuedActivation = Run-ShellSmoke 'normal'
    }
    finally {
        $resume.Set() | Out-Null
        $resume.Dispose()
        $queued.Dispose()
    }
}

function Assert-AcceptedThenClose {
    $closingProcessId = $report.launchedProcessId
    $received = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::AutoReset,
        'Local\DesktopGuides.Preview.AcceptanceReceived')
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        'Local\DesktopGuides.Preview.AcceptanceContinue')
    try {
        $received.Reset() | Out-Null
        $resume.Reset() | Out-Null
        Clear-LaunchResult $secondLaunchResultPath
        Start-ScheduledTask -TaskName $secondLaunchTask
        $secondLaunch = Wait-LaunchResult $secondLaunchResultPath $secondLaunchTask
        if (-not $received.WaitOne(15000)) {
            throw 'Second launch did not receive UI acceptance.'
        }
        $report.acceptedBeforeClose = $true
        Request-InstalledShellClose $closingProcessId
        Wait-InstalledShellExit $closingProcessId
        $resume.Set() | Out-Null
        Wait-InstalledShellExit ([int]$secondLaunch.processId)
        $remaining = @(Get-InstalledShellProcesses)
        if ($remaining.Count -ne 0) {
            throw 'An accepted second launch reopened the closing shell.'
        }
        $report.acceptedCloseDidNotRelaunch = $true
    }
    finally {
        $resume.Set() | Out-Null
        $resume.Dispose()
        $received.Dispose()
    }
}

function Run-ShellSmoke(
    [string] $mode,
    [string] $expectedResumeGuide = 'Route Test Guide',
    [int] $ExitDelayMilliseconds = 0,
    [string] $ResultName = $mode,
    [int] $ExpectedScalePercent = 0,
    [string] $ExpectedMaterial = '',
    [string] $SwitchToMaterial = '') {
    $resultPath = Join-Path $ResultDirectory "$ResultName.json"
    Clear-ShellSmokeResult $resultPath
    $invocationId = [Guid]::NewGuid().ToString('N')
    $script = Join-Path $PSScriptRoot 'windows_shell_ui_smoke.ps1'
    $arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass ' +
        '-File "' + $script + '" -Mode ' + $mode +
        ' -ResultPath "' + $resultPath + '"' +
        ' -InvocationId ' + $invocationId +
        ' -ProcessId ' + $report.launchedProcessId +
        ' -SessionId ' + $targetSessionId +
        ' -ExecutablePath "' + $expectedExecutablePath + '"' +
        ' -ExpectedResumeGuide "' + $expectedResumeGuide + '"' +
        ' -ExitDelayMilliseconds ' + $ExitDelayMilliseconds +
        ' -ExpectedScalePercent ' + $ExpectedScalePercent
    if ($ExpectedMaterial) {
        $arguments += ' -ExpectedMaterial ' + $ExpectedMaterial
    }
    if ($SwitchToMaterial) {
        $arguments += ' -SwitchToMaterial ' + $SwitchToMaterial
    }
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument $arguments -WorkingDirectory $PSScriptRoot
    Register-ScheduledTask -TaskName $smokeTask -Action $action `
        -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName $smokeTask
    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 500
    } while (-not (Test-Path $resultPath) -and (Get-Date) -lt $deadline)
    if (-not (Test-Path $resultPath)) {
        throw "Installed $mode shell smoke timed out."
    }
    if (-not (Wait-ScheduledTaskIdle $smokeTask 10 -RequireRegistered)) {
        throw "Installed $mode shell smoke task did not finish after writing its result."
    }
    $result = Read-ShellSmokeResult $resultPath $invocationId $mode `
        $report.launchedProcessId $targetSessionId
    if (-not $result.success) {
        throw "Installed $mode shell smoke failed: $($result.error)"
    }
    return $result
}

function Get-AppThemePreference {
    $path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
    $item = Get-ItemProperty -Path $path -ErrorAction SilentlyContinue
    $hasValue = $item -and
        $item.PSObject.Properties.Name -contains 'AppsUseLightTheme'
    return [ordered]@{
        path = $path
        hasValue = [bool]$hasValue
        value = if ($hasValue) { [int]$item.AppsUseLightTheme } else { $null }
    }
}

function Set-AppThemePreference([bool] $UseLightTheme) {
    $path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
    New-Item -Path $path -Force | Out-Null
    New-ItemProperty -Path $path -Name AppsUseLightTheme `
        -PropertyType DWord -Value ([int]$UseLightTheme) -Force | Out-Null
}

function Restore-AppThemePreference($Original) {
    if ($Original.hasValue) {
        New-ItemProperty -Path $Original.path -Name AppsUseLightTheme `
            -PropertyType DWord -Value $Original.value -Force | Out-Null
    }
    else {
        Remove-ItemProperty -Path $Original.path -Name AppsUseLightTheme `
            -ErrorAction SilentlyContinue
    }
}

function Run-DesignLanguageScenarios {
    $originalTheme = Get-AppThemePreference
    $report.originalAppTheme = $originalTheme
    # High contrast rewrites the active Windows theme, so T16.2 owns that pass.
    $report.highContrast = 'deferred-to-T16.2'
    try {
        Start-InstalledShell
        $report.designLanguageSystem = Run-ShellSmoke `
            'design-language' -ResultName 'design-system'
        Close-InstalledShell

        Set-AppThemePreference $true
        Start-InstalledShell
        $report.designLanguageLight = Run-ShellSmoke `
            'design-language' -ResultName 'design-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.designLanguageDark = Run-ShellSmoke `
            'design-language' -ResultName 'design-dark'
        Close-InstalledShell

        $lightLuminance = Get-ScreenshotLuminance `
            $report.designLanguageLight.libraryWideScreenshot
        $darkLuminance = Get-ScreenshotLuminance `
            $report.designLanguageDark.libraryWideScreenshot
        if ($lightLuminance -le ($darkLuminance + 40)) {
            throw "Light and dark screenshots did not differ enough: " +
                "$lightLuminance versus $darkLuminance."
        }
        $report.themeLuminance = [ordered]@{
            light = $lightLuminance
            dark = $darkLuminance
        }
    }
    finally {
        Restore-AppThemePreference $originalTheme
        $report.restoredAppTheme = Get-AppThemePreference
    }
}

function Set-StoredMaterial([string] $material) {
    dotnet run --project $seedProject -c Release --no-restore -- `
        set-material $dataRoot $material
    if ($LASTEXITCODE -ne 0) { throw "Could not store the $material window background." }
}

function Measure-LibraryStrip($result) {
    $bounds = $result.libraryBounds
    $region = Get-BoundaryStripRegion $bounds.windowLeft $bounds.windowHeight `
        $bounds.contentLeft
    $strip = Get-ScreenshotRegionStats $result.libraryScreenshot `
        $region.X $region.Y $region.Width $region.Height
    $result | Add-Member -NotePropertyName libraryStrip -NotePropertyValue $strip
    # The dialog command area between the button row and the panel's bottom edge.
    $dialog = $result.dialogBounds
    $dialogStrip = Get-ScreenshotRegionStats $result.dialogScreenshot `
        ([int]$dialog.buttonLeft) ([int]$dialog.buttonBottom + 8) 48 4
    $result | Add-Member -NotePropertyName dialogStrip -NotePropertyValue $dialogStrip
    return $result
}

function Run-MaterialScenarios {
    # Calibrated on the Windows 11 x64 host; see t11-materials-plan.md.
    $acrylicThreshold = 4
    $originalTheme = Get-AppThemePreference
    $report.materials = [ordered]@{}
    try {
        foreach ($light in @($true, $false)) {
            $themeName = if ($light) { 'light' } else { 'dark' }
            Set-AppThemePreference $light
            foreach ($material in @('Solid', 'Acrylic', 'Mica')) {
                Set-StoredMaterial $material
                Start-InstalledShell
                $report.materials["$themeName-$material"] = Measure-LibraryStrip (
                    Run-ShellSmoke 'material' -ResultName "material-$themeName-$material" `
                        -ExpectedMaterial $material)
                Close-InstalledShell
            }
            $solid = $report.materials["$themeName-Solid"].libraryStrip
            if ($solid.maxChannelRange -gt 2) {
                throw "The $themeName Solid window shows a pane/content seam: " +
                    "range $($solid.maxChannelRange)."
            }
            $acrylic = $report.materials["$themeName-Acrylic"].libraryStrip
            $difference = [Math]::Max([Math]::Abs($acrylic.meanR - $solid.meanR),
                [Math]::Max([Math]::Abs($acrylic.meanG - $solid.meanG),
                    [Math]::Abs($acrylic.meanB - $solid.meanB)))
            $report.materials["$themeName-acrylicDifference"] = $difference
            if ($difference -le $acrylicThreshold) {
                throw "The $themeName Acrylic backdrop matched Solid " +
                    "(difference $difference); the backdrop is not visible."
            }
            $solidDialog = $report.materials["$themeName-Solid"].dialogStrip
            $acrylicDialog = $report.materials["$themeName-Acrylic"].dialogStrip
            $dialogDifference = [Math]::Max([Math]::Abs($acrylicDialog.meanR - $solidDialog.meanR),
                [Math]::Max([Math]::Abs($acrylicDialog.meanG - $solidDialog.meanG),
                    [Math]::Abs($acrylicDialog.meanB - $solidDialog.meanB)))
            $report.materials["$themeName-dialogDifference"] = $dialogDifference
            if ($dialogDifference -le 2) {
                throw "The $themeName Acrylic dialog matched the Solid dialog."
            }
        }

        Set-AppThemePreference $true
        Set-StoredMaterial 'Mica'
        Start-InstalledShell
        $report.materialSwitch = Run-ShellSmoke 'material' `
            -ResultName 'material-switch' -ExpectedMaterial 'Mica' `
            -SwitchToMaterial 'Acrylic'
        Close-InstalledShell
        Start-InstalledShell
        $report.materialPersisted = Run-ShellSmoke 'material' `
            -ResultName 'material-persisted' -ExpectedMaterial 'Acrylic'
        Close-InstalledShell
    }
    finally {
        Set-StoredMaterial 'Mica'
        Restore-AppThemePreference $originalTheme
        $report.restoredAppTheme = Get-AppThemePreference
    }
}

try {
    $report.windowsAppRuntime = @(
        Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2*' |
            Where-Object { $_.Architecture -eq 'X64' } |
            Select-Object -ExpandProperty Version |
            ForEach-Object { $_.ToString() }
    )
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

    $signTool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' `
        -Recurse -Filter signtool.exe |
        Where-Object { $_.FullName -like '*\x64\signtool.exe' } |
        Sort-Object FullName |
        Select-Object -Last 1 -ExpandProperty FullName
    if (-not $signTool) { throw 'Windows SDK SignTool is unavailable.' }
    & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $signed
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE." }
    & $signTool verify /pa $signed
    if ($LASTEXITCODE -ne 0) { throw 'Signed MSIX verification failed.' }
    $report.signedPackageSha256 = (Get-FileHash $signed -Algorithm SHA256).Hash

    if (Get-AppxPackage -Name DesktopGuides.Preview) {
        throw 'A production preview package appeared before test install.'
    }
    Assert-FreshPreviewProfile $env:LOCALAPPDATA
    Add-AppxPackage -Path $signed
    $installed = Get-AppxPackage -Name DesktopGuides.Preview
    if (-not $installed) { throw 'Production package did not install.' }
    $report.packageFullName = $installed.PackageFullName
    $report.packageFamilyName = $installed.PackageFamilyName
    $report.installLocation = $installed.InstallLocation
    $expectedExecutablePath = Join-Path $installed.InstallLocation `
        'DesktopGuides.Production.exe'

    $dataRoot = Join-Path $env:LOCALAPPDATA `
        "Packages\$($installed.PackageFamilyName)\LocalState"
    New-Item -ItemType Directory -Force $dataRoot | Out-Null
    $seedProject = Join-Path $PSScriptRoot `
        'DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj'

    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME `
        -LogonType Interactive -RunLevel Limited
    $launchAction = New-ShellLaunchAction $launchResultPath
    $secondLaunchAction = New-ShellLaunchAction $secondLaunchResultPath
    Register-ScheduledTask -TaskName $launchTask -Action $launchAction `
        -Principal $principal -Force | Out-Null

    if ($DesignOnly) {
        dotnet run --project $seedProject -c Release --no-restore -- `
            seed-design $dataRoot
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not seed the design-language metadata.'
        }
        Run-DesignLanguageScenarios
        Run-MaterialScenarios
        $report.success = $true
        return
    }

    Start-InstalledShell
    $report.empty = Run-ShellSmoke 'empty' -ExitDelayMilliseconds 2000
    $report.emptyAfterDelayedTask = Run-ShellSmoke 'empty'
    Assert-SingleInstance
    $report.emptyAfterSecondLaunch = Run-ShellSmoke 'empty'
    $report.gameEditor = Run-ShellSmoke 'game-editor'
    Close-InstalledShell
    Start-InstalledShell
    $report.gameEditorPersisted = Run-ShellSmoke 'game-editor-persisted'

    Stop-InstalledShell
    dotnet run --project $seedProject -c Release --no-restore -- seed $dataRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed shell route metadata.' }
    Start-InstalledShell
    $report.gameEditorClosePrepared =
        Run-ShellSmoke 'prepare-game-editor-close'
    Assert-GameEditorDoesNotOpenDuringClose
    $report.normal = Run-ShellSmoke 'normal'
    Assert-RelaunchDuringClose
    Assert-ClosingTargetRedirect
    Assert-QueuedActivationClose
    Assert-LateGuideAfterClose
    Assert-LaterGuideWins
    Assert-AcceptedThenClose
    Assert-FailedLaterGuideDoesNotSaveEarlier
    Assert-ReaderRenderErrorDoesNotSaveResume

    Stop-InstalledShell
    dotnet run --project $seedProject -c Release --no-restore -- stale $dataRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not set stale last-guide ID.' }
    Start-InstalledShell
    $report.stale = Run-ShellSmoke 'stale'
    Close-InstalledShell

    dotnet run --project $seedProject -c Release --no-restore -- seed-long $dataRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed the long guide list.' }
    Start-InstalledShell
    $report.longList = Run-ShellSmoke 'long-list'
    Close-InstalledShell

    dotnet run --project $seedProject -c Release --no-restore -- seed-second $dataRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed the second game.' }
    Start-InstalledShell
    Assert-GameSwitchClearsWhileLoading
    Close-InstalledShell

    Get-ChildItem -LiteralPath $dataRoot -Force |
        Remove-Item -Recurse -Force
    dotnet run --project $seedProject -c Release --no-restore -- `
        seed-design $dataRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not seed the design-language metadata.'
    }
    Run-DesignLanguageScenarios
    Run-MaterialScenarios
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
    try {
        $report.processesAtFailure = @(
            Get-InstalledShellProcesses |
            ForEach-Object {
                [ordered]@{
                    processId = $_.ProcessId
                    sessionId = $_.SessionId
                    created = $_.CreationDate.ToString('o')
                    testOwned = ($ownedProcesses.ContainsKey([int]$_.ProcessId) -and
                        -not $ownedProcesses[[int]$_.ProcessId].HasExited)
                }
            })
    }
    catch {
        $report.processesAtFailureError = $_ | Out-String
    }
    try {
        $report.applicationEvents = @(
            Get-WinEvent -FilterHashtable @{
                LogName = 'Application'
                StartTime = [datetime]$report.observedAt
            } -ErrorAction Stop |
            Where-Object {
                $_.Message -match 'DesktopGuides\.Production' -and
                $_.ProviderName -in @('.NET Runtime', 'Application Error',
                    'Windows Error Reporting')
            } |
            Select-Object -First 8 |
            ForEach-Object {
                [ordered]@{
                    observedAt = $_.TimeCreated.ToUniversalTime().ToString('o')
                    provider = $_.ProviderName
                    eventId = $_.Id
                    message = $_.Message
                }
            })
    }
    catch {
        $report.applicationEventsError = $_ | Out-String
    }
}
finally {
    $tasksDrained = $true
    if (-not (Wait-ScheduledTaskIdle $smokeTask 10)) {
        Stop-ScheduledTask -TaskName $smokeTask -ErrorAction SilentlyContinue
        if (-not (Wait-ScheduledTaskIdle $smokeTask 10)) {
            $cleanupErrors.Add(
                "Interactive smoke task $smokeTask is still active.")
            $tasksDrained = $false
        }
    }
    try {
        Remove-OwnedScheduledTask $smokeTask
    }
    catch {
        $cleanupErrors.Add(
            "Could not remove interactive smoke task $smokeTask`: $($_.Exception.Message)")
    }
    foreach ($taskName in @($secondLaunchTask, $launchTask)) {
        if (-not (Wait-ScheduledTaskIdle $taskName 70)) {
            $cleanupErrors.Add(
                "Interactive launch task $taskName did not finish its child handoff.")
            Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
            if (-not (Wait-ScheduledTaskIdle $taskName 10)) {
                $cleanupErrors.Add(
                    "Interactive launch task $taskName is still active.")
                $tasksDrained = $false
            }
        }
        try {
            Remove-OwnedScheduledTask $taskName
        }
        catch {
            $cleanupErrors.Add(
                "Could not remove interactive launch task $taskName`: $($_.Exception.Message)")
        }
    }
    try {
        Assert-NoOwnedScheduledTasks @($smokeTask, $secondLaunchTask, $launchTask)
    }
    catch {
        $cleanupErrors.Add($_.Exception.Message)
    }
    if ($installed) { Stop-InstalledShell -BestEffort }
    $shellProcessesAfterStop = @(Get-InstalledShellProcesses)
    if ($shellProcessesAfterStop.Count -gt 0) {
        $cleanupErrors.Add(
            "Installed shell processes remain after cleanup: $(
                ($shellProcessesAfterStop | ForEach-Object { $_.ProcessId }) -join ', ').")
    }
    $installedNow = @(Get-AppxPackage -Name DesktopGuides.Preview)
    $ownedPackage = @($installedNow | Where-Object {
        $installed -and $_.PackageFullName -eq $installed.PackageFullName
    })
    if ($ownedPackage.Count -eq 1 -and $tasksDrained -and
        $shellProcessesAfterStop.Count -eq 0) {
        Remove-AppxPackage -Package $ownedPackage[0].PackageFullName `
            -ErrorAction SilentlyContinue
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
    $remainingPackages = @(Get-AppxPackage -Name DesktopGuides.Preview)
    $report.packageStillInstalled = $remainingPackages.Count -gt 0
    $report.certificateStillTrusted = if ($certificate) {
        Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
    } else { $false }
    if ($report.packageStillInstalled -or $report.certificateStillTrusted) {
        $report.success = $false
        $report.cleanupError = if ($remainingPackages.Count -gt 0 -and
            ($ownedPackage.Count -eq 0 -or
             $remainingPackages[0].PackageFullName -ne $installed.PackageFullName)) {
            'A Preview package not confirmed as test-owned remains untouched.'
        } else {
            'Temporary package or certificate remains.'
        }
    }
    if ($cleanupErrors.Count -gt 0) {
        $report.success = $false
        $report.processCleanupErrors = @($cleanupErrors)
    }
    $report | ConvertTo-Json -Depth 8 |
        Set-Content (Join-Path $ResultDirectory 'signed-install.json') -Encoding UTF8
}
if (-not $report.success) { exit 1 }
