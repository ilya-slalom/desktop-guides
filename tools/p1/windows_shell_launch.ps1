param(
    [Parameter(Mandatory = $true)]
    [string] $ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath,

    [string] $ExecutableArguments = '',

    [ValidateRange(1, 120)]
    [int] $HandoffTimeoutSeconds = 45
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if (-not ('DesktopGuidesOwnedProcess' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'windows_owned_process.cs')
}
$process = $null
$handedOff = $false
$errorText = $null
$token = [Guid]::NewGuid().ToString('N')
$report = [ordered]@{
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    executablePath = $ExecutablePath
    handoffToken = $token
    success = $false
}

try {
    $startArguments = @{
        FilePath = $ExecutablePath
        PassThru = $true
    }
    if ($ExecutableArguments) {
        $startArguments.ArgumentList = $ExecutableArguments
    }
    $process = Start-Process @startArguments
    $report.processId = $process.Id
    $report.sessionId = $process.SessionId
    $report.startedAt = $process.StartTime.ToUniversalTime().ToString('o')
    $report.success = $true

    $temporaryPath = "$ResultPath.tmp"
    $report | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $temporaryPath -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $ResultPath -Force

    $ackPath = "$ResultPath.ack"
    $deadline = (Get-Date).AddSeconds($HandoffTimeoutSeconds)
    do {
        if ($process.HasExited) {
            $handedOff = $true
            break
        }
        if (Test-Path -LiteralPath $ackPath) {
            $ack = Get-Content -LiteralPath $ackPath -Raw |
                ConvertFrom-Json
            if ($ack.handoffToken -eq $token) {
                $handedOff = $true
                break
            }
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    if (-not $handedOff) {
        throw 'Interactive launch ownership handoff timed out.'
    }
}
catch {
    $errorText = $_ | Out-String
    if (-not $process) {
        try {
            $report.error = $errorText
            $temporaryPath = "$ResultPath.tmp"
            $report | ConvertTo-Json -Depth 4 |
                Set-Content -LiteralPath $temporaryPath -Encoding UTF8
            Move-Item -LiteralPath $temporaryPath -Destination $ResultPath -Force
        }
        catch {
            $errorText += ($_ | Out-String)
        }
    }
}
finally {
    if ($process) {
        if (-not $handedOff) {
            try {
                if (-not [DesktopGuidesOwnedProcess]::TerminateAndWait(
                    $process.Handle, 20000)) {
                    throw 'Timed out stopping unclaimed launched process.'
                }
            }
            catch {
                $errorText += ($_ | Out-String)
            }
        }
        $process.Dispose()
    }
}
if ($errorText) {
    Write-Error $errorText
    exit 1
}
