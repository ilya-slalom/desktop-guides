# Desktop Guides project guidance

- Use `docs/initial-design.md` for product scope and `docs/work-breakdown.md`
  for R → S → T → TR traceability. Keep both current when requirements change.
- Follow `docs/p0-technical-design.md` for the Windows baseline and reader
  spike; record any engine choice that differs from its starting approach.
- The Windows UI uses WinUI 3. Treat native TXT, local WebView2 HTML, and the PDF
  renderer as separate reader adapters behind a common interface.
- Keep imported guides readable offline and originals untouched. Treat imported
  HTML and asset paths as untrusted.
- Validate reader behavior and packaging on Windows before claiming support
  for a target OS or architecture.
- For installed P1 E2E runs triggered over SSH, use an interactive scheduled
  task for MSIX installation and UI Automation. Follow
  `docs/p1/e2e-testing.md`, including its data-backup and cleanup rules.
- Preserve stable per-guide locators and an explicit completion state; do not
  infer completion from estimated reading percentage.
