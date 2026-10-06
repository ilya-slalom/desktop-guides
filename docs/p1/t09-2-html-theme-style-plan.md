# T09.2 HTML Theme Style Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An HTML guide opens styled for the applied theme (Light keeps
the page's colors, Dark forces a dark palette, high contrast is left to
Windows) and the guide's stored text scale. It never flashes white in
Dark, and a Windows light/dark switch while reading restyles the page
without a reload or a moved reading place. Nothing new is requested.

**Architecture:**

- Core gains `ReaderTheme`, a `ReaderAppearance` that carries it, and the
  pure `HtmlReaderStyle`: the CSS, the scale clamp, the page color, the
  write and readback scripts and the readback parser.
  `HtmlSessionDiagnostics` records each application and the entry
  navigations.
- `HtmlReaderSession` writes one `<style>` element through the existing
  DevTools `Runtime.evaluate` channel. It does this at open, before the
  view is shown, and on every later `ApplyAppearanceAsync`.
- The shell resolves the reader theme, reads the guide's stored scale at
  open, and refreshes the open session from `ApplyTheme` and
  `ShellRoot.ActualThemeChanged`.
- The installed `html` group gains three theme passes on a new fixture.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.5.1), WebView2,
SQLite, xUnit, PowerShell UI Automation.

**Spec:** [t09-2-html-theme-style-design.md](t09-2-html-theme-style-design.md)

## Global Constraints

**Branch:** `feat/p1-t09-2-html-theme-style`, already checked out. The
spec is commit `e4b6201`.

**Tooling:**

- There is no local `dotnet` or `pwsh`. Builds and tests run on the Windows
  host `pcsx2-win` or in CI.
- Host sync (staging folder `E:\work\desktop-guides\t09-2`, not a git
  checkout; create it once with `ssh -o BatchMode=yes pcsx2-win "mkdir E:\work\desktop-guides\t09-2"`):

  ```bash
  R=/Users/ilya.lissoboi/work/desktop-guides
  git -C $R ls-files -co --exclude-standard -z -- . ':!.claude' |
    tar -C $R --null -T - -cf - |
    ssh -o BatchMode=yes pcsx2-win "tar -xf - -C E:\work\desktop-guides\t09-2"
  ```

- Host commands (the host's default shell is `cmd`):

  ```bash
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t09-2\tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj"
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t09-2\tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t09-2\src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t09-2\tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release"
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
- Installed runs follow [e2e-testing.md](e2e-testing.md). Every change to
  the Windows app theme goes through `Set-AppThemePreference` inside a
  `try` whose `finally` calls `Restore-AppThemePreference`. Don't switch
  on high contrast (T16.2 owns that pass). No firewall rule. If a run
  needs an elevated scheduled task, stop and ask.
- Imported HTML is untrusted. No page data enters the CSS or a script;
  the CSS enters the write script only as a `JsonSerializer` string
  literal.

**Values (verbatim from the spec):**

- `public enum ReaderTheme { Light, Dark, HighContrast }`;
  `public sealed record ReaderAppearance(ReaderTheme Theme, double TextScale);`
- `HtmlReaderStyle.ElementId = "desktop-guides-style"`, `MinScale = 0.75`,
  `MaxScale = 2.0`; `ClampScale` gives 1.0 for NaN or infinity.
- `PageColor`: Light `#FFFFFF`, Dark `#1E1E1E`, HighContrast null.
- Every theme: `html { zoom: <scale> !important; }`, scale formatted with
  the invariant culture.
- Light: `:where(html) { color-scheme: light; background-color: #FFFFFF; color: #000000; }`
- Dark palette: page `#1E1E1E`, text `#E6E6E6`, borders `#5A5A5A`, links
  `#8AB4F8`, `mark` `#5C4B00`.
- The style has no `url(`, `@import`, `@font-face` or `font-family`.
- Diagnostics JSON `appearance` object: `theme`, `scale`, `applications`,
  `failures`, `bodyBackground`, `bodyColor`, `rootZoom`.
- Installed expectations: Dark at 1.5 gives `rgb(30, 30, 30)`,
  `rgb(230, 230, 230)`, `rootZoom` `1.5`; Light gives the authored
  `rgb(255, 255, 255)` and `rgb(34, 34, 34)`, `rootZoom` `1`.

**Rulings this plan makes against the spec** (Task 5 records them in the
spec's implementation notes):

1. **A live test file.** The session diagnostics are written only at
   dispose, but the switch check must read them while the guide is open.
   With the HtmlDiagnostics gate open, the session also writes
   `diagnostics\html-appearance-<pid>.json` after each application:
   `{ "opacity": <View.Opacity>, "session": <ToJson> }`. It's written
   atomically, the way `html-position-<pid>.json` is.
2. **Entry navigations are counted.** `HtmlSessionDiagnostics` gains
   `RecordEntryNavigation()` and an `entryNavigations` field, so "the page
   doesn't reload" is a number the harness checks (1).
3. **An unchanged appearance isn't rewritten.** `ApplyAppearanceAsync`
   with the appearance last written returns at once. Windows can raise
   both `ActualThemeChanged` and `ThemeSettings.Changed` for one switch,
   and the switch check expects exactly 2 applications.
4. **Writes are serialized by a loop, not a lock.** All calls arrive on
   the UI thread. A call during a write stores the appearance and
   returns; the writer loops until the stored appearance is the one it
   wrote. A call before the open completes is picked up by the open's
   write in the same way.
5. **A failed readback still counts the application.**
   `RecordAppearance` takes `HtmlAppliedStyle?`; null leaves the three
   computed fields null. A failed *write* counts as a failure, not an
   application.
6. **The offline check runs inside the theme modes.** Each theme smoke
   mode ends with `Assert-NoRemoteConnections`; the install side asserts
   the served set equals the fixture's files and nothing was denied.
   There's no separate `html-theme-offline` mode, and the theme passes run
   in their own `Run-HtmlThemeScenarios` (no canary: the fixture
   references nothing outside its root).
7. **Seed commands.** `seed-html-theme <dataRoot> <fixtures>` imports the
   fixture into an empty library and prints `{ "guide": "<N id>" }`.
   `set-html-appearance <dataRoot> <guideId> <System|Light|Dark> <scale|default>`
   sets the stored theme and the guide's `TextScale` before each pass.
8. **The live Windows switch.** The smoke writes `AppsUseLightTheme` with
   `Set-AppThemePreference` and then broadcasts `WM_SETTINGCHANGE` with
   `ImmersiveColorSet`, through a new
   `DesktopGuidesForegroundProbe.BroadcastThemeChange()`. The install
   side's `finally` restores the user's value.
9. **The id-collision check.** The fixture has
   `<p id="desktop-guides-style">Route notes stay visible.</p>`. The
   Dark pass checks that the paragraph's text is still on the page,
   which proves the script didn't take over a non-`style` element.
10. **The open check doesn't assert "the guide's start".** A new guide has
    no saved place, so the open pass doesn't check it. The switch pass
    checks the locator is unchanged across the switch.
11. **The original page color is kept.** HighContrast sets
    `DefaultBackgroundColor` back to the value the view had when the
    session was created. Without that, a Dark-to-HighContrast change
    would keep `#1E1E1E`.

## Review Focus

1. **The view stays invisible.** If the open's write throws or hangs, or
   the open fails half-way, `Opacity` must still return to 1. Task 4 sets
   it in `OpenAsync`'s `finally`. The open pass asserts `opacity` 1, and a
   Core test pins `ParseApplied(null)`.
2. **A theme change during the open.** A Windows switch that lands while
   `OpenEntryAsync` is still navigating must end with the latest theme on
   the page. Rulings 3 and 4 cover it. The switch pass waits for the first
   application before switching, so review the loop by reading it: the
   last stored appearance always gets written.
3. **A scale out of range in storage.** A row written before the range
   check, or edited by hand, can hold 5 or NaN. `ClampScale` bounds it.
   Task 1 tests 0.74, 2.01, NaN and both infinities.
4. **A page that already uses the id.** A non-`style` element with id
   `desktop-guides-style` must keep its content (ruling 9, fixture and
   Dark pass).
5. **A culture with a decimal comma.** Under `de-DE`, `1.5` must still
   print as `1.5`, not `1,5`, which CSS would drop. Task 1 tests it.

## File Map

| File | Change |
| --- | --- |
| `src/DesktopGuides.Core/Reading/ReaderContract.cs` | `ReaderTheme`; `ReaderAppearance` carries it |
| `src/DesktopGuides.Core/Html/HtmlReaderStyle.cs` | new: CSS, clamp, page color, scripts, parser, `HtmlAppliedStyle` |
| `src/DesktopGuides.Core/Html/HtmlSessionDiagnostics.cs` | appearance record, failures, entry navigations |
| `tests/DesktopGuides.Core.Tests/HtmlReaderStyleTests.cs` | new |
| `tests/DesktopGuides.Core.Tests/HtmlSessionDiagnosticsTests.cs` | appearance and navigation tests |
| `tests/fixtures/p1/html-theme/` | new fixture: `guide.html`, `style.css`, `images/route.png` |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | `seed-html-theme`, `set-html-appearance` |
| `tools/p1/windows_shell_foreground_probe.cs` | `BroadcastThemeChange()` |
| `tools/p1/windows_shell_ui_smoke.ps1` | modes `html-theme-open`, `html-theme-light`, `html-theme-switch` |
| `tools/p1/windows_shell_install.ps1` | `Invoke-HtmlThemePass`, `Assert-HtmlThemePass`, `Run-HtmlThemeScenarios` |
| `src/DesktopGuides.Production/HtmlReaderSession.cs` | the style write, opacity, page color, test file |
| `src/DesktopGuides.Production/ShellWindow.Theme.cs` | `ReaderThemeNow`, `RefreshReaderAppearance` |
| `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs` | the stored scale; `OpenAsync` gets the appearance |
| `docs/p1/*`, `docs/work-breakdown.md` | Task 5 |

---

### Task 1: Core `ReaderTheme` and `HtmlReaderStyle`

**Files:**
- Modify: `src/DesktopGuides.Core/Reading/ReaderContract.cs:91`
- Create: `src/DesktopGuides.Core/Html/HtmlReaderStyle.cs`
- Test: `tests/DesktopGuides.Core.Tests/HtmlReaderStyleTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `public enum ReaderTheme { Light, Dark, HighContrast }` (namespace
    `DesktopGuides.Core.Reading`);
  - `public sealed record ReaderAppearance(ReaderTheme Theme, double TextScale);`;
  - `public sealed record HtmlAppliedStyle(string BodyBackground, string BodyColor, string RootZoom);`
    (namespace `DesktopGuides.Core.Html`);
  - `HtmlReaderStyle.ElementId`, `MinScale`, `MaxScale`,
    `double ClampScale(double)`, `string Css(ReaderTheme, double)`,
    `string? PageColor(ReaderTheme)`, `string WriteScript(ReaderTheme, double)`,
    `const string ReadbackScript`, `HtmlAppliedStyle? ParseApplied(string?)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/HtmlReaderStyleTests.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlReaderStyleTests
{
    [Theory]
    [InlineData(0.74, 0.75)]
    [InlineData(0.75, 0.75)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.5)]
    [InlineData(2.0, 2.0)]
    [InlineData(2.01, 2.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 1.0)]
    public void ClampScaleBoundsTheStoredScale(double stored, double expected) =>
        Assert.Equal(expected, HtmlReaderStyle.ClampScale(stored));

    [Fact]
    public void LightOnlySetsZeroSpecificityDefaults() =>
        Assert.Equal(
            "html { zoom: 1 !important; }\n" +
            ":where(html) { color-scheme: light; background-color: #FFFFFF; color: #000000; }",
            HtmlReaderStyle.Css(ReaderTheme.Light, 1.0));

    [Fact]
    public void DarkForcesThePaletteAndKeepsImages() =>
        Assert.Equal(
            "html { zoom: 1.5 !important; }\n" +
            ":root { color-scheme: dark !important; }\n" +
            "html, body { background-color: #1E1E1E !important; color: #E6E6E6 !important; }\n" +
            "body * { background-color: transparent !important; color: inherit !important; " +
            "border-color: #5A5A5A !important; text-shadow: none !important; }\n" +
            "a:link, a:visited, a:link *, a:visited * { color: #8AB4F8 !important; }\n" +
            "mark { background-color: #5C4B00 !important; }",
            HtmlReaderStyle.Css(ReaderTheme.Dark, 1.5));

    [Fact]
    public void HighContrastOnlyScales() =>
        Assert.Equal("html { zoom: 0.75 !important; }", HtmlReaderStyle.Css(ReaderTheme.HighContrast, 0.75));

    [Fact]
    public void CssClampsTheScale() =>
        Assert.StartsWith("html { zoom: 2 !important; }", HtmlReaderStyle.Css(ReaderTheme.Dark, 9));

    [Fact]
    public void ZoomIgnoresTheCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.StartsWith("html { zoom: 1.25 !important; }", HtmlReaderStyle.Css(ReaderTheme.Light, 1.25));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(ReaderTheme.Light)]
    [InlineData(ReaderTheme.Dark)]
    [InlineData(ReaderTheme.HighContrast)]
    public void NoThemeCanMakeARequest(ReaderTheme theme)
    {
        string css = HtmlReaderStyle.Css(theme, 1.0);
        foreach (string token in new[] { "url(", "@import", "@font-face", "font-family" })
        {
            Assert.DoesNotContain(token, css, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(ReaderTheme.Light, "#FFFFFF")]
    [InlineData(ReaderTheme.Dark, "#1E1E1E")]
    [InlineData(ReaderTheme.HighContrast, null)]
    public void PageColorMatchesThePage(ReaderTheme theme, string? expected) =>
        Assert.Equal(expected, HtmlReaderStyle.PageColor(theme));

    [Fact]
    public void WriteScriptCarriesTheCssAsAJsonLiteral()
    {
        string script = HtmlReaderStyle.WriteScript(ReaderTheme.Dark, 1.5);
        string literal = JsonSerializer.Serialize(HtmlReaderStyle.Css(ReaderTheme.Dark, 1.5));

        Assert.Equal(
            "(() => { const css = " + literal + "; " +
            "let style = document.querySelector(\"style#desktop-guides-style\"); " +
            "if (!style) { style = document.createElement(\"style\"); style.id = \"desktop-guides-style\"; " +
            "(document.head || document.documentElement).appendChild(style); } " +
            "style.textContent = css; return true; })()",
            script);
    }

    [Fact]
    public void ParseAppliedReadsTheComputedValues() =>
        Assert.Equal(
            new HtmlAppliedStyle("rgb(30, 30, 30)", "rgb(230, 230, 230)", "1.5"),
            HtmlReaderStyle.ParseApplied(
                """{"bodyBackground":"rgb(30, 30, 30)","bodyColor":"rgb(230, 230, 230)","rootZoom":"1.5"}"""));

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("not json")]
    [InlineData("""{"bodyBackground":"rgb(0, 0, 0)","bodyColor":"rgb(0, 0, 0)"}""")]
    [InlineData("""{"bodyBackground":1,"bodyColor":"rgb(0, 0, 0)","rootZoom":"1"}""")]
    public void ParseAppliedRejectsAnythingElse(string? reply) =>
        Assert.Null(HtmlReaderStyle.ParseApplied(reply));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Sync, then run
`ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t09-2\tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj --filter FullyQualifiedName~DesktopGuides.Core.Tests.HtmlReaderStyleTests"`.
Expected: the build fails with CS0103 `The name 'HtmlReaderStyle' does not exist` and CS0246 `ReaderTheme`.

- [ ] **Step 3: Change the contract**

In `src/DesktopGuides.Core/Reading/ReaderContract.cs`, replace line 91
(`public sealed record ReaderAppearance(ThemePreference Theme, double TextScale);`)
with:

```csharp
// The theme a reader paints with. The shell resolves System before it
// calls a reader, so a reader never reads the Windows theme itself.
public enum ReaderTheme { Light, Dark, HighContrast }

public sealed record ReaderAppearance(ReaderTheme Theme, double TextScale);
```

If `ThemePreference` is now unused in that file, remove the `using` that
brought it in. Nothing calls `ApplyAppearanceAsync` yet. The three
sessions only pass the parameter through, so they compile unchanged.

- [ ] **Step 4: Write `HtmlReaderStyle`**

Create `src/DesktopGuides.Core/Html/HtmlReaderStyle.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Html;

public sealed record HtmlAppliedStyle(string BodyBackground, string BodyColor, string RootZoom);

/// <summary>
/// The app's fixed style for HTML guides. It's built from constants and a
/// clamped scale only, so no page data enters it, and it names no URL or
/// font, so it can't make a request.
/// </summary>
public static class HtmlReaderStyle
{
    public const string ElementId = "desktop-guides-style";
    public const double MinScale = 0.75;
    public const double MaxScale = 2.0;

    // Zero specificity: any rule the page sets wins.
    private const string Light =
        ":where(html) { color-scheme: light; background-color: #FFFFFF; color: #000000; }";

    // Background images, media, layout and fonts stay as authored.
    private const string Dark =
        ":root { color-scheme: dark !important; }\n" +
        "html, body { background-color: #1E1E1E !important; color: #E6E6E6 !important; }\n" +
        "body * { background-color: transparent !important; color: inherit !important; " +
        "border-color: #5A5A5A !important; text-shadow: none !important; }\n" +
        "a:link, a:visited, a:link *, a:visited * { color: #8AB4F8 !important; }\n" +
        "mark { background-color: #5C4B00 !important; }";

    // A stored scale can predate the range check; a bad one reads as 1.
    public static double ClampScale(double scale) =>
        double.IsFinite(scale) ? Math.Clamp(scale, MinScale, MaxScale) : 1.0;

    // High contrast sets no colors: WebView2 applies Windows' forced colors.
    public static string Css(ReaderTheme theme, double scale)
    {
        string zoom = "html { zoom: " +
            ClampScale(scale).ToString("0.###", CultureInfo.InvariantCulture) + " !important; }";
        return theme switch
        {
            ReaderTheme.Light => zoom + "\n" + Light,
            ReaderTheme.Dark => zoom + "\n" + Dark,
            _ => zoom
        };
    }

    // The WebView's color before the first paint; null keeps its default.
    public static string? PageColor(ReaderTheme theme) => theme switch
    {
        ReaderTheme.Light => "#FFFFFF",
        ReaderTheme.Dark => "#1E1E1E",
        _ => null
    };

    // Creates the element on the first write and rewrites it after that.
    // The lookup names the tag, so a page element that has the same id is
    // never emptied.
    public static string WriteScript(ReaderTheme theme, double scale) =>
        "(() => { const css = " + JsonSerializer.Serialize(Css(theme, scale)) + "; " +
        "let style = document.querySelector(\"style#" + ElementId + "\"); " +
        "if (!style) { style = document.createElement(\"style\"); style.id = \"" + ElementId + "\"; " +
        "(document.head || document.documentElement).appendChild(style); } " +
        "style.textContent = css; return true; })()";

    // Test diagnostics: what the page computed after a write.
    public const string ReadbackScript =
        "(() => { const body = getComputedStyle(document.body || document.documentElement); " +
        "const root = getComputedStyle(document.documentElement); " +
        "return { bodyBackground: body.backgroundColor, bodyColor: body.color, rootZoom: root.zoom }; })()";

    public static HtmlAppliedStyle? ParseApplied(string? json)
    {
        if (json is null) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   Text(root, "bodyBackground") is string background &&
                   Text(root, "bodyColor") is string color &&
                   Text(root, "rootZoom") is string zoom
                ? new HtmlAppliedStyle(background, color, zoom)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Sync, then run the Step 2 command.
Expected: PASS, 28 tests in `HtmlReaderStyleTests` (9 + 1 + 1 + 1 + 1 + 1 + 3 + 3 + 1 + 1 + 6).

Then run the whole Core and Infrastructure suites (host commands above)
and build Production.
Expected: all pass. Core is 807 + 28 = 835 tests, unless main has moved
(then record the new count). Infrastructure is unchanged. Production
builds with 0 errors.

- [ ] **Step 6: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core/Reading/ReaderContract.cs src/DesktopGuides.Core/Html/HtmlReaderStyle.cs tests/DesktopGuides.Core.Tests/HtmlReaderStyleTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(core): T09.2 reader theme and the HTML guide style

- ReaderTheme {Light, Dark, HighContrast}; ReaderAppearance carries it
- HtmlReaderStyle: fixed CSS per theme, zoom scale clamped to 0.75-2
  and formatted invariantly, page color, write and readback scripts

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Core diagnostics: appearance and entry navigations

**Files:**
- Modify: `src/DesktopGuides.Core/Html/HtmlSessionDiagnostics.cs`
- Test: `tests/DesktopGuides.Core.Tests/HtmlSessionDiagnosticsTests.cs`

**Interfaces:**
- Consumes: `HtmlAppliedStyle` (Task 1).
- Produces:
  - `void RecordAppearance(string theme, double scale, HtmlAppliedStyle? computed)`;
  - `void RecordAppearanceFailed()`;
  - `void RecordEntryNavigation()`;
  - `ToJson` adds `entryNavigations` (int) and `appearance`
    `{ theme, scale, applications, failures, bodyBackground, bodyColor, rootZoom }`.
    Before any application, `theme`, `scale` and the computed fields are
    null and the counts are 0.

- [ ] **Step 1: Write the failing tests**

Append to the `HtmlSessionDiagnosticsTests` class:

```csharp
    [Fact]
    public void RecordsTheLastAppearanceAndCountsApplications()
    {
        HtmlSessionDiagnostics diagnostics = new();
        diagnostics.RecordAppearance("Light", 1.0, new HtmlAppliedStyle("rgb(255, 255, 255)", "rgb(34, 34, 34)", "1"));
        diagnostics.RecordAppearanceFailed();
        diagnostics.RecordAppearance("Dark", 1.5, new HtmlAppliedStyle("rgb(30, 30, 30)", "rgb(230, 230, 230)", "1.5"));

        using JsonDocument json = JsonDocument.Parse(diagnostics.ToJson(Guid.NewGuid()));
        JsonElement appearance = json.RootElement.GetProperty("appearance");

        Assert.Equal("Dark", appearance.GetProperty("theme").GetString());
        Assert.Equal(1.5, appearance.GetProperty("scale").GetDouble());
        Assert.Equal(2, appearance.GetProperty("applications").GetInt32());
        Assert.Equal(1, appearance.GetProperty("failures").GetInt32());
        Assert.Equal("rgb(30, 30, 30)", appearance.GetProperty("bodyBackground").GetString());
        Assert.Equal("rgb(230, 230, 230)", appearance.GetProperty("bodyColor").GetString());
        Assert.Equal("1.5", appearance.GetProperty("rootZoom").GetString());
    }

    [Fact]
    public void AnApplicationWithoutAReadbackHasNoComputedValues()
    {
        HtmlSessionDiagnostics diagnostics = new();
        diagnostics.RecordAppearance("Dark", 1.0, new HtmlAppliedStyle("rgb(30, 30, 30)", "rgb(230, 230, 230)", "1"));
        diagnostics.RecordAppearance("Light", 1.0, null);

        using JsonDocument json = JsonDocument.Parse(diagnostics.ToJson(Guid.NewGuid()));
        JsonElement appearance = json.RootElement.GetProperty("appearance");

        Assert.Equal(2, appearance.GetProperty("applications").GetInt32());
        Assert.Equal(JsonValueKind.Null, appearance.GetProperty("bodyBackground").ValueKind);
        Assert.Equal(JsonValueKind.Null, appearance.GetProperty("bodyColor").ValueKind);
        Assert.Equal(JsonValueKind.Null, appearance.GetProperty("rootZoom").ValueKind);
    }

    [Fact]
    public void ANewSessionHasNoAppearanceAndNoNavigations()
    {
        using JsonDocument json = JsonDocument.Parse(new HtmlSessionDiagnostics().ToJson(Guid.NewGuid()));
        JsonElement appearance = json.RootElement.GetProperty("appearance");

        Assert.Equal(0, json.RootElement.GetProperty("entryNavigations").GetInt32());
        Assert.Equal(JsonValueKind.Null, appearance.GetProperty("theme").ValueKind);
        Assert.Equal(JsonValueKind.Null, appearance.GetProperty("scale").ValueKind);
        Assert.Equal(0, appearance.GetProperty("applications").GetInt32());
        Assert.Equal(0, appearance.GetProperty("failures").GetInt32());
    }

    [Fact]
    public void CountsEntryNavigations()
    {
        HtmlSessionDiagnostics diagnostics = new();
        diagnostics.RecordEntryNavigation();
        diagnostics.RecordEntryNavigation();

        using JsonDocument json = JsonDocument.Parse(diagnostics.ToJson(Guid.NewGuid()));

        Assert.Equal(2, json.RootElement.GetProperty("entryNavigations").GetInt32());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Sync, then run the Core suite filtered to
`DesktopGuides.Core.Tests.HtmlSessionDiagnosticsTests`.
Expected: the build fails with CS1061, `'HtmlSessionDiagnostics' does not
contain a definition for 'RecordAppearance'`.

- [ ] **Step 3: Implement**

In `HtmlSessionDiagnostics`, add the fields after `rejectedCaptures`:

```csharp
    private int entryNavigations;
    private string? appearanceTheme;
    private double? appearanceScale;
    private int appearanceApplications;
    private int appearanceFailures;
    private HtmlAppliedStyle? appearanceComputed;
```

add the methods after `RecordRestore`:

```csharp
    public void RecordEntryNavigation()
    {
        lock (gate) entryNavigations++;
    }

    // computed is null when the readback after the write failed.
    public void RecordAppearance(string theme, double scale, HtmlAppliedStyle? computed)
    {
        lock (gate)
        {
            appearanceTheme = theme;
            appearanceScale = scale;
            appearanceApplications++;
            appearanceComputed = computed;
        }
    }

    public void RecordAppearanceFailed()
    {
        lock (gate) appearanceFailures++;
    }
```

and in `ToJson`, after `restores = new Dictionary<string, int>(restores)`,
add (with a comma after the `restores` line):

```csharp
                entryNavigations,
                appearance = new
                {
                    theme = appearanceTheme,
                    scale = appearanceScale,
                    applications = appearanceApplications,
                    failures = appearanceFailures,
                    bodyBackground = appearanceComputed?.BodyBackground,
                    bodyColor = appearanceComputed?.BodyColor,
                    rootZoom = appearanceComputed?.RootZoom
                }
```

- [ ] **Step 4: Run the tests to verify they pass**

Sync, then run the Step 2 command.
Expected: PASS, including the existing `HtmlSessionDiagnosticsTests`.
Then run the whole Core suite.
Expected: 835 + 4 = 839 tests pass (or the Task 1 count + 4).

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core/Html/HtmlSessionDiagnostics.cs tests/DesktopGuides.Core.Tests/HtmlSessionDiagnosticsTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(core): T09.2 HTML diagnostics record the applied style

- RecordAppearance keeps the last theme, scale and computed values and
  counts applications; RecordAppearanceFailed counts failed writes
- RecordEntryNavigation counts entry loads, to prove no reload

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: Installed theme passes (the failing test)

The harness lands first and runs red in CI. The app still renders guides
as authored, so the open pass fails on its first appearance wait.

**Files:**
- Create: `tests/fixtures/p1/html-theme/guide.html`,
  `tests/fixtures/p1/html-theme/style.css`,
  `tests/fixtures/p1/html-theme/images/route.png` (a copy of
  `tests/fixtures/p1/html-long/images/route.png`)
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (after the
  `seed-html-position` block)
- Modify: `tools/p1/windows_shell_foreground_probe.cs`
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (mode set at :3-20, game
  mapping at :1227-1235, helpers beside `Read-HtmlPosition` at :1404, mode
  branch after `html-position`)
- Modify: `tools/p1/windows_shell_install.ps1` (functions after
  `Run-HtmlPositionScenarios`, group dispatch at :2336)

**Interfaces:**
- Consumes: the diagnostics JSON fields from Task 2. Ruling 1's test file
  `diagnostics\html-appearance-<pid>.json` is
  `{ "opacity": <double>, "session": <HtmlSessionDiagnostics.ToJson> }`.
  Task 4 writes it.
- Produces: smoke modes `html-theme-open`, `html-theme-light` and
  `html-theme-switch`; seed commands `seed-html-theme` and
  `set-html-appearance`; `Run-HtmlThemeScenarios` in the `html` group.

- [ ] **Step 1: Add the fixture**

`tests/fixtures/p1/html-theme/style.css`:

```css
body { background: #FFFFFF; color: #222222; font-size: 15px; margin: 24px; }
table { border-collapse: collapse; }
th { background-color: #DDE6F0; color: #1A2B3C; border: 1px solid #9AA8B8; padding: 4px 8px; }
td { border: 1px solid #C8C8C8; padding: 4px 8px; }
```

`tests/fixtures/p1/html-theme/guide.html`:

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Theme Web Guide</title>
<link rel="stylesheet" href="style.css">
</head>
<body>
<h1>Theme Web Guide</h1>
<p><a href="#route">Jump to the route</a></p>
<p id="desktop-guides-style">Route notes stay visible.</p>
<table>
<tr><th>Area</th><th>Item</th></tr>
<tr><td>Forest gate</td><td style="background-color: #FFFFCC">Silver key</td></tr>
<tr><td>Old mill</td><td>Lantern</td></tr>
</table>
<h2 id="route">Route</h2>
<p><img src="images/route.png" alt="Route map"></p>
<p>Take the silver key to the old mill. <mark>Don't skip the lantern.</mark></p>
</body>
</html>
```

```bash
mkdir -p /Users/ilya.lissoboi/work/desktop-guides/tests/fixtures/p1/html-theme/images
cp /Users/ilya.lissoboi/work/desktop-guides/tests/fixtures/p1/html-long/images/route.png /Users/ilya.lissoboi/work/desktop-guides/tests/fixtures/p1/html-theme/images/route.png
```

- [ ] **Step 2: Add the seed commands**

In `tools/p1/DesktopGuides.ShellSeed/Program.cs`, after the
`seed-html-position` block's closing `}`, add:

```csharp
if (args.Length == 3 && args[0] == "seed-html-theme")
{
    // One styled guide, imported as a user would. T09.2's passes set its
    // stored theme and scale with set-html-appearance.
    ManagedPathResolver themePaths = new(args[1]);
    await using SqliteLibraryRepository themeLibrary = new(themePaths);
    await themeLibrary.InitializeAsync();
    if ((await themeLibrary.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The HTML theme seed needs an empty library.");
    }
    string themeEntry = Path.Combine(Path.GetFullPath(args[2]), "p1", "html-theme", "guide.html");
    Game themeGame = await themeLibrary.AddGameAsync("Web Theme Game", null, null);
    ImportInspection themeInspection = await new GuideImportValidator().InspectAsync(themeEntry, CancellationToken.None);
    if (themeInspection is not ImportReady themeReady)
    {
        throw new InvalidOperationException($"The theme guide failed the import preview: {themeInspection}.");
    }
    Guid themeGuide = await new GuideImportPublisher(themeLibrary, themePaths).PublishAsync(
        themeReady.Manifest, themeGame.Id, "Theme Web Guide", false, null, CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(new { guide = themeGuide.ToString("N") }));
    return 0;
}

if (args.Length == 5 && args[0] == "set-html-appearance")
{
    if (!Enum.TryParse(args[3], false, out ThemePreference storedTheme) ||
        !Enum.IsDefined(storedTheme) || storedTheme.ToString() != args[3])
    {
        Console.Error.WriteLine("Theme must be System, Light, or Dark.");
        return 2;
    }
    double? storedScale = args[4] == "default"
        ? null
        : double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
    Guid appearanceGuide = Guid.ParseExact(args[2], "N");
    await using SqliteLibraryRepository appearanceLibrary = new(new ManagedPathResolver(args[1]));
    await appearanceLibrary.InitializeAsync();
    await appearanceLibrary.UpdateSettingsAsync(settings => settings with { Theme = storedTheme });
    await appearanceLibrary.SaveReaderPreferencesAsync(appearanceGuide, storedScale);
    // The save is an UPDATE: a guide without a preferences row would keep no scale.
    if ((await appearanceLibrary.GetReaderPreferencesAsync(appearanceGuide))?.TextScale != storedScale)
    {
        throw new InvalidOperationException("The guide's text scale did not save.");
    }
    return 0;
}
```

Add `using DesktopGuides.Core.Library;` at the top only if
`ThemePreference` doesn't resolve (`set-material` already uses
`WindowMaterial` from the same namespace, so it should).

Build ShellSeed on the host.
Expected: 0 errors.

- [ ] **Step 3: Add the broadcast helper**

In `tools/p1/windows_shell_foreground_probe.cs`, add beside the other
`DllImport`s:

```csharp
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window, uint message, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);
```

and this public method in the class:

```csharp
    // What Settings sends after it changes AppsUseLightTheme, so running
    // apps re-read the app theme. HWND_BROADCAST, WM_SETTINGCHANGE,
    // SMTO_ABORTIFHUNG.
    public static bool BroadcastThemeChange()
    {
        IntPtr result;
        return SendMessageTimeout(
            new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet",
            0x0002, 5000, out result) != IntPtr.Zero;
    }
```

- [ ] **Step 4: Add the smoke modes**

In `tools/p1/windows_shell_ui_smoke.ps1`:

1. In the `ValidateSet`, after `'html-position',` add
   `'html-theme-open', 'html-theme-light', 'html-theme-switch',`.
2. In the reader-mode list at :1227, after `'html-position',` add the
   same three names. In the `$textGame` mapping, after the
   `html-position` line add
   `elseif ($Mode -like 'html-theme-*') { 'Web Theme Game' }`.
3. After the `Wait-HtmlPosition` function, add:

```powershell
        # The app's applied style, written after each application while the
        # HtmlDiagnostics gate is open (T09.2).
        function Read-HtmlAppearance {
            $path = Join-Path $AppCacheRoot "diagnostics\html-appearance-$ProcessId.json"
            if (-not (Test-Path -LiteralPath $path)) { return $null }
            try {
                return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
            }
            catch {
                return $null
            }
        }

        function Wait-HtmlAppearance([int] $applications, [string] $what) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $file = Read-HtmlAppearance
                # The open writes the file once mid-open (opacity 0) and again
                # once the view is shown; a view that stays hidden times out here.
                if ($file -and [int] $file.session.appearance.applications -ge $applications -and
                    [double] $file.opacity -eq 1) { return $file }
                Start-Sleep -Milliseconds 250
            } while ((Get-Date) -lt $deadline)
            [void](Save-WindowScreenshot 'html-appearance-missing')
            throw "The app applied no style for $what (file: $(Read-HtmlAppearance | ConvertTo-Json -Compress -Depth 5))."
        }

        function Assert-HtmlAppearance($file, [string] $theme, [int] $applications,
            [string] $background, [string] $color, [string] $zoom, [string] $what) {
            $a = $file.session.appearance
            $seen = "theme=$($a.theme) applications=$($a.applications) failures=$($a.failures) " +
                "background=$($a.bodyBackground) color=$($a.bodyColor) zoom=$($a.rootZoom) opacity=$($file.opacity)"
            if ($a.theme -cne $theme -or [int] $a.applications -ne $applications -or [int] $a.failures -ne 0 -or
                $a.bodyBackground -cne $background -or $a.bodyColor -cne $color -or $a.rootZoom -cne $zoom -or
                [double] $file.opacity -ne 1) {
                throw "After $what the app reported $seen; expected theme=$theme applications=$applications " +
                    "failures=0 background=$background color=$color zoom=$zoom opacity=1."
            }
            return $seen
        }
```

4. After the `html-position` branch, add:

```powershell
        elseif ($Mode -in @('html-theme-open', 'html-theme-light')) {
            if (-not $AppCacheRoot -or -not $AppDataRoot) {
                throw "$Mode needs -AppDataRoot and -AppCacheRoot."
            }
            [void](Wait-Name 'LibraryHeading' 'Library')
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')
            Open-TextGuide 'Theme Web Guide'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Theme Web Guide')
            $applied = Wait-HtmlAppearance 1 'the open'
            if ($Mode -eq 'html-theme-open') {
                # Dark is stored and the scale is 1.5; Windows is light.
                $report.htmlAppearance = Assert-HtmlAppearance $applied 'Dark' 1 `
                    'rgb(30, 30, 30)' 'rgb(230, 230, 230)' '1.5' 'a Dark open at 1.5'
                # The page's own element with the style's id keeps its text.
                [void](Wait-PageName 'Route notes stay visible.')
                $report.htmlThemeScreenshot = Save-WindowScreenshot 'html-theme-dark-1.5'
            }
            else {
                # Light is stored and no scale; Windows is dark.
                $report.htmlAppearance = Assert-HtmlAppearance $applied 'Light' 1 `
                    'rgb(255, 255, 255)' 'rgb(34, 34, 34)' '1' 'a Light open'
                $report.htmlThemeScreenshot = Save-WindowScreenshot 'html-theme-light'
            }
            Assert-NoRemoteConnections 'themed HTML guide'
            Back-ToTextGame
            $report.phases += $Mode
        }
        elseif ($Mode -eq 'html-theme-switch') {
            if (-not $AppCacheRoot -or -not $AppDataRoot) {
                throw 'html-theme-switch needs -AppDataRoot and -AppCacheRoot.'
            }
            . (Join-Path $PSScriptRoot 'windows_shell_theme_preference.ps1')
            [void](Wait-Name 'LibraryHeading' 'Library')
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')
            Open-TextGuide 'Theme Web Guide'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Theme Web Guide')
            # System is stored and Windows is light.
            $before = Wait-HtmlAppearance 1 'the open'
            $report.htmlAppearanceBefore = Assert-HtmlAppearance $before 'Light' 1 `
                'rgb(255, 255, 255)' 'rgb(34, 34, 34)' '1' 'a System open with Windows light'
            [void](Wait-HtmlPosition { param($p) $p.locator } 'a first capture')
            $placeBefore = (Read-HtmlPosition).locator
            $report.htmlThemeLightScreenshot = Save-WindowScreenshot 'html-theme-system-light'

            # The install side restores the user's value in its finally.
            Set-AppThemePreference $false
            $report.themeBroadcast = [DesktopGuidesForegroundProbe]::BroadcastThemeChange()
            $after = Wait-HtmlAppearance 2 'the Windows dark switch'
            $report.htmlAppearanceAfter = Assert-HtmlAppearance $after 'Dark' 2 `
                'rgb(30, 30, 30)' 'rgb(230, 230, 230)' '1' 'the Windows dark switch'
            # No reload and no new request: the same entry load and the same files.
            $servedBefore = @($before.session.served) -join ','
            $servedAfter = @($after.session.served) -join ','
            if ($servedAfter -cne $servedBefore -or @($after.session.denied).Count -ne @($before.session.denied).Count -or
                [int] $after.session.entryNavigations -ne 1) {
                throw "The switch changed the session: served '$servedBefore' to '$servedAfter', " +
                    "denied $(@($before.session.denied).Count) to $(@($after.session.denied).Count), " +
                    "entry navigations $($after.session.entryNavigations)."
            }
            # Two polls past the switch, the reading place is the same.
            Start-Sleep -Milliseconds 1200
            $placeAfter = (Read-HtmlPosition).locator
            if ($placeAfter -cne $placeBefore) {
                throw "The switch moved the reading place from $placeBefore to $placeAfter."
            }
            $report.htmlThemeDarkScreenshot = Save-WindowScreenshot 'html-theme-system-dark'
            Assert-NoRemoteConnections 'themed HTML guide'
            Back-ToTextGame
            $report.phases += $Mode
        }
```

- [ ] **Step 5: Add the install passes**

In `tools/p1/windows_shell_install.ps1`, after `Run-HtmlPositionScenarios`,
add:

```powershell
function Invoke-HtmlThemePass([string] $mode) {
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $gates = @(
        foreach ($name in @('HtmlDiagnostics', 'HtmlPosition', 'ProgressOverride')) {
            [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\DesktopGuides.Preview.$name.$processId")
        })
    try {
        $result = Run-ShellSmoke $mode -ResultName $mode `
            -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        Close-InstalledShell
        return $result
    }
    finally {
        foreach ($gate in $gates) { $gate.Dispose() }
    }
}

function Assert-HtmlThemePass($pass, [string] $diagnostics) {
    # One session, one entry load, the fixture's own files and nothing
    # denied: the style adds no request (TR14.2).
    $files = @(Get-ChildItem -LiteralPath $diagnostics -Filter 'html-session-*.json' -ErrorAction SilentlyContinue)
    if ($files.Count -ne 1) {
        throw "The $($pass.mode) pass wrote $($files.Count) HTML session diagnostics; expected 1."
    }
    $session = Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
    $served = @($session.served) -join ','
    if ($served -cne 'guide.html,images/route.png,style.css') {
        throw "The $($pass.mode) pass served '$served'."
    }
    if (@($session.denied).Count -ne 0) {
        throw "The $($pass.mode) pass denied $(@($session.denied) | ConvertTo-Json -Compress)."
    }
    $a = $session.appearance
    if ([int] $session.entryNavigations -ne 1 -or $a.theme -cne $pass.theme -or
        [int] $a.applications -ne $pass.applications -or [int] $a.failures -ne 0) {
        throw "The $($pass.mode) pass recorded entry navigations $($session.entryNavigations), " +
            "theme $($a.theme), applications $($a.applications), failures $($a.failures)."
    }
    return $session
}

function Run-HtmlThemeScenarios {
    # T09.2: the stored Dark theme and scale at open (Windows light), the
    # stored Light theme keeps the page's colors (Windows dark), and a
    # Windows switch restyles an open guide that follows System.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    $ids = Invoke-ShellSeed @('seed-html-theme', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    $report.htmlTheme = [ordered]@{ guide = $ids.guide }
    $originalTheme = Get-AppThemePreference
    try {
        foreach ($pass in @(
                @{ mode = 'html-theme-open'; stored = 'Dark'; scale = '1.5'; light = $true; theme = 'Dark'; applications = 1 },
                @{ mode = 'html-theme-light'; stored = 'Light'; scale = 'default'; light = $false; theme = 'Light'; applications = 1 },
                @{ mode = 'html-theme-switch'; stored = 'System'; scale = 'default'; light = $true; theme = 'Dark'; applications = 2 })) {
            Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
            Invoke-ShellSeed @('set-html-appearance', $dataRoot, $ids.guide, $pass.stored, $pass.scale) | Out-Null
            Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
            Set-AppThemePreference $pass.light
            try {
                $report.htmlTheme[$pass.mode] = Invoke-HtmlThemePass $pass.mode
            }
            finally {
                Save-HtmlDiagnostics $pass.mode (Get-HtmlCacheRoot)
            }
            $report.htmlTheme["$($pass.mode)-session"] = Assert-HtmlThemePass $pass $diagnostics
        }
    }
    finally {
        Restore-AppThemePreference $originalTheme
        Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    }
}
```

In the group dispatch, change the `html` block to:

```powershell
    if (Enter-ScenarioGroup 'html') {
        Run-HtmlReaderScenarios
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Run-HtmlPositionScenarios
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Run-HtmlThemeScenarios
    }
```

`Save-HtmlDiagnostics` already copies every `*.json` in the folder, so
the live `html-appearance-<pid>.json` reaches the artifact too.

- [ ] **Step 6: Check the scripts are ASCII and commit**

```bash
LC_ALL=C grep -n '[^ -~	]' /Users/ilya.lissoboi/work/desktop-guides/tools/p1/windows_shell_ui_smoke.ps1 /Users/ilya.lissoboi/work/desktop-guides/tools/p1/windows_shell_install.ps1 || echo ascii-ok
git -C /Users/ilya.lissoboi/work/desktop-guides add tests/fixtures/p1/html-theme tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_foreground_probe.cs tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(shell): T09.2 installed HTML theme passes

- html-theme fixture with authored light styling, a highlighted cell, a
  local image and a page element that reuses the style's id
- seed-html-theme and set-html-appearance seed commands
- html-theme-open (Dark at 1.5), html-theme-light (authored colors) and
  html-theme-switch (live Windows switch, no reload, same place)
- BroadcastThemeChange sends WM_SETTINGCHANGE ImmersiveColorSet

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Expected: `ascii-ok`, then one commit.

- [ ] **Step 7: Run it red**

Run the CI loop with `shell-scope=html` and `dev-fast=true`.
Expected: the `html` shard fails in `html-theme-open` with
`The app applied no style for the open`. `html-reader` and
`html-position` pass as before. Any other failure is a harness bug: fix
it first, using superpowers:systematic-debugging.

### Task 4: Session and shell wiring (green)

There's no unit test project for Production. Task 3's installed passes
are this task's failing test, and Tasks 1-2 cover the pure logic.

**Files:**
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs` (fields,
  constructor, `OpenAsync` at :109, `ApplyAppearanceAsync` at :246,
  `OnNavigationStarting` at :533, a new test-file writer beside
  `WritePositionForTest`)
- Modify: `src/DesktopGuides.Production/ShellWindow.Theme.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs:38-48`

**Interfaces:**
- Consumes: Task 1's `ReaderTheme`, `ReaderAppearance` and
  `HtmlReaderStyle`; Task 2's `RecordAppearance`,
  `RecordAppearanceFailed` and `RecordEntryNavigation`; Task 3's test-file
  shape `{ "opacity", "session" }`.
- Produces:
  - `HtmlReaderSession.OpenAsync(ManagedGuideSource source, ReaderAppearance first, CancellationToken token)`
    replaces the two-argument overload;
  - `ShellWindow.ReaderThemeNow()`, `RefreshReaderAppearance()` and the
    `htmlTextScale` field.

- [ ] **Step 1: Session fields and constructor**

Add `using System.Globalization;` at the top of `HtmlReaderSession.cs`.
After the `lastStep` field, add:

```csharp
    // The appearance the shell asked for last, and the last one on the page.
    private ReaderAppearance appearance = new(ReaderTheme.Light, 1.0);
    private ReaderAppearance? applied;
    private bool writingAppearance;
    private readonly Windows.UI.Color defaultPageColor;
```

In the constructor, after `View = new WebView2();`, add:

```csharp
        defaultPageColor = View.DefaultBackgroundColor;
```

- [ ] **Step 2: Open with the first appearance**

Replace `OpenAsync` with:

```csharp
    // A saved point can be restored only once the entry has loaded;
    // RestoreLocationAsync waits on entryLoad. The style goes on first, so
    // the restore sees the final layout.
    public async Task OpenAsync(ManagedGuideSource source, ReaderAppearance first, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(first);
        appearance = first;
        SetPageColor(first.Theme);
        // Shown once the style is on the page, so Dark never flashes white.
        View.Opacity = 0;
        bool entryLoaded = false;
        try
        {
            await OpenEntryAsync(source, token);
            await WriteAppearanceAsync();
            entryLoaded = true;
        }
        finally
        {
            // Whatever the outcome; a failed open replaces the view anyway.
            if (!disposed) View.Opacity = 1;
            entryLoad.TrySetResult(entryLoaded);
            WriteAppearanceForTest();
        }
        token.ThrowIfCancellationRequested();
        if (disposed) return;
        opened = true;
        tracker.Start();
    }
```

- [ ] **Step 3: Apply, write and record**

Replace
`public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) => Task.CompletedTask;`
with:

```csharp
    // Before the open's write, the open picks the appearance up; during a
    // write, the running write does.
    public async Task ApplyAppearanceAsync(ReaderAppearance next, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(next);
        appearance = next;
        if (!opened || disposed) return;
        await WriteAppearanceAsync();
    }

    // Writes until the page has the latest appearance; an unchanged one is
    // never rewritten. A failed write leaves the page as authored and isn't
    // retried until the next refresh: the style is cosmetic.
    private async Task WriteAppearanceAsync()
    {
        if (writingAppearance) return;
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
                    return;
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
        }
        finally
        {
            writingAppearance = false;
        }
    }

    // The color before the first paint. High contrast keeps the view's own.
    private void SetPageColor(ReaderTheme theme)
    {
        if (disposed) return;
        View.DefaultBackgroundColor = HtmlReaderStyle.PageColor(theme) is string hex
            ? Microsoft.UI.ColorHelper.FromArgb(
                255,
                Convert.ToByte(hex[1..3], 16),
                Convert.ToByte(hex[3..5], 16),
                Convert.ToByte(hex[5..7], 16))
            : defaultPageColor;
    }
```

In `OnNavigationStarting`, in `case HtmlNavigationKind.Entry:`, add
`diagnostics?.RecordEntryNavigation();` before `entryNavigated = true;`.

After `WritePositionForTest`, add:

```csharp
    // Test gate only: the applied style while the guide is open. Written to
    // a temporary file and moved, so the smoke never reads half a file.
    private void WriteAppearanceForTest()
    {
        if (diagnostics is null || disposed) return;
        try
        {
            string json = "{\"opacity\":" + View.Opacity.ToString(CultureInfo.InvariantCulture) +
                ",\"session\":" + diagnostics.ToJson(policy.GuideId) + "}";
            string folder = Path.Combine(cacheRoot, "diagnostics");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"html-appearance-{Environment.ProcessId}.json");
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Test diagnostics must never affect reading.
        }
    }
```

- [ ] **Step 4: The shell's theme and refresh**

In `ShellWindow.Theme.cs`, add `using DesktopGuides.Core.Reading;`. Add
the field after `appliedTheme`:

```csharp
    // The open HTML guide's stored scale, read once at open.
    private double htmlTextScale = 1.0;
```

Change the `ActualThemeChanged` subscription in `InitializeThemeChoice`
to:

```csharp
        ShellRoot.ActualThemeChanged += (_, _) =>
        {
            UpdateThemeStatus();
            RefreshReaderAppearance();
        };
```

At the end of `ApplyTheme`, after `UpdateThemeStatus();`, add
`RefreshReaderAppearance();`. Then add:

```csharp
    // System resolves to the root's theme here, so a reader never reads
    // the Windows theme itself.
    private ReaderTheme ReaderThemeNow() => appliedTheme switch
    {
        _ when themeSettings?.HighContrast == true => ReaderTheme.HighContrast,
        AppliedTheme.Light => ReaderTheme.Light,
        AppliedTheme.Dark => ReaderTheme.Dark,
        _ => ShellRoot.ActualTheme == ElementTheme.Dark ? ReaderTheme.Dark : ReaderTheme.Light
    };

    // Restyles the open guide; TXT and PDF sessions ignore it.
    private async void RefreshReaderAppearance()
    {
        if (readerSession is not IReaderSession session) return;
        try
        {
            await session.ApplyAppearanceAsync(
                new ReaderAppearance(ReaderThemeNow(), htmlTextScale), CancellationToken.None);
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
        {
            // A session closed mid-write has nothing left to style.
        }
    }
```

- [ ] **Step 5: Read the scale and open with the appearance**

In `ShellWindow.HtmlReader.cs`, after
`HtmlGuideLoaded loaded = (HtmlGuideLoaded)load;`, add:

```csharp
        htmlTextScale = await ReadTextScaleAsync(guide.Id);
        if (generation != renderGeneration)
        {
            return false;
        }
```

Change the open call to:

```csharp
            await session.OpenAsync(
                new ManagedGuideSource(guide, loaded.EntryFilePath),
                new ReaderAppearance(ReaderThemeNow(), htmlTextScale), token);
```

and add, after `OpenHtmlGuideAsync`:

```csharp
    // T14.1 adds the controls; until then the stored scale only applies.
    // A failed read gives the default: the scale never blocks reading.
    private async Task<double> ReadTextScaleAsync(Guid guideId)
    {
        try
        {
            ReaderPreferences? preferences = await RequireRepository().GetReaderPreferencesAsync(guideId);
            return HtmlReaderStyle.ClampScale(preferences?.TextScale ?? 1.0);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return 1.0;
        }
    }
```

`ReaderPreferences` is in `DesktopGuides.Core.Library` (already imported).
Check with `grep -rn "record ReaderPreferences" src/DesktopGuides.Core`
and add the `using` if it lives elsewhere.

- [ ] **Step 6: Build and run the unit suites**

Sync, build Production and ShellSeed, and run Core and Infrastructure.
Expected: 0 errors and 0 new warnings. Core is at the Task 2 count and
Infrastructure unchanged, all passing.

- [ ] **Step 7: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production/HtmlReaderSession.cs src/DesktopGuides.Production/ShellWindow.Theme.cs src/DesktopGuides.Production/ShellWindow.HtmlReader.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(shell): T09.2 HTML guides follow the theme and stored scale

- the session writes the fixed style through Runtime.evaluate at open,
  hidden until then, and on every changed appearance after that
- the page color matches the theme before the first paint
- the shell resolves the reader theme, reads the guide's stored scale,
  and restyles the open guide on a theme or Windows change
- gated html-appearance test file and entry navigation count

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 8: Run it green**

Run the CI loop with `shell-scope=html` and `dev-fast=true`.
Expected: the `html` shard passes `html-theme-open`, `html-theme-light`
and `html-theme-switch`, and `html-reader` and `html-position` still pass.
The `html-reader-offline` pass now opens in Dark with the style injected,
and its exact served sets don't change. That's a free regression check.

If `html-theme-switch` times out on `the Windows dark switch`, find out
which layer failed before changing anything (superpowers:systematic-debugging):

1. `themeBroadcast` in the report.
2. Whether `AppThemeChoice`'s `ItemStatus` became `System (Dark)`, so
   that WinUI re-themed the root. Add a temporary report field that reads
   it after the switch.
3. Whether `applications` grew while `theme` stayed `Light`, which would
   mean `ReaderThemeNow` read a stale `ActualTheme`.

Record the evidence for each in the spec's implementation notes.

### Task 5: Real-guide check, docs, evidence and PR

**Files:**
- Modify: `docs/p1/t09-2-html-theme-style-design.md` (status,
  implementation notes, verification)
- Modify: `docs/p1/e2e-testing.md` (the fixture, the modes, the html
  group and a T09.2 checklist row)
- Modify: `docs/p1/implementation-plan.md` and `docs/work-breakdown.md`
  (the T09.2 lines)
- Modify: `docs/p1/t14-2-theme-setting-design.md` (a pointer beside
  "HTML guide content is out of scope")
- Create: `docs/p1/evidence/t09-2-html-theme-style/` (reports and
  screenshots)

**Interfaces:**
- Consumes: the green CI run from Task 4 and its artifacts.
- Produces: the PR.

- [ ] **Step 1: Check representative real guides for `!important` colors**

This is the design's first risk. Fetch one page from each of these:

- a GameFAQs FAQ (a text and HTML FAQ page);
- a Fandom wiki walkthrough page;
- a StrategyWiki walkthrough page.

Use `curl -sL -A "Mozilla/5.0"` into `/tmp/t09-2-guides/`, or WebFetch.
For each page and its same-site stylesheets, count:

```bash
grep -oiE '(color|background(-color)?)\s*:[^;"}]*!important' /tmp/t09-2-guides/<file> | wc -l
grep -oiE 'style="[^"]*(color|background)[^"]*!important' /tmp/t09-2-guides/<file> | wc -l
```

Record in the spec's implementation notes:

- the URLs and the counts;
- whether the rules hit guide content or only site chrome (navigation,
  ads), which a saved guide often drops;
- whether a site blocked the fetch;
- the uncertainty: three pages, one fetch each.

Set the risk's priority from the frequency and impact you found. Don't
change code here. If the risk is common in guide content, add it to the
PR as a follow-up for the user to decide on.

- [ ] **Step 2: Update the docs**

- **Spec:**
  - Change `Status:` to `implemented; see Verification.`
  - Add an `## Implementation notes` section with Rulings 1-11 from this
    plan, each in one or two lines, plus any rulings made during
    execution and Step 1's findings.
  - Add a `## Verification` section with the Core and Infrastructure
    counts, the CI run IDs (the red dev-fast run, the green dev-fast run
    and the full run), and the evidence folder.
- **`e2e-testing.md`:**
  - the `html-theme` fixture beside `html-long`;
  - the three modes in the html group's mode list;
  - the `Run-HtmlThemeScenarios` pass, and that it restores the Windows
    theme;
  - a T09.2 row in the checklist with the run ID.
- **`implementation-plan.md` and `work-breakdown.md`:** mark T09.2 done
  in the same form T14.2 used (check `git show 6e8d390 --stat` and copy
  the line shape).
- **`t14-2-theme-setting-design.md`:** after its "HTML guide content is
  out of scope" line, add `T09.2 adds it: see
  [t09-2-html-theme-style-design.md](t09-2-html-theme-style-design.md).`

- [ ] **Step 3: The full CI run and evidence**

Push and run the full CI workflow (not dev-fast). Expected: every shard
is green.

Download the artifacts into
`docs/p1/evidence/t09-2-html-theme-style/`:

- the three `html-theme-*` smoke reports;
- `html-appearance-*.json` from the html diagnostics;
- the screenshots `html-theme-dark-1.5.png`, `html-theme-light.png`,
  `html-theme-system-light.png` and `html-theme-system-dark.png`.

Look at each screenshot before committing it:

- Dark is dark with light text, and its PNG is unchanged;
- Light shows the authored `#222222` text on white;
- the 1.5 shot is visibly larger.

Leave out any file with user paths or data. The smoke reports use the
test data root only. Check with `grep -il "users\\\\" <files>`.

- [ ] **Step 4: Commit the docs and evidence**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add docs/p1/t09-2-html-theme-style-design.md docs/p1/e2e-testing.md docs/p1/implementation-plan.md docs/work-breakdown.md docs/p1/t14-2-theme-setting-design.md docs/p1/evidence/t09-2-html-theme-style
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "docs(p1): T09.2 verification and evidence

- implementation notes, real-guide check and verification in the spec
- html-theme fixture, modes and checklist row in e2e-testing.md
- T09.2 marked done in the plan and work breakdown
- T14.2 design points to T09.2 for HTML content

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
```

- [ ] **Step 5: Open the PR**

Per AGENTS.md, the description names:

- the target task (T09.2);
- the prerequisites and their status (T09.1 #35, T09.3 #39, T14.2 #48,
  all merged);
- the intended outcome (the design's success list).

Also include:

- the four screenshots;
- the real-guide findings;
- the rulings;
- the full CI run link.

End with the PR attribution line. Open it as a draft, then mark it ready
once the full run is green.

- [ ] **Step 6: Review**

Use superpowers:requesting-code-review to dispatch a reviewer against
the `review` skill. Give it:

- the PR URL, base `main` and the head branch;
- the spec and plan paths;
- this plan's Review Focus list verbatim;
- the ledger's `Ruling:` lines.

Ask it to pressure-test:

- the invisible view on a failed write;
- a theme change during the open;
- the id collision;
- the switch pass restoring the user's Windows theme on a failure.

Handle the report with superpowers:receiving-code-review.
