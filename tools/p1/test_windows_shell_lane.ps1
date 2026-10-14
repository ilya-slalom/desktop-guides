$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_shell_lane.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock] $Action, [string] $Expected, [string] $Message) {
    try { & $Action | Out-Null } catch {
        Assert-True ("$_" -like "*$Expected*") "$Message Got: $_"
        return
    }
    throw "$Message It was accepted."
}

$preview = Get-ShellLane 'Preview'
Assert-True ($preview.PackageName -eq 'DesktopGuides.Preview' -and $preview.EventPrefix -eq 'DesktopGuides.Preview') `
    "Unexpected Preview names: $($preview | ConvertTo-Json -Compress)"
$public = Get-ShellLane 'Public'
Assert-True ($public.PackageName -eq 'IlyaLissoboi.DesktopGuides' -and $public.EventPrefix -eq 'DesktopGuides') `
    "Unexpected Public names: $($public | ConvertTo-Json -Compress)"
Assert-True ((Get-ShellLane).Name -eq 'Preview') 'Preview is the default lane.'
Assert-Rejected { Get-ShellLane 'Store' } 'Store' 'An unknown lane should fail.'
Assert-True ((@(Get-ShellLanePackageNames) -join ',') -eq 'DesktopGuides.Preview,IlyaLissoboi.DesktopGuides') `
    'Both lanes'' packages are listed.'

$pf = 'C:\Program Files'
Assert-True (Test-InstalledLaneExecutable "$pf\WindowsApps\DesktopGuides.Preview_0.1.0.0_x64__en8r599z3n35j\DesktopGuides.Production.exe" $pf) `
    'The installed Preview exe is accepted.'
Assert-True (Test-InstalledLaneExecutable "$pf\WindowsApps\IlyaLissoboi.DesktopGuides_1.0.1.0_x64__8wekyb3d8bbwe\DesktopGuides.Production.exe" $pf) `
    'The installed Public exe is accepted.'
Assert-True (-not (Test-InstalledLaneExecutable "$pf\WindowsApps\Other.App_1.0.0.0_x64__x\DesktopGuides.Production.exe" $pf)) `
    'Another package''s exe is refused.'
Assert-True (-not (Test-InstalledLaneExecutable 'C:\Users\me\DesktopGuides.Production.exe' $pf)) `
    'A portable exe is refused.'

# The real pin, and a broken one.
$pinPath = Join-Path $PSScriptRoot '..\..\docs\p1\release\release-certificate.json'
Assert-True ((Get-ReleasePin $pinPath) -eq 'BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6') 'The release pin is read.'
$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-lane-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $badPin = Join-Path $scratch 'pin.json'
    Set-Content -LiteralPath $badPin -Value '{ "thumbprint": "not-a-thumbprint" }'
    Assert-Rejected { Get-ReleasePin $badPin } 'no valid thumbprint' 'A broken pin should fail.'

    # An unsigned file, and a file signed by someone else.
    $unsigned = Join-Path $scratch 'unsigned.msix'
    [IO.File]::WriteAllBytes($unsigned, [byte[]](1..64))
    Assert-Rejected { Assert-ReleaseSigner $unsigned 'BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6' } 'no signature' `
        'An unsigned package should fail.'
    $other = (Get-Process -Id $PID).Path
    Assert-Rejected { Assert-ReleaseSigner $other 'BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6' } 'not the release certificate' `
        'A package signed by another certificate should fail.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'windows_shell_lane: all cases passed.'
