# P0 reader probe: initial Windows observations

Status: partial, 25 September 2026. This records implementation evidence for
[T01.1–T02.4](../p0-technical-design.md) on one Windows 11 x64 host. The
working tree was uncommitted when these observations were made; app package
version was `0.1.0.0`. Fixture hashes and self-authored provenance are in the
[manifest](../../tests/fixtures/p0/manifest.json). The
[toolchain note](toolchain.md) records versions and reproduction commands.

| Fixture and SHA-256 | Windows observation | Evidence |
| --- | --- | --- |
| `txt-long`: `fe1fbddf8febae06a00c153cf9f4511a527ec34b78a618ffffa4d65ba45e78f7` | The 10 MiB file produced 197,845 indexed lines. After scrolling, 98 `ListViewItem` visuals were realized. Capture at visible line 20 and restore after increasing font size returned to line 20 with an exact content-hash match. Decode and line preparation took 46 ms; first paint and scroll gaps were not timed. | [Trace](evidence/txt-long.json) |
| `html-layout`: `bf517fb8c5733e683593be118af9ba95fa2b16333fb65ed461ee172b610e0127` | The local image loaded. Jumping to `resume`, capturing, returning to top, increasing text zoom to 1.10, and restoring used the element locator. A screenshot inspection showed the “Resume marker after chart” visible after restore. The complete HTML navigation took 1,310 ms. | [Trace](evidence/html-layout.json) |
| `html-hostile`: `9d1011d5b0f4f91024234078b5e61c44c1a9e9337f991af5c77a3c64eb1c087a` | A loopback canary on `127.0.0.1:8765` returned 204 for a health request. After loading hostile HTML and exercising its links, the canary logged **zero guide requests**. The adapter recorded 3 blocked requests or navigations and 1 blocked popup. Local CSS loaded. | [Trace](evidence/html-hostile.json) |
| `pdf-long`: `3ea02ce2dcd27b67034df87a0f8c3cbbf35580e9ae2deec808c1b239a4442eeb` | The 200-page PDF rendered page 1, moved to page 2, captured, moved back to page 1, and restored page 2. Estimated current decoded raster size was 7.4 MiB, below the 96 MiB cap. First page load and render took 125 ms. | [Trace](evidence/pdf-long.json) |

The TXT, HTML, and PDF views were also inspected visually in the WinUI window.
`txt-utf8` displayed café and Japanese characters; `html-static` displayed
its nested CSS and local blue map image; and `pdf-short` displayed a raster page
with accessible Previous/Next controls and an image-only text-accessibility
notice. After the last source change, locked restore and **20/20 core tests**
passed on the Windows host, and the current x64 and ARM64 MSIX builds both
succeeded. Packaging reported one warning for the optional `mspdbcmf.exe`
symbols tool; no build errors occurred.

## Limits of these observations

- The GUI run used a self-contained **unpackaged** x64 publish. Both x64 and
  ARM64 MSIX packages built, but neither current package was installed or
  launched. The ARM64 artifact was built on x64 hardware; native ARM64 and
  Windows 10 runs are pending.
- No clean VM, offline network-disconnect run, or missing-runtime recovery run
  was performed. The empty loopback log proves only that the tested hostile
  fixture did not reach that canary during this run.
- The HTML locator was restored after a font zoom change, but resize, delayed
  image loading, content edits, and symlink or junction escapes remain to test.
- The PDF renderer draws page images. Narrator document-text access, tagged,
  scanned, and password-protected PDF fixtures remain to test. Page fraction
  after a viewport resize and rapid zoom cancellation also remain to measure.
- Working-set numbers in the JSON traces are for the app process after the
  workflow. They are not peak memory, do not include separate WebView2
  processes, and were measured in a session that had opened earlier fixtures.
