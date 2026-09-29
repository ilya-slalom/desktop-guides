$script:AppThemePreferencePath =
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'

function Get-AppThemePreference([string] $Path = $script:AppThemePreferencePath) {
    $item = Get-ItemProperty -Path $Path -ErrorAction SilentlyContinue
    $hasValue = $item -and
        $item.PSObject.Properties.Name -contains 'AppsUseLightTheme'
    return [ordered]@{
        path = $Path
        hasValue = [bool]$hasValue
        value = if ($hasValue) { [int]$item.AppsUseLightTheme } else { $null }
    }
}

function Set-AppThemePreference(
    [bool] $UseLightTheme,
    [string] $Path = $script:AppThemePreferencePath) {
    # New-Item -Force would recreate the key and delete the user's other values.
    if (-not (Test-Path -Path $Path)) {
        New-Item -Path $Path -Force | Out-Null
    }
    New-ItemProperty -Path $Path -Name AppsUseLightTheme `
        -PropertyType DWord -Value ([int]$UseLightTheme) -Force | Out-Null
}

function Restore-AppThemePreference($Original) {
    if ($Original.hasValue) {
        New-ItemProperty -Path $Original.path -Name AppsUseLightTheme `
            -PropertyType DWord -Value $Original.value -Force | Out-Null
    }
    else {
        Remove-ItemProperty -Path $Original.path -Name AppsUseLightTheme `
            -ErrorAction SilentlyContinue
    }
}
