$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_profile.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-True $rejected $Message
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) `
    "desktop-guides-profile-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    Assert-FreshPreviewProfile $scratch
    $packagesRoot = Join-Path $scratch 'Packages'
    $unrelated = Join-Path $packagesRoot 'DesktopGuides.Unrelated_test'
    New-Item -ItemType Directory -Path $unrelated -Force | Out-Null
    Assert-FreshPreviewProfile $scratch

    $preview = Join-Path $packagesRoot 'DesktopGuides.Preview_test'
    $localState = Join-Path $preview 'LocalState'
    New-Item -ItemType Directory -Path $localState -Force | Out-Null
    $original = Join-Path $localState 'library.db'
    Set-Content -LiteralPath $original -Value 'keep this library'
    Assert-Rejected { Assert-FreshPreviewProfile $scratch } `
        'A populated unregistered Preview profile was accepted.'
    Assert-True ((Get-Content -LiteralPath $original -Raw).Trim() -eq
        'keep this library') 'The profile guard changed existing data.'

    Remove-Item -LiteralPath $preview -Recurse -Force
    New-Item -ItemType Directory -Path $preview | Out-Null
    Assert-Rejected { Assert-FreshPreviewProfile $scratch } `
        'An empty existing Preview profile was accepted.'
    Assert-Rejected { Assert-FreshPreviewProfile '' } `
        'An unavailable app-data path was accepted.'

    Write-Output 'Fresh Preview profile guard checks passed.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force `
        -ErrorAction SilentlyContinue
}
