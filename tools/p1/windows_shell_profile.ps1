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
