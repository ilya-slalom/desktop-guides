# P0 implementation plan

Status: in progress, 25 September 2026. The solution, WinUI 3 launch, core
tests, fixture generator, reader adapters, and package builds have initial
Windows results. CI execution, a clean MSIX installation, offline operation,
and PDF document-text accessibility remain open. Implements
[P0 technical design](../p0-technical-design.md) and S01–S02 in the
[work breakdown](../work-breakdown.md).

The immediate path is to make the core and test runner build on Windows, then
add the packaged WinUI probe and format adapters. The Windows workspace is
`E:\work\desktop-guides` on the SSH host `pcsx2-win`. The macOS checkout is the
source of truth; copy it to that path before each Windows verification run.

## Steps

- [x] **T01.1:** Pin .NET/Windows App SDK packages; create `DesktopGuides.sln`,
  `src/DesktopGuides.Core`, `src/DesktopGuides.App`, and
  `tests/DesktopGuides.Core.Tests`. Verify Core tests run and a WinUI window
  builds and launches on Windows 11 x64.
- [ ] **T02.1:** Add self-authored TXT/HTML/PDF fixtures, generator, and hash
  manifest. Verify hashes on both hosts before probing.
- [ ] **T02.2:** Write failing tests for strict TXT decoding, newline
  normalization, line indexing, and locator restoration. Implement the native
  bounded TXT view and measure it on the generated 10 MiB guide.
- [ ] **T02.3:** Add a restricted WebView2 static HTML probe. Test local assets,
  blocked hostile requests/navigation, capture/restore, and offline behavior
  on the Windows host.
- [ ] **T02.4:** Add a `Windows.Data.Pdf` page probe. Test page/zoom/restore
  and cache bounds; inspect tagged-document text access with Narrator.
- [ ] **T01.2:** Add a Windows CI workflow for restore, headless tests, and
  architecture-labeled package builds. Check that test failures make CI fail.
- [ ] **T01.3:** Build a development MSIX, install and launch on a clean
  Windows 11 x64 VM/session, record WebView2 and framework prerequisites, and
  repeat the probes offline.
- [ ] Record fixture-linked results and reader choices in `docs/p0/results.md`
  and `docs/p0/reader-decisions.md`. Mark untested OS/CPU combinations as such.

## Review focus

- Invalid UTF-8 must produce an encoding choice, not silent replacement.
- A long TXT guide must not create one persistent visual per source line.
- CSS and HTML requests must not reach an external host, even with scripts off.
- Restored HTML must survive images changing the page height.
- A PDF bitmap must not be reported as screen-reader-readable document text.
