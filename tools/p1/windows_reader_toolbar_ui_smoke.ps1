param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{32}$')]
    [string] $InvocationId,

    [Parameter(Mandatory = $true)]
    [string] $ProcessHandoffPath,

    [Parameter(Mandatory = $true)]
    [string] $InstallReceiptPath,

    [string] $PauseAfterInstallPath,

    [ValidateRange(0, 60)]
    [int] $SimulateSlowInstallSeconds = 0
)

$ErrorActionPreference = 'Stop'
$report = [ordered]@{
    invocationId = $InvocationId
    observedAt = (Get-Date).ToUniversalTime().ToString('o')
    sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    success = $false
    phases = @()
}
$installed = $null
$process = $null
$root = $null

try {
    if ($report.sessionId -eq 0 -or
        -not @(Get-Process explorer -ErrorAction SilentlyContinue |
            Where-Object { $_.SessionId -eq $report.sessionId })) {
        throw 'Toolbar smoke requires an interactive desktop session.'
    }
    if (Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke) {
        throw 'The toolbar test package is already installed.'
    }
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -AssemblyName System.Windows.Forms

    if ($SimulateSlowInstallSeconds -gt 0) {
        Start-Sleep -Seconds $SimulateSlowInstallSeconds
        if (Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke) {
            throw 'The toolbar test package appeared before installation.'
        }
    }
    Add-AppxPackage -Path $PackagePath
    $installedPackages =
        @(Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke)
    if ($installedPackages.Count -ne 1) {
        throw 'Expected one installed toolbar test package.'
    }
    $installed = $installedPackages[0]
    $report.packageFullName = $installed.PackageFullName
    [ordered]@{
        invocationId = $InvocationId
        packageFullName = $installed.PackageFullName
    } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath "$InstallReceiptPath.tmp" -Encoding UTF8
    Move-Item -LiteralPath "$InstallReceiptPath.tmp" `
        -Destination $InstallReceiptPath -Force
    $report.installReceiptWritten = $true
    $executable = Join-Path $installed.InstallLocation `
        'DesktopGuides.ReaderToolbarSmoke.exe'
    $process = Start-Process -FilePath $executable -PassThru
    $report.processId = $process.Id
    $report.startedAt = $process.StartTime.ToUniversalTime().ToString('o')
    $report.executablePath = $executable
    $handoffToken = [Guid]::NewGuid().ToString('N')
    [ordered]@{
        invocationId = $InvocationId
        handoffToken = $handoffToken
        packageFullName = $installed.PackageFullName
        processId = $report.processId
        startedAt = $report.startedAt
        sessionId = $report.sessionId
        executablePath = $executable
    } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath "$ProcessHandoffPath.tmp" -Encoding UTF8
    Move-Item -LiteralPath "$ProcessHandoffPath.tmp" `
        -Destination $ProcessHandoffPath -Force
    $handoffAckPath = "$ProcessHandoffPath.ack"
    $handoffDeadline = (Get-Date).AddSeconds(45)
    do {
        if (Test-Path -LiteralPath $handoffAckPath) { break }
        $process.Refresh()
        if ($process.HasExited) {
            throw 'Toolbar app exited before its process handoff was acknowledged.'
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $handoffDeadline)
    if (-not (Test-Path -LiteralPath $handoffAckPath)) {
        throw 'Toolbar process handoff was not acknowledged.'
    }
    $ack = Get-Content -LiteralPath $handoffAckPath -Raw |
        ConvertFrom-Json
    if ($ack.handoffToken -ne $handoffToken) {
        throw 'Toolbar process handoff acknowledgment did not match.'
    }
    $report.handoffAcknowledged = $true
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $process.Refresh()
        if ($process.HasExited) {
            throw "Toolbar test app exited with code $($process.ExitCode)."
        }
        if ($process.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    if ($process.MainWindowHandle -eq 0) {
        throw 'Toolbar test app did not open a window.'
    }
    if ($PauseAfterInstallPath) {
        Set-Content -LiteralPath $PauseAfterInstallPath `
            -Value $installed.PackageFullName -Encoding ASCII
        Start-Sleep -Seconds 60
    }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle(
        $process.MainWindowHandle)
    $scope = [System.Windows.Automation.TreeScope]::Descendants

    function Find-ById([string] $id) {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        return $root.FindFirst($scope, $condition)
    }

    function Find-VisibleByName([string] $name) {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        foreach ($element in $root.FindAll($scope, $condition)) {
            if (-not $element.Current.IsOffscreen) { return $element }
        }
        return $null
    }

    function Wait-VisibleByName([string] $name) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = Find-VisibleByName $name
            if ($element) { return $element }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected visible command '$name'."
    }

    function Wait-HiddenByName([string] $name) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            if (-not (Find-VisibleByName $name)) { return }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Command '$name' remained visible."
    }

    function Wait-ToolbarVisibility([bool] $expected) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $bar = Find-ById 'ReaderCommands'
            $visible = [bool]($bar -and -not $bar.Current.IsOffscreen)
            if ($visible -eq $expected) { return }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected toolbar visibility '$expected'."
    }

    function Wait-Action([string] $expected) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = Find-ById 'LastReaderAction'
            if ($element -and $element.Current.Name -eq $expected) { return }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        $actual = if ($element) { $element.Current.Name } else { 'missing' }
        throw "Expected action '$expected', got '$actual'."
    }

    function Invoke-Element($element) {
        if (-not $element -or $element.Current.IsOffscreen) {
            throw 'Expected a visible command to invoke.'
        }
        $pattern = $element.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
    }

    function Invoke-Id([string] $id) {
        Invoke-Element (Find-ById $id)
    }

    function Invoke-Command([string] $name, [string] $expectedAction) {
        Invoke-Element (Wait-VisibleByName $name)
        Wait-Action $expectedAction
    }

    function Find-More {
        foreach ($name in @('More', 'More options', 'More commands',
                'Show more', 'See more')) {
            $element = Find-VisibleByName $name
            if ($element) { return $element }
        }
        return $null
    }

    function Open-Overflow {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = Find-More
            if ($element) {
                Invoke-Element $element
                return
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw 'The CommandBar overflow button was not visible.'
    }

    function Close-Overflow {
        Invoke-Element (Find-ById 'MoreButton')
        [void](Wait-HiddenByName 'Go to page')
    }

    function Wait-FocusedCommand([string] $name) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = Find-VisibleByName $name
            if ($element -and $element.Current.HasKeyboardFocus) {
                return $element
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Keyboard focus did not return to '$name' after its dialog."
    }

    function Enter-DialogText([string] $value, [string] $submit) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $input = Find-ById 'ReaderCommandInput'
            if ($input -and -not $input.Current.IsOffscreen) { break }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        if (-not $input -or $input.Current.IsOffscreen) {
            throw 'Reader command dialog did not open.'
        }
        $pattern = $input.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)
        $pattern.SetValue($value)
        Invoke-Element (Wait-VisibleByName $submit)
    }

    function Wait-FocusedId([string] $id) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = Find-ById $id
            if ($element -and $element.Current.HasKeyboardFocus) { return $element }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        $actual = if ($focused) { "$($focused.Current.AutomationId) '$($focused.Current.Name)'" } else { 'nothing' }
        throw "Expected keyboard focus on '$id', got $actual."
    }

    # P15: the key is on the command itself, where Narrator reads it.
    function Assert-AcceleratorKey([string] $name, [string] $key) {
        $element = Wait-VisibleByName $name
        $actual = $element.Current.AcceleratorKey
        if ($actual -ne $key) { throw "Expected '$name' to name the key '$key', got '$actual'." }
    }

    # P2: a bad entry keeps the dialog open and says why.
    function Assert-DialogRefuses([string] $value) {
        Enter-DialogText $value 'Go'
        $expected = 'Enter a page from 1 to 5.'
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $message = Find-ById 'ReaderCommandError'
            if ($message -and -not $message.Current.IsOffscreen -and
                    $message.Current.Name -eq $expected) { break }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        $box = Find-ById 'ReaderCommandInput'
        if (-not $box -or $box.Current.IsOffscreen) {
            throw "The Go to page dialog closed after '$value'."
        }
        $actual = if ($message) { $message.Current.Name } else { 'missing' }
        if ($actual -ne $expected) { throw "Expected '$expected' after '$value', got '$actual'." }
    }

    function Send-ToolbarKeys([string] $keys, [string] $focusId) {
        [System.Windows.Forms.SendKeys]::SendWait($keys)
        Start-Sleep -Milliseconds 150
        [void](Wait-FocusedId $focusId)
    }

    # Gives a key time to land, then checks it ran nothing.
    function Assert-ActionStays([string] $expected, [string] $context) {
        Start-Sleep -Milliseconds 700
        $actual = (Find-ById 'LastReaderAction').Current.Name
        if ($actual -ne $expected) { throw "$context ran '$actual'." }
    }

    Wait-ToolbarVisibility $false
    [void](Wait-HiddenByName 'Next page')
    [void](Wait-HiddenByName 'Larger text')
    $report.phases += 'initial-no-session-capabilities'

    Invoke-Id 'TextControls'
    Wait-ToolbarVisibility $true
    [void](Wait-VisibleByName 'Larger text')
    [void](Wait-HiddenByName 'Next page')
    [void](Wait-HiddenByName 'Zoom in')
    Invoke-Command 'Larger text' 'Text size 1.1'
    $report.phases += 'worker-capability-change-text-dispatch'

    Invoke-Id 'AllControls'
    [void](Wait-VisibleByName 'Next page')
    [void](Wait-VisibleByName 'Zoom in')
    Invoke-Command 'Next page' 'Page turn 1'
    Invoke-Command 'Go to start' 'Page edge Start'
    Invoke-Command 'Go to end' 'Page edge End'
    Invoke-Command 'Zoom in' 'Zoom 1.1'
    Open-Overflow
    Invoke-Element (Wait-VisibleByName 'Go to page')
    foreach ($refused in @('0', '6', '')) { Assert-DialogRefuses $refused }
    Enter-DialogText '3' 'Go'
    Wait-Action 'Page jump 3'
    [void](Wait-FocusedCommand 'Go to page')
    $report.phases += 'page-dialog-refuses-out-of-range'
    $report.phases += 'page-dialog-restores-overflow-focus'
    Close-Overflow
    Open-Overflow
    Invoke-Command 'Fit to width' 'Fit to width'
    Open-Overflow
    Invoke-Element (Wait-VisibleByName 'Find in guide')
    Enter-DialogText 'boss' 'Find'
    Wait-Action 'Find boss'
    [void](Wait-FocusedCommand 'Find in guide')
    $report.phases += 'find-dialog-restores-overflow-focus'
    Close-Overflow
    $report.phases += 'all-capabilities-dispatch'

    foreach ($pair in @(@('Previous page', 'Page Up'), @('Next page', 'Page Down'),
            @('Go to start', 'Ctrl+Home'), @('Go to end', 'Ctrl+End'),
            @('Zoom in', 'Ctrl+Plus'), @('Zoom out', 'Ctrl+Minus'))) {
        Assert-AcceleratorKey $pair[0] $pair[1]
    }
    Open-Overflow
    Assert-AcceleratorKey 'Go to page' 'Ctrl+G'
    Assert-AcceleratorKey 'Fit to width' 'Ctrl+0'
    Close-Overflow
    $report.phases += 'shortcut-names'

    # Keys stay off until the shell turns them on (TXT until T16.1).
    $stand = 'ContentStandIn'
    (Find-ById $stand).SetFocus()
    [void](Wait-FocusedId $stand)
    Send-ToolbarKeys '{PGDN}' $stand
    Assert-ActionStays 'Find boss' 'Page Down with keys off'
    Invoke-Id 'KeysOn'
    (Find-ById $stand).SetFocus()
    foreach ($pair in @(@('{PGDN}', 'Page turn 1'), @('{PGUP}', 'Page turn -1'),
            @('^{HOME}', 'Page edge Start'), @('^{END}', 'Page edge End'),
            @('^=', 'Zoom 1.1'), @('^-', 'Zoom 0.9'), @('^{ADD}', 'Zoom 1.1'),
            @('^{SUBTRACT}', 'Zoom 0.9'), @('^0', 'Fit to width'))) {
        Send-ToolbarKeys $pair[0] $stand
        Wait-Action $pair[1]
    }
    $report.phases += 'keys-run-commands-without-moving-focus'

    # Ctrl+G: Esc, or a jump, hands focus to the content. Each starts in
    # the notes box, so the dialog's own focus restore lands there and
    # only ContentFocusRequested can move focus to the stand-in.
    (Find-ById 'HostNotes').SetFocus()
    [void](Wait-FocusedId 'HostNotes')
    [System.Windows.Forms.SendKeys]::SendWait('^g')
    [void](Wait-FocusedId 'ReaderCommandInput')
    Send-ToolbarKeys '{PGDN}' 'ReaderCommandInput'
    Assert-ActionStays 'Fit to width' 'Page Down in the Go to page box'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    [void](Wait-FocusedId $stand)
    (Find-ById 'HostNotes').SetFocus()
    [void](Wait-FocusedId 'HostNotes')
    [System.Windows.Forms.SendKeys]::SendWait('^g')
    [void](Wait-FocusedId 'ReaderCommandInput')
    Enter-DialogText '4' 'Go'
    Wait-Action 'Page jump 4'
    [void](Wait-FocusedId $stand)
    $report.phases += 'keyboard-dialog-focuses-content'

    # A text box keeps its page keys; zoom keys still run.
    (Find-ById 'HostNotes').SetFocus()
    [void](Wait-FocusedId 'HostNotes')
    Send-ToolbarKeys '{PGDN}' 'HostNotes'
    Send-ToolbarKeys '^{END}' 'HostNotes'
    Assert-ActionStays 'Page jump 4' 'Page keys in a text box'
    Send-ToolbarKeys '^=' 'HostNotes'
    Wait-Action 'Zoom 1.1'
    $report.phases += 'text-box-keeps-page-keys'

    # At the last step Zoom in is disabled, and its key does nothing.
    # Focus on Zoom in moves to Zoom out instead of leaving the toolbar.
    (Wait-VisibleByName 'Zoom in').SetFocus()
    Invoke-Id 'ZoomAtEnd'
    if ((Wait-VisibleByName 'Zoom in').Current.IsEnabled) { throw 'Zoom in stayed enabled at its end.' }
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    if (-not $focused -or $focused.Current.Name -ne 'Zoom out') {
        $actual = if ($focused) { "'$($focused.Current.Name)'" } else { 'nothing' }
        throw "Disabling Zoom in moved focus to $actual, not 'Zoom out'."
    }
    (Find-ById $stand).SetFocus()
    Send-ToolbarKeys '^0' $stand
    Wait-Action 'Fit to width'
    Send-ToolbarKeys '^=' $stand
    Assert-ActionStays 'Fit to width' 'Ctrl+= with Zoom in disabled'
    Send-ToolbarKeys '^-' $stand
    Wait-Action 'Zoom 0.9'
    Invoke-Id 'ZoomBothWays'
    $report.phases += 'zoom-end-disables-command-and-key'

    Invoke-Id 'NarrowToolbar'
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $more = Find-More
        $zoom = Find-VisibleByName 'Zoom in'
        if ($more -and -not $zoom) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    if (-not $more -or $zoom) {
        throw 'Narrow CommandBar did not move Zoom in to overflow.'
    }
    Open-Overflow
    Invoke-Command 'Zoom in' 'Zoom 1.1'
    $report.phases += 'narrow-primary-command-overflow'

    Invoke-Id 'NoControls'
    Wait-ToolbarVisibility $false
    [void](Wait-HiddenByName 'Next page')
    [void](Wait-HiddenByName 'Larger text')
    [void](Wait-HiddenByName 'Zoom in')
    $report.phases += 'worker-capability-removal'

    Invoke-Id 'DetachReader'
    Wait-ToolbarVisibility $false
    [void](Wait-HiddenByName 'Next page')
    $report.phases += 'detached-session-event-ignored'
    $report.success = $true
}
catch {
    $report.error = $_ | Out-String
    if ($root) {
        try {
            $report.uiElements = @(
                $root.FindAll(
                    [System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.Condition]::TrueCondition) |
                    Select-Object -First 100 |
                    ForEach-Object {
                        [ordered]@{
                            name = $_.Current.Name
                            automationId = $_.Current.AutomationId
                            offscreen = $_.Current.IsOffscreen
                        }
                    })
        }
        catch {
            $report.uiDumpError = $_ | Out-String
        }
    }
}
finally {
    if ($process) {
        try {
            $process.Refresh()
            if (-not $process.HasExited -and $root) {
                $window = $root.GetCurrentPattern(
                    [System.Windows.Automation.WindowPattern]::Pattern)
                $window.Close()
                [void]$process.WaitForExit(10000)
            }
            if (-not $process.HasExited) {
                $process.Kill()
                [void]$process.WaitForExit(10000)
            }
        }
        catch {
            $report.success = $false
            $report.processCleanupError = $_ | Out-String
        }
        $process.Dispose()
    }
    if ($installed) {
        try {
            Remove-AppxPackage -Package $installed.PackageFullName
        }
        catch {
            $report.success = $false
            $report.packageCleanupError = $_ | Out-String
        }
    }
    $report.packageStillInstalled =
        [bool](Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke)
    if ($report.packageStillInstalled) {
        $report.success = $false
    }
    $report | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath $ResultPath -Encoding UTF8
}
if (-not $report.success) { exit 1 }
