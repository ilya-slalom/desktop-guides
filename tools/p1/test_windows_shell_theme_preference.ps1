$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_theme_preference.ps1')

$root = "HKCU:\Software\DesktopGuides-ThemeTest-$([Guid]::NewGuid().ToString('N'))"
try {
    # The Personalize key also holds the user's transparency and Windows mode.
    New-Item -Path $root | Out-Null
    New-ItemProperty -Path $root -Name EnableTransparency -PropertyType DWord -Value 1 | Out-Null
    $original = Get-AppThemePreference $root
    if ($original.hasValue) { throw 'A missing app theme read as present.' }

    Set-AppThemePreference $true $root
    $values = Get-ItemProperty -Path $root
    if ($values.AppsUseLightTheme -ne 1) { throw 'The light app theme was not stored.' }
    if ($values.EnableTransparency -ne 1) {
        throw 'Setting the app theme removed another Personalize value.'
    }

    Restore-AppThemePreference $original
    $values = Get-ItemProperty -Path $root
    if ($values.PSObject.Properties.Name -contains 'AppsUseLightTheme') {
        throw 'Restoring a missing app theme left a value behind.'
    }
    if ($values.EnableTransparency -ne 1) {
        throw 'Restoring the app theme removed another Personalize value.'
    }

    $missing = "$root\Missing"
    Set-AppThemePreference $false $missing
    if ((Get-AppThemePreference $missing).value -ne 0) {
        throw 'The app theme was not stored under a missing key.'
    }
    'Theme preference checks passed.'
}
finally {
    Remove-Item -Path $root -Recurse -Force -ErrorAction SilentlyContinue
}
