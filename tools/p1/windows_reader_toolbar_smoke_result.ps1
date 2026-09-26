function Clear-ToolbarSmokeResult([string] $ResultPath) {
    if (Test-Path -LiteralPath $ResultPath) {
        Remove-Item -LiteralPath $ResultPath -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $ResultPath) {
        throw "Old toolbar smoke result remains at $ResultPath."
    }
}

function Read-ToolbarSmokeResult(
    [string] $ResultPath,
    [string] $InvocationId,
    [int] $SessionId) {
    $result = Get-Content -LiteralPath $ResultPath -Raw -ErrorAction Stop |
        ConvertFrom-Json -ErrorAction Stop
    if ($result.invocationId -ne $InvocationId -or
        $result.sessionId -ne $SessionId) {
        throw 'Toolbar smoke result does not belong to this invocation.'
    }
    return $result
}
