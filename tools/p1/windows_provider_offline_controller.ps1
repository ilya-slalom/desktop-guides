param(
    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory,

    [ValidateRange(1, 60)]
    [int] $TimeoutMinutes = 45,

    # The longest the rule may stay in place, whatever the installer does.
    [ValidateRange(1, 15)]
    [int] $WatchdogMinutes = 10
)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The offline controller must run elevated.'
}
$handshake = Join-Path $ResultDirectory 'offline-handshake'
$finalReport = Join-Path $ResultDirectory 'signed-install.json'
$ruleName = $null
$log = [ordered]@{ observedAt = (Get-Date).ToUniversalTime().ToString('o') }

function Read-Handshake([string] $Name) {
    $path = Join-Path $handshake $Name
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Write-Handshake([string] $Name, $Value) {
    $Value | ConvertTo-Json | Set-Content (Join-Path $handshake $Name) -Encoding UTF8
}

try {
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    $request = $null
    while (-not $request -and (Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $finalReport) { break }
        Start-Sleep -Seconds 1
        $request = Read-Handshake 'request.json'
    }
    if (-not $request) {
        $log.outcome = 'no-request'
        return
    }
    if ($request.runId -notmatch '^[0-9a-f]{32}$' -or
        $request.program -notlike "$env:ProgramFiles\WindowsApps\DesktopGuides.Preview_*_x64__*\DesktopGuides.Production.exe" -or
        -not (Test-Path -LiteralPath $request.program -PathType Leaf)) {
        throw 'The offline request did not name the installed Desktop Guides executable.'
    }
    $ruleName = "DesktopGuides-P1-ProviderOffline-$($request.runId)"
    New-NetFirewallRule -Name $ruleName -DisplayName $ruleName `
        -Direction Outbound -Action Block -Program $request.program -Profile Any | Out-Null
    if (-not (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue)) {
        throw "Firewall rule $ruleName was not created."
    }
    $log.ruleName = $ruleName
    $log.blockedAt = (Get-Date).ToUniversalTime().ToString('o')
    Write-Handshake 'blocked.json' ([ordered]@{ runId = $request.runId; ruleName = $ruleName })

    $watchdog = (Get-Date).AddMinutes($WatchdogMinutes)
    while (-not (Read-Handshake 'done.json') -and (Get-Date) -lt $watchdog) {
        Start-Sleep -Seconds 1
    }
    $log.outcome = if (Read-Handshake 'done.json') { 'done' } else { 'watchdog-expired' }
}
finally {
    if ($ruleName) {
        Remove-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
        $removed = -not (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue)
        $log.ruleRemoved = $removed
        Write-Handshake 'restored.json' ([ordered]@{ runId = $request.runId; removed = $removed })
    }
    $log | ConvertTo-Json | Set-Content (Join-Path $ResultDirectory 'offline-controller.json') -Encoding UTF8
}
if ($ruleName -and -not $log.ruleRemoved) { exit 1 }
