$ErrorActionPreference = 'Stop'

$scratch = Join-Path ([IO.Path]::GetTempPath()) `
    "desktop-guides-sign-guard-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $inputPackage = Join-Path $scratch 'input.msix'
    Set-Content -LiteralPath $inputPackage -Value 'No package is signed by this test.'
    $signer = Join-Path $PSScriptRoot 'sign_production_package.ps1'

    function Assert-PublicIdentityRejected(
        [string] $Name,
        [string] $Publisher) {
        $outputPackage = Join-Path $scratch 'signed.msix'
        try {
            & $signer `
                -PackagePath $inputPackage `
                -OutputPath $outputPackage `
                -CertificateThumbprint ('0' * 40) `
                -ExpectedName $Name `
                -ExpectedPublisher $Publisher `
                -ExpectedVersion '1.0.0.0' `
                -ExpectedArchitecture x64 `
                -TimestampUrl 'https://timestamp.invalid'
            throw "Public signing accepted $Name / $Publisher."
        }
        catch {
            if ($_.Exception.Message -notmatch
                '^A public release cannot use ') {
                throw "Expected identity rejection for $Name / $Publisher; got: $_"
            }
        }
        if (Test-Path -LiteralPath $outputPackage) {
            throw "Public signing created output for $Name / $Publisher."
        }
    }

    Assert-PublicIdentityRejected `
        'DesktopGuides.Preview' 'CN=Future Publisher'
    Assert-PublicIdentityRejected `
        'DesktopGuides.PackageUpgradeTest' 'CN=Future Publisher'
    Assert-PublicIdentityRejected `
        'DesktopGuides.Release' 'CN=DesktopGuides Development'

    Write-Output 'Public signing identity guard checks passed.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force `
        -ErrorAction SilentlyContinue
}
