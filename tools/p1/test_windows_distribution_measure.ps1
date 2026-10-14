$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_distribution_measure.ps1')
$driver = Join-Path $PSScriptRoot 'measure_distribution.ps1'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

Assert-True ((Get-MedianMilliseconds @(900, 100, 500)) -eq 500) 'The median of three is the middle value.'
Assert-True ((Get-MedianMilliseconds @(400, 100, 300, 200)) -eq 250) 'The median of four averages the middle two.'
Assert-True ((Get-MedianMilliseconds @(1499.6)) -eq 1500) 'Medians round to whole milliseconds.'
$rejected = $false
try { Get-MedianMilliseconds @() | Out-Null } catch { $rejected = $true }
Assert-True $rejected 'No launch times should fail.'

$summary = New-LaunchSummary @(1210.4, 1190.6, 1500.2)
Assert-True ($summary.medianMs -eq 1210) "Unexpected median: $($summary.medianMs)"
Assert-True ((@($summary.runsMs) -join ',') -eq '1210,1191,1500') "Runs keep their order: $(@($summary.runsMs) -join ',')"

$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-measure-test-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path (Join-Path $scratch 'tree\sub') | Out-Null
$saved = $env:LOCALAPPDATA
try {
    [IO.File]::WriteAllBytes((Join-Path $scratch 'tree\a.bin'), [byte[]](1..10))
    [IO.File]::WriteAllBytes((Join-Path $scratch 'tree\sub\b.bin'), [byte[]](1..5))
    $footprint = Get-FolderFootprint (Join-Path $scratch 'tree')
    Assert-True ($footprint.files -eq 2 -and $footprint.bytes -eq 15) "Unexpected footprint: $($footprint.files) files, $($footprint.bytes) bytes"
    $rejected = $false
    try { Get-FolderFootprint (Join-Path $scratch 'absent') | Out-Null } catch { $rejected = $true }
    Assert-True $rejected 'A missing extraction folder should fail.'

    # The driver must refuse, and touch nothing, when portable data exists.
    $local = Join-Path $scratch 'local'
    $data = Join-Path $local 'DesktopGuides'
    New-Item -ItemType Directory -Path $data | Out-Null
    [IO.File]::WriteAllText((Join-Path $data 'library.db'), 'user data')
    $exe = Join-Path $scratch 'DesktopGuides.Production.exe'
    [IO.File]::WriteAllBytes($exe, [byte[]](1..3))
    $env:LOCALAPPDATA = $local
    $refusal = $null
    try { & $driver -PortableExecutable $exe -ResultDirectory (Join-Path $scratch 'out') | Out-Null }
    catch { $refusal = "$_" }
    Assert-True ($refusal -like '*portable DesktopGuides data folder already exists*') "Expected a refusal, got: $refusal"
    Assert-True ((Get-Content -LiteralPath (Join-Path $data 'library.db') -Raw) -eq 'user data') 'The user data must stay untouched.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $scratch 'out\measurements.json'))) 'A refused run writes no measurements.'
}
finally {
    $env:LOCALAPPDATA = $saved
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'windows_distribution_measure: all cases passed.'
