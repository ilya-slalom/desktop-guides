param(
    [Parameter(Mandatory = $true)]
    [string] $ExecutablePath
)

$ErrorActionPreference = 'Stop'
$env:WEBVIEW2_BROWSER_EXECUTABLE_FOLDER =
    Join-Path $env:TEMP 'DesktopGuides-P0-Missing-WebView2-Runtime'
if (Test-Path $env:WEBVIEW2_BROWSER_EXECUTABLE_FOLDER) {
    throw 'The simulated missing WebView2 folder unexpectedly exists.'
}
& $ExecutablePath
