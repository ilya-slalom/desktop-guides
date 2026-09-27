param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('empty', 'game-editor', 'game-editor-persisted',
        'normal', 'stale', 'long-list', 'switch-game',
        'switch-game-prepare', 'switch-game-loading', 'queue-guide',
        'queue-guide-write', 'queue-later-guide', 'later-guide-result',
        'later-guide-failed-result', 'queue-reader-render-error',
        'reader-render-error-observed', 'reader-render-error-result',
        'late-guide-after-close', 'waiting-handoff')]
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

    function Wait-VisibleById([string] $id) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = Find-ById $id
            if ($element -and -not $element.Current.IsOffscreen) {
                return $element
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected visible '$id'."
    }

    function Set-Text([string] $id, [string] $value) {
        $element = Wait-VisibleById $id
        $pattern = $element.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)
        $pattern.SetValue($value)
    }

    function Wait-EditorClosed {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = Find-ById 'GameTitleInput'
            if (-not $element -or $element.Current.IsOffscreen) {
                return
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw 'Game editor stayed open after Cancel.'
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
        $namedButton = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                'Back'),
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button))
        $deadline = (Get-Date).AddSeconds(15)
        do {
            foreach ($button in @(
                (Find-ById 'NavigationViewBackButton'),
                ($root.FindFirst($scope, $namedButton)))) {
                if ($button -and -not $button.Current.IsOffscreen) {
                    $pattern = $null
                    if ($button.TryGetCurrentPattern(
                        [System.Windows.Automation.InvokePattern]::Pattern,
                        [ref]$pattern)) {
                        $pattern.Invoke()
                        return
                    }
                }
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw 'Expected an invokable Back button.'
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

    function Select-DifferentGuideFromGame {
        $list = Find-ById 'GuideList'
        if (-not $list -or $list.Current.IsOffscreen) {
            throw 'Expected a visible guide list.'
        }
        $selection = $list.GetCurrentPattern(
            [System.Windows.Automation.SelectionPattern]::Pattern)
        $selected = @($selection.Current.GetSelection())
        $target = if ($selected.Count -gt 0 -and
            $selected[0].Current.Name -eq 'Route Test Guide') {
            'Blocked Write Guide'
        }
        else {
            'Route Test Guide'
        }
        Select-Element $target
        return $target
    }

    function Save-WindowScreenshot([string] $view) {
        Add-Type -AssemblyName System.Drawing
        $path = [System.IO.Path]::ChangeExtension($ResultPath, "$view.png")
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -ErrorAction Stop
        }
        $bounds = $root.Current.BoundingRectangle
        $width = [int][Math]::Ceiling($bounds.Width)
        $height = [int][Math]::Ceiling($bounds.Height)
        if ($width -lt 1 -or $height -lt 1) {
            throw 'The shell window has no visible screenshot bounds.'
        }
        $bitmap = [System.Drawing.Bitmap]::new($width, $height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen(
                [int]$bounds.X, [int]$bounds.Y, 0, 0, $bitmap.Size)
            $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
            if (-not (Test-Path -LiteralPath $path) -or
                (Get-Item -LiteralPath $path).Length -eq 0) {
                throw 'The shell screenshot was not saved.'
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
        Open-GuideFromGame 'Blocked Write Guide'
        [void](Wait-Name 'ShellStatus' 'Opening guide...')
        $report.phases += 'guide-action-started'
    }
    elseif ($Mode -eq 'queue-guide-write') {
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Open-GuideFromGame 'Blocked Write Guide'
        [void](Wait-Name 'ReaderHeading' 'Blocked Write Guide')
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        $report.phases += 'guide-reader-open-while-save-blocked'
    }
    elseif ($Mode -eq 'queue-later-guide') {
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        [void](Wait-SelectedGuide 'Route Test Guide')
        Invoke-Element (Wait-Name 'OpenSelectedGuide' 'Open Route Test Guide')
        [void](Wait-Name 'ShellStatus' 'Opening guide...')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Select-Element 'Blocked Write Guide'
        [void](Wait-SelectedGuide 'Blocked Write Guide')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Opening guide...')
        $report.phases += 'later-guide-selected-while-first-read-blocked'
    }
    elseif ($Mode -eq 'late-guide-after-close') {
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Opening guide...')
        [void](Wait-SelectedGuide 'Blocked Write Guide')
        Select-Element 'Route Test Guide'
        [void](Wait-SelectedGuide 'Route Test Guide')
        [void](Wait-Name 'ShellStatus' 'Opening guide...')
        $report.phases += 'guide-selected-after-close-request'
    }
    elseif ($Mode -eq 'queue-reader-render-error') {
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')
        Select-Element 'Route Test Game'
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        Open-GuideFromGame 'Route Test Guide'
        [void](Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'ShellStatus' ('Loading guide' + [char]0x2026))
        $report.phases += 'reader-route-open-before-render-fault'
    }
    elseif ($Mode -in @('later-guide-result', 'later-guide-failed-result',
        'reader-render-error-observed', 'reader-render-error-result',
        'switch-game-loading', 'switch-game')) {
        # These modes continue a shell left on Reader, Game, or Library.
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
    elseif ($Mode -eq 'game-editor') {
        $title = "Pok$([char]0x00E9)mon Mystery Dungeon"
        $renamed = "$title DX"
        Invoke-Element (Wait-Name 'AddGameButton' 'Add game')
        [void](Wait-Name 'GameTitleFeedback' 'Enter a title to continue.')
        $report.addGameScreenshot = Save-WindowScreenshot 'add-game'
        Set-Text 'GameTitleInput' '   '
        [void](Wait-Name 'GameTitleFeedback' 'Enter a title to continue.')
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        [void](Wait-VisibleById 'GameTitleInput')
        Set-Text 'GameTitleInput' 'Discarded game'
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-EditorClosed
        [void](Wait-Name 'LibraryEmpty' 'No games in your library.')
        $report.phases += 'cancel-and-invalid-title-leave-empty-library'

        Invoke-Element (Wait-Name 'AddGameButton' 'Add game')
        Set-Text 'GameTitleInput' " $title "
        Set-Text 'GamePlatformInput' ' Windows '
        Set-Text 'GameNotesInput' '  Explore the postgame.  '
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        [void](Wait-Name 'GameHeading' $title)
        [void](Wait-Name 'GamePlatform' 'Windows')
        [void](Wait-Name 'GameNotes' 'Explore the postgame.')
        $report.phases += 'keyboard-add-unicode-and-optional-fields'

        Invoke-Element (Wait-Name 'EditGameButton' 'Edit game')
        $input = Wait-VisibleById 'GameTitleInput'
        $value = $input.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)
        if ($value.Current.Value -ne $title) {
            throw 'Edit game did not load the existing title.'
        }
        $report.editGameScreenshot = Save-WindowScreenshot 'edit-game'
        Set-Text 'GameTitleInput' 'Discarded edit'
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-EditorClosed
        [void](Wait-Name 'GameHeading' $title)
        $report.phases += 'cancel-edit-preserves-game'

        Invoke-Element (Wait-Name 'EditGameButton' 'Edit game')
        Set-Text 'GameTitleInput' $renamed
        Set-Text 'GamePlatformInput' ''
        Set-Text 'GameNotesInput' ''
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        [void](Wait-Name 'GameHeading' $renamed)
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        $report.phases += 'edit-by-id-and-clear-optional-fields'

        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        Invoke-Element (Wait-Name 'AddGameButton' 'Add game')
        Set-Text 'GameTitleInput' $renamed
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        [void](Wait-Name 'GameHeading' $renamed)
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        $list = Wait-VisibleById 'GameList'
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $renamed)
        if ($list.FindAll($scope, $condition).Count -ne 2) {
            throw 'Library did not show two distinct games with the same title.'
        }
        $report.phases += 'duplicate-title-games-are-distinct'
    }
    elseif ($Mode -eq 'game-editor-persisted') {
        $renamed = "Pok$([char]0x00E9)mon Mystery Dungeon DX"
        $list = Wait-VisibleById 'GameList'
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $renamed)
        if ($list.FindAll($scope, $condition).Count -ne 2) {
            throw 'Edited and duplicate games were not persisted after relaunch.'
        }
        $report.phases += 'edited-and-duplicate-games-survive-relaunch'
    }
    elseif ($Mode -eq 'stale') {
        $resume = Find-ById 'ResumeGuide'
        if ($resume -and -not $resume.Current.IsOffscreen) {
            throw 'A stale last-guide ID exposed Resume.'
        }
        $report.phases += 'stale-resume-hidden'
    }
    elseif ($Mode -eq 'long-list') {
        $target = 'ZZZ Focus Target Guide'
        Select-Element 'Route Test Game'
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        $list = Find-ById 'GuideList'
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $target)
        foreach ($item in $list.FindAll($scope, $condition)) {
            if (-not $item.Current.IsOffscreen) {
                throw 'The tail guide was visible before scrolling.'
            }
        }
        $report.phases += 'tail-guide-outside-initial-viewport'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')
        Invoke-Element (Wait-Name 'ResumeGuide' "Resume $target")
        [void](Wait-Name 'ReaderHeading' $target)
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        [void](Wait-SelectedGuide $target)
        Wait-FocusedGuide $target
        [void](Wait-Name 'OpenSelectedGuide' "Open $target")
        $report.phases += 'virtualized-guide-back-focus'
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        [void](Wait-Name 'ReaderHeading' $target)
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        $report.phases += 'virtualized-guide-enter-reopen'
    }
    elseif ($Mode -eq 'switch-game-prepare') {
        $target = 'ZZZ Focus Target Guide'
        Invoke-Element (Wait-Name 'ResumeGuide' "Resume $target")
        [void](Wait-Name 'ReaderHeading' $target)
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        [void](Wait-SelectedGuide $target)
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')
        $report.phases += 'first-game-guide-retained-before-switch'
    }
    elseif ($Mode -eq 'switch-game-loading') {
        $target = 'ZZZ Focus Target Guide'
        $loading = 'Loading game' + [char]0x2026
        Select-Element 'Second Test Game'
        [void](Wait-Name 'GameHeading' $loading)
        [void](Wait-Name 'ShellStatus' $loading)
        $list = Find-ById 'GuideList'
        if (-not $list -or $list.Current.IsEnabled) {
            throw 'The guide list was available while the second game loaded.'
        }
        $oldCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $target)
        if ($list.FindAll($scope, $oldCondition).Count -ne 0) {
            throw 'The first game guide row was visible while the second game loaded.'
        }
        $open = Find-ById 'OpenSelectedGuide'
        if ($open -and -not $open.Current.IsOffscreen) {
            throw 'The first game selected-guide action was visible during loading.'
        }
        $report.phases += 'game-switch-loading-clears-old-guide'
    }
    elseif ($Mode -eq 'switch-game') {
        [void](Wait-Name 'GameHeading' 'Second Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        $list = Find-ById 'GuideList'
        $oldCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            'ZZZ Focus Target Guide')
        if ($list.FindAll($scope, $oldCondition).Count -ne 0) {
            throw 'The second game retained a guide row from the first game.'
        }
        $open = Find-ById 'OpenSelectedGuide'
        if ($open -and -not $open.Current.IsOffscreen) {
            throw 'The second game retained the first game selected-guide action.'
        }
        [void](Wait-GuideRow 'Second Test Guide')
        $report.phases += 'game-switch-clears-old-guide'
        Select-Element 'Second Test Guide'
        [void](Wait-Name 'ReaderHeading' 'Second Test Guide')
        [void](Wait-Name 'ReaderGameName' 'Second Test Game')
        $report.phases += 'game-switch-opens-current-guide'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Second Test Game')
        [void](Wait-SelectedGuide 'Second Test Guide')
        Invoke-Element (Wait-Name 'OpenSelectedGuide' 'Open Second Test Guide')
        [void](Wait-Name 'ReaderHeading' 'Second Test Guide')
        [void](Wait-Name 'ReaderGameName' 'Second Test Game')
        $report.phases += 'game-switch-uia-reopen'
    }
    elseif ($Mode -eq 'later-guide-result') {
        [void](Wait-Name 'ReaderHeading' 'Blocked Write Guide')
        [void](Wait-Name 'ReaderGameName' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        $report.phases += 'later-guide-opened'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        [void](Wait-SelectedGuide 'Blocked Write Guide')
        Wait-FocusedGuide 'Blocked Write Guide'
        $report.phases += 'later-guide-back-selection-and-focus'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')
        [void](Wait-Name 'ResumeGuide' 'Resume Blocked Write Guide')
        $report.phases += 'later-guide-persisted-for-resume'
    }
    elseif ($Mode -eq 'later-guide-failed-result') {
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'This guide is no longer in your library.')
        $reader = Find-ById 'ReaderHeading'
        if ($reader -and -not $reader.Current.IsOffscreen) {
            throw 'A Reader opened after the later guide was removed.'
        }
        $report.phases += 'later-guide-failed-without-opening-reader'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')
        $resume = Find-ById 'ResumeGuide'
        if ($resume -and -not $resume.Current.IsOffscreen) {
            throw 'A superseded guide became Resume after the later guide failed.'
        }
        $report.phases += 'superseded-guide-not-saved-as-resume'
    }
    elseif ($Mode -eq 'reader-render-error-observed') {
        [void](Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'ShellStatus' `
            'Could not load this view: Stored guide format is invalid.')
        $report.phases += 'reader-render-read-failed-on-reader-route'
    }
    elseif ($Mode -eq 'reader-render-error-result') {
        [void](Wait-Name 'ShellStatus' `
            'Could not load this view: Stored guide format is invalid.')
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Name 'ShellStatus' 'Library ready.')
        $resume = Find-ById 'ResumeGuide'
        if ($resume -and -not $resume.Current.IsOffscreen) {
            throw 'A guide whose Reader failed to render became Resume.'
        }
        $report.phases += 'reader-render-error-did-not-save-resume'
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
        $report.readerScreenshot = Save-WindowScreenshot 'reader'
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

        $openSelected = Wait-Name 'OpenSelectedGuide' "Open $ExpectedResumeGuide"
        $report.gameScreenshot = Save-WindowScreenshot 'game'
        Invoke-Element $openSelected
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        $report.phases += 'uia-reopen-selected-guide'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-SelectedGuide $ExpectedResumeGuide)
        Wait-FocusedGuide $ExpectedResumeGuide

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

        $rapidGuide = Select-DifferentGuideFromGame
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        [void](Wait-Name 'ShellStatus' 'Settings ready.')
        Go-Back
        [void](Wait-Name 'ReaderHeading' $rapidGuide)
        [void](Wait-Name 'ShellStatus' 'Guide details ready.')
        $report.phases += 'rapid-guide-settings-back-reader'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Name 'ShellStatus' 'Game ready.')
        $report.phases += 'library-game-reader-game'
        if ($rapidGuide -ne 'Route Test Guide') {
            Open-GuideFromGame 'Route Test Guide'
            [void](Wait-Name 'ReaderHeading' 'Route Test Guide')
            [void](Wait-Name 'ShellStatus' 'Guide details ready.')
            Go-Back
            [void](Wait-Name 'GameHeading' 'Route Test Game')
            [void](Wait-Name 'ShellStatus' 'Game ready.')
            $report.phases += 'restore-last-guide-for-relaunch'
        }
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
