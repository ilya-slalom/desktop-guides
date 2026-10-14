# T17.4: the arithmetic behind measure_distribution.ps1.

function Get-MedianMilliseconds([double[]] $Values) {
    if (-not $Values -or $Values.Count -eq 0) { throw 'No launch times to summarise.' }
    $sorted = @($Values | Sort-Object)
    $middle = [int] [Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2 -eq 1) { $sorted[$middle] }
        else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    return [long] [Math]::Round($median, [MidpointRounding]::AwayFromZero)
}

function Get-FolderFootprint([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Extraction folder not found: $Path"
    }
    $files = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force)
    $bytes = 0L
    foreach ($file in $files) { $bytes += $file.Length }
    return [ordered]@{ files = $files.Count; bytes = $bytes }
}

function New-LaunchSummary([double[]] $Runs) {
    return [ordered]@{
        medianMs = Get-MedianMilliseconds $Runs
        runsMs = @($Runs | ForEach-Object { [long] [Math]::Round($_, [MidpointRounding]::AwayFromZero) })
    }
}

# Whether a launch has reached the opened library. $Find takes an
# AutomationId and says whether that element shows. The navigation item
# exists from the first frame, so readiness is the empty library view that
# rendering shows once the library has opened; a timed launch always opens
# the empty library its untimed first launch created.
function Test-LibraryShown([scriptblock] $Find) {
    if (& $Find 'LibraryUnavailable') {
        throw 'The library could not open, so the launch time means nothing.'
    }
    return [bool] (& $Find 'LibraryEmpty')
}
