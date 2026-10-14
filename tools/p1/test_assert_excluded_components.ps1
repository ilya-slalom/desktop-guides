$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'assert_excluded_components.ps1'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

# Returns the error text, or fails when the action succeeds.
function Assert-Rejected([scriptblock] $Action, [string] $Expected, [string] $Message) {
    try { & $Action | Out-Null } catch {
        Assert-True ("$_" -like "*$Expected*") "$Message Got: $_"
        return
    }
    throw "$Message It was accepted."
}

$excluded = [ordered]@{
    'Microsoft.WindowsAppSDK.AI' = '2.5.5'
    'Microsoft.WindowsAppSDK.ML' = '2.1.94'
    'Microsoft.WindowsAppSDK.Search' = '2.5.5'
    'Microsoft.WindowsAppSDK.Widgets' = '2.0.5'
    'Microsoft.WindowsAppSDK.DWrite' = '2.1.0'
    'Microsoft.Windows.AI.MachineLearning' = '2.1.74'
}

function New-Lock([string] $Path, [string[]] $Skip = @()) {
    $framework = [ordered]@{
        'Microsoft.WindowsAppSDK' = [ordered]@{ type = 'Direct'; resolved = '2.5.1' }
    }
    foreach ($id in $excluded.Keys) {
        if ($id -in $Skip) { continue }
        $framework[$id] = [ordered]@{ type = 'Transitive'; resolved = $excluded[$id] }
    }
    $lock = [ordered]@{ version = 1; dependencies = [ordered]@{
        'net10.0-windows10.0.19041' = $framework
        'net10.0-windows10.0.19041/win-x64' = [ordered]@{}
    } }
    [IO.File]::WriteAllText($Path, ($lock | ConvertTo-Json -Depth 5))
}

function New-PackageFile([string] $Root, [string] $Id, [string] $Relative) {
    $path = Join-Path $Root (Join-Path $Id.ToLowerInvariant() (Join-Path $excluded[$Id] $Relative))
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    [IO.File]::WriteAllBytes($path, [byte[]](1, 2, 3))
}

function New-Msix([string] $Path, [string[]] $Files, [string] $Dependencies) {
    $manifest = '<?xml version="1.0" encoding="utf-8"?>' +
        '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">' +
        "<Dependencies>$Dependencies</Dependencies></Package>"
    $zip = [IO.Compression.ZipFile]::Open($Path, 'Create')
    try {
        foreach ($name in @('AppxManifest.xml') + $Files) {
            $writer = [IO.StreamWriter]::new($zip.CreateEntry($name).Open())
            try { $writer.Write($(if ($name -eq 'AppxManifest.xml') { $manifest } else { 'x' })) }
            finally { $writer.Dispose() }
        }
    }
    finally { $zip.Dispose() }
}

$runtime = '<PackageDependency Name="Microsoft.WindowsAppRuntime.2" MinVersion="2.5.1.0" Publisher="CN=Microsoft Corporation" />'
$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-components-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $packages = Join-Path $scratch 'packages'
    New-PackageFile $packages 'Microsoft.WindowsAppSDK.AI' 'lib\net6.0-windows10.0.17763.0\Microsoft.Windows.AI.Projection.dll'
    New-PackageFile $packages 'Microsoft.WindowsAppSDK.AI' 'runtimes-framework\win-x64\native\Microsoft.Windows.Workloads.dll'
    New-PackageFile $packages 'Microsoft.WindowsAppSDK.Search' 'metadata\Microsoft.Windows.Search.winmd'
    New-PackageFile $packages 'Microsoft.WindowsAppSDK.Widgets' 'runtimes-framework\win-x64\native\Microsoft.Windows.Widgets.dll'
    New-PackageFile $packages 'Microsoft.WindowsAppSDK.DWrite' 'runtimes-framework\win-x64\native\DWriteCore.dll'
    New-PackageFile $packages 'Microsoft.Windows.AI.MachineLearning' 'runtimes\win-x64\native\onnxruntime.dll'
    New-PackageFile $packages 'Microsoft.Windows.AI.MachineLearning' 'build\readme.txt'
    # The ML package holds build files only; its folder must still exist.
    New-Item -ItemType Directory -Force -Path (Join-Path $packages 'microsoft.windowsappsdk.ml\2.1.94\build') | Out-Null
    $lock = Join-Path $scratch 'packages.lock.json'
    New-Lock $lock
    $common = @{ LockFile = $lock; PackageRoot = $packages }

    # A portable bundle list: clean, then with excluded files.
    $clean = Join-Path $scratch 'clean.txt'
    Set-Content -LiteralPath $clean -Value @('Microsoft.WinUI.dll', 'Microsoft.ui.xaml.dll', 'DesktopGuides.Production.dll')
    $output = & $checker -FileList $clean @common
    Assert-True ("$output" -like 'Excluded components: none*') "A clean list should pass, got: $output"

    $dirty = Join-Path $scratch 'dirty.txt'
    Set-Content -LiteralPath $dirty -Value @('Microsoft.WinUI.dll', 'DWriteCore.dll', 'runtimes\win-x64\native\ONNXRUNTIME.dll')
    Assert-Rejected { & $checker -FileList $dirty @common } 'DWriteCore.dll (Microsoft.WindowsAppSDK.DWrite)' `
        'A list holding DWriteCore.dll should fail, naming the file and its package.'
    Assert-Rejected { & $checker -FileList $dirty @common } 'ONNXRUNTIME.dll (Microsoft.Windows.AI.MachineLearning)' `
        'Names should match without regard to case or folder.'

    # Positive control: a list without WinUI isn't a build of this app.
    $empty = Join-Path $scratch 'empty.txt'
    Set-Content -LiteralPath $empty -Value @('DesktopGuides.Production.dll')
    Assert-Rejected { & $checker -FileList $empty @common } 'Microsoft.WinUI.dll' `
        'A list without Microsoft.WinUI.dll should fail.'
    $blank = Join-Path $scratch 'blank.txt'
    Set-Content -LiteralPath $blank -Value @('')
    Assert-Rejected { & $checker -FileList $blank @common } 'lists no files' 'An empty list should fail.'

    # The lock file and the package cache must both know every package.
    $partial = Join-Path $scratch 'partial.lock.json'
    New-Lock $partial -Skip 'Microsoft.WindowsAppSDK.Widgets'
    Assert-Rejected { & $checker -FileList $clean -LockFile $partial -PackageRoot $packages } `
        'has no Microsoft.WindowsAppSDK.Widgets' 'A lock file missing a package should fail.'
    Assert-Rejected { & $checker -FileList $clean -LockFile $lock -PackageRoot (Join-Path $scratch 'none') } `
        'Package folder not found' 'A missing package folder should fail.'

    # An MSIX: its payload and its runtime dependency.
    $good = Join-Path $scratch 'good.msix'
    New-Msix $good @('Microsoft.WinUI.dll', 'DesktopGuides.Production.dll') $runtime
    $output = & $checker -Msix $good @common
    Assert-True ("$output" -like 'Excluded components: none*') "A clean MSIX should pass, got: $output"

    $payload = Join-Path $scratch 'payload.msix'
    New-Msix $payload @('Microsoft.WinUI.dll', 'Microsoft.Windows.AI.Projection.dll') $runtime
    Assert-Rejected { & $checker -Msix $payload @common } 'Microsoft.Windows.AI.Projection.dll' `
        'An MSIX holding a projection of an excluded component should fail.'

    $old = Join-Path $scratch 'old.msix'
    New-Msix $old @('Microsoft.WinUI.dll') $runtime.Replace('2.5.1.0', '2.4.9.0')
    Assert-Rejected { & $checker -Msix $old @common } 'below 2.5.1.0' 'An older runtime minimum should fail.'

    $none = Join-Path $scratch 'none.msix'
    New-Msix $none @('Microsoft.WinUI.dll') ''
    Assert-Rejected { & $checker -Msix $none @common } 'Microsoft.WindowsAppRuntime.2' `
        'An MSIX without the runtime dependency should fail.'

    Assert-Rejected { & $checker @common } 'either -FileList or -Msix' 'No input should fail.'
    Assert-Rejected { & $checker -FileList $clean -Msix $good @common } 'either -FileList or -Msix' `
        'Two inputs should fail.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'assert_excluded_components: all cases passed.'
