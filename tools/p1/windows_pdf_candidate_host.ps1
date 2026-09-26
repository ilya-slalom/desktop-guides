param(
    [Parameter(Mandatory = $true)]
    [string] $ExePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath,

    [string] $UiScript,

    [ValidateSet('Online', 'OutboundBlocked')]
    [string] $NetworkMode = 'Online'
)

$ErrorActionPreference = 'Stop'
$taskName = 'DesktopGuides-P1-PdfCandidate'
$script = if ($UiScript) {
    $UiScript
} else {
    Join-Path $PSScriptRoot 'windows_pdf_candidate_ui.ps1'
}
$arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass ' +
    '-File "' + $script + '" -ExePath "' + $ExePath + '" -ResultPath "' + $ResultPath +
    '" -NetworkMode "' + $NetworkMode + '"'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument $arguments -WorkingDirectory (Split-Path $script)
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME `
    -LogonType Interactive -RunLevel Limited
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    throw "Task $taskName already exists; refusing to replace it."
}

try {
    Remove-Item $ResultPath -ErrorAction SilentlyContinue
    Register-ScheduledTask -TaskName $taskName -Action $action `
        -Principal $principal | Out-Null
    Start-ScheduledTask -TaskName $taskName
    $deadline = (Get-Date).AddMinutes(2)
    do {
        Start-Sleep -Seconds 1
    } while (-not (Test-Path $ResultPath) -and (Get-Date) -lt $deadline)
    if (-not (Test-Path $ResultPath)) {
        throw 'Interactive PDF candidate run timed out.'
    }
    $result = Get-Content $ResultPath -Raw | ConvertFrom-Json
    $result | ConvertTo-Json -Depth 6
    if (-not $result.success) { exit 1 }
}
finally {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false `
        -ErrorAction SilentlyContinue
}
