$ErrorActionPreference = 'Stop'
$gate = Join-Path $PSScriptRoot 'assert_trim_warnings.ps1'

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

$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-trim-gate-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $root = 'D:\a\desktop-guides\desktop-guides'
    $trimmed = '  Optimizing assemblies for size. This process might take a while.'
    $winrt = "ILLink : Trim analysis warning IL2104: Assembly 'WinRT.Runtime' produced trim warnings. For more information see https://aka.ms/il2104 [$root\src\DesktopGuides.Production\DesktopGuides.Production.csproj]"
    $partial = "$root\src\DesktopGuides.Production\GuideRowItem.cs(8,22): warning CsWinRT1028: Class 'GuideRowItem' implements WinRT interfaces but it or a parent type isn't marked partial. [$root\src\DesktopGuides.Production\DesktopGuides.Production.csproj]"
    $json = "$root\src\DesktopGuides.Core\Pdf\PdfSessionDiagnostics.cs(21,33): warning IL2026: Using member 'System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, JsonSerializerOptions)' which has 'RequiresUnreferencedCodeAttribute'. [$root\src\DesktopGuides.Core\DesktopGuides.Core.csproj]"
    $other = "ILLink : Trim analysis warning IL2081: ABI.Windows.Foo.Bar(): 'T' generic argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicParameterlessConstructor'."

    function New-Log([string] $Name, [string[]] $Lines) {
        $path = Join-Path $scratch $Name
        Set-Content -LiteralPath $path -Value $Lines
        return $path
    }
    $allow = Join-Path $scratch 'allow.txt'
    Set-Content -LiteralPath $allow -Value @(
        '# Third-party assemblies only.',
        '',
        'IL2104 WinRT.Runtime  # C#/WinRT runtime; its warnings are its own')
    $common = @{ AllowList = $allow; RepoRoot = $root }

    # Only allowed warnings, each repeated as MSBuild's summary repeats it.
    $clean = New-Log 'clean.log' @($trimmed, $winrt, 'Build succeeded.', $winrt)
    $output = & $gate -Log $clean @common
    Assert-True ("$output" -like 'Trim warnings: none new*') "A log with only allowed warnings should pass, got: $output"

    # A warning in the project's own code fails, keyed by repo path.
    $dirty = New-Log 'dirty.log' @($trimmed, $winrt, $partial, $json)
    Assert-Rejected { & $gate -Log $dirty @common } 'CsWinRT1028 src/DesktopGuides.Production/GuideRowItem.cs' `
        'A C#/WinRT warning in app code should fail, naming its path.'
    Assert-Rejected { & $gate -Log $dirty @common } 'IL2026 src/DesktopGuides.Core/Pdf/PdfSessionDiagnostics.cs' `
        'A JSON trim warning in app code should fail, naming its path.'

    # A warning with neither assembly nor path fails with its text.
    $unkeyed = New-Log 'unkeyed.log' @($trimmed, $winrt, $other)
    Assert-Rejected { & $gate -Log $unkeyed @common } 'IL2081' 'A warning without a path should fail.'

    # A stale entry fails, so fixes shrink the list.
    $stale = New-Log 'stale.log' @($trimmed)
    Assert-Rejected { & $gate -Log $stale @common } 'IL2104 WinRT.Runtime' 'An unused allow-list entry should fail.'

    # A log without trimming isn't a trim-analysis publish.
    $plain = New-Log 'plain.log' @('Build succeeded.')
    Assert-Rejected { & $gate -Log $plain @common } 'shows no trimming' 'A log without trimming should fail.'

    # Run as CI and the host do, with -File and the default allow-list:
    # Windows PowerShell leaves $PSScriptRoot empty in parameter defaults.
    $shell = (Get-Process -Id $PID).Path
    # One warning for each real entry, so only loading the default is tested.
    $real = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'trim-warning-allowlist.txt') |
        Where-Object { $_.Trim() -and -not $_.Trim().StartsWith('#') } |
        ForEach-Object { "ILLink : Trim analysis warning IL2104: Assembly '$((($_ -split '#')[0].Trim() -split '\s+')[1])' produced trim warnings." })
    $bare = New-Log 'bare.log' (@($trimmed) + $real)
    $ErrorActionPreference = 'Continue'
    $default = & $shell -NoProfile -ExecutionPolicy Bypass -File $gate -Log $bare 2>&1
    $ErrorActionPreference = 'Stop'
    Assert-True ($LASTEXITCODE -eq 0 -and "$default" -like 'Trim warnings: none new*') `
        "The default allow-list should load under -File, got: $default"

    # Entries need a reason, and only assembly-level entries are allowed.
    $noReason = Join-Path $scratch 'no-reason.txt'
    Set-Content -LiteralPath $noReason -Value @('IL2104 WinRT.Runtime')
    Assert-Rejected { & $gate -Log $clean -AllowList $noReason -RepoRoot $root } 'needs a # reason' `
        'An entry without a reason should fail.'
    $ownCode = Join-Path $scratch 'own-code.txt'
    Set-Content -LiteralPath $ownCode -Value @(
        'IL2104 WinRT.Runtime  # third party',
        'CsWinRT1028 src/DesktopGuides.Production/GuideRowItem.cs  # later')
    $ownLog = New-Log 'own.log' @($trimmed, $winrt, $partial)
    Assert-Rejected { & $gate -Log $ownLog -AllowList $ownCode -RepoRoot $root } 'only IL2104' `
        'An entry for the project''s own code should fail.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'assert_trim_warnings: all cases passed.'
