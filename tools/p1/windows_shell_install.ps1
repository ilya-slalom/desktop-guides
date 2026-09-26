param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$targetSessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
if ($targetSessionId -eq 0 -or
    -not @(Get-Process explorer -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $targetSessionId })) {
    throw 'Shell install test requires an interactive desktop session.'
}
$packageName = Split-Path $PackagePath -Leaf
if ($packageName -notmatch '^DesktopGuides\.Production_[0-9]+(\.[0-9]+){3}_x64\.msix$') {
    throw "Expected an x64 production MSIX, got $packageName."
}
if (Get-AppxPackage -Name DesktopGuides.Preview) {
    throw 'A production preview package is already installed; refusing to replace it.'
}

New-Item -ItemType Directory -Force $ResultDirectory | Out-Null
$ResultDirectory = (Resolve-Path $ResultDirectory).Path
$signed = Join-Path $ResultDirectory 'desktop-guides-production-signed-x64.msix'
$public = Join-Path $ResultDirectory 'test-certificate.cer'
$runId = [Guid]::NewGuid().ToString('N')
$report = [ordered]@{
    runId = $runId
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    osBuild = [Environment]::OSVersion.Version.ToString()
    cpuArchitecture = $env:PROCESSOR_ARCHITECTURE
    success = $false
}
$certificate = $null
$installed = $null
$expectedExecutablePath = $null
$imported = $false
$ownedProcesses = [System.Collections.Generic.Dictionary[int,datetime]]::new()
$cleanupErrors = [System.Collections.Generic.List[string]]::new()
$launchTask = "DesktopGuides-P1-ShellLaunch-$runId"
$secondLaunchTask = "DesktopGuides-P1-ShellSecondLaunch-$runId"
$smokeTask = "DesktopGuides-P1-ShellSmoke-$runId"
$launchResultPath = Join-Path $ResultDirectory 'launch.json'
$secondLaunchResultPath = Join-Path $ResultDirectory 'second-launch.json'

function Get-InstalledShellProcesses {
    if (-not $installed) { return @() }
    Get-CimInstance Win32_Process -Filter "Name = 'DesktopGuides.Production.exe'" |
        Where-Object {
            $_.SessionId -eq $targetSessionId -and
            [string]::Equals($_.ExecutablePath, $expectedExecutablePath,
                [StringComparison]::OrdinalIgnoreCase)
        }
}

function Stop-InstalledShell([switch] $BestEffort) {
    $before = $cleanupErrors.Count
    foreach ($ownedId in @($ownedProcesses.Keys)) {
        try {
            $process = Get-InstalledShellProcesses |
                Where-Object { $_.ProcessId -eq $ownedId } |
                Select-Object -First 1
            if ($process -and
                [Math]::Abs(($process.CreationDate.ToUniversalTime() -
                    $ownedProcesses[$ownedId]).TotalSeconds) -lt 2) {
                try {
                    Stop-Process -Id $ownedId -Force -ErrorAction Stop
                }
                catch {
                    if (Get-Process -Id $ownedId -ErrorAction SilentlyContinue) {
                        throw
                    }
                }
            }
        }
        catch {
            $cleanupErrors.Add("Could not stop test process ${ownedId}: $_")
        }
        finally {
            [void]$ownedProcesses.Remove($ownedId)
        }
    }
    if (-not $BestEffort -and $cleanupErrors.Count -gt $before) {
        throw 'Could not stop all test-owned shell processes.'
    }
}

function Wait-LaunchResult([string] $ResultPath) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
    } while (-not (Test-Path $ResultPath) -and (Get-Date) -lt $deadline)
    if (-not (Test-Path $ResultPath)) {
        throw "Interactive launch did not write $ResultPath."
    }
    $launch = Get-Content $ResultPath -Raw | ConvertFrom-Json
    if (-not $launch.success) {
        throw "Interactive launch failed: $($launch.error)"
    }
    if ($launch.sessionId -ne $targetSessionId -or
        -not [string]::Equals($launch.executablePath, $expectedExecutablePath,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Interactive launch returned an unexpected session or executable.'
    }
    $ownedProcesses[[int]$launch.processId] =
        ([datetime]$launch.startedAt).ToUniversalTime()
    return $launch
}

function Wait-InstalledShellWindow([int] $ShellProcessId) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $appProcess = Get-InstalledShellProcesses |
            Where-Object { $_.ProcessId -eq $ShellProcessId } |
            Select-Object -First 1
        if ($appProcess) {
            $windowProcess = Get-Process -Id $ShellProcessId `
                -ErrorAction SilentlyContinue
            if ($windowProcess -and $windowProcess.MainWindowHandle -ne 0) {
                break
            }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if (-not $appProcess -or -not $windowProcess -or
        $windowProcess.MainWindowHandle -eq 0) {
        throw "Test-launched shell process $ShellProcessId did not open a window."
    }
    $report.launchedProcessId = $appProcess.ProcessId
    $report.launchedSessionId = $appProcess.SessionId
}

function Start-InstalledShell {
    Remove-Item $launchResultPath -ErrorAction SilentlyContinue
    Start-ScheduledTask -TaskName $launchTask
    $launch = Wait-LaunchResult $launchResultPath
    Wait-InstalledShellWindow ([int]$launch.processId)
}

function New-ShellLaunchAction([string] $ResultPath) {
    $script = Join-Path $PSScriptRoot 'windows_shell_launch.ps1'
    $arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass ' +
        '-File "' + $script + '"' +
        ' -ExecutablePath "' + $expectedExecutablePath + '"' +
        ' -ResultPath "' + $ResultPath + '"'
    New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument $arguments -WorkingDirectory $PSScriptRoot
}

function Assert-SingleInstance {
    $firstProcessId = $report.launchedProcessId
    $activationEvent = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::AutoReset,
        'Local\DesktopGuides.Preview.RedirectedActivation')
    try {
        $activationEvent.Reset() | Out-Null
        Register-ScheduledTask -TaskName $secondLaunchTask -Action $secondLaunchAction `
            -Principal $principal -Force | Out-Null
        Remove-Item $secondLaunchResultPath -ErrorAction SilentlyContinue
        Start-ScheduledTask -TaskName $secondLaunchTask
        $secondLaunch = Wait-LaunchResult $secondLaunchResultPath
        $report.secondLaunchProcessId = $secondLaunch.processId
        if (-not $activationEvent.WaitOne(15000)) {
            throw 'Original shell did not acknowledge redirected activation.'
        }
        $report.redirectedActivationObserved = $true
    }
    finally {
        $activationEvent.Dispose()
    }
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $processes = @(Get-InstalledShellProcesses)
        if ($processes.Count -eq 1 -and
            $processes[0].ProcessId -eq $firstProcessId) {
            break
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    $report.secondLaunchProcessIds = @(
        $processes | ForEach-Object { $_.ProcessId })
    if ($processes.Count -ne 1 -or $processes[0].ProcessId -ne $firstProcessId) {
        throw "Second launch did not settle on original process $firstProcessId; found $($report.secondLaunchProcessIds -join ', ')."
    }
    $report.singleInstanceProcessId = $processes[0].ProcessId
}

function Request-InstalledShellClose([int] $ShellProcessId) {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $process = Get-Process -Id $ShellProcessId -ErrorAction Stop
    if ($process.MainWindowHandle -eq 0) {
        throw 'Installed production shell has no window to close.'
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle(
        $process.MainWindowHandle)
    $pattern = $window.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern)
    $pattern.Close()
}

function Wait-InstalledShellExit([int] $ShellProcessId) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
    } while ((Get-Process -Id $ShellProcessId -ErrorAction SilentlyContinue) -and
        (Get-Date) -lt $deadline)
    if (Get-Process -Id $ShellProcessId -ErrorAction SilentlyContinue) {
        throw 'Production shell did not exit after a normal window close.'
    }
}

function Close-InstalledShell {
    Request-InstalledShellClose $report.launchedProcessId
    Wait-InstalledShellExit $report.launchedProcessId
    $report.closedGracefully = $true
}

function Assert-RelaunchDuringClose {
    $closingProcessId = $report.launchedProcessId
    $lockReady = Join-Path $ResultDirectory 'write-lock-ready'
    $lockRelease = Join-Path $ResultDirectory 'write-lock-release'
    Remove-Item $lockReady, $lockRelease -ErrorAction SilentlyContinue
    $seedDll = Join-Path $PSScriptRoot `
        'DesktopGuides.ShellSeed\bin\Release\net10.0\DesktopGuides.ShellSeed.dll'
    if (-not (Test-Path $seedDll)) {
        throw 'Shell seed executable is unavailable for the close handoff.'
    }
    $lockArguments = @(
        ('"{0}"' -f $seedDll), 'hold-write-lock', ('"{0}"' -f $dataRoot),
        ('"{0}"' -f $lockReady), ('"{0}"' -f $lockRelease)
    )
    $lockProcess = Start-Process -FilePath 'dotnet.exe' `
        -ArgumentList $lockArguments -PassThru -WindowStyle Hidden
    try {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 100
            if ($lockProcess.HasExited) {
                throw "Write-lock helper exited early: $($lockProcess.ExitCode)."
            }
        } while (-not (Test-Path $lockReady) -and (Get-Date) -lt $deadline)
        if (-not (Test-Path $lockReady)) {
            throw 'Write-lock helper did not acquire the database.'
        }
        $report.pendingGuide = Run-ShellSmoke 'queue-guide'
        Request-InstalledShellClose $closingProcessId
        Start-Sleep -Milliseconds 300
        if (-not (Get-Process -Id $closingProcessId -ErrorAction SilentlyContinue)) {
            throw 'Closing shell exited before pending guide action drained.'
        }
        Start-InstalledShell
        $report.closeHandoffOverlapObserved = [bool](
            Get-Process -Id $closingProcessId -ErrorAction SilentlyContinue)
        if (-not $report.closeHandoffOverlapObserved) {
            throw 'New shell opened only after the old shell exited.'
        }
        $report.waitingDuringClose = Run-ShellSmoke 'waiting-handoff'
    }
    finally {
        New-Item -ItemType File -Force $lockRelease | Out-Null
        if (-not $lockProcess.WaitForExit(10000)) {
            Stop-Process -Id $lockProcess.Id -Force
            $lockProcess.WaitForExit()
        }
    }
    if ($lockProcess.ExitCode -ne 0) {
        throw "Write-lock helper failed: $($lockProcess.ExitCode)."
    }
    Wait-InstalledShellExit $closingProcessId
    $report.relaunchDuringCloseProcessId = $report.launchedProcessId
    $report.normalAfterCloseRelaunch = Run-ShellSmoke 'normal' 'Blocked Write Guide'
}

function Assert-ClosingTargetRedirect {
    $closingProcessId = $report.launchedProcessId
    $selected = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::AutoReset,
        'Local\DesktopGuides.Preview.RedirectSelected')
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        'Local\DesktopGuides.Preview.RedirectContinue')
    try {
        $selected.Reset() | Out-Null
        $resume.Reset() | Out-Null
        Remove-Item $secondLaunchResultPath -ErrorAction SilentlyContinue
        Start-ScheduledTask -TaskName $secondLaunchTask
        $secondLaunch = Wait-LaunchResult $secondLaunchResultPath
        if (-not $selected.WaitOne(15000)) {
            throw 'Second launch did not select the closing target.'
        }
        $report.closingTargetSelected = $true
        Request-InstalledShellClose $closingProcessId
        Wait-InstalledShellExit $closingProcessId
        $resume.Set() | Out-Null
        Wait-InstalledShellWindow ([int]$secondLaunch.processId)
        $report.closingTargetRelaunchProcessId = $report.launchedProcessId
        $report.normalAfterClosingTarget = Run-ShellSmoke 'normal'
    }
    finally {
        $resume.Set() | Out-Null
        $resume.Dispose()
        $selected.Dispose()
    }
}

function Assert-QueuedActivationClose {
    $closingProcessId = $report.launchedProcessId
    $queued = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::AutoReset,
        'Local\DesktopGuides.Preview.ActivationQueued')
    $resume = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        'Local\DesktopGuides.Preview.ActivationContinue')
    try {
        $queued.Reset() | Out-Null
        $resume.Reset() | Out-Null
        Remove-Item $secondLaunchResultPath -ErrorAction SilentlyContinue
        Start-ScheduledTask -TaskName $secondLaunchTask
        $secondLaunch = Wait-LaunchResult $secondLaunchResultPath
        if (-not $queued.WaitOne(15000)) {
            throw 'Original shell did not queue redirected activation.'
        }
        $report.activationQueuedBeforeClose = $true
        Request-InstalledShellClose $closingProcessId
        Wait-InstalledShellExit $closingProcessId
        $resume.Set() | Out-Null
        Wait-InstalledShellWindow ([int]$secondLaunch.processId)
        $report.queuedActivationRelaunchProcessId = $report.launchedProcessId
        $report.normalAfterQueuedActivation = Run-ShellSmoke 'normal'
    }
    finally {
        $resume.Set() | Out-Null
        $resume.Dispose()
        $queued.Dispose()
    }
}

function Run-ShellSmoke(
    [string] $mode,
    [string] $expectedResumeGuide = 'Route Test Guide') {
    $resultPath = Join-Path $ResultDirectory "$mode.json"
    Remove-Item $resultPath -ErrorAction SilentlyContinue
    $script = Join-Path $PSScriptRoot 'windows_shell_ui_smoke.ps1'
    $arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass ' +
        '-File "' + $script + '" -Mode ' + $mode +
        ' -ResultPath "' + $resultPath + '"' +
        ' -ProcessId ' + $report.launchedProcessId +
        ' -SessionId ' + $targetSessionId +
        ' -ExecutablePath "' + $expectedExecutablePath + '"' +
        ' -ExpectedResumeGuide "' + $expectedResumeGuide + '"'
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument $arguments -WorkingDirectory $PSScriptRoot
    Register-ScheduledTask -TaskName $smokeTask -Action $action `
        -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName $smokeTask
    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 500
    } while (-not (Test-Path $resultPath) -and (Get-Date) -lt $deadline)
    if (-not (Test-Path $resultPath)) {
        throw "Installed $mode shell smoke timed out."
    }
    Start-Sleep -Milliseconds 250
    $result = Get-Content $resultPath -Raw | ConvertFrom-Json
    if (-not $result.success) {
        throw "Installed $mode shell smoke failed: $($result.error)"
    }
    return $result
}

try {
    $report.windowsAppRuntime = @(
        Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2*' |
            Where-Object { $_.Architecture -eq 'X64' } |
            Select-Object -ExpandProperty Version |
            ForEach-Object { $_.ToString() }
    )
    $report.dotnetSdk = (dotnet --version)
    $report.sourcePackageSha256 = (Get-FileHash $PackagePath -Algorithm SHA256).Hash
    Copy-Item $PackagePath $signed -Force

    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject 'CN=DesktopGuides Development' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyExportPolicy NonExportable `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddDays(1)
    $report.certificateThumbprint = $certificate.Thumbprint
    Export-Certificate -Cert $certificate -FilePath $public | Out-Null
    Import-Certificate -FilePath $public `
        -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    $imported = $true

    $signTool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' `
        -Recurse -Filter signtool.exe |
        Where-Object { $_.FullName -like '*\x64\signtool.exe' } |
        Sort-Object FullName |
        Select-Object -Last 1 -ExpandProperty FullName
    if (-not $signTool) { throw 'Windows SDK SignTool is unavailable.' }
    & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $signed
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE." }
    & $signTool verify /pa $signed
    if ($LASTEXITCODE -ne 0) { throw 'Signed MSIX verification failed.' }
    $report.signedPackageSha256 = (Get-FileHash $signed -Algorithm SHA256).Hash

    if (Get-AppxPackage -Name DesktopGuides.Preview) {
        throw 'A production preview package appeared before test install.'
    }
    Add-AppxPackage -Path $signed
    $installed = Get-AppxPackage -Name DesktopGuides.Preview
    if (-not $installed) { throw 'Production package did not install.' }
    $report.packageFullName = $installed.PackageFullName
    $report.packageFamilyName = $installed.PackageFamilyName
    $report.installLocation = $installed.InstallLocation
    $expectedExecutablePath = Join-Path $installed.InstallLocation `
        'DesktopGuides.Production.exe'

    $dataRoot = Join-Path $env:LOCALAPPDATA `
        "Packages\$($installed.PackageFamilyName)\LocalState"
    New-Item -ItemType Directory -Force $dataRoot | Out-Null
    $seedProject = Join-Path $PSScriptRoot `
        'DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj'

    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME `
        -LogonType Interactive -RunLevel Limited
    $launchAction = New-ShellLaunchAction $launchResultPath
    $secondLaunchAction = New-ShellLaunchAction $secondLaunchResultPath
    Register-ScheduledTask -TaskName $launchTask -Action $launchAction `
        -Principal $principal -Force | Out-Null

    Start-InstalledShell
    $report.empty = Run-ShellSmoke 'empty'
    Assert-SingleInstance
    $report.emptyAfterSecondLaunch = Run-ShellSmoke 'empty'

    Stop-InstalledShell
    dotnet run --project $seedProject -c Release --no-restore -- seed $dataRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed shell route metadata.' }
    Start-InstalledShell
    $report.normal = Run-ShellSmoke 'normal'
    Assert-RelaunchDuringClose
    Assert-ClosingTargetRedirect
    Assert-QueuedActivationClose

    Stop-InstalledShell
    dotnet run --project $seedProject -c Release --no-restore -- stale $dataRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not set stale last-guide ID.' }
    Start-InstalledShell
    $report.stale = Run-ShellSmoke 'stale'
    Close-InstalledShell
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
    try {
        $report.processesAtFailure = @(
            Get-InstalledShellProcesses |
            ForEach-Object {
                [ordered]@{
                    processId = $_.ProcessId
                    sessionId = $_.SessionId
                    created = $_.CreationDate.ToString('o')
                    testOwned = $ownedProcesses.ContainsKey([int]$_.ProcessId)
                }
            })
    }
    catch {
        $report.processesAtFailureError = $_ | Out-String
    }
    try {
        $report.applicationEvents = @(
            Get-WinEvent -FilterHashtable @{
                LogName = 'Application'
                StartTime = [datetime]$report.observedAt
            } -ErrorAction Stop |
            Where-Object {
                $_.Message -match 'DesktopGuides\.Production' -and
                $_.ProviderName -in @('.NET Runtime', 'Application Error',
                    'Windows Error Reporting')
            } |
            Select-Object -First 8 |
            ForEach-Object {
                [ordered]@{
                    observedAt = $_.TimeCreated.ToUniversalTime().ToString('o')
                    provider = $_.ProviderName
                    eventId = $_.Id
                    message = $_.Message
                }
            })
    }
    catch {
        $report.applicationEventsError = $_ | Out-String
    }
}
finally {
    Stop-ScheduledTask -TaskName $smokeTask -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $smokeTask -Confirm:$false `
        -ErrorAction SilentlyContinue
    Stop-ScheduledTask -TaskName $secondLaunchTask -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $secondLaunchTask -Confirm:$false `
        -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $launchTask -Confirm:$false `
        -ErrorAction SilentlyContinue
    if ($installed) { Stop-InstalledShell -BestEffort }
    $installedNow = @(Get-AppxPackage -Name DesktopGuides.Preview)
    $ownedPackage = @($installedNow | Where-Object {
        $installed -and $_.PackageFullName -eq $installed.PackageFullName
    })
    if ($ownedPackage.Count -eq 1) {
        Remove-AppxPackage -Package $ownedPackage[0].PackageFullName `
            -ErrorAction SilentlyContinue
    }
    if ($certificate) {
        if ($imported) {
            Remove-Item "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)" `
                -ErrorAction SilentlyContinue
        }
        Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)" `
            -ErrorAction SilentlyContinue
    }
    Remove-Item $public -ErrorAction SilentlyContinue
    $remainingPackages = @(Get-AppxPackage -Name DesktopGuides.Preview)
    $report.packageStillInstalled = $remainingPackages.Count -gt 0
    $report.certificateStillTrusted = if ($certificate) {
        Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
    } else { $false }
    if ($report.packageStillInstalled -or $report.certificateStillTrusted) {
        $report.success = $false
        $report.cleanupError = if ($remainingPackages.Count -gt 0 -and
            ($ownedPackage.Count -eq 0 -or
             $remainingPackages[0].PackageFullName -ne $installed.PackageFullName)) {
            'A Preview package not confirmed as test-owned remains untouched.'
        } else {
            'Temporary package or certificate remains.'
        }
    }
    if ($cleanupErrors.Count -gt 0) {
        $report.success = $false
        $report.processCleanupErrors = @($cleanupErrors)
    }
    $report | ConvertTo-Json -Depth 8 |
        Set-Content (Join-Path $ResultDirectory 'signed-install.json') -Encoding UTF8
}
if (-not $report.success) { exit 1 }
