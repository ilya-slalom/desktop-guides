# T17.4: measures the portable exe: its size, what its first launch extracts,
# and cold and warm launch times to the library view. Run it through
# start_installed_task.ps1 -Installer, which gives it the interactive desktop.
param(
    [Parameter(Mandatory = $true)]
    [string] $PortableExecutable,
    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory,
    [ValidateRange(1, 9)]
    [int] $Runs = 3,
    [int] $ReadyTimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_profile.ps1')
. (Join-Path $PSScriptRoot 'windows_distribution_measure.ps1')

$PortableExecutable = (Resolve-Path -LiteralPath $PortableExecutable).Path
# The run owns the data folder only if it starts absent.
Assert-FreshPortableProfile $env:LOCALAPPDATA
$sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
if ($sessionId -eq 0 -or -not @(Get-Process explorer -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $sessionId })) {
    throw 'Measuring launches requires an interactive desktop session.'
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$automation = [System.Windows.Automation.AutomationElement]
$descendants = [System.Windows.Automation.TreeScope]::Descendants
$library = [System.Windows.Automation.PropertyCondition]::new($automation::AutomationIdProperty, 'LibraryNavigation')
$unavailable = [System.Windows.Automation.PropertyCondition]::new($automation::AutomationIdProperty, 'LibraryUnavailable')

function Stop-MeasuredApp([Diagnostics.Process] $Process) {
    if ($Process.HasExited) { return }
    [void] $Process.CloseMainWindow()
    if (-not $Process.WaitForExit(20000)) {
        $Process.Kill()
        [void] $Process.WaitForExit(10000)
        throw 'The app did not close within 20 s, so it was killed.'
    }
}

# Milliseconds from start until the library view shows.
function Measure-Launch([string] $ExtractBase) {
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $ExtractBase
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $PortableExecutable -PassThru
    try {
        while ($true) {
            if ($process.HasExited) { throw "The app exited during launch, with code $($process.ExitCode)." }
            $process.Refresh()
            if ($process.MainWindowHandle -ne 0) {
                $root = $null
                try { $root = $automation::FromHandle($process.MainWindowHandle) } catch { }
                if ($root -and $root.FindFirst($descendants, $library)) {
                    $elapsed = $clock.Elapsed.TotalMilliseconds
                    if ($root.FindFirst($descendants, $unavailable)) {
                        throw 'The library could not open, so the launch time means nothing.'
                    }
                    return $elapsed
                }
            }
            if ($clock.Elapsed.TotalSeconds -gt $ReadyTimeoutSeconds) {
                throw "The library view did not appear within $ReadyTimeoutSeconds s."
            }
            Start-Sleep -Milliseconds 50
        }
    }
    finally {
        Stop-MeasuredApp $process
        Remove-Item Env:\DOTNET_BUNDLE_EXTRACT_BASE_DIR -ErrorAction SilentlyContinue
    }
}

New-Item -ItemType Directory -Force -Path $ResultDirectory | Out-Null
$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-measure-$([Guid]::NewGuid().ToString('N'))"
$dataFolder = Join-Path $env:LOCALAPPDATA 'DesktopGuides'
try {
    # The untimed first launch creates the data folder, so every timed
    # launch opens the same empty library.
    $first = Join-Path $scratch 'first'
    [void] (Measure-Launch $first)
    $extraction = Get-FolderFootprint $first
    $cold = @(for ($i = 1; $i -le $Runs; $i++) { Measure-Launch (Join-Path $scratch "cold-$i") })
    $warmBase = Join-Path $scratch "cold-$Runs"
    $warm = @(for ($i = 1; $i -le $Runs; $i++) { Measure-Launch $warmBase })
    $exe = Get-Item -LiteralPath $PortableExecutable
    $result = [ordered]@{
        executable = $exe.Name
        executableBytes = $exe.Length
        extraction = $extraction
        coldLaunch = New-LaunchSummary $cold
        warmLaunch = New-LaunchSummary $warm
        readyWhen = 'the main window shows and UI Automation finds LibraryNavigation'
        osBuild = [Environment]::OSVersion.Version.ToString()
        processor = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
        measuredAt = (Get-Date).ToUniversalTime().ToString('o')
    }
    [IO.File]::WriteAllText((Join-Path $ResultDirectory 'measurements.json'),
        ($result | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
    Write-Output ("Measured {0}: {1} bytes; extraction {2} files, {3} bytes; cold {4} ms; warm {5} ms" -f
        $exe.Name, $exe.Length, $extraction.files, $extraction.bytes,
        $result.coldLaunch.medianMs, $result.warmLaunch.medianMs)
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    # Absent before the first launch, so it is this run's own. WebView2 may
    # hold it for a moment after the app exits.
    for ($try = 0; $try -lt 20 -and (Test-Path -LiteralPath $dataFolder); $try++) {
        Remove-Item -LiteralPath $dataFolder -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $dataFolder) { Start-Sleep -Milliseconds 500 }
    }
    if (Test-Path -LiteralPath $dataFolder) {
        Write-Output "measure_distribution: could not remove $dataFolder; remove it by hand."
    }
}
