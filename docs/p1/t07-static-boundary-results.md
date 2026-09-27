# T07.2 static HTML import boundary result

Status: implementation check on 27 September 2026. T03.3 and T07.1 are
merged into `main`. T06.1 preview UI and T06.3 staging/publication remain
later M2 work.

`StaticHtmlImportValidator` takes a selected absolute HTML entry and returns
the T07.1 manifest with typed missing, remote, unsafe, and unsupported
warnings. Its rooted stream source checks the selected directory and each
asset path for Windows reparse points, then compares an opened handle's
final path with the requested path before any bytes are read. Included asset
names that collide under Windows case rules fail preview.

The post-copy API checks each manifest-listed source and staged file against
the preview byte length and SHA-256. It reads at most one byte beyond the
recorded length and reports changed source and changed stage separately.
T06.3 will use this check before metadata publication; this task does not
copy or publish files.

On the Windows 11 x64 host (build `10.0.26200.0`, .NET SDK `10.0.401`),
merged PR #9 source plus T07.2 changes were staged under
`E:\work\desktop-guides\t07-boundary-20260927`. Locked Infrastructure and
production-project restores passed. Release Infrastructure tests passed
**81/81**, including **7/7 T07.2 checks** for warning classification,
encoded escape, file symlink, directory junction, selected-root junction,
case collision, changed source/stage, and linked stage file. The unsigned
Release x64 production MSIX build passed with zero errors and the host's
existing `mspdbcmf.exe` symbols-package warning.

No installed import workflow or ARM64 runtime result is claimed here.
The Windows CI headless and package jobs remain the branch exit check.
