# T14.3 Appearance Restore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A text-size step keeps an HTML guide's reading place: the same
context stays at the top, progress never saves a place that moved only
because the size changed, and a fraction-only fallback says, briefly,
that the place may have shifted. TXT, PDF and theme changes keep their
place as they already do.

**Architecture:**

- Core gains `HtmlLocationRules.NeedsAppearanceRestore` and
  `AppearanceOutcome`, `TextSizeSteps.ShiftedStatus`, and the contract
  event `IReaderSession.AppearanceRestored` with
  `AppearanceRestoredEventArgs`.
- `HtmlReaderSession` runs a restyle like a resize: it takes a fresh
  capture, pauses the tracker, writes the style, scrolls back to the
  pre-change point when the scale changed, re-baselines and raises
  `AppearanceRestored`. It never re-captures, so the locator stays the
  pre-change point.
- `ShellWindow.TextSize.cs` shows the shifted status on an Approximate or
  Unavailable outcome. TXT and PDF stub the event.
- Two installed phases in the `html-position` group (CI `html` shard):
  `position-text-size` on the long `<pre>` guide and
  `position-text-size-fallback` on a new image-only guide.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.5.1), WebView2,
SQLite, xUnit, PowerShell UI Automation.

**Spec:** [t14-3-appearance-restore-design.md](t14-3-appearance-restore-design.md)

## Global Constraints

**Branch:** `feat/p1-t14-3-appearance-restore`, already checked out. The
spec is commit `73b9a66`.

**Tooling:**

- There is no local `dotnet` or `pwsh`. Builds and tests run on the Windows
  host `pcsx2-win` or in CI.
- Host sync (staging folder `E:\work\desktop-guides\t14-3`, not a git
  checkout; create it once with `ssh -o BatchMode=yes pcsx2-win "mkdir E:\work\desktop-guides\t14-3"`):

  ```bash
  R=/Users/ilya.lissoboi/work/desktop-guides
  git -C $R ls-files -co --exclude-standard -z -- . ':!.claude' |
    tar -C $R --null -T - -cf - |
    ssh -o BatchMode=yes pcsx2-win "tar -xf - -C E:\work\desktop-guides\t14-3"
  ```

- Host commands (the host's default shell is `cmd`):

  ```bash
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-3\tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj"
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-3\tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t14-3\src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t14-3\tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t14-3\tools\p1\DesktopGuides.ReaderToolbarSmoke\DesktopGuides.ReaderToolbarSmoke.csproj -c Release -p:Platform=x64"
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
- This plan's group is `html` (Run-HtmlReaderScenarios, then the light
  and dark `html-position` passes, then Run-HtmlThemeScenarios).
- `dev-fast=true` runs are for iteration and are not PR evidence.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- PowerShell stays ASCII-only. Bash commands use absolute paths or
  `git -C`, never `cd`. Quote globs (zsh). Leave the untracked `.claude/`
  and `.superpowers/sdd/host-run` alone.
- Installed runs follow [e2e-testing.md](e2e-testing.md), including its
  backup and cleanup rules. No firewall rule. If a run needs an elevated
  scheduled task, stop and ask.
- Imported HTML is untrusted. The restore uses only the existing
  `HtmlPositionScripts` and checks every reply through `HtmlLocationRules`.
- An appearance change isn't reader movement: it never saves a reading
  place. The installed checks assert what the app controls (top line,
  locator, outcome diagnostics, status, save counts), not Chromium's
  layout.

**Values (verbatim from the spec):**

- `AppearanceRestoredEventArgs(RestoreOutcome outcome)` with
  `RestoreOutcome Outcome { get; }`;
  `event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored;`
  on `IReaderSession`. TXT, PDF and the fakes declare it
  `{ add { } remove { } }`.
- `NeedsAppearanceRestore(ReaderAppearance? applied, ReaderAppearance next)`:
  true only when `HtmlReaderStyle.ClampScale` of the two scales differs;
  a null `applied` is false.
- `AppearanceOutcome(HtmlRestoreTarget? landed)`: `Exact` or `Context`
  step → `Exact`; `Fraction` → `Approximate` with a null reason; null →
  `Unavailable` with `HtmlLocationRules.UnavailableReason`.
- `ShiftedStatus(1.5)` is `Text size 150%. Your place may have shifted.`
- `html-position-<pid>.json` gains `appearanceKind` and `appearanceStep`.
- `position-text-size`: 100 → 150 → 200 → 75% with MARK-0420 on top,
  then back to 100%.

**Rulings this plan makes against the spec** (Task 5 records them in the
spec's implementation notes):

1. **No unsubscribe.** The spec says the shell unsubscribes on detach. The
   HTML attach path (`ShellWindow.HtmlReader.cs:48-51`) subscribes
   `ExternalLinkRequested`, `UnavailableLinkRequested` and `Failed`
   without ever unsubscribing and filters with
   `ReferenceEquals(sender, readerSession)`. `AppearanceRestored` follows
   that pattern: subscribed there, filtered the same way.
2. **One flag joins the save status and the restore.** T14.1 shows
   `Status(scale)` only after the database save, so the save and the
   restore event can land in either order. `ShellWindow.TextSize.cs`
   gains `double? shiftedTextScale`: `OnTextSizeChanged` clears it at its
   start; a successful save shows `ShiftedStatus` when
   `shiftedTextScale == scale`, else `Status`; an Approximate or
   Unavailable event sets it to `readerTextScale` and shows
   `ShiftedStatus` unless a warning or error is showing; an Exact event
   clears it. Whichever lands last, the status is right.
3. **The fallback check reads the locator, not the view.** The spec's
   "estimated fraction within 0.05 of the one before" becomes "the
   locator is unchanged": the app chooses the locator, while the view's
   fraction after a zoom is Chromium's arithmetic.
4. **Two more diagnostics fields.** `appearanceScale` (the clamped scale
   the restore ran at) and `appearanceRestores` (a count) join
   `appearanceKind` and `appearanceStep`, so the smoke can wait for the
   restore of a given step. `Read-HtmlPosition` returns all four.
5. **Appearance restores aren't counted as restores.**
   `HtmlSessionDiagnostics.RecordRestore` keeps counting open-time
   restores only, so `Assert-HtmlPositionPass`'s count check stays valid.
6. **Progress counts in the position passes.** `Invoke-HtmlPositionPass`
   also opens the `ProgressDiagnostics` gate, and the smoke's
   `Read-ProgressCounts` moves from the `progress-*` block to the shared
   scope next to `Read-HtmlPosition`.
7. **`WriteAppearanceAsync` returns `Task<bool>`.** True when the page
   has the latest appearance; false after a failed write, on a failed
   page, or when another write is running. `OpenAsync` ignores it.
8. **The tracker and `GetLocationAsync` honor `restyling`.** A resize's
   `finally` can restart the tracker mid-restyle, so `OnTrackerTick`
   returns while `restyling` is set, as it does for `restoring`.
9. **The image-only guide joins the position seed.** `seed-html-position`
   also publishes `html-pictures` as "Picture Web Guide" and prints
   `guidePictures`. `Assert-HtmlPositionPass` accepts that guide's served
   set `guide.html,images/tile.png`.
10. **A theme-only change still pauses.** Steps 1–3 (capture, pause,
    write) run for every restyle; only step 4 depends on the scale.
11. **A write bumps the generation again.** After each write that
    changed the scale, `generation++`, so an open-time restore that
    started during the restyle sees the change and scrolls to its target
    again.
12. **The fallback steps to 110%.** The spec's example status says 125%;
    one *Larger text* from 100% is 110%, so the phase checks
    `Text size 110%. Your place may have shifted.` and then 100% on the
    way back.

## Review Focus

1. **A step during the open-time restore.** Pressing *Larger text* while
   a saved place is being restored must end at the saved place, with no
   appearance event. Ruling 11 and step 4's `!restoring` check; the
   restore's loop re-scrolls. Review it by reading `RestoreAsync` and
   `RestyleAsync` together.
2. **A resize during a restyle.** Dragging the window edge while a size
   write runs must end at the same top line, with the tracker running.
   Step 4 is skipped while `resizing`; the resize's `ReapplyAsync`
   scrolls to the same `current`; `RestyleAsync`'s `finally` doesn't
   start the tracker while `resizing`, the resize's does, and ruling 8
   keeps ticks out until the restyle ends. Review it by reading.
3. **A theme switch during a size restyle.** A Windows theme change
   while a size write runs must end with both on the page and one
   restore. `ApplyAppearanceAsync` returns while `restyling`; the loop
   picks the theme up. Review it by reading.
4. **A failed save after a shifted notice.** A failed text-size save
   must show T14.1's error, never the notice over it. Ruling 2: the
   notice checks `StatusShowsProblem()`. The revert's own restore event
   lands inside `RevertTextSizeAsync`, before `ShowErrorStatus`, so the
   error replaces it; one from a restyle already running lands after and
   is held back. Review it by reading; T14.1's `text-size-error` phase
   still passes.
5. **Closing the guide mid-restyle.** Going back while a write or scroll
   runs must raise nothing and never touch the next guide's status.
   Every await in `RestyleAsync` and `RestorePlaceAsync` is followed by a
   `disposed` check, and the shell filters by `readerSession`.

## File Map

| File | Change |
| --- | --- |
| `src/DesktopGuides.Core/Reading/ReaderContract.cs` | `AppearanceRestoredEventArgs`, `IReaderSession.AppearanceRestored` |
| `src/DesktopGuides.Core/Reading/TextSizeSteps.cs` | `ShiftedStatus` |
| `src/DesktopGuides.Core/Html/HtmlLocationRules.cs` | `NeedsAppearanceRestore`, `AppearanceOutcome` |
| `tests/DesktopGuides.Core.Tests/HtmlLocationRulesTests.cs` | rule tests |
| `tests/DesktopGuides.Core.Tests/TextSizeStepsTests.cs` | `ShiftedStatus` tests |
| `tests/DesktopGuides.Core.Tests/ReaderContractTests.cs` | event args test; fake stub |
| `tests/DesktopGuides.Core.Tests/ProgressCoordinatorTests.cs` | fake stub |
| `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs` | fake stub |
| `tools/p1/DesktopGuides.ReaderToolbarSmoke/ToolbarWindow.cs` | fake stub |
| `src/DesktopGuides.Production/TextReaderSession.cs`, `PdfReaderSession.cs` | stub |
| `src/DesktopGuides.Production/HtmlReaderSession.cs` | `RestyleAsync`, `RestorePlaceAsync`, diagnostics |
| `src/DesktopGuides.Production/ShellWindow.TextSize.cs` | `shiftedTextScale`, `OnAppearanceRestored` |
| `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs` | subscription |
| `tests/fixtures/p1/html-pictures/guide.html`, `images/tile.png` | new image-only fixture |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | `seed-html-position` publishes the picture guide |
| `tools/p1/windows_shell_install.ps1` | gate, served set, report id |
| `tools/p1/windows_shell_ui_smoke.ps1` | `Read-HtmlPosition` fields, `Read-ProgressCounts`, two phases |
| `docs/p1/*`, `docs/work-breakdown.md`, `docs/p1-technical-design.md` | Task 5 |

---

### Task 1: Core rules, the shifted status and the contract event

**Files:**
- Modify: `src/DesktopGuides.Core/Html/HtmlLocationRules.cs` (new section
  at the end of the class)
- Modify: `src/DesktopGuides.Core/Reading/TextSizeSteps.cs` (after
  `Status`)
- Modify: `src/DesktopGuides.Core/Reading/ReaderContract.cs:105-112`
- Modify (stubs): `src/DesktopGuides.Production/TextReaderSession.cs:40`,
  `src/DesktopGuides.Production/PdfReaderSession.cs:105`,
  `src/DesktopGuides.Production/HtmlReaderSession.cs:93`,
  `tests/DesktopGuides.Core.Tests/ReaderContractTests.cs:101`,
  `tests/DesktopGuides.Core.Tests/ProgressCoordinatorTests.cs:545`,
  `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs:1514`,
  `tools/p1/DesktopGuides.ReaderToolbarSmoke/ToolbarWindow.cs:167`
- Test: `tests/DesktopGuides.Core.Tests/HtmlLocationRulesTests.cs`,
  `tests/DesktopGuides.Core.Tests/TextSizeStepsTests.cs`,
  `tests/DesktopGuides.Core.Tests/ReaderContractTests.cs`

**Interfaces:**
- Consumes: `HtmlReaderStyle.ClampScale(double)`, `HtmlRestoreTarget`,
  `HtmlRestoreStep`, `RestoreOutcome`, `RestoreKind`, `ReaderAppearance`,
  `TextSizeSteps.Label(double)`.
- Produces:
  - `bool HtmlLocationRules.NeedsAppearanceRestore(ReaderAppearance? applied, ReaderAppearance next)`;
  - `RestoreOutcome HtmlLocationRules.AppearanceOutcome(HtmlRestoreTarget? landed)`;
  - `string TextSizeSteps.ShiftedStatus(double scale)`;
  - `sealed class AppearanceRestoredEventArgs(RestoreOutcome outcome) : EventArgs`
    with `RestoreOutcome Outcome { get; }`;
  - `event EventHandler<AppearanceRestoredEventArgs>? IReaderSession.AppearanceRestored`.
  HtmlReaderSession gets a real field-like event here
  (`public event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored;`),
  raised in Task 3.

- [ ] **Step 1: Write the failing rule tests**

Append to `HtmlLocationRulesTests` (before the class's closing brace):

```csharp
    // ---- NeedsAppearanceRestore ----

    [Fact]
    public void AFirstWriteNeedsNoAppearanceRestore() =>
        Assert.False(HtmlLocationRules.NeedsAppearanceRestore(
            null, new ReaderAppearance(ReaderTheme.Light, 1.5)));

    [Fact]
    public void AThemeOnlyChangeNeedsNoAppearanceRestore() =>
        Assert.False(HtmlLocationRules.NeedsAppearanceRestore(
            new ReaderAppearance(ReaderTheme.Light, 1.25), new ReaderAppearance(ReaderTheme.Dark, 1.25)));

    [Fact]
    public void AScaleChangeNeedsAnAppearanceRestore() =>
        Assert.True(HtmlLocationRules.NeedsAppearanceRestore(
            new ReaderAppearance(ReaderTheme.Light, 1.0), new ReaderAppearance(ReaderTheme.Light, 1.1)));

    [Fact]
    public void ScalesThatClampAlikeNeedNoAppearanceRestore() =>
        Assert.False(HtmlLocationRules.NeedsAppearanceRestore(
            new ReaderAppearance(ReaderTheme.Light, 2.0), new ReaderAppearance(ReaderTheme.Light, 9.0)));

    // ---- AppearanceOutcome ----

    [Theory]
    [InlineData(HtmlRestoreStep.Exact)]
    [InlineData(HtmlRestoreStep.Context)]
    public void ATextTargetIsAnExactAppearanceRestore(HtmlRestoreStep step) =>
        Assert.Equal(new RestoreOutcome(RestoreKind.Exact),
            HtmlLocationRules.AppearanceOutcome(new HtmlRestoreTarget(step, 120, 0.4)));

    [Fact]
    public void AFractionTargetIsAnApproximateAppearanceRestore() =>
        Assert.Equal(new RestoreOutcome(RestoreKind.Approximate),
            HtmlLocationRules.AppearanceOutcome(new HtmlRestoreTarget(HtmlRestoreStep.Fraction, 0, 0.5)));

    [Fact]
    public void NoTargetIsAnUnavailableAppearanceRestore() =>
        Assert.Equal(new RestoreOutcome(RestoreKind.Unavailable, HtmlLocationRules.UnavailableReason),
            HtmlLocationRules.AppearanceOutcome(null));
```

Append to `TextSizeStepsTests`:

```csharp
    [Fact]
    public void ShiftedStatusNamesTheSizeAndTheShift() =>
        Assert.Equal("Text size 150%. Your place may have shifted.", TextSizeSteps.ShiftedStatus(1.5));

    [Fact]
    public void ShiftedStatusIgnoresTheCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("Text size 125%. Your place may have shifted.", TextSizeSteps.ShiftedStatus(1.25));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
```

Append to `ReaderContractTests` (a test method, outside the fake):

```csharp
    [Fact]
    public void AppearanceRestoredCarriesItsOutcome()
    {
        RestoreOutcome outcome = new(RestoreKind.Approximate);
        Assert.Same(outcome, new AppearanceRestoredEventArgs(outcome).Outcome);
    }
```

- [ ] **Step 2: Run the Core tests to see them fail**

Sync, then run the Core tests command.
Expected: the build fails with CS0117 (`NeedsAppearanceRestore`,
`AppearanceOutcome`, `ShiftedStatus` not defined) and CS0246
(`AppearanceRestoredEventArgs` not found).

- [ ] **Step 3: Add the rules, the status and the contract**

In `HtmlLocationRules`, at the end of the class:

```csharp
    // T14.3: only a new zoom reflows the page; the theme part of the style
    // sets colors only. A first write is the open's, whose restore owns the
    // place.
    public static bool NeedsAppearanceRestore(ReaderAppearance? applied, ReaderAppearance next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return applied is not null &&
            HtmlReaderStyle.ClampScale(applied.TextScale) != HtmlReaderStyle.ClampScale(next.TextScale);
    }

    // The content can't change within a session, so no changed reason.
    public static RestoreOutcome AppearanceOutcome(HtmlRestoreTarget? landed) => landed?.Step switch
    {
        null => new RestoreOutcome(RestoreKind.Unavailable, UnavailableReason),
        HtmlRestoreStep.Fraction => new RestoreOutcome(RestoreKind.Approximate),
        _ => new RestoreOutcome(RestoreKind.Exact)
    };
```

In `TextSizeSteps`, after `Status`:

```csharp
    // T14.3: an HTML page that came back only by fraction.
    public static string ShiftedStatus(double scale) =>
        $"Text size {Label(scale)}. Your place may have shifted.";
```

In `ReaderContract.cs`, after `LocationChangedEventArgs` (line 105):

```csharp
// T14.3: raised after a session scrolled back to its place following an
// appearance change, with how closely it came back.
public sealed class AppearanceRestoredEventArgs(RestoreOutcome outcome) : EventArgs
{
    public RestoreOutcome Outcome { get; } = outcome;
}
```

and in `IReaderSession`, after `LocationChanged`:

```csharp
    event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored;
```

- [ ] **Step 4: Stub the event in every implementer**

In `TextReaderSession.cs` and `PdfReaderSession.cs`, after their
`CapabilitiesChanged` line:

```csharp
    // T14.3: their layout keeps its own place; nothing to report.
    public event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored { add { } remove { } }
```

In `HtmlReaderSession.cs`, after `LocationChanged` (line 93):

```csharp
    // T14.3: raised after a text-size change scrolled back to the place.
    public event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored;
```

In the four fakes (`ReaderContractTests.cs:101`,
`ProgressCoordinatorTests.cs:545`, `SqliteLibraryRepositoryTests.cs:1514`,
`ToolbarWindow.cs:167`), after their `CapabilitiesChanged` line:

```csharp
        public event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored { add { } remove { } }
```

The HTML field-like event gives warning CS0067 (never used) until Task 3;
if the build treats warnings as errors, add `#pragma warning disable
CS0067` around it in this task and remove it in Task 3.

- [ ] **Step 5: Run the tests and builds**

Sync, then run the Core tests, the Infrastructure tests, the Production
build and the ReaderToolbarSmoke build.
Expected: all Core and Infrastructure tests pass (the new ones included);
both builds succeed.

- [ ] **Step 6: Commit**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add src/DesktopGuides.Core src/DesktopGuides.Production tests tools/p1/DesktopGuides.ReaderToolbarSmoke
git -C $R commit -m "feat(core): T14.3 appearance restore rules and event

- HtmlLocationRules.NeedsAppearanceRestore and AppearanceOutcome
- TextSizeSteps.ShiftedStatus
- IReaderSession.AppearanceRestored; TXT, PDF and fakes stub it

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Installed `position-text-size` phases (the failing test)

**Files:**
- Create: `tests/fixtures/p1/html-pictures/guide.html`,
  `tests/fixtures/p1/html-pictures/images/tile.png`
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs:503-533`
- Modify: `tools/p1/windows_shell_install.ps1:1218-1300`
- Modify: `tools/p1/windows_shell_ui_smoke.ps1:1421-1440` (helpers),
  `:2125-2168` (phases), `:2790-2805` (`Read-ProgressCounts` moves)

**Interfaces:**
- Consumes: Task 1's `ShiftedStatus` text,
  `Text size <n>%. Your place may have shifted.`
- Produces (read by Task 3's diagnostics and Task 5's docs):
  - `html-position-<pid>.json` fields `appearanceKind` (`Exact`,
    `Approximate`, `Unavailable` or null), `appearanceStep` (`Exact`,
    `Context`, `Fraction` or null), `appearanceScale` (number or null),
    `appearanceRestores` (int);
  - `seed-html-position` prints `{guideLong, guideChanged, guidePictures}`;
  - report keys `htmlTextSizeMarks`, `htmlTextSizeSaves`,
    `htmlShiftedScreenshot`, phases `position-text-size` and
    `position-text-size-fallback`.

- [ ] **Step 1: Add the image-only fixture**

`tests/fixtures/p1/html-pictures/guide.html`:

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Picture Web Guide</title>
</head>
<body>
<!-- T14.3: tall local images and no text below the link, so a capture in
     the middle has no text box and comes back by fraction only. -->
<p><a href="#middle">Jump to the middle</a></p>
<img src="images/tile.png" alt="" style="display:block;width:100%;height:900px">
<img src="images/tile.png" alt="" style="display:block;width:100%;height:900px">
<img src="images/tile.png" alt="" style="display:block;width:100%;height:900px">
<div id="middle"></div>
<img src="images/tile.png" alt="" style="display:block;width:100%;height:900px">
<img src="images/tile.png" alt="" style="display:block;width:100%;height:900px">
<img src="images/tile.png" alt="" style="display:block;width:100%;height:900px">
</body>
</html>
```

`images/tile.png` is a byte copy of `tests/fixtures/p1/html-long/images/map.png`:

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
mkdir -p $R/tests/fixtures/p1/html-pictures/images
cp $R/tests/fixtures/p1/html-long/images/map.png $R/tests/fixtures/p1/html-pictures/images/tile.png
```

- [ ] **Step 2: Publish it in `seed-html-position`**

In `Program.cs`, after `guideChanged` (line 528):

```csharp
    Guid guidePictures = await PublishLongAsync("html-pictures", "Picture Web Guide");
```

and add `guidePictures = guidePictures.ToString("N")` to the printed
object, after `guideChanged`.

- [ ] **Step 3: Open the progress gate and accept the picture guide's files**

In `windows_shell_install.ps1`:

- `Invoke-HtmlPositionPass` (line 1222): the gate list becomes
  `@('HtmlDiagnostics', 'HtmlPosition', 'ProgressDiagnostics', 'ProgressOverride')`.
- `Assert-HtmlPositionPass`: replace the served check (lines 1251-1253)
  with

  ```powershell
        $served = @($session.served) -join ','
        # T14.3: the picture guide serves its entry and its one tile.
        $allowed = if ($session.guideId -eq $report.htmlPosition.guidePictures) {
            @('guide.html,images/tile.png')
        } else {
            @('guide.html,images/map.png', 'guide.html,images/map.png,images/route.png')
        }
        if ($served -cnotin $allowed) {
            throw "Guide $($session.guideId) served '$served' in the $pass pass."
        }
  ```

- `Run-HtmlPositionScenarios`: the report line becomes
  `$report.htmlPosition = [ordered]@{ guideLong = $ids.guideLong; guideChanged = $ids.guideChanged; guidePictures = $ids.guidePictures }`.

- [ ] **Step 4: Share `Read-ProgressCounts` and read the new fields**

In `windows_shell_ui_smoke.ps1`:

- Cut `Read-ProgressCounts` (lines 2790-2805) from the `progress-*` block
  and paste it unchanged after `Wait-HtmlPosition` (line 1440). The
  `progress-*` block is inside the same reader branch, so it still sees
  it.
- In `Read-HtmlPosition`, the `$position` object becomes:

  ```powershell
            $position = [ordered]@{
                locator = $file.locator; offset = $null; quote = $null
                kind = $file.kind; step = $file.step; reason = $file.reason
                appearanceKind = $file.appearanceKind; appearanceStep = $file.appearanceStep
                appearanceScale = $file.appearanceScale
                appearanceRestores = [int] $file.appearanceRestores
            }
  ```

- After `Read-ProgressCounts`, add:

  ```powershell
        # T14.3: one burst of quick toolbar clicks, then the restore the
        # session made after the burst's last write.
        function Step-HtmlTextSize([string] $command, [int] $clicks, [double] $scale, [string] $status) {
            $before = [int] (Read-HtmlPosition).appearanceRestores
            for ($index = 0; $index -lt $clicks; $index++) {
                $button = Find-VisibleName $command
                if (-not $button) { throw "The Reader toolbar has no visible '$command'." }
                Invoke-Element $button
            }
            [void](Wait-Status $status)
            return Wait-HtmlPosition {
                param($p)
                $p.appearanceRestores -gt $before -and $null -ne $p.appearanceScale -and
                    [Math]::Abs([double] $p.appearanceScale - $scale) -lt 0.001
            } "the restore at $scale"
        }
  ```

- [ ] **Step 5: Add `position-text-size`**

In the `html-position` block, after `$report.phases += 'position-resize'`
and before `$saved = (Read-HtmlPosition).locator`:

```powershell
            # position-text-size: each burst of size steps keeps MARK-0420
            # on top and the saved offset, by an exact restore, and saves
            # nothing: a size change isn't reader movement.
            Start-Sleep -Seconds 2
            $savesBefore = [int] (Read-ProgressCounts).saves
            $report.htmlTextSizeMarks = @()
            foreach ($burst in @(
                @{ command = 'Larger text'; clicks = 3; scale = 1.5; label = '150%' },
                @{ command = 'Larger text'; clicks = 2; scale = 2.0; label = '200%' },
                @{ command = 'Smaller text'; clicks = 7; scale = 0.75; label = '75%' },
                @{ command = 'Larger text'; clicks = 2; scale = 1.0; label = '100%' })) {
                $after = Step-HtmlTextSize $burst.command $burst.clicks $burst.scale "Text size $($burst.label)."
                $report.htmlTextSizeMarks += Wait-TopMark 420 "a size step to $($burst.label)"
                if ($after.appearanceKind -ne 'Exact') {
                    throw "A size step to $($burst.label) restored '$($after.appearanceKind)'; expected Exact."
                }
                $after = Read-HtmlPosition
                if ($after.offset -ne $target.offset) {
                    throw "After a size step to $($burst.label) the position offset was $($after.offset); expected $($target.offset)."
                }
            }
            # Past two polls and the progress timer.
            Start-Sleep -Seconds 5
            $report.htmlTextSizeSaves = [int] (Read-ProgressCounts).saves - $savesBefore
            if ($report.htmlTextSizeSaves -ne 0) {
                throw "Size steps saved the reading place $($report.htmlTextSizeSaves) times; expected none."
            }
            $report.phases += 'position-text-size'
```

- [ ] **Step 6: Add `position-text-size-fallback`**

Right after `$saved = (Read-HtmlPosition).locator` and its
`Back-ToTextGame` (lines 2167-2168), before `position-restore-exact`:

```powershell
            # position-text-size-fallback: with no text box on screen the
            # place comes back by fraction, the status says it may have
            # shifted, and the locator stays the pre-change point.
            Open-TextGuide 'Picture Web Guide'
            $report.sessionsOpened++
            [void](Wait-Status 'Guide ready.')
            Click-Element (Wait-PageVisible 'Jump to the middle')
            $middle = Wait-HtmlPosition { param($p) $p.locator -and -not $p.quote } 'a capture with no text'
            Start-Sleep -Seconds 2
            $savesBefore = [int] (Read-ProgressCounts).saves
            foreach ($step in @(
                @{ command = 'Larger text'; scale = 1.1; label = '110%' },
                @{ command = 'Smaller text'; scale = 1.0; label = '100%' })) {
                $after = Step-HtmlTextSize $step.command 1 $step.scale `
                    "Text size $($step.label). Your place may have shifted."
                if ($after.appearanceKind -ne 'Approximate' -or $after.appearanceStep -ne 'Fraction') {
                    throw "A size step to $($step.label) restored '$($after.appearanceKind)/$($after.appearanceStep)'; expected Approximate/Fraction."
                }
                if ($after.locator -cne $middle.locator) {
                    throw "A size step to $($step.label) changed the locator."
                }
                if ($step.scale -eq 1.1) {
                    $report.htmlShiftedScreenshot = Save-WindowScreenshot 'html-place-shifted'
                }
            }
            Start-Sleep -Seconds 5
            if ([int] (Read-ProgressCounts).saves -ne $savesBefore) {
                throw 'Size steps on the picture guide saved the reading place.'
            }
            Back-ToTextGame
            $report.phases += 'position-text-size-fallback'
```

- [ ] **Step 7: Run the `html` group to see it fail**

Commit (Step 8), push, and run the CI loop with `shell-scope=html`
`dev-fast=true`.
Expected: the `html-position-light` pass fails in `position-text-size`
with `The HTML position never showed the restore at 1.5.` (no
`appearanceScale` yet). The `html` reader and theme passes before it
still pass, and position-fragment and position-resize still pass. If
the failure is elsewhere (the seed, the served set, the gate), fix the
test, not the app.

- [ ] **Step 8: Commit**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add tests/fixtures/p1/html-pictures tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_install.ps1 tools/p1/windows_shell_ui_smoke.ps1
git -C $R commit -m "test(p1): T14.3 installed appearance restore phases

- html-pictures image-only fixture in seed-html-position
- position-text-size and position-text-size-fallback in html-position
- progress diagnostics gate in the position passes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: The HTML session keeps its place across a restyle

**Files:**
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs` — fields
  (after line 64), `GetLocationAsync` (line 245), `ApplyAppearanceAsync`
  and `WriteAppearanceAsync` (lines 266-311), `OnTrackerTick` (line 385),
  `RaiseLocationChanged` (line 542, a sibling after it),
  `WritePositionForTest` (line 772)
- Test: the installed `position-text-size` phase (Task 2)

**Interfaces:**
- Consumes: Task 1's `NeedsAppearanceRestore`, `AppearanceOutcome`,
  `AppearanceRestoredEventArgs`, the `AppearanceRestored` event; Task 2's
  diagnostics field names.
- Produces: `AppearanceRestored` raised once per restore, on the UI
  thread, after the scroll and the new baseline; never for a theme-only
  change, a failed write, a skipped restore or a disposed session.

The RED test is Task 2's run: `position-text-size` fails with no
`appearanceScale`.

- [ ] **Step 1: Add the fields**

After `private bool writingAppearance;` (line 64):

```csharp
    // T14.3: set while a restyle runs; the tracker and GetLocationAsync
    // leave the page alone, as during a resize.
    private bool restyling;
    // Test gate only: the last appearance restore.
    private RestoreOutcome? appearanceOutcome;
    private HtmlRestoreStep? appearanceStep;
    private double? appearanceScale;
    private int appearanceRestores;
```

- [ ] **Step 2: Keep the tracker and `GetLocationAsync` out of a restyle**

`OnTrackerTick`'s first line (385) becomes:

```csharp
        if (ticking || disposed || restoring || restyling) return;
```

`GetLocationAsync`'s comment and condition (lines 244-245) become:

```csharp
        // Mid-reflow, mid-restyle or mid-restore, the page's top isn't the reader's point.
        if (!resizing && !restoring && !restyling && await CaptureAsync() is HtmlCapture capture && capture != current)
```

- [ ] **Step 3: Restyle instead of a bare write**

Replace `ApplyAppearanceAsync` (lines 266-274) with:

```csharp
    // Before the open's write, the open picks the appearance up; during a
    // restyle, the running restyle does.
    public async Task ApplyAppearanceAsync(ReaderAppearance next, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(next);
        appearance = next;
        if (!opened || disposed || restyling) return;
        await RestyleAsync();
    }

    // T14.3: a new zoom reflows the page, so a restyle runs like a resize.
    // The point is taken first, the tracker waits, and after the last write
    // the page scrolls back to the point. The point isn't re-captured, so
    // progress keeps the true place even when the view came back by fraction.
    private async Task RestyleAsync()
    {
        if (appearance == applied) return;
        restyling = true;
        try
        {
            // A move the tracker hadn't polled yet is the reader's own.
            if (!resizing && !restoring && await CaptureAsync() is HtmlCapture capture &&
                !disposed && capture != current)
            {
                current = capture;
                WritePositionForTest();
                RaiseLocationChanged();
            }
            // A tick already in flight drops its result.
            generation++;
            tracker.Stop();
            while (!disposed)
            {
                ReaderAppearance? before = applied;
                bool written = await WriteAppearanceAsync();
                if (disposed) return;
                if (applied is ReaderAppearance now && HtmlLocationRules.NeedsAppearanceRestore(before, now))
                {
                    // An open-time restore that started during the restyle
                    // sees this and scrolls to its own target again; a
                    // pending resize's re-apply scrolls to the same point.
                    generation++;
                    if (!resizing && !restoring) await RestorePlaceAsync(now);
                }
                // A step that arrived during the restore needs another write.
                if (!written || appearance == applied) break;
            }
        }
        finally
        {
            restyling = false;
            // A pending resize starts the tracker after its re-apply.
            if (!disposed && !resizing) tracker.Start();
        }
    }

    // The offset script measures the target character's box, which lays
    // the page out at the new zoom first: that is the wait for layout.
    private async Task RestorePlaceAsync(ReaderAppearance now)
    {
        if (current is not HtmlCapture point) return;
        int started = generation;
        // A page with no text has only its fraction.
        HtmlRestoreStep step = point.Quote is null ? HtmlRestoreStep.Fraction : HtmlRestoreStep.Exact;
        HtmlRestoreTarget? landed = await ScrollToTargetAsync(new HtmlRestoreTarget(step, point.Offset, point.Fraction));
        if (disposed) return;
        HtmlScroll? scroll = HtmlLocationRules.ParseScroll(await RunScriptAsync(HtmlPositionScripts.ReadScroll));
        if (disposed) return;
        if (scroll is not null && started == generation) lastScroll = scroll;
        RestoreOutcome outcome = HtmlLocationRules.AppearanceOutcome(landed);
        appearanceOutcome = outcome;
        appearanceStep = landed?.Step;
        appearanceScale = HtmlReaderStyle.ClampScale(now.TextScale);
        appearanceRestores++;
        WritePositionForTest();
        RaiseAppearanceRestored(outcome);
    }
```

Not counted in `diagnostics.RecordRestore` (ruling 5).

- [ ] **Step 4: `WriteAppearanceAsync` reports whether the page is current**

Its comment, signature and body (lines 276-311) become:

```csharp
    // Writes until the page has the latest appearance; an unchanged one is
    // never rewritten. A failed write leaves the page as authored and isn't
    // retried until the next refresh: the style is cosmetic. True when the
    // page has the latest appearance.
    private async Task<bool> WriteAppearanceAsync()
    {
        if (writingAppearance) return false;
        writingAppearance = true;
        try
        {
            while (!disposed && !failed && appearance != applied)
            {
                ReaderAppearance next = appearance;
                double scale = HtmlReaderStyle.ClampScale(next.TextScale);
                SetPageColor(next.Theme);
                if (await RunScriptAsync(HtmlReaderStyle.WriteScript(next.Theme, scale)) != "true")
                {
                    diagnostics?.RecordAppearanceFailed();
                    WriteAppearanceForTest();
                    return false;
                }
                applied = next;
                if (diagnostics is not null)
                {
                    HtmlAppliedStyle? computed =
                        HtmlReaderStyle.ParseApplied(await RunScriptAsync(HtmlReaderStyle.ReadbackScript));
                    diagnostics.RecordAppearance(next.Theme.ToString(), scale, computed);
                    WriteAppearanceForTest();
                }
            }
            return !disposed && !failed && appearance == applied;
        }
        finally
        {
            writingAppearance = false;
        }
    }
```

`OpenAsync`'s `await WriteAppearanceAsync();` stays as it is; it ignores
the result.

- [ ] **Step 5: Raise the event and write the diagnostics**

After `RaiseLocationChanged`:

```csharp
    private void RaiseAppearanceRestored(RestoreOutcome outcome)
    {
        try
        {
            AppearanceRestored?.Invoke(this, new AppearanceRestoredEventArgs(outcome));
        }
        catch (Exception)
        {
            // A throwing handler must not break the restyle.
        }
    }
```

In `WritePositionForTest`, the serialized object becomes:

```csharp
            string json = JsonSerializer.Serialize(new
            {
                locator,
                kind = lastOutcome?.Kind.ToString(),
                step = lastStep?.ToString(),
                reason = lastOutcome?.Reason,
                appearanceKind = appearanceOutcome?.Kind.ToString(),
                appearanceStep = appearanceStep?.ToString(),
                appearanceScale,
                appearanceRestores
            });
```

Remove Task 1's `#pragma warning disable CS0067` if it was added.

- [ ] **Step 6: Build and run the unit tests**

Sync, then run the Production build and the Core tests.
Expected: the build succeeds with no new warnings; all Core tests pass.

- [ ] **Step 7: Commit and run the `html` group**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add src/DesktopGuides.Production/HtmlReaderSession.cs
git -C $R commit -m "feat(html): T14.3 keep the reading place across a text-size change

- a restyle captures, pauses the tracker, writes, scrolls back and
  re-baselines; the locator stays the pre-change point
- AppearanceRestored with the outcome; position diagnostics record it

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Push and run the CI loop with `shell-scope=html` `dev-fast=true`.
Expected: `position-text-size` passes in the light pass (MARK-0420 on top
after every burst, offset unchanged, `appearanceKind` Exact, no saves).
`position-text-size-fallback` fails with
`Expected shell status 'Text size 110%. Your place may have shifted.'`;
the shell doesn't show the notice yet (Task 4).

If `position-text-size` shows the top line drifting by more than one
line, follow the spec's Risk "Forced layout may not be enough": make the
offset scroll wait one animation frame before measuring, rerun, and
ledger the change for Task 5's notes.

### Task 4: The shell says when the place may have shifted

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.TextSize.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs:51`
- Test: the installed `position-text-size-fallback` phase (Task 2)

**Interfaces:**
- Consumes: Task 1's `TextSizeSteps.ShiftedStatus`,
  `AppearanceRestoredEventArgs.Outcome`; Task 3's event.
- Produces: the transient status
  `Text size <n>%. Your place may have shifted.` after an Approximate or
  Unavailable restore of the current session; nothing for Exact.

The RED test is Task 3's run: `position-text-size-fallback` waits for the
shifted status and times out.

- [ ] **Step 1: Subscribe with the other HTML events**

In `ShellWindow.HtmlReader.cs`, after `session.Failed += OnReaderSessionFailed;`
(line 51):

```csharp
        session.AppearanceRestored += OnAppearanceRestored;
```

- [ ] **Step 2: Join the save status and the restore (ruling 2)**

In `ShellWindow.TextSize.cs`:

- Add `using Microsoft.UI.Xaml.Controls;` after the existing usings.
- After `private Guid textSizeGuideId;`:

  ```csharp
      // T14.3: the size whose restore came back only by fraction. The save
      // and the restore can finish in either order; both show the notice.
      private double? shiftedTextScale;
  ```

- In `OnTextSizeChanged`, after `readerTextScale = scale;`:

  ```csharp
          shiftedTextScale = null;
  ```

- Its success status becomes:

  ```csharp
                  if (scale == readerTextScale)
                  {
                      ShowTransientStatus(shiftedTextScale == scale
                          ? TextSizeSteps.ShiftedStatus(scale)
                          : TextSizeSteps.Status(scale));
                  }
  ```

- Before `RevertTextSizeAsync`:

  ```csharp
      // T14.3: an HTML page that came back only by fraction says so. The
      // notice never covers a warning or an error, such as a failed save.
      private void OnAppearanceRestored(object? sender, AppearanceRestoredEventArgs args)
      {
          if (!ReferenceEquals(sender, readerSession)) return;
          if (args.Outcome.Kind == RestoreKind.Exact)
          {
              shiftedTextScale = null;
              return;
          }
          shiftedTextScale = readerTextScale;
          if (!StatusShowsProblem())
          {
              ShowTransientStatus(TextSizeSteps.ShiftedStatus(readerTextScale));
          }
      }

      private bool StatusShowsProblem() =>
          ShellStatusInfoBar.IsOpen &&
          ShellStatusInfoBar.Severity is InfoBarSeverity.Warning or InfoBarSeverity.Error;
  ```

No progress call: the session's point didn't change.

- [ ] **Step 3: Build**

Sync, then run the Production build.
Expected: it succeeds with no new warnings.

- [ ] **Step 4: Commit and run the `html` group**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add src/DesktopGuides.Production/ShellWindow.TextSize.cs src/DesktopGuides.Production/ShellWindow.HtmlReader.cs
git -C $R commit -m "feat(shell): T14.3 say when an HTML place may have shifted

- a fraction or unavailable restore shows the transient shifted status
- the save status and the restore agree in either order
- the notice never covers a warning or an error

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Push and run the CI loop with `shell-scope=html` `dev-fast=true`.
Expected: the `html` shard passes, both position passes included;
`html-place-shifted.png` is in the `production-shell-ui-html` artifact
and shows the notice.

### Task 5: Docs, evidence, full run and PR

**Files:**
- Modify: `docs/p1/t14-3-appearance-restore-design.md` (status,
  implementation notes, verification, CI runs)
- Modify: `docs/p1/implementation-plan.md:890` and the paragraph after
  the T14.1 one
- Modify: `docs/work-breakdown.md:456`, `docs/p1-technical-design.md:835`,
  `docs/p1/t14-1-text-size-design.md:299`, `docs/p1/e2e-testing.md`
- Create: `docs/p1/evidence/t14-3-appearance-restore/`

**Interfaces:**
- Consumes: the CI run ids and artifacts of Steps 1 and 3; the ledger's
  rulings.
- Produces: the PR.

- [ ] **Step 1: Full CI run**

Run the CI loop with no `shell-scope` and no `dev-fast`.
Expected: every job succeeds (core-tests, packages, production-packages,
all four production-shell-ui shards, reader-toolbar-ui, the ARM64 jobs).
A failure is debugged with superpowers:systematic-debugging before
anything else; a shard that passed on the branch before this task and
fails now is a regression, not flake, until shown otherwise.

- [ ] **Step 2: Evidence and docs**

- Download the html shard artifact and copy into
  `docs/p1/evidence/t14-3-appearance-restore/`: the `html-position-light`
  and `html-position-dark` results (JSON) and `html-place-shifted.png`
  from both passes, plus a `README.md` naming the run id, the commit and
  each file.
- The design: `Status: implemented.`; an "Implementation notes" section
  with this plan's rulings 1–12 and every ledgered ruling; a
  "Verification" section with the Core test counts, the installed phases
  and what each asserts, and the CI run ids.
- `implementation-plan.md` row 890: append "Implemented in the HTML
  session: a restyle captures first, pauses tracking and scrolls back
  after the write; TXT and PDF already keep their place; see [the
  design](t14-3-appearance-restore-design.md)." Add a T14.3 paragraph
  after the T14.1 one in the same voice.
- `work-breakdown.md:456` and `p1-technical-design.md:835`: mark T14.3
  implemented with a link to the design.
- `t14-1-text-size-design.md:299`: the Risks entry says the place across
  a size change is kept since T14.3, with the link.
- `e2e-testing.md`: a row "HTML appearance restore" after "Text size":
  `html-position` group, after `position-resize`; `position-text-size`
  (bursts 100 → 150 → 200 → 75 → 100%, MARK-0420 on top within one line,
  offset unchanged, `appearanceKind` Exact, plain status, no progress
  saves) and `position-text-size-fallback` (Picture Web Guide, middle,
  110% and back: Approximate/Fraction, locator unchanged, shifted status,
  no saves, screenshot `html-place-shifted`).

- [ ] **Step 3: Commit the docs and check the run**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add docs
git -C $R commit -m "docs(p1): T14.3 verification and evidence

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Push. A docs-only commit needs no new run; the PR names Step 1's run.

- [ ] **Step 4: Open the PR through the GitHub MCP**

`mcp__github__create_pull_request`, base `main`, head
`feat/p1-t14-3-appearance-restore`, title
`T14.3: keep the reading place across appearance changes`. Body:

- **Task:** T14.3 (TR14.1, TR14.2).
- **Prerequisites:** T08.3, T09.3, T10.3, T14.1 (#51), T14.2, all merged.
- **Outcome:** HTML keeps its context across text-size steps and never
  saves a drifted place; a fraction-only fallback says so; TXT, PDF and
  theme changes keep their place as before (existing checks named).
- **Screenshot:** `html-place-shifted.png` from the evidence folder
  (raw GitHub URL on the branch).
- **Verification:** Core tests, the two new installed phases, the full CI
  run id.
- **Rulings:** the list from the design's implementation notes.
- Ends with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

Commits, pushes, CI reruns and marking the PR ready need no approval; the
merge waits for the user.

- [ ] **Step 5: Review**

Brief a reviewer subagent with superpowers:requesting-code-review: the PR
URL, base `main`, the head commit, the spec and this plan, the Review
Focus list verbatim, and the ledger's rulings. Triage its findings with
superpowers:receiving-code-review: for each, judge whether a guide from a
major game-guide site could realistically trigger it and set its priority
from that, fix the real ones test-first, and rerun the affected shard.
