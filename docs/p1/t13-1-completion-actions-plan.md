# T13.1 Completion Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** From the keyboard, the user marks a guide `Complete` or `In
progress` on the Game page or in the Reader. Both controls call T13.2's
`GuideCompletionService`, show the committed state, and announce it.
Reading to the last line or page changes nothing.

**Architecture:**

- Core gains `GuideCompletionPresentation`, the labels and copy.
- The app gains a `GuideCompletionChoice` UserControl around Toolkit
  `Segmented`. It holds no storage logic.
- `ShellWindow.Completion.cs` is the only caller of the service. It
  serializes the write with route changes through `RunNavigationAsync`.
- The installed `completion` group proves the UIA selected state, both
  surfaces, a relaunch, the last line and page, and a held-lock error.

**Tech Stack:** .NET 10, WinUI 3, CommunityToolkit.WinUI.Controls.Segmented
8.2.251219, SQLite, xUnit, PowerShell UI Automation.

**Spec:** [t13-1-completion-actions-design.md](t13-1-completion-actions-design.md)

## Global Constraints

**Branch:** `feat/p1-t13-1-completion-actions`, already checked out. The
spec is commit `3ff0e28`.

**Tooling:**

- There is no local `dotnet` or `pwsh`. Builds and tests run on the Windows
  host `pcsx2-win` or in CI.
- Host sync (staging folder `E:\work\desktop-guides\t13-1`, not a git
  checkout; create it once with `ssh -o BatchMode=yes pcsx2-win "mkdir E:\work\desktop-guides\t13-1"`):

  ```bash
  R=/Users/ilya.lissoboi/work/desktop-guides
  git -C $R ls-files -co --exclude-standard -z -- . ':!.claude' |
    tar -C $R --null -T - -cf - |
    ssh -o BatchMode=yes pcsx2-win "tar -xf - -C E:\work\desktop-guides\t13-1"
  ```

- Host test commands (the host's default shell is `cmd`):

  ```bash
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t13-1\tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj"
  ssh -o BatchMode=yes pcsx2-win "dotnet test E:\work\desktop-guides\t13-1\tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj"
  ```

- The CI loop for branch `<b>` and group `<g>`:
  1. Push, then run
     `gh workflow run windows-ci.yml --ref <b> -f shell-scope=<g> [-f dev-fast=true]`.
  2. Run
     `gh run list --workflow windows-ci.yml --branch <b> --limit 1 --json databaseId,headSha -q '.[0]'`
     and check that `headSha` matches `git rev-parse HEAD`.
  3. Run `gh run watch <id> --exit-status --interval 60`.
  4. On failure, run `gh run view <id> --log-failed`.
- The baseline on `main` (`0807d5a`) is Core.Tests 728 and
  Infrastructure.Tests 528.
- `dev-fast=true` runs are for iteration and are not PR evidence.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- PowerShell stays ASCII-only. Bash commands use absolute paths or
  `git -C`, never `cd`.

**Values (verbatim from the spec):**

- Item labels `In progress` and `Complete`; AutomationIds
  `CompletionInProgress` and `CompletionComplete`.
- `ChoiceName(title)` = `Completion for <title>`.
- `Announcement` = `<title> marked complete.` or `<title> marked in progress.`.
- `SaveFailed(title)` = `Could not update completion for <title>. Try again.`.
- `Removed` = `This guide is no longer in your library.`.
- `IsComplete(completedUtc)` is true only when a time is stored. The
  estimate and open time play no part.
- Package `CommunityToolkit.WinUI.Controls.Segmented` at `8.2.251219`.
- Titles are plain text, used verbatim.

**Rulings this plan makes against the spec** (Task 5 records them in the
spec's implementation notes):

1. **Focus is restored after the Game render.** `RenderCurrentAsync`
   collapses `GamePanel`, which drops keyboard focus. The spec's "focus
   stays because the header isn't rebuilt" is wrong. After a successful
   Game-page write the shell calls `GameCompletionChoice.FocusSelection()`.
2. **Busy doesn't disable the control.** Disabling the focused item would
   move focus away. While busy, the choice sets `IsHitTestVisible=false`,
   and a keyboard change snaps back to the pending choice. The spec says
   "stays disabled".
3. **No `Show(committed)` after the Game render.** The render calls
   `UpdateOpenSelectedGuideAction`, which shows the selected row's stored
   state. That also covers a selection change during the write.
4. **The request carries the title:**
   `GuideCompletionRequest(Guid GuideId, string Title, bool Complete)`,
   public like the UserControl.
5. **Estimates are compared by capture, not by literal.** `seed-progress`
   stores no estimates, so `~37%` and `~16%` never appear. The harness
   captures a row's HelpText before marking it complete, and asserts the
   exact same text after marking it in progress. T13.2's Infrastructure
   tests already prove the columns at the database level.
6. **`hold-write-lock` takes an optional hold time.** The error mode holds
   the lock for 120 s, so the app's 30 s SQLite timeout fails first.
7. **The error check spans three smoke modes.** The installer, not the
   smoke script, owns the lock helper, so it starts the lock between
   `completion-error-prepare` and `completion-error`, and releases it
   before `completion-error-retry`.

## Review Focus

1. **Rapid toggle while a write runs.** Pressing Left while `Complete` is
   pending should leave `Complete` selected and send no second write. The
   `completion-error` mode checks this.
2. **Route change during a write.** Back or a game switch queued behind
   the write runs after it. On success the UI is left alone and the next
   render shows the stored state. On failure the error status is replaced
   by the next page's status. Reviewers should check that no stale choice
   is shown and no write is lost.
3. **Guide removed during a write.** `ReadingStateMissingException`
   re-renders: the Game page drops the row, and the Reader returns to the
   Library. Reviewers check that nothing shows `Complete` for a missing
   guide.
4. **Close during a write.** `closeRequested` makes every branch return
   without touching the UI. The write either commits or fails silently.
5. **Reader content fails to load.** The choice is shown before the format
   adapter opens, so a missing TXT, HTML, or PDF copy still leaves the
   choice usable.

---
### Task 1: Core `GuideCompletionPresentation`

**Files:**
- Create: `src/DesktopGuides.Core/Library/GuideCompletionPresentation.cs`
- Test: `tests/DesktopGuides.Core.Tests/GuideCompletionPresentationTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces (namespace `DesktopGuides.Core.Library`):
  - `const string InProgressLabel`, `const string CompleteLabel`, `const string Removed`
  - `static bool IsComplete(DateTimeOffset? completedUtc)`
  - `static string ChoiceName(string title)`
  - `static string Announcement(string title, DateTimeOffset? completedUtc)`
  - `static string SaveFailed(string title)`

- [ ] **Step 1: Write the failing tests**

```csharp
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideCompletionPresentationTests
{
    private static readonly DateTimeOffset Finished =
        new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LabelsNameBothStates()
    {
        Assert.Equal("In progress", GuideCompletionPresentation.InProgressLabel);
        Assert.Equal("Complete", GuideCompletionPresentation.CompleteLabel);
    }

    [Fact]
    public void NoStoredTimeIsInProgress() =>
        Assert.False(GuideCompletionPresentation.IsComplete(null));

    [Fact]
    public void AStoredTimeIsComplete() =>
        Assert.True(GuideCompletionPresentation.IsComplete(Finished));

    [Fact]
    public void ChoiceNameNamesTheGuide() =>
        Assert.Equal("Completion for Walkthrough", GuideCompletionPresentation.ChoiceName("Walkthrough"));

    [Fact]
    public void AnnouncementForComplete() =>
        Assert.Equal("Walkthrough marked complete.",
            GuideCompletionPresentation.Announcement("Walkthrough", Finished));

    [Fact]
    public void AnnouncementForInProgress() =>
        Assert.Equal("Walkthrough marked in progress.",
            GuideCompletionPresentation.Announcement("Walkthrough", null));

    [Fact]
    public void SaveFailedOffersARetry() =>
        Assert.Equal("Could not update completion for Walkthrough. Try again.",
            GuideCompletionPresentation.SaveFailed("Walkthrough"));

    [Fact]
    public void RemovedMatchesTheReaderMessage() =>
        Assert.Equal("This guide is no longer in your library.", GuideCompletionPresentation.Removed);

    [Fact]
    public void TitlesAreKeptVerbatim()
    {
        string title = "Ōkami 大神 <b>&amp;</b> " + new string('x', 300);
        Assert.Equal($"Completion for {title}", GuideCompletionPresentation.ChoiceName(title));
        Assert.Equal($"{title} marked complete.", GuideCompletionPresentation.Announcement(title, Finished));
        Assert.Equal($"Could not update completion for {title}. Try again.",
            GuideCompletionPresentation.SaveFailed(title));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Sync to the host, then run the Core test command with
`--filter FullyQualifiedName~GuideCompletionPresentationTests`.
Expected: the build FAILS, and the only errors are `CS0103` naming
`GuideCompletionPresentation`.

- [ ] **Step 3: Write the implementation**

```csharp
namespace DesktopGuides.Core.Library;

/// <summary>The completion choice's labels, name, and status copy.</summary>
public static class GuideCompletionPresentation
{
    public const string InProgressLabel = "In progress";
    public const string CompleteLabel = "Complete";
    // The text the Reader already shows for a missing guide.
    public const string Removed = "This guide is no longer in your library.";

    // Only a stored completion time is complete; the estimate plays no part.
    public static bool IsComplete(DateTimeOffset? completedUtc) => completedUtc is not null;

    public static string ChoiceName(string title) => $"Completion for {title}";

    public static string Announcement(string title, DateTimeOffset? completedUtc) =>
        IsComplete(completedUtc) ? $"{title} marked complete." : $"{title} marked in progress.";

    public static string SaveFailed(string title) =>
        $"Could not update completion for {title}. Try again.";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Sync, then run the filtered command again. Expected: 9 passed. Then run the
full Core test command. Expected: Core.Tests 737 passed (728 + 9).

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add \
  src/DesktopGuides.Core/Library/GuideCompletionPresentation.cs \
  tests/DesktopGuides.Core.Tests/GuideCompletionPresentationTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(core): T13.1 completion presentation" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 2: `GuideCompletionChoice` control and the Segmented package

TDD skip: this is WinUI markup and selection plumbing with no headless
host. Task 3's installed modes are its tests. The skip is recorded here.

**Files:**
- Modify: `Directory.Packages.props` (beside the other Toolkit pins)
- Modify: `src/DesktopGuides.Production/DesktopGuides.Production.csproj` (beside the other Toolkit references)
- Modify: `src/DesktopGuides.Production/packages.lock.json` (regenerated)
- Create: `src/DesktopGuides.Production/GuideCompletionChoice.xaml`
- Create: `src/DesktopGuides.Production/GuideCompletionChoice.xaml.cs`

**Interfaces:**
- Consumes: Task 1's `GuideCompletionPresentation`.
- Produces (namespace `DesktopGuides.Production`):
  - `public sealed record GuideCompletionRequest(Guid GuideId, string Title, bool Complete)`
  - `public sealed partial class GuideCompletionChoice : UserControl` with
    `Show(Guid guideId, string title, bool complete)`, `Hide()`, `Revert()`,
    `SetBusy(bool busy)`, `FocusSelection()`, `Guid? GuideId`,
    `event EventHandler<GuideCompletionRequest>? CompletionRequested`.

- [ ] **Step 1: Add the package**

`Directory.Packages.props`, after the HeaderedControls pin:

```xml
    <PackageVersion Include="CommunityToolkit.WinUI.Controls.Segmented"
                    Version="8.2.251219" />
```

Match the line wrapping of the neighboring pins. In the csproj, after the
HeaderedControls reference:

```xml
    <PackageReference Include="CommunityToolkit.WinUI.Controls.Segmented" />
```

- [ ] **Step 2: Regenerate the lock file on the host**

```bash
ssh -o BatchMode=yes pcsx2-win "dotnet restore E:\work\desktop-guides\t13-1\src\DesktopGuides.Production\DesktopGuides.Production.csproj --force-evaluate -p:Platform=x64"
scp pcsx2-win:E:/work/desktop-guides/t13-1/src/DesktopGuides.Production/packages.lock.json \
  /Users/ilya.lissoboi/work/desktop-guides/src/DesktopGuides.Production/packages.lock.json
```

Expected: the restore succeeds, and `git diff --stat` on the lock file shows
only additions for `CommunityToolkit.WinUI.Controls.Segmented` in all three
targets (`net10.0-windows10.0.19041`, `/win-arm64`, `/win-x64`). If the
diff also changes unrelated packages, stop and investigate. Then sync and
check that the locked restore passes for both platforms:

```bash
ssh -o BatchMode=yes pcsx2-win "dotnet restore E:\work\desktop-guides\t13-1\src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 && dotnet restore E:\work\desktop-guides\t13-1\src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=ARM64"
```

- [ ] **Step 3: Write the control**

`GuideCompletionChoice.xaml`:

```xml
<UserControl
    x:Class="DesktopGuides.Production.GuideCompletionChoice"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:toolkit="using:CommunityToolkit.WinUI.Controls"
    Visibility="Collapsed">
    <toolkit:Segmented x:Name="Choice"
                       VerticalAlignment="Top"
                       AutomationProperties.AutomationId="CompletionChoice">
        <toolkit:SegmentedItem x:Name="InProgressItem"
                               AutomationProperties.AutomationId="CompletionInProgress" />
        <toolkit:SegmentedItem x:Name="CompleteItem"
                               AutomationProperties.AutomationId="CompletionComplete" />
    </toolkit:Segmented>
</UserControl>
```

`GuideCompletionChoice.xaml.cs`:

```csharp
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed record GuideCompletionRequest(Guid GuideId, string Title, bool Complete);

// Shows a guide's committed completion and raises a request when the user
// changes it. It holds no storage logic.
public sealed partial class GuideCompletionChoice : UserControl
{
    private bool showing;
    private bool busy;
    private bool shownComplete;
    private bool pendingComplete;

    public GuideCompletionChoice()
    {
        InitializeComponent();
        InProgressItem.Content = GuideCompletionPresentation.InProgressLabel;
        CompleteItem.Content = GuideCompletionPresentation.CompleteLabel;
        Choice.SelectionChanged += SelectionChanged;
    }

    // Raised only by a user change of selection.
    public event EventHandler<GuideCompletionRequest>? CompletionRequested;

    public Guid? GuideId { get; private set; }

    private string title = string.Empty;

    // Shows the committed state without raising CompletionRequested.
    public void Show(Guid guideId, string guideTitle, bool complete)
    {
        GuideId = guideId;
        title = guideTitle;
        shownComplete = complete;
        AutomationProperties.SetName(Choice, GuideCompletionPresentation.ChoiceName(guideTitle));
        Select(complete);
        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        GuideId = null;
    }

    // Puts the selection back to the last committed state.
    public void Revert() => Select(shownComplete);

    // Busy keeps focus where it is: pointer input is ignored, and a keyboard
    // change snaps back to the pending choice.
    public void SetBusy(bool value)
    {
        busy = value;
        pendingComplete = ReferenceEquals(Choice.SelectedItem, CompleteItem);
        Choice.IsHitTestVisible = !value;
    }

    public void FocusSelection()
    {
        Control item = shownComplete ? CompleteItem : InProgressItem;
        if (!item.Focus(FocusState.Programmatic))
        {
            // The panel was just shown; focus after its first layout.
            DispatcherQueue.TryEnqueue(() => item.Focus(FocusState.Programmatic));
        }
    }

    private void Select(bool complete)
    {
        showing = true;
        try
        {
            Choice.SelectedItem = complete ? CompleteItem : InProgressItem;
        }
        finally
        {
            showing = false;
        }
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (showing)
        {
            return;
        }
        bool restore = busy ? pendingComplete : shownComplete;
        if (Choice.SelectedIndex < 0)
        {
            // Ctrl+Space can clear a ListView selection; a choice always has one.
            DispatcherQueue.TryEnqueue(() => Select(restore));
            return;
        }
        bool complete = ReferenceEquals(Choice.SelectedItem, CompleteItem);
        if (busy)
        {
            if (complete != pendingComplete)
            {
                DispatcherQueue.TryEnqueue(() => Select(pendingComplete));
            }
            return;
        }
        if (complete == shownComplete || GuideId is not Guid guideId)
        {
            return;
        }
        CompletionRequested?.Invoke(this, new GuideCompletionRequest(guideId, title, complete));
    }
}
```

- [ ] **Step 4: Build on the host**

```bash
ssh -o BatchMode=yes pcsx2-win "dotnet build E:\work\desktop-guides\t13-1\src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64"
```

Expected: build succeeded with 0 errors and no new warnings. The control
isn't placed yet, so nothing else changes.

- [ ] **Step 5: Commit**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add Directory.Packages.props \
  src/DesktopGuides.Production/DesktopGuides.Production.csproj \
  src/DesktopGuides.Production/packages.lock.json \
  src/DesktopGuides.Production/GuideCompletionChoice.xaml \
  src/DesktopGuides.Production/GuideCompletionChoice.xaml.cs
git -C $R commit -m "feat(shell): T13.1 completion choice control" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 3: Installed `completion` group (the failing tests)

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs:17-18` (lock hold time), `:694` (usage)
- Modify: `tools/p1/windows_shell_install.ps1` (switch, group, lock parameter, timeout, `Run-CompletionScenarios`, dispatch)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (ValidateSet, reader-block list, `Wait-Status` timeout, `completion-*` branch)
- Modify: `.github/workflows/windows-ci.yml:14-29` (option), `~:368` (switch map)

**Interfaces:**
- Consumes: the AutomationIds `CompletionInProgress` and `CompletionComplete`,
  the item names `In progress` and `Complete`, and the copy from Task 1.
- Produces: smoke modes `completion-segmented`, `completion-last-page`,
  `completion-game`, `completion-reader`, `completion-restart`,
  `completion-restart-after`, `completion-error-prepare`, `completion-error`,
  `completion-error-retry`; installer switch `-CompletionOnly`; CI
  `shell-scope=completion`.

- [ ] **Step 1: ShellSeed takes an optional hold time**

Replace the guard at `Program.cs:17-18`:

```csharp
if (args.Length is 4 or 5 &&
    args[0] is "hold-write-lock" or "hold-read-lock")
```

Replace `DateTime deadline = DateTime.UtcNow.AddSeconds(30);` with:

```csharp
        // The holder outlasts a caller's 30 s SQLite timeout when asked to.
        int holdSeconds = args.Length == 5 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 30;
        DateTime deadline = DateTime.UtcNow.AddSeconds(holdSeconds);
```

Add `using System.Globalization;` if it's missing. In the usage text change
`<ready-path> <release-path>` to `<ready-path> <release-path> [hold-seconds]`.

- [ ] **Step 2: The installer's lock helper passes it**

In `Start-ShellDatabaseLock`, add a fourth parameter `[int] $HoldSeconds = 0`
and, after `$lockArguments` is built:

```powershell
    if ($HoldSeconds -gt 0) { $lockArguments += [string]$HoldSeconds }
```

- [ ] **Step 3: Register the group**

- Add `[switch] $CompletionOnly,` after `$ProgressOnly` in the parameters.
- Add `'completion' = $CompletionOnly.IsPresent` after `'progress'` in
  `$scenarioGroups`.
- In `Run-ShellSmoke`, add `-or $mode -like 'completion-*'` to the 240 s
  timeout condition at `:742`.
- After `if (Enter-ScenarioGroup 'progress') { Run-ProgressScenarios }` add
  `if (Enter-ScenarioGroup 'completion') { Run-CompletionScenarios }`.
- In `windows-ci.yml`, add `- completion` after `- progress` in the
  `shell-scope` options, and `'completion' = 'CompletionOnly'` after
  `'progress' = 'ProgressOnly'` in `$groupSwitches`.

- [ ] **Step 4: Write `Run-CompletionScenarios`**

Put it after `Run-ProgressScenarios`. It reuses `Invoke-ProgressPass`, which
starts the shell, runs one smoke mode, and closes the shell.

```powershell
function Get-StoredCompletion([string] $title) {
    $stored = Invoke-ShellSeed @('describe-progress', $dataRoot) | ConvertFrom-Json
    $state = @($stored | Where-Object { $_.title -eq $title })
    if ($state.Count -ne 1) { throw "No stored state for '$title'." }
    return $state[0]
}

function Assert-NotCompleted([string] $title, [string] $step) {
    $state = Get-StoredCompletion $title
    if ($null -ne $state.completedUtcMs) { throw "$step left '$title' completed." }
    return $state
}

function Assert-Completed([string] $title, [string] $step) {
    $state = Get-StoredCompletion $title
    if ($null -eq $state.completedUtcMs) { throw "$step did not complete '$title'." }
    return $state
}

function Run-CompletionScenarios {
    # TR13.1-TR13.2: completion is an explicit choice on the Game page and in
    # the Reader, it survives a relaunch, the last line or page never sets it,
    # and a failed write is shown and put back.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\pdf-long.pdf'))) {
        throw 'pdf-long.pdf is missing; run tools/p0/make_fixtures.py first.'
    }
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    $ids = Invoke-ShellSeed @('seed-progress', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    $report.completion = [ordered]@{ ids = $ids }
    $lockDir = Join-Path $env:TEMP "dg-completion-lock-$PID"
    try {
        # The Segmented gate runs first: a failure here means the RadioButtons fallback.
        $report.completion.segmented = Invoke-ProgressPass 'completion-segmented' 0

        $report.completion.lastPage = Invoke-ProgressPass 'completion-last-page' 0
        foreach ($title in 'Numbered Lines Guide', 'Long PDF Guide') {
            $state = Assert-NotCompleted $title 'Reading to the end'
            if ($null -eq $state.estimate -or [double]$state.estimate -lt 0.9) {
                throw "'$title' estimate after the end is $($state.estimate)."
            }
        }

        $originalTheme = Get-AppThemePreference
        try {
            Set-AppThemePreference $true
            $report.completion.gameLight = Invoke-ProgressPass 'completion-game' 0 -resultName 'completion-game-light'
            $report.completion.readerLight = Invoke-ProgressPass 'completion-reader' 0 -resultName 'completion-reader-light'
            Set-AppThemePreference $false
            $report.completion.gameDark = Invoke-ProgressPass 'completion-game' 0 -resultName 'completion-game-dark'
            $report.completion.readerDark = Invoke-ProgressPass 'completion-reader' 0 -resultName 'completion-reader-dark'
        }
        finally {
            Restore-AppThemePreference $originalTheme
        }
        [void](Assert-NotCompleted 'Numbered Lines Guide' 'completion-game')
        $webBefore = Assert-Completed 'Long Web Guide' 'completion-reader'

        $report.completion.restart = Invoke-ProgressPass 'completion-restart' 0
        $report.completion.restartAfter = Invoke-ProgressPass 'completion-restart-after' 0
        $webAfter = Assert-NotCompleted 'Long Web Guide' 'completion-restart'
        if ([Math]::Abs([double]$webAfter.estimate - [double]$webBefore.estimate) -gt 0.01) {
            throw "Long Web Guide estimate moved from $($webBefore.estimate) to $($webAfter.estimate)."
        }

        # A held write lock makes the app's write time out after 30 s.
        New-Item -ItemType Directory -Force $lockDir | Out-Null
        $ready = Join-Path $lockDir 'ready'
        $release = Join-Path $lockDir 'release'
        Start-InstalledShell
        $report.completion.errorPrepare = Run-ShellSmoke 'completion-error-prepare' `
            -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        $lock = Start-ShellDatabaseLock 'hold-write-lock' $ready $release -HoldSeconds 120
        try {
            $report.completion.error = Run-ShellSmoke 'completion-error' `
                -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        }
        finally {
            Release-ShellDatabaseLock $lock $release
        }
        [void](Assert-NotCompleted 'Numbered Lines Guide' 'completion-error')
        $report.completion.errorRetry = Run-ShellSmoke 'completion-error-retry' `
            -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        Close-InstalledShell
        [void](Assert-Completed 'Numbered Lines Guide' 'completion-error-retry')
    }
    finally {
        Remove-Item -LiteralPath $lockDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    }
}
```

`Invoke-ProgressPass` opens the progress diagnostic gates; they're harmless
here and keep one launch helper.

- [ ] **Step 5: Register the smoke modes**

In `windows_shell_ui_smoke.ps1`:

- Append to the `-Mode` ValidateSet, after `'progress-changed'`:
  `'completion-segmented', 'completion-last-page', 'completion-game', 'completion-reader', 'completion-restart', 'completion-restart-after', 'completion-error-prepare', 'completion-error', 'completion-error-retry'`.
- Append the same nine names to the reader block's `-in` list (`:1198-1200`).
- In `$textGame`, change `elseif ($Mode -like 'progress-*')` to
  `elseif ($Mode -like 'progress-*' -or $Mode -like 'completion-*')`.
- Give `Wait-Status` a timeout parameter. Its signature becomes
  `function Wait-Status([string[]] $expected, [switch] $AllowHidden, [int] $Seconds = 15)`,
  and `$deadline = (Get-Date).AddSeconds(15)` becomes
  `$deadline = (Get-Date).AddSeconds($Seconds)`.

- [ ] **Step 6: Write the `completion-*` branch**

Add it after the `progress-*` branch closes, inside the reader block:

```powershell
        elseif ($Mode -like 'completion-*') {
            $numbered = 'Numbered Lines Guide'
            $web = 'Long Web Guide'
            $pdf = 'Long PDF Guide'

            function Test-ItemSelected([string] $id) {
                $item = Find-ById $id
                if (-not $item -or $item.Current.IsOffscreen) { return $false }
                return $item.GetCurrentPattern(
                    [System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
            }

            # The committed state is the only selected item.
            function Wait-CompletionShown([bool] $complete, [string] $step) {
                $on = if ($complete) { 'CompletionComplete' } else { 'CompletionInProgress' }
                $off = if ($complete) { 'CompletionInProgress' } else { 'CompletionComplete' }
                $deadline = (Get-Date).AddSeconds(10)
                do {
                    if ((Test-ItemSelected $on) -and -not (Test-ItemSelected $off)) { return }
                    Start-Sleep -Milliseconds 100
                } while ((Get-Date) -lt $deadline)
                throw "$step expected '$on' as the only selected completion item."
            }

            function Focus-Completion {
                $id = if (Test-ItemSelected 'CompletionComplete') { 'CompletionComplete' }
                      else { 'CompletionInProgress' }
                (Wait-VisibleById $id).SetFocus()
                Wait-FocusedId $id
            }

            function Send-Keys([string] $keys) {
                [System.Windows.Forms.SendKeys]::SendWait($keys)
            }

            function Get-RowHelp([string] $name) {
                $row = @(Get-ListRows 'GuideList' | Where-Object { $_.Current.Name -eq $name })
                if ($row.Count -ne 1) { return $null }
                return $row[0].Current.HelpText
            }

            # -Exact compares the whole text; otherwise $pattern is a -like pattern.
            function Wait-RowHelp([string] $name, [string] $pattern, [switch] $Exact, [switch] $NotCompleted) {
                $deadline = (Get-Date).AddSeconds(10)
                do {
                    $help = Get-RowHelp $name
                    $matched = $help -and $(if ($Exact) { $help -ceq $pattern } else { $help -like $pattern })
                    if ($matched -and $NotCompleted) { $matched = $help -notlike '*Completed*' }
                    if ($matched) { return $help }
                    Start-Sleep -Milliseconds 200
                } while ((Get-Date) -lt $deadline)
                throw "Row '$name' help text was '$help'; expected '$pattern'."
            }

            function Anchor-Guide([string] $guide) {
                Open-TextGuide $guide
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                Back-ToTextGame
                [void](Wait-VisibleById 'CompletionInProgress')
            }

            if ($Mode -notin @('completion-error', 'completion-error-retry')) {
                [void](Wait-Name 'LibraryHeading' 'Library')
                Resize-ShellWindow 1500 720
                Select-Element $textGame
                [void](Wait-Name 'GameHeading' $textGame)
                [void](Wait-Status 'Game ready.')
            }

            if ($Mode -eq 'completion-segmented') {
                # The UIA gate: two list items with their names, the selected
                # state matching storage and following the arrow keys.
                Anchor-Guide $numbered
                $choice = Wait-VisibleById 'CompletionChoice'
                if ($choice.Current.Name -ne "Completion for $numbered") {
                    throw "The choice is named '$($choice.Current.Name)'."
                }
                foreach ($pair in @(@('CompletionInProgress', 'In progress'), @('CompletionComplete', 'Complete'))) {
                    $item = Wait-VisibleById $pair[0]
                    if ($item.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem -or
                        $item.Current.Name -ne $pair[1]) {
                        throw "$($pair[0]) is a $($item.Current.ControlType.ProgrammaticName) named '$($item.Current.Name)'."
                    }
                }
                Wait-CompletionShown $false 'A guide never completed'
                (Wait-VisibleById 'OpenSelectedGuide').SetFocus()
                Wait-FocusedId 'OpenSelectedGuide'
                Send-Keys '+{TAB}'
                Wait-FocusedId 'CompletionInProgress'
                Send-Keys '{RIGHT}'
                [void](Wait-Status "$numbered marked complete.")
                Wait-CompletionShown $true 'Right'
                Wait-FocusedId 'CompletionComplete'
                Send-Keys '{LEFT}'
                [void](Wait-Status "$numbered marked in progress.")
                Wait-CompletionShown $false 'Left'
                Wait-FocusedId 'CompletionInProgress'
                $report.phases += 'completion-segmented'
            }
            elseif ($Mode -eq 'completion-last-page') {
                # TR13.1: reaching the last line or page doesn't complete a guide.
                Open-TextGuide $numbered
                [void](Wait-Status 'Guide ready.')
                Invoke-ReaderCommand 'Go to end'
                Start-Sleep -Seconds 1
                Back-ToTextGame
                Open-TextGuide $pdf
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                Invoke-ReaderCommand 'Go to end'
                [void](Wait-PdfPage 200 200 'page 200 of 200' 60)
                Back-ToTextGame
                $report.completionLastPageRows = @(
                    (Wait-RowHelp $numbered 'Text (TXT), about * percent, opened today at *'),
                    (Wait-RowHelp $pdf 'PDF, about * percent, opened today at *'))
                Wait-CompletionShown $false 'The last PDF page'
                $report.phases += 'completion-last-page'
            }
            elseif ($Mode -eq 'completion-game') {
                # The Game page: complete, then back to in progress with the
                # row's estimate and open time shown exactly as before.
                Anchor-Guide $numbered
                $before = Wait-RowHelp $numbered 'Text (TXT), *' -NotCompleted
                Focus-Completion
                Send-Keys '{RIGHT}'
                [void](Wait-Status "$numbered marked complete.")
                $completed = Wait-RowHelp $numbered 'Text (TXT), Completed, opened today at *'
                Wait-CompletionShown $true 'Marking complete on the Game page'
                Wait-FocusedId 'CompletionComplete'
                $report.completionGameScreenshot = Save-WindowScreenshot 'completion-game'
                Send-Keys '{LEFT}'
                [void](Wait-Status "$numbered marked in progress.")
                $after = Wait-RowHelp $numbered $before -Exact
                Wait-CompletionShown $false 'Marking in progress on the Game page'
                Wait-FocusedId 'CompletionInProgress'
                $report.completionGameRows = [ordered]@{ before = $before; completed = $completed; after = $after }
                $report.phases += 'completion-game'
            }
            elseif ($Mode -eq 'completion-reader') {
                # The light pass finds Web in progress and completes it. The
                # dark pass finds it complete, toggles twice, and ends complete.
                Open-TextGuide $web
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                [void](Wait-VisibleById 'CompletionChoice')
                $startedComplete = Test-ItemSelected 'CompletionComplete'
                Focus-Completion
                if ($startedComplete) {
                    Send-Keys '{LEFT}'
                    [void](Wait-Status "$web marked in progress.")
                    Wait-CompletionShown $false 'Marking in progress in the Reader'
                    Wait-FocusedId 'CompletionInProgress'
                }
                Send-Keys '{RIGHT}'
                [void](Wait-Status "$web marked complete.")
                Wait-CompletionShown $true 'Marking complete in the Reader'
                Wait-FocusedId 'CompletionComplete'
                $report.completionReaderStartedComplete = $startedComplete
                $report.completionReaderScreenshot = Save-WindowScreenshot 'completion-reader'
                Back-ToTextGame
                $report.completionReaderRow = Wait-RowHelp $web 'Web page (HTML), Completed, opened today at *'
                $report.phases += 'completion-reader'
            }
            elseif ($Mode -eq 'completion-restart') {
                # TR13.2: completion survives a relaunch, and so does clearing it.
                [void](Wait-RowHelp $web 'Web page (HTML), Completed, opened today at *')
                Open-TextGuide $web
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                Wait-CompletionShown $true 'Reopening a completed guide'
                Focus-Completion
                Send-Keys '{LEFT}'
                [void](Wait-Status "$web marked in progress.")
                Wait-CompletionShown $false 'Marking in progress after a relaunch'
                Back-ToTextGame
                $report.phases += 'completion-restart'
            }
            elseif ($Mode -eq 'completion-restart-after') {
                $report.completionRestartRow = Wait-RowHelp $web 'Web page (HTML), *opened today at *' -NotCompleted
                Open-TextGuide $web
                [void](Wait-Status 'Guide ready.' -Seconds 60)
                Wait-CompletionShown $false 'Reopening after clearing completion'
                Back-ToTextGame
                $report.phases += 'completion-restart-after'
            }
            elseif ($Mode -eq 'completion-error-prepare') {
                # The installer takes the write lock after this mode.
                Anchor-Guide $numbered
                Wait-CompletionShown $false 'Before the held lock'
                $report.phases += 'completion-error-prepare'
            }
            elseif ($Mode -eq 'completion-error') {
                # The write waits on the held lock. Left while it waits keeps
                # the pending choice; the timeout shows the error and puts
                # the choice back.
                Focus-Completion
                Send-Keys '{RIGHT}'
                Wait-CompletionShown $true 'The pending write'
                Send-Keys '{LEFT}'
                Start-Sleep -Seconds 1
                Wait-CompletionShown $true 'Left while the write is pending'
                [void](Wait-Status "Could not update completion for $numbered. Try again." -Seconds 60)
                Wait-CompletionShown $false 'After the failed write'
                [void](Wait-RowHelp $numbered 'Text (TXT), *' -NotCompleted)
                $report.completionErrorScreenshot = Save-WindowScreenshot 'completion-error'
                $report.phases += 'completion-error'
            }
            else {
                # completion-error-retry: the lock is gone and the retry commits.
                Focus-Completion
                Send-Keys '{RIGHT}'
                [void](Wait-Status "$numbered marked complete.")
                Wait-CompletionShown $true 'The retry'
                $report.phases += 'completion-error-retry'
            }
        }
```

- [ ] **Step 7: Parse-check the scripts**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
LC_ALL=C grep -nP '[^\x00-\x7F]' $R/tools/p1/windows_shell_ui_smoke.ps1 $R/tools/p1/windows_shell_install.ps1 || echo ascii-ok
```

Expected: `ascii-ok`. Then sync and run the host's parser on both scripts:

```bash
ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -Command \"foreach (\$f in 'windows_shell_ui_smoke.ps1','windows_shell_install.ps1') { \$e = \$null; [void][System.Management.Automation.Language.Parser]::ParseFile('E:\work\desktop-guides\t13-1\tools\p1\' + \$f, [ref]\$null, [ref]\$e); if (\$e) { \$e } else { \$f + ' ok' } }\""
```

Expected: both `ok`.

- [ ] **Step 8: Watch the group fail**

Commit, push, then run the CI loop with `shell-scope=completion` and
`dev-fast=true`.

Expected: `dev-production-shell-ui` FAILS in `completion-segmented` with
`Expected visible 'CompletionInProgress'.` The shell doesn't place the
control yet. Any other failure is a harness bug to fix first.

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_install.ps1 \
  tools/p1/windows_shell_ui_smoke.ps1 .github/workflows/windows-ci.yml
git -C $R commit -m "test(shell): T13.1 installed completion group" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C $R push -u origin feat/p1-t13-1-completion-actions
```

---
### Task 4: Shell wiring (the group passes)

**Files:**
- Modify: `src/DesktopGuides.Production/GuideRowItem.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (`GameGuidesHeader` ~:341, Reader header ~:417)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (constructor ~:129, `InitializeCoreAsync` ~:278, `UpdateOpenSelectedGuideAction` ~:564, `RenderCurrentAsync` preamble ~:1463 and Reader branch ~:1673)
- Create: `src/DesktopGuides.Production/ShellWindow.Completion.cs`

**Interfaces:**
- Consumes: Task 1's presentation, Task 2's `GuideCompletionChoice` and
  `GuideCompletionRequest`, T13.2's
  `GuideCompletionService(IGuideCompletionStore, TimeProvider)` with
  `MarkCompleteAsync`/`MarkInProgressAsync` returning `Task<DateTimeOffset?>`,
  and `ReadingStateMissingException` (`DesktopGuides.Core.Library`).
- Produces: XAML names `GameCompletionChoice` and `ReaderCompletionChoice`;
  `GuideRowItem.CompletedUtc`.

- [ ] **Step 1: Keep the row's completion time**

In `GuideRowItem`'s constructor body add `CompletedUtc = summary.State?.CompletedUtc;`,
and below `Guide`:

```csharp
    internal DateTimeOffset? CompletedUtc { get; }
```

- [ ] **Step 2: Place the controls**

`GameGuidesHeader` gets a fifth column. Its definitions become `*`, `Auto`,
`Auto`, `Auto`, `Auto`. Insert after the "Guides" TextBlock:

```xml
                    <local:GuideCompletionChoice x:Name="GameCompletionChoice"
                                                 Grid.Column="1" />
```

Then `OpenSelectedGuideButton` moves to `Grid.Column="2"`,
`RemoveSelectedGuideButton` to `3`, and `ImportGuideButton` to `4`.

In the Reader header grid, replace the format `Border` in column 1 with a
stack that holds the choice and then the unchanged Border (drop the
Border's `Grid.Column`):

```xml
                    <StackPanel Grid.Column="1"
                                Orientation="Horizontal"
                                VerticalAlignment="Top"
                                Spacing="{StaticResource DesktopGuidesSpacing16}">
                        <local:GuideCompletionChoice x:Name="ReaderCompletionChoice" />
                        <Border Padding="10,4"
                                ...the existing Border attributes and ReaderFormat, unchanged... />
                    </StackPanel>
```

- [ ] **Step 3: Write `ShellWindow.Completion.cs`**

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Navigation;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Production;

// The only caller of GuideCompletionService. The write runs in the
// navigation queue, so it is serialized with route changes.
public sealed partial class ShellWindow
{
    private GuideCompletionService? completion;
    private bool completionRequested;

    private async void CompletionChoiceRequested(object? sender, GuideCompletionRequest request)
    {
        if (sender is not GuideCompletionChoice choice)
        {
            return;
        }
        GuideCompletionService? service = completion;
        if (service is null || completionRequested || closeRequested ||
            removeRequested || importRequested)
        {
            choice.Revert();
            return;
        }
        ShellRoute? route = navigator.Current;
        completionRequested = true;
        GameCompletionChoice.SetBusy(true);
        ReaderCompletionChoice.SetBusy(true);
        bool written = false;
        try
        {
            await RunNavigationAsync(async () =>
            {
                written = true;
                DateTimeOffset? committed;
                try
                {
                    committed = request.Complete
                        ? await service.MarkCompleteAsync(request.GuideId)
                        : await service.MarkInProgressAsync(request.GuideId);
                }
                catch (ReadingStateMissingException)
                {
                    if (closeRequested)
                    {
                        return;
                    }
                    // The Game page drops the row; the Reader returns to the Library.
                    await RenderCurrentAsync();
                    ShowWarningStatus(GuideCompletionPresentation.Removed);
                    return;
                }
                catch (Exception)
                {
                    if (closeRequested)
                    {
                        return;
                    }
                    if (choice.GuideId == request.GuideId)
                    {
                        choice.Revert();
                    }
                    ShowErrorStatus(GuideCompletionPresentation.SaveFailed(request.Title));
                    return;
                }
                if (closeRequested || !Equals(navigator.Current, route))
                {
                    // The next render shows the committed state.
                    return;
                }
                string announcement = GuideCompletionPresentation.Announcement(request.Title, committed);
                if (route is GameRoute)
                {
                    // The render refreshes the row fact and the choice; it
                    // collapses the panel, so focus is put back.
                    await RenderCurrentAsync();
                    if (GameCompletionChoice.GuideId == request.GuideId)
                    {
                        GameCompletionChoice.FocusSelection();
                    }
                    ShowTransientStatus(announcement);
                }
                else if (route is ReaderRoute reader && reader.GuideId == request.GuideId)
                {
                    ReaderCompletionChoice.Show(
                        request.GuideId, request.Title, GuideCompletionPresentation.IsComplete(committed));
                    ShowTransientStatus(announcement);
                }
            });
        }
        finally
        {
            completionRequested = false;
            GameCompletionChoice.SetBusy(false);
            ReaderCompletionChoice.SetBusy(false);
            // RunNavigationAsync skips the action once a close begins.
            if (!written && !closeRequested && choice.GuideId == request.GuideId)
            {
                choice.Revert();
            }
        }
    }
}
```

The usings are checked: `ShellRoute`, `GameRoute` and `ReaderRoute` are in
`DesktopGuides.Core.Navigation`, `GuideCompletionService` in
`DesktopGuides.Core.Reading`, and `ReadingStateMissingException` and
`ReadingState` in `DesktopGuides.Core.Library`.

- [ ] **Step 4: Wire the service and the events**

- In the constructor, after `ReaderActions.CommandFailed += ShowErrorStatus;`:

  ```csharp
          GameCompletionChoice.CompletionRequested += CompletionChoiceRequested;
          ReaderCompletionChoice.CompletionRequested += CompletionChoiceRequested;
  ```

- In `InitializeCoreAsync`, after `guideRemover = new GuideRemover(repository, paths);`:

  ```csharp
              completion = new GuideCompletionService(repository, TimeProvider.System);
  ```

- In the `RenderCurrentAsync` preamble, after
  `RemoveSelectedGuideButton.Visibility = Visibility.Collapsed;`:

  ```csharp
          GameCompletionChoice.Hide();
          ReaderCompletionChoice.Hide();
  ```

- In `UpdateOpenSelectedGuideAction`, inside the visible branch, add after
  the two `SetName` calls:

  ```csharp
              if (GuideList.SelectedItem is GuideRowItem row)
              {
                  GameCompletionChoice.Show(guide.Id, guide.Title,
                      GuideCompletionPresentation.IsComplete(row.CompletedUtc));
              }
  ```

  and in the `else` branch add `GameCompletionChoice.Hide();`.

- In the Reader branch of `RenderCurrentAsync`, after
  `ReaderFormat.Text = ...;`:

  ```csharp
                      // Shown before the content opens, so it stays usable when a load fails.
                      ReadingState? readingState = await library.GetReadingStateAsync(guide.Id);
                      if (generation != renderGeneration)
                      {
                          return false;
                      }
                      ReaderCompletionChoice.Show(guide.Id, guide.Title,
                          GuideCompletionPresentation.IsComplete(readingState?.CompletedUtc));
  ```

- [ ] **Step 5: Build and run the headless suites**

Sync, then build the Production project as in Task 2 Step 4, then run both
host test commands.

Expected: build succeeded with no new warnings; Core.Tests 737 and
Infrastructure.Tests 528 passed.

- [ ] **Step 6: Run the group**

Commit and push, then run the CI loop with `shell-scope=completion` and
`dev-fast=true`.

Expected: `dev-production-shell-ui` passes, and the downloaded result's
`completion` object holds every phase from `completion-segmented` to
`completion-error-retry`.

If `completion-segmented` fails on the selected state, the names, the
ListItem type, or the arrow keys, apply the fallback: in
`GuideCompletionChoice.xaml` replace `toolkit:Segmented` with `RadioButtons`
holding two `RadioButton`s with the same `x:Name`s and AutomationIds, swap
`SelectedItem`/`SelectedIndex` for the `RadioButtons` equivalents, change the
`ListItem` check and `SelectionItemPattern` read in the smoke to
`RadioButton`, and remove the package from Task 2's three files. Record the
failing UIA output in the spec's implementation notes.

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add src/DesktopGuides.Production/GuideRowItem.cs src/DesktopGuides.Production/ShellWindow.xaml \
  src/DesktopGuides.Production/ShellWindow.xaml.cs src/DesktopGuides.Production/ShellWindow.Completion.cs
git -C $R commit -m "feat(shell): T13.1 Game and Reader completion actions" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C $R push
```

---
### Task 5: Evidence runs and docs

**Files:**
- Create: `docs/p1/evidence/t13-1-completion-actions/` (result JSON excerpts and the light and dark screenshots)
- Modify: `docs/p1/t13-1-completion-actions-design.md` (status, implementation notes, verification)
- Modify: `docs/p1/e2e-testing.md` (the `completion` group)
- Modify: `docs/p1/implementation-plan.md`, `docs/work-breakdown.md`, `docs/p1-technical-design.md` §8 S13 (T13.1 lines)

**Interfaces:**
- Consumes: the CI run IDs and result files from Tasks 3-4.
- Produces: the PR evidence.

- [ ] **Step 1: Optional host installed run**

The CI group run is the required evidence. A host run is optional. Before
one, check for an installed Preview package with
`ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -Command \"Get-AppxPackage *DesktopGuides* | Select Name, Version\""`.
The installer imports a certificate into `Cert:\LocalMachine\TrustedPeople`,
which needs an elevated scheduled task. **Stop and ask the user before any
elevated run.** If approved, follow [e2e-testing.md](e2e-testing.md),
including its data-backup and cleanup rules.

- [ ] **Step 2: Full CI run**

Run the CI loop with `shell-scope=completion` and no `dev-fast`.
Expected: every job passes, including ARM64, packages,
`production-shell-ui` and `reader-toolbar-ui`, with Core.Tests 737 and
Infrastructure.Tests 528. Then run the loop with `shell-scope=progress` and
no `dev-fast`, because the `Wait-Status` and lock-helper changes touch
shared harness code. Expected: every job passes.

- [ ] **Step 3: Save the evidence**

Download the `production-shell-ui` artifact with
`gh run download <id> -D /tmp/t13-1-full`, copy the `completion-*` result
JSON and the eight screenshots (`completion-game`, `completion-reader` for
light and dark, `completion-error`, plus the segmented gate's result) into
`docs/p1/evidence/t13-1-completion-actions/`, and check each screenshot by
eye: the choice sits before *Open selected guide* on the Game page and
before the format badge in the Reader, and neither clips at 1500 px.

- [ ] **Step 4: Update the docs**

- Spec: status line `implemented; CI run <id> passed shell-scope=completion
  and the full matrix.` Implementation notes list the seven rulings from
  this plan's Global Constraints and the `Segmented` gate's result (passed,
  or the fallback and its UIA output). Verification lists the Core tests,
  each installed mode with what it showed, and the run IDs and counts.
- `e2e-testing.md`: a `completion` row in the scenario-group table, with
  `-CompletionOnly`, the nine modes, the 120 s lock hold, and the screenshots.
- `implementation-plan.md`, `work-breakdown.md`, and §8 S13 T13.1 of
  `p1-technical-design.md`: mark T13.1 done with the PR, and record the
  `Segmented` decision in §8 if the fallback was used.

- [ ] **Step 5: Commit and push**

```bash
R=/Users/ilya.lissoboi/work/desktop-guides
git -C $R add docs/p1/evidence/t13-1-completion-actions docs/p1/t13-1-completion-actions-design.md \
  docs/p1/e2e-testing.md docs/p1/implementation-plan.md docs/work-breakdown.md docs/p1-technical-design.md
git -C $R commit -m "docs(p1): T13.1 record the implementation and evidence" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git -C $R push
```
