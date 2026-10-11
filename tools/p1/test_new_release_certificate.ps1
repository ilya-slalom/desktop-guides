$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'new_release_certificate.ps1'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$subject = "CN=DesktopGuides CertScript Test $([Guid]::NewGuid().ToString('N'))"
$out = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-cert-$([Guid]::NewGuid().ToString('N'))"
try {
    $lines = @(& $script -OutputDirectory $out -Subject $subject -Years 1)
    $info = ($lines | Where-Object { $_ -like '{*' } | Select-Object -First 1) | ConvertFrom-Json
    Assert-True ($info.subject -eq $subject) 'The certificate Subject is wrong.'
    Assert-True ($info.thumbprint -match '^[0-9A-F]{40}$') 'The thumbprint is missing.'
    foreach ($file in @('pfx', 'pfx.base64.txt', 'password.txt', 'cer')) {
        Assert-True (Test-Path -LiteralPath (Join-Path $out "desktop-guides-release.$file")) "The $file file is missing."
    }
    Assert-True (-not (Test-Path "Cert:\CurrentUser\My\$($info.thumbprint)")) `
        'The certificate must be removed from the store.'
    Assert-True (@($lines | Where-Object { $_ -like '*gh secret set RELEASE_SIGNING_PFX*' }).Count -eq 1) `
        'The PFX secret command is missing.'
    $password = [IO.File]::ReadAllText((Join-Path $out 'desktop-guides-release.password.txt'))
    Assert-True (@($lines | Where-Object { $_.Contains($password) }).Count -eq 0) 'The password was printed.'
    $pfx = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        (Join-Path $out 'desktop-guides-release.pfx'), $password)
    Assert-True ($pfx.Subject -eq $subject -and $pfx.HasPrivateKey) 'The PFX does not open with its password.'
    $eku = $pfx.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] }
    Assert-True (@($eku.EnhancedKeyUsages | Where-Object Value -eq '1.3.6.1.5.5.7.3.3').Count -eq 1) `
        'The certificate is not for code signing.'
    $pfx.Dispose()
    $refused = $false
    try { & $script -OutputDirectory $out -Subject $subject | Out-Null } catch { $refused = $true }
    Assert-True $refused 'A second run into a used folder must refuse.'
    Write-Output 'new_release_certificate: all cases passed.'
}
finally {
    Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction SilentlyContinue
    # Leave no test certificate behind, even when an assertion failed.
    Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $subject } |
        ForEach-Object { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($_.Thumbprint)" -ErrorAction SilentlyContinue }
}
