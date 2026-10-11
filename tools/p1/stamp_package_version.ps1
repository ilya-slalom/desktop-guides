param(
    [Parameter(Mandatory = $true)]
    [string] $Manifest,

    [Parameter(Mandatory = $true)]
    [string] $Version
)
# Writes the release version into the public package manifest's Identity,
# leaving every other byte as it was (T17.1).
$ErrorActionPreference = 'Stop'
$publicName = 'IlyaLissoboi.DesktopGuides'
if ($Version -notmatch '^[1-9][0-9]{0,4}\.[0-9]{1,5}\.[0-9]{1,5}\.0$') {
    throw "Expected a public package version X.Y.Z.0, got '$Version'."
}
$text = [IO.File]::ReadAllText($Manifest)
[xml] $parsed = $text
if ($parsed.Package.Identity.Name -ne $publicName) {
    throw "Refusing to stamp a manifest that isn't the public identity: $Manifest"
}
$identity = [regex]::Match($text, '<Identity\b[^>]*>')
if (-not $identity.Success) { throw "No Identity element in $Manifest." }
$stampedIdentity = [regex]::Replace($identity.Value, 'Version="[^"]*"', "Version=""$Version""")
$stamped = $text.Substring(0, $identity.Index) + $stampedIdentity +
    $text.Substring($identity.Index + $identity.Length)
[IO.File]::WriteAllText($Manifest, $stamped, [Text.UTF8Encoding]::new($false))
[xml] $check = [IO.File]::ReadAllText($Manifest)
if ($check.Package.Identity.Version -ne $Version -or $check.Package.Identity.Name -ne $publicName) {
    throw "Stamping $Manifest did not produce version $Version."
}
