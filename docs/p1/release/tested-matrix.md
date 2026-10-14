# Tested matrix

What each release was tested on before it was published (T17.3). A target
is supported only when it has a passing row here. The run's full evidence
stays with its run; this page holds the sanitized summary.

## How a row is made

The `rc` group of the installed harness runs on the release's draft
assets ([design](../t17-3-release-candidate-design.md)):
1. install the previous release;
2. build a library through the UI;
3. upgrade in place to the candidate;
4. read, resume and complete;
5. relaunch with the app's network blocked;
6. export, remove, and restore.

Every stage must pass.

## 1.0.1

Tested on 14 October 2026, on the draft release's own assets, built from
`77f45f2` by release run
[38578894569](https://github.com/ilya-slalom/desktop-guides/actions/runs/38578894569).

| Target | Windows | CPU | Windows App Runtime | WebView2 Runtime | Package | Result |
| --- | --- | --- | --- | --- | --- | --- |
| Windows 11 x64 | Windows 11 Enterprise, build 10.0.26200.0 | AMD Ryzen 7 7800X3D (x64) | `Microsoft.WindowsAppRuntime.2` 2.5.1.0 | 155.0.4283.45 | `DesktopGuides-1.0.1.0-x64.msix`, SHA-256 `EAA2F6A0D2E439FF52B6FFFC98191FF7AE47F0907C5FEAA511B32DBA73B478F1`, signer `BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6` | Passed |

The stages, all passed:
1. **Install the previous release:** v1.0.0, SHA-256
   `F3B75010937555656D0D4307BC9144D1636D800F2E1BEB4F7AC683DF1BE015B9`.
2. **`workflow-build`** (8.2 s): add a game, and import TXT, HTML and PDF
   through the UI. Then the originals were removed.
3. **The upgrade:** from 1.0.0.0 to 1.0.1.0 in place, with the same family
   and the library intact.
4. **`workflow-read`** (14.4 s) and **`workflow-resume`** (10.3 s), with
   completion.
5. **`workflow-offline`** (10.5 s): with a firewall rule blocking the app,
   every guide opened at its place, and no non-loopback connection was
   made. The rule was then removed.
6. **`workflow-export`** (4.1 s) and **`workflow-restore`** (13.3 s).
7. **Cleanup:** the package and the certificate trust were removed, and
   no crash entries were logged.

## Not tested

| Target | Why |
| --- | --- |
| A clean Windows 11 x64 machine without the Windows App Runtime | The runtime-free VM check is T17.8, in the final milestone. |
| Windows 10 x64 | Not advertised. |
| Windows 11 ARM64 | Not released; it needs its own native full-workflow result. |
| The portable build's own release gates | First run without installation, data location and replacing the exe are defined separately. Its scenario groups run in CI on every change. |
