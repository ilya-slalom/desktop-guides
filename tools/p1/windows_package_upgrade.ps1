param(
    [Parameter(Mandatory = $true)] [string] $OldPackage,
    [Parameter(Mandatory = $true)] [string] $NewPackage,
    # Public certificate both packages are signed with.
    [Parameter(Mandatory = $true)] [string] $Certificate,
    # Built DesktopGuides.ShellSeed.dll.
    [Parameter(Mandatory = $true)] [string] $SeedTool,
    [Parameter(Mandatory = $true)] [string] $ResultPath,
    # Checks the packages and the machine, then stops before installing.
    [switch] $PreflightOnly,
    # True when the public app is installed; replaceable for tests.
    [scriptblock] $InstalledCheck
)
# T17.1: installs the old public package, seeds a library, installs the new
# package over it without uninstalling, and proves the identity and the
# library survive and the upgraded app opens. Refuses to run where the
# public app is already installed, and removes everything it added.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$publicName = 'IlyaLissoboi.DesktopGuides'
$publicPublisher = 'CN=359962D5-3801-484E-BE5B-39D0D498BEA4'
$windowTitle = 'Desktop Guides'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not $InstalledCheck) {
    $InstalledCheck = { @(Get-AppxPackage -Name $publicName).Count -gt 0 }
}

function Read-Identity([string] $Path) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw "$Path has no AppxManifest.xml." }
        $reader = [IO.StreamReader]::new($entry.Open())
        [xml] $xml = $reader.ReadToEnd()
        $reader.Dispose()
    }
    finally { $zip.Dispose() }
    $identity = $xml.Package.Identity
    [pscustomobject]@{
        Name = $identity.Name
        Publisher = $identity.Publisher
        Version = [Version] $identity.Version
        Architecture = $identity.ProcessorArchitecture
    }
}

$old = Read-Identity $OldPackage
$new = Read-Identity $NewPackage
foreach ($package in @($old, $new)) {
    if ($package.Name -ne $publicName -or $package.Publisher -ne $publicPublisher) {
        throw "Expected the public identity, got $($package.Name) / $($package.Publisher)."
    }
}
if ($new.Architecture -ne $old.Architecture) { throw 'The packages target different architectures.' }
if ($new.Version -le $old.Version) { throw "The new version $($new.Version) isn't higher than $($old.Version)." }
if (-not (Test-Path -LiteralPath $Certificate -PathType Leaf)) { throw "Certificate not found: $Certificate" }
if (& $InstalledCheck) { throw "$publicName is already installed here; the gate runs only on a machine without it." }
if ($PreflightOnly) { return }

$runId = [Guid]::NewGuid().ToString('N')
$taskName = "DesktopGuides-T17-1-Upgrade-$runId"
$launchResult = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-upgrade-launch-$runId.json"
$trusted = $null
$sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
if ($sessionId -eq 0 -or -not @(Get-Process explorer -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $sessionId })) {
    throw 'The upgrade gate needs an interactive desktop session.'
}
$report = [ordered]@{
    oldVersion = "$($old.Version)"
    newVersion = "$($new.Version)"
    libraryPreserved = $false
    upgradedAppLaunched = $false
}

function Invoke-Seed([string[]] $Arguments) {
    $output = @(dotnet $SeedTool @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "ShellSeed $($Arguments[0]) failed: $($output | Select-Object -Last 3)" }
    $output | Select-Object -Last 1
}

function Start-PublicApp([string] $Executable) {
    Remove-Item -LiteralPath $launchResult, "$launchResult.tmp", "$launchResult.ack", "$launchResult.ack.tmp" `
        -ErrorAction SilentlyContinue
    $script = Join-Path $PSScriptRoot 'windows_shell_launch.ps1'
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -WorkingDirectory $PSScriptRoot -Argument (
        '-NoProfile -NonInteractive -Sta -WindowStyle Hidden -ExecutionPolicy Bypass ' +
        "-File ""$script"" -ExecutablePath ""$Executable"" -ResultPath ""$launchResult""")
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Path -LiteralPath $launchResult) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if (-not (Test-Path -LiteralPath $launchResult)) { throw 'The interactive launch wrote no result.' }
    $launch = Get-Content -LiteralPath $launchResult -Raw | ConvertFrom-Json
    if (-not $launch.success) { throw "The interactive launch failed: $($launch.error)" }
    if ($launch.sessionId -ne $sessionId -or
        -not [string]::Equals($launch.executablePath, $Executable, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The interactive launch returned an unexpected session or executable.'
    }
    $token = [Guid]::Empty
    if (-not [Guid]::TryParseExact([string] $launch.handoffToken, 'N', [ref] $token)) {
        throw 'The interactive launch returned an invalid handoff token.'
    }
    # Take the process before the ack: the launcher stops it if no ack comes.
    $process = Get-Process -Id ([int] $launch.processId)
    if ([math]::Abs(($process.StartTime.ToUniversalTime() - ([datetime] $launch.startedAt).ToUniversalTime()).TotalSeconds) -gt 1) {
        throw "Process $($launch.processId) is not the one the launcher started."
    }
    # windows_shell_launch.ps1 waits for this before it lets the app go.
    @{ handoffToken = $token.ToString('N') } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath "$launchResult.ack.tmp" -Encoding UTF8
    Move-Item -LiteralPath "$launchResult.ack.tmp" -Destination "$launchResult.ack" -Force
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-ScheduledTask -TaskName $taskName).State -ne 'Ready' -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    if ((Get-ScheduledTask -TaskName $taskName).State -ne 'Ready') {
        throw 'The interactive launch task did not finish after the handoff.'
    }
    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    } while ($process.MainWindowTitle -ne $windowTitle -and -not $process.HasExited -and (Get-Date) -lt $deadline)
    if ($process.MainWindowTitle -ne $windowTitle) {
        throw "The app didn't show a '$windowTitle' window (title '$($process.MainWindowTitle)')."
    }
    [void] $process.CloseMainWindow()
    if (-not $process.WaitForExit(30000)) { throw 'The app did not close.' }
}

function Get-LibraryState([string] $DataRoot) {
    $content = Join-Path $DataRoot 'library\content'
    $files = @(Get-ChildItem -LiteralPath $content -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object FullName | ForEach-Object {
            '{0} {1}' -f $_.FullName.Substring($content.Length), (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        })
    [ordered]@{ description = (Invoke-Seed @('describe-library', $DataRoot)); content = $files }
}

try {
    $trusted = Import-Certificate -FilePath $Certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople
    Add-AppxPackage -Path $OldPackage
    $installed = Get-AppxPackage -Name $publicName
    $report.family = $installed.PackageFamilyName
    $executable = Join-Path $installed.InstallLocation 'DesktopGuides.Production.exe'
    Start-PublicApp $executable
    $dataRoot = Join-Path $env:LOCALAPPDATA "Packages\$($installed.PackageFamilyName)\LocalState"
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Invoke-Seed @('seed', $dataRoot) | Out-Null
    $report.libraryBefore = Get-LibraryState $dataRoot

    Add-AppxPackage -Path $NewPackage
    $upgraded = Get-AppxPackage -Name $publicName
    if ($upgraded.PackageFamilyName -ne $report.family) {
        throw "The package family changed: $($report.family) -> $($upgraded.PackageFamilyName)."
    }
    if ([Version] $upgraded.Version -ne $new.Version) {
        throw "Expected version $($new.Version) after the upgrade, got $($upgraded.Version)."
    }
    Start-PublicApp (Join-Path $upgraded.InstallLocation 'DesktopGuides.Production.exe')
    $report.upgradedAppLaunched = $true
    $report.libraryAfter = Get-LibraryState $dataRoot
    $report.libraryPreserved =
        ($report.libraryBefore | ConvertTo-Json -Depth 4) -eq ($report.libraryAfter | ConvertTo-Json -Depth 4)
    if (-not $report.libraryPreserved) { throw 'The library changed across the upgrade.' }
}
finally {
    Get-AppxPackage -Name $publicName | Remove-AppxPackage -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $launchResult, "$launchResult.tmp", "$launchResult.ack", "$launchResult.ack.tmp" `
        -ErrorAction SilentlyContinue
    if ($trusted) {
        Remove-Item -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($trusted.Thumbprint)" -ErrorAction SilentlyContinue
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
}
