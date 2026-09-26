param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
$report = [ordered]@{
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

    Add-AppxPackage -Path $PackagePath
    $installed = Get-AppxPackage -Name DesktopGuides.ReaderToolbarSmoke
    if (-not $installed) {
        throw 'Toolbar test package did not install.'
    }
    $report.packageFullName = $installed.PackageFullName
    $executable = Join-Path $installed.InstallLocation `
        'DesktopGuides.ReaderToolbarSmoke.exe'
    $process = Start-Process -FilePath $executable -PassThru
    $report.processId = $process.Id
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
    Invoke-Command 'Zoom in' 'Zoom 1.1'
    Open-Overflow
    Invoke-Element (Wait-VisibleByName 'Go to page')
    Enter-DialogText '3' 'Go'
    Wait-Action 'Page jump 3'
    Open-Overflow
    Invoke-Command 'Fit to width' 'Fit to width'
    Open-Overflow
    Invoke-Element (Wait-VisibleByName 'Find in guide')
    Enter-DialogText 'boss' 'Find'
    Wait-Action 'Find boss'
    $report.phases += 'all-capabilities-dispatch'

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
