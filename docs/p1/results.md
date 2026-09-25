# P1 implementation results

Status: M0 implemented on `feat/p1-m0` for review, 25 September 2026.
The P1 first usable release remains in progress. The
[dependency plan](implementation-plan.md) defines all 49 task exit gates;
this file records checks actually run.

## M0 task results

| Task | Implemented output | Verification and remaining scope |
| --- | --- | --- |
| T03.1 | Portable Game, Guide, ReadingState, ReaderPreferences, and Settings records; repository contract; SQLite v1 schema and repository. IDs are generated, timestamps use an injected UTC clock, and foreign keys are enabled on each connection. `TextCodePage` is stored only for TXT. | Windows integration tests reopen two guides under one game with independent locators, estimates, completion timestamps, and preferences; reject an orphan state. The v1→v2 migration and public guide publication belong to T03.2 and T06.3. |
| T03.3 | `ILibraryPaths`, strict forward-slash managed relative paths, and an injected-root resolver under generated guide IDs. | Windows tests accept a nested CSS asset and reject traversal, absolute/UNC paths, percent escapes, symlinks, and an NTFS junction. |
| T11.2 | Portable reader session, typed actions and capability policy, plus the WinUI view adapter contract. | Fake-reader tests show supported commands dispatch and unsupported commands stop at the policy. Production TXT/HTML/PDF adapters and shell command controls are later M3 work. |
| T12.1 | Versioned bounded JSON codecs for TXT, HTML, and PDF, with fingerprint checks and exact/context/approximate restore candidates. | Core tests cover round trips, changed bytes, future versions, invalid JSON/numbers, duplicate fields, oversized text, wrong format, and HTML entry-document mismatch. The codecs do not alter completion. Actual readers use them in M3. |
| T10.0 | [Native hybrid PDF decision](pdf-decision.md) with restricted WebView2 comparison, license check, fixture traces, keyboard/UIA selection, password retry, app-specific outbound-blocked run, and 23 repeated page turns. | The prototype passed its tested decision gates on Windows 11 x64. It is not a production PDF adapter. Narrator speech, complex reading order, installed offline use, and cache/endurance limits remain T10.1–T10.3, T16.3, and T17.3 gates. |

## Windows M0 verification

- Host: Windows 11 x64, build `10.0.26200.0`; source staged under
  `E:\work\desktop-guides`; .NET SDK `10.0.401`. Locked restores passed for
  Core tests, Infrastructure tests, the WinUI app, and the PDF tool.
- Release headless tests: **55/55 Core and 9/9 Infrastructure passed**,
  including the final two-guide completion-state assertion.
- Unpackaged WinUI x64 publish and `PdfTextSpike` Release build passed, with
  zero PDF-tool build warnings or errors. An unsigned x64 MSIX build passed;
  its sole warning was that `mspdbcmf.exe` was unavailable, so no symbols
  package was generated. The
  [interactive native trace](evidence/pdf-ui-candidate.json) and
  [outbound-blocked trace](evidence/pdf-ui-candidate-offline.json) passed on
  the published assembly SHA-256
  `36ffc1af5c5d6ed4541f4dd292cc9ed8ed6773f2b60152f3c053a9a14c9452c9`.
  The [WebView2 comparison](evidence/pdf-web-candidate.json) did not expose
  tagged document text or a validated locator in its restricted prototype.
- After testing, no candidate firewall rule, scheduled task, or app process
  remained. The stale temporary directory from the earlier junction test
  was removed. Windows package CI and native ARM64 CI checks are pending the
  review branch.

P0's 14-fixture installed result remains [P0 evidence](../p0/results.md);
these M0 prototype results do not claim that the production shell, import,
resume coordinator, PDF reader, or release package is complete.
