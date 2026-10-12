# T17.2 E: runs inside the task that start_installed_task.ps1 registers.
# It runs the installer in a child PowerShell with the recorded parameters,
# copies everything the child prints to installer.log, and writes
# task-exit.json. With -Child it is that child.
param(
    [Parameter(Mandatory = $true)]
    [string] $InputPath,

    [switch] $Child
)

$ErrorActionPreference = 'Stop'
$handOver = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
$errorPath = "$($handOver.exitPath).error"

if ($Child) {
    $arguments = @{}
    foreach ($property in $handOver.parameters.PSObject.Properties) {
        $arguments[$property.Name] = $property.Value
    }
    try {
        $global:LASTEXITCODE = 0
        & $handOver.installer @arguments
        exit ([int] $global:LASTEXITCODE)
    }
    catch {
        $failure = ($_ | Out-String).Trim()
        Set-Content -LiteralPath $errorPath -Value $failure -Encoding UTF8
        [Console]::Out.WriteLine($failure)
        exit 98
    }
}

$utf8 = [System.Text.UTF8Encoding]::new($false)
[ordered]@{ processId = $PID } | ConvertTo-Json |
    Set-Content -LiteralPath $handOver.startedPath -Encoding UTF8
Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue

# The task starts with the user's stored environment, not the caller's;
# CI's setup-dotnet SDK is on the caller's PATH only. The child inherits it.
$env:PATH = $handOver.path
if ($handOver.dotnetRoot) { $env:DOTNET_ROOT = $handOver.dotnetRoot }

# A child with redirected output prints Out-Host and Write-Host to stdout,
# which the installer uses for dotnet build; in-process capture misses them.
# Its stderr lines arrive as error records, so they must not stop this loop.
$shell = (Get-Process -Id $PID).Path
$exitCode = 98
$failure = $null
try {
    $ErrorActionPreference = 'Continue'
    & $shell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $PSCommandPath `
        -InputPath $InputPath -Child 2>&1 | ForEach-Object {
            [System.IO.File]::AppendAllText($handOver.logPath, "$_`r`n", $utf8)
        }
    $exitCode = [int] $LASTEXITCODE
}
catch {
    $failure = ($_ | Out-String).Trim()
    [System.IO.File]::AppendAllText($handOver.logPath, "$failure`r`n", $utf8)
}
finally {
    $ErrorActionPreference = 'Stop'
}
if (Test-Path -LiteralPath $errorPath) {
    $failure = (Get-Content -LiteralPath $errorPath -Raw).Trim()
    Remove-Item -LiteralPath $errorPath -Force
}

# Written beside, then renamed, so the entry point never reads half a file.
$pending = "$($handOver.exitPath).tmp"
[ordered]@{ exitCode = $exitCode; error = $failure } | ConvertTo-Json |
    Set-Content -LiteralPath $pending -Encoding UTF8
Move-Item -LiteralPath $pending -Destination $handOver.exitPath -Force
