$ErrorActionPreference = 'Stop'
$gate = Join-Path $PSScriptRoot 'windows_package_upgrade.ps1'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock] $Action, [string] $Message, [string] $Pattern) {
    $failure = $null
    try { & $Action } catch { $failure = $_.Exception.Message }
    Assert-True ($null -ne $failure) $Message
    Assert-True ($failure -match $Pattern) "$Message (got: $failure)"
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-upgrade-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null

# A stand-in package: a zip holding only AppxManifest.xml with the given identity.
function New-Package([string] $File, [string] $Name, [string] $Publisher, [string] $Version,
    [string] $Architecture = 'x64') {
    $path = Join-Path $scratch $File
    $zip = [IO.Compression.ZipFile]::Open($path, 'Create')
    try {
        $entry = $zip.CreateEntry('AppxManifest.xml')
        $writer = [IO.StreamWriter]::new($entry.Open())
        $writer.Write("<Package xmlns=""http://schemas.microsoft.com/appx/manifest/foundation/windows10""><Identity Name=""$Name"" Publisher=""$Publisher"" Version=""$Version"" ProcessorArchitecture=""$Architecture"" /></Package>")
        $writer.Dispose()
    }
    finally { $zip.Dispose() }
    $path
}

try {
    $public = 'IlyaLissoboi.DesktopGuides'
    $publisher = 'CN=359962D5-3801-484E-BE5B-39D0D498BEA4'
    $v1 = New-Package 'v1.msix' $public $publisher '1.0.0.0'
    $v2 = New-Package 'v2.msix' $public $publisher '1.0.1.0'
    $otherPublisher = New-Package 'other.msix' $public 'CN=Someone Else' '1.0.1.0'
    $preview = New-Package 'preview.msix' 'DesktopGuides.Preview' 'CN=DesktopGuides Development' '1.0.1.0'
    $arm64 = New-Package 'arm64.msix' $public $publisher '1.0.1.0' 'arm64'
    $cer = Join-Path $scratch 'release.cer'
    Set-Content -LiteralPath $cer -Value 'x'
    $notInstalled = { $false }
    $common = @{ Certificate = $cer; SeedTool = 'seed.dll'; ResultPath = (Join-Path $scratch 'r.json'); PreflightOnly = $true }

    & $gate -OldPackage $v1 -NewPackage $v2 -InstalledCheck $notInstalled @common
    Assert-Rejected { & $gate -OldPackage $v1 -NewPackage $v2 -InstalledCheck { $true } @common } `
        'The gate must refuse when the public app is already installed.' 'already installed'
    Assert-Rejected { & $gate -OldPackage $v2 -NewPackage $v1 -InstalledCheck $notInstalled @common } `
        'A lower new version must be rejected.' "isn't higher than 1\.0\.1\.0"
    Assert-Rejected { & $gate -OldPackage $v1 -NewPackage $v1 -InstalledCheck $notInstalled @common } `
        'The same version must be rejected.' "1\.0\.0\.0 isn't higher than 1\.0\.0\.0"
    Assert-Rejected { & $gate -OldPackage $v1 -NewPackage $otherPublisher -InstalledCheck $notInstalled @common } `
        'A different Publisher must be rejected.' 'Expected the public identity, got .* / CN=Someone Else'
    Assert-Rejected { & $gate -OldPackage $preview -NewPackage $v2 -InstalledCheck $notInstalled @common } `
        'A non-public package must be rejected.' 'Expected the public identity, got DesktopGuides\.Preview'
    Assert-Rejected { & $gate -OldPackage $v1 -NewPackage $arm64 -InstalledCheck $notInstalled @common } `
        'Packages for different architectures must be rejected.' 'different architectures'
    $missingCertificate = $common.Clone()
    $missingCertificate.Certificate = Join-Path $scratch 'missing.cer'
    Assert-Rejected { & $gate -OldPackage $v1 -NewPackage $v2 -InstalledCheck $notInstalled @missingCertificate } `
        'A missing certificate must be rejected.' 'Certificate not found'
    Write-Output 'windows_package_upgrade preflight: all cases passed.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
