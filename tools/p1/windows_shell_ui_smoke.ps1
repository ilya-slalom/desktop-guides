param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('empty', 'game-editor', 'game-editor-persisted',
        'normal', 'design-language', 'stale', 'long-list', 'switch-game',
        'switch-game-prepare', 'switch-game-loading', 'queue-guide',
        'queue-guide-write', 'prepare-game-editor-close',
        'queue-game-editor', 'queue-later-guide', 'later-guide-result',
        'later-guide-failed-result', 'queue-reader-render-error',
        'reader-render-error-observed', 'reader-render-error-result',
        'late-guide-after-close', 'waiting-handoff', 'material')]
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
    [int] $ExitDelayMilliseconds = 0,

    [ValidateRange(0, 400)]
    [int] $ExpectedScalePercent = 0,

    [ValidateSet('Mica', 'Acrylic', 'Solid')]
    [string] $ExpectedMaterial = 'Mica',

    [ValidateSet('', 'Mica', 'Acrylic', 'Solid')]
    [string] $SwitchToMaterial = ''
)

$ErrorActionPreference = 'Stop'
$report = [ordered]@{
    mode = $Mode
    invocationId = $InvocationId
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    processId = $ProcessId
    sessionId = $SessionId
    exitDelayMilliseconds = $ExitDelayMilliseconds
    expectedScalePercent = $ExpectedScalePercent
    success = $false
    phases = @()
}
$lastStatusSequence = -1L

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

    function Find-RawById([string] $id) {
        $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
        $pending = [System.Collections.Generic.Queue[System.Windows.Automation.AutomationElement]]::new()
        $pending.Enqueue($root)
        while ($pending.Count -gt 0) {
            $element = $pending.Dequeue()
            try {
                if ($element.Current.AutomationId -eq $id) {
                    return $element
                }
                $child = $walker.GetFirstChild($element)
                while ($child) {
                    $pending.Enqueue($child)
                    $child = $walker.GetNextSibling($child)
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] {
                # The window can redraw while the raw tree is traversed.
            }
        }
        return $null
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

    function Wait-Status(
        [string] $expected,
        [switch] $AllowHidden) {
        $transient = $expected -in @(
            'Library ready.',
            'Game ready.',
            'Guide details ready.',
            'Settings ready.')
        $lastObserved = 'status probe was not found'
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $probe = Find-RawById 'ShellContent'
            $status = if ($probe) { $probe.Current.ItemStatus } else { '' }
            if ($probe) {
                $lastObserved = "status probe contained '$status'"
            }
            $separator = $status.IndexOf('|')
            $sequence = 0L
            $message = ''
            if ($separator -gt 0 -and
                [long]::TryParse(
                    $status.Substring(0, $separator), [ref] $sequence)) {
                $message = $status.Substring($separator + 1)
            }
            if ($message -eq $expected -and
                $sequence -gt $script:lastStatusSequence) {
                if ($transient -or $AllowHidden) {
                    $script:lastStatusSequence = $sequence
                    return $probe
                }
                $visibleStatus = Find-ById 'ShellStatus'
                if ($visibleStatus -and
                    $visibleStatus.Current.Name -eq $expected -and
                    -not $visibleStatus.Current.IsOffscreen) {
                    $script:lastStatusSequence = $sequence
                    return $probe
                }
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected shell status '$expected'; $lastObserved."
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

    function Wait-HiddenById([string] $id) {
        $deadline = (Get-Date).AddSeconds(10)
        do {
            $element = Find-ById $id
            if (-not $element -or $element.Current.IsOffscreen) {
                return
            }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        throw "Expected '$id' to hide."
    }

    function Wait-EnabledById([string] $id) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = Find-ById $id
            if ($element -and -not $element.Current.IsOffscreen -and
                $element.Current.IsEnabled) {
                return $element
            }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        throw "Expected enabled '$id'."
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
        $toggle = Find-ById 'PART_PaneToggleButton'
        if (-not $toggle) {
            throw 'Expected the TitleBar pane toggle button.'
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
                (Find-ById 'PART_BackButton'),
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

    function Count-GameRows([string] $name) {
        $list = Wait-VisibleById 'GameList'
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $count = 0
        foreach ($item in $list.FindAll($scope, $condition)) {
            $pattern = $null
            if ($item.TryGetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern,
                [ref]$pattern)) {
                $count++
            }
        }
        return $count
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

    function Resize-ShellWindow([int] $width, [int] $height) {
        $workingArea = [System.Windows.Forms.SystemInformation]::WorkingArea
        $targetWidth = [Math]::Min($width, $workingArea.Width)
        $targetHeight = [Math]::Min($height, $workingArea.Height)
        $targetX = $workingArea.X +
            [int][Math]::Floor(($workingArea.Width - $targetWidth) / 2)
        $targetY = $workingArea.Y +
            [int][Math]::Floor(($workingArea.Height - $targetHeight) / 2)
        [DesktopGuidesForegroundProbe]::Resize(
            $process.MainWindowHandle,
            $targetX, $targetY, $targetWidth, $targetHeight)
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $bounds = $root.Current.BoundingRectangle
            if ([Math]::Abs($bounds.Width - $targetWidth) -le 8 -and
                [Math]::Abs($bounds.Height - $targetHeight) -le 8) {
                Start-Sleep -Milliseconds 300
                return
            }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        throw "Shell did not resize to $targetWidth x $targetHeight."
    }

    function Assert-HeadingLevel([string] $id, [int] $expected) {
        $element = Wait-VisibleById $id
        $value = $element.GetCurrentPropertyValue(
            [System.Windows.Automation.AutomationElement]::HeadingLevelProperty)
        if ([int]$value -ne $expected) {
            throw "Expected '$id' heading level $expected, got $value."
        }
    }

    function Assert-InsideWindow([string] $id) {
        $element = Wait-VisibleById $id
        $bounds = $element.Current.BoundingRectangle
        $window = $root.Current.BoundingRectangle
        $tolerance = 2
        if ($bounds.Left -lt ($window.Left - $tolerance) -or
            $bounds.Top -lt ($window.Top - $tolerance) -or
            $bounds.Right -gt ($window.Right + $tolerance) -or
            $bounds.Bottom -gt ($window.Bottom + $tolerance)) {
            throw "'$id' is clipped outside the shell window: $bounds."
        }
    }

    function Assert-ReachesWindowRightEdge([string] $id) {
        $element = if ($id -eq 'ShellContent') {
            Find-RawById $id
        }
        else {
            Wait-VisibleById $id
        }
        if (-not $element) {
            throw "Expected '$id' in the UI Automation tree."
        }
        $bounds = $element.Current.BoundingRectangle
        $window = $root.Current.BoundingRectangle
        $dpi = [DesktopGuidesForegroundProbe]::Dpi($process.MainWindowHandle)
        $tolerance = [Math]::Ceiling(16 * ($dpi / 96.0))
        if ([Math]::Abs($window.Right - $bounds.Right) -gt $tolerance) {
            throw "'$id' leaves an unexpected right gutter: $bounds in $window."
        }
    }

    function Assert-NoOverlap([string] $firstId, [string] $secondId) {
        $first = Wait-VisibleById $firstId
        $second = Wait-VisibleById $secondId
        $a = $first.Current.BoundingRectangle
        $b = $second.Current.BoundingRectangle
        $overlap = $a.Left -lt $b.Right -and $a.Right -gt $b.Left -and
            $a.Top -lt $b.Bottom -and $a.Bottom -gt $b.Top
        if ($overlap) {
            throw "'$firstId' overlaps '$secondId': $a and $b."
        }
    }

    function Focus-And-Verify([string] $id) {
        $element = Wait-EnabledById $id
        $element.SetFocus()
        $deadline = (Get-Date).AddSeconds(10)
        do {
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            if ($focused -and $focused.Current.AutomationId -eq $id) {
                return
            }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        throw "Expected keyboard focus on '$id'."
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

    function Get-ComboSelection([string] $id) {
        $combo = Wait-VisibleById $id
        $selection = $combo.GetCurrentPattern(
            [System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
        if ($selection.Length -ne 1) { throw "$id has $($selection.Length) selected items." }
        return $selection[0].Current.Name
    }

    function Select-ComboItem([string] $id, [string] $name) {
        $combo = Wait-VisibleById $id
        $expand = $combo.GetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $expand.Expand()
        $item = $null
        $deadline = (Get-Date).AddSeconds(5)
        do {
            $itemCondition = [System.Windows.Automation.AndCondition]::new(
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $name),
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::ListItem))
            # WinUI hosts the open drop-down in a popup outside the ComboBox subtree.
            $item = $combo.FindFirst($scope, $itemCondition)
            if (-not $item) { $item = $root.FindFirst($scope, $itemCondition) }
            if (-not $item) { Start-Sleep -Milliseconds 100 }
        } while (-not $item -and (Get-Date) -lt $deadline)
        if (-not $item) { throw "$id has no item named $name." }
        $item.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        if ($expand.Current.ExpandCollapseState -ne
                [System.Windows.Automation.ExpandCollapseState]::Collapsed) {
            $expand.Collapse()
        }
    }

    function Assert-ShellForeground {
        if ([DesktopGuidesForegroundProbe]::GetForegroundWindow() -ne
                $process.MainWindowHandle) {
            throw 'The shell was not the foreground window; its backdrop would be inactive.'
        }
    }

    if ($Mode -eq 'waiting-handoff') {
        [void](Wait-Status 'Waiting for previous window...')
        $report.phases += 'waiting-for-library-lease'
    }
    elseif ($Mode -eq 'queue-guide') {
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Open-GuideFromGame 'Blocked Write Guide'
        [void](Wait-Status 'Opening guide...')
        $report.phases += 'guide-action-started'
    }
    elseif ($Mode -eq 'queue-guide-write') {
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Open-GuideFromGame 'Blocked Write Guide'
        [void](Wait-Name 'ReaderHeading' 'Blocked Write Guide')
        [void](Wait-Status 'Guide details ready.')
        $report.phases += 'guide-reader-open-while-save-blocked'
    }
    elseif ($Mode -eq 'queue-game-editor') {
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        $edit = Wait-Name 'EditGameButton' 'Edit game'
        Invoke-Element $edit
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $edit = Find-ById 'EditGameButton'
            if ($edit -and -not $edit.Current.IsEnabled) {
                break
            }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        if (-not $edit -or $edit.Current.IsEnabled) {
            throw 'Edit game did not enter its queued repository read.'
        }
        $report.phases += 'game-editor-read-started'
    }
    elseif ($Mode -eq 'queue-later-guide') {
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide 'Route Test Guide')
        Invoke-Element (Wait-Name 'OpenSelectedGuide' 'Open Route Test Guide')
        [void](Wait-Status 'Opening guide...')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Select-Element 'Blocked Write Guide'
        [void](Wait-SelectedGuide 'Blocked Write Guide')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        $report.phases += 'later-guide-selected-while-first-read-blocked'
    }
    elseif ($Mode -eq 'late-guide-after-close') {
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Opening guide...' -AllowHidden)
        [void](Wait-SelectedGuide 'Blocked Write Guide')
        Select-Element 'Route Test Guide'
        [void](Wait-SelectedGuide 'Route Test Guide')
        $report.phases += 'guide-selected-after-close-request'
    }
    elseif ($Mode -eq 'queue-reader-render-error') {
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        Select-Element 'Route Test Game'
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        Open-GuideFromGame 'Route Test Guide'
        [void](Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Status ('Loading guide' + [char]0x2026))
        $report.phases += 'reader-route-open-before-render-fault'
    }
    elseif ($Mode -in @('later-guide-result', 'later-guide-failed-result',
        'reader-render-error-observed', 'reader-render-error-result',
        'switch-game-loading', 'switch-game')) {
        # These modes continue a shell left on Reader, Game, or Library.
    }
    else {
        [void](Wait-Status 'Library ready.')
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
    elseif ($Mode -eq 'design-language') {
        $designGame = 'The Legend of Zelda: Tears of the Kingdom'
        $designGuide = 'Complete Story Walkthrough'
        $wideWidth = 1500
        $narrowWidth = 600
        $windowHeight = 720

        $dpi = [DesktopGuidesForegroundProbe]::Dpi($process.MainWindowHandle)
        $report.windowDpi = $dpi
        $report.scalePercent = [int][Math]::Round(($dpi / 96.0) * 100)
        if ($ExpectedScalePercent -gt 0 -and
            [Math]::Abs($report.scalePercent - $ExpectedScalePercent) -gt 1) {
            throw "Expected $ExpectedScalePercent% display scale, got $($report.scalePercent)%."
        }

        Resize-ShellWindow $wideWidth $windowHeight
        [void](Wait-HiddenById 'ShellStatus')
        Assert-InsideWindow 'AppTitleBar'
        Assert-InsideWindow 'PART_PaneToggleButton'
        Assert-InsideWindow 'PART_BackButton'
        Assert-ReachesWindowRightEdge 'ShellContent'
        Assert-HeadingLevel 'LibraryHeading' 1
        Assert-InsideWindow 'LibraryHeading'
        Assert-InsideWindow 'AddGameButton'
        Focus-And-Verify 'AddGameButton'
        $report.libraryWideScreenshot =
            Save-WindowScreenshot 'library-wide'
        $report.phases += 'library-wide-focus-and-heading'

        Resize-ShellWindow $narrowWidth $windowHeight
        Assert-InsideWindow 'LibraryHeading'
        Assert-InsideWindow 'AddGameButton'
        Assert-NoOverlap 'PART_PaneToggleButton' 'LibraryHeading'
        Assert-NoOverlap 'PART_PaneToggleButton' 'AddGameButton'
        Assert-NoOverlap 'PART_BackButton' 'LibraryHeading'
        [void](Wait-GameRow $designGame)
        $report.libraryNarrowScreenshot =
            Save-WindowScreenshot 'library-narrow'
        $report.phases += 'library-narrow'

        Resize-ShellWindow $wideWidth $windowHeight
        Press-Enter (Wait-GameRow $designGame)
        [void](Wait-Name 'GameHeading' $designGame)
        [void](Wait-Name 'GamePlatform' 'Nintendo Switch')
        [void](Wait-Name 'GameNotes' (
            'Keep the main story, shrine routes, and armor upgrades together ' +
            'for quick reference while playing.'))
        [void](Wait-Status 'Game ready.')
        [void](Wait-HiddenById 'ShellStatus')
        Assert-HeadingLevel 'GameHeading' 1
        Assert-InsideWindow 'GameHeading'
        Assert-InsideWindow 'EditGameButton'
        Assert-ReachesWindowRightEdge 'ShellContent'
        $report.gameWideScreenshot = Save-WindowScreenshot 'game-wide'
        $report.phases += 'game-wide-full-width-metadata'

        Resize-ShellWindow $narrowWidth $windowHeight
        Assert-InsideWindow 'GameHeading'
        Assert-InsideWindow 'EditGameButton'
        Assert-InsideWindow 'GameNotesScroll'
        Assert-NoOverlap 'PART_PaneToggleButton' 'GameHeading'
        Assert-NoOverlap 'PART_PaneToggleButton' 'EditGameButton'
        Assert-NoOverlap 'PART_BackButton' 'GameHeading'
        [void](Wait-GuideRow $designGuide)
        $report.gameNarrowScreenshot = Save-WindowScreenshot 'game-narrow'
        $report.phases += 'game-narrow-long-title'

        Resize-ShellWindow $wideWidth $windowHeight
        Press-Enter (Wait-GuideRow $designGuide)
        [void](Wait-Name 'ReaderHeading' $designGuide)
        [void](Wait-Name 'ReaderGameName' $designGame)
        [void](Wait-Name 'ReaderFormat' 'TXT')
        [void](Wait-Status 'Guide details ready.')
        [void](Wait-HiddenById 'ShellStatus')
        Assert-HeadingLevel 'ReaderHeading' 1
        Assert-InsideWindow 'ReaderHeading'
        Assert-InsideWindow 'ReaderBackToGame'
        $report.readerWideScreenshot = Save-WindowScreenshot 'reader-wide'
        $report.phases += 'reader-wide'

        Resize-ShellWindow $narrowWidth $windowHeight
        Assert-InsideWindow 'ReaderHeading'
        Assert-InsideWindow 'ReaderBackToGame'
        Assert-InsideWindow 'ReaderFormat'
        Assert-InsideWindow 'ReaderPlaceholder'
        Assert-NoOverlap 'PART_PaneToggleButton' 'ReaderBackToGame'
        Assert-NoOverlap 'PART_BackButton' 'ReaderBackToGame'
        $report.readerNarrowScreenshot =
            Save-WindowScreenshot 'reader-narrow'
        $report.phases += 'reader-narrow'

        Resize-ShellWindow $wideWidth $windowHeight
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        [void](Wait-Name 'LibraryStorageSettingsCard' (
            'Library storage. Your library is stored on this device.'))
        [void](Wait-Status 'Settings ready.')
        [void](Wait-HiddenById 'ShellStatus')
        Assert-HeadingLevel 'SettingsHeading' 1
        Resize-ShellWindow $narrowWidth $windowHeight
        Assert-InsideWindow 'SettingsHeading'
        Assert-InsideWindow 'LibraryStorageSettingsCard'
        Assert-NoOverlap 'PART_PaneToggleButton' 'SettingsHeading'
        Assert-NoOverlap 'PART_BackButton' 'SettingsHeading'
        $report.settingsNarrowScreenshot =
            Save-WindowScreenshot 'settings-narrow'
        $report.phases += 'settings-narrow'

        Resize-ShellWindow $wideWidth $windowHeight
        Assert-InsideWindow 'SettingsHeading'
        Assert-InsideWindow 'LibraryStorageSettingsCard'
        $report.settingsWideScreenshot =
            Save-WindowScreenshot 'settings-wide'
        $report.phases += 'settings-wide'
    }
    elseif ($Mode -eq 'material') {
        $designGame = 'The Legend of Zelda: Tears of the Kingdom'
        $designGuide = 'Complete Story Walkthrough'
        Resize-ShellWindow 1500 720
        Select-Element 'Settings'
        [void](Wait-Name 'WindowMaterialSettingsCard' (
            'Window background. Choose how much of your desktop shows behind the app.'))
        $selected = Get-ComboSelection 'WindowMaterialSelector'
        if ($selected -ne $ExpectedMaterial) {
            throw "Expected $ExpectedMaterial window background, found $selected."
        }
        $report.phases += "material-$selected-restored"

        if ($SwitchToMaterial) {
            Select-ComboItem 'WindowMaterialSelector' $SwitchToMaterial
            [void](Wait-Status "Window background set to $SwitchToMaterial.")
            $selected = $SwitchToMaterial
            $report.phases += "material-switched-$selected"
        }
        [void](Wait-HiddenById 'ShellStatus')
        Assert-ShellForeground
        $report.settingsScreenshot = Save-WindowScreenshot "material-$selected-settings"

        Select-Element 'Library'
        [void](Wait-GameRow $designGame)
        Start-Sleep -Milliseconds 400
        Assert-ShellForeground
        $report.libraryScreenshot = Save-WindowScreenshot "material-$selected-library"
        # windows_shell_install.ps1 measures the pane/content strip from these bounds.
        $window = $root.Current.BoundingRectangle
        $report.libraryBounds = [ordered]@{
            windowLeft = $window.Left
            windowHeight = $window.Height
            contentLeft = (Find-RawById 'ShellContent').Current.BoundingRectangle.Left
        }

        Press-Enter (Wait-GameRow $designGame)
        [void](Wait-Name 'GameHeading' $designGame)
        Invoke-Element (Wait-EnabledById 'EditGameButton')
        [void](Wait-VisibleById 'GameTitleInput')
        Assert-ShellForeground
        $report.dialogScreenshot = Save-WindowScreenshot "material-$selected-edit-game"
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-EditorClosed

        Press-Enter (Wait-GuideRow $designGuide)
        [void](Wait-Name 'ReaderHeading' $designGuide)
        [void](Wait-Status 'Guide details ready.')
        [void](Wait-HiddenById 'ShellStatus')
        Assert-ShellForeground
        $report.readerScreenshot = Save-WindowScreenshot "material-$selected-reader"
        $report.material = $selected
        $report.phases += "material-$selected-captured"
    }
    elseif ($Mode -eq 'prepare-game-editor-close') {
        Select-Element 'Route Test Game'
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        $report.phases += 'game-ready-for-editor-close'
    }
    elseif ($Mode -eq 'game-editor') {
        $title = "Pok$([char]0x00E9)mon Mystery Dungeon"
        $renamed = "$title DX"
        Invoke-Element (Wait-EnabledById 'AddGameButton')
        [void](Wait-Name 'GameTitleFeedback' 'Enter a title to continue.')
        $report.addGameScreenshot = Save-WindowScreenshot 'add-game'
        Set-Text 'GameTitleInput' '   '
        [void](Wait-Name 'GameTitleFeedback' 'Enter a title to continue.')
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        [void](Wait-VisibleById 'GameTitleInput')
        $boundaryTitle = ' ' + ('T' * 160) + ' '
        Set-Text 'GameTitleInput' $boundaryTitle
        [void](Wait-Name 'GameTitleFeedback' '160 / 160 characters')
        $boundaryInput = Wait-VisibleById 'GameTitleInput'
        $boundaryValue = $boundaryInput.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)
        if ($boundaryValue.Current.Value.Length -ne 162) {
            throw 'The title input truncated a valid trimmed boundary value.'
        }
        Set-Text 'GameTitleInput' 'Discarded game'
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-EditorClosed
        [void](Wait-Name 'LibraryEmpty' 'No games in your library.')
        $report.phases += 'cancel-and-invalid-title-leave-empty-library'

        Invoke-Element (Wait-EnabledById 'AddGameButton')
        Set-Text 'GameTitleInput' " $title "
        Set-Text 'GamePlatformInput' ' Windows '
        Set-Text 'GameNotesInput' '  Explore the postgame.  '
        [void](Wait-Name 'GameTitleFeedback' "$($title.Length) / 160 characters")
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        [void](Wait-Name 'GameHeading' $title)
        [void](Wait-Name 'GamePlatform' 'Windows')
        [void](Wait-Name 'GameNotes' 'Explore the postgame.')
        $report.phases += 'keyboard-add-unicode-and-optional-fields'

        Invoke-Element (Wait-EnabledById 'EditGameButton')
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

        Invoke-Element (Wait-EnabledById 'EditGameButton')
        Set-Text 'GameTitleInput' $renamed
        Set-Text 'GamePlatformInput' ''
        $longNotes = ('Note ' * 399) + 'Notes'
        Set-Text 'GameNotesInput' $longNotes
        [void](Wait-Name 'GameTitleFeedback' "$($renamed.Length) / 160 characters")
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        [void](Wait-Name 'GameHeading' $renamed)
        [void](Wait-Status 'Game ready.')
        $notes = Wait-VisibleById 'GameNotes'
        if ($notes.Current.Name.Length -ne 2000) {
            throw 'The saved 2,000-character game note was not rendered in full.'
        }
        $notesScroll = Wait-VisibleById 'GameNotesScroll'
        if ($notesScroll.Current.BoundingRectangle.Height -ge
            ($root.Current.BoundingRectangle.Height / 3)) {
            throw 'Long game notes exceeded their bounded metadata region.'
        }
        $emptyGuide = Wait-Name 'GameEmpty' 'No guides in this game.'
        if ($emptyGuide.Current.BoundingRectangle.Top -le
            $notesScroll.Current.BoundingRectangle.Bottom) {
            throw 'Long game notes overlap the empty guide state.'
        }
        Start-Sleep -Milliseconds 500
        $report.longNotesScreenshot = Save-WindowScreenshot 'long-notes'
        $report.phases += 'long-notes-preserve-guide-area'

        Invoke-Element (Wait-EnabledById 'EditGameButton')
        Set-Text 'GameNotesInput' ''
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        [void](Wait-Name 'GameHeading' $renamed)
        [void](Wait-Status 'Game ready.')
        $report.phases += 'edit-by-id-and-clear-optional-fields'

        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        Invoke-Element (Wait-EnabledById 'AddGameButton')
        Set-Text 'GameTitleInput' $renamed
        [void](Wait-Name 'GameTitleFeedback' "$($renamed.Length) / 160 characters")
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        [void](Wait-Name 'GameHeading' $renamed)
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        $count = Count-GameRows $renamed
        if ($count -ne 2) {
            throw "Library showed $count selectable duplicate-title games, expected two."
        }
        $report.phases += 'duplicate-title-games-are-distinct'
    }
    elseif ($Mode -eq 'game-editor-persisted') {
        $renamed = "Pok$([char]0x00E9)mon Mystery Dungeon DX"
        $count = Count-GameRows $renamed
        if ($count -ne 2) {
            throw "Relaunched library showed $count duplicate-title games, expected two."
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
        [void](Wait-Status 'Game ready.')
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
        [void](Wait-Status 'Library ready.')
        Invoke-Element (Wait-Name 'ResumeGuide' "Resume $target")
        [void](Wait-Name 'ReaderHeading' $target)
        [void](Wait-Status 'Guide details ready.')
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide $target)
        Wait-FocusedGuide $target
        [void](Wait-Name 'OpenSelectedGuide' "Open $target")
        $report.phases += 'virtualized-guide-back-focus'
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        [void](Wait-Name 'ReaderHeading' $target)
        [void](Wait-Status 'Guide details ready.')
        $report.phases += 'virtualized-guide-enter-reopen'
    }
    elseif ($Mode -eq 'switch-game-prepare') {
        $target = 'ZZZ Focus Target Guide'
        Invoke-Element (Wait-Name 'ResumeGuide' "Resume $target")
        [void](Wait-Name 'ReaderHeading' $target)
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide $target)
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        $report.phases += 'first-game-guide-retained-before-switch'
    }
    elseif ($Mode -eq 'switch-game-loading') {
        $target = 'ZZZ Focus Target Guide'
        $loading = 'Loading game' + [char]0x2026
        Select-Element 'Second Test Game'
        [void](Wait-Name 'GameHeading' $loading)
        [void](Wait-Status $loading)
        $list = Find-ById 'GuideList'
        if ($list -and -not $list.Current.IsOffscreen -and
            $list.Current.IsEnabled) {
            throw 'The guide list was available while the second game loaded.'
        }
        $oldCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $target)
        if ($list -and $list.FindAll($scope, $oldCondition).Count -ne 0) {
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
        [void](Wait-Status 'Game ready.')
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
        [void](Wait-Status 'Guide details ready.')
        $report.phases += 'later-guide-opened'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide 'Blocked Write Guide')
        Wait-FocusedGuide 'Blocked Write Guide'
        $report.phases += 'later-guide-back-selection-and-focus'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        [void](Wait-Name 'ResumeGuide' 'Resume Blocked Write Guide')
        $report.phases += 'later-guide-persisted-for-resume'
    }
    elseif ($Mode -eq 'later-guide-failed-result') {
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'This guide is no longer in your library.')
        $reader = Find-ById 'ReaderHeading'
        if ($reader -and -not $reader.Current.IsOffscreen) {
            throw 'A Reader opened after the later guide was removed.'
        }
        $report.phases += 'later-guide-failed-without-opening-reader'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        $resume = Find-ById 'ResumeGuide'
        if ($resume -and -not $resume.Current.IsOffscreen) {
            throw 'A superseded guide became Resume after the later guide failed.'
        }
        $report.phases += 'superseded-guide-not-saved-as-resume'
    }
    elseif ($Mode -eq 'reader-render-error-observed') {
        [void](Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Status `
            'Could not load this view: Stored guide format is invalid.')
        $report.phases += 'reader-render-read-failed-on-reader-route'
    }
    elseif ($Mode -eq 'reader-render-error-result') {
        [void](Wait-Status `
            'Could not load this view: Stored guide format is invalid.')
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
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
        [void](Wait-Status 'Guide details ready.')
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
        Invoke-Element $readerBack
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide $ExpectedResumeGuide)
        Wait-FocusedGuide $ExpectedResumeGuide
        $report.phases += 'reader-back-game'

        $openSelected = Wait-Name 'OpenSelectedGuide' "Open $ExpectedResumeGuide"
        $report.gameScreenshot = Save-WindowScreenshot 'game'
        Invoke-Element $openSelected
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Status 'Guide details ready.')
        $report.phases += 'uia-reopen-selected-guide'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide $ExpectedResumeGuide)
        Wait-FocusedGuide $ExpectedResumeGuide

        Click-Element (Wait-SelectedGuide $ExpectedResumeGuide)
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Status 'Guide details ready.')
        $report.phases += 'pointer-reopen-selected-guide'
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide $ExpectedResumeGuide)
        Wait-FocusedGuide $ExpectedResumeGuide
        Activate-SelectedGuide $ExpectedResumeGuide
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Status 'Guide details ready.')
        $report.phases += 'keyboard-reopen-selected-guide'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
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
        [void](Wait-Status 'Guide details ready.')
        $report.phases += 'focused-guide-enter'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Open-GuideFromGame $ExpectedResumeGuide
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Status 'Guide details ready.')
        $report.phases += 'restore-route-guide'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        $report.phases += 'game-back-library'

        Click-Element (Wait-GameRow 'Route Test Game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        Click-Element (Wait-GuideRow 'Route Test Guide')
        [void](Wait-Name 'ReaderHeading' 'Route Test Guide')
        [void](Wait-Status 'Guide details ready.')
        Click-Element (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        $report.phases += 'pointer-library-game-reader-game'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')

        Press-Enter (Wait-GameRow 'Route Test Game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        Press-Enter (Wait-GuideRow 'Route Test Guide')
        [void](Wait-Name 'ReaderHeading' 'Route Test Guide')
        [void](Wait-Status 'Guide details ready.')
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        $report.phases += 'keyboard-library-game-reader-game'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')

        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        $report.phases += 'settings'
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        $report.phases += 'settings-back-library'

        Press-Enter (Wait-GameRow 'Route Test Game')
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        [void](Wait-Status 'Settings ready.')
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        $report.phases += 'rapid-game-settings-back-game'

        $rapidGuide = Select-DifferentGuideFromGame
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        [void](Wait-Status 'Settings ready.')
        Go-Back
        [void](Wait-Name 'ReaderHeading' $rapidGuide)
        [void](Wait-Status 'Guide details ready.')
        $report.phases += 'rapid-guide-settings-back-reader'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        $report.phases += 'library-game-reader-game'
        if ($rapidGuide -ne 'Route Test Guide') {
            Open-GuideFromGame 'Route Test Guide'
            [void](Wait-Name 'ReaderHeading' 'Route Test Guide')
            [void](Wait-Status 'Guide details ready.')
            Go-Back
            [void](Wait-Name 'GameHeading' 'Route Test Game')
            [void](Wait-Status 'Game ready.')
            $report.phases += 'restore-last-guide-for-relaunch'
        }
    }
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
    if ($Mode -eq 'game-editor' -and $root) {
        try {
            foreach ($id in @('ShellStatus', 'GameHeading',
                'GameTitleFeedback', 'GameSaveError')) {
                $element = Find-ById $id
                if ($element) {
                    $report["failure$id"] = [ordered]@{
                        name = $element.Current.Name
                        visible = -not $element.Current.IsOffscreen
                    }
                }
            }
            $report.failureScreenshot = Save-WindowScreenshot 'failure'
        }
        catch {
            $report.failureInspectionError = $_ | Out-String
        }
    }
}
finally {
    $report | ConvertTo-Json -Depth 6 |
        Set-Content $ResultPath -Encoding UTF8
}
if ($ExitDelayMilliseconds -gt 0) {
    Start-Sleep -Milliseconds $ExitDelayMilliseconds
}
if (-not $report.success) { exit 1 }
