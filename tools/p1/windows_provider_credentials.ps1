# Reads `label: value` lines. Errors name the missing label, never a value.
function Read-LabelledValues([string] $Path, [string[]] $Labels) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Credential file $Path is missing."
    }
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $parts = $line -split ':', 2
        if ($parts.Count -ne 2) { continue }
        $label = $parts[0].Trim().ToLowerInvariant()
        if ($Labels -contains $label) { $values[$label] = $parts[1].Trim() }
    }
    foreach ($label in $Labels) {
        if (-not $values.ContainsKey($label) -or
            $values[$label].Length -lt 1 -or $values[$label].Length -gt 128) {
            throw "Credential file $Path has no usable '$label' line."
        }
    }
    return $values
}

# SendKeys treats these characters as commands; braces make them literal.
function ConvertTo-SendKeysLiteral([string] $Value) {
    $builder = [System.Text.StringBuilder]::new()
    foreach ($character in $Value.ToCharArray()) {
        if ('+^%~(){}[]'.IndexOf($character) -ge 0) {
            [void]$builder.Append('{').Append($character).Append('}')
        }
        else {
            [void]$builder.Append($character)
        }
    }
    return $builder.ToString()
}

# Returns the paths of files that contain any value as UTF-8 or UTF-16LE.
function Find-SecretInFiles([string[]] $Roots, [string[]] $Secrets) {
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $needles = foreach ($secret in $Secrets) {
        $latin1.GetString([System.Text.Encoding]::UTF8.GetBytes($secret))
        $latin1.GetString([System.Text.Encoding]::Unicode.GetBytes($secret))
    }
    foreach ($root in $Roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse -Force) {
            $text = $latin1.GetString([System.IO.File]::ReadAllBytes($file.FullName))
            foreach ($needle in $needles) {
                if ($text.IndexOf($needle, [StringComparison]::Ordinal) -ge 0) {
                    $file.FullName
                    break
                }
            }
        }
    }
}
