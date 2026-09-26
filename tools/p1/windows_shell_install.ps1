param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
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
$imported = $false
$launchTask = "DesktopGuides-P1-ShellLaunch-$runId"
$secondLaunchTask = "DesktopGuides-P1-ShellSecondLaunch-$runId"
$smokeTask = "DesktopGuides-P1-ShellSmoke-$runId"

function Stop-InstalledShell {
    Get-CimInstance Win32_Process -Filter "Name = 'DesktopGuides.Production.exe'" |
        Where-Object { $_.ExecutablePath -like "$($installed.InstallLocation)*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
}

function Start-InstalledShell([int] $ExcludeProcessId = 0) {
    Start-ScheduledTask -TaskName $launchTask
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $appProcess = Get-CimInstance Win32_Process `
            -Filter "Name = 'DesktopGuides.Production.exe'" |
            Where-Object { $_.ExecutablePath -like "$($installed.InstallLocation)*" } |
            Where-Object { $_.ProcessId -ne $ExcludeProcessId } |
            Where-Object {
                $windowProcess = Get-Process -Id $_.ProcessId `
                    -ErrorAction SilentlyContinue
                $windowProcess -and $windowProcess.MainWindowHandle -ne 0
            } | Select-Object -First 1
    } while (-not $appProcess -and (Get-Date) -lt $deadline)
    if (-not $appProcess) {
        throw 'Installed production shell did not open a window.'
    }
    $report.launchedProcessId = $appProcess.ProcessId
    $report.launchedSessionId = $appProcess.SessionId
}

function Assert-SingleInstance {
    $firstProcessId = $report.launchedProcessId
    $activationEvent = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::AutoReset,
        'Local\DesktopGuides.Preview.RedirectedActivation')
    try {
        $activationEvent.Reset() | Out-Null
        Register-ScheduledTask -TaskName $secondLaunchTask -Action $launchAction `
            -Principal $principal -Force | Out-Null
        $previousRun = (Get-ScheduledTaskInfo -TaskName $secondLaunchTask).LastRunTime
        Start-ScheduledTask -TaskName $secondLaunchTask
        $deadline = (Get-Date).AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 250
            $launchInfo = Get-ScheduledTaskInfo -TaskName $secondLaunchTask
        } while ($launchInfo.LastRunTime -le $previousRun -and
            (Get-Date) -lt $deadline)
        if ($launchInfo.LastRunTime -le $previousRun) {
            throw 'Second production launch task never started.'
        }
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
        $processes = @(Get-CimInstance Win32_Process `
            -Filter "Name = 'DesktopGuides.Production.exe'" |
            Where-Object { $_.ExecutablePath -like "$($installed.InstallLocation)*" })
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
    Request-InstalledShellClose $closingProcessId
    Start-InstalledShell $closingProcessId
    Wait-InstalledShellExit $closingProcessId
    $report.relaunchDuringCloseProcessId = $report.launchedProcessId
    $report.staleAfterCloseRelaunch = Run-ShellSmoke 'stale'
}

function Run-ShellSmoke([string] $mode) {
    $resultPath = Join-Path $ResultDirectory "$mode.json"
    Remove-Item $resultPath -ErrorAction SilentlyContinue
    $script = Join-Path $PSScriptRoot 'windows_shell_ui_smoke.ps1'
    $arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass ' +
        '-File "' + $script + '" -Mode ' + $mode + ' -ResultPath "' + $resultPath + '"'
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

    $dataRoot = Join-Path $env:LOCALAPPDATA `
        "Packages\$($installed.PackageFamilyName)\LocalState"
    New-Item -ItemType Directory -Force $dataRoot | Out-Null
    $seedProject = Join-Path $PSScriptRoot `
        'DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj'

    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME `
        -LogonType Interactive -RunLevel Limited
    $appId = "shell:AppsFolder\$($installed.PackageFamilyName)!App"
    $launchAction = New-ScheduledTaskAction -Execute "$env:WINDIR\explorer.exe" `
        -Argument $appId
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

    Stop-InstalledShell
    dotnet run --project $seedProject -c Release --no-restore -- stale $dataRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not set stale last-guide ID.' }
    Start-InstalledShell
    $report.stale = Run-ShellSmoke 'stale'
    Assert-RelaunchDuringClose
    Close-InstalledShell
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
    try {
        $report.processesAtFailure = @(
            Get-CimInstance Win32_Process `
                -Filter "Name = 'DesktopGuides.Production.exe'" |
            Where-Object { $_.ExecutablePath -like "$($installed.InstallLocation)*" } |
            ForEach-Object {
                [ordered]@{
                    processId = $_.ProcessId
                    sessionId = $_.SessionId
                    created = $_.CreationDate.ToString('o')
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
    if ($installed) { Stop-InstalledShell }
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
    $report | ConvertTo-Json -Depth 8 |
        Set-Content (Join-Path $ResultDirectory 'signed-install.json') -Encoding UTF8
}
if (-not $report.success) { exit 1 }
