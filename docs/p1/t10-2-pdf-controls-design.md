# T10.2 PDF page controls, zoom and passwords design

Status: implemented; final CI run 37432552738 (after the final-review fix
wave) passed the full matrix, including the installed `pdf` and `import`
groups. Earlier full run: 37421797876, on `48a17ff`.
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
    - Screenshots in light and dark, taken at 100%: the 200% horizontal
      scroll bar is asserted by the smoke, not shown in the PNG.
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

## Implementation notes

Planning refinements, where the code departs from or narrows the text above:

- **Natural width is `PdfPage.Size.Width` (P1).** Windows.Data.Pdf already
  reports page size in DIPs, so no points-to-DIPs conversion is applied.
- **Go to page is a `TextBox` checked by `PageEntry.TryParse` (P2).**
  `NumberBox` was dropped because it rounds and clamps input itself, which
  hides the refusal the spec wants explained. A refusal cancels the dialog's
  close and shows `ReaderCommandError` under the box, so the dialog stays
  open. The toolbar takes the count from `ReaderToolbar.PageCount`; with no
  count (TXT) the old check and message stay. After a Ctrl+G jump the toolbar
  raises `ContentFocusRequested` and the shell focuses the preview.
- **There is no `UnlockAsync`, and the session holds no password (P3).** The
  shell calls `ManagedPdfGuideLoader.LoadAsync(guide, password, token)` and
  then `PdfReaderSession.OpenAsync(source, password, token)` once. PdfPig
  decrypts when it opens, so later text pages need no password.
- **Keys live on the toolbar (P4).** Each accelerator has hidden placement
  and no scope owner, and its handler runs only when `KeysEnabled` is set, no
  prompt is open, and its command is visible and enabled. With a `TextBox` or
  `PasswordBox` focused, Page Up/Down and Ctrl+Home/End are left unhandled.
  Ctrl+Plus is `Ctrl+Add`, `Ctrl+187` or `Ctrl+Shift+187`; Ctrl+Minus is
  `Ctrl+Subtract` or `Ctrl+189`; Ctrl+0 is `Ctrl+Number0` or
  `Ctrl+NumberPad0`.
- **Zoom state comes from the session (P5).** It raises `ZoomChanged` with
  `PdfZoomState(Label, CanZoomIn, CanZoomOut)` and the shell passes it to
  `ReaderToolbar.SetZoomAvailability`. At *Fit* the horizontal scroll bar is
  `Disabled` and the image width is `NaN`; at a percent it is `Auto` with an
  explicit width. A zoom relayout raises no `LocationChanged`. `PdfZoom`
  throws for a fit percent that is not finite and positive. Within half a
  percent counts as equal, so *Zoom in* from a fit of 99.8% goes to 125%,
  not 100%.
- **The smoke matches the status by prefix (P6).** `Wait-PdfPage` compares
  `Page N of M` followed by a space and U+00B7, so a zoom label after it
  doesn't matter.
- **Each mode is its own launch (P7).** `pdf-jump`, `pdf-zoom`, `pdf-keys`
  and `pdf-locked` run as separate smoke launches in each theme, after
  `pdf-reader`. Every `pdf-*.json` must show `disposedCleanly` true and
  `abandonedExtraction` false; only `pdf-reader` keeps the `requests >= 199`
  check.
- **The originals are temporary copies (P8).** `seed-pdf-reader` copies the
  fixtures to `%TEMP%\desktop-guides-pdf-originals-<guid>` and imports from
  there. The installer deletes that folder (guarded) and relaunches for
  `pdf-offline`, which opens Tagged, Long and Locked and passes
  `Assert-NoRemoteConnections` in each.
- **`import-pdf-locked` (P9).** A wrong password shows the message, leaves
  the box empty and keeps focus in it. The right password shows
  `Password protected` and `1 page`. The mode cancels the dialog, so nothing
  is published.
- **Bounded close (P10).** `PdfPageTextSource.CloseAsync(TimeSpan wait)`
  returns false when an extraction still holds the gate after `wait`; the
  document then closes when the extraction ends. The session waits up to 2 s
  for the scheduler, then up to 2 s for the text source (zero if the
  scheduler didn't settle), and records `AbandonedExtraction`.
- **The unlock panel lives in `ShellWindow` beside `ReaderLoadError` (P11).**
  No session exists before the PDF opens, so it can't live in
  `PdfReaderView`. The Reader heading already shows the title and *Back*
  is the way back, so the panel holds only the message, box, error and
  **Unlock**. `ShowReaderSurface` collapses it.
- **Only PdfPig decides `PasswordRequired` and `PasswordIncorrect` (P12).**
  PdfPig runs first. Windows.Data.Pdf's `0x8007052B` after PdfPig accepted
  the password maps to `PasswordProtected`, with the copy `This PDF's
  protection isn't supported. Remove the password and re-import it.`
- **Unlock is enabled only when the box has text (P13).** Clearing the box
  disables **Unlock**, so the code refocuses the box after each attempt. The
  import password box isn't disabled while the check runs.
- **The preview routes its own page keys (P14).** A focused `ScrollViewer`
  pages itself and may handle Page Up/Down and Ctrl+Home/End before a global
  accelerator runs. The PDF view's `PreviewKeyDown` therefore hands keys to
  `ReaderToolbar.TryRunKey(key, modifiers, fromContent: true)` and marks them
  handled when one runs. The accelerators leave page keys alone while a
  `TextBox`, `PasswordBox` or `ScrollViewer` has focus, so a key never runs
  twice. Both routes share one rule.
- **Shortcut names are exposed (P15).** Each keyed command sets
  `AutomationProperties.AcceleratorKey` (`Page Up`, `Ctrl+G`, ...) beside its
  tooltip, so UI Automation and Narrator read the key.
- **Disabling a focused zoom command first moves focus to the other one.**
  `SetZoomAvailability` does this so focus isn't dropped to the window when
  *Zoom in* reaches 400% or *Zoom out* reaches 50%.
- **The import password step keeps the file details on screen and stands in
  for the page facts until the password is checked.** The `Pages` and
  `Protection` rows appear only after the right password.

Changes the first CI runs forced on the plan's code:

- **The Reader unlock panel is wrapped in `local:AutomationGroup`.** A bare
  `StackPanel` has no automation peer, so UI Automation couldn't find
  `PdfUnlockPanel`. The group carries the id, visibility and width; the
  spacing stays on the inner panel.
- **After an unlock the preview takes focus once the new view has loaded.** On
  a fast open the view wasn't loaded yet and focus fell to the title bar.
  The code focuses at once if the view is loaded, and otherwise on its first
  `Loaded`, if the session is still current. The cause is inferred from the
  symptom, not observed.
- **An unlock attempt consumes the prompt when it starts.** A second attempt
  queued behind a successful one then does nothing, instead of reopening over
  the live session. A wrong password re-arms it.
- **The smoke's PDF scroller lookup retries for up to 3 s** with the same
  failure message, after a one-off `The PDF preview has no scroller.` flake
  during a zoom relayout.
- **In the import smoke, after a wrong attempt, the check is `Wait-Text
  'ImportFileName' 'pdf-locked.pdf'`** instead of `Wait-VisibleById
  'ImportPreview'`. `ImportPreview` is a peerless `StackPanel` and reports
  offscreen because it's taller than the dialog. The file name leaves the UIA
  tree if a status replaces the preview, so the check can still fail.
- **The toolbar smoke host's own key-control buttons are not tab stops**, so
  invoking them through UIA doesn't steal focus from the toolbar command under
  test. The Ctrl+G content-focus check starts from the host's notes box, so
  only `ContentFocusRequested` can move focus to the content.

Rulings made during execution, each with what it costs if wrong:

- P1–P15 stand over the spec text they refine (`TextBox` not `NumberBox`,
  no `UnlockAsync` and no session-held password, the panel in `ShellWindow`,
  DIPs from `PdfPage.Size`). They are narrower and safer. If wrong, Tasks 5–7
  need rework.
- F1: the smoke reuses its existing `Enter-Secret`; a second definition would
  shadow it at runtime. Cost: minor.
- F2: the vacuous `Assert.DoesNotContain(WrongAttempt, failed.ToString())` is
  dropped, because a test that cannot fail is a defect. Cost: Review Focus 1
  relies on the Task 2 and Task 4 checks.
- F3: the `Loader(harness)` helper is added in the test file. Cost: none.
- F4: one locked-without-password test is kept, the renamed
  `AnEncryptedCopyNeedsAPassword`. Cost: none.
- F5: `AChangedLockedCopyIsStillChanged` is kept; it pins that supplying a
  password never bypasses the change check. Cost: none.
- F6: `Assert-PdfDiagnostics` calls `Assert-PdfClosedCleanly` instead of
  repeating the check, and each JSON is copied once. Cost: minor.
- F11: the toolbar test reads `AllControls` and sets `PageCount`, so the
  refusal copy test is meaningful (count known, so the range message). Cost:
  minor.
- F7–F10 and F12: line numbers in the plan are stale, so targets are found by
  content, Task 9's edits win over its file list, Task 7 includes the
  `ShellWindow.xaml.cs` constructor edit, and Task 1 starts from `f99611b`.
  Cost: none.
- Task 4's plan-mandated checks that could not fail are fixed (the top of the
  page after a jump, and the refocus after a wrong attempt), because the spec
  requires both. Cost: a few lines of smoke and one more installed step.
- Task 4's 400% *Zoom in* check (which passed if the button was missing) and
  its `pdf-scan` `Page 1 of 1` check (which was lost) are restored in the
  same round, because both passed vacuously. Cost: trivial.
- Task 5's acceptance bar was `pdf-reader-light` green with
  `abandonedExtraction` false and `pdf-zoom-light` failing only at `Zoom in
  stayed enabled at 400%.`. `pdf-reader-dark` was not reached, because the
  installer order stops first. Cost: the dark Reader was verified in Task 6
  and 7 runs instead.
- Task 6's keyboard-dialog check starts Ctrl+G from the host's notes box, so
  only the event can move focus to the content. Cost: one small smoke edit and
  a CI run.
- Task 7's queued unlock closure consumes the prompt after its own guard, not
  before queuing, because clearing it earlier would make the first attempt's
  guard fail; a wrong password re-arms it. Cost: one more fix round.
- No smoke covers the double-unlock race, because it needs a slow PdfPig
  open and a timing check would be flaky. Cost: a regression there goes
  undetected by CI.
- The throwaway debug commits on the branch are not squashed by force-push;
  the net diff is clean and a squash merge removes them. Cost: noisy branch
  history only.
- The two Task 7 deviations from the plan's code (the `AutomationGroup` and the
  deferred focus) stand, because the plan's code failed on CI. Cost: none
  beyond the notes above.
- The `ImportPreview` smoke deviation stands, because the reviewer confirmed
  the replacement still fails when a status replaces the preview, while the
  brief's check could never pass. Cost: none beyond the note above.

## Verification

The final run is
[37432552738](https://github.com/ilya-slalom/desktop-guides/actions/runs/37432552738)
(`windows-ci.yml`, no `dev-fast`, no `shell-scope`, so every group ran, on
`52b0fca`, after the final-review fixes): every job passed, and `core-tests`
reported Core.Tests 788 passed and Infrastructure.Tests 540 passed, none
failed or skipped. The earlier full run,
[37421797876](https://github.com/ilya-slalom/desktop-guides/actions/runs/37421797876)
on `48a17ff`, had the same counts, and its `pdf-reader-dark` job needed a
rerun (the intermittent below). The counts below are test methods in each class
(theories count once):

- Core: `PdfZoomTests` (11), `PageEntryTests` (4), `PdfLocationRulesTests`
  (17), `PdfGuideLoadMessagesTests` (6), `ImportPresentationTests` (9).
- Infrastructure: `GuideImportValidatorTests` (21),
  `GuideImportPublisherTests` (36), `ManagedPdfGuideLoaderTests` (21),
  `PdfPageTextSourceTests` (15). Among them, the wrong-password tests
  (`WrongPasswordLeavesNoTraceOfTheAttempt`, `AWrongPasswordIsIncorrect`),
  `ChangedSourceAfterInspectionIsChanged`,
  `PublishingAChangedLockedSourceIsChanged` and
  `CloseDuringAnExtractionReturnsAndClosesWhenItEnds` pin Review Focus 1, 2
  and 5.

That run passed every job: `core-tests`, `reader-toolbar-ui`, both
`packages` and `production-packages` jobs (x64 and ARM64), `native-arm64-core`,
`native-arm64-ui` and `production-shell-ui` (all groups, including `pdf` and
`import`). `production-shell-ui` passed on the second attempt of that run; see
the intermittent below. The run had one warning, the existing CA1416 at
`ManagedPdfGuideLoaderTests.cs:305` (`FileStream.Lock`), which predates T10.2.

Installed modes in the run's `production-shell-ui` job, each in light and dark
unless noted. Every `pdf-*` diagnostics file shows `disposedCleanly` true and
`abandonedExtraction` false:

- `pdf-jump`: *Go to page* refuses 0, 201 and blank with `Enter a page from 1
  to 200.` and stays open; 150 jumps and shows that page at its top; focus
  returns to *Go to page*, or to the preview after Ctrl+G.
- `pdf-zoom`: from page 121 at fraction 0.3, 75%, 100% and Fit keep the page
  and the fraction (all 0.300); at 200% the horizontal scroll bar shows for
  pages 121 and 122 at different widths (view sizes 26.2 and 20.2); at 400%
  *Zoom in* is disabled. The screenshot is taken at 100%: the 200%
  scroll bar is asserted by the smoke, not shown in the PNG.
- `pdf-keys`: the page, Home/End, Ctrl+G, zoom and Fit keys run their
  commands with focus unchanged; Page Down in the page text and in the Go to
  page box doesn't turn the page.
- `pdf-locked` and `pdf-locked-reopen`: the panel shows with focus in the
  empty box and **Unlock** disabled; `wrong-7Q2x` shows the wrong-password
  line, an empty box and focus back in the box; `guide` opens `Locked guide
  secret page` with focus on the preview; a reopen asks again.
- `pdf-offline`: with the temporary originals deleted, a relaunch opens
  Tagged, Long and Locked and `remoteConnections` is 0 for each
  (`Assert-NoRemoteConnections`).
- `import-pdf-locked` and `import-pdf-unlocked` (phases of `import-light` and
  `import-dark`): the wrong attempt shows the message under an empty box with
  focus kept and the file name still on screen; `guide` shows
  `Protection: Password protected` and `1 page` and enables Import; Close
  publishes nothing.
- The toolbar smoke's new phases, in `reader-toolbar-ui`:
  `page-dialog-refuses-out-of-range`, `shortcut-names`,
  `keys-run-commands-without-moving-focus`, `keyboard-dialog-focuses-content`,
  `text-box-keeps-page-keys` and `zoom-end-disables-command-and-key`.
- The two password scans ran after the modes. `passwordScanUnreadable` (after
  `pdf-locked`) and `importPasswordScanUnreadable` (after the import group)
  were both empty lists, so every file under the app's LocalState was read
  and none held `wrong-7Q2x`. The copied evidence doesn't contain it either.

RED and GREEN runs, by task:

- Task 1: RED 37402202964, GREEN 37402368268.
- Task 2: RED 37402783211, GREEN 37402988305.
- Task 3: RED 37403561027, GREEN 37403830314; a nullable-password warning fix
  was green in 37405242835.
- Task 4 (the harness, red by design): 37404477975 and 37405645490 failed in
  `pdf-reader-light` at the missing zoom label.
- Task 5: GREEN for the session in 37406336818 (`pdf-reader-light` passed;
  `pdf-zoom-light` failed at the expected 400% check).
- Task 6 Step 3: RED 37407458822 (`reader-toolbar-ui` build errors);
  GREEN 37410123268. The Ctrl+G check was shown able to fail in 37411709255
  and green in 37412256027.
- Task 7: GREEN 37417492545; prompt-consumption fix green in 37418692800.
- Task 8 Step 2: RED 37419645116 (`import-light` at
  `Expected 'ImportFileName' named 'pdf-locked.pdf'.`); GREEN 37421019023.

Runs other than 37432552738 and 37421797876 used `dev-fast` or a
`shell-scope`; those two are full matrices. A full run on `7667b79`
(37430835784) failed in `production-shell-ui` on a smoke-helper scope error
in the fix wave itself, corrected in `52b0fca`.

An intermittent, not explained: `The PDF reader has no visible 'Next page'
command.` failed twice, in `pdf-reader-light` at `9dedf06` (run 37413047727,
attempt 1) and in `pdf-reader-dark` at `48a17ff` (run 37421797876, attempt 1).
Both reruns passed. The failed-job logs don't show which `Invoke-NextPages`
call failed. The smoke did a single UIA lookup with no wait. T10.2 changed
this bar: the PDF session now reports Zoom, PageJump and FitWidth, so it shows
six primary commands and a More button, where T10.1 showed four. The
suspected cause is the `CommandBar` dynamic-overflow re-layout when the
commands are shown again after the previous guide, briefly leaving Next page
offscreen or in overflow. Steady-state screenshots at full and 600-wide
windows show Next page on the bar. The cause is unproven. The smoke now waits
up to 5 s for a visible, enabled Next page and records diagnostics on failure
(a `pdf-next-page-missing` screenshot, every 'Next page' element with its
`IsOffscreen` and `IsEnabled`, and the `ReaderCommands` bounds).

Screenshots and results are in
[evidence/t10-2-pdf-controls](evidence/t10-2-pdf-controls/).

No installed run on the Windows host was made: it needs an elevated task for
the certificate, and CI is the required evidence. This commit is docs only, so
the runs above are on the commit before it.
