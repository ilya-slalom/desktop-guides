param(
    [Parameter(Mandatory = $true)]
    [string] $ExePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$app = $null
$report = [ordered]@{
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    osBuild = [Environment]::OSVersion.Version.ToString()
    sessionId = (Get-Process -Id $PID).SessionId
    executableSha256 = (Get-FileHash $ExePath -Algorithm SHA256).Hash.ToLowerInvariant()
    appAssemblySha256 = (Get-FileHash (
        Join-Path (Split-Path $ExePath) 'DesktopGuides.App.dll'
    ) -Algorithm SHA256).Hash.ToLowerInvariant()
    success = $false
}

try {
    $env:DESKTOP_GUIDES_PDF_CANDIDATE = 'web'
    $app = Start-Process -FilePath $ExePath -PassThru
    $deadline = (Get-Date).AddSeconds(25)
    do {
        Start-Sleep -Milliseconds 250
        $app.Refresh()
    } while ($app.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
    if ($app.MainWindowHandle -eq 0) { throw 'WebView2 PDF candidate window did not open.' }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
    $scope = [System.Windows.Automation.TreeScope]::Descendants
    function Find-ById([string] $id) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        return $root.FindFirst($scope, $condition)
    }
    function Find-ByName([string] $name) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        return $root.FindFirst($scope, $condition)
    }
    function Invoke-Button([string] $name) {
        $button = $null
        for ($attempt = 0; $attempt -lt 30 -and -not $button; $attempt++) {
            $button = Find-ByName $name
            if (-not $button) { Start-Sleep -Milliseconds 100 }
        }
        if (-not $button) { throw "Button '$name' is missing." }
        $button.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }

    $picker = Find-ById 'FixturePicker'
    if (-not $picker) { throw 'Fixture picker is missing.' }
    $picker.GetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $item = $null
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $item = Find-ByName 'pdf-access'
        if (-not $item) { Start-Sleep -Milliseconds 100 }
    } while (-not $item -and (Get-Date) -lt $deadline)
    if (-not $item) { throw 'Tagged PDF fixture is missing.' }
    $item.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Button 'Open'
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 100
        $status = (Find-ById 'StatusText').Current.Name
    } while ($status -like 'Opening*' -and (Get-Date) -lt $deadline)
    $report.openStatus = $status
    $report.loaded = $status -like 'pdf-access: WebView2 PDF candidate*'
    if ($report.loaded) {
        Start-Sleep -Seconds 2
        $all = $root.FindAll(
            $scope, [System.Windows.Automation.Condition]::TrueCondition)
        $report.automationElementCount = $all.Count
        $report.taggedParagraphInName = $false
        $report.taggedParagraphInTextPattern = $false
        $report.textPatternCount = 0
        $textPatterns = [System.Collections.Generic.List[object]]::new()
        foreach ($element in $all) {
            if ($element.Current.Name -like '*Tagged guide paragraph for Narrator*') {
                $report.taggedParagraphInName = $true
            }
            $pattern = $null
            if ($element.TryGetCurrentPattern(
                    [System.Windows.Automation.TextPattern]::Pattern, [ref] $pattern)) {
                $report.textPatternCount++
                $text = $pattern.DocumentRange.GetText(-1)
                $textPatterns.Add([ordered]@{
                    name = $element.Current.Name
                    textPreview = if ($text.Length -gt 120) { $text.Substring(0, 120) } else { $text }
                })
                if ($text -like '*Tagged guide paragraph for Narrator*') {
                    $report.taggedParagraphInTextPattern = $true
                }
            }
        }
        $report.textPatterns = $textPatterns
        Invoke-Button 'Capture'
        Start-Sleep -Milliseconds 200
        $report.captureStatus = (Find-ById 'StatusText').Current.Name
    }
    $report.success = $true
}
catch {
    $report.error = ($_ | Out-String)
}
finally {
    if ($app -and -not $app.HasExited) {
        Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Force (Split-Path $ResultPath) | Out-Null
    $report | ConvertTo-Json -Depth 6 | Set-Content $ResultPath -Encoding UTF8
}
if (-not $report.success) { exit 1 }
