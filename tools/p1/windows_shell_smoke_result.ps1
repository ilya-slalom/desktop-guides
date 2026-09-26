function Clear-ShellSmokeResult([string] $ResultPath) {
    if (Test-Path -LiteralPath $ResultPath) {
        Remove-Item -LiteralPath $ResultPath -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $ResultPath) {
        throw "Old shell smoke result remains at $ResultPath."
    }
}

function Read-ShellSmokeResult(
    [string] $ResultPath,
    [string] $InvocationId,
    [string] $Mode,
    [int] $ProcessId,
    [int] $SessionId) {
    $result = Get-Content -LiteralPath $ResultPath -Raw -ErrorAction Stop |
        ConvertFrom-Json -ErrorAction Stop
    if ($result.invocationId -ne $InvocationId -or
        $result.mode -ne $Mode -or
        $result.processId -ne $ProcessId -or
        $result.sessionId -ne $SessionId) {
        throw "Installed $Mode shell smoke result does not belong to this invocation."
    }
    return $result
}
