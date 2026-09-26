# P1 implementation results

Status: M0 implemented on `feat/p1-m0` in [PR #3](https://github.com/ilya-slalom/desktop-guides/pull/3)
for review, with a SQLite initialization review fix on 26 September 2026.
The P1 first usable release remains in progress. The
[dependency plan](implementation-plan.md) defines all 49 task exit gates;
this file records checks actually run.

## M0 task results

| Task | Implemented output | Verification and remaining scope |
| --- | --- | --- |
| T03.1 | Portable Game, Guide, ReadingState, ReaderPreferences, and Settings records; repository contract; SQLite v1 schema and repository. IDs are generated, timestamps use an injected UTC clock, foreign keys are enabled on each connection, and a stored guide root must match its ID. `TextCodePage` is stored only for TXT. | Windows integration tests reopen two guides under one game with independent locators, estimates, completion timestamps, and preferences; reject an orphan state and a mismatched guide root. An interrupted first initialization can retry when the version-0 database has no schema objects, passes integrity check, and has no other application ID. Existing unknown schema or another application ID is preserved and rejected. The v1→v2 migration and public guide publication belong to T03.2 and T06.3. |
| T03.3 | `ILibraryPaths`, strict forward-slash managed relative paths, and an injected-root resolver under generated guide IDs. | Windows tests accept a nested CSS asset and reject traversal, absolute/UNC paths, percent escapes, symlinks, and an NTFS junction. |
| T11.2 | Portable reader session, typed actions and capability policy, plus the WinUI view adapter contract. | Fake-reader tests show supported commands dispatch and unsupported commands stop at the policy. Production TXT/HTML/PDF adapters and shell command controls are later M3 work. |
| T12.1 | Versioned bounded JSON codecs for TXT, HTML, and PDF, with fingerprint checks and exact/context/approximate restore candidates. | Core tests cover round trips, changed bytes, future versions, invalid JSON/numbers, duplicate fields, oversized text, wrong format, and HTML entry-document mismatch. The codecs do not alter completion. Actual readers use them in M3. |
| T10.0 | [Native hybrid PDF decision](pdf-decision.md) with restricted WebView2 comparison, license check, fixture traces, keyboard/UIA selection, password retry, app-specific outbound-blocked run, and 23 repeated page turns. | The prototype passed its tested decision gates on Windows 11 x64. It is not a production PDF adapter. Narrator speech, complex reading order, installed offline use, and cache/endurance limits remain T10.1–T10.3, T16.3, and T17.3 gates. |

## Windows M0 verification

These checks were recorded for the initial M0 implementation on 25 September 2026.

- Host: Windows 11 x64, build `10.0.26200.0`; source staged under
  `E:\work\desktop-guides`; .NET SDK `10.0.401`. Locked restores passed for
  Core tests, Infrastructure tests, the WinUI app, and the PDF tool.
- Release headless tests: **61/61 Core and 10/10 Infrastructure passed**,
  including the two-guide completion-state assertion and Windows reserved
  device-name path cases.
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
  was removed.

## Review follow-up — 26 September 2026

The review found that an interrupted first initialization could leave an
existing SQLite file at `user_version = 0`. The repository previously rejected
it solely because the file existed. Initialization now retries only if the
database has no schema objects, passes `integrity_check(1)`, and has
`application_id = 0`. Unknown schema objects or a different application ID
still require recovery instead of replacement.

On the Windows 11 x64 host under `E:\work\desktop-guides`, the three focused
tests first reproduced the issue against the old code (one failure, two
passes), then passed with the fix (three passes). The full locked Release run
passed **61/61 Core and 13/13 Infrastructure** tests. The PR checks for this
follow-up are tracked on [PR #3](https://github.com/ilya-slalom/desktop-guides/pull/3).

## PR CI evidence

[PR CI run 36157085242](https://github.com/ilya-slalom/desktop-guides/actions/runs/36157085242)
passed all five jobs for code head `6c6deb8527e271e4398d6d833d3bb77698776e22`
before the 26 September review follow-up:
locked Core/Infrastructure tests on Windows x64, native Windows 11 ARM64
Core/Infrastructure tests, x64 and ARM64 unsigned MSIX builds, and the
installed ARM64 P0 fixture regression. GitHub's `pull_request` checkout used
synthetic merge commit `c6b09d302d2c9a15bfb2417a6ecd1c674616dbd2`,
recorded by the [native environment](evidence/ci/native-arm64-environment.json).

The retained [x64 Core](evidence/ci/core-tests.trx) and
[Infrastructure](evidence/ci/infrastructure-tests.trx) results report
**61/61** and **10/10** passes. The [native ARM64 Core](evidence/ci/native-arm64-core-tests.trx)
and [Infrastructure](evidence/ci/native-arm64-infrastructure-tests.trx)
results report the same counts, with OS build `10.0.26200.0`, native `Arm64`
process, and SDK `10.0.401`.

The [installed ARM64 record](evidence/ci/native-arm64-signed-install.json)
reports **14/14** P0 fixture workflows passed in interactive session 2 and
confirms that its test package and trust certificate were removed. The
[suite traces](evidence/ci/native-arm64-ui-suite/suite.json) are retained.
This verifies the diagnostic P0 package on ARM64; the M0 PDF text candidate
was exercised interactively on Windows 11 x64, and the production P1 reader
has not been installed or tested.

P0's 14-fixture installed result remains [P0 evidence](../p0/results.md);
these M0 prototype results do not claim that the production shell, import,
resume coordinator, PDF reader, or release package is complete.
