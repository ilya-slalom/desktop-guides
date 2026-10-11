$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'stamp_package_version.ps1'
$project = Join-Path $PSScriptRoot '..\..\src\DesktopGuides.Production'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-True $rejected $Message
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-stamp-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $public = Join-Path $scratch 'Package.Public.appxmanifest'
    Copy-Item (Join-Path $project 'Package.Public.appxmanifest') $public
    $before = [IO.File]::ReadAllText($public)

    & $script -Manifest $public -Version '2.3.4.0'

    $after = [IO.File]::ReadAllText($public)
    [xml] $xml = $after
    Assert-True ($xml.Package.Identity.Version -eq '2.3.4.0') 'The Identity version was not stamped.'
    Assert-True ($xml.Package.Identity.Name -eq 'IlyaLissoboi.DesktopGuides') 'The Identity name changed.'
    $expected = $before -replace 'Version="1\.0\.0\.0"', 'Version="2.3.4.0"'
    Assert-True ($after -eq $expected) 'Stamping changed more than the Identity version.'

    $preview = Join-Path $scratch 'Package.appxmanifest'
    Copy-Item (Join-Path $project 'Package.appxmanifest') $preview
    Assert-Rejected { & $script -Manifest $preview -Version '2.3.4.0' } `
        'The Preview manifest must not be stamped.'
    Assert-Rejected { & $script -Manifest $public -Version '2.3.4.1' } `
        'A fourth field other than 0 must be rejected.'
    Assert-Rejected { & $script -Manifest $public -Version '0.3.4.0' } `
        'A zero major must be rejected.'
    Write-Output 'stamp_package_version: all cases passed.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
