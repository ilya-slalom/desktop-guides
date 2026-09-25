param(
    [Parameter(Mandatory = $true)]
    [string] $ExePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
$ruleName = 'DesktopGuides-P1-PdfCandidate-' + [Guid]::NewGuid().ToString('N')
$hostScript = Join-Path $PSScriptRoot 'windows_pdf_candidate_host.ps1'

try {
    New-NetFirewallRule -Name $ruleName -DisplayName $ruleName `
        -Direction Outbound -Action Block -Program $ExePath `
        -Profile Any -Enabled True | Out-Null
    if (-not (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue)) {
        throw 'The candidate outbound block was not installed.'
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $hostScript `
        -ExePath $ExePath -ResultPath $ResultPath `
        -NetworkMode OutboundBlocked
    if ($LASTEXITCODE -ne 0) {
        throw "The outbound-blocked candidate run failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
}
