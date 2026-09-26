if (-not ('DesktopGuidesOwnedProcess' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'windows_owned_process.cs')
}

function Stop-OwnedShellHandle(
    [DesktopGuidesOwnedProcess] $Owned,
    [int] $TimeoutMilliseconds) {
    return $Owned.TerminateAndWait($TimeoutMilliseconds)
}

function Stop-InstalledShell(
    [switch] $BestEffort,
    [ValidateRange(0, 120)]
    [int] $ExitTimeoutSeconds = 20) {
    $before = $cleanupErrors.Count
    foreach ($ownedId in @($ownedProcesses.Keys)) {
        $exited = $false
        $owned = $ownedProcesses[$ownedId]
        try {
            if (-not (Stop-OwnedShellHandle $owned ($ExitTimeoutSeconds * 1000))) {
                throw "Timed out waiting for test process $ownedId to exit."
            }
            $exited = $true
        }
        catch {
            $cleanupErrors.Add("Could not stop test process ${ownedId}: $_")
        }
        finally {
            if ($exited) {
                $owned.Dispose()
                [void]$ownedProcesses.Remove($ownedId)
            }
        }
    }
    if (-not $BestEffort -and $cleanupErrors.Count -gt $before) {
        throw 'Could not stop all test-owned shell processes.'
    }
}
