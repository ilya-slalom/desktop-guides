function Get-TestOwnedShellProcess([int] $OwnedId, [datetime] $StartedAt) {
    Get-InstalledShellProcesses |
        Where-Object {
            $_.ProcessId -eq $OwnedId -and
            [Math]::Abs(($_.CreationDate.ToUniversalTime() -
                $StartedAt).TotalSeconds) -lt 2
        } |
        Select-Object -First 1
}

function Stop-InstalledShell(
    [switch] $BestEffort,
    [ValidateRange(0, 120)]
    [int] $ExitTimeoutSeconds = 20) {
    $before = $cleanupErrors.Count
    foreach ($ownedId in @($ownedProcesses.Keys)) {
        $exited = $false
        try {
            $startedAt = $ownedProcesses[$ownedId]
            if (Get-TestOwnedShellProcess $ownedId $startedAt) {
                try {
                    Stop-Process -Id $ownedId -Force -ErrorAction Stop
                }
                catch {
                    if (Get-TestOwnedShellProcess $ownedId $startedAt) {
                        throw
                    }
                }
                $deadline = (Get-Date).AddSeconds($ExitTimeoutSeconds)
                while ((Get-TestOwnedShellProcess $ownedId $startedAt) -and
                    (Get-Date) -lt $deadline) {
                    Start-Sleep -Milliseconds 250
                }
                if (Get-TestOwnedShellProcess $ownedId $startedAt) {
                    throw "Timed out waiting for test process $ownedId to exit."
                }
            }
            $exited = $true
        }
        catch {
            $cleanupErrors.Add("Could not stop test process ${ownedId}: $_")
        }
        finally {
            if ($exited) { [void]$ownedProcesses.Remove($ownedId) }
        }
    }
    if (-not $BestEffort -and $cleanupErrors.Count -gt $before) {
        throw 'Could not stop all test-owned shell processes.'
    }
}
