# T17.3: the names that differ between the Preview and Public lanes, and the
# release-signer check. The app's own names are in AppLaneNames (Core).

function Get-ShellLane([string] $Lane = 'Preview') {
    switch ($Lane) {
        'Preview' { return [pscustomobject]@{ Name = 'Preview'; PackageName = 'DesktopGuides.Preview'; EventPrefix = 'DesktopGuides.Preview' } }
        'Public' { return [pscustomobject]@{ Name = 'Public'; PackageName = 'IlyaLissoboi.DesktopGuides'; EventPrefix = 'DesktopGuides' } }
    }
    throw "Unknown lane '$Lane'; expected Preview or Public."
}

# Every lane's package, so a leftover check misses neither.
function Get-ShellLanePackageNames { return @('DesktopGuides.Preview', 'IlyaLissoboi.DesktopGuides') }

function Test-InstalledLaneExecutable([string] $Path, [string] $ProgramFiles = $env:ProgramFiles) {
    foreach ($name in Get-ShellLanePackageNames) {
        if ($Path -like "$ProgramFiles\WindowsApps\$($name)_*_x64__*\DesktopGuides.Production.exe") { return $true }
    }
    return $false
}

function Get-ReleasePin([string] $PinPath) {
    $pin = Get-Content -LiteralPath $PinPath -Raw | ConvertFrom-Json
    if ("$($pin.thumbprint)" -notmatch '^[0-9A-F]{40}$') { throw "The release pin $PinPath has no valid thumbprint." }
    return [string] $pin.thumbprint
}

# Refuses a package that isn't signed by the pinned release certificate.
# Trust isn't checked here: Add-AppxPackage checks it once the run has
# trusted the certificate. A changed package fails as HashMismatch.
function Assert-ReleaseSigner([string] $Path, [string] $Thumbprint) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if (-not $signature.SignerCertificate) {
        throw "$Path has no signature the harness can read (status $($signature.Status))."
    }
    if ($signature.SignerCertificate.Thumbprint -ne $Thumbprint) {
        throw "$Path is signed by $($signature.SignerCertificate.Thumbprint), not the release certificate $Thumbprint."
    }
    if ("$($signature.Status)" -in @('HashMismatch', 'NotSigned', 'NotSupportedFileFormat', 'Incompatible')) {
        throw "$Path has a broken signature (status $($signature.Status))."
    }
}
