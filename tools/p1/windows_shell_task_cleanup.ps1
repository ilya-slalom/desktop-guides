function Remove-OwnedScheduledTask([string] $TaskName) {
    $task = Get-ScheduledTask -TaskName $TaskName `
        -ErrorAction SilentlyContinue
    if ($task) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false `
            -ErrorAction Stop
    }
}

function Assert-NoOwnedScheduledTasks([string[]] $TaskNames) {
    $remaining = @(Get-ScheduledTask -ErrorAction Stop | Where-Object {
        $TaskNames -contains $_.TaskName
    })
    if ($remaining.Count -gt 0) {
        throw "Test-owned scheduled tasks remain: $(
            ($remaining | ForEach-Object { $_.TaskName }) -join ', ')."
    }
}
