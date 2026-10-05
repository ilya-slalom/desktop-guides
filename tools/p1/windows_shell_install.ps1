param(
    # Pass exactly one: the unsigned MSIX to sign and install, or a published
    # portable DesktopGuides.Production.exe to launch in place.
    [string] $PackagePath,

    [string] $PortableExecutable,

    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory,

    # Pass at most one *Only switch to run a single scenario group against a
    # fresh install. With none, every group runs in the order listed below.
    [switch] $CoreOnly,
    [switch] $DesignOnly,
    [switch] $CatalogOnly,
    [switch] $TxtOnly,
    [switch] $HtmlOnly,
    [switch] $PdfOnly,
    [switch] $ImportOnly,
    [switch] $GameActionsOnly,
    [switch] $ProviderOnly,

    # Paths only. The values are read in memory and never passed on.
    [string] $IgdbCredentialFile = 'E:\work\igdb_credentials.txt',

    [string] $SteamGridDbCredentialFile = 'E:\work\steamgriddb_credentials.txt',

    # Wait for windows_provider_offline_controller.ps1 to block the app's
    # network access for one scenario. Needs the user's authorization.
    [switch] $AllowOfflineFirewallRule
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# Scenario groups in run order; CI's shell-scope input uses these names.
$scenarioGroups = [ordered]@{
    'core' = $CoreOnly.IsPresent
    'design' = $DesignOnly.IsPresent
    'catalog' = $CatalogOnly.IsPresent
    'txt' = $TxtOnly.IsPresent
    'html' = $HtmlOnly.IsPresent
    'pdf' = $PdfOnly.IsPresent
    'import' = $ImportOnly.IsPresent
    'game-actions' = $GameActionsOnly.IsPresent
    'provider' = $ProviderOnly.IsPresent
}
$selectedGroups = @($scenarioGroups.Keys | Where-Object { $scenarioGroups[$_] })
if ($selectedGroups.Count -gt 1) {
    throw "Pass at most one *Only switch; got $($selectedGroups.Count)."
}
$scenarioScope = if ($selectedGroups.Count -eq 1) { $selectedGroups[0] } else { 'all' }
$targetSessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
if ($targetSessionId -eq 0 -or
    -not @(Get-Process explorer -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $targetSessionId })) {
    throw 'Shell install test requires an interactive desktop session.'
}
$portable = [bool]$PortableExecutable
if ($portable -eq [bool]$PackagePath) {
    throw 'Pass either -PackagePath or -PortableExecutable.'
}
. (Join-Path $PSScriptRoot 'windows_shell_profile.ps1')
if ($portable) {
    if ((Split-Path $PortableExecutable -Leaf) -ne 'DesktopGuides.Production.exe' -or
        -not (Test-Path -LiteralPath $PortableExecutable -PathType Leaf)) {
        throw "Expected a portable DesktopGuides.Production.exe, got $PortableExecutable."
    }
    # The offline controller only accepts the installed package's executable.
    if ($AllowOfflineFirewallRule) {
        throw '-AllowOfflineFirewallRule applies only to the installed MSIX.'
    }
    $PortableExecutable = (Resolve-Path -LiteralPath $PortableExecutable).Path
    Assert-FreshPortableProfile $env:LOCALAPPDATA
}
else {
    $packageName = Split-Path $PackagePath -Leaf
    if ($packageName -notmatch '^DesktopGuides\.Production_[0-9]+(\.[0-9]+){3}_x64\.msix$') {
        throw "Expected an x64 production MSIX, got $packageName."
    }
    if (Get-AppxPackage -Name DesktopGuides.Preview) {
        throw 'A production preview package is already installed; refusing to replace it.'
    }
    Assert-FreshPreviewProfile $env:LOCALAPPDATA
}
. (Join-Path $PSScriptRoot 'windows_shell_smoke_result.ps1')
. (Join-Path $PSScriptRoot 'windows_shell_task_cleanup.ps1')
. (Join-Path $PSScriptRoot 'windows_shell_screenshot_stats.ps1')
. (Join-Path $PSScriptRoot 'windows_shell_theme_preference.ps1')
. (Join-Path $PSScriptRoot 'windows_provider_credentials.ps1')

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
    # Portable runs are not evidence for the signed-install gates.
    mode = if ($portable) { 'portable' } else { 'signed-msix' }
    scenarioScope = $scenarioScope
    scenarioGroupsRun = [System.Collections.Generic.List[string]]::new()
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
$dataRoot = $null
$portableDataRoot = if ($portable) { Join-Path $env:LOCALAPPDATA 'DesktopGuides' }
$portableDataOwned = $false

function Get-InstalledShellProcesses {
    if (-not $expectedExecutablePath) { return @() }
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
    [string] $SwitchToMaterial = '',
    [string] $IgdbCredentialFile = '',
    [string] $SteamGridDbCredentialFile = '',
    [string] $ExpectedProviderFailure = '',
    [string] $ExpectedGuideTitle = '',
    [string] $AppDataRoot = '',
    [string] $AppCacheRoot = '') {
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
    if ($IgdbCredentialFile) {
        $arguments += ' -IgdbCredentialFile "' + $IgdbCredentialFile + '"'
    }
    if ($SteamGridDbCredentialFile) {
        $arguments += ' -SteamGridDbCredentialFile "' + $SteamGridDbCredentialFile + '"'
    }
    if ($ExpectedProviderFailure) {
        $arguments += ' -ExpectedProviderFailure ' + $ExpectedProviderFailure
    }
    if ($ExpectedGuideTitle) {
        $arguments += ' -ExpectedGuideTitle "' + $ExpectedGuideTitle + '"'
    }
    if ($AppDataRoot) {
        $arguments += ' -AppDataRoot "' + $AppDataRoot + '"'
    }
    if ($AppCacheRoot) {
        $arguments += ' -AppCacheRoot "' + $AppCacheRoot + '"'
    }
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument $arguments -WorkingDirectory $PSScriptRoot
    Register-ScheduledTask -TaskName $smokeTask -Action $action `
        -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName $smokeTask
    $timeoutSeconds = if ($mode -like 'provider-*' -or $mode -like 'pdf-*') { 240 }
        elseif ($mode -like 'catalog*' -or $mode -like 'import-*' -or $mode -like 'game-actions*' -or $mode -like 'html-*') { 120 }
        else { 60 }
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
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

function Run-CatalogScenarios {
    Invoke-ShellSeed @('seed-catalog', $dataRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.catalogLight = Run-ShellSmoke 'catalog' -ResultName 'catalog-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.catalogDark = Run-ShellSmoke 'catalog' -ResultName 'catalog-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
    $state = Get-ProviderState
    if ($state.CredentialBlobExists) {
        throw 'The catalog run found a provider credential blob.'
    }
    $missing = @($state.Games | Where-Object {
        $_.Title -like 'Catalog C Missing Art*' -and -not $_.ArtworkExists })
    if ($missing.Count -ne 10) {
        throw "Expected 10 catalog games with missing artwork, found $($missing.Count)."
    }
    $report.catalogGames = @($state.Games).Count
}

function Run-CatalogFactsScenarios {
    Invoke-ShellSeed @('seed-facts', $dataRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.catalogFactsLight = Run-ShellSmoke 'catalog-facts' -ResultName 'catalog-facts-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.catalogFactsDark = Run-ShellSmoke 'catalog-facts' -ResultName 'catalog-facts-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}

function Run-LibrarySearchScenarios {
    Invoke-ShellSeed @('seed-search', $dataRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.librarySearchLight = Run-ShellSmoke 'library-search' -ResultName 'library-search-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.librarySearchDark = Run-ShellSmoke 'library-search' -ResultName 'library-search-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}

function Run-StableNavigationScenarios {
    # The smoke renames and removes, so each theme gets a fresh seed.
    $originalTheme = Get-AppThemePreference
    try {
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Invoke-ShellSeed @('seed-navigation', $dataRoot) | Out-Null
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.stableNavigationLight = Run-ShellSmoke 'stable-navigation' -ResultName 'stable-navigation-light'
        Close-InstalledShell

        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Invoke-ShellSeed @('seed-navigation', $dataRoot) | Out-Null
        Set-AppThemePreference $false
        Start-InstalledShell
        $report.stableNavigationDark = Run-ShellSmoke 'stable-navigation' -ResultName 'stable-navigation-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}

function Run-TxtReaderScenarios {
    # The smoke only reads, so one seed serves both themes.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\txt-long.txt'))) {
        throw 'txt-long.txt is missing. Run tools/p0/make_fixtures.py first.'
    }
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Invoke-ShellSeed @('seed-txt-reader', $dataRoot, $fixtureRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.txtReaderLight = Run-ShellSmoke 'txt-reader' -ResultName 'txt-reader-light'
        Close-InstalledShell

        Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
        Set-AppThemePreference $false
        Start-InstalledShell
        $report.txtReaderDark = Run-ShellSmoke 'txt-reader' -ResultName 'txt-reader-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
    Assert-TxtBackDuringLoad
}

function Assert-TxtBackDuringLoad {
    # Holds a TXT load at its test gate so Back runs while the load is in flight.
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $reached = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::AutoReset,
        "Local\DesktopGuides.Preview.TextLoad.$($processId).Reached")
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\DesktopGuides.Preview.TextLoad.$($processId).Continue")
    try {
        $report.txtLoadPaused = Run-ShellSmoke 'txt-load-paused'
        if (-not $reached.WaitOne(15000)) {
            throw 'The TXT load did not reach its test gate.'
        }
        $report.txtBackDuringLoad = Run-ShellSmoke 'txt-back-during-load'
        $resume.Set() | Out-Null
        $report.txtLoadReleased = Run-ShellSmoke 'txt-load-released'
        Close-InstalledShell
    }
    finally {
        $resume.Set() | Out-Null
        $resume.Dispose()
        $reached.Dispose()
    }
}

function Get-HtmlCacheRoot {
    # Matches AppCacheRoot: portable uses %LOCALAPPDATA%\DesktopGuides\Cache,
    # packaged uses the package's LocalCache beside LocalState.
    if ($portable) { return Join-Path $dataRoot 'Cache' }
    return Join-Path (Split-Path -Parent $dataRoot) 'LocalCache'
}

function Test-HtmlCanaryListening {
    try {
        Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:8765/health' -TimeoutSec 2 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Start-HtmlCanary([string] $logPath) {
    $script = (Resolve-Path (Join-Path $PSScriptRoot '..\p0\http_canary.py')).Path
    $python = (Get-Command python -ErrorAction Stop).Source
    $process = Start-Process -FilePath $python -PassThru -WindowStyle Hidden -ArgumentList @(
        ('"' + $script + '"'), '--log', ('"' + $logPath + '"'), '--port', '8765')
    $deadline = (Get-Date).AddSeconds(15)
    do {
        if ($process.HasExited) {
            throw "The loopback canary exited with code $($process.ExitCode)."
        }
        if (Test-HtmlCanaryListening) { return $process }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Stop-HtmlCanary $process
    throw 'The loopback canary did not answer its health check.'
}

function Stop-HtmlCanary($process) {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
    }
}

function Get-HtmlCanaryLines([string] $logPath) {
    if (-not (Test-Path -LiteralPath $logPath)) { return @() }
    return @(Get-Content -LiteralPath $logPath)
}

function Invoke-HtmlReaderPass([string] $resultName, [string] $mode = 'html-reader', [switch] $NoRuntime) {
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $diagnosticsGate = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\DesktopGuides.Preview.HtmlDiagnostics.$processId")
    $launchGate = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\DesktopGuides.Preview.ExternalLaunch.$processId")
    # Points the runtime probe at an empty folder, as on a PC without WebView2.
    $runtimeGate = $null
    if ($NoRuntime) {
        $runtimeGate = [System.Threading.EventWaitHandle]::new(
            $false, [System.Threading.EventResetMode]::ManualReset,
            "Local\DesktopGuides.Preview.WebView2Missing.$processId")
    }
    try {
        $report.htmlReader[$resultName] = Run-ShellSmoke $mode -ResultName $resultName
        Close-InstalledShell
    }
    finally {
        if ($runtimeGate) { $runtimeGate.Dispose() }
        $launchGate.Dispose()
        $diagnosticsGate.Dispose()
    }
}

function Save-HtmlDiagnostics([string] $resultName, [string] $cacheRoot) {
    # Keeps each pass's diagnostics with the evidence, even when a check fails.
    $diagnostics = Join-Path $cacheRoot 'diagnostics'
    foreach ($file in @(Get-ChildItem -LiteralPath $diagnostics -Filter '*.json' -ErrorAction SilentlyContinue)) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $ResultDirectory "$resultName.$($file.Name)")
    }
}

function Assert-HtmlReaderPass(
    [string] $pass, [string] $cacheRoot, $expectedSessions, [string[]] $expectedLaunches,
    [string] $logPath, [int] $baseline) {
    # R19: isolation is shown by exactly what each session served. Deny
    # counts are kept as evidence only, because the CSP can stop a
    # reference before the request handler sees it.
    $diagnostics = Join-Path $cacheRoot 'diagnostics'
    $remaining = [System.Collections.ArrayList]::new()
    foreach ($expected in $expectedSessions) { [void]$remaining.Add($expected) }
    $files = @(Get-ChildItem -LiteralPath $diagnostics -Filter 'html-session-*.json' -ErrorAction SilentlyContinue)
    if ($files.Count -ne $remaining.Count) {
        throw "The $pass pass wrote $($files.Count) HTML session diagnostics; expected $($remaining.Count)."
    }
    $sessions = @()
    foreach ($file in $files) {
        $session = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $served = @($session.served) -join ','
        $match = $null
        foreach ($candidate in $remaining) {
            if ($candidate.guideId -eq $session.guideId -and $candidate.served -ceq $served) {
                $match = $candidate
                break
            }
        }
        if (-not $match) {
            throw "Guide $($session.guideId) served '$served' in the $pass pass, which matches no expected session."
        }
        $remaining.Remove($match)
        $sessions += $session
    }

    $launchesPath = Join-Path $diagnostics 'external-launches.json'
    $launches = @()
    if (Test-Path -LiteralPath $launchesPath) {
        # Two statements, so Windows PowerShell doesn't wrap the parsed array.
        $launches = Get-Content -LiteralPath $launchesPath -Raw | ConvertFrom-Json
        $launches = @($launches)
    }
    if (($launches -join "`n") -cne ($expectedLaunches -join "`n")) {
        throw "The $pass pass launched '$($launches -join ', ')'; expected '$($expectedLaunches -join ', ')'."
    }

    $newLines = @(Get-HtmlCanaryLines $logPath | Select-Object -Skip $baseline)
    if ($newLines.Count -ne 0) {
        throw "The canary recorded guide-originated traffic in the $pass pass: $($newLines -join '; ')."
    }
    return [ordered]@{
        sessions = $sessions
        externalLaunches = $launches
        canaryLinesBefore = $baseline
    }
}

function Run-HtmlReaderScenarios {
    # Each canary guide opens with the loopback canary listening (light) and
    # stopped (dark): TR07.1-TR07.3. Both passes stop Guide B's renderer and
    # reopen it. A third pass has no runtime and a deleted entry: TR09.2-TR09.3.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    $ids = Invoke-ShellSeed @('seed-html-reader', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $cacheRoot = Get-HtmlCacheRoot
    $diagnostics = Join-Path $cacheRoot 'diagnostics'
    $logPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
        (Join-Path $ResultDirectory 'html-canary.log'))
    Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    $report.htmlReader = [ordered]@{ guideA = $ids.guideA; guideB = $ids.guideB }

    # Guide B is a Save Page As export named after its page title. The '%'
    # makes the import alias its entry to guide.html; its companion folder
    # keeps the source name. Non-ASCII characters are built so this file
    # stays ASCII.
    $titleB = "Canary Guide B (PS1) - Walkthrough's 100% Caf" + [char]0x00E9 + ' ' + [char]0x2013 + ' v2'
    $servedA = 'guide.html,images/a.png,style.css'
    $servedB = "${titleB}_files/b.png,${titleB}_files/style.css,guide.html"
    # html-crash: Guide B's stopped session and its reopened one.
    $readerSessions = @(
        @{ guideId = $ids.guideA; served = $servedA },
        @{ guideId = $ids.guideB; served = $servedB },
        @{ guideId = $ids.guideB; served = $servedB })
    $readerLaunches = @('https://example.com/desktop-guides-canary')

    # html-profile-sweep: startup removes a leftover profile and nothing else.
    $profiles = Join-Path $cacheRoot 'WebView2'
    $leftover = Join-Path $profiles ([Guid]::NewGuid().ToString('N'))
    $keep = Join-Path $profiles 'keep-me'
    New-Item -ItemType Directory -Force -Path $leftover, $keep | Out-Null
    Set-Content -LiteralPath (Join-Path $leftover 'leftover.txt') -Value 'leftover' -Encoding ASCII

    $originalTheme = Get-AppThemePreference
    $canary = $null
    try {
        $canary = Start-HtmlCanary $logPath
        $baseline = @(Get-HtmlCanaryLines $logPath).Count
        Set-AppThemePreference $true
        Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
        Invoke-HtmlReaderPass 'html-reader-online'
        Save-HtmlDiagnostics 'html-reader-online' $cacheRoot
        if (Test-Path -LiteralPath $leftover) {
            throw 'Startup left a leftover WebView2 profile in place.'
        }
        if (-not (Test-Path -LiteralPath $keep)) {
            throw 'The profile sweep removed a folder that is not a profile.'
        }
        $report.htmlReader.profileSweep = 'removed'
        $report.htmlReader.online = Assert-HtmlReaderPass 'online' $cacheRoot $readerSessions $readerLaunches $logPath $baseline

        Stop-HtmlCanary $canary
        $canary = $null
        if (Test-HtmlCanaryListening) {
            throw 'The loopback canary still answered after it was stopped.'
        }
        Remove-Item -LiteralPath $diagnostics -Recurse -Force
        $baseline = @(Get-HtmlCanaryLines $logPath).Count
        Set-AppThemePreference $false
        Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
        Invoke-HtmlReaderPass 'html-reader-offline'
        Save-HtmlDiagnostics 'html-reader-offline' $cacheRoot
        $report.htmlReader.offline = Assert-HtmlReaderPass 'offline' $cacheRoot $readerSessions $readerLaunches $logPath $baseline

        # The loader runs before WebView2, so Guide B reports its deleted
        # entry; Guide A's session fails at the runtime probe and serves nothing.
        Remove-Item -LiteralPath $ids.guideBEntry -Force
        Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
        $canary = Start-HtmlCanary $logPath
        $baseline = @(Get-HtmlCanaryLines $logPath).Count
        Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
        Invoke-HtmlReaderPass 'html-runtime-missing' 'html-runtime-missing' -NoRuntime
        Save-HtmlDiagnostics 'html-runtime-missing' $cacheRoot
        $report.htmlReader.runtimeMissing = Assert-HtmlReaderPass 'runtime-missing' $cacheRoot `
            @(@{ guideId = $ids.guideA; served = '' }) `
            @('https://developer.microsoft.com/microsoft-edge/webview2/') $logPath $baseline
    }
    finally {
        Stop-HtmlCanary $canary
        Remove-Item -LiteralPath $keep -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $cacheRoot 'missing-runtime-test') -Recurse -Force -ErrorAction SilentlyContinue
        Restore-AppThemePreference $originalTheme
    }
}

function Invoke-HtmlPositionPass([string] $resultName) {
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $gates = @(
        foreach ($name in @('HtmlDiagnostics', 'HtmlPosition', 'ProgressOverride')) {
            [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\DesktopGuides.Preview.$name.$processId")
        })
    try {
        $result = Run-ShellSmoke 'html-position' -ResultName $resultName `
            -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        Close-InstalledShell
        return $result
    }
    finally {
        foreach ($gate in $gates) { $gate.Dispose() }
    }
}

function Assert-HtmlPositionPass([string] $pass, [string] $diagnostics, $result) {
    # Every session served the entry and the eager map, the lazy route map
    # only when the reader got near it, and nothing else; each unimported
    # click was denied once, as a navigation.
    $files = @(Get-ChildItem -LiteralPath $diagnostics -Filter 'html-session-*.json' -ErrorAction SilentlyContinue)
    if ($files.Count -ne [int] $result.sessionsOpened) {
        throw "The $pass pass wrote $($files.Count) HTML session diagnostics; the smoke opened $($result.sessionsOpened)."
    }
    $unimported = 0
    $sessions = @()
    foreach ($file in $files) {
        $session = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $served = @($session.served) -join ','
        if ($served -cne 'guide.html,images/map.png' -and $served -cne 'guide.html,images/map.png,images/route.png') {
            throw "Guide $($session.guideId) served '$served' in the $pass pass."
        }
        foreach ($deny in @($session.denied)) {
            if ($deny.reason -eq 'UnimportedPage' -and $deny.context -eq 'Navigation') {
                $unimported += [int] $deny.count
            }
        }
        $sessions += $session
    }
    if ($unimported -ne [int] $result.unimportedClicks) {
        throw "The $pass pass denied $unimported unimported pages; the smoke clicked $($result.unimportedClicks)."
    }
    # Each restore the smoke saw was counted once, by kind, in diagnostics.
    $counted = @{}
    foreach ($session in $sessions) {
        if (-not $session.restores) { continue }
        foreach ($kind in @($session.restores.PSObject.Properties)) {
            $counted[$kind.Name] = [int] $counted[$kind.Name] + [int] $kind.Value
        }
    }
    $seen = @{}
    foreach ($kind in @($result.restoreKinds)) {
        if ($kind) { $seen[$kind] = [int] $seen[$kind] + 1 }
    }
    $countedText = (@($counted.Keys) | Sort-Object | ForEach-Object { "$_=$($counted[$_])" }) -join ','
    $seenText = (@($seen.Keys) | Sort-Object | ForEach-Object { "$_=$($seen[$_])" }) -join ','
    if ($countedText -cne $seenText) {
        throw "The $pass pass counted restores '$countedText'; the smoke saw '$seenText'."
    }
    return $sessions
}

function Run-HtmlPositionScenarios {
    # TR09.1-TR09.2: capture, resize, restore and unimported links on a
    # long <pre> guide. Light then dark.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    $ids = Invoke-ShellSeed @('seed-html-position', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    $report.htmlPosition = [ordered]@{ guideLong = $ids.guideLong; guideChanged = $ids.guideChanged }
    $originalTheme = Get-AppThemePreference
    try {
        foreach ($pass in @(
            @{ name = 'html-position-light'; light = $true },
            @{ name = 'html-position-dark'; light = $false })) {
            Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath (Join-Path $dataRoot 'test') -Recurse -Force -ErrorAction SilentlyContinue
            Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
            Set-AppThemePreference $pass.light
            try {
                $result = Invoke-HtmlPositionPass $pass.name
                $report.htmlPosition[$pass.name] = $result
            }
            finally {
                Save-HtmlDiagnostics $pass.name (Get-HtmlCacheRoot)
            }
            $report.htmlPosition["$($pass.name)-sessions"] = Assert-HtmlPositionPass $pass.name $diagnostics $result
        }
    }
    finally {
        Restore-AppThemePreference $originalTheme
        Remove-Item -LiteralPath (Join-Path $dataRoot 'test') -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-PdfReaderPass([string] $resultName) {
    Start-InstalledShell
    $diagnosticsGate = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\DesktopGuides.Preview.PdfDiagnostics.$($report.launchedProcessId)")
    try {
        $report.pdfReader[$resultName] = Run-ShellSmoke 'pdf-reader' -ResultName $resultName
        Close-InstalledShell
    }
    finally {
        $diagnosticsGate.Dispose()
    }
}

function Assert-PdfDiagnostics([string] $pass, [string] $diagnostics, [string] $guideId) {
    # Counts only; the file holds no guide text and no paths.
    $path = Join-Path $diagnostics "pdf-$guideId.json"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "The $pass pass wrote no diagnostics for the long PDF guide."
    }
    Copy-Item -LiteralPath $path -Destination (Join-Path $ResultDirectory "$pass.pdf-long.json")
    $counts = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    foreach ($f in @('requests', 'loads', 'staleResults', 'peakCacheBytes', 'maxCacheBytes',
        'cachedPagesAtClose', 'peakTextPages', 'peakTextCharacters', 'disposedCleanly',
        'evictions')) {
        if ($counts.PSObject.Properties.Name -notcontains $f) {
            throw "The $pass pass diagnostics have no '$f'."
        }
    }
    [long] $requests = $counts.requests
    [long] $loads = $counts.loads
    [long] $staleResults = $counts.staleResults
    [long] $peakCacheBytes = $counts.peakCacheBytes
    [long] $maxCacheBytes = $counts.maxCacheBytes
    [long] $cachedPagesAtClose = $counts.cachedPagesAtClose
    [long] $peakTextPages = $counts.peakTextPages
    [long] $evictions = $counts.evictions
    if ($maxCacheBytes -ne 100663296) {
        throw "The $pass pass used a $maxCacheBytes-byte render cap; expected 100663296."
    }
    if ($requests -lt 199) {
        throw "The $pass pass recorded only $requests page requests."
    }
    if ($peakCacheBytes -le 0) {
        throw "The $pass pass cached no page images."
    }
    if ($peakCacheBytes -gt 100663296) {
        throw "The $pass pass cached $peakCacheBytes bytes of page images, over the cap."
    }
    if ($evictions -le 0) {
        throw "The $pass pass never evicted a page image; the cap was not exercised."
    }
    if ($cachedPagesAtClose -ge 200) {
        throw "The $pass pass still held $cachedPagesAtClose page images at close."
    }
    if ($staleResults -le 0 -and $loads -ge $requests) {
        throw "The $pass pass dropped no superseded page: $loads loads for $requests requests."
    }
    if ($peakTextPages -gt 8) {
        throw "The $pass pass kept the text of $peakTextPages pages; expected at most 8."
    }
    if ($counts.disposedCleanly -isnot [bool] -or $counts.disposedCleanly -ne $true) {
        throw "The $pass pass did not close the long PDF guide cleanly."
    }
    return $counts
}

function Run-PdfReaderScenarios {
    # TR10.2-TR10.3: tagged text in UI Automation, an image-only page, 200
    # rapid page turns with a bounded cache, typed errors, and TXT still
    # opening. Light then dark.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\pdf-long.pdf'))) {
        throw 'pdf-long.pdf is missing; run tools/p0/make_fixtures.py first.'
    }
    $ids = Invoke-ShellSeed @('seed-pdf-reader', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    $report.pdfReader = [ordered]@{ pdfLong = $ids.pdfLong }
    $originalTheme = Get-AppThemePreference
    try {
        foreach ($pass in @(
            @{ name = 'pdf-reader-light'; light = $true },
            @{ name = 'pdf-reader-dark'; light = $false })) {
            Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
            Set-AppThemePreference $pass.light
            Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
            Invoke-PdfReaderPass $pass.name
            $report.pdfReader["$($pass.name)-diagnostics"] = Assert-PdfDiagnostics $pass.name $diagnostics $ids.pdfLong
        }
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}

function Run-ImportScenarios {
    Invoke-ShellSeed @('seed-import', $dataRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.importLight = Run-ShellSmoke 'import-preview' -ResultName 'import-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.importDark = Run-ShellSmoke 'import-preview' -ResultName 'import-dark'
        Close-InstalledShell

        $state = Invoke-ShellSeed @('describe-import', $dataRoot) | ConvertFrom-Json
        $report.importPreviewState = $state
        foreach ($name in @('Guides', 'FileOperations', 'StagingEntries', 'ContentEntries')) {
            if ($state.$name -ne 0) {
                throw "The import preview left $($state.$name) $name; expected none."
            }
        }

        Set-AppThemePreference $true
        Start-InstalledShell
        $report.importPublishLight = Run-ShellSmoke 'import-publish' -ResultName 'import-publish-light'
        Close-InstalledShell

        $existingTitle = $report.importPublishLight.importedTitle

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.importDuplicateDark = Run-ShellSmoke 'import-duplicate-copy' `
            -ResultName 'import-duplicate-dark' -ExpectedGuideTitle $existingTitle
        Close-InstalledShell

        Set-AppThemePreference $true
        Start-InstalledShell
        $report.importOpenLight = Run-ShellSmoke 'import-duplicate-open' `
            -ResultName 'import-open-light' -ExpectedGuideTitle $existingTitle
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
    $state = Invoke-ShellSeed @('describe-import', $dataRoot) | ConvertFrom-Json
    $report.importState = $state
    $expected = [ordered]@{
        Guides = 2; FileOperations = 0; StagingEntries = 0; ContentEntries = 2; LegacyTextGuides = 2
    }
    foreach ($name in $expected.Keys) {
        if ($state.$name -ne $expected[$name]) {
            throw "After an import, a copy and Open existing, $name was $($state.$name); expected $($expected[$name])."
        }
    }
}

function Assert-RemovalState([string] $label, $expected) {
    $state = Invoke-ShellSeed @('describe-import', $dataRoot) | ConvertFrom-Json
    foreach ($name in $expected.Keys) {
        if ($state.$name -ne $expected[$name]) {
            throw "$label, $name was $($state.$name); expected $($expected[$name])."
        }
    }
    return $state
}

function Run-RemovalScenarios {
    $title = $report.importPublishLight.importedTitle
    if (-not $title) {
        throw 'Guide removal needs the title from the light import-publish run.'
    }
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $false
        Start-InstalledShell
        $report.removeCancelDark = Run-ShellSmoke 'remove-guide-cancel' `
            -ResultName 'remove-cancel-dark' -ExpectedGuideTitle $title
        Close-InstalledShell

        $report.removeCancelState = Assert-RemovalState 'After Cancel' ([ordered]@{
            Guides = 2; FileOperations = 0; StagingEntries = 0; ContentEntries = 2
            TrashEntries = 0; ReadingStates = 2; ReaderPreferences = 2; LegacyTextGuides = 2
        })

        Set-AppThemePreference $true
        Start-InstalledShell
        $report.removeLight = Run-ShellSmoke 'remove-guide' `
            -ResultName 'remove-light' -ExpectedGuideTitle $title
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
    $report.removeState = Assert-RemovalState 'After removal' ([ordered]@{
        Guides = 1; FileOperations = 0; StagingEntries = 0; ContentEntries = 1
        TrashEntries = 0; ReadingStates = 1; ReaderPreferences = 1; LegacyTextGuides = 1
    })
}

function Assert-GameActionsState([string] $label, $seed) {
    $state = Invoke-ShellSeed @('describe-actions', $dataRoot) | ConvertFrom-Json
    if ($state.GameCount -ne 1) {
        throw "$label, the library had $($state.GameCount) games; expected 1."
    }
    $game = @($state.Games)[0]
    if ($game.Id -ne $seed.RenameGameId -or $game.Title -ne 'Renamed Linked Game' -or
        $game.ExternalId -ne '900100' -or -not $game.ArtworkExists) {
        throw "$label, the remaining game was $($game | ConvertTo-Json -Compress)."
    }
    if (@($state.ArtworkFolders) -contains $seed.EmptyGameId) {
        throw "$label, the removed game's artwork folder remains."
    }
    $guides = @(@($game.GuideIds) | Sort-Object)
    $expected = @(@($seed.AlphaGuideId, $seed.BetaGuideId) | Sort-Object)
    if (($guides -join ',') -ne ($expected -join ',')) {
        throw "$label, the guide IDs were $($guides -join ', ')."
    }
    $alpha = @($state.ReadingStates | Where-Object { $_.GuideId -eq $seed.AlphaGuideId })
    if ($alpha.Count -ne 1 -or $alpha[0].EstimatedFraction -ne 0.45 -or
        $alpha[0].LastOpenedUtcMs -ne $seed.AlphaLastOpenedUtcMs) {
        throw "$label, Alpha Route Guide's reading state was $($alpha | ConvertTo-Json -Compress)."
    }
    if ($state.LastActiveGuideId -ne $seed.BetaGuideId) {
        throw "$label, the Resume guide was $($state.LastActiveGuideId)."
    }
    if (@($state.ArtworkFolders) -contains $seed.GuidedGameId) {
        throw "$label, the guided game's artwork folder remains."
    }
    $removedGuides = @($seed.GuidedGuideIds)
    $leftContent = @(@($state.ContentDirectories) | Where-Object { $removedGuides -contains $_ })
    if ($leftContent.Count -ne 0) {
        throw "$label, removed guide content remains: $($leftContent -join ', ')."
    }
    $leftStates = @(@($state.ReadingStates) | Where-Object { $removedGuides -contains $_.GuideId })
    if ($leftStates.Count -ne 0) {
        throw "$label, removed guides' reading states remain."
    }
    foreach ($kept in @($seed.AlphaGuideId, $seed.BetaGuideId)) {
        if (@($state.ContentDirectories) -notcontains $kept) {
            throw "$label, guide $kept lost its content."
        }
    }
    if ($state.TrashEntries -ne 0 -or $state.FileOperations -ne 0) {
        throw "$label, $($state.TrashEntries) trash entries and $($state.FileOperations) file operations remain."
    }
    return $state
}

function Run-GameActionsScenarios {
    $originalTheme = Get-AppThemePreference
    try {
        $seed = Invoke-ShellSeed @('seed-actions', $dataRoot) | ConvertFrom-Json
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.gameActionsLight = Run-ShellSmoke 'game-actions' -ResultName 'game-actions-light'
        Close-InstalledShell
        $report.gameActionsLightState = Assert-GameActionsState 'After the light run' $seed

        Start-InstalledShell
        $report.gameActionsPersisted = Run-ShellSmoke 'game-actions-persisted'
        Close-InstalledShell
        $report.gameActionsPersistedState = Assert-GameActionsState 'After the relaunch' $seed

        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        $seed = Invoke-ShellSeed @('seed-actions', $dataRoot) | ConvertFrom-Json
        Set-AppThemePreference $false
        Start-InstalledShell
        $report.gameActionsDark = Run-ShellSmoke 'game-actions' -ResultName 'game-actions-dark'
        Close-InstalledShell
        $report.gameActionsDarkState = Assert-GameActionsState 'After the dark run' $seed
    }
    finally {
        Restore-AppThemePreference $originalTheme
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
    $dialog = Get-DialogStripRegion $result.dialogBounds.buttonLeft `
        $result.dialogBounds.buttonBottom
    $dialogStrip = Get-ScreenshotRegionStats $result.dialogScreenshot `
        $dialog.X $dialog.Y $dialog.Width $dialog.Height
    $result | Add-Member -NotePropertyName dialogStrip -NotePropertyValue $dialogStrip
    return $result
}

# These passes check what the app controls: the material it reports, its own
# layers, the dialog style, and the stored choice. How Windows renders Mica
# and Acrylic is not tested.
function Run-MaterialScenarios {
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
            # Mica is not checked: with transparency off, Windows draws it in
            # the same color as our Solid fill.
            $acrylic = $report.materials["$themeName-Acrylic"].libraryStrip
            if (Test-MatchesSolidFill $acrylic $solid) {
                throw "The $themeName Acrylic window shows the Solid fill; " +
                    'an app layer covers the backdrop.'
            }
            $solidDialog = $report.materials["$themeName-Solid"].dialogStrip
            $acrylicDialog = $report.materials["$themeName-Acrylic"].dialogStrip
            $report.materials["$themeName-dialogDifference"] =
                Get-AcrylicSurfaceDifference $acrylicDialog $solidDialog
            if (-not (Test-AcrylicSurfaceVisible $acrylicDialog $solidDialog)) {
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

function Invoke-ShellSeed([string[]] $SeedArguments) {
    $output = @(dotnet run --project $seedProject -c Release --no-restore -- @SeedArguments)
    if ($LASTEXITCODE -ne 0) {
        throw "ShellSeed $($SeedArguments[0]) failed: $($output | Select-Object -Last 3)"
    }
    return $output | Select-Object -Last 1
}

function Get-ProviderState {
    return Invoke-ShellSeed @('describe-providers', $dataRoot) | ConvertFrom-Json
}

function Assert-NoCredentialLeak([string[]] $Secrets, [string] $Label) {
    $found = @(Find-SecretInFiles @($ResultDirectory, $dataRoot) $Secrets)
    $report.providers["leakScan$Label"] = [ordered]@{
        roots = @($ResultDirectory, $dataRoot)
        filesWithCredentialValues = $found
    }
    if ($found.Count -gt 0) {
        throw "Credential values were found in $($found.Count) file(s): $($found -join ', ')"
    }
}

function Wait-HandshakeFile([string] $Path, [int] $Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $Path) {
            $content = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
            if ($content.runId -eq $runId) { return $content }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Run-BlockedNetworkScenario {
    $handshake = Join-Path $ResultDirectory 'offline-handshake'
    New-Item -ItemType Directory -Force $handshake | Out-Null
    [ordered]@{ runId = $runId; program = $expectedExecutablePath } |
        ConvertTo-Json | Set-Content (Join-Path $handshake 'request.json') -Encoding UTF8
    $blocked = Wait-HandshakeFile (Join-Path $handshake 'blocked.json') 120
    if (-not $blocked) {
        throw 'The offline controller did not confirm the firewall rule.'
    }
    $report.providers.firewallRule = $blocked.ruleName
    try {
        Start-InstalledShell
        $report.providers.blockedNetwork = Run-ShellSmoke 'provider-offline' `
            -ResultName 'provider-offline-blocked' -ExpectedProviderFailure 'Unavailable'
        Close-InstalledShell
    }
    finally {
        [ordered]@{ runId = $runId } | ConvertTo-Json |
            Set-Content (Join-Path $handshake 'done.json') -Encoding UTF8
        $restored = Wait-HandshakeFile (Join-Path $handshake 'restored.json') 60
        $report.providers.firewallRuleRemoved = [bool]($restored -and $restored.removed)
        if (-not $report.providers.firewallRuleRemoved) {
            $cleanupErrors.Add('The offline controller did not confirm that it removed the firewall rule.')
        }
    }
}

function Run-ProviderScenarios {
    $report.providers = [ordered]@{}
    $providers = $report.providers
    $fixtureDirectory = Join-Path $PSScriptRoot `
        '..\..\tests\DesktopGuides.Infrastructure.Tests\Providers\Fixtures'
    $secrets = @()
    $igdbReady = $false
    $steamGridDbFile = ''
    try {
        $secrets += @((Read-LabelledValues $IgdbCredentialFile `
            @('igdb client id', 'igdb client secret')).Values)
        $igdbReady = $true
    }
    catch {
        $providers.live = 'skipped'
        $providers.liveSkipReason = $_.Exception.Message
    }
    try {
        $secrets += @((Read-LabelledValues $SteamGridDbCredentialFile `
            @('steamgriddb api key')).Values)
        $steamGridDbFile = $SteamGridDbCredentialFile
    }
    catch {
        $providers.steamGridDb = 'skipped'
        $providers.steamGridDbSkipReason = $_.Exception.Message
    }

    [void](Invoke-ShellSeed @('seed-linked-game', $dataRoot))
    Start-InstalledShell
    $providers.none = Run-ShellSmoke 'provider-none'
    $providers.offlineWithoutCredentials = Run-ShellSmoke 'provider-offline' `
        -ResultName 'provider-offline-none' -ExpectedProviderFailure 'NotConfigured'
    if ($igdbReady) {
        $providers.settings = Run-ShellSmoke 'provider-settings' `
            -IgdbCredentialFile $IgdbCredentialFile `
            -SteamGridDbCredentialFile $steamGridDbFile
    }
    Close-InstalledShell
    if (-not $igdbReady) {
        $providers.blockedNetwork = 'skipped: no IGDB credential file'
        $providers.finalState = Get-ProviderState
        return
    }

    $providers.fixtureFields = Invoke-ShellSeed @(
        'check-igdb-fields', $IgdbCredentialFile, $fixtureDirectory)
    $saved = Get-ProviderState
    if (-not $saved.CredentialBlobExists) {
        throw 'Saving provider credentials did not create providers.bin.'
    }

    Start-InstalledShell
    $providers.live = Run-ShellSmoke 'provider-live'
    Close-InstalledShell
    $providers.cancelDuringSearch = if ($providers.live.cancelObserved) {
        'observed'
    } else {
        "not-observed: $($providers.live.cancelReason)"
    }
    $state = Get-ProviderState
    $halfLife = @($state.Games | Where-Object { $_.Title -eq 'Half-Life' })
    if ($halfLife.Count -ne 1 -or $halfLife[0].Provider -ne 'igdb' -or
        -not $halfLife[0].HasMetadata -or
        $halfLife[0].Platform -ne 'My test platform' -or
        $halfLife[0].Notes -ne 'My local notes.') {
        throw "The added game was not stored as expected: $($halfLife | ConvertTo-Json -Compress)"
    }
    $withArtwork = @($state.Games | Where-Object { $_.ArtworkRelativePath })
    if (@($withArtwork | Where-Object { -not $_.ArtworkExists }).Count -gt 0 -or
        $state.ArtworkFiles -ne $withArtwork.Count -or $state.StagingFiles -ne 0) {
        throw "Artwork files did not match the rows: $($state | ConvertTo-Json -Compress -Depth 4)"
    }
    $providers.liveState = $state
    Assert-NoCredentialLeak $secrets 'AfterLive'

    if ($AllowOfflineFirewallRule) {
        Run-BlockedNetworkScenario
    }
    else {
        $providers.blockedNetwork = 'not-run: -AllowOfflineFirewallRule was not passed'
    }

    Start-InstalledShell
    $providers.remove = Run-ShellSmoke 'provider-remove'
    Close-InstalledShell
    $providers.finalState = Get-ProviderState
    if ($providers.finalState.CredentialBlobExists) {
        throw 'Removing provider credentials left providers.bin behind.'
    }
    Assert-NoCredentialLeak $secrets 'AfterRemove'
}

try {
    $report.windowsAppRuntime = @(
        Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2*' |
            Where-Object { $_.Architecture -eq 'X64' } |
            Select-Object -ExpandProperty Version |
            ForEach-Object { $_.ToString() }
    )
    $report.dotnetSdk = (dotnet --version)
    if ($portable) {
        $report.portableExecutable = $PortableExecutable
        $report.portableExecutableSha256 =
            (Get-FileHash $PortableExecutable -Algorithm SHA256).Hash
        $expectedExecutablePath = $PortableExecutable
        Assert-FreshPortableProfile $env:LOCALAPPDATA
        $portableDataOwned = $true
        $dataRoot = $portableDataRoot
    }
    else {
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
    }
    New-Item -ItemType Directory -Force $dataRoot | Out-Null
    $seedProject = Join-Path $PSScriptRoot `
        'DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj'

    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME `
        -LogonType Interactive -RunLevel Limited
    $launchAction = New-ShellLaunchAction $launchResultPath
    $secondLaunchAction = New-ShellLaunchAction $secondLaunchResultPath
    Register-ScheduledTask -TaskName $launchTask -Action $launchAction `
        -Principal $principal -Force | Out-Null

    # Each group after the first starts from an empty data root, so a single
    # group run against a fresh install sees what the full run gives it.
    function Enter-ScenarioGroup([string] $name) {
        if ($scenarioScope -ne 'all' -and $scenarioScope -ne $name) { return $false }
        if ($report.scenarioGroupsRun.Count -gt 0) {
            Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        }
        $report.scenarioGroupsRun.Add($name)
        return $true
    }

    if (Enter-ScenarioGroup 'core') {
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
    }

    if (Enter-ScenarioGroup 'design') {
        dotnet run --project $seedProject -c Release --no-restore -- `
            seed-design $dataRoot
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not seed the design-language metadata.'
        }
        Run-DesignLanguageScenarios
        Run-MaterialScenarios
    }

    if (Enter-ScenarioGroup 'catalog') {
        Run-CatalogScenarios
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Run-CatalogFactsScenarios
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Run-LibrarySearchScenarios
        Run-StableNavigationScenarios
    }

    if (Enter-ScenarioGroup 'txt') { Run-TxtReaderScenarios }

    if (Enter-ScenarioGroup 'html') {
        Run-HtmlReaderScenarios
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Run-HtmlPositionScenarios
    }

    if (Enter-ScenarioGroup 'pdf') { Run-PdfReaderScenarios }

    if (Enter-ScenarioGroup 'import') {
        Run-ImportScenarios
        Run-RemovalScenarios
    }

    if (Enter-ScenarioGroup 'game-actions') { Run-GameActionsScenarios }

    if (Enter-ScenarioGroup 'provider') { Run-ProviderScenarios }

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
    if ($dataRoot) {
        $blob = Join-Path $dataRoot 'providers.bin'
        if (Test-Path -LiteralPath $blob) {
            Stop-InstalledShell -BestEffort
            Remove-Item -LiteralPath $blob -Force -ErrorAction SilentlyContinue
            $report.credentialBlobDeletedByCleanup = -not (Test-Path -LiteralPath $blob)
            if (-not $report.credentialBlobDeletedByCleanup) {
                $cleanupErrors.Add('Could not delete providers.bin during cleanup.')
            }
        }
    }
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
    if ($expectedExecutablePath) { Stop-InstalledShell -BestEffort }
    $shellProcessesAfterStop = @(Get-InstalledShellProcesses)
    if ($shellProcessesAfterStop.Count -gt 0) {
        $cleanupErrors.Add(
            "Installed shell processes remain after cleanup: $(
                ($shellProcessesAfterStop | ForEach-Object { $_.ProcessId }) -join ', ').")
    }
    if ($portable) {
        # Only a folder this run created is deleted, and only once nothing
        # can still be writing to it.
        if ($portableDataOwned -and $tasksDrained -and
            $shellProcessesAfterStop.Count -eq 0) {
            Remove-Item -LiteralPath $portableDataRoot -Recurse -Force `
                -ErrorAction SilentlyContinue
        }
        $report.portableDataStillPresent = $portableDataOwned -and
            (Test-Path -LiteralPath $portableDataRoot)
        if ($report.portableDataStillPresent) {
            $report.success = $false
            $report.cleanupError = 'The portable data folder this run created remains.'
        }
    }
    else {
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
    }
    if ($cleanupErrors.Count -gt 0) {
        $report.success = $false
        $report.processCleanupErrors = @($cleanupErrors)
    }
    $report | ConvertTo-Json -Depth 8 |
        Set-Content (Join-Path $ResultDirectory $(
            if ($portable) { 'portable-run.json' } else { 'signed-install.json' })) `
            -Encoding UTF8
}
if (-not $report.success) { exit 1 }
