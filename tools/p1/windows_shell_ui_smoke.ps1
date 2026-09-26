param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('empty', 'normal', 'stale', 'queue-guide', 'waiting-handoff')]
    [string] $Mode,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{32}$')]
    [string] $InvocationId,

    [Parameter(Mandatory = $true)]
    [int] $ProcessId,

    [Parameter(Mandatory = $true)]
    [int] $SessionId,

    [Parameter(Mandatory = $true)]
    [string] $ExecutablePath,

    [ValidateSet('Route Test Guide', 'Blocked Write Guide')]
    [string] $ExpectedResumeGuide = 'Route Test Guide',

    [ValidateRange(0, 5000)]
    [int] $ExitDelayMilliseconds = 0
)

$ErrorActionPreference = 'Stop'
$report = [ordered]@{
    mode = $Mode
    invocationId = $InvocationId
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    processId = $ProcessId
    sessionId = $SessionId
    exitDelayMilliseconds = $ExitDelayMilliseconds
    success = $false
    phases = @()
}

try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -Path (Join-Path $PSScriptRoot 'windows_shell_foreground_probe.cs')
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

    function Click-Element($element) {
        if (-not $element -or $element.Current.IsOffscreen) {
            throw 'Expected a visible element for pointer input.'
        }
        $bounds = $element.Current.BoundingRectangle
        $window = $root.Current.BoundingRectangle
        $left = [Math]::Max($bounds.Left, $window.Left)
        $top = [Math]::Max($bounds.Top, $window.Top)
        $right = [Math]::Min($bounds.Right, $window.Right)
        $bottom = [Math]::Min($bounds.Bottom, $window.Bottom)
        if ($right -le $left -or $bottom -le $top) {
            throw "Element has no visible pointer bounds: $bounds."
        }
        [DesktopGuidesForegroundProbe]::Click(
            [int][Math]::Floor(($left + $right) / 2),
            [int][Math]::Floor(($top + $bottom) / 2))
    }

    function Press-Enter($element) {
        if (-not $element -or $element.Current.IsOffscreen) {
            throw 'Expected a visible element for keyboard input.'
        }
        $element.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    }

    function Wait-PaneState([string] $expected) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $navigation = Find-ById 'Navigation'
            if ($navigation -and $navigation.Current.ItemStatus -eq $expected) {
                return
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected navigation pane state '$expected'."
    }

    function Find-PaneToggle {
        $toggle = Find-ById 'TogglePaneButton'
        if (-not $toggle) {
            throw 'Expected the NavigationView pane toggle button.'
        }
        return $toggle
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

    function Wait-SelectedGuide([string] $expected) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $list = Find-ById 'GuideList'
            if ($list -and -not $list.Current.IsOffscreen) {
                $selection = $list.GetCurrentPattern(
                    [System.Windows.Automation.SelectionPattern]::Pattern)
                foreach ($item in $selection.Current.GetSelection()) {
                    if ($item.Current.Name -eq $expected) {
                        return $item
                    }
                }
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected selected guide '$expected' after returning to Game."
    }

    function Wait-GameRow([string] $expected) {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $expected)
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $list = Find-ById 'GameList'
            if ($list -and -not $list.Current.IsOffscreen) {
                foreach ($item in $list.FindAll($scope, $condition)) {
                    try {
                        if ($item.Current.IsOffscreen) { continue }
                        [void]$item.GetCurrentPattern(
                            [System.Windows.Automation.SelectionItemPattern]::Pattern)
                        return $item
                    }
                    catch {
                        continue
                    }
                }
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected visible game row '$expected'."
    }

    function Wait-GuideRow([string] $expected) {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $expected)
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $list = Find-ById 'GuideList'
            if ($list -and -not $list.Current.IsOffscreen) {
                foreach ($item in $list.FindAll($scope, $condition)) {
                    try {
                        if ($item.Current.IsOffscreen) { continue }
                        [void]$item.GetCurrentPattern(
                            [System.Windows.Automation.SelectionItemPattern]::Pattern)
                        return $item
                    }
                    catch {
                        continue
                    }
                }
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected visible guide row '$expected'."
    }

    function Wait-FocusedGuide([string] $expected) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            if ($focused -and $focused.Current.Name -eq $expected) {
                return
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected keyboard focus on '$expected' after Back."
    }

    function Focus-OtherGuideWithoutSelection(
        [string] $selectedName, [string] $focusedName) {
        $selected = Wait-SelectedGuide $selectedName
        $other = Wait-GuideRow $focusedName
        $selectedTop = $selected.Current.BoundingRectangle.Top
        $otherTop = $other.Current.BoundingRectangle.Top
        if ($selectedTop -eq $otherTop) {
            throw 'Guide rows have the same vertical position.'
        }
        $keys = if ($otherTop -lt $selectedTop) { '^{UP}' }
                else { '^{DOWN}' }
        $selected.SetFocus()
        Wait-FocusedGuide $selectedName
        [System.Windows.Forms.SendKeys]::SendWait($keys)
        Wait-FocusedGuide $focusedName
        [void](Wait-SelectedGuide $selectedName)
    }

    function Activate-SelectedGuide([string] $expected) {
        $selected = Wait-SelectedGuide $expected
        $selected.SetFocus()
        Wait-FocusedGuide $expected
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    }

    function Open-GuideFromGame([string] $name) {
        $list = Find-ById 'GuideList'
        if (-not $list -or $list.Current.IsOffscreen) {
            throw 'Expected a visible guide list.'
        }
        $selection = $list.GetCurrentPattern(
            [System.Windows.Automation.SelectionPattern]::Pattern)
        foreach ($item in $selection.Current.GetSelection()) {
            if ($item.Current.Name -eq $name) {
                Activate-SelectedGuide $name
                return
            }
        }
        Select-Element $name
    }

    function Save-ReaderScreenshot {
        Add-Type -AssemblyName System.Drawing
        $path = [System.IO.Path]::ChangeExtension($ResultPath, 'reader.png')
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -ErrorAction Stop
        }
        $bounds = $root.Current.BoundingRectangle
        $width = [int][Math]::Ceiling($bounds.Width)
        $height = [int][Math]::Ceiling($bounds.Height)
        if ($width -lt 1 -or $height -lt 1) {
            throw 'The reader window has no visible screenshot bounds.'
        }
        $bitmap = [System.Drawing.Bitmap]::new($width, $height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen(
                [int]$bounds.X, [int]$bounds.Y, 0, 0, $bitmap.Size)
            $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
            if (-not (Test-Path -LiteralPath $path) -or
                (Get-Item -LiteralPath $path).Length -eq 0) {
                throw 'The Reader screenshot was not saved.'
            }
            return $path
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
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
        [void](Wait-Name 'ReaderGameName' 'Route Test Game')
        [void](Wait-Name 'ReaderFormat' 'TXT')
        [void](Wait-Name 'ReaderPlaceholder' `
            'Reading this guide is unavailable in this preview.')
        $commands = Find-ById 'ReaderCommands'
        if ($commands -and -not $commands.Current.IsOffscreen) {
            throw 'The preview reader exposed commands without an adapter.'
        }
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        Wait-PaneState 'Navigation pane closed'
        $report.readerScreenshot = Save-ReaderScreenshot
        $report.phases += 'resume-reader'

        $paneToggle = Find-PaneToggle
        Click-Element $paneToggle
        Wait-PaneState 'Navigation pane open'
        Click-Element (Find-PaneToggle)
        Wait-PaneState 'Navigation pane closed'
        $report.phases += 'reader-pane-toggle'

        $readerBack = Wait-Name 'ReaderBackToGame' 'Back to game'
        Click-Element $readerBack
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        [void](Wait-SelectedGuide $ExpectedResumeGuide)
        Wait-FocusedGuide $ExpectedResumeGuide
        $report.phases += 'reader-back-game'

        Click-Element (Wait-SelectedGuide $ExpectedResumeGuide)
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        $report.phases += 'pointer-reopen-selected-guide'
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-SelectedGuide $ExpectedResumeGuide)
        Wait-FocusedGuide $ExpectedResumeGuide
        Activate-SelectedGuide $ExpectedResumeGuide
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        $report.phases += 'keyboard-reopen-selected-guide'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-SelectedGuide $ExpectedResumeGuide)
        Wait-FocusedGuide $ExpectedResumeGuide
        $otherGuide = if ($ExpectedResumeGuide -eq 'Route Test Guide') {
            'Blocked Write Guide'
        }
        else {
            'Route Test Guide'
        }
        Focus-OtherGuideWithoutSelection $ExpectedResumeGuide $otherGuide
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        [void](Wait-Name 'ReaderHeading' $otherGuide)
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        $report.phases += 'focused-guide-enter'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Open-GuideFromGame $ExpectedResumeGuide
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        $report.phases += 'restore-route-guide'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')
        $report.phases += 'game-back-library'

        Click-Element (Wait-GameRow 'Route Test Game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        Click-Element (Wait-GuideRow 'Route Test Guide')
        [void](Wait-Name 'ReaderHeading' 'Route Test Guide')
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        Click-Element (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        $report.phases += 'pointer-library-game-reader-game'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')

        Press-Enter (Wait-GameRow 'Route Test Game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        Press-Enter (Wait-GuideRow 'Route Test Guide')
        [void](Wait-Name 'ReaderHeading' 'Route Test Guide')
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        $report.phases += 'keyboard-library-game-reader-game'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')

        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        $report.phases += 'settings'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')
        $report.phases += 'settings-back-library'

        Press-Enter (Wait-GameRow 'Route Test Game')
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        [void](Wait-Name 'ShellStatus' 'Settings ready.')
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        $report.phases += 'rapid-game-settings-back-game'

        Open-GuideFromGame 'Route Test Guide'
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        [void](Wait-Name 'ShellStatus' 'Settings ready.')
        Go-Back
        [void](Wait-Name 'ReaderHeading' 'Route Test Guide')
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
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
if ($ExitDelayMilliseconds -gt 0) {
    Start-Sleep -Milliseconds $ExitDelayMilliseconds
}
if (-not $report.success) { exit 1 }
