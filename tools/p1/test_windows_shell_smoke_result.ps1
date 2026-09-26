$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_smoke_result.ps1')

$root = Join-Path $env:TEMP `
    "DesktopGuides-SmokeResult-$([Guid]::NewGuid().ToString('N'))"
$resultPath = Join-Path $root 'normal.json'
$invocationId = [Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $root | Out-Null
try {
    '{"success":true}' | Set-Content -LiteralPath $resultPath
    (Get-Item -LiteralPath $resultPath).IsReadOnly = $true
    $rejected = $false
    try {
        Clear-ShellSmokeResult $resultPath
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected -or -not (Test-Path -LiteralPath $resultPath)) {
        throw 'Read-only stale result did not fail closed.'
    }

    (Get-Item -LiteralPath $resultPath).IsReadOnly = $false
    Clear-ShellSmokeResult $resultPath
    if (Test-Path -LiteralPath $resultPath) {
        throw 'Writable stale result was not cleared.'
    }

    @{
        invocationId = [Guid]::NewGuid().ToString('N')
        mode = 'normal'
        processId = 123
        sessionId = 2
        success = $true
    } | ConvertTo-Json | Set-Content -LiteralPath $resultPath
    $rejected = $false
    try {
        [void](Read-ShellSmokeResult $resultPath $invocationId 'normal' 123 2)
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw 'Old successful invocation was accepted.'
    }

    @{
        invocationId = $invocationId
        mode = 'normal'
        processId = 123
        sessionId = 2
        success = $true
    } | ConvertTo-Json | Set-Content -LiteralPath $resultPath
    [void](Read-ShellSmokeResult $resultPath $invocationId 'normal' 123 2)
    $rejected = $false
    try {
        [void](Read-ShellSmokeResult $resultPath $invocationId 'normal' 456 2)
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw 'Result for a different shell process was accepted.'
    }
    Write-Output 'Smoke result checks passed.'
}
finally {
    if (Test-Path -LiteralPath $resultPath) {
        (Get-Item -LiteralPath $resultPath).IsReadOnly = $false
    }
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
