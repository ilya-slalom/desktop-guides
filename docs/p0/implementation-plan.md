# P0 implementation plan and exit gates

Status: available Windows 11 x64 work verified on 25 September 2026; native Windows 11 ARM64 Core and installed UI fixtures also passed in CI. The user deferred the clean-machine absent-prerequisite gate because the available x64 host has Windows App Runtime and WebView2 installed and no disposable VM is enabled. Windows 10 x64 is also deferred. The [technical design](../p0-technical-design.md), [results](results.md) and [reader decisions](reader-decisions.md) explain the evidence and limits.

The macOS repository is the source checkout. Windows verification uses `E:\work\desktop-guides` on `pcsx2-win`. Copy source there before each build or UI run.

| Task | State | Verified outcome or remaining gate |
| --- | --- | --- |
| T01.1 WinUI solution and contracts | Done | WinUI 3 window, separate Core and probe adapters, locked toolchain, Windows 11 x64 launch. |
| T01.2 Windows CI | Done | Fresh runner 23/23 tests, native ARM64 Core 23/23 tests, architecture-labeled x64 and ARM64 packages with SHA-256 manifests; deliberate failing Core test failed CI and skipped packaging. |
| T01.3 Signed installation and prerequisites | **Partial; clean VM deferred** | Development-signed x64 MSIX installed/launched on Windows 11; 14/14 fixture workflows passed online and during a physically disconnected relaunch. Native ARM64 CI installed/launched and passed 14/14 UI fixtures. Temporary cert/package cleanup verified. Missing WebView2 was simulated without removing the shared runtime and recovery passed. The user deferred a **clean Windows 11 VM** check of actual missing Windows App Runtime/WebView2 failure and offline-installer recovery. |
| T02.1 Fixture corpus | Done | Deterministic generator and 19-entry CC0 manifest, including tagged, scanned and encrypted PDFs; SHA-256 verified on macOS and Windows. Filesystem-link escape tests ran on Windows. |
| T02.2 Native TXT probe | Done for P0 | Strict UTF-8 and explicit CP437, newline/whitespace tests, 10 MiB first-text and realized-item metrics, 17 sampled window responses during scrolling (all under 1 ms, no failures), exact restore after resize/font change and a tested edited-context fallback. |
| T02.3 Restricted HTML probe | Done for P0 | Fresh staged assets/profile, blocked navigation/resources/popups, canary zero guide requests, online/offline local assets, ID and text-context restore after reflow. |
| T02.4 PDF probe and engine decision | Done with S10 blocker | Fit-width page preview, bounded neighbor cache, rapid-turn generation checks, fractional restore, locked-PDF password flow and accessibility tree/Narrator keyboard observations. Native raster rendering is rejected as the final text-accessible PDF reader; S10 awaits a text-capable engine. |
| Evidence and choices | Done | Fixture-linked [online](evidence/installed-final/ui-suite/suite.json) and [offline](evidence/offline/ui-suite/suite.json) traces, CI manifests, signed install record and format decisions are preserved. |

## Deferred external gate

When a disposable, runtime-free Windows 11 x64 VM is available, run the [signed install procedure](toolchain.md) first without the Windows App Runtime framework and record the deployment error. Install the official x64 framework dependency, install the app, then check HTML before and after adding the official offline WebView2 Runtime. Record app version, OS/CPU, hashes, installer logs and the three reader outcomes. The user deferred this check; the local host cannot give an absent-prerequisite observation without removing shared runtimes or enabling a VM and rebooting it.

Windows 10 x64 launch stays `untested` until a suitable host or remote CI is available. Native ARM64 installation, launch and 14 reader fixtures passed in CI; ARM64 offline and physical keyboard password entry were not checked. S10 remains blocked by the PDF engine decision, independently of deployment.
