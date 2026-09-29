function Assert-FreshPreviewProfile([string] $LocalAppDataPath) {
    if ([string]::IsNullOrWhiteSpace($LocalAppDataPath)) {
        throw 'Local app data path is unavailable.'
    }
    $packagesRoot = Join-Path $LocalAppDataPath 'Packages'
    if (-not (Test-Path -LiteralPath $packagesRoot)) {
        return
    }
    if (-not (Test-Path -LiteralPath $packagesRoot -PathType Container)) {
        throw 'Package profile root is not a directory.'
    }
    $profiles = @(Get-ChildItem -LiteralPath $packagesRoot -Directory `
        -Filter 'DesktopGuides.Preview_*' -ErrorAction Stop)
    if ($profiles.Count -gt 0) {
        throw 'A DesktopGuides.Preview profile already exists; refusing the fresh-profile install test.'
    }
}

# The portable build has no package profile; its data lives in
# %LOCALAPPDATA%\DesktopGuides. The run owns that folder only if it starts absent.
function Assert-FreshPortableProfile([string] $LocalAppDataPath) {
    if ([string]::IsNullOrWhiteSpace($LocalAppDataPath)) {
        throw 'Local app data path is unavailable.'
    }
    if (Test-Path -LiteralPath (Join-Path $LocalAppDataPath 'DesktopGuides')) {
        throw 'A portable DesktopGuides data folder already exists; refusing the fresh-profile test.'
    }
}
