param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $CertificateThumbprint,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedName,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedPublisher,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedVersion,

    [Parameter(Mandatory = $true)]
    [ValidateSet('x64', 'ARM64')]
    [string] $ExpectedArchitecture,

    [string] $TimestampUrl,

    [string] $PreviousPackagePath,

    [switch] $DevelopmentTest
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$source = (Resolve-Path -LiteralPath $PackagePath -ErrorAction Stop).Path
$target = [IO.Path]::GetFullPath($OutputPath)
if ([string]::Equals($source, $target,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Input and output MSIX paths must differ.'
}
if (Test-Path -LiteralPath $target) {
    throw 'Output MSIX already exists.'
}
if ($ExpectedName -eq 'DesktopGuides.App') {
    throw 'The diagnostic package cannot be signed as production.'
}
if ($DevelopmentTest -and $ExpectedName -notmatch '(\.Preview|Test)$') {
    throw 'The development signing mode requires a Preview or test package identity.'
}
if (-not $DevelopmentTest) {
    if ($ExpectedName -match '(\.Preview|Test)$') {
        throw 'A public release cannot use a Preview or test package identity.'
    }
    if ($ExpectedPublisher -eq 'CN=DesktopGuides Development') {
        throw 'A public release cannot use the development publisher.'
    }
    if ($ExpectedVersion -notmatch '^[1-9][0-9]*\.[0-9]+\.[0-9]+\.0$') {
        throw 'A public release version must be major.minor.patch.0 with a nonzero major version.'
    }
    if (-not $TimestampUrl) {
        throw 'A public release requires a trusted timestamp URL.'
    }
}

$certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint" `
    -ErrorAction Stop
if (-not [string]::Equals($certificate.Subject, $ExpectedPublisher,
        [StringComparison]::Ordinal)) {
    throw 'The certificate subject does not match the manifest publisher.'
}
if (-not $certificate.HasPrivateKey) {
    throw 'The selected certificate has no available private key.'
}
$now = Get-Date
if ($now -lt $certificate.NotBefore -or $now -ge $certificate.NotAfter) {
    throw 'The selected certificate is outside its validity period.'
}
$codeSigningEku = @(
    $certificate.EnhancedKeyUsageList |
        Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })
if ($codeSigningEku.Count -ne 1) {
    throw 'The selected certificate is not valid for code signing.'
}

$verifier = Join-Path $PSScriptRoot 'verify_production_package.py'
$verifyArgs = @(
    $verifier, $source,
    '--name', $ExpectedName,
    '--publisher', $ExpectedPublisher,
    '--version', $ExpectedVersion,
    '--architecture', $ExpectedArchitecture
)
if ($PreviousPackagePath) {
    $previous = (Resolve-Path -LiteralPath $PreviousPackagePath `
        -ErrorAction Stop).Path
    $verifyArgs += @('--previous-package', $previous)
}
$metadataJson = & python @verifyArgs
if ($LASTEXITCODE -ne 0) {
    throw 'The unsigned package failed its production identity check.'
}
$metadata = $metadataJson | ConvertFrom-Json

$signTool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' `
    -Recurse -Filter signtool.exe |
    Where-Object { $_.FullName -like '*\x64\signtool.exe' } |
    Sort-Object FullName |
    Select-Object -Last 1 -ExpandProperty FullName
if (-not $signTool) {
    throw 'Windows SDK SignTool is unavailable.'
}

try {
    Copy-Item -LiteralPath $source -Destination $target -ErrorAction Stop
    $signArgs = @(
        'sign', '/fd', 'SHA256',
        '/sha1', $certificate.Thumbprint,
        '/s', 'My'
    )
    if ($TimestampUrl) {
        $signArgs += @('/tr', $TimestampUrl, '/td', 'SHA256')
    }
    $signArgs += $target
    $signOutput = & $signTool @signArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool sign failed with exit code $LASTEXITCODE`: $signOutput"
    }
    $signVerifyArgs = @('verify', '/pa')
    if (-not $DevelopmentTest) {
        $signVerifyArgs += '/tw'
    }
    $signVerifyArgs += $target
    $verifyOutput = & $signTool @signVerifyArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool verify failed with exit code $LASTEXITCODE`: $verifyOutput"
    }
    $verifySignedArgs = @(
        $verifier, $target,
        '--name', $ExpectedName,
        '--publisher', $ExpectedPublisher,
        '--version', $ExpectedVersion,
        '--architecture', $ExpectedArchitecture
    )
    if ($PreviousPackagePath) {
        $verifySignedArgs += @('--previous-package', $previous)
    }
    $verifiedJson = & python @verifySignedArgs
    if ($LASTEXITCODE -ne 0) {
        throw 'The signed package failed its production identity check.'
    }
    $verified = $verifiedJson | ConvertFrom-Json
    if ($verified.windowsAppRuntimeMinimum -ne
        $metadata.windowsAppRuntimeMinimum) {
        throw 'Windows App Runtime dependency changed during signing.'
    }
    [ordered]@{
        packagePath = $target
        packageSha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        name = $verified.name
        publisher = $verified.publisher
        version = $verified.version
        previousVersion = $verified.previousVersion
        architecture = $verified.architecture
        windowsAppRuntimeMinimum = $verified.windowsAppRuntimeMinimum
        signerThumbprint = $certificate.Thumbprint
        signerNotAfter = $certificate.NotAfter.ToUniversalTime().ToString('o')
        timestamped = [bool]$TimestampUrl
    } | ConvertTo-Json
}
catch {
    Remove-Item -LiteralPath $target -ErrorAction SilentlyContinue
    throw
}
