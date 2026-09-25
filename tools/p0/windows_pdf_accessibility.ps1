param(
    [Parameter(Mandatory = $true)]
    [string] $ResultPath,

    [switch] $LaunchNarrator
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process DesktopGuides.App -ErrorAction Stop |
    Where-Object { $_.MainWindowHandle -ne 0 } |
    Select-Object -First 1
if (-not $process) { throw 'Open pdf-access in the interactive Desktop Guides window first.' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$scope = [System.Windows.Automation.TreeScope]::Descendants

function Find-Name([string] $name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $root.FindFirst($scope, $condition)
}

$previous = Find-Name 'Previous page'
$next = Find-Name 'Next page'
$image = Find-Name 'Rendered image of PDF page 1 of 1'
if (-not $previous -or -not $next -or -not $image) {
    throw 'The PDF page and controls must be visible before the accessibility check.'
}
$textPattern = $null
$hasTextPattern = $image.TryGetCurrentPattern(
    [System.Windows.Automation.TextPattern]::Pattern, [ref] $textPattern)
$all = $root.FindAll($scope, [System.Windows.Automation.Condition]::TrueCondition)
$documentTextInTree = @($all | Where-Object {
    $_.Current.Name -like '*Tagged guide paragraph for Narrator*'
}).Count -gt 0

$previous.SetFocus()
Start-Sleep -Milliseconds 150
$focusedPrevious = [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name
[System.Windows.Forms.SendKeys]::SendWait('{TAB}')
Start-Sleep -Milliseconds 150
$focusedAfterTab = [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name

$alreadyRunning = @(Get-Process Narrator -ErrorAction SilentlyContinue)
$startedNarrator = $false
try {
    if ($LaunchNarrator -and $alreadyRunning.Count -eq 0) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Run elevated to launch Narrator so cleanup can stop it afterward.'
        }
        Start-Process "$env:WINDIR\System32\Narrator.exe"
        $startedNarrator = $true
        Start-Sleep -Seconds 3
    }
    $narrator = @(Get-Process Narrator -ErrorAction SilentlyContinue)
    $previous.SetFocus()
    Start-Sleep -Milliseconds 250
    [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
    Start-Sleep -Milliseconds 250
    $focusedWithNarrator = [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name
    $result = [ordered]@{
        observedAt = (Get-Date).ToUniversalTime().ToString('o')
        osBuild = [Environment]::OSVersion.Version.ToString()
        appProcessId = $process.Id
        narratorRunning = ($narrator.Count -gt 0)
        narratorStartedForCheck = $startedNarrator
        previousKeyboardFocusable = $previous.Current.IsKeyboardFocusable
        nextKeyboardFocusable = $next.Current.IsKeyboardFocusable
        focusedPrevious = $focusedPrevious
        focusedAfterTab = $focusedAfterTab
        focusedWithNarrator = $focusedWithNarrator
        pageImageName = $image.Current.Name
        pageImageKeyboardFocusable = $image.Current.IsKeyboardFocusable
        pageImageHasTextPattern = $hasTextPattern
        taggedParagraphInAutomationTree = $documentTextInTree
        narratorSpeechCaptured = $false
    }
    New-Item -ItemType Directory -Force (Split-Path $ResultPath) | Out-Null
    $result | ConvertTo-Json | Set-Content $ResultPath -Encoding UTF8
}
finally {
    if ($startedNarrator) {
        Get-Process Narrator -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction Stop
    }
}
