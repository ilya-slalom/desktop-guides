$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'windows_shell_screenshot_stats.ps1')

$root = Join-Path $env:TEMP "DesktopGuides-Stats-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $path = Join-Path $root 'split.png'
    $bitmap = [System.Drawing.Bitmap]::new(40, 10)
    try {
        for ($x = 0; $x -lt 40; $x++) {
            for ($y = 0; $y -lt 10; $y++) {
                $color = if ($x -lt 20) { [System.Drawing.Color]::FromArgb(243, 243, 243) }
                    else { [System.Drawing.Color]::FromArgb(249, 249, 249) }
                $bitmap.SetPixel($x, $y, $color)
            }
        }
        $bitmap.SetPixel(20, 5, [System.Drawing.Color]::FromArgb(229, 229, 229))
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }

    $uniform = Get-ScreenshotRegionStats $path 0 0 20 10
    if ($uniform.maxChannelRange -ne 0 -or $uniform.meanR -ne 243) {
        throw "Uniform region stats were wrong: $($uniform | ConvertTo-Json -Compress)"
    }
    $split = Get-ScreenshotRegionStats $path 10 0 20 10
    if ($split.maxChannelRange -ne 20) {
        throw "Split region range was $($split.maxChannelRange), expected 20."
    }
    $outside = $false
    try { [void](Get-ScreenshotRegionStats $path 30 0 20 10) } catch { $outside = $true }
    if (-not $outside) { throw 'A region outside the image did not fail closed.' }
    $region = Get-BoundaryStripRegion 100 400 420
    if ($region.X -ne 280 -or $region.Y -ne 376 -or
        $region.Width -ne 80 -or $region.Height -ne 8) {
        throw "Boundary strip region was wrong: $($region | ConvertTo-Json -Compress)"
    }
    $solidDialog = [ordered]@{ meanR = 243; meanG = 243; meanB = 243; maxChannelRange = 0 }
    $fallbackDialog = [ordered]@{ meanR = 249; meanG = 249; meanB = 249; maxChannelRange = 0 }
    # Real in-app acrylic over gray content: close to Solid, but with noise.
    $liveDialog = [ordered]@{ meanR = 244.74; meanG = 244.74; meanB = 244.74; maxChannelRange = 3 }
    $matchedDialog = [ordered]@{ meanR = 244; meanG = 244; meanB = 244; maxChannelRange = 1 }
    if (-not (Test-AcrylicSurfaceVisible $fallbackDialog $solidDialog)) {
        throw 'The Acrylic fallback color was not told apart from Solid.'
    }
    if (-not (Test-AcrylicSurfaceVisible $liveDialog $solidDialog)) {
        throw 'The live Acrylic noise was not told apart from Solid.'
    }
    if (Test-AcrylicSurfaceVisible $matchedDialog $solidDialog) {
        throw 'A surface that matched Solid was reported as Acrylic.'
    }
    if ((Get-ScreenshotLuminance $path) -lt 243) {
        throw 'Luminance of a light image was too low.'
    }
    'Screenshot stats checks passed.'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
