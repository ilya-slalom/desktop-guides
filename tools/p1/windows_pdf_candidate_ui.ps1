param(
    [Parameter(Mandatory = $true)]
    [string] $ExePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath,

    [ValidateSet('Online', 'OutboundBlocked')]
    [string] $NetworkMode = 'Online'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$app = $null
$report = [ordered]@{
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    osBuild = [Environment]::OSVersion.Version.ToString()
    sessionId = (Get-Process -Id $PID).SessionId
    executableSha256 = (Get-FileHash $ExePath -Algorithm SHA256).Hash.ToLowerInvariant()
    appAssemblySha256 = (Get-FileHash (
        Join-Path (Split-Path $ExePath) 'DesktopGuides.App.dll'
    ) -Algorithm SHA256).Hash.ToLowerInvariant()
    networkMode = $NetworkMode
    success = $false
}

try {
    $env:DESKTOP_GUIDES_PDF_CANDIDATE = 'native'
    $app = Start-Process -FilePath $ExePath -PassThru
    $deadline = (Get-Date).AddSeconds(25)
    do {
        Start-Sleep -Milliseconds 250
        $app.Refresh()
    } while ($app.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
    if ($app.MainWindowHandle -eq 0) {
        throw 'The candidate app did not open an interactive window.'
    }

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
    function Status {
        $item = Find-ById 'StatusText'
        if (-not $item) { throw 'Probe status is missing.' }
        return $item.Current.Name
    }
    function Page-Status {
        $item = Find-ById 'PdfPageStatus'
        return $(if ($item) { $item.Current.Name } else { '' })
    }
    function Wait-Page([int] $page) {
        $until = (Get-Date).AddSeconds(20)
        do {
            $current = Page-Status
            if ($current -eq "Page $page of 200") { return }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $until)
        throw "Page $page did not appear; last status: $current"
    }
    function Open-Fixture([string] $fixture) {
        $picker = Find-ById 'FixturePicker'
        if (-not $picker) { throw 'Fixture picker is missing.' }
        $picker.GetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        $item = $null
        $until = (Get-Date).AddSeconds(5)
        do {
            $item = Find-ByName $fixture
            if (-not $item) { Start-Sleep -Milliseconds 100 }
        } while (-not $item -and (Get-Date) -lt $until)
        if (-not $item) { throw "Fixture '$fixture' is missing." }
        $item.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Invoke-Button 'Open'
        $until = (Get-Date).AddSeconds(25)
        do {
            Start-Sleep -Milliseconds 100
            $message = Status
        } while ($message -like 'Opening*' -and (Get-Date) -lt $until)
        $expected = $message -like "$($fixture): text candidate*" -or
            ($fixture -eq 'pdf-locked' -and
             $message -like 'pdf-locked: Password required*')
        if (-not $expected) {
            throw "Candidate could not open $fixture`: $message"
        }
    }
    function Document-Text {
        $element = Find-ById 'PdfDocumentText'
        if (-not $element) { throw 'The document text control is missing.' }
        $pattern = $null
        $hasPattern = $element.TryGetCurrentPattern(
            [System.Windows.Automation.TextPattern]::Pattern, [ref] $pattern)
        if (-not $hasPattern) {
            return [ordered]@{
                hasTextPattern = $false
                text = ''
                selection = ''
            }
        }
        $text = $pattern.DocumentRange.GetText(-1)
        $element.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('^a')
        Start-Sleep -Milliseconds 150
        $selected = @($pattern.GetSelection() | ForEach-Object { $_.GetText(-1) }) -join ''
        return [ordered]@{
            hasTextPattern = $true
            text = $text
            selection = $selected
        }
    }

    Open-Fixture 'pdf-access'
    $tagged = Document-Text
    $report.tagged = $tagged
    $report.taggedPageStatus = Page-Status
    if (-not $tagged.hasTextPattern -or
        $tagged.text -notlike '*Tagged guide paragraph for Narrator*' -or
        $tagged.selection -notlike '*Tagged guide paragraph for Narrator*') {
        throw 'Tagged PDF text was not exposed and keyboard-selectable through UIA.'
    }

    Open-Fixture 'pdf-scan'
    $scan = Document-Text
    $scanLabel = (Find-ById 'PdfTextStatus').Current.Name
    $report.scanned = $scan
    $report.scannedLabel = $scanLabel
    if ($scan.text.Length -ne 0 -or $scanLabel -notlike '*Image-only*') {
        throw 'Scanned PDF was not presented as an image-only page.'
    }

    Open-Fixture 'pdf-locked'
    $password = Find-ByName 'PDF password'
    if (-not $password) { throw 'Locked PDF password control is missing.' }
    $value = $null
    if (-not $password.TryGetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern, [ref] $value)) {
        throw 'Locked PDF password control has no writable UIA ValuePattern.'
    }
    $value.SetValue('wrong')
    Invoke-Button 'Unlock PDF'
    Start-Sleep -Milliseconds 250
    $report.wrongPasswordStatus = Page-Status
    if ($report.wrongPasswordStatus -notlike 'Incorrect password*') {
        throw 'Wrong PDF password did not leave a retryable error.'
    }
    $value.SetValue('guide')
    Invoke-Button 'Unlock PDF'
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        $lockedStatus = Page-Status
    } while ($lockedStatus -ne 'Page 1 of 1' -and (Get-Date) -lt $deadline)
    $report.lockedPageStatus = $lockedStatus
    $report.lockedText = (Document-Text).text
    if ($lockedStatus -ne 'Page 1 of 1' -or
        $report.lockedText -notlike '*Locked guide secret page*') {
        throw 'Locked PDF did not expose text and preview after the correct password.'
    }

    Open-Fixture 'pdf-long'
    Invoke-Button 'Next page'
    Wait-Page 2
    $report.longPage2Text = (Document-Text).text
    Invoke-Button 'Capture'
    Invoke-Button 'Next page'
    Wait-Page 3
    Invoke-Button 'Restore'
    Wait-Page 2
    $report.longRestoredPageStatus = Page-Status
    if ($report.longPage2Text -notlike '*page 2 of 200*') {
        throw 'Long PDF text did not follow its selected page.'
    }
    $app.Refresh()
    $report.longWorkingSetStartBytes = $app.WorkingSet64
    $report.longWorkingSetPeakBytes = $app.WorkingSet64
    for ($page = 3; $page -le 25; $page++) {
        Invoke-Button 'Next page'
        Wait-Page $page
        $app.Refresh()
        $report.longWorkingSetPeakBytes = [Math]::Max(
            $report.longWorkingSetPeakBytes, $app.WorkingSet64)
    }
    $report.longPageTurnCount = 23
    $report.longPage25Text = (Document-Text).text
    $app.Refresh()
    $report.longWorkingSetEndBytes = $app.WorkingSet64
    if ($report.longPage25Text -notlike '*page 25 of 200*') {
        throw 'Long PDF text did not follow repeated page turns.'
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
    $parent = Split-Path $ResultPath
    New-Item -ItemType Directory -Force $parent | Out-Null
    $report | ConvertTo-Json -Depth 6 | Set-Content $ResultPath -Encoding UTF8
}
if (-not $report.success) { exit 1 }
