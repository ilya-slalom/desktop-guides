# P0 technical design: Windows baseline and reader spike

Status: implementation design, 25 September 2026. Scope: **S01 and S02 only**
in the [work breakdown](work-breakdown.md), covering T01.1–T01.3 and
T02.1–T02.4. The [high-level design](initial-design.md) defines the product
scope. Initial implementation observations are in [P0 results](p0/results.md).

P0 produces a small installable WinUI 3 prototype, reproducible fixture corpus,
measurements, and reviewable reader decisions. Library management, production
import, SQLite, completion tracking, and final release packaging belong to P1.

## 1. Decisions and boundaries

| Question | Options and tradeoff | P0 starting choice |
| --- | --- | --- |
| Packaged project | Single-project MSIX keeps a new WinUI 3 app small; a separate packaging project offers more control but adds build wiring. | Start with single-project MSIX; record a change if the selected Windows App SDK toolchain requires a separate packaging project. |
| TXT view | One large text control is simple but may grow a large visual tree; line or chunk virtualization adds locator work. | Prototype chunked native rendering and compare with a simple control on the same fixtures. |
| Local HTML | `file:` is simple but makes local resource and origin rules awkward; a virtual host supports relative assets but needs strict request and navigation controls. | Use one synthetic host mapped to one fixture root in WebView2; reject all other destinations. |
| PDF | `Windows.Data.Pdf` provides native page rendering and a straightforward page locator; a text-capable engine may be needed for document text access. | Prototype native pages, then make the engine decision from accessibility, resume, and performance evidence. |
| Windows App SDK deployment | Framework-dependent MSIX keeps the app package smaller but adds a framework dependency; self-contained deployment moves more bytes into the app. | Measure a framework-dependent package first, and document the installed prerequisites and an offline installation route. |

P0 confirms these choices; it does not treat them as proven behavior. Any
failed gate creates a short decision record and updates S08–S10 or S17 before P1.
Versions of .NET, Windows App SDK, Windows SDK, Visual Studio Build Tools, and
WebView2 SDK are selected from compatible supported releases at kickoff, then
**pinned** in the prototype and its toolchain note. Avoid floating package
versions in a reported result.

## 2. Deliverables and traceability

| Task | Output | Required evidence |
| --- | --- | --- |
| T01.1 | Buildable solution and minimal probe contracts | Project references, pinned toolchain, successful WinUI 3 launch |
| T01.2 | Windows CI workflow | Restore/build/unit-test logs and an unsigned package artifact per built architecture |
| T01.3 | Development-signed test MSIX and install guide | Clean VM installation, prerequisite check, app launch, OS/CPU record |
| T02.1 | Fixture manifest and generation scripts | Reproducible files, checksums, licenses, expected features |
| T02.2 | Native TXT probe | Open/scroll/resize metrics, bounded visuals, anchor accuracy |
| T02.3 | Restricted HTML probe | Local asset rendering, request log, hostile-fixture outcomes, anchor accuracy |
| T02.4 | PDF probe and engine decision | Page/cache metrics, resume results, screen-reader observations |

`TR01.1` is evidenced by WinUI references confined to the app project and
core tests running without UI initialization. `TR01.2` needs a build manifest
and installation record, not merely a successful compiler exit. `TR02.1` needs
fixture-linked results for each reader. `TR02.2` needs offline and controlled
request observations. `TR02.3` needs an explicit PDF text-accessibility result.

## 3. T01.1 — solution and prototype contracts

### Proposed repository layout

```text
DesktopGuides.sln
global.json
Directory.Packages.props
.github/workflows/windows-ci.yml
src/DesktopGuides.Core/              # no WinUI or Windows App SDK reference
src/DesktopGuides.App/               # packaged WinUI 3 app and probe pages
tests/DesktopGuides.Core.Tests/      # headless tests for text/locator helpers
tests/fixtures/p0/                  # small self-authored fixture inputs
tools/p0/                            # deterministic large-fixture generator
docs/p0/                             # environment, results, decision record
```

The app references Core; Core.Tests references Core. Core references neither
the app nor a Windows UI package. The P0 app starts on a `ReaderProbePage` with
three fixture choices, a position readout, `Capture`, `Restore`, font/zoom
controls, and a diagnostic event panel. This page is a test harness and is not
the P1 reader shell. A probe-only `Load fixtures` action copies the selected
fixture set into the app's scratch data directory and checks its manifest;
this is not the production import pipeline.

Core owns only platform-independent text normalization, locator data, and
result types. Rendering adapters live in the app during P0. Use a small probe
contract so each format supports the same experiment:

```csharp
public interface IReaderProbe : IAsyncDisposable
{
    GuideFormat Format { get; }
    UIElement View { get; }
    Task OpenAsync(FixtureId fixture, CancellationToken cancellationToken);
    Task<ReaderLocation> CaptureAsync(CancellationToken cancellationToken);
    Task<RestoreResult> RestoreAsync(
        ReaderLocation location, CancellationToken cancellationToken);
}
```

Declare this probe interface in the app project because `UIElement` is a WinUI
type. `FixtureId` resolves through the test harness to a fixture under its own
app-data root; it is never interpreted as an arbitrary file path from page
content. `ReaderLocation` is a versioned, typed TXT/HTML/PDF union with a
content SHA-256 and an approximate fraction in `[0,1]`. P0 can serialize it
to JSON for a close/relaunch test without creating the P1 database. Each
adapter owns a WinUI view, uses asynchronous opening/rendering, and returns
structured errors for missing or unsupported content.

Start from the packaged WinUI 3 template using the chosen pinned toolchain.
Keep `TargetFramework` Windows-specific in the app and portable in Core. Set
the package's minimum Windows version only after checking the intended
Windows 10 22H2 and Windows 11 test targets. An OS being technically supported
by WinUI 3 does not itself make it a supported product target.

**T01.1 done when:** the app launches on Windows 11 x64, switches among three
placeholder probe views, Core.Tests runs without Windows UI activation, and
the exact SDK/target settings are written in `docs/p0/toolchain.md`.

## 4. T01.2 — Windows CI

Use a Windows GitHub Actions runner for compile and headless tests. Pin the
action revisions and .NET SDK; record the runner image and Windows SDK versions
in build output. The implementation workflow has four ordered steps:

1. Restore from the repository's package lock state.
2. Build Core and run Core.Tests in Release.
3. Build the WinUI 3 app/package for x64 and ARM64 as distinct artifacts.
4. Upload test results, package files, and a build manifest containing commit,
   SDK versions, target architecture, package version, and hashes.

An x64 hosted runner can build an ARM64 artifact but cannot establish that it
**runs** on ARM64. CI is a compile and unit-test gate. Interactive WinUI,
screen-reader, offline, and installation checks run on a dedicated Windows VM
or device and are recorded separately. If the hosted image cannot build one
architecture, preserve the failure and switch that artifact to a verified
Windows build host; do not mark it tested from another architecture's result.

The initial CI package may be unsigned. Signing keys and development
certificates stay out of the repository and CI logs. P0 does not need a
production signing identity.

**T01.2 done when:** a fresh Windows runner restores, builds, runs headless
tests, and emits architecture-labeled artifacts plus a manifest. A deliberate
failing Core test makes the workflow fail.

## 5. T01.3 — MSIX installation and prerequisites

Use a disposable Windows 11 x64 VM snapshot for the required P0 install check.
Record OS build, CPU architecture, RAM, display scale, .NET and Windows App SDK
versions, WebView2 Runtime version, package version, and test time. The VM
starts without the prototype installed.

Procedure:

1. Build a development MSIX and sign it with a local development certificate.
   Trust that certificate **only on the test VM**. Do not commit the private
   key or a reusable signed secret.
2. Check package dependency resolution during installation. A
   framework-dependent packaged app may fail to install if the matching
   Windows App SDK framework is unavailable; the app cannot show its own
   error before installation succeeds.
3. Launch the app with WebView2 available, then repeat on a clean snapshot
   without it. At startup, check runtime availability and show a nonblocking,
   actionable installer/repair message; TXT and PDF probes remain available.
   Opening HTML also handles a failed runtime initialization cleanly.
4. Install required runtimes, copy the fixture corpus, disconnect the VM from
   the network, and relaunch each probe. Keep runtime setup separate from
   the claim that imported content reads offline.
5. Re-run install/launch checks on Windows 10 22H2 x64 and Windows ARM64
   hardware or a suitable native VM before making compatibility claims for
   those targets.

Document both online and offline prerequisite installation paths. The
Evergreen WebView2 Runtime has a standalone offline installer; the final
distribution may bundle or link it after the packaging decision. A missing
Windows App SDK framework dependency is an installer concern; a missing
WebView2 Runtime can be surfaced by the app after launch. Compare
framework-dependent and self-contained package size and clean-install
behavior in the decision record if the framework dependency complicates the
intended distribution.

**T01.3 done when:** a development-signed package installs and opens on a
clean Windows 11 x64 VM, prerequisite failure and recovery are documented,
and the offline relaunch succeeds. Windows 10 and ARM64 results are labeled
`untested` until run on those targets.

## 6. T02.1 — reproducible fixture corpus

Keep small self-authored fixtures in `tests/fixtures/p0/`. Generate large
variants with a checked-in deterministic script into a git-ignored temporary
directory. A manifest records fixture ID, format, byte size, SHA-256, source or
generation command, redistribution rights, expected features, and expected
negative behavior. The test harness verifies hashes before opening files.
Keep hostile HTML fixtures and their canary host local to the test VM. Generate
any symlink or junction escape fixture on the Windows test host, where its
filesystem behavior can be observed. The manifest has a schema version and,
for each fixture, `id`, relative `path`, `format`, `sha256`, `bytes`,
`provenance`, and `expectations`; reject absolute or escaping manifest paths.

| Fixture IDs | Contents | What it proves |
| --- | --- | --- |
| `txt-utf8`, `txt-bom`, `txt-legacy` | LF/CRLF, non-ASCII characters, legacy code page, narrow and wide prose | Strict decoding, explicit encoding choice, stable normalization |
| `txt-ascii`, `txt-long` | ASCII map with aligned columns and long lines; generated 10 MiB or larger guide | Whitespace fidelity, horizontal overflow, bounded rendering |
| `html-static`, `html-layout` | Headings, internal anchors, nested relative images/CSS, late image dimensions | Offline assets and location restoration after layout shifts |
| `html-hostile` | Remote image/CSS, `@import`, iframe, form, `target=_blank`, script, meta redirect, `file:` and `../` paths | Script and navigation blocking, asset-root isolation, no guide-originated network |
| `pdf-short`, `pdf-long` | Self-authored text PDF and many-page mixed-size PDF | Page rendering, zoom/cache limits, page locator |
| `pdf-access`, `pdf-scan`, `pdf-locked` | Tagged/selectable text, scanned image, encrypted PDF | Screen-reader reality, text-layer decision, readable error path |

Include a short expected-location script for each positive fixture: open,
scroll or page to a labeled marker, capture, close/relaunch, resize or change
font/zoom, restore, and compare visible content. The corpus is for P0 evidence;
it does not establish support for every legacy guide in the wild.

**T02.1 done when:** fixture hashes reproduce on the Windows host, every
reader experiment names its fixture IDs, and no unlicensed external guide is
committed.

## 7. T02.2 — TXT reader experiment

### Decode and normalize

Read bytes without mutating the fixture. Recognize UTF-8 BOM; otherwise try
strict UTF-8 decoding. On failure, require an explicit legacy encoding
selection in the probe (start with Windows-1252 and CP437, registering .NET
code-page support if necessary). Record the selected encoding with the
location. Normalize CRLF and bare CR to LF **after** decoding, preserving
spaces, tabs, and all other characters. A character offset means a UTF-16
code-unit offset in the normalized string, matching C# string indexing.

Use an immutable line-start index over the normalized string. Each visible
chunk covers a bounded number of lines, with a known start and end character
offset. Default to monospace and no line wrapping so maps and columns remain
aligned; a long line can scroll horizontally. The probe may compare an
individual-line virtualized view against chunks, but must not keep one WinUI
element for every line or character.

### Capture and restore

At capture, identify the first visible line/chunk and its normalized character
offset. Store `{schemaVersion, sha256, encoding, charOffset, contextQuote,
fraction}`. `contextQuote` holds a short bounded span of nearby normalized
text; `fraction` is an approximate fallback. At restore with unchanged bytes,
clamp the offset, position the containing chunk or line, then adjust within
it. Repeat after changing font size, window width, and display scaling. The
expected top visible logical line is within one line of the captured one.
If the content hash changes, search for `contextQuote` and prefer the match
nearest the old offset; use the fraction only when no unambiguous match exists
and report that result as approximate.

### Measurements and choice

Compare time from Open to first visible text, scrolling gaps on the UI thread,
peak memory increase, realized element count, and capture/restore accuracy on
`txt-ascii` and `txt-long`. The initial spike budget is first text within
three seconds for a 10 MiB fixture on a recorded reference Windows 11 x64
machine, no repeated UI-thread gap above 500 ms during scrolling, and
realized element count that stays bounded as fixture line count grows. These
are **decision thresholds**, not release performance guarantees; record the
hardware and adjust a threshold only with written evidence.

**T02.2 done when:** whitespace tests pass; both resize and font changes
restore within one logical line on unchanged content; measurements support a
bounded native strategy or document why S08 must use a different one.

## 8. T02.3 — restricted HTML reader experiment

### Loading and isolation

Copy the fixture and supported static assets under a dedicated probe
directory. Map only that directory to a synthetic HTTPS virtual host via
WebView2. Do not navigate to raw `file:` URLs. Use a fresh WebView2 user-data
folder for security tests so cached content cannot mask a request.

For the probe:

- Disable document JavaScript, host objects, web messages, and unneeded
  permissions. The host can still run fixed DOM queries with
  `ExecuteScriptAsync` when page scripts are disabled.
- Register a resource-request filter and deny all requests outside the
  active synthetic origin, including image, stylesheet, frame, fetch, and
  indirect CSS loads. Restrict top-level navigation separately and cancel
  new windows and downloads. Internal fragment links remain available.
- Resolve paths beneath the fixture root. Reject parent traversal, absolute
  paths, symlinks escaping the root, and unsupported active assets before
  mapping. Deny navigation into a different guide root even if the host is
  reused.
- Treat an external link as a request for an explicit system-browser action;
  clicking it cannot make the embedded reader fetch the page.

The resource filter and navigation events are separate defenses. Disabling
page scripts alone does not stop remote images, CSS, meta redirects, or links.
Log allowed and denied URLs by origin and resource kind, without recording
guide text.

### HTML position algorithm

On capture, a fixed host-owned DOM query samples the visible text near the
top of the viewport and returns a small JSON object:
`{relativeDocument, elementId?, textContext?, textOffset?, scrollFraction}`.
Try a caret/text-range query at the viewport point; fall back to the nearest
stable element and a short text quote. Validate lengths, types, bounds, and
document path before accepting the JSON. On restore, wait for navigation and
layout readiness, prefer an ID or text-context match, then fall back to the
fraction. Recheck after local images settle, with a bounded timeout.

For `html-static`, unchanged content should return to the same paragraph
after window and font changes. A layout-shift fixture tests that an early
fraction alone is insufficient. If a changed fixture only restores by
fraction, label the result approximate. P0 records whether static HTML with
page scripts disabled covers the corpus; script-dependent websites remain
outside the MVP import promise.

### Offline and hostile-input proof

Point hostile URLs at a local HTTP canary and record whether it receives a
request. Capture WebView2 resource and navigation events. Repeat with the VM
network disconnected. A passing result has zero guide-originated requests to
the canary or any external origin, while local CSS/images and internal anchors
still work. If WebView2 itself makes an unrelated runtime connection, record
it separately rather than attributing it to the guide.

**T02.3 done when:** local assets display; the hostile fixture cannot fetch,
redirect, open a new window, or escape the mapped root; and the static fixture
restores to the same paragraph after reflow.

## 9. T02.4 — PDF reader experiment

### Native page-render path

Load the managed fixture with `Windows.Data.Pdf.PdfDocument`, request pages by
zero-based index, and render each `PdfPage` to an in-memory stream at the
requested raster size. Decode asynchronously and update the WinUI image only
when the request still matches the selected page and zoom generation. Dispose
page and stream resources after use. Use page dimensions and viewport width
to calculate fit-to-width; render at an appropriate display scale and cap
the requested raster size.

Cache the current page and a small neighbor window with an LRU byte budget
(initial cap: 96 MiB of rendered images). Cache keys include document hash,
page index, and raster width/zoom. A rapid page or zoom change cancels or
ignores obsolete render results. A many-page PDF must not render every page
up front.

The locator is `{schemaVersion, sha256, pageIndex, pageFraction}` with
`pageFraction` in `[0,1]` from the top of that page. Clamp both values on
restore. Check mixed page sizes, resize, fit-to-width, and an out-of-range
saved page. The expected page index is exact and the visible point should
remain within one tenth of the page height after resize. If the PDF content
hash changes, clamp to the available pages and label the result approximate.

### Text access and engine decision

Test `pdf-access` with Windows Narrator and keyboard-only navigation. Record
whether a user can reach page controls and whether the document text itself
is read or selectable. Compare the tagged PDF with `pdf-scan` so an image-only
source is not mistaken for a renderer defect. `Windows.Data.Pdf` is a
page-render API; do not infer a document text layer from a page bitmap.

If PDF document text access, selection, or search is required for the MVP,
evaluate a text-capable alternative before S10. Candidates include the
WebView2 PDF viewer and a licensed PDF engine, but neither is accepted until
P0 proves offline use, controllable per-page position, accessibility, and
redistribution terms. Record the decision, including any stated limitation
if native page rendering is retained.

Measure first-page time, page-turn latency, peak cache bytes, memory after
repeated forward/backward turns, and zoom cancellation behavior on
`pdf-long`. A pass keeps rendered-image cache at or below its configured
budget and restores the correct page without a stale frame after rapid input.
Record hardware and source PDF size with all timings.

**T02.4 done when:** page/zoom/restore tests have results, Narrator behavior is
recorded, and a PDF engine decision or explicit blocker is written before S10.

## 10. Evidence format and P0 exit

Store `docs/p0/results.md` and `docs/p0/reader-decisions.md` with links to
build artifacts and the fixture manifest. Each experiment entry includes:

```text
task ID; fixture ID and SHA-256; app commit and package version;
OS build and CPU; SDK/runtime versions; machine RAM and display scale;
steps performed; expected and observed result; timings and memory;
network or accessibility observations; pass/fail; follow-up task.
```

The decision record states the chosen TXT view, HTML content mode, PDF engine,
package dependency route, reasons, rejected alternatives, and P1 backlog
changes. Keep raw diagnostic traces as build artifacts where practical; the
Markdown summary is the durable reviewable result.

P0 is complete only when:

1. S01 has a reproducible Windows build and CI test result, plus an installed
   development MSIX launch on Windows 11 x64.
2. S02 has fixture-linked results for TXT, HTML, and PDF, including HTML
   request isolation and PDF text-accessibility observations.
3. Failed decision thresholds have an explicit alternate design or block the
   affected P1 story. No untested Windows 10 or ARM64 target is described as
   validated.
4. `TR01.1`, `TR01.2`, `TR02.1`, `TR02.2`, and `TR02.3` have traceable evidence.

## Primary technical references

- [Microsoft: single-project MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)
  and [framework-dependent packaged deployment](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps)
- [Microsoft: Windows Runtime APIs in desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
- [Microsoft: WebView2 local content strategies](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content),
  [security guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security),
  and [runtime distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [Microsoft: WebView2 virtual-host mapping](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.setvirtualhostnametofoldermapping)
  and [`IsScriptEnabled` behavior](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings.isscriptenabled)
- [Microsoft: PDF document](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfdocument)
  and [page rendering](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage)
- [Microsoft: code-page encoding provider](https://learn.microsoft.com/en-us/dotnet/api/system.text.codepagesencodingprovider)
