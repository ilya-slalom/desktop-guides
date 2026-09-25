param(
    [Parameter(Mandatory = $true)]
    [string] $FixtureId,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
trap {
    $_ | Out-String | Set-Content ($ResultPath + '.error.txt') -Encoding UTF8
    exit 1
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$process = Get-Process DesktopGuides.App | Select-Object -First 1
if ($null -eq $process -or $process.MainWindowHandle -eq 0) {
    throw 'Run this script in the interactive Windows session with Desktop Guides open.'
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

function Status {
    $element = Find-ById 'StatusText'
    if ($null -eq $element) {
        throw 'Probe status is unavailable.'
    }
    return $element.Current.Name
}

function Record-Phase([string] $action) {
    Start-Sleep -Milliseconds 250
    $items = $root.FindAll($scope, [System.Windows.Automation.Condition]::TrueCondition)
    $realized = 0
    $pageText = $null
    $markerText = $null
    foreach ($item in $items) {
        if ($item.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
            $realized++
        }
        if ($item.Current.Name -like 'page */*') {
            $pageText = $item.Current.Name
        }
        if ($item.Current.Name -like 'Marker:*' -or $item.Current.Name -eq 'At top.') {
            $markerText = $item.Current.Name
        }
    }
    $phases.Add([ordered] @{
        action = $action
        status = Status
        page = $pageText
        marker = $markerText
        realizedListItems = $realized
    })
}

$phases = [System.Collections.Generic.List[object]]::new()
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
Invoke-Button 'Open'

$deadline = (Get-Date).AddSeconds(20)
do {
    Start-Sleep -Milliseconds 200
    $openStatus = Status
} while ($openStatus -like 'Opening*' -and (Get-Date) -lt $deadline)
Record-Phase 'open'
if ($openStatus -notlike "$($FixtureId):*") {
    throw "Opening failed: $openStatus"
}

if ($FixtureId -like 'txt-*') {
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
                $scroll.Scroll(
                    [System.Windows.Automation.ScrollAmount]::NoAmount,
                    [System.Windows.Automation.ScrollAmount]::LargeIncrement)
            }
            Record-Phase 'scroll'
        }
    }
    Invoke-Button 'Capture'
    Record-Phase 'capture'
    Invoke-Button 'Larger'
    Record-Phase 'larger'
    Invoke-Button 'Restore'
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
    Invoke-Button 'Larger'
    Record-Phase 'larger'
    Invoke-Button 'Restore'
    Record-Phase 'restore'
}
elseif ($FixtureId -like 'pdf-*') {
    Invoke-Button 'Next page'
    Invoke-Button 'Capture'
    Record-Phase 'capture-next'
    Invoke-Button 'Previous page'
    Record-Phase 'previous'
    Invoke-Button 'Restore'
    Record-Phase 'restore'
}

$process.Refresh()
$result = [ordered] @{
    fixture = $FixtureId
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    osBuild = [System.Environment]::OSVersion.Version.ToString()
    cpuArchitecture = $env:PROCESSOR_ARCHITECTURE
    processWorkingSetBytes = $process.WorkingSet64
    phases = $phases
}
$directory = Split-Path $ResultPath
New-Item -ItemType Directory -Force $directory | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content $ResultPath -Encoding UTF8
