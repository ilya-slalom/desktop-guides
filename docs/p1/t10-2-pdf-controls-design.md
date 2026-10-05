# T10.2 PDF page controls, zoom and passwords design

Status: designed; not implemented.
Prerequisites: T10.1 (the PDF adapter, PR #36, merge commit `e2b9b8c`) and
T10.3 (the PDF locator, PR #37, merge commit `541c245`) are merged. T06.2's
import validation is merged with T06.1 (PR #17).

## Intent

The PDF Reader can turn pages, but it can't jump to a page, zoom, or be
driven from the keyboard, and a password-protected PDF can't be imported at
all. T10.2 adds those controls and lets a locked PDF in through import and
the Reader, without ever storing its password.

Success means:

- "Page N of M" is visible. *Go to page* accepts only 1 to M, and an
  out-of-range page never moves the Reader or crashes it (TR10.1).
- *Fit to width*, *Zoom in* and *Zoom out* work on short and long PDFs, and
  the reading point (page and fraction, T10.3) survives every zoom change.
- Every PDF command has a visible key, shown in its tooltip. A page turn,
  zoom or fit never moves keyboard focus, so Narrator stays where it was.
- A password-protected PDF imports after the right password, and opens in
  the Reader after the right password. A wrong password can be retried. The
  attempted password is cleared after each attempt and is never written to
  the database, files, logs or exception text.
- With the originals removed and after a relaunch, PDFs open from the
  managed copy with no non-loopback TCP connection (TR10.2).

Traces: the T10.2 row of [implementation-plan.md](implementation-plan.md)
(TR10.1–TR10.3), [work-breakdown.md](../work-breakdown.md) S10 T10.2, and
[p1-technical-design.md](../p1-technical-design.md) §8 S10 and the T06.2
password rule.

Decisions made during brainstorming:

- **Passwords at import and in the Reader.** Import asks for the password in
  the preview, checks the PDF with it, and forgets it. The Reader asks each
  time the guide opens. Keeping the import rejection and handling only a
  managed copy that became locked was rejected: almost no user would reach
  the prompt. Deferring passwords was rejected: it drops stated scope and
  delays T16.3's locked-PDF check.
- **PDF accelerators now.** T10.2 adds Reader-scoped keys to the toolbar
  commands the PDF session supports. TXT and HTML get the same keys in
  T16.1, which also documents them. Adding them to TXT now was rejected: it
  pulls T16.1 into this task.
- **Zoom lasts only while the guide is open.** Each open starts at *Fit
  width*. Nothing asks for zoom to persist, and T14.1 keeps PDF zoom out of
  the text preferences. A stored per-guide zoom was rejected: it needs a
  migration for a preference nobody asked for.
- **Zoom by an explicit page width, re-rendered.** The view sets the page
  image's width and re-renders through the existing raster path.
  `ScrollViewer.ZoomFactor` was rejected: it scales the extent the T10.3
  tracker reads, and its intermediate events fight the layout guard.
  Ctrl+wheel zoom is a follow-up (#45).
- **Offline proof without a firewall rule.** The installed run records the
  app's TCP connections while it reads PDFs. A physical disconnection stays
  in T17.3.
- **Bounded close in, large-file measurement out.** Closing waits at most
  2 s for a page-text extraction. The 100 MB–1 GiB working-set measurement
  T10.1 left open moves to T17.3.

## Core

### `PdfZoom`

`DesktopGuides.Core/Pdf/PdfZoom.cs` is pure and unit-tested.

```csharp
public readonly record struct PdfZoom(int? Percent)   // null = fit width
{
    public static PdfZoom Fit { get; }
    public static IReadOnlyList<int> Steps { get; }   // 50, 75, 100, 125, 150, 200, 300, 400
    public bool IsFit { get; }

    // fitPercent is the effective percent of fit width: viewport width over the page's natural width.
    public PdfZoom In(double fitPercent);    // next step above the effective percent
    public PdfZoom Out(double fitPercent);   // next step below it
    public bool CanZoomIn(double fitPercent);
    public bool CanZoomOut(double fitPercent);

    // The page image's layout width in DIPs.
    public double WidthFor(double viewportWidth, double naturalPageWidth);
    public string Label { get; }   // "Fit width" or "125%"
}
```

- The natural page width is the page's size in points × 96 ⁄ 72.
- From *Fit*, *Zoom in* picks the first step strictly above the effective
  percent, and *Zoom out* the last step strictly below it. With no such step,
  the command is unavailable.
- From a step, *Zoom in* and *Zoom out* move one step. At 50% and 400% the
  matching command is unavailable.
- The effective percent depends on the shown page. On `pdf-long`, whose pages
  have mixed sizes, a fixed percent therefore gives pages different widths,
  and *Fit* gives every page the viewport width.

### Page entry

`PdfLocationRules` gains `bool IsPageInRange(int pageNumber, int pageCount)`,
1-based. The dialog and the session both use it.

### Load and import copy

- `PdfGuideLoadError` gains `PasswordRequired` and `PasswordIncorrect` at the
  end. `PasswordProtected` stays, for an engine that can't open a locked
  file even with the password.
- `PdfGuideLoadMessages`:
  - `PasswordRequired`: "This PDF needs a password." It has no action; the
    unlock panel is the action.
  - `PasswordIncorrect`: "That password didn't open this PDF. Try again."
- `ImportPresentation` gains the same wrong-password text for the import
  step.

## Import

Following the TXT encoding step:

```csharp
public sealed record ImportNeedsPdfPassword(ImportSource Source, string SuggestedTitle, string Fingerprint)
    : ImportInspection;

public sealed record PdfImportManifest(
    ImportSource Source, string SuggestedTitle, int PageCount, bool HasText,
    string Fingerprint, bool PasswordRequired = false)
    : ImportManifest(Source, GuideFormat.Pdf, SuggestedTitle, Fingerprint);

// IGuideImportValidator
Task<PdfImportManifest> ResolvePdfPasswordAsync(
    ImportNeedsPdfPassword inspection, string password, CancellationToken token);
```

- **Detection.** `GuideImportValidator.ReadPdf` returns
  `ImportNeedsPdfPassword` on `PdfDocumentEncryptedException`, instead of
  throwing `ImportIssue.Encrypted`.
- **Checking the password.** `ResolvePdfPasswordAsync` re-checks the source's
  size and write time, then opens it with PdfPig `ParsingOptions.Password`
  and runs the same page and text checks.
  - A wrong password throws `GuideImportException(ImportIssue.PasswordIncorrect, …)`.
  - A changed file throws `Changed`.
  - The password is a parameter only. It isn't kept in a field, the
    manifest, or the exception.
- **Dialog.** `ImportGuideDialog` shows a `PasswordBox` and **Unlock** in
  place of the preview facts.
  - **Unlock** copies the password, clears the box, then calls the
    resolver.
  - A wrong password shows the message under the box, and focus stays in
    the box.
  - The resolved manifest shows the usual preview with "Password protected"
    among its facts. Importing needs no password.
- **Publisher.** For `PasswordRequired`, `VerifyPdf` checks that the staged
  copy starts like a PDF and still throws `PdfDocumentEncryptedException`
  without a password. The publisher already compares the staged fingerprint
  with the manifest (GuideImportPublisher.cs:107), so the bytes are the ones
  validated. Unlocked PDFs keep today's checks.
- **Issues.** `ImportIssue` gains `PasswordIncorrect` at the end.
  `Encrypted` is no longer raised but stays in the enum. Import doesn't
  open Windows.Data.Pdf, so a PDF that PdfPig opens with the password but
  Windows.Data.Pdf can't reaches the Reader, which reports
  `PasswordProtected`.
- **Storage.** `PasswordRequired` is not stored. The Reader detects a
  locked copy when it opens.

## Reader

### Session

- **Capabilities.** `PdfReaderSession.Capabilities` becomes
  `PageNavigation | PageJump | FitWidth | Zoom`.
- **`ExecuteAsync`.**
  - `PageJumpAction(n)` refuses an out-of-range page with
    `ArgumentOutOfRangeException` and doesn't move. In range, it shows that
    page at its top, like a page turn.
  - `FitWidthAction` sets `PdfZoom.Fit`.
  - `ZoomAction(f)` calls `In` when `f > 1` and `Out` when `f < 1`.
- **Zoom state.** `ZoomChanged` reports the label and whether each
  direction is available, so the toolbar can disable an end command.
- **Rendering a zoom.**
  1. The session captures the page and fraction from `PdfPagePosition`.
  2. It sets the view's page width.
  3. It re-renders through `PdfRasterBudget`, the cache and the
     scheduler, keyed by the new raster width, like the debounced resize.
  4. After the new extent has settled under the layout guard, it scrolls
     to the captured fraction.
  - A zoom doesn't count as reader movement, so it doesn't schedule a
    progress save.
- **Opening a locked PDF.** `OpenAsync(password: null)` reports
  `PasswordRequired` when PdfPig throws `PdfDocumentEncryptedException` or
  Windows.Data.Pdf returns 0x8007052B. `UnlockAsync(string password)` then
  opens both engines with the password:
  `PdfDocument.LoadFromStreamAsync(stream, password)`, and `PdfPageTextSource`
  with `ParsingOptions.Password`.
  - The password is held only in the session, for later text pages, and
    the session drops it on close.
  - A wrong password reports `PasswordIncorrect`.
  - If only one engine accepts the password, the open fails as
    `PasswordProtected`.
- **Bounded close.** Close cancels the scheduler and waits at most 2 s for a
  running PdfPig extraction. After that, the close completes and the
  extraction's result is discarded when it finishes. `PdfSessionDiagnostics`
  records an abandoned extraction.

### View

- **Zoom layout.** The preview `Image` gets an explicit `Width` from
  `PdfZoom.WidthFor`. The `ScrollViewer`'s horizontal scroll bar is `Auto`,
  so it shows only when the page is wider than the viewport.
- **Status.** `PdfPageStatus` reads "Page N of M · Fit width" or
  "Page N of M · 125%". It has `AutomationProperties.LiveSetting="Polite"`,
  so page and zoom changes are announced without moving focus.
- **Unlock panel.** `PdfUnlockPanel` replaces the preview and text while the
  password is needed. It has:
  - the guide title and "This PDF needs a password.";
  - a `PasswordBox` (`PdfPasswordInput`);
  - **Unlock** (`PdfUnlockButton`, the default for Enter);
  - **Back to library**;
  - an error line.

  When the panel appears, the box gets focus. **Unlock** copies the
  password, clears the box, and calls `UnlockAsync`. After a successful
  unlock, the saved place restores (T12.2) and the preview gets focus. The
  toolbar's page commands are hidden until the PDF opens, because the
  session's capabilities aren't published before then.

### Go to page

- `ReaderToolbar`'s *Go to page* dialog replaces the `TextBox` with a
  `NumberBox` (`Minimum=1`, `Maximum=PageCount`, no spin buttons).
- The toolbar gets the page count from a new `ReaderToolbar.PageCount`
  property, which the shell sets from the session.
- A blank, fractional or out-of-range entry keeps the dialog open with
  "Enter a page from 1 to M." **Go** stays available, so the message
  explains the refusal.
- After a jump started from the keyboard (Ctrl+G), focus goes to the
  preview. After a jump started from the overflow, it goes back to the
  invoking command, as today.

### Keyboard

`ReaderToolbar` adds `KeyboardAccelerator`s to its commands. Their scope
owner is the Reader route, and the tooltips show the key.

| Key | Command | Capability |
| --- | --- | --- |
| Page Up / Page Down | Previous / Next page | `PageNavigation` |
| Ctrl+Home / Ctrl+End | Go to start / Go to end | `PageNavigation` |
| Ctrl+G | Go to page | `PageJump` |
| Ctrl+Plus, Ctrl+= / Ctrl+Minus | Zoom in / Zoom out | `Zoom` |
| Ctrl+0 | Fit to width | `FitWidth` |

- A key runs only when its command is visible and enabled. TXT's toolbar
  shows the page commands, but their keys stay off outside PDF until T16.1:
  the shell enables accelerators only for a PDF session.
- While a `TextBox` (including the read-only `PdfDocumentText`), the
  `NumberBox` or the `PasswordBox` has focus, Page Up/Down and
  Ctrl+Home/End stay with that control. Ctrl+G and the zoom keys still
  work.
- Keys never move focus. Ctrl+G opens the dialog, and after it closes,
  focus goes to the preview.

## Untrusted input

- PDF bytes come from the managed copy only; the original is never reopened
  after import.
- A password is user input that never leaves memory: no SQLite row, file,
  log line, diagnostics field or exception message holds it. The
  `PasswordBox` is cleared before each attempt.
- The page count used to range-check comes from the opened document, never
  from the locator or a seed.
- Zoom widths pass through `PdfRasterBudget`, so a large page at 400% stays
  within the 4096 px cap and the 96 MiB cache.

## Testing

- **Core**
  - `PdfZoomTests`: the steps; *In* and *Out* from *Fit* at several
    effective percents; the ends; `WidthFor` in both modes; the labels.
  - `PdfLocationRulesTests`: `IsPageInRange` at 0, 1, M and M+1.
  - `PdfGuideLoadMessagesTests`: the new copy.
- **Infrastructure** (using `pdf-locked`, password `guide`)
  - The validator returns `ImportNeedsPdfPassword`. A wrong password
    throws `PasswordIncorrect`, and its message and the exception text
    don't contain the attempt. The right password returns a manifest with
    `PasswordRequired` and the page count and text facts. A changed source
    between the steps is `Changed`.
  - The publisher publishes a locked PDF without a password, and rejects a
    staged copy whose fingerprint differs.
  - The loader and text source: a wrong password fails, the right one
    gives page text, and a changed copy is still `Changed`.
- **Installed `pdf-reader` group.** `seed-pdf-reader` adds Locked PDF
  Guide, imported through `ResolvePdfPasswordAsync`. New modes:
  - `pdf-jump`: the dialog refuses 0, 201 and blank with the message, and
    150 jumps; focus returns to *Go to page* or to the preview.
  - `pdf-zoom`:
    - From page 121 at fraction 0.3, Zoom in to a percent, again, Zoom
      out, and Fit each keep page 121 and the fraction within the T10.3
      tolerance.
    - At 200% the horizontal scroll bar is present, and at Fit it's absent.
    - The status names the zoom.
    - Screenshots in light and dark.
  - `pdf-keys`: each key in the table runs its command with focus
    unchanged. With focus in `PdfDocumentText`, Page Down doesn't turn the
    page.
  - `pdf-locked`:
    - The unlock panel shows with focus in the box.
    - A wrong password shows the message and an empty box. The right
      password opens the page, and `PdfDocumentText` exposes `Locked guide
      secret page`.
    - Back and reopen ask again.
    - Screenshots in light and dark.
  - `pdf-offline`: after a relaunch with the originals deleted, the Tagged,
    Long and Locked guides open, and `Assert-NoRemoteConnections` passes in
    each.
- **Installed import group.** It gains `import-pdf-locked`, which imports
  `pdf-locked` through the dialog with a wrong and then the right password.
  A screenshot shows the password step.
- **Diagnostics.** `Assert-PdfDiagnostics` also checks that no abandoned
  extraction is left after the run.

## Docs

In the implementing branch:

- the implementation notes and verification in this file;
- the new modes in [e2e-testing.md](e2e-testing.md);
- the T10.2 lines in [implementation-plan.md](implementation-plan.md),
  [work-breakdown.md](../work-breakdown.md), and §8 S10 of
  [p1-technical-design.md](../p1-technical-design.md), with the T06.2
  password rule now saying that import accepts a locked PDF after the
  password;
- the T10.1 design's deferrals marked as done or moved to T17.3.

## Out of scope

- Ctrl+wheel and pinch zoom (#45).
- Keys for TXT and HTML, and a shortcut list (T16.1).
- A stored zoom, and remembering a password.
- The large-file working-set measurement (T17.3).
- A physical offline relaunch (T17.3).
