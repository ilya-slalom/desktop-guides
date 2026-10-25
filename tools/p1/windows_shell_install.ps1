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
    [switch] $ProgressOnly,
    [switch] $CompletionOnly,
    [switch] $ThemeOnly,
    [switch] $TextSizeOnly,
    [switch] $ImportOnly,
    [switch] $GameActionsOnly,
    [switch] $ProviderOnly,
    [switch] $BackupOnly,
    [switch] $WorkflowOnly,
    # T17.3: the release-candidate journey. Needs -Lane Public, -ReleaseSigned,
    # -PreviousPackage and -PackagePath (the candidate); runs alone.
    [switch] $RcOnly,
    # T18.1: in-guide search.
    [switch] $NavigationOnly,
    [switch] $MapsOnly,
    # S22: Find online against fixture pages; no live site is loaded.
    [switch] $OnlineOnly,

    # Or name several groups, as CI shards do. They still run in the order
    # listed below. Don't combine with an *Only switch.
    [string[]] $Groups,

    # With the pdf group alone, run only the PDF passes whose names match one
    # of these wildcards, for example pdf-zoom-* or pdf-locked-light. Other
    # groups chain state from one mode to the next, so they can't be filtered.
    [string[]] $PassFilter,

    # Paths only. The values are read in memory and never passed on.
    [string] $IgdbCredentialFile = 'E:\work\igdb_credentials.txt',

    [string] $SteamGridDbCredentialFile = 'E:\work\steamgriddb_credentials.txt',

    # Wait for windows_provider_offline_controller.ps1 to block the app's
    # network access for one scenario. Needs the user's authorization.
    [switch] $AllowOfflineFirewallRule,

    # T17.2 D: with no real IGDB credential file, run the provider group
    # against the recorded fixtures in tests/fixtures/p1/providers.
    [switch] $FakeProviders,

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
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'windows_shell_lane.ps1')
# Not $lane: PowerShell names are case-insensitive, so that would assign to
# the validated -Lane parameter.
$laneNames = Get-ShellLane $Lane
$lanePackage = $laneNames.PackageName
$eventPrefix = $laneNames.EventPrefix
# Scenario groups in run order; CI's shell-scope input uses these names.
$scenarioGroups = [ordered]@{
    'core' = $CoreOnly.IsPresent
    'design' = $DesignOnly.IsPresent
    'catalog' = $CatalogOnly.IsPresent
    'txt' = $TxtOnly.IsPresent
    'html' = $HtmlOnly.IsPresent
    'pdf' = $PdfOnly.IsPresent
    'progress' = $ProgressOnly.IsPresent
    'completion' = $CompletionOnly.IsPresent
    'theme' = $ThemeOnly.IsPresent
    'text-size' = $TextSizeOnly.IsPresent
    'import' = $ImportOnly.IsPresent
    'game-actions' = $GameActionsOnly.IsPresent
    'provider' = $ProviderOnly.IsPresent
    'backup' = $BackupOnly.IsPresent
    'workflow' = $WorkflowOnly.IsPresent
    'navigation' = $NavigationOnly.IsPresent
    'maps' = $MapsOnly.IsPresent
    'online' = $OnlineOnly.IsPresent
    'rc' = $RcOnly.IsPresent
}
$selectedGroups = @($scenarioGroups.Keys | Where-Object { $scenarioGroups[$_] })
if ($selectedGroups.Count -gt 1) {
    throw "Pass at most one *Only switch; got $($selectedGroups.Count)."
}
if ($Groups) {
    if ($selectedGroups.Count -gt 0) { throw 'Pass either -Groups or one *Only switch.' }
    $unknown = @($Groups | Where-Object { $_ -notin $scenarioGroups.Keys })
    if ($unknown.Count -gt 0) { throw "Unknown scenario group: $($unknown -join ', ')." }
    $selectedGroups = @($scenarioGroups.Keys | Where-Object { $_ -in $Groups })
}
$scenarioScope = if ($selectedGroups.Count -gt 0) { $selectedGroups -join ',' } else { 'all' }
# T17.3: rc runs only when asked for, on a release candidate.
if ($selectedGroups -contains 'rc' -and -not $RcOnly) { throw 'Run the rc group with -RcOnly.' }
if ($RcOnly -and -not ($Lane -eq 'Public' -and $ReleaseSigned -and $PackagePath -and $PreviousPackage)) {
    throw '-RcOnly needs -Lane Public, -ReleaseSigned, -PackagePath (the candidate) and -PreviousPackage.'
}
$pdfModes = @('pdf-reader', 'pdf-zoom', 'pdf-keys', 'pdf-jump', 'pdf-locked')
function Test-PdfPassSelected([string] $pass) {
    if (-not $PassFilter) { return $true }
    return [bool]@($PassFilter | Where-Object { $pass -like $_ })
}
if ($PassFilter) {
    if ($scenarioScope -ne 'pdf') { throw '-PassFilter needs the pdf group alone.' }
    $pdfPasses = @(foreach ($mode in $pdfModes) { "$mode-light" }) + 'pdf-offline'
    if (-not @($pdfPasses | Where-Object { Test-PdfPassSelected $_ })) {
        throw "-PassFilter matched none of: $($pdfPasses -join ', ')."
    }
}
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
if ($ReleaseSigned -and $portable) { throw '-ReleaseSigned applies only to an MSIX.' }
if ($ReleaseSigned -and $Lane -ne 'Public') { throw '-ReleaseSigned needs -Lane Public.' }
if (-not $portable -and $Lane -eq 'Public' -and -not $ReleaseSigned) {
    throw 'A Public MSIX can only be installed release-signed: pass -ReleaseSigned.'
}
if ($PreviousPackage -and -not $RcOnly) { throw '-PreviousPackage applies only to the rc group.' }
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
    # Release assets are DesktopGuides-<version>-x64.msix (release.yml).
    $expectedName = if ($ReleaseSigned) { '^DesktopGuides-[0-9]+(\.[0-9]+){3}-x64\.msix$' }
        else { '^DesktopGuides\.Production_[0-9]+(\.[0-9]+){3}_x64\.msix$' }
    if ($packageName -notmatch $expectedName) {
        throw "Expected an x64 production MSIX, got $packageName."
    }
    if ($PreviousPackage -and (Split-Path $PreviousPackage -Leaf) -notmatch $expectedName) {
        throw "Expected the previous release's MSIX, got $(Split-Path $PreviousPackage -Leaf)."
    }
    if (Get-AppxPackage -Name $lanePackage) {
        throw "A $lanePackage package is already installed; refusing to replace it."
    }
    Assert-FreshPackageProfile $env:LOCALAPPDATA $lanePackage
}
. (Join-Path $PSScriptRoot 'windows_shell_smoke_result.ps1')
. (Join-Path $PSScriptRoot 'windows_shell_task_cleanup.ps1')
. (Join-Path $PSScriptRoot 'windows_shell_screenshot_stats.ps1')
. (Join-Path $PSScriptRoot 'windows_shell_theme_preference.ps1')
. (Join-Path $PSScriptRoot 'windows_provider_credentials.ps1')

New-Item -ItemType Directory -Force $ResultDirectory | Out-Null
$ResultDirectory = (Resolve-Path $ResultDirectory).Path
$seedProject = Join-Path $PSScriptRoot 'DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj'
$seedDll = Join-Path $PSScriptRoot 'DesktopGuides.ShellSeed\bin\Release\net10.0\DesktopGuides.ShellSeed.dll'
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
    # T17.2: each Run-ShellSmoke call's timing, for the evidence checklist.
    smokeRuns = [System.Collections.Generic.List[object]]::new()
    success = $false
}
$certificate = $null
$installed = $null
$expectedExecutablePath = $null
$imported = $false
. (Join-Path $PSScriptRoot 'windows_shell_process.ps1')
. (Join-Path $PSScriptRoot 'windows_shell_run_record.ps1')
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
        "Local\$eventPrefix.RedirectedActivation")
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
    [string] $ReleasePath,
    [int] $HoldSeconds = 0) {
    if ($Mode -notin @('hold-write-lock', 'hold-read-lock')) {
        throw "Unsupported shell database lock mode $Mode."
    }
    if (-not (Test-Path $seedDll)) {
        throw 'Shell seed executable is unavailable for the database lock.'
    }
    $lockArguments = @(
        ('"{0}"' -f $seedDll), $Mode, ('"{0}"' -f $dataRoot),
        ('"{0}"' -f $ReadyPath), ('"{0}"' -f $ReleasePath)
    )
    if ($HoldSeconds -gt 0) { $lockArguments += [string]$HoldSeconds }
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
    $report.resumeAfterCloseRelaunch = Run-ShellSmoke 'relaunch-check' 'Blocked Write Guide'
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
    $report.resumeAfterLateClose = Run-ShellSmoke 'relaunch-check' 'Blocked Write Guide'
}

function Assert-FailedLaterGuideDoesNotSaveEarlier {
    Start-InstalledShell
    $report.resumeBeforeFailedGuide = Run-ShellSmoke 'relaunch-check' 'Blocked Write Guide'
    dotnet $seedDll `
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
        "Local\$eventPrefix.ReaderLoad.$($processId).Reached")
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\$eventPrefix.ReaderLoad.$($processId).Continue")
    $fixtureCorrupted = $false
    try {
        $report.queuedBeforeRenderError = Run-ShellSmoke 'queue-reader-render-error'
        if (-not $reached.WaitOne(15000)) {
            throw 'Reader route did not reach its second metadata read.'
        }
        dotnet $seedDll `
            corrupt-reader-guide $dataRoot
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not corrupt the disposable Reader metadata fixture.'
        }
        $fixtureCorrupted = $true
        $resume.Set() | Out-Null
        $report.readerRenderError = Run-ShellSmoke 'reader-render-error-observed'
        dotnet $seedDll `
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
                dotnet $seedDll `
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
        "Local\$eventPrefix.RedirectSelected")
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\$eventPrefix.RedirectContinue")
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
        $report.resumeAfterClosingTarget = Run-ShellSmoke 'relaunch-check'
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
        "Local\$eventPrefix.ActivationQueued")
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\$eventPrefix.ActivationContinue")
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
        $report.resumeAfterQueuedActivation = Run-ShellSmoke 'relaunch-check'
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
        "Local\$eventPrefix.AcceptanceReceived")
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\$eventPrefix.AcceptanceContinue")
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

# The app's crash and error-report entries in the Application log since $Since.
function Get-AppCrashEvents([datetime] $Since) {
    return @(
        Get-WinEvent -FilterHashtable @{
            LogName = 'Application'
            StartTime = $Since
        } -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Message -match 'DesktopGuides\.Production' -and
            $_.ProviderName -in @('.NET Runtime', 'Application Error',
                'Windows Error Reporting')
        })
}

function Run-ShellSmoke(
    [string] $mode,
    [string] $expectedResumeGuide = 'Route Test Guide',
    [int] $ExitDelayMilliseconds = 0,
    [string] $ResultName = $mode,
    [int] $ExpectedScalePercent = 0,
    [string] $ExpectedMaterial = '',
    [string] $SwitchToMaterial = '',
    [string] $ExpectedIcons = '',
    [string] $SwitchIcons = '',
    [switch] $EmptyLibrary,
    [string] $ExpectedTheme = '',
    [string] $ExpectedThemeStatus = '',
    [string] $SwitchToTheme = '',
    [string] $WindowsTheme = '',
    [string] $IgdbCredentialFile = '',
    [string] $SteamGridDbCredentialFile = '',
    [string] $ExpectedProviderFailure = '',
    [string] $ExpectedGuideTitle = '',
    [int] $ExpectedTopLine = 0,
    [string] $AppDataRoot = '',
    [string] $AppCacheRoot = '',
    [string] $BackupPath = '',
    [string] $WorkflowRoot = '',
    [string] $WorkflowStatePath = '') {
    $opensBefore = @(Read-GuideOpenTimes).Count
    try {
        return Measure-SmokeRun $report.smokeRuns $ResultName $mode {
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
                ' -ExpectedScalePercent ' + $ExpectedScalePercent +
                ' -ExpectedTopLine ' + $ExpectedTopLine
            $arguments += ' -EventPrefix ' + $eventPrefix
            if ($ExpectedMaterial) {
                $arguments += ' -ExpectedMaterial ' + $ExpectedMaterial
            }
            if ($SwitchToMaterial) {
                $arguments += ' -SwitchToMaterial ' + $SwitchToMaterial
            }
            if ($ExpectedIcons) { $arguments += ' -ExpectedIcons ' + $ExpectedIcons }
            if ($SwitchIcons) { $arguments += ' -SwitchIcons ' + $SwitchIcons }
            if ($EmptyLibrary) { $arguments += ' -EmptyLibrary' }
            if ($ExpectedTheme) {
                $arguments += ' -ExpectedTheme ' + $ExpectedTheme
            }
            if ($ExpectedThemeStatus) {
                $arguments += ' -ExpectedThemeStatus "' + $ExpectedThemeStatus + '"'
            }
            if ($SwitchToTheme) {
                $arguments += ' -SwitchToTheme ' + $SwitchToTheme
            }
            if ($WindowsTheme) {
                $arguments += ' -WindowsTheme ' + $WindowsTheme
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
            if ($BackupPath) { $arguments += ' -BackupPath "' + $BackupPath + '"' }
            if ($WorkflowRoot) { $arguments += ' -WorkflowRoot "' + $WorkflowRoot + '"' }
            if ($WorkflowStatePath) { $arguments += ' -WorkflowStatePath "' + $WorkflowStatePath + '"' }
            $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
                -Argument $arguments -WorkingDirectory $PSScriptRoot
            Register-ScheduledTask -TaskName $smokeTask -Action $action `
                -Principal $principal -Force | Out-Null
            Start-ScheduledTask -TaskName $smokeTask
            $timeoutSeconds = if ($mode -like 'provider-*' -or $mode -like 'pdf-*' -or $mode -like 'progress-*' -or $mode -like 'completion-*' -or $mode -like 'export-*' -or $mode -like 'restore-*' -or $mode -like 'workflow-*' -or $mode -eq 'html-position') { 240 }
                elseif ($mode -like 'catalog*' -or $mode -like 'import-*' -or $mode -like 'game-actions*' -or $mode -like 'html-*' -or $mode -like 'theme-*' -or $mode -like 'text-size-*') { 120 }
                # #102: room for one guide open at the smoke's 60 s budget.
                else { 120 }
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
                if ("$($result.error)" -match "Loading guide|Web page guides couldn't start") {
                    Save-OpenStallEvidence $ResultName
                }
                throw "Installed $mode shell smoke failed: $($result.error)"
            }
            return $result
        }
    }
    finally {
        # #102: slow guide opens stay visible in the evidence when they pass.
        $times = @(Read-GuideOpenTimes)
        if ($times.Count -gt $opensBefore -and $report.smokeRuns.Count -gt 0) {
            $slowest = ($times[$opensBefore..($times.Count - 1)] | Measure-Object -Maximum).Maximum
            $report.smokeRuns[$report.smokeRuns.Count - 1]['slowestOpenSeconds'] = [Math]::Round($slowest / 1000, 1)
        }
    }
}

# #102: each finished guide open in the launched app, in milliseconds. A read
# can meet the app's replace of the file, so it tries again for a moment.
function Read-GuideOpenTimes {
    $path = Join-Path (Get-HtmlCacheRoot) "diagnostics\html-open-$($report.launchedProcessId).json"
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        if (-not (Test-Path -LiteralPath $path)) { return @() }
        try {
            return @((Get-Content -LiteralPath $path -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json).ready)
        }
        catch {
            Start-Sleep -Milliseconds 50
        }
    }
    return @()
}

# #102: what the app and WebView2 were doing when a guide open never
# finished. The app is still running here; the shard stops after this.
function Save-OpenStallEvidence([string] $resultName) {
    $evidence = [ordered]@{ processId = $report.launchedProcessId }
    try {
        $timeline = Join-Path (Get-HtmlCacheRoot) "diagnostics\html-open-$($report.launchedProcessId).json"
        $evidence.timeline = if (Test-Path -LiteralPath $timeline) {
            Get-Content -LiteralPath $timeline -Raw -Encoding UTF8 | ConvertFrom-Json
        } else { $null }
        $app = Get-Process -Id $report.launchedProcessId -ErrorAction SilentlyContinue
        $evidence.appResponding = if ($app) { $app.Responding } else { $null }
        $evidence.webView2 = @(
            Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" |
                ForEach-Object {
                    $type = if ($_.CommandLine -match '--type=(\S+)') { $Matches[1] } else { 'browser' }
                    $userData = if ($_.CommandLine -match '--user-data-dir="?([^"]+?)"?(\s|$)') {
                        Split-Path -Leaf $Matches[1]
                    } else { $null }
                    [ordered]@{
                        processId = $_.ProcessId
                        parentProcessId = $_.ParentProcessId
                        created = $_.CreationDate.ToString('o')
                        type = $type
                        profile = $userData
                    }
                })
    }
    catch {
        $evidence.error = $_ | Out-String
    }
    $evidence | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $ResultDirectory "$resultName.open-stall.json") -Encoding UTF8
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

# T14.4: each reader at wide and narrow widths in light and dark.
function Run-ReaderDesignScenarios {
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    [void](Invoke-ShellSeed @('seed-text-size', $dataRoot, $fixtureRoot))
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.designReadersLight = Run-ShellSmoke 'design-readers' `
            -ResultName 'design-readers-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.designReadersDark = Run-ShellSmoke 'design-readers' `
            -ResultName 'design-readers-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
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
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}

function Run-StableNavigationScenarios {
    # The smoke renames and removes, so it starts from a fresh seed.
    $originalTheme = Get-AppThemePreference
    try {
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Invoke-ShellSeed @('seed-navigation', $dataRoot) | Out-Null
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.stableNavigationLight = Run-ShellSmoke 'stable-navigation' -ResultName 'stable-navigation-light'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}

function Run-TxtReaderScenarios {
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
        # T16.1: the TXT reader keys, on the long guide.
        Start-InstalledShell
        $report.keyboardTxt = Run-ShellSmoke 'keyboard-txt'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
    Assert-TxtBackDuringLoad
    Assert-LibraryRecovery
}

function Assert-TxtBackDuringLoad {
    # Holds a TXT load at its test gate so Back runs while the load is in flight.
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $reached = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::AutoReset,
        "Local\$eventPrefix.TextLoad.$($processId).Reached")
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\$eventPrefix.TextLoad.$($processId).Continue")
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

function Assert-LibraryRecovery {
    # T15.1: the shell must leave a broken library byte-for-byte unchanged,
    # then open it after the seed restores it and Try again runs.
    foreach ($break in @('damaged', 'missing')) {
        $key = if ($break -eq 'damaged') { 'libraryDamaged' } else { 'libraryMissing' }
        $before = Invoke-ShellSeed @('break-library', $dataRoot, $break)
        try {
            Start-InstalledShell
            $report[$key] = Run-ShellSmoke "library-$break" -ResultName "library-$break"
            $after = Invoke-ShellSeed @('break-library', $dataRoot, 'describe')
            if ($after -ne $before) {
                throw "The $break library changed while the shell showed it: '$before' became '$after'."
            }
        }
        finally {
            Invoke-ShellSeed @('break-library', $dataRoot, 'restore') | Out-Null
        }
        $report["${key}Retry"] = Run-ShellSmoke 'library-retry' -ResultName "library-$break-retry"
        Close-InstalledShell
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
    # Ruling 19 of the T15.1 plan: the startup runtime probe runs as soon as
    # the library opens, so the shell waits on the lease until the gate exists.
    $lease = $null
    if ($NoRuntime) {
        $lease = [System.IO.File]::Open(
            (Join-Path $dataRoot 'library.session.lock'),
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    }
    try {
        Start-InstalledShell
    }
    catch {
        if ($lease) { $lease.Dispose() }
        throw
    }
    $processId = $report.launchedProcessId
    $diagnosticsGate = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\$eventPrefix.HtmlDiagnostics.$processId")
    $launchGate = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\$eventPrefix.ExternalLaunch.$processId")
    # Points the runtime probe at an empty folder, as on a PC without WebView2.
    $runtimeGate = $null
    if ($NoRuntime) {
        $runtimeGate = [System.Threading.EventWaitHandle]::new(
            $false, [System.Threading.EventResetMode]::ManualReset,
            "Local\$eventPrefix.WebView2Missing.$processId")
    }
    if ($lease) { $lease.Dispose() }
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
            @('https://developer.microsoft.com/microsoft-edge/webview2/',
              'https://developer.microsoft.com/microsoft-edge/webview2/') $logPath $baseline
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
        foreach ($name in @('HtmlDiagnostics', 'HtmlPosition', 'ProgressDiagnostics', 'ProgressOverride')) {
            [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\$eventPrefix.$name.$processId")
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
        # T14.3: the picture guide serves its entry and its one tile.
        $allowed = if ($session.guideId -eq $report.htmlPosition.guidePictures) {
            @('guide.html,images/tile.png')
        } elseif ($session.guideId -eq $report.htmlPosition.guideFixedHeader) {
            @('guide.html')
        } else {
            @('guide.html,images/map.png', 'guide.html,images/map.png,images/route.png')
        }
        if ($served -cnotin $allowed) {
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
    # long <pre> guide, in light. The dark style changes colours only;
    # html-theme-switch keeps the place across a live switch.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    $ids = Invoke-ShellSeed @('seed-html-position', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    $report.htmlPosition = [ordered]@{
        guideLong = $ids.guideLong; guideChanged = $ids.guideChanged; guidePictures = $ids.guidePictures
        guideFixedHeader = $ids.guideFixedHeader
    }
    $originalTheme = Get-AppThemePreference
    try {
        foreach ($pass in @(@{ name = 'html-position-light'; light = $true })) {
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

function Invoke-HtmlThemePass([string] $mode) {
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $gates = @(
        foreach ($name in @('HtmlDiagnostics', 'HtmlPosition', 'ProgressOverride')) {
            [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\$eventPrefix.$name.$processId")
        })
    try {
        $result = Run-ShellSmoke $mode -ResultName $mode `
            -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        Close-InstalledShell
        return $result
    }
    finally {
        foreach ($gate in $gates) { $gate.Dispose() }
    }
}

function Assert-HtmlThemePass($pass, [string] $diagnostics) {
    # One session, one entry load, the fixture's own files, and no denied
    # request but the browser's own (its favicon, NotInManifest as Other):
    # the style adds no request (TR14.2).
    $files = @(Get-ChildItem -LiteralPath $diagnostics -Filter 'html-session-*.json' -ErrorAction SilentlyContinue)
    if ($files.Count -ne 1) {
        throw "The $($pass.mode) pass wrote $($files.Count) HTML session diagnostics; expected 1."
    }
    $session = Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
    $served = @($session.served) -join ','
    if ($served -cne 'guide.html,images/route.png,style.css') {
        throw "The $($pass.mode) pass served '$served'."
    }
    $pageDenied = @($session.denied | Where-Object { $_.reason -cne 'NotInManifest' -or $_.context -cne 'Other' })
    if ($pageDenied.Count -ne 0) {
        throw "The $($pass.mode) pass denied $(@($session.denied) | ConvertTo-Json -Compress)."
    }
    $a = $session.appearance
    if ([int] $session.entryNavigations -ne 1 -or $a.theme -cne $pass.theme -or
        [int] $a.applications -ne $pass.applications -or [int] $a.failures -ne 0) {
        throw "The $($pass.mode) pass recorded entry navigations $($session.entryNavigations), " +
            "theme $($a.theme), applications $($a.applications), failures $($a.failures)."
    }
    return $session
}

function Run-HtmlThemeScenarios {
    # T09.2: the stored Dark theme and scale at open (Windows light), the
    # stored Light theme keeps the page's colors (Windows dark), and a
    # Windows switch restyles an open guide that follows System.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    $ids = Invoke-ShellSeed @('seed-html-theme', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    $report.htmlTheme = [ordered]@{ guide = $ids.guide }
    $originalTheme = Get-AppThemePreference
    try {
        foreach ($pass in @(
                @{ mode = 'html-theme-open'; stored = 'Dark'; scale = '1.5'; light = $true; theme = 'Dark'; applications = 1 },
                @{ mode = 'html-theme-light'; stored = 'Light'; scale = 'default'; light = $false; theme = 'Light'; applications = 1 },
                @{ mode = 'html-theme-switch'; stored = 'System'; scale = 'default'; light = $true; theme = 'Dark'; applications = 2 })) {
            Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
            Invoke-ShellSeed @('set-html-appearance', $dataRoot, $ids.guide, $pass.stored, $pass.scale) | Out-Null
            Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
            Set-AppThemePreference $pass.light
            try {
                $report.htmlTheme[$pass.mode] = Invoke-HtmlThemePass $pass.mode
            }
            finally {
                Save-HtmlDiagnostics $pass.mode (Get-HtmlCacheRoot)
            }
            $report.htmlTheme["$($pass.mode)-session"] = Assert-HtmlThemePass $pass $diagnostics
        }
    }
    finally {
        Restore-AppThemePreference $originalTheme
        Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-PdfReaderPass([string] $resultName, [string] $mode = 'pdf-reader') {
    Start-InstalledShell
    $diagnosticsGate = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\$eventPrefix.PdfDiagnostics.$($report.launchedProcessId)")
    try {
        $report.pdfReader[$resultName] = Run-ShellSmoke $mode -ResultName $resultName
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
    $counts = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    foreach ($f in @('requests', 'loads', 'staleResults', 'peakCacheBytes', 'maxCacheBytes',
        'cachedPagesAtClose', 'peakTextPages', 'peakTextCharacters', 'disposedCleanly',
        'evictions', 'abandonedExtraction')) {
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
    # Checks disposedCleanly and abandonedExtraction for every PDF the pass
    # opened, and copies each pdf-*.json to the results.
    Assert-PdfClosedCleanly $pass $diagnostics
    return $counts
}

# P7: every PDF a pass opened closed cleanly, with no extraction left behind.
function Assert-PdfClosedCleanly([string] $pass, [string] $diagnostics) {
    $files = @(Get-ChildItem -LiteralPath $diagnostics -Filter 'pdf-*.json' -File -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { throw "The $pass pass wrote no PDF diagnostics." }
    foreach ($file in $files) {
        $counts = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ($counts.disposedCleanly -isnot [bool] -or $counts.disposedCleanly -ne $true) {
            throw "The $pass pass did not close $($file.Name) cleanly."
        }
        if ($counts.abandonedExtraction -isnot [bool] -or $counts.abandonedExtraction -ne $false) {
            throw "The $pass pass abandoned a page-text extraction in $($file.Name)."
        }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $ResultDirectory "$pass.$($file.Name)")
    }
}

# Review Focus 1: the wrong attempt reaches no file the app wrote, in ASCII
# or UTF-16. Bytes are compared through Latin-1, which maps each byte to one
# character.
function Assert-NoPasswordTrace([string] $attempt) {
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $needles = @(
        $latin1.GetString([System.Text.Encoding]::ASCII.GetBytes($attempt)),
        $latin1.GetString([System.Text.Encoding]::Unicode.GetBytes($attempt)))
    $unreadable = @()
    foreach ($root in @($dataRoot, (Get-HtmlCacheRoot))) {
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue)) {
            try { $text = $latin1.GetString([System.IO.File]::ReadAllBytes($file.FullName)) }
            catch {
                # WebView2 can hold a lock file in the cache root; the app
                # data must scan fully.
                if ($file.FullName.StartsWith($dataRoot, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Couldn't scan $($file.Name) in the app data for the password attempt."
                }
                $unreadable += $file.Name
                continue
            }
            foreach ($needle in $needles) {
                if ($text.IndexOf($needle, [StringComparison]::Ordinal) -ge 0) {
                    throw "The wrong password attempt was written to $($file.Name)."
                }
            }
        }
    }
    # The comma keeps an empty list a list.
    return ,$unreadable
}

# P8: deletes only the folder seed-pdf-reader printed, directly under %TEMP%.
function Remove-PdfOriginals([string] $path) {
    $temp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')
    $full = [System.IO.Path]::GetFullPath($path)
    $leaf = Split-Path -Leaf $full
    if ((Split-Path -Parent $full).TrimEnd('\') -ne $temp -or
        $leaf -notmatch '^desktop-guides-pdf-originals-[0-9a-f]{32}$') {
        throw "Refusing to delete '$full': it isn't the PDF seed's originals folder."
    }
    Remove-Item -LiteralPath $full -Recurse -Force
    if (Test-Path -LiteralPath $full) { throw "The PDF originals at '$full' are still there." }
}

function Run-PdfReaderScenarios {
    # TR10.2-TR10.3: tagged text in UI Automation, an image-only page, 200
    # rapid page turns with a bounded cache, typed errors, and TXT still
    # opening (pdf-reader); then zoom, keys, Go to page and locked PDFs, each
    # in its own launch (pdf-zoom, pdf-keys, pdf-jump, pdf-locked), in
    # light. A final pdf-offline launch runs with the originals deleted.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\pdf-long.pdf'))) {
        throw 'pdf-long.pdf is missing; run tools/p0/make_fixtures.py first.'
    }
    $ids = Invoke-ShellSeed @('seed-pdf-reader', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    $report.pdfReader = [ordered]@{
        pdfLong = $ids.pdfLong
        pdfLocked = $ids.pdfLocked
        passFilter = @($PassFilter)
        passesRun = [System.Collections.Generic.List[string]]::new()
    }
    $originalTheme = Get-AppThemePreference
    try {
        foreach ($theme in @(@{ name = 'light'; light = $true })) {
            Set-AppThemePreference $theme.light
            foreach ($mode in $pdfModes) {
                $pass = "$mode-$($theme.name)"
                if (-not (Test-PdfPassSelected $pass)) { continue }
                $report.pdfReader.passesRun.Add($pass)
                Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
                Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
                Invoke-PdfReaderPass $pass $mode
                if ($mode -eq 'pdf-reader') {
                    # Also runs Assert-PdfClosedCleanly for this pass.
                    $report.pdfReader["$pass-diagnostics"] = Assert-PdfDiagnostics $pass $diagnostics $ids.pdfLong
                }
                else {
                    Assert-PdfClosedCleanly $pass $diagnostics
                }
            }
        }
        $report.pdfReader.passwordScanUnreadable = Assert-NoPasswordTrace 'wrong-7Q2x'

        # TR10.2: with the originals gone, a fresh launch reads the managed copies.
        Remove-PdfOriginals $ids.originals
        if (Test-PdfPassSelected 'pdf-offline') {
            $report.pdfReader.passesRun.Add('pdf-offline')
            Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
            Invoke-PdfReaderPass 'pdf-offline' 'pdf-offline'
            Assert-PdfClosedCleanly 'pdf-offline' $diagnostics
        }
    }
    finally {
        Restore-AppThemePreference $originalTheme
        if ($ids.originals -and (Test-Path -LiteralPath $ids.originals)) {
            Remove-PdfOriginals $ids.originals
        }
    }
}

# T18.1: in-guide search. Searching must never save the reading place; the
# smoke checks the save count, and this checks the stored estimate.
function Run-NavigationScenarios {
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    Invoke-ShellSeed @('seed-progress', $dataRoot, $fixtureRoot) | Out-Null
    Invoke-ShellSeed @('seed-find', $dataRoot, $fixtureRoot) | Out-Null
    $estimateOf = {
        param([string] $title)
        @(Invoke-ShellSeed @('describe-progress', $dataRoot) | ConvertFrom-Json |
            Where-Object { $_.title -eq $title })[0].estimate
    }

    $report.findTxt = Invoke-ProgressPass 'find-txt' 0
    if ($null -eq (& $estimateOf 'Numbered Lines Guide')) {
        throw 'After closing the find bar and paging down, no place was stored.'
    }

    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.findBack = Invoke-ProgressPass 'find-back' 0
    if ($null -ne (& $estimateOf 'Numbered Lines Guide')) {
        throw 'Leaving the guide with the find bar open stored a place.'
    }

    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.findHtml = Invoke-ProgressPass 'find-html' 0 -HtmlPosition
    if ($null -eq (& $estimateOf 'Long Web Guide')) {
        throw 'After closing the find bar and scrolling, no HTML place was stored.'
    }

    $report.findHtmlText = Invoke-ProgressPass 'find-html-text' 0
    $report.findHtmlClose = Invoke-ProgressPass 'find-html-close' 0 -HtmlPosition

    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.findPdf = Invoke-ProgressPass 'find-pdf' 0
    if ($null -eq (& $estimateOf 'Long PDF Guide')) {
        throw 'After closing the find bar and turning a page, no PDF place was stored.'
    }

    $report.findPdfScan = Invoke-ProgressPass 'find-pdf-scan' 0
    # find-pdf left the place on page 199.
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.findPdfWait = Invoke-ProgressPass 'find-pdf-wait' 0
    $report.findPdfLines = Invoke-ProgressPass 'find-pdf-lines' 0

    # T18.2: contents for TXT guides.
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.contentsTxt = Invoke-ProgressPass 'contents-txt' 0
    if ($null -eq (& $estimateOf 'Sections Guide')) {
        throw 'Following a contents entry stored no place.'
    }
    $report.contentsNone = Invoke-ProgressPass 'contents-none' 0
    # contents-txt left the place at Walkthrough: Castle.
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.contentsNarrow = Invoke-ProgressPass 'contents-narrow' 0
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.contentsHtml = Invoke-ProgressPass 'contents-html' 0 -HtmlPosition
    if ($null -eq (& $estimateOf 'Contents Web Guide')) {
        throw 'Following an HTML contents entry stored no place.'
    }
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.contentsHtmlHeadings = Invoke-ProgressPass 'contents-html-headings' 0 -HtmlPosition
    # T21.2: a web guide of several pages.
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.htmlPagesLinks = Invoke-ProgressPass 'html-pages-links' 0 -HtmlPosition
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    Remove-Item -Path (Join-Path $diagnostics 'html-session-*.json') -ErrorAction SilentlyContinue
    $report.htmlPagesStyle = Invoke-ProgressPass 'html-pages-style' 0 -HtmlPosition -HtmlDiagnostics
    $served = @(Get-ChildItem -LiteralPath $diagnostics -Filter 'html-session-*.json' |
        ForEach-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).served })
    foreach ($sheet in @('Sample Quest - Guides Example_files/load.php', 'Chapter 1 - Guides Example_files/load.php')) {
        if ($served -notcontains $sheet) { throw "The page's stylesheet $sheet was not served." }
    }
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.htmlPagesRestart = Invoke-ProgressPass 'html-pages-restart' 0 -HtmlPosition
    $estimate = & $estimateOf 'Pages Web Guide'
    if ($null -eq $estimate -or $estimate -lt 0.5 -or $estimate -gt 0.75) {
        throw "A place on page 3 of 4 stored progress $estimate; expected 0.5 to 0.75."
    }
    $report.htmlPagesReopen = Invoke-ProgressPass 'html-pages-reopen' 0 -HtmlPosition
    # T21.2 PR c: contents, search and bookmarks across the pages.
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.htmlPagesContents = Invoke-ProgressPass 'html-pages-contents' 0 -HtmlPosition
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.htmlPagesFind = Invoke-ProgressPass 'html-pages-find' 0 -HtmlPosition
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.htmlPagesFindAgain = Invoke-ProgressPass 'html-pages-find-again' 0 -HtmlPosition
    $report.htmlPagesBookmark = Invoke-ProgressPass 'html-pages-bookmark' 0 -HtmlPosition
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.contentsPdf = Invoke-ProgressPass 'contents-pdf' 0
    if ($null -eq (& $estimateOf 'Outline PDF Guide')) {
        throw 'Following a PDF outline entry stored no place.'
    }
    # T18.3: the pane reopens as it was left, and opens on its own for a
    # guide of several HTML pages until the reader chooses.
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.contentsRememberOpen = Invoke-ProgressPass 'contents-remember-open' 0
    $report.contentsRememberReopen = Invoke-ProgressPass 'contents-remember-reopen' 0 -ContentsPane keep
    $report.contentsRememberClosed = Invoke-ProgressPass 'contents-remember-closed' 0 -ContentsPane keep
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.contentsAuto = Invoke-ProgressPass 'contents-auto' 0 -HtmlPosition -ContentsPane unset
    $report.contentsAutoNarrow = Invoke-ProgressPass 'contents-auto-narrow' 0 -HtmlPosition -ContentsPane unset

    # T19.1: bookmarks.
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    Invoke-ShellSeed @('clear-bookmarks', $dataRoot) | Out-Null
    $report.bookmarkTxt = Invoke-ProgressPass 'bookmark-txt' 0
    if ($null -eq (& $estimateOf 'Sections Guide')) {
        throw 'Following a bookmark stored no place.'
    }
    $report.bookmarkRestart = Invoke-ProgressPass 'bookmark-restart' 0
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.bookmarkHtml = Invoke-ProgressPass 'bookmark-html' 0 -HtmlPosition
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.bookmarkPdf = Invoke-ProgressPass 'bookmark-pdf' 0
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    Invoke-ShellSeed @('seed-bookmarks', $dataRoot) | Out-Null
    $report.bookmarkChanged = Invoke-ProgressPass 'bookmark-changed' 0

    # T19.2: the reflow view.
    Invoke-ShellSeed @('seed-reflow', $dataRoot, $fixtureRoot) | Out-Null
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.reflowSwitch = Invoke-ProgressPass 'reflow-switch' 0
    $report.reflowRestart = Invoke-ProgressPass 'reflow-restart' 0
    $report.reflowNone = Invoke-ProgressPass 'reflow-none' 0
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.reflowFind = Invoke-ProgressPass 'reflow-find' 0
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    Invoke-ShellSeed @('clear-bookmarks', $dataRoot) | Out-Null
    $report.reflowBookmark = Invoke-ProgressPass 'reflow-bookmark' 0
    # T14.5: centered in both views.
    $report.reflowCentered = Invoke-ProgressPass 'reflow-centered' 0
}

# T21.2 PR b: a map zooms, pans and reopens at its view; a strip over 16384
# pixels wide displays; neither counts as reading; removing a map removes
# its cache.
# S22: Find online. Both in-app browsers answer from the fixture manifest
# while the OnlineFixtures gate is open, so nothing reaches a live site.
function Invoke-OnlineMode([string] $mode, [string] $fixtures) {
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $gates = @(
        foreach ($name in @('OnlineFixtures', 'ExternalLaunch', 'CrashDiagnostics')) {
            [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\$eventPrefix.$name.$processId")
        })
    try {
        $report[$mode] = Run-ShellSmoke $mode -ResultName $mode -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        Close-InstalledShell
    }
    finally {
        foreach ($gate in $gates) { $gate.Dispose() }
        $crash = Join-Path (Get-HtmlCacheRoot) "diagnostics\unhandled-$processId.txt"
        if (Test-Path -LiteralPath $crash) {
            Copy-Item -LiteralPath $crash -Destination (Join-Path $ResultDirectory "$mode-unhandled.log") -Force
        }
        $loads = Join-Path (Get-HtmlCacheRoot) "diagnostics\online-loads-$processId.txt"
        if (Test-Path -LiteralPath $loads) {
            Copy-Item -LiteralPath $loads -Destination (Join-Path $ResultDirectory "$mode-loads.log") -Force
        }
    }
}

function Set-OnlineRoute([string] $manifest, [string] $url, [string] $file, [int] $status = 200) {
    $json = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    $route = @($json.routes | Where-Object { $_.url -eq $url })[0]
    if (-not $route) { throw "The online manifest has no route for $url." }
    $route.file = $file
    $route.status = $status
    [System.IO.File]::WriteAllText($manifest, ($json | ConvertTo-Json -Depth 5))
}

# A fresh library between online modes. The fixtures and the cache stay: in
# the portable lane the cache is under the data folder, and a closed app's
# WebView2 processes hold the online profile there for a few seconds. Only
# the unfinished captures in it are cleared.
function Reset-OnlineLibrary([string] $cacheRoot) {
    $cacheName = Split-Path -Leaf $cacheRoot
    Get-ChildItem -LiteralPath $dataRoot -Force |
        Where-Object { $_.Name -ne 'test' -and $_.Name -ne $cacheName } |
        Remove-Item -Recurse -Force
    $captures = Join-Path $cacheRoot 'online-captures'
    $deadline = (Get-Date).AddSeconds(15)
    while ((Test-Path -LiteralPath $captures) -and (Get-Date) -lt $deadline) {
        Remove-Item -LiteralPath $captures -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $captures) { Start-Sleep -Milliseconds 500 }
    }
    if (Test-Path -LiteralPath $captures) { throw "Couldn't clear $captures between online modes." }
}

function Run-OnlineScenarios([string] $fixtureRoot) {
    $fixtures = Join-Path $dataRoot 'test\online'
    Remove-Item -LiteralPath $fixtures -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $fixtures | Out-Null
    Copy-Item -Path (Join-Path $fixtureRoot '*') -Destination $fixtures -Recurse -Force
    $manifest = Join-Path $fixtures 'manifest.json'
    $saga = 'https://gamefaqs.gamespot.com/ps5/100002-example-saga/faqs/78736/'
    Invoke-ShellSeed @('seed-online', $dataRoot) | Out-Null
    $cacheRoot = Get-HtmlCacheRoot

    Invoke-OnlineMode 'online-browse' $fixtures
    Invoke-OnlineMode 'online-save' $fixtures
    $state = Invoke-ShellSeed @('describe-online', $dataRoot, $cacheRoot) | ConvertFrom-Json
    $report.onlineSaved = $state
    $html = @($state.Guides | Where-Object { $_.Format -eq 'Html' })
    $text = @($state.Guides | Where-Object { $_.Format -eq 'Txt' })
    if ($html.Count -ne 1 -or $html[0].Pages -ne 3 -or $html[0].Images -lt 1) {
        throw "Expected one saved HTML guide with 3 pages and its image; got $($state | ConvertTo-Json -Depth 4 -Compress)."
    }
    if ($text.Count -ne 1 -or $text[0].Site -ne 'GameFaqs') { throw 'Expected one saved text FAQ.' }
    # The copy's built-in stylesheet; how it looks is the live check's.
    if ($html[0].StyleSheets -ne 1) { throw "Expected the saved HTML guide's stylesheet; got $($html[0].StyleSheets)." }
    if ($null -ne $html[0].CapturedPages) { throw 'The complete save was recorded as partial.' }

    # Saving again after page 2 changed replaces the guide in place.
    Set-OnlineRoute $manifest ($saga + 'cairn') 'online/html-slug-cairn-v2.html'
    Invoke-OnlineMode 'online-resave' $fixtures
    $after = Invoke-ShellSeed @('describe-online', $dataRoot, $cacheRoot) | ConvertFrom-Json
    $resaved = @($after.Guides | Where-Object { $_.Format -eq 'Html' })
    if ($resaved.Count -ne 1 -or $resaved[0].Id -ne $html[0].Id -or $resaved[0].ContentSha256 -eq $html[0].ContentSha256) {
        throw 'Saving again did not replace the guide in place.'
    }

    # Review Focus 1: a challenge on page 2 pauses there; the smoke restores it and resumes.
    Reset-OnlineLibrary $cacheRoot
    Invoke-ShellSeed @('seed-online', $dataRoot) | Out-Null
    Set-OnlineRoute $manifest ($saga + 'cairn') 'online/challenge.html'
    Invoke-OnlineMode 'online-pause' $fixtures
    # Review Focus 2: leaving mid-save keeps the draft, which the next visit resumes.
    Reset-OnlineLibrary $cacheRoot
    Invoke-ShellSeed @('seed-online', $dataRoot) | Out-Null
    Set-OnlineRoute $manifest ($saga + 'cairn') 'gamefaqs/html-slug-cairn.html'
    Invoke-OnlineMode 'online-resume' $fixtures
    $resumed = Invoke-ShellSeed @('describe-online', $dataRoot, $cacheRoot) | ConvertFrom-Json
    if (@($resumed.Guides).Count -ne 1 -or $resumed.Drafts -ne 0) {
        throw "After resuming, expected one guide and no drafts; got $($resumed | ConvertTo-Json -Depth 4 -Compress)."
    }

    # S22 PR 4: Steam. A refused image saves nothing; then a pasted guide
    # saves without a key; then search, with a key and the game's Steam page.
    $steamImage = 'https://images.steamusercontent.com/ugc/3/FULLB/'
    Set-OnlineRoute $manifest $steamImage 'online/map.png' 429
    Invoke-OnlineMode 'online-steam-limit' $fixtures
    $limited = Invoke-ShellSeed @('describe-online', $dataRoot, $cacheRoot) | ConvertFrom-Json
    if (@($limited.Guides | Where-Object { $_.Site -eq 'Steam' }).Count -ne 0 -or $limited.Drafts -ne 0) {
        throw "A refused Steam image left a guide or a draft: $($limited | ConvertTo-Json -Depth 4 -Compress)."
    }
    Set-OnlineRoute $manifest $steamImage 'online/map.png'
    Invoke-OnlineMode 'online-steam-nokey' $fixtures
    Invoke-OnlineMode 'online-steam-search' $fixtures
    $steam = @((Invoke-ShellSeed @('describe-online', $dataRoot, $cacheRoot) | ConvertFrom-Json).Guides |
        Where-Object { $_.Site -eq 'Steam' })
    # The fixture's third image has no route, so it's the copy's placeholder.
    if ($steam.Count -ne 1 -or $steam[0].ItemId -ne '4242' -or $steam[0].Images -ne 2 -or $steam[0].StyleSheets -ne 1) {
        throw "Expected one saved Steam guide with its two images and stylesheet; got $($steam | ConvertTo-Json -Depth 4 -Compress)."
    }

    # S22 PR 5: VGMaps. Two maps saved, one of them a GIF made PNG; saved again, both are up to date.
    Invoke-OnlineMode 'online-vgmaps' $fixtures
    $maps = @((Invoke-ShellSeed @('describe-online', $dataRoot, $cacheRoot) | ConvertFrom-Json).Guides |
        Where-Object { $_.Site -eq 'VgMaps' })
    if ($maps.Count -ne 2 -or @($maps | Where-Object { $_.Format -ne 'Image' }).Count -ne 0 -or
        (@($maps.ItemId | Sort-Object) -join '|') -ne 'PSX/OnlineTestGame-Cave.gif|PSX/OnlineTestGame-World.png') {
        throw "Expected two saved VGMaps maps; got $($maps | ConvertTo-Json -Depth 4 -Compress)."
    }
}

function Run-MapReaderScenario([string] $fixtureRoot) {
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\maps\big-map.png'))) {
        throw 'big-map.png is missing; run tools/p0/make_fixtures.py first.'
    }
    $report.mapsReaderSeed = Invoke-ShellSeed @('seed-maps-reader', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $cacheRoot = Get-HtmlCacheRoot
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $gates = @(
        foreach ($name in @('ProgressDiagnostics', 'MapDiagnostics')) {
            [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\$eventPrefix.$name.$processId")
        })
    try {
        $report.mapsReader = Run-ShellSmoke 'maps-reader' -ResultName 'maps-reader' `
            -AppDataRoot $dataRoot -AppCacheRoot $cacheRoot
        Close-InstalledShell
    }
    finally {
        foreach ($gate in $gates) { $gate.Dispose() }
    }
    # T21.2 PR c: a glance at a map from the guide, by pointer, then by keyboard.
    foreach ($mode in @('maps-popup', 'maps-keyboard')) {
        Start-InstalledShell
        $processId = $report.launchedProcessId
        $gates = @(
            foreach ($name in @('ProgressDiagnostics', 'MapDiagnostics')) {
                [System.Threading.EventWaitHandle]::new(
                    $false, [System.Threading.EventResetMode]::ManualReset,
                    "Local\$eventPrefix.$name.$processId")
            })
        try {
            $report[$mode] = Run-ShellSmoke $mode -ResultName $mode -AppDataRoot $dataRoot -AppCacheRoot $cacheRoot
            Close-InstalledShell
        }
        finally {
            foreach ($gate in $gates) { $gate.Dispose() }
        }
    }
    $timings = Join-Path $cacheRoot 'diagnostics\map-cache.json'
    if (Test-Path -LiteralPath $timings) {
        $report.mapsReaderBuilds = Get-Content -LiteralPath $timings -Raw | ConvertFrom-Json
    }
    $state = Invoke-ShellSeed @('describe-maps', $dataRoot, $cacheRoot) | ConvertFrom-Json
    $report.mapsReaderState = $state
    $big = $state.Maps.'big map'
    if (-not $big.HasPlace) { throw 'The big map stored no view.' }
    if ($null -ne $big.Zoom) { throw "The big map, left fitted, stored zoom $($big.Zoom)." }
    if ($null -ne $big.Estimate) { throw "The big map stored an estimate, $($big.Estimate)." }
    if (-not $big.CacheComplete) { throw "The big map's cache isn't complete." }
    if ($state.Maps.'castle floor 1'.HasPlace) { throw 'The unopened castle map stored a view.' }
    foreach ($property in $state.Maps.PSObject.Properties) {
        if ($property.Value.LastOpened) { throw "Opening '$($property.Name)' was recorded as reading." }
    }
    if ($null -ne $state.Maps.'strip map') { throw 'The removed strip map is still in the library.' }
    if ($null -ne $state.Maps.'second big map') { throw 'The map removed while building is still in the library.' }
    if ($state.LastActiveTitle -ne 'Walkthrough') { throw "Resume pointed to '$($state.LastActiveTitle)'." }
    if ($state.CacheFolders -ne 1) { throw "The cache held $($state.CacheFolders) maps; expected only the big map's." }
}

function Invoke-ProgressPass([string] $mode, [int] $expectedTopLine, [switch] $Kill, [string] $resultName = $mode,
    [switch] $HtmlPosition, [switch] $HtmlDiagnostics, [ValidateSet('false', 'true', 'unset', 'keep')] [string] $ContentsPane = 'false') {
    # T18.3: the pane's state lasts across launches; each pass starts with it
    # closed, as every launch did before, unless the pass tests that state.
    if ($ContentsPane -ne 'keep') { Invoke-ShellSeed @('set-contents-pane', $dataRoot, $ContentsPane) | Out-Null }
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $names = @('ProgressDiagnostics', 'ProgressOverride')
    if ($HtmlPosition) { $names += 'HtmlPosition' }
    if ($HtmlDiagnostics) { $names += 'HtmlDiagnostics' }
    $gates = @(
        foreach ($name in $names) {
            [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\$eventPrefix.$name.$processId")
        })
    try {
        $result = Run-ShellSmoke $mode -ResultName $resultName -ExpectedTopLine $expectedTopLine `
            -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        if ($Kill) {
            # No flush: only a save the timer already made survives.
            Stop-Process -Id $processId -Force
            Wait-InstalledShellExit $processId
        }
        else {
            Close-InstalledShell
        }
        return $result
    }
    finally {
        foreach ($gate in $gates) { $gate.Dispose() }
    }
}

function Assert-ProgressRows($rows, $stored) {
    # Each row's percentage is the stored estimate's; no estimate, no percentage.
    foreach ($row in $rows) {
        $state = @($stored | Where-Object { $_.title -eq $row.Name })
        if ($state.Count -ne 1) { throw "No stored state for row '$($row.Name)'." }
        if ($null -ne $state[0].completedUtcMs) { throw "Row '$($row.Name)' is completed." }
        if ($row.HelpText -match 'about (\d+) percent') {
            if ($null -eq $state[0].estimate) {
                throw "Row '$($row.Name)' shows $($Matches[1]) percent with no stored estimate."
            }
            $expected = [Math]::Round([double]$state[0].estimate * 100, [MidpointRounding]::AwayFromZero)
            if ([Math]::Abs([int]$Matches[1] - $expected) -gt 1) {
                throw "Row '$($row.Name)' shows $($Matches[1]) percent; the stored estimate is $($state[0].estimate)."
            }
        }
        elseif ($null -ne $state[0].estimate) {
            throw "Row '$($row.Name)' shows no percentage; the stored estimate is $($state[0].estimate)."
        }
    }
}

function Run-ProgressScenarios {
    # TR12.1-TR12.2: the quiet and deadline saves survive a killed process,
    # Back and closing save at once, a burst writes a bounded number of times,
    # and two guides keep their own places across restarts.
    # TR12.3 and TR05.2: rows show the stored estimate and open time, an
    # unopened guide stays Not started, and changed TXT and PDF copies restore
    # approximately.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\pdf-long.pdf'))) {
        throw 'pdf-long.pdf is missing; run tools/p0/make_fixtures.py first.'
    }
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    $ids = Invoke-ShellSeed @('seed-progress', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $dataRoot 'test') -Recurse -Force -ErrorAction SilentlyContinue
    $report.progress = [ordered]@{ ids = $ids }
    try {
        $timer = Invoke-ProgressPass 'progress-timer' 0 -Kill
        $report.progress.timer = $timer
        $restored = Invoke-ProgressPass 'progress-restored' $timer.progressTopLine
        $report.progress.restored = $restored
        $report.progress.twoGuides = Invoke-ProgressPass 'progress-two-guides' $restored.progressTopLine

        $stored = Invoke-ShellSeed @('describe-progress', $dataRoot) | ConvertFrom-Json
        $report.progress.stored = $stored
        $originalTheme = Get-AppThemePreference
        try {
            Set-AppThemePreference $true
            $light = Invoke-ProgressPass 'progress-row' 0 -resultName 'progress-row-light'
            Assert-ProgressRows $light.progressRows $stored
            $report.progress.rows = [ordered]@{ light = $light }
        }
        finally {
            Restore-AppThemePreference $originalTheme
        }

        # The changed copies are managed; the fixtures they came from stay untouched.
        $originals = @('p1\txt-numbered.txt', 'p0\generated\pdf-long.pdf') | ForEach-Object {
            Join-Path $fixtureRoot $_ }
        $before = @($originals | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
        Invoke-ShellSeed @('change-progress-copies', $dataRoot) | Out-Null
        $report.progress.changed = Invoke-ProgressPass 'progress-changed' $restored.progressTopLine
        $after = @($originals | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
        if (($before -join ',') -ne ($after -join ',')) { throw 'A progress fixture original changed.' }
    }
    finally {
        Remove-Item -LiteralPath (Join-Path $dataRoot 'test') -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-StoredCompletion([string] $title) {
    $stored = Invoke-ShellSeed @('describe-progress', $dataRoot) | ConvertFrom-Json
    $state = @($stored | Where-Object { $_.title -eq $title })
    if ($state.Count -ne 1) { throw "No stored state for '$title'." }
    return $state[0]
}

function Assert-NotCompleted([string] $title, [string] $step) {
    $state = Get-StoredCompletion $title
    if ($null -ne $state.completedUtcMs) { throw "$step left '$title' completed." }
    return $state
}

function Assert-Completed([string] $title, [string] $step) {
    $state = Get-StoredCompletion $title
    if ($null -eq $state.completedUtcMs) { throw "$step did not complete '$title'." }
    return $state
}

function Run-CompletionScenarios {
    # TR13.1-TR13.2: completion is an explicit choice on the Game page and in
    # the Reader, it survives a relaunch, the last line or page never sets it,
    # and a failed write is shown and put back.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\pdf-long.pdf'))) {
        throw 'pdf-long.pdf is missing; run tools/p0/make_fixtures.py first.'
    }
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    $ids = Invoke-ShellSeed @('seed-progress', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    $report.completion = [ordered]@{ ids = $ids }
    $lockDir = Join-Path $env:TEMP "dg-completion-lock-$PID"
    try {
        # The UIA gate runs first: names, Segmented items and their selected state.
        $report.completion.segmented = Invoke-ProgressPass 'completion-segmented' 0

        $report.completion.lastPage = Invoke-ProgressPass 'completion-last-page' 0
        foreach ($title in 'Numbered Lines Guide', 'Long PDF Guide') {
            $state = Assert-NotCompleted $title 'Reading to the end'
            if ($null -eq $state.estimate -or [double]$state.estimate -lt 0.9) {
                throw "'$title' estimate after the end is $($state.estimate)."
            }
        }

        $originalTheme = Get-AppThemePreference
        try {
            Set-AppThemePreference $true
            $report.completion.gameLight = Invoke-ProgressPass 'completion-game' 0 -resultName 'completion-game-light'
            $report.completion.readerLight = Invoke-ProgressPass 'completion-reader' 0 -resultName 'completion-reader-light'
            # Dark keeps only the Reader pass: the one that starts from Complete.
            Set-AppThemePreference $false
            $report.completion.readerDark = Invoke-ProgressPass 'completion-reader' 0 -resultName 'completion-reader-dark'
        }
        finally {
            Restore-AppThemePreference $originalTheme
        }
        [void](Assert-NotCompleted 'Numbered Lines Guide' 'completion-game')
        $webBefore = Assert-Completed 'Long Web Guide' 'completion-reader'

        $report.completion.restart = Invoke-ProgressPass 'completion-restart' 0
        $report.completion.restartAfter = Invoke-ProgressPass 'completion-restart-after' 0
        $webAfter = Assert-NotCompleted 'Long Web Guide' 'completion-restart'
        if ([Math]::Abs([double]$webAfter.estimate - [double]$webBefore.estimate) -gt 0.01) {
            throw "Long Web Guide estimate moved from $($webBefore.estimate) to $($webAfter.estimate)."
        }

        # A held write lock makes the app's write time out after 30 s.
        New-Item -ItemType Directory -Force $lockDir | Out-Null
        $ready = Join-Path $lockDir 'ready'
        $release = Join-Path $lockDir 'release'
        Start-InstalledShell
        $report.completion.errorPrepare = Run-ShellSmoke 'completion-error-prepare' `
            -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        $lock = Start-ShellDatabaseLock 'hold-write-lock' $ready $release -HoldSeconds 120
        try {
            $report.completion.error = Run-ShellSmoke 'completion-error' `
                -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        }
        finally {
            Release-ShellDatabaseLock $lock $release
        }
        [void](Assert-NotCompleted 'Numbered Lines Guide' 'completion-error')
        $report.completion.errorRetry = Run-ShellSmoke 'completion-error-retry' `
            -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        Close-InstalledShell
        [void](Assert-Completed 'Numbered Lines Guide' 'completion-error-retry')
    }
    finally {
        Remove-Item -LiteralPath $lockDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-StoredTheme([string] $expected, [string] $step) {
    $stored = (Invoke-ShellSeed @('describe-theme', $dataRoot)).Trim()
    if ($stored -ne $expected) { throw "After $step the stored theme is '$stored', not '$expected'." }
    return $stored
}

function Run-ThemeScenarios {
    # TR14.2: the App theme choice applies at once, survives a relaunch,
    # makes no remote connection, and puts back a failed save. The UIA
    # gate runs first. High contrast is deferred to T16.2.
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    [void](Invoke-ShellSeed @('seed-design', $dataRoot))
    $report.theme = [ordered]@{ highContrast = 'deferred-to-T16.2' }
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.theme.selector = Run-ShellSmoke 'theme-selector'
        Close-InstalledShell
        [void](Assert-StoredTheme 'System' 'the gate')

        # Windows light: System to Dark (one drop-down choice, one save).
        Start-InstalledShell
        $report.theme.lightToDark = Run-ShellSmoke 'theme-change' -ResultName 'theme-change-light' `
            -ExpectedTheme System -ExpectedThemeStatus 'System (Light)' -SwitchToTheme Dark
        Close-InstalledShell
        [void](Assert-StoredTheme 'Dark' 'choosing Dark')

        # Windows dark: Dark survives the relaunch; choose Light.
        Set-AppThemePreference $false
        Start-InstalledShell
        $report.theme.darkToLight = Run-ShellSmoke 'theme-change' -ResultName 'theme-change-dark' `
            -ExpectedTheme Dark -ExpectedThemeStatus 'Dark' -SwitchToTheme Light
        Close-InstalledShell
        [void](Assert-StoredTheme 'Light' 'choosing Light')

        # Light survives; back to System, which follows Windows dark.
        Start-InstalledShell
        $report.theme.lightToSystem = Run-ShellSmoke 'theme-change' -ResultName 'theme-change-system' `
            -ExpectedTheme Light -ExpectedThemeStatus 'Light' -SwitchToTheme System `
            -WindowsTheme Dark
        Close-InstalledShell
        [void](Assert-StoredTheme 'System' 'choosing System')

        # System follows Windows again after a relaunch.
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.theme.restored = Run-ShellSmoke 'theme-restored' `
            -ExpectedTheme System -ExpectedThemeStatus 'System (Light)'

        # A held write lock makes the app's save time out after 30 s.
        $ready = Join-Path $ResultDirectory "theme-lock-ready-$runId"
        $release = Join-Path $ResultDirectory "theme-lock-release-$runId"
        $lock = Start-ShellDatabaseLock 'hold-write-lock' $ready $release -HoldSeconds 120
        try {
            $report.theme.error = Run-ShellSmoke 'theme-error' `
                -ExpectedTheme System -ExpectedThemeStatus 'System (Light)' -SwitchToTheme Light
        }
        finally {
            Release-ShellDatabaseLock $lock $release
        }
        [void](Assert-StoredTheme 'System' 'the failed save')
        $report.theme.errorRetry = Run-ShellSmoke 'theme-error-retry' `
            -ExpectedTheme System -SwitchToTheme Light
        Close-InstalledShell
        [void](Assert-StoredTheme 'Light' 'the retry')
    }
    finally {
        Restore-AppThemePreference $originalTheme
        $report.theme.restoredAppTheme = Get-AppThemePreference
    }
}

function Assert-StoredTextScales($ids, $expected, [string] $step) {
    $keys = @($expected.Keys)
    $json = Invoke-ShellSeed (@('describe-text-scales', $dataRoot) + @($keys | ForEach-Object { $ids.$_ }))
    $stored = $json | ConvertFrom-Json
    $result = [ordered]@{}
    foreach ($key in $keys) {
        $actual = $stored.($ids.$key)
        $want = $expected[$key]
        $ok = if ($null -eq $want) { $null -eq $actual }
            else { $null -ne $actual -and [math]::Abs([double] $actual - $want) -lt 0.0001 }
        if (-not $ok) { throw "After $step the stored text scale of $key is '$actual', not '$want'." }
        $result[$key] = $actual
    }
    return $result
}

function Invoke-TextSizeLaunch([scriptblock] $passes) {
    # One launch with the TXT and HTML diagnostics gates open.
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $gates = @(
        foreach ($name in @('TextDiagnostics', 'HtmlDiagnostics')) {
            [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\$eventPrefix.$name.$processId")
        })
    try {
        & $passes
        Close-InstalledShell
    }
    finally {
        foreach ($gate in $gates) { $gate.Dispose() }
    }
}

function Run-TextSizeScenarios {
    # TR14.1: fixed text size steps for TXT and HTML, saved per guide,
    # restored after a relaunch, fixed-width TXT columns, PDF untouched,
    # and a failed save put back.
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    $cacheRoot = Get-HtmlCacheRoot
    Remove-Item -LiteralPath (Join-Path $cacheRoot 'diagnostics') -Recurse -Force -ErrorAction SilentlyContinue
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\pdf-long.pdf'))) {
        throw 'pdf-long.pdf is missing; run tools/p0/make_fixtures.py first.'
    }
    $ids = Invoke-ShellSeed @('seed-text-size', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $report.textSize = [ordered]@{}
    function Run-TextSizePass([string] $mode) {
        $report.textSize[$mode] = Run-ShellSmoke $mode -ResultName $mode `
            -AppDataRoot $dataRoot -AppCacheRoot $cacheRoot
    }

    Invoke-TextSizeLaunch {
        Run-TextSizePass 'text-size-steps'
        Run-TextSizePass 'text-size-whitespace'
        Run-TextSizePass 'text-size-restart'
    }
    $report.textSize.storedBeforeRelaunch = Assert-StoredTextScales $ids `
        ([ordered]@{ ascii = 1.25; web = 0.9; utf8 = $null }) 'the first launch'

    Invoke-TextSizeLaunch {
        Run-TextSizePass 'text-size-restart-after'
        Run-TextSizePass 'text-size-pdf'
    }
    $report.textSize.storedAfterPdf = Assert-StoredTextScales $ids `
        ([ordered]@{ ascii = 1.25; web = 0.9; utf8 = $null; pdf = $null }) 'the PDF zoom'

    Invoke-TextSizeLaunch {
        Run-TextSizePass 'text-size-error-prepare'
        # A held write lock makes the app's save time out after 30 s.
        $ready = Join-Path $ResultDirectory "text-size-lock-ready-$runId"
        $release = Join-Path $ResultDirectory "text-size-lock-release-$runId"
        $lock = Start-ShellDatabaseLock 'hold-write-lock' $ready $release -HoldSeconds 120
        try {
            Run-TextSizePass 'text-size-error'
        }
        finally {
            Release-ShellDatabaseLock $lock $release
        }
        [void](Assert-StoredTextScales $ids ([ordered]@{ ascii = 1.25 }) 'the failed save')
        Run-TextSizePass 'text-size-error-retry'
    }
    $report.textSize.storedAfterRetry = Assert-StoredTextScales $ids `
        ([ordered]@{ ascii = 1.5; web = 0.9 }) 'the retry'
}

function Run-ImportScenarios {
    Invoke-ShellSeed @('seed-import', $dataRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.importLight = Run-ShellSmoke 'import-preview' -ResultName 'import-light'
        Close-InstalledShell

        $state = Invoke-ShellSeed @('describe-import', $dataRoot) | ConvertFrom-Json
        $report.importPreviewState = $state
        foreach ($name in @('Guides', 'FileOperations', 'StagingEntries', 'ContentEntries')) {
            if ($state.$name -ne 0) {
                throw "The import preview left $($state.$name) $name; expected none."
            }
        }

        # Review Focus 1: the wrong import attempt reached no file either.
        $report.importPasswordScanUnreadable = Assert-NoPasswordTrace 'wrong-7Q2x'

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

# T21.2: after the removal runs, which count Import Test Game's guides.
function Run-MultipageImportScenario {
    # T21.2: a multi-page guide imports with its four pages.
    Start-InstalledShell
    $report.importHtmlPages = Run-ShellSmoke 'import-html-pages' -ResultName 'import-html-pages'
    Close-InstalledShell
    $pagesState = Invoke-ShellSeed @('describe-import', $dataRoot) | ConvertFrom-Json
    $report.importHtmlPagesState = $pagesState
    if ($pagesState.MostGuidePages -ne 4) {
        throw "The multi-page guide stored $($pagesState.MostGuidePages) pages; expected 4."
    }
}

# T21.2: maps, several at once, then one by Ctrl+O; none counts as reading.
function Run-MapImportScenario {
    Start-InstalledShell
    $report.importMaps = Run-ShellSmoke 'import-maps' -ResultName 'import-maps'
    Close-InstalledShell
    $mapsState = Invoke-ShellSeed @('describe-import', $dataRoot) | ConvertFrom-Json
    $report.importMapsState = $mapsState
    if ($mapsState.MapGuides -ne 3) { throw "The library stored $($mapsState.MapGuides) maps; expected 3." }
    if ($mapsState.MapReadings -ne 0) { throw "Opening a map recorded reading on $($mapsState.MapReadings) maps." }
    if ($mapsState.MapSizes -ne '640x480,64x48,20000x400') { throw "The maps' sizes were '$($mapsState.MapSizes)'." }
    if ($mapsState.LastActiveIsMap) { throw 'Opening a map set Resume to it.' }
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
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}

function Set-StoredMaterial([string] $material) {
    dotnet $seedDll `
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
            foreach ($material in @('Solid', 'Acrylic')) {
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
            # Mica gets no launch here: with transparency off, Windows draws it
            # in the same color as our Solid fill. material-switch starts
            # from Mica.
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

function Set-StoredColorfulIcons([bool] $on) {
    $value = if ($on) { 'true' } else { 'false' }
    dotnet $seedDll set-colorful-icons $dataRoot $value
    if ($LASTEXITCODE -ne 0) { throw "Could not store colorful icons $value." }
}

# T11.5: the toggle, the applied icons and the stored choice are app code.
# How the SVGs render is reviewed on the screenshots.
function Run-IconScenarios {
    $originalTheme = Get-AppThemePreference
    $report.icons = [ordered]@{}
    try {
        Set-AppThemePreference $true
        Set-StoredColorfulIcons $false
        Start-InstalledShell
        $report.icons['light-switch-on'] = Run-ShellSmoke 'icons' `
            -ResultName 'icons-light-switch-on' -ExpectedIcons 'Off' -SwitchIcons 'On'
        Close-InstalledShell
        Start-InstalledShell
        $report.icons['light-persisted'] = Run-ShellSmoke 'icons' `
            -ResultName 'icons-light-persisted' -ExpectedIcons 'On' -SwitchIcons 'Off'
        Close-InstalledShell

        Set-AppThemePreference $false
        Set-StoredColorfulIcons $true
        Start-InstalledShell
        $report.icons['dark-on'] = Run-ShellSmoke 'icons' `
            -ResultName 'icons-dark-on' -ExpectedIcons 'On'
        Close-InstalledShell

        # Last in the design group: the empty library needs an empty data root.
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Set-AppThemePreference $true
        Set-StoredColorfulIcons $true
        Start-InstalledShell
        $report.icons['light-empty-on'] = Run-ShellSmoke 'icons' `
            -ResultName 'icons-light-empty' -ExpectedIcons 'On' -EmptyLibrary
        Close-InstalledShell
    }
    finally {
        Set-StoredColorfulIcons $false
        Restore-AppThemePreference $originalTheme
        $report.restoredAppThemeAfterIcons = Get-AppThemePreference
    }
}

function Invoke-ShellSeed([string[]] $SeedArguments) {
    $output = @(dotnet $seedDll @SeedArguments)
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
    try {
        foreach ($fixture in $fixtures) {
            $source = Join-Path $fixtureRoot ($fixture -replace '/', '\')
            if (-not (Test-Path -LiteralPath $source)) {
                throw "The workflow fixture $fixture is missing; run tools/p0/make_fixtures.py first."
            }
            Copy-Item -LiteralPath $source -Destination $originals -Recurse
        }
    }
    catch {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
        throw
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

# T17.3: replaces the previous release with the candidate in place. The
# version must rise, the family stay, and the library read the same; the
# install location carries the version, so the launch task is rebuilt.
function Invoke-RcUpgrade {
    $before = Get-AppxPackage -Name $lanePackage
    $libraryBefore = Invoke-ShellSeed @('describe-library', $dataRoot)
    Add-AppxPackage -Path $PackagePath
    $after = Get-AppxPackage -Name $lanePackage
    if (-not $after) { throw 'The candidate did not install over the previous release.' }
    # Recorded before any check, so cleanup owns (and removes) the candidate
    # even when a check below fails.
    $script:installed = $after
    $report.packageFullName = $after.PackageFullName
    $report.installLocation = $after.InstallLocation
    $script:expectedExecutablePath = Join-Path $after.InstallLocation 'DesktopGuides.Production.exe'
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

function Run-BackupScenarios {
    # T20.2 PR a: the first-import reminder, export, cancel and a refused folder.
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Invoke-ShellSeed @('seed-import', $dataRoot) | Out-Null
    $backupRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) (
        'desktop-guides-backup-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backupRoot | Out-Null
    $protectedRoot = $null
    try {
        Start-InstalledShell
        $report.importReminder = Run-ShellSmoke 'import-reminder'
        Close-InstalledShell

        $backup = Join-Path $backupRoot 'DesktopGuides-backup.zip'
        Start-InstalledShell
        $report.exportBackup = Run-ShellSmoke 'export-backup' -BackupPath $backup
        Close-InstalledShell
        $described = Invoke-ShellSeed @('describe-backup', $backup) | ConvertFrom-Json
        $report.exportBackupDescribed = $described
        if (-not $described.verified -or $described.guides -lt 2 -or $described.credentials) {
            throw "The exported backup isn't complete: $($described | ConvertTo-Json -Compress)."
        }

        $canceled = Join-Path $backupRoot 'canceled.zip'
        Start-InstalledShell
        $prefix = "Local\$eventPrefix.Backup.$($report.launchedProcessId)"
        $reached = [System.Threading.EventWaitHandle]::new(
            $false, [System.Threading.EventResetMode]::AutoReset, "$prefix.Reached")
        $resume = [System.Threading.EventWaitHandle]::new(
            $false, [System.Threading.EventResetMode]::AutoReset, "$prefix.Continue")
        try {
            $report.exportCancel = Run-ShellSmoke 'export-cancel' -BackupPath $canceled
        }
        finally {
            $resume.Set() | Out-Null
            $resume.Dispose()
            $reached.Dispose()
        }
        Close-InstalledShell
        if ((Test-Path -LiteralPath $canceled) -or @(Get-ChildItem -LiteralPath $backupRoot -Filter '*.tmp').Count) {
            throw 'A canceled export left a file behind.'
        }

        # Packaged: any folder under %LOCALAPPDATA% outside the package is
        # refused. Portable protects only its own DesktopGuides folder.
        $refused = if ($portable) {
            Join-Path $dataRoot 'inside-app-data.zip'
        }
        else {
            $protectedRoot = Join-Path $env:LOCALAPPDATA (
                'desktop-guides-smoke-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $protectedRoot | Out-Null
            Join-Path $protectedRoot 'outside-package.zip'
        }
        $report.exportProtectedPath = $refused
        Start-InstalledShell
        $report.exportProtected = Run-ShellSmoke 'export-protected' -BackupPath $refused
        Close-InstalledShell
        if (Test-Path -LiteralPath $refused) { throw 'A refused export left a file in app data.' }
        if ($protectedRoot -and @(Get-ChildItem -LiteralPath $protectedRoot -Force).Count) {
            throw 'A refused export left a file in the %LOCALAPPDATA% folder.'
        }

        # T20.2 PR c: restore. The source backup holds the reminder's guides
        # plus a linked game with artwork.
        Invoke-ShellSeed @('seed-linked-game', $dataRoot) | Out-Null
        $source = Join-Path $backupRoot 'restore-source.zip'
        Invoke-ShellSeed @('export-backup', $dataRoot, $source) | Out-Null
        $sourceState = Invoke-ShellSeed @('describe-library', $dataRoot) | ConvertFrom-Json
        $truncated = Join-Path $backupRoot 'truncated.zip'
        $missingArtwork = Join-Path $backupRoot 'missing-artwork.zip'
        Invoke-ShellSeed @('damage-backup', $source, $truncated, 'truncate') | Out-Null
        Invoke-ShellSeed @('damage-backup', $source, $missingArtwork, 'missing-artwork') | Out-Null

        function Assert-Restored([string] $what) {
            $state = Invoke-ShellSeed @('describe-library', $dataRoot) | ConvertFrom-Json
            if ($state.games -ne $sourceState.games -or $state.guides -ne $sourceState.guides -or
                $state.marker -or $state.stages -ne 0 -or $state.parked -ne 0) {
                throw "$what left '$($state | ConvertTo-Json -Compress)'; expected the source library and no restore folders."
            }
        }

        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Start-InstalledShell
        $report.restoreClean = Run-ShellSmoke 'restore-clean' -BackupPath $source
        Close-InstalledShell
        Assert-Restored 'A clean restore'

        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Invoke-ShellSeed @('seed', $dataRoot) | Out-Null
        Start-InstalledShell
        $report.restoreReplace = Run-ShellSmoke 'restore-replace' -BackupPath $source
        Close-InstalledShell
        Assert-Restored 'Replacing a populated library'

        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Invoke-ShellSeed @('seed', $dataRoot) | Out-Null
        foreach ($case in @(
                @{ Mode = 'restore-corrupt'; Path = $truncated },
                @{ Mode = 'restore-missing-artwork'; Path = $missingArtwork },
                @{ Mode = 'restore-cancel'; Path = $source })) {
            $before = Invoke-ShellSeed @('describe-library', $dataRoot)
            Start-InstalledShell
            $gate = $null
            if ($case.Mode -eq 'restore-cancel') {
                $prefix = "Local\$eventPrefix.Backup.$($report.launchedProcessId)"
                $gate = @(
                    [System.Threading.EventWaitHandle]::new(
                        $false, [System.Threading.EventResetMode]::AutoReset, "$prefix.Reached"),
                    [System.Threading.EventWaitHandle]::new(
                        $false, [System.Threading.EventResetMode]::AutoReset, "$prefix.Continue"))
            }
            try {
                $report[$case.Mode] = Run-ShellSmoke $case.Mode -BackupPath $case.Path
            }
            finally {
                if ($gate) {
                    $gate[1].Set() | Out-Null
                    $gate | ForEach-Object { $_.Dispose() }
                }
            }
            Close-InstalledShell
            $after = Invoke-ShellSeed @('describe-library', $dataRoot)
            if ($after -ne $before) {
                throw "$($case.Mode) changed the library: '$before' became '$after'."
            }
        }

        $prior = Invoke-ShellSeed @('describe-library', $dataRoot)
        Invoke-ShellSeed @('interrupt-restore', $dataRoot, $source) | Out-Null
        Start-InstalledShell
        $report.restoreInterrupted = Run-ShellSmoke 'restore-interrupted'
        Close-InstalledShell
        $after = Invoke-ShellSeed @('describe-library', $dataRoot)
        if ($after -ne $prior) {
            throw "An interrupted restore didn't put the previous library back: '$prior' became '$after'."
        }
    }
    finally {
        Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
        if ($protectedRoot) {
            Remove-Item -LiteralPath $protectedRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Run-ProviderScenarios {
    $report.providers = [ordered]@{ source = 'live' }
    $providers = $report.providers
    $fixtureDirectory = Join-Path $PSScriptRoot `
        '..\..\tests\DesktopGuides.Infrastructure.Tests\Providers\Fixtures'
    # T17.2 D: real keys win. Without them, the fixtures stand in for the
    # providers, answered inside a Preview build through the gate below.
    $fixtureSource = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures\p1\providers')).Path
    $fixtureGate = $null
    if ($FakeProviders -and -not (Test-Path -LiteralPath $IgdbCredentialFile -PathType Leaf)) {
        $IgdbCredentialFile = Join-Path $fixtureSource 'credentials\igdb-credentials.txt'
        $SteamGridDbCredentialFile = Join-Path $fixtureSource 'credentials\steamgriddb-credentials.txt'
        $providers.source = 'fixtures'
    }
    $secrets = @()
    $igdbReady = $false
    $steamGridDbFile = ''
    try {
        $secrets += @((Read-LabelledValues $IgdbCredentialFile `
            @('igdb client id', 'igdb client secret')).Values)
        $igdbReady = $true
    }
    catch {
        # The fixtures supply their own key files, so a failure here is a
        # broken fixture, not a missing user key: a skip would pass silently.
        if ($providers.source -eq 'fixtures') { throw }
        $providers.live = 'skipped'
        $providers.liveSkipReason = $_.Exception.Message
    }
    try {
        $secrets += @((Read-LabelledValues $SteamGridDbCredentialFile `
            @('steamgriddb api key')).Values)
        $steamGridDbFile = $SteamGridDbCredentialFile
    }
    catch {
        if ($providers.source -eq 'fixtures') { throw }
        $providers.steamGridDb = 'skipped'
        $providers.steamGridDbSkipReason = $_.Exception.Message
    }

    [void](Invoke-ShellSeed @('seed-linked-game', $dataRoot))
    if ($providers.source -eq 'fixtures') {
        # The credential files stay behind: the leak scans search this folder.
        $fixtureFolder = Join-Path $dataRoot 'test\providers'
        New-Item -ItemType Directory -Force -Path $fixtureFolder | Out-Null
        Get-ChildItem -LiteralPath $fixtureSource -File | Copy-Item -Destination $fixtureFolder
        $fixtureGate = [System.Threading.EventWaitHandle]::new(
            $false, [System.Threading.EventResetMode]::ManualReset,
            "Local\$eventPrefix.ProviderFixtures")
    }
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

    $providers.fixtureFields = if ($providers.source -eq 'fixtures') {
        'skipped: the fixtures would be compared with themselves'
    } else {
        Invoke-ShellSeed @('check-igdb-fields', $IgdbCredentialFile, $fixtureDirectory)
    }
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
    if ($fixtureGate) {
        $fixtureGate.Dispose()
        $unmatchedPath = Join-Path $dataRoot 'test\providers\unmatched.json'
        $providers.unmatched = @(if (Test-Path -LiteralPath $unmatchedPath) {
            Get-Content -LiteralPath $unmatchedPath -Raw | ConvertFrom-Json
        })
        if ($providers.unmatched.Count -gt 0) {
            Copy-Item -LiteralPath $unmatchedPath -Destination (Join-Path $ResultDirectory 'provider-unmatched.json')
            throw "The fixtures had no route for $($providers.unmatched.Count) provider request(s): " +
                (($providers.unmatched | ForEach-Object { "$($_.method) $($_.host)$($_.path)" }) -join '; ')
        }
    }
    Assert-NoCredentialLeak $secrets 'AfterRemove'
}

# #102: one gate for the whole run, so every launch records its guide opens,
# including a guide reopened at startup.
$openTimelineGate = [System.Threading.EventWaitHandle]::new(
    $false, [System.Threading.EventResetMode]::ManualReset, "Local\$eventPrefix.HtmlOpenTimeline")
try {
    $report.windowsAppRuntime = @(
        Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2*' |
            Where-Object { $_.Architecture -eq 'X64' } |
            Select-Object -ExpandProperty Version |
            ForEach-Object { $_.ToString() }
    )
    $report.dotnetSdk = (dotnet --version)
    $report.webView2Version = Get-WebView2RuntimeVersion
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
        }

        if (Get-AppxPackage -Name $lanePackage) {
            throw "A $lanePackage package appeared before test install."
        }
        Assert-FreshPackageProfile $env:LOCALAPPDATA $lanePackage
        # The rc group installs the previous release first and upgrades later.
        Add-AppxPackage -Path $(if ($RcOnly) { $PreviousPackage } else { $signed })
        $installed = Get-AppxPackage -Name $lanePackage
        if (-not $installed) { throw 'Production package did not install.' }
        $report.packageFullName = $installed.PackageFullName
        if ($RcOnly) {
            $report.rc = [ordered]@{
                previousVersion = $installed.Version.ToString(); candidateVersion = $null
                familyUnchanged = $false; libraryIntact = $false; offline = $null
            }
        }
        $report.packageFamilyName = $installed.PackageFamilyName
        $report.installLocation = $installed.InstallLocation
        # T17.2: the runtime the package resolved. A fresh runner registers
        # the framework for this user only with the install, so the query
        # before it can come back empty.
        $report.windowsAppRuntime = @(
            $installed.Dependencies |
                Where-Object { $_.Name -like 'Microsoft.WindowsAppRuntime.2*' } |
                ForEach-Object { "$($_.Name) $($_.Version)" })
        $expectedExecutablePath = Join-Path $installed.InstallLocation `
            'DesktopGuides.Production.exe'

        $dataRoot = Join-Path $env:LOCALAPPDATA `
            "Packages\$($installed.PackageFamilyName)\LocalState"
    }
    New-Item -ItemType Directory -Force $dataRoot | Out-Null
    # Build the seed tool once; each call then starts the DLL directly.
    dotnet build $seedProject -c Release --no-restore | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Could not build DesktopGuides.ShellSeed.' }

    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME `
        -LogonType Interactive -RunLevel Limited
    $launchAction = New-ShellLaunchAction $launchResultPath
    $secondLaunchAction = New-ShellLaunchAction $secondLaunchResultPath
    Register-ScheduledTask -TaskName $launchTask -Action $launchAction `
        -Principal $principal -Force | Out-Null

    # Each group after the first starts from an empty data root, so a single
    # group run against a fresh install sees what the full run gives it.
    function Enter-ScenarioGroup([string] $name) {
        if ($name -eq 'rc' -and -not $RcOnly) { return $false }
        if ($scenarioScope -ne 'all' -and $selectedGroups -notcontains $name) { return $false }
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
        dotnet $seedDll seed $dataRoot
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
        dotnet $seedDll stale $dataRoot
        if ($LASTEXITCODE -ne 0) { throw 'Could not set stale last-guide ID.' }
        Start-InstalledShell
        $report.stale = Run-ShellSmoke 'stale'
        Close-InstalledShell

        dotnet $seedDll seed-long $dataRoot
        if ($LASTEXITCODE -ne 0) { throw 'Could not seed the long guide list.' }
        Start-InstalledShell
        $report.longList = Run-ShellSmoke 'long-list'
        Close-InstalledShell

        dotnet $seedDll seed-second $dataRoot
        if ($LASTEXITCODE -ne 0) { throw 'Could not seed the second game.' }
        Start-InstalledShell
        Assert-GameSwitchClearsWhileLoading
        Close-InstalledShell
    }

    if (Enter-ScenarioGroup 'design') {
        dotnet $seedDll `
            seed-design $dataRoot
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not seed the design-language metadata.'
        }
        Run-DesignLanguageScenarios
        Run-MaterialScenarios
        Run-ReaderDesignScenarios
        Start-InstalledShell
        $report.keyboard = Run-ShellSmoke 'keyboard'
        Close-InstalledShell
        Run-IconScenarios
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
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Run-HtmlThemeScenarios
    }

    if (Enter-ScenarioGroup 'pdf') { Run-PdfReaderScenarios }

    if (Enter-ScenarioGroup 'progress') { Run-ProgressScenarios }
    if (Enter-ScenarioGroup 'completion') { Run-CompletionScenarios }
    if (Enter-ScenarioGroup 'theme') { Run-ThemeScenarios }
    if (Enter-ScenarioGroup 'text-size') { Run-TextSizeScenarios }

    if (Enter-ScenarioGroup 'import') {
        Run-ImportScenarios
        Run-RemovalScenarios
        Run-MultipageImportScenario
        Run-MapImportScenario
    }

    if (Enter-ScenarioGroup 'game-actions') { Run-GameActionsScenarios }

    if (Enter-ScenarioGroup 'provider') { Run-ProviderScenarios }
    if (Enter-ScenarioGroup 'backup') { Run-BackupScenarios }
    if (Enter-ScenarioGroup 'workflow') { Run-WorkflowScenarios }
    if (Enter-ScenarioGroup 'navigation') { Run-NavigationScenarios }
    # T21.2: the map reader and the Maps pane, in their own group so they can
    # be run alone; the navigation shard runs both groups.
    if (Enter-ScenarioGroup 'maps') {
        Run-MapReaderScenario (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    }
    if (Enter-ScenarioGroup 'online') {
        Run-OnlineScenarios (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures\p3')).Path
    }
    if (Enter-ScenarioGroup 'rc') { Run-RcScenarios }

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
}
finally {
    $openTimelineGate.Dispose()
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
            # S22: WebView2 child processes can outlive a killed app for a
            # few seconds and hold the online profile under the cache.
            $removeDeadline = (Get-Date).AddSeconds(15)
            do {
                Remove-Item -LiteralPath $portableDataRoot -Recurse -Force `
                    -ErrorAction SilentlyContinue
                if (-not (Test-Path -LiteralPath $portableDataRoot)) { break }
                Start-Sleep -Milliseconds 500
            } while ((Get-Date) -lt $removeDeadline)
        }
        $report.portableDataStillPresent = $portableDataOwned -and
            (Test-Path -LiteralPath $portableDataRoot)
        if ($report.portableDataStillPresent) {
            $report.success = $false
            $report.cleanupError = 'The portable data folder this run created remains.'
        }
    }
    else {
        $installedNow = @(Get-AppxPackage -Name $lanePackage)
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
        if ($releaseTrustAdded) {
            Remove-Item "Cert:\LocalMachine\TrustedPeople\$releaseThumbprint" -ErrorAction SilentlyContinue
        }
        Remove-Item $public -ErrorAction SilentlyContinue
        $remainingPackages = @(Get-AppxPackage -Name $lanePackage)
        $report.packageStillInstalled = $remainingPackages.Count -gt 0
        $report.certificateStillTrusted = if ($certificate) {
            Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
        } elseif ($releaseTrustAdded) {
            Test-Path "Cert:\LocalMachine\TrustedPeople\$releaseThumbprint"
        } else { $false }
        if ($report.packageStillInstalled -or $report.certificateStillTrusted) {
            $report.success = $false
            $report.cleanupError = if ($remainingPackages.Count -gt 0 -and
                ($ownedPackage.Count -eq 0 -or
                 $remainingPackages[0].PackageFullName -ne $installed.PackageFullName)) {
                "A $lanePackage package not confirmed as test-owned remains untouched."
            } else {
                'Temporary package or certificate remains.'
            }
        }
    }
    # Crash events are kept on passing runs too: an app that crashes as it
    # closes still passes its checks, and only the event log shows it.
    try {
        $events = @(Get-AppCrashEvents ([datetime]$report.observedAt))
        $report.applicationEventCount = $events.Count
        $report.applicationEvents = @(
            $events |
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
    if ($cleanupErrors.Count -gt 0) {
        $report.success = $false
        $report.processCleanupErrors = @($cleanupErrors)
    }
    $report.finishedAt = (Get-Date).ToUniversalTime().ToString('o')
    $report | ConvertTo-Json -Depth 8 |
        Set-Content (Join-Path $ResultDirectory $(
            if ($portable) { 'portable-run.json' } else { 'signed-install.json' })) `
            -Encoding UTF8
}
if (-not $report.success) { exit 1 }
