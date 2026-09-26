param(
    [Parameter(Mandatory = $true)]
    [string] $ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
$report = [ordered]@{
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    executablePath = $ExecutablePath
    success = $false
}

try {
    $process = Start-Process -FilePath $ExecutablePath -PassThru
    $report.processId = $process.Id
    $report.sessionId = $process.SessionId
    $report.startedAt = $process.StartTime.ToUniversalTime().ToString('o')
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
}
finally {
    $temporaryPath = "$ResultPath.tmp"
    $report | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $temporaryPath -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $ResultPath -Force
}
if (-not $report.success) { exit 1 }
