$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'release_version.ps1'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

foreach ($case in @(
        @('v1.0.0', '1.0.0.0'),
        @('v12.3.45', '12.3.45.0'),
        @('v65535.65535.65535', '65535.65535.65535.0'))) {
    $version = & $script -Tag $case[0]
    Assert-True ($version -eq $case[1]) "Expected $($case[1]) for $($case[0]), got '$version'."
}

foreach ($tag in @('1.0.0', 'v0.1.0', 'v1.0', 'v1.0.0.0', 'v01.0.0', 'v1.00.0',
        'v1.0.0-rc1', 'v65536.0.0', 'v1.0.0 ', 'V1.0.0', 'v-1.0.0')) {
    $rejected = $false
    try { & $script -Tag $tag | Out-Null } catch { $rejected = $true }
    Assert-True $rejected "The tag '$tag' should be rejected."
}
Write-Output 'release_version: all cases passed.'
