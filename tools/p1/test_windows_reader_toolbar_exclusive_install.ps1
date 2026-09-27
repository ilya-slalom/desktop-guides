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
$release = Join-Path $ResultDirectory 'release-timeout-cleanup.txt'
$contenderDirectory = Join-Path $ResultDirectory 'contender'
$resultPath = Join-Path $ResultDirectory 'overlap-result.json'
$firstOutput = Join-Path $ResultDirectory 'first-stdout.txt'
$firstError = Join-Path $ResultDirectory 'first-stderr.txt'
$contenderOutput = Join-Path $ResultDirectory 'contender-stdout.txt'
$contenderError = Join-Path $ResultDirectory 'contender-stderr.txt'
$first = $null
$report = [ordered]@{
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    shellExecutable = $shellExecutable
    contenderRejected = $false
    firstPackagePreserved = $false
    firstProcessPreserved = $false
    firstCleanupVerified = $false
    success = $false
}

try {
    if (Test-Path -LiteralPath $contenderDirectory) {
        throw 'The contender result directory must be fresh.'
    }
    Remove-Item -LiteralPath $marker, $release, $resultPath,
        $firstOutput, $firstError, $contenderOutput, $contenderError,
        (Join-Path $ResultDirectory 'signed-install.json') `
        -ErrorAction SilentlyContinue
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
    $processes = @(Get-Process -Name DesktopGuides.ReaderToolbarSmoke `
        -ErrorAction SilentlyContinue)
    $markerPackage = (Get-Content -LiteralPath $marker -Raw).Trim()
    if ($installed.Count -ne 1 -or
        $installed[0].PackageFullName -ne $markerPackage -or
        $processes.Count -ne 1) {
        throw 'The first toolbar package and process were not installed.'
    }
    $report.firstPackageFullName = $markerPackage
    $report.firstProcessId = $processes[0].Id

    $contenderArguments = '-NoProfile -ExecutionPolicy Bypass -File "' +
        $installer + '" -PackagePath "' + $PackagePath +
        '" -ResultDirectory "' + $contenderDirectory + '"'
    $contender = Start-Process -FilePath $shellExecutable `
        -ArgumentList $contenderArguments -PassThru -Wait -WindowStyle Hidden `
        -RedirectStandardOutput $contenderOutput `
        -RedirectStandardError $contenderError
    $report.contenderExitCode = $contender.ExitCode
    $contenderMessage = @(
        Get-Content -LiteralPath $contenderOutput -Raw
        Get-Content -LiteralPath $contenderError -Raw
    ) -join "`n"
    $contender.Dispose()
    if ($report.contenderExitCode -ne 1 -or
        $contenderMessage -notmatch 'Toolbar install lock is unavailable' -or
        (Test-Path -LiteralPath $contenderDirectory)) {
        throw 'The overlapping toolbar run was not rejected before setup.'
    }
    $report.contenderRejected = $true

    $installedAfter = @(Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke)
    $firstProcess = Get-Process -Id $report.firstProcessId `
        -ErrorAction SilentlyContinue
    $report.firstPackagePreserved = $installedAfter.Count -eq 1 -and
        $installedAfter[0].PackageFullName -eq $markerPackage
    $report.firstProcessPreserved = [bool]$firstProcess
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
        -not $installedResult.taskStillRegistered
    if (-not $report.firstCleanupVerified) {
        throw 'The first toolbar install did not clean up after the overlap.'
    }
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
}
finally {
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
    if (-not $report.success) {
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
