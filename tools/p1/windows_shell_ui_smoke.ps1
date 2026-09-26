param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('normal', 'stale')]
    [string] $Mode,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
$report = [ordered]@{
    mode = $Mode
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    success = $false
    phases = @()
}

try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $process = Get-Process DesktopGuides.Production -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1
        if ($process) { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if (-not $process) { throw 'Production shell has no interactive window.' }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle(
        $process.MainWindowHandle)
    $scope = [System.Windows.Automation.TreeScope]::Descendants

    function Find-ById([string] $id) {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        return $root.FindFirst($scope, $condition)
    }

    function Find-ByName([string] $name) {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        return $root.FindFirst($scope, $condition)
    }

    function Wait-Name([string] $id, [string] $expected) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = Find-ById $id
            if ($element -and $element.Current.Name -eq $expected -and
                -not $element.Current.IsOffscreen) {
                return $element
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected visible '$id' named '$expected'."
    }

    function Invoke-Element($element) {
        if (-not $element) { throw 'Expected UI element is missing.' }
        $pattern = $element.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
    }

    function Select-Element($element) {
        if (-not $element) { throw 'Expected UI selection is missing.' }
        $pattern = $element.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        $pattern.Select()
    }

    function Go-Back {
        $button = Find-ByName 'Back'
        if (-not $button) { $button = Find-ById 'NavigationViewBackButton' }
        Invoke-Element $button
    }

    [void](Wait-Name 'ShellStatus' 'Library ready.')
    [void](Wait-Name 'LibraryHeading' 'Library')
    if (Find-ById 'FixturePicker') {
        throw 'The production shell exposes a P0 fixture picker.'
    }
    $report.phases += 'library'

    if ($Mode -eq 'stale') {
        $resume = Find-ById 'ResumeGuide'
        if ($resume -and -not $resume.Current.IsOffscreen) {
            throw 'A stale last-guide ID exposed Resume.'
        }
        $report.phases += 'stale-resume-hidden'
    }
    else {
        $resume = Wait-Name 'ResumeGuide' 'Resume Route Test Guide'
        Invoke-Element $resume
        [void](Wait-Name 'ReaderHeading' 'Route Test Guide')
        $report.phases += 'resume-reader'

        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        $report.phases += 'reader-back-game'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        $report.phases += 'game-back-library'

        Select-Element (Find-ByName 'Settings')
        [void](Wait-Name 'SettingsHeading' 'Settings')
        $report.phases += 'settings'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        $report.phases += 'settings-back-library'

        Select-Element (Find-ByName 'Route Test Game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Select-Element (Find-ByName 'Route Test Guide')
        [void](Wait-Name 'ReaderHeading' 'Route Test Guide')
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        $report.phases += 'library-game-reader-game'
    }
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
}
finally {
    $report | ConvertTo-Json -Depth 6 |
        Set-Content $ResultPath -Encoding UTF8
}
if (-not $report.success) { exit 1 }
