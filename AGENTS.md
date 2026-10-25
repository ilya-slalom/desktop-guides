# Desktop Guides project guidance

- Read `docs/architecture.md` before changing code, and `docs/contributing.md`
  for building, testing and PR conventions. Keep both current when the design
  or the workflow changes.
- `docs/roadmap.md` lists the open work by ID. Name the roadmap item a PR
  targets, and remove an item once it's finished.
- The Windows UI uses WinUI 3. Treat native TXT, local WebView2 HTML, the PDF
  renderer and image maps as separate reader adapters behind a common
  interface.
- Keep imported guides readable offline and originals untouched. Treat imported
  HTML and asset paths as untrusted.
- Validate reader behavior and packaging on Windows before claiming support
  for a target OS or architecture. `docs/release/tested-matrix.md` records
  what each release was tested on.
- For installed E2E runs triggered over SSH, use an interactive scheduled
  task for MSIX installation and UI Automation. Follow `docs/testing.md`,
  including its data-backup and cleanup rules.
- Preserve stable per-guide locators and an explicit completion state; do not
  infer completion from estimated reading percentage.
- Online sources follow `docs/sources.md`: no new source or fetch path
  without its terms review, and live sites are checked by hand, never by
  automation.
- In each PR description, name the target roadmap item, the prerequisites and
  their status, and the intended outcome. For UI changes, include a screenshot
  of the changed interface in the PR description.
- Review PRs primarily for functional defects and regressions; prioritize
  test-harness details and small edge cases when they affect those outcomes.
- When triaging HTML or PDF review findings, assess whether a guide from a
  major public game-guide website could realistically trigger the issue through
  a supported import path. Check representative guides when feasible. Set
  priority from likely frequency and user impact, and state uncertainty instead
  of treating a contrived fixture as evidence of prevalence.
- Release steps are in `docs/releasing.md`. Documentation history before
  25 October 2026 (per-task designs, plans, results and evidence) is under the
  git tag `docs-history-2026-10-25`.
