# T14.2 Theme Setting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In Settings, the user picks System, Light or Dark from the
keyboard. The shell, dialogs and title bar switch at once, the choice
survives a restart, and Windows high contrast always wins. The control is
a Toolkit `Segmented` that UI Automation reads as selectable items. Once
it passes that gate, the T13.1 completion choice moves to the same
control.

**Architecture:**

- Core gains `ThemePresentation`: the options, the pure `Resolve` and the
  copy.
- The app gains `BoundedChoice`, a `Segmented` subclass bound to
  `ItemsSource`, used by the Settings card and later by
  `GuideCompletionChoice`.
- `ShellWindow.Theme.cs` owns applying, saving and reverting the theme.
  `DialogSurface` also sets each dialog's theme.
- The installed `theme` group runs the UIA gate first, then the change,
  restart, offline and error checks.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.5.1),
CommunityToolkit.WinUI.Controls.Segmented 8.2.251219, SQLite, xUnit,
PowerShell UI Automation.

**Spec:** [t14-2-theme-setting-design.md](t14-2-theme-setting-design.md)

## Global Constraints

**Branch:** `feat/p1-t14-2-theme-setting`, already checked out. The spec
is commit `4542c64`.

**Tooling:**

- There is no local `dotnet` or `pwsh`. Builds and tests run on the Windows
  host `pcsx2-win` or in CI.
- Host sync (staging folder `E:\work\desktop-guides\t14-2`, not a git
  checkout; create it once with `ssh -o BatchMode=yes pcsx2-win "mkdir E:\work\desktop-guides\t14-2"`):

  ```bash
  R=/Users/ilya.lissoboi/work/desktop-guides
  git -C $R ls-files -co --exclude-standard -z -- . ':!.claude' |
    tar -C $R --null -T - -cf - |
    ssh -o BatchMode=yes pcsx2-win "tar -xf - -C E:\work\desktop-guides\t14-2"
  ```

- Host commands (the host's default shell is `cmd`):

  ```bash
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-2\tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj"
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t14-2\tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t14-2\src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64"
  ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t14-2\tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release"
  ```

  After a package change, restore first with
  `dotnet restore <csproj> -p:Platform=x64` (without `--locked-mode`) so
  the lock file updates. Then copy the host's
  `src\DesktopGuides.Production\packages.lock.json` back with `scp` and
  diff it against the expected entry in Task 2.
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
  `git -C`, never `cd`.
- Installed runs follow [e2e-testing.md](e2e-testing.md). Every change to
  the Windows app theme goes through `Set-AppThemePreference` inside a
  `try` whose `finally` calls `Restore-AppThemePreference`. Don't switch
  on high contrast (T16.2 owns that pass). No firewall rule. If a run
  needs an elevated scheduled task, stop and ask.

**Values (verbatim from the spec):**

- Options in order: `System`, `Light`, `Dark`; labels `System`, `Light`,
  `Dark`; AutomationIds `ThemeSystem`, `ThemeLight`, `ThemeDark`.
- `Resolve(preference, highContrast)` returns `FollowSystem`, `Light` or
  `Dark`. High contrast always gives `FollowSystem`. An undefined
  preference gives `FollowSystem`.
- Status `App theme set to <Label>.`; save failure
  `Could not save the app theme: <message>`.
- Card `AppThemeSettingsCard`, header `App theme`, description
  `Choose light or dark, or follow Windows.`, automation name
  `App theme. Choose light or dark, or follow Windows.`. Choice
  `AppThemeChoice`, automation name `App theme`.
- Choice `ItemStatus`: `Light` or `Dark` for an explicit choice;
  `System (<ActualTheme>)` for System, for example `System (Light)`.
- Package `CommunityToolkit.WinUI.Controls.Segmented` at `8.2.251219`.
- Completion options `In progress` (`CompletionInProgress`) and
  `Complete` (`CompletionComplete`); the choice keeps AutomationId
  `CompletionChoice`.

**Rulings this plan makes against the spec** (Task 7 records them in the
spec's implementation notes):

1. **The high-contrast status.** The spec gives `ItemStatus` only for the
   normal cases. While high contrast overrides an explicit choice, the
   status reads `<Label> (high contrast)`, for example
   `Dark (high contrast)`. With System it stays `System (<ActualTheme>)`.
2. **Saves are serialized, and a failure reverts to the last committed
   theme.** Two quick arrow presses start two saves. A `SemaphoreSlim`
   runs them in order. A failed save reverts only if no newer choice was
   made since, and it reverts to the last *committed* theme, not the one
   before the failed request.
3. **Labels are set on the container, with no `DisplayMemberPath`.**
   `PrepareContainerForItemOverride` sets the container's `Content` to the
   label. Reflection binding to a record would need `[Bindable]` metadata.
4. **The offline check runs inside the other modes.** `theme-change` and
   `theme-restored` end with `Assert-NoRemoteConnections`; there's no
   separate `theme-offline` smoke mode. The report records the count under
   each mode's `remoteConnections`.
5. **Five smoke modes:** `theme-segmented`, `theme-change`,
   `theme-restored`, `theme-error` and `theme-error-retry`. The restart
   check is `theme-restored` (and the start assertion of `theme-change`)
   after a relaunch.
6. **The dialog and title bar report their theme for the tests.**
   `DialogSurface` sets the dialog's `ItemStatus` to its `ActualTheme` when
   it opens. `ApplyTheme` sets `AppTitleBar`'s `ItemStatus` to the
   `TitleBarTheme` it applied.
7. **The error check switches to Light.** It's one arrow press, so one
   save times out (30 s) under the 120 s lock.
8. **The gate records each item's supported patterns** in the report
   before it asserts, so a failure shows which peer UIA sees.

## Review Focus

1. **Rapid arrow presses.** System to Dark passes through Light, which
   starts two saves. The stored value must be Dark and the status must
   read `App theme set to Dark.`. Task 4's handler serializes the saves.
   `theme-change` (Windows light, System to Dark) checks the stored value
   after the relaunch.
2. **A failed save while a newer choice is pending.** It must not put back
   a theme the user has already moved past. Task 4's `requested ==
   requestedTheme` guard covers it; reviewers check that branch.
3. **Ctrl+Space clears a `ListViewBase` selection.** A choice always has
   one selected item. `BoundedChoice` re-selects the last selection on the
   dispatcher, and both callers ignore the empty state. Task 2 pins this
   in code; the reviewer checks the guard.
4. **System follows a live Windows theme change.** `ShellRoot`'s
   `ActualThemeChanged` refreshes the status, so `System (Dark)` doesn't
   go stale. `theme-restored` checks it after a relaunch; a live switch
   isn't checked.
5. **A dialog opened before the first theme change.** Every dialog gets
   the current theme from the same field when it's created. Task 4
   changes all seven `DialogSurface.Apply` call sites, so none keeps the
   old two-argument form, which no longer compiles.

---
### Task 1: Core `ThemePresentation`

**Files:**
- Create: `src/DesktopGuides.Core/Library/ThemePresentation.cs`
- Test: `tests/DesktopGuides.Core.Tests/ThemePresentationTests.cs`

**Interfaces:**
- Consumes: `ThemePreference { System, Light, Dark }` from
  `src/DesktopGuides.Core/Library/LibraryModels.cs`.
- Produces (namespace `DesktopGuides.Core.Library`):
  - `enum AppliedTheme { FollowSystem, Light, Dark }`
  - `sealed record ThemeOption(ThemePreference Preference, string Label, string AutomationId)`
  - `static IReadOnlyList<ThemeOption> Options`
  - `static int IndexOf(ThemePreference preference)` (0 for an undefined value)
  - `static AppliedTheme Resolve(ThemePreference preference, bool highContrast)`
  - `static string Status(ThemePreference preference, AppliedTheme applied, string actualTheme)`
  - `static string Saved(ThemePreference preference)`
  - `static string SaveFailed(string message)`

- [ ] **Step 0: Record the baseline**

Sync to the host (Global Constraints) and run both test projects. Write
the two passing counts into the ledger as `Baseline: Core.Tests <n>,
Infrastructure.Tests <m>`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ThemePresentationTests
{
    [Fact]
    public void OptionsAreSystemLightDarkInOrder()
    {
        Assert.Equal(
            [
                new ThemeOption(ThemePreference.System, "System", "ThemeSystem"),
                new ThemeOption(ThemePreference.Light, "Light", "ThemeLight"),
                new ThemeOption(ThemePreference.Dark, "Dark", "ThemeDark")
            ],
            ThemePresentation.Options);
    }

    [Theory]
    [InlineData(ThemePreference.System, 0)]
    [InlineData(ThemePreference.Light, 1)]
    [InlineData(ThemePreference.Dark, 2)]
    [InlineData((ThemePreference)99, 0)]
    public void IndexOfFindsTheOption(ThemePreference preference, int index) =>
        Assert.Equal(index, ThemePresentation.IndexOf(preference));

    [Theory]
    [InlineData(ThemePreference.System, AppliedTheme.FollowSystem)]
    [InlineData(ThemePreference.Light, AppliedTheme.Light)]
    [InlineData(ThemePreference.Dark, AppliedTheme.Dark)]
    [InlineData((ThemePreference)99, AppliedTheme.FollowSystem)]
    public void WithoutHighContrastThePreferenceApplies(
        ThemePreference preference, AppliedTheme applied) =>
        Assert.Equal(applied, ThemePresentation.Resolve(preference, highContrast: false));

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void HighContrastAlwaysFollowsSystem(ThemePreference preference) =>
        Assert.Equal(AppliedTheme.FollowSystem, ThemePresentation.Resolve(preference, highContrast: true));

    [Theory]
    [InlineData(ThemePreference.Light, AppliedTheme.Light, "Light", "Light")]
    [InlineData(ThemePreference.Dark, AppliedTheme.Dark, "Dark", "Dark")]
    [InlineData(ThemePreference.System, AppliedTheme.FollowSystem, "Light", "System (Light)")]
    [InlineData(ThemePreference.System, AppliedTheme.FollowSystem, "Dark", "System (Dark)")]
    [InlineData(ThemePreference.Dark, AppliedTheme.FollowSystem, "Light", "Dark (high contrast)")]
    public void StatusNamesTheAppliedTheme(
        ThemePreference preference, AppliedTheme applied, string actual, string expected) =>
        Assert.Equal(expected, ThemePresentation.Status(preference, applied, actual));

    [Fact]
    public void SavedNamesTheChoice() =>
        Assert.Equal("App theme set to Dark.", ThemePresentation.Saved(ThemePreference.Dark));

    [Fact]
    public void SaveFailedCarriesTheMessage() =>
        Assert.Equal("Could not save the app theme: database is locked",
            ThemePresentation.SaveFailed("database is locked"));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Sync, then run Core.Tests on the host.
Expected: build FAIL, "The name 'ThemePresentation' does not exist".

- [ ] **Step 3: Write the implementation**

```csharp
namespace DesktopGuides.Core.Library;

/// <summary>The theme the shell applies, independent of WinUI types.</summary>
public enum AppliedTheme
{
    FollowSystem,
    Light,
    Dark
}

public sealed record ThemeOption(ThemePreference Preference, string Label, string AutomationId);

/// <summary>The App theme choice's options, resolution, and status copy.</summary>
public static class ThemePresentation
{
    public static IReadOnlyList<ThemeOption> Options { get; } =
    [
        new(ThemePreference.System, "System", "ThemeSystem"),
        new(ThemePreference.Light, "Light", "ThemeLight"),
        new(ThemePreference.Dark, "Dark", "ThemeDark")
    ];

    public static int IndexOf(ThemePreference preference) =>
        Math.Max(0, Options.ToList().FindIndex(option => option.Preference == preference));

    // Windows high contrast takes precedence over the stored preference.
    public static AppliedTheme Resolve(ThemePreference preference, bool highContrast) =>
        highContrast ? AppliedTheme.FollowSystem : preference switch
        {
            ThemePreference.Light => AppliedTheme.Light,
            ThemePreference.Dark => AppliedTheme.Dark,
            _ => AppliedTheme.FollowSystem
        };

    // The installed checks read this as the choice's ItemStatus.
    public static string Status(ThemePreference preference, AppliedTheme applied, string actualTheme) =>
        applied != AppliedTheme.FollowSystem ? Label(preference)
            : preference is ThemePreference.Light or ThemePreference.Dark
                ? $"{Label(preference)} (high contrast)"
                : $"System ({actualTheme})";

    public static string Saved(ThemePreference preference) => $"App theme set to {Label(preference)}.";

    public static string SaveFailed(string message) => $"Could not save the app theme: {message}";

    private static string Label(ThemePreference preference) => Options[IndexOf(preference)].Label;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Sync, then run Core.Tests on the host.
Expected: PASS, with the baseline count plus 19 (seven tests expanding
to 19 cases).

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core/Library/ThemePresentation.cs tests/DesktopGuides.Core.Tests/ThemePresentationTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(core): T14.2 theme presentation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: `BoundedChoice` and the App theme card

The card shows the stored theme and the choice moves, but nothing is
applied or saved yet (Task 4). That is enough for the UIA gate.

**Files:**
- Modify: `Directory.Packages.props` (after the `HeaderedControls` pin)
- Modify: `src/DesktopGuides.Production/DesktopGuides.Production.csproj:26`
- Modify: `src/DesktopGuides.Production/packages.lock.json` (before `SettingsControls`)
- Create: `src/DesktopGuides.Production/BoundedChoice.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (Settings `StackPanel`, before `WindowMaterialSettingsCard`)
- Create: `src/DesktopGuides.Production/ShellWindow.Theme.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (constructor; `InitializeCoreAsync` settings read)

**Interfaces:**
- Consumes: `ThemePresentation.Options`, `ThemePresentation.IndexOf` (Task 1).
- Produces:
  - `public sealed record BoundedChoiceOption(string Label, string AutomationId)`
  - `public partial class BoundedChoice : Segmented` with
    `void SetOptions(IReadOnlyList<BoundedChoiceOption> options)` and
    `Control? ContainerAt(int index)`
  - `ShellWindow.ShowTheme(ThemePreference requested)` in
    `ShellWindow.Theme.cs`: selects the option without raising a save
    (guarded by `applyingThemeSelection`). Task 4 replaces it with
    `ApplyTheme`.
  - XAML names `AppThemeSettingsCard` and `AppThemeChoice`.

TDD skip: this task is a control and markup with no logic that runs
off Windows. Task 3's installed gate is its test.

- [ ] **Step 1: Restore the Segmented package**

`Directory.Packages.props`, after the `HeaderedControls` pin:

```xml
    <PackageVersion Include="CommunityToolkit.WinUI.Controls.Segmented"
                    Version="8.2.251219" />
```

The Production csproj, after the `HeaderedControls` reference:

```xml
    <PackageReference Include="CommunityToolkit.WinUI.Controls.Segmented" />
```

`packages.lock.json`, under the `net10.0-windows10.0.19041` direct
dependencies, before `CommunityToolkit.WinUI.Controls.SettingsControls`.
This is the entry commit `253449e` added and `5e88a6e` removed:

```json
      "CommunityToolkit.WinUI.Controls.Segmented": {
        "type": "Direct",
        "requested": "[8.2.251219, )",
        "resolved": "8.2.251219",
        "contentHash": "HobW6MT+WpEvLSmjcSEw84lAxk4XYxI7v1a4ZWjva5lfz1W2kAXenWWlRl+Kypaod8axcy+m3Gw9iHoHm9/M4g==",
        "dependencies": {
          "CommunityToolkit.WinUI.Extensions": "8.2.251219",
          "Microsoft.WindowsAppSDK": "1.6.250108002"
        }
      },
```

Check `git -C /Users/ilya.lissoboi/work/desktop-guides show 5e88a6e -- src/DesktopGuides.Production/packages.lock.json`.
If it removed more than this one entry (for example a transitive
`CommunityToolkit.WinUI.Extensions` entry), restore those lines too, so
the result is the reverse of that hunk.

- [ ] **Step 2: Write `BoundedChoice`**

```csharp
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed record BoundedChoiceOption(string Label, string AutomationId);

// A Toolkit Segmented bound to data, so each item is a generated container.
// T13.1 found that items declared in XAML exposed no SelectionItem pattern.
public partial class BoundedChoice : Segmented
{
    private int lastSelectedIndex = -1;

    public BoundedChoice()
    {
        SelectionChanged += KeepOneSelected;
    }

    public void SetOptions(IReadOnlyList<BoundedChoiceOption> options) =>
        ItemsSource = options;

    public Control? ContainerAt(int index) => ContainerFromIndex(index) as Control;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is ContentControl container && item is BoundedChoiceOption option)
        {
            container.Content = option.Label;
            AutomationProperties.SetName(container, option.Label);
            AutomationProperties.SetAutomationId(container, option.AutomationId);
        }
    }

    // Ctrl+Space can clear a ListViewBase selection; a choice always has one.
    private void KeepOneSelected(object sender, SelectionChangedEventArgs args)
    {
        if (SelectedIndex >= 0)
        {
            lastSelectedIndex = SelectedIndex;
            return;
        }
        if (lastSelectedIndex >= 0)
        {
            int restore = lastSelectedIndex;
            DispatcherQueue.TryEnqueue(() => SelectedIndex = restore);
        }
    }
}
```

The class is `partial` and not `sealed`, so Task 3's fallback can
override `GetContainerForItemOverride` in the same file. Don't set
`DefaultStyleKey`: `Segmented`'s constructor already sets
`typeof(Segmented)`, so the Toolkit style applies.

- [ ] **Step 3: Add the card**

In `ShellWindow.xaml`, before `WindowMaterialSettingsCard`:

```xml
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
                            <local:BoundedChoice x:Name="AppThemeChoice"
                                                 IsEnabled="False"
                                                 AutomationProperties.AutomationId="AppThemeChoice"
                                                 AutomationProperties.Name="App theme" />
                        </toolkit:SettingsCard>
```

- [ ] **Step 4: Show the stored theme**

Create `ShellWindow.Theme.cs`:

```csharp
using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    private bool applyingThemeSelection;

    private void InitializeThemeChoice() =>
        AppThemeChoice.SetOptions(
            ThemePresentation.Options
                .Select(option => new BoundedChoiceOption(option.Label, option.AutomationId))
                .ToList());

    private void ShowTheme(ThemePreference requested)
    {
        applyingThemeSelection = true;
        AppThemeChoice.SelectedIndex = ThemePresentation.IndexOf(requested);
        applyingThemeSelection = false;
    }
}
```

In the `ShellWindow` constructor, call `InitializeThemeChoice();` just
before `ApplyWindowMaterial(WindowMaterial.Mica);`, and
`ShowTheme(ThemePreference.System);` just after it.

In `InitializeCoreAsync`, replace the material-only settings read with
one read for both values:

```csharp
            AppSettings? settings = null;
            try
            {
                settings = await repository.GetSettingsAsync();
            }
            catch (InvalidDataException)
            {
                // An invalid setting; RenderCurrentAsync reports it.
            }
            WindowMaterial requestedMaterial = settings?.WindowMaterial ?? WindowMaterial.Mica;
            ShowTheme(settings?.Theme ?? ThemePreference.System);
            ApplyWindowMaterial(requestedMaterial);
            WindowMaterialSelector.IsEnabled = true;
            AppThemeChoice.IsEnabled = true;
```

Keep the rest of the method as it is.

- [ ] **Step 5: Build on the host**

Sync, restore the Production project (Global Constraints), then build it.
Expected: build succeeds, and the host's restored `packages.lock.json`
matches the committed file (`diff` prints nothing).

- [ ] **Step 6: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add Directory.Packages.props src/DesktopGuides.Production
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(shell): T14.2 App theme card on a bound Segmented

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: The UIA gate (`theme` group, `theme-segmented`)

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the `Mode` `ValidateSet`; a new `theme-*` branch before `elseif ($Mode -eq 'material')`)
- Modify: `tools/p1/windows_shell_install.ps1` (`*Only` switches, `$scenarioGroups`, a new `Run-ThemeScenarios` after `Run-CompletionScenarios`, the group dispatch)
- Modify: `.github/workflows/windows-ci.yml` (`shell-scope` options; the `pdf` shard)
- Conditional (Step 6): `src/DesktopGuides.Production/BoundedChoice.cs`

**Interfaces:**
- Consumes: `AppThemeChoice` and the `ThemeSystem`, `ThemeLight` and
  `ThemeDark` containers (Task 2); `seed-design` in ShellSeed;
  `Set-AppThemePreference`, `Get-AppThemePreference` and
  `Restore-AppThemePreference`.
- Produces:
  - Group `theme` (switch `-ThemeOnly`), run after `completion` and before
    `import`.
  - Smoke helpers inside the `theme-*` branch, reused by Task 5:
    `Test-ThemeSelected([string] $id)`,
    `Wait-ThemeShown([string] $label, [string] $step)`,
    `Open-ThemeSettings`, `Send-ThemeKeys([string] $keys)`.
  - Report key `themeItems` (per item: `name`, `controlType`,
    `patterns`).

- [ ] **Step 1: Add the smoke mode**

Add `'theme-segmented'` to the `Mode` `ValidateSet` (on the line after
the `completion-*` modes). Before `elseif ($Mode -eq 'material') {`,
insert:

```powershell
    elseif ($Mode -like 'theme-*') {
        $themeIds = [ordered]@{ System = 'ThemeSystem'; Light = 'ThemeLight'; Dark = 'ThemeDark' }

        function Test-ThemeSelected([string] $id) {
            $item = Find-ById $id
            if (-not $item -or $item.Current.IsOffscreen) { return $false }
            return $item.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
        }

        # The shown theme is the only selected item.
        function Wait-ThemeShown([string] $label, [string] $step) {
            $deadline = (Get-Date).AddSeconds(10)
            do {
                $selected = @($themeIds.Keys | Where-Object { Test-ThemeSelected $themeIds[$_] })
                if ($selected.Count -eq 1 -and $selected[0] -eq $label) { return }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "$step expected '$label' as the only selected theme; found '$($selected -join ',')'."
        }

        function Send-ThemeKeys([string] $keys) {
            [System.Windows.Forms.SendKeys]::SendWait($keys)
        }

        function Open-ThemeSettings {
            Select-Element 'Settings'
            [void](Wait-Name 'AppThemeSettingsCard' 'App theme. Choose light or dark, or follow Windows.')
            [void](Wait-EnabledById 'AppThemeChoice')
        }

        Resize-ShellWindow 1500 720
        Open-ThemeSettings

        if ($Mode -eq 'theme-segmented') {
            # The UIA gate: three list items with their names, a readable
            # selected state, and selection that follows the arrow keys.
            $choice = Wait-VisibleById 'AppThemeChoice'
            if ($choice.Current.Name -ne 'App theme') {
                throw "The theme choice is named '$($choice.Current.Name)'."
            }
            $report.themeItems = [ordered]@{}
            foreach ($label in $themeIds.Keys) {
                $item = Wait-VisibleById $themeIds[$label]
                $report.themeItems[$label] = [ordered]@{
                    name = $item.Current.Name
                    controlType = $item.Current.ControlType.ProgrammaticName
                    patterns = @($item.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName })
                }
            }
            foreach ($label in $themeIds.Keys) {
                $item = Wait-VisibleById $themeIds[$label]
                if ($item.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem -or
                    $item.Current.Name -ne $label) {
                    throw "$($themeIds[$label]) is a $($item.Current.ControlType.ProgrammaticName) named '$($item.Current.Name)'."
                }
            }
            Wait-ThemeShown 'System' 'A new library'
            (Wait-VisibleById 'ThemeSystem').SetFocus()
            Wait-FocusedId 'ThemeSystem'
            Send-ThemeKeys '{RIGHT}'
            Wait-ThemeShown 'Light' 'Right'
            Wait-FocusedId 'ThemeLight'
            Send-ThemeKeys '{LEFT}'
            Wait-ThemeShown 'System' 'Left'
            Wait-FocusedId 'ThemeSystem'
            # The CI launch size: the three items stay whole on the card.
            Resize-ShellWindow 768 519
            Start-Sleep -Milliseconds 400
            foreach ($id in $themeIds.Values) {
                $bounds = (Wait-VisibleById $id).Current.BoundingRectangle
                if ($bounds.Width -lt 24 -or $bounds.Height -lt 24) {
                    throw "At 768x519 $id is $bounds."
                }
            }
            $report.themeNarrowScreenshot = Save-WindowScreenshot 'theme-segmented-narrow'
            Resize-ShellWindow 1500 720
            $report.phases += 'theme-segmented'
        }
    }
```

`Wait-EnabledById`, `Wait-FocusedId`, `Wait-VisibleById`, `Find-ById`,
`Resize-ShellWindow` and `Save-WindowScreenshot` are existing top-level
helpers. Check each name with `grep -n "function <name>"` before
relying on it.

- [ ] **Step 2: Add the `theme` group to the installer**

In `windows_shell_install.ps1`:

- After `[switch] $CompletionOnly,` add `[switch] $ThemeOnly,`.
- In `$scenarioGroups`, after `'completion' = ...`, add
  `'theme' = $ThemeOnly.IsPresent`.
- In the `Run-ShellSmoke` timeout, add `-or $mode -like 'theme-*'` to the
  120-second branch (the `catalog*` line).
- After `Run-CompletionScenarios`, add:

```powershell
function Run-ThemeScenarios {
    # TR14.2: the App theme choice. The UIA gate runs first.
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    [void](Invoke-ShellSeed @('seed-design', $dataRoot))
    $report.theme = [ordered]@{}
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.theme.segmented = Run-ShellSmoke 'theme-segmented'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
        $report.theme.restoredAppTheme = Get-AppThemePreference
    }
}
```

- After `if (Enter-ScenarioGroup 'completion') { Run-CompletionScenarios }`,
  add `if (Enter-ScenarioGroup 'theme') { Run-ThemeScenarios }`.

Parse-check both scripts on the host with Windows PowerShell 5.1:

```bash
ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -Command \"foreach ($f in 'windows_shell_install.ps1','windows_shell_ui_smoke.ps1') { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile('E:\work\desktop-guides\t14-2\tools\p1\' + $f, [ref]$null, [ref]$e); \\\"$f $($e.Count)\\\" }\""
```

If the quoting breaks over SSH, `scp` a two-line `.ps1` with the same
body to `E:\work`, run it with `powershell -NoProfile -File`, and delete
it afterwards. Expected: `0` errors for each file. Also run
`LC_ALL=C grep -nP '[^\x00-\x7F]' tools/p1/*.ps1`; expected: no output.

- [ ] **Step 3: Wire CI**

In `.github/workflows/windows-ci.yml`, add `- theme` after
`- completion` in the `shell-scope` options. In the shard JSON, change
`"groups":"pdf,progress"` to `"groups":"pdf,progress,theme"`. Check the
YAML with
`ruby -ryaml -e 'YAML.load_file(ARGV[0])' .github/workflows/windows-ci.yml`;
expected: no output.

- [ ] **Step 4: Commit and run the gate**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1 .github/workflows/windows-ci.yml
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(shell): T14.2 theme UIA gate

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push -u origin feat/p1-t14-2-theme-setting
```

Run the CI loop with `shell-scope=theme` and `dev-fast=true`.

- [ ] **Step 5: Read the gate result**

Download the `production-shell-ui-theme` artifact and read
`theme-segmented.json`'s `themeItems`. Write one ledger line:
`Gate (ItemsSource): pass|fail; patterns: <list for ThemeSystem>`.

- If the run passed, skip Step 6.
- If it failed only on `SelectionItemPattern` (the `patterns` list lacks
  it, or `Test-ThemeSelected` threw `Unsupported Pattern`), do Step 6.
- Any other failure is a defect in Tasks 2 or 3. Use
  superpowers:systematic-debugging, fix it, and rerun.

- [ ] **Step 6 (only if Step 5 failed on the pattern): the custom peer**

Append to `BoundedChoice.cs`, and add `using Microsoft.UI.Xaml.Automation.Peers;`
and `using Microsoft.UI.Xaml.Automation.Provider;`:

```csharp
// Exposes SelectionItem on each generated container (the T14.2 gate fallback).
public partial class BoundedChoiceItem : SegmentedItem
{
    protected override AutomationPeer OnCreateAutomationPeer() => new BoundedChoiceItemPeer(this);
}

internal sealed partial class BoundedChoiceItemPeer(BoundedChoiceItem owner)
    : ListViewItemAutomationPeer(owner), ISelectionItemProvider
{
    public bool IsSelected => owner.IsSelected;

    public IRawElementProviderSimple? SelectionContainer =>
        ItemsControl.ItemsControlFromItemContainer(owner) is { } choice &&
        FrameworkElementAutomationPeer.FromElement(choice) is { } peer
            ? ProviderFromPeer(peer)
            : null;

    public void Select() => owner.IsSelected = true;

    public void AddToSelection() => owner.IsSelected = true;

    // A choice always has one selected item.
    public void RemoveFromSelection() { }

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.SelectionItem ? this : base.GetPatternCore(patternInterface);
}
```

In `BoundedChoice`, add:

```csharp
    protected override DependencyObject GetContainerForItemOverride() => new BoundedChoiceItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is BoundedChoiceItem;
```

- If `SegmentedItem` turns out to be sealed, or the build fails on these
  overrides, stop and ask the user. The spec leaves that case to them.
- Otherwise, build on the host, then commit:
  `fix(shell): T14.2 SelectionItem peer for BoundedChoice`.
- Rerun Steps 4–5. If the gate still fails, stop and ask the user, with
  the `themeItems` evidence.

### Task 4: Apply, save and revert the theme

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.Theme.cs` (replace `ShowTheme` with `ApplyTheme`; add the save handler)
- Modify: `src/DesktopGuides.Production/Materials/DialogSurface.cs`
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml.cs:67,272`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (constructor; `InitializeCoreAsync`; the six `DialogSurface.Apply` calls at ~726, 785, 856, 946, 1042, 1164)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (`SelectionChanged` on `AppThemeChoice`)

**Interfaces:**
- Consumes: `ThemePresentation.Resolve`, `Status`, `Saved`, `SaveFailed`,
  `Options`, `IndexOf`; `AppliedTheme` (Task 1); `AppThemeChoice`,
  `applyingThemeSelection` (Task 2); `repository.UpdateSettingsAsync`;
  `ShowTransientStatus` and `ShowErrorStatus`.
- Produces:
  - `DialogSurface.Apply(ContentDialog dialog, WindowMaterial material, ElementTheme theme)`
  - `ReaderToolbar.DialogTheme` (`internal ElementTheme`, default `Default`)
  - `ShellWindow.DialogTheme` (`internal ElementTheme`)
  - `ShellWindow.ApplyTheme(ThemePreference requested)`
  - `ItemStatus` on `AppThemeChoice` (Task 1's `Status`), on
    `AppTitleBar` (`UseDefaultAppMode`, `Light` or `Dark`), and on every
    dialog once open (its `ActualTheme`: `Light` or `Dark`).

TDD: the decision logic is Task 1's tested `Resolve` and `Status`. This
task is WinUI wiring, and Task 5's installed modes are its tests.

- [ ] **Step 1: Give dialogs the theme**

`DialogSurface.cs`:

```csharp
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production.Materials;

internal static class DialogSurface
{
    // A ContentDialog sits in the popup root, so it doesn't inherit the
    // shell root's RequestedTheme.
    public static void Apply(ContentDialog dialog, WindowMaterial material, ElementTheme theme)
    {
        dialog.Style = (Style)Application.Current.Resources[
            material == WindowMaterial.Acrylic
                ? "DesktopGuidesAcrylicDialogStyle"
                : "DefaultContentDialogStyle"];
        dialog.RequestedTheme = theme;
        // The installed checks read the theme the dialog resolved.
        dialog.Opened += (sender, _) =>
            AutomationProperties.SetItemStatus(sender, sender.ActualTheme.ToString());
    }
}
```

In `ReaderToolbar.xaml.cs`, after `DialogMaterial`, add
`internal ElementTheme DialogTheme { get; set; } = ElementTheme.Default;`.
Change line 272 to `DialogSurface.Apply(dialog, DialogMaterial, DialogTheme);`.

In `ShellWindow.xaml.cs`, after `EffectiveMaterial`, add
`internal ElementTheme DialogTheme { get; private set; } = ElementTheme.Default;`.
Change each of the six calls from `DialogSurface.Apply(<x>, EffectiveMaterial);`
to `DialogSurface.Apply(<x>, EffectiveMaterial, DialogTheme);`. Then run
`grep -rn "DialogSurface.Apply" src/DesktopGuides.Production --include='*.cs'`.
Expected: seven calls, all with three arguments.

- [ ] **Step 2: Replace `ShowTheme` with `ApplyTheme` and the handler**

Replace `ShellWindow.Theme.cs` with:

```csharp
using DesktopGuides.Core.Library;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    private readonly AccessibilitySettings accessibility = new();
    private readonly SemaphoreSlim themeSaveGate = new(1, 1);
    private bool applyingThemeSelection;
    // The choice on screen, and the last one storage accepted.
    private ThemePreference requestedTheme = ThemePreference.System;
    private ThemePreference committedTheme = ThemePreference.System;
    private AppliedTheme appliedTheme = AppliedTheme.FollowSystem;

    private void InitializeThemeChoice()
    {
        AppThemeChoice.SetOptions(
            ThemePresentation.Options
                .Select(option => new BoundedChoiceOption(option.Label, option.AutomationId))
                .ToList());
        // System follows Windows, so its status follows the resolved theme.
        ShellRoot.ActualThemeChanged += (_, _) => UpdateThemeStatus();
        // Raised off the UI thread.
        accessibility.HighContrastChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => ApplyTheme(requestedTheme));
    }

    private void ApplyTheme(ThemePreference requested)
    {
        requestedTheme = requested;
        appliedTheme = ThemePresentation.Resolve(requested, accessibility.HighContrast);
        ElementTheme element = appliedTheme switch
        {
            AppliedTheme.Light => ElementTheme.Light,
            AppliedTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        ShellRoot.RequestedTheme = element;
        DialogTheme = element;
        ReaderActions.DialogTheme = element;
        TitleBarTheme titleBar = appliedTheme switch
        {
            AppliedTheme.Light => TitleBarTheme.Light,
            AppliedTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode
        };
        AppWindow.TitleBar.PreferredTheme = titleBar;
        AutomationProperties.SetItemStatus(AppTitleBar, titleBar.ToString());
        applyingThemeSelection = true;
        AppThemeChoice.SelectedIndex = ThemePresentation.IndexOf(requested);
        applyingThemeSelection = false;
        UpdateThemeStatus();
    }

    private void UpdateThemeStatus() =>
        AutomationProperties.SetItemStatus(
            AppThemeChoice,
            ThemePresentation.Status(requestedTheme, appliedTheme, ShellRoot.ActualTheme.ToString()));

    private async void AppThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A cleared selection is put back by BoundedChoice.
        if (applyingThemeSelection || repository is null || AppThemeChoice.SelectedIndex < 0)
        {
            return;
        }
        ThemePreference requested = ThemePresentation.Options[AppThemeChoice.SelectedIndex].Preference;
        if (requested == requestedTheme)
        {
            return;
        }
        ApplyTheme(requested);
        // Saves commit in the order the user chose.
        await themeSaveGate.WaitAsync();
        try
        {
            await repository.UpdateSettingsAsync(s => s with { Theme = requested });
            committedTheme = requested;
            if (requested == requestedTheme)
            {
                ShowTransientStatus(ThemePresentation.Saved(requested));
            }
        }
        catch (Exception error)
        {
            // A newer choice is already on screen; its own save decides.
            if (requested == requestedTheme)
            {
                ApplyTheme(committedTheme);
            }
            ShowErrorStatus(ThemePresentation.SaveFailed(error.Message));
        }
        finally
        {
            themeSaveGate.Release();
        }
    }
}
```

`ShellRoot` is the window's root `Grid`, and `AppTitleBar` is inside it,
so the WinUI `TitleBar` follows `ShellRoot.RequestedTheme`.

- [ ] **Step 3: Wire it**

- In `ShellWindow.xaml`, add `SelectionChanged="AppThemeSelectionChanged"`
  to `AppThemeChoice`.
- In the constructor, replace `ShowTheme(ThemePreference.System);` with
  `ApplyTheme(ThemePreference.System);`.
- In `InitializeCoreAsync`, replace `ShowTheme(settings?.Theme ?? ThemePreference.System);`
  with:

```csharp
            ThemePreference storedTheme = settings?.Theme ?? ThemePreference.System;
            committedTheme = storedTheme;
            ApplyTheme(storedTheme);
```

Check that `ShowTheme` is gone: `grep -rn "ShowTheme" src`. Expected: no output.

- [ ] **Step 4: Build on the host, and run the tests**

Sync, build Production, and run Core.Tests and Infrastructure.Tests.
Expected: the build succeeds, and the test counts equal the Task 1 count
(Core) and the baseline (Infrastructure), with none failing.

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(shell): T14.2 apply and save the app theme

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: Installed change, restart, offline and error checks

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (a `describe-theme` mode after `set-material`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (params; `ValidateSet`; the `theme-*` branch)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-ShellSmoke` params; `Run-ThemeScenarios`)

**Interfaces:**
- Consumes: Task 3's smoke helpers and group; Task 4's `ItemStatus`
  values on `AppThemeChoice`, `AppTitleBar` and `GameEditorDialog`;
  `Assert-NoRemoteConnections`; `Start-ShellDatabaseLock` and
  `Release-ShellDatabaseLock`.
- Produces:
  - ShellSeed `describe-theme <dataRoot>`, which prints the stored
    `ThemePreference` name (`System`, `Light` or `Dark`).
  - Smoke params `-ExpectedTheme` (`System`, `Light` or `Dark`; default
    `System`), `-ExpectedThemeStatus` (string), `-SwitchToTheme` (`''`,
    `System`, `Light` or `Dark`), `-WindowsTheme` (`''`, `Light` or
    `Dark`), and the matching `Run-ShellSmoke`
    params.
  - Smoke modes `theme-change`, `theme-restored`, `theme-error` and
    `theme-error-retry`.

- [ ] **Step 1: `describe-theme` in ShellSeed**

After the `set-material` block:

```csharp
if (args.Length == 2 && args[0] == "describe-theme")
{
    await using SqliteLibraryRepository themeRepository =
        new(new ManagedPathResolver(args[1]));
    await themeRepository.InitializeAsync();
    Console.WriteLine((await themeRepository.GetSettingsAsync()).Theme);
    return 0;
}
```

Build ShellSeed on the host. Expected: success.

- [ ] **Step 2: Smoke params and modes**

Add to the `param` block, after `$SwitchToMaterial`:

```powershell
    [ValidateSet('System', 'Light', 'Dark')]
    [string] $ExpectedTheme = 'System',

    # The choice's ItemStatus at the start, for example 'System (Light)'.
    [string] $ExpectedThemeStatus = '',

    [ValidateSet('', 'System', 'Light', 'Dark')]
    [string] $SwitchToTheme = '',

    # The Windows app theme, needed when the start status doesn't name it.
    [ValidateSet('', 'Light', 'Dark')]
    [string] $WindowsTheme = '',
```

Add `'theme-change', 'theme-restored', 'theme-error', 'theme-error-retry'`
to the `Mode` `ValidateSet`, next to `'theme-segmented'`.

In the `theme-*` branch, after `Send-ThemeKeys`, add these helpers:

```powershell
        function Wait-ItemStatus([string] $id, [string] $expected, [string] $step) {
            $deadline = (Get-Date).AddSeconds(10)
            do {
                $element = Find-ById $id
                $status = if ($element) { $element.Current.ItemStatus } else { '' }
                if ($status -ceq $expected) { return }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "$step expected $id to report '$expected', found '$status'."
        }

        # The theme the window, title bar and choice report now.
        function Assert-ThemeShown([string] $label, [string] $status, [string] $step) {
            Wait-ThemeShown $label $step
            Wait-ItemStatus 'AppThemeChoice' $status $step
            $titleBar = if ($label -eq 'System') { 'UseDefaultAppMode' } else { $label }
            Wait-ItemStatus 'AppTitleBar' $titleBar $step
        }

        # Keyboard only: focus the shown item, then arrow to the target.
        function Select-ThemeByKeys([string] $from, [string] $to) {
            $order = @('System', 'Light', 'Dark')
            $steps = $order.IndexOf($to) - $order.IndexOf($from)
            (Wait-VisibleById $themeIds[$from]).SetFocus()
            Wait-FocusedId $themeIds[$from]
            $key = if ($steps -gt 0) { '{RIGHT}' } else { '{LEFT}' }
            for ($i = 0; $i -lt [Math]::Abs($steps); $i++) { Send-ThemeKeys $key }
        }

        $windowsTheme = if ($WindowsTheme) { $WindowsTheme }
            elseif ($ExpectedThemeStatus -like 'System (*)') {
                $ExpectedThemeStatus.Substring(8).TrimEnd(')') }
            else { '' }
```

After the `theme-segmented` block, add:

```powershell
        elseif ($Mode -in @('theme-change', 'theme-restored')) {
            # TR14.2: the stored theme is applied at launch, and a keyboard
            # change applies at once and is confirmed.
            Assert-ThemeShown $ExpectedTheme $ExpectedThemeStatus 'Launch'
            $report.themeAtLaunch = $ExpectedThemeStatus
            if ($Mode -eq 'theme-change') {
                Select-ThemeByKeys $ExpectedTheme $SwitchToTheme
                [void](Wait-Status "App theme set to $SwitchToTheme.")
                if ($SwitchToTheme -eq 'System' -and -not $windowsTheme) {
                    throw 'Switching to System needs -WindowsTheme or a System start status.'
                }
                $after = if ($SwitchToTheme -eq 'System') { "System ($windowsTheme)" } else { $SwitchToTheme }
                Assert-ThemeShown $SwitchToTheme $after 'Change'
                $dialogTheme = if ($SwitchToTheme -eq 'System') { $windowsTheme } else { $SwitchToTheme }
                [void](Wait-HiddenById 'ShellStatus')
                Assert-ShellForeground
                $report.themeSettingsScreenshot = Save-WindowScreenshot "theme-$SwitchToTheme-settings"

                $designGame = 'The Legend of Zelda: Tears of the Kingdom'
                $designGuide = 'Complete Story Walkthrough'
                Select-Element 'Library'
                [void](Wait-GameRow $designGame)
                Start-Sleep -Milliseconds 400
                Assert-ShellForeground
                $report.themeLibraryScreenshot = Save-WindowScreenshot "theme-$SwitchToTheme-library"
                Press-Enter (Wait-GameRow $designGame)
                [void](Wait-Name 'GameHeading' $designGame)
                Invoke-Element (Wait-EnabledById 'EditGameButton')
                [void](Wait-VisibleById 'GameTitleInput')
                Wait-ItemStatus 'GameEditorDialog' $dialogTheme 'Edit game'
                Assert-ShellForeground
                $report.themeDialogScreenshot = Save-WindowScreenshot "theme-$SwitchToTheme-edit-game"
                Send-ThemeKeys '{ESC}'
                Wait-EditorClosed
                Press-Enter (Wait-GuideRow $designGuide)
                [void](Wait-Name 'ReaderHeading' $designGuide)
                [void](Wait-Status 'Guide ready.')
                [void](Wait-HiddenById 'ShellStatus')
                Assert-ShellForeground
                $report.themeReaderScreenshot = Save-WindowScreenshot "theme-$SwitchToTheme-reader"
                # The overflow menu is a popup; the screenshot shows its theme.
                $more = $null
                # The candidates match the pdf branch's Invoke-OverflowCommand.
                foreach ($candidate in @('More', 'More options', 'More commands', 'Show more', 'See more')) {
                    $more = Find-VisibleName $candidate
                    if ($more) { break }
                }
                if (-not $more) { throw 'The Reader toolbar has no visible overflow button.' }
                Invoke-Element $more
                Start-Sleep -Milliseconds 400
                Assert-ShellForeground
                $report.themeMenuScreenshot = Save-WindowScreenshot "theme-$SwitchToTheme-reader-menu"
                Send-ThemeKeys '{ESC}'
                $report.themeAfter = $after
            }
            Assert-NoRemoteConnections "theme ($Mode)"
            $report.phases += $Mode
        }
        elseif ($Mode -eq 'theme-error') {
            # The installer holds the write lock, so the save times out.
            Assert-ThemeShown $ExpectedTheme $ExpectedThemeStatus 'Before the failed save'
            Select-ThemeByKeys $ExpectedTheme $SwitchToTheme
            [void](Wait-Status 'Could not save the app theme: ' -Prefix -Seconds 60)
            Assert-ThemeShown $ExpectedTheme $ExpectedThemeStatus 'After the failed save'
            Assert-ShellForeground
            $report.themeErrorScreenshot = Save-WindowScreenshot 'theme-error'
            $report.phases += 'theme-error'
        }
        elseif ($Mode -eq 'theme-error-retry') {
            Select-ThemeByKeys $ExpectedTheme $SwitchToTheme
            [void](Wait-Status "App theme set to $SwitchToTheme.")
            Assert-ThemeShown $SwitchToTheme $SwitchToTheme 'Retry'
            $report.phases += 'theme-error-retry'
        }
```

The failure text ends with the SQLite message, so `Wait-Status` (top
level, ~line 217) gains a `[switch] $Prefix` param. With it, a message
matches when it starts with an expected value. Replace its match test:

```powershell
            $matched = if ($Prefix) {
                @($expected | Where-Object { $message.StartsWith($_) }).Count -gt 0
            } else { $message -in $expected }
            if ($matched -and
                $sequence -gt $script:lastStatusSequence) {
```

The visible-status check below it compares `Name` with `$message`, which
stays correct.

`Find-VisibleName` is defined inside the `pdf-*` branch (~line 1537).
Move it to the top level with the other helpers, keeping its body as is,
so the `theme-*` branch can use it. Every other helper used above is
already top level.

- [ ] **Step 3: Pass the params through `Run-ShellSmoke`**

Add `[string] $ExpectedTheme = ''`, `[string] $ExpectedThemeStatus = ''`,
`[string] $SwitchToTheme = ''` and `[string] $WindowsTheme = ''` to `Run-ShellSmoke`'s `param` list.
After the `SwitchToMaterial` block, add:

```powershell
    if ($ExpectedTheme) {
        $arguments += ' -ExpectedTheme ' + $ExpectedTheme
    }
    if ($ExpectedThemeStatus) {
        $arguments += ' -ExpectedThemeStatus "' + $ExpectedThemeStatus + '"'
    }
    if ($SwitchToTheme) {
        $arguments += ' -SwitchToTheme ' + $SwitchToTheme
    }
    if ($WindowsTheme) {
        $arguments += ' -WindowsTheme ' + $WindowsTheme
    }
```

- [ ] **Step 4: The full `Run-ThemeScenarios`**

Replace the Task 3 body with:

```powershell
function Assert-StoredTheme([string] $expected, [string] $step) {
    $stored = (Invoke-ShellSeed @('describe-theme', $dataRoot)).Trim()
    if ($stored -ne $expected) { throw "After $step the stored theme is '$stored', not '$expected'." }
    return $stored
}

function Run-ThemeScenarios {
    # TR14.2: the App theme choice applies at once, survives a relaunch,
    # makes no remote connection, and puts back a failed save. The UIA
    # gate runs first. High contrast is deferred to T16.2.
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    [void](Invoke-ShellSeed @('seed-design', $dataRoot))
    $report.theme = [ordered]@{ highContrast = 'deferred-to-T16.2' }
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.theme.segmented = Run-ShellSmoke 'theme-segmented'
        Close-InstalledShell
        [void](Assert-StoredTheme 'System' 'the gate')

        # Windows light: System to Dark (two arrow presses, two saves).
        Start-InstalledShell
        $report.theme.lightToDark = Run-ShellSmoke 'theme-change' -ResultName 'theme-change-light' `
            -ExpectedTheme System -ExpectedThemeStatus 'System (Light)' -SwitchToTheme Dark
        Close-InstalledShell
        [void](Assert-StoredTheme 'Dark' 'choosing Dark')

        # Windows dark: Dark survives the relaunch; choose Light.
        Set-AppThemePreference $false
        Start-InstalledShell
        $report.theme.darkToLight = Run-ShellSmoke 'theme-change' -ResultName 'theme-change-dark' `
            -ExpectedTheme Dark -ExpectedThemeStatus 'Dark' -SwitchToTheme Light
        Close-InstalledShell
        [void](Assert-StoredTheme 'Light' 'choosing Light')

        # Light survives; back to System, which follows Windows dark.
        Start-InstalledShell
        $report.theme.lightToSystem = Run-ShellSmoke 'theme-change' -ResultName 'theme-change-system' `
            -ExpectedTheme Light -ExpectedThemeStatus 'Light' -SwitchToTheme System `
            -WindowsTheme Dark
        Close-InstalledShell
        [void](Assert-StoredTheme 'System' 'choosing System')

        # System follows Windows again after a relaunch.
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.theme.restored = Run-ShellSmoke 'theme-restored' `
            -ExpectedTheme System -ExpectedThemeStatus 'System (Light)'

        # A held write lock makes the app's save time out after 30 s.
        $ready = Join-Path $ResultDirectory "theme-lock-ready-$runId"
        $release = Join-Path $ResultDirectory "theme-lock-release-$runId"
        $lock = Start-ShellDatabaseLock 'hold-write-lock' $ready $release -HoldSeconds 120
        try {
            $report.theme.error = Run-ShellSmoke 'theme-error' `
                -ExpectedTheme System -ExpectedThemeStatus 'System (Light)' -SwitchToTheme Light
        }
        finally {
            Release-ShellDatabaseLock $lock $release
        }
        [void](Assert-StoredTheme 'System' 'the failed save')
        $report.theme.errorRetry = Run-ShellSmoke 'theme-error-retry' `
            -ExpectedTheme System -SwitchToTheme Light
        Close-InstalledShell
        [void](Assert-StoredTheme 'Light' 'the retry')
    }
    finally {
        Restore-AppThemePreference $originalTheme
        $report.theme.restoredAppTheme = Get-AppThemePreference
    }
}
```

`Start-ShellDatabaseLock` (~line 390) takes `Mode`, `ReadyPath`,
`ReleasePath` and `HoldSeconds`, and `Invoke-ShellSeed` (~line 1937)
throws on a non-zero exit. `Assert-StoredTheme` runs only after
`Close-InstalledShell`, as the completion group's `Assert-Completed`
does.

- [ ] **Step 5: Parse-check, commit and run**

Parse-check both scripts as in Task 3, run the ASCII check, then commit:

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tools/p1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(shell): T14.2 installed theme change, restart and error checks

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
```

Run the CI loop with `shell-scope=theme`, without `dev-fast`.
Expected: green. Download the artifact into
`docs/p1/evidence/t14-2-theme-setting/` (JSON files and PNGs only), then
look at every screenshot. The Light and Dark sets must show the matching
theme on Settings, Library, the Edit game dialog, the Reader and its
menu. If the menu has the wrong theme, that popup needs
`RequestedTheme`. Fix it and rerun, using superpowers:systematic-debugging.

### Task 6: Completion choice on `BoundedChoice`

Start only after Task 3's gate passed, with or without the Step 6 peer.

**Files:**
- Modify: `src/DesktopGuides.Production/GuideCompletionChoice.xaml`
- Modify: `src/DesktopGuides.Production/GuideCompletionChoice.xaml.cs`
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the `completion-segmented` block, ~line 2876)
- Modify: `tools/p1/windows_shell_install.ps1` (the comment at ~line 1618)

**Interfaces:**
- Consumes: Task 2's `BoundedChoice`, `BoundedChoiceOption`,
  `SetOptions` and `ContainerAt`; `GuideCompletionPresentation.InProgressLabel`
  and `CompleteLabel`.
- Produces: no new surface. `Show`, `Hide`, `Revert`, `SetBusy`,
  `FocusSelection`, `GuideId` and `CompletionRequested` keep their
  signatures and behavior. The AutomationIds stay `CompletionChoice`,
  `CompletionInProgress` and `CompletionComplete`.

TDD skip: the control has no unit-test host. The installed `completion`
group covers its behavior, as it did for T13.1.

- [ ] **Step 1: Replace the XAML**

```xml
<UserControl
    x:Class="DesktopGuides.Production.GuideCompletionChoice"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:DesktopGuides.Production"
    Visibility="Collapsed">
    <local:BoundedChoice x:Name="Choice"
                         VerticalAlignment="Top"
                         AutomationProperties.AutomationId="CompletionChoice" />
</UserControl>
```

- [ ] **Step 2: Index-based selection in the code-behind**

Replace the members that refer to the radio items. Index 0 is In
progress and index 1 is Complete.

```csharp
    private const int InProgressIndex = 0;
    private const int CompleteIndex = 1;

    public GuideCompletionChoice()
    {
        InitializeComponent();
        Choice.SetOptions(
        [
            new BoundedChoiceOption(GuideCompletionPresentation.InProgressLabel, "CompletionInProgress"),
            new BoundedChoiceOption(GuideCompletionPresentation.CompleteLabel, "CompletionComplete"),
        ]);
        Choice.SelectionChanged += SelectionChanged;
    }
```

```csharp
    public void SetBusy(bool value)
    {
        busy = value;
        pendingComplete = Choice.SelectedIndex == CompleteIndex;
        Choice.IsHitTestVisible = !value;
    }

    public void FocusSelection()
    {
        int index = shownComplete ? CompleteIndex : InProgressIndex;
        if (Choice.ContainerAt(index)?.Focus(FocusState.Programmatic) != true)
        {
            // The panel was just shown; its containers exist after layout.
            DispatcherQueue.TryEnqueue(
                () => Choice.ContainerAt(index)?.Focus(FocusState.Programmatic));
        }
    }

    private void Select(bool complete)
    {
        showing = true;
        try
        {
            Choice.SelectedIndex = complete ? CompleteIndex : InProgressIndex;
        }
        finally
        {
            showing = false;
        }
    }
```

In `SelectionChanged`:

- Replace the `SelectedIndex < 0` branch with a plain `return`, so
  `BoundedChoice` alone restores a cleared selection (Review Focus 3). Two
  queued restores could otherwise race and raise a duplicate request.
  The restored selection comes back through this handler and follows the
  busy and committed rules below.
- Replace `ReferenceEquals(Choice.SelectedItem, CompleteItem)` with
  `Choice.SelectedIndex == CompleteIndex`.
- Delete the now-unused `restore` local.

Remove `using Microsoft.UI.Xaml.Controls;` only if nothing else in the
file needs it (`UserControl` and `SelectionChangedEventArgs` do, so it
probably stays).

- [ ] **Step 3: Build on the host**

Sync the tree, then build Production x64 as in Global Constraints.
Expected: success, no new warnings.

- [ ] **Step 4: The completion gate checks `ListItem`**

In the `completion-segmented` block, update the comment and the type
check:

```powershell
                # The UIA gate: two Segmented list items with their names, the
                # selected state matching storage and following the arrow keys.
```

```powershell
                    if ($item.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem -or
```

Keep the rest of the block, including the Tab order and the 768x519
header check. In `windows_shell_install.ps1` (~line 1618), change the
comment to:

```powershell
        # The UIA gate runs first: names, Segmented items and their selected state.
```

Parse-check and ASCII-check both scripts as in Task 3.

- [ ] **Step 5: Commit, push and run the completion group**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production/GuideCompletionChoice.xaml src/DesktopGuides.Production/GuideCompletionChoice.xaml.cs tools/p1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(shell): T14.2 completion choice on the shared Segmented

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
```

Run the CI loop with `shell-scope=completion`, without `dev-fast`.
Expected: green. Compare the completion screenshots (Game page light and
dark, Reader light and dark) with T13.1's evidence. The choice must fit
the header row at 768x519, as the existing check asserts. If it fails,
use superpowers:systematic-debugging. If the Segmented items need width
that the header doesn't have, stop and ask the user; don't go back to
RadioButtons on your own.

### Task 7: Docs, evidence, full run and PR

**Files:**
- Modify: `docs/p1/t14-2-theme-setting-design.md` (status; new `## Implementation notes` and `## Verification`)
- Modify: `docs/p1/e2e-testing.md` (~lines 225-240, 288; a new Theme row after Completion)
- Modify: `docs/p1/implementation-plan.md` (~line 762, the T13.1 note at ~901, a T14.2 note after it)
- Modify: `docs/work-breakdown.md` (~line 449)
- Modify: `docs/p1-technical-design.md` (~lines 358, 384, 385)
- Modify: `docs/p1/t13-1-completion-actions-design.md` (a dated note under `## Implementation notes`)
- Create: `docs/p1/evidence/t14-2-theme-setting/` (JSON and PNGs from the CI artifacts)

**Interfaces:**
- Consumes: the gate result (Task 3, Step 5), whether the custom peer was
  needed, the CI run IDs of Tasks 5 and 6, and the ledger's rulings.
- Produces: docs only.

TDD skip: docs only.

- [ ] **Step 1: The spec's notes and verification**

Set the status line to
`Status: implemented; CI run <full run id>.` (the id comes from Step 5,
so write this line last). Before `## Risks`, add `## Implementation notes`
with one bullet each for:

- **The gate result.** Either: "The `ItemsSource`-bound `Segmented`
  exposed `SelectionItem` on its generated containers (run <id>), so no
  custom peer was needed." Or: "It still lacked `SelectionItem` (run
  <id>, patterns <list from the report>); `BoundedChoiceItem` supplies a
  peer that implements `ISelectionItemProvider` (run <id>)."
- `ThemePresentation` in Core: labels, ids, `Resolve`, and the status and
  failure texts.
- The applied theme on `ShellRoot.RequestedTheme`, dialogs through
  `DialogSurface.Apply`, the title bar through `PreferredTheme`, and the
  `ItemStatus` values the tests read.
- The saves are serialized, and a failed save reverts only when no newer
  choice is pending.
- High contrast: the status reads `<Label> (high contrast)`. The pass
  itself is deferred to T16.2.
- `DisplayMemberPath` isn't used. The container's `Content` is set to the
  label in `PrepareContainerForItemOverride`.
- Completion moved to `BoundedChoice`, and a cleared selection is
  restored only by `BoundedChoice`.
- Any ledger ruling that changed a value from this spec.

Add `## Verification`, in the style of the T13.1 design: Core test
counts, then each installed mode with its run id and what it showed.

- [ ] **Step 2: The e2e guide**

In `docs/p1/e2e-testing.md`:

- **Shard list (~line 228).** Replace `` `pdf` (pdf,
  progress) `` with `` `pdf` (pdf, progress, theme) ``.
- **Only-switch list (~line 236).** Add `` `-ThemeOnly` `` after
  `` `-CompletionOnly` ``.
- **Completion row.** Replace `its two `RadioButton` items` with `its two
  Toolkit `Segmented` `ListItem`s`.
- **New Theme row, after the Completion row:**

```markdown
| Theme | `-ThemeOnly`, after `seed-design`; Windows app theme saved and restored. `theme-segmented` (gate): `AppThemeChoice` is named `App theme` and has three `ListItem`s, `System`, `Light` and `Dark`, whose `SelectionItem` state starts on System and follows Right and Left; at 768x519 every item is visible. `theme-change`, from the keyboard: Windows light System to Dark, Windows dark Dark to Light, then Light to System, each relaunched. The status reads `App theme set to <Label>.`; the choice's `ItemStatus` reads `<Label>` or `System (<Windows theme>)`; `AppTitleBar` and the Edit game dialog report the same theme; the stored value matches. `theme-restored`: System follows Windows light after a relaunch. `theme-change` and `theme-restored` make no non-loopback connection. `theme-error`, `theme-error-retry`: with the write lock held for 120 s, Light shows `Could not save the app theme: <message>` and the choice returns to System; after the release the retry stores Light. Screenshots per theme: Settings, Library, Edit game, Reader, Reader overflow menu; and `theme-error`. High contrast is deferred to T16.2. | T14.2, TR14.2 |
```

- [ ] **Step 3: Plan, breakdown and technical design**

- **`implementation-plan.md` ~line 762.** In the T14.2 row, replace
  `with accessible native-radio fallback` with `bound to data, with a
  custom selection automation peer as the fallback`.
- **`implementation-plan.md` ~line 901.** In the T13.1 note, replace
  `Toolkit `Segmented` failed the selected-state UIA gate, so the choice
  uses native `RadioButtons`.` with `T14.2 later moved the choice to the
  shared data-bound `Segmented` (`BoundedChoice`), which passes the gate.`
- **`implementation-plan.md`, after that paragraph:**

```markdown
T14.2 is implemented; see the
[design and implementation notes](t14-2-theme-setting-design.md).
Settings has an *App theme* choice (System, Light, Dark) on the shared
`BoundedChoice` Segmented. It applies at once to the shell, dialogs and
title bar, is stored, survives a relaunch, and puts back a failed save.
Windows high contrast always wins.
```

- **`work-breakdown.md` ~line 449.** Replace `with an accessible
  native-radio fallback if installed testing finds a regression` with
  `bound to data so each item exposes its selected state, with a custom
  automation peer as the fallback`.
- **`p1-technical-design.md` ~line 358.** End the `Segmented` row's
  purpose with: `T14.2 binds the items to data (BoundedChoice), which
  exposes SelectionItem; T13.1's completion choice uses it too.` Delete
  the sentence starting `T13.1 found no`.
- **`p1-technical-design.md` ~line 384.** Make the T13.1 row read
  `` `Segmented` (`BoundedChoice`) for the two explicit completion
  states; it replaced T13.1's `RadioButtons` fallback in T14.2. ``
- **`p1-technical-design.md` ~line 385.** Leave the T14.1–T14.4 row as
  it is.

- [ ] **Step 4: The T13.1 note**

Under `## Implementation notes` in
`docs/p1/t13-1-completion-actions-design.md`, add:

```markdown
- **2026-10: moved to `Segmented` in T14.2.** The completion choice now
  hosts the shared `BoundedChoice`, a data-bound Toolkit `Segmented`
  whose generated items expose `SelectionItem`. The smoke checks
  `ListItem`s again. See the
  [T14.2 design](t14-2-theme-setting-design.md).
```

Leave the earlier notes as the historical record.

- [ ] **Step 5: Full CI run and evidence**

Commit the docs so far:

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add docs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "docs(p1): T14.2 notes, e2e rows and control mapping

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
```

Run the CI loop with `shell-scope=all`, without `dev-fast`. Expected:
all four shards green. If a shard fails, use
superpowers:systematic-debugging; don't rerun until the cause is known.

Download the `pdf`, `html` and `design` artifacts. Copy the theme JSON
and PNGs, the completion `completion-segmented` JSON, and the completion
light and dark screenshots into
`docs/p1/evidence/t14-2-theme-setting/`. Check that no file holds a
credential, a user path outside the runner or a password. Then fill the
run ids into Step 1's status line and Verification, and commit:

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add docs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "docs(p1): T14.2 verification and evidence

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C /Users/ilya.lissoboi/work/desktop-guides push
```

- [ ] **Step 6: Final review and PR**

Use superpowers:verification-before-completion on the host (Core and
Infrastructure tests, Production and ShellSeed builds) and on the green
full run. Then use superpowers:finishing-a-development-branch and choose
the PR. The PR description:

- **Target task:** T14.2 (TR14.2), plus the T13.1 completion choice moved
  to `Segmented`.
- **Prerequisites:** T03.2 (merged, PR #4) and T11.1 (merged, PR #6).
- **Intended outcome:** the user picks System, Light or Dark from the
  keyboard; it applies at once, survives a relaunch, makes no remote
  connection, puts back a failed save, and yields to high contrast.
- **The gate result** and whether the custom peer was needed.
- **Screenshots:** the Settings card (light and dark), the Library and
  Reader in each theme, and the completion choice on the Game page.
  Embed them from the committed evidence folder.
- **CI:** the run links.
- End with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

Mark the PR ready. Don't merge: ask the user.
