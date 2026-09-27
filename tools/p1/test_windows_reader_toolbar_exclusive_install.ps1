param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory
)

$ErrorActionPreference = 'Stop'
$shellExecutable = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
New-Item -ItemType Directory -Force $ResultDirectory | Out-Null
$ResultDirectory = (Resolve-Path $ResultDirectory).Path
$installer = Join-Path $PSScriptRoot 'windows_reader_toolbar_install.ps1'
$marker = Join-Path $ResultDirectory 'installed-before-timeout.txt'
$processHandoff = Join-Path $ResultDirectory 'toolbar-process.json'
$release = Join-Path $ResultDirectory 'release-timeout-cleanup.txt'
$contenderDirectory = Join-Path $ResultDirectory 'contender'
$resultPath = Join-Path $ResultDirectory 'overlap-result.json'
$firstOutput = Join-Path $ResultDirectory 'first-stdout.txt'
$firstError = Join-Path $ResultDirectory 'first-stderr.txt'
$contenderOutput = Join-Path $ResultDirectory 'contender-stdout.txt'
$contenderError = Join-Path $ResultDirectory 'contender-stderr.txt'
$decoyDirectory = Join-Path $ResultDirectory 'unrelated-process'
$decoyExecutable = Join-Path $decoyDirectory `
    'DesktopGuides.ReaderToolbarSmoke.exe'
$first = $null
$contender = $null
$firstApp = $null
$decoy = $null
if (-not ('DesktopGuidesOwnedProcess' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'windows_owned_process.cs')
}
$report = [ordered]@{
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    shellExecutable = $shellExecutable
    contenderRejected = $false
    firstPackagePreserved = $false
    firstProcessPreserved = $false
    firstCleanupVerified = $false
    unrelatedProcessPresentBeforeInstall = $false
    unrelatedProcessPreserved = $false
    success = $false
}

try {
    if (Test-Path -LiteralPath $contenderDirectory) {
        throw 'The contender result directory must be fresh.'
    }
    if (Test-Path -LiteralPath $decoyDirectory) {
        throw 'The unrelated-process directory must be fresh.'
    }
    Remove-Item -LiteralPath $marker, $release, $resultPath,
        $firstOutput, $firstError, $contenderOutput, $contenderError,
        (Join-Path $ResultDirectory 'signed-install.json') `
        -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $decoyDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $env:WINDIR 'System32\cmd.exe') `
        -Destination $decoyExecutable
    $decoy = Start-Process -FilePath $decoyExecutable `
        -ArgumentList '/c ping -n 300 127.0.0.1 > nul' `
        -PassThru -WindowStyle Hidden
    $report.unrelatedProcessPresentBeforeInstall =
        -not $decoy.HasExited -and
        $decoy.ProcessName -eq 'DesktopGuides.ReaderToolbarSmoke'
    if (-not $report.unrelatedProcessPresentBeforeInstall) {
        throw 'The unrelated same-name process did not start.'
    }

    $firstArguments = '-NoProfile -ExecutionPolicy Bypass -File "' +
        $installer + '" -PackagePath "' + $PackagePath +
        '" -ResultDirectory "' + $ResultDirectory +
        '" -SimulateSmokeTimeoutAfterInstall' +
        ' -SimulateProcessExitDuringInspection' +
        ' -PauseTimeoutCleanupUntilPath "' + $release + '"'
    $first = Start-Process -FilePath $shellExecutable `
        -ArgumentList $firstArguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $firstOutput -RedirectStandardError $firstError

    $deadline = (Get-Date).AddSeconds(90)
    do {
        $first.Refresh()
        if ($first.HasExited) {
            throw 'First toolbar install exited before its hold.'
        }
        if (Test-Path -LiteralPath $marker) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    if (-not (Test-Path -LiteralPath $marker)) {
        throw 'First toolbar install did not reach its hold.'
    }

    $installed = @(Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke)
    $markerPackage = (Get-Content -LiteralPath $marker -Raw).Trim()
    if ($installed.Count -ne 1 -or
        $installed[0].PackageFullName -ne $markerPackage) {
        throw 'The first toolbar package was not installed.'
    }
    $report.firstPackageFullName = $markerPackage
    $handoff = Get-Content -LiteralPath $processHandoff -Raw |
        ConvertFrom-Json
    if ($handoff.packageFullName -ne $markerPackage) {
        throw 'The first toolbar process handoff did not match its app.'
    }
    $expectedExecutable = Join-Path $installed[0].InstallLocation `
        'DesktopGuides.ReaderToolbarSmoke.exe'
    $firstApp = [DesktopGuidesOwnedProcess]::OpenVerified(
        [int]$handoff.processId, [datetime]$handoff.startedAt,
        [int]$handoff.sessionId, $expectedExecutable)
    if (-not $firstApp) {
        throw 'The first toolbar app exited before the overlap check.'
    }
    $report.firstProcessId = [int]$handoff.processId

    $contenderArguments = '-NoProfile -ExecutionPolicy Bypass -File "' +
        $installer + '" -PackagePath "' + $PackagePath +
        '" -ResultDirectory "' + $contenderDirectory + '"'
    $contenderStart = [System.Diagnostics.ProcessStartInfo]::new()
    $contenderStart.FileName = $shellExecutable
    $contenderStart.Arguments = $contenderArguments
    $contenderStart.UseShellExecute = $false
    $contenderStart.CreateNoWindow = $true
    $contenderStart.RedirectStandardOutput = $true
    $contenderStart.RedirectStandardError = $true
    $contender = [System.Diagnostics.Process]::new()
    $contender.StartInfo = $contenderStart
    if (-not $contender.Start()) {
        throw 'The overlapping toolbar contender did not start.'
    }
    $stdoutRead = $contender.StandardOutput.ReadToEndAsync()
    $stderrRead = $contender.StandardError.ReadToEndAsync()
    if (-not $contender.WaitForExit(20000)) {
        throw 'The overlapping toolbar contender did not exit in 20 seconds.'
    }
    if (-not $stdoutRead.Wait(5000) -or -not $stderrRead.Wait(5000)) {
        throw 'The overlapping toolbar contender output did not drain.'
    }
    $report.contenderExitCode = $contender.ExitCode
    $contenderStdout = $stdoutRead.Result
    $contenderStderr = $stderrRead.Result
    $contenderMessage = @($contenderStdout, $contenderStderr) -join "`n"
    Set-Content -LiteralPath $contenderOutput -Value $contenderStdout `
        -Encoding UTF8
    Set-Content -LiteralPath $contenderError -Value $contenderStderr `
        -Encoding UTF8
    if ($report.contenderExitCode -ne 1 -or
        $contenderMessage -notmatch 'Toolbar install lock is unavailable' -or
        (Test-Path -LiteralPath $contenderDirectory)) {
        throw 'The overlapping toolbar run was not rejected before setup.'
    }
    $report.contenderRejected = $true

    $installedAfter = @(Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke)
    $report.firstPackagePreserved = $installedAfter.Count -eq 1 -and
        $installedAfter[0].PackageFullName -eq $markerPackage
    $report.firstProcessPreserved = -not $firstApp.HasExited
    if (-not $report.firstPackagePreserved -or
        -not $report.firstProcessPreserved) {
        throw 'The overlapping run changed the first package or process.'
    }

    Set-Content -LiteralPath $release -Value 'continue' -Encoding ASCII
    if (-not $first.WaitForExit(90000)) {
        throw 'First toolbar install did not clean up after release.'
    }
    $installedResult = Get-Content -LiteralPath `
        (Join-Path $ResultDirectory 'signed-install.json') -Raw |
        ConvertFrom-Json
    $report.firstCleanupVerified =
        -not $installedResult.success -and
        $installedResult.simulatedProcessExit -and
        $installedResult.parentStoppedProcess -and
        $installedResult.parentRemovedPackage -and
        $installedResult.processCleanupError -match
            'Simulated process exit during module inspection' -and
        $installedResult.error -match
            'Simulated toolbar smoke timeout after installation' -and
        -not $installedResult.packageStillInstalled -and
        -not $installedResult.processStillRunning -and
        -not $installedResult.certificateStillTrusted -and
        -not $installedResult.taskStillRegistered -and
        $installedResult.installReceiptVerified -and
        $installedResult.processHandleAcquired -and $firstApp.HasExited
    if (-not $report.firstCleanupVerified) {
        throw 'The first toolbar install did not clean up after the overlap.'
    }
    $decoy.Refresh()
    $report.unrelatedProcessPreserved = -not $decoy.HasExited
    if (-not $report.unrelatedProcessPreserved) {
        throw 'Toolbar cleanup stopped the unrelated same-name process.'
    }
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
}
finally {
    if ($contender) {
        try {
            $contender.Refresh()
            if (-not $contender.HasExited) {
                $contender.Kill()
                if (-not $contender.WaitForExit(10000)) {
                    throw 'The overlapping toolbar contender did not stop.'
                }
            }
        }
        catch {
            $report.contenderCleanupError = $_ | Out-String
            $report.success = $false
        }
        $contender.Dispose()
    }
    if ($first) {
        try {
            Set-Content -LiteralPath $release -Value 'continue' -Encoding ASCII
            if (-not $first.WaitForExit(90000)) {
                $report.releaseError = 'First toolbar installer is still running.'
                $report.success = $false
            }
        }
        catch {
            $report.releaseError = $_ | Out-String
            $report.success = $false
        }
        $first.Dispose()
    }
    if ($firstApp) { $firstApp.Dispose() }
    if ($decoy) {
        try {
            $decoy.Refresh()
            if (-not $decoy.HasExited) {
                $decoy.Kill()
                if (-not $decoy.WaitForExit(10000)) {
                    throw 'The unrelated same-name process did not exit.'
                }
            }
        }
        catch {
            $report.unrelatedProcessCleanupError = $_ | Out-String
            $report.success = $false
        }
        $decoy.Dispose()
    }
    Remove-Item -LiteralPath $decoyDirectory -Recurse -Force `
        -ErrorAction SilentlyContinue
    if (-not $report.success) {
        if (Test-Path -LiteralPath $contenderOutput) {
            $report.contenderStdout =
                Get-Content -LiteralPath $contenderOutput -Raw
        }
        if (Test-Path -LiteralPath $contenderError) {
            $report.contenderStderr =
                Get-Content -LiteralPath $contenderError -Raw
        }
        if (Test-Path -LiteralPath $firstOutput) {
            $report.firstStdout = Get-Content -LiteralPath $firstOutput -Raw
        }
        if (Test-Path -LiteralPath $firstError) {
            $report.firstStderr = Get-Content -LiteralPath $firstError -Raw
        }
        $firstInstallPath = Join-Path $ResultDirectory 'signed-install.json'
        if (Test-Path -LiteralPath $firstInstallPath) {
            $firstInstall = Get-Content -LiteralPath $firstInstallPath -Raw |
                ConvertFrom-Json
            $report.firstInstallError = $firstInstall.error
        }
    }
    $report | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath $resultPath -Encoding UTF8
}
if (-not $report.success) { exit 1 }
