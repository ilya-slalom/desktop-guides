# P1 PDF text path decision (T10.0)

Decision: use a **native hybrid** for T10.1. `Windows.Data.Pdf` supplies the
page preview; pinned `PdfPig` 0.1.16 extracts selectable text from the same
page into a WinUI control with a UI Automation `TextPattern`. This is a
prototype decision on Windows 11 x64, not acceptance of the production PDF
reader or of S10/S17. The [P0 raster result](../p0/reader-decisions.md)
required a text-capable alternative.

## Compared candidates

Both candidates ran as unpackaged diagnostic modes of the WinUI fixture app
in interactive session 1 on Windows 11 x64 build `10.0.26200.0`. The
[native trace](evidence/pdf-ui-candidate.json) and
[outbound-blocked native trace](evidence/pdf-ui-candidate-offline.json) used
`DesktopGuides.App.dll` SHA-256
`36ffc1af5c5d6ed4541f4dd292cc9ed8ed6773f2b60152f3c053a9a14c9452c9`.
The [restricted WebView2 trace](evidence/pdf-web-candidate.json) used the
earlier candidate assembly SHA-256
`f5dc3a8bb53a8b9264eefefb64b1e22f47547f75bd906e25d0e61a312a740e85`.
The fixture source and hashes are in the [P0 manifest](../../tests/fixtures/p0/manifest.json).

| Check | Restricted WebView2 PDF prototype | Native preview plus PdfPig text prototype |
| --- | --- | --- |
| Tagged `pdf-access` | The synthetic local PDF loaded with one allowlisted request. The tagged paragraph appeared in neither UIA names nor document `TextPattern` output. This observation applies to the tested configuration, not every WebView2 setup. | PdfPig extracted the 35-character paragraph; the WinUI read-only text control exposed the entire paragraph in `TextPattern` and in the keyboard selection after Ctrl+A. |
| Scanned `pdf-scan` | Not run in this candidate. | No extracted text; the visible status said `Image-only page; OCR is unavailable`. |
| Encrypted `pdf-locked` | Not run in this candidate. | Wrong password left a retryable error; the fixture password opened preview and exposed `Locked guide secret page` as text. The attempted password is cleared from the UI control. |
| Long `pdf-long` | No validated capture/restore API; `Capture` reported unsupported. | Page 2 text matched page 2 preview and a captured page-2 locator restored after visiting page 3. Twenty-three further turns reached page 25 with matching text. Process working set rose from 259,538,944 to 262,098,944 bytes in the online run; the peak was 262,098,944 bytes. |
| Offline dependency | Not assessed. | The same UI checks passed while a temporary Windows Firewall rule blocked outbound traffic from the candidate executable. The blocked run rose from 257,867,776 to a peak of 261,054,464 working-set bytes. The rule was removed after the run. This is app-specific outbound blocking, not a physically disconnected or installed-app test. |
| Distribution | WebView2 is already a P0 dependency. The tested restricted prototype failed the text and locator gates. | `Windows.Data.Pdf` is supplied by Windows. The direct `PdfPig` 0.1.16 NuGet package declares Apache-2.0 and has no package dependencies in the locked graph. Its source release is [v0.1.16](https://github.com/UglyToad/PdfPig/releases/tag/v0.1.16); [NuGet metadata](https://www.nuget.org/packages/PdfPig/0.1.16) lists the license. |

The headless extraction traces show text on the tagged, locked, and 200-page
fixtures, and none on the scanned page:
[tagged](evidence/pdf-access-text.json),
[scanned](evidence/pdf-scan-text.json),
[locked](evidence/pdf-locked-text.json), and
[long](evidence/pdf-long-text.json). The prototype hashes through a stream
and calls PdfPig's stream overload, avoiding an explicit whole-file byte
array in application code. The diagnostic text view holds one extracted page
at a time; T10.1 still needs measured large-file memory behavior and an
explicit render-cache bound.

## Decision limits and next checks

- The tagged fixture is one short paragraph. UI Automation text and keyboard
  selection passed; actual Narrator speech and reading order across complex
  tagged layouts were not captured. T16.3 must verify them in the production
  adapter. A scanned page remains image-only; OCR is outside P1.
- The long prototype run covered pages 2–25, not a 200-page endurance session
  or the P1 1 GiB import limit. Process working set includes WinUI and other
  fixtures and is not a PDF cache measurement. T10.1 must enforce the 96 MiB
  initial rendered-image cap, bounded text retention, stale-render handling,
  and disposal under rapid turns and malformed inputs.
- The firewall check proves the candidate UI can read the local fixtures
  with its own outbound access denied. T10.2 and T17.3 still require an
  installed-app offline relaunch; this prototype does not establish a release
  support claim.
- T10.1 must read only the committed managed copy, check both engines'
  page counts, reject or explain text-extraction failure, and keep page
  preview and accessible text aligned. Keep the password only for an open
  session, clear each attempt, and never persist it.

The experimental modes and repeatable scripts live under
`src/DesktopGuides.App/Probes/` and `tools/p1/`. They remain separate from
the production `IReaderAdapter` that T10.1 will implement.
