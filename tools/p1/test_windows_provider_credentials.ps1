$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_provider_credentials.ps1')

$root = Join-Path $env:TEMP "DesktopGuides-Creds-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $file = Join-Path $root 'igdb.txt'
    Set-Content -LiteralPath $file -Encoding UTF8 -Value @(
        'IGDB Client ID:  abc123 ',
        'igdb client secret: s3cr+t{x}',
        'unrelated: ignored')
    $values = Read-LabelledValues $file @('igdb client id', 'igdb client secret')
    if ($values['igdb client id'] -ne 'abc123' -or $values['igdb client secret'] -ne 's3cr+t{x}') {
        throw 'Labelled values were not parsed.'
    }

    Set-Content -LiteralPath $file -Encoding UTF8 -Value 'igdb client id: only-this'
    $message = $null
    try { [void](Read-LabelledValues $file @('igdb client id', 'igdb client secret')) }
    catch { $message = $_.Exception.Message }
    if (-not $message -or $message -notmatch 'igdb client secret' -or $message -match 'only-this') {
        throw "A missing label was not reported by name alone: $message"
    }

    if ((ConvertTo-SendKeysLiteral 'a+b^c%d~e(f)g{h}i[j]') -ne
        'a{+}b{^}c{%}d{~}e{(}f{)}g{{}h{}}i{[}j{]}') {
        throw 'SendKeys metacharacters were not escaped.'
    }

    $clean = Join-Path $root 'clean.json'
    $utf8 = Join-Path $root 'leak-utf8.json'
    $utf16 = Join-Path $root 'leak-utf16.txt'
    Set-Content -LiteralPath $clean -Encoding UTF8 -Value '{"ok":true}'
    Set-Content -LiteralPath $utf8 -Encoding UTF8 -Value '{"x":"s3cr+t{x}"}'
    [System.IO.File]::WriteAllText($utf16, 'value s3cr+t{x}', [System.Text.Encoding]::Unicode)
    $found = @(Find-SecretInFiles @($root) @('s3cr+t{x}', 'never-there'))
    $names = @($found | ForEach-Object { Split-Path $_ -Leaf } | Sort-Object)
    if (($names -join ',') -ne 'leak-utf16.txt,leak-utf8.json') {
        throw "Leak scan found the wrong files: $($names -join ',')"
    }
    'Provider credential helper checks passed.'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
