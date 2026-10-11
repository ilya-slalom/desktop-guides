param(
    [Parameter(Mandatory = $true)]
    [string] $Tag
)
# Turns a release tag vX.Y.Z into the package version X.Y.Z.0 (T17.1).
$ErrorActionPreference = 'Stop'
if ($Tag -cnotmatch '^v(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$') {
    throw "Expected a release tag vX.Y.Z, got '$Tag'."
}
$parts = @([int] $Matches[1], [int] $Matches[2], [int] $Matches[3])
if ($parts[0] -lt 1) { throw "The major version must be at least 1, got '$Tag'." }
if (@($parts | Where-Object { $_ -gt 65535 }).Count -gt 0) {
    throw "Each version field must be at most 65535, got '$Tag'."
}
'{0}.{1}.{2}.0' -f $parts[0], $parts[1], $parts[2]
