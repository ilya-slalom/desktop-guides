$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_process.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Reset-Case([int] $ObservationsBeforeExit) {
    $script:ownedProcesses =
        [System.Collections.Generic.Dictionary[int,datetime]]::new()
    $script:cleanupErrors = [System.Collections.Generic.List[string]]::new()
    $script:startedAt = (Get-Date).ToUniversalTime()
    $script:ownedProcesses[43210] = $script:startedAt
    $script:observationsRemaining = $ObservationsBeforeExit
    $script:lookups = 0
    $script:stoppedId = $null
}

function Get-InstalledShellProcesses {
    $script:lookups++
    if ($script:observationsRemaining -gt 0) {
        $script:observationsRemaining--
        [pscustomobject]@{
            ProcessId = 43210
            CreationDate = $script:startedAt
        }
    }
}

function Stop-Process {
    [CmdletBinding()]
    param([int] $Id, [switch] $Force)
    $script:stoppedId = $Id
}

Reset-Case 4
Stop-InstalledShell -ExitTimeoutSeconds 3
Assert-True ($stoppedId -eq 43210) 'Delayed-exit case did not request a stop.'
Assert-True ($lookups -ge 4) 'Delayed-exit case did not wait for process exit.'
Assert-True ($ownedProcesses.Count -eq 0) 'Exited process retained ownership.'
Assert-True ($cleanupErrors.Count -eq 0) 'Delayed exit recorded a cleanup error.'

Reset-Case 0
Stop-InstalledShell
Assert-True ($null -eq $stoppedId) 'Already-exited process was stopped again.'
Assert-True ($ownedProcesses.Count -eq 0) 'Already-exited process retained ownership.'
Assert-True ($cleanupErrors.Count -eq 0) 'Already-exited process recorded an error.'

Reset-Case 100
Stop-InstalledShell -BestEffort -ExitTimeoutSeconds 0
Assert-True ($ownedProcesses.ContainsKey(43210)) 'Timed-out process lost ownership.'
Assert-True ($cleanupErrors.Count -eq 1) 'Timeout did not record one cleanup error.'

Write-Output 'Installed-shell process cleanup checks passed.'
