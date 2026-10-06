# T14.1 Text Size Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A TXT or HTML guide's text size moves through fixed steps from
75% to 200% from the toolbar or the keyboard, shows its value, resets to
100%, is saved per guide and survives a relaunch. TXT keeps its
fixed-width layout, PDF keeps its own zoom, and a failed save puts the
stored size back.

**Architecture:**

- Core gains the pure `TextSizeSteps` (steps, normalize, step up and
  down, label and status texts). `TextSizeAction` carries a target
  `Scale` and rejects one outside 0.75–2.0.
- `TextReaderSession` and `HtmlReaderSession` declare
  `ReaderCapabilities.TextSize` and apply a scale. TXT re-measures its
  rows through the existing `Remeasure()`; HTML rewrites T09.2's style
  with the new scale.
- `ReaderToolbar` shows *Smaller text* | `TextSizeValue` | *Larger text*
  and *Reset text size*, steps the scale, and raises `TextSizeChanged`.
- `ShellWindow.TextSize.cs` reads the stored scale at open, saves each
  change, serialized, and reverts a failed save.
- A new installed `text-size` group runs in the CI `core` shard.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.5.1), WebView2,
SQLite, xUnit, PowerShell UI Automation.

**Spec:** [t14-1-text-size-design.md](t14-1-text-size-design.md)

## Global Constraints

**Branch:** `feat/p1-t14-1-text-size`, already checked out. The spec is
commit `612d8b2`.

**Tooling:**

- There is no local `dotnet` or `pwsh`. Builds and tests run on the Windows
  host `pcsx2-win` or in CI.
- Host sync (staging folder `E:\work\desktop-guides\t14-1`, not a git
  checkout; create it once with `ssh -o BatchMode=yes pcsx2-win "mkdir E:\work\desktop-guides\t14-1"`):

  ```bash
  R=/Users/ilya.lissoboi/work/desktop-guides
  git -C $R ls-files -co --exclude-standard -z -- . ':!.claude' |
    tar -C $R --null -T - -cf - |
    ssh -o BatchMode=yes pcsx2-win "tar -xf - -C E:\work\desktop-guides\t14-1"
  ```

- Host commands (the host's default shell is `cmd`):

  ```bash
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-1\tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj"
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-1\tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t14-1\src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t14-1\tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t14-1\tools\p1\DesktopGuides.ReaderToolbarSmoke\DesktopGuides.ReaderToolbarSmoke.csproj -c Release -p:Platform=x64"
  ```

  To run one test class, add
  `--filter FullyQualifiedName~DesktopGuides.Core.Tests.<Class>`.
- The CI loop for branch `<b>` and group `<g>`:
  1. Push, then run
     `gh workflow run windows-ci.yml --ref <b> -f shell-scope=<g> [-f dev-fast=true]`.
  2. Run
     `gh run list --workflow windows-ci.yml --branch <b> --limit 1 --json databaseId,headSha -q '.[0]'`
     and check that `headSha` matches `git rev-parse HEAD`.
  3. Run `gh run watch <id> --exit-status --interval 60`.
  4. On failure, run `gh run view <id> --log-failed`, and download the
     shard artifact with `gh run download <id> -n production-shell-ui-<g>`.
- `dev-fast=true` runs are for iteration and are not PR evidence.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- PowerShell stays ASCII-only. Bash commands use absolute paths or
  `git -C`, never `cd`. Quote globs (zsh).
- Installed runs follow [e2e-testing.md](e2e-testing.md). No firewall
  rule. If a run needs an elevated scheduled task, stop and ask.
- Imported HTML is untrusted. The scale reaches the page only through
  T09.2's `HtmlReaderStyle.WriteScript`, which clamps it and formats it
  with the invariant culture.
- A size change isn't reader movement: it never saves a reading place.

**Values (verbatim from the spec):**

- Steps: 75, 90, 100, 110, 125, 150, 175 and 200% (`0.75, 0.9, 1.0,
  1.1, 1.25, 1.5, 1.75, 2.0`). `TextSizeSteps.Default = 1.0`.
- `Normalize`: a missing, non-finite or out-of-range stored value gives
  1.0; a value inside the range but between steps is kept.
- `Label(1.1)` is `110%` (invariant culture, rounded percent);
  `Status(1.1)` is `Text size 110%.`;
  `SaveFailed(m)` is `Could not save the text size: <m>`.
- `TextSizeAction(double Scale)`; a scale outside 0.75–2.0 (or non-finite)
  throws `ArgumentOutOfRangeException`.
- Toolbar: *Smaller text* | `TextSizeValue` | *Larger text*;
  *Reset text size* (`ResetTextSize`) in the overflow, disabled at 100%.
  At 75% *Smaller text* is disabled; at 200% *Larger text* is disabled.
- Keys: Ctrl+Plus, Ctrl+= and Ctrl+Shift+= are *Larger text*; Ctrl+Minus
  is *Smaller text*; Ctrl+0 is *Reset text size*. Tooltips:
  `Smaller text (Ctrl+Minus)`, `Larger text (Ctrl+Plus)`,
  `Reset text size (Ctrl+0)`; `AcceleratorKey` `Ctrl+Minus`, `Ctrl+Plus`,
  `Ctrl+0`.
- `txt-ascii` line 6 reads `Columns:   one     two`.
- CI: the `text-size` group runs after `theme` and before `import`, in
  the `core` shard (`core,txt,text-size,game-actions`).

**Rulings this plan makes against the spec** (Task 6 records them in the
spec's implementation notes):

1. **The text-size keys don't need `KeysEnabled`.** The shell turns keys
   on for PDF only; T16.1 owns the other TXT and HTML keys. Ctrl+Plus,
   Ctrl+Minus and Ctrl+0 reach the text-size commands whatever
   `KeysEnabled` says; every other key still needs it.
2. **`CommandFor` chooses by visibility.** Zoom and *Fit to width* win
   while they're visible, even when disabled, so "Ctrl+= with Zoom in
   disabled" still does nothing. Otherwise the keys go to the text-size
   commands.
3. **The toolbar does the stepping.** The spec puts the steps in the
   shell; the toolbar is the shell's control and already owns the
   command states, so it holds the current scale (set by the shell with
   `SetTextSize(double)`), computes the target with `TextSizeSteps`,
   updates the label and states, raises `TextSizeChanged(session,
   target)` and then executes `TextSizeAction(target)`. The event comes
   first, so the shell's scale is current if a theme refresh lands while
   an HTML write is running. On a failed save the shell calls
   `SetTextSize(stored)` and executes `TextSizeAction(stored)`.
4. **The TXT test hook keeps its own multiplier.** The view's font size is
   `baseFontSize * testScale * textScale`; the remeasure hook sets
   `testScale` to 1.5 and leaves `textScale` alone.
5. **The TXT initial scale is a constructor parameter.**
   `TextReaderSession(document, maxColumns, textScale, diagnosticsFolder)`
   passes it to the view before the first measure.
6. **One shell scale field.** `readerTextScale` serves TXT and HTML. It is
   read once at open with `TextSizeSteps.Normalize` and is what
   `RefreshReaderAppearance` sends. T09.2's `htmlTextScale` goes.
7. **TXT diagnostics.** With the gate
   `Local\DesktopGuides.Preview.TextDiagnostics.<pid>` open at session
   creation, the view writes `diagnostics\txt-text-size-<pid>.json` in
   the cache root after each measure:
   `{ "scale": <textScale>, "rowWidth": .., "cellWidth": .., "rowHeight": .. }`,
   atomically (a `.tmp` file, then `File.Move` with overwrite).
8. **Saves.** They're serialized by one gate. A queued save that is no
   longer the latest for the open guide is skipped. A save for a guide
   that has since closed still runs but doesn't touch the UI. A failure
   reverts only while its guide is open and no newer change is pending.
9. **TXT keys from the list.** If the TXT `ListView` handles the keys
   before the toolbar's accelerators, the view routes them from
   `PreviewKeyDown` to `TryRunKey(..., fromContent: true)`, as the PDF
   preview does (`ShellWindow.PdfReader.cs:221`). Task 5's first run
   decides; the ledger records which.
10. **HTML keys with page focus.** `text-size-restart` focuses the
    WebView2 element and sends Ctrl+Minus. The report records
    `htmlPageFocusKeys` as `ran` or `not-delivered`. If not delivered, the
    pass invokes *Smaller text* instead and the spec's notes record it;
    there is no page-script workaround.
11. **The label's automation name.** The spec gives `TextSizeValue` the
    name `Text size`. Its text is `110%`; its automation name is set in
    code to `Text size 110%`, so the live region announces the context
    and the smoke reads one property, as `PdfPageStatus` does.
12. **HTML canaries.** HTML guides now show the text-size commands, so the
    `html` group's `Assert-NoReaderCommands` checks on Canary Guide A and
    B (successful loads) become `Assert-OnlyTextSizeCommands`. The error
    and stopped cases keep `Assert-NoReaderCommands`: they have no
    session.

## Review Focus

1. **Rapid presses during a pending save.** Five quick Ctrl+Plus presses
   while the database is slow must end with the label, the view and the
   stored row at the last step, and one status for it. Ruling 8;
   `text-size-restart` sends two Ctrl+= from 100% without waiting between
   them, and the install script checks through `describe-text-scales`
   that the stored value is 1.25.
2. **The guide changes mid-save.** A failed save that finishes after the
   Reader opened another guide must not change the new guide's label,
   size or status. Task 5's `OnTextSizeChanged` checks the session is
   still current before any UI change; review it by reading.
3. **A stored value between steps or out of range.** A hand-edited 1.3
   opens at 130%, Larger goes to 150% and Smaller to 125%; 5 or NaN opens
   at 100%. Task 1 tests `Normalize`, `Larger` and `Smaller` for these.
4. **A culture with a decimal comma.** Under `de-DE`, `Label(1.25)` must
   still read `125%`, and the HTML style must still get `1.25`. Task 1
   tests the label; T09.2's tests cover the style.
5. **A theme change during a size change (HTML).** A Windows theme switch
   while an HTML size write runs must end with both the new theme and the
   new size on the page. Ruling 3 makes `readerTextScale` current before
   the write starts, so `RefreshReaderAppearance` sends the new scale;
   review it by reading.

## File Map

| File | Change |
| --- | --- |
| `src/DesktopGuides.Core/Reading/TextSizeSteps.cs` | new: steps, normalize, stepping, texts |
| `src/DesktopGuides.Core/Reading/ReaderContract.cs` | `TextSizeAction(double Scale)` with validation |
| `tests/DesktopGuides.Core.Tests/TextSizeStepsTests.cs` | new |
| `tests/DesktopGuides.Core.Tests/ReaderContractTests.cs` | action validation tests |
| `src/DesktopGuides.Production/TextReaderView.xaml.cs` | `TextScale`, `testScale`, diagnostics file |
| `src/DesktopGuides.Production/TextReaderSession.cs` | capability, action, constructor, gate |
| `src/DesktopGuides.Production/HtmlReaderSession.cs` | capability, action |
| `src/DesktopGuides.Production/ReaderToolbar.xaml(.cs)` | label, reset, keys, `SetTextSize`, `TextSizeChanged` |
| `src/DesktopGuides.Production/ShellWindow.TextSize.cs` | new: read, save, revert |
| `src/DesktopGuides.Production/ShellWindow.xaml.cs` | TXT open wiring, event subscription |
| `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs` | HTML open wiring; `ReadTextScaleAsync` moves |
| `src/DesktopGuides.Production/ShellWindow.Theme.cs` | `htmlTextScale` becomes `readerTextScale` |
| `tools/p1/DesktopGuides.ReaderToolbarSmoke/ToolbarWindow.cs` | `.Scale` |
| `tools/p1/windows_reader_toolbar_ui_smoke.ps1` | text-size accelerator keys |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | `seed-text-size`, `describe-text-scales` |
| `tools/p1/windows_shell_ui_smoke.ps1` | `text-size-*` modes; canary checks |
| `tools/p1/windows_shell_install.ps1` | `Run-TextSizeScenarios`, `-TextSizeOnly` |
| `.github/workflows/windows-ci.yml` | `text-size` scope; `core` shard |
| `docs/p1/*`, `docs/work-breakdown.md`, `docs/p1-technical-design.md` | Task 6 |

---

### Task 1: Core `TextSizeSteps` and `TextSizeAction(Scale)`

**Files:**
- Create: `src/DesktopGuides.Core/Reading/TextSizeSteps.cs`
- Modify: `src/DesktopGuides.Core/Reading/ReaderContract.cs:52-53`
- Test: `tests/DesktopGuides.Core.Tests/TextSizeStepsTests.cs` (new),
  `tests/DesktopGuides.Core.Tests/ReaderContractTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces (namespace `DesktopGuides.Core.Reading`):
  - `TextSizeSteps.Default` (1.0), `Min` (0.75), `Max` (2.0),
    `IReadOnlyList<double> Steps`;
  - `double Normalize(double?)`, `double Larger(double)`,
    `double Smaller(double)`, `bool CanLarger(double)`,
    `bool CanSmaller(double)`;
  - `string Label(double)`, `string Status(double)`,
    `string SaveFailed(string)`;
  - `public sealed record TextSizeAction(double Scale)`, which throws
    `ArgumentOutOfRangeException` for a non-finite scale or one outside
    `Min`–`Max`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/TextSizeStepsTests.cs`:

```csharp
using System.Globalization;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextSizeStepsTests
{
    [Fact]
    public void StepsAreFixed() =>
        Assert.Equal([0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0], TextSizeSteps.Steps);

    [Fact]
    public void TheDefaultIsAHundredPercent() =>
        Assert.Equal(1.0, TextSizeSteps.Default);

    [Theory]
    [InlineData(null, 1.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 1.0)]
    [InlineData(0.74, 1.0)]
    [InlineData(2.01, 1.0)]
    [InlineData(5.0, 1.0)]
    [InlineData(0.75, 0.75)]
    [InlineData(2.0, 2.0)]
    [InlineData(1.25, 1.25)]
    [InlineData(1.3, 1.3)]
    public void NormalizeKeepsOnlyAStoredValueInRange(double? stored, double expected) =>
        Assert.Equal(expected, TextSizeSteps.Normalize(stored));

    [Theory]
    [InlineData(0.75, 0.9)]
    [InlineData(1.0, 1.1)]
    [InlineData(1.1, 1.25)]
    [InlineData(1.75, 2.0)]
    [InlineData(1.3, 1.5)]
    [InlineData(0.8, 0.9)]
    [InlineData(1.004, 1.1)]
    [InlineData(0.996, 1.1)]
    public void LargerMovesToTheNextStepUp(double scale, double expected) =>
        Assert.Equal(expected, TextSizeSteps.Larger(scale));

    [Theory]
    [InlineData(2.0, 1.75)]
    [InlineData(1.1, 1.0)]
    [InlineData(1.0, 0.9)]
    [InlineData(0.9, 0.75)]
    [InlineData(1.3, 1.25)]
    [InlineData(1.004, 0.9)]
    [InlineData(0.996, 0.9)]
    public void SmallerMovesToTheNextStepDown(double scale, double expected) =>
        Assert.Equal(expected, TextSizeSteps.Smaller(scale));

    [Theory]
    [InlineData(2.0)]
    [InlineData(1.998)]
    public void LargerStopsAtTheTop(double scale)
    {
        Assert.Equal(scale, TextSizeSteps.Larger(scale));
        Assert.False(TextSizeSteps.CanLarger(scale));
        Assert.True(TextSizeSteps.CanSmaller(scale));
    }

    [Theory]
    [InlineData(0.75)]
    [InlineData(0.752)]
    public void SmallerStopsAtTheBottom(double scale)
    {
        Assert.Equal(scale, TextSizeSteps.Smaller(scale));
        Assert.False(TextSizeSteps.CanSmaller(scale));
        Assert.True(TextSizeSteps.CanLarger(scale));
    }

    [Theory]
    [InlineData(0.75, "75%")]
    [InlineData(1.0, "100%")]
    [InlineData(1.1, "110%")]
    [InlineData(1.25, "125%")]
    [InlineData(2.0, "200%")]
    [InlineData(1.3, "130%")]
    [InlineData(1.004, "100%")]
    public void LabelIsTheRoundedPercent(double scale, string expected) =>
        Assert.Equal(expected, TextSizeSteps.Label(scale));

    [Fact]
    public void LabelIgnoresTheCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("125%", TextSizeSteps.Label(1.25));
            Assert.Equal("Text size 125%.", TextSizeSteps.Status(1.25));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void StatusNamesTheSize() =>
        Assert.Equal("Text size 110%.", TextSizeSteps.Status(1.1));

    [Fact]
    public void SaveFailedExplainsTheError() =>
        Assert.Equal(
            "Could not save the text size: database is locked",
            TextSizeSteps.SaveFailed("database is locked"));
}
```

In `tests/DesktopGuides.Core.Tests/ReaderContractTests.cs`, add inside the
class:

```csharp
    [Theory]
    [InlineData(0.75)]
    [InlineData(1.3)]
    [InlineData(2.0)]
    public void TextSizeActionCarriesTheScale(double scale) =>
        Assert.Equal(scale, new TextSizeAction(scale).Scale);

    [Theory]
    [InlineData(0.7)]
    [InlineData(2.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void TextSizeActionRejectsAScaleOutOfRange(double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextSizeAction(scale));
```

- [ ] **Step 2: Run the tests to verify they fail**

Sync to the host (Global Constraints), then run:
`ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-1\tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj"`
Expected: build FAILS with `CS0103: The name 'TextSizeSteps' does not
exist` and `CS1061: 'TextSizeAction' does not contain a definition for
'Scale'`.

- [ ] **Step 3: Implement `TextSizeSteps` and the action**

Create `src/DesktopGuides.Core/Reading/TextSizeSteps.cs`:

```csharp
using System.Globalization;

namespace DesktopGuides.Core.Reading;

// T14.1: TXT and HTML text size moves through fixed steps, as PdfZoom does,
// so the value shown and stored is exact and repeated steps don't drift.
public static class TextSizeSteps
{
    public const double Default = 1.0;
    public const double Min = 0.75;
    public const double Max = 2.0;

    // In percent: a value this close to a step counts as that step, so it
    // isn't offered as a step that changes nothing.
    private const double Tolerance = 0.5;
    private static readonly int[] percents = [75, 90, 100, 110, 125, 150, 175, 200];

    public static IReadOnlyList<double> Steps { get; } =
        Array.AsReadOnly(percents.Select(percent => percent / 100.0).ToArray());

    // A value between steps (an older or hand-edited database) is kept.
    public static double Normalize(double? stored) =>
        stored is double value && double.IsFinite(value) && value >= Min && value <= Max
            ? value
            : Default;

    public static double Larger(double scale)
    {
        double current = scale * 100;
        foreach (int step in percents)
        {
            if (step > current + Tolerance)
            {
                return step / 100.0;
            }
        }
        return scale;
    }

    public static double Smaller(double scale)
    {
        double current = scale * 100;
        for (int index = percents.Length - 1; index >= 0; index--)
        {
            if (percents[index] < current - Tolerance)
            {
                return percents[index] / 100.0;
            }
        }
        return scale;
    }

    public static bool CanLarger(double scale) => Larger(scale) != scale;

    public static bool CanSmaller(double scale) => Smaller(scale) != scale;

    public static string Label(double scale) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)Math.Round(scale * 100, MidpointRounding.AwayFromZero)}%");

    public static string Status(double scale) => $"Text size {Label(scale)}.";

    public static string SaveFailed(string message) =>
        $"Could not save the text size: {message}";
}
```

In `src/DesktopGuides.Core/Reading/ReaderContract.cs`, replace

```csharp
public sealed record TextSizeAction(double Factor)
    : ReaderAction(ReaderCommand.TextSize);
```

with

```csharp
// The target scale, not a factor: the shell steps, the sessions apply.
public sealed record TextSizeAction(double Scale)
    : ReaderAction(ReaderCommand.TextSize)
{
    public double Scale { get; } =
        double.IsFinite(Scale) && Scale >= TextSizeSteps.Min && Scale <= TextSizeSteps.Max
            ? Scale
            : throw new ArgumentOutOfRangeException(
                nameof(Scale), Scale, "A text size must be between 75% and 200%.");
}
```

The toolbar's handlers in `src/DesktopGuides.Production/ReaderToolbar.xaml.cs`
still build unchanged: `new TextSizeAction(0.9)` and `new TextSizeAction(1.1)`
are valid scales, and Task 4 replaces both handlers. The one use of
`Factor` is in `tools/p1/DesktopGuides.ReaderToolbarSmoke/ToolbarWindow.cs:155`;
replace `text.Factor` with `text.Scale`:

```csharp
        TextSizeAction text => $"Text size {text.Scale:G}",
```

- [ ] **Step 4: Run the tests to verify they pass**

Sync, then run:
`ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-1\tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj"`
Expected: PASS; the count rises by the new cases (record the before and
after counts in the ledger for Task 6's verification).

Also run the Infrastructure tests (Global Constraints) and the toolbar
smoke build:
`ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t14-1\tools\p1\DesktopGuides.ReaderToolbarSmoke\DesktopGuides.ReaderToolbarSmoke.csproj -c Release -p:Platform=x64"`
Expected: PASS and `Build succeeded`, 0 errors.

- [ ] **Step 5: Commit**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add src/DesktopGuides.Core/Reading/TextSizeSteps.cs src/DesktopGuides.Core/Reading/ReaderContract.cs tests/DesktopGuides.Core.Tests/TextSizeStepsTests.cs tests/DesktopGuides.Core.Tests/ReaderContractTests.cs tools/p1/DesktopGuides.ReaderToolbarSmoke/ToolbarWindow.cs
git -C $R commit -m "feat(core): T14.1 fixed text size steps and a target-scale action" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Installed `text-size` group (the failing test)

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (after `seed-progress`, about line 715)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (ValidateSet line 3; reader
  branch about line 1227; a new sub-branch before the TXT `else` at about
  line 3189; canary checks at about lines 1959 and 2025)
- Modify: `tools/p1/windows_shell_install.ps1` (switches lines 12–24,
  `$scenarioGroups` lines 48–61, `Run-ShellSmoke` timeouts, a new
  `Run-TextSizeScenarios` after `Run-ThemeScenarios`, dispatch about line 2427)
- Modify: `.github/workflows/windows-ci.yml` (shell-scope choices lines
  17–34; the core shard about line 330)
- Modify: `tools/p1/windows_reader_toolbar_ui_smoke.ps1:352`
- Modify: `docs/p1/e2e-testing.md` (lines 228, 234–236, a row after Theme
  about line 289)

**Interfaces:**
- Consumes: the strings in Global Constraints.
- Produces, for Tasks 3–5 to satisfy:
  - automation: `TextSizeValue` (Name `Text size <label>`), buttons named
    `Smaller text`, `Larger text`, overflow `Reset text size`;
  - gate `Local\DesktopGuides.Preview.TextDiagnostics.<pid>` and file
    `<cacheRoot>\diagnostics\txt-text-size-<pid>.json` with numeric
    `scale`, `rowWidth`, `cellWidth`, `rowHeight`;
  - HTML: the existing `html-appearance-<pid>.json`, whose
    `session.appearance.scale` is the applied scale;
  - statuses `Text size <label>.` and the prefix
    `Could not save the text size: `.

- [ ] **Step 1: Add the seed commands**

In `tools/p1/DesktopGuides.ShellSeed/Program.cs`, after the
`seed-progress` block, add:

```csharp
if (args.Length == 3 && args[0] == "seed-text-size")
{
    // T14.1: two TXT guides, an HTML guide and a PDF guide in one game.
    ManagedPathResolver sizePaths = new(args[1]);
    await using SqliteLibraryRepository sizeRepository = new(sizePaths);
    await sizeRepository.InitializeAsync();
    if ((await sizeRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The text size seed needs an empty library.");
    }
    string fixtures = Path.GetFullPath(args[2]);
    Game sizeGame = await sizeRepository.AddGameAsync("Text Size Game", null, null);
    GuideImportValidator sizeValidator = new();
    GuideImportPublisher sizePublisher = new(sizeRepository, sizePaths);
    async Task<Guid> PublishAsync(string relative, string title)
    {
        ImportInspection inspection = await sizeValidator.InspectAsync(
            Path.Combine(fixtures, relative), CancellationToken.None);
        if (inspection is not ImportReady ready)
        {
            throw new InvalidOperationException($"The {relative} fixture failed the import preview: {inspection}.");
        }
        return await sizePublisher.PublishAsync(
            ready.Manifest, sizeGame.Id, title, false, null, CancellationToken.None);
    }
    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    Guid ascii = Guid.NewGuid();
    await InsertTextGuideAsync(sizePaths, sizeGame.Id, ascii, "ASCII Map Guide", now,
        File.ReadAllBytes(Path.Combine(fixtures, "p0", "txt-ascii.txt")));
    Guid utf8 = Guid.NewGuid();
    await InsertTextGuideAsync(sizePaths, sizeGame.Id, utf8, "UTF-8 Guide", now,
        File.ReadAllBytes(Path.Combine(fixtures, "p0", "txt-utf8.txt")));
    Guid web = await PublishAsync(Path.Combine("p0", "html-static", "guide.html"), "Static Web Guide");
    Guid pdf = await PublishAsync(Path.Combine("p0", "generated", "pdf-long.pdf"), "Long PDF Guide");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ascii = ascii.ToString("N"),
        utf8 = utf8.ToString("N"),
        web = web.ToString("N"),
        pdf = pdf.ToString("N")
    }));
    return 0;
}

if (args.Length >= 3 && args[0] == "describe-text-scales")
{
    // Each guide's stored TextScale, or null when it has none.
    ManagedPathResolver scalePaths = new(args[1]);
    await using SqliteLibraryRepository scaleRepository = new(scalePaths);
    await scaleRepository.InitializeAsync();
    Dictionary<string, double?> scales = [];
    foreach (string id in args.Skip(2))
    {
        ReaderPreferences? preferences = await scaleRepository.GetReaderPreferencesAsync(Guid.Parse(id));
        scales[id] = preferences?.TextScale;
    }
    Console.WriteLine(JsonSerializer.Serialize(scales));
    return 0;
}
```

If `p0/html-static/guide.html` isn't the fixture's entry file, use the
entry `seed-html-reader` publishes for its static guide (check with
`grep -n "html-static" tools/p1/DesktopGuides.ShellSeed/Program.cs`).

- [ ] **Step 2: Add the smoke modes**

In `tools/p1/windows_shell_ui_smoke.ps1`:

1. Append to the `$Mode` ValidateSet, after `'theme-error-retry'`:

   ```powershell
           'text-size-steps', 'text-size-whitespace', 'text-size-restart', 'text-size-restart-after',
           'text-size-pdf', 'text-size-error-prepare', 'text-size-error', 'text-size-error-retry'
   ```

2. Add the same eight names to the reader branch's `-in @(...)` list
   (about line 1227), and add to the `$textGame` choice, before the final
   `else`:

   ```powershell
               elseif ($Mode -like 'text-size-*') { 'Text Size Game' }
   ```

3. After `Assert-NoReaderCommands` (about line 1292), add:

   ```powershell
        # T14.1: a loaded HTML guide shows only the text-size commands.
        function Assert-OnlyTextSizeCommands([string] $guide) {
            foreach ($name in @('Smaller text', 'Larger text')) {
                if (-not (Wait-ReaderCommand $name)) { throw "$guide has no visible '$name'." }
            }
            foreach ($name in @('Next page', 'Zoom in', 'Go to page')) {
                if (Find-VisibleName $name) { throw "$guide exposed '$name'." }
            }
        }
   ```

   Replace `Assert-NoReaderCommands 'Canary Guide A'` (about line 1959) and
   `Assert-NoReaderCommands 'Canary Guide B'` (about line 2025) with
   `Assert-OnlyTextSizeCommands 'Canary Guide A'` and
   `Assert-OnlyTextSizeCommands 'Canary Guide B'`. Leave the other
   `Assert-NoReaderCommands` calls: those guides have no session.

4. Before the TXT branch's final `else {` (the one that opens the
   `TextRemeasure` handle, about line 3189), add:

```powershell
        elseif ($Mode -like 'text-size-*') {
            $ascii = 'ASCII Map Guide'
            $utf8 = 'UTF-8 Guide'
            $web = 'Static Web Guide'
            $columns = 'Columns:   one     two'

            function Send-Keys([string] $keys) {
                [System.Windows.Forms.SendKeys]::SendWait($keys)
            }

            function Get-TextSizeName {
                $value = Find-ById 'TextSizeValue'
                if (-not $value -or $value.Current.IsOffscreen) { return $null }
                return $value.Current.Name
            }

            function Wait-TextSize([string] $label) {
                $deadline = (Get-Date).AddSeconds(10)
                do {
                    if ((Invoke-UiaRetry { Get-TextSizeName }) -ceq "Text size $label") { return }
                    Start-Sleep -Milliseconds 100
                } while ((Get-Date) -lt $deadline)
                throw "Expected the text size label 'Text size $label'; saw '$(Get-TextSizeName)'."
            }

            function Step-TextSize([string] $keys, [string] $label) {
                Send-Keys $keys
                Wait-TextSize $label
                [void](Wait-Status "Text size $label.")
            }

            function Assert-TextCommand([string] $name, [bool] $enabled) {
                $command = Find-VisibleName $name
                if (-not $command) { throw "The Reader toolbar has no visible '$name'." }
                if ($command.Current.IsEnabled -ne $enabled) {
                    throw "'$name' is enabled=$($command.Current.IsEnabled); expected $enabled."
                }
            }

            # Reset lives in the overflow: open it, read the state, close it.
            function Assert-ResetEnabled([bool] $enabled) {
                $more = $null
                foreach ($candidate in @('More', 'More options', 'More commands', 'Show more', 'See more')) {
                    $more = Find-VisibleName $candidate
                    if ($more) { break }
                }
                if (-not $more) { throw 'The Reader toolbar has no visible overflow button.' }
                Invoke-Element $more
                $deadline = (Get-Date).AddSeconds(5)
                do {
                    $reset = Find-VisibleName 'Reset text size'
                    if ($reset) { break }
                    Start-Sleep -Milliseconds 100
                } while ((Get-Date) -lt $deadline)
                if (-not $reset) { throw "The Reader toolbar overflow has no visible 'Reset text size'." }
                $actual = $reset.Current.IsEnabled
                Send-Keys '{ESC}'
                if ($actual -ne $enabled) { throw "'Reset text size' is enabled=$actual; expected $enabled." }
            }

            function Read-TextDiagnostics {
                $path = Join-Path $AppCacheRoot "diagnostics\txt-text-size-$ProcessId.json"
                if (-not (Test-Path -LiteralPath $path)) { return $null }
                try { return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json }
                catch { return $null }
            }

            function Wait-TextDiagnostics([double] $scale, [string] $what) {
                $deadline = (Get-Date).AddSeconds(10)
                do {
                    $file = Read-TextDiagnostics
                    if ($file -and [math]::Abs([double] $file.scale - $scale) -lt 0.001) { return $file }
                    Start-Sleep -Milliseconds 200
                } while ((Get-Date) -lt $deadline)
                throw "After $what the TXT view reported $(Read-TextDiagnostics | ConvertTo-Json -Compress); expected scale $scale."
            }

            function Wait-HtmlScale([double] $scale, [string] $what) {
                $deadline = (Get-Date).AddSeconds(15)
                do {
                    $file = Read-HtmlAppearance
                    if ($file -and [double] $file.opacity -eq 1 -and
                        [math]::Abs([double] $file.session.appearance.scale - $scale) -lt 0.001) { return $file }
                    Start-Sleep -Milliseconds 250
                } while ((Get-Date) -lt $deadline)
                throw "After $what the HTML view reported $(Read-HtmlAppearance | ConvertTo-Json -Compress -Depth 5); expected scale $scale."
            }

            function Open-TextSizeGame {
                [void](Wait-Name 'LibraryHeading' 'Library')
                [void](Wait-Status 'Library ready.')
                Select-Element $textGame
                [void](Wait-Name 'GameHeading' $textGame)
                [void](Wait-Status 'Game ready.')
            }

            function Open-SizedGuide([string] $guide, [string] $label) {
                Open-TextGuide $guide
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                Wait-TextSize $label
            }

            if ($Mode -eq 'text-size-steps') {
                # Keyboard only, on A. Every Larger binding gets a turn.
                Open-TextSizeGame
                Open-SizedGuide $ascii '100%'
                [void](Wait-TextDiagnostics 1.0 'the open')
                Assert-ResetEnabled $false
                [void](Wait-FirstTextRow)
                (Find-ById 'ReaderTextLines').SetFocus()
                Wait-FocusWithin 'ReaderTextLines'
                Step-TextSize '^=' '110%'
                Step-TextSize '^{ADD}' '125%'
                Step-TextSize '^+=' '150%'
                Step-TextSize '^=' '175%'
                # A focused Larger text that becomes disabled hands focus to Smaller text.
                (Find-VisibleName 'Larger text').SetFocus()
                Wait-FocusedName 'Larger text'
                Step-TextSize '^=' '200%'
                Assert-TextCommand 'Larger text' $false
                Wait-FocusedName 'Smaller text'
                $report.textSize200Screenshot = Save-WindowScreenshot 'text-size-200'
                Step-TextSize '^0' '100%'
                Assert-ResetEnabled $false
                Assert-TextCommand 'Larger text' $true
                Step-TextSize '^-' '90%'
                Assert-ResetEnabled $true
                Step-TextSize '^{SUBTRACT}' '75%'
                Assert-TextCommand 'Smaller text' $false
                Wait-FocusedName 'Larger text'
                [void](Wait-TextDiagnostics 0.75 'the last step')
                $report.textSize75Screenshot = Save-WindowScreenshot 'text-size-75'
                $report.phases += 'text-size-steps'
            }
            elseif ($Mode -eq 'text-size-whitespace') {
                # Continues on A at 75%. The rows grow with the size and keep
                # their columns; the top line stays.
                Step-TextSize '^0' '100%'
                $at100 = Wait-TextDiagnostics 1.0 'the reset'
                $top = Get-TopRowName
                foreach ($label in @('110%', '125%', '150%')) {
                    Invoke-Element (Find-VisibleName 'Larger text')
                    Wait-TextSize $label
                    [void](Wait-Status "Text size $label.")
                }
                $at150 = Wait-TextDiagnostics 1.5 'three steps up'
                $expected = 1.5 * [double] $at100.rowWidth
                if ([math]::Abs([double] $at150.rowWidth - $expected) -gt [double] $at150.cellWidth) {
                    throw "At 150% the row is $($at150.rowWidth) px wide; expected $expected within one cell ($($at150.cellWidth) px)."
                }
                Assert-RowNames $ascii $asciiNames
                if ($asciiNames[5] -cne $columns) { throw "txt-ascii line 6 is '$($asciiNames[5])', not '$columns'." }
                $after = Get-TopRowName
                if ($after -cne $top) { throw "The top line moved from '$top' to '$after' at 150%." }
                $report.textSizeWhitespace = [ordered]@{
                    at100 = $at100; at150 = $at150; topLine = $top
                }
                $report.textSize150Screenshot = Save-WindowScreenshot 'text-size-150'
                $report.phases += 'text-size-whitespace'
            }
            elseif ($Mode -eq 'text-size-restart') {
                # Continues on A at 150%. Two quick presses save only where they end.
                Step-TextSize '^0' '100%'
                Send-Keys '^='
                Send-Keys '^='
                Wait-TextSize '125%'
                [void](Wait-Status 'Text size 125%.')
                [void](Wait-TextDiagnostics 1.25 'two quick presses')
                Back-ToTextGame

                Open-SizedGuide $web '100%'
                [void](Wait-HtmlScale 1.0 'the HTML open')
                # Ruling 10: try the key with focus in the page first.
                $page = @(Get-PageRoots)[0]
                $report.htmlPageFocusKeys = 'not-delivered'
                if ($page) {
                    try { $page.SetFocus() } catch { }
                    Send-Keys '^-'
                    $deadline = (Get-Date).AddSeconds(5)
                    do {
                        if ((Get-TextSizeName) -ceq 'Text size 90%') { $report.htmlPageFocusKeys = 'ran'; break }
                        Start-Sleep -Milliseconds 100
                    } while ((Get-Date) -lt $deadline)
                }
                if ($report.htmlPageFocusKeys -ne 'ran') {
                    Invoke-Element (Find-VisibleName 'Smaller text')
                }
                Wait-TextSize '90%'
                [void](Wait-Status 'Text size 90%.')
                [void](Wait-HtmlScale 0.9 'Smaller text')
                Assert-OnlyTextSizeCommands $web
                $report.textSizeHtmlScreenshot = Save-WindowScreenshot 'text-size-html-90'
                Back-ToTextGame

                Open-SizedGuide $utf8 '100%'
                Back-ToTextGame
                $report.phases += 'text-size-restart'
            }
            elseif ($Mode -eq 'text-size-restart-after') {
                Open-TextSizeGame
                Open-SizedGuide $ascii '125%'
                [void](Wait-TextDiagnostics 1.25 'reopening A')
                Back-ToTextGame
                Open-SizedGuide $web '90%'
                [void](Wait-HtmlScale 0.9 'reopening the HTML guide')
                Back-ToTextGame
                Open-SizedGuide $utf8 '100%'
                Back-ToTextGame
                $report.phases += 'text-size-restart-after'
            }
            elseif ($Mode -eq 'text-size-pdf') {
                # Continues on the Game page. PDF keeps its zoom and has no text size.
                Open-TextGuide 'Long PDF Guide'
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                [void](Wait-PdfPage 1 200 'page 1 of 200')
                foreach ($name in @('Zoom in', 'Zoom out')) {
                    if (-not (Wait-ReaderCommand $name)) { throw "The PDF guide has no visible '$name'." }
                }
                foreach ($name in @('Larger text', 'Smaller text')) {
                    if (Find-VisibleName $name) { throw "The PDF guide exposed '$name'." }
                }
                if (Get-TextSizeName) { throw 'The PDF guide showed a text size label.' }
                (Find-ById 'PdfPreviewScroller').SetFocus()
                [void](Wait-FocusedId 'PdfPreviewScroller')
                $before = (Find-ById 'PdfPageStatus').Current.Name
                Send-Keys '^='
                $deadline = (Get-Date).AddSeconds(10)
                do {
                    $after = (Find-ById 'PdfPageStatus').Current.Name
                    if ($after -cne $before) { break }
                    Start-Sleep -Milliseconds 100
                } while ((Get-Date) -lt $deadline)
                if ($after -ceq $before) { throw "Ctrl+= didn't zoom the PDF guide: the status still reads '$before'." }
                $report.textSizePdf = [ordered]@{ before = $before; after = $after }
                Back-ToTextGame
                $report.phases += 'text-size-pdf'
            }
            elseif ($Mode -eq 'text-size-error-prepare') {
                # The installer takes the write lock after this mode.
                Open-TextSizeGame
                Open-SizedGuide $ascii '125%'
                [void](Wait-TextDiagnostics 1.25 'the open before the lock')
                $report.phases += 'text-size-error-prepare'
            }
            elseif ($Mode -eq 'text-size-error') {
                # The size applies at once; the timed-out save puts the stored one back.
                Invoke-Element (Find-VisibleName 'Larger text')
                Wait-TextSize '150%'
                [void](Wait-TextDiagnostics 1.5 'the pending save')
                [void](Wait-Status 'Could not save the text size: ' -Prefix -Seconds 60)
                Wait-TextSize '125%'
                [void](Wait-TextDiagnostics 1.25 'the failed save')
                $report.textSizeErrorScreenshot = Save-WindowScreenshot 'text-size-error'
                $report.phases += 'text-size-error'
            }
            else {
                # text-size-error-retry: the lock is gone and the retry saves.
                Invoke-Element (Find-VisibleName 'Larger text')
                Wait-TextSize '150%'
                [void](Wait-Status 'Text size 150%.')
                $report.phases += 'text-size-error-retry'
            }
        }
```

Check that `Wait-FocusWithin`, `Wait-FocusedId`, `Get-PageRoots`,
`Wait-PdfPage`, `Invoke-UiaRetry` and `Assert-RowNames` are defined
before this branch runs (they are reader-branch helpers today); if one is
defined inside another mode's branch, move it up beside its siblings.

- [ ] **Step 3: Add the install group**

In `tools/p1/windows_shell_install.ps1`:

1. Add `[switch] $TextSizeOnly,` after `[switch] $ThemeOnly,`, and
   `'text-size' = $TextSizeOnly.IsPresent` after `'theme'` in
   `$scenarioGroups`.
2. In `Run-ShellSmoke`'s timeout choice, add `-or $mode -like 'text-size-*'`
   to the 120-second branch.
3. After `Run-ThemeScenarios`, add:

```powershell
function Assert-StoredTextScales($ids, $expected, [string] $step) {
    $keys = @($expected.Keys)
    $json = Invoke-ShellSeed (@('describe-text-scales', $dataRoot) + @($keys | ForEach-Object { $ids.$_ }))
    $stored = $json | ConvertFrom-Json
    $result = [ordered]@{}
    foreach ($key in $keys) {
        $actual = $stored.($ids.$key)
        $want = $expected[$key]
        $ok = if ($null -eq $want) { $null -eq $actual }
            else { $null -ne $actual -and [math]::Abs([double] $actual - $want) -lt 0.0001 }
        if (-not $ok) { throw "After $step the stored text scale of $key is '$actual', not '$want'." }
        $result[$key] = $actual
    }
    return $result
}

function Invoke-TextSizeLaunch([scriptblock] $passes) {
    # One launch with the TXT and HTML diagnostics gates open.
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $gates = @(
        foreach ($name in @('TextDiagnostics', 'HtmlDiagnostics')) {
            [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\DesktopGuides.Preview.$name.$processId")
        })
    try {
        & $passes
        Close-InstalledShell
    }
    finally {
        foreach ($gate in $gates) { $gate.Dispose() }
    }
}

function Run-TextSizeScenarios {
    # TR14.1: fixed text size steps for TXT and HTML, saved per guide,
    # restored after a relaunch, fixed-width TXT columns, PDF untouched,
    # and a failed save put back.
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    $cacheRoot = Get-HtmlCacheRoot
    Remove-Item -LiteralPath (Join-Path $cacheRoot 'diagnostics') -Recurse -Force -ErrorAction SilentlyContinue
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\pdf-long.pdf'))) {
        throw 'pdf-long.pdf is missing; run tools/p0/make_fixtures.py first.'
    }
    $ids = Invoke-ShellSeed @('seed-text-size', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $report.textSize = [ordered]@{}
    function Run-TextSizePass([string] $mode) {
        $report.textSize[$mode] = Run-ShellSmoke $mode -ResultName $mode `
            -AppDataRoot $dataRoot -AppCacheRoot $cacheRoot
    }

    Invoke-TextSizeLaunch {
        Run-TextSizePass 'text-size-steps'
        Run-TextSizePass 'text-size-whitespace'
        Run-TextSizePass 'text-size-restart'
    }
    $report.textSize.storedBeforeRelaunch = Assert-StoredTextScales $ids `
        ([ordered]@{ ascii = 1.25; web = 0.9; utf8 = $null }) 'the first launch'

    Invoke-TextSizeLaunch {
        Run-TextSizePass 'text-size-restart-after'
        Run-TextSizePass 'text-size-pdf'
    }
    $report.textSize.storedAfterPdf = Assert-StoredTextScales $ids `
        ([ordered]@{ ascii = 1.25; web = 0.9; utf8 = $null; pdf = $null }) 'the PDF zoom'

    Invoke-TextSizeLaunch {
        Run-TextSizePass 'text-size-error-prepare'
        # A held write lock makes the app's save time out after 30 s.
        $ready = Join-Path $ResultDirectory "text-size-lock-ready-$runId"
        $release = Join-Path $ResultDirectory "text-size-lock-release-$runId"
        $lock = Start-ShellDatabaseLock 'hold-write-lock' $ready $release -HoldSeconds 120
        try {
            Run-TextSizePass 'text-size-error'
        }
        finally {
            Release-ShellDatabaseLock $lock $release
        }
        [void](Assert-StoredTextScales $ids ([ordered]@{ ascii = 1.25 }) 'the failed save')
        Run-TextSizePass 'text-size-error-retry'
    }
    $report.textSize.storedAfterRetry = Assert-StoredTextScales $ids `
        ([ordered]@{ ascii = 1.5; web = 0.9 }) 'the retry'
}
```

4. In the dispatch, after
   `if (Enter-ScenarioGroup 'theme') { Run-ThemeScenarios }`, add:

   ```powershell
       if (Enter-ScenarioGroup 'text-size') { Run-TextSizeScenarios }
   ```


- [ ] **Step 4: Add CI, docs and the toolbar-smoke checks**

1. `.github/workflows/windows-ci.yml`: add `- text-size` after `- theme` in
   the `shell-scope` options, and in the shard matrix (about line 330)
   change `"groups":"core,txt,game-actions"` to
   `"groups":"core,txt,text-size,game-actions"`.
2. `docs/p1/e2e-testing.md`:
   - line 227: `` `core` (core, txt, game-actions) `` becomes
     `` `core` (core, txt, text-size, game-actions) ``;
   - line 236: add `` `-TextSizeOnly`, `` after `` `-ThemeOnly`, ``;
   - after the Theme row (about line 289), add:

     ```markdown
     | Text size | `-TextSizeOnly`, in the core shard; `seed-text-size` adds Text Size Game with ASCII Map Guide (`txt-ascii`), UTF-8 Guide (`txt-utf8`), Static Web Guide (`html-static`) and Long PDF Guide (`pdf-long`). The TXT and HTML diagnostics gates are open. `text-size-steps`, keyboard only on ASCII Map Guide: the label `TextSizeValue` reads `Text size 100%`; Ctrl+=, Ctrl+Plus (numpad) and Ctrl+Shift+= step through 110, 125, 150, 175 and 200%, each with the status `Text size <label>.`; at 200% *Larger text* is disabled and focus moves to *Smaller text*; Ctrl+0 returns to 100% with *Reset text size* disabled; Ctrl+Minus steps to 90 and 75%, where *Smaller text* is disabled and focus moves to *Larger text*. `text-size-whitespace`: at 150% the gated `txt-text-size-<pid>.json` reports scale 1.5 and a row 1.5 times the 100% row (within one cell), line 6 still reads `Columns:   one     two`, and the top line hasn't moved. `text-size-restart`: two quick presses store 1.25 for the ASCII guide; the HTML guide is set to 90% (Ctrl+Minus with page focus, recorded as `htmlPageFocusKeys`, or *Smaller text*), and its `html-appearance-<pid>.json` reports scale 0.9; UTF-8 Guide is opened and left at 100%. After a relaunch, `text-size-restart-after` reads 125%, 90% and 100%; the stored scales are 1.25, 0.9 and null. `text-size-pdf`: the PDF guide shows *Zoom in* and *Zoom out* and no text size; Ctrl+= zooms it, and its stored scale stays null. `text-size-error`, `text-size-error-retry`: with the write lock held for 120 s, *Larger text* applies 150% at once, then shows `Could not save the text size: <message>` and puts 125% back in the label and the view; after the release the retry stores 1.5. Screenshots at 200, 75 and 150%, the HTML guide at 90%, and the error. | T14.1, TR14.1 |
     ```
3. `tools/p1/windows_reader_toolbar_ui_smoke.ps1:350-352`: the accelerator
   list becomes

   ```powershell
       foreach ($pair in @(@('Previous page', 'Page Up'), @('Next page', 'Page Down'),
               @('Go to start', 'Ctrl+Home'), @('Go to end', 'Ctrl+End'),
               @('Smaller text', 'Ctrl+Minus'), @('Larger text', 'Ctrl+Plus'),
               @('Zoom in', 'Ctrl+Plus'), @('Zoom out', 'Ctrl+Minus'))) {
   ```

   and after `Assert-AcceleratorKey 'Fit to width' 'Ctrl+0'` add
   `Assert-AcceleratorKey 'Reset text size' 'Ctrl+0'`.

- [ ] **Step 5: Check the scripts parse and are ASCII**

Run (on the Mac):

```bash
LC_ALL=C grep -nP '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1 tools/p1/windows_reader_toolbar_ui_smoke.ps1 || echo ascii-ok
```

Expected: `ascii-ok`.

Then sync to the host and parse each script there:

```bash
ssh pcsx2-win "powershell -NoProfile -Command \"foreach ($f in 'windows_shell_ui_smoke.ps1','windows_shell_install.ps1','windows_reader_toolbar_ui_smoke.ps1') { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile('E:\work\desktop-guides\t14-1\tools\p1\' + $f, [ref]$null, [ref]$e); if ($e) { $e | ForEach-Object { $f + ': ' + $_.Message } } else { $f + ': ok' } }\""
```

Expected: three `ok` lines.

Build the seed on the host:
`dotnet build E:\work\desktop-guides\t14-1\tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 6: Commit and watch the group fail**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tools/p1 .github/workflows/windows-ci.yml docs/p1/e2e-testing.md
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(shell): T14.1 installed text-size group" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push -u origin feat/p1-t14-1-text-size
gh workflow run windows-ci.yml --ref feat/p1-t14-1-text-size -f shell-scope=text-size -f dev-fast=true
```

Run: the CI loop in Global Constraints.
Expected: the `production-shell-ui-text-size` job FAILS in `text-size-steps`,
at `Wait-TextSize '100%'` ("Expected the text size label 'Text size
100%'"), because nothing shows the label yet. The toolbar smoke job is
skipped by `dev-fast`; its new checks fail until Task 4 adds the
tooltips and the reset, so Task 4's run checks them. If the failure is anywhere else (the seed, a
parse error, the install), fix the harness before Task 3.

### Task 3: TXT and HTML sessions apply a text size

**Files:**
- Modify: `src/DesktopGuides.Production/TextReaderView.xaml.cs`
- Modify: `src/DesktopGuides.Production/TextReaderSession.cs`
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs:89,325-326`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs:1770` (the
  constructor call only; Task 5 passes the stored scale)

**Interfaces:**
- Consumes: `TextSizeAction(double Scale)` and `TextSizeSteps.Default`
  (Task 1).
- Produces:
  - `TextReaderView(TextLineList lines, int maxColumns, double textScale, string? diagnosticsPath)`
    and `double TextScale { get; set; }`;
  - `TextReaderSession(TextGuideDocument document, int maxColumns, double textScale, string? diagnosticsFolder)`
    and `static string? TextReaderSession.DiagnosticsFolderForTest(string cacheRoot)`;
  - both sessions' `Capabilities` include `ReaderCapabilities.TextSize`,
    and `ExecuteAsync(new TextSizeAction(s))` applies `s`;
  - the diagnostics file of Task 2's Interfaces.

The failing test is Task 2's `text-size` group. This task alone doesn't
make it pass (there's no label yet), so its check is the build and
Task 2's run is repeated after Task 5.

- [ ] **Step 1: Give the TXT view a text scale**

In `src/DesktopGuides.Production/TextReaderView.xaml.cs`:

1. Add `using System.Globalization;` with the other usings.
2. Replace the fields `private double fontScale = 1;` and the constructor
   with:

```csharp
    private readonly string? diagnosticsPath;
    private double cellWidth;
    // The font size is the base size times the test hook's multiplier
    // (Ruling 4) times the guide's text size.
    private double testScale = 1;
    private double textScale;
```

```csharp
    public TextReaderView(TextLineList lines, int maxColumns, double textScale, string? diagnosticsPath)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(maxColumns);
        this.lines = lines;
        this.maxColumns = maxColumns;
        this.textScale = textScale;
        this.diagnosticsPath = diagnosticsPath;
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }
```

3. After `public int LineCount => lines.Count;`, add:

```csharp
    // T14.1: the guide's text size. Rows re-measure at the new size, keep
    // Consolas, their measured width and no wrapping, and the top line stays.
    // Before the first layout the value is only kept.
    public double TextScale
    {
        get => textScale;
        set
        {
            if (value == textScale) return;
            textScale = value;
            Remeasure();
        }
    }

    private double CellFontSize => baseFontSize * testScale * textScale;
```

4. Replace `Measure()` with:

```csharp
    private void Measure()
    {
        CellProbe.FontSize = CellFontSize;
        CellProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        cellWidth = CellProbe.DesiredSize.Width / ProbeColumns;
        rowWidth = Math.Ceiling(cellWidth * maxColumns);
        rowHeight = Math.Ceiling(CellProbe.DesiredSize.Height);
        WriteDiagnostics();
    }

    // Installed tests read the applied size and row measure (Ruling 7).
    private void WriteDiagnostics()
    {
        if (diagnosticsPath is null) return;
        string json = string.Create(CultureInfo.InvariantCulture,
            $"{{\"scale\":{textScale:R},\"rowWidth\":{rowWidth:R},\"cellWidth\":{cellWidth:R},\"rowHeight\":{rowHeight:R}}}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(diagnosticsPath)!);
            string temporary = diagnosticsPath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, diagnosticsPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Test diagnostics must not affect reading.
        }
    }
```

5. In `ApplyRowSize`, replace `text.FontSize = baseFontSize * fontScale;`
   with `text.FontSize = CellFontSize;`.
6. In `OpenRemeasureHook`'s callback, replace

```csharp
                    fontScale = TestFontScale;
                    CellProbe.FontSize = baseFontSize * fontScale;
```

   with

```csharp
                    testScale = TestFontScale;
                    CellProbe.FontSize = CellFontSize;
```

Check: `grep -n "fontScale" src/DesktopGuides.Production/TextReaderView.xaml.cs`
prints nothing.

- [ ] **Step 2: Declare and handle the capability in the TXT session**

In `src/DesktopGuides.Production/TextReaderSession.cs`, replace the
constructor and `Capabilities` with:

```csharp
    // The initial text scale applies before the first measure (Ruling 5);
    // with a diagnostics folder the view reports each measure there.
    public TextReaderSession(
        TextGuideDocument document, int maxColumns, double textScale, string? diagnosticsFolder)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.document = document;
        string? diagnosticsPath = diagnosticsFolder is null
            ? null
            : Path.Combine(diagnosticsFolder, $"txt-text-size-{Environment.ProcessId}.json");
        View = new TextReaderView(new TextLineList(document), maxColumns, textScale, diagnosticsPath);
        View.TopLineChanged += OnTopLineChanged;
    }

    // Installed tests open the gate to read the view's applied size.
    public static string? DiagnosticsFolderForTest(string cacheRoot) =>
        TestGate.IsOpen($@"Local\DesktopGuides.Preview.TextDiagnostics.{Environment.ProcessId}")
            ? Path.Combine(cacheRoot, "diagnostics")
            : null;

    public TextReaderView View { get; }
    public GuideFormat Format => GuideFormat.Txt;
    public ReaderCapabilities Capabilities =>
        ReaderCapabilities.Scroll | ReaderCapabilities.PageNavigation | ReaderCapabilities.TextSize;
```

In `ExecuteAsync`, add before `default:`:

```csharp
            case TextSizeAction size:
                View.TextScale = size.Scale;
                break;
```

- [ ] **Step 3: Declare and handle the capability in the HTML session**

In `src/DesktopGuides.Production/HtmlReaderSession.cs`:

1. Line 89 becomes
   `public ReaderCapabilities Capabilities => ReaderCapabilities.Scroll | ReaderCapabilities.TextSize;`
2. Replace `ExecuteAsync` (lines 325–326) with:

```csharp
    // A text size keeps the current theme; the style's zoom applies it.
    public Task ExecuteAsync(ReaderAction action, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action is TextSizeAction size
            ? ApplyAppearanceAsync(appearance with { TextScale = size.Scale }, token)
            : throw new NotSupportedException($"HTML guides don't support {action.Command}.");
    }
```

- [ ] **Step 4: Keep the shell building**

In `src/DesktopGuides.Production/ShellWindow.xaml.cs:1770`, replace
`TextReaderSession session = new(document, maxColumns);` with:

```csharp
                    TextReaderSession session = new(
                        document, maxColumns, TextSizeSteps.Default,
                        TextReaderSession.DiagnosticsFolderForTest(cacheRoot!));
```

(Task 5 replaces `TextSizeSteps.Default` with the stored scale.) Add
`using DesktopGuides.Core.Reading;` to the file if it isn't there.

- [ ] **Step 5: Build**

Sync, then run the Production build (Global Constraints).
Expected: `Build succeeded.`, 0 errors and no new warnings.
Run: the Core and Infrastructure tests.
Expected: both PASS with the counts from Task 1.

- [ ] **Step 6: Commit**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add src/DesktopGuides.Production/TextReaderView.xaml.cs src/DesktopGuides.Production/TextReaderSession.cs src/DesktopGuides.Production/HtmlReaderSession.cs src/DesktopGuides.Production/ShellWindow.xaml.cs
git -C $R commit -m "feat(shell): T14.1 TXT and HTML sessions apply a text size" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: Toolbar label, reset, keys and stepping

**Files:**
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml` (SmallerText,
  LargerText, a label container between them, `ResetTextSize` in the
  overflow)
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml.cs`

**Interfaces:**
- Consumes: `TextSizeSteps` (Task 1); sessions that declare `TextSize`
  (Task 3).
- Produces, for Task 5:
  - `public void SetTextSize(double scale)`: shows `scale` in the label
    and the command states without raising anything;
  - `public event Action<IReaderSession, double>? TextSizeChanged`: raised
    by a step, before its `TextSizeAction(target)` runs, with the session
    the step was for;
  - after `SetSession`, the toolbar shows 100% until the shell calls
    `SetTextSize`.

The failing tests are Task 2's toolbar-smoke checks (the AcceleratorKeys of
*Smaller text*, *Larger text* and *Reset text size*) and the installed
`text-size` group.

- [ ] **Step 1: Add the label, tooltips and reset**

In `src/DesktopGuides.Production/ReaderToolbar.xaml`, replace the
`SmallerText` and `LargerText` buttons with:

```xml
            <AppBarButton x:Name="SmallerText"
                          Icon="Remove"
                          Label="Smaller text"
                          ToolTipService.ToolTip="Smaller text (Ctrl+Minus)"
                          AutomationProperties.AcceleratorKey="Ctrl+Minus"
                          Visibility="Collapsed"
                          Click="SmallerTextClicked" />
            <AppBarElementContainer x:Name="TextSizeContainer"
                                    VerticalContentAlignment="Center"
                                    IsTabStop="False"
                                    Visibility="Collapsed">
                <TextBlock x:Name="TextSizeValue"
                           Text="100%"
                           MinWidth="44"
                           Margin="4,0"
                           TextAlignment="Center"
                           VerticalAlignment="Center"
                           AutomationProperties.AutomationId="TextSizeValue"
                           AutomationProperties.Name="Text size 100%"
                           AutomationProperties.LiveSetting="Polite" />
            </AppBarElementContainer>
            <AppBarButton x:Name="LargerText"
                          Icon="Add"
                          Label="Larger text"
                          ToolTipService.ToolTip="Larger text (Ctrl+Plus)"
                          AutomationProperties.AcceleratorKey="Ctrl+Plus"
                          Visibility="Collapsed"
                          Click="LargerTextClicked" />
```

and after the `FitToWidth` button in `SecondaryCommands` add:

```xml
            <AppBarButton x:Name="ResetTextSize"
                          Label="Reset text size"
                          ToolTipService.ToolTip="Reset text size (Ctrl+0)"
                          AutomationProperties.AcceleratorKey="Ctrl+0"
                          Visibility="Collapsed"
                          IsEnabled="False"
                          Click="ResetTextSizeClicked" />
```

- [ ] **Step 2: Hold the scale and step it**

In `src/DesktopGuides.Production/ReaderToolbar.xaml.cs`:

1. After `private bool promptOpen;` add:

```csharp
    // The open guide's text size; the shell sets it at open (Ruling 3).
    private double textScale = TextSizeSteps.Default;
```

2. After `public event Action<string>? CommandFailed;` add:

```csharp
    // Raised by a text size step before its action runs, so the shell's
    // scale is current for a theme refresh during the write (Ruling 3).
    public event Action<IReaderSession, double>? TextSizeChanged;
```

3. In `SetSession`, after `ZoomOut.IsEnabled = true;` add
   `ShowTextSize(TextSizeSteps.Default, announce: false);`.
4. In `RefreshCommands`, after `LargerText.Visibility = Show(textSize);`
   add:

```csharp
        TextSizeContainer.Visibility = Show(textSize);
        ResetTextSize.Visibility = Show(textSize);
```

5. In `TryRunKey`, replace the first condition with:

```csharp
        AppBarButton? command = CommandFor(key, modifiers);
        // Ruling 1: the text size keys run before T16.1 turns the other keys on.
        bool textKey = command == SmallerText || command == LargerText || command == ResetTextSize;
        if (command is null || !(KeysEnabled || textKey) || promptOpen || session is null ||
            XamlRoot is null || Commands.Visibility != Visibility.Visible ||
            command.Visibility != Visibility.Visible || !command.IsEnabled ||
            DialogOpen())
        {
            return false;
        }
```

6. Replace the three zoom arms of `CommandFor` with:

```csharp
            (VirtualKey.Add or EqualsKey, Ctrl) => ZoomOr(ZoomIn, LargerText),
            (EqualsKey, Ctrl | VirtualKeyModifiers.Shift) => ZoomOr(ZoomIn, LargerText),
            (VirtualKey.Subtract or MinusKey, Ctrl) => ZoomOr(ZoomOut, SmallerText),
            (VirtualKey.Number0 or VirtualKey.NumberPad0, Ctrl) => ZoomOr(FitToWidth, ResetTextSize),
```

   and after `CommandFor` add:

```csharp
    // Ruling 2: a session shows zoom or text size, not both. The zoom
    // commands win while visible, even disabled.
    private static AppBarButton ZoomOr(AppBarButton zoom, AppBarButton text) =>
        zoom.Visibility == Visibility.Visible ? zoom : text;
```

7. In `RunAsync`, before the `throw`, add:

```csharp
        if (command == SmallerText) return StepTextAsync(TextSizeSteps.Smaller(textScale), "make the text smaller");
        if (command == LargerText) return StepTextAsync(TextSizeSteps.Larger(textScale), "make the text larger");
        if (command == ResetTextSize) return StepTextAsync(TextSizeSteps.Default, "reset the text size");
```

8. After `SetZoomAvailability` add:

```csharp
    // The shell shows the open guide's size, at open and after a failed save.
    public void SetTextSize(double scale) => ShowTextSize(scale, announce: false);

    private void ShowTextSize(double scale, bool announce)
    {
        textScale = scale;
        string label = TextSizeSteps.Label(scale);
        TextSizeValue.Text = label;
        AutomationProperties.SetName(TextSizeValue, $"Text size {label}");
        bool larger = TextSizeSteps.CanLarger(scale);
        bool smaller = TextSizeSteps.CanSmaller(scale);
        // P5. Disabling the focused command would move focus, so it moves to the other one first.
        if (!larger && smaller && LargerText.FocusState != FocusState.Unfocused)
        {
            SmallerText.Focus(LargerText.FocusState);
        }
        else if (!smaller && larger && SmallerText.FocusState != FocusState.Unfocused)
        {
            LargerText.Focus(SmallerText.FocusState);
        }
        LargerText.IsEnabled = larger;
        SmallerText.IsEnabled = smaller;
        ResetTextSize.IsEnabled = scale != TextSizeSteps.Default;
        if (announce)
        {
            FrameworkElementAutomationPeer.CreatePeerForElement(TextSizeValue)
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    // The toolbar steps (Ruling 3); the session only applies the target.
    private Task StepTextAsync(double target, string description)
    {
        IReaderSession? current = session;
        if (current is null || target == textScale)
        {
            return Task.CompletedTask;
        }
        ShowTextSize(target, announce: true);
        TextSizeChanged?.Invoke(current, target);
        return ExecuteAsync(new TextSizeAction(target), description);
    }
```

9. In `RestorePromptFocus`'s fallback list, add `ResetTextSize` after
   `FitToWidth`.
10. Replace the two text handlers with:

```csharp
    private async void SmallerTextClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(SmallerText);

    private async void LargerTextClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(LargerText);

    private async void ResetTextSizeClicked(object sender, RoutedEventArgs args) =>
        await RunAsync(ResetTextSize);
```

- [ ] **Step 3: Build and run the toolbar smoke**

Sync, then run the Production and toolbar-smoke builds (Global
Constraints). Expected: `Build succeeded.`, 0 errors, for both.

Run the toolbar smoke on CI, which isn't `dev-fast`:

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production/ReaderToolbar.xaml src/DesktopGuides.Production/ReaderToolbar.xaml.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(shell): T14.1 toolbar text size label, reset and keys" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
gh workflow run windows-ci.yml --ref feat/p1-t14-1-text-size -f shell-scope=text-size
```

Run: the CI loop.
Expected:
- the reader-toolbar smoke job PASSES, including `shortcut-names` (the
  new AcceleratorKeys), `worker-capability-change-text-dispatch`
  (`Text size 1.1`, 100% stepped to 110%) and
  `Ctrl+= with Zoom in disabled` (zoom still wins, Ruling 2);
- the `text-size` group still FAILS in `text-size-steps`. With keys
  arriving it stops at `Wait-Status 'Text size 110%.'`, because nothing
  saves yet. If it stops at `Wait-TextSize '110%'` instead, the TXT list
  is eating Ctrl+=: Task 5 Step 4 handles that (Ruling 9).

If the toolbar smoke fails on an overflow check that counts commands,
add *Reset text size* to that check's expected list and record a ruling.

### Task 5: Shell reads, saves and puts back the size

**Files:**
- Create: `src/DesktopGuides.Production/ShellWindow.TextSize.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (subscription
  near line 132; TXT open near line 1770)
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs:40-56,78,84-97`
- Modify: `src/DesktopGuides.Production/ShellWindow.Theme.cs:20-21,87`

**Interfaces:**
- Consumes: `ReaderToolbar.SetTextSize(double)` and
  `ReaderToolbar.TextSizeChanged` (Task 4), the Task 3 constructor,
  `TextSizeSteps.Normalize`, `Status` and `SaveFailed` (Task 1),
  `GetReaderPreferencesAsync` and `SaveReaderPreferencesAsync` (existing).
- Produces: the statuses and stored values Task 2 checks.

The failing test is Task 2's `text-size` group, which Task 4's run left
failing at the first status.

- [ ] **Step 1: Add `ShellWindow.TextSize.cs`**

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Production;

// T14.1: the open TXT or HTML guide's text size, read once at open and
// saved per guide. A size change isn't reader movement: no place is saved.
public sealed partial class ShellWindow
{
    private readonly SemaphoreSlim textSizeSaveGate = new(1, 1);
    // The size on screen, and the last one storage accepted, for the guide
    // of the current reader session (Ruling 6).
    private double readerTextScale = TextSizeSteps.Default;
    private double committedTextScale = TextSizeSteps.Default;
    private Guid textSizeGuideId;

    // A failed read gives the default: the size never blocks reading.
    private async Task<double> ReadTextScaleAsync(Guid guideId)
    {
        try
        {
            ReaderPreferences? preferences = await RequireRepository().GetReaderPreferencesAsync(guideId);
            return TextSizeSteps.Normalize(preferences?.TextScale);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return TextSizeSteps.Default;
        }
    }

    // After the toolbar has the session: it shows the guide's size.
    private void BeginTextSize(Guid guideId, double scale)
    {
        textSizeGuideId = guideId;
        readerTextScale = scale;
        committedTextScale = scale;
        ReaderActions.SetTextSize(scale);
    }

    // The toolbar has already applied the size (Ruling 3). Saves run one at
    // a time; only the latest change for the open guide counts (Ruling 8).
    private async void OnTextSizeChanged(IReaderSession session, double scale)
    {
        if (!ReferenceEquals(session, readerSession) || repository is null)
        {
            return;
        }
        Guid guideId = textSizeGuideId;
        readerTextScale = scale;
        await textSizeSaveGate.WaitAsync();
        try
        {
            if (ReferenceEquals(session, readerSession) && scale != readerTextScale)
            {
                // A newer change is pending; its own save stores the size.
                return;
            }
            await repository.SaveReaderPreferencesAsync(guideId, scale);
            if (ReferenceEquals(session, readerSession))
            {
                committedTextScale = scale;
                if (scale == readerTextScale)
                {
                    ShowTransientStatus(TextSizeSteps.Status(scale));
                }
            }
        }
        catch (Exception error)
        {
            // A closed guide's save, or one a newer change replaced, leaves the screen alone.
            if (ReferenceEquals(session, readerSession) && scale == readerTextScale)
            {
                await RevertTextSizeAsync(session);
                ShowErrorStatus(TextSizeSteps.SaveFailed(error.Message));
            }
        }
        finally
        {
            textSizeSaveGate.Release();
        }
    }

    private async Task RevertTextSizeAsync(IReaderSession session)
    {
        double stored = committedTextScale;
        readerTextScale = stored;
        ReaderActions.SetTextSize(stored);
        try
        {
            await session.ExecuteAsync(new TextSizeAction(stored), CancellationToken.None);
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
        {
            // A session closed mid-revert has nothing left to size.
        }
    }
}
```

- [ ] **Step 2: Wire TXT and HTML opens**

1. `ShellWindow.xaml.cs`, after `ReaderActions.CommandFailed += ShowErrorStatus;`:

```csharp
        ReaderActions.TextSizeChanged += OnTextSizeChanged;
```

2. `ShellWindow.xaml.cs`, the TXT branch: replace Task 3's session
   construction and the `ReaderActions.SetSession(session);` after it with:

```csharp
                    double textScale = await ReadTextScaleAsync(guide.Id);
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    TextReaderSession session = new(
                        document, maxColumns, textScale,
                        TextReaderSession.DiagnosticsFolderForTest(cacheRoot!));
                    readerSession = session;
                    ShowReaderSurface(placeholder: false, view: session.View);
                    ReaderActions.SetSession(session);
                    BeginTextSize(guide.Id, textScale);
```

3. `ShellWindow.HtmlReader.cs`:
   - line 40: `htmlTextScale = await ReadTextScaleAsync(guide.Id);` becomes
     `double textScale = await ReadTextScaleAsync(guide.Id);`, and after the
     generation check that follows add `readerTextScale = textScale;` (a
     theme refresh during the open sends it);
   - line 56: `new ReaderAppearance(ReaderThemeNow(), htmlTextScale), token);`
     becomes `new ReaderAppearance(ReaderThemeNow(), textScale), token);`;
   - after `ReaderActions.SetSession(session);` (line 78) add
     `BeginTextSize(guide.Id, textScale);`;
   - delete `ReadTextScaleAsync` and its two comment lines (lines 84–97):
     it now lives in `ShellWindow.TextSize.cs`. Remove any `using` that
     only it needed.
4. `ShellWindow.Theme.cs`: delete the field
   `private double htmlTextScale = 1.0;` and its comment (lines 20–21),
   and in `RefreshReaderAppearance` replace `htmlTextScale` with
   `readerTextScale`.

Check: `grep -rn "htmlTextScale\|ClampScale(preferences" src/DesktopGuides.Production`
prints nothing.

- [ ] **Step 3: Build and run the group**

Sync, then run the Production build. Expected: `Build succeeded.`, 0
errors.

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production/ShellWindow.TextSize.cs src/DesktopGuides.Production/ShellWindow.xaml.cs src/DesktopGuides.Production/ShellWindow.HtmlReader.cs src/DesktopGuides.Production/ShellWindow.Theme.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(shell): T14.1 save, restore and put back a guide's text size" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
gh workflow run windows-ci.yml --ref feat/p1-t14-1-text-size -f shell-scope=text-size -f dev-fast=true
```

Run: the CI loop.
Expected: the `text-size` group PASSES: every mode in its report's
`phases`, `storedBeforeRelaunch` `{ascii: 1.25, web: 0.9, utf8: null}`,
`storedAfterPdf` with `pdf: null`, `storedAfterRetry` `ascii: 1.5`, and
`htmlPageFocusKeys` recorded. Write `htmlPageFocusKeys` to the ledger
for Task 6.

- [ ] **Step 4: Only if Ctrl+= doesn't reach the toolbar from the TXT list**

If `text-size-steps` fails at `Wait-TextSize '110%'` after Ctrl+= with
focus in `ReaderTextLines` (Ruling 9), route the view's keys like the
PDF preview. In the TXT branch of `ShellWindow.xaml.cs`, after
`readerSession = session;` add:

```csharp
                    session.View.PreviewKeyDown += OnTextPreviewKeyDown;
```

and in `ShellWindow.TextSize.cs` add (with `using Microsoft.UI.Xaml.Input;`):

```csharp
    // Ruling 9: the TXT list handles Ctrl+= and Ctrl+Minus itself, so its
    // keys go to the toolbar first, as the PDF preview's do.
    private void OnTextPreviewKeyDown(object sender, KeyRoutedEventArgs args) =>
        args.Handled = ReaderActions.TryRunKey(args.Key, CurrentModifiers(), fromContent: true);
```

Rebuild, commit (`fix(shell): T14.1 TXT list keys reach the text size commands`),
push and rerun. Record in the ledger which case held.

- [ ] **Step 5: Run the groups the change touches**

Run the CI loop with `-f shell-scope=txt -f dev-fast=true`, then with
`-f shell-scope=html -f dev-fast=true`.
Expected: both PASS. In `html`, Canary Guide A and B pass
`Assert-OnlyTextSizeCommands` (Ruling 12), and the T09.2 theme passes
still report the stored scale at open (`html-theme-open` root zoom
`1.5`).

If a failure isn't explained by this task's change, use
superpowers:systematic-debugging before changing anything.

### Task 6: Docs, evidence, full run and PR

**Files:**
- Modify: `docs/p1/t14-1-text-size-design.md` (status, implementation
  notes, verification)
- Modify: `docs/p1/implementation-plan.md:889` and the summary paragraphs
  after line 914
- Modify: `docs/work-breakdown.md:450`
- Modify: `docs/p1-technical-design.md:824-829`
- Create: `docs/p1/evidence/t14-1-text-size/` (reports and screenshots)

**Interfaces:**
- Consumes: the CI run ids, the test counts and the ledger's rulings from
  Tasks 1–5.

- [ ] **Step 1: Full CI run**

```bash
gh workflow run windows-ci.yml --ref feat/p1-t14-1-text-size
```

Run: the CI loop (all shards; not `dev-fast`).
Expected: every job PASSES, including the `core` shard with `text-size`,
the reader-toolbar smoke and `native-arm64-ui`. Record the run id.

- [ ] **Step 2: Copy the evidence**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
mkdir -p $R/docs/p1/evidence/t14-1-text-size
gh run download <run-id> -n production-shell-ui-core -D /tmp/t14-1-core
```

Copy the `text-size-*` JSON reports and the screenshots
(`text-size-200`, `text-size-75`, `text-size-150`, `text-size-html-90`,
`text-size-error`) from `/tmp/t14-1-core` into
`docs/p1/evidence/t14-1-text-size/`. Before committing, check that no
report contains a path outside the runner's work folder or a credential:
`grep -rniE "password|token|igdb|steamgrid" docs/p1/evidence/t14-1-text-size || echo clean`.
Expected: `clean`.

- [ ] **Step 3: Update the docs**

1. `docs/p1/t14-1-text-size-design.md`:
   - line 3 becomes `Status: implemented; CI run <run-id>.`;
   - before `## Risks`, add `## Implementation notes` listing Rulings
     1–12 of this plan, one bullet each in the spec's voice, plus every
     ledger ruling made during execution, the Ruling 9 outcome and the
     `htmlPageFocusKeys` result (Risks says it is recorded here);
   - then `## Verification`: the Core test counts before and after (from
     the ledger), `TextSizeStepsTests` and the two `ReaderContractTests`
     cases, the full run id, one bullet per `text-size-*` mode with what
     it showed and links to the evidence, and the `txt` and `html` group
     results.
2. `docs/p1/implementation-plan.md`: in the T14.1 row (line 889), after
   "TXT fixed-width layout." add
   ` Implemented with fixed steps 75–200%, a label and a reset; see [the design](t14-1-text-size-design.md).`
   After the T09.2 paragraph (ending "The style makes no request."), add:

   ```markdown
   T14.1 is implemented; see the
   [design and implementation notes](t14-1-text-size-design.md).
   TXT and HTML guides show *Smaller text*, the current size and *Larger
   text*, with *Reset text size* in the overflow and the Ctrl+Minus,
   Ctrl+Plus and Ctrl+0 keys. The size moves through fixed steps from 75%
   to 200%, applies at once, is saved per guide and survives a relaunch,
   and a failed save puts the stored size back. TXT keeps its fixed-width
   columns; PDF keeps its own zoom.
   ```
3. `docs/work-breakdown.md:450`: after the T14.1 line add
   `  Implemented; see [p1/t14-1-text-size-design.md](p1/t14-1-text-size-design.md).`
4. `docs/p1-technical-design.md`, the S14 T14.1 entry: after "Show
   Smaller/Larger and a current value with bounded steps;" add
   ` the steps are 75, 90, 100, 110, 125, 150, 175 and 200%, with a reset to 100%;`.

- [ ] **Step 4: Commit and open the PR**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add docs
git -C $R commit -m "docs(p1): T14.1 verification and evidence" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C $R push
```

Open a draft PR to `main` with the GitHub MCP `create_pull_request`
(check for a PR template first). The body names:
- the target task, T14.1 (TR14.1);
- prerequisites and their status: T03.2 (PR #4), T08.2, T09.1 (PR #35),
  T11.3 and T09.2 (PR #49), all merged;
- the intended outcome (the spec's success bullets);
- the full CI run, its artifact `production-shell-ui-core`, and the
  `htmlPageFocusKeys` result;
- screenshots of the changed toolbar: the 200%, 75% and HTML 90%
  captures from the evidence folder, linked by their branch URLs;
- ending with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

- [ ] **Step 5: Review**

Use superpowers:requesting-code-review with the PR URL, base `main`,
head `feat/p1-t14-1-text-size`, the spec and this plan, Rulings 1–12,
the ledger's rulings, and this plan's Review Focus verbatim. Address the
findings with superpowers:receiving-code-review, then mark the PR ready.
Merging waits for the user.
