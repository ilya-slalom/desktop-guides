param(
    [Parameter(Mandatory = $true)] [string] $Thumbprint,
    [string] $PinFile
)
# T17.1: the release job signs only with the pinned certificate, so a wrong
# or rotated secret fails before anything is signed.
$ErrorActionPreference = 'Stop'
# Resolved here: Windows PowerShell leaves $PSScriptRoot empty in parameter
# defaults when the script runs with -File.
if (-not $PinFile) { $PinFile = Join-Path $PSScriptRoot '..\..\docs\p1\release\release-certificate.json' }
$pin = Get-Content -LiteralPath $PinFile -Raw | ConvertFrom-Json
if ($Thumbprint -ne $pin.thumbprint) {
    throw "The signing certificate $Thumbprint is not the pinned release certificate $($pin.thumbprint)."
}
$cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction SilentlyContinue
if (-not $cert) { throw "Certificate $Thumbprint is not in Cert:\CurrentUser\My." }
if ($cert.Subject -ne $pin.subject) { throw "The certificate Subject '$($cert.Subject)' is not '$($pin.subject)'." }
# pwsh 7 parses the ISO date itself and 5.1 leaves a string; compare both in UTC.
if (([datetime] $pin.notAfter).ToUniversalTime() -lt [datetime]::UtcNow) {
    throw "The pinned release certificate expired on $($pin.notAfter); create and pin a new one."
}
