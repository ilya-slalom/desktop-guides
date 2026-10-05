param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('empty', 'game-editor', 'game-editor-persisted',
        'normal', 'design-language', 'stale', 'long-list', 'switch-game',
        'switch-game-prepare', 'switch-game-loading', 'queue-guide',
        'queue-guide-write', 'prepare-game-editor-close',
        'queue-game-editor', 'queue-later-guide', 'later-guide-result',
        'later-guide-failed-result', 'queue-reader-render-error',
        'reader-render-error-observed', 'reader-render-error-result',
        'late-guide-after-close', 'waiting-handoff', 'material', 'catalog', 'catalog-facts', 'library-search', 'stable-navigation',
        'provider-none', 'provider-offline', 'provider-settings',
        'import-preview', 'import-publish', 'import-duplicate-copy', 'import-duplicate-open',
        'remove-guide-cancel', 'remove-guide',
        'provider-live', 'provider-remove', 'game-actions', 'game-actions-persisted', 'txt-reader',
        'txt-load-paused', 'txt-back-during-load', 'txt-load-released', 'html-reader',
        'html-runtime-missing', 'pdf-reader', 'html-position',
        'progress-timer', 'progress-restored', 'progress-two-guides', 'progress-row', 'progress-changed',
        'completion-segmented', 'completion-last-page', 'completion-game', 'completion-reader', 'completion-restart', 'completion-restart-after', 'completion-error-prepare', 'completion-error', 'completion-error-retry')]
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

    [int] $ExpectedTopLine = 0,

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
    [string] $ExpectedProviderFailure = 'NotConfigured',

    # The app's LocalState and LocalCache folders, for HTML position gates.
    [string] $AppDataRoot = '',

    [string] $AppCacheRoot = ''
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
    Add-Type -Path (Join-Path $PSScriptRoot 'windows_shell_response_monitor.cs')
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

    # A short window caps the Game details card, so an element inside it can
    # sit past the card's viewport. Scroll the card one step toward it.
    function Step-GameDetailToward([string] $id, $element) {
        $viewer = Find-ById 'GameMetadataScroll'
        if (-not $viewer -or $viewer.Current.IsOffscreen) { return }
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        if (-not $viewer.FindFirst($scope, $condition)) { return }
        $pattern = $null
        if (-not $viewer.TryGetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern, [ref] $pattern) -or
            -not $pattern.Current.VerticallyScrollable) {
            return
        }
        $amount = if ($element.Current.BoundingRectangle.Top -lt
            $viewer.Current.BoundingRectangle.Top) {
            [System.Windows.Automation.ScrollAmount]::SmallDecrement
        }
        else {
            [System.Windows.Automation.ScrollAmount]::SmallIncrement
        }
        try {
            $pattern.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount, $amount)
        }
        catch [System.InvalidOperationException] {
            # Already at that end of the card.
        }
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
            if ($element -and $element.Current.IsOffscreen) {
                Step-GameDetailToward $id $element
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected visible '$id' named '$expected'."
    }

    # A transient status takes rows from the content until it closes itself,
    # so wait for it before measuring the reader's layout.
    # Call it after Wait-Status; the bar can lag the status probe briefly.
    function Wait-StatusClosed {
        $deadline = (Get-Date).AddSeconds(1)
        do {
            $status = Find-ById 'ShellStatus'
            if ($status -and -not $status.Current.IsOffscreen) { break }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        $deadline = (Get-Date).AddSeconds(10)
        do {
            $status = Find-ById 'ShellStatus'
            if (-not $status -or $status.Current.IsOffscreen) { return }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        throw "The shell status '$($status.Current.Name)' did not close."
    }

    function Wait-Status(
        [string[]] $expected,
        [switch] $AllowHidden,
        [int] $Seconds = 15) {
        $transient = @($expected | Where-Object { $_ -in @(
            'Library ready.',
            'Game ready.',
            'Guide ready.',
            'Settings ready.') }).Count -gt 0
        $lastObserved = 'status probe was not found'
        $deadline = (Get-Date).AddSeconds($Seconds)
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
            if ($element) {
                Step-GameDetailToward $id $element
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

    function Get-GameDetailsScroll {
        $viewer = Wait-VisibleById 'GameMetadataScroll'
        return $viewer.GetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern)
    }

    # A page with room to spare shows the whole details card unscrolled.
    function Assert-GameDetailsUncapped {
        if ((Get-GameDetailsScroll).Current.VerticallyScrollable) {
            throw 'The Game details card scrolled on a page with room for all of it.'
        }
    }

    # However tall the Game details are, the guide list keeps at least one
    # whole row on screen.
    function Assert-GuideListUsable {
        $list = Find-ById 'GuideList'
        if (-not $list -or $list.Current.IsOffscreen) {
            throw 'Expected a visible guide list.'
        }
        $bounds = $list.Current.BoundingRectangle
        $items = $list.FindAll($scope,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem))
        foreach ($item in $items) {
            $row = $item.Current.BoundingRectangle
            if (-not $item.Current.IsOffscreen -and $row.Height -gt 0 -and
                $row.Top -ge $bounds.Top - 2 -and $row.Bottom -le $bounds.Bottom + 2) {
                return
            }
        }
        throw "Expected a whole guide row in the guide list ($($bounds.Height) px tall)."
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
        [void](Wait-Status 'Guide ready.')
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
    elseif ($Mode -in @('txt-reader', 'txt-load-paused', 'txt-back-during-load',
        'txt-load-released', 'html-reader', 'html-runtime-missing', 'pdf-reader', 'html-position',
        'progress-timer', 'progress-restored', 'progress-two-guides', 'progress-row', 'progress-changed',
        'completion-segmented', 'completion-last-page', 'completion-game', 'completion-reader', 'completion-restart', 'completion-restart-after', 'completion-error-prepare', 'completion-error', 'completion-error-retry')) {
        $textGame = if ($Mode -in @('html-reader', 'html-runtime-missing')) { 'Web Reader Game' }
            elseif ($Mode -eq 'html-position') { 'Web Position Game' }
            elseif ($Mode -eq 'pdf-reader') { 'PDF Reader Game' }
            elseif ($Mode -like 'progress-*' -or $Mode -like 'completion-*') { 'Progress Game' }
            else { 'Text Reader Game' }
        $missingMessage = "This guide's file is missing from the library."
        $listItem = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        $fixtureRoot = Join-Path $PSScriptRoot '..\..\tests\fixtures'
        # The reader names a blank or whitespace-only row "Blank line" and
        # shows no row for the empty line after a final newline.
        $asciiText = [System.IO.File]::ReadAllText((Join-Path $fixtureRoot 'p0\txt-ascii.txt'))
        if ($asciiText.EndsWith("`n")) { $asciiText = $asciiText.Substring(0, $asciiText.Length - 1) }
        $asciiNames = @(
            $asciiText -split "`n" |
                ForEach-Object { if ([string]::IsNullOrWhiteSpace($_)) { 'Blank line' } else { $_ } })
        if ($asciiNames.Count -ne 7) {
            throw "Expected 7 txt-ascii lines; read $($asciiNames.Count)."
        }

        function Get-TextRows {
            $lines = Find-ById 'ReaderTextLines'
            if (-not $lines -or $lines.Current.IsOffscreen) { return @() }
            return @($lines.FindAll($scope, $listItem))
        }

        function Wait-FirstTextRow {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $lines = Find-ById 'ReaderTextLines'
                if ($lines -and -not $lines.Current.IsOffscreen -and
                    $lines.FindFirst($scope, $listItem)) {
                    return
                }
                Start-Sleep -Milliseconds 50
            } while ((Get-Date) -lt $deadline)
            throw 'The TXT reader showed no lines.'
        }

        function Assert-RowNames([string] $guide, [string[]] $expected) {
            Wait-FirstTextRow
            $names = @(Get-TextRows | ForEach-Object { $_.Current.Name })
            if ($names.Count -ne $expected.Count) {
                throw "$guide showed $($names.Count) rows; expected $($expected.Count)."
            }
            for ($index = 0; $index -lt $expected.Count; $index++) {
                if ($names[$index] -ne $expected[$index]) {
                    throw "$guide row $index was '$($names[$index])'; expected '$($expected[$index])'."
                }
            }
        }

        function Assert-NoReaderCommands([string] $guide) {
            $commands = Find-ById 'ReaderCommands'
            if ($commands -and -not $commands.Current.IsOffscreen) {
                throw "$guide exposed reader commands."
            }
        }

        # The smoke window shows only a few guide rows, so scroll a lower
        # guide into view before selecting it.
        function Show-TextGuide([string] $guide) {
            $list = Find-ById 'GuideList'
            if (-not $list -or $list.Current.IsOffscreen) {
                throw 'Expected a visible guide list.'
            }
            $row = [System.Windows.Automation.AndCondition]::new(
                $listItem,
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $guide))
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $item = $list.FindFirst($scope, $row)
                if ($item) {
                    if ($item.Current.IsOffscreen) {
                        $item.GetCurrentPattern(
                            [System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView()
                    }
                    return
                }
                # A virtualized row may not exist until the list scrolls toward it.
                $scroll = $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
                if ($scroll.Current.VerticallyScrollable) {
                    $scroll.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount,
                        [System.Windows.Automation.ScrollAmount]::LargeIncrement)
                }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "The guide list has no row named '$guide'."
        }

        function Open-TextGuide([string] $guide) {
            Show-TextGuide $guide
            Open-GuideFromGame $guide
            [void](Wait-Name 'ReaderHeading' $guide)
        }

        function Back-ToTextGame {
            Go-Back
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')
        }

        # WebView2 content sits in a top-level Chromium window owned by
        # the shell's WebView2 browser process, so its UI Automation tree
        # is read from that window rather than from the shell window.
        function Get-PageRoots {
            $browsers = @(Get-CimInstance Win32_Process -Filter (
                "ParentProcessId = $ProcessId AND Name = 'msedgewebview2.exe'") |
                ForEach-Object { [uint32] $_.ProcessId })
            if ($browsers.Count -eq 0) { return @() }
            $pages = @()
            foreach ($window in [DesktopGuidesForegroundProbe]::FindWindows(
                'Chrome_RenderWidgetHostHWND', [uint32[]] $browsers)) {
                try {
                    $pages += [System.Windows.Automation.AutomationElement]::FromHandle($window)
                }
                catch [System.Windows.Automation.ElementNotAvailableException] {
                    # The window closed after it was listed.
                }
            }
            return $pages
        }

        function Find-PageByName([string] $name) {
            $condition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty, $name)
            foreach ($page in Get-PageRoots) {
                try {
                    $element = $page.FindFirst($scope, $condition)
                    if ($element) { return $element }
                }
                catch [System.Windows.Automation.ElementNotAvailableException] {
                    # A closing session's window can vanish mid-search.
                }
            }
            return $null
        }

        function Wait-PageName([string] $name) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $element = Find-PageByName $name
                if ($element) { return $element }
                Start-Sleep -Milliseconds 250
            } while ((Get-Date) -lt $deadline)
            [void](Save-WindowScreenshot 'html-page-missing')
            $webViews = @(Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" |
                ForEach-Object { "$($_.ProcessId)<-$($_.ParentProcessId)" })
            throw "The guide page did not show '$name'. WebView2 processes (pid<-parent): $($webViews -join ', ')."
        }

        # The page viewport grows back after the link bar closes, so a
        # link can be found while it is still below the visible area.
        function Wait-PageVisible([string] $name) {
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $element = Wait-PageName $name
                try {
                    if (-not $element.Current.IsOffscreen) { return $element }
                }
                catch [System.Windows.Automation.ElementNotAvailableException] {
                    # The page tree was rebuilt; find the link again.
                }
                Start-Sleep -Milliseconds 250
            } while ((Get-Date) -lt $deadline)
            [void](Save-WindowScreenshot 'html-page-offscreen')
            throw "The guide page showed '$name' only off-screen."
        }

        # The app's own view of the position, written only while the
        # HtmlPosition gate is open. The locator is the T12.1 codec JSON.
        function Read-HtmlPosition {
            $path = Join-Path $AppCacheRoot "diagnostics\html-position-$ProcessId.json"
            if (-not (Test-Path -LiteralPath $path)) { return $null }
            try {
                $file = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
            }
            catch {
                return $null
            }
            $position = [ordered]@{
                locator = $file.locator; offset = $null; quote = $null
                kind = $file.kind; step = $file.step; reason = $file.reason
            }
            if ($file.locator) {
                $payload = ($file.locator | ConvertFrom-Json).payload
                $position.offset = [int] $payload.textOffset
                $position.quote = [string] $payload.textQuote
            }
            return [pscustomobject] $position
        }

        function Wait-HtmlPosition([scriptblock] $until, [string] $what) {
            $deadline = (Get-Date).AddSeconds(10)
            $position = $null
            do {
                $position = Read-HtmlPosition
                if ($position -and (& $until $position)) { return $position }
                Start-Sleep -Milliseconds 250
            } while ((Get-Date) -lt $deadline)
            [void](Save-WindowScreenshot 'html-position-timeout')
            throw "The HTML position never showed $what. Last: offset=$($position.offset) kind=$($position.kind) step=$($position.step)."
        }

        # The page's tree has no TextPattern, and a point hit-test stops at
        # WinUI's content bridge, so the top line comes from the page tree:
        # each fixture line is its own span, and the on-screen line whose box
        # crosses the page's top edge (or starts just below it) is the top
        # line; none near the edge means the page's top isn't a mark line.
        $script:topLineNote = 'no page'
        $onScreen = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::IsOffscreenProperty, $false)
        function Get-PageTopLine {
            foreach ($page in Get-PageRoots) {
                try {
                    $rect = $page.Current.BoundingRectangle
                    if ($page.Current.IsOffscreen -or $rect.Width -lt 1 -or $rect.Height -lt 1) { continue }
                    $started = Get-Date
                    $best = $null
                    $count = 0
                    foreach ($element in $page.FindAll($scope, $onScreen)) {
                        $name = $element.Current.Name
                        if ($name -notmatch '^MARK-\d{4}\b') { continue }
                        $box = $element.Current.BoundingRectangle
                        if ($box.Height -lt 1 -or $box.Height -gt 60) { continue }
                        $count++
                        if ($box.Bottom -le $rect.Top + 2 -or $box.Top -ge $rect.Top + 30) { continue }
                        if (-not $best -or $box.Top -lt $best.Top) {
                            $best = [pscustomobject]@{ Top = $box.Top; Name = $name.Trim() }
                        }
                    }
                    $elapsed = [int]((Get-Date) - $started).TotalMilliseconds
                    $script:topLineNote = "page top $([int]$rect.Top); $count mark lines on screen in $elapsed ms"
                    if ($best) { return $best.Name }
                }
                catch [System.Windows.Automation.ElementNotAvailableException] {
                    # The page tree was rebuilt; try the next window.
                }
            }
            return $null
        }

        # The fixture's lines start MARK-<4 digits>. "Within one line" allows
        # the neighbor on either side, for rounding at a line boundary.
        function Wait-TopMark([int] $mark, [string] $step) {
            $deadline = (Get-Date).AddSeconds(10)
            $seen = 'nothing'
            do {
                $line = Get-PageTopLine
                if ($line -match '^MARK-(\d{4})\b') {
                    $seen = "MARK-$($Matches[1])"
                    if ([Math]::Abs([int] $Matches[1] - $mark) -le 1) { return [int] $Matches[1] }
                }
                elseif ($line) {
                    $seen = "a line without a mark ('$($line.Substring(0, [Math]::Min(40, $line.Length)))')"
                }
                Start-Sleep -Milliseconds 250
            } while ((Get-Date) -lt $deadline)
            [void](Save-WindowScreenshot "html-top-$step")
            throw "After $step the page's top line was $seen; expected MARK-$('{0:D4}' -f $mark) within one line ($script:topLineNote)."
        }

        # The page text through TextPattern, as a screen reader reads it;
        # null while the text box is not shown.
        function Get-PdfText {
            $box = Find-ById 'PdfDocumentText'
            if (-not $box -or $box.Current.IsOffscreen) { return $null }
            return $box.GetCurrentPattern(
                [System.Windows.Automation.TextPattern]::Pattern).DocumentRange.GetText(-1)
        }

        # An empty status line is collapsed.
        function Get-PdfStatus([string] $id) {
            $status = Find-ById $id
            if (-not $status -or $status.Current.IsOffscreen) { return '' }
            return $status.Current.Name
        }

        # Waits until the page status, the preview's name and the text all
        # show one page.
        function Wait-PdfPage([int] $page, [int] $count, [string] $text, [int] $seconds = 15) {
            $label = "Page $page of $count"
            $deadline = (Get-Date).AddSeconds($seconds)
            do {
                try {
                    $status = Find-ById 'PdfPageStatus'
                    $preview = Find-ById 'PdfPreviewImage'
                    if ($status -and $preview -and $status.Current.Name -eq $label -and
                        $preview.Current.Name -eq "$label preview") {
                        $read = Get-PdfText
                        if ($read -and $read.Contains($text)) { return $preview }
                    }
                }
                catch [System.Windows.Automation.ElementNotAvailableException] {
                    # The view replaced the element mid-read.
                }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "Expected $label with its preview, and text containing '$text'."
        }

        function Invoke-NextPages([int] $count) {
            $next = Find-ByName 'Next page'
            if (-not $next -or $next.Current.IsOffscreen) {
                throw "The PDF reader has no visible 'Next page' command."
            }
            # No waiting between turns: superseded pages must be dropped.
            for ($i = 0; $i -lt $count; $i++) { Invoke-Element $next }
        }

        function Get-PdfScroll {
            $scroller = Find-ById 'PdfPreviewScroller'
            if (-not $scroller) { throw 'The PDF preview has no scroller.' }
            return $scroller.GetCurrentPattern(
                [System.Windows.Automation.ScrollPattern]::Pattern)
        }

        # The share of the page above the top of the viewport. The
        # scroller holds only the page image, so its extent is the page.
        # A page that can't scroll reads -1 percent at a 100% view: 0.
        function Get-PdfFraction {
            $scroll = Get-PdfScroll
            return $scroll.Current.VerticalScrollPercent / 100 *
                (1 - $scroll.Current.VerticalViewSize / 100)
        }

        # Waits for the point to sit within 0.1 of $wanted, or of the
        # lowest point this window can scroll to if that is higher up,
        # and to stay there through the debounced re-render.
        function Wait-PdfFraction([double] $wanted, [string] $context) {
            $deadline = (Get-Date).AddSeconds(5)
            $held = $false
            do {
                $scroll = Get-PdfScroll
                $expected = [Math]::Min($wanted, 1 - $scroll.Current.VerticalViewSize / 100)
                $fraction = Get-PdfFraction
                if ([Math]::Abs($fraction - $expected) -le 0.1) {
                    if ($held) { return $fraction }
                    $held = $true
                    Start-Sleep -Milliseconds 1000
                    continue
                }
                $held = $false
                Start-Sleep -Milliseconds 100
                # A hold always gets its re-read, even past the deadline.
            } while ($held -or (Get-Date) -lt $deadline)
            throw ("$context left the page at fraction $([Math]::Round($fraction, 3)); " +
                "expected $([Math]::Round($expected, 3)) +/- 0.1.")
        }

        # The shell's progress override replaces the guide's stored locator.
        function Set-RestoreLocator([string] $json) {
            $folder = Join-Path $AppDataRoot 'test'
            [void](New-Item -ItemType Directory -Force -Path $folder)
            [System.IO.File]::WriteAllText((Join-Path $folder 'restore-locator.json'), $json)
        }

        function Clear-RestoreLocator {
            Remove-Item -LiteralPath (Join-Path $AppDataRoot 'test\restore-locator.json') -Force -ErrorAction SilentlyContinue
        }

        function Clear-HtmlPosition {
            Remove-Item -LiteralPath (Join-Path $AppCacheRoot "diagnostics\html-position-$ProcessId.json") `
                -Force -ErrorAction SilentlyContinue
        }

        function Open-RestoredGuide([string] $guide, [string] $restore, [string] $status = 'Guide ready.') {
            Clear-HtmlPosition
            Set-RestoreLocator $restore
            try {
                Open-TextGuide $guide
                $report.sessionsOpened++
                [void](Wait-Status $status)
                return Wait-HtmlPosition { param($p) $p.kind } 'a restore outcome'
            }
            finally {
                Clear-RestoreLocator
            }
        }

        # Pass '' for a step or reason that must be null.
        function Assert-Restore($position, [string] $kind, [string] $step, [string] $reason, [string] $what) {
            if ([string] $position.kind -cne $kind -or [string] $position.step -cne $step -or
                [string] $position.reason -cne $reason) {
                throw "After $what the restore was kind=$($position.kind) step=$($position.step) reason=$($position.reason); expected kind=$kind step=$step reason=$reason."
            }
            $report.restoreKinds += $kind
        }

        # A row can be recycled or not yet realized mid-read; retry briefly,
        # then report the last error.
        function Invoke-UiaRetry([scriptblock] $read) {
            $deadline = (Get-Date).AddSeconds(2)
            while ($true) {
                try { return & $read }
                catch {
                    if ((Get-Date) -ge $deadline) { throw }
                    Start-Sleep -Milliseconds 100
                }
            }
        }

        # The first row whose middle is inside the list is the top line.
        function Get-TopRow {
            $top = (Find-ById 'ReaderTextLines').Current.BoundingRectangle.Top
            $rows = @(Get-TextRows | Where-Object { -not $_.Current.IsOffscreen } |
                Sort-Object { $_.Current.BoundingRectangle.Top })
            foreach ($row in $rows) {
                $bounds = $row.Current.BoundingRectangle
                if ($bounds.Top + $bounds.Height / 2 -ge $top) { return $row }
            }
            throw 'The TXT reader showed no rows.'
        }

        function Get-TopLine {
            return Invoke-UiaRetry {
                $name = (Get-TopRow).Current.Name
                if ($name -notmatch '^Line (\d{4}) ') { throw "Unexpected top row '$name'." }
                [int]$Matches[1]
            }
        }

        function Get-TopRowName { return Invoke-UiaRetry { (Get-TopRow).Current.Name } }

        function Get-TopRowHeight {
            return Invoke-UiaRetry { (Get-TopRow).Current.BoundingRectangle.Height }
        }

        function Wait-TopLine([int] $expected, [string] $step) {
            $deadline = (Get-Date).AddSeconds(10)
            do {
                $actual = Get-TopLine
                if ($actual -eq $expected) { return }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "$step left line $actual at the top; expected line $expected."
        }

        function Wait-TopLineChange([int] $from, [string] $step) {
            $deadline = (Get-Date).AddSeconds(10)
            do {
                $actual = Get-TopLine
                if ($actual -ne $from) { Start-Sleep -Milliseconds 300; return Get-TopLine }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "$step did not move the top line from $from."
        }

        function Get-FullyVisibleRows {
            return Invoke-UiaRetry {
                $list = (Find-ById 'ReaderTextLines').Current.BoundingRectangle
                @(Get-TextRows | Where-Object {
                    $bounds = $_.Current.BoundingRectangle
                    -not $_.Current.IsOffscreen -and
                        $bounds.Top -ge $list.Top - 1 -and $bounds.Bottom -le $list.Bottom + 1
                }).Count
            }
        }

        # Commands and resizes move the text vertically only.
        function Assert-HorizontalPercent($scroll, [double] $expected, [string] $step) {
            Start-Sleep -Milliseconds 300
            $actual = $scroll.Current.HorizontalScrollPercent
            if ([Math]::Abs($actual - $expected) -gt 1) {
                throw "$step moved the text sideways to $([Math]::Round($actual, 1))%; expected $([Math]::Round($expected, 1))%."
            }
        }

        function Invoke-ReaderCommand([string] $name) {
            $button = Find-ByName $name
            if (-not $button -or $button.Current.IsOffscreen) {
                throw "The TXT reader has no visible '$name' command."
            }
            Invoke-Element $button
        }

        # The installer holds this process's TXT load at its test gate between
        # these three modes, so Back runs while the load is still in flight.
        if ($Mode -eq 'txt-load-paused') {
            [void](Wait-Name 'LibraryHeading' 'Library')
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')
            Open-TextGuide 'ASCII Map Guide'
            $report.phases += 'txt-load-paused'
        }
        elseif ($Mode -eq 'txt-back-during-load') {
            # Back doesn't wait for the held load.
            Back-ToTextGame
            Assert-Absent 'ReaderTextLines'
            Assert-NoReaderCommands 'Game page after Back'
            $report.phases += 'txt-back-during-load'
        }
        elseif ($Mode -eq 'txt-load-released') {
            # The released load was cancelled: no late text or Guide ready.
            Start-Sleep -Seconds 2
            $status = (Find-RawById 'ShellContent').Current.ItemStatus
            $message = $status.Substring($status.IndexOf('|') + 1)
            if ($message -ne 'Game ready.') {
                throw "After the held TXT load was released the status was '$message'; expected 'Game ready.'."
            }
            [void](Wait-Name 'GameHeading' $textGame)
            Assert-Absent 'ReaderTextLines'
            Open-TextGuide 'ASCII Map Guide'
            [void](Wait-Status 'Guide ready.')
            Assert-RowNames 'ASCII Map Guide (after a cancelled load)' $asciiNames
            $report.phases += 'txt-load-released'
        }
        elseif ($Mode -eq 'html-reader') {
            [void](Wait-Name 'LibraryHeading' 'Library')
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')

            # Non-asserting diagnostic: canary log line counts at each step,
            # so any guide-originated connection can be attributed to a step.
            $canaryLog = Join-Path (Split-Path -Parent $ResultPath) 'html-canary.log'
            $report.canaryLines = @()
            function Mark-CanaryLines([string] $step) {
                $count = if (Test-Path -LiteralPath $canaryLog) { @(Get-Content -LiteralPath $canaryLog).Count } else { 0 }
                $report.canaryLines += "$step=$count"
            }

            function Assert-NoExternalLinkBar([string] $after) {
                $bar = Find-ById 'ReaderExternalLinkBar'
                if ($bar -and -not $bar.Current.IsOffscreen) {
                    throw "The external-link bar opened after $after."
                }
            }

            # html-canary-a: the guide renders at its own origin with no
            # commands and no load error.
            Mark-CanaryLines 'before-a'
            Open-TextGuide 'Canary Guide A'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Canary guide A loaded')
            Assert-NoReaderCommands 'Canary Guide A'
            Assert-Absent 'ReaderLoadError'
            Assert-Absent 'ReaderPlaceholder'
            # The meta refresh fires after one second. It isn't
            # user-initiated, so it is cancelled without the bar (R9).
            Mark-CanaryLines 'loaded-a'
            # Non-asserting diagnostic: the runtime version and whether the
            # browser process started with the session's proxy arguments.
            $browser = @(Get-CimInstance Win32_Process -Filter (
                "ParentProcessId = $ProcessId AND Name = 'msedgewebview2.exe'")) | Select-Object -First 1
            if ($browser -and $browser.ExecutablePath) {
                $report.webView2Runtime = (Get-Item -LiteralPath $browser.ExecutablePath).VersionInfo.ProductVersion
                $report.webView2ProxyArguments = [bool] ($browser.CommandLine -match [regex]::Escape('--proxy-bypass-list=<-loopback>'))
            }
            Start-Sleep -Seconds 2
            Assert-NoExternalLinkBar 'the meta refresh'
            [void](Wait-PageName 'Canary guide A loaded')
            $report.phases += 'html-canary-a'

            # html-external-links: another guide's origin is denied
            # silently; website links go to the bar; a fragment scrolls.
            Mark-CanaryLines 'after-refresh'
            Click-Element (Wait-PageVisible 'Open canary guide B')
            Start-Sleep -Seconds 1
            Assert-NoExternalLinkBar 'a link into another guide'
            [void](Wait-PageName 'Canary guide A loaded')
            if (Find-PageByName 'Canary guide B loaded') {
                throw 'A link into another guide loaded that guide.'
            }

            Mark-CanaryLines 'after-cross-guide-link'
            Click-Element (Wait-PageVisible 'External canary link')
            [void](Wait-VisibleById 'ReaderExternalLinkBar')
            [void](Wait-Name 'ReaderExternalLinkUrl' 'https://example.com/desktop-guides-canary')
            $report.htmlExternalLinkScreenshot = Save-WindowScreenshot 'html-external-link'
            Invoke-Element (Find-ById 'ReaderExternalLinkOpen')
            Wait-HiddenById 'ReaderExternalLinkBar'
            [void](Wait-PageName 'Canary guide A loaded')

            Mark-CanaryLines 'after-external-link'
            Click-Element (Wait-PageVisible 'New window canary link')
            [void](Wait-VisibleById 'ReaderExternalLinkBar')
            [void](Wait-Name 'ReaderExternalLinkUrl' 'https://example.org/desktop-guides-canary-blank')
            Invoke-Element (Find-ById 'ReaderExternalLinkDismiss')
            Wait-HiddenById 'ReaderExternalLinkBar'

            Mark-CanaryLines 'after-blank-link'
            Click-Element (Wait-PageVisible 'Jump to details')
            $details = Wait-PageName 'Canary details'
            $deadline = (Get-Date).AddSeconds(5)
            while ($details.Current.IsOffscreen -and (Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 250
            }
            if ($details.Current.IsOffscreen) {
                throw 'The fragment link did not scroll to its section.'
            }
            Assert-NoExternalLinkBar 'a fragment link'
            [void](Wait-PageName 'Canary guide A loaded')
            $report.phases += 'html-external-links'

            # html-canary-b: the second guide renders at its own origin.
            Back-ToTextGame
            Mark-CanaryLines 'after-links'
            Open-TextGuide 'Canary Guide B'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Canary guide B loaded')
            Assert-NoReaderCommands 'Canary Guide B'
            Assert-Absent 'ReaderLoadError'
            Assert-NoExternalLinkBar 'opening Canary Guide B'
            $report.phases += 'html-canary-b'

            # html-crash: a stopped renderer shows an error with Reopen,
            # not a blank page, and Reopen opens the guide again.
            Mark-CanaryLines 'before-crash'
            $browsers = @(Get-CimInstance Win32_Process -Filter (
                "ParentProcessId = $ProcessId AND Name = 'msedgewebview2.exe'") |
                ForEach-Object { $_.ProcessId })
            $renderers = @(foreach ($browser in $browsers) {
                Get-CimInstance Win32_Process -Filter (
                    "ParentProcessId = $browser AND Name = 'msedgewebview2.exe'") |
                    Where-Object { $_.CommandLine -match '--type=renderer' }
            })
            if ($renderers.Count -eq 0) {
                throw 'Canary Guide B has no WebView2 renderer to stop.'
            }
            foreach ($renderer in $renderers) {
                Stop-Process -Id $renderer.ProcessId -Force -ErrorAction SilentlyContinue
            }
            [void](Wait-Name 'ReaderLoadError' 'This guide stopped responding.')
            [void](Wait-Name 'ReaderLoadErrorAction' 'Reopen')
            Assert-NoReaderCommands 'Canary Guide B (stopped)'
            $report.htmlCrashScreenshot = Save-WindowScreenshot 'html-crash'
            Invoke-Element (Find-ById 'ReaderLoadErrorAction')
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Canary guide B loaded')
            Assert-Absent 'ReaderLoadError'
            Assert-Absent 'ReaderLoadErrorAction'
            $report.phases += 'html-crash'

            Mark-CanaryLines 'opened-b'
            # Back closes the session, which writes its diagnostics.
            Back-ToTextGame
            Mark-CanaryLines 'closed-b'
        }
        elseif ($Mode -eq 'html-runtime-missing') {
            [void](Wait-Name 'LibraryHeading' 'Library')
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')

            # html-runtime-missing: the guide names the missing runtime and
            # offers Microsoft's page; the click goes to the test launcher.
            Open-TextGuide 'Canary Guide A'
            $runtimeMessage = 'Web page guides need the Microsoft Edge WebView2 Runtime.'
            [void](Wait-Name 'ReaderLoadError' $runtimeMessage)
            [void](Wait-Name 'ReaderLoadErrorAction' 'Get WebView2 Runtime')
            Assert-NoReaderCommands 'Canary Guide A (no runtime)'
            $report.htmlRuntimeMissingScreenshot = Save-WindowScreenshot 'html-runtime-missing'
            Invoke-Element (Find-ById 'ReaderLoadErrorAction')
            Start-Sleep -Seconds 1
            [void](Wait-Name 'ReaderLoadError' $runtimeMessage)
            $report.phases += 'html-runtime-missing'

            # html-missing-entry: the loader runs before WebView2, so a
            # deleted entry still says it is missing, with no action.
            Back-ToTextGame
            Open-TextGuide 'Canary Guide B'
            [void](Wait-Name 'ReaderLoadError' $missingMessage)
            Assert-Absent 'ReaderLoadErrorAction'
            $report.phases += 'html-missing-entry'

            # TXT guides still open, and show no HTML action.
            Back-ToTextGame
            Open-TextGuide 'Plain Text Guide'
            [void](Wait-Status 'Guide ready.')
            Assert-RowNames 'Plain Text Guide' $asciiNames
            Assert-Absent 'ReaderLoadError'
            Assert-Absent 'ReaderLoadErrorAction'
            $report.phases += 'html-runtime-missing-txt'
            Back-ToTextGame
        }
        elseif ($Mode -eq 'html-position') {
            if (-not $AppCacheRoot -or -not $AppDataRoot) {
                throw 'html-position needs -AppDataRoot and -AppCacheRoot.'
            }
            [void](Wait-Name 'LibraryHeading' 'Library')
            # A known width first, so each resize below is a real change.
            Resize-ShellWindow 1500 720
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')
            $report.sessionsOpened = 0
            $report.unimportedClicks = 0
            $report.restoreKinds = @()

            # position-fragment: a fragment link moves the page, the next
            # poll captures it, and the captured line is the visible one.
            Open-TextGuide 'Long Web Guide'
            $report.sessionsOpened++
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Long Web Guide')
            [void](Wait-HtmlPosition { param($p) $p.locator } 'a first capture')
            Click-Element (Wait-PageVisible 'Jump to MARK-0420')
            $target = Wait-HtmlPosition { param($p) $p.quote -like 'MARK-0420 *' } 'the MARK-0420 line'
            [void](Wait-TopMark 420 'the fragment link')
            $report.htmlPositionOffset = $target.offset
            $report.phases += 'position-fragment'

            # position-resize: the <pre> lines rewrap at each width; the same
            # line stays on top and the saved offset doesn't move.
            $report.htmlResizeMarks = @()
            foreach ($width in @(600, 1100, 1500)) {
                Resize-ShellWindow $width 720
                # Past the 300 ms settle, the re-apply and two polls.
                Start-Sleep -Milliseconds 1500
                $report.htmlResizeMarks += Wait-TopMark 420 "a resize to $width px"
                $after = Read-HtmlPosition
                if (-not $after -or $after.offset -ne $target.offset) {
                    throw "After a resize to $width px the position offset was $($after.offset); expected $($target.offset)."
                }
            }
            $report.phases += 'position-resize'

            $saved = (Read-HtmlPosition).locator
            Back-ToTextGame

            # position-restore-exact: the saved locator in a new session, at
            # another width than the capture's, puts the same line on top.
            Resize-ShellWindow 1100 720
            $restored = Open-RestoredGuide 'Long Web Guide' $saved
            Assert-Restore $restored 'Exact' 'Exact' '' 'an exact restore'
            if ($restored.offset -ne $target.offset) {
                throw "An exact restore captured offset $($restored.offset); expected $($target.offset)."
            }
            [void](Wait-TopMark 420 'an exact restore')
            $report.htmlRestoreScreenshot = Save-WindowScreenshot 'html-restore'
            Back-ToTextGame
            $report.phases += 'position-restore-exact'

            # position-restore-late-images: images answer 1 s late. With
            # scripts off the lazy route map above the target loads eagerly,
            # and the restore runs only after the page's load, so the line
            # is still on top.
            $delay = [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\DesktopGuides.Preview.HtmlAssetDelay.$ProcessId")
            try {
                $delayed = Open-RestoredGuide 'Long Web Guide' $saved
            }
            finally {
                $delay.Dispose()
            }
            Assert-Restore $delayed 'Exact' 'Exact' '' 'a restore with late images'
            if ($delayed.offset -ne $target.offset) {
                throw "A restore with late images captured offset $($delayed.offset); expected $($target.offset)."
            }
            [void](Wait-TopMark 420 'a restore with late images')
            Back-ToTextGame
            $report.phases += 'position-restore-late-images'

            # position-restore-changed: in changed bytes the quote is found
            # by context; the text inserted above moved every offset.
            $changed = Open-RestoredGuide 'Changed Long Web Guide' $saved `
                'Opened near your last place. The guide changed since you were here.'
            Assert-Restore $changed 'Approximate' 'Context' `
                'The guide changed, so this is an approximate position.' 'a restore in changed bytes'
            if ($changed.offset -le $target.offset) {
                throw "A restore in changed bytes captured offset $($changed.offset); expected more than $($target.offset)."
            }
            [void](Wait-TopMark 420 'a restore in changed bytes')
            $report.progressApproximateScreenshot = Save-WindowScreenshot 'progress-approximate'
            Back-ToTextGame
            $report.phases += 'position-restore-changed'

            # position-restore-invalid: the shell can't decode a malformed
            # locator, so it says so and the page stays at its start. The
            # session never restores, so no restore kind is counted.
            Clear-HtmlPosition
            Set-RestoreLocator '{"format":"Html","schemaVersion":1,"payload":'
            try {
                Open-TextGuide 'Long Web Guide'
                $report.sessionsOpened++
                [void](Wait-Status "Couldn't return to your last place, so the guide opened at the start.")
                $invalid = Wait-HtmlPosition { param($p) $p.locator } 'a position after a malformed locator'
            }
            finally {
                Clear-RestoreLocator
            }
            if ([string] $invalid.quote -notlike 'Long Web Guide*') {
                throw "After a malformed locator the position was not the page's start."
            }
            $top = Get-PageTopLine
            if ($top -match '^MARK-') {
                throw "After a malformed locator the page's top line was $top."
            }
            $report.progressUnavailableHtmlScreenshot = Save-WindowScreenshot 'progress-unavailable-html'
            Back-ToTextGame
            $report.phases += 'position-restore-invalid'

            # position-unimported-link: a link to a page that wasn't imported
            # shows the unavailable bar, and the point stays where it was.
            $unavailable = "This link goes to a page that isn't part of the imported guide."
            Clear-HtmlPosition
            Open-TextGuide 'Long Web Guide'
            $report.sessionsOpened++
            [void](Wait-Status 'Guide ready.')
            [void](Wait-HtmlPosition { param($p) $p.kind -eq 'Exact' } 'the saved place')
            $report.restoreKinds += 'Exact'
            [void](Wait-PageName 'Long Web Guide')
            $here = Wait-HtmlPosition { param($p) $p.quote -like 'MARK-0420 *' } 'the MARK-0420 line'
            [void](Wait-TopMark 420 'the saved place')
            Click-Element (Wait-PageVisible 'Part 2 of this guide')
            $report.unimportedClicks++
            [void](Wait-VisibleById 'ReaderUnavailableLinkBar')
            [void](Wait-Name 'ReaderUnavailableLinkBar' $unavailable)
            # The bar makes the page shorter; the resize re-apply keeps the
            # point. One second covers the 300 ms settle and a 500 ms poll.
            Start-Sleep -Seconds 1
            [void](Wait-TopMark 420 'an unimported link')
            $after = Read-HtmlPosition
            if ($after.offset -ne $here.offset) {
                throw "An unimported link moved the point from offset $($here.offset) to $($after.offset)."
            }
            $report.htmlUnavailableLinkScreenshot = Save-WindowScreenshot 'html-unavailable-link'

            # Showing either link bar hides the other.
            Click-Element (Wait-PageVisible 'the website')
            [void](Wait-VisibleById 'ReaderExternalLinkBar')
            Wait-HiddenById 'ReaderUnavailableLinkBar'
            Click-Element (Wait-PageVisible 'Part 2 of this guide')
            $report.unimportedClicks++
            [void](Wait-VisibleById 'ReaderUnavailableLinkBar')
            Wait-HiddenById 'ReaderExternalLinkBar'
            Back-ToTextGame

            # A new session starts without the bar.
            Open-TextGuide 'Long Web Guide'
            $report.sessionsOpened++
            [void](Wait-Status 'Guide ready.')
            [void](Wait-HtmlPosition { param($p) $p.kind -eq 'Exact' } 'the saved place')
            $report.restoreKinds += 'Exact'
            [void](Wait-PageName 'Long Web Guide')
            Wait-HiddenById 'ReaderUnavailableLinkBar'
            Back-ToTextGame
            $report.phases += 'position-unimported-link'
        }
        elseif ($Mode -eq 'pdf-reader') {
            [void](Wait-Name 'LibraryHeading' 'Library')
            Resize-ShellWindow 1500 720
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')

            # Tagged text is readable through UI Automation beside its preview.
            Open-TextGuide 'Tagged PDF Guide'
            [void](Wait-Status 'Guide ready.')
            $preview = Wait-PdfPage 1 1 'Tagged guide paragraph for Narrator'
            $bounds = $preview.Current.BoundingRectangle
            if ($bounds.Width -lt 1 -or $bounds.Height -lt 1) {
                throw 'The PDF page preview has no visible size.'
            }
            $textStatus = Get-PdfStatus 'PdfTextStatus'
            if ($textStatus) { throw "Expected no text status for tagged text; saw '$textStatus'." }
            $previewStatus = Get-PdfStatus 'PdfPreviewStatus'
            if ($previewStatus) { throw "Expected no preview status; saw '$previewStatus'." }
            Assert-Absent 'ReaderPlaceholder'
            Assert-Absent 'ReaderLoadError'
            $report.pdfReaderScreenshot = Save-WindowScreenshot 'pdf-reader'

            # Narrow windows stack the text under the preview; both stay shown.
            Resize-ShellWindow 600 720
            [void](Wait-PdfPage 1 1 'Tagged guide paragraph for Narrator')
            $report.pdfReaderNarrowScreenshot = Save-WindowScreenshot 'pdf-reader-narrow'
            Resize-ShellWindow 1500 720
            $report.phases += 'pdf-access'

            # An image-only page says so and claims no text.
            Back-ToTextGame
            Open-TextGuide 'Scanned PDF Guide'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-Name 'PdfPageStatus' 'Page 1 of 1')
            [void](Wait-Name 'PdfTextStatus' 'Image-only page; OCR is unavailable')
            $scanText = Get-PdfText
            if ($scanText) { throw "Expected no text for an image-only page; read '$scanText'." }
            $report.phases += 'pdf-scan'

            # 199 rapid turns end on page 200 with its own preview and text,
            # then Start and End, then a second rapid sweep and a third sweep that
            # waits on every page.
            Back-ToTextGame
            Open-TextGuide 'Long PDF Guide'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            Invoke-NextPages 199
            [void](Wait-PdfPage 200 200 'page 200 of 200' 60)
            Invoke-ReaderCommand 'Go to start'
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            Invoke-ReaderCommand 'Go to end'
            [void](Wait-PdfPage 200 200 'page 200 of 200')
            Invoke-ReaderCommand 'Go to start'
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            Invoke-NextPages 199
            [void](Wait-PdfPage 200 200 'page 200 of 200' 60)
            # Sweep 3 waits on every page, so all 200 previews load and the
            # render cache must evict to stay under its cap.
            Invoke-ReaderCommand 'Go to start'
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            for ($p = 2; $p -le 200; $p++) {
                Invoke-NextPages 1
                [void](Wait-PdfPage $p 200 "page $p of 200")
            }
            $report.phases += 'pdf-long'

            # pdf-resize (T10.3): a point 30% down portrait page 121 survives
            # narrow, medium and wide windows; a page turn starts at the top.
            Invoke-ReaderCommand 'Go to start'
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            Invoke-NextPages 120
            [void](Wait-PdfPage 121 200 'page 121 of 200' 60)
            $scroll = Get-PdfScroll
            $room = 1 - $scroll.Current.VerticalViewSize / 100
            if (-not $scroll.Current.VerticallyScrollable -or $room -lt 0.3) {
                throw "Page 121 scrolls only $([Math]::Round($room, 3)) of its height; pdf-resize needs 0.3."
            }
            $scroll.SetScrollPercent(
                [System.Windows.Automation.ScrollPattern]::NoScroll, 0.3 / $room * 100)
            Start-Sleep -Milliseconds 300
            $set = Get-PdfFraction
            if ([Math]::Abs($set - 0.3) -gt 0.02) {
                throw "Scrolling page 121 reached fraction $([Math]::Round($set, 3)); expected 0.3."
            }
            $report.pdfPosition = [ordered]@{ set = $set }
            # 1500 last: the page was scrolled there, so 0.3 itself must return
            # even if 1100 px clamped it.
            foreach ($size in @(@(600, 'narrow'), @(1100, 'medium'), @(1500, 'wide'))) {
                Resize-ShellWindow $size[0] 720
                $report.pdfPosition[$size[1]] = Wait-PdfFraction 0.3 "Resizing to $($size[0]) px"
                [void](Wait-PdfPage 121 200 'page 121 of 200')
            }
            $report.pdfResizeScreenshot = Save-WindowScreenshot 'pdf-resize'
            # Page 122 is landscape and may not scroll; 123 is portrait.
            Invoke-NextPages 2
            [void](Wait-PdfPage 123 200 'page 123 of 200')
            Start-Sleep -Milliseconds 300
            $nextPage = Get-PdfFraction
            if ($nextPage -ge 0.02) {
                throw "Page 123 opened at fraction $([Math]::Round($nextPage, 3)); expected its top."
            }
            $report.pdfPosition.nextPage = $nextPage
            $report.phases += 'pdf-resize'

            # Going back closes the session, which writes the diagnostics.
            Back-ToTextGame
            $damaged = "This PDF is damaged, so it can't be opened. Re-import it from the original file."
            Open-TextGuide 'Damaged PDF Guide'
            [void](Wait-Status $damaged)
            [void](Wait-Name 'ReaderLoadError' $damaged)
            Assert-Absent 'ReaderLoadErrorAction'
            Assert-Absent 'PdfDocumentText'
            $report.pdfErrorScreenshot = Save-WindowScreenshot 'pdf-error'
            $report.phases += 'pdf-damaged'

            Back-ToTextGame
            Open-TextGuide 'Missing PDF Guide'
            [void](Wait-Status $missingMessage)
            [void](Wait-Name 'ReaderLoadError' $missingMessage)
            Assert-Absent 'ReaderLoadErrorAction'
            Assert-Absent 'PdfDocumentText'
            $report.phases += 'pdf-missing'

            # TXT guides still open after the PDF errors.
            Back-ToTextGame
            Open-TextGuide 'Plain Text Guide'
            [void](Wait-Status 'Guide ready.')
            Assert-RowNames 'Plain Text Guide' $asciiNames
            Assert-Absent 'ReaderLoadError'
            $report.phases += 'pdf-txt'
            Back-ToTextGame
        }
        elseif ($Mode -like 'progress-*') {
            $saveFailed = "Couldn't save your place in this guide."

            function Read-ProgressCounts {
                $path = Join-Path $AppCacheRoot "diagnostics\progress-$ProcessId.json"
                $deadline = (Get-Date).AddSeconds(2)
                do {
                    try {
                        if (Test-Path -LiteralPath $path) {
                            return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
                        }
                    }
                    catch {
                        # Read during the atomic replace; try again.
                    }
                    Start-Sleep -Milliseconds 100
                } while ((Get-Date) -lt $deadline)
                return [pscustomobject] @{ saves = 0; skippedUnchanged = 0; failures = 0 }
            }

            function Assert-NoSaveFailure([string] $step) {
                $counts = Read-ProgressCounts
                if ($counts.failures -ne 0) { throw "$step had $($counts.failures) failed saves." }
                # The status probe holds the latest message as 'sequence|message'.
                $probe = Find-RawById 'ShellContent'
                if ($probe -and $probe.Current.ItemStatus -like "*|$saveFailed") {
                    throw "$step showed '$saveFailed'."
                }
            }

            function Open-NumberedAt([int] $line, [string] $step) {
                Open-TextGuide 'Numbered Lines Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                Wait-FirstTextRow
                Wait-TopLine $line $step
            }

            [void](Wait-Name 'LibraryHeading' 'Library')
            Resize-ShellWindow 1500 720
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')

            if ($Mode -eq 'progress-timer') {
                # progress-timer: the quiet timer saves within 5 s with no
                # flush; the runner then kills the process.
                Open-NumberedAt 1 'A first open'
                $top = 1
                for ($i = 0; $i -lt 3; $i++) {
                    Invoke-ReaderCommand 'Next page'
                    $top = Wait-TopLineChange $top 'Next page'
                }
                $report.progressTopLine = $top
                Start-Sleep -Seconds 5
                $counts = Read-ProgressCounts
                if ($counts.saves -lt 1) { throw "No save within 5 s of the last movement." }
                Assert-NoSaveFailure 'progress-timer'
                $report.progressTimerCounts = $counts
                $report.phases += 'progress-timer'
            }
            elseif ($Mode -eq 'progress-restored') {
                Open-NumberedAt $ExpectedTopLine 'Reopening after the process was killed'
                Back-ToTextGame

                # progress-flush: Back saves a PDF place at once.
                Open-TextGuide 'Long PDF Guide'
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PdfPage 1 200 'page 1 of 200')
                Invoke-NextPages 120
                [void](Wait-PdfPage 121 200 'page 121 of 200' 60)
                $scroll = Get-PdfScroll
                $room = 1 - $scroll.Current.VerticalViewSize / 100
                if (-not $scroll.Current.VerticallyScrollable -or $room -lt 0.3) {
                    throw "Page 121 scrolls only $([Math]::Round($room, 3)) of its height; progress-flush needs 0.3."
                }
                $scroll.SetScrollPercent(
                    [System.Windows.Automation.ScrollPattern]::NoScroll, 0.3 / $room * 100)
                Back-ToTextGame
                Open-TextGuide 'Long PDF Guide'
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PdfPage 121 200 'page 121 of 200')
                $report.progressPdfFraction = Wait-PdfFraction 0.3 'Reopening after Back'
                Back-ToTextGame
                $report.phases += 'progress-flush'

                # progress-burst: 30 page turns write at most once per 4 s
                # deadline plus the final quiet save.
                Open-NumberedAt $ExpectedTopLine 'Reopening for the burst'
                Start-Sleep -Seconds 2
                $before = Read-ProgressCounts
                $watch = [System.Diagnostics.Stopwatch]::StartNew()
                foreach ($command in @(@('Next page') * 20 + @('Previous page') * 10)) {
                    Invoke-ReaderCommand $command
                    Start-Sleep -Milliseconds 100
                }
                $elapsed = $watch.Elapsed.TotalSeconds
                Start-Sleep -Seconds 6
                if ($elapsed -ge 8) {
                    throw "The burst took $([Math]::Round($elapsed, 1)) s; the bound assumes under 8 s."
                }
                $after = Read-ProgressCounts
                $saves = $after.saves - $before.saves
                $allowed = [Math]::Floor($elapsed / 4) + 1
                if ($saves -lt 1 -or $saves -gt $allowed) {
                    throw "The burst made $saves saves in $([Math]::Round($elapsed, 1)) s; expected 1 to $allowed."
                }
                Assert-NoSaveFailure 'progress-burst'
                $report.progressBurst = [ordered]@{ seconds = $elapsed; saves = $saves; allowed = $allowed }
                $report.progressTopLine = Get-TopLine
                Back-ToTextGame
                $report.phases += 'progress-burst'

                # The HTML guide moves last and stays open: closing the window saves it.
                Open-TextGuide 'Long Web Guide'
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PageName 'Long Web Guide')
                Click-Element (Wait-PageVisible 'Jump to MARK-0420')
                [void](Wait-TopMark 420 'the fragment link')
                $report.phases += 'progress-restored'
            }
            elseif ($Mode -eq 'progress-row') {
                # progress-row: after a normal close and relaunch, moved guides
                # show their estimate and open time; an unopened guide does not.
                $report.progressRows = Assert-RowFacts 'GuideList' @(
                    @('Numbered Lines Guide', 'Text (TXT), about * percent, opened today at *'),
                    @('Long Web Guide', 'Web page (HTML), about * percent, opened today at *'),
                    @('Long PDF Guide', 'PDF, about * percent, opened today at *'),
                    @('Unopened Guide', 'Text (TXT), Not started'))
                Wait-HiddenById 'ShellStatus'
                $report.progressRowScreenshot = Save-WindowScreenshot 'progress-row'
                $report.phases += 'progress-row'
            }
            elseif ($Mode -eq 'progress-changed') {
                # progress-changed: changed managed copies restore near the
                # saved place by its estimate, and say so.
                $approximate = 'Opened near your last place. The guide changed since you were here.'
                Open-TextGuide 'Numbered Lines Guide'
                [void](Wait-Status $approximate)
                Wait-FirstTextRow
                Wait-TopLine $ExpectedTopLine 'Reopening a changed TXT guide'
                $report.progressChangedTxtScreenshot = Save-WindowScreenshot 'progress-changed-txt'
                Back-ToTextGame
                Open-TextGuide 'Long PDF Guide'
                [void](Wait-Status $approximate)
                [void](Wait-PdfPage 121 200 'page 121 of 200')
                $report.progressPdfFraction = Wait-PdfFraction 0.3 'Reopening a changed PDF guide'
                $report.progressChangedPdfScreenshot = Save-WindowScreenshot 'progress-changed-pdf'
                Back-ToTextGame
                Assert-NoSaveFailure 'progress-changed'
                $report.phases += 'progress-changed'
            }
            else {
                # progress-two-guides: after a normal close both guides
                # reopen at their own places.
                Open-NumberedAt $ExpectedTopLine 'Reopening the TXT guide after a restart'
                Back-ToTextGame
                Open-TextGuide 'Long Web Guide'
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PageName 'Long Web Guide')
                [void](Wait-TopMark 420 'Reopening the HTML guide after a restart')
                Back-ToTextGame
                $report.phases += 'progress-two-guides'

                # progress-unavailable: a lost place opens at the start, says
                # so, and leaves the stored place for the next open.
                Set-RestoreLocator '{'
                try {
                    Open-TextGuide 'Numbered Lines Guide'
                    [void](Wait-Status "Couldn't return to your last place, so the guide opened at the start.")
                    Wait-FirstTextRow
                    Wait-TopLine 1 'An unreadable saved place'
                    $report.progressUnavailableTxtScreenshot = Save-WindowScreenshot 'progress-unavailable-txt'
                }
                finally {
                    Clear-RestoreLocator
                }
                Back-ToTextGame
                Open-NumberedAt $ExpectedTopLine 'Reopening after an unreadable place'
                Back-ToTextGame
                Assert-NoSaveFailure 'progress-two-guides'
                $report.phases += 'progress-unavailable'
            }
        }
        elseif ($Mode -like 'completion-*') {
            $numbered = 'Numbered Lines Guide'
            $web = 'Long Web Guide'
            $pdf = 'Long PDF Guide'

            function Test-ItemSelected([string] $id) {
                $item = Find-ById $id
                if (-not $item -or $item.Current.IsOffscreen) { return $false }
                return $item.GetCurrentPattern(
                    [System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
            }

            # The committed state is the only selected item.
            function Wait-CompletionShown([bool] $complete, [string] $step) {
                $on = if ($complete) { 'CompletionComplete' } else { 'CompletionInProgress' }
                $off = if ($complete) { 'CompletionInProgress' } else { 'CompletionComplete' }
                $deadline = (Get-Date).AddSeconds(10)
                do {
                    if ((Test-ItemSelected $on) -and -not (Test-ItemSelected $off)) { return }
                    Start-Sleep -Milliseconds 100
                } while ((Get-Date) -lt $deadline)
                throw "$step expected '$on' as the only selected completion item."
            }

            function Focus-Completion {
                $id = if (Test-ItemSelected 'CompletionComplete') { 'CompletionComplete' }
                      else { 'CompletionInProgress' }
                (Wait-VisibleById $id).SetFocus()
                Wait-FocusedId $id
            }

            function Send-Keys([string] $keys) {
                [System.Windows.Forms.SendKeys]::SendWait($keys)
            }

            function Get-RowHelp([string] $name) {
                $row = @(Get-ListRows 'GuideList' | Where-Object { $_.Current.Name -eq $name })
                if ($row.Count -ne 1) { return $null }
                return $row[0].Current.HelpText
            }

            # -Exact compares the whole text; otherwise $pattern is a -like pattern.
            function Wait-RowHelp([string] $name, [string] $pattern, [switch] $Exact, [switch] $NotCompleted) {
                $deadline = (Get-Date).AddSeconds(10)
                do {
                    $help = Get-RowHelp $name
                    $matched = $help -and $(if ($Exact) { $help -ceq $pattern } else { $help -like $pattern })
                    if ($matched -and $NotCompleted) { $matched = $help -notlike '*Completed*' }
                    if ($matched) { return $help }
                    Start-Sleep -Milliseconds 200
                } while ((Get-Date) -lt $deadline)
                throw "Row '$name' help text was '$help'; expected '$pattern'."
            }

            function Anchor-Guide([string] $guide) {
                Open-TextGuide $guide
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                Back-ToTextGame
                [void](Wait-VisibleById 'CompletionInProgress')
            }

            if ($Mode -notin @('completion-error', 'completion-error-retry')) {
                [void](Wait-Name 'LibraryHeading' 'Library')
                Resize-ShellWindow 1500 720
                Select-Element $textGame
                [void](Wait-Name 'GameHeading' $textGame)
                [void](Wait-Status 'Game ready.')
            }

            if ($Mode -eq 'completion-segmented') {
                # The UIA gate: two radio buttons with their names, the selected
                # state matching storage and following the arrow keys.
                Anchor-Guide $numbered
                $choice = Wait-VisibleById 'CompletionChoice'
                if ($choice.Current.Name -ne "Completion for $numbered") {
                    throw "The choice is named '$($choice.Current.Name)'."
                }
                foreach ($pair in @(@('CompletionInProgress', 'In progress'), @('CompletionComplete', 'Complete'))) {
                    $item = Wait-VisibleById $pair[0]
                    if ($item.Current.ControlType -ne [System.Windows.Automation.ControlType]::RadioButton -or
                        $item.Current.Name -ne $pair[1]) {
                        throw "$($pair[0]) is a $($item.Current.ControlType.ProgrammaticName) named '$($item.Current.Name)'."
                    }
                }
                Wait-CompletionShown $false 'A guide never completed'
                (Wait-VisibleById 'OpenSelectedGuide').SetFocus()
                Wait-FocusedId 'OpenSelectedGuide'
                Send-Keys '+{TAB}'
                Wait-FocusedId 'CompletionInProgress'
                Send-Keys '{RIGHT}'
                [void](Wait-Status "$numbered marked complete.")
                Wait-CompletionShown $true 'Right'
                Wait-FocusedId 'CompletionComplete'
                Send-Keys '{LEFT}'
                [void](Wait-Status "$numbered marked in progress.")
                Wait-CompletionShown $false 'Left'
                Wait-FocusedId 'CompletionInProgress'
                # The CI launch size: the header actions stay whole and the
                # guide list keeps GamePageLayout.MinGuideListHeight (96).
                Resize-ShellWindow 768 519
                $scale = [DesktopGuidesForegroundProbe]::Dpi($process.MainWindowHandle) / 96.0
                $deadline = (Get-Date).AddSeconds(5)
                do {
                    $import = (Wait-VisibleById 'ImportGuideButton').Current.BoundingRectangle
                    $list = (Wait-VisibleById 'GuideList').Current.BoundingRectangle
                    $fits = $import.Right -le $list.Right + 1 -and $list.Height -ge 96 * $scale - 1
                    if ($fits) { break }
                    Start-Sleep -Milliseconds 200
                } while ((Get-Date) -lt $deadline)
                if (-not $fits) {
                    throw "At 768x519 Import guide is $import and the guide list is $list."
                }
                $report.completionNarrow = [ordered]@{ import = $import.ToString(); list = $list.ToString() }
                Resize-ShellWindow 1500 720
                $report.phases += 'completion-segmented'
            }
            elseif ($Mode -eq 'completion-last-page') {
                # TR13.1: reaching the last line or page doesn't complete a guide.
                Open-TextGuide $numbered
                [void](Wait-Status 'Guide ready.')
                Invoke-ReaderCommand 'Go to end'
                Start-Sleep -Seconds 1
                Back-ToTextGame
                Open-TextGuide $pdf
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                Invoke-ReaderCommand 'Go to end'
                [void](Wait-PdfPage 200 200 'page 200 of 200' 60)
                Back-ToTextGame
                $report.completionLastPageRows = @(
                    (Wait-RowHelp $numbered 'Text (TXT), about * percent, opened today at *'),
                    (Wait-RowHelp $pdf 'PDF, about * percent, opened today at *'))
                Wait-CompletionShown $false 'The last PDF page'
                $report.phases += 'completion-last-page'
            }
            elseif ($Mode -eq 'completion-game') {
                # The Game page: complete, then back to in progress with the
                # row's estimate and open time shown exactly as before.
                Anchor-Guide $numbered
                $before = Wait-RowHelp $numbered 'Text (TXT), *' -NotCompleted
                Focus-Completion
                Send-Keys '{RIGHT}'
                [void](Wait-Status "$numbered marked complete.")
                $completed = Wait-RowHelp $numbered 'Text (TXT), Completed, opened today at *'
                Wait-CompletionShown $true 'Marking complete on the Game page'
                Wait-FocusedId 'CompletionComplete'
                $report.completionGameScreenshot = Save-WindowScreenshot 'completion-game'
                Send-Keys '{LEFT}'
                [void](Wait-Status "$numbered marked in progress.")
                $after = Wait-RowHelp $numbered $before -Exact
                Wait-CompletionShown $false 'Marking in progress on the Game page'
                Wait-FocusedId 'CompletionInProgress'
                $report.completionGameRows = [ordered]@{ before = $before; completed = $completed; after = $after }
                $report.phases += 'completion-game'
            }
            elseif ($Mode -eq 'completion-reader') {
                # The light pass finds Web in progress and completes it. The
                # dark pass finds it complete, toggles twice, and ends complete.
                Open-TextGuide $web
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                [void](Wait-VisibleById 'CompletionChoice')
                $startedComplete = Test-ItemSelected 'CompletionComplete'
                Focus-Completion
                if ($startedComplete) {
                    Send-Keys '{LEFT}'
                    [void](Wait-Status "$web marked in progress.")
                    Wait-CompletionShown $false 'Marking in progress in the Reader'
                    Wait-FocusedId 'CompletionInProgress'
                }
                Send-Keys '{RIGHT}'
                [void](Wait-Status "$web marked complete.")
                Wait-CompletionShown $true 'Marking complete in the Reader'
                Wait-FocusedId 'CompletionComplete'
                $report.completionReaderStartedComplete = $startedComplete
                $report.completionReaderScreenshot = Save-WindowScreenshot 'completion-reader'
                Back-ToTextGame
                $report.completionReaderRow = Wait-RowHelp $web 'Web page (HTML), Completed, opened today at *'
                $report.phases += 'completion-reader'
            }
            elseif ($Mode -eq 'completion-restart') {
                # TR13.2: completion survives a relaunch, and so does clearing it.
                [void](Wait-RowHelp $web 'Web page (HTML), Completed, opened today at *')
                Open-TextGuide $web
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                Wait-CompletionShown $true 'Reopening a completed guide'
                Focus-Completion
                Send-Keys '{LEFT}'
                [void](Wait-Status "$web marked in progress.")
                Wait-CompletionShown $false 'Marking in progress after a relaunch'
                Back-ToTextGame
                $report.phases += 'completion-restart'
            }
            elseif ($Mode -eq 'completion-restart-after') {
                $report.completionRestartRow = Wait-RowHelp $web 'Web page (HTML), *opened today at *' -NotCompleted
                Open-TextGuide $web
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                Wait-CompletionShown $false 'Reopening after clearing completion'
                Back-ToTextGame
                $report.phases += 'completion-restart-after'
            }
            elseif ($Mode -eq 'completion-error-prepare') {
                # The installer takes the write lock after this mode.
                Anchor-Guide $numbered
                Wait-CompletionShown $false 'Before the held lock'
                $report.phases += 'completion-error-prepare'
            }
            elseif ($Mode -eq 'completion-error') {
                # The write waits on the held lock. Left while it waits keeps
                # the pending choice; the timeout shows the error and puts
                # the choice back.
                Focus-Completion
                Send-Keys '{RIGHT}'
                Wait-CompletionShown $true 'The pending write'
                Send-Keys '{LEFT}'
                Start-Sleep -Seconds 1
                Wait-CompletionShown $true 'Left while the write is pending'
                [void](Wait-Status "Could not update completion for $numbered. Try again." -Seconds 60)
                Wait-CompletionShown $false 'After the failed write'
                [void](Wait-RowHelp $numbered 'Text (TXT), *' -NotCompleted)
                $report.completionErrorScreenshot = Save-WindowScreenshot 'completion-error'
                $report.phases += 'completion-error'
            }
            else {
                # completion-error-retry: the lock is gone and the retry commits.
                Focus-Completion
                Send-Keys '{RIGHT}'
                [void](Wait-Status "$numbered marked complete.")
                Wait-CompletionShown $true 'The retry'
                $report.phases += 'completion-error-retry'
            }
        }
        else {
            # The view re-measures at a larger test font size on this signal.
            $remeasure = [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::AutoReset,
                "Local\DesktopGuides.Preview.TextRemeasure.$($process.Id)")
            $report.txtPosition = [ordered]@{}
            [void](Wait-Name 'LibraryHeading' 'Library')
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')

            # txt-ascii: exact lines, whitespace kept, a 2048-column line scrolls sideways.
            Open-TextGuide 'ASCII Map Guide'
            [void](Wait-Status 'Guide ready.')
            Assert-RowNames 'ASCII Map Guide' $asciiNames
            [void](Wait-Name 'ReaderTextLines' 'Guide text')
            $scroll = (Find-ById 'ReaderTextLines').GetCurrentPattern(
                [System.Windows.Automation.ScrollPattern]::Pattern)
            if (-not $scroll.Current.HorizontallyScrollable) {
                throw 'The long txt-ascii line did not make the reader scroll sideways.'
            }
            Assert-Absent 'ReaderPlaceholder'
            Assert-Absent 'ReaderLoadError'
            foreach ($name in 'Go to start', 'Previous page', 'Next page', 'Go to end') {
                $button = Find-ByName $name
                if (-not $button -or $button.Current.IsOffscreen) {
                    throw "ASCII Map Guide has no visible '$name' command."
                }
            }
            $report.txtReaderScreenshot = Save-WindowScreenshot 'txt-reader'
            $report.phases += 'txt-ascii'

            # txt-tabs: 8-column tab stops; a form feed shows as a space.
            Back-ToTextGame
            Open-TextGuide 'Tab Table Guide'
            Assert-RowNames 'Tab Table Guide' @(
                'Item    Cost    Where',
                'Potion  50      Item shop',
                'Elixir  1500    Secret room',
                ' Chapter 2')
            $report.phases += 'txt-tabs'

            # A guide saved with code page 437 shows its decoded characters.
            Back-ToTextGame
            Open-TextGuide 'Legacy Code Page Guide'
            Assert-RowNames 'Legacy Code Page Guide' @("Guide $([char]0x00E9)", 'Item list')
            $report.phases += 'txt-legacy'

            # txt-long: the P0 measures, gated at the P0 thresholds.
            Back-ToTextGame
            Show-TextGuide 'Long Text Guide'
            $clock = [System.Diagnostics.Stopwatch]::StartNew()
            Open-GuideFromGame 'Long Text Guide'
            Wait-FirstTextRow
            $clock.Stop()
            [void](Wait-Status 'Guide ready.')
            $realizedAfterOpen = @(Get-TextRows).Count
            $scroll = (Find-ById 'ReaderTextLines').GetCurrentPattern(
                [System.Windows.Automation.ScrollPattern]::Pattern)
            $monitor = [WindowResponseMonitor]::new($process.MainWindowHandle)
            $monitor.Start()
            try {
                for ($step = 0; $step -lt 8; $step++) {
                    $scroll.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount,
                        [System.Windows.Automation.ScrollAmount]::LargeIncrement)
                }
                Start-Sleep -Milliseconds 500
            }
            finally {
                $monitor.Stop()
            }
            $realizedAfterScroll = @(Get-TextRows).Count
            $report.txtLong = [ordered]@{
                firstTextMilliseconds = $clock.ElapsedMilliseconds
                realizedAfterOpen = $realizedAfterOpen
                realizedAfterScroll = $realizedAfterScroll
                verticalScrollPercent = $scroll.Current.VerticalScrollPercent
                responseSamples = $monitor.Samples
                responseMaximumMilliseconds = $monitor.MaximumMilliseconds
                responseSlowSamples = $monitor.SlowSamples
                responseTimeouts = $monitor.Timeouts
            }
            if ($clock.ElapsedMilliseconds -gt 3000) {
                throw "txt-long first text took $($clock.ElapsedMilliseconds) ms; the limit is 3000 ms."
            }
            if ($realizedAfterOpen -gt 300 -or $realizedAfterScroll -gt 300) {
                throw "txt-long realized $realizedAfterOpen then $realizedAfterScroll rows; the limit is 300."
            }
            if ($scroll.Current.VerticalScrollPercent -le 0) {
                throw 'txt-long did not scroll.'
            }
            if ($monitor.Samples -lt 5) {
                throw "The response monitor took only $($monitor.Samples) samples."
            }
            if ($monitor.SlowSamples -ge 2 -or $monitor.Timeouts -gt 0) {
                throw "txt-long had $($monitor.SlowSamples) responses over 500 ms and $($monitor.Timeouts) timeouts."
            }
            $report.phases += 'txt-long'

            Back-ToTextGame

            # A missing managed file shows one sentence and no text or commands.
            Open-TextGuide 'Missing File Guide'
            [void](Wait-Status $missingMessage)
            [void](Wait-Name 'ReaderLoadError' $missingMessage)
            Assert-Absent 'ReaderTextLines'
            Assert-Absent 'ReaderPlaceholder'
            Assert-NoReaderCommands 'Missing File Guide'
            $report.phases += 'txt-missing'

            # A Web Page Guide with no saved asset rows (imported before
            # schema v4) asks to be re-imported.
            Back-ToTextGame
            Open-TextGuide 'Web Page Guide'
            [void](Wait-Status 'Re-import this guide to read it.')
            [void](Wait-Name 'ReaderLoadError' 'Re-import this guide to read it.')
            Assert-Absent 'ReaderTextLines'
            Assert-Absent 'ReaderPlaceholder'
            Assert-NoReaderCommands 'Web Page Guide'
            $report.phases += 'html-no-manifest'

            # Reopening reads the file again.
            Back-ToTextGame
            Open-TextGuide 'ASCII Map Guide'
            Assert-RowNames 'ASCII Map Guide (reopened)' $asciiNames
            $report.phases += 'txt-reopen'

            # txt-commands: whole-row pages, Start and End.
            Back-ToTextGame
            Open-TextGuide 'Numbered Lines Guide'
            [void](Wait-Status 'Guide ready.')
            Wait-StatusClosed
            Wait-FirstTextRow
            Wait-TopLine 1 'Opening the guide'
            $visibleRows = Get-FullyVisibleRows
            Invoke-ReaderCommand 'Next page'
            $pageStep = (Wait-TopLineChange 1 'Next page') - 1
            if ($pageStep -lt 1 -or $pageStep -gt $visibleRows) {
                throw "Next page moved $pageStep rows; $visibleRows rows were fully visible."
            }
            Invoke-ReaderCommand 'Next page'
            Wait-TopLine (1 + 2 * $pageStep) 'A second Next page'
            Invoke-ReaderCommand 'Previous page'
            Wait-TopLine (1 + $pageStep) 'Previous page'
            Invoke-ReaderCommand 'Go to end'
            $endTop = Wait-TopLineChange (1 + $pageStep) 'Go to end'
            $last = @(Get-TextRows | Where-Object {
                -not $_.Current.IsOffscreen -and $_.Current.Name -like 'Line 0400 *' })
            if ($last.Count -ne 1) { throw 'Go to end did not show Line 0400.' }
            # Previous after End pages from the real top line.
            Invoke-ReaderCommand 'Previous page'
            Wait-TopLine ([Math]::Max(1, $endTop - $pageStep)) 'Previous page after Go to end'
            Invoke-ReaderCommand 'Go to start'
            Wait-TopLine 1 'Go to start'
            $report.txtPosition.pageStep = $pageStep
            $report.txtPosition.visibleRows = $visibleRows
            $report.txtPosition.endTopLine = $endTop
            $report.phases += 'txt-commands'

            # txt-resize: near the end, a taller window clamps the top line;
            # shrinking again brings the kept line back.
            $window = $root.Current.BoundingRectangle
            $listHeight = (Find-ById 'ReaderTextLines').Current.BoundingRectangle.Height
            $rowHeight = Get-TopRowHeight
            # Leave about three rows, so the shorter page step ends past the taller window's last top line.
            $shrink = [int]($listHeight - 3.5 * $rowHeight)
            Resize-ShellWindow ([int]$window.Width) ([int]($window.Height - $shrink))
            Invoke-ReaderCommand 'Go to end'
            $shortEnd = Wait-TopLineChange 1 'Go to end in a shorter window'
            Invoke-ReaderCommand 'Previous page'
            $anchor = Wait-TopLineChange $shortEnd 'Previous page in a shorter window'
            Resize-ShellWindow ([int]$window.Width) ([int]$window.Height)
            $clamped = Wait-TopLineChange $anchor 'The taller window'
            Resize-ShellWindow ([int]$window.Width) ([int]($window.Height - $shrink))
            Wait-TopLine $anchor 'A shorter window again'
            $report.txtPosition.resizeTopLines = @($anchor, $clamped)
            Resize-ShellWindow ([int]$window.Width) ([int]$window.Height)
            Invoke-ReaderCommand 'Go to start'
            Wait-TopLine 1 'Go to start after resizing'
            Invoke-ReaderCommand 'Next page'
            $anchor = Wait-TopLineChange 1 'Next page before re-measuring'
            $report.phases += 'txt-resize'

            # txt-remeasure (issue #29): larger text grows the rows, keeps the line.
            $heightBefore = Get-TopRowHeight
            [void]$remeasure.Set()
            $deadline = (Get-Date).AddSeconds(10)
            do {
                Start-Sleep -Milliseconds 100
                $heightRatio = (Get-TopRowHeight) / $heightBefore
            } while (($heightRatio -lt 1.3 -or $heightRatio -gt 1.7) -and (Get-Date) -lt $deadline)
            if ($heightRatio -lt 1.3 -or $heightRatio -gt 1.7) {
                throw "Re-measured rows were $([Math]::Round($heightRatio, 2))x as tall; expected about 1.5x."
            }
            Wait-TopLine $anchor 'Re-measuring the rows'
            $report.txtPosition.heightRatio = $heightRatio

            # The 2,048-column line widens with the text, so it still scrolls fully.
            Back-ToTextGame
            Open-TextGuide 'ASCII Map Guide'
            Assert-RowNames 'ASCII Map Guide (before re-measuring)' $asciiNames
            # Row rectangles are clipped to the viewport, so compare the
            # horizontal view size (viewport / extent) instead.
            $scroll = (Find-ById 'ReaderTextLines').GetCurrentPattern(
                [System.Windows.Automation.ScrollPattern]::Pattern)
            $viewBefore = $scroll.Current.HorizontalViewSize
            [void]$remeasure.Set()
            $deadline = (Get-Date).AddSeconds(10)
            do {
                Start-Sleep -Milliseconds 100
                $widthRatio = $viewBefore / $scroll.Current.HorizontalViewSize
            } while (($widthRatio -lt 1.3 -or $widthRatio -gt 1.7) -and (Get-Date) -lt $deadline)
            if ($widthRatio -lt 1.3 -or $widthRatio -gt 1.7) {
                throw "The re-measured extent was $([Math]::Round($widthRatio, 2))x as wide; expected about 1.5x."
            }
            $report.txtPosition.widthRatio = $widthRatio
            $report.phases += 'txt-remeasure'

            # txt-switch: a reopened guide returns to its saved line with normal rows.
            Back-ToTextGame
            Open-TextGuide 'Numbered Lines Guide'
            [void](Wait-Status 'Guide ready.')
            Wait-StatusClosed
            Wait-FirstTextRow
            Wait-TopLine $anchor 'Reopening the Numbered guide'
            $switchRatio = (Get-TopRowHeight) / $heightBefore
            if ([Math]::Abs($switchRatio - 1) -gt 0.1) {
                throw "A new TXT session kept the test text size (rows $([Math]::Round($switchRatio, 2))x)."
            }
            Invoke-ReaderCommand 'Next page'
            Wait-TopLine ($anchor + $pageStep) 'Next page after switching guides'
            $report.phases += 'txt-switch'

            # txt-horizontal: paging, Go to start and resizing keep the sideways scroll.
            Back-ToTextGame
            Open-TextGuide 'ASCII Map Guide'
            Assert-RowNames 'ASCII Map Guide (horizontal)' $asciiNames
            [void](Wait-Status 'Guide ready.')
            Wait-StatusClosed
            $window = $root.Current.BoundingRectangle
            $listHeight = (Find-ById 'ReaderTextLines').Current.BoundingRectangle.Height
            # About three rows, so Next page moves through the seven lines.
            $shrink = [int]($listHeight - 3.5 * (Get-TopRowHeight))
            Resize-ShellWindow ([int]$window.Width) ([int]($window.Height - $shrink))
            $scroll = (Find-ById 'ReaderTextLines').GetCurrentPattern(
                [System.Windows.Automation.ScrollPattern]::Pattern)
            $scroll.SetScrollPercent(50, [System.Windows.Automation.ScrollPattern]::NoScroll)
            Start-Sleep -Milliseconds 300
            $horizontal = $scroll.Current.HorizontalScrollPercent
            if ($horizontal -lt 10) { throw "The ASCII guide scrolled only to $horizontal% sideways." }
            $topName = Get-TopRowName
            Invoke-ReaderCommand 'Next page'
            $deadline = (Get-Date).AddSeconds(10)
            while ((Get-TopRowName) -eq $topName -and (Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 100
            }
            if ((Get-TopRowName) -eq $topName) { throw 'Next page did not move the ASCII guide.' }
            Assert-HorizontalPercent $scroll $horizontal 'Next page'
            Invoke-ReaderCommand 'Go to start'
            Assert-HorizontalPercent $scroll $horizontal 'Go to start'
            Resize-ShellWindow ([int]$window.Width) ([int]$window.Height)
            Assert-HorizontalPercent $scroll $horizontal 'The restored window'
            $report.txtPosition.horizontalPercent = $horizontal
            $report.phases += 'txt-horizontal'
            $remeasure.Dispose()
        }
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
        Assert-InsideWindow 'RemoveGameButton'
        Assert-ReachesWindowRightEdge 'ShellContent'
        Assert-GameDetailsUncapped
        $report.gameWideScreenshot = Save-WindowScreenshot 'game-wide'
        $report.phases += 'game-wide-full-width-metadata'

        Resize-ShellWindow $narrowWidth $windowHeight
        Assert-InsideWindow 'GameHeading'
        Assert-InsideWindow 'EditGameButton'
        Assert-InsideWindow 'RemoveGameButton'
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
        [void](Wait-Status 'Guide ready.')
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
        Assert-InsideWindow 'ReaderTextLines'
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
        [void](Wait-Status 'Guide ready.')
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

        # Press Clear from the keyboard, so focus starts on the button it hides.
        [void](Wait-VisibleById 'LibrarySearchClear')
        Focus-And-Verify 'LibrarySearchClear'
        [System.Windows.Forms.SendKeys]::SendWait(' ')
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
        [void](Wait-FocusedGameRow $zeta)
        $report.phases += 'search-kept-after-back'

        Assert-NoRemoteConnections 'library search'
        $report.phases += 'search-no-provider-traffic'
    }
    elseif ($Mode -eq 'stable-navigation') {
        $atlas = 'Atlas Navigation Game'
        $beacon = 'Beacon Navigation Game'
        $cobalt = 'Cobalt Other Game'
        $renamedBeacon = 'Beacon Renamed Game'
        $query = 'navigation'

        function Assert-QueryKept([string] $where) {
            if ((Get-SearchText 'LibrarySearchInput') -ne $query) {
                throw "$where dropped the Library search query."
            }
        }

        function Assert-NavigationRows {
            [void](Wait-GameRow $atlas)
            [void](Wait-GameRow $beacon)
            if ((Count-GameRows $cobalt) -ne 0) {
                throw "The '$query' query showed $cobalt."
            }
        }

        function Back-ToLibrary([string] $where) {
            Go-Back
            [void](Wait-Name 'LibraryHeading' 'Library')
            [void](Wait-Status 'Library ready.')
            Assert-QueryKept $where
        }

        function Open-AtlasGame {
            Select-Element $atlas
            [void](Wait-Name 'GameHeading' $atlas)
            [void](Wait-Status 'Game ready.')
        }

        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-EnabledById 'LibrarySearchInput')
        Set-SearchQuery $query 'LibrarySearchInput'
        [void](Wait-Status '2 of 3 games match.' -AllowHidden)
        Assert-NavigationRows

        # Resume pushes Library -> Game -> Reader; Back walks the same entries.
        Invoke-Element (Wait-Name 'ResumeGuide' 'Resume Beacon Guide')
        [void](Wait-Name 'ReaderHeading' 'Beacon Guide')
        [void](Wait-Status 'Guide ready.')
        Go-Back
        [void](Wait-Name 'GameHeading' $beacon)
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide 'Beacon Guide')
        [void](Wait-FocusedGuide 'Beacon Guide')
        Back-ToLibrary 'Back after Resume'
        Assert-NavigationRows
        [void](Wait-FocusedGameRow $beacon)
        $report.phases += 'resume-back-keeps-query-and-row'

        # Focus returns to the opened row without selecting (opening) it.
        Open-AtlasGame
        Back-ToLibrary 'Back from a game'
        [void](Wait-FocusedGameRow $atlas)
        Start-Sleep -Milliseconds 500
        [void](Wait-HiddenById 'GameHeading')
        [void](Wait-HiddenById 'ShellStatus')
        $report.libraryFocusScreenshot = Save-WindowScreenshot 'library-focus-restored'
        $report.phases += 'back-focuses-opened-row'

        Open-AtlasGame
        Open-GuideFromGame 'Atlas Second Guide'
        [void](Wait-Name 'ReaderHeading' 'Atlas Second Guide')
        [void](Wait-Status 'Guide ready.')
        Go-Back
        [void](Wait-Name 'GameHeading' $atlas)
        [void](Wait-SelectedGuide 'Atlas Second Guide')
        [void](Wait-FocusedGuide 'Atlas Second Guide')
        $report.phases += 'reader-back-keeps-guide'

        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        Go-Back
        [void](Wait-Name 'GameHeading' $atlas)
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide 'Atlas Second Guide')
        $report.phases += 'settings-back-keeps-guide'

        # Library -> another game -> Back, Back returns to Atlas's guide.
        Press-Enter (Wait-VisibleById 'LibraryNavigation')
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        Assert-QueryKept 'The Library navigation item'
        Select-Element $beacon
        [void](Wait-Name 'GameHeading' $beacon)
        [void](Wait-Status 'Game ready.')
        Back-ToLibrary 'Back from another game'
        [void](Wait-FocusedGameRow $beacon)
        Go-Back
        [void](Wait-Name 'GameHeading' $atlas)
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide 'Atlas Second Guide')
        $report.phases += 'other-game-back-keeps-guide'

        # Opening Second recorded an open, so the rows are Second, Third,
        # First. Removing the top row selects the row after it, twice.
        Invoke-Element (Wait-Name 'RemoveSelectedGuide' 'Remove Atlas Second Guide')
        [void](Wait-VisibleById 'RemoveGuideDialog')
        Invoke-Element (Wait-EnabledById 'PrimaryButton')
        [void](Wait-HiddenById 'RemoveGuideDialog')
        [void](Wait-Status 'Removed Atlas Second Guide.')
        [void](Wait-GuideRowCount 2)
        [void](Wait-SelectedGuide 'Atlas Third Guide')
        [void](Wait-FocusedGuide 'Atlas Third Guide')
        $report.phases += 'remove-selects-next-guide'

        Invoke-Element (Wait-Name 'RemoveSelectedGuide' 'Remove Atlas Third Guide')
        [void](Wait-VisibleById 'RemoveGuideDialog')
        Invoke-Element (Wait-EnabledById 'PrimaryButton')
        [void](Wait-HiddenById 'RemoveGuideDialog')
        [void](Wait-Status 'Removed Atlas Third Guide.')
        [void](Wait-GuideRowCount 1)
        [void](Wait-SelectedGuide 'Atlas First Guide')
        [void](Wait-FocusedGuide 'Atlas First Guide')
        $report.phases += 'remove-again-selects-next-guide'

        # This Library entry is the one Atlas was opened from.
        Back-ToLibrary 'Back after guide removal'
        [void](Wait-FocusedGameRow $atlas)
        $report.phases += 'remove-keeps-query'

        # A rename that leaves the query hides the row; focus takes the first row.
        Select-Element $beacon
        [void](Wait-Name 'GameHeading' $beacon)
        [void](Wait-Status 'Game ready.')
        Invoke-Element (Wait-EnabledById 'EditGameButton')
        Set-Text 'GameTitleInput' $renamedBeacon
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        [void](Wait-Name 'GameHeading' $renamedBeacon)
        [void](Wait-Status 'Game ready.')
        Back-ToLibrary 'Back after rename'
        [void](Wait-GameRow $atlas)
        if ((Count-GameRows $renamedBeacon) -ne 0) {
            throw "The '$query' query showed the renamed game."
        }
        [void](Wait-FocusedGameRow $atlas)
        $report.phases += 'rename-keeps-query-and-falls-back'

        Assert-NoRemoteConnections 'stable navigation'
        $report.phases += 'navigation-no-provider-traffic'
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
        [void](Wait-FocusedGameRow $shortTitle)
        [System.Windows.Forms.SendKeys]::SendWait('{END}')
        [void](Wait-Name 'GameHeading' $lastTitle)
        [void](Wait-Status 'Game ready.')
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        [void](Wait-FocusedGameRow $lastTitle)
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
            if ($Mode -eq 'import-publish') {
                Set-SearchQuery 'Import Test' 'LibrarySearchInput'
                [void](Wait-Status '1 of 1 games match.' -AllowHidden)
                [void](Wait-GameRow 'Import Test Game')
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
                Go-Back
                [void](Wait-Name 'LibraryHeading' 'Library')
                [void](Wait-Status 'Library ready.')
                if ((Get-SearchText 'LibrarySearchInput') -ne 'Import Test') {
                    throw 'Back after an import dropped the Library search query.'
                }
                [void](Wait-FocusedGameRow 'Import Test Game')
                $report.phases += 'query-kept-after-import'
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
                    [void](Wait-Status 'Guide ready.')
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
        [void](Wait-Status 'Guide ready.')
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
    elseif ($Mode -like 'game-actions*') {
        $renameTitle = 'Linked Rename Game'
        $renamed = 'Renamed Linked Game'
        $emptyTitle = 'Empty Linked Game'
        $guidedTitle = 'Guided Remove Game'
        $summary = 'A seeded summary for the game actions check.'

        # The title-bar Back button stays visible on the Library, disabled.
        function Test-BackEnabled {
            $back = Find-ById 'PART_BackButton'
            return [bool]($back -and -not $back.Current.IsOffscreen -and $back.Current.IsEnabled)
        }

        # Finds the dialog title whether UIA names the dialog or its title text.
        function Wait-VisibleName([string] $name) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $element = Find-ByName $name
                if ($element -and -not $element.Current.IsOffscreen) {
                    return $element
                }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            throw "Expected a visible element named '$name'."
        }

        # The shared prelude already consumed 'Library ready.'.
        [void](Wait-Name 'LibraryHeading' 'Library')
        if ($Mode -eq 'game-actions') {
            # The guided game goes first, while it is still the Resume guide's game (ruling 6).
            [void](Wait-Name 'ResumeGuide' 'Resume Guided Walkthrough')
            [void](Wait-GameRow $guidedTitle)
            Select-Element $guidedTitle
            [void](Wait-Name 'GameHeading' $guidedTitle)
            [void](Wait-Status 'Game ready.')
            [void](Wait-GuideRowCount 2)
            Invoke-Element (Wait-EnabledById 'RemoveGameButton')
            [void](Wait-VisibleById 'RemoveGameDialog')
            [void](Wait-VisibleName "Remove $guidedTitle and its 2 guides?")
            [void](Wait-Name 'RemoveGameMessage' ('This removes the game, its 2 guides with their reading progress, ' +
                "and their 3 managed files from Desktop Guides. The original files you imported aren't affected."))
            Assert-Absent 'RemoveGameCountChanged'
            Wait-FocusedId 'CloseButton'
            $report.guidedDialogScreenshot = Save-WindowScreenshot 'remove-game-with-guides-confirm'
            [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
            [void](Wait-HiddenById 'RemoveGameDialog')
            Wait-FocusedId 'RemoveGameButton'
            [void](Wait-Name 'GameHeading' $guidedTitle)
            [void](Wait-GuideRowCount 2)
            $report.phases += 'remove-with-guides-cancel'

            Invoke-Element (Wait-EnabledById 'RemoveGameButton')
            [void](Wait-VisibleById 'RemoveGameDialog')
            Invoke-Element (Wait-EnabledById 'PrimaryButton')
            [void](Wait-HiddenById 'RemoveGameDialog')
            [void](Wait-Name 'LibraryHeading' 'Library')
            [void](Wait-Status "Removed $guidedTitle.")
            Wait-FocusedId 'AddGameButton'
            Assert-Absent 'ResumeGuide'
            [void](Wait-GameRow $renameTitle)
            if ((Count-GameRows $guidedTitle) -ne 0) {
                throw "The removed game '$guidedTitle' is still listed."
            }
            $report.phases += 'remove-with-guides'

            $before = (Wait-GameRow $renameTitle).Current.HelpText
            Select-Element $renameTitle
            [void](Wait-Name 'GameHeading' $renameTitle)
            [void](Wait-Status 'Game ready.')
            $remove = Wait-EnabledById 'RemoveGameButton'
            if ($remove.Current.HelpText) {
                throw "Remove game's HelpText was '$($remove.Current.HelpText)' for a game with guides."
            }
            Assert-Absent 'RemoveGameHint'
            $report.phases += 'remove-enabled-with-guides'

            Assert-GuideListUsable
            $report.phases += 'guide-list-keeps-a-row'

            # Selecting a guide opens it (ruling 5), so come back to the game
            # with it selected before renaming.
            Open-GuideFromGame 'Beta Route Guide'
            [void](Wait-Name 'ReaderHeading' 'Beta Route Guide')
            [void](Wait-Status 'Guide ready.')
            Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
            [void](Wait-Name 'GameHeading' $renameTitle)
            [void](Wait-Status 'Game ready.')
            [void](Wait-SelectedGuide 'Beta Route Guide')
            Wait-FocusedGuide 'Beta Route Guide'
            Invoke-Element (Wait-EnabledById 'EditGameButton')
            Set-Text 'GameTitleInput' $renamed
            Press-Enter (Wait-VisibleById 'GameTitleInput')
            [void](Wait-Name 'GameHeading' $renamed)
            [void](Wait-Status 'Game ready.')
            [void](Wait-SelectedGuide 'Beta Route Guide')
            Wait-FocusedId 'EditGameButton'
            [void](Wait-Name 'GameSummary' $summary)
            $report.phases += 'rename-keeps-selection'

            # Scroll this game's capped details to the end; the next game
            # must open with its details at the top (final review minor 1).
            Resize-ShellWindow 768 519
            $details = Get-GameDetailsScroll
            if (-not $details.Current.VerticallyScrollable) {
                throw "The details card was not capped in a 768 x 519 window."
            }
            $details.SetScrollPercent(
                [System.Windows.Automation.ScrollPattern]::NoScroll, 100)

            Go-Back
            [void](Wait-Name 'LibraryHeading' 'Library')
            [void](Wait-Status 'Library ready.')
            $after = (Wait-GameRow $renamed).Current.HelpText
            if ($after -ne $before) {
                throw "The renamed row's facts were '$after'; before the rename they were '$before'."
            }
            if ((Count-GameRows $renameTitle) -ne 0) {
                throw "A row still had the old title '$renameTitle'."
            }
            $report.phases += 'rename-library-row'

            # A query that only the empty game matches; the Library must
            # reapply it after the removal (Review Focus 4).
            Set-SearchQuery 'Empty' 'LibrarySearchInput'
            [void](Wait-Status '1 of 2 games match.' -AllowHidden)
            [void](Wait-GameRow $emptyTitle)
            if ((Count-GameRows $renamed) -ne 0) {
                throw "The query 'Empty' still listed '$renamed'."
            }
            Select-Element $emptyTitle
            [void](Wait-Name 'GameHeading' $emptyTitle)
            [void](Wait-Status 'Game ready.')
            $details = Get-GameDetailsScroll
            if ($details.Current.VerticallyScrollable -and
                $details.Current.VerticalScrollPercent -gt 0) {
                throw "'$emptyTitle' opened with its details scrolled to $($details.Current.VerticalScrollPercent)%."
            }
            $report.phases += 'next-game-details-at-top'
            $remove = Wait-EnabledById 'RemoveGameButton'
            if ($remove.Current.HelpText) {
                throw "Remove game's HelpText was '$($remove.Current.HelpText)' for a game without guides."
            }
            if (-not (Test-BackEnabled)) {
                throw 'Back was unavailable on the Game page before the removal.'
            }
            Invoke-Element $remove
            [void](Wait-VisibleById 'RemoveGameDialog')
            [void](Wait-VisibleName "Remove ${emptyTitle}?")
            [void](Wait-Name 'RemoveGameMessage' 'This removes the game and its details from Desktop Guides.')
            [void](Wait-Name 'PrimaryButton' 'Remove')
            [void](Wait-Name 'CloseButton' 'Cancel')
            $report.dialogScreenshot = Save-WindowScreenshot 'remove-game-confirm'
            $report.phases += 'remove-confirm'

            [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
            [void](Wait-HiddenById 'RemoveGameDialog')
            Wait-FocusedId 'RemoveGameButton'
            [void](Wait-Name 'GameHeading' $emptyTitle)
            $report.phases += 'remove-escape-cancels'

            # Cancel is the default button (ruling 11), so Enter cancels too.
            Invoke-Element (Wait-EnabledById 'RemoveGameButton')
            [void](Wait-VisibleById 'RemoveGameDialog')
            Wait-FocusedId 'CloseButton'
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            [void](Wait-HiddenById 'RemoveGameDialog')
            Wait-FocusedId 'RemoveGameButton'
            [void](Wait-Name 'GameHeading' $emptyTitle)
            $report.phases += 'remove-enter-cancels'

            Invoke-Element (Wait-EnabledById 'RemoveGameButton')
            [void](Wait-VisibleById 'RemoveGameDialog')
            Invoke-Element (Wait-EnabledById 'PrimaryButton')
            [void](Wait-HiddenById 'RemoveGameDialog')
            [void](Wait-Name 'LibraryHeading' 'Library')
            [void](Wait-Status "Removed $emptyTitle.")
            if ((Get-SearchText 'LibrarySearchInput') -ne 'Empty') {
                throw "The Library query was '$(Get-SearchText 'LibrarySearchInput')' after the removal."
            }
            [void](Wait-Name 'LibraryNoResults' 'No games or guides match "Empty".')
            [void](Wait-HiddenById 'GameList')
            Wait-FocusedId 'AddGameButton'
            if (Test-BackEnabled) {
                throw 'Back was available after the removal.'
            }
            Set-SearchQuery '' 'LibrarySearchInput'
            [void](Wait-GameRow $renamed)
            if ((Count-GameRows $emptyTitle) -ne 0) {
                throw "The removed game '$emptyTitle' is still listed."
            }
            $report.phases += 'removed'
        }
        else {
            [void](Wait-GameRow $renamed)
            if ((Count-GameRows $guidedTitle) -ne 0) {
                throw "The removed game '$guidedTitle' came back."
            }
            Invoke-Element (Wait-Name 'ResumeGuide' 'Resume Beta Route Guide')
            [void](Wait-Name 'ReaderHeading' 'Beta Route Guide')
            [void](Wait-Name 'ReaderGameName' $renamed)
            [void](Wait-Status 'Guide ready.')
            $report.phases += 'persisted-resume'

            Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
            [void](Wait-Name 'GameHeading' $renamed)
            [void](Wait-Status 'Game ready.')
            [void](Wait-SelectedGuide 'Beta Route Guide')
            $report.phases += 'persisted-selection'

            $alpha = (Wait-GuideRow 'Alpha Route Guide').Current.HelpText
            if ($alpha -notlike '*about 45 percent*') {
                throw "Alpha Route Guide's facts were '$alpha'."
            }
            [void](Wait-Name 'GameSummary' $summary)
            [void](Wait-VisibleById 'GameCover')
            $report.phases += 'persisted-facts'

            Assert-NoRemoteConnections 'game page'
            $report.phases += 'persisted-no-provider-traffic'
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
        [void](Wait-Status 'Guide ready.')
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide $target)
        Wait-FocusedGuide $target
        [void](Wait-Name 'OpenSelectedGuide' "Open $target")
        # The open moved the tail guide to the top of the list.
        $firstRow = @(Get-ListRows 'GuideList')[0].Current.Name
        if ($firstRow -ne $target) {
            throw "The first guide row after Back was '$firstRow', not the opened guide."
        }
        $report.phases += 'opened-guide-back-focus-first-row'
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        [void](Wait-Name 'ReaderHeading' $target)
        [void](Wait-Status 'Guide ready.')
        $report.phases += 'opened-guide-enter-reopen'
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
        [void](Wait-Status 'Guide ready.')
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
        [void](Wait-VisibleById 'ReaderTextLines')
        [void](Wait-Name 'ReaderTextLines' 'Guide text')
        $firstLine = (Find-ById 'ReaderTextLines').FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem)))
        if (-not $firstLine -or $firstLine.Current.Name -ne 'Test guide.') {
            throw 'The TXT reader did not show the seeded guide text.'
        }
        Assert-Absent 'ReaderPlaceholder'
        foreach ($name in 'Go to start', 'Previous page', 'Next page', 'Go to end') {
            $button = Find-ByName $name
            if (-not $button -or $button.Current.IsOffscreen) {
                throw "The TXT reader has no visible '$name' command."
            }
        }
        [void](Wait-Status 'Guide ready.')
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
        [void](Wait-Status 'Guide ready.')
        $report.phases += 'uia-reopen-selected-guide'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide $ExpectedResumeGuide)
        Wait-FocusedGuide $ExpectedResumeGuide

        Click-Element (Wait-SelectedGuide $ExpectedResumeGuide)
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Status 'Guide ready.')
        $report.phases += 'pointer-reopen-selected-guide'
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide $ExpectedResumeGuide)
        Wait-FocusedGuide $ExpectedResumeGuide
        Activate-SelectedGuide $ExpectedResumeGuide
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Status 'Guide ready.')
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
        [void](Wait-Status 'Guide ready.')
        $report.phases += 'focused-guide-enter'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        Open-GuideFromGame $ExpectedResumeGuide
        [void](Wait-Name 'ReaderHeading' $ExpectedResumeGuide)
        [void](Wait-Status 'Guide ready.')
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
        [void](Wait-Status 'Guide ready.')
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
        [void](Wait-Status 'Guide ready.')
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
        [void](Wait-Status 'Guide ready.')
        $report.phases += 'rapid-guide-settings-back-reader'
        Go-Back
        [void](Wait-Name 'GameHeading' 'Route Test Game')
        [void](Wait-Status 'Game ready.')
        $report.phases += 'library-game-reader-game'
        if ($rapidGuide -ne 'Route Test Guide') {
            Open-GuideFromGame 'Route Test Guide'
            [void](Wait-Name 'ReaderHeading' 'Route Test Guide')
            [void](Wait-Status 'Guide ready.')
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
        $Mode -like 'provider-*' -or $Mode -like 'game-actions*') -and $root) {
        try {
            foreach ($id in @('ShellStatus', 'GameHeading',
                'GameTitleFeedback', 'GameSaveError', 'GameSearchStatus',
                'ProviderSettingsStatus', 'GameEditorNotice', 'GameAttribution',
                'ImportStatus', 'ImportGuideDialog', 'ImportBusyText',
                'RemoveGuideDialog', 'RemoveGuideMessage', 'RemoveSelectedGuide',
                'RemoveGameButton', 'RemoveGameCountChanged', 'RemoveGameDialog', 'RemoveGameMessage')) {
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
