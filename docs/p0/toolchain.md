# P0 Windows toolchain, signing and prerequisites

The macOS repository is the source checkout. Windows commands run on the SSH host `pcsx2-win` from `E:\work\desktop-guides`. The test host is Windows 11 Enterprise x64 build `10.0.26200.0`, with VS 18 Community MSBuild, .NET SDK `10.0.401`, Windows App Runtime `2.5.1` x64, and Evergreen WebView2 Runtime `153.0.4234.48`. It has about 63 GiB RAM at 96 DPI. The [results](results.md) distinguish this interactive host from the Windows Server 2025 x64 CI image.

| Input | Pinned value |
| --- | --- |
| .NET SDK | `10.0.401` in [`global.json`](../../global.json) |
| Target | `net10.0-windows10.0.19041.0`; minimum Windows build 19041 |
| Windows App SDK | `2.5.1` |
| WebView2 SDK | `1.0.4191.47` |
| Test SDK / xUnit / runner | `18.10.1` / `2.9.3` / `3.1.5` |
| Restore | Per-project `packages.lock.json`; `--locked-mode` |
| Built packages | x64 and ARM64; both installed and launched on native target runners |

## Regenerate, test and build

```powershell
Set-Location E:\work\desktop-guides
python tools\p0\make_fixtures.py
python tools\p0\verify_fixtures.py
dotnet restore DesktopGuides.sln --locked-mode -p:Platform=x64
dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj `
  -c Release --no-restore
dotnet build src\DesktopGuides.App\DesktopGuides.App.csproj `
  -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true `
  -p:AppxPackageSigningEnabled=false
dotnet restore src\DesktopGuides.App\DesktopGuides.App.csproj `
  --locked-mode -p:Platform=ARM64
dotnet build src\DesktopGuides.App\DesktopGuides.App.csproj `
  -c Release -p:Platform=ARM64 -p:GenerateAppxPackageOnBuild=true `
  -p:AppxPackageSigningEnabled=false
```

The unsigned packages appear under `src\DesktopGuides.App\AppPackages\DesktopGuides.App_0.1.0.0_<architecture>_Test\`. Those folders include architecture-specific `Dependencies` with the Windows App Runtime framework package. The local SDK reported a missing optional `mspdbcmf.exe` symbols tool; the packages built successfully. [Windows CI](../../.github/workflows/windows-ci.yml) repeats fixture verification, locked restore, 23 Core tests and both architecture builds. It also accepts a manually dispatched `verify-test-gate` input that inserts a failing Core test to verify packaging is skipped. The passing [x64](evidence/ci/x64-final-manifest.json) and [ARM64](evidence/ci/arm64-final-manifest.json) build manifests identify runner versions and package hashes.

For unpackaged UI iteration:

```powershell
dotnet publish src\DesktopGuides.App\DesktopGuides.App.csproj `
  -c Release -p:Platform=x64 -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true -p:SelfContained=true `
  -o E:\work\desktop-guides\artifacts\unpackaged
```

Launch the executable in the active Windows desktop session. [`windows_ui_suite.ps1`](../../tools/p0/windows_ui_suite.ps1) runs all 14 guide fixtures through UI Automation. It records Open, capture, resize, scale and restore outcomes; the long PDF also exercises rapid and repeated page turns. The UI script must run in that interactive session.

## Development-signed install

Run [`windows_signed_install.ps1`](../../tools/p0/windows_signed_install.ps1) in an **elevated interactive PowerShell session**. It creates a one-day nonexportable development certificate matching the manifest publisher, temporarily imports only its public certificate into `LocalMachine\TrustedPeople`, signs a copy of the MSIX, installs and launches it, runs the full UI suite, then removes the app and certificate in `finally`. The test certificate and private key are never checked in. An SSH-initiated install on this host passed signature and dependency checks but failed Windows app lifecycle initialization with `0x80070005`; the interactive-session install succeeded.

```powershell
$package = 'E:\work\desktop-guides\src\DesktopGuides.App\AppPackages\DesktopGuides.App_0.1.0.0_x64_Test\DesktopGuides.App_0.1.0.0_x64.msix'
.\tools\p0\windows_signed_install.ps1 `
  -PackagePath $package `
  -ResultDirectory 'E:\work\desktop-guides\artifacts\signed-install'
```

For a physical offline relaunch on a dedicated test host, pass `-OfflineNetworkAdapter` with the active adapter name. The script registers a five-minute network-restore watchdog before disabling the adapter, then reenables the adapter in `finally`. This run was authorized for `Wi-Fi 2` on the local host and passed [14/14 offline fixtures](evidence/offline/signed-install.json). Do not use the option on a remote session without the scheduled watchdog and an operator aware that SSH will disconnect.

## Prerequisites on a new machine

The Windows App SDK package is **framework dependent**, though the .NET app is self-contained. A matching x64 Windows App Runtime framework must be installed before the app package can resolve its dependency. For an offline installer test, transfer the generated `Dependencies\x64\Microsoft.WindowsAppRuntime.2.msix` from the x64 AppPackages output and install that Microsoft-signed dependency before the development-signed app MSIX. For a connected install, use Microsoft's [packaged-app deployment guidance](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps) and official Windows App SDK redistributable. Keep development signing and trust limited to a disposable test machine.

HTML additionally requires the Evergreen Microsoft Edge WebView2 Runtime. A connected installer can use the Evergreen Bootstrapper; an offline installer can transfer the Evergreen Standalone Installer from Microsoft's [WebView2 distribution page](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution). The app checks availability at startup and surfaces an installer/repair message if WebView2 initialization fails, while TXT and PDF remain usable. The [simulated missing-runtime trace](evidence/missing-webview.json) sets `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` to a nonexistent folder; it does **not** substitute for a truly runtime-free machine.

The host already had Windows App Runtime and WebView2 installed. The user deferred the clean Windows 11 x64 VM check of the precise missing-framework install error, missing-WebView2 startup behavior, and recovery after offline installers. Windows 10 x64 remains deferred and untested. Native ARM64 CI installed and launched the package, then passed 23 Core tests and [14/14 reader fixtures](evidence/arm64-ci/ui-suite/suite.json). The ARM64 UI test used ValuePattern for the fixture password; it did not test physical keyboard entry or offline operation on ARM64.
