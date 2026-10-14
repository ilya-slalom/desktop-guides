# T17.4: fails when a build carries a Windows App SDK component the app
# doesn't use. The forbidden file names are the excluded packages' own
# .dll and .winmd files, at the versions packages.lock.json resolves.
param(
    # A portable bundle list (BundleFileListPath), one relative path a line.
    [string] $FileList,
    [string] $Msix,
    [string] $LockFile = (Join-Path $PSScriptRoot '..\..\src\DesktopGuides.Production\packages.lock.json'),
    [string] $PackageRoot,
    [string] $MinimumRuntime = '2.5.1.0'
)
$ErrorActionPreference = 'Stop'
$excluded = @('Microsoft.WindowsAppSDK.AI', 'Microsoft.WindowsAppSDK.ML',
    'Microsoft.WindowsAppSDK.Search', 'Microsoft.WindowsAppSDK.Widgets',
    'Microsoft.WindowsAppSDK.DWrite', 'Microsoft.Windows.AI.MachineLearning')
# Every build of the app carries the WinUI projection; a list without it
# isn't this app's, and would pass for the wrong reason.
$control = 'microsoft.winui.dll'

if ([bool] $FileList -eq [bool] $Msix) { throw 'Pass either -FileList or -Msix.' }
if (-not $PackageRoot) {
    $PackageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES }
        else { Join-Path $env:USERPROFILE '.nuget\packages' }
}

# The framework section (no RID) holds every package's resolved version.
$lock = Get-Content -LiteralPath $LockFile -Raw | ConvertFrom-Json
$framework = @($lock.dependencies.PSObject.Properties | Where-Object { $_.Name -notmatch '/' })[0].Value
$forbidden = @{}
foreach ($id in $excluded) {
    $entry = $framework.PSObject.Properties[$id]
    if (-not $entry) { throw "packages.lock.json has no $id; the check would prove nothing." }
    $folder = Join-Path $PackageRoot (Join-Path $id.ToLowerInvariant() $entry.Value.resolved)
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
        throw "Package folder not found: $folder. Restore first."
    }
    Get-ChildItem -LiteralPath $folder -Recurse -File |
        Where-Object { $_.Extension -in @('.dll', '.winmd') } |
        ForEach-Object { $forbidden[$_.Name.ToLowerInvariant()] = $id }
}
if ($forbidden.Count -eq 0) { throw 'The excluded packages hold no .dll or .winmd files.' }

if ($FileList) {
    $names = @(Get-Content -LiteralPath $FileList | ForEach-Object { $_.Trim() } |
        Where-Object { $_ } | ForEach-Object { Split-Path $_ -Leaf })
    if ($names.Count -eq 0) { throw "$FileList lists no files." }
}
else {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Msix).Path)
    try {
        $names = @($zip.Entries | Where-Object { $_.Name } |
            ForEach-Object { [Uri]::UnescapeDataString($_.Name) })
        $entry = $zip.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw "$Msix has no AppxManifest.xml." }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
    $ns = @{ m = 'http://schemas.microsoft.com/appx/manifest/foundation/windows10' }
    $runtime = @(Select-Xml -Xml $manifest -Namespace $ns `
        -XPath "//m:Dependencies/m:PackageDependency[@Name='Microsoft.WindowsAppRuntime.2']")
    if ($runtime.Count -ne 1) {
        throw "Expected one Microsoft.WindowsAppRuntime.2 dependency in $Msix, found $($runtime.Count)."
    }
    $minimum = $runtime[0].Node.GetAttribute('MinVersion')
    if ([version] $minimum -lt [version] $MinimumRuntime) {
        throw "The Microsoft.WindowsAppRuntime.2 minimum $minimum is below $MinimumRuntime."
    }
}

if (-not ($names | Where-Object { $_.ToLowerInvariant() -eq $control })) {
    throw "The build lists no Microsoft.WinUI.dll; it isn't a build of this app."
}
$found = @($names | Where-Object { $forbidden.ContainsKey($_.ToLowerInvariant()) } | Sort-Object -Unique)
if ($found.Count -gt 0) {
    $list = ($found | ForEach-Object { "$_ ($($forbidden[$_.ToLowerInvariant()]))" }) -join ', '
    throw "Excluded Windows App SDK components are in the build: $list"
}
Write-Output "Excluded components: none of $($forbidden.Count) file names among $($names.Count) entries."
