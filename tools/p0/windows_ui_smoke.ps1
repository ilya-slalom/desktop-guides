param(
    [Parameter(Mandatory = $true)]
    [string] $FixtureId,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath,

    [switch] $ExpectWebViewUnavailable
)

$ErrorActionPreference = 'Stop'
trap {
    $_ | Out-String | Set-Content ($ResultPath + '.error.txt') -Encoding UTF8
    if ($null -ne $phases) {
        [ordered]@{
            fixture = $FixtureId
            phases = $phases
        } | ConvertTo-Json -Depth 8 |
            Set-Content ($ResultPath + '.partial.json') -Encoding UTF8
    }
    exit 1
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SmokeWindow {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] public static extern bool MoveWindow(
        IntPtr handle, int x, int y, int width, int height, bool repaint);
    public struct Rect { public int Left, Top, Right, Bottom; }
}
'@

$deadline = (Get-Date).AddSeconds(15)
do {
    $process = Get-Process DesktopGuides.App -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } |
        Select-Object -First 1
    if ($null -ne $process) { break }
    Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $deadline)
if ($null -eq $process) {
    $details = Get-Process DesktopGuides.App -ErrorAction SilentlyContinue |
        ForEach-Object { "id=$($_.Id) session=$($_.SessionId) window=$($_.MainWindowHandle)" }
    throw "Desktop Guides has no interactive window. Processes: $details"
}
$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$scope = [System.Windows.Automation.TreeScope]::Descendants

function Find-ByName([string] $name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst($scope, $condition)
}

function Find-ById([string] $id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $root.FindFirst($scope, $condition)
}

function Invoke-Button([string] $name) {
    $button = $null
    for ($attempt = 0; $attempt -lt 30 -and $null -eq $button; $attempt++) {
        $button = Find-ByName $name
        if ($null -eq $button) {
            Start-Sleep -Milliseconds 100
        }
    }
    if ($null -eq $button) {
        throw "Button '$name' is unavailable."
    }
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Enter-Password(
    [System.Windows.Automation.AutomationElement] $box,
    [string] $password
) {
    $value = $null
    if (-not $box.TryGetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern, [ref] $value) -or
        $value.Current.IsReadOnly) {
        throw 'The PDF password field has no writable UI Automation ValuePattern.'
    }
    $value.SetValue($password)
}

function Status {
    $element = Find-ById 'StatusText'
    if ($null -eq $element) {
        throw 'Probe status is unavailable.'
    }
    return $element.Current.Name
}

function Page-Status {
    $element = Find-ById 'PdfPageStatus'
    if ($null -ne $element) {
        return $element.Current.Name
    }
    return $null
}

function Wait-Page([int] $number) {
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $current = Page-Status
        if ($current -like "page $number/*") {
            return
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "Page $number did not render. Last status: $current"
}

function Wait-Restore {
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $current = Status
        if ($current -like 'Position restored.*') {
            return
        }
        if ($current -like 'Restore failed:*') {
            throw $current
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "Restore did not finish. Last status: $current"
}

function Resize-Window {
    $rect = New-Object SmokeWindow+Rect
    if (-not [SmokeWindow]::GetWindowRect($process.MainWindowHandle, [ref] $rect)) {
        throw 'Could not read the probe window bounds.'
    }
    $script:originalRect = $rect
    $width = [Math]::Max(650, ($rect.Right - $rect.Left) - 250)
    if (-not [SmokeWindow]::MoveWindow(
            $process.MainWindowHandle, $rect.Left, $rect.Top,
            $width, $rect.Bottom - $rect.Top, $true)) {
        throw 'Could not resize the probe window.'
    }
    Start-Sleep -Milliseconds 500
}

function Record-Phase([string] $action) {
    Start-Sleep -Milliseconds 250
    $process.Refresh()
    $items = $root.FindAll($scope, [System.Windows.Automation.Condition]::TrueCondition)
    $realized = 0
    $pageText = $null
    $imageText = $null
    $imageHasTextPattern = $false
    $taggedTextInAutomation = $false
    $markerText = $null
    foreach ($item in $items) {
        if ($item.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
            $realized++
        }
        if ($item.Current.Name -like 'Rendered image of PDF page *') {
            $imageText = $item.Current.Name
            $textPattern = $null
            $imageHasTextPattern = $item.TryGetCurrentPattern(
                [System.Windows.Automation.TextPattern]::Pattern, [ref] $textPattern)
        }
        if ($item.Current.Name -like '*Tagged guide paragraph for Narrator*') {
            $taggedTextInAutomation = $true
        }
        if ($item.Current.Name -like 'Marker:*' -or $item.Current.Name -eq 'At top.' -or
            $item.Current.Name -like 'Restored *') {
            $markerText = $item.Current.Name
        }
    }
    if ($FixtureId -like 'pdf-*') {
        $pageText = Page-Status
    }
    $phases.Add([ordered] @{
        action = $action
        status = Status
        page = $pageText
        image = $imageText
        imageHasTextPattern = $imageHasTextPattern
        taggedTextInAutomation = $taggedTextInAutomation
        marker = $markerText
        realizedListItems = $realized
        elapsedMilliseconds = $clock.ElapsedMilliseconds
        workingSetBytes = $process.WorkingSet64
        peakWorkingSetBytes = $process.PeakWorkingSet64
    })
}

$clock = [System.Diagnostics.Stopwatch]::StartNew()
$phases = [System.Collections.Generic.List[object]]::new()
$scrollCalls = [System.Collections.Generic.List[long]]::new()
$picker = Find-ById 'FixturePicker'
if ($null -eq $picker) {
    throw 'Fixture picker is unavailable.'
}
$picker.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
$fixture = $null
for ($attempt = 0; $attempt -lt 30 -and $null -eq $fixture; $attempt++) {
    $fixture = Find-ByName $FixtureId
    if ($null -eq $fixture) {
        Start-Sleep -Milliseconds 100
    }
}
if ($null -eq $fixture) {
    throw "Fixture '$FixtureId' is unavailable."
}
$fixture.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$openClock = [System.Diagnostics.Stopwatch]::StartNew()
Invoke-Button 'Open'

$deadline = (Get-Date).AddSeconds(20)
do {
    Start-Sleep -Milliseconds 200
    $openStatus = Status
} while ($openStatus -like 'Opening*' -and (Get-Date) -lt $deadline)
Record-Phase 'open'
$expectedFailure = $false
if ($ExpectWebViewUnavailable) {
    if ($openStatus -notlike 'Could not open html-static: HTML requires Microsoft Edge WebView2 Runtime*') {
        throw "Missing-WebView2 simulation did not produce an actionable error: $openStatus"
    }
    $expectedFailure = $true
}
$openToVisibleMilliseconds = $null
if ($FixtureId -eq 'txt-legacy' -and $openStatus -like 'Could not open txt-legacy:*') {
    Record-Phase 'strict-utf8-error'
    $encoding = Find-ById 'EncodingPicker'
    $encoding.GetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $choice = Find-ByName 'CP437'
    if ($null -eq $choice) {
        throw 'CP437 selection is unavailable.'
    }
    $choice.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Button 'Open'
    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $openStatus = Status
    } while ($openStatus -like 'Opening*' -and (Get-Date) -lt $deadline)
    Record-Phase 'open-cp437'
}
if ($FixtureId -like 'txt-*' -and $openStatus -like "$($FixtureId):*") {
    $deadline = (Get-Date).AddSeconds(3)
    do {
        $items = $root.FindAll(
            $scope,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem)))
        if ($items.Count -gt 0) {
            $openToVisibleMilliseconds = $openClock.ElapsedMilliseconds
            break
        }
        Start-Sleep -Milliseconds 50
    } while ((Get-Date) -lt $deadline)
}
if (-not $expectedFailure -and $openStatus -notlike "$($FixtureId):*") {
    throw "Opening failed: $openStatus"
}

if ($expectedFailure) {
    # The normal result writer below records the opening error as a passing negative test.
}
elseif ($FixtureId -like 'txt-*') {
    if ($FixtureId -eq 'txt-long') {
        $list = $root.FindFirst(
            $scope,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::List)))
        $scroll = $null
        if ($null -ne $list -and $list.TryGetCurrentPattern(
                [System.Windows.Automation.ScrollPattern]::Pattern, [ref] $scroll)) {
            for ($index = 0; $index -lt 8; $index++) {
                $callClock = [System.Diagnostics.Stopwatch]::StartNew()
                $scroll.Scroll(
                    [System.Windows.Automation.ScrollAmount]::NoAmount,
                    [System.Windows.Automation.ScrollAmount]::LargeIncrement)
                $scrollCalls.Add($callClock.ElapsedMilliseconds)
            }
            Record-Phase 'scroll'
        }
    }
    Invoke-Button 'Capture'
    Record-Phase 'capture'
    Resize-Window
    Record-Phase 'resize'
    Invoke-Button 'Larger'
    Record-Phase 'larger'
    Invoke-Button 'Restore'
    Wait-Restore
    Record-Phase 'restore'
}
elseif ($FixtureId -like 'html-*' -or $FixtureId -eq 'redirect') {
    if ($FixtureId -eq 'html-hostile') {
        Invoke-Button 'Try fixture links'
        Record-Phase 'try-links'
    }
    Invoke-Button 'Jump to last marker'
    Record-Phase 'jump'
    Invoke-Button 'Capture'
    Record-Phase 'capture'
    Invoke-Button 'Back to top'
    Record-Phase 'top'
    Resize-Window
    Record-Phase 'resize'
    Invoke-Button 'Larger'
    Record-Phase 'larger'
    Invoke-Button 'Restore'
    Wait-Restore
    Record-Phase 'restore'
    $restoredMarker = $phases[$phases.Count - 1].marker
    if ($FixtureId -eq 'html-static' -and $restoredMarker -notlike 'Restored element: boss') {
        throw "Static HTML marker was not restored: $restoredMarker"
    }
    if ($FixtureId -eq 'html-layout' -and $restoredMarker -notlike 'Restored element: resume') {
        throw "Layout HTML marker was not restored: $restoredMarker"
    }
}
elseif ($FixtureId -like 'pdf-*') {
    if ($FixtureId -eq 'pdf-locked') {
        if ((Status) -notlike '*Password required*') {
            throw 'Protected PDF did not request a password.'
        }
        $box = Find-ByName 'PDF password'
        if ($null -eq $box) {
            throw 'Password field is unavailable.'
        }
        Enter-Password $box 'wrong'
        Invoke-Button 'Unlock PDF'
        $deadline = (Get-Date).AddSeconds(10)
        do {
            $wrongStatus = Page-Status
            if ($wrongStatus -like 'Could not unlock PDF.*') { break }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        if ($wrongStatus -notlike 'Could not unlock PDF.*') {
            throw "Wrong password did not return an error: $wrongStatus"
        }
        Record-Phase 'wrong-password'
        Enter-Password $box 'guide'
        Invoke-Button 'Unlock PDF'
        Wait-Page 1
        Record-Phase 'unlock'
    }
    elseif ($FixtureId -eq 'pdf-long') {
        for ($index = 0; $index -lt 5; $index++) {
            Invoke-Button 'Next page'
        }
        Wait-Page 6
        Record-Phase 'rapid-next'
        $pdfScroller = Find-ById 'PdfScroller'
        $pdfScrollPattern = $null
        if ($null -ne $pdfScroller -and $pdfScroller.TryGetCurrentPattern(
                [System.Windows.Automation.ScrollPattern]::Pattern, [ref] $pdfScrollPattern)) {
            $pdfScrollPattern.Scroll(
                [System.Windows.Automation.ScrollAmount]::NoAmount,
                [System.Windows.Automation.ScrollAmount]::LargeIncrement)
            Record-Phase 'scroll-page'
        }
        $capturedPage = Page-Status
        $capturedFraction = if ($capturedPage -match 'fraction ([0-9.]+)') {
            [double]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
        } else { throw 'PDF fraction was not reported.' }
        if ($capturedFraction -le 0.1) {
            throw "PDF page did not scroll far enough for the locator check: $capturedFraction"
        }
        Invoke-Button 'Capture'
        Record-Phase 'capture'
        Resize-Window
        Record-Phase 'resize'
        Invoke-Button 'Larger'
        Record-Phase 'zoom'
        Invoke-Button 'Previous page'
        Wait-Page 5
        Record-Phase 'previous'
        Invoke-Button 'Restore'
        Wait-Restore
        Wait-Page 6
        Record-Phase 'restore'
        $restoredPage = Page-Status
        $restoredFraction = if ($restoredPage -match 'fraction ([0-9.]+)') {
            [double]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
        } else { throw 'Restored PDF fraction was not reported.' }
        if ([Math]::Abs($restoredFraction - $capturedFraction) -gt 0.1) {
            throw "PDF fraction changed from $capturedFraction to $restoredFraction"
        }
        for ($index = 0; $index -lt 3; $index++) {
            Invoke-Button 'Next page'
            Wait-Page 7
            Invoke-Button 'Previous page'
            Wait-Page 6
        }
        Record-Phase 'repeated-turns'
    }
    else {
        Invoke-Button 'Capture'
        Record-Phase 'capture'
        Resize-Window
        Record-Phase 'resize'
        Invoke-Button 'Larger'
        Record-Phase 'zoom'
        Invoke-Button 'Restore'
        Wait-Restore
        Record-Phase 'restore'
    }
}

$process.Refresh()
if ($null -ne $originalRect) {
    [SmokeWindow]::MoveWindow(
        $process.MainWindowHandle, $originalRect.Left, $originalRect.Top,
        $originalRect.Right - $originalRect.Left,
        $originalRect.Bottom - $originalRect.Top, $true) | Out-Null
}
$process.Refresh()
$result = [ordered] @{
    fixture = $FixtureId
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    osBuild = [System.Environment]::OSVersion.Version.ToString()
    cpuArchitecture = $env:PROCESSOR_ARCHITECTURE
    processWorkingSetBytes = $process.WorkingSet64
    processPeakWorkingSetBytes = $process.PeakWorkingSet64
    passwordEntryMode = if ($FixtureId -eq 'pdf-locked') {
        'UI Automation ValuePattern'
    } else { $null }
    openToVisibleMilliseconds = $openToVisibleMilliseconds
    scrollCallsMilliseconds = $scrollCalls
    phases = $phases
}
$directory = Split-Path $ResultPath
New-Item -ItemType Directory -Force $directory | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content $ResultPath -Encoding UTF8
