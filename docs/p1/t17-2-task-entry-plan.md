# T17.2 Piece E (Scheduled-Task Entry Point) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Installed shell runs start through one checked-in entry point, an
elevated interactive scheduled task, on both CI and the Windows host.

**Architecture:**
- `tools/p1/start_installed_task.ps1` takes the installer's parameters and
  writes them, with the caller's `PATH` and `DOTNET_ROOT`, to
  `task-input.json`. It registers and starts the task, prints
  `installer.log` as it grows, and exits with the code from
  `task-exit.json`.
- `tools/p1/run_installed_task_inner.ps1` runs inside the task, calls the
  installer with those parameters, and writes the log and the exit file.
- The installer is unchanged.

**Tech Stack:** PowerShell 5.1 and 7, Task Scheduler cmdlets, GitHub
Actions.

**Spec:** `docs/p1/t17-2-task-entry-design.md`

**Target:** T17.2, piece E, in one PR on `feat/p1-t17-2-task-entry` (the
spec commit `3879d30` is its first commit).
**Prerequisites:** T17.2 pieces A and B, merged in #90 and #91.

## Global Constraints

- PowerShell scripts stay ASCII-only and run under Windows PowerShell 5.1
  and PowerShell 7.
- `task-input.json` holds paths and switches only, never credential values.
- The task is `DesktopGuides-P1-Installed-<id>`, for the current user,
  `-LogonType Interactive`, `-RunLevel Highest`.
- Exit codes: the installer's own; 98 (installer threw); 97 (task never
  ran, or ended without `task-exit.json`); 124 (timeout); 2 (refused).
- On timeout the entry point reports leftovers and never removes the
  package.
- `-TimeoutMinutes` defaults to 45. `-Installer` is the only test hook.
- No app code changes.

## Rulings against the spec

Task 3 records these in the design.

1. **The task runs the caller's PowerShell.** CI runs the installer under
   `pwsh` today, and the host under `powershell.exe` 5.1. The task action
   uses the entry point's own executable (`(Get-Process -Id $PID).Path`),
   so each keeps the shell it is tested with. Running every CI shard under
   5.1 would change what CI tests.
2. **The entry point mirrors the installer's parameters explicitly** and
   forwards only the ones the caller bound, so the installer's own
   defaults, such as the credential-file paths, still apply. A test
   compares the two parameter lists, so a new installer switch can't be
   forgotten here.
3. **Relative paths are resolved before the hand-over.** The task starts
   in `tools\p1`, not in the caller's folder, and CI passes
   `artifacts/production-shell/installed`. The entry point turns
   `-ResultDirectory`, `-PackagePath`, `-PortableExecutable` and the
   credential-file paths into absolute paths.
4. **`-TimeoutMinutes` is a `[double]`**, so the timeout test can use 0.25
   minutes instead of a full minute.
5. **On timeout the entry point also ends the inner process tree.** The
   inner script records its process ID in `task-started.json`. After
   `Stop-ScheduledTask`, the entry point runs `taskkill /PID <id> /T /F`, so
   no test-owned `powershell` or `dotnet` child keeps running. The app
   itself is started through the installer's own launch task, not as a
   child; it is reported as a leftover, not killed.

## Review Focus

1. **A run whose installer prints nothing for minutes**, for example a long
   `dotnet build`. The wait must not mistake silence for a dead task. It
   decides from the task's state and the exit file, never from the log.
   Pinned by the slow-start case in Task 1.
2. **The last lines of a failing run.** They must reach the CI log even
   when they arrive just before `task-exit.json`. Pinned by the
   final-drain check in Task 1's output case.
3. **A path with spaces or a quote-like character** reaching the
   installer. The hand-over through JSON must keep it whole. Pinned by
   Task 1's argument case.
4. **A caller that passes `-Groups` and an `*Only` switch together.** The
   installer refuses that combination, so the entry point must pass both
   through and return the installer's failure, not validate it itself.
   Pinned by the argument case forwarding both to the fake.
5. **A timeout on the host while a package is installed.** The entry point
   must list the package and stop there. Pinned by the timeout case
   checking that the "may be left" report is printed.

## Host commands

The Mac has no PowerShell, so tests run on `pcsx2-win`:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t17-2e) { Remove-Item -Recurse -Force E:\work\desktop-guides\t17-2e }; New-Item -ItemType Directory E:\work\desktop-guides\t17-2e | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t17-2e'
}
```

- **Entry-point tests:** `stage && s 'cd /d E:\work\desktop-guides\t17-2e && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_start_installed_task.ps1'`
- **Parse check:** `tools\p1\test_windows_scripts_parse.ps1`, run the same way.
- **ASCII check:** `grep -nP '[^\x00-\x7F]' <file>` on the Mac, with no output.

---

### Task 1: The entry point and the inner script

**Files:**
- Create: `tools/p1/start_installed_task.ps1`
- Create: `tools/p1/run_installed_task_inner.ps1`
- Create: `tools/p1/test_start_installed_task.ps1`

**Interfaces:**
- Consumes: the parameter block of `tools/p1/windows_shell_install.ps1`
  (lines 1–44), unchanged.
- Produces:
  - `start_installed_task.ps1` with every installer parameter, plus
    `[double] -TimeoutMinutes = 45` and `[string] -Installer`. It exits
    with the codes in Global Constraints.
  - In `-ResultDirectory`:
    - `task-input.json`: `installer`, `parameters`, `path`, `dotnetRoot`,
      `logPath`, `exitPath` and `startedPath`;
    - `task-started.json`: `{ processId }`;
    - `installer.log`;
    - `task-exit.json`: `{ exitCode, error }`.
  - The output lines `start_installed_task: ...` for refusals, a missing
    result, a timeout and leftovers.

- [ ] **Step 1: Write the failing test**

```powershell
$ErrorActionPreference = 'Stop'
$entry = Join-Path $PSScriptRoot 'start_installed_task.ps1'
$prefix = 'DesktopGuides-P1-Installed-'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-NoEntryTask([string] $step) {
    $left = @(Get-ScheduledTask -TaskName "$prefix*" -ErrorAction SilentlyContinue)
    Assert-True ($left.Count -eq 0) "$step left $($left.Count) task(s): $($left.TaskName -join ', ')."
}

# Runs the entry point in this process and returns its exit code and output.
function Invoke-Entry([string] $fake, [hashtable] $arguments) {
    $global:LASTEXITCODE = 0
    $output = @(& $entry -Installer $fake @arguments 2>&1 | ForEach-Object { "$_" })
    return [pscustomobject]@{ code = $LASTEXITCODE; output = $output }
}

function New-Fake([string] $name, [string] $body) {
    $path = Join-Path $root $name
    Set-Content -LiteralPath $path -Encoding ASCII -Value (
        "param([string] `$PackagePath, [string] `$ResultDirectory, [string[]] `$Groups, [switch] `$DesignOnly)`r`n" + $body)
    return $path
}

Assert-NoEntryTask 'An earlier run'
$root = Join-Path ([IO.Path]::GetTempPath()) "dg-entry-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $root | Out-Null
$savedPath = $env:PATH
$savedRoot = $env:DOTNET_ROOT
try {
    # 1, 4 and 5: arguments, output, environment and the exit code.
    $record = New-Fake 'fake-record.ps1' @'
[ordered]@{
    packagePath = $PackagePath; resultDirectory = $ResultDirectory
    groups = @($Groups); designOnly = [bool] $DesignOnly
    path = $env:PATH; dotnetRoot = $env:DOTNET_ROOT
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ResultDirectory 'fake-received.json') -Encoding UTF8
Write-Output 'fake line one'
Write-Output 'fake line last'
exit 3
'@
    $marker = Join-Path $root 'marker path'
    $env:PATH = "$marker;$savedPath"
    $env:DOTNET_ROOT = 'C:\dg-fake-dotnet-root'
    $out = Join-Path $root 'out one'
    $package = Join-Path $root "pkg dir\it's [x].msix"
    $run = Invoke-Entry $record @{ PackagePath = $package; ResultDirectory = $out; Groups = @('core', 'txt'); DesignOnly = $true }
    $env:PATH = $savedPath
    $env:DOTNET_ROOT = $savedRoot
    Assert-True ($run.code -eq 3) "The installer's exit code 3 came back as $($run.code)."
    $received = Get-Content -LiteralPath (Join-Path $out 'fake-received.json') -Raw | ConvertFrom-Json
    Assert-True ($received.packagePath -eq $package) "The package path arrived as '$($received.packagePath)'."
    Assert-True ($received.resultDirectory -eq $out) "The result folder arrived as '$($received.resultDirectory)'."
    Assert-True ((@($received.groups) -join ',') -eq 'core,txt') "Groups arrived as '$(@($received.groups) -join ',')'."
    Assert-True ($received.designOnly) 'The switch did not arrive.'
    Assert-True ($received.path.StartsWith("$marker;")) "PATH arrived as '$($received.path.Substring(0, 80))...'."
    Assert-True ($received.dotnetRoot -eq 'C:\dg-fake-dotnet-root') "DOTNET_ROOT arrived as '$($received.dotnetRoot)'."
    Assert-True ($run.output -contains 'fake line one') 'The first line did not reach the output.'
    Assert-True ($run.output -contains 'fake line last') 'The last line did not reach the output.'
    $log = Get-Content -LiteralPath (Join-Path $out 'installer.log') -Raw
    Assert-True ($log.Contains('fake line one') -and $log.Contains('fake line last')) 'installer.log is incomplete.'
    $handOver = Get-Content -LiteralPath (Join-Path $out 'task-input.json') -Raw
    Assert-True (-not $handOver.Contains('IgdbCredentialFile')) 'An unbound installer parameter was forwarded.'
    Assert-NoEntryTask 'The recorded run'

    # 3: a throw is reported as 98 with its message.
    $throw = New-Fake 'fake-throw.ps1' "throw 'fake installer exploded'"
    $out = Join-Path $root 'out throw'
    $run = Invoke-Entry $throw @{ PackagePath = $package; ResultDirectory = $out }
    Assert-True ($run.code -eq 98) "A throwing installer exited $($run.code), not 98."
    $exit = Get-Content -LiteralPath (Join-Path $out 'task-exit.json') -Raw | ConvertFrom-Json
    Assert-True ("$($exit.error)".Contains('fake installer exploded')) 'task-exit.json lost the error.'
    Assert-True ((@($run.output) -join "`n").Contains('fake installer exploded')) 'The error did not reach the output.'
    Assert-NoEntryTask 'The throwing run'

    # Review Focus 1: a silent installer is not a dead task.
    $slow = New-Fake 'fake-slow.ps1' "Start-Sleep -Seconds 15`r`nexit 0"
    $run = Invoke-Entry $slow @{ PackagePath = $package; ResultDirectory = (Join-Path $root 'out slow') }
    Assert-True ($run.code -eq 0) "A silent installer exited $($run.code), not 0."
    Assert-NoEntryTask 'The silent run'

    # 6 and Review Focus 5: a timeout stops the run and lists leftovers.
    $sleep = New-Fake 'fake-sleep.ps1' "Write-Output 'sleeping'`r`nStart-Sleep -Seconds 300`r`nexit 0"
    $run = Invoke-Entry $sleep @{ PackagePath = $package; ResultDirectory = (Join-Path $root 'out sleep'); TimeoutMinutes = 0.25 }
    Assert-True ($run.code -eq 124) "A timed-out run exited $($run.code), not 124."
    Assert-True ((@($run.output) -join "`n").Contains("the installer's cleanup did not run")) 'The leftover report is missing.'
    Assert-NoEntryTask 'The timed-out run'
    $innerPid = (Get-Content -LiteralPath (Join-Path $root 'out sleep\task-started.json') -Raw | ConvertFrom-Json).processId
    Assert-True (-not (Get-Process -Id $innerPid -ErrorAction SilentlyContinue)) "The inner process $innerPid kept running."

    # 8: a stale task refuses the run and is left alone.
    $stale = "${prefix}stale-$([Guid]::NewGuid().ToString('N'))"
    Register-ScheduledTask -TaskName $stale -Action (New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c exit 0') | Out-Null
    try {
        $run = Invoke-Entry $record @{ PackagePath = $package; ResultDirectory = (Join-Path $root 'out stale') }
        Assert-True ($run.code -eq 2) "A stale task gave $($run.code), not 2."
        Assert-True ([bool] (Get-ScheduledTask -TaskName $stale -ErrorAction SilentlyContinue)) 'The stale task was removed.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'out stale\task-input.json'))) 'A refused run wrote a hand-over.'
    }
    finally {
        Unregister-ScheduledTask -TaskName $stale -Confirm:$false -ErrorAction SilentlyContinue
    }

    # Ruling 2: the entry point mirrors every installer parameter.
    function Get-ParameterNames([string] $file) {
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($file, [ref] $tokens, [ref] $errors)
        return @($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath } | Sort-Object)
    }
    $mine = @(Get-ParameterNames $entry | Where-Object { $_ -notin @('TimeoutMinutes', 'Installer') })
    $theirs = Get-ParameterNames (Join-Path $PSScriptRoot 'windows_shell_install.ps1')
    Assert-True (($mine -join ',') -eq ($theirs -join ',')) "Parameters differ: entry [$($mine -join ',')] installer [$($theirs -join ',')]."
}
finally {
    $env:PATH = $savedPath
    $env:DOTNET_ROOT = $savedRoot
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
$global:LASTEXITCODE = 0
Write-Output 'start_installed_task: all cases passed.'
```

- [ ] **Step 2: Run it to verify it fails**

Run: **Entry-point tests**.
Expected: FAIL, because `start_installed_task.ps1` doesn't exist.

- [ ] **Step 3: Write the entry point**

```powershell
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
    [string[]] $Groups,
    [string[]] $PassFilter,
    [string] $IgdbCredentialFile,
    [string] $SteamGridDbCredentialFile,
    [switch] $AllowOfflineFirewallRule,

    # The entry point's own.
    [double] $TimeoutMinutes = 45,
    [string] $Installer = (Join-Path $PSScriptRoot 'windows_shell_install.ps1')
)

$ErrorActionPreference = 'Stop'
$taskPrefix = 'DesktopGuides-P1-Installed-'
$pathParameters = @('PackagePath', 'PortableExecutable', 'ResultDirectory',
    'IgdbCredentialFile', 'SteamGridDbCredentialFile')

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
$taskName = $taskPrefix + [Guid]::NewGuid().ToString('N')
$shell = (Get-Process -Id $PID).Path
$inner = Join-Path $PSScriptRoot 'run_installed_task_inner.ps1'
$action = New-ScheduledTaskAction -Execute $shell -WorkingDirectory $PSScriptRoot -Argument (
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $inner +
    '" -InputPath "' + $inputPath + '"')
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

function Stop-InnerRun {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $startedPath) {
        $innerId = [int] (Get-Content -LiteralPath $startedPath -Raw | ConvertFrom-Json).processId
        if (Get-Process -Id $innerId -ErrorAction SilentlyContinue) {
            Start-Process -FilePath 'taskkill.exe' -ArgumentList "/PID $innerId /T /F" `
                -NoNewWindow -Wait
        }
    }
}

# Lists what the installer's own cleanup would have removed. Never removes it.
function Write-Leftovers {
    $left = @()
    if (Get-AppxPackage -Name 'DesktopGuides.Preview' -ErrorAction SilentlyContinue) {
        $left += 'the DesktopGuides.Preview package is still installed'
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
```

- [ ] **Step 4: Write the inner script**

```powershell
# T17.2 E: runs inside the task that start_installed_task.ps1 registers.
# It calls the installer with the recorded parameters, copies its output
# to installer.log, and writes task-exit.json.
param(
    [Parameter(Mandatory = $true)]
    [string] $InputPath
)

$ErrorActionPreference = 'Stop'
$handOver = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
$utf8 = [System.Text.UTF8Encoding]::new($false)
[ordered]@{ processId = $PID } | ConvertTo-Json |
    Set-Content -LiteralPath $handOver.startedPath -Encoding UTF8

# The task starts with the user's stored environment, not the caller's;
# CI's setup-dotnet SDK is on the caller's PATH only.
$env:PATH = $handOver.path
if ($handOver.dotnetRoot) { $env:DOTNET_ROOT = $handOver.dotnetRoot }

$arguments = @{}
foreach ($property in $handOver.parameters.PSObject.Properties) {
    $arguments[$property.Name] = $property.Value
}

$exitCode = 98
$failure = $null
try {
    $global:LASTEXITCODE = 0
    & $handOver.installer @arguments *>&1 | ForEach-Object {
        [System.IO.File]::AppendAllText($handOver.logPath, "$_`r`n", $utf8)
    }
    $exitCode = [int] $global:LASTEXITCODE
}
catch {
    $failure = ($_ | Out-String).Trim()
    [System.IO.File]::AppendAllText($handOver.logPath, "$failure`r`n", $utf8)
}

# Written beside, then renamed, so the entry point never reads half a file.
$pending = "$($handOver.exitPath).tmp"
[ordered]@{ exitCode = $exitCode; error = $failure } | ConvertTo-Json |
    Set-Content -LiteralPath $pending -Encoding UTF8
Move-Item -LiteralPath $pending -Destination $handOver.exitPath -Force
```

- [ ] **Step 5: Run it to verify it passes**

Run: **Entry-point tests**, then **Parse check**, then **ASCII check** on
the three new files.
Expected: `start_installed_task: all cases passed.` (about a minute,
mostly the silent and timeout cases), the parse test passes, and there are
no non-ASCII lines.

If the throwing fake's error isn't caught (exit 97 instead of 98), check
whether the inner script's process ended before writing the exit file.
Use superpowers:systematic-debugging. Don't widen the test's expectations.

- [ ] **Step 6: Commit**

```bash
git add tools/p1/start_installed_task.ps1 tools/p1/run_installed_task_inner.ps1 tools/p1/test_start_installed_task.ps1
git commit -m "feat(p1): start installed shell runs through an interactive scheduled task"
```

### Task 2: CI starts every installed shard through the entry point

**Files:**
- Modify: `.github/workflows/windows-ci.yml`:
  - `core-tests`, after "Check the Windows test scripts parse";
  - "Sign, install, and test production shell" in the
    `&production-shell-ui-steps` anchor (lines 524–544).

**Interfaces:**
- Consumes: `start_installed_task.ps1` from Task 1, with the installer's
  parameters and `-TimeoutMinutes`.
- Produces: installed shards whose logs show the installer's output through
  the entry point, and whose artifacts add `installer.log`,
  `task-input.json`, `task-started.json` and `task-exit.json`. These match
  the existing `*.log` and `*.json` patterns.

- [ ] **Step 1: Add the test to `core-tests`**

```yaml
      - name: Check the scheduled-task entry point
        shell: pwsh
        run: ./tools/p1/test_start_installed_task.ps1
```

- [ ] **Step 2: Start the installer through the entry point**

Replace the installer call in "Sign, install, and test production shell":

```yaml
          & ./tools/p1/start_installed_task.ps1 `
            -PackagePath $package.FullName `
            -ResultDirectory artifacts/production-shell/installed `
            -Groups @($env:SHELL_GROUPS -split ',') `
            -TimeoutMinutes 45 `
            @filter
          exit $LASTEXITCODE
```

The rest of the step, and the evidence and upload steps, stay as they are.

- [ ] **Step 3: Check locally**

Run:
- the YAML load: `ruby -C /Users/ilya.lissoboi/work/desktop-guides -ryaml -e 'f = ".github/workflows/windows-ci.yml"; begin; YAML.load_file(f, aliases: true); rescue ArgumentError; YAML.load_file(f); end'`;
- **ASCII check** on the workflow file.

Expected: the YAML loads, with no non-ASCII lines.

- [ ] **Step 4: Commit, push and dispatch one shard**

```bash
git add .github/workflows/windows-ci.yml
git commit -m "ci(p1): start installed shell shards through the scheduled-task entry point"
git push -u origin feat/p1-t17-2-task-entry
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-2-task-entry -f shell-scope=workflow -f dev-fast=true
```

Expected:
- `core-tests` passes, including "Check the scheduled-task entry point".
- `dev-production-shell-ui (workflow)` passes.
- Its log shows the installer's own lines, for example the `dotnet build`
  output for `DesktopGuides.ShellSeed`, after the step's own "Scenario
  groups: workflow" line.
- The artifact holds `installer.log` and `task-exit.json` with
  `"exitCode": 0`.
- The evidence summary lists the five workflow stages, as on #91.

When it fails:
1. Read `installer.log`, `task-exit.json` and the evidence summary.
2. Use superpowers:systematic-debugging.
3. Ledger any change to a step this plan specifies as a ruling.

A `dotnet` version difference inside the task would show as a different
`.NET SDK` line in the evidence than `10.0.401`.

### Task 3: Docs, the full PR run and the PR

**Files:**
- Modify: `docs/p1/e2e-testing.md`:
  - "Installation decision" (lines 23–29);
  - the host instructions in the provider paragraph ("On the host, start
    the installer from an interactive task with `-RunLevel Highest`",
    line 55).
- Modify: `docs/p1/t17-2-task-entry-design.md` (Status; "Rulings"; "T17.2
  piece E verification record")
- Modify: `docs/p1/implementation-plan.md` (the T17.2 note) and
  `docs/progress.md` (the T17.2 row and "Open:")

**Interfaces:**
- Consumes: Tasks 1–2, and the Task 2 dispatch run.
- Produces: the PR, with a green full run.

- [ ] **Step 1: Update `e2e-testing.md`**

Replace the paragraph starting "The existing [`production-shell-ui` CI
job]" with:

```markdown
`tools/p1/start_installed_task.ps1` is the scheduled-task entry point for
installed shell runs, on CI and on the host. It takes the installer's
parameters, plus `-TimeoutMinutes` (default 45), and runs
`windows_shell_install.ps1` in an elevated interactive task for the
logged-in user:

- The parameters cross into the task in `task-input.json`, with the
  caller's `PATH` and `DOTNET_ROOT`. The file holds paths and switches,
  never credential values.
- The installer's output is printed as it runs, and kept as
  `installer.log` in the result folder.
- The entry point exits with the installer's exit code. Its own codes are
  98 (the installer threw), 97 (the task ended without a result), 124
  (timeout) and 2 (refused: no logged-in desktop, or an earlier
  `DesktopGuides-P1-Installed-*` task still registered).
- On a timeout it stops the run and lists what may be left, such as the
  installed package, but removes nothing.

Every `production-shell-ui` shard starts through it. The task runs the
same PowerShell as the caller: `pwsh` on CI, Windows PowerShell 5.1 on the
host.
```

In the provider paragraph, replace "On the host, start the installer from
an interactive task with `-RunLevel Highest` (it trusts its temporary
certificate in `LocalMachine`), restore" with "On the host, start the run
over SSH with `powershell -NoProfile -ExecutionPolicy Bypass -File
tools\p1\start_installed_task.ps1 <installer parameters>`. The task is
elevated, because the installer trusts its temporary certificate in
`LocalMachine`. Restore".

- [ ] **Step 2: Update the design and status pages**

In the design:
- Set Status to "approved; implemented on `feat/p1-t17-2-task-entry`". Step 3 adds the PR number once the PR is open.
- Add a "Rulings" section holding plan rulings 1–5, word for word.
- Add a "T17.2 piece E verification record" section with the Task 2
  dispatch run ID and a line for the PR run.

In `implementation-plan.md`, append to the T17.2 note:

```markdown
Piece E starts every installed shell run through an elevated interactive
scheduled task, `tools/p1/start_installed_task.ps1`, on CI and on the
host; see the [piece E design](t17-2-task-entry-design.md).
```

In `docs/progress.md`:
- Add "and piece E (the scheduled-task entry point)" to the T17.2 row's
  outcome.
- Change "the CI scheduled-task entry point, the portable CI job and the
  fake-provider lane remain" to "the portable CI job and the fake-provider
  lane remain".

- [ ] **Step 3: Commit, push and open the PR**

```bash
git add docs/p1/e2e-testing.md docs/p1/t17-2-task-entry-design.md docs/p1/t17-2-task-entry-plan.md docs/p1/implementation-plan.md docs/progress.md
git commit -m "docs(p1): record the T17.2 scheduled-task entry point"
git push
```

Open the PR against `main`, titled "T17.2 piece E: scheduled-task entry
point for installed runs". The body names:
- the task: T17.2, piece E;
- the prerequisites and their status: pieces A and B merged in #90 and
  #91;
- the outcome: every installed shard, and host runs, start through
  `start_installed_task.ps1`;
- the dispatch run, with the log excerpt showing the installer's output
  through the task.

There are no UI changes, so no screenshot.

- [ ] **Step 4: Wait for the PR run**

Expected: every job passes, including the five `production-shell-ui`
shards and `core-tests`. Record the run ID in the verification record in a
follow-up commit. Then stop for the user's merge decision.

## Traceability

| Spec item | Task |
| --- | --- |
| The entry point: parameters, refusals, hand-over, task, waiting, exit | 1 |
| The inner script: environment, run, log, exit file | 1 |
| Exit codes 98, 97, 124, 2 and the installer's own | 1 |
| Timeout: stop the run, list leftovers, remove nothing | 1 |
| Tests 1–8 | 1 |
| CI: the install step, every shard, uploads | 2 |
| Host: one command in `e2e-testing.md` | 3 |
| Delivery and verification | 2, 3 |
