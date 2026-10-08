param(
    # Full or short hash of the commit the publish was built from.
    [Parameter(Mandatory = $true)]
    [string] $Commit,

    [string] $PublishDirectory,

    [string] $OutputDirectory
)
# Zips a single-file portable publish with a SHA-256 and a JSON manifest.
# Refuses anything beside the exe and never replaces an existing release.
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $PublishDirectory) {
    $PublishDirectory = Join-Path $repoRoot `
        'artifacts\portable\bin\DesktopGuides.Production\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\portable\release' }
if ($Commit -notmatch '^[0-9a-f]{7,40}$') { throw "Expected a commit hash, got '$Commit'." }

$exeName = 'DesktopGuides.Production.exe'
if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "Publish folder not found: $PublishDirectory"
}
$published = @(Get-ChildItem -LiteralPath $PublishDirectory -Force)
if ($published.Count -ne 1 -or $published[0].Name -ne $exeName -or $published[0].PSIsContainer) {
    $names = ($published | Select-Object -First 5 | ForEach-Object Name) -join ', '
    throw "Expected a single-file publish holding only $exeName; found $($published.Count) entries: $names"
}
$exe = $published[0]

$name = "DesktopGuides-portable-x64-$($Commit.Substring(0, 7))"
$zipPath = Join-Path $OutputDirectory "$name.zip"
$manifestPath = Join-Path $OutputDirectory "$name.json"
$checksumPath = Join-Path $OutputDirectory "$name.zip.sha256"
foreach ($path in @($zipPath, $manifestPath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) { throw "Release file already exists: $path" }
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$exeHash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash
# Windows' own tar writes zip files; Git's GNU tar may come first on PATH.
$tar = Join-Path $env:SystemRoot 'System32\tar.exe'
$check = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-release-$([Guid]::NewGuid().ToString('N'))"
try {
    & $tar -a -cf $zipPath -C $PublishDirectory $exeName
    if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE." }
    # The archive must hold the exe alone and unpack to the same bytes.
    $listed = @(& $tar -tf $zipPath)
    if ($listed.Count -ne 1 -or $listed[0] -ne $exeName) {
        throw "The archive lists '$($listed -join ', ')' instead of $exeName alone."
    }
    New-Item -ItemType Directory -Path $check | Out-Null
    & $tar -xf $zipPath -C $check
    if ($LASTEXITCODE -ne 0) { throw "tar could not unpack the archive: $LASTEXITCODE." }
    $unpackedHash = (Get-FileHash -LiteralPath (Join-Path $check $exeName) -Algorithm SHA256).Hash
    if ($unpackedHash -ne $exeHash) { throw 'The archived exe differs from the published one.' }
}
catch {
    Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
    throw
}
finally {
    Remove-Item -LiteralPath $check -Recurse -Force -ErrorAction SilentlyContinue
}

$zip = Get-Item -LiteralPath $zipPath
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$sdk = (Get-Content -LiteralPath (Join-Path $repoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$windowsAppSdk = ([xml](Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Packages.props') -Raw)).
    Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Microsoft.WindowsAppSDK' |
    ForEach-Object Version
$manifest = [ordered]@{
    name = $zip.Name
    commit = $Commit
    architecture = 'x64'
    kind = 'portable single-file exe: Windows App SDK and .NET bundled, no package identity'
    executable = $exeName
    executableBytes = $exe.Length
    executableSha256 = $exeHash
    executableFileVersion = $exe.VersionInfo.FileVersion
    dataFolder = '%LOCALAPPDATA%\DesktopGuides'
    extractionFolder = '%TEMP%\.net\DesktopGuides.Production'
    notBundled = 'WebView2 Runtime (HTML guides)'
    dotnetSdk = $sdk
    windowsAppSdk = $windowsAppSdk
    zipBytes = $zip.Length
    sha256 = $zipHash
    builtOn = [Environment]::MachineName
    builtAt = (Get-Date).ToUniversalTime().ToString('o')
}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), $utf8)
[IO.File]::WriteAllText($checksumPath, "$zipHash  $($zip.Name)`n", $utf8)
Write-Output "Packaged $($zip.Name): $($zip.Length) bytes, SHA-256 $zipHash"
