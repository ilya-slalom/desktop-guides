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
