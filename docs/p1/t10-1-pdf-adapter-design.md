# T10.1 PDF reader adapter design

Status: implemented in PR #36; CI run 37169058467 passed the installed
`pdf-reader` scenario in light and dark.
Prerequisites: T06.3 is merged (PR #19, merge commit `494cb02`); T10.0's
[decision](pdf-decision.md) selected the native hybrid; T11.2's reader
contract is merged.

## Intent

PDF guides still show the Reader placeholder ("PDF keeps the placeholder
until T10"). T10.1 replaces it with the engine T10.0 selected: a
`Windows.Data.Pdf` page preview beside the same page's text, extracted by
pinned `PdfPig` 0.1.16 into a read-only WinUI control with a UI Automation
`TextPattern`. The adapter reads only the committed managed copy, works
offline, and keeps memory bounded on long documents and under rapid page
turns (TR10.1–TR10.3).

Success means the installed smoke shows, on CI:

- `pdf-access`: the tagged paragraph is in the text control's `TextPattern`.
- `pdf-scan`: the page says "Image-only page; OCR is unavailable" and claims
  no text.
- `pdf-long`: after 199 rapid Next clicks the preview, the text and the page
  status all agree on page 200; across three full sweeps the measured
  rendered-image cache never exceeds 96 MiB and never holds every page.
- Damaged and missing managed copies show typed errors, and a TXT guide
  still opens afterwards.

Traces: the T10.1 row of [implementation-plan.md](implementation-plan.md)
(TR10.2, TR10.3; TR10.1 for page range checks),
[work-breakdown.md](../work-breakdown.md) S10,
[p1-technical-design.md](../p1-technical-design.md) §6 S10, and the
"Decision limits and next checks" of [pdf-decision.md](pdf-decision.md).

Decisions made during brainstorming:

- **No passwords in T10.1.** T06.2 import rejects password-protected PDFs,
  so none can reach the Reader. A managed copy that is encrypted anyway
  shows a typed error. Password sessions and retry belong to T10.2, which
  would also have to relax the import rule. The decision doc's password
  rules (session-only, cleared per attempt, never persisted) carry over to
  that task unchanged.
- **Side-by-side layout.** Preview on the left, page text on the right, as
  T10.0 validated. Narrow windows stack the text below the preview.
- **Split by testability.** Cache, raster budget and scheduling rules are
  pure Core code; managed-copy checks and text extraction are
  Infrastructure; only `Windows.Data.Pdf` and XAML live in Production,
  which has no unit test host. Porting the probe into one Production class
  was rejected because the cap and stale-render rules would then be tested
  only by the installed smoke. Extracting text at import was rejected: it
  changes import, stores guide text twice and can go stale.
- **Changed-byte detection is not T10.1's.** The adapter does not re-hash the
  managed copy on open. It uses the stored `ContentSha256` as the cache-key
  fingerprint and the locator fingerprint. Comparing bytes and returning
  `Approximate` on a change is T10.3 (and T12.3 for the estimate).

## Core: `DesktopGuides.Core/Pdf/`

### `PdfRenderCache<TImage>`

A least-recently-used cache of rendered page images.

- Key: `PdfRenderKey(string Fingerprint, int PageIndex, int PixelWidth)`.
- Each entry records **measured** bytes, `PixelWidth × PixelHeight × 4`,
  from the decoded image's pixel size, not from the requested size.
- `MaxBytes` is 96 MiB (100,663,296 bytes), passed in so tests can use a
  small cap.
- `Add` evicts least-recently-used entries until the total fits. The entry
  just added always stays, even if it alone exceeds what remains; an entry
  larger than `MaxBytes` is never offered (the raster budget prevents it).
- `TryGet` moves a hit to most recent.
- `CachedBytes`, `PeakBytes` and `Count` are exposed. `PeakBytes` is the
  highest `CachedBytes` after eviction completes, so it never exceeds
  `MaxBytes`.
- `Clear()` drops every entry; `CachedBytes` and `Count` become 0.
- The type is generic so tests use a fake image; Production uses
  `BitmapImage`. It is used from the UI thread only and is not thread-safe.

### `PdfRasterBudget`

`PdfRasterBudget.WidthFor(double displayPixels, double aspectRatio, long
maxBytes)` returns a `PdfRasterWidth` result: either a pixel width or
`PageTooLarge`.

- Non-finite or non-positive inputs give `PageTooLarge`.
- The width is rounded up to a multiple of 64, so small resizes reuse cached
  images, then clamped to 64–4096.
- While `width × ceil(width × aspectRatio) × 4 > maxBytes`, the width is
  halved, which may take it below 64. If it falls below 1, the result is
  `PageTooLarge`.

### `LatestWinsScheduler`

Runs page loads one at a time and drops superseded requests.

- `Request(int pageIndex)` records the newest wanted page. If no load is
  running, it starts one; otherwise the request waits, replacing any earlier
  waiting request.
- When a load finishes, its result is applied only if no newer request
  arrived while it ran; otherwise it is reported stale and the newest
  waiting request starts.
- A load that throws is reported to the caller and does not stop the next
  request.
- `CancelAsync()` cancels the running load, drops the waiting one, and
  completes when the running load has returned.
- Counters: `Requests`, `Loads`, `StaleResults`.

Fifty rapid requests therefore cause at most two loads: the one running and
the newest.

### `PdfGuideLoadError` and `PdfGuideLoadMessages`

Same shape as `HtmlGuideLoadMessages`: `For(error)`, `ActionFor(error)`,
`ActionLabel(action)`. New enum members go at the end.

| Error | Cause | Message | Action |
| --- | --- | --- | --- |
| `Missing` | Managed file or guide folder absent | "This guide's file is missing from the library." | None |
| `Changed` | The path is a link, a folder, or resolves outside the guide root | "This guide's files have changed. Re-import it to read it." | None |
| `Unreadable` | IO or access error while opening | "This guide's file can't be opened. Close any app that's using it, then open the guide again." | Reopen |
| `Damaged` | Either engine can't parse it, it has zero pages, or the engines' page counts differ | "This PDF is damaged, so it can't be opened. Re-import it from the original file." | None |
| `PasswordProtected` | The managed copy is encrypted | "This PDF's protection isn't supported. Remove the password and re-import it." | None |
| `Failed` | The open session stopped working | "This guide stopped responding." | Reopen |

`Missing` and `Unreadable` use the TXT wording; a Core test pins `Missing`.

## Infrastructure: `DesktopGuides.Infrastructure/Reading/`

### `ManagedPdfGuideLoader`

`ManagedPdfGuideLoader(ManagedPathResolver paths)` with
`LoadAsync(Guide guide, CancellationToken token)` returning
`PdfGuideLoaded(string FilePath, PdfPageTextSource Text)` or
`PdfGuideLoadFailed(PdfGuideLoadError Error)`.

- The planned path is checked first, like the HTML loader: an absent file
  (and absent guide folder) is `Missing`.
- `ResolveExistingGuideFile` then gives the managed path. Links, folders,
  and anything outside the root are `Changed`; IO and access errors are
  `Unreadable`.
- A read-only `FileStream` (`FileShare.Read`) is opened on that path and
  handed to PdfPig through the import's `CancellableReadStream`.
  `PdfDocumentEncryptedException` is `PasswordProtected`; any other parse
  exception, or zero pages, is `Damaged`.
- On any failure the stream is disposed before returning.
- It never opens the guide's original source.

### `PdfPageTextSource`

Owns the PdfPig document and its stream; `IDisposable`.

- `PageCount`.
- `GetPageTextAsync(int pageIndex, CancellationToken token)` returns
  `PdfPageText(string Text, bool HasLetters, bool Truncated)`. It throws
  `ArgumentOutOfRangeException` for an index outside `[0, PageCount)`.
- Extraction uses `ContentOrderTextExtractor` on a worker thread. Access to
  the document is serialized with a `SemaphoreSlim`, because PdfPig is not
  thread-safe.
- A parse failure on one page throws `PdfPageTextException`; the session
  shows it as a per-page failure.
- Text longer than `MaxPageCharacters` (default 1,048,576) is cut at that
  length and marked `Truncated`.
- Recently extracted pages are kept in a small LRU: at most `MaxPages`
  (default 8) and `MaxCharacters` (default 4,194,304) in total. The limits
  are constructor parameters so tests can use small values.
- `PeakPages` and `PeakCharacters` are exposed for the diagnostics.
- `Dispose` waits for a running extraction, then disposes the document and
  the stream. Later calls throw `ObjectDisposedException`.

## Production

### `PdfReaderSession : IReaderSession`

Built by the shell from a `PdfGuideLoaded`.

- `Format` is `Pdf`. `Capabilities` is `PageNavigation` and never changes,
  so the existing toolbar shows Previous, Next, Start and End. Page entry,
  fit-width and zoom are T10.2.
- **Open.** `OpenAsync` opens a second read-only `FileStream` on
  `PdfGuideLoaded.FilePath` and loads `Windows.Data.Pdf.PdfDocument` from
  `AsRandomAccessStream()`. The path is not resolved again and no
  `StorageFile` is used. A load failure is `Damaged` (or `PasswordProtected`
  when the engine reports a password). Page counts that differ from
  `PdfPageTextSource.PageCount` are `Damaged`. Errors surface as
  `PdfGuideLoadException(PdfGuideLoadError)`. It then shows page 1.
- **Showing a page.** Every navigation calls `scheduler.Request(index)`. A
  load:
  1. Starts text extraction and the raster render together.
  2. Raster: width from `PdfRasterBudget` using the preview column's
     `ActualWidth × XamlRoot.RasterizationScale` and the page's aspect
     ratio. A cache hit is reused. A miss renders with
     `PdfPageRenderOptions.DestinationWidth` into an
     `InMemoryRandomAccessStream`, decodes a `BitmapImage`, and adds it with
     its measured bytes. The `PdfPage` is disposed after rendering.
  3. Applies both results to the view together, and only if the scheduler
     says the load is current, so the text of one page never sits beside
     the preview of another.
  4. Raises `LocationChanged`.
- **Resize.** A size change is debounced by 150 ms and re-renders the
  current page only if the raster width changes.
- **Location.** `GetLocationAsync` returns
  `ReaderLocation(Pdf, CurrentVersion, ContentSha256,
  PdfPosition(pageIndex, 0), (pageIndex + 1.0) / PageCount)`.
  `RestoreLocationAsync` decodes through `ReaderLocationCodec`, clamps the
  page index to `[0, PageCount - 1]`, shows it, and returns `Exact` when
  the index was in range or `Approximate` when it was clamped. An
  undecodable location returns `Unavailable` and leaves the page alone.
  Fraction restore is T10.3.
- **Failures while open.** A per-page text failure or raster failure (or
  `PageTooLarge`) shows on that page only (see the view). Three consecutive
  pages that fail both ways raise `Failed`, which the shell handles like
  HTML's `OnReaderSessionFailed`: it disposes the session and shows the
  error with Reopen.
- **Dispose** (idempotent): cancel the scheduler and await it, cancel the
  resize debounce, clear the image source and the cache, dispose the text
  source and both streams, then write test diagnostics if the gate is open.

### `PdfReaderView`

A `UserControl`:

- Left: a `ScrollViewer` holding an `Image` named "Page 5 of 200 preview",
  and a preview status line (`PdfPreviewStatus`) that is empty unless the
  preview failed: "This page's preview couldn't be shown."
- Right: a text status line (`PdfTextStatus`) and a read-only, wrapping
  `TextBox` (`PdfDocumentText`, named "Page text, page 5 of 200") for UIA
  `TextPattern` and keyboard selection. The status is empty for normal
  text, "Image-only page; OCR is unavailable" when the page has no letters,
  "This page's text couldn't be read." on failure, and "Page text is
  shortened; it's too long to show in full." when truncated.
- A page status (`PdfPageStatus`): "Page 5 of 200".
- Below 720 effective pixels wide, a visual state moves the text under the
  preview.
- A page turn keeps keyboard focus where it is.

### `ShellWindow.PdfReader.cs`

`OpenPdfGuideAsync(guide, generation)`, mirroring `OpenHtmlGuideAsync`:
reader surface on, `readerLoad` token, loader, generation check, error
surface for `PdfGuideLoadFailed`, session build and `OpenAsync`, error
surface and dispose for `PdfGuideLoadException`, then
`ReaderActions.SetSession` and "Guide ready." A newer render disposes a
half-open session. `RenderCurrentAsync` routes `GuideFormat.Pdf` here in
place of the placeholder branch.

Errors use the existing `ReaderLoadError` and `ReaderLoadErrorAction`
surface and the warning status. Reopen re-runs the current route.

### Test diagnostics

The gate is the named event
`Local\DesktopGuides.Preview.PdfDiagnostics.<pid>`, checked with
`TestGate.IsOpen`. With the gate open, dispose writes
`<cacheRoot>\diagnostics\pdf-<guideId>.json` with counts only:
`requests`, `loads`, `staleResults`, `peakCacheBytes`, `maxCacheBytes`,
`cachedPagesAtClose`, `peakTextPages`, `peakTextCharacters`,
`disposedCleanly`, `evictions`. No guide text and no paths. A write failure never
affects closing the guide.

## Logging and untrusted input

PDF bytes are untrusted. Parse exceptions are caught by type and mapped to
errors; PdfPig throws several exception types for malformed files, as the
import code notes. Messages, status lines and diagnostics never contain
guide text, passwords or absolute paths.

## Testing

### Core xUnit

- `PdfRenderCache` (fake image, small caps):
  - Evicts least-recently-used entries once over the cap.
  - An entry bigger than what remains evicts others and stays.
  - `PeakBytes` never exceeds the cap.
  - A hit becomes most recent and survives the next eviction.
  - Keys that differ only by width or fingerprint don't collide.
  - `Clear` leaves 0 bytes and 0 entries.
  - A simulated 200-page, three-sweep session at a 96 MiB cap with
    page-sized images stays within the cap and never holds 200 entries.
- `PdfRasterBudget`: rounding to 64, clamping to 64–4096, halving for tall
  pages, `PageTooLarge` for extreme ratios, NaN, zero and negative inputs.
- `LatestWinsScheduler`:
  - Fifty rapid requests run at most two loads and apply the newest.
  - Superseded results are counted stale and not applied.
  - `CancelAsync` waits for the running load and stops further loads.
  - A throwing load is reported and the next request still runs.
- `PdfGuideLoadMessages`: every error has a message and an action;
  `ActionLabel(None)` throws; `Missing` matches the TXT copy.

### Infrastructure xUnit

Published through the existing `PublisherHarness` from the P0 fixtures.
Malformed inputs are built in the test.

- Loader:
  - A published `pdf-access` loads with one page.
  - A deleted file and a deleted guide folder are `Missing`.
  - A folder in place of the file is `Changed`.
  - A junction for the guide folder is `Changed` (Windows only).
  - Truncated and garbage managed bytes are `Damaged`.
  - The managed copy overwritten with `pdf-locked` is `PasswordProtected`.
  - Loading works after the original source file is deleted.
- `PdfPageTextSource`:
  - `pdf-access` page 1 contains the tagged paragraph.
  - `pdf-scan` page 1 has no letters.
  - `pdf-long` page index 199 is "Desktop Guides P0 - page 200 of 200".
  - Indexes -1 and `PageCount` throw `ArgumentOutOfRangeException`.
  - After all 200 pages, at most `MaxPages` pages and `MaxCharacters`
    characters are retained (checked through `PeakPages` and
    `PeakCharacters`).
  - A small `MaxPageCharacters` truncates and sets `Truncated`.
  - `Dispose` during an extraction waits for it; later calls throw
    `ObjectDisposedException`.
  - A cancelled token stops an extraction with `OperationCanceledException`.

### Installed smoke (`production-shell-ui`)

A new `seed-pdf-reader <app-data-root> <fixtures-root>` ShellSeed mode
publishes, through the real publisher, a PDF Reader Game with
`pdf-access`, `pdf-scan`, `pdf-long`, a damaged guide (published from
`pdf-short`, then the managed copy truncated), a missing guide (managed
copy deleted) and a TXT guide.

A new `pdf-reader` scenario runs with the diagnostics gate open:

1. `pdf-access`: `PdfDocumentText`'s `TextPattern.DocumentRange` contains
   the tagged paragraph; `PdfTextStatus` is empty; the preview is named
   "Page 1 of 1 preview" and has a non-empty bounding rectangle.
2. `pdf-scan`: `PdfTextStatus` is "Image-only page; OCR is unavailable" and
   the text is empty.
3. `pdf-long`: 199 Next invocations without waiting, then
   `PdfPageStatus` is "Page 200 of 200", the preview is named
   "Page 200 of 200 preview", and the text contains
   "page 200 of 200". Start and End reach pages 1 and 200 with matching
   text. A second full sweep is rapid; the third invokes Next once per
   page and waits for every page to load, so the 200 previews exceed the
   cap. After closing the guide, the diagnostics file shows `evictions > 0`,
   `peakCacheBytes <= 100663296`,
   `cachedPagesAtClose < 200`, `staleResults > 0` or `loads < requests`,
   `peakTextPages <= 8`, and `disposedCleanly` true.
4. Damaged: the `Damaged` message with no action. Missing: the `Missing`
   message with no action.
5. Back to the game, the TXT guide opens and shows its rows.

Process working set isn't app-controlled and isn't asserted. Narrator
(T16.3) and a physical offline relaunch (T17.3) are out of scope; T10.2's
`pdf-offline` mode checks that no remote connection is made. The PR
includes light and dark screenshots of the side-by-side Reader and the PDF
error surface.

## Docs

- `docs/p1/implementation-plan.md`: change "T09.1 is in review in PR #35" to
  merged through PR #35 on 3 October 2026 (merge commit `9f2ad26`;
  final-HEAD CI run 37127070994), and add a T10.1 paragraph.
- `docs/work-breakdown.md`: add the T10.1 PR pointer.
- Evidence under `docs/p1/evidence/t10-1-pdf-adapter/`: the smoke's PDF
  diagnostics and the screenshots.

## Out of scope

- Passwords, page entry, page count control, fit-width, zoom and keyboard
  parity (T10.2). Done in T10.2.
- Within-page fraction restore and changed-byte detection (T10.3, T12.3).
- Narrator verification (T16.3) and a physical offline relaunch (T17.3).
- OCR for scanned pages (P2).
- Prefetching neighboring pages.
- The T10.0 probes under `src/DesktopGuides.App/Probes/` stay as they are.

## Implementation notes

Planning rulings, from the
[implementation plan](t10-1-pdf-adapter-plan.md):

- **R1.** `PdfGuideLoadMessages.ActionFor` returns the existing
  `HtmlGuideLoadAction`, so the Reader error surface and its Reopen button
  are shared rather than duplicated.
- **R2.** `PdfLocationRules` validates the decoded locator itself; the
  session holds no codec logic.
- **R3.** `CancellableReadStream` has a settable `Token`, so each text
  extraction can be cancelled through the import's stream wrapper.
- **R4.** The loader reuses the import's `%PDF` header check.
- **R5.** Diagnostics are a Core record (`PdfSessionDiagnostics`) with a
  unit-tested JSON shape.
- **R6.** The narrow layout is switched in code-behind, not with
  `VisualStateManager`; no XAML in the repo uses visual states yet.
- **R7.** Production has no unit test host, so its RED step is the
  installed smoke (Task 7), made GREEN by the view and the session
  (Task 8).

Execution notes:

- The session is `PdfReaderSession`, not the `PdfReaderAdapter` named in
  the P1 technical design; it implements `IReaderSession` like the TXT and
  HTML sessions.
- RED was observed in CI, as for T09.1. No local .NET toolchain was used.
- `Apply` never throws. The scheduler stalls if its apply callback throws,
  so an exception from `ShowPage` counts as a failed page.
- On a second open, `PdfReaderSession.OpenAsync` maps a file that vanished
  between load and open to `Missing`, and other IO and access errors to
  `Unreadable`. This is in addition to the spec's `Damaged` and
  `PasswordProtected`.
- Task 6 ruling. An IO or access error on the planned-path check maps to
  `Changed`, mirroring `ManagedHtmlGuideLoader`, although the error table
  says `Unreadable`.
- Known limit. Text extraction checks cancellation only when PdfPig reads
  the file, so closing a guide waits for a running page extraction to
  finish. A pathologically slow page delays Back until it does. T10.2
  bounds the close at 2 s and records an abandoned extraction.
- Known limit. Process memory on large publisher PDFs (100 MB to 1 GiB) was
  not measured. T10.1 bounds the render cache and page text, and PdfPig
  reads lazily from the stream. A large-file working-set measurement is
  moved to T17.3.
- The 96 MiB cap counts cached images only. The image on screen stays alive
  after it is evicted, so real image memory can briefly reach the cap plus
  one page raster.

## Verification

CI run 37169058467 passed `core-tests` and the installed `production-shell-ui`
`pdf-reader` scenario in light and dark. The `pdf-long` diagnostics after
three sweeps, the third waiting on every page:

| Pass | `requests` | `loads` | `staleResults` | `evictions` | `peakCacheBytes` (cap 100663296) | `cachedPagesAtClose` | `peakTextPages` | `disposedCleanly` |
|---|---|---|---|---|---|---|---|---|
| [Light](evidence/t10-1-pdf-adapter/pdf-reader-light.pdf-long.json) | 602 | 294 | 76 | 118 | 100620800 | 121 | 8 | true |
| [Dark](evidence/t10-1-pdf-adapter/pdf-reader-dark.pdf-long.json) | 602 | 281 | 69 | 112 | 100602880 | 121 | 8 | true |

The cache reached its cap and evicted, so the bound was exercised in the
real app. The installed PDF smoke ran on x64 only; ARM64 PDF behaviour is
untested.

Screenshots:

- [Side by side, light](evidence/t10-1-pdf-adapter/pdf-reader-light.png)
- [Side by side, dark](evidence/t10-1-pdf-adapter/pdf-reader-dark.png)
- [Narrow, light](evidence/t10-1-pdf-adapter/pdf-reader-narrow-light.png)
- [Narrow, dark](evidence/t10-1-pdf-adapter/pdf-reader-narrow-dark.png)
- [Damaged, light](evidence/t10-1-pdf-adapter/pdf-error-light.png)
- [Damaged, dark](evidence/t10-1-pdf-adapter/pdf-error-dark.png)
