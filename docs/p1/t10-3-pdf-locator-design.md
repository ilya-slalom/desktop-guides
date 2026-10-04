# T10.3 PDF locator and restore design

Status: designed; not yet implemented.
Prerequisites: T10.1 is merged (PR #36, merge commit `e2b9b8c`); T12.1's
locator codec is merged (PR #3).

## Intent

T10.1 records a PDF position as a page at its top edge and restores only the
page. A re-render after a resize scrolls the preview back to the top, so a
reader halfway down a tall page loses their place. T10.3 completes the PDF
locator: it captures how far down the page the reader is, keeps that point
across resizes and re-renders, and restores a saved page and point safely
when the saved value is out of range, malformed or from changed bytes.

Success means:

- `pdf-long`, page 121, scrolled about 30% down the page: after resizing to
  narrow, wide and back, the page status and page text still show page 121
  and the preview's page fraction is within 0.1 of where it was.
- A page turn after scrolling shows the next page from its top.
- Core tests show each restore case: exact; page out of range; changed
  bytes; malformed, wrong-format and future-version values. None throws or
  changes completion.

Traces: the T10.3 row of [implementation-plan.md](implementation-plan.md)
(TR10.1, TR10.3), [work-breakdown.md](../work-breakdown.md) T10.3 and the S10
acceptance line "Resume returns to the saved page and approximate vertical
point after resizing", and [p1-technical-design.md](../p1-technical-design.md)
§3 (locator envelope) and §6 S10 T10.3. In the work breakdown TR10.3 is the
UI Automation text requirement; T10.3 touches it only by checking that the
text pane names the same page after a resize.

Decisions made during brainstorming:

- **In-session only.** As in T08.3 for TXT, T10.3 captures and restores
  within an open guide. Saving the locator to `ReadingStates` and restoring
  it when a guide reopens is T12.2's `ProgressCoordinator`, for all three
  formats at once. A PDF still reopens at page 1 until T12.2.
- **A Core position tracker.** The within-page rules (which fraction a newly
  shown page gets, what a resize re-applies, which scroll events move the
  saved point) live in a pure Core class with unit tests. Holding the anchor
  in the view, as T08.3 did, was rejected because Production has no unit test
  host; holding it in the session was rejected for the same reason and
  because the session is already large.
- **Zoom stays in T10.2.** The technical design says "after resize/zoom";
  there is no zoom until T10.2, which must keep this tracker's rules.

## The within-page fraction

The preview is an `Image` with `Stretch="Uniform"` inside a vertical
`ScrollViewer`, so the image fills the viewport width and its height is the
page's height at that width. The page fraction is the share of the page
above the top of the viewport:

```
fraction = verticalOffset / imageHeight          (0 = top of the page)
offset   = clamp(fraction * imageHeight, 0, max(0, imageHeight - viewportHeight))
```

A fraction survives a width change because both the offset and the image
height scale with the width. Near the bottom of a page the offset is clamped
to the last scrollable point; the stored fraction is not reduced by that
clamp, so growing the window again returns to the original point.

## Core: `DesktopGuides.Core/Pdf/`

### `PdfLocationRules` (changed)

- `Capture(contentSha256, pageIndex, pageFraction, pageCount)` writes
  `PdfPosition(pageIndex, pageFraction)` and an estimate of
  `(pageIndex + pageFraction) / pageCount`, clamped to [0, 1], which is the
  T12.3 formula. It throws on a fraction that is non-finite or outside
  [0, 1], as it already does on a bad page.
- `Restore(location, contentSha256, pageCount)` returns
  `PdfRestore(int PageIndex, double PageFraction, RestoreOutcome Outcome)`:

| Saved location | Page | Fraction | Outcome |
| --- | --- | --- | --- |
| PDF, current version, same bytes, page in range | saved | saved | `Exact` |
| Same bytes, page out of range | nearest valid | 0 | `Approximate`, `ClampedReason` |
| Different bytes (`ContentSha256` differs) | saved, clamped to range | saved, or 0 if clamped | `Approximate`, `ChangedReason` |
| Wrong format or payload, other version, fraction non-finite or outside [0, 1] | 0 | 0 | `Unavailable`, `UnavailableReason` |

- `ChangedReason` is "The guide changed, so this is an approximate
  position.", the same words as `TextLocator.ApproximateReason`.
  `ClampedReason` and `UnavailableReason` keep their T10.1 text.
- The hash comparison stays case-insensitive: the codec stores lowercase
  hex and `Guide.ContentSha256` is uppercase.
- No restore result writes or reads completion.

### `PdfPagePosition` (new)

A small mutable tracker the session owns for one open document. It is not
thread-safe; the session uses it on the UI thread only.

- `Target(int page, double fraction)`: the session is about to request
  `page`. A page turn passes 0; a restore passes the restored fraction.
- `Shown(int page)`: the session has shown `page`. If `page` is the pending
  target, the anchor becomes the target's fraction and the target is
  consumed. If `page` is the page already shown (a re-render after a resize
  or DPI change), the anchor is kept. Otherwise the anchor becomes 0.
- `OffsetFor(double imageHeight, double viewportHeight)`: the offset to
  scroll to, by the formula above. It records the offset as the last
  applied offset. It returns 0 if either height is non-finite or not
  positive.
- `Scrolled(double offset, double imageHeight, double viewportHeight)`:
  a final (non-intermediate) scroll stopped at `offset`. It returns `true`
  and moves the anchor to `offset / imageHeight` only when that is a user
  scroll:
  - an offset within 1 effective pixel of the last applied offset is the
    echo of the app's own scroll and is ignored;
  - a page with nothing to scroll (`imageHeight <= viewportHeight`) or a
    non-finite or non-positive height is ignored.
- `Page` and `Fraction`: the shown page and its anchor, for `Capture`.

## Production

### `PdfReaderView`

- `ShowPage` no longer scrolls to the top. After the new image is laid out,
  the view raises `PreviewLayoutChanged` with the image height and viewport
  height. It also raises it when either size changes (the image's and the
  scroller's `SizeChanged`).
- `ScrollTo(double offset)` calls `ChangeView(null, offset, null,
  disableAnimation: true)`.
- `PreviewScrolled` is raised on each final `ViewChanged` with the vertical
  offset, image height and viewport height.
- The scroller gets `AutomationProperties.AutomationId="PdfPreviewScroller"`
  so the smoke can read its `ScrollPattern`. No other UI change.

### `PdfReaderSession`

- Page turns and page-edge commands call `Target(page, 0)` before
  requesting the page; `RestoreLocationAsync` calls
  `Target(restore.PageIndex, restore.PageFraction)`.
- `Apply` calls `Shown(page)` before `View.ShowPage`. On
  `PreviewLayoutChanged` the session calls `View.ScrollTo(OffsetFor(...))`.
- On `PreviewScrolled`, if `Scrolled(...)` returns `true`, the session raises
  `LocationChanged`. It still raises it after each page is shown.
- `GetLocationAsync` captures `Page` and `Fraction`.
- T10.1's ordering rules are unchanged: all of this runs on the UI thread
  and stops when `DisposeAsync` starts.

## Logging and untrusted input

A locator is untrusted. Decoding stays in `ReaderLocationCodec` (bounded to
4 KiB, depth 8, duplicate fields rejected); `PdfLocationRules` range-checks
the page against the open document and the fraction against [0, 1]. No
locator content is logged.

## Testing

Core unit tests (`DesktopGuides.Core.Tests`):

- `PdfLocationRules`: a capture-restore round trip keeps page and fraction;
  the estimate is `(page + fraction) / count` and stays within [0, 1];
  `Capture` rejects a fraction below 0, above 1, NaN and infinity; every row
  of the restore table, including a hash that differs only in case being
  `Exact`; a decoded `ContentChanged` locator restores as `Approximate`
  through `ReaderLocationCodec.Deserialize`.
- `PdfPagePosition`: a page turn shows the top; a restore target applies its
  fraction once; a re-render keeps the anchor; a width change re-applies the
  same fraction at the new height; the bottom clamp doesn't reduce the
  stored fraction; the echo of an applied offset is ignored; a user scroll
  moves the anchor and returns `true`; zero, negative and non-finite heights
  are ignored.

Installed smoke (`tools/p1/windows_shell_ui_smoke.ps1`), new `pdf-resize`
phase on `pdf-long`, run in the light and dark passes after `pdf-long`:

1. At 1500×720, go to page 121 (`Go to start`, then 120 rapid Next clicks
   with the existing `Invoke-NextPages`) and wait for "page 121 of 200".
2. Read the preview's `ScrollPattern` and scroll to page fraction 0.3:
   `percent = 0.3 / (1 - viewSize / 100) * 100`. Fail clearly if that is
   over 100, since the fixture would then be too short to test.
3. Resize to 600×720, then 1500×720, then 1100×720. After each, poll for up
   to 5 seconds until the page fraction
   `scrollPercent / 100 * (1 - viewSize / 100)` is within 0.1 of 0.3, failing
   with the last value read; then check that `PdfPageStatus` is "Page 121 of
   200" and the text box holds page 121's text.
4. Press Next and check that page 122 is shown at a fraction below 0.02.
5. Record the measured fractions in the report as `pdfPosition`.

RED is this phase failing on T10.1's code (the re-render scrolls to the
top); the view and session changes make it GREEN.

## Docs

On completion, update this file's status, implementation notes and
verification; the T10.3 row note in
[implementation-plan.md](implementation-plan.md);
[work-breakdown.md](../work-breakdown.md); and §6 S10 of
[p1-technical-design.md](../p1-technical-design.md) if the shipped shape
differs from it.

## Out of scope

- Saving the locator and restoring it when a guide reopens, the debounce and
  flush rules, and showing restore reasons in the shell (T12.2). Until then
  `RestoreLocationAsync` has no shell caller and is covered by Core tests.
- Fit-width and zoom, and keeping the point across a zoom change (T10.2).
- The progress percentage shown to users and its approximate label (T12.3).
- OCR for `pdf-scan`.
