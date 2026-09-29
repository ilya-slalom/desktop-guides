function Get-ScreenshotLuminance([string] $Path) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new($Path)
    try {
        $stepX = [Math]::Max(1, [int][Math]::Floor($bitmap.Width / 80))
        $stepY = [Math]::Max(1, [int][Math]::Floor($bitmap.Height / 60))
        [double]$total = 0
        [int]$count = 0
        for ($y = 0; $y -lt $bitmap.Height; $y += $stepY) {
            for ($x = 0; $x -lt $bitmap.Width; $x += $stepX) {
                $color = $bitmap.GetPixel($x, $y)
                $total += (0.2126 * $color.R) +
                    (0.7152 * $color.G) +
                    (0.0722 * $color.B)
                $count++
            }
        }
        if ($count -eq 0) { throw "Screenshot $Path has no pixels." }
        return [Math]::Round($total / $count, 2)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Get-ScreenshotRegionStats(
    [string] $Path, [int] $X, [int] $Y, [int] $Width, [int] $Height) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new($Path)
    try {
        if ($X -lt 0 -or $Y -lt 0 -or $Width -lt 1 -or $Height -lt 1 -or
            $X + $Width -gt $bitmap.Width -or $Y + $Height -gt $bitmap.Height) {
            throw "Region $X,$Y ${Width}x$Height is outside $Path."
        }
        $min = @(255, 255, 255); $max = @(0, 0, 0); $sum = @(0.0, 0.0, 0.0)
        for ($py = $Y; $py -lt $Y + $Height; $py++) {
            for ($px = $X; $px -lt $X + $Width; $px++) {
                $c = $bitmap.GetPixel($px, $py)
                $values = @($c.R, $c.G, $c.B)
                for ($i = 0; $i -lt 3; $i++) {
                    $sum[$i] += $values[$i]
                    if ($values[$i] -lt $min[$i]) { $min[$i] = $values[$i] }
                    if ($values[$i] -gt $max[$i]) { $max[$i] = $values[$i] }
                }
            }
        }
        $count = $Width * $Height
        return [ordered]@{
            meanR = [Math]::Round($sum[0] / $count, 2)
            meanG = [Math]::Round($sum[1] / $count, 2)
            meanB = [Math]::Round($sum[2] / $count, 2)
            maxChannelRange = [int](@(0, 1, 2 | ForEach-Object { $max[$_] - $min[$_] }) |
                Measure-Object -Maximum).Maximum
        }
    }
    finally { $bitmap.Dispose() }
}

# An 80x8 strip centered on the pane/content boundary, 24 px above the
# window's bottom edge, in window-relative pixels.
function Get-BoundaryStripRegion(
    [double] $WindowLeft, [double] $WindowHeight, [double] $ContentLeft) {
    $boundary = [int]($ContentLeft - $WindowLeft)
    return [ordered]@{
        X = $boundary - 40; Y = [int]($WindowHeight - 24); Width = 80; Height = 8
    }
}

# A 160x4 strip in a dialog's command area, 8 px below the primary button,
# in window-relative pixels.
function Get-DialogStripRegion([double] $ButtonLeft, [double] $ButtonBottom) {
    return [ordered]@{
        X = [int]$ButtonLeft; Y = [int]$ButtonBottom + 8; Width = 160; Height = 4
    }
}

# True when a strip repeats Solid's fill within one level on every channel,
# as it does when a layer of ours covers the backdrop. How Windows tints
# Mica or Acrylic is not checked.
function Test-MatchesSolidFill($Strip, $Solid) {
    return [Math]::Abs($Strip.meanR - $Solid.meanR) -le 1 -and
        [Math]::Abs($Strip.meanG - $Solid.meanG) -le 1 -and
        [Math]::Abs($Strip.meanB - $Solid.meanB) -le 1
}

function Get-AcrylicSurfaceDifference($Acrylic, $Solid) {
    $mean = [Math]::Max([Math]::Abs($Acrylic.meanR - $Solid.meanR),
        [Math]::Max([Math]::Abs($Acrylic.meanG - $Solid.meanG),
            [Math]::Abs($Acrylic.meanB - $Solid.meanB)))
    return [ordered]@{
        meanDifference = [Math]::Round($mean, 2)
        rangeDifference = $Acrylic.maxChannelRange - $Solid.maxChannelRange
    }
}

# Acrylic shows as either its fallback color (transparency off) or its noise
# texture (transparency on); the live tint depends on what is behind it.
function Test-AcrylicSurfaceVisible($Acrylic, $Solid) {
    $difference = Get-AcrylicSurfaceDifference $Acrylic $Solid
    return $difference.meanDifference -gt 2 -or $difference.rangeDifference -ge 2
}
