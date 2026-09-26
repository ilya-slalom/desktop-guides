param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$packageName = Split-Path $PackagePath -Leaf
if ($packageName -notmatch
    '^DesktopGuides\.ReaderToolbarSmoke_[0-9]+(\.[0-9]+){3}_x64\.msix$') {
    throw "Expected an x64 toolbar test MSIX, got $packageName."
}
if (-not @(Get-Process explorer -ErrorAction SilentlyContinue |
    Where-Object {
        $_.SessionId -eq [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    })) {
    throw 'Toolbar install test requires an interactive desktop session.'
}
if (Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke) {
    throw 'The toolbar test package is already installed.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Toolbar install test requires an elevated runner account.'
}
New-Item -ItemType Directory -Force $ResultDirectory | Out-Null
$ResultDirectory = (Resolve-Path $ResultDirectory).Path
$signed = Join-Path $ResultDirectory 'reader-toolbar-signed-x64.msix'
$public = Join-Path $ResultDirectory 'test-certificate.cer'
$smokeResult = Join-Path $ResultDirectory 'toolbar-ui.json'
$taskName = 'DesktopGuides-P1-Toolbar-' + [Guid]::NewGuid().ToString('N')
$report = [ordered]@{
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    sourcePackageSha256 = (Get-FileHash $PackagePath -Algorithm SHA256).Hash
    success = $false
}
$certificate = $null
$imported = $false
$registered = $false

try {
    Copy-Item -LiteralPath $PackagePath -Destination $signed -Force
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
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE."
    }
    & $signTool verify /pa $signed
    if ($LASTEXITCODE -ne 0) {
        throw 'Signed toolbar MSIX verification failed.'
    }
    $report.signedPackageSha256 = (Get-FileHash $signed -Algorithm SHA256).Hash

    $script = Join-Path $PSScriptRoot 'windows_reader_toolbar_ui_smoke.ps1'
    $arguments = '-NoProfile -NonInteractive -Sta -WindowStyle Hidden ' +
        '-ExecutionPolicy Bypass -File "' + $script + '"' +
        ' -PackagePath "' + $signed + '"' +
        ' -ResultPath "' + $smokeResult + '"'
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument $arguments -WorkingDirectory $PSScriptRoot
    $interactive = New-ScheduledTaskPrincipal -UserId $env:USERNAME `
        -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $taskName -Action $action `
        -Principal $interactive -Force | Out-Null
    $registered = $true
    Start-ScheduledTask -TaskName $taskName
    $deadline = (Get-Date).AddSeconds(180)
    do {
        if (Test-Path -LiteralPath $smokeResult) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    if (-not (Test-Path -LiteralPath $smokeResult)) {
        throw 'Interactive toolbar smoke timed out.'
    }
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $task = Get-ScheduledTask -TaskName $taskName
        if ($task.State -eq 'Ready') { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    if ($task.State -ne 'Ready') {
        throw 'Interactive toolbar smoke task did not finish.'
    }
    $smoke = Get-Content -LiteralPath $smokeResult -Raw | ConvertFrom-Json
    if (-not $smoke.success) {
        throw "Interactive toolbar smoke failed: $($smoke.error)"
    }
    if ($smoke.sessionId -ne
        [System.Diagnostics.Process]::GetCurrentProcess().SessionId) {
        throw 'Toolbar smoke ran in an unexpected desktop session.'
    }
    $report.phases = $smoke.phases
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
}
finally {
    if ($registered) {
        $task = Get-ScheduledTask -TaskName $taskName `
            -ErrorAction SilentlyContinue
        if ($task -and $task.State -ne 'Ready') {
            Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        }
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false `
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
    Remove-Item -LiteralPath $public -ErrorAction SilentlyContinue
    $report.packageStillInstalled =
        [bool](Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke)
    $report.certificateStillTrusted = if ($certificate) {
        Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
    } else { $false }
    $report.taskStillRegistered =
        [bool](Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)
    if ($report.packageStillInstalled -or $report.certificateStillTrusted -or
        $report.taskStillRegistered) {
        $report.success = $false
    }
    $report | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $ResultDirectory 'signed-install.json') `
            -Encoding UTF8
}
if (-not $report.success) { exit 1 }
