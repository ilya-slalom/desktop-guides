# T17.2: facts the installer records for the evidence checklist.

# Runs one smoke and records its timing, whether it passes or throws.
# $Body reads its caller's variables through this function's scope, so the
# locals here carry a measure prefix that no caller uses.
function Measure-SmokeRun(
    [System.Collections.IList] $Runs,
    [string] $Name,
    [string] $Mode,
    [scriptblock] $Body) {
    $measureStarted = (Get-Date).ToUniversalTime()
    $measureWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $measurePassed = $false
    $measureFailure = $null
    try {
        $measureResult = & $Body
        $measurePassed = $true
        return $measureResult
    }
    catch {
        $measureFailure = $_.Exception.Message
        throw
    }
    finally {
        $Runs.Add([ordered]@{
            name = $Name; mode = $Mode; startedAt = $measureStarted.ToString('o')
            seconds = [Math]::Round($measureWatch.Elapsed.TotalSeconds, 1)
            passed = $measurePassed; error = $measureFailure
        })
    }
}

# WebView2 Runtime registers its version under the EdgeUpdate client key:
# machine-wide (32-bit view on x64, then native), or per user.
function Get-WebView2RuntimeVersion([string[]] $Keys = @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')) {
    foreach ($key in $Keys) {
        $value = (Get-ItemProperty -Path $key -Name pv -ErrorAction SilentlyContinue).pv
        if ($value -and $value -ne '0.0.0.0') { return [string] $value }
    }
    return $null
}
