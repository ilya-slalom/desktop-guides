# T14.4 Reader and Settings Design Language Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Reader (TXT, HTML, PDF) and Settings follow the T11.4 design
language: nothing moves when a route opens or a confirmation arrives, the
Reader has one Back, a metadata line with the format, grouped left-aligned
commands with unambiguous glyphs and one reading frame, and Settings groups
its cards under Appearance, Library and Game data with two drop-downs.

**Architecture:**

- Core gains `ReaderCommandPolicy.ShowsGroupSeparator` and
  `TextSizeSteps.ShiftedNotice`; its `Could not` strings become `Couldn't`.
- The shell sorts status: `AnnounceStatus` (probe plus a UI Automation
  notification, no bar) for route-ready and in-place confirmations;
  `ShowRouteProgress` (an overlaid indeterminate bar) for route loads;
  `ShowReaderNotice` (an `InfoBar` in the reading card) for approximate
  place. The shared bar keeps warnings, errors and other confirmations.
- The Reader header loses its Back button (Alt+Left is added) and its
  format pill; the toolbar takes a new `Styles/Reader.xaml` style, new
  glyphs, a group separator and explicit overflow order; the HTML card
  takes the page's color with no padding.
- Settings gets three section headings, an `AppThemeSelector` drop-down
  in place of `BoundedChoice`, and a self-closing provider status.
- `CommunityToolkit.WinUI.Extensions` is pinned centrally.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.5.1), Windows
Community Toolkit 8.2.251219, WebView2, SQLite, xUnit, PowerShell UI
Automation.

**Spec:** [t14-4-reader-settings-design.md](t14-4-reader-settings-design.md)

## Global Constraints

**Branch:** `feat/p1-t14-4-reader-settings-design`, already checked out and
rebased on `main` (`5795a24`). The spec is commit `efbe0b2`.

**Tooling:**

- There is no local `dotnet` or `pwsh`. Builds and tests run on the Windows
  host `pcsx2-win` or in CI.
- Host staging folder `E:\work\desktop-guides\t14-4` (not a git checkout).
  Create it once, then copy the gitignored generated fixtures, which the
  Infrastructure tests need:

  ```bash
  ssh -o BatchMode=yes pcsx2-win "mkdir E:\work\desktop-guides\t14-4"
  R=/Users/ilya.lissoboi/work/desktop-guides
  COPYFILE_DISABLE=1 tar -C $R -cf - tests/fixtures/p0/generated |
    ssh -o BatchMode=yes pcsx2-win "tar -xf - -C E:\work\desktop-guides\t14-4"
  ```

- Host sync (run before every host command):

  ```bash
  R=/Users/ilya.lissoboi/work/desktop-guides
  git -C $R ls-files -co --exclude-standard -z -- . ':!.claude' ':!.superpowers' |
    COPYFILE_DISABLE=1 tar -C $R --null -T - -cf - |
    ssh -o BatchMode=yes pcsx2-win "tar -xf - -C E:\work\desktop-guides\t14-4"
  ```

- Host commands (the host's default shell is `cmd`):

  ```bash
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-4\tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj"
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-4\tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t14-4\src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t14-4\tools\p1\DesktopGuides.ReaderToolbarSmoke\DesktopGuides.ReaderToolbarSmoke.csproj -c Release -p:Platform=x64"
  ```

  To run one test class, add
  `--filter FullyQualifiedName~DesktopGuides.Core.Tests.<Class>`.
- The CI loop for branch `b=feat/p1-t14-4-reader-settings-design` and
  group `<g>`:
  1. Push, then run
     `gh workflow run windows-ci.yml --ref $b -f shell-scope=<g> [-f dev-fast=true]`.
  2. Run
     `gh run list --workflow windows-ci.yml --branch $b --limit 1 --json databaseId,headSha -q '.[0]'`
     and check that `headSha` matches `git rev-parse HEAD`.
  3. Run `gh run watch <id> --exit-status --interval 60`.
  4. On failure, run `gh run view <id> --log-failed`, and download the
     shard artifact with `gh run download <id> -n production-shell-ui-<g>`.
- `dev-fast=true` runs only the x64 shell smoke and Core tests; it skips the
  `reader-toolbar-ui` job, so Task 6 dispatches without it. `dev-fast` runs
  are for iteration and are not PR evidence.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- PowerShell stays ASCII-only: write `…` as `[char]0x2026` and `'` as is.
  Bash commands use absolute paths or `git -C`, never `cd`. Quote globs
  (zsh). Leave the untracked `.claude/` and `.superpowers/` alone.
- Installed runs follow [e2e-testing.md](e2e-testing.md), including its
  backup and cleanup rules. No firewall rule. If a run needs an elevated
  scheduled task, stop and ask.
- Provider credentials live in `E:\work\igdb_credentials.txt` and
  `E:\work\steamgriddb_credentials.txt` on the host. Never print, copy or log
  their values.
- Installed checks assert what app code controls (bounds of our elements,
  `ItemStatus`, names, heading levels, visibility), never WinUI or Windows
  rendering. Glyphs, the HTML single frame and label centering are checked
  in screenshot review only.

**Values (verbatim from the spec):**

- `ReaderCommandPolicy.ShowsGroupSeparator`: true when the visible commands
  include `PageTurn` or `PageEdge` and also `TextSize` or `Zoom`.
- `TextSizeSteps.ShiftedNotice` is `Your place may have shifted.`
- New AutomationIds: `RouteProgress`, `ReaderLoading`, `ReaderNotice`,
  `ReaderGroupSeparator`, `AppThemeSelector`, `SettingsAppearanceHeading`,
  `SettingsLibraryHeading`, `SettingsGameDataHeading`. Removed:
  `ReaderBackToGame`, `ReaderPlaceholder`, `AppThemeChoice`.
- Glyphs: Go to start `Symbol.Previous`, Go to end `Symbol.Next`, Smaller
  text `Symbol.FontDecrease`, Larger text `Symbol.FontIncrease`, Go to page
  `Symbol.Page`, Fit to width `FontIcon` `E9A6`, Reset text size
  `Symbol.Font`, Find `Symbol.Find`.
- `DynamicOverflowOrder`: Go to start and Go to end `1`; the size or zoom
  group `2`; Previous and Next page `3`.
- Tokens: `DesktopGuidesTextSizeValueMinWidth` 44,
  `DesktopGuidesSettingsChoiceMinWidth` 160,
  `DesktopGuidesCredentialInputMinWidth` 260,
  `DesktopGuidesInlineProgressSize` 20,
  `DesktopGuidesPdfSplitBreakpoint` 720,
  `DesktopGuidesPdfUnlockMaxWidth` 360,
  `DesktopGuidesReaderMonoFontFamily` Consolas.
- Settings sections, in order: Appearance (App theme, Window background),
  Library (Library storage), Game data (Game data providers).
- Toolkit: `CommunityToolkit.WinUI.Extensions` at `8.2.251219`.

**Rulings this plan makes against the spec** (Task 9 records them in the
spec's implementation notes):

1. **Copy count.** The spec's "17 strings" counted the P0 diagnostic app
   (`src/DesktopGuides.App`), which stays as it is. The change is 14
   strings: three in Core, eleven in Production.
2. **A card notice also updates the probe.** `ShowReaderNotice` calls
   `AnnounceStatus`, so `Wait-Status` still sees approximate-place messages.
3. **`StatusShowsProblem` goes.** It kept the T14.3 notice from covering a
   warning in the same bar; the notice now lives in the card.
4. **Startup.** `ShellStatusInfoBar` starts closed and `RouteProgress`
   starts visible; startup's `Loading library…` uses `ShowRouteProgress`.
   `Waiting for previous window…` keeps the bar.
5. **`placeholder:` becomes `loading:`.** `ShowReaderSurface` shows
   `ReaderLoading` while true. The three calls that start a format's load
   (TXT, HTML, PDF) pass `true`; failure and dispose paths pass `false`.
6. **A failed render stops loading.** `RenderCurrentAsync`'s catch shows the
   error bar, hides `RouteProgress` and turns `ReaderLoading` off.
7. **`RouteProgress` hides in one place.** `RenderCurrentAsync` hides it in
   a `finally` when its generation is current, and `ShowErrorStatus` and
   `ShowWarningStatus` hide it too, which covers `OpenGuideAsync`'s
   failures before the render.
8. **The completion row moves by code.** The header grid's `RowSpacing` is 0
   in wide layout and `DesktopGuidesSpacing12` in narrow, so an empty second
   row adds no gap.
9. **Alt+Left lives on `ShellRoot`.** It may not fire while focus is inside
   the WebView2 page; T16.1 owns keyboard parity there.
10. **Drop-down keys.** Keyboard theme selection focuses `AppThemeSelector`,
    sends Alt+Down, arrows to the item and presses Enter.
11. **Mode rename.** The smoke mode `theme-segmented` becomes
    `theme-selector`.
12. **Provider routine status.** `Success` and `Informational` provider
    messages close after 3 s; errors stay. CI has no credentials, so the
    check runs on the host with `-ProviderOnly`.
13. **Reader screenshots.** A new smoke mode `design-readers` runs in the
    `design` group after the material passes, from a wiped data root seeded
    with `seed-text-size`, in light and dark.
14. **The shifted notice.** `OnAppearanceRestored` shows the notice and
    announces `ShiftedStatus`; the save path announces `ShiftedStatus` or
    `Status` as before. The newest announcement wins.
15. **`ShowsGroupSeparator` takes `IReadOnlyCollection<ReaderCommand>`**, so
    the toolbar's `HashSet` and `VisibleCommands`' list both pass.
16. **Narrator.** The UI Automation client in the harness can't observe
    notification events. Before the PR the user listens with Narrator on
    the host (Task 9); the full pass is T16.2.
17. **The metadata line keeps the body size.** The spec names
    `MetadataStyle`; the approved mockup shows the game name at its current
    secondary body size, so all three blocks use
    `DesktopGuidesSecondaryBodyStyle`. A left-aligned grid with a star
    column trims a long game name and keeps the format beside it.
18. **No label strip.** A compact `CommandBar` reserves a hidden label row
    under each icon, which is why the size label sits low. The Reader
    style sets `DefaultLabelPosition="Collapsed"`: labels stay as automation
    names and tooltips, and "More" still opens the overflow menu, but the
    bar no longer shows labels when it opens.

## Review Focus

1. **A warning while a quiet message arrives.** A failed save's error bar
   must stay when "Guide ready." or "Text size 110%." follows. Task 4's
   `AnnounceStatus` never touches the bar; `text-size-error` and
   `theme-error` still find their errors after the quiet messages.
2. **A route load that fails.** A broken guide must not leave the progress
   line or the card's ring spinning. Rulings 6 and 7; Task 4 Step 7's
   `reader-render-error-observed` check asserts `ReaderLoading` is absent.
3. **Back from the keyboard.** With the in-page button gone, a keyboard user
   must still leave the Reader. Task 5's Alt+Left steps replace every
   `Press-Enter` on `ReaderBackToGame`; the title-bar Back keeps the
   pointer path.
4. **A narrow toolbar.** At narrow widths Previous and Next must stay on the
   bar. Task 6's toolbar smoke check asserts it after Go to start has moved
   to overflow.
5. **A notice opening under a restore.** An approximate restore shows the
   card notice, which shrinks the viewport once; TXT must keep its top line
   and PDF its page. Task 3's `progress-changed` checks run
   `Wait-TopLine` and `Wait-PdfFraction` after the notice is visible.

## File Map

| File | Change |
| --- | --- |
| `src/DesktopGuides.Core/Reading/ReaderContract.cs` | `ShowsGroupSeparator` |
| `src/DesktopGuides.Core/Reading/TextSizeSteps.cs` | `ShiftedNotice`, copy |
| `src/DesktopGuides.Core/Library/ThemePresentation.cs`, `GuideCompletionPresentation.cs` | copy |
| `tests/DesktopGuides.Core.Tests/ReaderContractTests.cs`, `TextSizeStepsTests.cs`, `ThemePresentationTests.cs`, `GuideCompletionPresentationTests.cs` | tests |
| `Directory.Packages.props`, `src/DesktopGuides.Production/DesktopGuides.Production.csproj`, `packages.lock.json` | Extensions pin |
| `src/DesktopGuides.Production/Styles/Reader.xaml` (new), `DesignTokens.xaml`, `Typography.xaml`, `App.xaml` | resources |
| `tools/p1/DesktopGuides.ReaderToolbarSmoke/DesktopGuides.ReaderToolbarSmoke.csproj`, `App.xaml` | link `Reader.xaml` |
| `src/DesktopGuides.Production/ShellWindow.xaml` | progress overlay, Reader header, card, notice, Settings |
| `src/DesktopGuides.Production/ShellWindow.xaml.cs` | status methods, Alt+Left, header layout, card chrome, copy |
| `src/DesktopGuides.Production/ShellWindow.Progress.cs`, `ShellWindow.TextSize.cs`, `ShellWindow.Completion.cs`, `ShellWindow.Theme.cs`, `ShellWindow.HtmlReader.cs`, `ShellWindow.PdfReader.cs` | status call sites, theme drop-down, HTML card |
| `src/DesktopGuides.Production/ReaderToolbar.xaml`, `ReaderToolbar.xaml.cs` | style, glyphs, separator, order, copy |
| `src/DesktopGuides.Production/TextReaderView.xaml`, `PdfReaderView.xaml`, `PdfReaderView.xaml.cs` | tokens, copy |
| `src/DesktopGuides.Production/ProviderSettingsCard.xaml`, `.xaml.cs` | styles, tokens, self-closing status |
| `src/DesktopGuides.Production/GameEditorDialog.xaml.cs`, `AddGameDialog.xaml.cs`, `Program.cs` | copy |
| `tools/p1/windows_shell_ui_smoke.ps1` | quiet list, new checks, Back, theme, Settings, `design-readers` |
| `tools/p1/windows_reader_toolbar_ui_smoke.ps1` | overflow order check |
| `tools/p1/windows_shell_install.ps1` | `theme-selector`, `design-readers` pass |
| `docs/p1/*`, `docs/work-breakdown.md`, `docs/p1-technical-design.md`, `docs/progress.md` | Task 9 |

---

### Task 1: Core rules and copy

**Files:**
- Modify: `src/DesktopGuides.Core/Reading/ReaderContract.cs` (`ReaderCommandPolicy`)
- Modify: `src/DesktopGuides.Core/Reading/TextSizeSteps.cs`
- Modify: `src/DesktopGuides.Core/Library/ThemePresentation.cs:44`
- Modify: `src/DesktopGuides.Core/Library/GuideCompletionPresentation.cs:19-20`
- Test: `tests/DesktopGuides.Core.Tests/ReaderContractTests.cs`, `TextSizeStepsTests.cs:106-110`, `ThemePresentationTests.cs:60`, `GuideCompletionPresentationTests.cs:42,55`
- Modify: `tools/p1/windows_shell_ui_smoke.ps1:3303,3580,4188` (the three app strings these classes produce)

**Interfaces:**
- Produces: `public static bool ReaderCommandPolicy.ShowsGroupSeparator(IReadOnlyCollection<ReaderCommand> visible)`
  (Task 6) and `public const string TextSizeSteps.ShiftedNotice` (Task 4).

- [ ] **Step 1: Write the failing tests**

In `ReaderContractTests.cs`, after `PageEdgeIsVisibleOnlyWithPageNavigation`:

```csharp
    // T14.4: the separator sits between movement and size or zoom.
    [Theory]
    [InlineData(ReaderCapabilities.Scroll | ReaderCapabilities.PageNavigation | ReaderCapabilities.TextSize, true)]
    [InlineData(ReaderCapabilities.Scroll | ReaderCapabilities.TextSize, false)]
    [InlineData(ReaderCapabilities.PageNavigation | ReaderCapabilities.PageJump |
        ReaderCapabilities.FitWidth | ReaderCapabilities.Zoom, true)]
    [InlineData(ReaderCapabilities.PageNavigation, false)]
    [InlineData(ReaderCapabilities.None, false)]
    public void GroupSeparatorNeedsMovementAndSizeOrZoom(ReaderCapabilities capabilities, bool expected)
    {
        using FakeReader reader = new(GuideFormat.Txt, capabilities);
        Assert.Equal(expected,
            ReaderCommandPolicy.ShowsGroupSeparator(ReaderCommandPolicy.VisibleCommands(reader).ToList()));
    }
```

The rows are TXT, HTML, PDF, movement only and nothing.

In `TextSizeStepsTests.cs`, replace `SaveFailedExplainsTheError` and add two facts
after `ShiftedStatusNamesTheSizeAndTheShift`:

```csharp
    [Fact]
    public void SaveFailedExplainsTheError() =>
        Assert.Equal(
            "Couldn't save the text size: database is locked",
            TextSizeSteps.SaveFailed("database is locked"));
```

```csharp
    [Fact]
    public void ShiftedNoticeSaysThePlaceMayHaveShifted() =>
        Assert.Equal("Your place may have shifted.", TextSizeSteps.ShiftedNotice);

    [Fact]
    public void ShiftedStatusEndsWithTheNotice() =>
        Assert.EndsWith(TextSizeSteps.ShiftedNotice, TextSizeSteps.ShiftedStatus(1.1));
```

In `ThemePresentationTests.cs:60` the expected text becomes
`"Couldn't save the app theme: database is locked"`. In
`GuideCompletionPresentationTests.cs:42` it becomes
`"Couldn't update completion for Walkthrough. Try again."` and at `:55`
`$"Couldn't update completion for {title}. Try again."`.

- [ ] **Step 2: Run the tests to verify they fail**

Sync, then:

```bash
ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-4\tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj --filter FullyQualifiedName~ReaderContractTests|FullyQualifiedName~TextSizeStepsTests|FullyQualifiedName~ThemePresentationTests|FullyQualifiedName~GuideCompletionPresentationTests"
```

Expected: the build fails on `ShowsGroupSeparator` and `ShiftedNotice`
(CS0117). That is the red state for the new members.

- [ ] **Step 3: Write the minimal implementation**

In `ReaderContract.cs`, inside `ReaderCommandPolicy`, after `VisibleCommands`:

```csharp
    // T14.4: the toolbar's separator sits between movement and size or
    // zoom, so it shows only when a format has both.
    public static bool ShowsGroupSeparator(IReadOnlyCollection<ReaderCommand> visible) =>
        (visible.Contains(ReaderCommand.PageTurn) || visible.Contains(ReaderCommand.PageEdge)) &&
        (visible.Contains(ReaderCommand.TextSize) || visible.Contains(ReaderCommand.Zoom));
```

In `TextSizeSteps.cs`, before `ShiftedStatus`:

```csharp
    // T14.4: the reading card's notice when a size step came back by fraction.
    public const string ShiftedNotice = "Your place may have shifted.";
```

and change `ShiftedStatus` and `SaveFailed` to:

```csharp
    public static string ShiftedStatus(double scale) =>
        $"Text size {Label(scale)}. {ShiftedNotice}";

    public static string SaveFailed(string message) =>
        $"Couldn't save the text size: {message}";
```

In `ThemePresentation.cs:44`:

```csharp
    public static string SaveFailed(string message) => $"Couldn't save the app theme: {message}";
```

In `GuideCompletionPresentation.cs`:

```csharp
    public static string SaveFailed(string title) =>
        $"Couldn't update completion for {title}. Try again.";
```

- [ ] **Step 4: Run the tests to verify they pass**

Run the command from Step 2, then the whole Core suite:

```bash
ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-4\tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj"
```

Expected: PASS. The suite was 899 before this task; it is now 906 (five
theory rows and two facts).

- [ ] **Step 5: Update the three smoke strings**

In `windows_shell_ui_smoke.ps1`, the waits for these messages change with
them (ASCII: write the apostrophe plainly inside double quotes):

```powershell
                [void](Wait-Status "Couldn't update completion for $numbered. Try again." -Seconds 60)
```

```powershell
                [void](Wait-Status "Couldn't save the text size: " -Prefix -Seconds 60)
```

```powershell
            [void](Wait-Status "Couldn't save the app theme: " -Prefix -Seconds 60)
```

- [ ] **Step 6: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core tests/DesktopGuides.Core.Tests tools/p1/windows_shell_ui_smoke.ps1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(core): T14.4 group separator rule, shifted notice and copy

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Shared resources and the Toolkit pin

This task is mechanical (resources nothing uses yet, a package pin and a
lock file), so it skips TDD; the locked restores and builds are its check.

**Files:**
- Create: `src/DesktopGuides.Production/Styles/Reader.xaml`
- Modify: `src/DesktopGuides.Production/Styles/DesignTokens.xaml` (after `DesktopGuidesHeroCornerRadius`)
- Modify: `src/DesktopGuides.Production/Styles/Typography.xaml` (before `</ResourceDictionary>`)
- Modify: `src/DesktopGuides.Production/App.xaml`
- Modify: `Directory.Packages.props`, `src/DesktopGuides.Production/DesktopGuides.Production.csproj:24-28`, `src/DesktopGuides.Production/packages.lock.json`
- Modify: `tools/p1/DesktopGuides.ReaderToolbarSmoke/DesktopGuides.ReaderToolbarSmoke.csproj`, `tools/p1/DesktopGuides.ReaderToolbarSmoke/App.xaml`

**Interfaces:**
- Produces the resource keys in Global Constraints › Values, plus
  `DesktopGuidesSettingsSectionMargin` (`0,24,0,8`) and
  `DesktopGuidesTextLinePadding` (`12,0`), for Tasks 4–8.

- [ ] **Step 1: Create `Styles/Reader.xaml`**

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- T14.4: the Reader's commands sit on the page, from the left. -->
    <Style x:Key="DesktopGuidesReaderCommandBarStyle" TargetType="CommandBar">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="HorizontalAlignment" Value="Left" />
        <Setter Property="IsDynamicOverflowEnabled" Value="True" />
    </Style>

    <x:Double x:Key="DesktopGuidesTextSizeValueMinWidth">44</x:Double>
</ResourceDictionary>
```

- [ ] **Step 2: Add the tokens and styles**

In `DesignTokens.xaml`, after `DesktopGuidesHeroCornerRadius`:

```xml

    <!-- T14.4: sizes the Reader and Settings used as literals. -->
    <x:Double x:Key="DesktopGuidesSettingsChoiceMinWidth">160</x:Double>
    <x:Double x:Key="DesktopGuidesCredentialInputMinWidth">260</x:Double>
    <x:Double x:Key="DesktopGuidesInlineProgressSize">20</x:Double>
    <x:Double x:Key="DesktopGuidesPdfSplitBreakpoint">720</x:Double>
    <x:Double x:Key="DesktopGuidesPdfUnlockMaxWidth">360</x:Double>
    <Thickness x:Key="DesktopGuidesSettingsSectionMargin">0,24,0,8</Thickness>
    <Thickness x:Key="DesktopGuidesTextLinePadding">12,0</Thickness>
```

In `Typography.xaml`, before `</ResourceDictionary>`:

```xml

    <!-- T14.4: TXT keeps Consolas; T08.3's line metrics depend on it. -->
    <FontFamily x:Key="DesktopGuidesReaderMonoFontFamily">Consolas</FontFamily>

    <Style x:Key="DesktopGuidesSettingsSectionStyle"
           TargetType="TextBlock"
           BasedOn="{StaticResource BodyStrongTextBlockStyle}">
        <Setter Property="Foreground"
                Value="{ThemeResource DesktopGuidesPrimaryTextBrush}" />
        <Setter Property="Margin"
                Value="{StaticResource DesktopGuidesSettingsSectionMargin}" />
        <Setter Property="TextWrapping" Value="Wrap" />
        <Setter Property="AutomationProperties.HeadingLevel" Value="Level2" />
    </Style>
```

`Typography.xaml` merges after `DesignTokens.xaml` in `App.xaml`, so the
margin resolves.

- [ ] **Step 3: Merge `Reader.xaml`**

In `src/DesktopGuides.Production/App.xaml`, after the `Controls.xaml` line:

```xml
                <ResourceDictionary Source="ms-appx:///Styles/Reader.xaml" />
```

In `tools/p1/DesktopGuides.ReaderToolbarSmoke/App.xaml`, after the
`DesignTokens.xaml` line, add the same line. In
`DesktopGuides.ReaderToolbarSmoke.csproj`, after the `DesignTokens.xaml`
`Page` item:

```xml
    <Page Include="../../../src/DesktopGuides.Production/Styles/Reader.xaml"
          Link="Styles/Reader.xaml" />
```

- [ ] **Step 4: Pin the Extensions package**

In `Directory.Packages.props`, after the `Segmented` entry:

```xml
    <PackageVersion Include="CommunityToolkit.WinUI.Extensions"
                    Version="8.2.251219" />
```

In `DesktopGuides.Production.csproj`, after the `Segmented` reference:

```xml
    <!-- T14.4: Styles/Controls.xaml uses FrameworkElementExtensions directly. -->
    <PackageReference Include="CommunityToolkit.WinUI.Extensions" />
```

- [ ] **Step 5: Regenerate the lock file on the host**

Sync, then:

```bash
ssh -o BatchMode=yes pcsx2-win "dotnet restore E:\work\desktop-guides\t14-4\src\DesktopGuides.Production\DesktopGuides.Production.csproj --force-evaluate -p:Platform=x64"
scp -q "pcsx2-win:E:/work/desktop-guides/t14-4/src/DesktopGuides.Production/packages.lock.json" /Users/ilya.lissoboi/work/desktop-guides/src/DesktopGuides.Production/packages.lock.json
git -C /Users/ilya.lissoboi/work/desktop-guides diff --stat -- src/DesktopGuides.Production/packages.lock.json
```

Expected: `CommunityToolkit.WinUI.Extensions` turns `"type": "Direct"` with
`"requested": "[8.2.251219, )"` in each target section; nothing else changes
version. Then check locked restores for both CI platforms:

```bash
ssh -o BatchMode=yes pcsx2-win "dotnet restore E:\work\desktop-guides\t14-4\src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 && dotnet restore E:\work\desktop-guides\t14-4\src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=ARM64 && dotnet restore E:\work\desktop-guides\t14-4\tools\p1\DesktopGuides.ReaderToolbarSmoke\DesktopGuides.ReaderToolbarSmoke.csproj --locked-mode -p:Platform=x64"
```

Expected: three successful restores.

- [ ] **Step 6: Build**

Run the Production and ReaderToolbarSmoke host builds from Global
Constraints. Expected: both succeed with 0 warnings.

- [ ] **Step 7: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add Directory.Packages.props src/DesktopGuides.Production tools/p1/DesktopGuides.ReaderToolbarSmoke
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "build(shell): T14.4 Reader and Settings resources, pin Toolkit Extensions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: Installed status checks (the failing test)

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (`Wait-Status` at `:234`, a new helper after `Assert-NoOverlap` at `:873`, `txt-load-paused` at `:1952`, the `Assert-Absent 'ReaderPlaceholder'` lines, `position-text-size-fallback` at `:2245`, `completion-reader` at `:3242`, `progress-changed` at `:3024`, `Step-TextSize` at `:3343`)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `Test-QuietStatus([string] $message)` and
  `Assert-ReaderCommandsStay([scriptblock] $action, [string] $what)` in the
  smoke's shared scope, used again by Tasks 5 and 6. The app ids it expects
  (`ReaderLoading`, `RouteProgress`, `ReaderNotice`) come from Task 4.

- [ ] **Step 1: Teach `Wait-Status` which messages are quiet**

Above `function Wait-Status`, add:

```powershell
    # T14.4: these messages never open the shell bar. The probe and a UI
    # Automation notification carry them; the page itself shows the state.
    $quietStatusPatterns = @(
        '^(Library|Game|Guide|Settings) ready\.$',
        ('^(Opening guide|Loading library|Loading game|Loading guide)' + [char]0x2026 + '$'),
        '^Text size \d+%\.( Your place may have shifted\.)?$',
        ' marked (complete|in progress)\.$',
        '^App theme set to (System|Light|Dark)\.$',
        '^Window background set to (Mica|Acrylic|Solid)\.$',
        '^(No games match\.|\d+ of \d+ games match\.)$',
        '^Opened near your last place\. The guide changed since you were here\.$')

    function Test-QuietStatus([string] $message) {
        foreach ($pattern in $quietStatusPatterns) {
            if ($message -cmatch $pattern) { return $true }
        }
        return $false
    }

    # Watches the bar briefly: a quiet message must never be on it.
    function Assert-QuietStatus([string] $message) {
        $deadline = (Get-Date).AddMilliseconds(400)
        do {
            $bar = Find-ById 'ShellStatus'
            if ($bar -and -not $bar.Current.IsOffscreen -and $bar.Current.Name -ceq $message) {
                throw "The quiet status '$message' opened the shell status bar."
            }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
    }
```

In `Wait-Status`, delete the `$transient = ...` block (the four ready
messages) and replace

```powershell
                if ($transient -or $AllowHidden) {
                    $script:lastStatusSequence = $sequence
                    return $probe
                }
```

with

```powershell
                if (Test-QuietStatus $message) {
                    Assert-QuietStatus $message
                    $script:lastStatusSequence = $sequence
                    return $probe
                }
                if ($AllowHidden) {
                    $script:lastStatusSequence = $sequence
                    return $probe
                }
```

- [ ] **Step 2: Add the toolbar-stays helper**

After `function Assert-NoOverlap`, add:

```powershell
    # T14.4: a quiet confirmation opens no bar above the Reader, so the
    # Reader's toolbar stays where it was.
    function Assert-ReaderCommandsStay([scriptblock] $action, [string] $what) {
        [void](Wait-HiddenById 'ShellStatus')
        $before = (Wait-VisibleById 'ReaderCommands').Current.BoundingRectangle.Top
        & $action
        Start-Sleep -Milliseconds 300
        $after = (Wait-VisibleById 'ReaderCommands').Current.BoundingRectangle.Top
        if ([Math]::Abs($after - $before) -gt 1) {
            throw "$what moved the Reader toolbar from $before to $after."
        }
    }
```

The block runs with `&`, so it sees the caller's `$keys`, `$label` and
`$web` through PowerShell's dynamic scoping. Don't add `GetNewClosure()`: a
closure's module can't see the smoke's script-scope functions.

- [ ] **Step 3: Use it for text size and completion**

Replace `Step-TextSize` with:

```powershell
            function Step-TextSize([string] $keys, [string] $label) {
                Assert-ReaderCommandsStay {
                    Send-Keys $keys
                    Wait-TextSize $label
                    [void](Wait-Status "Text size $label.")
                } "A text-size step to $label"
            }
```

In `completion-reader`, replace

```powershell
                Send-Keys '{RIGHT}'
                [void](Wait-Status "$web marked complete.")
```

with

```powershell
                Assert-ReaderCommandsStay {
                    Send-Keys '{RIGHT}'
                    [void](Wait-Status "$web marked complete.")
                } 'Marking complete in the Reader'
```

- [ ] **Step 4: Check the in-page loading state**

In `txt-load-paused`, after `Open-TextGuide 'ASCII Map Guide'`:

```powershell
            # T14.4: the held load shows in the card and the progress line,
            # not in the shell bar.
            [void](Wait-VisibleById 'ReaderLoading')
            [void](Wait-VisibleById 'RouteProgress')
            Assert-Absent 'ShellStatus'
```

Replace each `Assert-Absent 'ReaderPlaceholder'` (six lines) with
`Assert-Absent 'ReaderLoading'`.

- [ ] **Step 5: Check the card notice**

In `progress-changed`, after each `[void](Wait-Status $approximate)` (two
lines), add:

```powershell
                [void](Wait-Name 'ReaderNotice' $approximate)
```

The existing `Wait-TopLine` and `Wait-PdfFraction` after them check the place
survived the notice opening (Review Focus 5).

In `position-text-size-fallback`, after the `$after = Step-HtmlTextSize ...`
statement inside the loop, add:

```powershell
                [void](Wait-Name 'ReaderNotice' 'Your place may have shifted.')
```

- [ ] **Step 6: Run it to verify it fails**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tools/p1/windows_shell_ui_smoke.ps1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(p1): T14.4 quiet statuses never open the shell bar

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
gh workflow run windows-ci.yml --ref feat/p1-t14-4-reader-settings-design -f shell-scope=txt -f dev-fast=true
```

Follow the CI loop. Expected: `dev-production-shell-ui (txt)` fails at the
first ready message with `The quiet status 'Library ready.' opened the shell
status bar.` (or `Game ready.`). The other new checks are proved green in
Task 4.

### Task 4: Quiet status, route progress, card loading and notice

**Files:**
- Modify: `src/DesktopGuides.Production/Styles/Reader.xaml`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (`ShellStatusInfoBar` `:79-87`, the `ShellContent` close `:635`, the reading card `:502-557`)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (usings, `:183-240`, `:283-286`, `:1289-1326`, `:1420`, `:1474-1810`, `:1849`, copy)
- Modify: `src/DesktopGuides.Production/ShellWindow.Progress.cs:186-200`, `ShellWindow.TextSize.cs:66-109`, `ShellWindow.Completion.cs:83,89`, `ShellWindow.Theme.cs:118`, `ShellWindow.HtmlReader.cs`, `ShellWindow.PdfReader.cs`
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml.cs:304`, `GameEditorDialog.xaml.cs:93`, `AddGameDialog.xaml.cs:241`, `Program.cs:137` (copy)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (`:1160-1207`, `:5528-5535`)

**Interfaces:**
- Consumes: `TextSizeSteps.ShiftedNotice` (Task 1); `DesktopGuidesInlineProgressSize` (Task 2).
- Produces, in `ShellWindow`: `AnnounceStatus(string)`, `ShowRouteProgress(string)`,
  `HideRouteProgress()`, `ShowReaderNotice(string)`, `HideReaderNotice()`,
  `ShowReaderSurface(bool loading, string? error = null, UIElement? view = null, HtmlGuideLoadAction action = HtmlGuideLoadAction.None)`,
  and the named elements `RouteProgress`, `ReaderLoading`, `ReaderLoadingRing`,
  `ReaderNotice`, `ReaderSurfaceCard` (Task 7 restyles the card).

- [ ] **Step 1: The notice margin**

In `Styles/Reader.xaml`, after `DesktopGuidesTextSizeValueMinWidth`:

```xml
    <!-- Collapsed elements take no margin, so a closed notice leaves no gap. -->
    <Thickness x:Key="DesktopGuidesReaderNoticeMargin">0,0,0,12</Thickness>
```

- [ ] **Step 2: The shell bar starts closed; the progress line overlays the content**

In `ShellWindow.xaml`, replace the `ShellStatusInfoBar` element with:

```xml
            <InfoBar x:Name="ShellStatusInfoBar"
                     Style="{StaticResource DesktopGuidesStatusInfoBarStyle}"
                     Margin="0,0,0,16"
                     IsOpen="False"
                     Severity="Informational"
                     AutomationProperties.AutomationId="ShellStatus"
                     AutomationProperties.LiveSetting="Polite" />
```

Wrap `ShellContent` in a host grid. Before `<Grid x:Name="ShellContent"`:

```xml
        <Grid x:Name="ShellContentHost">
```

and replace the closing pair at the end of the file

```xml
        </Grid>
        </NavigationView>
```

with

```xml
        </Grid>
        <!-- T14.4: route loads show here, over the content, so nothing moves. -->
        <ProgressBar x:Name="RouteProgress"
                     IsIndeterminate="True"
                     VerticalAlignment="Top"
                     AutomationProperties.AutomationId="RouteProgress"
                     AutomationProperties.Name="Loading" />
        </Grid>
        </NavigationView>
```

`RouteProgress` starts visible: the app is loading at launch (ruling 4).

- [ ] **Step 3: The reading card gets a notice row and a loading state**

Replace the reading card (`<Border Grid.Row="3" ...>` through its
`</Border>`) with:

```xml
                <Border x:Name="ReaderSurfaceCard"
                        Grid.Row="3"
                        Style="{StaticResource DesktopGuidesElevatedSurfaceStyle}"
                        Background="{ThemeResource DesktopGuidesReadingSurfaceBrush}">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="*" />
                        </Grid.RowDefinitions>
                        <!-- T14.4: an approximate place is said here, so the commands above never move. -->
                        <InfoBar x:Name="ReaderNotice"
                                 Margin="{StaticResource DesktopGuidesReaderNoticeMargin}"
                                 Visibility="Collapsed"
                                 IsOpen="False"
                                 IsClosable="True"
                                 Severity="Informational"
                                 Closed="ReaderNoticeClosed"
                                 AutomationProperties.AutomationId="ReaderNotice" />
                        <local:AutomationGroup x:Name="ReaderLoading"
                                               Grid.Row="1"
                                               Visibility="Collapsed"
                                               AutomationProperties.AutomationId="ReaderLoading"
                                               AutomationProperties.Name="Opening guide…">
                            <StackPanel Orientation="Horizontal"
                                        VerticalAlignment="Top"
                                        Spacing="{StaticResource DesktopGuidesSpacing12}">
                                <ProgressRing x:Name="ReaderLoadingRing"
                                              IsActive="False"
                                              Width="{StaticResource DesktopGuidesInlineProgressSize}"
                                              Height="{StaticResource DesktopGuidesInlineProgressSize}"
                                              AutomationProperties.AccessibilityView="Raw" />
                                <TextBlock Text="Opening guide…"
                                           Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                                           AutomationProperties.AccessibilityView="Raw" />
                            </StackPanel>
                        </local:AutomationGroup>
                        <StackPanel Grid.Row="1" Spacing="{StaticResource DesktopGuidesSpacing12}">
                            <!-- ReaderLoadError, ReaderLoadErrorAction and PdfUnlockPanel exactly as before -->
                        </StackPanel>
                        <ContentControl x:Name="ReaderSurface"
                                        Grid.Row="1"
                                        HorizontalAlignment="Stretch"
                                        VerticalAlignment="Stretch"
                                        HorizontalContentAlignment="Stretch"
                                        VerticalContentAlignment="Stretch" />
                    </Grid>
                </Border>
```

Move the existing `ReaderLoadError`, `ReaderLoadErrorAction` and
`PdfUnlockPanel` elements into the `Grid.Row="1"` `StackPanel` unchanged (the
comment marks where; Task 7 restyles `PdfUnlockPanel`). The
`ReaderPlaceholder` text block is gone.

- [ ] **Step 4: The status methods**

In `ShellWindow.xaml.cs`, add `using Microsoft.UI.Xaml.Automation.Peers;`.
After `ShowErrorStatus`, add:

```csharp
    // T14.4: a ready route, or a change the page already shows, is
    // announced without the bar. The probe still counts it.
    private void AnnounceStatus(string message)
    {
        AutomationProperties.SetItemStatus(
            ShellContent, $"{++statusSequence}|{message}");
        AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(Navigation)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(Navigation);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent,
            message,
            "DesktopGuidesStatus");
    }

    private void ShowRouteProgress(string message)
    {
        RouteProgress.Visibility = Visibility.Visible;
        AnnounceStatus(message);
    }

    private void HideRouteProgress() => RouteProgress.Visibility = Visibility.Collapsed;

    private void ShowReaderNotice(string message)
    {
        ReaderNotice.Message = message;
        AutomationProperties.SetName(ReaderNotice, message);
        ReaderNotice.Visibility = Visibility.Visible;
        ReaderNotice.IsOpen = true;
        AnnounceStatus(message);
    }

    private void HideReaderNotice()
    {
        ReaderNotice.IsOpen = false;
        ReaderNotice.Visibility = Visibility.Collapsed;
    }

    private void ReaderNoticeClosed(InfoBar sender, InfoBarClosedEventArgs args) =>
        sender.Visibility = Visibility.Collapsed;
```

Change `ShowWarningStatus` and `ShowErrorStatus` to hide the progress line
first (ruling 7):

```csharp
    private void ShowWarningStatus(string message)
    {
        HideRouteProgress();
        ShowStatus(message, InfoBarSeverity.Warning, true, false);
    }

    private void ShowErrorStatus(string message)
    {
        HideRouteProgress();
        ShowStatus(message, InfoBarSeverity.Error, true, false);
    }
```

- [ ] **Step 5: The reader surface's loading state**

Rename `ShowReaderSurface`'s `placeholder` parameter to `loading`, and
replace its first line with:

```csharp
        ReaderLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ReaderLoadingRing.IsActive = loading;
```

Then (ruling 5):

- `CloseReaderSessionAsync`: `ShowReaderSurface(loading: true);`, and add
  `HideReaderNotice();` after `HideUnavailableLinkBar();`.
- The TXT start in `RenderCurrentAsync` (the call just before
  `readerLoad = new CancellationTokenSource();`), the first call in
  `OpenHtmlGuideAsync`, and the call inside `if (password is null)` in
  `OpenPdfGuideAsync`: `loading: true`.
- Every other call: `placeholder: false` becomes `loading: false`:

  ```bash
  sed -i '' 's/ShowReaderSurface(placeholder: false/ShowReaderSurface(loading: false/' \
    /Users/ilya.lissoboi/work/desktop-guides/src/DesktopGuides.Production/ShellWindow*.cs
  ```

  Run it first, then make the three `loading: true` edits above.

- [ ] **Step 6: Route loads and ready messages**

In `ShellWindow.xaml.cs`:

| Where | Now | T14.4 |
| --- | --- | --- |
| startup, after the lease | `ShowBusyStatus("Waiting for previous window...")` | `ShowBusyStatus("Waiting for previous window…")` |
| startup, after the lease | `ShowBusyStatus("Loading library...")` | `HideStatus();` then `ShowRouteProgress("Loading library…");` |
| `OpenGuideAsync` | `ShowBusyStatus("Opening guide...")` | `ShowRouteProgress("Opening guide…")` |
| Library route | `ShowBusyStatus("Loading library…")` / `ShowTransientStatus("Library ready.")` | `ShowRouteProgress(...)` / `AnnounceStatus(...)` |
| Game route | `ShowBusyStatus("Loading game…")` / `ShowTransientStatus("Game ready.")` | `ShowRouteProgress(...)` / `AnnounceStatus(...)` |
| Reader route | `ShowBusyStatus("Loading guide…")` | `ShowRouteProgress("Loading guide…")` |
| Settings route | `ShowTransientStatus("Settings ready.")` | `AnnounceStatus("Settings ready.")` |
| Library search | `ShowTransientStatus(matches.Count == 0 ? ...)` | `AnnounceStatus(...)` |
| window background saved | `ShowTransientStatus($"Window background set to {requested}.")` | `AnnounceStatus(...)` |

In `RenderCurrentAsync`, give the `try`/`catch` a `finally`, and turn off the
card's loading state in the catch (ruling 6):

```csharp
        catch (Exception error)
        {
            if (generation == renderGeneration)
            {
                LibraryLoadingState.Visibility = Visibility.Collapsed;
                LibraryProgress.IsActive = false;
                if (navigator.Current is ReaderRoute)
                {
                    ShowReaderSurface(loading: false);
                }
                ShowErrorStatus($"Couldn't load this view: {error.Message}");
            }
            return false;
        }
        finally
        {
            // A newer render owns the line; this one only hides its own.
            if (generation == renderGeneration)
            {
                HideRouteProgress();
            }
        }
```

In `ShellWindow.Progress.cs`, `ShowRestoreStatus` becomes:

```csharp
    private void ShowRestoreStatus(RestoreKind? kind)
    {
        if (kind == RestoreKind.Approximate)
        {
            ShowReaderNotice(ApproximateRestoreMessage);
        }
        else if (kind == RestoreKind.Unavailable)
        {
            ShowWarningStatus(UnavailableRestoreMessage);
        }
        else
        {
            AnnounceStatus("Guide ready.");
        }
    }
```

In `ShellWindow.TextSize.cs`, the save's `ShowTransientStatus(shiftedTextScale == scale ? ... : ...)`
becomes `AnnounceStatus(...)` with the same argument, and the end of
`OnAppearanceRestored` becomes (ruling 14):

```csharp
        shiftedTextScale = readerTextScale;
        ShowReaderNotice(TextSizeSteps.ShiftedNotice);
        AnnounceStatus(TextSizeSteps.ShiftedStatus(readerTextScale));
    }
```

Delete `StatusShowsProblem()` and the comment line above
`OnAppearanceRestored` that mentions covering a warning (ruling 3). In
`ShellWindow.Completion.cs` both `ShowTransientStatus(announcement)` become
`AnnounceStatus(announcement)`; in `ShellWindow.Theme.cs`
`ShowTransientStatus(ThemePresentation.Saved(requested))` becomes
`AnnounceStatus(...)`. In `ShellWindow.PdfReader.cs`, the `HideStatus();` in
`ShowPdfUnlock` becomes `HideRouteProgress();`, and its comment's last
sentence becomes "The panel says what is wrong, so the progress line just
hides."

- [ ] **Step 7: Copy across Production**

Change `Could not` to `Couldn't` in these strings (ruling 1):
`ShellWindow.xaml.cs` (the window background save, opening the library, the
game and the guide, saving Resume, loading this view, refreshing metadata),
`ReaderToolbar.xaml.cs:304`, `GameEditorDialog.xaml.cs:93`,
`AddGameDialog.xaml.cs:241` and `Program.cs:137`. Then check:

```bash
grep -rn 'Could not\|\.\.\."' /Users/ilya.lissoboi/work/desktop-guides/src/DesktopGuides.Production /Users/ilya.lissoboi/work/desktop-guides/src/DesktopGuides.Core --include='*.cs' --include='*.xaml' | grep -v '/obj/'
```

Expected: no output.

In `windows_shell_ui_smoke.ps1`:

- `'Waiting for previous window...'` becomes `('Waiting for previous window' + [char]0x2026)`.
- The three `'Opening guide...'` become `('Opening guide' + [char]0x2026)`.
- Both `'Could not load this view: Stored guide format is invalid.'` become
  `"Couldn't load this view: Stored guide format is invalid."`.
- In `reader-render-error-observed`, after its `Wait-Status`, add
  `Assert-Absent 'ReaderLoading'` (Review Focus 2).

- [ ] **Step 8: Build, then run the groups the status touches**

Sync and run the Production host build: expected success with 0 warnings.
Then:

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production tools/p1/windows_shell_ui_smoke.ps1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(shell): T14.4 quiet status, route progress and the reading-card notice

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
gh workflow run windows-ci.yml --ref feat/p1-t14-4-reader-settings-design -f shell-scope=all -f dev-fast=true
```

Follow the CI loop. Expected: all four `dev-production-shell-ui` shards and
`core-tests` pass. Task 3's checks are now green: no quiet message on the
bar, `ReaderLoading` and `RouteProgress` during the held TXT load, the
toolbar unmoved by size and completion changes, and `ReaderNotice` with the
approximate-restore and shifted texts.

### Task 5: One Back, the metadata line and the narrow header

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (helpers after `Assert-NoOverlap`; `:1220`; `design-language` reader block `:3955-3975`; `:5129`, `:5251`, `:5370`, `:5413`, `:5434`, `:5528`, `:5820-5821`, `:5844`, `:5888`, `:5902`)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (`ReaderPanel` rows and header `:409-453`)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (constructor, `ReaderBackClicked` `:443`, `ShellContentSizeChanged` `:243`)

**Interfaces:**
- Consumes: Task 3's `Wait-Status` quiet path.
- Produces: `Press-Back`, `Assert-SameLine`, `Assert-Below` in the smoke;
  `bool readerNarrow` in `ShellWindow` (Task 7 reads it); the `ReaderHeader`
  grid; the header rows shift up, so the toolbar is `Grid.Row="1"` and the
  card `Grid.Row="2"`.

- [ ] **Step 1: Write the failing checks**

After `Assert-NoOverlap` (and the Task 3 helper), add:

```powershell
    # T14.4: the keyboard's Back. Alt+Left is the shell's accelerator.
    function Press-Back {
        [System.Windows.Forms.SendKeys]::SendWait('%{LEFT}')
    }

    function Assert-SameLine([string] $firstId, [string] $secondId) {
        $a = (Wait-VisibleById $firstId).Current.BoundingRectangle
        $b = (Wait-VisibleById $secondId).Current.BoundingRectangle
        if ([Math]::Abs($a.Top - $b.Top) -gt 2 -or $b.Left -lt $a.Right) {
            throw "'$secondId' isn't after '$firstId' on its line: $a and $b."
        }
    }

    function Assert-Below([string] $lowerId, [string] $upperId) {
        $lower = (Wait-VisibleById $lowerId).Current.BoundingRectangle
        $upper = (Wait-VisibleById $upperId).Current.BoundingRectangle
        if ($lower.Top -lt $upper.Bottom) {
            throw "'$lowerId' isn't below '$upperId': $lower and $upper."
        }
    }
```

In `design-language`, the reader-wide block's
`Assert-InsideWindow 'ReaderBackToGame'` becomes:

```powershell
        Assert-Absent 'ReaderBackToGame'
        Assert-NoOverlap 'PART_BackButton' 'ReaderHeading'
        Assert-SameLine 'ReaderGameName' 'ReaderFormat'
```

and the reader-narrow block's `ReaderBackToGame` lines become:

```powershell
        Assert-InsideWindow 'ReaderHeading'
        Assert-InsideWindow 'ReaderFormat'
        Assert-InsideWindow 'ReaderTextLines'
        Assert-InsideWindow 'CompletionChoice'
        Assert-Below 'CompletionChoice' 'ReaderFormat'
        Assert-NoOverlap 'CompletionChoice' 'ReaderHeading'
        Assert-NoOverlap 'PART_PaneToggleButton' 'ReaderHeading'
        Assert-NoOverlap 'PART_BackButton' 'ReaderHeading'
```

The other `ReaderBackToGame` uses:

| Line | Now | T14.4 |
| --- | --- | --- |
| `:1220` | `[void](Wait-Name 'ReaderBackToGame' 'Back to game')` | `[void](Wait-VisibleById 'ReaderLoading')` |
| `:5129` (an imported guide, possibly HTML) | `Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')` | `Go-Back` |
| `:5251`, `:5370`, `:5413`, `:5434`, `:5844`, `:5902` (seeded TXT guides) | the same `Press-Enter` | `Press-Back` |
| `:5528` | `[void](Wait-Name 'ReaderBackToGame' 'Back to game')` | delete it; after the `Wait-Status` add `Assert-Absent 'GameHeading'` and `Assert-Absent 'LibraryHeading'` (the Reader route is still showing) |
| `:5820-5821` | `$readerBack = Wait-Name ...` / `Invoke-Element $readerBack` | `Go-Back` |
| `:5888` | `Click-Element (Wait-Name 'ReaderBackToGame' 'Back to game')` | `Click-Element (Wait-VisibleById 'PART_BackButton')` |

Check nothing is left:

```bash
grep -n 'ReaderBackToGame' /Users/ilya.lissoboi/work/desktop-guides/tools/p1/windows_shell_ui_smoke.ps1
```

Expected: only the `Assert-Absent 'ReaderBackToGame'` line.

- [ ] **Step 2: Run them to verify they fail**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tools/p1/windows_shell_ui_smoke.ps1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(p1): T14.4 one Back and the Reader metadata line

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
gh workflow run windows-ci.yml --ref feat/p1-t14-4-reader-settings-design -f shell-scope=design -f dev-fast=true
```

Expected: `design` fails with `'ReaderBackToGame' was visible.`

- [ ] **Step 3: The header XAML**

In `ReaderPanel`, remove `ReaderBackButton`, make the row definitions three
(`Auto`, `Auto`, `*`), set the toolbar's `StackPanel` to `Grid.Row="1"` and
`ReaderSurfaceCard` to `Grid.Row="2"`, and replace the header grid with:

```xml
                <Grid x:Name="ReaderHeader"
                      ColumnSpacing="{StaticResource DesktopGuidesSpacing16}">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                    </Grid.RowDefinitions>
                    <StackPanel Spacing="{StaticResource DesktopGuidesSpacing4}">
                        <TextBlock x:Name="ReaderHeading"
                                   Style="{StaticResource DesktopGuidesPageTitleStyle}"
                                   AutomationProperties.AutomationId="ReaderHeading" />
                        <!-- T14.4: a left-aligned grid sizes to its content, so a long
                             game name trims while the format stays beside it. -->
                        <Grid HorizontalAlignment="Left"
                              ColumnSpacing="{StaticResource DesktopGuidesSpacing8}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock x:Name="ReaderGameName"
                                       Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                                       TextWrapping="NoWrap"
                                       TextTrimming="CharacterEllipsis"
                                       AutomationProperties.AutomationId="ReaderGameName" />
                            <TextBlock Grid.Column="1"
                                       Text="·"
                                       Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                                       AutomationProperties.AccessibilityView="Raw" />
                            <TextBlock x:Name="ReaderFormat"
                                       Grid.Column="2"
                                       Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                                       AutomationProperties.AutomationId="ReaderFormat" />
                        </Grid>
                    </StackPanel>
                    <local:GuideCompletionChoice x:Name="ReaderCompletionChoice"
                                                 Grid.Column="1"
                                                 VerticalAlignment="Top" />
                </Grid>
```

The metadata line keeps the secondary body size the game name had, as the
approved mockup shows (ruling 17); the spec's "MetadataStyle" named the
pattern, not the caption size.

- [ ] **Step 4: Alt+Left and the narrow switch**

In `ShellWindow.xaml.cs`, delete `ReaderBackClicked`. In the constructor,
after `Activated += WindowActivated;`:

```csharp
        // T14.4: Alt+Left goes back wherever the title bar's Back would.
        KeyboardAccelerator back = new()
        {
            Key = VirtualKey.Left,
            Modifiers = VirtualKeyModifiers.Menu
        };
        back.Invoked += BackAcceleratorInvoked;
        ShellRoot.KeyboardAccelerators.Add(back);
        ShellRoot.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
```

and after `TitleBarBackRequested`:

```csharp
    private async void BackAcceleratorInvoked(
        KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!AppTitleBar.IsBackButtonEnabled) return;
        args.Handled = true;
        CancelReaderLoad();
        await RunNavigationAsync(GoBackAsync);
    }
```

Add a field next to `statusSequence`: `private bool readerNarrow;`. At the end
of `ShellContentSizeChanged`:

```csharp
        // T14.4: a narrow Reader puts the completion choice under the
        // metadata line (ruling 8).
        readerNarrow = args.NewSize.Width <= narrowBreakpoint;
        Grid.SetRow(ReaderCompletionChoice, readerNarrow ? 1 : 0);
        Grid.SetColumn(ReaderCompletionChoice, readerNarrow ? 0 : 1);
        ReaderCompletionChoice.HorizontalAlignment =
            readerNarrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        ReaderHeader.RowSpacing = readerNarrow
            ? (double)resources["DesktopGuidesSpacing12"]
            : 0;
```

- [ ] **Step 5: Run them to verify they pass**

Sync and run the Production host build (0 warnings). Then:

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(shell): T14.4 one Back with Alt+Left, the metadata line and the narrow header

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
gh workflow run windows-ci.yml --ref feat/p1-t14-4-reader-settings-design -f shell-scope=all -f dev-fast=true
```

Expected: all four shards pass. The `design` shard proves the header
checks; `core` (route traces), `game-actions` and `import` prove each Back
path.

### Task 6: Reader commands

**Files:**
- Modify: `tools/p1/windows_reader_toolbar_ui_smoke.ps1` (the `NarrowToolbar` block, `:430-441`)
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml` (whole file)
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml.cs` (`RefreshCommands`)
- Modify: `src/DesktopGuides.Production/Styles/Reader.xaml`

**Interfaces:**
- Consumes: `ReaderCommandPolicy.ShowsGroupSeparator` (Task 1),
  `DesktopGuidesReaderCommandBarStyle` and `DesktopGuidesTextSizeValueMinWidth` (Task 2).
- Produces: `ReaderGroupSeparator` (`AppBarSeparator`, x:Name `GroupSeparator`).

- [ ] **Step 1: Write the failing check**

In `windows_reader_toolbar_ui_smoke.ps1`, in the `NarrowToolbar` block,
after the `throw 'Narrow CommandBar did not move Zoom in to overflow.'`
check and before `Open-Overflow`:

```powershell
    # T14.4: page movement leaves the bar last (DynamicOverflowOrder).
    if (-not (Find-VisibleByName 'Previous page') -or -not (Find-VisibleByName 'Next page')) {
        throw 'Narrow CommandBar moved Previous or Next page to overflow.'
    }
    if (Find-VisibleByName 'Go to start') {
        throw 'Narrow CommandBar kept Go to start while Zoom in overflowed.'
    }
```

- [ ] **Step 2: Run it to verify it fails**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tools/p1/windows_reader_toolbar_ui_smoke.ps1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(p1): T14.4 page movement leaves the narrow toolbar last

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
gh workflow run windows-ci.yml --ref feat/p1-t14-4-reader-settings-design -f shell-scope=txt
```

No `dev-fast`: that skips `reader-toolbar-ui`. Expected: `reader-toolbar-ui`
fails with `Narrow CommandBar kept Go to start while Zoom in overflowed.`
(today the bar overflows from the right).

- [ ] **Step 3: Drop the label strip**

In `Styles/Reader.xaml`, add to `DesktopGuidesReaderCommandBarStyle`
(ruling 18):

```xml
        <!-- No label strip under compact icons, so the size label centers on them. -->
        <Setter Property="DefaultLabelPosition" Value="Collapsed" />
```

- [ ] **Step 4: Replace `ReaderToolbar.xaml`**

```xml
<UserControl
    x:Class="DesktopGuides.Production.ReaderToolbar"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- T14.4: movement leaves the bar last (order 3), size or zoom before it
         (2), Go to start and Go to end first (1). -->
    <CommandBar x:Name="Commands"
                Style="{StaticResource DesktopGuidesReaderCommandBarStyle}"
                Visibility="Collapsed"
                AutomationProperties.AutomationId="ReaderCommands">
        <CommandBar.PrimaryCommands>
            <AppBarButton x:Name="PageStart"
                          Icon="Previous"
                          Label="Go to start"
                          ToolTipService.ToolTip="Go to start (Ctrl+Home)"
                          AutomationProperties.AcceleratorKey="Ctrl+Home"
                          AutomationProperties.AutomationId="ReaderStart"
                          DynamicOverflowOrder="1"
                          Visibility="Collapsed"
                          Click="PageStartClicked" />
            <AppBarButton x:Name="PreviousPage"
                          Icon="Back"
                          Label="Previous page"
                          ToolTipService.ToolTip="Previous page (Page Up)"
                          AutomationProperties.AcceleratorKey="Page Up"
                          DynamicOverflowOrder="3"
                          Visibility="Collapsed"
                          Click="PreviousPageClicked" />
            <AppBarButton x:Name="NextPage"
                          Icon="Forward"
                          Label="Next page"
                          ToolTipService.ToolTip="Next page (Page Down)"
                          AutomationProperties.AcceleratorKey="Page Down"
                          DynamicOverflowOrder="3"
                          Visibility="Collapsed"
                          Click="NextPageClicked" />
            <AppBarButton x:Name="PageEnd"
                          Icon="Next"
                          Label="Go to end"
                          ToolTipService.ToolTip="Go to end (Ctrl+End)"
                          AutomationProperties.AcceleratorKey="Ctrl+End"
                          AutomationProperties.AutomationId="ReaderEnd"
                          DynamicOverflowOrder="1"
                          Visibility="Collapsed"
                          Click="PageEndClicked" />
            <AppBarSeparator x:Name="GroupSeparator"
                             DynamicOverflowOrder="2"
                             Visibility="Collapsed"
                             AutomationProperties.AutomationId="ReaderGroupSeparator" />
            <AppBarButton x:Name="SmallerText"
                          Icon="FontDecrease"
                          Label="Smaller text"
                          ToolTipService.ToolTip="Smaller text (Ctrl+Minus)"
                          AutomationProperties.AcceleratorKey="Ctrl+Minus"
                          DynamicOverflowOrder="2"
                          Visibility="Collapsed"
                          Click="SmallerTextClicked" />
            <AppBarElementContainer x:Name="TextSizeContainer"
                                    VerticalContentAlignment="Center"
                                    DynamicOverflowOrder="2"
                                    IsTabStop="False"
                                    Visibility="Collapsed">
                <TextBlock x:Name="TextSizeValue"
                           Text="100%"
                           MinWidth="{StaticResource DesktopGuidesTextSizeValueMinWidth}"
                           TextAlignment="Center"
                           VerticalAlignment="Center"
                           AutomationProperties.AutomationId="TextSizeValue"
                           AutomationProperties.Name="Text size 100%"
                           AutomationProperties.LiveSetting="Polite" />
            </AppBarElementContainer>
            <AppBarButton x:Name="LargerText"
                          Icon="FontIncrease"
                          Label="Larger text"
                          ToolTipService.ToolTip="Larger text (Ctrl+Plus)"
                          AutomationProperties.AcceleratorKey="Ctrl+Plus"
                          DynamicOverflowOrder="2"
                          Visibility="Collapsed"
                          Click="LargerTextClicked" />
            <AppBarButton x:Name="ZoomOut"
                          Icon="ZoomOut"
                          Label="Zoom out"
                          ToolTipService.ToolTip="Zoom out (Ctrl+Minus)"
                          AutomationProperties.AcceleratorKey="Ctrl+Minus"
                          DynamicOverflowOrder="2"
                          Visibility="Collapsed"
                          Click="ZoomOutClicked" />
            <AppBarButton x:Name="ZoomIn"
                          Icon="ZoomIn"
                          Label="Zoom in"
                          ToolTipService.ToolTip="Zoom in (Ctrl+Plus)"
                          AutomationProperties.AcceleratorKey="Ctrl+Plus"
                          DynamicOverflowOrder="2"
                          Visibility="Collapsed"
                          Click="ZoomInClicked" />
        </CommandBar.PrimaryCommands>
        <CommandBar.SecondaryCommands>
            <AppBarButton x:Name="GoToPage"
                          Icon="Page"
                          Label="Go to page"
                          ToolTipService.ToolTip="Go to page (Ctrl+G)"
                          AutomationProperties.AcceleratorKey="Ctrl+G"
                          Visibility="Collapsed"
                          Click="GoToPageClicked" />
            <AppBarButton x:Name="FitToWidth"
                          Label="Fit to width"
                          ToolTipService.ToolTip="Fit to width (Ctrl+0)"
                          AutomationProperties.AcceleratorKey="Ctrl+0"
                          Visibility="Collapsed"
                          Click="FitToWidthClicked">
                <AppBarButton.Icon>
                    <!-- FitPage; checked in the WinUI Gallery icon list. -->
                    <FontIcon Glyph="&#xE9A6;" />
                </AppBarButton.Icon>
            </AppBarButton>
            <AppBarButton x:Name="ResetTextSize"
                          Icon="Font"
                          Label="Reset text size"
                          ToolTipService.ToolTip="Reset text size (Ctrl+0)"
                          AutomationProperties.AcceleratorKey="Ctrl+0"
                          Visibility="Collapsed"
                          IsEnabled="False"
                          Click="ResetTextSizeClicked" />
            <AppBarButton x:Name="FindInGuide"
                          Icon="Find"
                          Label="Find in guide"
                          Visibility="Collapsed"
                          Click="FindInGuideClicked" />
        </CommandBar.SecondaryCommands>
    </CommandBar>
</UserControl>
```

Before committing, open the WinUI Gallery's Iconography page (or the Segoe
Fluent Icons reference) and confirm `E9A6` is FitPage. If it isn't, use the
glyph the reference names for fitting a page and record it in Task 9.

- [ ] **Step 5: Show the separator by the rule**

In `ReaderToolbar.xaml.cs` `RefreshCommands`, after
`FindInGuide.Visibility = Show(find);`:

```csharp
        GroupSeparator.Visibility = Show(ReaderCommandPolicy.ShowsGroupSeparator(supported));
```

- [ ] **Step 6: Run it to verify it passes**

Sync and run the Production and ReaderToolbarSmoke host builds (0 warnings).
Then:

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(shell): T14.4 grouped Reader commands with clear glyphs (#32)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
gh workflow run windows-ci.yml --ref feat/p1-t14-4-reader-settings-design -f shell-scope=txt
gh workflow run windows-ci.yml --ref feat/p1-t14-4-reader-settings-design -f shell-scope=all -f dev-fast=true
```

Expected: `reader-toolbar-ui` and the `txt` shard pass on the first run, and
all four shards on the second. Then dispatch `-f shell-scope=pdf -f dev-fast=true`
once more: T10.2's intermittent "no visible Next page" lived on the overflow
re-layout path this task changed, so the PDF group runs twice before moving
on. Download `production-shell-ui-txt` and look at a TXT and an HTML Reader
shot: end-bar glyphs, A-glyphs, the separator only on TXT, the size label
level with the buttons.

### Task 7: The reading surface

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1:2518`
- Modify: `src/DesktopGuides.Production/Styles/Reader.xaml`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (reading card, link bars, `ReaderLoadErrorAction`, `PdfUnlockPanel`)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (`ShowReaderSurface`, `ShellContentSizeChanged`), `ShellWindow.Theme.cs` (`RefreshReaderAppearance`)
- Modify: `src/DesktopGuides.Production/PdfReaderView.xaml`, `PdfReaderView.xaml.cs`, `TextReaderView.xaml`

**Interfaces:**
- Consumes: `ReaderSurfaceCard` (Task 4), `readerNarrow` (Task 5),
  `HtmlReaderStyle.PageColor(ReaderTheme)` and `ReaderThemeNow()` (existing).
- Produces: `UpdateReaderSurfaceChrome()` and the `ReaderPageBackdrop` border.

- [ ] **Step 1: Write the failing check**

At `:2518` the PDF text status gains its period:

```powershell
            [void](Wait-Name 'PdfTextStatus' 'Image-only page; OCR is unavailable.')
```

Commit, push and run `-f shell-scope=pdf -f dev-fast=true`. Expected: the
`pdf` shard fails with `Expected visible 'PdfTextStatus' named 'Image-only
page; OCR is unavailable.'`.

- [ ] **Step 2: Reader spacing tokens**

In `Styles/Reader.xaml`, after `DesktopGuidesReaderNoticeMargin`:

```xml
    <Thickness x:Key="DesktopGuidesReaderGapAbove">0,12,0,0</Thickness>
    <Thickness x:Key="DesktopGuidesReaderGapBelow">0,0,0,12</Thickness>
    <Thickness x:Key="DesktopGuidesPdfTextPaneMargin">16,0,0,0</Thickness>
```

- [ ] **Step 3: One frame for HTML**

In the reading card's grid (Task 4), add as the first child:

```xml
                        <!-- T14.4: an HTML page's own color behind it, so the card is one frame.
                             A separate border keeps the card's ThemeResource fill. -->
                        <Border x:Name="ReaderPageBackdrop"
                                Grid.RowSpan="2"
                                CornerRadius="{StaticResource DesktopGuidesSurfaceCornerRadius}"
                                AutomationProperties.AccessibilityView="Raw" />
```

In `ShellWindow.xaml.cs`, add:

```csharp
    // T14.4: an HTML page fills its card in the page's own color; other
    // readers keep the card's padding, narrower in a narrow window.
    private void UpdateReaderSurfaceChrome()
    {
        bool html = readerSession is HtmlReaderSession;
        string? pageColor = html ? HtmlReaderStyle.PageColor(ReaderThemeNow()) : null;
        ReaderPageBackdrop.Background = pageColor is string hex
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
                255,
                Convert.ToByte(hex[1..3], 16),
                Convert.ToByte(hex[3..5], 16),
                Convert.ToByte(hex[5..7], 16)))
            : null;
        ReaderSurfaceCard.Padding = html
            ? new Thickness(0)
            : (Thickness)Application.Current.Resources[readerNarrow
                ? "DesktopGuidesSurfacePaddingNarrow"
                : "DesktopGuidesSurfacePadding"];
    }
```

Call it at the end of `ShowReaderSurface` (every attach, error and close
passes there), at the end of `ShellContentSizeChanged`, as the first line
of `RefreshReaderAppearance` in `ShellWindow.Theme.cs` (a theme change
recolors the page), and in `CloseReaderSessionAsync` right after
`readerSession = null;`: that method calls `ShowReaderSurface` while the
closing session is still set, so without the second call a closed HTML
guide's chrome would stay while the next guide loads.

- [ ] **Step 4: Link bars, the error action and the unlock panel**

In `ShellWindow.xaml`:

- `ReaderExternalLinkBar` and `ReaderUnavailableLinkBar`: `Margin="0,12,0,0"`
  becomes `Margin="{StaticResource DesktopGuidesReaderGapAbove}"`; the
  external bar's inner `StackPanel` `Margin="0,0,0,12"` becomes
  `Margin="{StaticResource DesktopGuidesReaderGapBelow}"`.
- `ReaderExternalLinkOpen`: `Style="{StaticResource DesktopGuidesPrimaryActionButtonStyle}"`;
  `ReaderExternalLinkDismiss` and `ReaderLoadErrorAction`:
  `Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"`.
- `PdfUnlockPanel`: `MaxWidth="{StaticResource DesktopGuidesPdfUnlockMaxWidth}"`;
  `PdfUnlockError`: `Foreground="{ThemeResource DesktopGuidesDestructiveBrush}"`;
  `PdfUnlockButton`: `Style="{StaticResource DesktopGuidesPrimaryActionButtonStyle}"`.

- [ ] **Step 5: PDF and TXT views**

In `PdfReaderView.xaml`: the three `Margin="0,0,0,12"` become
`Margin="{StaticResource DesktopGuidesReaderGapBelow}"`; `TextPane`'s
`Margin="16,0,0,0"` becomes
`Margin="{StaticResource DesktopGuidesPdfTextPaneMargin}"`; delete the three
`Foreground` attributes (their styles already set them).

In `PdfReaderView.xaml.cs`, replace `private const double NarrowWidth = 720;` with

```csharp
    private static double NarrowWidth =>
        (double)Application.Current.Resources["DesktopGuidesPdfSplitBreakpoint"];
```

replace the `TextPane.Margin = ...` line in `OnSizeChanged` with

```csharp
        TextPane.Margin = (Thickness)Application.Current.Resources[
            isNarrow ? "DesktopGuidesReaderGapAbove" : "DesktopGuidesPdfTextPaneMargin"];
```

and add the period: `{ HasLetters: false } => "Image-only page; OCR is unavailable.",`.

In `TextReaderView.xaml`, both `FontFamily="Consolas"` become
`FontFamily="{StaticResource DesktopGuidesReaderMonoFontFamily}"`, and the
item style's `Padding` value `12,0` becomes
`{StaticResource DesktopGuidesTextLinePadding}`. Check no code names the font:

```bash
grep -n 'Consolas' /Users/ilya.lissoboi/work/desktop-guides/src/DesktopGuides.Production/*.cs
```

Expected: no output (the cell probe reads the XAML font).

- [ ] **Step 6: Run it to verify it passes**

Sync and build Production (0 warnings). Commit
(`feat(shell): T14.4 one reading frame and token-only reader views`), push,
then run `-f shell-scope=all -f dev-fast=true`. Expected: all shards pass.
Download `production-shell-ui-html` and open `html-place-shifted.png` (dark):
the page should fill the card with no inner frame. If WebView2's square
corners show over the card's rounded border, give the HTML case a padding of
`new Thickness(1)` in `UpdateReaderSurfaceChrome` and note it for Task 9
(spec Risks).

### Task 8: Settings sections, the theme drop-down and provider status

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (mode list `:21`; `design-language` Settings block; the `theme-*` block `:4004-4215`; `provider-settings` `:5640-5646`)
- Modify: `tools/p1/windows_shell_install.ps1:1805`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (Settings `StackPanel` `:578-632`)
- Modify: `src/DesktopGuides.Production/ShellWindow.Theme.cs`, `ShellWindow.xaml.cs:324`
- Modify: `src/DesktopGuides.Production/ProviderSettingsCard.xaml`, `ProviderSettingsCard.xaml.cs`

**Interfaces:**
- Consumes: `DesktopGuidesSettingsSectionStyle`, `DesktopGuidesSettingsChoiceMinWidth`,
  `DesktopGuidesCredentialInputMinWidth`, `DesktopGuidesInlineProgressSize` (Task 2);
  `Assert-Below` (Task 5); `ThemePresentation.Options` (existing).
- Produces: `AppThemeSelector` (`ComboBox`) and the three heading ids.

- [ ] **Step 1: Write the failing Settings checks**

In `design-language`, after `Assert-HeadingLevel 'SettingsHeading' 1`:

```powershell
        Assert-HeadingLevel 'SettingsAppearanceHeading' 2
        Assert-HeadingLevel 'SettingsLibraryHeading' 2
        Assert-HeadingLevel 'SettingsGameDataHeading' 2
        Assert-Below 'AppThemeSettingsCard' 'SettingsAppearanceHeading'
        Assert-Below 'WindowMaterialSettingsCard' 'AppThemeSettingsCard'
        Assert-Below 'SettingsLibraryHeading' 'WindowMaterialSettingsCard'
        Assert-Below 'LibraryStorageSettingsCard' 'SettingsLibraryHeading'
        Assert-Below 'SettingsGameDataHeading' 'LibraryStorageSettingsCard'
        Assert-Below 'ProviderSettingsExpander' 'SettingsGameDataHeading'
```

- [ ] **Step 2: Write the failing theme passes**

In the `theme-*` block:

- Replace `Test-ThemeSelected` and `Wait-ThemeShown` with:

  ```powershell
        # The drop-down's one selected item is the shown theme.
        function Wait-ThemeShown([string] $label, [string] $step) {
            $deadline = (Get-Date).AddSeconds(10)
            do {
                $shown = Get-ComboSelection 'AppThemeSelector'
                if ($shown -eq $label) { return }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "$step expected '$label' in the theme drop-down; found '$shown'."
        }
  ```

- In `Assert-ThemeShown`, `Wait-ItemStatus 'AppThemeChoice'` becomes
  `Wait-ItemStatus 'AppThemeSelector'`; in `Open-ThemeSettings`,
  `Wait-EnabledById 'AppThemeChoice'` becomes `Wait-EnabledById 'AppThemeSelector'`.
- Replace `Select-ThemeByKeys` (ruling 10):

  ```powershell
        # Keyboard only: open the drop-down, arrow to the target, commit.
        function Select-ThemeByKeys([string] $from, [string] $to) {
            $order = @('System', 'Light', 'Dark')
            $steps = $order.IndexOf($to) - $order.IndexOf($from)
            Focus-And-Verify 'AppThemeSelector'
            Send-ThemeKeys '%{DOWN}'
            Start-Sleep -Milliseconds 300
            $key = if ($steps -gt 0) { '{DOWN}' } else { '{UP}' }
            for ($i = 0; $i -lt [Math]::Abs($steps); $i++) { Send-ThemeKeys $key }
            Send-ThemeKeys '{ENTER}'
        }
  ```

- Replace the `theme-segmented` branch with (ruling 11):

  ```powershell
        if ($Mode -eq 'theme-selector') {
            # TR14.2 with the drop-down: named, three named items with their
            # ids, keyboard selection, whole at the CI launch size.
            $selector = Wait-VisibleById 'AppThemeSelector'
            if ($selector.Current.Name -ne 'App theme') {
                throw "The theme drop-down is named '$($selector.Current.Name)'."
            }
            Wait-ThemeShown 'System' 'A new library'
            $expand = $selector.GetCurrentPattern(
                [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            $expand.Expand()
            Start-Sleep -Milliseconds 300
            $report.themeItems = [ordered]@{}
            foreach ($label in $themeIds.Keys) {
                $item = Wait-VisibleById $themeIds[$label]
                if ($item.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem -or
                    $item.Current.Name -ne $label) {
                    throw "$($themeIds[$label]) is a $($item.Current.ControlType.ProgrammaticName) named '$($item.Current.Name)'."
                }
                $report.themeItems[$label] = $item.Current.Name
            }
            $expand.Collapse()
            Select-ThemeByKeys 'System' 'Light'
            Wait-ThemeShown 'Light' 'Alt+Down, Down, Enter'
            [void](Wait-Status 'App theme set to Light.')
            Select-ThemeByKeys 'Light' 'System'
            Wait-ThemeShown 'System' 'Alt+Down, Up, Enter'
            [void](Wait-Status 'App theme set to System.')
            # The CI launch size: the drop-down stays whole on its card.
            Resize-ShellWindow 768 519
            Start-Sleep -Milliseconds 400
            Assert-InsideWindow 'AppThemeSelector'
            $report.themeNarrowScreenshot = Save-WindowScreenshot 'theme-selector-narrow'
            Resize-ShellWindow 1500 720
            $report.phases += 'theme-selector'
        }
  ```

- In `theme-error`, both `Wait-FocusedId $themeIds[$ExpectedTheme]` become
  `Wait-FocusedId 'AppThemeSelector'` (a failed save puts the selection back
  and focus stays on the drop-down).
- In the mode list at `:21`, `'theme-segmented'` becomes `'theme-selector'`.
- In `windows_shell_install.ps1:1805`:
  `$report.theme.selector = Run-ShellSmoke 'theme-selector'`; the comment at
  `:1809` becomes `# Windows light: System to Dark (one drop-down choice, one save).`
  Nothing counts saves, so no check changes. `completion-segmented` keeps its
  name: the completion choice is still `Segmented`.

- [ ] **Step 3: Write the provider check**

In `provider-settings`, after `$report.phases += 'credentials-saved'`:

```powershell
        # T14.4: a routine provider message closes by itself (ruling 12).
        [void](Wait-HiddenById 'ProviderSettingsStatus')
        $report.phases += 'saved-status-closes'
```

- [ ] **Step 4: Run them to verify they fail**

Commit (`test(p1): T14.4 Settings sections, theme drop-down and provider status`),
push, then dispatch `-f shell-scope=theme -f dev-fast=true` and
`-f shell-scope=design -f dev-fast=true`. Expected: `theme` fails with
`Expected enabled 'AppThemeSelector'.` and `design` with
`Expected visible 'SettingsAppearanceHeading'.`

- [ ] **Step 5: The Settings XAML**

Replace the Settings `StackPanel` (inside the `ScrollViewer`) with:

```xml
                    <StackPanel Spacing="{StaticResource DesktopGuidesSpacing4}">
                        <TextBlock x:Name="SettingsAppearanceHeading"
                                   Text="Appearance"
                                   Style="{StaticResource DesktopGuidesSettingsSectionStyle}"
                                   AutomationProperties.AutomationId="SettingsAppearanceHeading" />
                        <toolkit:SettingsCard
                            x:Name="AppThemeSettingsCard"
                            Header="App theme"
                            Description="Choose light or dark, or follow Windows."
                            HorizontalAlignment="Stretch"
                            AutomationProperties.AutomationId="AppThemeSettingsCard"
                            AutomationProperties.Name="App theme. Choose light or dark, or follow Windows.">
                            <toolkit:SettingsCard.HeaderIcon>
                                <FontIcon Glyph="&#xE771;"
                                          AutomationProperties.AccessibilityView="Raw" />
                            </toolkit:SettingsCard.HeaderIcon>
                            <ComboBox x:Name="AppThemeSelector"
                                      MinWidth="{StaticResource DesktopGuidesSettingsChoiceMinWidth}"
                                      IsEnabled="False"
                                      SelectionChanged="AppThemeSelectionChanged"
                                      AutomationProperties.AutomationId="AppThemeSelector"
                                      AutomationProperties.Name="App theme" />
                        </toolkit:SettingsCard>
                        <!-- WindowMaterialSettingsCard exactly as before, with
                             MinWidth="{StaticResource DesktopGuidesSettingsChoiceMinWidth}"
                             on WindowMaterialSelector -->
                        <TextBlock x:Name="SettingsLibraryHeading"
                                   Text="Library"
                                   Style="{StaticResource DesktopGuidesSettingsSectionStyle}"
                                   AutomationProperties.AutomationId="SettingsLibraryHeading" />
                        <!-- LibraryStorageSettingsCard exactly as before -->
                        <TextBlock x:Name="SettingsGameDataHeading"
                                   Text="Game data"
                                   Style="{StaticResource DesktopGuidesSettingsSectionStyle}"
                                   AutomationProperties.AutomationId="SettingsGameDataHeading" />
                        <local:ProviderSettingsCard x:Name="ProviderSettings" />
                    </StackPanel>
```

Move `WindowMaterialSettingsCard` and `LibraryStorageSettingsCard` to the
marked places unchanged, apart from the selector's `MinWidth`.

- [ ] **Step 6: The theme drop-down's code**

In `ShellWindow.Theme.cs`, add `using Microsoft.UI.Xaml.Controls;`. Replace
the `AppThemeChoice.SetOptions(...)` statement and the
`AppThemeChoice.ChoiceChanged += ...` line in `InitializeThemeChoice` with:

```csharp
        // T14.4: a drop-down, like Window background; each item keeps its id.
        foreach (ThemeOption option in ThemePresentation.Options)
        {
            ComboBoxItem item = new() { Content = option.Label };
            AutomationProperties.SetAutomationId(item, option.AutomationId);
            AppThemeSelector.Items.Add(item);
        }
```

In `ApplyTheme`, `AppThemeChoice.SelectedIndex = ...` becomes
`AppThemeSelector.SelectedIndex = ThemePresentation.IndexOf(requested);`.
In `UpdateThemeStatus`, `AppThemeChoice` becomes `AppThemeSelector`. Replace
the handler's signature and first lines:

```csharp
    private async void AppThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (applyingThemeSelection || repository is null || AppThemeSelector.SelectedIndex < 0)
        {
            return;
        }
        ThemePreference requested = ThemePresentation.Options[AppThemeSelector.SelectedIndex].Preference;
```

The rest of the handler (the same-theme check, `ApplyTheme`, the save gate,
`AnnounceStatus`, the revert and `ShowErrorStatus`) stays. In
`ShellWindow.xaml.cs:324`, `AppThemeChoice.IsEnabled = true;` becomes
`AppThemeSelector.IsEnabled = true;`. Check nothing is left:

```bash
grep -rn 'AppThemeChoice' /Users/ilya.lissoboi/work/desktop-guides/src /Users/ilya.lissoboi/work/desktop-guides/tools/p1 | grep -v '/obj/'
```

Expected: no output.

- [ ] **Step 7: The provider card**

In `ProviderSettingsCard.xaml`: the three `MinWidth="260"` become
`MinWidth="{StaticResource DesktopGuidesCredentialInputMinWidth}"`;
`ProviderBusy`'s `Width="20" Height="20"` become
`Width="{StaticResource DesktopGuidesInlineProgressSize}"` and the same for
`Height`; `TestConnectionButton` and `RemoveButton` take
`Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"` and
`SaveButton` `Style="{StaticResource DesktopGuidesPrimaryActionButtonStyle}"`.

In `ProviderSettingsCard.xaml.cs`, add
`using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;`,
a field, and replace `Show` (ruling 12):

```csharp
    // T14.4: routine results close after 3 s; errors stay until closed (TR11.3).
    private DispatcherQueueTimer? statusTimer;

    private void Show(InfoBarSeverity severity, string message)
    {
        ProviderSettingsStatus.Severity = severity;
        ProviderSettingsStatus.Message = message;
        AutomationProperties.SetName(ProviderSettingsStatus, message);
        ProviderSettingsStatus.IsOpen = true;
        if (statusTimer is null)
        {
            statusTimer = DispatcherQueue.CreateTimer();
            statusTimer.Interval = TimeSpan.FromSeconds(3);
            statusTimer.IsRepeating = false;
            statusTimer.Tick += (_, _) => ProviderSettingsStatus.IsOpen = false;
        }
        statusTimer.Stop();
        if (severity is InfoBarSeverity.Success or InfoBarSeverity.Informational)
        {
            statusTimer.Start();
        }
    }
```

- [ ] **Step 8: Run them to verify they pass**

Sync and build Production (0 warnings). Commit
(`feat(shell): T14.4 Settings sections, theme drop-down and self-closing provider status`),
push, then:

```bash
gh workflow run windows-ci.yml --ref feat/p1-t14-4-reader-settings-design -f shell-scope=theme
gh workflow run windows-ci.yml --ref feat/p1-t14-4-reader-settings-design -f shell-scope=design -f dev-fast=true
```

Expected: both pass. The first (not `dev-fast`) also uploads
`desktop-guides-production-x64` for the host provider run:

```bash
ID=<the theme run's databaseId>
rm -rf "$TMPDIR/t14-4-msix" && gh run download $ID -n desktop-guides-production-x64 -D "$TMPDIR/t14-4-msix"
MSIX=$(ls "$TMPDIR/t14-4-msix"/*.msix | head -1)
ssh -o BatchMode=yes pcsx2-win "mkdir E:\work\desktop-guides\t14-4-pkg"
scp -q "$MSIX" "pcsx2-win:E:/work/desktop-guides/t14-4-pkg/"
ssh -o BatchMode=yes pcsx2-win "cd /d E:\work\desktop-guides\t14-4 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\windows_shell_install.ps1 -PackagePath E:\work\desktop-guides\t14-4-pkg\\$(basename "$MSIX") -ResultDirectory E:\work\desktop-guides\t14-4-results\providers -ProviderOnly"
```

Expected: exit 0; the result's `providers.settings.phases` includes
`saved-status-closes`, and its credential scan reports no match. Never print
the credential files.

### Task 9: Reader screenshots, the literal check, docs and the PR

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (mode list at the top; a `design-readers` branch after `design-language`)
- Modify: `tools/p1/windows_shell_install.ps1` (a `Run-ReaderDesignScenarios` function after `Run-DesignLanguageScenarios`; the `design` group at `:2494`)
- Modify: the docs listed in Step 5
- Create: `docs/p1/evidence/t14-4-reader-settings/`

**Interfaces:**
- Consumes: everything above; `seed-text-size` (existing), `Open-GuideFromGame`,
  `Go-Back`, `Find-VisibleName`, `Invoke-Element`, `Assert-ShellForeground`,
  `Save-WindowScreenshot` (existing smoke helpers).

- [ ] **Step 1: The `design-readers` mode (ruling 13)**

Add `'design-readers'` to the smoke's mode list next to `'design-language'`.
After the `design-language` branch:

```powershell
    elseif ($Mode -eq 'design-readers') {
        # T14.4: each reader at wide and narrow widths, and the open
        # overflow menu, for screenshot review. Rendering isn't asserted.
        $sizeGame = 'Text Size Game'
        Resize-ShellWindow 1500 720
        Select-Element $sizeGame
        [void](Wait-Name 'GameHeading' $sizeGame)
        [void](Wait-Status 'Game ready.')
        foreach ($reader in @(
            @{ guide = 'ASCII Map Guide'; format = 'TXT'; name = 'txt' },
            @{ guide = 'Static Web Guide'; format = 'HTML'; name = 'html' },
            @{ guide = 'Long PDF Guide'; format = 'PDF'; name = 'pdf' })) {
            Open-GuideFromGame $reader.guide
            [void](Wait-Name 'ReaderHeading' $reader.guide)
            [void](Wait-Name 'ReaderFormat' $reader.format)
            [void](Wait-Status 'Guide ready.' -Seconds 60)
            [void](Wait-VisibleById 'ReaderCommands')
            Assert-Absent 'ShellStatus'
            Start-Sleep -Milliseconds 400
            $report["$($reader.name)WideScreenshot"] =
                Save-WindowScreenshot "reader-$($reader.name)-wide"
            Resize-ShellWindow 600 720
            Start-Sleep -Milliseconds 400
            Assert-InsideWindow 'ReaderHeading'
            Assert-InsideWindow 'ReaderCommands'
            Assert-Below 'CompletionChoice' 'ReaderFormat'
            $report["$($reader.name)NarrowScreenshot"] =
                Save-WindowScreenshot "reader-$($reader.name)-narrow"
            Resize-ShellWindow 1500 720
            Go-Back
            [void](Wait-Name 'GameHeading' $sizeGame)
            [void](Wait-Status 'Game ready.')
        }

        # The open menu is a popup outside the shell root; the shot shows
        # its theme (T14.2 left it unchecked).
        Open-GuideFromGame 'Long PDF Guide'
        [void](Wait-Status 'Guide ready.' -Seconds 60)
        $more = $null
        foreach ($candidate in @('More', 'More options', 'More commands', 'Show more', 'See more')) {
            $more = Find-VisibleName $candidate
            if ($more) { break }
        }
        if (-not $more) { throw 'The PDF Reader toolbar has no More button.' }
        Invoke-Element $more
        $deadline = (Get-Date).AddSeconds(5)
        while (-not (Find-VisibleName 'Fit to width') -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 100
        }
        if (-not (Find-VisibleName 'Fit to width')) { throw 'The overflow menu did not open.' }
        Start-Sleep -Milliseconds 400
        Assert-ShellForeground
        $report.overflowScreenshot = Save-WindowScreenshot 'reader-pdf-overflow'
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Go-Back
        [void](Wait-Name 'GameHeading' $sizeGame)
        $report.phases += 'design-readers'
    }
```

In `windows_shell_install.ps1`, after `Run-DesignLanguageScenarios`:

```powershell
# T14.4: each reader at wide and narrow widths in light and dark.
function Run-ReaderDesignScenarios {
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    [void](Invoke-ShellSeed @('seed-text-size', $dataRoot, $fixtureRoot))
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.designReadersLight = Run-ShellSmoke 'design-readers' `
            -ResultName 'design-readers-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.designReadersDark = Run-ShellSmoke 'design-readers' `
            -ResultName 'design-readers-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}
```

and in the `design` group, after `Run-MaterialScenarios`, call
`Run-ReaderDesignScenarios`.

Commit (`test(p1): T14.4 Reader design screenshots in light and dark`),
push and run `-f shell-scope=design -f dev-fast=true`. Expected: pass, with
`design-readers-light` and `design-readers-dark` results and 14 new
screenshots in `production-shell-ui-design`. Review them: one Back, "Game ·
TXT", the end-bar and A-glyphs, the separator on TXT and PDF only, one HTML
frame in dark, the menu in the window's theme.

- [ ] **Step 2: The literal-value check**

```bash
F=/Users/ilya.lissoboi/work/desktop-guides/src/DesktopGuides.Production
P='(Margin|Padding|MinWidth|MaxWidth|Width|Height|FontFamily|Foreground|Background|Spacing)="[^{]'
grep -nE "$P" $F/ReaderToolbar.xaml $F/TextReaderView.xaml $F/PdfReaderView.xaml $F/ProviderSettingsCard.xaml | grep -v 'Definition'
awk '/x:Name="ReaderPanel"/,0' $F/ShellWindow.xaml | grep -nE "$P" | grep -v 'Definition'
```

Expected: no output (row and column definitions are layout, not literals).
Anything else gets a token from Task 2 or a new one in `Reader.xaml`.

- [ ] **Step 3: Narrator (ruling 16)**

Ask the user whether they want to listen now. If yes, they install the
latest CI MSIX on VEGA, turn Narrator on, open a TXT guide ("Guide
ready."), press Ctrl+Plus ("Text size 110%."), and open the Picture Web
Guide and press Ctrl+Plus ("Text size 110%. Your place may have shifted.").
Record what they heard, or that they deferred it to T16.2, in the spec's
Verification.

- [ ] **Step 4: The full run**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides push
gh workflow run windows-ci.yml --ref feat/p1-t14-4-reader-settings-design
```

No inputs: every job, all four shards, `reader-toolbar-ui` and both
package builds. Expected: all pass. Record the run id, the Core and
Infrastructure counts from `core-tests` (Core 906; Infrastructure 540), and
any rerun.

- [ ] **Step 5: Docs**

- `docs/p1/t14-4-reader-settings-design.md`:
  - Status: implemented, with the full run's id.
  - Add `## Implementation notes` listing rulings 1–18 in a sentence each,
    plus the `E9A6` check result and whether WebView2's corners needed the
    1 px inset.
  - Add `## Verification`: the run ids per task, the counts, the host
    provider run, the screenshots reviewed, the Narrator result or deferral.
- `docs/p1/t11-design-language-plan.md` › Copy:
  - Replace "Loading uses a top `InfoBar`; routine ready messages close
    after three seconds, while warnings and errors remain dismissible." with:
    "Route loads show a progress line over the content and, in the Reader,
    in the reading card. Ready messages and changes the page already shows
    are announced, not shown. An approximate reading place is said in the
    reading card until dismissed. Other confirmations close after three
    seconds; warnings and errors stay until dismissed (T14.4)."
  - In its examples, `Back to game` becomes `Open in browser`.
  - Add a `### Icons` section after `### Copy`: "Segoe Fluent monochrome
    icons from the `Symbol` enum where one names the action. In the Reader,
    Go to start and Go to end use the end-bar glyphs (`Previous`, `Next`)
    beside the ← / → page arrows; text size uses `FontDecrease` /
    `FontIncrease`, distinct from PDF's magnifier zoom (T14.4, #32)."
- `docs/p1/e2e-testing.md`:
  - The Design language row adds the `design-readers` pass and the quiet
    status.
  - The Theme row becomes the `theme-selector` drop-down gate.
  - The TXT position row loses "after the 'Guide ready.' status has closed
    (it takes rows from the reader while open)".
- `docs/p1/implementation-plan.md`:
  - T14.4 row: "the T14.2 `Segmented` choice" becomes "the App theme
    drop-down", and it ends "High contrast, Windows text scale and 200%
    display scale are checked in T16.2."
  - T14.2 row: add "T14.4 moved the choice to a drop-down at the user's
    request."
  - Add a T14.4 paragraph after T14.3's, in the same form.
- `docs/work-breakdown.md`:
  - T14.2: add "T14.4 moved it to a drop-down that matches Window
    background, at the user's request."
  - T14.4: add "Implemented; see [the design](p1/t14-4-reader-settings-design.md)."
- `docs/p1-technical-design.md`:
  - The component rows that map `Segmented` to System/Light/Dark (the
    T14.1–T14.4 row and the `Segmented` row) now name the completion choice
    only.
  - Any sentence saying an inexact restore "says so in the status bar" now
    says "in the reading card".

```bash
grep -n 'Segmented' /Users/ilya.lissoboi/work/desktop-guides/docs/p1-technical-design.md /Users/ilya.lissoboi/work/desktop-guides/docs/work-breakdown.md /Users/ilya.lissoboi/work/desktop-guides/docs/p1/implementation-plan.md
grep -n 'status bar' /Users/ilya.lissoboi/work/desktop-guides/docs/p1-technical-design.md /Users/ilya.lissoboi/work/desktop-guides/docs/p1/implementation-plan.md
```

Expected after the edits: `Segmented` appears only for the completion
choice or as history ("T14.2 … moved"), and no "status bar" claim about an
inexact restore remains.

- [ ] **Step 6: Evidence**

From the full run, download `production-shell-ui-design`,
`production-shell-ui-pdf` (theme) and `reader-toolbar-ui`, and copy into
`docs/p1/evidence/t14-4-reader-settings/`:

- the 14 `design-readers` screenshots and their two JSON results;
- the light and dark `design-language` Settings wide and narrow shots;
- `theme-selector-narrow`;
- the toolbar smoke JSON;
- the host provider result JSON (Task 8), after confirming its credential
  scan found nothing.

Commit with `docs(p1): T14.4 verification and evidence`.

- [ ] **Step 7: The PR**

Open it with the GitHub MCP `create_pull_request` (base `main`, head
`feat/p1-t14-4-reader-settings-design`), then bind it with
`mcp__ccd_pr__bind_pr`. The body:

- **Target task:** T14.4 (TR11.3, TR14.3), closing #32.
- **Prerequisites:** T05.4, T08.3, T09.2, T10.2, T11.3 and T14.2, all
  merged.
- **Intended outcome:** the spec's Intent bullets.
- **Changes:** one line per task.
- **Verification:** the full run, counts, host provider run, Narrator.
- **Screenshots:** TXT, HTML and PDF wide (light), HTML dark, Settings
  dark, and the overflow menu. Link the committed evidence files as
  `https://github.com/ilya-slalom/desktop-guides/blob/<sha>/docs/p1/evidence/t14-4-reader-settings/<file>.png?raw=true`.
- **Deferred:** high contrast, text scale, 200% display scale and the full
  Narrator pass go to T16.2.
- The body ends with the Claude Code attribution line.

Then add the PR's rows to `docs/p1/results.md` (merged-PR summary, filled
in at merge) and `docs/progress.md` in a final docs commit, as the earlier
tasks did.

---

## Self-review notes

- Every spec section maps to a task:
  - Status behaviour → 3, 4.
  - Reader header → 5; commands → 1, 6; reading surface → 7; copy → 1, 4.
  - Settings → 8.
  - Shared resources and Toolkit lock → 2, 7, 9 (Step 2).
  - Core → 1.
  - Installed checks → 3, 5, 6, 8, 9; docs → 9.
- The spec's Risks each have a check: WebView2 corners (Task 7 Step 6),
  left-aligned overflow (Task 6), the overflow flake (Task 6's second PDF
  run), announcements (Task 9 Step 3), card-notice resize (Task 3 Step 5),
  shared bar (Task 4's all-shard run).
