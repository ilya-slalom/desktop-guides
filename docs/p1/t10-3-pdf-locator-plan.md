# T10.3 PDF Locator and Restore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capture how far down a PDF page the reader is, keep that point
across resizes and re-renders, and restore a saved page and point safely
when the saved value is out of range, malformed or from changed bytes
(TR10.1, TR10.3).

**Architecture:** The rules go in `DesktopGuides.Core/Pdf/`:
`PdfLocationRules` (capture and restore, now with the page fraction and
changed-byte matching) and a new `PdfPagePosition` tracker (which fraction a
shown page gets, what a resize re-applies, which scroll events are the
user's). Both have unit tests. `PdfReaderView` reports layout and scroll
events and scrolls on request; `PdfReaderSession` wires the tracker
between them. Production has no unit test host, so a new installed smoke
phase, `pdf-resize`, covers the wiring.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), xUnit, PowerShell 5.1
UI Automation smoke.

**Spec:** [t10-3-pdf-locator-design.md](t10-3-pdf-locator-design.md)

## Global Constraints

**Branch:** `feat/p1-t10-3-pdf-locator` (already checked out; the spec is
commit `d6c3a97`).

**Tooling:**
- There is no local `dotnet` or `pwsh` on the authoring Mac. Every "Run" step
  is a CI run:
  1. Push, then trigger it with `gh workflow run windows-ci.yml --ref feat/p1-t10-3-pdf-locator`.
  2. Get its ID with `gh run list --workflow windows-ci.yml --branch feat/p1-t10-3-pdf-locator --limit 1 --json databaseId,headSha -q '.[0]'`
     and check that `headSha` is the commit you pushed.
  3. Watch it with `gh run watch <id> --exit-status --interval 60`.
  4. If it fails, read `gh run view <id> --log-failed`.
- The `core-tests` job (about 3 minutes) runs Core.Tests and
  Infrastructure.Tests; every other job needs it. The `production-shell-ui`
  job (a full run is about 20 minutes) builds Production and runs the
  installed smoke. A core-only step may cancel the run with
  `gh run cancel <id>` once `core-tests` has finished.
- A RED run may be batched with the previous task's GREEN run only when they
  land in different jobs (Task 3's smoke RED rides with Task 2's core GREEN).
- PowerShell files stay ASCII-only. Check with
  `LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/windows_shell_ui_smoke.ps1`
  (expected: no output).

**Values (verbatim):**
- `ChangedReason`: "The guide changed, so this is an approximate position."
  (equal to `TextLocator.ApproximateReason`).
- `ClampedReason`: "That page isn't in this guide, so the nearest page is shown." (unchanged).
- `UnavailableReason`: "This reading position can't be used with this guide." (unchanged).
- A valid page fraction is in [0, 1]; NaN and infinities are invalid.
- The estimate is `(pageIndex + pageFraction) / pageCount`, at most 1.
- The scroll echo tolerance is **1** effective pixel.
- The smoke's target fraction is **0.3** on page **121** of `pdf-long`; the
  tolerance is **0.1**; each check polls for up to **5 s** and then must
  still hold after **1 s**; a new page must sit below **0.02**.
- New AutomationId: `PdfPreviewScroller`.

**Rules:**
- In-session only. Nothing here writes `ReadingStates` or restores on reopen
  (T12.2). No restore result reads or writes completion.
- A locator is untrusted. No locator content is logged.
- Everything in the session runs on the UI thread and stops once
  `DisposeAsync` starts.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

**Spec refinements made while planning** (each one is recorded in the
design doc by Task 5):
- **P1. Scrolled ignores a layout change.** `Scrolled` also returns
  `false` when the image or viewport height differs by more than 1 px from
  the last `OffsetFor` call. When a resize shrinks the scroll range, the
  scroller clamps its own offset and reports it as a final scroll. Without
  this rule that clamp would read as a user scroll and lower the saved
  point. The layout event that follows re-applies the point.
- **P2. The session applies the point after ShowPage.** `Apply` calls
  `View.ScrollTo(OffsetFor(...))` straight after `ShowPage`, instead of the
  view raising `PreviewLayoutChanged` from `ShowPage`. A new image of the
  same height raises no `SizeChanged`, so this call covers it; a new height
  raises `SizeChanged`, which re-applies the point.
- **P3. Same-page targets apply at once.** A restore or edge command for
  the page already shown applies its fraction straight away, since no load
  will. A page turn that is clamped at either end does nothing, as in T10.1.
- **P4. The smoke's resize order and next-page check change.**
  - The order is 600, then 1100, then 1500 (the width where 0.3 was set).
  - At each width the smoke expects `min(0.3, 1 - viewSize/100)`. At
    1100 px the page may be too short to put 0.3 at the top, and the bottom
    clamp is correct behaviour there.
  - The final 1500 px check is against 0.3 itself. It shows that the clamp
    didn't reduce the stored point.
  - The next-page check turns two pages, to portrait page 123. Landscape
    page 122 may have nothing to scroll, so it would pass even with the bug.

## Review Focus

- **A resize clamp mistaken for a user scroll.** Shrinking the window
  clamps the scroller's offset and raises a final `ViewChanged`. The saved
  point must not drop. Pinned by
  `PdfPagePositionTests.AScrollAfterTheLayoutChangedIsIgnored` (Task 2) and
  the smoke's 1100 px then 1500 px checks (Task 3).
- **Before the first layout.** Before layout, heights are 0 or NaN. The
  offset must be 0 and no scroll may count. Pinned by
  `ABadHeightGivesTheTopAndNoScroll` (Task 2).
- **Mixed portrait and landscape pages.** A page with nothing to scroll
  must not count as a scroll, and the next page must start at its top
  whatever its shape. Pinned by `APageWithNothingToScrollIgnoresScrolls`
  (Task 2) and the smoke's turn to page 123 (Task 3).
- **A restore for the page already shown, or in flight.** The fraction must
  still apply, once. Pinned by `ATargetOnTheShownPageReplacesItsPoint` and
  `AnotherPageShownFirstKeepsThePendingTarget` (Task 2). The session's
  same-page branch has no shell caller until T12.2. The reviewer should
  read it against these two tests.
- **DPI rounding of the app's own scroll.** The scroller snaps offsets to
  physical pixels, so the echo can differ by a fraction of a pixel. It must
  not count as a user scroll. Pinned by `TheEchoOfAnAppliedOffsetIsIgnored`
  (Task 2).

---

### Task 1: Page fraction and changed bytes in `PdfLocationRules`

**Files:**
- Modify: `src/DesktopGuides.Core/Pdf/PdfLocationRules.cs` (whole file)
- Modify: `src/DesktopGuides.Production/PdfReaderSession.cs:125` (the
  `Capture` call only, so Production keeps compiling)
- Test: `tests/DesktopGuides.Core.Tests/PdfLocationRulesTests.cs` (whole file)

**Interfaces:**
- Consumes: `ReaderLocation`, `PdfPosition(int PageIndex, double PageFraction)`,
  `ReaderLocationCodec.{CurrentVersion, Serialize, Deserialize}`,
  `LocationDecodeStatus.ContentChanged`, `RestoreOutcome`, `RestoreKind`,
  `TextLocator.ApproximateReason`.
- Produces:
  - `public sealed record PdfRestore(int PageIndex, double PageFraction, RestoreOutcome Outcome);`
  - `PdfLocationRules.ChangedReason` (const string);
  - `PdfLocationRules.Capture(string contentSha256, int pageIndex, double pageFraction, int pageCount)`;
  - `PdfLocationRules.Restore(ReaderLocation location, string contentSha256, int pageCount)`,
    which returns `PdfRestore(0, 0, Unavailable)` for an unusable location.

- [ ] **Step 1: Write the failing tests**

Replace `tests/DesktopGuides.Core.Tests/PdfLocationRulesTests.cs` with:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfLocationRulesTests
{
    private const string Sha = "ab12";

    private static ReaderLocation At(int page, double fraction = 0, string sha = Sha) =>
        new(GuideFormat.Pdf, ReaderLocationCodec.CurrentVersion, sha, new PdfPosition(page, fraction), null);

    [Fact]
    public void CaptureRecordsThePagePointAndAnEstimate()
    {
        ReaderLocation location = PdfLocationRules.Capture("AB12", 4, 0.5, 200);

        Assert.Equal(GuideFormat.Pdf, location.Format);
        Assert.Equal(ReaderLocationCodec.CurrentVersion, location.SchemaVersion);
        Assert.Equal("ab12", location.ContentSha256);
        Assert.Equal(new PdfPosition(4, 0.5), location.Payload);
        Assert.Equal(4.5 / 200, location.EstimatedFraction);
    }

    [Fact]
    public void TheBottomOfTheLastPageEstimatesTheEnd() =>
        Assert.Equal(1.0, PdfLocationRules.Capture(Sha, 199, 1, 200).EstimatedFraction);

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void CaptureRejectsABadFraction(double fraction) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfLocationRules.Capture(Sha, 3, fraction, 200));

    [Fact]
    public void CapturedLocationsSurviveTheCodec()
    {
        string sha = new('a', 64);
        ReaderLocation location = PdfLocationRules.Capture(sha, 199, 0.25, 200);

        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            ReaderLocationCodec.Serialize(location), GuideFormat.Pdf, sha);

        Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
        Assert.Equal(new PdfPosition(199, 0.25), decoded.Location!.Payload);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(57, 0.4)]
    [InlineData(199, 1.0)]
    public void AnInRangePageIsExact(int page, double fraction)
    {
        PdfRestore restore = PdfLocationRules.Restore(At(page, fraction), Sha, 200);

        Assert.Equal(page, restore.PageIndex);
        Assert.Equal(fraction, restore.PageFraction);
        Assert.Equal(RestoreKind.Exact, restore.Outcome.Kind);
        Assert.Null(restore.Outcome.Reason);
    }

    [Fact]
    public void ShaComparisonIgnoresCase() =>
        Assert.Equal(RestoreKind.Exact, PdfLocationRules.Restore(At(3, sha: "AB12"), Sha, 200).Outcome.Kind);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(200, 199)]
    [InlineData(int.MaxValue, 199)]
    public void AnOutOfRangePageIsClampedToItsTop(int page, int expected)
    {
        PdfRestore restore = PdfLocationRules.Restore(At(page, 0.6), Sha, 200);

        Assert.Equal(expected, restore.PageIndex);
        Assert.Equal(0, restore.PageFraction);
        Assert.Equal(RestoreKind.Approximate, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.ClampedReason, restore.Outcome.Reason);
    }

    [Fact]
    public void ChangedBytesKeepThePointButAreApproximate()
    {
        PdfRestore restore = PdfLocationRules.Restore(At(57, 0.4, "ff00"), Sha, 200);

        Assert.Equal(57, restore.PageIndex);
        Assert.Equal(0.4, restore.PageFraction);
        Assert.Equal(RestoreKind.Approximate, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.ChangedReason, restore.Outcome.Reason);
    }

    [Fact]
    public void ChangedBytesWithAMissingPageGoToTheNearestPageTop()
    {
        PdfRestore restore = PdfLocationRules.Restore(At(250, 0.4, "ff00"), Sha, 200);

        Assert.Equal(199, restore.PageIndex);
        Assert.Equal(0, restore.PageFraction);
        Assert.Equal(RestoreKind.Approximate, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.ChangedReason, restore.Outcome.Reason);
    }

    [Fact]
    public void TheChangedReasonMatchesText() =>
        Assert.Equal(TextLocator.ApproximateReason, PdfLocationRules.ChangedReason);

    [Fact]
    public void ADecodedChangedLocationRestoresApproximately()
    {
        string saved = new('a', 64);
        string current = new('b', 64);
        string json = ReaderLocationCodec.Serialize(PdfLocationRules.Capture(saved, 10, 0.3, 200));

        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(json, GuideFormat.Pdf, current);
        PdfRestore restore = PdfLocationRules.Restore(decoded.Location!, current, 200);

        Assert.Equal(LocationDecodeStatus.ContentChanged, decoded.Status);
        Assert.Equal(10, restore.PageIndex);
        Assert.Equal(0.3, restore.PageFraction);
        Assert.Equal(PdfLocationRules.ChangedReason, restore.Outcome.Reason);
    }

    [Fact]
    public void AnotherFormatIsUnavailable() =>
        AssertUnavailable(new ReaderLocation(GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, Sha,
            new TextPosition(0, "x"), null));

    [Fact]
    public void APdfLocationWithATextPayloadIsUnavailable() =>
        AssertUnavailable(new ReaderLocation(GuideFormat.Pdf, ReaderLocationCodec.CurrentVersion, Sha,
            new TextPosition(0, "x"), null));

    [Fact]
    public void AnotherSchemaVersionIsUnavailable() =>
        AssertUnavailable(At(3) with { SchemaVersion = ReaderLocationCodec.CurrentVersion + 1 });

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ABadPageFractionIsUnavailable(double fraction) =>
        AssertUnavailable(At(3, fraction));

    [Fact]
    public void NoPagesIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfLocationRules.Restore(At(0), Sha, 0));

    private static void AssertUnavailable(ReaderLocation location)
    {
        PdfRestore restore = PdfLocationRules.Restore(location, Sha, 200);

        Assert.Equal(0, restore.PageIndex);
        Assert.Equal(0, restore.PageFraction);
        Assert.Equal(RestoreKind.Unavailable, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.UnavailableReason, restore.Outcome.Reason);
    }
}
```

`RestoreOutcome(RestoreKind Kind, string? Reason = null)` gives `Exact` a
null reason.

- [ ] **Step 2: Run the tests and confirm they fail**

Commit the tests on their own (Step 5's first commit), push, and trigger
CI.
Expected: `core-tests` fails to compile Core.Tests. The errors name the
4-argument `Capture`, `PdfRestore.PageFraction` and
`PdfLocationRules.ChangedReason`.

- [ ] **Step 3: Write the implementation**

Replace `src/DesktopGuides.Core/Pdf/PdfLocationRules.cs` with:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Pdf;

public sealed record PdfRestore(int PageIndex, double PageFraction, RestoreOutcome Outcome);

// A PDF position is a page and the share of that page above the top of the
// viewport. A location for other bytes of the guide keeps its page and point
// but is only approximate; one that can't be used opens at the first page.
public static class PdfLocationRules
{
    public const string ClampedReason = "That page isn't in this guide, so the nearest page is shown.";
    public const string ChangedReason = "The guide changed, so this is an approximate position.";
    public const string UnavailableReason = "This reading position can't be used with this guide.";

    public static ReaderLocation Capture(string contentSha256, int pageIndex, double pageFraction, int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, pageCount);
        if (!ValidFraction(pageFraction))
        {
            throw new ArgumentOutOfRangeException(nameof(pageFraction), pageFraction, "A page fraction is between 0 and 1.");
        }
        return new ReaderLocation(
            GuideFormat.Pdf,
            ReaderLocationCodec.CurrentVersion,
            contentSha256.ToLowerInvariant(),
            new PdfPosition(pageIndex, pageFraction),
            Math.Min(1, (pageIndex + pageFraction) / pageCount));
    }

    public static PdfRestore Restore(ReaderLocation location, string contentSha256, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
        if (location.Format != GuideFormat.Pdf ||
            location.SchemaVersion != ReaderLocationCodec.CurrentVersion ||
            location.Payload is not PdfPosition position ||
            !ValidFraction(position.PageFraction))
        {
            return new PdfRestore(0, 0, new RestoreOutcome(RestoreKind.Unavailable, UnavailableReason));
        }
        int page = Math.Clamp(position.PageIndex, 0, pageCount - 1);
        bool clamped = page != position.PageIndex;
        // A point on a page that isn't there means nothing; show its top.
        double fraction = clamped ? 0 : position.PageFraction;
        if (!string.Equals(location.ContentSha256, contentSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new PdfRestore(page, fraction, new RestoreOutcome(RestoreKind.Approximate, ChangedReason));
        }
        return clamped
            ? new PdfRestore(page, 0, new RestoreOutcome(RestoreKind.Approximate, ClampedReason))
            : new PdfRestore(page, fraction, new RestoreOutcome(RestoreKind.Exact));
    }

    // NaN fails both comparisons.
    private static bool ValidFraction(double fraction) => fraction is >= 0 and <= 1;
}
```

In `src/DesktopGuides.Production/PdfReaderSession.cs`, `GetLocationAsync`,
change only the `Capture` call so Production still compiles. Task 4
replaces this with the tracker's values.

```csharp
        return Task.FromResult(PdfLocationRules.Capture(guide.ContentSha256, displayed, 0, pageCount));
```

`RestoreLocationAsync` only reads `PageIndex` and `Outcome`, so it compiles
unchanged.

- [ ] **Step 4: Run the tests and confirm they pass**

Push and trigger CI.
Expected: `core-tests` is green, including all `PdfLocationRulesTests`.
Cancel the run once `core-tests` has finished.

- [ ] **Step 5: Commit**

Two commits. The first is made before the push in Step 2:

```bash
git add tests/DesktopGuides.Core.Tests/PdfLocationRulesTests.cs
git commit -m "test(p1): T10.3 PDF page fraction and changed-byte restore"
```

The second is made before the push in Step 4:

```bash
git add src/DesktopGuides.Core/Pdf/PdfLocationRules.cs src/DesktopGuides.Production/PdfReaderSession.cs
git commit -m "feat(p1): T10.3 restore PDF page fraction and changed bytes"
```

---

### Task 2: `PdfPagePosition` tracker

**Files:**
- Create: `src/DesktopGuides.Core/Pdf/PdfPagePosition.cs`
- Test: `tests/DesktopGuides.Core.Tests/PdfPagePositionTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces (`DesktopGuides.Core.Pdf.PdfPagePosition`, a sealed class with a
  parameterless constructor):
  - `void Target(int page, double fraction)`: throws
    `ArgumentOutOfRangeException` on a negative page or a fraction outside
    [0, 1].
  - `void Shown(int page)`: if `page` is the pending target, the point
    becomes the target's fraction and the target is consumed. Otherwise
    the same page keeps its point, and any other page gets 0 while the
    pending target is kept.
  - `double OffsetFor(double imageHeight, double viewportHeight)`.
  - `bool Scrolled(double offset, double imageHeight, double viewportHeight)`.
  - `int Page { get; }` and `double Fraction { get; }`, both 0 initially.

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/PdfPagePositionTests.cs`:

```csharp
using DesktopGuides.Core.Pdf;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfPagePositionTests
{
    // A 1,000 px tall page in a 400 px viewport scrolls 600 px.
    private static PdfPagePosition ScrolledTo(int page, double offset)
    {
        PdfPagePosition position = new();
        position.Shown(page);
        position.OffsetFor(1000, 400);
        Assert.True(position.Scrolled(offset, 1000, 400));
        return position;
    }

    [Fact]
    public void ANewPageStartsAtItsTop()
    {
        PdfPagePosition position = ScrolledTo(0, 300);
        position.Target(1, 0);

        position.Shown(1);

        Assert.Equal(1, position.Page);
        Assert.Equal(0, position.Fraction);
        Assert.Equal(0, position.OffsetFor(1000, 400));
    }

    [Fact]
    public void ARestoreTargetAppliesItsFractionOnce()
    {
        PdfPagePosition position = new();
        position.Target(5, 0.4);

        position.Shown(5);
        Assert.Equal(0.4, position.Fraction);
        Assert.Equal(400, position.OffsetFor(1000, 400));

        position.Shown(6);
        position.Shown(5);
        Assert.Equal(0, position.Fraction);
    }

    [Fact]
    public void AnotherPageShownFirstKeepsThePendingTarget()
    {
        PdfPagePosition position = new();
        position.Target(5, 0.4);

        position.Shown(3);
        Assert.Equal(0, position.Fraction);

        position.Shown(5);
        Assert.Equal(0.4, position.Fraction);
    }

    [Fact]
    public void AReRenderKeepsThePoint()
    {
        PdfPagePosition position = ScrolledTo(2, 300);

        position.Shown(2);

        Assert.Equal(0.3, position.Fraction, 6);
    }

    [Fact]
    public void ATargetOnTheShownPageReplacesItsPoint()
    {
        PdfPagePosition position = ScrolledTo(4, 500);
        position.Target(4, 0.2);

        position.Shown(4);

        Assert.Equal(0.2, position.Fraction);
    }

    [Fact]
    public void AWidthChangeReappliesTheFraction()
    {
        PdfPagePosition position = ScrolledTo(2, 300);

        Assert.Equal(600, position.OffsetFor(2000, 400), 6);
    }

    [Fact]
    public void TheBottomClampDoesntReduceTheFraction()
    {
        PdfPagePosition position = new();
        position.Target(0, 0.8);
        position.Shown(0);

        Assert.Equal(600, position.OffsetFor(1000, 400));
        Assert.Equal(0.8, position.Fraction);
        Assert.Equal(2400, position.OffsetFor(3000, 400));
    }

    [Fact]
    public void TheEchoOfAnAppliedOffsetIsIgnored()
    {
        PdfPagePosition position = new();
        position.Target(0, 0.3);
        position.Shown(0);
        double applied = position.OffsetFor(1000, 400);

        Assert.False(position.Scrolled(applied + 0.5, 1000, 400));
        Assert.Equal(0.3, position.Fraction);
    }

    [Fact]
    public void AUserScrollMovesThePointOnce()
    {
        PdfPagePosition position = ScrolledTo(0, 500);

        Assert.Equal(0.5, position.Fraction);
        Assert.False(position.Scrolled(500, 1000, 400));
    }

    [Fact]
    public void AScrollAfterTheLayoutChangedIsIgnored()
    {
        PdfPagePosition position = ScrolledTo(0, 500);

        // The window shrank: the scroller clamped its own offset.
        Assert.False(position.Scrolled(400, 800, 400));
        Assert.False(position.Scrolled(400, 1000, 600));
        Assert.Equal(0.5, position.Fraction);
    }

    [Fact]
    public void AScrollBeforeAnyOffsetIsIgnored()
    {
        PdfPagePosition position = new();
        position.Shown(0);

        Assert.False(position.Scrolled(100, 1000, 400));
        Assert.Equal(0, position.Fraction);
    }

    [Fact]
    public void APageWithNothingToScrollIgnoresScrolls()
    {
        PdfPagePosition position = new();
        position.Shown(0);

        Assert.Equal(0, position.OffsetFor(300, 400));
        Assert.False(position.Scrolled(10, 300, 400));
        Assert.Equal(0, position.Fraction);
    }

    [Theory]
    [InlineData(double.NaN, 400)]
    [InlineData(double.PositiveInfinity, 400)]
    [InlineData(0, 400)]
    [InlineData(-1, 400)]
    [InlineData(1000, double.NaN)]
    [InlineData(1000, 0)]
    [InlineData(1000, -1)]
    public void ABadHeightGivesTheTopAndNoScroll(double imageHeight, double viewportHeight)
    {
        PdfPagePosition position = new();
        position.Target(0, 0.5);
        position.Shown(0);

        Assert.Equal(0, position.OffsetFor(imageHeight, viewportHeight));
        Assert.False(position.Scrolled(100, imageHeight, viewportHeight));
        Assert.Equal(0.5, position.Fraction);
    }

    [Fact]
    public void ANonFiniteOffsetIsIgnored()
    {
        PdfPagePosition position = ScrolledTo(0, 300);

        Assert.False(position.Scrolled(double.NaN, 1000, 400));
        Assert.Equal(0.3, position.Fraction, 6);
    }

    [Theory]
    [InlineData(-1, 0.0)]
    [InlineData(0, -0.1)]
    [InlineData(0, 1.1)]
    [InlineData(0, double.NaN)]
    public void ABadTargetIsRejected(int page, double fraction) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPagePosition().Target(page, fraction));
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Commit the tests (Step 5's first commit), push and trigger CI.
Expected: `core-tests` fails to compile Core.Tests because
`PdfPagePosition` doesn't exist.

- [ ] **Step 3: Write the implementation**

Create `src/DesktopGuides.Core/Pdf/PdfPagePosition.cs`:

```csharp
namespace DesktopGuides.Core.Pdf;

// Where the reader is on the shown PDF page: the share of the page above
// the top of the viewport. A resize or re-render keeps it; a new page starts
// at its top unless a restore asked for a point. The session uses it on the
// UI thread only; it isn't thread-safe.
public sealed class PdfPagePosition
{
    // The scroller snaps offsets to physical pixels, so its echo of the
    // app's own scroll can be a little off.
    private const double EchoTolerance = 1;
    private int? pendingPage;
    private double pendingFraction;
    private double appliedOffset = double.NaN;
    private double appliedImage = double.NaN;
    private double appliedViewport = double.NaN;

    public int Page { get; private set; }
    public double Fraction { get; private set; }

    // The session is about to request this page: 0 for a turn, the saved
    // point for a restore.
    public void Target(int page, double fraction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        if (fraction is not (>= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "A page fraction is between 0 and 1.");
        }
        pendingPage = page;
        pendingFraction = fraction;
    }

    public void Shown(int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        if (page == pendingPage)
        {
            Fraction = pendingFraction;
            pendingPage = null;
        }
        else if (page != Page)
        {
            Fraction = 0;
        }
        Page = page;
    }

    // The bottom clamp limits the offset, never the stored point, so a
    // taller window returns to it.
    public double OffsetFor(double imageHeight, double viewportHeight)
    {
        double offset = Usable(imageHeight, viewportHeight)
            ? Math.Clamp(Fraction * imageHeight, 0, Math.Max(0, imageHeight - viewportHeight))
            : 0;
        appliedOffset = offset;
        appliedImage = imageHeight;
        appliedViewport = viewportHeight;
        return offset;
    }

    // A scroll came to rest. Only a user's scroll moves the point: the echo
    // of an applied offset doesn't, nor does the scroller's own clamp after
    // a layout change, which the next layout event re-applies over.
    public bool Scrolled(double offset, double imageHeight, double viewportHeight)
    {
        if (!Usable(imageHeight, viewportHeight) || !double.IsFinite(offset) ||
            imageHeight <= viewportHeight ||
            !Near(imageHeight, appliedImage) || !Near(viewportHeight, appliedViewport) ||
            Near(offset, appliedOffset))
        {
            return false;
        }
        Fraction = Math.Clamp(offset / imageHeight, 0, 1);
        appliedOffset = offset;
        return true;
    }

    private static bool Usable(double imageHeight, double viewportHeight) =>
        double.IsFinite(imageHeight) && double.IsFinite(viewportHeight) &&
        imageHeight > 0 && viewportHeight > 0;

    // False when either side is NaN, so nothing is near an offset not yet applied.
    private static bool Near(double a, double b) => Math.Abs(a - b) <= EchoTolerance;
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

This run is batched with Task 3's RED, so make Task 3's commit first, then
push and trigger CI.
Expected: `core-tests` is green, including all `PdfPagePositionTests` and
`PdfLocationRulesTests`. `production-shell-ui` then fails as Task 3 Step 4
describes.

- [ ] **Step 5: Commit**

Two commits. The first is made before the push in Step 2:

```bash
git add tests/DesktopGuides.Core.Tests/PdfPagePositionTests.cs
git commit -m "test(p1): T10.3 PDF within-page position tracker"
```

The second is made after Step 3:

```bash
git add src/DesktopGuides.Core/Pdf/PdfPagePosition.cs
git commit -m "feat(p1): T10.3 PDF within-page position tracker"
```

---

### Task 3: Installed `pdf-resize` smoke (RED)

**Files:**
- Modify: `src/DesktopGuides.Production/PdfReaderView.xaml:33-36`, which
  adds the AutomationId only (a test hook with no behaviour, so the RED
  fails on the real defect and not on a missing element)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`, in the `pdf-reader` branch:
  helpers after `Invoke-NextPages` (about line 1700), and the phase after
  `$report.phases += 'pdf-long'` (about line 1762)

**Interfaces:**
- Consumes: the smoke's `Find-ById`, `Resize-ShellWindow`,
  `Invoke-ReaderCommand`, `Save-WindowScreenshot`, `Wait-PdfPage` and
  `Invoke-NextPages`; `pdf-long`'s portrait odd pages (612 x 792) and
  landscape even pages (792 x 612), with the text "Desktop Guides P0 - page
  N of 200".
- Produces:
  - the `PdfPreviewScroller` AutomationId;
  - the phase name `pdf-resize`;
  - report fields `pdfPosition` (`set`, `narrow`, `medium`, `wide`,
    `nextPage`) and `pdfResizeScreenshot`, which Task 5 reads.

The install harness checks no phase list. A throwing smoke fails the pass,
so it needs no change.

- [ ] **Step 1: Add the AutomationId**

In `src/DesktopGuides.Production/PdfReaderView.xaml`, give the scroller an
AutomationId:

```xml
            <ScrollViewer x:Name="PreviewScroller"
                          Grid.Row="1"
                          AutomationProperties.AutomationId="PdfPreviewScroller"
                          HorizontalScrollBarVisibility="Disabled"
                          VerticalScrollBarVisibility="Auto">
```

- [ ] **Step 2: Add the helpers**

In `tools/p1/windows_shell_ui_smoke.ps1`, directly after the closing brace
of `function Invoke-NextPages`, add:

```powershell

            function Get-PdfScroll {
                $scroller = Find-ById 'PdfPreviewScroller'
                if (-not $scroller) { throw 'The PDF preview has no scroller.' }
                return $scroller.GetCurrentPattern(
                    [System.Windows.Automation.ScrollPattern]::Pattern)
            }

            # The share of the page above the top of the viewport. The
            # scroller holds only the page image, so its extent is the page.
            # A page that can't scroll reads -1 percent at a 100% view: 0.
            function Get-PdfFraction {
                $scroll = Get-PdfScroll
                return $scroll.Current.VerticalScrollPercent / 100 *
                    (1 - $scroll.Current.VerticalViewSize / 100)
            }

            # Waits for the point to sit within 0.1 of $wanted, or of the
            # lowest point this window can scroll to if that is higher up,
            # and to stay there through the debounced re-render.
            function Wait-PdfFraction([double] $wanted, [string] $context) {
                $deadline = (Get-Date).AddSeconds(5)
                $held = $false
                do {
                    $scroll = Get-PdfScroll
                    $expected = [Math]::Min($wanted, 1 - $scroll.Current.VerticalViewSize / 100)
                    $fraction = Get-PdfFraction
                    if ([Math]::Abs($fraction - $expected) -le 0.1) {
                        if ($held) { return $fraction }
                        $held = $true
                        Start-Sleep -Milliseconds 1000
                        continue
                    }
                    $held = $false
                    Start-Sleep -Milliseconds 100
                } while ((Get-Date) -lt $deadline)
                throw ("$context left the page at fraction $([Math]::Round($fraction, 3)); " +
                    "expected $([Math]::Round($expected, 3)) +/- 0.1.")
            }
```

The loop rechecks after the 1 s hold. A value that drifts away during the
hold (T10.1's re-render scrolling to the top) resets `$held` and keeps
polling until the deadline, then fails with the last value.

- [ ] **Step 3: Add the phase**

Directly after `$report.phases += 'pdf-long'`, before the
`# Going back closes the session` comment, add:

```powershell

            # pdf-resize (T10.3): a point 30% down portrait page 121 survives
            # narrow, medium and wide windows; a page turn starts at the top.
            Invoke-ReaderCommand 'Go to start'
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            Invoke-NextPages 120
            [void](Wait-PdfPage 121 200 'page 121 of 200' 60)
            $scroll = Get-PdfScroll
            $room = 1 - $scroll.Current.VerticalViewSize / 100
            if (-not $scroll.Current.VerticallyScrollable -or $room -lt 0.3) {
                throw "Page 121 scrolls only $([Math]::Round($room, 3)) of its height; pdf-resize needs 0.3."
            }
            $scroll.SetScrollPercent(
                [System.Windows.Automation.ScrollPattern]::NoScroll, 0.3 / $room * 100)
            Start-Sleep -Milliseconds 300
            $set = Get-PdfFraction
            if ([Math]::Abs($set - 0.3) -gt 0.02) {
                throw "Scrolling page 121 reached fraction $([Math]::Round($set, 3)); expected 0.3."
            }
            $report.pdfPosition = [ordered]@{ set = $set }
            # 1500 last: the page was scrolled there, so 0.3 itself must return
            # even if 1100 px clamped it.
            foreach ($size in @(@(600, 'narrow'), @(1100, 'medium'), @(1500, 'wide'))) {
                Resize-ShellWindow $size[0] 720
                $report.pdfPosition[$size[1]] = Wait-PdfFraction 0.3 "Resizing to $($size[0]) px"
                [void](Wait-PdfPage 121 200 'page 121 of 200')
            }
            $report.pdfResizeScreenshot = Save-WindowScreenshot 'pdf-resize'
            # Page 122 is landscape and may not scroll; 123 is portrait.
            Invoke-NextPages 2
            [void](Wait-PdfPage 123 200 'page 123 of 200')
            Start-Sleep -Milliseconds 300
            $nextPage = Get-PdfFraction
            if ($nextPage -ge 0.02) {
                throw "Page 123 opened at fraction $([Math]::Round($nextPage, 3)); expected its top."
            }
            $report.pdfPosition.nextPage = $nextPage
            $report.phases += 'pdf-resize'
```

Then confirm the file is still ASCII-only:
`LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/windows_shell_ui_smoke.ps1`
Expected: no output.

- [ ] **Step 4: Run the smoke and confirm it fails**

Commit (Step 5), then push together with Task 2's implementation commit and
trigger CI.
Expected: `core-tests` is green (Task 2 Step 4). `production-shell-ui`
fails in `pdf-reader-light` in the `pdf-resize` phase, with "Resizing to
<600|1100|1500> px left the page at fraction <about 0>; expected ... +/-
0.1." This happens because T10.1's re-render scrolls the preview back to the
top.

If it fails elsewhere, fix the script before going on. For example:
- "has no scroller" means the AutomationId isn't reaching UI Automation;
- "scrolls only" means the layout is shorter than planned;
- "Scrolling page 121 reached" means `SetScrollPercent` didn't apply.

For "scrolls only", rule on a different page or window height, and ledger
it. The failure must come from the scroll-to-top defect.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Production/PdfReaderView.xaml tools/p1/windows_shell_ui_smoke.ps1
git commit -m "test(p1): T10.3 installed PDF resize position smoke"
```

---

### Task 4: View and session keep the point (GREEN)

**Files:**
- Modify: `src/DesktopGuides.Production/PdfReaderView.xaml.cs` (constructor,
  new members, `ShowPage`'s last line)
- Modify: `src/DesktopGuides.Production/PdfReaderSession.cs`. The changes:
  - fields;
  - the constructor;
  - `GetLocationAsync` and `RestoreLocationAsync`;
  - the `ExecuteAsync` switch and `GoTo`;
  - `Apply`;
  - three new handlers and helpers after `Apply`;
  - `DisposeAsync`'s unsubscribe lines.

**Interfaces:**
- Consumes:
  - Task 1's `PdfLocationRules.Capture(sha, page, fraction, count)` and
    `PdfRestore.PageFraction`;
  - Task 2's `PdfPagePosition` (`Target`, `Shown`, `OffsetFor`, `Scrolled`,
    `Page`, `Fraction`);
  - Task 3's smoke.
- Produces:
  - `public readonly record struct PdfPreviewLayout(double Offset, double ImageHeight, double ViewportHeight)`;
  - on `PdfReaderView`: `PdfPreviewLayout PreviewLayout { get; }`,
    `void ScrollTo(double offset)`, and the `PreviewLayoutChanged` and
    `PreviewScrolled` events (both `EventHandler?`).

No unit test host exists for Production. Task 3's smoke is this task's
test: it is RED now and must turn GREEN.

- [ ] **Step 1: Change the view**

In `src/DesktopGuides.Production/PdfReaderView.xaml.cs`:

Above the class, after the namespace line, add:

```csharp
// The preview's vertical scroll state, in effective pixels.
public readonly record struct PdfPreviewLayout(double Offset, double ImageHeight, double ViewportHeight);

```

Change the class comment's last sentence and the constructor to:

```csharp
// One PDF page: its preview beside its text, or the text under the preview
// below 720 effective pixels. The session decides what to show and where on
// the page to scroll; a new page never moves keyboard focus.
public sealed partial class PdfReaderView : UserControl
{
    private const double NarrowWidth = 720;
    private bool? narrow;

    public PdfReaderView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        PreviewScroller.SizeChanged += (_, _) =>
        {
            PreviewSizeChanged?.Invoke(this, EventArgs.Empty);
            PreviewLayoutChanged?.Invoke(this, EventArgs.Empty);
        };
        Preview.SizeChanged += (_, _) => PreviewLayoutChanged?.Invoke(this, EventArgs.Empty);
        PreviewScroller.ViewChanged += (_, args) =>
        {
            if (!args.IsIntermediate) PreviewScrolled?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? PreviewSizeChanged;
    // The page image or its viewport changed height.
    public event EventHandler? PreviewLayoutChanged;
    // A scroll of the preview, the user's or the app's, came to rest.
    public event EventHandler? PreviewScrolled;

    // The width a preview fills, in effective pixels; 0 before layout.
    public double PreviewWidth => PreviewScroller.ActualWidth;

    public PdfPreviewLayout PreviewLayout =>
        new(PreviewScroller.VerticalOffset, Preview.ActualHeight, PreviewScroller.ViewportHeight);

    public void ScrollTo(double offset) =>
        PreviewScroller.ChangeView(null, offset, null, disableAnimation: true);
```

In `ShowPage`, delete the last line:

```csharp
        PreviewScroller.ChangeView(null, 0, null, disableAnimation: true);
```

`Preview.ActualHeight` is the image height the design's formula uses. It
is current when `Preview.SizeChanged` is raised, which may not be true of
`ExtentHeight`.

- [ ] **Step 2: Change the session's fields, constructor and locator methods**

In `src/DesktopGuides.Production/PdfReaderSession.cs`:

Replace the field `private int displayed;` with:

```csharp
    private readonly PdfPagePosition position = new();
```

(`position.Page` replaces `displayed` everywhere.)

In the constructor, after `View.PreviewSizeChanged += OnPreviewSizeChanged;`:

```csharp
        View.PreviewLayoutChanged += OnPreviewLayoutChanged;
        View.PreviewScrolled += OnPreviewScrolled;
```

In `GetLocationAsync`:

```csharp
        return Task.FromResult(
            PdfLocationRules.Capture(guide.ContentSha256, position.Page, position.Fraction, pageCount));
```

In `RestoreLocationAsync`, replace `GoTo(restore.PageIndex);` with:

```csharp
            GoTo(restore.PageIndex, restore.PageFraction);
```

The `Unavailable` guard stays: restore is only called at open (T12.2), so
the first page's top is already showing.

- [ ] **Step 3: Change the commands and `GoTo`**

Replace the two `case` bodies in `ExecuteAsync`:

```csharp
            // Turns build on the wanted page, not the shown one, so ten fast
            // Next clicks move ten pages. A turn past either end does nothing.
            case PageTurnAction turn:
                long next = Math.Clamp((long)target + turn.Delta, 0, pageCount - 1);
                if (next != target) GoTo(next, 0);
                break;
            case PageEdgeAction edge:
                GoTo(edge.Edge == ReaderEdge.Start ? 0 : pageCount - 1, 0);
                break;
```

Replace `GoTo` with:

```csharp
    // A new page applies the point when its load is shown. The page already
    // shown has no load coming, so it applies the point now.
    private void GoTo(long index, double fraction)
    {
        int page = (int)Math.Clamp(index, 0, pageCount - 1);
        position.Target(page, fraction);
        if (page != target)
        {
            target = page;
            scheduler.Request(page);
        }
        else if (page == position.Page)
        {
            position.Shown(page);
            ScrollToPoint();
            RaiseLocationChanged();
        }
    }
```

If `page == target` but the page isn't shown yet, its load is in flight.
That load's `Apply` consumes the pending target.

- [ ] **Step 4: Change `Apply` and add the handlers**

In `Apply`:
- Replace `displayed = index;` with `position.Shown(index);`.
- Inside the `try`, after `View.ShowPage(...)`, add `ScrollToPoint();`.
- Replace the `LocationChanged` try/catch with `RaiseLocationChanged();`.

The result:

```csharp
    // Runs on the UI thread, only for the newest request.
    private void Apply(int index, PageResult result)
    {
        if (disposed || failed) return;
        position.Shown(index);
        appliedWidth = result.RasterWidth;
        appliedAspect = result.Aspect;
        bool shown = true;
        try
        {
            View.ShowPage(index, pageCount, result.Image, result.Text);
            // A same-height image raises no SizeChanged, so apply the point
            // here; a new height re-applies it from OnPreviewLayoutChanged.
            ScrollToPoint();
        }
        catch (Exception)
        {
            // Apply must never throw: the scheduler would stall. A page that
            // can't be shown counts as failed both ways.
            shown = false;
        }
        failedPages = !shown || (result.Image is null && result.Text is null) ? failedPages + 1 : 0;
        RaiseLocationChanged();
        if (failedPages < FailuresBeforeStop) return;
        failed = true;
        // Queued, so the shell disposes the session after this load returns.
        View.DispatcherQueue.TryEnqueue(() =>
        {
            if (!disposed) Failed?.Invoke(this, PdfGuideLoadError.Failed);
        });
    }

    // The image or the viewport changed height: put the point back.
    private void OnPreviewLayoutChanged(object? sender, EventArgs args)
    {
        if (!disposed) ScrollToPoint();
    }

    // Only a user's scroll moves the point; PdfPagePosition ignores the
    // app's own scrolls and the scroller's clamps.
    private void OnPreviewScrolled(object? sender, EventArgs args)
    {
        if (disposed) return;
        PdfPreviewLayout layout = View.PreviewLayout;
        if (position.Scrolled(layout.Offset, layout.ImageHeight, layout.ViewportHeight))
        {
            RaiseLocationChanged();
        }
    }

    private void ScrollToPoint()
    {
        PdfPreviewLayout layout = View.PreviewLayout;
        View.ScrollTo(position.OffsetFor(layout.ImageHeight, layout.ViewportHeight));
    }

    private void RaiseLocationChanged()
    {
        try
        {
            LocationChanged?.Invoke(this, new LocationChangedEventArgs());
        }
        catch (Exception)
        {
            // A throwing handler must not escape into the scheduler or a
            // XAML event.
        }
    }
```

In `DisposeAsync`, after `View.PreviewSizeChanged -= OnPreviewSizeChanged;`:

```csharp
        View.PreviewLayoutChanged -= OnPreviewLayoutChanged;
        View.PreviewScrolled -= OnPreviewScrolled;
```

Check that nothing else still names `displayed`:
`grep -n "displayed" src/DesktopGuides.Production/PdfReaderSession.cs`
Expected: no output.

- [ ] **Step 5: Run the full CI and confirm it passes**

Commit (Step 6), push and trigger CI.
Expected: every job is green. `production-shell-ui` passes `pdf-resize`
in `pdf-reader-light` and `pdf-reader-dark`, and every earlier PDF phase
still passes. Record the run ID as `<RUN>` for Task 5.

If `pdf-resize` still fails, use superpowers:systematic-debugging before
changing anything. Start by reading the failure's fraction:
- About 0 means the point isn't being re-applied: check the event wiring
  and `PreviewLayout`'s heights.
- A value between 0 and 0.3 means a clamp was counted as a user scroll:
  check P1's layout guard against the heights the view reports.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Production/PdfReaderView.xaml.cs src/DesktopGuides.Production/PdfReaderSession.cs
git commit -m "feat(p1): T10.3 keep the PDF page point across resizes"
```

---

### Task 5: Docs and evidence

**Files:**
- Modify: `docs/p1/t10-3-pdf-locator-design.md` (status line; append
  "Implementation notes" and "Verification")
- Modify: `docs/p1/implementation-plan.md`:
  - the T10.1 paragraph (about line 817), to say merged;
  - a new T10.3 paragraph after it.
- Modify: `docs/work-breakdown.md` (the T10.3 bullet, about line 324)
- Modify: `docs/p1-technical-design.md` (the T10.3 bullet, about line 719)
- Create: `docs/p1/evidence/t10-3-pdf-locator/` holding
  `pdf-position-light.json`, `pdf-position-dark.json`,
  `pdf-resize-light.png` and `pdf-resize-dark.png`

**Interfaces:**
- Consumes: Task 4's green run `<RUN>` and its `production-shell-ui`
  artifact.
- Produces: docs only.

This task is mechanical (docs and copied artifacts), so it has no TDD cycle.

- [ ] **Step 1: Copy the evidence from the CI artifact**

```bash
RUN=<run id from Task 4 Step 5>
rm -rf /tmp/t10-3-artifact && mkdir -p /tmp/t10-3-artifact
gh run download "$RUN" --dir /tmp/t10-3-artifact
find /tmp/t10-3-artifact -name 'pdf-resize*' -o -name '*.json' | sort
```

Expected: a `pdf-resize` PNG in each of the `pdf-reader-light` and
`pdf-reader-dark` result folders, plus the install report JSON.

Then:
- Copy the PNGs into `docs/p1/evidence/t10-3-pdf-locator/` as
  `pdf-resize-<light|dark>.png`.
- From the report, write each pass's `pdfPosition` object to
  `pdf-position-<light|dark>.json`. It sits at
  `pdfReader.<pass>.pdfPosition`; if the nesting differs, find it with
  `grep -rl pdfPosition /tmp/t10-3-artifact`.
- Check the JSON with `cat docs/p1/evidence/t10-3-pdf-locator/*.json`.

Expected: only `set`, `narrow`, `medium`, `wide` and `nextPage`, all
numbers, with no paths and no guide text.

Open each PNG. It should show page 121 scrolled partway down at the wide
window, with its text beside it.

- [ ] **Step 2: Update the design doc**

Replace the status line in `docs/p1/t10-3-pdf-locator-design.md`:

```markdown
Status: implemented in PR #PRNUM; CI run <RUN> passed the installed
`pdf-resize` phase in light and dark.
```

`#PRNUM` is replaced once the PR exists.

Append the following. Replace each `<...>` with the light-pass value
rounded to three places, and the dark value in brackets:

```markdown
## Implementation notes

Planning refinements, from the
[implementation plan](t10-3-pdf-locator-plan.md):

- **P1.** `PdfPagePosition.Scrolled` also ignores a scroll whose image or
  viewport height differs by more than 1 px from the last applied offset's.
  A shrinking window makes the scroller clamp its own offset; that clamp is
  not a user scroll, and the layout event that follows re-applies the point.
- **P2.** The session applies the point straight after `ShowPage`, instead
  of the view raising `PreviewLayoutChanged` from `ShowPage`. A new image
  of the same height raises no `SizeChanged`.
- **P3.** A restore or edge command for the page already shown applies its
  point at once. A page turn clamped at either end does nothing.
- **P4.** The smoke resizes to 600, then 1100, then 1500 px.
  - At each width it expects the lower of 0.3 and the lowest point that
    width can scroll to.
  - The last check, at the 1500 px width where 0.3 was set, expects 0.3
    itself.
  - The next-page check turns two pages, to portrait page 123.
- The view reports the image's `ActualHeight` rather than the scroller's
  `ExtentHeight`, since it is current when the image's `SizeChanged` is
  raised.

## Verification

- `PdfLocationRulesTests` cover:
  - capture, the estimate and fraction rejection;
  - every restore row;
  - a decoded `ContentChanged` locator.

  `PdfPagePositionTests` cover the tracker rules, including the echo, the
  layout guard, the bottom clamp and bad heights.
- CI run <RUN>, `pdf-resize` on `pdf-long` page 121, light [dark]:

  | Step | Page fraction |
  | --- | --- |
  | Scrolled to 0.3 at 1500 px | <set> [<set>] |
  | 600 px | <narrow> [<narrow>] |
  | 1100 px | <medium> [<medium>] |
  | Back to 1500 px | <wide> [<wide>] |
  | Two pages on, page 123 | <nextPage> [<nextPage>] |

  The values are from
  [light](evidence/t10-3-pdf-locator/pdf-position-light.json) and
  [dark](evidence/t10-3-pdf-locator/pdf-position-dark.json). The page status
  and text named page 121 after each resize.
  Screenshots: [light](evidence/t10-3-pdf-locator/pdf-resize-light.png),
  [dark](evidence/t10-3-pdf-locator/pdf-resize-dark.png).
- Not covered: restoring on reopen and showing restore reasons (T12.2); zoom
  (T10.2).
```

If the 1100 px value is below 0.3 because of the bottom clamp, add one
sentence under the table saying so.

- [ ] **Step 3: Update the implementation plan, work breakdown and technical design**

In `docs/p1/implementation-plan.md`, replace the T10.1 paragraph's first
line, "T10.1 is in review in PR #36 (CI run 37169058467); see the", with:

```markdown
T10.1 was merged through PR #36 on 4 October 2026 (merge commit `e2b9b8c`;
CI run 37169058467); see the
```

After that paragraph, add:

```markdown
T10.3 is in review in PR #PRNUM (CI run <RUN>); see the
[design and implementation notes](t10-3-pdf-locator-design.md). A PDF
position is now a page and the share of the page above the viewport. It
survives narrow, medium and wide windows on `pdf-long` and resets to the
top on a page turn. A restore clamps a missing page to the nearest page's
top, keeps the point as `Approximate` when the bytes changed, and opens at
the first page for a malformed, wrong-format or future-version locator.
Saving it and restoring on reopen remain T12.2.
```

In `docs/work-breakdown.md`, replace the T10.3 bullet with:

```markdown
- **T10.3** Persist zero-based page index and within-page fraction.
  Implemented in PR #PRNUM (in-session capture and restore; saving is
  T12.2); see [p1/t10-3-pdf-locator-design.md](p1/t10-3-pdf-locator-design.md).
```

In `docs/p1-technical-design.md`, append to the T10.3 bullet, after "OCR
for `pdf-scan` remains out of scope.":

```markdown
  See the [T10.3 design](p1/t10-3-pdf-locator-design.md). The session
  captures and restores in memory. T12.2 persists the envelope and restores
  it on reopen, and T10.2 keeps the point across zoom.
```

- [ ] **Step 4: Commit**

```bash
git add docs/p1/t10-3-pdf-locator-design.md docs/p1/implementation-plan.md \
  docs/work-breakdown.md docs/p1-technical-design.md docs/p1/evidence/t10-3-pdf-locator
git commit -m "docs(p1): T10.3 PDF locator notes and evidence"
```

After the PR exists, replace `#PRNUM` in the three files with the real
number and commit that as `docs(p1): T10.3 link PR #<n>`.
