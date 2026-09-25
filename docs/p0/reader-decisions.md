# P0 reader decisions so far

Status: provisional, 25 September 2026. Evidence is in
[the initial results](results.md). These decisions guide the next prototype
iteration; the [P0 exit gates](../p0-technical-design.md) are still open.

| Area | Current direction | Reason and remaining gate |
| --- | --- | --- |
| TXT | Continue with the native virtualized line view and normalized-character locator. | The 10 MiB fixture returned to the same line after font scaling while keeping realized visuals under 100 in the observed run. Measure first paint, scrolling gaps, and an edited-content fallback before freezing the reader design. |
| Static HTML | Continue with a synthetic HTTPS origin served only through a manifest-restricted `WebResourceRequested` handler, with page scripts disabled and host-owned position queries. | Local nested CSS and images displayed; the tested hostile fixture reached no loopback canary endpoint; a marker restored after zoom. Repeat offline and test layout delays, external link activation, and filesystem escapes. |
| PDF | Keep `Windows.Data.Pdf` as an image-rendering experiment only. Decide the product engine after document-text accessibility testing. | Page and zoom basics are working, and the current-page raster remains under the cap. This view does not expose PDF document text from its bitmap. A tagged PDF with Narrator, selectable text, and search requirements may require a different engine for S10. |
| Deployment | Keep single-project MSIX for package builds and self-contained unpackaged publish for local UI development. | x64 and ARM64 MSIX outputs built; x64 unpackaged UI launched. A development MSIX install, framework prerequisite failure and recovery, and offline relaunch require a clean test VM. |

No Windows 10, native ARM64, or PDF document-text accessibility claim follows
from the current build results.
