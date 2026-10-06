# T10.2 PDF Page Controls, Zoom and Passwords Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The PDF Reader shows "Page N of M · zoom", jumps only to pages 1 to M,
fits to width and zooms while keeping the reading point, has Reader-scoped
keys that never move focus, and opens password-protected PDFs at import and
in the Reader without storing the password (TR10.1–TR10.3).

**Architecture:** Core gains pure rules (`PdfZoom`, `PageEntry`,
`PdfLocationRules.IsPageInRange`) and the new copy. Import adds a password
step after inspection, mirroring the TXT encoding step: the validator returns
`ImportNeedsPdfPassword`, and `ResolvePdfPasswordAsync` checks a password
without keeping it. The loader takes an optional password for PdfPig, and
the session takes it once for Windows.Data.Pdf; neither keeps it. Zoom sets
an explicit page width in the view and re-renders through the existing
raster path. Keys are toolbar-level `KeyboardAccelerator`s that the shell
enables only for a PDF session. The text source gains a bounded close so a
hung extraction can't hold the Reader open.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), xUnit, PowerShell 5.1
UI Automation smoke.

**Spec:** [t10-2-pdf-controls-design.md](t10-2-pdf-controls-design.md)

## Global Constraints

- **Branch:** `feat/p1-t10-2-pdf-controls` (already checked out; the spec is
  commit `aebca62`).
- **Tooling:** there is no local `dotnet` or `pwsh`. Every build and test is a
  CI run on the pushed branch:
  1. `git -C /Users/ilya.lissoboi/work/desktop-guides push`
  2. `gh workflow run windows-ci.yml -R ilya-slalom/desktop-guides --ref feat/p1-t10-2-pdf-controls -f shell-scope=pdf` (use `-f shell-scope=import` for the import group; add `-f dev-fast=true` for a quicker focused run)
  3. `gh run list -R ilya-slalom/desktop-guides --workflow windows-ci.yml --branch feat/p1-t10-2-pdf-controls --limit 1 --json databaseId,headSha -q '.[0]'`, and check that `headSha` is the commit you pushed.
  4. `gh run watch <id> -R ilya-slalom/desktop-guides --exit-status --interval 60`; on failure, `gh run view <id> -R ilya-slalom/desktop-guides --log-failed`.

  `core-tests` takes about 3 minutes and runs Core.Tests and
  Infrastructure.Tests. A task whose tests are all in those projects may
  cancel the run (`gh run cancel <id>`) once `core-tests` has finished. A
  RED commit may share a run with an earlier GREEN commit only when the two
  are checked by different jobs.
- **ASCII check** for every changed `.ps1`:
  `LC_ALL=C grep -n "$(printf '[\200-\377]')" <file>` — expected: no output.
  Write a non-ASCII character in PowerShell as `[char]0x00B7`.
- **Values (verbatim):**
  - Zoom steps: `50, 75, 100, 125, 150, 200, 300, 400`. Labels: `Fit width`, `125%`.
  - Status: `Page 121 of 200 · Fit width` (the separator is space, U+00B7, space).
  - Range message: `Enter a page from 1 to 200.`
  - `PasswordRequired`: `This PDF needs a password.`
  - `PasswordIncorrect` and the import wrong-password text: `That password didn't open this PDF. Try again.`
  - `PasswordProtected` (new copy): `This PDF's protection isn't supported. Remove the password and re-import it.`
  - Import fact: label `Protection`, value `Password protected`.
  - Tooltips: `Previous page (Page Up)`, `Next page (Page Down)`, `Go to start (Ctrl+Home)`, `Go to end (Ctrl+End)`, `Go to page (Ctrl+G)`, `Zoom in (Ctrl+Plus)`, `Zoom out (Ctrl+Minus)`, `Fit to width (Ctrl+0)`.
  - Close wait: 2 s. Diagnostics key: `abandonedExtraction`.
  - Fixture `pdf-locked`: user password `guide`, one page, text `Locked guide secret page`. The wrong attempt used everywhere is `wrong-7Q2x`.
  - AutomationIds: `PdfUnlockPanel`, `PdfPasswordInput`, `PdfUnlockButton`, `PdfUnlockError`, `ReaderCommandError`, `ImportPdfPasswordInput`, `ImportPdfUnlock`, `ImportPdfPasswordError`, `ImportProtectedRow`, `ImportProtected`.
- **Rules:**
  - A password is never written to SQLite, a file, a log, diagnostics or
    exception text, and never passed as a process or task argument. Each
    password box is cleared before its attempt runs. The fixture password
    `guide` is public test data and may appear in the seed and the smoke.
  - Imported files are untrusted. The original is never reopened after
    import, and the page count comes from the opened document only.
  - UI checks assert only what app code controls.
  - Installed runs follow [e2e-testing.md](e2e-testing.md), including its
    backup and cleanup rules. No firewall rule. If a run needs an elevated
    scheduled task, stop and ask.
  - No `cd` in shell commands: use absolute paths and `git -C`.
  - Leave the untracked `.claude/` and `.superpowers/sdd/host-run` alone.
  - Commit messages end with
    `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Spec refinements made while planning** (each one is recorded in the
  design doc by Task 9):
  - **P1. Natural width is `PdfPage.Size.Width`.** Windows.Data.Pdf already
    reports page size in DIPs, so no points-to-DIPs conversion is applied.
  - **P2. Go to page keeps a `TextBox`.** `PageEntry.TryParse` checks it, and
    a refusal sets `args.Cancel` and shows `ReaderCommandError` under the box,
    so the dialog stays open. `NumberBox` was dropped: it rounds and clamps
    input itself, which hides the refusal the spec wants explained. The
    toolbar gets the count from `ReaderToolbar.PageCount`; with no count
    (TXT), the old check and message stay. After a Ctrl+G jump the toolbar
    raises `ContentFocusRequested`, and the shell focuses the preview.
  - **P3. No `UnlockAsync`, and the session holds no password.** The shell
    calls `ManagedPdfGuideLoader.LoadAsync(guide, password, token)` and then
    `PdfReaderSession.OpenAsync(source, password, token)`. PdfPig decrypts
    when it opens, so later text pages need no password.
  - **P4. Keys live on the toolbar `UserControl`.** Each accelerator has
    hidden placement and no scope owner, and its handler runs only when
    `KeysEnabled` is set, no prompt is open, and its command is visible and
    enabled. With a `TextBox` or `PasswordBox` focused, Page Up/Down and
    Ctrl+Home/End are left unhandled (and see P14 for the preview).
    Ctrl+Plus is `Ctrl+Add`, `Ctrl+187` or `Ctrl+Shift+187`,
    Ctrl+Minus is `Ctrl+Subtract` or `Ctrl+189`, and Ctrl+0 is
    `Ctrl+Number0` or `Ctrl+NumberPad0`.
  - **P5. Zoom state.** The session raises `ZoomChanged` with
    `PdfZoomState(Label, CanZoomIn, CanZoomOut)`, and the shell passes it to
    `ReaderToolbar.SetZoomAvailability`. At *Fit* the horizontal scroll bar
    is `Disabled` and the image width is `NaN`; at a percent it is `Auto`
    with an explicit width. A zoom relayout raises no `LocationChanged`.
    `PdfZoom` throws for a fit percent that is not finite and positive.
    Within half a percent counts as equal, so *Zoom in* from a fit of
    99.8% goes to 125%, not 100%.
  - **P6. The smoke matches the status by prefix.** `Wait-PdfPage` compares
    `Page N of M` followed by `" " + [char]0x00B7`.
  - **P7. Separate launches per mode.** `pdf-jump`, `pdf-zoom`, `pdf-keys`
    and `pdf-locked` each run as their own smoke launch in each theme, after
    `pdf-reader`. Every `pdf-*.json` must show `disposedCleanly` true and
    `abandonedExtraction` false; only `pdf-reader` keeps the `requests >=
    199` check.
  - **P8. Offline originals.** `seed-pdf-reader` copies the fixtures to
    `%TEMP%\desktop-guides-pdf-originals-<guid>` and imports from there.
    The installer deletes that folder (guarded) and relaunches for
    `pdf-offline`, which opens Tagged, Long and Locked and passes
    `Assert-NoRemoteConnections` in each.
  - **P9. `import-pdf-locked`.** A wrong password shows the message, leaves
    the box empty and keeps focus in it. The right password shows
    `Password protected` and `1 page`. Screenshots in both states. The mode
    cancels the dialog, so nothing is published.
  - **P10. Bounded close.** `PdfPageTextSource.CloseAsync(TimeSpan wait)`
    returns false when an extraction still holds the gate after `wait`; the
    document then closes when the extraction ends. The session waits up to
    2 s for the scheduler, then up to 2 s for the text source (zero if the
    scheduler didn't settle), and records `AbandonedExtraction`.
  - **P11. The unlock panel lives in `ShellWindow`** beside
    `ReaderLoadError`, not in `PdfReaderView`, because no session exists
    before the PDF opens. The Reader heading already shows the title, and
    *Back* (`ReaderBackToGame`) is the way back, so the panel holds only the
    message, box, error and **Unlock**. `ShowReaderSurface` collapses it.
  - **P12. Engine split.** `PasswordRequired` and `PasswordIncorrect` come
    only from PdfPig, which runs first. Windows.Data.Pdf's
    `0x8007052B` after PdfPig accepted the password maps to
    `PasswordProtected`, with the new copy.
  - **P13. Unlock is enabled only when the box has text.** Clearing the box
    disables **Unlock**, so the code refocuses the box after each attempt.
    The import password box isn't disabled while the check runs.
  - **P14. The preview routes its own page keys.** A focused `ScrollViewer`
    pages itself and may handle Page Up/Down and Ctrl+Home/End before a
    global accelerator runs. The PDF view's `PreviewKeyDown` therefore
    hands keys to `ReaderToolbar.TryRunKey(key, modifiers, fromContent: true)`
    and marks them handled when it runs one. The accelerators leave page keys
    alone while a `TextBox`, `PasswordBox` or `ScrollViewer` has focus, so
    a key never runs twice. Both routes share one rule.
  - **P15. Shortcut names.** Each keyed command sets
    `AutomationProperties.AcceleratorKey` (`Page Up`, `Ctrl+G`, ...) beside
    its tooltip, so UI Automation and Narrator read the key.

## Review Focus

1. **A password in text that outlives the attempt.** An exception message,
   `ToString()`, diagnostics JSON, the import status or a LocalState file
   holding the attempt. Pinned by Task 2's
   `WrongPasswordLeavesNoTraceOfTheAttempt`, Task 3's
   `AWrongPasswordIsIncorrectAndNotEchoed`, and Task 4's LocalState scan for
   `wrong-7Q2x` after `pdf-locked`.
2. **Closing during a hung extraction.** Back while PdfPig is stuck on a
   page must not freeze the Reader. Pinned by Task 3's
   `CloseDuringAnExtractionReturnsAndClosesWhenItEnds`.
3. **Zoom on mixed page sizes.** At a fixed percent a landscape page is
   wider than a portrait one; at *Fit* both match the viewport. Pinned by
   Task 1's `WidthForUsesTheShownPage` and Task 4's `pdf-zoom` width check
   on pages 121 and 122.
4. **Page keys while a text box has focus.** Page Down in
   `PdfDocumentText` or in the *Go to page* box must not turn the page.
   Pinned by Task 4's `pdf-keys` checks for both.
5. **The source changes between the password step and import.** Pinned by
   Task 2's `ChangedSourceAfterInspectionIsChanged` and
   `PublishingAChangedLockedSourceIsChanged`.

---

### Task 1: Core rules and copy

**Files:**
- Create: `src/DesktopGuides.Core/Pdf/PdfZoom.cs`
- Create: `src/DesktopGuides.Core/Reading/PageEntry.cs`
- Modify: `src/DesktopGuides.Core/Pdf/PdfLocationRules.cs`
- Modify: `src/DesktopGuides.Core/Pdf/PdfGuideLoadMessages.cs`
- Modify: `src/DesktopGuides.Core/Pdf/PdfSessionDiagnostics.cs`
- Modify: `src/DesktopGuides.Core/Import/ImportPresentation.cs`
- Test: `tests/DesktopGuides.Core.Tests/PdfZoomTests.cs` (create)
- Test: `tests/DesktopGuides.Core.Tests/PageEntryTests.cs` (create)
- Test: `tests/DesktopGuides.Core.Tests/PdfLocationRulesTests.cs`,
  `PdfGuideLoadMessagesTests.cs`, `PdfSessionDiagnosticsTests.cs`,
  `ImportPresentationTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `public readonly record struct PdfZoom(int? Percent)` with `static PdfZoom Fit`,
    `static IReadOnlyList<int> Steps`, `bool IsFit`, `PdfZoom In(double fitPercent)`,
    `PdfZoom Out(double fitPercent)`, `bool CanZoomIn(double fitPercent)`,
    `bool CanZoomOut(double fitPercent)`,
    `double WidthFor(double viewportWidth, double naturalPageWidth)`, `string Label`.
  - `PdfLocationRules.IsPageInRange(int pageNumber, int pageCount)`.
  - `PageEntry.TryParse(string? text, int pageCount, out int pageNumber)` and
    `PageEntry.RangeMessage(int pageCount)` in `DesktopGuides.Core.Reading`.
  - `PdfGuideLoadError.PasswordRequired`, `PdfGuideLoadError.PasswordIncorrect`
    (appended).
  - `PdfSessionDiagnostics(..., int Evictions, bool AbandonedExtraction)`.
  - `ImportPresentation.PdfPasswordIncorrect` and
    `ImportPresentation.PasswordProtectedFact`.

- [ ] **Step 1: Write the failing zoom tests**

```csharp
using DesktopGuides.Core.Pdf;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfZoomTests
{
    [Fact]
    public void StepsAreFixed() =>
        Assert.Equal([50, 75, 100, 125, 150, 200, 300, 400], PdfZoom.Steps);

    [Fact]
    public void FitIsTheDefault()
    {
        Assert.True(PdfZoom.Fit.IsFit);
        Assert.True(default(PdfZoom).IsFit);
        Assert.False(new PdfZoom(125).IsFit);
    }

    [Theory]
    [InlineData(37.0, 50)]
    [InlineData(80.0, 100)]
    [InlineData(100.0, 125)]
    [InlineData(99.8, 125)]
    [InlineData(250.0, 300)]
    public void InFromFitPicksTheFirstStepAbove(double fitPercent, int expected) =>
        Assert.Equal(new PdfZoom(expected), PdfZoom.Fit.In(fitPercent));

    [Theory]
    [InlineData(80.0, 75)]
    [InlineData(100.0, 75)]
    [InlineData(100.3, 75)]
    [InlineData(450.0, 400)]
    public void OutFromFitPicksTheLastStepBelow(double fitPercent, int expected) =>
        Assert.Equal(new PdfZoom(expected), PdfZoom.Fit.Out(fitPercent));

    [Fact]
    public void FromAStepZoomMovesOneStep()
    {
        Assert.Equal(new PdfZoom(150), new PdfZoom(125).In(37));
        Assert.Equal(new PdfZoom(100), new PdfZoom(125).Out(500));
    }

    [Fact]
    public void TheEndsAreUnavailable()
    {
        Assert.False(new PdfZoom(400).CanZoomIn(80));
        Assert.Equal(new PdfZoom(400), new PdfZoom(400).In(80));
        Assert.False(new PdfZoom(50).CanZoomOut(80));
        Assert.Equal(new PdfZoom(50), new PdfZoom(50).Out(80));
        Assert.True(new PdfZoom(50).CanZoomIn(80));
        Assert.True(new PdfZoom(400).CanZoomOut(80));
    }

    [Fact]
    public void FitBeyondTheStepsCantGoFurther()
    {
        Assert.False(PdfZoom.Fit.CanZoomIn(400));
        Assert.False(PdfZoom.Fit.CanZoomIn(410));
        Assert.Equal(PdfZoom.Fit, PdfZoom.Fit.In(410));
        Assert.False(PdfZoom.Fit.CanZoomOut(50));
        Assert.False(PdfZoom.Fit.CanZoomOut(30));
    }

    [Fact]
    public void WidthForUsesTheShownPage()
    {
        // pdf-long mixes portrait and landscape pages, whose natural widths differ.
        Assert.Equal(900, PdfZoom.Fit.WidthFor(900, 612));
        Assert.Equal(900, PdfZoom.Fit.WidthFor(900, 792));
        Assert.Equal(765, new PdfZoom(125).WidthFor(900, 612));
        Assert.Equal(990, new PdfZoom(125).WidthFor(900, 792));
    }

    [Fact]
    public void LabelsNameTheZoom()
    {
        Assert.Equal("Fit width", PdfZoom.Fit.Label);
        Assert.Equal("125%", new PdfZoom(125).Label);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnInvalidFitPercentThrows(double fitPercent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfZoom.Fit.In(fitPercent));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfZoom.Fit.CanZoomOut(fitPercent));
    }

    [Theory]
    [InlineData(0.0, 612.0)]
    [InlineData(900.0, 0.0)]
    [InlineData(double.NaN, 612.0)]
    public void AnInvalidWidthThrows(double viewportWidth, double naturalPageWidth) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PdfZoom.Fit.WidthFor(viewportWidth, naturalPageWidth));
}
```

- [ ] **Step 2: Write the failing page-entry, range, copy and diagnostics tests**

`tests/DesktopGuides.Core.Tests/PageEntryTests.cs`:

```csharp
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PageEntryTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("200", 200)]
    [InlineData(" 150 ", 150)]
    [InlineData("007", 7)]
    public void InRangeWholeNumbersParse(string text, int expected)
    {
        Assert.True(PageEntry.TryParse(text, 200, out int page));
        Assert.Equal(expected, page);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("201")]
    [InlineData("-1")]
    [InlineData("+3")]
    [InlineData("1.5")]
    [InlineData("1,000")]
    [InlineData("1e2")]
    [InlineData("99999999999")]
    [InlineData("three")]
    public void EverythingElseIsRefused(string? text)
    {
        Assert.False(PageEntry.TryParse(text, 200, out int page));
        Assert.Equal(0, page);
    }

    [Fact]
    public void NoPagesRefusesEverything() =>
        Assert.False(PageEntry.TryParse("1", 0, out _));

    [Fact]
    public void TheMessageNamesTheRange()
    {
        Assert.Equal("Enter a page from 1 to 200.", PageEntry.RangeMessage(200));
        Assert.Equal("Enter a page from 1 to 1.", PageEntry.RangeMessage(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PageEntry.RangeMessage(0));
    }
}
```

Add to `PdfLocationRulesTests`:

```csharp
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    [InlineData(-1, false)]
    public void IsPageInRangeIsOneBased(int pageNumber, bool expected) =>
        Assert.Equal(expected, PdfLocationRules.IsPageInRange(pageNumber, 200));
```

In `PdfGuideLoadMessagesTests`, replace the `PasswordProtected` message row
and add the new rows to both theories:

```csharp
    [InlineData(PdfGuideLoadError.PasswordProtected, "This PDF's protection isn't supported. Remove the password and re-import it.")]
    [InlineData(PdfGuideLoadError.PasswordRequired, "This PDF needs a password.")]
    [InlineData(PdfGuideLoadError.PasswordIncorrect, "That password didn't open this PDF. Try again.")]
```

```csharp
    [InlineData(PdfGuideLoadError.PasswordRequired, HtmlGuideLoadAction.None)]
    [InlineData(PdfGuideLoadError.PasswordIncorrect, HtmlGuideLoadAction.None)]
```

In `PdfSessionDiagnosticsTests.JsonHoldsOnlyTheCounts`, construct with a
trailing `true`, add `"abandonedExtraction"` at the end of the key list, and
assert `Assert.True(root.GetProperty("abandonedExtraction").GetBoolean());`.
Every other `new PdfSessionDiagnostics(...)` in that file gets a trailing
`false`.

Add to `ImportPresentationTests`:

```csharp
    [Fact]
    public void PdfPasswordCopy()
    {
        Assert.Equal("That password didn't open this PDF. Try again.", ImportPresentation.PdfPasswordIncorrect);
        Assert.Equal("Password protected", ImportPresentation.PasswordProtectedFact);
    }
```

- [ ] **Step 3: Push and confirm RED**

Commit the tests alone (`test(core): T10.2 zoom, page entry and password copy`),
push, dispatch with `-f shell-scope=pdf -f dev-fast=true`, and cancel after
`core-tests`.
Expected: `core-tests` fails to compile on `PdfZoom`, `PageEntry`,
`IsPageInRange`, `PasswordRequired`, `PasswordIncorrect`, the 11-argument
diagnostics constructor and the two `ImportPresentation` members.

- [ ] **Step 4: Implement `PdfZoom`**

```csharp
using System.Globalization;

namespace DesktopGuides.Core.Pdf;

/// <summary>
/// The PDF Reader's zoom: fit width (null), or a percent of the shown page's
/// natural width. It lasts only while the guide is open.
/// </summary>
public readonly record struct PdfZoom(int? Percent)
{
    // Within half a percent counts as equal, so a step a hair above the fit
    // percent isn't offered as a zoom that changes nothing.
    private const double Tolerance = 0.5;
    private static readonly int[] steps = [50, 75, 100, 125, 150, 200, 300, 400];

    public static PdfZoom Fit => default;
    public static IReadOnlyList<int> Steps { get; } = Array.AsReadOnly(steps);
    public bool IsFit => Percent is null;
    public string Label => Percent is int percent
        ? string.Create(CultureInfo.InvariantCulture, $"{percent}%")
        : "Fit width";

    // fitPercent is the viewport width over the shown page's natural width, × 100.
    public PdfZoom In(double fitPercent) => Above(fitPercent) is int step ? new PdfZoom(step) : this;
    public PdfZoom Out(double fitPercent) => Below(fitPercent) is int step ? new PdfZoom(step) : this;
    public bool CanZoomIn(double fitPercent) => Above(fitPercent) is not null;
    public bool CanZoomOut(double fitPercent) => Below(fitPercent) is not null;

    public double WidthFor(double viewportWidth, double naturalPageWidth)
    {
        Positive(viewportWidth, nameof(viewportWidth));
        Positive(naturalPageWidth, nameof(naturalPageWidth));
        return Percent is int percent ? naturalPageWidth * percent / 100 : viewportWidth;
    }

    private int? Above(double fitPercent)
    {
        double current = Effective(fitPercent);
        foreach (int step in steps)
        {
            if (step > current + Tolerance) return step;
        }
        return null;
    }

    private int? Below(double fitPercent)
    {
        double current = Effective(fitPercent);
        for (int index = steps.Length - 1; index >= 0; index--)
        {
            if (steps[index] < current - Tolerance) return steps[index];
        }
        return null;
    }

    private double Effective(double fitPercent)
    {
        Positive(fitPercent, nameof(fitPercent));
        return Percent ?? fitPercent;
    }

    private static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Must be finite and positive.");
        }
    }
}
```

- [ ] **Step 5: Implement the range rule and `PageEntry`**

In `PdfLocationRules`:

```csharp
    // 1-based, like the page number the user types.
    public static bool IsPageInRange(int pageNumber, int pageCount) =>
        pageNumber >= 1 && pageNumber <= pageCount;
```

`src/DesktopGuides.Core/Reading/PageEntry.cs`:

```csharp
using System.Globalization;
using DesktopGuides.Core.Pdf;

namespace DesktopGuides.Core.Reading;

/// <summary>The Go to page entry: digits only, within the open document's pages.</summary>
public static class PageEntry
{
    public static bool TryParse(string? text, int pageCount, out int pageNumber)
    {
        pageNumber = 0;
        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
            !PdfLocationRules.IsPageInRange(value, pageCount))
        {
            return false;
        }
        pageNumber = value;
        return true;
    }

    public static string RangeMessage(int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        return string.Create(CultureInfo.InvariantCulture, $"Enter a page from 1 to {pageCount}.");
    }
}
```

- [ ] **Step 6: Update the copy and the diagnostics record**

`PdfGuideLoadMessages.cs`:

```csharp
public enum PdfGuideLoadError
{
    Missing, Changed, Unreadable, Damaged, PasswordProtected, Failed, PasswordRequired, PasswordIncorrect
}
```

In `For`, replace the `PasswordProtected` arm and add two:

```csharp
        PdfGuideLoadError.PasswordProtected =>
            "This PDF's protection isn't supported. Remove the password and re-import it.",
        PdfGuideLoadError.PasswordRequired => "This PDF needs a password.",
        PdfGuideLoadError.PasswordIncorrect => "That password didn't open this PDF. Try again.",
```

In `ActionFor`, extend the `None` arm:

```csharp
        PdfGuideLoadError.Missing or PdfGuideLoadError.Changed or PdfGuideLoadError.Damaged or
            PdfGuideLoadError.PasswordProtected or PdfGuideLoadError.PasswordRequired or
            PdfGuideLoadError.PasswordIncorrect => HtmlGuideLoadAction.None,
```

`PdfSessionDiagnostics`: add `bool AbandonedExtraction` after `int Evictions`,
and `abandonedExtraction = AbandonedExtraction` after `evictions = Evictions`
in `ToJson`. Update the one production caller,
`PdfReaderSession.DisposeAsync`, to pass `false` for now (Task 5 sets it).

`ImportPresentation`:

```csharp
    public const string PdfPasswordIncorrect = "That password didn't open this PDF. Try again.";
    public const string PasswordProtectedFact = "Password protected";
```

- [ ] **Step 7: Push and confirm GREEN**

Commit (`feat(core): T10.2 zoom, page entry and password copy`), push,
dispatch with `-f shell-scope=pdf -f dev-fast=true`, and cancel after
`core-tests`.
Expected: `core-tests` passes, including every new test. The Production
build in later jobs isn't needed for this task.

- [ ] **Step 8: Commit check**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides log --oneline -2
```
Expected: the `test(core)` and `feat(core)` commits on top of `aebca62`.

### Task 2: Import password step

**Files:**
- Modify: `src/DesktopGuides.Core/Import/ImportContracts.cs`
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs`
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs:347-355`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorHtmlPdfTests.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/PublisherHarness.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportPublisherTests.cs`

**Interfaces:**
- Consumes: `ImportPresentation.PdfPasswordIncorrect` (Task 1).
- Produces:
  - `public sealed record ImportNeedsPdfPassword(ImportSource Source, string SuggestedTitle, string Fingerprint) : ImportInspection;`
  - `PdfImportManifest(ImportSource Source, string SuggestedTitle, int PageCount, bool HasText, string Fingerprint, bool PasswordRequired = false)`
  - `ImportIssue.PasswordIncorrect` (appended).
  - `IGuideImportValidator.ResolvePdfPasswordAsync(ImportNeedsPdfPassword inspection, string password, CancellationToken token) → Task<PdfImportManifest>`
  - `PublisherHarness.InspectAsync(string path, int? codePage = null, string? password = null)`

- [ ] **Step 1: Write the failing validator tests**

In `GuideImportValidatorHtmlPdfTests`, replace `PasswordProtectedPdfIsEncrypted`
with:

```csharp
    private const string WrongAttempt = "wrong-7Q2x";

    [Fact]
    public async Task PasswordProtectedPdfNeedsAPassword()
    {
        string path = P0Fixtures.Resolve("pdf-locked.pdf");

        ImportNeedsPdfPassword needs = Assert.IsType<ImportNeedsPdfPassword>(await Inspect(path));

        Assert.Equal("pdf-locked", needs.SuggestedTitle);
        Assert.Equal(new FileInfo(path).Length, needs.Source.ByteCount);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), needs.Fingerprint);
    }

    [Fact]
    public async Task TheRightPasswordGivesAProtectedManifest()
    {
        GuideImportValidator validator = new();
        ImportNeedsPdfPassword needs = Assert.IsType<ImportNeedsPdfPassword>(
            await validator.InspectAsync(P0Fixtures.Resolve("pdf-locked.pdf"), CancellationToken.None));

        PdfImportManifest manifest = await validator.ResolvePdfPasswordAsync(needs, "guide", CancellationToken.None);

        Assert.True(manifest.PasswordRequired);
        Assert.Equal(1, manifest.PageCount);
        Assert.True(manifest.HasText);
        Assert.Equal(needs.Fingerprint, manifest.Fingerprint);
        Assert.Equal(needs.SuggestedTitle, manifest.SuggestedTitle);
    }

    [Fact]
    public async Task WrongPasswordLeavesNoTraceOfTheAttempt()
    {
        GuideImportValidator validator = new();
        ImportNeedsPdfPassword needs = Assert.IsType<ImportNeedsPdfPassword>(
            await validator.InspectAsync(P0Fixtures.Resolve("pdf-locked.pdf"), CancellationToken.None));

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => validator.ResolvePdfPasswordAsync(needs, WrongAttempt, CancellationToken.None));

        Assert.Equal(ImportIssue.PasswordIncorrect, error.Issue);
        Assert.Equal("That password didn't open this PDF. Try again.", error.Message);
        Assert.DoesNotContain(WrongAttempt, error.ToString());
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(WrongAttempt, needs.ToString());
    }

    [Fact]
    public async Task ChangedSourceAfterInspectionIsChanged()
    {
        using ImportTestDirectory files = new();
        string path = files.Copy("pdf-locked.pdf", "locked.pdf");
        GuideImportValidator validator = new();
        ImportNeedsPdfPassword needs = Assert.IsType<ImportNeedsPdfPassword>(
            await validator.InspectAsync(path, CancellationToken.None));
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(-5));

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => validator.ResolvePdfPasswordAsync(needs, "guide", CancellationToken.None));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        Assert.Equal("locked.pdf changed after it was checked. Choose it again.", error.Message);
    }

    [Fact]
    public async Task ReplacedBytesWithTheSameSizeAndTimeAreChanged()
    {
        using ImportTestDirectory files = new();
        string path = files.Copy("pdf-locked.pdf", "locked.pdf");
        GuideImportValidator validator = new();
        ImportNeedsPdfPassword needs = Assert.IsType<ImportNeedsPdfPassword>(
            await validator.InspectAsync(path, CancellationToken.None));
        DateTime written = File.GetLastWriteTimeUtc(path);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^2] ^= 0x01;   // inside the trailing whitespace or %%EOF
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, written);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => validator.ResolvePdfPasswordAsync(needs, "guide", CancellationToken.None));

        Assert.Equal(ImportIssue.Changed, error.Issue);
    }
```

Add `using System.Security.Cryptography;` if the file lacks it. In
`TypedIssuesStayDistinct`, replace the `pdf-locked` entry with an empty file:
`files.Write("empty.txt", "")` expecting `ImportIssue.Empty` (keep the
assertion that the issues stay distinct). `InspectingLeavesHtmlAndPdfFixturesUnchanged`
keeps `pdf-locked` and needs no change: inspection still reads it without
writing.

- [ ] **Step 2: Write the failing publisher tests**

In `PublisherHarness.InspectAsync`, add the parameter and the case:

```csharp
    public async Task<ImportManifest> InspectAsync(string path, int? codePage = null, string? password = null)
    {
        ImportInspection inspection = await Validator.InspectAsync(path, CancellationToken.None);
        return inspection switch
        {
            ImportReady ready => ready.Manifest,
            ImportNeedsTxtEncoding needs => await Validator.ResolveTxtEncodingAsync(
                needs, codePage ?? throw new InvalidOperationException("A code page is required."), CancellationToken.None),
            ImportNeedsPdfPassword needs => await Validator.ResolvePdfPasswordAsync(
                needs, password ?? throw new InvalidOperationException("A password is required."), CancellationToken.None),
            _ => throw new InvalidOperationException(),
        };
    }
```

In `GuideImportPublisherTests`:

```csharp
    [Fact]
    public async Task PublishesALockedPdfWithoutItsPassword()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("pdf-locked.pdf", "locked.pdf");

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source, password: "guide"));

        Guide guide = (await harness.Repository.GetGuideAsync(id))!;
        Assert.Equal((GuideFormat.Pdf, "guide.pdf"), (guide.Format, guide.PrimaryRelativePath));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, "guide.pdf")));
    }

    [Fact]
    public async Task PublishingAChangedLockedSourceIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("pdf-locked.pdf", "locked.pdf");
        ImportManifest manifest = await harness.InspectAsync(source, password: "guide");
        File.AppendAllText(source, "\n");

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task AnUnlockedPdfClaimingAPasswordIsRefused()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("pdf-short.pdf", "short.pdf");
        PdfImportManifest manifest = (PdfImportManifest)await harness.InspectAsync(source);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest with { PasswordRequired = true }));

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        harness.AssertNothingLeft();
    }
```

If `PublishAsync` wraps errors differently from `PublishesPdfGuide`'s
siblings that assert failures, match those siblings' assertion style; the
issue values above stay.

- [ ] **Step 3: Push and confirm RED**

Commit the tests (`test(import): T10.2 PDF password step`), push, dispatch
with `-f shell-scope=import -f dev-fast=true`, cancel after `core-tests`.
Expected: compile errors on `ImportNeedsPdfPassword`, `PasswordRequired`,
`ImportIssue.PasswordIncorrect` and `ResolvePdfPasswordAsync`.

- [ ] **Step 4: Extend the contracts**

In `ImportContracts.cs`:

```csharp
public sealed record PdfImportManifest(
    ImportSource Source, string SuggestedTitle, int PageCount, bool HasText, string Fingerprint,
    bool PasswordRequired = false)
    : ImportManifest(Source, GuideFormat.Pdf, SuggestedTitle, Fingerprint);
```

```csharp
/// <summary>The PDF is encrypted; the password is checked by ResolvePdfPasswordAsync and never kept.</summary>
public sealed record ImportNeedsPdfPassword(
    ImportSource Source, string SuggestedTitle, string Fingerprint) : ImportInspection;
```

```csharp
public enum ImportIssue
{
    Missing, Unsupported, Empty, TooLarge, Unreadable, Encrypted,
    UnsupportedEncoding, Changed, NotEnoughSpace, SaveFailed, Duplicate,
    PasswordIncorrect,
}
```

```csharp
public interface IGuideImportValidator
{
    Task<ImportInspection> InspectAsync(string fullPath, CancellationToken token);
    Task<TxtImportManifest> ResolveTxtEncodingAsync(
        ImportNeedsTxtEncoding inspection, int codePage, CancellationToken token);
    // The password is used for this check only; nothing keeps it.
    Task<PdfImportManifest> ResolvePdfPasswordAsync(
        ImportNeedsPdfPassword inspection, string password, CancellationToken token);
}
```

Fix any other `IGuideImportValidator` implementation the build reports
(test fakes) by adding a member that throws `NotSupportedException`.

- [ ] **Step 5: Implement the validator**

Change `ReadPdf` (keep it `internal static`) to take the password and return
the password step:

```csharp
    internal static ImportInspection ReadPdf(
        Stream stream, ImportSource source, string title, string fingerprint, CancellationToken token,
        string? password = null)
    {
        try
        {
            if (!StartsLikePdf(stream))
            {
                throw NotPdf();
            }
            stream.Position = 0;
            // PdfPig ignores the token, so the stream checks it on each read.
            using PdfDocument document = PdfDocument.Open(
                new CancellableReadStream(stream, token), new ParsingOptions { Password = password });
            int pages = document.NumberOfPages;
            if (pages == 0)
            {
                throw NotPdf();
            }
            bool hasText = false;
            for (int number = 1; number <= Math.Min(pages, TextSamplePages) && !hasText; number++)
            {
                token.ThrowIfCancellationRequested();
                hasText = document.GetPage(number).Text.Any(char.IsLetter);
            }
            return new ImportReady(new PdfImportManifest(
                source, title, pages, hasText, fingerprint, PasswordRequired: password is not null));
        }
        catch (PdfDocumentEncryptedException)
        {
            // PdfPig raises the same exception with no password and a wrong one.
            // The exception never carries the attempt.
            return password is null
                ? new ImportNeedsPdfPassword(source, title, fingerprint)
                : throw new GuideImportException(ImportIssue.PasswordIncorrect, ImportPresentation.PdfPasswordIncorrect);
        }
        catch (Exception error) when (error is not (GuideImportException or OperationCanceledException))
        {
            // PdfPig reports malformed files with several exception types.
            throw NotPdf();
        }
    }
```

`ParsingOptions` is `UglyToad.PdfPig.ParsingOptions`; add its `using` and
`DesktopGuides.Core.Import`'s `ImportPresentation` if missing. Note: an
unencrypted PDF opened with a password succeeds in PdfPig, which is why
`PasswordRequired` comes from the caller: only `ResolvePdfPasswordAsync`
passes one, and only after inspection asked for it.

Add after `ResolveTxtEncodingAsync`:

```csharp
    public async Task<PdfImportManifest> ResolvePdfPasswordAsync(
        ImportNeedsPdfPassword inspection, string password, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentException.ThrowIfNullOrEmpty(password);
        token.ThrowIfCancellationRequested();
        ImportSource source = inspection.Source;
        FileInfo file = new(source.FullPath);
        if (!file.Exists)
        {
            throw Missing(source.FileName);
        }
        if (file.Length != source.ByteCount ||
            file.LastWriteTimeUtc != source.LastWriteUtc.UtcDateTime)
        {
            throw Changed(source);
        }
        return await Task.Run(() =>
        {
            using FileStream stream = OpenSource(source.FullPath, source.FileName, asyncIo: false);
            string fingerprint;
            try
            {
                fingerprint = GuideFingerprint.OfStream(stream, token);
            }
            catch (IOException)
            {
                throw Unreadable(source.FileName);
            }
            // The size and time can survive an edit; the bytes can't.
            if (fingerprint != inspection.Fingerprint)
            {
                throw Changed(source);
            }
            stream.Position = 0;
            return ReadPdf(stream, source, inspection.SuggestedTitle, fingerprint, token, password) is
                ImportReady { Manifest: PdfImportManifest manifest } ? manifest : throw NotPdf();
        }, token).ConfigureAwait(false);
    }

    private static GuideImportException Changed(ImportSource source) =>
        new(ImportIssue.Changed, $"{source.FileName} changed after it was checked. Choose it again.");
```

Use `Changed(source)` in `ResolveTxtEncodingAsync` too, so the text stays in
one place.

- [ ] **Step 6: Implement the publisher check**

Replace `VerifyPdf`:

```csharp
    private static void VerifyPdf(string path, PdfImportManifest pdf, CancellationToken token)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ImportInspection staged = GuideImportValidator.ReadPdf(stream, pdf.Source, pdf.SuggestedTitle, pdf.Fingerprint, token);
        // A locked copy can't be read without its password, which isn't kept;
        // the fingerprint check before this one ties it to the checked bytes.
        bool matches = pdf.PasswordRequired
            ? staged is ImportNeedsPdfPassword
            : staged is ImportReady { Manifest: PdfImportManifest copy } && copy.PageCount == pdf.PageCount;
        if (!matches)
        {
            throw GuideImportValidator.NotPdf();
        }
    }
```

- [ ] **Step 7: Push and confirm GREEN**

Commit (`feat(import): T10.2 accept a password-protected PDF after its password`),
push, dispatch with `-f shell-scope=import -f dev-fast=true`, cancel after
`core-tests`.
Expected: `core-tests` passes. `Infrastructure.Tests` includes the five
validator tests and three publisher tests above. The installed
`import-pdf-locked` phase still expects the old message; Task 8 replaces it.

### Task 3: Loader password and bounded text-source close

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Reading/ManagedPdfGuideLoader.cs`
- Modify: `src/DesktopGuides.Infrastructure/Reading/PdfPageTextSource.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedPdfGuideLoaderTests.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/PdfPageTextSourceTests.cs`

**Interfaces:**
- Consumes: `PdfGuideLoadError.PasswordRequired`, `PasswordIncorrect` (Task 1);
  `PublisherHarness.InspectAsync(path, password:)` (Task 2).
- Produces:
  - `ManagedPdfGuideLoader.LoadAsync(Guide guide, string? password, CancellationToken token)`;
    the existing two-argument overload calls it with `null`.
  - `PdfPageTextSource.Open(Stream file, CancellationToken token, string? password = null, ...)`
    (the password is the third parameter, before the limits).
  - `PdfPageTextSource.CloseAsync(TimeSpan wait) → Task<bool>`: true when it
    closed within `wait`.

- [ ] **Step 1: Write the failing loader tests**

In `ManagedPdfGuideLoaderTests`, change `AnEncryptedCopyIsPasswordProtected`
to expect `PdfGuideLoadError.PasswordRequired` and rename it
`AnEncryptedCopyNeedsAPassword`. Add (using the file's `PublishAsync`,
`ManagedFile`, `LoadAsync` and `FailedAsync` helpers; publish a locked guide
with `harness.InspectAsync(source, password: "guide")` as in Task 2):

```csharp
    private const string WrongAttempt = "wrong-7Q2x";

    [Fact]
    public async Task ALockedGuideNeedsItsPassword()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLockedAsync(harness);

        PdfGuideLoad load = await Loader(harness).LoadAsync(guide, CancellationToken.None);

        Assert.Equal(PdfGuideLoadError.PasswordRequired, Assert.IsType<PdfGuideLoadFailed>(load).Error);
    }

    [Fact]
    public async Task AWrongPasswordIsIncorrectAndNotEchoed()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLockedAsync(harness);

        PdfGuideLoad load = await Loader(harness).LoadAsync(guide, WrongAttempt, CancellationToken.None);

        PdfGuideLoadFailed failed = Assert.IsType<PdfGuideLoadFailed>(load);
        Assert.Equal(PdfGuideLoadError.PasswordIncorrect, failed.Error);
        Assert.DoesNotContain(WrongAttempt, failed.ToString());
        // The managed copy is closed again: it can be opened for writing.
        using FileStream exclusive = new(ManagedFile(harness, guide), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task TheRightPasswordGivesThePageText()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLockedAsync(harness);

        PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(
            await Loader(harness).LoadAsync(guide, "guide", CancellationToken.None));
        using PdfPageTextSource text = loaded.Text;

        Assert.Equal(1, text.PageCount);
        Assert.Contains("Locked guide secret page", (await text.GetPageTextAsync(0, CancellationToken.None)).Text);
    }

    [Fact]
    public async Task AChangedLockedCopyIsStillChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLockedAsync(harness);
        string managed = ManagedFile(harness, guide);
        File.Delete(managed);
        Directory.CreateDirectory(managed);

        PdfGuideLoad load = await Loader(harness).LoadAsync(guide, "guide", CancellationToken.None);

        Assert.Equal(PdfGuideLoadError.Changed, Assert.IsType<PdfGuideLoadFailed>(load).Error);
    }
```

Add `PublishLockedAsync(harness)` beside the existing `PublishAsync` helper:
it copies `pdf-locked.pdf` into `harness.Sources`, publishes
`await harness.InspectAsync(source, password: "guide")`, and returns the
stored guide. Use the file's existing way to build the loader for
`Loader(harness)` (`new ManagedPdfGuideLoader(harness.Paths)`).
`AFailedLoadLeavesTheFileClosed` keeps `pdf-locked` and now covers the
`PasswordRequired` path.

- [ ] **Step 2: Write the failing close tests**

In `PdfPageTextSourceTests` (reuse `GatedStream`):

```csharp
    [Fact]
    public async Task CloseDuringAnExtractionReturnsAndClosesWhenItEnds()
    {
        GatedStream file = new(File.ReadAllBytes(P0Fixtures.Resolve(Long)));
        PdfPageTextSource source = PdfPageTextSource.Open(file, CancellationToken.None);
        file.Hold();
        Task<PdfPageText> reading = source.GetPageTextAsync(150, CancellationToken.None);
        Assert.True(await file.ReadSeen.WaitAsync(TimeSpan.FromSeconds(5)), "Extraction never read the file.");

        Task<bool> closing = source.CloseAsync(TimeSpan.FromMilliseconds(200));
        Assert.Same(closing, await Task.WhenAny(closing, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.False(await closing);
        Assert.False(file.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => source.GetPageTextAsync(0, CancellationToken.None));

        file.Release();
        await reading;
        await WaitUntil(() => file.Disposed, TimeSpan.FromSeconds(5));
        source.Dispose();
    }

    [Fact]
    public async Task CloseWithNoExtractionClosesAtOnce()
    {
        GatedStream file = new(File.ReadAllBytes(P0Fixtures.Resolve(Long)));
        PdfPageTextSource source = PdfPageTextSource.Open(file, CancellationToken.None);

        Assert.True(await source.CloseAsync(TimeSpan.Zero));
        Assert.True(file.Disposed);
        Assert.True(await source.CloseAsync(TimeSpan.Zero));
        source.Dispose();
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition never held.");
            await Task.Delay(20);
        }
    }
```

A `GetPageTextAsync` that starts after `CloseAsync` throws
`ObjectDisposedException`, because the close marks the source closing before
it waits. If the file already has a `WaitUntil` helper, use it instead.

- [ ] **Step 3: Push and confirm RED**

Commit the tests (`test(reading): T10.2 PDF passwords and bounded close`),
push, dispatch with `-f shell-scope=pdf -f dev-fast=true`, cancel after
`core-tests`.
Expected: compile errors on the three-argument `LoadAsync` and `CloseAsync`.

- [ ] **Step 4: Implement the text source**

```csharp
    // On success the source owns file; on failure the caller still does.
    // PdfPig decrypts on open, so the password isn't needed or kept afterwards.
    internal static PdfPageTextSource Open(Stream file, CancellationToken token, string? password = null,
        int maxPageCharacters = DefaultMaxPageCharacters, int maxPages = DefaultMaxPages,
        long maxCharacters = DefaultMaxCharacters)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPageCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, maxPageCharacters);
        CancellableReadStream reader = new(file, token);
        PdfDocument document = PdfDocument.Open(reader, new ParsingOptions { Password = password });
        reader.Token = CancellationToken.None;
        return new PdfPageTextSource(file, reader, document, maxPageCharacters, maxPages, maxCharacters);
    }
```

Existing callers that pass limits positionally must name them
(`maxPages: 2`); fix what the build reports in the tests.

Add a `closing` flag and the bounded close; `Dispose` keeps its wait:

```csharp
    private volatile bool closing;

    public async Task<PdfPageText> GetPageTextAsync(int pageIndex, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed || closing, this);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed || closing, this);
            if (TakeRecent(pageIndex) is PdfPageText hit) return hit;
            PdfPageText text = await Task.Run(() => Extract(pageIndex, token), token).ConfigureAwait(false);
            Remember(pageIndex, text);
            return text;
        }
        finally
        {
            gate.Release();
        }
    }

    // Waits at most `wait` for a running extraction. If it is still running,
    // returns false and closes the document when the extraction ends.
    public async Task<bool> CloseAsync(TimeSpan wait)
    {
        closing = true;
        if (await gate.WaitAsync(wait).ConfigureAwait(false))
        {
            try { CloseHeld(); } finally { gate.Release(); }
            return true;
        }
        _ = CloseWhenFreeAsync();
        return false;
    }

    private async Task CloseWhenFreeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { CloseHeld(); } finally { gate.Release(); }
    }

    // Waits for a running extraction, so the document is never closed under it.
    public void Dispose()
    {
        closing = true;
        gate.Wait();
        try { CloseHeld(); } finally { gate.Release(); }
    }

    private void CloseHeld()
    {
        if (disposed) return;
        disposed = true;
        recent.Clear();
        recentCharacters = 0;
        document.Dispose();
        file.Dispose();
    }
```

Format the `try`/`finally` blocks on separate lines to match the file.

- [ ] **Step 5: Implement the loader overload**

```csharp
    public Task<PdfGuideLoad> LoadAsync(Guide guide, CancellationToken token) =>
        LoadAsync(guide, null, token);

    // The password opens PdfPig once; it isn't stored, and a wrong one
    // reports PasswordIncorrect without echoing it.
    public async Task<PdfGuideLoad> LoadAsync(Guide guide, string? password, CancellationToken token)
```

Keep the body; change its last line to
`return await Task.Run(() => Open(path, password, token), token).ConfigureAwait(false);`.
In `Open(string path, string? password, CancellationToken token)`, pass the
password to `PdfPageTextSource.Open(file, token, password)` and change the
encrypted case:

```csharp
        catch (PdfDocumentEncryptedException)
        {
            file.Dispose();
            return Failed(password is null ? PdfGuideLoadError.PasswordRequired : PdfGuideLoadError.PasswordIncorrect);
        }
```

- [ ] **Step 6: Push and confirm GREEN**

Commit (`feat(reading): T10.2 open locked PDFs and close without waiting on a hung page`),
push, dispatch with `-f shell-scope=pdf -f dev-fast=true`, cancel after
`core-tests`.
Expected: `core-tests` passes, including the four loader tests, the renamed
encrypted test, the two close tests and the existing
`DisposeDuringAnExtractionWaitsForIt`.

### Task 4: Installed PDF harness (RED first)

This task writes every installed check before the app code exists, so each
mode is seen failing first. Tasks 5–8 turn it green.

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs:528-575` (`seed-pdf-reader`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the `ValidateSet` at line 3,
  the mode list at line 1201, `$textGame` at line 1206, `Wait-PdfPage` at
  line 1486, the `pdf-reader` mode at line 2048, and new modes after it)
- Modify: `tools/p1/windows_shell_install.ps1:1273-1361`
  (`Invoke-PdfReaderPass`, `Assert-PdfDiagnostics`, `Run-PdfReaderScenarios`)

**Interfaces:**
- Consumes (Task 2): `ImportNeedsPdfPassword`,
  `IGuideImportValidator.ResolvePdfPasswordAsync(ImportNeedsPdfPassword, string, CancellationToken) → Task<PdfImportManifest>`.
- Produces for Tasks 5–8:
  - `seed-pdf-reader` prints `{"pdfLong":"<N>","pdfLocked":"<N>","originals":"<path>"}`.
  - Smoke modes `pdf-jump`, `pdf-zoom`, `pdf-keys`, `pdf-locked` and
    `pdf-offline`, run by the installer's `pdf` group.
  - `Assert-NoPasswordTrace([string] $attempt)` in the installer, which
    throws on a match and returns the names of the cache files it couldn't
    read; Task 8 calls it for the import group.
  - The smoke's shared `Enter-Secret([string] $id, [string] $text)`, which
    Task 8 also uses.
  - The app must expose the AutomationIds and copy in Global Constraints.
    The toolbar commands are found by name (`Zoom in`, `Zoom out`,
    `Go to page`, `Fit to width`); *Go to page* and *Fit to width* are in the
    overflow.

- [ ] **Step 1: Seed from a temp copy and add the locked guide**

In `seed-pdf-reader`, after `string pdfFixtures = ...`, add:

```csharp
    // P8: imports read a temp copy of the fixtures, so the installer can
    // delete the originals before pdf-offline. The TXT guide is inserted
    // directly and needs no original.
    string originals = Path.Combine(
        Path.GetTempPath(), $"desktop-guides-pdf-originals-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path.Combine(originals, "generated"));
    foreach (string fixture in new[]
    {
        "pdf-access.pdf", "pdf-scan.pdf", "pdf-short.pdf", "pdf-locked.pdf",
        Path.Combine("generated", "pdf-long.pdf"),
    })
    {
        File.Copy(Path.Combine(pdfFixtures, fixture), Path.Combine(originals, fixture));
    }
```

In `PublishPdfAsync`, change `Path.Combine(pdfFixtures, fixture)` to
`Path.Combine(originals, fixture)`. After it, add:

```csharp
    // A locked fixture goes through the password step, as the dialog does.
    // "guide" is the fixture's public test password.
    async Task<Guid> PublishLockedPdfAsync(string fixture, string title, string password)
    {
        ImportInspection inspection = await pdfValidator.InspectAsync(
            Path.Combine(originals, fixture), CancellationToken.None);
        if (inspection is not ImportNeedsPdfPassword needs)
        {
            throw new InvalidOperationException(
                $"The {fixture} fixture didn't ask for a password: {inspection.GetType().Name}.");
        }
        PdfImportManifest manifest = await pdfValidator.ResolvePdfPasswordAsync(
            needs, password, CancellationToken.None);
        return await pdfPublisher.PublishAsync(
            manifest, pdfGame.Id, title, false, null, CancellationToken.None);
    }
```

After the `pdf-long` publish, add
`Guid pdfLocked = await PublishLockedPdfAsync("pdf-locked.pdf", "Locked PDF Guide", "guide");`.
Replace the final `Console.WriteLine` with:

```csharp
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        pdfLong = pdfLong.ToString("N"),
        pdfLocked = pdfLocked.ToString("N"),
        originals,
    }));
```

The guide list gains one row. `pdf-reader` selects guides by name, so its
phases don't change.

- [ ] **Step 2: Register the modes and match the status by prefix**

In `windows_shell_ui_smoke.ps1`:

1. Add `'pdf-jump', 'pdf-zoom', 'pdf-keys', 'pdf-locked', 'pdf-offline'` to
   the `ValidateSet` (line 3) beside `'pdf-reader'`, and to the
   `$Mode -in @(...)` list at line 1201.
2. At line 1206, change `elseif ($Mode -eq 'pdf-reader')` to
   `elseif ($Mode -like 'pdf-*')`.
3. After `Wait-FocusedId` (line 530), add the shared password helper. It
   sits with the shared helpers because Task 8's `import-preview` mode
   uses it too:

```powershell
    # A PasswordBox exposes no readable value, so the attempt is typed.
    # Callers pass only the fixture's test passwords, which hold no SendKeys
    # metacharacters.
    function Enter-Secret([string] $id, [string] $text) {
        (Wait-VisibleById $id).SetFocus()
        Wait-FocusedId $id
        [System.Windows.Forms.SendKeys]::SendWait($text)
    }
```

4. Replace `Wait-PdfPage` (line 1486) and add the helpers after it:

```powershell
        # P6: the status reads "Page N of M <dot> <zoom>", so pages match by
        # prefix and the zoom by suffix.
        $pdfDot = ' ' + [char]0x00B7 + ' '

        # Waits until the page status, the preview's name and the text all
        # show one page.
        function Wait-PdfPage([int] $page, [int] $count, [string] $text, [int] $seconds = 15) {
            $label = "Page $page of $count"
            $deadline = (Get-Date).AddSeconds($seconds)
            do {
                try {
                    $status = Find-ById 'PdfPageStatus'
                    $preview = Find-ById 'PdfPreviewImage'
                    if ($status -and $preview -and $status.Current.Name.StartsWith($label + $pdfDot) -and
                        $preview.Current.Name -eq "$label preview") {
                        $read = Get-PdfText
                        if ($read -and $read.Contains($text)) { return $preview }
                    }
                }
                catch [System.Windows.Automation.ElementNotAvailableException] {
                    # The view replaced the element mid-read.
                }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "Expected $label with its preview, and text containing '$text'."
        }

        # Waits for the status's zoom to match $pattern (a regex) and differ
        # from $not, and returns it.
        function Wait-PdfZoom([string] $pattern, [string] $not = '', [int] $seconds = 15) {
            $regex = '^Page \d+ of \d+' + [regex]::Escape($pdfDot) + "($pattern)$"
            $seen = ''
            $deadline = (Get-Date).AddSeconds($seconds)
            do {
                try {
                    $status = Find-ById 'PdfPageStatus'
                    if ($status) {
                        $seen = $status.Current.Name
                        $match = [regex]::Match($seen, $regex)
                        if ($match.Success -and $match.Groups[1].Value -ne $not) {
                            return $match.Groups[1].Value
                        }
                    }
                }
                catch [System.Windows.Automation.ElementNotAvailableException] {
                }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "Expected the PDF zoom to match '$pattern'; the status read '$seen'."
        }

        function Find-VisibleName([string] $name) {
            $element = Find-ByName $name
            if ($element -and -not $element.Current.IsOffscreen) { return $element }
            return $null
        }

        # Go to page and Fit to width sit in the CommandBar overflow.
        function Invoke-OverflowCommand([string] $name) {
            $more = $null
            foreach ($candidate in @('More', 'More options', 'More commands', 'Show more', 'See more')) {
                $more = Find-VisibleName $candidate
                if ($more) { break }
            }
            if (-not $more) { throw 'The Reader toolbar has no visible overflow button.' }
            Invoke-Element $more
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $command = Find-VisibleName $name
                if ($command) { Invoke-Element $command; return }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "The Reader toolbar overflow has no visible '$name'."
        }

        function Wait-FocusedName([string] $name) {
            $deadline = (Get-Date).AddSeconds(10)
            do {
                $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
                if ($focused -and $focused.Current.Name -eq $name) { return }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "Keyboard focus did not reach '$name'."
        }

        function Submit-PageNumber([string] $value) {
            $box = Wait-VisibleById 'ReaderCommandInput'
            $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
            $go = Find-VisibleName 'Go'
            if (-not $go) { throw 'The Go to page dialog has no visible Go.' }
            Invoke-Element $go
        }

        # A refused entry keeps the dialog open with the range message, and
        # the Reader stays on its page.
        function Assert-PageRefused([string] $value, [string] $message, [int] $page, [int] $count) {
            Submit-PageNumber $value
            $line = ''
            $deadline = (Get-Date).AddSeconds(5)
            do {
                Start-Sleep -Milliseconds 100
                $box = Find-ById 'ReaderCommandInput'
                if (-not $box -or $box.Current.IsOffscreen) {
                    throw "The Go to page dialog closed after '$value'."
                }
                $shown = Find-ById 'ReaderCommandError'
                if ($shown -and -not $shown.Current.IsOffscreen) { $line = $shown.Current.Name }
            } while ($line -ne $message -and (Get-Date) -lt $deadline)
            if ($line -ne $message) {
                throw "After '$value' the dialog showed '$line'; expected '$message'."
            }
            [void](Wait-PdfPage $page $count "page $page of $count")
        }

        function Open-PdfGame {
            [void](Wait-Name 'LibraryHeading' 'Library')
            Resize-ShellWindow 1500 720
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')
        }
```

4. In the `pdf-reader` mode, replace its first five lines (from
   `Wait-Name 'LibraryHeading'` to `Wait-Status 'Game ready.'`) with
   `Open-PdfGame`. In `pdf-scan`, replace
   `[void](Wait-Name 'PdfPageStatus' 'Page 1 of 1')` with
   `[void](Wait-PdfZoom 'Fit width')`; the scan has no text, so
   `Wait-PdfPage` can't be used there.

- [ ] **Step 3: Write `pdf-jump`**

Add after the `pdf-reader` branch:

```powershell
        elseif ($Mode -eq 'pdf-jump') {
            # TR10.1: Go to page refuses 0, M+1, blank and non-whole entries
            # with the range message and doesn't move; 150 jumps to its top.
            Open-PdfGame
            Open-TextGuide 'Long PDF Guide'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            Invoke-OverflowCommand 'Go to page'
            $range = 'Enter a page from 1 to 200.'
            foreach ($value in @('0', '201', '', '1.5', 'x')) {
                Assert-PageRefused $value $range 1 200
            }
            $report.pdfJumpScreenshot = Save-WindowScreenshot 'pdf-jump'
            Submit-PageNumber '150'
            [void](Wait-PdfPage 150 200 'page 150 of 200')
            Start-Sleep -Milliseconds 300
            $top = Get-PdfFraction
            if ($top -ge 0.02) {
                throw "Page 150 opened at fraction $([Math]::Round($top, 3)); expected its top."
            }
            # Started from the overflow, so focus returns to the command.
            Wait-FocusedName 'Go to page'
            $report.phases += 'pdf-jump'
            Back-ToTextGame
        }
```

- [ ] **Step 4: Write `pdf-zoom`**

```powershell
        elseif ($Mode -eq 'pdf-zoom') {
            # A point 30% down page 121 survives Zoom in, Zoom in, Zoom out and
            # Fit (T10.3 tolerance). A percent gives landscape page 122 a wider
            # image than portrait page 121; Fit gives both the viewport.
            Open-PdfGame
            Open-TextGuide 'Long PDF Guide'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            [void](Wait-PdfZoom 'Fit width')
            if ((Get-PdfScroll).Current.HorizontallyScrollable) {
                throw 'At Fit width the page scrolls sideways.'
            }
            Invoke-NextPages 120
            [void](Wait-PdfPage 121 200 'page 121 of 200' 60)
            $scroll = Get-PdfScroll
            $room = 1 - $scroll.Current.VerticalViewSize / 100
            if (-not $scroll.Current.VerticallyScrollable -or $room -lt 0.3) {
                throw "Page 121 scrolls only $([Math]::Round($room, 3)) of its height; pdf-zoom needs 0.3."
            }
            $scroll.SetScrollPercent(
                [System.Windows.Automation.ScrollPattern]::NoScroll, 0.3 / $room * 100)
            Start-Sleep -Milliseconds 300
            $report.pdfZoom = [ordered]@{ set = Get-PdfFraction }

            Invoke-ReaderCommand 'Zoom in'
            $first = Wait-PdfZoom '\d+%'
            $report.pdfZoom[$first] = Wait-PdfFraction 0.3 "Zoom in to $first"
            [void](Wait-PdfPage 121 200 'page 121 of 200')
            Invoke-ReaderCommand 'Zoom in'
            $second = Wait-PdfZoom '\d+%' $first
            if ([int] $second.TrimEnd('%') -le [int] $first.TrimEnd('%')) {
                throw "Zoom in went from $first to $second."
            }
            $report.pdfZoom[$second] = Wait-PdfFraction 0.3 "Zoom in to $second"
            [void](Wait-PdfPage 121 200 'page 121 of 200')
            $report.pdfZoomScreenshot = Save-WindowScreenshot 'pdf-zoom'
            Invoke-ReaderCommand 'Zoom out'
            $back = Wait-PdfZoom '\d+%' $second
            if ($back -ne $first) { throw "Zoom out from $second went to $back; expected $first." }
            [void](Wait-PdfFraction 0.3 "Zoom out to $back")
            [void](Wait-PdfPage 121 200 'page 121 of 200')
            Invoke-OverflowCommand 'Fit to width'
            [void](Wait-PdfZoom 'Fit width')
            $report.pdfZoom.fit = Wait-PdfFraction 0.3 'Fit to width'
            [void](Wait-PdfPage 121 200 'page 121 of 200')

            # 200%: the page is wider than the viewport, so it scrolls sideways.
            $zoom = 'Fit width'
            for ($i = 0; $i -lt 8 -and $zoom -ne '200%'; $i++) {
                Invoke-ReaderCommand 'Zoom in'
                $zoom = Wait-PdfZoom '\d+%' $zoom
            }
            if ($zoom -ne '200%') { throw "Zoom in never reached 200%; it stopped at $zoom." }
            [void](Wait-PdfPage 121 200 'page 121 of 200')
            Start-Sleep -Milliseconds 300
            $portrait = Get-PdfScroll
            if (-not $portrait.Current.HorizontallyScrollable) {
                throw 'At 200% page 121 does not scroll sideways.'
            }
            $portraitView = $portrait.Current.HorizontalViewSize
            Invoke-NextPages 1
            [void](Wait-PdfPage 122 200 'page 122 of 200')
            [void](Wait-PdfZoom '200%')
            Start-Sleep -Milliseconds 300
            $landscapeView = (Get-PdfScroll).Current.HorizontalViewSize
            $report.pdfZoom.horizontalViewSize = [ordered]@{ page121 = $portraitView; page122 = $landscapeView }
            if ($landscapeView -ge 0.95 * $portraitView) {
                throw ("At 200% landscape page 122 shows $([Math]::Round($landscapeView, 1))% of its width " +
                    "and portrait page 121 $([Math]::Round($portraitView, 1))%; the landscape page should be wider.")
            }

            # 400% is the last step, so Zoom in turns off there.
            for ($i = 0; $i -lt 4 -and $zoom -ne '400%'; $i++) {
                Invoke-ReaderCommand 'Zoom in'
                $zoom = Wait-PdfZoom '\d+%' $zoom
            }
            if ($zoom -ne '400%') { throw "Zoom in never reached 400%; it stopped at $zoom." }
            $deadline = (Get-Date).AddSeconds(5)
            while ((Find-VisibleName 'Zoom in').Current.IsEnabled -and (Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 100
            }
            if ((Find-VisibleName 'Zoom in').Current.IsEnabled) { throw 'Zoom in stayed enabled at 400%.' }
            Invoke-OverflowCommand 'Fit to width'
            [void](Wait-PdfZoom 'Fit width')
            Start-Sleep -Milliseconds 300
            if ((Get-PdfScroll).Current.HorizontallyScrollable) {
                throw 'Back at Fit width page 122 still scrolls sideways.'
            }
            $report.phases += 'pdf-zoom'
            Back-ToTextGame
        }
```

- [ ] **Step 5: Write `pdf-keys`**

```powershell
        elseif ($Mode -eq 'pdf-keys') {
            # Every key in the design's table runs its command and leaves focus
            # where it was. Page keys stay with a focused text box; Ctrl+G and
            # the zoom keys still work there.
            function Send-ReaderKeys([string] $keys, [string] $focusId) {
                [System.Windows.Forms.SendKeys]::SendWait($keys)
                Start-Sleep -Milliseconds 150
                [void](Wait-FocusedId $focusId)
            }

            # Gives a page turn time to land, then checks none did.
            function Assert-PdfStays([int] $page, [int] $count, [string] $context) {
                Start-Sleep -Milliseconds 700
                $name = (Find-ById 'PdfPageStatus').Current.Name
                if (-not $name.StartsWith("Page $page of $count" + $pdfDot)) {
                    throw "$context moved the Reader: the status reads '$name'."
                }
            }

            Open-PdfGame
            Open-TextGuide 'Long PDF Guide'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            (Find-ById 'PdfPreviewScroller').SetFocus()
            [void](Wait-FocusedId 'PdfPreviewScroller')

            $preview = 'PdfPreviewScroller'
            Send-ReaderKeys '{PGDN}' $preview
            [void](Wait-PdfPage 2 200 'page 2 of 200')
            Send-ReaderKeys '{PGUP}' $preview
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            Send-ReaderKeys '^{END}' $preview
            [void](Wait-PdfPage 200 200 'page 200 of 200')
            Send-ReaderKeys '^{HOME}' $preview
            [void](Wait-PdfPage 1 200 'page 1 of 200')

            Send-ReaderKeys '^=' $preview
            $first = Wait-PdfZoom '\d+%'
            Send-ReaderKeys '^{ADD}' $preview
            $second = Wait-PdfZoom '\d+%' $first
            Send-ReaderKeys '^-' $preview
            [void](Wait-PdfZoom ([regex]::Escape($first)))
            Send-ReaderKeys '^{SUBTRACT}' $preview
            $lower = Wait-PdfZoom '(\d+%|Fit width)' $first
            Send-ReaderKeys '^0' $preview
            [void](Wait-PdfZoom 'Fit width')
            $report.pdfKeys = [ordered]@{ zoomIn = $first; zoomInAgain = $second; zoomOutTwice = $lower }

            # Ctrl+G opens the dialog. Page Down there stays in its box; Esc
            # closes it, and focus goes to the preview.
            [System.Windows.Forms.SendKeys]::SendWait('^g')
            [void](Wait-FocusedId 'ReaderCommandInput')
            [System.Windows.Forms.SendKeys]::SendWait('{PGDN}')
            Assert-PdfStays 1 200 'Page Down in the Go to page box'
            [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
            [void](Wait-FocusedId $preview)
            Assert-Absent 'ReaderCommandInput'
            # A keyboard jump also returns focus to the preview.
            [System.Windows.Forms.SendKeys]::SendWait('^g')
            [void](Wait-FocusedId 'ReaderCommandInput')
            Submit-PageNumber '150'
            [void](Wait-PdfPage 150 200 'page 150 of 200')
            [void](Wait-FocusedId $preview)

            # In the read-only page text, Page Down scrolls the text, not the
            # page; Ctrl+= still zooms.
            (Find-ById 'PdfDocumentText').SetFocus()
            [void](Wait-FocusedId 'PdfDocumentText')
            Send-ReaderKeys '{PGDN}' 'PdfDocumentText'
            Assert-PdfStays 150 200 'Page Down in the page text'
            Send-ReaderKeys '^{END}' 'PdfDocumentText'
            Assert-PdfStays 150 200 'Ctrl+End in the page text'
            Send-ReaderKeys '^=' 'PdfDocumentText'
            [void](Wait-PdfZoom '\d+%')
            Send-ReaderKeys '^0' 'PdfDocumentText'
            [void](Wait-PdfZoom 'Fit width')
            $report.phases += 'pdf-keys'
            Back-ToTextGame
        }
```

- [ ] **Step 6: Write `pdf-locked` and `pdf-offline`**

```powershell
        elseif ($Mode -in @('pdf-locked', 'pdf-offline')) {
            $needsPassword = 'This PDF needs a password.'
            $wrongPassword = "That password didn't open this PDF. Try again."

            # The panel is up, focus is in the empty box, and no page shows.
            function Wait-UnlockPanel {
                [void](Wait-VisibleById 'PdfUnlockPanel')
                if (-not (Find-VisibleName $needsPassword)) {
                    throw "The unlock panel doesn't say '$needsPassword'."
                }
                [void](Wait-FocusedId 'PdfPasswordInput')
                # P13: Unlock is enabled only when the box has text.
                if ((Wait-VisibleById 'PdfUnlockButton').Current.IsEnabled) {
                    throw 'Unlock was enabled with an empty password box.'
                }
                Assert-Absent 'PdfDocumentText'
                Assert-Absent 'ReaderLoadError'
            }

            # "guide" is the fixture's public test password.
            function Unlock-LockedGuide {
                Enter-Secret 'PdfPasswordInput' 'guide'
                Invoke-Element (Wait-EnabledById 'PdfUnlockButton')
                [void](Wait-Status 'Guide ready.')
                [void](Wait-PdfPage 1 1 'Locked guide secret page')
                Assert-Absent 'PdfUnlockPanel'
                [void](Wait-FocusedId 'PdfPreviewScroller')
            }

            Open-PdfGame
            if ($Mode -eq 'pdf-locked') {
                Open-TextGuide 'Locked PDF Guide'
                Wait-UnlockPanel
                $report.pdfLockedScreenshot = Save-WindowScreenshot 'pdf-locked'

                # A wrong attempt explains itself and leaves an empty box with
                # focus, ready for the next try. Enter submits.
                Enter-Secret 'PdfPasswordInput' 'wrong-7Q2x'
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                [void](Wait-Name 'PdfUnlockError' $wrongPassword)
                [void](Wait-FocusedId 'PdfPasswordInput')
                if ((Wait-VisibleById 'PdfUnlockButton').Current.IsEnabled) {
                    throw 'The password box kept the wrong attempt: Unlock is still enabled.'
                }
                Assert-Absent 'PdfDocumentText'
                $report.pdfLockedWrongScreenshot = Save-WindowScreenshot 'pdf-locked-wrong'

                Unlock-LockedGuide
                $report.phases += 'pdf-locked'

                # The password isn't remembered: reopening asks again.
                Back-ToTextGame
                Open-TextGuide 'Locked PDF Guide'
                Wait-UnlockPanel
                if (Find-VisibleName $wrongPassword) { throw 'A reopened guide showed the last error.' }
                $report.phases += 'pdf-locked-reopen'
                Back-ToTextGame
            }
            else {
                # TR10.2: the installer deleted the originals and relaunched;
                # each guide opens from its managed copy with no remote
                # connection.
                foreach ($guide in @(
                    @{ name = 'Tagged PDF Guide'; page = 1; count = 1; text = 'Tagged guide paragraph for Narrator' },
                    @{ name = 'Long PDF Guide'; page = 1; count = 200; text = 'page 1 of 200' })) {
                    Open-TextGuide $guide.name
                    [void](Wait-Status 'Guide ready.')
                    [void](Wait-PdfPage $guide.page $guide.count $guide.text)
                    Assert-NoRemoteConnections "PDF reader for $($guide.name)"
                    Back-ToTextGame
                }
                Open-TextGuide 'Locked PDF Guide'
                Wait-UnlockPanel
                Unlock-LockedGuide
                Assert-NoRemoteConnections 'PDF reader for Locked PDF Guide'
                $report.phases += 'pdf-offline'
                Back-ToTextGame
            }
        }
```

- [ ] **Step 7: Run each mode in its own launch, scan for the attempt, and go offline**

In `windows_shell_install.ps1`:

1. `Invoke-PdfReaderPass` gains a mode:

```powershell
function Invoke-PdfReaderPass([string] $resultName, [string] $mode = 'pdf-reader') {
```

   and its smoke call becomes
   `$report.pdfReader[$resultName] = Run-ShellSmoke $mode -ResultName $resultName`.

2. In `Assert-PdfDiagnostics`, add `'abandonedExtraction'` to the field list,
   and after the `disposedCleanly` check:

```powershell
    if ($counts.abandonedExtraction -isnot [bool] -or $counts.abandonedExtraction -ne $false) {
        throw "The $pass pass abandoned a page-text extraction when it closed the long PDF guide."
    }
```

3. Add after `Assert-PdfDiagnostics`:

```powershell
# P7: every PDF a pass opened closed cleanly, with no extraction left behind.
function Assert-PdfClosedCleanly([string] $pass, [string] $diagnostics) {
    $files = @(Get-ChildItem -LiteralPath $diagnostics -Filter 'pdf-*.json' -File -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { throw "The $pass pass wrote no PDF diagnostics." }
    foreach ($file in $files) {
        $counts = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ($counts.disposedCleanly -isnot [bool] -or $counts.disposedCleanly -ne $true) {
            throw "The $pass pass did not close $($file.Name) cleanly."
        }
        if ($counts.abandonedExtraction -isnot [bool] -or $counts.abandonedExtraction -ne $false) {
            throw "The $pass pass abandoned a page-text extraction in $($file.Name)."
        }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $ResultDirectory "$pass.$($file.Name)")
    }
}

# Review Focus 1: the wrong attempt reaches no file the app wrote, in ASCII
# or UTF-16. Bytes are compared through Latin-1, which maps each byte to one
# character.
function Assert-NoPasswordTrace([string] $attempt) {
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $needles = @(
        $latin1.GetString([System.Text.Encoding]::ASCII.GetBytes($attempt)),
        $latin1.GetString([System.Text.Encoding]::Unicode.GetBytes($attempt)))
    $unreadable = @()
    foreach ($root in @($dataRoot, (Get-HtmlCacheRoot))) {
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue)) {
            try { $text = $latin1.GetString([System.IO.File]::ReadAllBytes($file.FullName)) }
            catch {
                # WebView2 can hold a lock file in the cache root; the app
                # data must scan fully.
                if ($file.FullName.StartsWith($dataRoot, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Couldn't scan $($file.Name) in the app data for the password attempt."
                }
                $unreadable += $file.Name
                continue
            }
            foreach ($needle in $needles) {
                if ($text.IndexOf($needle, [StringComparison]::Ordinal) -ge 0) {
                    throw "The wrong password attempt was written to $($file.Name)."
                }
            }
        }
    }
    # The comma keeps an empty list a list.
    return ,$unreadable
}

# P8: deletes only the folder seed-pdf-reader printed, directly under %TEMP%.
function Remove-PdfOriginals([string] $path) {
    $temp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')
    $full = [System.IO.Path]::GetFullPath($path)
    $leaf = Split-Path -Leaf $full
    if ((Split-Path -Parent $full).TrimEnd('\') -ne $temp -or
        $leaf -notmatch '^desktop-guides-pdf-originals-[0-9a-f]{32}$') {
        throw "Refusing to delete '$full': it isn't the PDF seed's originals folder."
    }
    Remove-Item -LiteralPath $full -Recurse -Force
    if (Test-Path -LiteralPath $full) { throw "The PDF originals at '$full' are still there." }
}
```

4. Replace the `try` block of `Run-PdfReaderScenarios` (keep the fixture
   check, the seed call and `$diagnostics`):

```powershell
    $report.pdfReader = [ordered]@{ pdfLong = $ids.pdfLong; pdfLocked = $ids.pdfLocked }
    $originalTheme = Get-AppThemePreference
    try {
        foreach ($theme in @(@{ name = 'light'; light = $true }, @{ name = 'dark'; light = $false })) {
            Set-AppThemePreference $theme.light
            foreach ($mode in @('pdf-reader', 'pdf-zoom', 'pdf-keys', 'pdf-jump', 'pdf-locked')) {
                $pass = "$mode-$($theme.name)"
                Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
                Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
                Invoke-PdfReaderPass $pass $mode
                if ($mode -eq 'pdf-reader') {
                    $report.pdfReader["$pass-diagnostics"] = Assert-PdfDiagnostics $pass $diagnostics $ids.pdfLong
                }
                Assert-PdfClosedCleanly $pass $diagnostics
            }
        }
        $report.pdfReader.passwordScanUnreadable = Assert-NoPasswordTrace 'wrong-7Q2x'

        # TR10.2: with the originals gone, a fresh launch reads the managed copies.
        Remove-PdfOriginals $ids.originals
        Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
        Invoke-PdfReaderPass 'pdf-offline' 'pdf-offline'
        Assert-PdfClosedCleanly 'pdf-offline' $diagnostics
    }
    finally {
        Restore-AppThemePreference $originalTheme
        if ($ids.originals -and (Test-Path -LiteralPath $ids.originals)) {
            Remove-PdfOriginals $ids.originals
        }
    }
```

The order runs `pdf-zoom` and `pdf-keys` before `pdf-jump` and `pdf-locked`,
so after Task 5 the first failure is in the session's zoom (the toolbar's end
state), not in the dialog or the unlock panel, which arrive in Tasks 6 and 7.

Update the function's header comment to list the new modes. The pass names
`pdf-reader-light` and `pdf-reader-dark` are unchanged, so earlier evidence
names still match.

- [ ] **Step 8: Check ASCII, commit, and confirm RED**

Run: `LC_ALL=C grep -n "$(printf '[\200-\377]')" /Users/ilya.lissoboi/work/desktop-guides/tools/p1/windows_shell_ui_smoke.ps1 /Users/ilya.lissoboi/work/desktop-guides/tools/p1/windows_shell_install.ps1`
Expected: no output.

Commit (`test(pdf): T10.2 installed checks for jump, zoom, keys, passwords and offline`),
push, dispatch with `-f shell-scope=pdf -f dev-fast=true`.
Expected: `core-tests` passes; `dev-production-shell-ui` seeds (the locked
guide goes through `ResolvePdfPasswordAsync`) and then fails in
`pdf-reader-light` with
`Expected Page 1 of 1 with its preview, and text containing 'Tagged guide paragraph for Narrator'.`,
because the status has no zoom yet. A failure in the seed or in an earlier
step is a harness bug: fix it before Task 5. Record the run ID for the
design doc's verification.

### Task 5: Session zoom, jump, password open and bounded close

The session and view have no headless tests (they need a WinUI host), so
Task 4's installed modes are this task's failing tests. After this task
`pdf-reader` passes in both themes and `pdf-zoom` gets as far as its 400%
end check, which needs the toolbar work in Task 6.

**Files:**
- Modify: `src/DesktopGuides.Production/PdfReaderView.xaml`
- Modify: `src/DesktopGuides.Production/PdfReaderView.xaml.cs`
- Modify: `src/DesktopGuides.Production/PdfReaderSession.cs`

**Interfaces:**
- Consumes: `PdfZoom`, `PageEntry.RangeMessage`,
  `PdfLocationRules.IsPageInRange`, `PdfSessionDiagnostics(..., bool AbandonedExtraction)`
  (Task 1); `PdfPageTextSource.CloseAsync(TimeSpan) → Task<bool>` (Task 3).
- Produces for Tasks 6–7:
  - `public readonly record struct PdfZoomState(string Label, bool CanZoomIn, bool CanZoomOut)`
    in `DesktopGuides.Production` (top of `PdfReaderSession.cs`).
  - `PdfReaderSession.ZoomState` (`PdfZoomState`), `event EventHandler? ZoomChanged`,
    `int PageCount`, and
    `Task OpenAsync(ManagedGuideSource source, string? password, CancellationToken token)`
    (the two-argument overload passes `null`).
  - `PdfReaderSession.Capabilities` is `PageNavigation | PageJump | FitWidth | Zoom`.
  - `PdfReaderView.FocusPreview() → bool`.
  - `PageJumpAction` out of range throws `ArgumentOutOfRangeException` whose
    message starts with `Enter a page from 1 to M.`

- [ ] **Step 1: Confirm the RED run**

Task 4's last run is this task's RED: `pdf-reader-light` failed on the
missing zoom label. No new run is needed.

- [ ] **Step 2: View: live status, page width and focus**

In `PdfReaderView.xaml`, add `AutomationProperties.LiveSetting="Polite"` to
`PageStatus`, and `IsTabStop="True"` to `PreviewScroller` so it can hold
focus for the keys (a ScrollViewer isn't a tab stop by default).

In `PdfReaderView.xaml.cs`, add `using Microsoft.UI.Xaml.Automation.Peers;`
and replace `ShowPage`'s first two lines, so the signature takes the label:

```csharp
    public void ShowPage(int index, int count, string zoomLabel, ImageSource? image, PdfPageText? text)
    {
        string page = $"Page {index + 1} of {count}";
        ShowStatus($"{page} \u00B7 {zoomLabel}");
```

and keep the rest of the method. Add:

```csharp
    // Fit (NaN) fills the viewport with no horizontal scroll; a percent sets
    // the image's width and lets the page scroll sideways when it's wider.
    public void SetPageWidth(double width)
    {
        Preview.Width = width;
        PreviewScroller.HorizontalScrollBarVisibility =
            double.IsNaN(width) ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    public bool FocusPreview() => PreviewScroller.Focus(FocusState.Programmatic);

    // A polite live region announces page and zoom changes without moving
    // focus. It's raised only when the text changes, so a re-render of the
    // same page at the same zoom stays quiet.
    private void ShowStatus(string status)
    {
        if (PageStatus.Text == status) return;
        PageStatus.Text = status;
        FrameworkElementAutomationPeer.FromElement(PageStatus)
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
```

`Clear()` also calls `SetPageWidth(double.NaN)`.

- [ ] **Step 3: Session: state, capabilities and the password open**

In `PdfReaderSession.cs`, above the class:

```csharp
// What the toolbar shows for zoom: the status label and which directions
// have a step left on the shown page.
public readonly record struct PdfZoomState(string Label, bool CanZoomIn, bool CanZoomOut);
```

Replace the `WrongPassword` comment with
`// ERROR_WRONG_PASSWORD after PdfPig accepted the password (P12): an engine split, reported as PasswordProtected.`
Add fields after `appliedAspect`:

```csharp
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(2);
    private PdfZoom zoom = PdfZoom.Fit;
    // The shown page's natural width in DIPs (P1); NaN before the first page.
    private double appliedNatural = double.NaN;
    // The page a zoom or resize re-renders. Its Apply isn't reader movement,
    // so it raises no LocationChanged and schedules no progress save.
    private int relayoutOf = -1;
```

Change `BothFailed` to `new(null, null, 0, double.NaN, double.NaN)`, and the
members after `Format`:

```csharp
    public ReaderCapabilities Capabilities =>
        ReaderCapabilities.PageNavigation | ReaderCapabilities.PageJump |
        ReaderCapabilities.FitWidth | ReaderCapabilities.Zoom;
    public int PageCount => pageCount;
    public PdfZoomState ZoomState
    {
        get
        {
            double fit = FitPercent;
            bool known = double.IsFinite(fit) && fit > 0;
            return new(zoom.Label, known && zoom.CanZoomIn(fit), known && zoom.CanZoomOut(fit));
        }
    }
    public event EventHandler? ZoomChanged;

    // The shown page's fit width as a percent of its natural width; NaN
    // before layout or before a page is shown.
    private double FitPercent =>
        View.PreviewWidth >= 1 && double.IsFinite(appliedNatural) && appliedNatural > 0
            ? View.PreviewWidth / appliedNatural * 100
            : double.NaN;
```

Replace the `OpenAsync` signature and its load line:

```csharp
    public Task OpenAsync(ManagedGuideSource source, CancellationToken token) =>
        OpenAsync(source, null, token);

    // The password is a parameter only: Windows.Data.Pdf uses it once, and
    // the session never stores it. PdfPig already opened with it (P3).
    public async Task OpenAsync(ManagedGuideSource source, string? password, CancellationToken token)
```

```csharp
            document = await (password is null
                ? WinPdf.PdfDocument.LoadFromStreamAsync(stream)
                : WinPdf.PdfDocument.LoadFromStreamAsync(stream, password)).AsTask(token);
```

Add `ZoomChanged = null;` after `Failed = null;` in `DisposeAsync`.

- [ ] **Step 4: Session: jump and zoom actions**

Add cases to `ExecuteAsync`'s switch, before `default`:

```csharp
            // The count comes from the opened document, never the locator.
            case PageJumpAction jump:
                if (!PdfLocationRules.IsPageInRange(jump.PageNumber, pageCount))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(action), jump.PageNumber, PageEntry.RangeMessage(pageCount));
                }
                GoTo(jump.PageNumber - 1, 0);
                break;
            case FitWidthAction:
                SetZoom(PdfZoom.Fit);
                break;
            // Before the first page is laid out there's no fit percent, so a
            // zoom does nothing rather than guess.
            case ZoomAction step when double.IsFinite(FitPercent):
                SetZoom(step.Factor > 1 ? zoom.In(FitPercent) : step.Factor < 1 ? zoom.Out(FitPercent) : zoom);
                break;
            case ZoomAction:
                break;
```

In `GoTo`, inside `if (page != target)`, add `relayoutOf = -1;` before
`scheduler.Request(page);`, so a turn after a zoom is reader movement again.

Add after `GoTo`:

```csharp
    private void SetZoom(PdfZoom next)
    {
        if (next == zoom) return;
        zoom = next;
        Relayout();
        RaiseZoomChanged();
    }

    // Sets the new width now, so the old bitmap stretches to it while the
    // sharper one renders; the layout guard then puts the point back.
    private void Relayout()
    {
        View.SetPageWidth(DisplayWidth(View.PreviewWidth, appliedNatural));
        relayoutOf = target == position.Page ? target : -1;
        scheduler.Request(target);
    }

    // NaN means fit: the view fills the viewport.
    private double DisplayWidth(double viewport, double natural) =>
        zoom.IsFit || !double.IsFinite(natural) || natural <= 0
            ? double.NaN
            : zoom.WidthFor(viewport >= 1 ? viewport : natural, natural);

    private void RaiseZoomChanged()
    {
        try
        {
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Like LocationChanged: a handler must not escape into a XAML event.
        }
    }
```

- [ ] **Step 5: Session: render at the zoom width and apply it**

The records gain the page's natural width:

```csharp
    private sealed record Raster(BitmapImage? Image, int Width, double Aspect, double NaturalWidth);

    private sealed record PageResult(
        PdfPageText? Text, BitmapImage? Image, int RasterWidth, double Aspect, double NaturalWidth);
```

`LoadPageAsync` returns
`new PageResult(await textTask, raster.Image, raster.Width, raster.Aspect, raster.NaturalWidth)`.
In `RenderAsync`, add `double natural = double.NaN;` beside `aspect`, set
`natural = page.Size.Width;` after `aspect`, and pass `natural` as the last
argument of every `new Raster(...)`, including the `catch`.

Replace `WidthFor`. The zoom width goes through `PdfRasterBudget`, so 400%
of a large page stays under the 4096 px cap and the cache budget:

```csharp
    // Before layout the preview has no width, so the page's own width stands in.
    private PdfRasterWidth WidthFor(double pageWidth, double aspect)
    {
        double viewport = View.PreviewWidth >= 1 ? View.PreviewWidth : pageWidth;
        double display = zoom.IsFit ? viewport : zoom.WidthFor(viewport, pageWidth);
        double scale = View.XamlRoot?.RasterizationScale ?? 1;
        return PdfRasterBudget.WidthFor(display * scale, aspect, PdfRasterBudget.MaxBytes);
    }
```

In `Apply`, after `appliedAspect = result.Aspect;`:

```csharp
        // A failed page keeps the last known width, so a zoom still has a
        // fit percent to step from.
        if (double.IsFinite(result.NaturalWidth)) appliedNatural = result.NaturalWidth;
        bool relayout = index == relayoutOf;
        relayoutOf = -1;
```

Inside its `try`, replace the `ShowPage` line with:

```csharp
            // Pages differ in size, so a percent gives each its own width.
            View.SetPageWidth(DisplayWidth(View.PreviewWidth, appliedNatural));
            View.ShowPage(index, pageCount, zoom.Label, result.Image, result.Text);
```

and replace `RaiseLocationChanged();` after the failure count with:

```csharp
        if (!relayout) RaiseLocationChanged();
        // The new page may allow a different step, so the toolbar refreshes.
        RaiseZoomChanged();
```

In `OnPreviewSizeChanged`, replace the final `if` with:

```csharp
        if (WidthFor(appliedNatural, appliedAspect).Width != appliedWidth)
        {
            Relayout();
        }
        // The fit percent follows the viewport, so a step may have opened or closed.
        RaiseZoomChanged();
```

and add `|| !double.IsFinite(appliedNatural)` to the guard before it. The
old call passed the viewport width as the page width; `WidthFor` now takes
the page's natural width and reads the viewport itself.

- [ ] **Step 6: Session: bounded close (P10)**

Replace `DisposeAsync` from `bool clean = true;` to the end of the method:

```csharp
        bool clean = true;
        // A hung PdfPig page must not hold Back: wait at most CloseWait for
        // the scheduler, then at most CloseWait for the text source.
        Task cancelling = scheduler.CancelAsync();
        bool settled = await Task.WhenAny(cancelling, Task.Delay(CloseWait)) == cancelling;
        if (settled)
        {
            try
            {
                await cancelling;
            }
            catch (Exception)
            {
                // Recorded for the test diagnostics; closing goes on regardless.
                clean = false;
            }
        }
        else
        {
            clean = false;
        }
        bool closed = await text.CloseAsync(settled ? CloseWait : TimeSpan.Zero);
        PdfSessionDiagnostics counts = new(
            scheduler.Requests, scheduler.Loads, scheduler.StaleResults,
            cache.PeakBytes, cache.MaxBytes, cache.Count,
            text.PeakPages, text.PeakCharacters, clean, cache.Evictions,
            AbandonedExtraction: !closed);
        View.Clear();
        cache.Clear();
        if (settled)
        {
            stream?.Dispose();
            file?.Dispose();
        }
        else
        {
            // The render may still be reading the stream; close it when the
            // load ends, and observe its fault so it isn't unobserved.
            IRandomAccessStream? heldStream = stream;
            FileStream? heldFile = file;
            _ = cancelling.ContinueWith(
                done =>
                {
                    _ = done.Exception;
                    heldStream?.Dispose();
                    heldFile?.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        WriteDiagnostics(counts);
    }
```

`text.Dispose()` is gone: it blocks on a running extraction, and
`CloseAsync` already closes the document, now or when the extraction ends.

- [ ] **Step 7: Push and check the pdf group**

Commit (`feat(pdf): T10.2 session zoom, page jump, password open and bounded close`),
push, and dispatch with `-f shell-scope=pdf -f dev-fast=true`.
Expected:
- `core-tests` passes, and the Production project builds.
- `pdf-reader-light` and `pdf-reader-dark` pass, including
  `abandonedExtraction` false in their diagnostics.
- `pdf-zoom-light` gets past the 200% width checks on pages 121 and 122 and
  fails with `Zoom in stayed enabled at 400%.`: the session refuses the step,
  but the toolbar can't disable the command until Task 6.

Any earlier `pdf-zoom` failure (the fraction after a zoom, the horizontal
scroll at 200% or at Fit, or the width on page 122) is a session bug: use
superpowers:systematic-debugging before Task 6. Record the run ID.

### Task 6: Toolbar keys, page-entry refusal and zoom availability

The toolbar smoke (`reader-toolbar-ui`, a full run without `dev-fast`) hosts
the production `ReaderToolbar` with a fake session, so it checks the
toolbar's own rules: the refusal copy, the shortcut names, which keys run,
and where focus goes. The installed `pdf-*` modes check the same code
against the real session in Task 7.

**Files:**
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml`
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml.cs`
- Modify: `tools/p1/DesktopGuides.ReaderToolbarSmoke/ToolbarWindow.cs`
- Modify: `tools/p1/windows_reader_toolbar_ui_smoke.ps1`

**Interfaces:**
- Consumes: `PageEntry.TryParse`, `PageEntry.RangeMessage` (Task 1).
- Produces for Task 7:
  - `ReaderToolbar.PageCount` (`int`, 0 = no count) and
    `ReaderToolbar.KeysEnabled` (`bool`). `SetSession` resets both, so the
    shell sets them after `SetSession`.
  - `ReaderToolbar.SetZoomAvailability(bool canZoomIn, bool canZoomOut)`;
    `SetSession` re-enables both commands.
  - `event EventHandler? ContentFocusRequested`, raised after a dialog that
    Ctrl+G opened closes.
  - `public bool TryRunKey(VirtualKey key, VirtualKeyModifiers modifiers, bool fromContent = false)` (P14).

- [ ] **Step 1: Host the new toolbar state in the smoke window**

In `ToolbarWindow.cs`, add fields:

```csharp
    private readonly Button contentStandIn = new() { Content = "Content" };
    private readonly TextBox hostNotes = new() { Header = "Notes", Width = 240 };
```

In the constructor, after `toolbar.SetSession(session);`:

```csharp
        // A five-page document, so the dialog's range is 1 to 5.
        toolbar.PageCount = 5;
        AutomationProperties.SetAutomationId(contentStandIn, "ContentStandIn");
        AutomationProperties.SetAutomationId(hostNotes, "HostNotes");
        toolbar.ContentFocusRequested += (_, _) => contentStandIn.Focus(FocusState.Programmatic);
```

Add a second row of control buttons after the `WideToolbar` button, so the
first row doesn't run off a small window (an offscreen button can't be
invoked):

```csharp
        StackPanel keyControls = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        keyControls.Children.Add(ControlButton("Keys on", "KeysOn",
            () =>
            {
                toolbar.KeysEnabled = true;
                return Task.CompletedTask;
            }));
        keyControls.Children.Add(ControlButton("Zoom at end", "ZoomAtEnd",
            () =>
            {
                toolbar.SetZoomAvailability(canZoomIn: false, canZoomOut: true);
                return Task.CompletedTask;
            }));
        keyControls.Children.Add(ControlButton("Zoom both ways", "ZoomBothWays",
            () =>
            {
                toolbar.SetZoomAvailability(canZoomIn: true, canZoomOut: true);
                return Task.CompletedTask;
            }));
```

Add `keyControls` to `content` after `controls`, and `contentStandIn` and
`hostNotes` after `lastAction`.
`AllControls` must keep `PageCount` at 5: `SetCapabilities` doesn't call
`SetSession`, so it does.

- [ ] **Step 2: Write the failing toolbar smoke checks**

In `windows_reader_toolbar_ui_smoke.ps1`, add after `Enter-DialogText`:

```powershell
    function Wait-FocusedId([string] $id) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = Find-ById $id
            if ($element -and $element.Current.HasKeyboardFocus) { return $element }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        $actual = if ($focused) { "$($focused.Current.AutomationId) '$($focused.Current.Name)'" } else { 'nothing' }
        throw "Expected keyboard focus on '$id', got $actual."
    }

    # P15: the key is on the command itself, where Narrator reads it.
    function Assert-AcceleratorKey([string] $name, [string] $key) {
        $element = Wait-VisibleByName $name
        $actual = $element.Current.AcceleratorKey
        if ($actual -ne $key) { throw "Expected '$name' to name the key '$key', got '$actual'." }
    }

    # P2: a bad entry keeps the dialog open and says why.
    function Assert-DialogRefuses([string] $value) {
        Enter-DialogText $value 'Go'
        $expected = 'Enter a page from 1 to 5.'
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $message = Find-ById 'ReaderCommandError'
            if ($message -and -not $message.Current.IsOffscreen -and
                    $message.Current.Name -eq $expected) { break }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        $box = Find-ById 'ReaderCommandInput'
        if (-not $box -or $box.Current.IsOffscreen) {
            throw "The Go to page dialog closed after '$value'."
        }
        $actual = if ($message) { $message.Current.Name } else { 'missing' }
        if ($actual -ne $expected) { throw "Expected '$expected' after '$value', got '$actual'." }
    }

    function Send-ToolbarKeys([string] $keys, [string] $focusId) {
        [System.Windows.Forms.SendKeys]::SendWait($keys)
        Start-Sleep -Milliseconds 150
        [void](Wait-FocusedId $focusId)
    }

    # Gives a key time to land, then checks it ran nothing.
    function Assert-ActionStays([string] $expected, [string] $context) {
        Start-Sleep -Milliseconds 700
        $actual = (Find-ById 'LastReaderAction').Current.Name
        if ($actual -ne $expected) { throw "$context ran '$actual'." }
    }
```

Replace the `Enter-DialogText '3' 'Go'` line with:

```powershell
    foreach ($refused in @('0', '6', '')) { Assert-DialogRefuses $refused }
    Enter-DialogText '3' 'Go'
```

and add `$report.phases += 'page-dialog-refuses-out-of-range'` before
`$report.phases += 'page-dialog-restores-overflow-focus'`.

After `$report.phases += 'all-capabilities-dispatch'`, add:

```powershell
    foreach ($pair in @(@('Previous page', 'Page Up'), @('Next page', 'Page Down'),
            @('Go to start', 'Ctrl+Home'), @('Go to end', 'Ctrl+End'),
            @('Zoom in', 'Ctrl+Plus'), @('Zoom out', 'Ctrl+Minus'))) {
        Assert-AcceleratorKey $pair[0] $pair[1]
    }
    Open-Overflow
    Assert-AcceleratorKey 'Go to page' 'Ctrl+G'
    Assert-AcceleratorKey 'Fit to width' 'Ctrl+0'
    Close-Overflow
    $report.phases += 'shortcut-names'

    # Keys stay off until the shell turns them on (TXT until T16.1).
    $stand = 'ContentStandIn'
    (Find-ById $stand).SetFocus()
    [void](Wait-FocusedId $stand)
    Send-ToolbarKeys '{PGDN}' $stand
    Assert-ActionStays 'Find boss' 'Page Down with keys off'
    Invoke-Id 'KeysOn'
    (Find-ById $stand).SetFocus()
    foreach ($pair in @(@('{PGDN}', 'Page turn 1'), @('{PGUP}', 'Page turn -1'),
            @('^{HOME}', 'Page edge Start'), @('^{END}', 'Page edge End'),
            @('^=', 'Zoom 1.1'), @('^-', 'Zoom 0.9'), @('^{ADD}', 'Zoom 1.1'),
            @('^{SUBTRACT}', 'Zoom 0.9'), @('^0', 'Fit to width'))) {
        Send-ToolbarKeys $pair[0] $stand
        Wait-Action $pair[1]
    }
    $report.phases += 'keys-run-commands-without-moving-focus'

    # Ctrl+G: Esc, or a jump, hands focus to the content.
    [System.Windows.Forms.SendKeys]::SendWait('^g')
    [void](Wait-FocusedId 'ReaderCommandInput')
    Send-ToolbarKeys '{PGDN}' 'ReaderCommandInput'
    Assert-ActionStays 'Fit to width' 'Page Down in the Go to page box'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    [void](Wait-FocusedId $stand)
    [System.Windows.Forms.SendKeys]::SendWait('^g')
    [void](Wait-FocusedId 'ReaderCommandInput')
    Enter-DialogText '4' 'Go'
    Wait-Action 'Page jump 4'
    [void](Wait-FocusedId $stand)
    $report.phases += 'keyboard-dialog-focuses-content'

    # A text box keeps its page keys; zoom keys still run.
    (Find-ById 'HostNotes').SetFocus()
    [void](Wait-FocusedId 'HostNotes')
    Send-ToolbarKeys '{PGDN}' 'HostNotes'
    Send-ToolbarKeys '^{END}' 'HostNotes'
    Assert-ActionStays 'Page jump 4' 'Page keys in a text box'
    Send-ToolbarKeys '^=' 'HostNotes'
    Wait-Action 'Zoom 1.1'
    $report.phases += 'text-box-keeps-page-keys'

    # At the last step Zoom in is disabled, and its key does nothing.
    # Focus on Zoom in moves to Zoom out instead of leaving the toolbar.
    (Wait-VisibleByName 'Zoom in').SetFocus()
    Invoke-Id 'ZoomAtEnd'
    if ((Wait-VisibleByName 'Zoom in').Current.IsEnabled) { throw 'Zoom in stayed enabled at its end.' }
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    if (-not $focused -or $focused.Current.Name -ne 'Zoom out') {
        $actual = if ($focused) { "'$($focused.Current.Name)'" } else { 'nothing' }
        throw "Disabling Zoom in moved focus to $actual, not 'Zoom out'."
    }
    (Find-ById $stand).SetFocus()
    Send-ToolbarKeys '^0' $stand
    Wait-Action 'Fit to width'
    Send-ToolbarKeys '^=' $stand
    Assert-ActionStays 'Fit to width' 'Ctrl+= with Zoom in disabled'
    Send-ToolbarKeys '^-' $stand
    Wait-Action 'Zoom 0.9'
    Invoke-Id 'ZoomBothWays'
    $report.phases += 'zoom-end-disables-command-and-key'
```

The narrow-toolbar phase that follows invokes *Zoom in*, so it must be
enabled again first; `ZoomBothWays` does that.

Check the script stays ASCII:
`LC_ALL=C grep -n "$(printf '[\200-\377]')" /Users/ilya.lissoboi/work/desktop-guides/tools/p1/windows_reader_toolbar_ui_smoke.ps1`
Expected: no output.

- [ ] **Step 3: Push the checks and watch them fail**

Commit the host and smoke changes
(`test(reader): T10.2 toolbar keys and page-entry checks`), push, and
dispatch a full run (no `dev-fast`, which skips `reader-toolbar-ui`):
`gh workflow run windows-ci.yml -R ilya-slalom/desktop-guides --ref feat/p1-t10-2-pdf-controls -f shell-scope=pdf`.
Expected: `reader-toolbar-ui` fails to build the smoke host with CS1061 on
`PageCount`, `ContentFocusRequested` and `SetZoomAvailability`
(`KeysEnabled` too). Record the run ID as Task 6's RED.

- [ ] **Step 4: Name the keys in the toolbar XAML**

In `ReaderToolbar.xaml`, add a tooltip and a shortcut name to each keyed
command. For example, `PreviousPage` becomes:

```xml
            <AppBarButton x:Name="PreviousPage"
                          Icon="Back"
                          Label="Previous page"
                          ToolTipService.ToolTip="Previous page (Page Up)"
                          AutomationProperties.AcceleratorKey="Page Up"
                          Visibility="Collapsed"
                          Click="PreviousPageClicked" />
```

Add the same two attributes, after `Label`, to the other keyed commands:

| Button | `ToolTipService.ToolTip` | `AutomationProperties.AcceleratorKey` |
| --- | --- | --- |
| `PageStart` | `Go to start (Ctrl+Home)` | `Ctrl+Home` |
| `NextPage` | `Next page (Page Down)` | `Page Down` |
| `PageEnd` | `Go to end (Ctrl+End)` | `Ctrl+End` |
| `ZoomOut` | `Zoom out (Ctrl+Minus)` | `Ctrl+Minus` |
| `ZoomIn` | `Zoom in (Ctrl+Plus)` | `Ctrl+Plus` |
| `GoToPage` | `Go to page (Ctrl+G)` | `Ctrl+G` |
| `FitToWidth` | `Fit to width (Ctrl+0)` | `Ctrl+0` |

*Smaller text*, *Larger text* and *Find in guide* get no key until T16.1.

- [ ] **Step 5: Add the keys, the refusal and zoom availability**

In `ReaderToolbar.xaml.cs`, add `using Microsoft.UI.Xaml.Automation.Peers;`,
`using Microsoft.UI.Xaml.Input;`, `using Microsoft.UI.Xaml.Media;` and
`using Windows.System;`. Add the state and the public surface:

```csharp
    // VK_OEM_PLUS and VK_OEM_MINUS: the = and - keys of the main keyboard.
    private const VirtualKey EqualsKey = (VirtualKey)187;
    private const VirtualKey MinusKey = (VirtualKey)189;
    private const VirtualKeyModifiers Ctrl = VirtualKeyModifiers.Control;

    private static readonly (VirtualKey Key, VirtualKeyModifiers Modifiers)[] KeyTable =
    [
        (VirtualKey.PageUp, VirtualKeyModifiers.None),
        (VirtualKey.PageDown, VirtualKeyModifiers.None),
        (VirtualKey.Home, Ctrl),
        (VirtualKey.End, Ctrl),
        (VirtualKey.G, Ctrl),
        (VirtualKey.Add, Ctrl),
        (EqualsKey, Ctrl),
        (EqualsKey, Ctrl | VirtualKeyModifiers.Shift),
        (VirtualKey.Subtract, Ctrl),
        (MinusKey, Ctrl),
        (VirtualKey.Number0, Ctrl),
        (VirtualKey.NumberPad0, Ctrl)
    ];

    private bool promptOpen;

    // The open document's page count; 0 means the dialog has no range.
    public int PageCount { get; set; }

    // The shell turns keys on for PDF only; TXT and HTML get them in T16.1.
    public bool KeysEnabled { get; set; }

    // Raised after a dialog that a key opened closes, for the shell to focus content.
    public event EventHandler? ContentFocusRequested;
```

Replace the constructor:

```csharp
    public ReaderToolbar()
    {
        InitializeComponent();
        // The tooltips name the keys, so WinUI's own key tips stay hidden.
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        foreach ((VirtualKey key, VirtualKeyModifiers modifiers) in KeyTable)
        {
            KeyboardAccelerator accelerator = new() { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (sender, args) =>
                args.Handled = TryRunKey(sender.Key, sender.Modifiers);
            KeyboardAccelerators.Add(accelerator);
        }
    }
```

In `SetSession`, after `session = value;`:

```csharp
        PageCount = 0;
        KeysEnabled = false;
        ZoomIn.IsEnabled = true;
        ZoomOut.IsEnabled = true;
```

Add the key route, after `Show`:

```csharp
    // P4 and P14: the accelerators and the PDF preview both come here.
    public bool TryRunKey(
        VirtualKey key, VirtualKeyModifiers modifiers, bool fromContent = false)
    {
        AppBarButton? command = CommandFor(key, modifiers);
        if (command is null || !KeysEnabled || promptOpen || session is null ||
            XamlRoot is null || Commands.Visibility != Visibility.Visible ||
            command.Visibility != Visibility.Visible || !command.IsEnabled ||
            DialogOpen())
        {
            return false;
        }
        // A text box or scroll viewer keeps its own page keys. The preview
        // routes its keys here itself (fromContent), so they never run twice.
        bool pageKey = command == PreviousPage || command == NextPage ||
            command == PageStart || command == PageEnd;
        if (pageKey && !fromContent &&
            FocusManager.GetFocusedElement(XamlRoot) is TextBox or PasswordBox or ScrollViewer)
        {
            return false;
        }
        _ = RunAsync(command, fromKeyboard: true);
        return true;
    }

    // Any ContentDialog, including the shell's, owns the keyboard while open.
    private bool DialogOpen() =>
        VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
            .Any(popup => popup.Child is ContentDialog);

    private AppBarButton? CommandFor(VirtualKey key, VirtualKeyModifiers modifiers) =>
        (key, modifiers) switch
        {
            (VirtualKey.PageUp, VirtualKeyModifiers.None) => PreviousPage,
            (VirtualKey.PageDown, VirtualKeyModifiers.None) => NextPage,
            (VirtualKey.Home, Ctrl) => PageStart,
            (VirtualKey.End, Ctrl) => PageEnd,
            (VirtualKey.G, Ctrl) => GoToPage,
            (VirtualKey.Add or EqualsKey, Ctrl) => ZoomIn,
            (EqualsKey, Ctrl | VirtualKeyModifiers.Shift) => ZoomIn,
            (VirtualKey.Subtract or MinusKey, Ctrl) => ZoomOut,
            (VirtualKey.Number0 or VirtualKey.NumberPad0, Ctrl) => FitToWidth,
            _ => null
        };

    // One place maps a keyed command to its action, for clicks and keys.
    private Task RunAsync(AppBarButton command, bool fromKeyboard = false)
    {
        if (command == GoToPage) return GoToPageAsync(fromKeyboard);
        if (command == PreviousPage) return ExecuteAsync(new PageTurnAction(-1), "turn to the previous page");
        if (command == NextPage) return ExecuteAsync(new PageTurnAction(1), "turn to the next page");
        if (command == PageStart) return ExecuteAsync(new PageEdgeAction(ReaderEdge.Start), "go to the start");
        if (command == PageEnd) return ExecuteAsync(new PageEdgeAction(ReaderEdge.End), "go to the end");
        if (command == ZoomOut) return ExecuteAsync(new ZoomAction(0.9), "zoom out");
        if (command == ZoomIn) return ExecuteAsync(new ZoomAction(1.1), "zoom in");
        if (command == FitToWidth) return ExecuteAsync(new FitWidthAction(), "fit to width");
        throw new ArgumentException("This command has no key.", nameof(command));
    }

    // P5. Disabling the focused command would move focus, so it moves to the other one first.
    public void SetZoomAvailability(bool canZoomIn, bool canZoomOut)
    {
        if (!canZoomIn && canZoomOut && ZoomIn.FocusState != FocusState.Unfocused)
        {
            ZoomOut.Focus(ZoomIn.FocusState);
        }
        else if (!canZoomOut && canZoomIn && ZoomOut.FocusState != FocusState.Unfocused)
        {
            ZoomIn.Focus(ZoomOut.FocusState);
        }
        ZoomIn.IsEnabled = canZoomIn;
        ZoomOut.IsEnabled = canZoomOut;
    }
```

`ExecuteAsync` already catches every failure, and `GoToPageAsync` awaits
only `PromptAsync` and `ExecuteAsync`, so the discarded task can't fault
unobserved. `DialogOpen` also stops a second `ShowAsync` while a shell
dialog is open, which would throw.

Replace the eight keyed `...Clicked` handlers, so clicks and keys share
`RunAsync`:

```csharp
    private async void PreviousPageClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(PreviousPage);

    private async void NextPageClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(NextPage);

    private async void PageStartClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(PageStart);

    private async void PageEndClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(PageEnd);

    private async void ZoomOutClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(ZoomOut);

    private async void ZoomInClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(ZoomIn);

    private async void FitToWidthClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(FitToWidth);

    private async void GoToPageClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(GoToPage);
```

and add `GoToPageAsync` in place of the old `GoToPageClicked` body:

```csharp
    private async Task GoToPageAsync(bool fromKeyboard)
    {
        IReaderSession? current = session;
        if (current is null)
        {
            return;
        }
        // P2: with a count the dialog refuses a bad entry itself.
        int count = PageCount;
        Func<string, string?>? validate = count > 0
            ? text => PageEntry.TryParse(text, count, out _) ? null : PageEntry.RangeMessage(count)
            : null;
        string? value = await PromptAsync(
            "Go to page", "Page number", "Go", GoToPage, current, validate, fromKeyboard);
        if (value is null || !ReferenceEquals(current, session))
        {
            return;
        }
        bool valid = count > 0
            ? PageEntry.TryParse(value, count, out int page)
            : int.TryParse(value, out page) && page >= 1;
        if (!valid)
        {
            CommandFailed?.Invoke("Enter a page number greater than zero.");
            return;
        }
        await ExecuteAsync(new PageJumpAction(page), "go to that page");
    }
```

Replace `PromptAsync`, and add `RequestContentFocus` after it:

```csharp
    private async Task<string?> PromptAsync(
        string title, string label, string primaryButtonText,
        Control invokingControl, IReaderSession expectedSession,
        Func<string, string?>? validate = null, bool fromKeyboard = false)
    {
        TextBox input = new()
        {
            Header = label
        };
        AutomationProperties.SetAutomationId(input, "ReaderCommandInput");
        TextBlock error = new()
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetAutomationId(error, "ReaderCommandError");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        ContentDialog dialog = new()
        {
            Title = title,
            Content = new StackPanel { Spacing = 8, Children = { input, error } },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        DialogSurface.Apply(dialog, DialogMaterial);
        dialog.Opened += (_, _) => input.Focus(FocusState.Programmatic);
        if (validate is not null)
        {
            // Go stays available, so a refusal explains itself (P2).
            dialog.PrimaryButtonClick += (_, args) =>
            {
                string? refusal = validate(input.Text);
                if (refusal is null)
                {
                    return;
                }
                args.Cancel = true;
                error.Text = refusal;
                error.Visibility = Visibility.Visible;
                FrameworkElementAutomationPeer.CreatePeerForElement(error)
                    ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                input.Focus(FocusState.Programmatic);
                input.SelectAll();
            };
        }
        promptOpen = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? input.Text.Trim()
                : null;
        }
        finally
        {
            promptOpen = false;
            if (fromKeyboard)
            {
                RequestContentFocus(expectedSession);
            }
            else
            {
                RestorePromptFocus(invokingControl, expectedSession);
            }
        }
    }

    // After Ctrl+G the reader content, not the overflow command, gets focus.
    private void RequestContentFocus(IReaderSession expectedSession) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(session, expectedSession))
            {
                ContentFocusRequested?.Invoke(this, EventArgs.Empty);
            }
        });
```

`FindInGuideClicked` keeps calling `PromptAsync` with five arguments, so
*Find in guide* is unchanged.

- [ ] **Step 6: Push and watch the toolbar smoke pass**

Commit (`feat(reader): T10.2 toolbar keys, page-entry refusal and zoom availability`),
push, and dispatch the same full run as Step 3.
Expected:
- `reader-toolbar-ui` passes, with `page-dialog-refuses-out-of-range`,
  `shortcut-names`, `keys-run-commands-without-moving-focus`,
  `keyboard-dialog-focuses-content`, `text-box-keeps-page-keys` and
  `zoom-end-disables-command-and-key` in its phases.
- `pdf-zoom-light` still fails with `Zoom in stayed enabled at 400%.`: the
  shell doesn't pass zoom state to the toolbar until Task 7.

If `keys-run-commands-without-moving-focus` fails on its first key with the
action unchanged, the accelerators aren't reaching the toolbar: check that
`KeyboardAccelerators` has 12 entries and `Commands` is visible. If it
fails on `^=` only, log `sender.Key` from the `Invoked` handler: the key
code is wrong for that layout. Use superpowers:systematic-debugging; don't
change the test's keys. Record the run ID as Task 6's GREEN.

### Task 7: Shell unlock panel and Reader wiring

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (the Reader surface grid)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (`ShowReaderSurface`)
- Modify: `src/DesktopGuides.Production/ShellWindow.PdfReader.cs`

**Interfaces:**
- Consumes:
  - `ManagedPdfGuideLoader.LoadAsync(Guide, string?, CancellationToken)` (Task 3);
  - `PdfReaderSession.OpenAsync(source, string?, token)`, `PageCount`,
    `ZoomState`, `ZoomChanged` and `View.FocusPreview()` (Task 5);
  - `ReaderToolbar.PageCount`, `KeysEnabled`, `SetZoomAvailability`,
    `ContentFocusRequested` and `TryRunKey` (Task 6);
  - `PdfGuideLoadMessages.For(PasswordRequired | PasswordIncorrect)` (Task 1).
- Produces: the installed behaviour Task 4's `pdf-jump`, `pdf-zoom`,
  `pdf-keys`, `pdf-locked` and `pdf-offline` modes check. Nothing later
  consumes new code.

- [ ] **Step 1: Confirm the RED**

Task 6 Step 6's run is this task's RED: `pdf-zoom-light` fails with
`Zoom in stayed enabled at 400%.`, and the Task 4 runs show `pdf-keys`,
`pdf-jump` and `pdf-locked` failing on the missing shell wiring. No new
test is written; Task 4 wrote them.

- [ ] **Step 2: Add the unlock panel**

In `ShellWindow.xaml`, inside the Reader surface's `StackPanel`, after
`ReaderLoadErrorAction`:

```xml
                            <StackPanel x:Name="PdfUnlockPanel"
                                        Visibility="Collapsed"
                                        MaxWidth="360"
                                        HorizontalAlignment="Left"
                                        Spacing="{StaticResource DesktopGuidesSpacing12}"
                                        AutomationProperties.AutomationId="PdfUnlockPanel">
                                <TextBlock x:Name="PdfUnlockMessage"
                                           TextWrapping="Wrap"
                                           Style="{StaticResource DesktopGuidesSecondaryBodyStyle}" />
                                <PasswordBox x:Name="PdfPasswordInput"
                                             Header="Password"
                                             PasswordChanged="PdfPasswordChanged"
                                             KeyDown="PdfPasswordKeyDown"
                                             AutomationProperties.AutomationId="PdfPasswordInput" />
                                <TextBlock x:Name="PdfUnlockError"
                                           Visibility="Collapsed"
                                           TextWrapping="Wrap"
                                           Foreground="{ThemeResource SystemFillColorCriticalBrush}"
                                           AutomationProperties.LiveSetting="Polite"
                                           AutomationProperties.AutomationId="PdfUnlockError" />
                                <Button x:Name="PdfUnlockButton"
                                        Content="Unlock"
                                        IsEnabled="False"
                                        Style="{StaticResource AccentButtonStyle}"
                                        Click="PdfUnlockClicked"
                                        AutomationProperties.AutomationId="PdfUnlockButton" />
                            </StackPanel>
```

In `ShowReaderSurface`, after the `ReaderLoadErrorAction.Visibility`
assignment, so every surface change hides the panel (P11):

```csharp
        PdfUnlockPanel.Visibility = Visibility.Collapsed;
        PdfUnlockError.Visibility = Visibility.Collapsed;
        PdfPasswordInput.Password = string.Empty;
```

- [ ] **Step 3: Open with a password and show the panel**

In `ShellWindow.PdfReader.cs`, add `using Microsoft.UI.Input;`,
`using Microsoft.UI.Xaml;`, `using Microsoft.UI.Xaml.Automation.Peers;`,
`using Microsoft.UI.Xaml.Controls;`, `using Microsoft.UI.Xaml.Input;`,
`using Windows.System;` and `using Windows.UI.Core;`. Add the fields:

```csharp
    // The locked guide the panel is asking for, and the render that showed it.
    private Guide? pdfUnlockGuide;
    private int pdfUnlockGeneration = -1;
```

Change `OpenPdfGuideAsync` to take the attempt. The password is a
parameter only; nothing stores it (Global Constraints):

```csharp
    // Returns false when a newer render took over, like the TXT path.
    private async Task<bool> OpenPdfGuideAsync(Guide guide, int generation, string? password = null)
    {
        // An unlock attempt keeps the panel up while it checks.
        if (password is null)
        {
            ShowReaderSurface(placeholder: false);
        }
        readerLoad?.Dispose();
        readerLoad = new CancellationTokenSource();
        CancellationToken token = readerLoad.Token;
        PdfGuideLoad load;
        try
        {
            load = await pdfLoader!.LoadAsync(guide, password, token);
        }
```

The rest of the method changes in four places:

1. Replace the `PdfGuideLoadFailed` branch:

```csharp
        if (load is PdfGuideLoadFailed failed)
        {
            if (failed.Error is PdfGuideLoadError.PasswordRequired or PdfGuideLoadError.PasswordIncorrect)
            {
                ShowPdfUnlock(guide, generation, failed.Error);
            }
            else
            {
                ShowPdfLoadError(failed.Error);
            }
            return true;
        }
```

2. After `session.Failed += OnPdfSessionFailed;`:

```csharp
        session.ZoomChanged += OnPdfZoomChanged;
        session.View.PreviewKeyDown += OnPdfPreviewKeyDown;
```

3. Pass the attempt to the session:
   `await session.OpenAsync(new ManagedGuideSource(guide, loaded.FilePath), password, token);`

4. Replace the tail from `ReaderActions.SetSession(session);`:

```csharp
        ReaderActions.SetSession(session);
        // SetSession resets these, so they follow it.
        ReaderActions.PageCount = session.PageCount;
        ReaderActions.KeysEnabled = true;
        PdfZoomState zoom = session.ZoomState;
        ReaderActions.SetZoomAvailability(zoom.CanZoomIn, zoom.CanZoomOut);
        bool opened = await OpenAtSavedPlaceAsync(
            guide, session, generation, loaded.ContentSha256, null, token);
        // After an unlock the preview takes focus from the gone password box.
        if (opened && password is not null && ReferenceEquals(readerSession, session))
        {
            session.View.FocusPreview();
        }
        return opened;
    }
```

`readerLoad?.Dispose()` matters on a retry: the first attempt's source is
still set. `CloseReaderSessionAsync` cancels and disposes the current one,
as before.

Add the panel and its handlers after `ShowPdfLoadError`:

```csharp
    private void ShowPdfUnlock(Guide guide, int generation, PdfGuideLoadError error)
    {
        ShowReaderSurface(placeholder: false);
        pdfUnlockGuide = guide;
        pdfUnlockGeneration = generation;
        PdfUnlockMessage.Text = PdfGuideLoadMessages.For(PdfGuideLoadError.PasswordRequired);
        PdfUnlockPanel.Visibility = Visibility.Visible;
        if (error == PdfGuideLoadError.PasswordIncorrect)
        {
            PdfUnlockError.Text = PdfGuideLoadMessages.For(error);
            PdfUnlockError.Visibility = Visibility.Visible;
            FrameworkElementAutomationPeer.CreatePeerForElement(PdfUnlockError)
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        // The box was cleared before the attempt, so Unlock is disabled
        // (P13); focus goes back to the box for the next try.
        ShowTransientStatus(PdfUnlockMessage.Text);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == renderGeneration && PdfUnlockPanel.Visibility == Visibility.Visible)
            {
                PdfPasswordInput.Focus(FocusState.Programmatic);
            }
        });
    }

    private void PdfPasswordChanged(object sender, RoutedEventArgs args) =>
        PdfUnlockButton.IsEnabled = PdfPasswordInput.Password.Length > 0;

    // Enter in the box unlocks, like a default button.
    private async void PdfPasswordKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            await UnlockPdfAsync();
        }
    }

    private async void PdfUnlockClicked(object sender, RoutedEventArgs args) =>
        await UnlockPdfAsync();

    private async Task UnlockPdfAsync()
    {
        if (pdfUnlockGuide is not { } guide || PdfPasswordInput.Password.Length == 0)
        {
            return;
        }
        int generation = pdfUnlockGeneration;
        // Copy the attempt, then clear the box before it runs.
        string password = PdfPasswordInput.Password;
        PdfPasswordInput.Password = string.Empty;
        PdfUnlockError.Visibility = Visibility.Collapsed;
        await RunNavigationAsync(async () =>
        {
            // Back or another guide during the click wins.
            if (generation != renderGeneration || !ReferenceEquals(guide, pdfUnlockGuide))
            {
                return;
            }
            await OpenPdfGuideAsync(guide, generation, password);
        });
    }
```

`ShowTransientStatus` replaces the render's `Loading guide…`, so the
status doesn't stay busy while the panel waits. A second Enter while an
attempt runs finds an empty box and does nothing.

- [ ] **Step 4: Wire zoom state, keys and content focus**

Add after `UnlockPdfAsync`:

```csharp
    // A late event from a replaced session is ignored.
    private void OnPdfZoomChanged(object? sender, EventArgs args)
    {
        if (sender is PdfReaderSession session && ReferenceEquals(session, readerSession))
        {
            PdfZoomState zoom = session.ZoomState;
            ReaderActions.SetZoomAvailability(zoom.CanZoomIn, zoom.CanZoomOut);
        }
    }

    // P14: a focused preview would page itself, so its keys go to the
    // toolbar first. The page text keeps its own page keys.
    private void OnPdfPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.OriginalSource is TextBox or PasswordBox)
        {
            return;
        }
        args.Handled = ReaderActions.TryRunKey(args.Key, CurrentModifiers(), fromContent: true);
    }

    private static VirtualKeyModifiers CurrentModifiers()
    {
        VirtualKeyModifiers modifiers = VirtualKeyModifiers.None;
        if (IsKeyDown(VirtualKey.Control)) modifiers |= VirtualKeyModifiers.Control;
        if (IsKeyDown(VirtualKey.Shift)) modifiers |= VirtualKeyModifiers.Shift;
        if (IsKeyDown(VirtualKey.Menu)) modifiers |= VirtualKeyModifiers.Menu;
        return modifiers;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
```

In the `ShellWindow` constructor (`ShellWindow.xaml.cs`), after
`ReaderActions.CommandFailed += ShowErrorStatus;`, add:

```csharp
        // After Ctrl+G the Reader content takes focus back (P2).
        ReaderActions.ContentFocusRequested += (_, _) =>
            (readerSession as PdfReaderSession)?.View.FocusPreview();
```

TXT and HTML never set `KeysEnabled`, so their toolbars ignore every key
and `ContentFocusRequested` is never raised for them.

- [ ] **Step 5: Push and check the pdf group**

Commit (`feat(shell): T10.2 PDF unlock panel, zoom state and Reader keys`),
push, and dispatch with `-f shell-scope=pdf -f dev-fast=true`.
Expected: every `pdf-*` mode passes in both themes, with these checks in
the results: `pdf-jump` refuses 0, 201 and blank and jumps to 150;
`pdf-zoom` keeps page 121's fraction and disables *Zoom in* at 400%;
`pdf-keys` runs every key; `pdf-locked` shows the panel, the wrong-password
message with an empty box, then the page; `pdf-offline` passes
`Assert-NoRemoteConnections` for all three guides; and the LocalState scan
finds no `wrong-7Q2x`. Record the run ID as Task 7's GREEN.

A failure in a mode Tasks 5 and 6 already passed (`pdf-reader`, the
toolbar smoke) is a regression from this task's wiring: use
superpowers:systematic-debugging before going on.

### Task 8: Import dialog password step

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1:3783-3787` (the
  `import-pdf-locked` phase of `import-preview`)
- Modify: `tools/p1/windows_shell_install.ps1:1565-1585` (`Run-ImportScenarios`)
- Modify: `src/DesktopGuides.Production/ImportGuideDialog.xaml` (the preview
  `StackPanel` and `ImportDetailsRows`)
- Modify: `src/DesktopGuides.Production/ImportGuideDialog.xaml.cs`

**Interfaces:**
- Consumes:
  - `ImportNeedsPdfPassword(ImportSource Source, string SuggestedTitle, string Fingerprint)`
    and `IGuideImportValidator.ResolvePdfPasswordAsync(ImportNeedsPdfPassword, string, CancellationToken) → Task<PdfImportManifest>` (Task 2);
  - `PdfImportManifest.PasswordRequired` (Task 2);
  - `GuideImportException.Issue == ImportIssue.PasswordIncorrect`, whose
    message is `ImportPresentation.PdfPasswordIncorrect` (Tasks 1 and 2);
  - `ImportPresentation.PasswordProtectedFact` (Task 1);
  - the smoke's `Enter-Secret` and the installer's `Assert-NoPasswordTrace`,
    which returns the names of the files it couldn't read (Task 4).
- Produces: the installed `import-pdf-locked` and `import-pdf-unlocked`
  phases and their screenshots, which Task 9 records. Nothing later
  consumes new code.

The step follows the TXT encoding step: the preview shows the file's
details, the password step stands in for the facts the PDF can't give yet,
and a resolved manifest fills them in.

- [ ] **Step 1: Write the failing smoke phase**

In `windows_shell_ui_smoke.ps1`, replace the `import-pdf-locked` phase
(lines 3783–3787) with:

```powershell
            # P9: a locked PDF asks for its password in the preview. "guide"
            # is the fixture's public test password.
            Click-Element (Wait-EnabledById 'SecondaryButton')
            Choose-PickerFile 'pdf-locked.pdf'
            [void](Wait-Text 'ImportFileName' 'pdf-locked.pdf')
            [void](Wait-Text 'ImportFormat' 'PDF')
            [void](Wait-FocusedId 'ImportPdfPasswordInput')
            if ((Wait-PresentById 'ImportPdfUnlock').Current.IsEnabled) {
                throw 'Unlock was enabled with an empty password box.'
            }
            foreach ($id in @('ImportPages', 'ImportProtected', 'ImportPdfPasswordError')) {
                if (Find-ById $id) { throw "'$id' was shown before the password was checked." }
            }
            if ((Wait-VisibleById 'PrimaryButton').Current.IsEnabled) {
                throw 'Import was enabled before the password was checked.'
            }

            # A wrong attempt explains itself under the box, leaves it empty
            # with focus, and keeps the preview. Enter submits.
            Enter-Secret 'ImportPdfPasswordInput' 'wrong-7Q2x'
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            [void](Wait-Text 'ImportPdfPasswordError' "That password didn't open this PDF. Try again.")
            [void](Wait-FocusedId 'ImportPdfPasswordInput')
            if ((Wait-PresentById 'ImportPdfUnlock').Current.IsEnabled) {
                throw 'The password box kept the wrong attempt: Unlock is still enabled.'
            }
            [void](Wait-VisibleById 'ImportPreview')
            if ((Find-ById 'ImportStatus')) { throw 'A wrong password replaced the preview with a status.' }
            $report.importPdfLockedScreenshot = Save-WindowScreenshot 'import-pdf-locked'
            $report.phases += 'import-pdf-locked'

            # The right password fills in the facts and enables Import.
            Enter-Secret 'ImportPdfPasswordInput' 'guide'
            Invoke-Element (Wait-PresentById 'ImportPdfUnlock')
            [void](Wait-Text 'ImportProtected' 'Password protected')
            [void](Wait-Text 'ImportPages' '1 page')
            [void](Wait-EnabledById 'PrimaryButton')
            [void](Wait-FocusedId 'GuideTitleInput')
            foreach ($id in @('ImportPdfPasswordInput', 'ImportPdfPasswordError')) {
                if (Find-ById $id) { throw "'$id' stayed after the password was accepted." }
            }
            $report.importPdfUnlockedScreenshot = Save-WindowScreenshot 'import-pdf-unlocked'
            $report.phases += 'import-pdf-unlocked'
```

The close that follows (`CloseButton`) cancels the dialog, so nothing is
published; `Run-ImportScenarios` already requires zero guides, operations
and staging entries after both `import-preview` runs.

`ImportStatus` is an `InfoBar` with `IsOpen="False"` until a message
shows, and a closed `InfoBar` leaves the UIA tree, so `Find-ById
'ImportStatus'` is empty while no status is open. If the first run shows it
present without a message, compare `Current.IsOffscreen` instead and record
the ruling.

In `windows_shell_install.ps1`, in `Run-ImportScenarios`, after the
`describe-import` loop that requires no guides:

```powershell
        # Review Focus 1: the wrong import attempt reached no file either.
        $report.importPasswordScanUnreadable = Assert-NoPasswordTrace 'wrong-7Q2x'
```

Check ASCII:

```bash
LC_ALL=C grep -n "$(printf '[\200-\377]')" /Users/ilya.lissoboi/work/desktop-guides/tools/p1/windows_shell_ui_smoke.ps1 /Users/ilya.lissoboi/work/desktop-guides/tools/p1/windows_shell_install.ps1
```

Expected: no output.

- [ ] **Step 2: Run it to confirm RED**

Commit (`test(import): T10.2 import a locked PDF after its password`),
push, and dispatch with `-f shell-scope=import -f dev-fast=true`.
Expected: `import-light` fails with `Expected 'ImportFileName' named
'pdf-locked.pdf'.` after `import-html-warnings`: since Task 2 the validator
returns `ImportNeedsPdfPassword`, which `Check` doesn't handle yet, so the
preview stays collapsed. Record the run ID as Task 8's RED.

- [ ] **Step 3: Add the password step and the protected row**

In `ImportGuideDialog.xaml`, after the title `StackPanel` (the one holding
`GuideTitleInput` and `GuideTitleFeedback`) and before
`ImportNoTextWarning`, add the step. It sits high in the dialog so the box
and **Unlock** are on screen at the 768x519 launch size:

```xml
                <StackPanel x:Name="ImportPdfPasswordStep"
                            Visibility="Collapsed"
                            Spacing="{StaticResource DesktopGuidesSpacing4}">
                    <PasswordBox x:Name="ImportPdfPasswordInput"
                                 Header="PDF password"
                                 PasswordChanged="ImportPdfPasswordChanged"
                                 KeyDown="ImportPdfPasswordKeyDown"
                                 AutomationProperties.AutomationId="ImportPdfPasswordInput" />
                    <TextBlock x:Name="ImportPdfPasswordError"
                               Visibility="Collapsed"
                               TextWrapping="Wrap"
                               Foreground="{ThemeResource SystemFillColorCriticalBrush}"
                               AutomationProperties.LiveSetting="Polite"
                               AutomationProperties.AutomationId="ImportPdfPasswordError" />
                    <Button x:Name="ImportPdfUnlock"
                            Content="Unlock"
                            IsEnabled="False"
                            Click="ImportPdfUnlockClicked"
                            AutomationProperties.AutomationId="ImportPdfUnlock" />
                </StackPanel>
```

**Unlock** keeps the default button style: *Import* is the dialog's accent
button, and one accent per dialog keeps the default action clear.

In `ImportDetailsRows`, after `ImportPagesRow`, add:

```xml
                        <Grid x:Name="ImportProtectedRow"
                              Visibility="Collapsed"
                              ColumnSpacing="{StaticResource DesktopGuidesSpacing12}"
                              AutomationProperties.AutomationId="ImportProtectedRow">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="120" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Protection" Style="{StaticResource DesktopGuidesMetadataStyle}" />
                            <TextBlock x:Name="ImportProtected" Grid.Column="1"
                                       Style="{StaticResource DesktopGuidesBodyStyle}"
                                       AutomationProperties.AutomationId="ImportProtected" />
                        </Grid>
```

- [ ] **Step 4: Handle the password in the dialog**

In `ImportGuideDialog.xaml.cs`, add the usings:

```csharp
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Windows.System;
```

Add the field beside `needsEncoding`:

```csharp
    private ImportNeedsPdfPassword? needsPassword;
```

In `Check`, clear it beside `needsEncoding = null;`:

```csharp
        needsPassword = null;
```

and add a case after `case ImportNeedsTxtEncoding needs:`. Focus goes to the
box, not the title, so the case returns before the title focus:

```csharp
                case ImportNeedsPdfPassword locked:
                    ShowPreview(locked.Source, locked.SuggestedTitle, GuideFormat.Pdf);
                    ShowPasswordStep(locked);
                    ImportPdfPasswordInput.Focus(FocusState.Programmatic);
                    return;
```

In `ShowStatus`, clear it beside `needsEncoding = null;`:

```csharp
        needsPassword = null;
```

In `ShowPreview`, after `ImportPagesRow.Visibility = Visibility.Collapsed;`:

```csharp
        ImportProtectedRow.Visibility = Visibility.Collapsed;
        ImportPdfPasswordStep.Visibility = Visibility.Collapsed;
        ImportPdfPasswordError.Visibility = Visibility.Collapsed;
        ImportPdfPasswordInput.Password = string.Empty;
```

In `ShowManifest`'s `PdfImportManifest` case, after
`ImportNoTextWarning.IsOpen = !pdf.HasText;`:

```csharp
                ImportProtected.Text = ImportPresentation.PasswordProtectedFact;
                ImportProtectedRow.Visibility = pdf.PasswordRequired ? Visibility.Visible : Visibility.Collapsed;
```

After `ShowEncodingChoice`, add the step's methods:

```csharp
    private void ShowPasswordStep(ImportNeedsPdfPassword needs)
    {
        needsPassword = needs;
        ImportPdfPasswordInput.Password = string.Empty;
        ImportPdfPasswordError.Visibility = Visibility.Collapsed;
        ImportPdfUnlock.IsEnabled = false;
        ImportPdfPasswordStep.Visibility = Visibility.Visible;
    }

    // P13: Unlock needs text in the box.
    private void ImportPdfPasswordChanged(object sender, RoutedEventArgs args) =>
        ImportPdfUnlock.IsEnabled = ImportPdfPasswordInput.Password.Length > 0;

    // Enter unlocks; it never reaches the dialog's default button.
    private void ImportPdfPasswordKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            UnlockPdf();
        }
    }

    private void ImportPdfUnlockClicked(object sender, RoutedEventArgs args) => UnlockPdf();

    private void UnlockPdf()
    {
        if (closing || needsPassword is not { } needs || ImportPdfPasswordInput.Password.Length == 0)
        {
            return;
        }
        // The box is cleared before the attempt; the copy lives only in this
        // call and is never shown, stored or logged.
        string password = ImportPdfPasswordInput.Password;
        ImportPdfPasswordInput.Password = string.Empty;
        ImportPdfPasswordError.Visibility = Visibility.Collapsed;
        Track(RunAsync($"Checking {needs.Source.FileName}…", async (current, token) =>
        {
            PdfImportManifest manifest;
            try
            {
                manifest = await validator.ResolvePdfPasswordAsync(needs, password, token);
            }
            catch (GuideImportException error) when (error.Issue == ImportIssue.PasswordIncorrect)
            {
                // A wrong password keeps the preview; RunAsync's handler would
                // replace it with a status.
                if (current == generation && !closing)
                {
                    ShowPasswordError(error.Message);
                }
                return;
            }
            if (current != generation || closing)
            {
                return;
            }
            Guide? existing = await findDuplicate(manifest, token);
            if (current != generation || closing)
            {
                return;
            }
            needsPassword = null;
            ImportPdfPasswordStep.Visibility = Visibility.Collapsed;
            ShowManifest(manifest);
            ShowDuplicate(existing);
            GuideTitleInput.Focus(FocusState.Programmatic);
        }, keepPreview: true));
    }

    private void ShowPasswordError(string message)
    {
        ImportPdfPasswordError.Text = message;
        ImportPdfPasswordError.Visibility = Visibility.Visible;
        FrameworkElementAutomationPeer.CreatePeerForElement(ImportPdfPasswordError)
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        ImportPdfPasswordInput.Focus(FocusState.Programmatic);
    }
```

The box isn't disabled while the check runs (P13), so focus never leaves
it. Any other failure, such as `Changed`, goes through `RunAsync`'s
`GuideImportException` handler: `ShowStatus` collapses the preview,
including the step, and shows the message, as for any other file.

- [ ] **Step 5: Push and check the import group**

Commit (`feat(import): T10.2 password step in the import preview`), push,
and dispatch with `-f shell-scope=import -f dev-fast=true`.
Expected: `import-light` and `import-dark` pass with `import-pdf-locked`
and `import-pdf-unlocked` among their phases; `importPreviewState` shows no
guides, operations or staging entries; and `importPasswordScanUnreadable`
is recorded with no throw. Record the run ID as Task 8's GREEN.

If `Wait-FocusedId 'ImportPdfPasswordInput'` fails right after the pick,
the dialog's open handler may have moved focus after `Check` ran: use
superpowers:systematic-debugging, starting from `DialogOpened`
(`ImportGuideDialog.xaml.cs:70`).

### Task 9: Docs, evidence and the full run

**Files:**
- Modify: `docs/p1/t10-2-pdf-controls-design.md` (status, implementation
  notes, verification)
- Modify: `docs/p1/e2e-testing.md:232` and the scenario table (lines 258–282)
- Modify: `docs/p1/implementation-plan.md:771`
- Modify: `docs/work-breakdown.md:325`
- Modify: `docs/p1-technical-design.md:381`, `:567-570` and `:718-721`
- Modify: `docs/p1/t10-1-pdf-adapter-design.md:346`, `:347`, `:365`, `:412`
- Create: `docs/p1/evidence/t10-2-pdf-controls/` (result JSON and screenshots)

**Interfaces:**
- Consumes: the GREEN run IDs of Tasks 1–8 and the screenshots
  `pdf-jump`, `pdf-zoom`, `pdf-locked`, `pdf-locked-wrong`,
  `import-pdf-locked` and `import-pdf-unlocked` (Tasks 4 and 8).
- Produces: the docs the PR links. No code.

This task changes only docs and evidence, so it has no RED; the full CI run
in Step 5 is its check. Before writing a line, read each target line: the
line numbers are from `aebca62`, and earlier tasks don't touch these files.

- [ ] **Step 1: Run the full matrix**

Dispatch without `dev-fast` and without a `shell-scope`, so every group
runs:

```bash
gh workflow run windows-ci.yml -R ilya-slalom/desktop-guides --ref feat/p1-t10-2-pdf-controls
```

Expected: every job passes, including ARM64, packages,
`production-shell-ui` (all groups, including `pdf` and `import`) and
`reader-toolbar-ui`. Record the run ID and the Core.Tests and
Infrastructure.Tests counts from the `core-tests` log. A failure in a group
this branch didn't target (`progress`, `completion`, `catalog`) is a
regression: use superpowers:systematic-debugging, fix it under TDD in the
task that owns the code, and re-run.

- [ ] **Step 2: Collect the evidence**

```bash
RUN=<the Step 1 run ID>
gh run download "$RUN" -R ilya-slalom/desktop-guides -D /tmp/t10-2-full
find /tmp/t10-2-full \( -name 'pdf-*' -o -name 'import-*' \) \( -name '*.json' -o -name '*.png' \) | sort
```

Copy into `docs/p1/evidence/t10-2-pdf-controls/`:

- the result JSON of `pdf-jump`, `pdf-zoom`, `pdf-keys` and `pdf-locked`
  in light and dark, `pdf-offline`, and `import-light` and `import-dark`;
- the screenshots `pdf-jump`, `pdf-zoom`, `pdf-locked` and
  `pdf-locked-wrong` in light and dark, and `import-pdf-locked` and
  `import-pdf-unlocked` from `import-light` and `import-dark`.

Then scan the copies for the attempt, which must be absent from evidence
too:

```bash
grep -rl 'wrong-7Q2x' /Users/ilya.lissoboi/work/desktop-guides/docs/p1/evidence/t10-2-pdf-controls/ || echo clean
```

Expected: `clean`. The smoke never records what it types, so a match means
the app echoed the attempt somewhere UIA reads: treat it as a Review Focus 1
defect, not an evidence problem.

Look at each screenshot. Check that:

- `pdf-zoom` shows the horizontal scroll bar and the status names a percent;
- `pdf-locked` shows the message, an empty box and a disabled **Unlock**,
  with no page;
- `pdf-locked-wrong` shows the wrong-password line under the box;
- `import-pdf-locked` shows the wrong-password line under the box with the
  file details above it;
- `import-pdf-unlocked` shows `Protection: Password protected` and `Pages:
  1 page`, and nothing is clipped in either theme.

- [ ] **Step 3: Record the implementation in the design doc**

In `docs/p1/t10-2-pdf-controls-design.md`, replace the `Status:` line with
(run IDs are the ones recorded in Tasks 1–8 and Step 1):

```markdown
Status: implemented; CI run <Step 1 run> passed the full matrix, including
the installed `pdf` and `import` groups.
```

Append two sections after *Out of scope*. **Implementation notes** lists
the planning refinements P1–P15 from this plan's Global Constraints, one
bullet each, in the design's voice (what the code does and why, not what
the plan said), and then every `Ruling:` line from the execution ledger
(`.superpowers/sdd/t10-2-pdf-controls-plan/progress.md`) with what it costs
if wrong. It must at least say:

- `NumberBox` was dropped for a `TextBox` checked by `PageEntry.TryParse`,
  because `NumberBox` rounds and clamps input itself and hides the refusal
  (P2).
- There is no `UnlockAsync`; the password goes to the loader and to
  `OpenAsync` once, and the session keeps none (P3).
- The unlock panel lives in `ShellWindow` beside `ReaderLoadError` (P11),
  and only PdfPig decides `PasswordRequired` and `PasswordIncorrect` (P12).
- The preview routes its own page keys, because a focused `ScrollViewer`
  handles them before an accelerator would (P14).
- Disabling a focused zoom command first moves focus to the other one.
- The import password step keeps the file details on screen and stands in
  for the page facts until the password is checked.

**Verification** follows the T13.1 design's shape:

- the Core and Infrastructure tests by class, with counts
  (`PdfZoomTests`, `PageEntryTests`, `PdfLocationRulesTests`,
  `PdfGuideLoadMessagesTests`, `ImportPresentationTests`, the validator,
  publisher, loader and text-source tests);
- each installed mode with what it showed and its run: `pdf-jump`,
  `pdf-zoom`, `pdf-keys`, `pdf-locked`, `pdf-locked-reopen`, `pdf-offline`
  (with `Assert-NoRemoteConnections` for Tagged, Long and Locked),
  `import-pdf-locked`, `import-pdf-unlocked`, and the toolbar smoke's new
  phases;
- the two password scans (`passwordScanUnreadable` and
  `importPasswordScanUnreadable`) and what each list held;
- the RED runs (Task 4's harness run, Task 6 Step 3, Task 8 Step 2) and
  the GREEN runs;
- a link to [evidence/t10-2-pdf-controls](evidence/t10-2-pdf-controls/);
- that no installed run on the Windows host was made, if none was: it needs
  an elevated task for the certificate, and CI is the required evidence.

- [ ] **Step 4: Update the other docs**

1. `docs/p1/e2e-testing.md`:
   - In the *Import preview* row (line 272), replace
     `` `pdf-locked` shows the password-protected message; `` with
     `` `pdf-locked` asks for its password with focus in the box and Import disabled; `wrong-7Q2x` shows `That password didn't open this PDF. Try again.` under the empty box and keeps the preview; `guide` shows `Protection: Password protected` and `1 page` and enables Import, then Close publishes nothing; ``
     and append to its last column `, T10.2, TR10.3`. After `Afterwards no
     guide, file operation, or staged or managed file exists.`, add
     `` No app data file holds the wrong attempt. ``
   - After the *Reading progress* row, add a *PDF controls* row:

     ```markdown
     | PDF controls | `-PdfOnly`, after `seed-pdf-reader` imports from a temporary copy of the fixtures; each mode is its own launch in light and dark. `pdf-jump`: *Go to page* refuses 0, 201 and blank with `Enter a page from 1 to 200.` and stays open, and 150 jumps; focus returns to *Go to page*, or to the preview after Ctrl+G. `pdf-zoom`: from page 121 at fraction 0.3, Zoom in twice, Zoom out and Fit keep the page and the fraction within the T10.3 tolerance; at 200% the horizontal scroll bar shows and at Fit it doesn't; at 400% *Zoom in* is disabled; the status names the zoom. `pdf-keys`: Page Up/Down, Ctrl+Home/End, Ctrl+G, Ctrl+Plus, Ctrl+= and Ctrl+Minus, and Ctrl+0 run their commands with focus unchanged; in the page text and the Go to page box, Page Down doesn't turn the page. `pdf-locked`: the unlock panel shows with focus in the empty box; `wrong-7Q2x` shows the wrong-password line and an empty box; `guide` opens `Locked guide secret page` with focus on the preview; reopening asks again. Afterwards no app data file holds `wrong-7Q2x`, and no session reports an abandoned extraction. `pdf-offline`: with the temporary originals deleted, a relaunch opens Tagged, Long and Locked with no non-loopback TCP connection. Screenshots: `pdf-jump`, `pdf-zoom`, `pdf-locked`, `pdf-locked-wrong`. | T10.2, TR10.1, TR10.2, TR10.3 |
     ```
2. `docs/p1/implementation-plan.md:771`: append to the T10.2 row's outcome
   ` Implemented in PR #<N>; see [t10-2-pdf-controls-design.md](t10-2-pdf-controls-design.md).`
   (the PR number is filled in when the PR exists; until then leave the
   sentence out and add it in the PR's own follow-up commit).
3. `docs/work-breakdown.md:325`: add the same pointer to S10 T10.2, in the
   form the T10.1 and T10.3 lines beside it use.
4. `docs/p1-technical-design.md`:
   - Line 381 (§8 S10 Toolkit row): replace `` `NumberBox` page entry `` with
     `` page entry in a checked `TextBox` (a `NumberBox` rounds and clamps, which hides the refusal) ``.
   - Lines 567–570 (T06.2): replace
     `PDF checks readable pages and whether a password is required; a password is never persisted. If the selected PDF engine cannot handle an encrypted file, reject it before publication with a concrete reason.`
     with
     `PDF checks readable pages and whether a password is required. A locked PDF imports after its password is checked in the preview; the password is never persisted, and the managed copy stays encrypted. The Reader asks for it on every open.`
   - Lines 718–721 (§8 S10 T10.2): add after `never write it to storage/logs.`
     ` Implemented as described in [p1/t10-2-pdf-controls-design.md](p1/t10-2-pdf-controls-design.md); zoom lasts only while the guide is open.`
5. `docs/p1/t10-1-pdf-adapter-design.md`:
   - Line 347: replace `the installed offline relaunch (T17.3) are out of scope.`
     with ``a physical offline relaunch (T17.3) are out of scope; T10.2's `pdf-offline` mode checks that no remote connection is made.``
   - Line 363: append ` Done in T10.2.` after `parity (T10.2).`
   - Line 365: replace `installed offline relaunch (T17.3).` with
     `a physical offline relaunch (T17.3).`
   - Line 409: replace `A bounded close is left for T10.2.` with
     `T10.2 bounds the close at 2 s and records an abandoned extraction.`
   - Line 413: replace `left for T10.2's installed cases.` with
     `moved to T17.3.`

Re-read each changed paragraph for sense after the edit, then:

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides diff --stat
grep -n "T10.2" /Users/ilya.lissoboi/work/desktop-guides/docs/p1/t10-1-pdf-adapter-design.md
```

Expected: only docs and evidence in the stat, and every remaining T10.2
reference in the T10.1 design is either history or marked done.

- [ ] **Step 5: Commit and push**

Commit (`docs(p1): T10.2 record controls, passwords and evidence`) and push.
No CI run is needed for a docs-only commit after the Step 1 run; the PR
says that the runs are on the commit before it.
