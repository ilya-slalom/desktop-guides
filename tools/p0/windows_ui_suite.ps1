param(
    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory,

    [string[]] $FixtureIds = @(
        'txt-utf8', 'txt-bom', 'txt-legacy', 'txt-ascii', 'txt-long',
        'html-static', 'html-layout', 'html-hostile', 'redirect',
        'pdf-short', 'pdf-long', 'pdf-access', 'pdf-scan', 'pdf-locked'
    )
)

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'windows_ui_smoke.ps1'
New-Item -ItemType Directory -Force $ResultDirectory | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
foreach ($id in $FixtureIds) {
    $result = Join-Path $ResultDirectory ($id + '.json')
    Remove-Item $result,($result + '.error.txt') -ErrorAction SilentlyContinue
    & powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden `
        -ExecutionPolicy Bypass -File $script -FixtureId $id -ResultPath $result
    $results.Add([ordered]@{
        fixture = $id
        passed = ($LASTEXITCODE -eq 0 -and (Test-Path $result))
        result = $result
    })
}
$summary = Join-Path $ResultDirectory 'suite.json'
$results | ConvertTo-Json -Depth 4 | Set-Content $summary -Encoding UTF8
if (@($results | Where-Object { -not $_.passed }).Count -gt 0) {
    exit 1
}
