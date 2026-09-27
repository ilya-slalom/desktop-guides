$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_reader_toolbar_install_receipt.ps1')

$directory = Join-Path ([System.IO.Path]::GetTempPath()) `
    ('desktop-guides-toolbar-receipt-' + [Guid]::NewGuid().ToString('N'))
$path = Join-Path $directory 'receipt.json'
$invocationId = [Guid]::NewGuid().ToString('N')
$package = 'DesktopGuides.ReaderToolbarSmoke_0.1.0.0_x64__en8r599z3n35j'
try {
    New-Item -ItemType Directory -Path $directory | Out-Null
    if (Test-ToolbarInstallReceipt $path $invocationId $package) {
        throw 'A missing install receipt was accepted.'
    }
    @{ invocationId = [Guid]::NewGuid().ToString('N')
       packageFullName = $package } | ConvertTo-Json |
        Set-Content -LiteralPath $path -Encoding UTF8
    if (Test-ToolbarInstallReceipt $path $invocationId $package) {
        throw 'A stale install receipt was accepted.'
    }
    @{ invocationId = $invocationId
       packageFullName = 'DesktopGuides.ReaderToolbarSmoke_other' } |
        ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8
    if (Test-ToolbarInstallReceipt $path $invocationId $package) {
        throw 'A receipt for a different package was accepted.'
    }
    Set-Content -LiteralPath $path -Value '{invalid' -Encoding UTF8
    if (Test-ToolbarInstallReceipt $path $invocationId $package) {
        throw 'A malformed install receipt was accepted.'
    }
    @{ invocationId = $invocationId
       packageFullName = $package } | ConvertTo-Json |
        Set-Content -LiteralPath $path -Encoding UTF8
    if (-not (Test-ToolbarInstallReceipt $path $invocationId $package)) {
        throw 'The current install receipt was rejected.'
    }
    Write-Output 'Toolbar install receipt ownership checks passed.'
}
finally {
    Remove-Item -LiteralPath $directory -Recurse -Force `
        -ErrorAction SilentlyContinue
}
