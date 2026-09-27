$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_reader_toolbar_smoke_result.ps1')

$root = Join-Path $env:TEMP `
    "DesktopGuides-ToolbarResult-$([Guid]::NewGuid().ToString('N'))"
$resultPath = Join-Path $root 'toolbar-ui.json'
$invocationId = [Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $root | Out-Null
try {
    @{
        invocationId = [Guid]::NewGuid().ToString('N')
        sessionId = 2
        success = $true
    } | ConvertTo-Json | Set-Content -LiteralPath $resultPath
    (Get-Item -LiteralPath $resultPath).IsReadOnly = $true
    $rejected = $false
    try {
        Clear-ToolbarSmokeResult $resultPath
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected -or -not (Test-Path -LiteralPath $resultPath)) {
        throw 'Read-only stale toolbar result did not fail closed.'
    }

    (Get-Item -LiteralPath $resultPath).IsReadOnly = $false
    $rejected = $false
    try {
        [void](Read-ToolbarSmokeResult $resultPath $invocationId 2)
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw 'Old successful toolbar invocation was accepted.'
    }

    Clear-ToolbarSmokeResult $resultPath
    if (Test-Path -LiteralPath $resultPath) {
        throw 'Writable stale toolbar result was not cleared.'
    }

    @{
        invocationId = $invocationId
        sessionId = 2
        success = $true
    } | ConvertTo-Json | Set-Content -LiteralPath $resultPath
    [void](Read-ToolbarSmokeResult $resultPath $invocationId 2)
    $rejected = $false
    try {
        [void](Read-ToolbarSmokeResult $resultPath $invocationId 3)
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw 'Toolbar result for another desktop session was accepted.'
    }
    Write-Output 'Toolbar smoke result checks passed.'
}
finally {
    if (Test-Path -LiteralPath $resultPath) {
        (Get-Item -LiteralPath $resultPath).IsReadOnly = $false
    }
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
