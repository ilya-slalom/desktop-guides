# T17.4: fails a trim-analysis publish on any trim or C#/WinRT warning the
# allow-list doesn't name, and on any allow-list entry no warning needs.
# Only assembly-level IL2104 entries are allowed: the project's own code
# has no exceptions.
param(
    [Parameter(Mandatory = $true)]
    [string] $Log,
    [string] $AllowList = (Join-Path $PSScriptRoot 'trim-warning-allowlist.txt'),
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)
$ErrorActionPreference = 'Stop'
$lines = @(Get-Content -LiteralPath $Log)
if (-not ($lines | Where-Object { $_ -like '*Optimizing assemblies for size*' })) {
    throw "$Log shows no trimming; it isn't a trim-analysis publish."
}

$root = $RepoRoot.TrimEnd('\', '/') + '\'
$found = [Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::Ordinal)
foreach ($line in $lines) {
    $warning = [regex]::Match($line, 'warning (?<code>IL\d{4}|CsWinRT\d{4})\s*:\s*(?<text>.*)$')
    if (-not $warning.Success) { continue }
    $code = $warning.Groups['code'].Value
    $assembly = [regex]::Match($warning.Groups['text'].Value, "^Assembly '(?<name>[^']+)' produced")
    $source = [regex]::Match($line, '^\s*(?<path>[A-Za-z]:\\[^(]+\.cs)\(\d+')
    if ($code -eq 'IL2104' -and $assembly.Success) {
        $key = "IL2104 $($assembly.Groups['name'].Value)"
    }
    elseif ($source.Success) {
        $path = $source.Groups['path'].Value
        if ($path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            $path = $path.Substring($root.Length)
        }
        $key = "$code $($path -replace '\\', '/')"
    }
    else {
        $key = "$code $(($warning.Groups['text'].Value -replace '\s*\[[^\]]+\]\s*$', '').Trim())"
    }
    if (-not $found.ContainsKey($key)) { $found[$key] = $line.Trim() }
}

$allowed = @{}
foreach ($entry in @(Get-Content -LiteralPath $AllowList)) {
    $text = $entry.Trim()
    if (-not $text -or $text.StartsWith('#')) { continue }
    $parts = $text -split '#', 2
    if ($parts.Count -lt 2 -or -not $parts[1].Trim()) { throw "Allow-list entry '$text' needs a # reason." }
    $key = ($parts[0].Trim() -replace '\s+', ' ')
    if ($key -notmatch '^IL2104 \S+$') {
        throw "Allow-list entry '$key' isn't allowed: only IL2104 entries for assemblies the project doesn't own."
    }
    $allowed[$key] = $true
}

$new = @($found.Keys | Where-Object { -not $allowed.ContainsKey($_) })
$stale = @($allowed.Keys | Where-Object { -not $found.ContainsKey($_) } | Sort-Object)
$problems = @()
if ($new.Count -gt 0) { $problems += "New trim or C#/WinRT warnings:`n  " + ($new -join "`n  ") }
if ($stale.Count -gt 0) { $problems += "Allow-list entries no warning needs (remove them):`n  " + ($stale -join "`n  ") }
if ($problems.Count -gt 0) { throw ($problems -join "`n") }
Write-Output "Trim warnings: none new; $($found.Count) allowed."
