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
    path = $env:PATH; dotnetRoot = $env:DOTNET_ROOT; psHome = $PSHOME
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ResultDirectory 'fake-received.json') -Encoding UTF8
Write-Output 'fake line one'
# The installer sends dotnet build to Out-Host, which skips the streams.
'fake host line' | Out-Host
Write-Host 'fake write-host line'
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
    # pwsh puts its own folder first when it starts; 5.1 doesn't. Nothing
    # else may come before the caller's whole PATH.
    $carried = "$marker;$savedPath"
    Assert-True ($received.path -eq $carried -or $received.path -eq "$($received.psHome);$carried") `
        "PATH arrived as '$($received.path.Substring(0, 80))...'."
    Assert-True ($received.dotnetRoot -eq 'C:\dg-fake-dotnet-root') "DOTNET_ROOT arrived as '$($received.dotnetRoot)'."
    Assert-True ($run.output -contains 'fake line one') 'The first line did not reach the output.'
    Assert-True ($run.output -contains 'fake line last') 'The last line did not reach the output.'
    Assert-True ($run.output -contains 'fake host line') 'Out-Host output did not reach the output.'
    Assert-True ($run.output -contains 'fake write-host line') 'Write-Host output did not reach the output.'
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
