# P0 Windows toolchain and local build

The macOS checkout is the source of truth. Windows verification used the SSH
host `pcsx2-win`, with source under `E:\work\desktop-guides`. The commands below
run from that Windows directory in PowerShell. Generated files under
`artifacts/`, `bin/`, `obj/`, and `AppPackages/` are not source files.

## Pinned inputs

| Item | Version or setting |
| --- | --- |
| .NET SDK | 10.0.401 in [`global.json`](../../global.json) |
| Target | `net10.0-windows10.0.19041.0`; minimum OS build 19041 |
| Windows App SDK | 2.5.1 |
| WebView2 SDK | 1.0.4191.47 |
| Test SDK / xUnit / runner | 18.10.1 / 2.9.3 / 3.1.5 |
| Restore | Per-project `packages.lock.json`; use `--locked-mode` |
| Architectures | x64 and ARM64 package builds; only x64 launched |

The test host was Windows 11 Enterprise build 26200, x64, with Visual Studio
18 Community MSBuild, AMD Ryzen 7 7800X3D, about 63 GiB physical RAM,
and 96 DPI. The x64 host reported Windows App Runtime 2.5.1 and Evergreen
WebView2 Runtime 153.0.4234.32 when the GUI probes ran. These are host
observations, not clean-machine prerequisites proved by an installation test.

## Build and test

```powershell
Set-Location E:\work\desktop-guides
python tools\p0\make_fixtures.py
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

The unsigned outputs are under
`src\DesktopGuides.App\AppPackages\DesktopGuides.App_0.1.0.0_<architecture>_Test\`.
The host reported a missing optional `mspdbcmf.exe` symbols-packaging tool;
both MSIX files were still produced. The Windows CI workflow repeats locked
restore, fixture generation, core tests, and architecture-specific packages.
CI has not yet run on a fresh GitHub runner.

For the GUI smoke run on the current host, a self-contained unpackaged copy
was built without changing the repository's packaged app configuration:

```powershell
dotnet publish src\DesktopGuides.App\DesktopGuides.App.csproj `
  -c Release -p:Platform=x64 -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true -p:SelfContained=true `
  -o E:\work\desktop-guides\artifacts\unpackaged
```

The unpackaged executable was launched in the active console session. The
[`windows_ui_smoke.ps1`](../../tools/p0/windows_ui_smoke.ps1) script uses Windows
UI Automation in that session and writes JSON observations for a selected
fixture. It does not establish MSIX installation success.

## Installation gate still open

The current MSIX files are unsigned. An earlier development-signing attempt
on this host reached `Add-AppxPackage` but was rejected with `0x800B0109`
because the current-user trust setup did not satisfy package validation. On
a **disposable test VM**, follow Microsoft's
[test-certificate import procedure](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide)
using an elevated `LocalMachine\TrustedPeople` store for the public
development certificate. Do not alter trust stores on a regular workstation
to make a smoke test pass.
The temporary certificate and scheduled smoke-test tasks created on the
Windows host were removed after verification.

The clean-VM procedure remains: record installed Windows App SDK and WebView2
runtimes; sign the current MSIX with a VM-only development certificate; trust
that certificate in the VM; install and launch; then repeat with required
offline runtime installers and network disconnected. Windows 10 22H2 and
native ARM64 launches remain untested. Microsoft's
[packaged-app deployment guidance](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps)
and [WebView2 distribution guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
are the reference for that check.
