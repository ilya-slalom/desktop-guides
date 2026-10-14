# T17.2 E: runs windows_shell_install.ps1 in an elevated, interactive
# scheduled task for the logged-in user, so CI and SSH host runs start the
# same way. Only paths and switches cross into the task.
param(
    # The installer's parameters, mirrored; test_start_installed_task.ps1
    # fails if the two lists drift apart.
    [string] $PackagePath,
    [string] $PortableExecutable,
    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory,
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
    [switch] $RcOnly,
    [switch] $NavigationOnly,
    [string[]] $Groups,
    [string[]] $PassFilter,
    [string] $IgdbCredentialFile,
    [string] $SteamGridDbCredentialFile,
    [switch] $AllowOfflineFirewallRule,

    # T17.2 D: with no real IGDB credential file, run the provider group
    # against the recorded fixtures in tests/fixtures/p1/providers.
    [switch] $FakeProviders,

    # T17.3: the lane, release-signed packages and the previous release.
    [ValidateSet('Preview', 'Public')]
    [string] $Lane,
    [switch] $ReleaseSigned,
    [string] $PreviousPackage,

    # The entry point's own.
    [double] $TimeoutMinutes = 45,
    [string] $Installer
)

$ErrorActionPreference = 'Stop'
# Resolved here: Windows PowerShell leaves $PSScriptRoot empty in parameter
# defaults when the script runs with -File.
if (-not $Installer) { $Installer = Join-Path $PSScriptRoot 'windows_shell_install.ps1' }
. (Join-Path $PSScriptRoot 'windows_shell_lane.ps1')
$taskPrefix = 'DesktopGuides-P1-Installed-'
$pathParameters = @('PackagePath', 'PortableExecutable', 'ResultDirectory',
    'IgdbCredentialFile', 'SteamGridDbCredentialFile', 'PreviousPackage')

# The task starts in tools\p1, so relative paths are made absolute here.
function Get-FullPath([string] $path) {
    return [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine((Get-Location).ProviderPath, $path))
}

function Write-Note([string] $text) { Write-Output "start_installed_task: $text" }

# Refusals: nothing is registered or written.
$desktops = @(Get-CimInstance Win32_Process -Filter "Name = 'explorer.exe'" | Where-Object {
    $_.SessionId -ne 0 -and
        (Invoke-CimMethod -InputObject $_ -MethodName GetOwner).User -eq $env:USERNAME
})
if ($desktops.Count -eq 0) {
    Write-Note "$env:USERNAME has no logged-in desktop; nothing started."
    exit 2
}
$stale = @(Get-ScheduledTask -TaskName "$taskPrefix*" -ErrorAction SilentlyContinue)
if ($stale.Count -gt 0) {
    Write-Note "$($stale[0].TaskName) is still registered; an earlier run may still be going. Nothing started."
    exit 2
}

# The hand-over: bound installer parameters only, so its defaults apply.
$ResultDirectory = Get-FullPath $ResultDirectory
New-Item -ItemType Directory -Force -Path $ResultDirectory | Out-Null
$forward = [ordered]@{}
foreach ($name in @($PSBoundParameters.Keys)) {
    if ($name -in @('TimeoutMinutes', 'Installer')) { continue }
    $value = $PSBoundParameters[$name]
    if ($value -is [System.Management.Automation.SwitchParameter]) { $value = $value.IsPresent }
    elseif ($name -in $pathParameters) { $value = Get-FullPath $value }
    elseif ($value -is [array]) { $value = @($value) }
    $forward[$name] = $value
}
$inputPath = Join-Path $ResultDirectory 'task-input.json'
$logPath = Join-Path $ResultDirectory 'installer.log'
$exitPath = Join-Path $ResultDirectory 'task-exit.json'
$startedPath = Join-Path $ResultDirectory 'task-started.json'
Remove-Item -LiteralPath $logPath, $exitPath, $startedPath -Force -ErrorAction SilentlyContinue
[ordered]@{
    installer = Get-FullPath $Installer
    parameters = $forward
    path = $env:PATH
    dotnetRoot = $env:DOTNET_ROOT
    logPath = $logPath
    exitPath = $exitPath
    startedPath = $startedPath
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $inputPath -Encoding UTF8

# The task runs this same PowerShell, so CI keeps pwsh and the host 5.1.
# A visible console on the test desktop takes focus from the app under test.
# -WindowStyle Hidden hides it only after it appears, and a window created a
# moment late stayed visible, so the console host runs headless: no window.
$taskName = $taskPrefix + [Guid]::NewGuid().ToString('N')
$shell = (Get-Process -Id $PID).Path
$inner = Join-Path $PSScriptRoot 'run_installed_task_inner.ps1'
$action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\conhost.exe') `
    -WorkingDirectory $PSScriptRoot -Argument (
    '--headless "' + $shell + '" -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' +
    $inner + '" -InputPath "' + $inputPath + '"')
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes ([Math]::Ceiling($TimeoutMinutes) + 5))

# Prints the log's complete new lines; -Final also prints a last partial line.
$script:logOffset = 0L
$script:pending = ''
$script:decoder = [System.Text.UTF8Encoding]::new($false).GetDecoder()
function Write-NewLogLines([switch] $Final) {
    if (Test-Path -LiteralPath $logPath) {
        $stream = [System.IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite, Delete')
        try {
            $count = [int] ($stream.Length - $script:logOffset)
            if ($count -gt 0) {
                [void] $stream.Seek($script:logOffset, 'Begin')
                $bytes = New-Object byte[] $count
                $read = $stream.Read($bytes, 0, $count)
                $script:logOffset += $read
                $chars = New-Object char[] ($script:decoder.GetCharCount($bytes, 0, $read))
                [void] $script:decoder.GetChars($bytes, 0, $read, $chars, 0)
                $script:pending += [string]::new($chars)
            }
        }
        finally { $stream.Dispose() }
    }
    $lines = $script:pending -split "`r?`n"
    for ($i = 0; $i -lt $lines.Count - 1; $i++) { Write-Output $lines[$i] }
    $script:pending = $lines[$lines.Count - 1]
    if ($Final -and $script:pending) {
        Write-Output $script:pending
        $script:pending = ''
    }
}

# Ends the inner script's whole tree first: stopping the task first ends the
# inner script alone and orphans the installer it started.
function Stop-InnerRun {
    if (Test-Path -LiteralPath $startedPath) {
        $innerId = [int] (Get-Content -LiteralPath $startedPath -Raw | ConvertFrom-Json).processId
        if (Get-Process -Id $innerId -ErrorAction SilentlyContinue) {
            & cmd.exe /c "taskkill /PID $innerId /T /F >nul 2>&1"
        }
    }
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
}

# Lists what the installer's own cleanup would have removed. Never removes it.
function Write-Leftovers {
    $left = @()
    foreach ($name in Get-ShellLanePackageNames) {
        if (Get-AppxPackage -Name $name -ErrorAction SilentlyContinue) {
            $left += "the $name package is still installed"
        }
    }
    $tasks = @(Get-ScheduledTask -TaskName 'DesktopGuides-P1-*' -ErrorAction SilentlyContinue |
        Where-Object { $_.TaskName -ne $taskName })
    if ($tasks.Count -gt 0) { $left += "scheduled tasks remain: $(@($tasks.TaskName) -join ', ')" }
    if (Get-Process -Name 'DesktopGuides.Production' -ErrorAction SilentlyContinue) {
        $left += 'a DesktopGuides.Production process is running'
    }
    $found = if ($left.Count -gt 0) { $left -join '; ' } else { 'nothing found' }
    Write-Note "the installer's cleanup did not run. May be left: $found. Remove them by hand; see docs/p1/e2e-testing.md."
}

$code = 97
$started = Get-Date
$deadline = $started.AddMinutes($TimeoutMinutes)
$seenRunning = $false
$endedAt = $null
try {
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal `
        -Settings $settings | Out-Null
    Start-ScheduledTask -TaskName $taskName
    while ($true) {
        Start-Sleep -Milliseconds 500
        Write-NewLogLines
        # The inner script renames its exit file into place, so it is whole.
        if (Test-Path -LiteralPath $exitPath) {
            Start-Sleep -Milliseconds 500
            Write-NewLogLines -Final
            $result = Get-Content -LiteralPath $exitPath -Raw | ConvertFrom-Json
            if ($result.error) { Write-Note "the installer threw: $($result.error)" }
            $code = [int] $result.exitCode
            break
        }
        if ((Get-Date) -gt $deadline) {
            Write-NewLogLines -Final
            Write-Note "no result after $TimeoutMinutes minutes; stopping the run."
            Stop-InnerRun
            Write-Leftovers
            $code = 124
            break
        }
        # Silence in the log means nothing; only the task's state does.
        $state = "$((Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue).State)"
        if ($state -eq 'Running') {
            $seenRunning = $true
            $endedAt = $null
        }
        elseif ($seenRunning -or (Test-Path -LiteralPath $startedPath) -or
            ((Get-Date) - $started).TotalSeconds -gt 60) {
            # Ended, or never ran: give the exit file 10 s to land.
            if (-not $endedAt) { $endedAt = Get-Date }
            elseif (((Get-Date) - $endedAt).TotalSeconds -gt 10) {
                Write-NewLogLines -Final
                $last = (Get-ScheduledTaskInfo -TaskName $taskName -ErrorAction SilentlyContinue).LastTaskResult
                Write-Note "the task ended without writing $exitPath (task state '$state', last result $last)."
                $code = 97
                break
            }
        }
    }
}
finally {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
}
exit $code
