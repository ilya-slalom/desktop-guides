$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'assert_release_certificate.ps1'

function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw $Message }
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-pin-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
$subject = "CN=DesktopGuides Pin Test $([Guid]::NewGuid().ToString('N'))"
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
    -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddDays(1)
try {
    function Write-Pin([string] $Thumbprint, [string] $PinSubject, [datetime] $NotAfter) {
        $path = Join-Path $scratch "pin-$([Guid]::NewGuid().ToString('N')).json"
        [ordered]@{ subject = $PinSubject; thumbprint = $Thumbprint; notAfter = $NotAfter.ToUniversalTime().ToString('o') } |
            ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8
        $path
    }
    $good = Write-Pin $cert.Thumbprint $subject $cert.NotAfter
    & $script -Thumbprint $cert.Thumbprint -PinFile $good

    $other = Write-Pin ('0' * 40) $subject $cert.NotAfter
    Assert-Rejected { & $script -Thumbprint $cert.Thumbprint -PinFile $other } `
        'A certificate that is not the pinned one must be rejected.'
    $wrongSubject = Write-Pin $cert.Thumbprint 'CN=Someone Else' $cert.NotAfter
    Assert-Rejected { & $script -Thumbprint $cert.Thumbprint -PinFile $wrongSubject } `
        'A Subject mismatch must be rejected.'
    $expired = Write-Pin $cert.Thumbprint $subject (Get-Date).AddDays(-1)
    Assert-Rejected { & $script -Thumbprint $cert.Thumbprint -PinFile $expired } `
        'An expired pin must be rejected.'
    $absent = Write-Pin ('A' * 40) $subject $cert.NotAfter
    Assert-Rejected { & $script -Thumbprint ('A' * 40) -PinFile $absent } `
        'A thumbprint missing from the store must be rejected.'
    Write-Output 'release certificate pin: all cases passed.'
}
finally {
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
