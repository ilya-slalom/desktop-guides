param(
    [Parameter(Mandatory = $true)]
    [string] $ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath,

    [string] $ExecutableArguments = '',

    [ValidateRange(0, 2147483647)]
    [int] $ForegroundTargetProcessId = 0,

    [ValidateRange(1, 120)]
    [int] $HandoffTimeoutSeconds = 45
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if (-not ('DesktopGuidesOwnedProcess' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'windows_owned_process.cs')
}
$process = $null
$handedOff = $false
$errorText = $null
$token = [Guid]::NewGuid().ToString('N')
$foregroundProbe = $null
$foregroundTargetHandle = [IntPtr]::Zero
$foregroundBefore = [IntPtr]::Zero
$report = [ordered]@{
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    executablePath = $ExecutablePath
    handoffToken = $token
    success = $false
}

try {
    if ($ForegroundTargetProcessId -gt 0) {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing
        Add-Type -Path (Join-Path $PSScriptRoot 'windows_shell_foreground_probe.cs')
        $targetProcess = Get-Process -Id $ForegroundTargetProcessId `
            -ErrorAction Stop
        $targetProcess.Refresh()
        $foregroundTargetHandle = $targetProcess.MainWindowHandle
        if ($foregroundTargetHandle -eq [IntPtr]::Zero) {
            throw 'Foreground target has no window.'
        }
        $foregroundProbe = [System.Windows.Forms.Form]::new()
        $foregroundProbe.Text = 'Desktop Guides launch focus probe'
        $foregroundProbe.StartPosition =
            [System.Windows.Forms.FormStartPosition]::Manual
        $foregroundProbe.Location = [System.Drawing.Point]::new(100, 100)
        $foregroundProbe.Size = [System.Drawing.Size]::new(270, 180)
        $foregroundProbe.TopMost = $true
        $foregroundProbe.Show()
        [System.Windows.Forms.Application]::DoEvents()
        $point = $foregroundProbe.PointToScreen(
            [System.Drawing.Point]::new(130, 90))
        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            [DesktopGuidesForegroundProbe]::Click($point.X, $point.Y)
            [System.Windows.Forms.Application]::DoEvents()
            if ([DesktopGuidesForegroundProbe]::GetForegroundWindow() -eq
                $foregroundProbe.Handle) {
                break
            }
            Start-Sleep -Milliseconds 200
        }
        $foregroundProbe.TopMost = $false
        [System.Windows.Forms.Application]::DoEvents()
        if ([DesktopGuidesForegroundProbe]::GetForegroundWindow() -ne
            $foregroundProbe.Handle) {
            throw 'Foreground probe did not receive input focus.'
        }
        $foregroundBefore =
            [DesktopGuidesForegroundProbe]::GetForegroundWindow()
    }

    $startArguments = @{
        FilePath = $ExecutablePath
        PassThru = $true
    }
    if ($ExecutableArguments) {
        $startArguments.ArgumentList = $ExecutableArguments
    }
    $process = Start-Process @startArguments
    $report.processId = $process.Id
    $report.sessionId = $process.SessionId
    $report.startedAt = $process.StartTime.ToUniversalTime().ToString('o')
    $report.success = $true

    $temporaryPath = "$ResultPath.tmp"
    $report | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $temporaryPath -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $ResultPath -Force

    $ackPath = "$ResultPath.ack"
    $deadline = (Get-Date).AddSeconds($HandoffTimeoutSeconds)
    do {
        if ($process.HasExited) {
            $handedOff = $true
            break
        }
        if (Test-Path -LiteralPath $ackPath) {
            $ack = Get-Content -LiteralPath $ackPath -Raw |
                ConvertFrom-Json
            if ($ack.handoffToken -eq $token) {
                $handedOff = $true
                break
            }
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    if (-not $handedOff) {
        throw 'Interactive launch ownership handoff timed out.'
    }
    if ($foregroundProbe) {
        $childExited = $process.WaitForExit(8000)
        $foregroundAfter =
            [DesktopGuidesForegroundProbe]::GetForegroundWindow()
        $focusReport = [ordered]@{
            handoffToken = $token
            foregroundTargetProcessId = $ForegroundTargetProcessId
            probeWindowHandle = $foregroundProbe.Handle.ToInt64()
            foregroundWindowBefore = $foregroundBefore.ToInt64()
            targetWindowHandle = $foregroundTargetHandle.ToInt64()
            foregroundWindowHandle = $foregroundAfter.ToInt64()
            childExited = $childExited
            success = $childExited -and
                $foregroundAfter -eq $foregroundTargetHandle
        }
        $focusResultPath = "$ResultPath.foreground.json"
        $focusReport | ConvertTo-Json -Depth 3 |
            Set-Content -LiteralPath "$focusResultPath.tmp" -Encoding UTF8
        Move-Item -LiteralPath "$focusResultPath.tmp" `
            -Destination $focusResultPath -Force
    }
}
catch {
    $errorText = $_ | Out-String
    if (-not $process) {
        try {
            $report.error = $errorText
            $temporaryPath = "$ResultPath.tmp"
            $report | ConvertTo-Json -Depth 4 |
                Set-Content -LiteralPath $temporaryPath -Encoding UTF8
            Move-Item -LiteralPath $temporaryPath -Destination $ResultPath -Force
        }
        catch {
            $errorText += ($_ | Out-String)
        }
    }
}
finally {
    if ($foregroundProbe) {
        $foregroundProbe.Dispose()
    }
    if ($process) {
        if (-not $handedOff) {
            try {
                if (-not [DesktopGuidesOwnedProcess]::TerminateAndWait(
                    $process.Handle, 20000)) {
                    throw 'Timed out stopping unclaimed launched process.'
                }
            }
            catch {
                $errorText += ($_ | Out-String)
            }
        }
        $process.Dispose()
    }
}
if ($errorText) {
    Write-Error $errorText
    exit 1
}
