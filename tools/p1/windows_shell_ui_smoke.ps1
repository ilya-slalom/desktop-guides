param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('empty', 'normal', 'stale', 'queue-guide', 'waiting-handoff')]
    [string] $Mode,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath,

    [Parameter(Mandatory = $true)]
    [int] $ProcessId,

    [Parameter(Mandatory = $true)]
    [int] $SessionId,

    [Parameter(Mandatory = $true)]
    [string] $ExecutablePath,

    [ValidateSet('Route Test Guide', 'Blocked Write Guide')]
    [string] $ExpectedResumeGuide = 'Route Test Guide'
)

$ErrorActionPreference = 'Stop'
$report = [ordered]@{
    mode = $Mode
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    processId = $ProcessId
    sessionId = $SessionId
    success = $false
    phases = @()
}

try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $installedProcess = Get-CimInstance Win32_Process `
            -Filter "ProcessId = $ProcessId"
        if (-not $installedProcess) {
            throw "Expected production shell process $ProcessId exited."
        }
        if ($installedProcess.SessionId -ne $SessionId -or
            -not [string]::Equals($installedProcess.ExecutablePath,
                $ExecutablePath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Process $ProcessId is not the installed shell in session $SessionId."
        }
        $process = Get-Process -Id $ProcessId -ErrorAction Stop
        if ($process.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if ($process.MainWindowHandle -eq 0) {
        throw 'Production shell has no interactive window.'
    }

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

    function Select-Element([string] $name) {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $candidates = $root.FindAll($scope, $condition)
            foreach ($element in $candidates) {
                try {
                    if ($element.Current.IsOffscreen) { continue }
                    $pattern = $element.GetCurrentPattern(
                        [System.Windows.Automation.SelectionItemPattern]::Pattern)
                }
                catch {
                    continue
                }
                $pattern.Select()
                return
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        $listId = if ($name -eq 'Route Test Game') {
            'GameList'
        }
        elseif ($name -eq 'Route Test Guide') {
            'GuideList'
        }
        else {
            'Navigation'
        }
        $list = Find-ById $listId
        $bounds = if ($list) { $list.Current.BoundingRectangle.ToString() }
                  else { 'missing' }
        throw "Expected visible selectable '$name'; $listId bounds: $bounds."
    }

    function Go-Back {
        $button = Find-ByName 'Back'
        if (-not $button) { $button = Find-ById 'NavigationViewBackButton' }
        Invoke-Element $button
    }

    if ($Mode -eq 'waiting-handoff') {
        [void](Wait-Name 'ShellStatus' 'Waiting for previous window...')
        $report.phases += 'waiting-for-library-lease'
    }
    elseif ($Mode -eq 'queue-guide') {
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Select-Element 'Blocked Write Guide'
        [void](Wait-Name 'ShellStatus' 'Opening guide...')
        $report.phases += 'guide-action-started'
    }
    else {
        [void](Wait-Name 'ShellStatus' 'Library ready.')
        [void](Wait-Name 'LibraryHeading' 'Library')
        if (Find-ById 'FixturePicker') {
            throw 'The production shell exposes a P0 fixture picker.'
        }
        $report.phases += 'library'
    }

    if ($Mode -eq 'empty') {
        [void](Wait-Name 'LibraryEmpty' 'No games in your library.')
        $resume = Find-ById 'ResumeGuide'
        if ($resume -and -not $resume.Current.IsOffscreen) {
            throw 'An empty library exposed Resume.'
        }
        $report.phases += 'empty-library'
    }
    elseif ($Mode -eq 'stale') {
        $resume = Find-ById 'ResumeGuide'
        if ($resume -and -not $resume.Current.IsOffscreen) {
            throw 'A stale last-guide ID exposed Resume.'
        }
        $report.phases += 'stale-resume-hidden'
    }
    elseif ($Mode -eq 'normal') {
        $resume = Wait-Name 'ResumeGuide' "Resume $ExpectedResumeGuide"
        Invoke-Element $resume
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Name 'ShellStatus' 'Guide ready.')
        $report.phases += 'resume-reader'

        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        $report.phases += 'reader-back-game'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')
        $report.phases += 'game-back-library'

        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        $report.phases += 'settings'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')
        $report.phases += 'settings-back-library'

        Select-Element 'Route Test Game'
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        [void](Wait-Name 'ShellStatus' 'Settings ready.')
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        $report.phases += 'rapid-game-settings-back-game'

        Select-Element 'Route Test Guide'
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        [void](Wait-Name 'ShellStatus' 'Settings ready.')
        Go-Back
        [void](Wait-Name 'ReaderHeading' 'Route Test Guide')
        [void](Wait-Name 'ShellStatus' 'Guide ready.')
        $report.phases += 'rapid-guide-settings-back-reader'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
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
