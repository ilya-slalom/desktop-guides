$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_task_cleanup.ps1')

$taskName = "DesktopGuides-CleanupCheck-$([Guid]::NewGuid().ToString('N'))"
$action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c exit 0'
Register-ScheduledTask -TaskName $taskName -Action $action -Force |
    Out-Null
try {
    $detected = $false
    try {
        Assert-NoOwnedScheduledTasks @($taskName)
    }
    catch {
        $detected = $true
    }
    if (-not $detected) {
        throw 'Retained test-owned scheduled task was not reported.'
    }
    Remove-OwnedScheduledTask $taskName
    Assert-NoOwnedScheduledTasks @($taskName)
    Write-Output 'Scheduled task cleanup checks passed.'
}
finally {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false `
        -ErrorAction SilentlyContinue
}
