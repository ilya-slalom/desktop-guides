$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_run_record.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

# A passing body: its value comes back, and the run is recorded.
$runs = [System.Collections.Generic.List[object]]::new()
$value = Measure-SmokeRun $runs 'empty-again' 'empty' { [ordered]@{ success = $true; mode = 'empty' } }
Assert-True ($value.mode -eq 'empty') 'The body result was not returned.'
Assert-True ($runs.Count -eq 1) 'A passing run was not recorded.'
Assert-True ($runs[0].name -eq 'empty-again' -and $runs[0].mode -eq 'empty') 'The run name or mode is wrong.'
Assert-True ($runs[0].passed -and $null -eq $runs[0].error) 'A passing run was not marked passed.'
Assert-True ($runs[0].seconds -ge 0) 'The duration is missing.'
[void][DateTime]::Parse($runs[0].startedAt)

# Run-ShellSmoke's body reads its own parameters from inside the block.
function Invoke-LikeRunShellSmoke([string] $mode, [string] $ResultName = $mode) {
    return Measure-SmokeRun $runs $ResultName $mode { "$ResultName ran $mode" }
}
Assert-True ((Invoke-LikeRunShellSmoke 'pdf-zoom' 'pdf-zoom-dark') -eq 'pdf-zoom-dark ran pdf-zoom') `
    "The body could not read its caller's parameters."
$runs.RemoveAt($runs.Count - 1)

# A failing body: the failure is recorded, then rethrown unchanged.
$caught = $null
try { Measure-SmokeRun $runs 'keyboard' 'keyboard' { Start-Sleep -Milliseconds 120; throw 'Installed keyboard shell smoke failed: x' } }
catch { $caught = $_.Exception.Message }
Assert-True ($caught -eq 'Installed keyboard shell smoke failed: x') "The failure was not rethrown (got: $caught)."
Assert-True ($runs.Count -eq 2 -and -not $runs[1].passed) 'A failing run was not recorded as failed.'
Assert-True ($runs[1].error -eq $caught) 'The failing run lost its error.'
Assert-True ($runs[1].seconds -ge 0.1) "The failing run's duration is $($runs[1].seconds)."

# The WebView2 version: the first key with a real version wins.
$base = "HKCU:\Software\DesktopGuidesRunRecordTest-$([Guid]::NewGuid().ToString('N'))"
try {
    $missing = "$base\missing"
    $zero = "$base\zero"
    $real = "$base\real"
    New-Item -Path $zero -Force | Out-Null
    New-ItemProperty -Path $zero -Name pv -Value '0.0.0.0' | Out-Null
    New-Item -Path $real -Force | Out-Null
    New-ItemProperty -Path $real -Name pv -Value '141.0.3537.71' | Out-Null
    Assert-True ((Get-WebView2RuntimeVersion @($missing, $zero, $real)) -eq '141.0.3537.71') 'The real version was not found.'
    Assert-True ($null -eq (Get-WebView2RuntimeVersion @($missing, $zero))) 'A missing runtime did not read as null.'
}
finally {
    Remove-Item -Path $base -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'windows_shell_run_record: all cases passed.'
