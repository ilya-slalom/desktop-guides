$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_process.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-True $rejected $Message
}

function Wait-File([string] $Path, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while (-not (Test-Path -LiteralPath $Path) -and
        (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $Path)) {
        $details = if (Test-Path -LiteralPath "$marker.stderr") {
            Get-Content -LiteralPath "$marker.stderr" -Raw
        } else { 'No helper stderr.' }
        throw "Missing test file $Path. $details"
    }
}

function Start-Sleeper([int] $Seconds = 120) {
    $marker = Join-Path $scratch ([Guid]::NewGuid().ToString('N'))
    $arguments = '-NoProfile -NonInteractive -WindowStyle Hidden ' +
        '-ExecutionPolicy Bypass -File "' + $sleeperScript + '"' +
        ' -Marker "' + $marker + '" -SleepSeconds ' + $Seconds
    $child = Start-Process -FilePath $windowsPowerShell `
        -ArgumentList $arguments -PassThru -WindowStyle Hidden
    Wait-File $marker
    return $child
}

function Open-Sleeper([System.Diagnostics.Process] $Child) {
    return [DesktopGuidesOwnedProcess]::OpenVerified(
        $Child.Id, $Child.StartTime.ToUniversalTime(),
        $Child.SessionId, $windowsPowerShell)
}

function Start-LaunchHelper(
    [string] $Marker,
    [string] $ResultPath,
    [int] $HandoffTimeoutSeconds) {
    $childArguments = '-NoProfile -NonInteractive -WindowStyle Hidden ' +
        '-ExecutionPolicy Bypass -File "' + $sleeperScript + '"' +
        ' -Marker "' + $Marker + '" -SleepSeconds 120'
    $helperScript = Join-Path $PSScriptRoot 'windows_shell_launch.ps1'
    $command = '& "' + $helperScript + '"' +
        ' -ExecutablePath "' + $windowsPowerShell + '"' +
        " -ExecutableArguments '$childArguments'" +
        ' -ResultPath "' + $ResultPath + '"' +
        ' -HandoffTimeoutSeconds ' + $HandoffTimeoutSeconds
    $encoded = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($command))
    $arguments = '-NoProfile -NonInteractive -WindowStyle Hidden ' +
        '-ExecutionPolicy Bypass -EncodedCommand ' + $encoded
    return Start-Process -FilePath $windowsPowerShell `
        -ArgumentList $arguments -PassThru -WindowStyle Hidden `
        -RedirectStandardError "$Marker.stderr" `
        -RedirectStandardOutput "$Marker.stdout"
}

function Assert-NoSleeper([string] $Marker) {
    $matches = @(Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" |
        Where-Object { $_.CommandLine -like "*$Marker*" })
    Assert-True ($matches.Count -eq 0) "Launched child $Marker was orphaned."
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) `
    "desktop-guides-owned-process-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
$windowsPowerShell = Join-Path $env:WINDIR `
    'System32\WindowsPowerShell\v1.0\powershell.exe'
$sleeperScript = Join-Path $scratch 'sleeper.ps1'
@'
param([string] $Marker, [int] $SleepSeconds)
Set-Content -LiteralPath $Marker -Value $PID
Start-Sleep -Seconds $SleepSeconds
'@ | Set-Content -LiteralPath $sleeperScript -Encoding UTF8
$children = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$launchHelpers = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

try {
    $script:ownedProcesses =
        [System.Collections.Generic.Dictionary[int,DesktopGuidesOwnedProcess]]::new()
    $script:cleanupErrors = [System.Collections.Generic.List[string]]::new()

    $child = Start-Sleeper
    $children.Add($child)
    $owned = Open-Sleeper $child
    Assert-True ($null -ne $owned) 'Could not open the test child.'
    Assert-Rejected {
        [DesktopGuidesOwnedProcess]::OpenVerified(
            $child.Id, $child.StartTime.ToUniversalTime().AddMilliseconds(1),
            $child.SessionId, $windowsPowerShell) | Out-Null
    } 'A different start time was accepted.'
    Assert-Rejected {
        [DesktopGuidesOwnedProcess]::OpenVerified(
            $child.Id, $child.StartTime.ToUniversalTime(),
            $child.SessionId + 1, $windowsPowerShell) | Out-Null
    } 'A different session was accepted.'
    Assert-Rejected {
        [DesktopGuidesOwnedProcess]::OpenVerified(
            $child.Id, $child.StartTime.ToUniversalTime(),
            $child.SessionId, (Join-Path $scratch 'wrong.exe')) | Out-Null
    } 'A different image path was accepted.'

    $decoy = Start-Sleeper
    $children.Add($decoy)
    $ownedProcesses[$decoy.Id] = $owned
    Stop-InstalledShell
    Assert-True $child.HasExited 'Retained handle did not stop its child.'
    Assert-True (-not $decoy.HasExited) 'Cleanup stopped the decoy PID.'
    Assert-True ($ownedProcesses.Count -eq 0) 'Exited handle stayed owned.'
    Assert-True ($cleanupErrors.Count -eq 0) 'Handle cleanup reported an error.'
    $decoyOwned = Open-Sleeper $decoy
    Assert-True ($decoyOwned.TerminateAndWait(10000)) `
        'Could not stop the surviving decoy.'
    $decoyOwned.Dispose()

    $shortLived = Start-Sleeper 2
    $children.Add($shortLived)
    $shortOwned = Open-Sleeper $shortLived
    $shortLived.WaitForExit()
    Assert-True ($null -eq (Open-Sleeper $shortLived)) `
        'An exited launch child was treated as live ownership.'
    $ownedProcesses[$shortLived.Id] = $shortOwned
    Stop-InstalledShell
    Assert-True ($ownedProcesses.Count -eq 0) `
        'Already-exited process retained ownership.'

    $timed = Start-Sleeper
    $children.Add($timed)
    $ownedProcesses[$timed.Id] = Open-Sleeper $timed
    $originalStop = (Get-Command Stop-OwnedShellHandle).ScriptBlock
    try {
        function Stop-OwnedShellHandle { return $false }
        Stop-InstalledShell -BestEffort -ExitTimeoutSeconds 0
    }
    finally {
        Set-Item Function:Stop-OwnedShellHandle $originalStop
    }
    Assert-True ($ownedProcesses.ContainsKey($timed.Id)) `
        'Timed-out process lost ownership.'
    Assert-True ($cleanupErrors.Count -eq 1) `
        'Timeout did not record one cleanup error.'
    Stop-InstalledShell
    Assert-True $timed.HasExited 'Timed-out process was not stopped on retry.'

    $resultPath = Join-Path $scratch 'success.json'
    $marker = Join-Path $scratch 'handoff-marker'
    $helper = Start-LaunchHelper $marker $resultPath 10
    $children.Add($helper)
    $launchHelpers.Add($helper)
    Wait-File $resultPath
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    Assert-True $result.success 'Launch helper did not report success.'
    $handoffChild = [DesktopGuidesOwnedProcess]::OpenVerified(
        [int]$result.processId, [datetime]$result.startedAt,
        [int]$result.sessionId, $windowsPowerShell)
    Assert-True ($null -ne $handoffChild) 'Launch child exited before handoff.'
    $ownedProcesses[[int]$result.processId] = $handoffChild
    @{ handoffToken = $result.handoffToken } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath "$resultPath.ack" -Encoding UTF8
    Assert-True ($helper.WaitForExit(10000)) 'Launch helper ignored acknowledgment.'
    Assert-True (-not (Get-Content -LiteralPath "$marker.stderr" -Raw)) `
        'Launch helper reported an error after handoff.'
    Assert-True (-not $handoffChild.HasExited) `
        'Launch helper stopped an acknowledged child.'
    Stop-InstalledShell
    Assert-True ($ownedProcesses.Count -eq 0) `
        'Acknowledged child retained ownership after cleanup.'
    Assert-NoSleeper $marker

    $missingResultPath = Join-Path $scratch 'missing\failed.json'
    $failedMarker = Join-Path $scratch 'failed-marker'
    $helper = Start-LaunchHelper $failedMarker $missingResultPath 5
    $children.Add($helper)
    $launchHelpers.Add($helper)
    Assert-True ($helper.WaitForExit(15000)) `
        'Launch helper hung after result write failure.'
    Assert-True ([bool](Get-Content -LiteralPath "$failedMarker.stderr" -Raw)) `
        'Launch helper accepted a failed result write.'
    Assert-NoSleeper $failedMarker

    $stalePath = Join-Path $scratch 'stale.json'
    $staleMarker = Join-Path $scratch 'stale-marker'
    @{ handoffToken = [Guid]::NewGuid().ToString('N') } |
        ConvertTo-Json -Compress |
        Set-Content -LiteralPath "$stalePath.ack" -Encoding UTF8
    $helper = Start-LaunchHelper $staleMarker $stalePath 1
    $children.Add($helper)
    $launchHelpers.Add($helper)
    Wait-File $stalePath
    Assert-True ($helper.WaitForExit(10000)) `
        'Launch helper hung after unacknowledged child.'
    Assert-True ([bool](Get-Content -LiteralPath "$staleMarker.stderr" -Raw)) `
        'Launch helper accepted a stale acknowledgment.'
    Assert-NoSleeper $staleMarker

    Write-Output 'Installed-shell handle and launch-handoff checks passed.'
}
finally {
    foreach ($launchHelper in $launchHelpers) {
        if (-not $launchHelper.HasExited) {
            [void]$launchHelper.WaitForExit(35000)
        }
    }
    foreach ($ownedId in @($ownedProcesses.Keys)) {
        $ownedProcesses[$ownedId].TerminateAndWait(10000) | Out-Null
        $ownedProcesses[$ownedId].Dispose()
    }
    foreach ($child in $children) {
        if (-not $child.HasExited) {
            [DesktopGuidesOwnedProcess]::TerminateAndWait(
                $child.Handle, 10000) | Out-Null
        }
        $child.Dispose()
    }
    Remove-Item -LiteralPath $scratch -Recurse -Force `
        -ErrorAction SilentlyContinue
}
