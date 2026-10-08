$ErrorActionPreference = 'Stop'
$packager = Join-Path $PSScriptRoot 'package_portable_release.ps1'
$exeName = 'DesktopGuides.Production.exe'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-True $rejected $Message
}

function New-Publish([string] $Path) {
    New-Item -ItemType Directory -Path $Path | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $Path $exeName), [byte[]](1..200))
}

function Get-Outputs([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    @(Get-ChildItem -LiteralPath $Path -Force | ForEach-Object Name)
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) `
    "desktop-guides-portable-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $publish = Join-Path $scratch 'publish'
    $release = Join-Path $scratch 'release'
    New-Publish $publish
    $commit = '0123456789abcdef0123456789abcdef01234567'
    & $packager -PublishDirectory $publish -OutputDirectory $release -Commit $commit | Out-Null

    $name = 'DesktopGuides-portable-x64-0123456'
    $zip = Join-Path $release "$name.zip"
    $outputs = Get-Outputs $release | Sort-Object
    Assert-True (($outputs -join ',') -eq "$name.json,$name.zip,$name.zip.sha256") `
        "Unexpected release files: $($outputs -join ', ')"
    $listed = @(& (Join-Path $env:SystemRoot 'System32\tar.exe') -tf $zip)
    Assert-True ($listed.Count -eq 1 -and $listed[0] -eq $exeName) `
        "The archive should hold the exe alone at its root, got: $($listed -join ', ')"

    $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    $exeHash = (Get-FileHash -LiteralPath (Join-Path $publish $exeName) -Algorithm SHA256).Hash
    $manifest = Get-Content -LiteralPath (Join-Path $release "$name.json") -Raw | ConvertFrom-Json
    Assert-True ($manifest.commit -eq $commit) 'The manifest should record the full commit.'
    Assert-True ($manifest.executable -eq $exeName) 'The manifest should name the exe.'
    Assert-True ($manifest.executableSha256 -eq $exeHash) 'The manifest exe hash is wrong.'
    Assert-True ($manifest.executableBytes -eq 200) 'The manifest exe size is wrong.'
    Assert-True ($manifest.sha256 -eq $zipHash) 'The manifest archive hash is wrong.'
    Assert-True ($manifest.zipBytes -eq (Get-Item -LiteralPath $zip).Length) `
        'The manifest archive size is wrong.'
    Assert-True ($manifest.dotnetSdk -match '^\d+\.\d+\.\d+$') 'The manifest SDK is missing.'
    Assert-True ($manifest.windowsAppSdk -match '^\d+\.\d+\.\d+') `
        'The manifest Windows App SDK version is missing.'
    $checksum = (Get-Content -LiteralPath (Join-Path $release "$name.zip.sha256") -Raw).Trim()
    Assert-True ($checksum -eq "$zipHash  $name.zip") "Unexpected checksum line: $checksum"

    # A second run must not replace a published release.
    Assert-Rejected {
        & $packager -PublishDirectory $publish -OutputDirectory $release -Commit $commit
    } 'An existing release was overwritten.'
    Assert-True ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -eq $zipHash) `
        'The refused run changed the existing archive.'

    # Anything beside the exe means the publish isn't single-file.
    $cases = @(
        @{ Label = 'a symbol file'; Add = { param($p) Set-Content (Join-Path $p 'DesktopGuides.Core.pdb') 'x' } },
        @{ Label = 'a loose DLL'; Add = { param($p) Set-Content (Join-Path $p 'Microsoft.UI.Xaml.dll') 'x' } },
        @{ Label = 'a language folder'; Add = { param($p) New-Item -ItemType Directory (Join-Path $p 'en-us') | Out-Null } },
        @{ Label = 'no exe'; Add = { param($p) Remove-Item (Join-Path $p $exeName) } }
    )
    $index = 0
    foreach ($case in $cases) {
        $index++
        $badPublish = Join-Path $scratch "bad-publish-$index"
        $badRelease = Join-Path $scratch "bad-release-$index"
        New-Publish $badPublish
        & $case.Add $badPublish
        Assert-Rejected {
            & $packager -PublishDirectory $badPublish -OutputDirectory $badRelease -Commit $commit
        } "A publish folder with $($case.Label) was packaged."
        Assert-True ((Get-Outputs $badRelease).Count -eq 0) `
            "The refused run with $($case.Label) left release files."
    }

    Assert-Rejected {
        & $packager -PublishDirectory $publish -OutputDirectory (Join-Path $scratch 'r') -Commit 'main'
    } 'A commit that is not a hash was accepted.'

    Write-Output 'Portable release packaging checks passed.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
