param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('empty', 'game-editor', 'game-editor-persisted',
        'normal', 'design-language', 'stale', 'long-list', 'switch-game',
        'switch-game-prepare', 'switch-game-loading', 'queue-guide',
        'queue-guide-write', 'prepare-game-editor-close',
        'queue-game-editor', 'queue-later-guide', 'later-guide-result',
        'later-guide-failed-result', 'queue-reader-render-error',
        'reader-render-error-observed', 'reader-render-error-result',
        'late-guide-after-close', 'waiting-handoff', 'material', 'catalog', 'catalog-facts', 'library-search',
        'provider-none', 'provider-offline', 'provider-settings',
        'import-preview', 'import-publish', 'import-duplicate-copy', 'import-duplicate-open',
        'remove-guide-cancel', 'remove-guide',
        'provider-live', 'provider-remove')]
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
    [string] $SwitchToMaterial = '',

    [string] $IgdbCredentialFile = '',

    [string] $SteamGridDbCredentialFile = '',

    # The existing guide a duplicate preview must name.
    [string] $ExpectedGuideTitle = '',

    # The provider failure Refresh and search should report: no saved
    # credentials, or credentials saved while the network is blocked.
    [ValidateSet('NotConfigured', 'Unavailable')]
    [string] $ExpectedProviderFailure = 'NotConfigured'
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
    . (Join-Path $PSScriptRoot 'windows_provider_credentials.ps1')
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
        [string[]] $expected,
        [switch] $AllowHidden) {
        $transient = @($expected | Where-Object { $_ -in @(
            'Library ready.',
            'Game ready.',
            'Guide details ready.',
            'Settings ready.') }).Count -gt 0
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
            if ($message -in $expected -and
                $sequence -gt $script:lastStatusSequence) {
                if ($message -eq 'Library ready.') { Assert-Absent 'LibraryLoading' }
                if ($transient -or $AllowHidden) {
                    $script:lastStatusSequence = $sequence
                    return $probe
                }
                $visibleStatus = Find-ById 'ShellStatus'
                if ($visibleStatus -and
                    $visibleStatus.Current.Name -eq $message -and
                    -not $visibleStatus.Current.IsOffscreen) {
                    $script:lastStatusSequence = $sequence
                    return $probe
                }
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected shell status '$($expected -join "' or '")'; $lastObserved. Last consumed sequence: $script:lastStatusSequence."
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

    function Wait-FocusedId([string] $id) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            if ($focused -and $focused.Current.AutomationId -eq $id) { return }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected keyboard focus on '$id'."
    }

    # Focus in an AutoSuggestBox lands on its inner edit box.
    function Wait-FocusWithin([string] $id) {
        $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = [System.Windows.Automation.AutomationElement]::FocusedElement
            while ($element) {
                if ($element.Current.AutomationId -eq $id) { return }
                $element = $walker.GetParent($element)
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected keyboard focus inside '$id'."
    }

    function Get-GuideRowNames {
        $list = Find-ById 'GuideList'
        if (-not $list -or $list.Current.IsOffscreen) {
            throw 'Expected a visible guide list.'
        }
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        $names = @($list.FindAll($scope, $condition) | ForEach-Object { $_.Current.Name })
        return ,$names
    }

    function Wait-GuideRowCount([int] $expected) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $names = Get-GuideRowNames
            if ($names.Count -eq $expected) { return ,$names }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected $expected guide rows, found $($names.Count): $($names -join ', ')."
    }

    function Wait-FocusedGameRow([string] $expected) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            if ($focused -and
                $focused.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
                (-not $expected -or $focused.Current.Name -eq $expected)) {
                return $focused
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected keyboard focus on game row '$expected'."
    }

    function Get-GameListItems {
        $list = Wait-VisibleById 'GameList'
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        return @($list.FindAll($scope, $condition))
    }

    # UIA lists the rows that have a container, including the off-screen
    # cache, and reports empty bounds for the off-screen ones, so don't filter
    # on bounds. The name check skips unnamed items and, defensively, any peer
    # named after its .NET type; CI has shown none of those.
    function Test-RealizedGameRow($item) {
        $name = $item.Current.Name
        return [bool]($name -and $name -notlike 'DesktopGuides.*')
    }

    function Get-RealizedGameRows {
        return @(Get-GameListItems | Where-Object { Test-RealizedGameRow $_ })
    }

    # Realized, named ListItems of a list, in display order.
    function Get-ListRows([string] $id) {
        $list = Wait-VisibleById $id
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        return @($list.FindAll($scope, $condition) | Where-Object { Test-RealizedGameRow $_ })
    }

    # $expected holds @(name, help-text pattern) pairs, in display order.
    # Names must match exactly; help text is matched with -like.
    function Assert-RowFacts([string] $id, $expected) {
        $deadline = (Get-Date).AddSeconds(10)
        do {
            $actual = @(Get-ListRows $id | ForEach-Object {
                [ordered]@{ Name = $_.Current.Name; HelpText = $_.Current.HelpText } })
            $matched = $actual.Count -eq $expected.Count
            for ($i = 0; $matched -and $i -lt $expected.Count; $i++) {
                $matched = $actual[$i].Name -eq $expected[$i][0] -and
                    $actual[$i].HelpText -like $expected[$i][1]
            }
            if ($matched) { return $actual }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        $shown = ($actual | ForEach-Object { "$($_.Name) [$($_.HelpText)]" }) -join '; '
        throw "$id rows were: $shown"
    }

    # Counts screen pixels within 24 per channel of $rgb in the artwork column
    # of a row. UIA bounds and CopyFromScreen both use physical pixels.
    function Measure-RowArtwork($row, [int[]] $rgb, [double] $scale) {
        Add-Type -AssemblyName System.Drawing
        $bounds = $row.Current.BoundingRectangle
        $width = [int][Math]::Min($bounds.Width, 120 * $scale)
        $height = [int]$bounds.Height
        if ($width -lt 1 -or $height -lt 1) {
            throw "Game row '$($row.Current.Name)' has no visible bounds."
        }
        $bitmap = [System.Drawing.Bitmap]::new($width, $height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen(
                [int]$bounds.X, [int]$bounds.Y, 0, 0, $bitmap.Size)
            $hits = 0
            for ($y = 0; $y -lt $height; $y++) {
                for ($x = 0; $x -lt $width; $x++) {
                    $pixel = $bitmap.GetPixel($x, $y)
                    if ([Math]::Abs($pixel.R - $rgb[0]) -le 24 -and
                        [Math]::Abs($pixel.G - $rgb[1]) -le 24 -and
                        [Math]::Abs($pixel.B - $rgb[2]) -le 24) {
                        $hits++
                    }
                }
            }
            return $hits
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }

    function Wait-RowArtwork($row, [int[]] $rgb, [double] $scale, [double] $minimum) {
        $deadline = (Get-Date).AddSeconds(10)
        do {
            $count = Measure-RowArtwork $row $rgb $scale
            if ($count -ge $minimum) { return $count }
            Start-Sleep -Milliseconds 250
        } while ((Get-Date) -lt $deadline)
        return $count
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

    function Assert-EffectiveMaterial([string] $material) {
        $status = (Wait-VisibleById 'WindowMaterialSelector').Current.ItemStatus
        if ($status -ne $material) {
            throw "Expected the $material window background to be applied, found '$status'."
        }
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

    $providerFailureMessages = @{
        NotConfigured = 'Add IGDB credentials in Settings to search.'
        Unavailable = "Can't reach IGDB. Check your connection."
    }

    function Open-ProviderSettings {
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        Expand-ProviderSettings
    }

    function Expand-ProviderSettings {
        $expander = Wait-VisibleById 'ProviderSettingsExpander'
        $pattern = $null
        if ($expander.TryGetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
            [ref]$pattern)) {
            if ($pattern.Current.ExpandCollapseState -ne
                [System.Windows.Automation.ExpandCollapseState]::Expanded) {
                $pattern.Expand()
            }
        }
        else {
            Click-Element $expander
        }
        [void](Wait-VisibleById 'IgdbClientIdInput')
    }

    function Assert-ProviderSettingsExpanded {
        $expander = Wait-VisibleById 'ProviderSettingsExpander'
        $pattern = $null
        if ($expander.TryGetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
            [ref]$pattern) -and
            $pattern.Current.ExpandCollapseState -ne
                [System.Windows.Automation.ExpandCollapseState]::Expanded) {
            throw 'The provider settings card was not expanded.'
        }
        [void](Wait-VisibleById 'IgdbClientIdInput')
    }

    # Types a value into a PasswordBox. It checks focus first, so a value
    # is never typed into the wrong control, and its errors never include
    # the value.
    function Enter-Secret([string] $id, [string] $value, [switch] $Replace) {
        $element = Wait-EnabledById $id
        $element.SetFocus()
        Start-Sleep -Milliseconds 150
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        if (-not $focused -or $focused.Current.AutomationId -ne $id) {
            throw "Focus did not reach $id, so nothing was typed."
        }
        try {
            if ($Replace) { [System.Windows.Forms.SendKeys]::SendWait('^a{DEL}') }
            [System.Windows.Forms.SendKeys]::SendWait((ConvertTo-SendKeysLiteral $value))
        }
        catch {
            throw "Typing into $id failed."
        }
    }

    function Assert-Absent([string] $id) {
        $element = Find-ById $id
        if ($element -and -not $element.Current.IsOffscreen) {
            throw "'$id' was visible."
        }
    }

    function Assert-NoRemoteConnections([string] $view) {
        $loopback = @('127.0.0.1', '::1', '0.0.0.0', '::')
        $remote = @(Get-NetTCPConnection -OwningProcess $ProcessId -ErrorAction SilentlyContinue |
            Where-Object { $_.State -ne 'Listen' -and $_.RemoteAddress -notin $loopback })
        $report.remoteConnections = $remote.Count
        if ($remote.Count -gt 0) {
            throw "The $view opened $($remote.Count) non-loopback connection(s)."
        }
    }

    function Open-AddGameSearch {
        Invoke-Element (Wait-EnabledById 'AddGameButton')
        [void](Wait-VisibleById 'GameSearchInput')
        Assert-Absent 'GameTitleInput'
    }

    # The AutoSuggestBox exposes its text through its inner edit box.
    function Get-SearchEdit([string] $id) {
        $box = Wait-VisibleById $id
        $edit = $box.FindFirst($scope,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Edit))
        if (-not $edit) { throw "The search box '$id' has no editable text." }
        return $edit
    }

    function Get-SearchPattern([string] $id) {
        $box = Wait-VisibleById $id
        $pattern = $null
        if (-not $box.TryGetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
            $pattern = (Get-SearchEdit $id).GetCurrentPattern(
                [System.Windows.Automation.ValuePattern]::Pattern)
        }
        return $pattern
    }

    function Set-SearchQuery([string] $query, [string] $id = 'GameSearchInput') {
        (Get-SearchPattern $id).SetValue($query)
    }

    function Get-SearchText([string] $id) {
        return (Get-SearchPattern $id).Current.Value
    }

    function Search-Library([string] $query, [string] $status, $rows) {
        Set-SearchQuery $query 'LibrarySearchInput'
        [void](Wait-Status $status -AllowHidden)
        [void](Assert-RowFacts 'GameList' $rows)
    }

    function Get-SearchResults {
        $list = Find-ById 'GameSearchResults'
        if (-not $list -or $list.Current.IsOffscreen) { return @() }
        return @($list.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem)))
    }

    function Search-Games([string] $query) {
        Set-SearchQuery $query
        Invoke-Element (Wait-EnabledById 'GameSearchButton')
        $deadline = (Get-Date).AddSeconds(30)
        do {
            Start-Sleep -Milliseconds 250
            # The busy row is a StackPanel with no automation peer; its
            # Cancel button shows exactly while work runs.
            $busy = Find-ById 'GameSearchCancel'
            $running = $busy -and -not $busy.Current.IsOffscreen
            $status = Find-ById 'GameSearchStatus'
            $hasStatus = $status -and -not $status.Current.IsOffscreen -and
                $status.Current.Name
            $results = Get-SearchResults
            if (-not $running -and ($results.Count -gt 0 -or $hasStatus)) {
                return $results
            }
        } while ((Get-Date) -lt $deadline)
        throw "Search for '$query' did not finish."
    }

    function Activate-Item($item) {
        $pattern = $null
        if ($item.TryGetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
            $pattern.Invoke()
        }
        else {
            Click-Element $item
        }
    }

    # Escape can land in the search box instead of the dialog, so use the
    # dialog's Close button.
    function Close-AddGameSearch {
        Invoke-Element (Wait-EnabledById 'CloseButton')
        Wait-HiddenById 'GameSearchInput'
    }

    function Get-FactsName {
        $facts = Wait-VisibleById 'GameFacts'
        if ($facts.Current.Name) { return $facts.Current.Name }
        $text = $facts.FindFirst($scope,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text))
        if ($text) { return $text.Current.Name }
        return ''
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
        $search = Find-ById 'LibrarySearchInput'
        if (-not $search -or $search.Current.IsEnabled) {
            throw 'An empty library left search enabled.'
        }
        Assert-Absent 'LibrarySearchClear'
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
        Assert-EffectiveMaterial $selected
        $report.phases += "material-$selected-restored"

        if ($SwitchToMaterial) {
            Select-ComboItem 'WindowMaterialSelector' $SwitchToMaterial
            [void](Wait-Status "Window background set to $SwitchToMaterial.")
            $selected = $SwitchToMaterial
            Assert-EffectiveMaterial $selected
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
        # The dialog's own peer spans the smoke layer, so anchor on the primary
        # button inside the dialog panel's command area.
        $primary = (Wait-VisibleById 'PrimaryButton').Current.BoundingRectangle
        $report.dialogBounds = [ordered]@{
            buttonLeft = $primary.Left - $window.Left
            buttonBottom = $primary.Bottom - $window.Top
        }
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
    elseif ($Mode -eq 'catalog-facts') {
        [void](Wait-Name 'LibraryHeading' 'Library')
        $report.libraryRows = Assert-RowFacts 'GameList' @(
            @('Zeta Archive Game', 'Windows, Manual, 1 guide'),
            @('Facts Test Game', 'PC, IGDB, 4 guides'),
            @('Empty Test Game', 'Manual, No guides'))
        $report.phases += 'library-facts'
        [void](Wait-HiddenById 'ShellStatus')
        Save-WindowScreenshot 'library-facts'

        Select-Element 'Facts Test Game'
        [void](Wait-Name 'GameHeading' 'Facts Test Game')
        [void](Wait-Status 'Game ready.')
        # The host's culture sets the exact time and date (Ruling 9).
        $report.guideRows = Assert-RowFacts 'GuideList' @(
            @('Main Story Walkthrough', 'Web page (HTML), In progress, opened today at *'),
            @('Collectibles Map', 'PDF, about 45 percent, opened yesterday'),
            @('Weapon Upgrade Guide', 'Text (TXT), Completed, opened on *'),
            @('Achievement Checklist', 'Text (TXT), Not started'))
        $report.phases += 'guide-facts'
        [void](Wait-HiddenById 'ShellStatus')
        Save-WindowScreenshot 'game-facts'

        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
    }
    elseif ($Mode -eq 'library-search') {
        # Built from code points: Windows PowerShell 5.1 reads this file as ANSI.
        $zeta = 'Zeta Archive Game'
        $pokemon = 'Pok' + [char]0x00E9 + 'mon Crystal'
        $okami = [string][char]0x014C + 'kami HD'
        $dragonPrefix = [string]::new([char[]]@(0x30C9, 0x30E9, 0x30B4, 0x30F3))
        $dragon = $dragonPrefix + [string]::new([char[]]@(0x30AF, 0x30A8, 0x30B9, 0x30C8)) + 'XI'
        $fullwidthXi = [string]::new([char[]]@(0xFF38, 0xFF29))
        $zetaRow = @($zeta, 'Manual, 1 guide')
        $zetaMatch = @($zeta, 'Manual, 1 guide, Guide: Complete Walkthrough')
        $pokemonRow = @($pokemon, 'Manual, No guides')
        $okamiRow = @($okami, 'Manual, No guides')
        $dragonRow = @($dragon, 'Manual, No guides')
        $allRows = @($zetaRow, $pokemonRow, $okamiRow, $dragonRow)

        [void](Wait-Name 'LibraryHeading' 'Library')
        $report.libraryRows = Assert-RowFacts 'GameList' $allRows
        Assert-Absent 'LibrarySearchClear'

        # Real keystrokes for the ASCII query; ValuePattern for the rest (Ruling 10).
        # The AutoSuggestBox itself is not focusable; its inner edit box is.
        [void](Wait-EnabledById 'LibrarySearchInput')
        (Get-SearchEdit 'LibrarySearchInput').SetFocus()
        [void](Wait-FocusWithin 'LibrarySearchInput')
        [System.Windows.Forms.SendKeys]::SendWait('POKEMON')
        [void](Wait-Status '1 of 4 games match.' -AllowHidden)
        [void](Assert-RowFacts 'GameList' @(,$pokemonRow))
        [void](Wait-VisibleById 'LibrarySearchClear')
        $report.phases += 'search-case-accent'

        Search-Library 'okami' '1 of 4 games match.' @(,$okamiRow)
        Search-Library $dragonPrefix '1 of 4 games match.' @(,$dragonRow)
        Search-Library $fullwidthXi '1 of 4 games match.' @(,$dragonRow)
        $report.phases += 'search-non-ascii'

        Search-Library 'walkthrough' '1 of 4 games match.' @(,$zetaMatch)
        $report.phases += 'search-guide-title'
        [void](Wait-HiddenById 'ShellStatus')
        Save-WindowScreenshot 'search-results'

        Set-SearchQuery 'zzzz' 'LibrarySearchInput'
        [void](Wait-Status 'No games match.' -AllowHidden)
        [void](Wait-Name 'LibraryNoResults' 'No games or guides match "zzzz".')
        Wait-HiddenById 'GameList'
        [void](Wait-VisibleById 'LibrarySearchClear')
        $report.phases += 'search-no-results'
        [void](Wait-HiddenById 'ShellStatus')
        Save-WindowScreenshot 'no-results'

        Invoke-Element (Wait-VisibleById 'LibrarySearchClear')
        [void](Assert-RowFacts 'GameList' $allRows)
        [void](Wait-FocusWithin 'LibrarySearchInput')
        Wait-HiddenById 'LibrarySearchClear'
        if ((Get-SearchText 'LibrarySearchInput') -ne '') {
            throw 'Clear search left text in the box.'
        }
        $report.phases += 'search-clear'

        Search-Library 'walkthrough' '1 of 4 games match.' @(,$zetaMatch)
        Select-Element $zeta
        [void](Wait-Name 'GameHeading' $zeta)
        [void](Wait-Status 'Game ready.')
        [void](Assert-RowFacts 'GuideList' @(,@('Complete Walkthrough', 'Text (TXT), Not started')))
        $report.phases += 'search-not-started'

        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        if ((Get-SearchText 'LibrarySearchInput') -ne 'walkthrough') {
            throw 'Back to the Library dropped the search query.'
        }
        [void](Assert-RowFacts 'GameList' @(,$zetaMatch))
        $report.phases += 'search-kept-after-back'

        Assert-NoRemoteConnections 'library search'
        $report.phases += 'search-no-provider-traffic'
    }
    elseif ($Mode -eq 'catalog') {
        $catalogStarted = Get-Date
        $longTitle = 'Catalog A ' + ('W' * 150)
        $shortTitle = 'Catalog B Short'
        # Built from code points: Windows PowerShell 5.1 reads this file as ANSI.
        $lastTitle = [string]::new([char[]]@(
            0x30BC, 0x30EB, 0x30C0, 0x306E, 0x4F1D, 0x8AAC))
        $wideWidth = 1500
        $narrowWidth = 600
        $windowHeight = 720
        $realizedLimit = 80

        $workingArea = [System.Windows.Forms.SystemInformation]::WorkingArea
        $report.workingArea = "$($workingArea.Width)x$($workingArea.Height)"
        $dpi = [DesktopGuidesForegroundProbe]::Dpi($process.MainWindowHandle)
        $report.windowDpi = $dpi
        $report.scalePercent = [int][Math]::Round(($dpi / 96.0) * 100)

        Resize-ShellWindow $wideWidth $windowHeight
        [void](Wait-HiddenById 'ShellStatus')
        $longRow = Wait-GameRow $longTitle
        $shortRow = Wait-GameRow $shortTitle
        if ($longRow.Current.HelpText -ne 'PC, IGDB, No guides') {
            throw "The long-title row's help text was '$($longRow.Current.HelpText)'."
        }
        $report.phases += 'catalog-row-facts'
        [void](Wait-GameRow 'Catalog C Corrupt Art')
        [void](Wait-GameRow 'Catalog C Missing Art 0')
        $longHeight = $longRow.Current.BoundingRectangle.Height
        $shortHeight = $shortRow.Current.BoundingRectangle.Height
        $report.longRowHeight = $longHeight
        $report.shortRowHeight = $shortHeight
        if ($longHeight -gt ($shortHeight + 1)) {
            throw "The long-title row is taller than a short row: $longHeight versus $shortHeight."
        }
        $items = Get-GameListItems
        $topCount = @($items | Where-Object { Test-RealizedGameRow $_ }).Count
        $report.realizedRowsAtTop = $topCount
        $report.gameListItemsAtTop = $items.Count
        $report.otherItemNamesAtTop = @($items |
            Where-Object { -not (Test-RealizedGameRow $_) } |
            ForEach-Object { $_.Current.Name } | Select-Object -Unique -First 3)
        # Fail before the slower pixel and keyboard checks, so a broken
        # virtualization reports its row count instead of a harness timeout.
        if ($topCount -ge $realizedLimit) {
            throw "GameList realized $topCount rows at the top of a 500-game library."
        }

        # Seeded covers are solid (shade, 255 - shade, 0x80). A loaded cover
        # fills most of its 45x60 tile; the placeholder has none of that colour.
        $scale = $dpi / 96.0
        $loaded = 0.5 * 45 * 60 * $scale * $scale
        $absent = 0.1 * 45 * 60 * $scale * $scale
        $artwork = [ordered]@{
            long = Wait-RowArtwork $longRow @(0x20, 0xDF, 0x80) $scale $loaded
            short = Wait-RowArtwork $shortRow @(0x40, 0xBF, 0x80) $scale $loaded
        }
        $artwork.corrupt = Measure-RowArtwork (Wait-GameRow 'Catalog C Corrupt Art') @(0x60, 0x9F, 0x80) $scale
        $artwork.missing = Measure-RowArtwork (Wait-GameRow 'Catalog C Missing Art 0') @(0x70, 0x8F, 0x80) $scale
        $report.artworkPixels = $artwork

        $failures = @()
        foreach ($key in 'long', 'short') {
            if ($artwork[$key] -lt $loaded) {
                $failures += "The $key-title row shows no cover ($($artwork[$key]) matching pixels)."
            }
        }
        foreach ($key in 'corrupt', 'missing') {
            if ($artwork[$key] -ge $absent) {
                $failures += "The $key-art row shows its seeded colour ($($artwork[$key]) pixels)."
            }
        }
        if ($failures.Count -gt 0) {
            throw ($failures -join ' ')
        }
        $report.phases += 'catalog-artwork'
        $report.libraryWideScreenshot = Save-WindowScreenshot 'library-wide'
        $report.phases += 'catalog-top-virtualized'

        Focus-And-Verify 'AddGameButton'
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
        [void](Wait-FocusWithin 'LibrarySearchInput')
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
        [void](Wait-FocusedGameRow $longTitle)
        Start-Sleep -Milliseconds 500
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-HiddenById 'GameHeading')
        [System.Windows.Forms.SendKeys]::SendWait('^{DOWN}')
        [void](Wait-FocusedGameRow $shortTitle)
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        [void](Wait-Name 'GameHeading' $shortTitle)
        [void](Wait-Status 'Game ready.')
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        Focus-And-Verify 'AddGameButton'
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
        [void](Wait-FocusWithin 'LibrarySearchInput')
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
        [void](Wait-FocusedGameRow '')
        [System.Windows.Forms.SendKeys]::SendWait('{END}')
        [void](Wait-Name 'GameHeading' $lastTitle)
        [void](Wait-Status 'Game ready.')
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        $report.phases += 'catalog-keyboard'

        [void](Wait-HiddenById 'ShellStatus')
        $list = Wait-VisibleById 'GameList'
        $scroll = $list.GetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern)
        $scroll.SetScrollPercent(
            [System.Windows.Automation.ScrollPattern]::NoScroll, 100)
        [void](Wait-GameRow $lastTitle)
        $endFacts = [ordered]@{
            $lastTitle = 'Nintendo Switch, IGDB, No guides'
            'Catalog Game 483' = 'IGDB, No guides'
            'Catalog Game 481' = 'Manual, No guides'
        }
        foreach ($title in $endFacts.Keys) {
            $help = (Wait-GameRow $title).Current.HelpText
            if ($help -ne $endFacts[$title]) {
                throw "Row '$title' had help text '$help' at the end of the list."
            }
        }
        $report.phases += 'catalog-end-row-facts'
        $endCount = (Get-RealizedGameRows).Count
        $report.realizedRowsAtEnd = $endCount
        if ($endCount -ge $realizedLimit) {
            throw "GameList realized $endCount rows at the end of a 500-game library."
        }
        # Rows at the end use recycled containers: each shows its own cover
        # or the placeholder, never a cover left by an earlier item. The seed
        # ends with Catalog Game 479-483, an Arabic title and the last title.
        $endArtwork = [ordered]@{
            last = Wait-RowArtwork (Wait-GameRow $lastTitle) @(0xA0, 0x5F, 0x80) $scale $loaded
            game483 = Wait-RowArtwork (Wait-GameRow 'Catalog Game 483') @(0x53, 0xAC, 0x80) $scale $loaded
            game481 = [ordered]@{}
        }
        $row481 = Wait-GameRow 'Catalog Game 481'
        $endArtwork.game481.lastColour = Measure-RowArtwork $row481 @(0xA0, 0x5F, 0x80) $scale
        $endArtwork.game481.game483Colour = Measure-RowArtwork $row481 @(0x53, 0xAC, 0x80) $scale
        $report.artworkPixelsAtEnd = $endArtwork
        $failures = @()
        foreach ($key in 'last', 'game483') {
            if ($endArtwork[$key] -lt $loaded) {
                $failures += "The $key row shows no cover at the end of the list ($($endArtwork[$key]) matching pixels)."
            }
        }
        foreach ($key in 'lastColour', 'game483Colour') {
            if ($endArtwork.game481[$key] -ge $absent) {
                $failures += "Catalog Game 481 has no artwork but shows the $key ($($endArtwork.game481[$key]) pixels)."
            }
        }
        if ($failures.Count -gt 0) {
            throw ($failures -join ' ')
        }
        $report.phases += 'catalog-end-artwork'
        $report.libraryEndScreenshot = Save-WindowScreenshot 'library-end'
        $report.phases += 'catalog-end-virtualized'

        Resize-ShellWindow $narrowWidth $windowHeight
        [void](Wait-GameRow $lastTitle)
        $listBounds = (Wait-VisibleById 'GameList').Current.BoundingRectangle
        foreach ($row in Get-RealizedGameRows) {
            $bounds = $row.Current.BoundingRectangle
            if ($row.Current.IsOffscreen -or $bounds.IsEmpty) { continue }
            if ($bounds.Left -lt ($listBounds.Left - 2) -or
                $bounds.Right -gt ($listBounds.Right + 2)) {
                throw "Game row '$($row.Current.Name)' is wider than GameList: $bounds in $listBounds."
            }
        }
        $report.libraryNarrowScreenshot = Save-WindowScreenshot 'library-narrow'
        $report.phases += 'catalog-narrow'

        Start-Sleep -Milliseconds 1000
        $status = Find-ById 'ShellStatus'
        if ($status -and -not $status.Current.IsOffscreen) {
            throw "The catalog showed a status: '$($status.Current.Name)'."
        }
        Assert-NoRemoteConnections 'catalog'
        $report.phases += 'catalog-no-provider-traffic'
        $report.catalogSeconds = [Math]::Round(((Get-Date) - $catalogStarted).TotalSeconds, 1)
    }
    elseif ($Mode -like 'import-*') {
        $fixtureRoot = (Resolve-Path -LiteralPath (
            Join-Path $PSScriptRoot '..\..\tests\fixtures\p0')).Path
        $uia = [System.Windows.Automation.AutomationElement]

        function Wait-FilePicker {
            # The picker runs in the app's process as a #32770 window. Look
            # among top-level windows first, then under the shell window.
            $condition = [System.Windows.Automation.AndCondition]::new(
                [System.Windows.Automation.PropertyCondition]::new(
                    $uia::ClassNameProperty, '#32770'),
                [System.Windows.Automation.PropertyCondition]::new(
                    $uia::ProcessIdProperty, $process.Id))
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $picker = $uia::RootElement.FindFirst(
                    [System.Windows.Automation.TreeScope]::Children, $condition)
                if (-not $picker) {
                    $picker = $root.FindFirst(
                        [System.Windows.Automation.TreeScope]::Children, $condition)
                }
                if ($picker) { return $picker }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            throw 'The system Open dialog did not appear.'
        }

        # Managed UIA sees the dialog's Win32 controls as panes without
        # patterns, so match them by control id and window class and drive
        # them through their window handles.
        function Find-InPicker($picker, [string] $id, [string] $className) {
            $condition = [System.Windows.Automation.AndCondition]::new(
                [System.Windows.Automation.PropertyCondition]::new(
                    $uia::AutomationIdProperty, $id),
                [System.Windows.Automation.PropertyCondition]::new(
                    $uia::ClassNameProperty, $className))
            $element = $picker.FindFirst($scope, $condition)
            if (-not $element) { throw "The Open dialog has no '$id' $className." }
            return [IntPtr]$element.Current.NativeWindowHandle
        }

        function Send-PickerCommand($picker, [int] $buttonId) {
            [DesktopGuidesForegroundProbe]::Command(
                [IntPtr]$picker.Current.NativeWindowHandle, $buttonId,
                (Find-InPicker $picker ([string]$buttonId) 'Button'))
        }

        function Wait-PickerClosed($picker) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                try {
                    if ($picker.Current.ProcessId -ne $process.Id) { return }
                }
                catch [System.Windows.Automation.ElementNotAvailableException] {
                    return
                }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            throw 'The system Open dialog did not close.'
        }

        function Choose-PickerFile([string] $relativePath) {
            $picker = Wait-FilePicker
            $path = Join-Path $fixtureRoot $relativePath
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                # The dialog answers a missing path with its own message box,
                # which would block cleanup, so close it before failing.
                Send-PickerCommand $picker 2
                Wait-PickerClosed $picker
                throw "The fixture '$relativePath' does not exist."
            }
            [DesktopGuidesForegroundProbe]::SetText((Find-InPicker $picker '1148' 'Edit'), $path)
            Send-PickerCommand $picker 1
            Wait-PickerClosed $picker
        }

        function Cancel-Picker {
            $picker = Wait-FilePicker
            Send-PickerCommand $picker 2
            Wait-PickerClosed $picker
        }

        function Select-ById([string] $id) {
            $element = Wait-VisibleById $id
            $element.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            return $element
        }

        # The dialog content scrolls, so rows below the fold report
        # IsOffscreen. Collapsed elements leave the UIA tree, so presence
        # alone shows the app made them visible.
        function Wait-PresentById([string] $id) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $element = Find-ById $id
                if ($element) { return $element }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            throw "Expected '$id' to be shown."
        }

        function Wait-Text([string] $id, [string] $expected) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $element = Find-ById $id
                if ($element -and $element.Current.Name -eq $expected) { return $element }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            throw "Expected '$id' named '$expected'."
        }

        if ($Mode -eq 'import-preview') {
            Select-Element 'Import Test Game'
            [void](Wait-Name 'GameHeading' 'Import Test Game')
            [void](Wait-Status 'Game ready.')
            Click-Element (Wait-EnabledById 'ImportGuideButton')
            Choose-PickerFile 'txt-legacy.txt'
            [void](Wait-VisibleById 'ImportGuideDialog')
            [void](Wait-Text 'ImportFileName' 'txt-legacy.txt')
            [void](Wait-Text 'ImportFormat' 'Text (TXT)')
            $title = (Wait-VisibleById 'GuideTitleInput').GetCurrentPattern(
                [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
            if ($title -ne 'txt-legacy') { throw "Expected the suggested title 'txt-legacy', got '$title'." }
            [void](Wait-PresentById 'ImportEncodingWindows1252')
            $sample = (Wait-VisibleById 'ImportEncodingCp437Sample').Current.Name
            if ($sample -notlike '*Guide*') { throw "The CP437 sample was '$sample'." }
            $cp437 = Select-ById 'ImportEncodingCp437'
            [void](Wait-Text 'ImportEncodingValue' 'DOS (CP437)')
            if (-not $cp437.GetCurrentPattern(
                    [System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) {
                throw 'CP437 was not selected.'
            }
            $report.importTxtScreenshot = Save-WindowScreenshot 'import-txt'
            $report.phases += 'import-txt'

            Click-Element (Wait-EnabledById 'SecondaryButton')
            Choose-PickerFile 'html-static\guide.html'
            [void](Wait-Text 'ImportFormat' 'Web page (HTML)')
            [void](Wait-VisibleById 'ImportDetailsHeading')
            $assets = (Wait-PresentById 'ImportAssets').Current.Name
            if ($assets -notlike '3 linked files, * in total') { throw "The linked files row was '$assets'." }
            foreach ($id in @('ImportDetailsGroup', 'ImportWarningsGroup', 'ImportEncodingCp437')) {
                if (Find-ById $id) { throw "'$id' was shown for a web page without warnings." }
            }
            $report.phases += 'import-html'

            Click-Element (Wait-EnabledById 'SecondaryButton')
            Choose-PickerFile 'html-hostile\guide.html'
            [void](Wait-VisibleById 'ImportDetailsGroup')
            [void](Wait-PresentById 'ImportWarningsGroup')
            [void](Wait-PresentById 'ImportWarning0')
            if (Find-ById 'ImportDetailsHeading') { throw 'The native details heading was shown with groups.' }
            $report.importWarningsScreenshot = Save-WindowScreenshot 'import-warnings'
            $report.phases += 'import-html-warnings'

            Click-Element (Wait-EnabledById 'SecondaryButton')
            Choose-PickerFile 'pdf-locked.pdf'
            [void](Wait-Name 'ImportStatus' "Password-protected PDFs aren't supported. Remove the password and import again.")
            Assert-Absent 'ImportPreview'
            $report.phases += 'import-pdf-locked'

            Invoke-Element (Wait-EnabledById 'CloseButton')
            [void](Wait-HiddenById 'ImportGuideDialog')
            Wait-FocusedId 'ImportGuideButton'
            Click-Element (Wait-EnabledById 'ImportGuideButton')
            Cancel-Picker
            [void](Wait-EnabledById 'ImportGuideButton')
            Assert-Absent 'ImportGuideDialog'
            Wait-FocusedId 'ImportGuideButton'
            $report.phases += 'import-picker-cancel'
        }
        else {
            if ($Mode -ne 'import-publish' -and -not $ExpectedGuideTitle) {
                throw "$Mode needs -ExpectedGuideTitle."
            }
            Select-Element 'Import Test Game'
            [void](Wait-Name 'GameHeading' 'Import Test Game')
            [void](Wait-Status 'Game ready.')
            Click-Element (Wait-EnabledById 'ImportGuideButton')
            Choose-PickerFile 'txt-legacy.txt'
            [void](Wait-VisibleById 'ImportGuideDialog')
            [void](Wait-PresentById 'ImportEncodingCp437')
            if ((Wait-VisibleById 'PrimaryButton').Current.IsEnabled) {
                throw 'Import was enabled before an encoding was chosen.'
            }
            [void](Select-ById 'ImportEncodingCp437')
            [void](Wait-Text 'ImportEncodingValue' 'DOS (CP437)')

            if ($Mode -eq 'import-publish') {
                $importTitle = 'Imported Guide ' + [guid]::NewGuid().ToString('N').Substring(0, 8)
                [void](Wait-Name 'PrimaryButton' 'Import')
                Assert-Absent 'ImportDuplicate'
                Set-Text 'GuideTitleInput' $importTitle
                [void](Wait-EnabledById 'PrimaryButton')
                $report.importReadyScreenshot = Save-WindowScreenshot 'import-ready'
                $report.phases += 'import-ready'

                Invoke-Element (Wait-EnabledById 'PrimaryButton')
                [void](Wait-HiddenById 'ImportGuideDialog')
                [void](Wait-SelectedGuide $importTitle)
                Wait-FocusedGuide $importTitle
                $report.importedTitle = $importTitle
                $report.importPublishedScreenshot = Save-WindowScreenshot 'import-published'
                $report.phases += 'import-published'
            }
            else {
                $duplicateMessage = 'This file is already in Import Test Game as "' + $ExpectedGuideTitle + '".'
                [void](Wait-Name 'ImportDuplicate' $duplicateMessage)
                [void](Wait-Name 'PrimaryButton' 'Import another copy')
                [void](Wait-EnabledById 'ImportOpenExisting')
                $report.importDuplicateScreenshot = Save-WindowScreenshot 'import-duplicate'
                $report.phases += 'import-duplicate'

                if ($Mode -eq 'import-duplicate-copy') {
                    $copyTitle = 'Copied Guide ' + [guid]::NewGuid().ToString('N').Substring(0, 8)
                    Set-Text 'GuideTitleInput' $copyTitle
                    Invoke-Element (Wait-EnabledById 'PrimaryButton')
                    [void](Wait-HiddenById 'ImportGuideDialog')
                    [void](Wait-SelectedGuide $copyTitle)
                    Wait-FocusedGuide $copyTitle
                    $report.importedTitle = $copyTitle
                    $report.importCopiedScreenshot = Save-WindowScreenshot 'import-copied'
                    $report.phases += 'import-copied'
                }
                else {
                    Invoke-Element (Wait-EnabledById 'ImportOpenExisting')
                    [void](Wait-HiddenById 'ImportGuideDialog')
                    [void](Wait-Name 'ReaderHeading' $ExpectedGuideTitle)
                    [void](Wait-Status 'Guide details ready.')
                    $report.importOpenExistingScreenshot = Save-WindowScreenshot 'import-open-existing'
                    $report.phases += 'import-open-existing'
                }
            }
        }
    }
    elseif ($Mode -like 'remove-*') {
        if (-not $ExpectedGuideTitle) {
            throw "$Mode needs -ExpectedGuideTitle."
        }
        $title = $ExpectedGuideTitle
        Select-Element 'Import Test Game'
        [void](Wait-Name 'GameHeading' 'Import Test Game')
        [void](Wait-Status 'Game ready.')

        # Selecting a guide opens it (Ruling 1), so come back to the game
        # with it selected before removing it.
        Open-GuideFromGame $title
        [void](Wait-Name 'ReaderHeading' $title)
        [void](Wait-Status 'Guide details ready.')
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Import Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide $title)
        [void](Wait-GuideRowCount 2)

        Invoke-Element (Wait-Name 'RemoveSelectedGuide' "Remove $title")
        [void](Wait-VisibleById 'RemoveGuideDialog')
        [void](Wait-Name 'RemoveGuideMessage' ("This removes the guide, its reading progress, and its 1 managed file " +
            "from Desktop Guides. The original file you imported isn't affected."))
        [void](Wait-Name 'PrimaryButton' 'Remove')
        [void](Wait-Name 'CloseButton' 'Cancel')
        $report.removeConfirmScreenshot = Save-WindowScreenshot 'remove-confirm'
        $report.phases += 'remove-confirm'

        if ($Mode -eq 'remove-guide-cancel') {
            Invoke-Element (Wait-EnabledById 'CloseButton')
            [void](Wait-HiddenById 'RemoveGuideDialog')
            Wait-FocusedId 'RemoveSelectedGuide'
            [void](Wait-GuideRowCount 2)
            [void](Wait-SelectedGuide $title)
            $report.phases += 'remove-cancelled'
        }
        else {
            Invoke-Element (Wait-EnabledById 'PrimaryButton')
            [void](Wait-HiddenById 'RemoveGuideDialog')
            [void](Wait-Status "Removed $title.")
            $remaining = Wait-GuideRowCount 1
            if ($remaining[0] -eq $title) {
                throw "The removed guide '$title' is still listed."
            }
            [void](Wait-SelectedGuide $remaining[0])
            Wait-FocusedGuide $remaining[0]
            $report.remainingTitle = $remaining[0]
            $report.removedScreenshot = Save-WindowScreenshot 'removed'
            $report.phases += 'removed'
        }
    }
    elseif ($Mode -eq 'long-list') {
        $target = 'ZZZ Focus Target Guide'
        Select-Element 'Route Test Game'
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        $guideRows = @(Get-ListRows 'GuideList').Count
        $report.realizedGuideRows = $guideRows
        if ($guideRows -lt 1 -or $guideRows -ge 60) {
            throw "GuideList realized $guideRows rows for a 99-guide game."
        }
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
    elseif ($Mode -eq 'provider-none') {
        Invoke-Element (Wait-EnabledById 'AddGameButton')
        [void](Wait-Name 'GameEditorNotice' $providerFailureMessages.NotConfigured)
        [void](Wait-VisibleById 'GameTitleInput')
        Assert-Absent 'GameSearchInput'
        $report.noCredentialsScreenshot = Save-WindowScreenshot 'add-game-no-credentials'
        $report.phases += 'no-credentials-opens-manual-add-with-notice'

        Invoke-Element (Wait-VisibleById 'GameEditorNoticeSettingsLink')
        Wait-EditorClosed
        [void](Wait-Status 'Settings ready.')
        Assert-ProviderSettingsExpanded
        [void](Wait-Name 'ProviderSettingsExpander' `
            'Game data providers. Add your IGDB credentials to search for games.')
        $report.phases += 'notice-opens-expanded-provider-settings'
        Go-Back
        [void](Wait-Status 'Library ready.')
    }
    elseif ($Mode -eq 'provider-offline') {
        (Wait-GameRow 'Seeded Linked Game').GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        [void](Wait-Name 'GameHeading' 'Seeded Linked Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-VisibleById 'GameCover')
        $facts = Get-FactsName
        foreach ($expected in @('Released 2004', 'Main game',
            'Platforms: PC (Microsoft Windows), PlayStation 2')) {
            if (-not $facts.Contains($expected)) {
                throw "Game facts '$facts' did not include '$expected'."
            }
        }
        [void](Wait-Name 'GameSummary' 'A seeded summary that the offline check reads back.')
        [void](Wait-Name 'GameGenres' 'Genres: Shooter')
        [void](Wait-Name 'GameCompanies' 'Developed by Seed Developer. Published by Seed Publisher.')
        [void](Wait-Name 'GameAttribution' 'Metadata from IGDB. Artwork from SteamGridDB.')
        [void](Wait-VisibleById 'GameProviderLink')
        $report.gameFacts = $facts
        $report.offlineGameScreenshot = Save-WindowScreenshot "linked-game-$($ExpectedProviderFailure.ToLowerInvariant())"
        $report.phases += 'snapshot-and-cover-from-local-data'

        Invoke-Element (Wait-EnabledById 'RefreshMetadataButton')
        $expectedFailure = $providerFailureMessages[$ExpectedProviderFailure]
        [void](Wait-Status $expectedFailure)
        [void](Wait-Name 'GameHeading' 'Seeded Linked Game')
        [void](Wait-Name 'GameAttribution' 'Metadata from IGDB. Artwork from SteamGridDB.')
        [void](Wait-EnabledById 'RefreshMetadataButton')
        $report.phases += 'failed-refresh-keeps-snapshot'

        # Return to Library so the next smoke in this shell starts where the
        # shared prelude expects, with 'Library ready.' as the latest status.
        Go-Back
        [void](Wait-Status 'Library ready.')
        if ($ExpectedProviderFailure -eq 'Unavailable') {
            Open-AddGameSearch
            [void](Search-Games 'Half-Life')
            [void](Wait-Name 'GameSearchStatus' $expectedFailure)
            [void](Wait-VisibleById 'GameSearchRetry')
            if ((Get-SearchResults).Count -ne 0) {
                throw 'A failed search showed results.'
            }
            $report.offlineSearchScreenshot = Save-WindowScreenshot 'search-offline'
            Close-AddGameSearch
            $report.phases += 'offline-search-reports-unavailable'
        }
    }
    elseif ($Mode -eq 'provider-settings') {
        $igdb = Read-LabelledValues $IgdbCredentialFile @('igdb client id', 'igdb client secret')
        $steamGridDbKey = $null
        if ($SteamGridDbCredentialFile) {
            $steamGridDbKey = (Read-LabelledValues $SteamGridDbCredentialFile @('steamgriddb api key'))['steamgriddb api key']
        }
        Open-ProviderSettings
        Invoke-Element (Wait-EnabledById 'ProviderTestButton')
        [void](Wait-Name 'ProviderSettingsStatus' 'Enter credentials to test them.')
        $report.phases += 'test-with-no-credentials-asks-for-them'

        Enter-Secret 'IgdbClientIdInput' $igdb['igdb client id']
        Enter-Secret 'IgdbClientSecretInput' 'desktop-guides-wrong-secret'
        Invoke-Element (Wait-EnabledById 'ProviderTestButton')
        [void](Wait-Name 'ProviderSettingsStatus' 'IGDB rejected the client ID or secret.')
        $report.phases += 'wrong-secret-is-rejected'

        Enter-Secret 'IgdbClientSecretInput' $igdb['igdb client secret'] -Replace
        $expectedTest = 'IGDB connected.'
        if ($steamGridDbKey) {
            Enter-Secret 'SteamGridDbKeyInput' $steamGridDbKey
            $expectedTest = 'IGDB connected. SteamGridDB connected.'
        }
        Invoke-Element (Wait-EnabledById 'ProviderTestButton')
        [void](Wait-Name 'ProviderSettingsStatus' $expectedTest)
        $report.steamGridDbTested = [bool]$steamGridDbKey
        $report.phases += 'real-credentials-connect'

        Invoke-Element (Wait-EnabledById 'ProviderSaveButton')
        [void](Wait-Name 'ProviderSettingsStatus' 'Provider credentials saved.')
        [void](Wait-Name 'ProviderSettingsExpander' 'Game data providers. IGDB credentials saved.')
        $report.settingsScreenshot = Save-WindowScreenshot 'provider-settings-saved'
        $report.phases += 'credentials-saved'
        Go-Back
        [void](Wait-Status 'Library ready.')
    }
    elseif ($Mode -eq 'provider-live') {
        Open-AddGameSearch
        Set-SearchQuery 'Half-Life'
        Start-Sleep -Seconds 1
        Assert-Absent 'GameSearchCancel'
        if ((Get-SearchResults).Count -ne 0) { throw 'Typing alone showed results.' }
        $report.searchDialogScreenshot = Save-WindowScreenshot 'add-game-search'
        $report.phases += 'typing-does-not-search'

        $results = Search-Games 'Half-Life'
        if ($results.Count -lt 1 -or $results.Count -gt 20) {
            throw "Search returned $($results.Count) results; expected 1 to 20."
        }
        $report.searchResultCount = $results.Count
        $report.searchResultNames = @($results | ForEach-Object { $_.Current.Name })
        # Thumbnails load one at a time after the results show and are raw
        # in the UIA tree, so give them time to appear in the screenshot.
        Start-Sleep -Seconds 5
        $report.searchResultsScreenshot = Save-WindowScreenshot 'search-results'
        $halfLife = @($results | Where-Object { $_.Current.Name -like 'Half-Life, Main game, 1998, *' })
        if ($halfLife.Count -ne 1) {
            throw "Expected one 1998 Half-Life result; found $($halfLife.Count)."
        }
        $report.phases += 'search-returns-labelled-results'

        Activate-Item $halfLife[0]
        Wait-HiddenById 'GameSearchInput'
        [void](Wait-Name 'GameHeading' 'Half-Life')
        # GameProviderDetails is a StackPanel with no automation peer; the
        # facts group inside it shows exactly when the details do.
        [void](Wait-VisibleById 'GameFacts')
        $attribution = (Wait-VisibleById 'GameAttribution').Current.Name
        if ($attribution -notin @(
            'Metadata from IGDB. Artwork from SteamGridDB.',
            'Metadata and artwork from IGDB.')) {
            throw "Unexpected attribution '$attribution'."
        }
        [void](Wait-VisibleById 'GameCover')
        [void](Wait-VisibleById 'GameProviderLink')
        $report.addedAttribution = $attribution
        $report.addedFacts = Get-FactsName
        $report.linkedGameScreenshot = Save-WindowScreenshot 'linked-game-live'
        $report.phases += 'pick-adds-linked-game-with-cover'

        Go-Back
        [void](Wait-Status 'Library ready.')
        Open-AddGameSearch
        $again = Search-Games 'Half-Life'
        Activate-Item @($again | Where-Object { $_.Current.Name -like 'Half-Life, Main game, 1998, *' })[0]
        Wait-HiddenById 'GameSearchInput'
        [void](Wait-Status 'Half-Life is already in your library.' -AllowHidden)
        [void](Wait-Name 'GameHeading' 'Half-Life')
        Go-Back
        [void](Wait-Status 'Library ready.')
        # 'Library ready.' can precede the realized rows; count only once
        # the list shows its games.
        [void](Wait-GameRow 'Seeded Linked Game')
        [void](Wait-GameRow 'Half-Life')
        if ((Count-GameRows 'Half-Life') -ne 1) { throw 'A duplicate add created a second row.' }
        $report.phases += 'duplicate-add-opens-existing-game'

        (Wait-GameRow 'Half-Life').GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        [void](Wait-Status 'Game ready.')
        Invoke-Element (Wait-EnabledById 'EditGameButton')
        Set-Text 'GamePlatformInput' 'My test platform'
        Set-Text 'GameNotesInput' 'My local notes.'
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        Wait-EditorClosed
        [void](Wait-Name 'GamePlatform' 'My test platform')
        Invoke-Element (Wait-EnabledById 'RefreshMetadataButton')
        $refreshed = Wait-Status @('Metadata refreshed.',
            "Metadata refreshed. A new cover couldn't be downloaded.") -AllowHidden
        $report.refreshStatus = $refreshed.Current.ItemStatus
        [void](Wait-Name 'GameHeading' 'Half-Life')
        [void](Wait-Name 'GamePlatform' 'My test platform')
        [void](Wait-Name 'GameNotes' 'My local notes.')
        $report.phases += 'refresh-preserves-local-fields'

        Go-Back
        [void](Wait-Status 'Library ready.')
        Open-AddGameSearch
        [void](Search-Games 'zzqx no such game 91377')
        [void](Wait-Name 'GameSearchStatus' 'No games match "zzqx no such game 91377".')
        $report.phases += 'empty-search-says-so'

        # Ruling T12-a: record whether Cancel was seen, never assume it.
        $report.cancelObserved = $false
        $report.cancelAttempts = 0
        for ($attempt = 1; $attempt -le 3 -and -not $report.cancelObserved; $attempt++) {
            $report.cancelAttempts = $attempt
            Set-SearchQuery 'Final Fantasy'
            Invoke-Element (Wait-EnabledById 'GameSearchButton')
            $cancel = Find-ById 'GameSearchCancel'
            if (-not $cancel -or $cancel.Current.IsOffscreen) {
                $report.cancelReason = 'The search finished before Cancel could be pressed.'
                Start-Sleep -Seconds 2
                continue
            }
            Invoke-Element $cancel
            Wait-HiddenById 'GameSearchCancel'
            $status = Find-ById 'GameSearchStatus'
            if ((Get-SearchResults).Count -eq 0 -and
                (-not $status -or $status.Current.IsOffscreen)) {
                $report.cancelObserved = $true
                $report.cancelReason = $null
            }
            else {
                $report.cancelReason = 'The search completed while Cancel was pressed.'
            }
        }
        if ($report.cancelObserved) { $report.phases += 'cancel-during-search-shows-nothing' }

        Invoke-Element (Wait-VisibleById 'AddGameManuallyLink')
        [void](Wait-VisibleById 'GameTitleInput')
        Assert-Absent 'GameEditorNotice'
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-EditorClosed
        # Cancelling the editor stays on Library and posts no new status.
        [void](Wait-Name 'LibraryHeading' 'Library')
        $report.phases += 'add-manually-from-search'
    }
    elseif ($Mode -eq 'provider-remove') {
        Open-ProviderSettings
        Invoke-Element (Wait-EnabledById 'ProviderRemoveButton')
        [void](Wait-Name 'ProviderSettingsStatus' 'Provider credentials removed.')
        [void](Wait-Name 'ProviderSettingsExpander' `
            'Game data providers. Add your IGDB credentials to search for games.')
        Go-Back
        [void](Wait-Status 'Library ready.')
        Invoke-Element (Wait-EnabledById 'AddGameButton')
        [void](Wait-Name 'GameEditorNotice' $providerFailureMessages.NotConfigured)
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-EditorClosed
        $report.phases += 'remove-restores-manual-add'
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
    if (($Mode -eq 'game-editor' -or $Mode -eq 'catalog' -or $Mode -like 'import-*' -or $Mode -like 'remove-*' -or
        $Mode -like 'provider-*') -and $root) {
        try {
            foreach ($id in @('ShellStatus', 'GameHeading',
                'GameTitleFeedback', 'GameSaveError', 'GameSearchStatus',
                'ProviderSettingsStatus', 'GameEditorNotice', 'GameAttribution',
                'ImportStatus', 'ImportGuideDialog', 'ImportBusyText',
                'RemoveGuideDialog', 'RemoveGuideMessage', 'RemoveSelectedGuide')) {
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
