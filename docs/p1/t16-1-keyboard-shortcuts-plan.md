# T16.1 Keyboard Shortcuts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every keyboard shortcut works in its context and nowhere else,
never takes keys away from typing, matches a visible control, and appears
in one Settings list. Reader shortcuts also work while focus is inside an
HTML guide.

**Architecture:** Core gains `KeyboardShortcuts` (the shortcut table as data)
and `ShortcutRouting` (run or ignore, by route, text focus and open dialog).
The shell gains a `ShellWindow.Keyboard.cs` partial with Ctrl+F, Ctrl+O and
the Library search's Esc, and turns the reader toolbar's keys on for TXT and
HTML. HTML forwarding uses whichever mechanism the Task 3 probe proves. A
`ShellWindow.Shortcuts.cs` partial builds the Settings list from the table.

**Tech Stack:** .NET 10, WinUI 3 (`KeyboardAccelerator`, WebView2 and the
DevTools protocol), CommunityToolkit SettingsControls, xUnit, the PowerShell
5.1 UI Automation harness.

**Spec:** `docs/p1/t16-1-keyboard-shortcuts-design.md`

**Target:** T16.1, in one PR on `feat/p1-t16-1-keyboard-shortcuts`.
**Prerequisites:** T05.2, T06.1, T08.3, T10.2 and T11.3, all merged. T16.2
waits on this task.

## Global Constraints

- A shortcut does nothing outside its context: no error, no navigation, no
  status.
- While a text field has focus (`TextBox`, `PasswordBox`, `RichEditBox`,
  including the AutoSuggestBox's inner box), Ctrl+F, Ctrl+O, Page Up/Down
  and Ctrl+Home/End go to the field.
- While any `ContentDialog` is open, only the dialog sees keys.
- A shortcut runs only when its visible control is visible and enabled.
- Automation names don't change. `AutomationProperties.AcceleratorKey`
  names the key on each control that has one.
- Key text uses Windows names: "Ctrl+O", "Alt+Left", "Page Down", "Esc",
  "Ctrl+Plus", "Ctrl+Minus".
- HTML guide content never gets script access. Page scripts and web
  messages stay off. The forwarded key set is fixed.
- Copy is verbatim from this plan, with a straight ASCII apostrophe and
  "…" (U+2026) where shown.
- UI tests assert only what app code controls.
- PowerShell scripts stay ASCII-only.
- Never print, copy or log provider credential values.

## Rulings against the spec

Task 6 records these in the design's verification record.

1. **Reader keys are off for TXT and HTML today.** The spec's "existing
   behaviour" said the toolbar handles the reader keys. In fact
   `ReaderToolbar.KeysEnabled` is set only for PDF. T08.3, T10.2 and T14.1
   each deferred the TXT and HTML keys to T16.1. Task 2 turns them on.
2. **The shortcuts follow each format's capabilities:**
   - **TXT** has page navigation and text size, but no Go to page.
   - **HTML** has scrolling and text size only.
   - **PDF** has page navigation, Go to page, fit width and zoom.

   So Ctrl+G is PDF-only. In HTML the reader keys are just Ctrl+Plus, Minus
   and 0, which set text size. The Settings rows name the formats, for
   example "Go to page (PDF)". The HTML forwarded set is Alt+Left and the
   text-size keys. Ctrl+Home/End and Page Up/Down stay with the page.
3. **The PDF password prompt is not a dialog.** It's an inline
   `PasswordBox` on the Reader page. So the focus-return rule doesn't
   apply, but the typing rule does.
4. **Focus return:**
   - **Go to page** already returns focus to the reader content when Ctrl+G
     opened it, and to the toolbar button when a click did (T10.2 P2).
     That's what TR16.2 asks for: focus goes back where the user was.
   - **Ctrl+O** opens the system file picker first. When it closes, focus
     must be back where it was before Ctrl+O. The smoke checks this rather
     than expecting the Import guide button.
5. **PDF keys keep their existing coverage.** The `pdf` group's smoke
   already drives Page Up/Down, Ctrl+Plus/Minus/0, Ctrl+G and Esc. The new
   `keyboard` mode covers TXT, HTML, the Library, the Game page and
   Settings.
6. **No completion shortcut.** T13.1 deferred "shortcut keys for
   completion" to T16.1, but the approved spec adds no commands. The
   completion choice is a keyboard-operable control (Tab, then arrow keys),
   which meets TR16.1.
7. **The toolbar's tooltips stay in XAML.** They already name their keys.
   A Core test (`ReaderToolbarTooltipTests`, Task 5) reads
   `ReaderToolbar.xaml` from the repo and checks that each tooltip names its
   `KeyboardShortcuts` keys, so the two can't drift.
8. **The trace runs in the `design` group,** after
   `Run-ReaderDesignScenarios` and before `Run-IconScenarios`. That is where
   the library holds a game with TXT, HTML and PDF guides
   ("Text Size Game"). `Run-IconScenarios` wipes the data root and must
   stay last.

9. **The Keyboard shortcuts card's icon joins the T11.5 catalog.** The user's
   T11.5 feedback was that a Settings card without a colour icon looks
   wrong when Colorful icons is on. Fluent's colour set has no keyboard
   icon, so the card uses `question_circle`, which suits the Help section,
   with the circled-question-mark glyph `E9CE` when colour is off. That
   makes 12 icon places and 15 asset files, so T11.5's counting tests and
   the icons smoke's `images:11` change with it. Task 4 confirms the glyph
   on a screenshot.

## Review Focus

1. **Ctrl+F while a game row or toolbar button has focus on the Library
   page.** Focus must move to the search box even though focus was in a
   list. Pinned by the `keyboard` smoke, which presses Ctrl+F with focus on
   the first game row (Task 5).
2. **Ctrl+O pressed twice quickly.** Only one picker may open.
   `ImportGuideClicked` already guards with `importRequested`. The handler
   calls it directly, so the guard applies. Checked in code review
   (Task 2).
3. **Esc in an empty Library search when the library has no games.** Focus
   must go to Add game, not nowhere. Pinned by
   `EscInAnEmptySearchOfAnEmptyLibraryTargetsAddGame` (Task 1); the shell
   maps the result.
4. **A key forwarded from the HTML page arriving after the reader closed.**
   It must do nothing. The forwarder runs through `TryRunKey`, which
   returns false without a session, and Back checks
   `AppTitleBar.IsBackButtonEnabled`. Checked in code review (Task 3).
5. **AutoSuggestBox with its suggestion list open.** The first Esc must
   close the list without clearing the text. The Library search sets no
   suggestions today, so no test can open the list. The handler is
   attached without `handledEventsToo`, so the box's own Esc handling wins
   if suggestions are ever added. Checked in code review (Task 2).

## Host commands

The Mac has no dotnet, so every build and test runs on `pcsx2-win`:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t16-1) { Remove-Item -Recurse -Force E:\work\desktop-guides\t16-1 }; New-Item -ItemType Directory E:\work\desktop-guides\t16-1 | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t16-1'
}
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t16-1 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
- **One class:** append `--filter "FullyQualifiedName~<Class>"` inside the
  quoted command.
- **Production build:** `stage && s 'cd /d E:\work\desktop-guides\t16-1 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'`
- **Installed group:** `gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t16-1-keyboard-shortcuts -f shell-scope=design -f dev-fast=true`

Installed UI runs use CI, the gate of record. If a host run is needed, follow
`docs/p1/e2e-testing.md` and ask before touching the user's data folders.

---
### Task 1: Core shortcut table and routing

**Files:**
- Create: `src/DesktopGuides.Core/Navigation/KeyboardShortcuts.cs`
- Create: `src/DesktopGuides.Core/Navigation/ShortcutRouting.cs`
- Test: `tests/DesktopGuides.Core.Tests/KeyboardShortcutsTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/ShortcutRoutingTests.cs`

**Interfaces:**
- Consumes: `ShellRoute`, `LibraryRoute`, `GameRoute`, `ReaderRoute`,
  `SettingsRoute` (`Core/Navigation/ShellNavigator.cs`).
- Produces (namespace `DesktopGuides.Core.Navigation`):
  - `enum ShortcutContext { Everywhere, Library, LibrarySearch, Game, Reader, Dialog }`
  - `enum ShortcutCommand { Back, FocusLibrarySearch, ClearLibrarySearch, ImportGuide, PreviousPage, NextPage, PageStart, PageEnd, GoToPage, ZoomIn, ZoomOut, ZoomReset, CloseDialog }`
  - `sealed record KeyboardShortcut(ShortcutContext Context, string Keys, ShortcutCommand Command, string Description)`
  - `KeyboardShortcuts.All → IReadOnlyList<KeyboardShortcut>`,
    `KeyboardShortcuts.For(ShortcutCommand) → KeyboardShortcut`,
    `KeyboardShortcuts.ContextHeading(ShortcutContext) → string`,
    `KeyboardShortcuts.ToolTip(string label, ShortcutCommand) → string`,
    `KeyboardShortcuts.AutomationName(KeyboardShortcut) → string`, and the
    constants `SectionHeading`, `ExpanderHeader`, `ExpanderDescription`.
  - `enum ShortcutDecision { Run, Ignore }`
  - `ShortcutRouting.Resolve(ShortcutCommand command, ShellRoute route, bool textInputFocused, bool dialogOpen) → ShortcutDecision`
  - `enum LibrarySearchEscape { ClearText, FocusFirstGame, FocusAddGame }`
  - `ShortcutRouting.LibrarySearchEsc(bool hasText, int gameCount) → LibrarySearchEscape`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/DesktopGuides.Core.Tests/KeyboardShortcutsTests.cs
using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class KeyboardShortcutsTests
{
    [Fact]
    public void TheTableMatchesTheSpec()
    {
        Assert.Equal(
            [
                (ShortcutContext.Everywhere, "Alt+Left", ShortcutCommand.Back, "Go back"),
                (ShortcutContext.Library, "Ctrl+F", ShortcutCommand.FocusLibrarySearch, "Search the library"),
                (ShortcutContext.LibrarySearch, "Esc", ShortcutCommand.ClearLibrarySearch, "Clear the search"),
                (ShortcutContext.Game, "Ctrl+O", ShortcutCommand.ImportGuide, "Import a guide"),
                (ShortcutContext.Reader, "Page Up", ShortcutCommand.PreviousPage, "Previous page (TXT, PDF)"),
                (ShortcutContext.Reader, "Page Down", ShortcutCommand.NextPage, "Next page (TXT, PDF)"),
                (ShortcutContext.Reader, "Ctrl+Home", ShortcutCommand.PageStart, "Go to start (TXT, PDF)"),
                (ShortcutContext.Reader, "Ctrl+End", ShortcutCommand.PageEnd, "Go to end (TXT, PDF)"),
                (ShortcutContext.Reader, "Ctrl+G", ShortcutCommand.GoToPage, "Go to page (PDF)"),
                (ShortcutContext.Reader, "Ctrl+Plus", ShortcutCommand.ZoomIn, "Zoom in (PDF) or larger text"),
                (ShortcutContext.Reader, "Ctrl+Minus", ShortcutCommand.ZoomOut, "Zoom out (PDF) or smaller text"),
                (ShortcutContext.Reader, "Ctrl+0", ShortcutCommand.ZoomReset, "Fit to width (PDF) or reset text size"),
                (ShortcutContext.Dialog, "Esc", ShortcutCommand.CloseDialog, "Close the dialog"),
            ],
            KeyboardShortcuts.All.Select(s => (s.Context, s.Keys, s.Command, s.Description)));
    }

    [Fact]
    public void EveryCommandAppearsOnce()
    {
        Assert.Equal(
            Enum.GetValues<ShortcutCommand>().Order(),
            KeyboardShortcuts.All.Select(s => s.Command).Order());
    }

    [Fact]
    public void EveryContextHasAnEntry()
    {
        Assert.All(Enum.GetValues<ShortcutContext>(),
            context => Assert.Contains(KeyboardShortcuts.All, s => s.Context == context));
    }

    [Theory]
    [InlineData(ShortcutContext.Everywhere, "Everywhere")]
    [InlineData(ShortcutContext.Library, "Library")]
    [InlineData(ShortcutContext.LibrarySearch, "Library search")]
    [InlineData(ShortcutContext.Game, "Game page")]
    [InlineData(ShortcutContext.Reader, "Reader")]
    [InlineData(ShortcutContext.Dialog, "Dialogs")]
    public void EachContextHasAHeading(ShortcutContext context, string heading)
    {
        Assert.Equal(heading, KeyboardShortcuts.ContextHeading(context));
    }

    [Fact]
    public void TooltipsAndNamesUseTheKeys()
    {
        Assert.Equal("Import guide (Ctrl+O)", KeyboardShortcuts.ToolTip("Import guide", ShortcutCommand.ImportGuide));
        Assert.Equal("Search (Ctrl+F)", KeyboardShortcuts.ToolTip("Search", ShortcutCommand.FocusLibrarySearch));
        Assert.Equal("Import a guide, Ctrl+O",
            KeyboardShortcuts.AutomationName(KeyboardShortcuts.For(ShortcutCommand.ImportGuide)));
    }

    [Fact]
    public void TheSettingsCopyIsVerbatim()
    {
        Assert.Equal("Help", KeyboardShortcuts.SectionHeading);
        Assert.Equal("Keyboard shortcuts", KeyboardShortcuts.ExpanderHeader);
        Assert.Equal("Keys for the Library, Game pages, the reader and dialogs.",
            KeyboardShortcuts.ExpanderDescription);
    }
}
```

```csharp
// tests/DesktopGuides.Core.Tests/ShortcutRoutingTests.cs
using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ShortcutRoutingTests
{
    private static readonly ShellRoute Library = new LibraryRoute();
    private static readonly ShellRoute Game = new GameRoute(Guid.NewGuid());
    private static readonly ShellRoute Reader = new ReaderRoute(Guid.NewGuid(), Guid.NewGuid());
    private static readonly ShellRoute Settings = new SettingsRoute();

    public static TheoryData<ShortcutCommand, ShellRoute> RightPlace() => new()
    {
        { ShortcutCommand.Back, Game },
        { ShortcutCommand.Back, Reader },
        { ShortcutCommand.Back, Settings },
        { ShortcutCommand.FocusLibrarySearch, Library },
        { ShortcutCommand.ImportGuide, Game },
        { ShortcutCommand.NextPage, Reader },
        { ShortcutCommand.GoToPage, Reader },
        { ShortcutCommand.ZoomIn, Reader },
    };

    public static TheoryData<ShortcutCommand, ShellRoute> WrongPlace() => new()
    {
        { ShortcutCommand.FocusLibrarySearch, Game },
        { ShortcutCommand.FocusLibrarySearch, Reader },
        { ShortcutCommand.FocusLibrarySearch, Settings },
        { ShortcutCommand.ImportGuide, Library },
        { ShortcutCommand.ImportGuide, Reader },
        { ShortcutCommand.ImportGuide, Settings },
        { ShortcutCommand.NextPage, Library },
        { ShortcutCommand.GoToPage, Game },
        { ShortcutCommand.ZoomIn, Settings },
        { ShortcutCommand.ClearLibrarySearch, Game },
    };

    [Theory]
    [MemberData(nameof(RightPlace))]
    public void ACommandRunsInItsContext(ShortcutCommand command, ShellRoute route)
    {
        Assert.Equal(ShortcutDecision.Run, ShortcutRouting.Resolve(command, route, false, false));
    }

    [Theory]
    [MemberData(nameof(WrongPlace))]
    public void ACommandIsIgnoredElsewhere(ShortcutCommand command, ShellRoute route)
    {
        Assert.Equal(ShortcutDecision.Ignore, ShortcutRouting.Resolve(command, route, false, false));
    }

    [Theory]
    [InlineData(ShortcutCommand.FocusLibrarySearch)]
    [InlineData(ShortcutCommand.ImportGuide)]
    [InlineData(ShortcutCommand.PreviousPage)]
    [InlineData(ShortcutCommand.NextPage)]
    [InlineData(ShortcutCommand.PageStart)]
    [InlineData(ShortcutCommand.PageEnd)]
    public void TypingWins(ShortcutCommand command)
    {
        ShellRoute route = command switch
        {
            ShortcutCommand.FocusLibrarySearch => Library,
            ShortcutCommand.ImportGuide => Game,
            _ => Reader
        };
        Assert.Equal(ShortcutDecision.Ignore, ShortcutRouting.Resolve(command, route, true, false));
    }

    [Theory]
    [InlineData(ShortcutCommand.Back)]
    [InlineData(ShortcutCommand.GoToPage)]
    [InlineData(ShortcutCommand.ZoomIn)]
    [InlineData(ShortcutCommand.ZoomReset)]
    public void TheseStillRunFromATextField(ShortcutCommand command)
    {
        ShellRoute route = command == ShortcutCommand.Back ? Game : Reader;
        Assert.Equal(ShortcutDecision.Run, ShortcutRouting.Resolve(command, route, true, false));
    }

    [Fact]
    public void ClearingTheSearchNeedsTheSearchBoxFocused()
    {
        Assert.Equal(ShortcutDecision.Run,
            ShortcutRouting.Resolve(ShortcutCommand.ClearLibrarySearch, Library, true, false));
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(ShortcutCommand.ClearLibrarySearch, Library, false, false));
    }

    [Theory]
    [InlineData(ShortcutCommand.Back)]
    [InlineData(ShortcutCommand.FocusLibrarySearch)]
    [InlineData(ShortcutCommand.ImportGuide)]
    [InlineData(ShortcutCommand.NextPage)]
    [InlineData(ShortcutCommand.GoToPage)]
    [InlineData(ShortcutCommand.ClearLibrarySearch)]
    public void AnOpenDialogBlocksEverythingElse(ShortcutCommand command)
    {
        foreach (ShellRoute route in new[] { Library, Game, Reader, Settings })
        {
            Assert.Equal(ShortcutDecision.Ignore, ShortcutRouting.Resolve(command, route, false, true));
        }
    }

    [Fact]
    public void CloseDialogRunsOnlyWithADialogOpen()
    {
        Assert.Equal(ShortcutDecision.Run, ShortcutRouting.Resolve(ShortcutCommand.CloseDialog, Game, false, true));
        Assert.Equal(ShortcutDecision.Ignore, ShortcutRouting.Resolve(ShortcutCommand.CloseDialog, Game, false, false));
    }

    [Theory]
    [InlineData(true, 0, LibrarySearchEscape.ClearText)]
    [InlineData(true, 3, LibrarySearchEscape.ClearText)]
    [InlineData(false, 3, LibrarySearchEscape.FocusFirstGame)]
    public void EscInTheSearchClearsThenLeaves(bool hasText, int games, LibrarySearchEscape expected)
    {
        Assert.Equal(expected, ShortcutRouting.LibrarySearchEsc(hasText, games));
    }

    [Fact]
    public void EscInAnEmptySearchOfAnEmptyLibraryTargetsAddGame()
    {
        Assert.Equal(LibrarySearchEscape.FocusAddGame, ShortcutRouting.LibrarySearchEsc(false, 0));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Core tests with `--filter "FullyQualifiedName~KeyboardShortcuts|FullyQualifiedName~ShortcutRouting"`
Expected: FAIL to compile; `KeyboardShortcuts` and `ShortcutRouting` don't
exist.

- [ ] **Step 3: Implement the table**

```csharp
// src/DesktopGuides.Core/Navigation/KeyboardShortcuts.cs
namespace DesktopGuides.Core.Navigation;

public enum ShortcutContext { Everywhere, Library, LibrarySearch, Game, Reader, Dialog }

public enum ShortcutCommand
{
    Back, FocusLibrarySearch, ClearLibrarySearch, ImportGuide, PreviousPage, NextPage,
    PageStart, PageEnd, GoToPage, ZoomIn, ZoomOut, ZoomReset, CloseDialog
}

public sealed record KeyboardShortcut(
    ShortcutContext Context, string Keys, ShortcutCommand Command, string Description);

/// <summary>T16.1: every keyboard shortcut, for Settings, tooltips and tests.</summary>
public static class KeyboardShortcuts
{
    public const string SectionHeading = "Help";
    public const string ExpanderHeader = "Keyboard shortcuts";
    public const string ExpanderDescription = "Keys for the Library, Game pages, the reader and dialogs.";

    public static IReadOnlyList<KeyboardShortcut> All { get; } =
    [
        new(ShortcutContext.Everywhere, "Alt+Left", ShortcutCommand.Back, "Go back"),
        new(ShortcutContext.Library, "Ctrl+F", ShortcutCommand.FocusLibrarySearch, "Search the library"),
        new(ShortcutContext.LibrarySearch, "Esc", ShortcutCommand.ClearLibrarySearch, "Clear the search"),
        new(ShortcutContext.Game, "Ctrl+O", ShortcutCommand.ImportGuide, "Import a guide"),
        new(ShortcutContext.Reader, "Page Up", ShortcutCommand.PreviousPage, "Previous page (TXT, PDF)"),
        new(ShortcutContext.Reader, "Page Down", ShortcutCommand.NextPage, "Next page (TXT, PDF)"),
        new(ShortcutContext.Reader, "Ctrl+Home", ShortcutCommand.PageStart, "Go to start (TXT, PDF)"),
        new(ShortcutContext.Reader, "Ctrl+End", ShortcutCommand.PageEnd, "Go to end (TXT, PDF)"),
        new(ShortcutContext.Reader, "Ctrl+G", ShortcutCommand.GoToPage, "Go to page (PDF)"),
        new(ShortcutContext.Reader, "Ctrl+Plus", ShortcutCommand.ZoomIn, "Zoom in (PDF) or larger text"),
        new(ShortcutContext.Reader, "Ctrl+Minus", ShortcutCommand.ZoomOut, "Zoom out (PDF) or smaller text"),
        new(ShortcutContext.Reader, "Ctrl+0", ShortcutCommand.ZoomReset, "Fit to width (PDF) or reset text size"),
        new(ShortcutContext.Dialog, "Esc", ShortcutCommand.CloseDialog, "Close the dialog"),
    ];

    public static KeyboardShortcut For(ShortcutCommand command) => All.Single(s => s.Command == command);

    public static string ContextHeading(ShortcutContext context) => context switch
    {
        ShortcutContext.Everywhere => "Everywhere",
        ShortcutContext.Library => "Library",
        ShortcutContext.LibrarySearch => "Library search",
        ShortcutContext.Game => "Game page",
        ShortcutContext.Reader => "Reader",
        _ => "Dialogs"
    };

    public static string ToolTip(string label, ShortcutCommand command) => $"{label} ({For(command).Keys})";

    public static string AutomationName(KeyboardShortcut shortcut) => $"{shortcut.Description}, {shortcut.Keys}";
}
```

- [ ] **Step 4: Implement the routing**

```csharp
// src/DesktopGuides.Core/Navigation/ShortcutRouting.cs
namespace DesktopGuides.Core.Navigation;

public enum ShortcutDecision { Run, Ignore }

public enum LibrarySearchEscape { ClearText, FocusFirstGame, FocusAddGame }

/// <summary>T16.1: whether a shortcut runs where the user is.</summary>
public static class ShortcutRouting
{
    // Keys a text field uses itself; the field wins while it has focus.
    private static readonly HashSet<ShortcutCommand> TypingKeys =
    [
        ShortcutCommand.FocusLibrarySearch, ShortcutCommand.ImportGuide,
        ShortcutCommand.PreviousPage, ShortcutCommand.NextPage,
        ShortcutCommand.PageStart, ShortcutCommand.PageEnd
    ];

    public static ShortcutDecision Resolve(
        ShortcutCommand command, ShellRoute route, bool textInputFocused, bool dialogOpen)
    {
        if (command == ShortcutCommand.CloseDialog)
        {
            return dialogOpen ? ShortcutDecision.Run : ShortcutDecision.Ignore;
        }
        if (dialogOpen || (textInputFocused && TypingKeys.Contains(command)))
        {
            return ShortcutDecision.Ignore;
        }
        bool inContext = KeyboardShortcuts.For(command).Context switch
        {
            ShortcutContext.Everywhere => true,
            ShortcutContext.Library => route is LibraryRoute,
            // Esc clears only while the search box itself has focus.
            ShortcutContext.LibrarySearch => route is LibraryRoute && textInputFocused,
            ShortcutContext.Game => route is GameRoute,
            ShortcutContext.Reader => route is ReaderRoute,
            _ => false
        };
        return inContext ? ShortcutDecision.Run : ShortcutDecision.Ignore;
    }

    public static LibrarySearchEscape LibrarySearchEsc(bool hasText, int gameCount) =>
        hasText ? LibrarySearchEscape.ClearText
        : gameCount > 0 ? LibrarySearchEscape.FocusFirstGame
        : LibrarySearchEscape.FocusAddGame;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: the same filter, then the whole Core suite.
Expected: PASS, 51 new tests (`KeyboardShortcutsTests` 11,
`ShortcutRoutingTests` 40, counting each theory row). The whole suite is
green.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Core/Navigation/KeyboardShortcuts.cs \
  src/DesktopGuides.Core/Navigation/ShortcutRouting.cs \
  tests/DesktopGuides.Core.Tests/KeyboardShortcutsTests.cs \
  tests/DesktopGuides.Core.Tests/ShortcutRoutingTests.cs
git commit -m "feat(core): add the T16.1 shortcut table and routing"
```

### Task 2: Shell shortcuts and the TXT and HTML reader keys

**Files:**
- Create: `src/DesktopGuides.Production/ShellWindow.Keyboard.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (constructor; the TXT `SetSession` call)
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs` (the HTML `SetSession` call)
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml.cs` (comments only)

**Interfaces:**
- Consumes: Task 1's `KeyboardShortcuts`, `ShortcutRouting`,
  `ShortcutCommand`, `ShortcutDecision`, `LibrarySearchEscape`; the
  existing `navigator`, `DialogOpen()`, `ImportGuideClicked`,
  `ApplyLibrarySearch(bool announce)`, `LibrarySearchInput`, `GameList`,
  `AddGameButton`, `ImportGuideButton`, `ReaderActions`.
- Produces:
  - `ShellWindow.TextInputFocused() → bool`, used by Task 3;
  - the Ctrl+F and Ctrl+O accelerators on `ShellRoot`;
  - the tooltip "Import guide (Ctrl+O)" on `ImportGuideButton` and
    "Search (Ctrl+F)" on `LibrarySearchInput`, each with
    `AutomationProperties.AcceleratorKey`;
  - `ReaderActions.KeysEnabled = true` for TXT and HTML sessions.

There is no Production unit-test project. The decisions this task wires are
pinned by Task 1's tests, and Task 5's installed trace is its RED/GREEN.

- [ ] **Step 1: Add the keyboard partial**

```csharp
// src/DesktopGuides.Production/ShellWindow.Keyboard.cs
using CommunityToolkit.WinUI;
using DesktopGuides.Core.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T16.1: Ctrl+F and Ctrl+O sit beside Alt+Left on ShellRoot. Each runs
    // only where KeyboardShortcuts places it, and never over typing.
    private void InitializeKeyboard()
    {
        AddShortcut(VirtualKey.F, ShortcutCommand.FocusLibrarySearch, FocusLibrarySearch);
        AddShortcut(VirtualKey.O, ShortcutCommand.ImportGuide, ImportFromKeyboard);
        // Not handledEventsToo: the box's own Esc closes its suggestions first.
        LibrarySearchInput.KeyDown += LibrarySearchKeyDown;
        ShowShortcut(ImportGuideButton, "Import guide", ShortcutCommand.ImportGuide);
        ShowShortcut(LibrarySearchInput, "Search", ShortcutCommand.FocusLibrarySearch);
    }

    private static void ShowShortcut(FrameworkElement control, string label, ShortcutCommand command)
    {
        ToolTipService.SetToolTip(control, KeyboardShortcuts.ToolTip(label, command));
        AutomationProperties.SetAcceleratorKey(control, KeyboardShortcuts.For(command).Keys);
    }

    private void AddShortcut(VirtualKey key, ShortcutCommand command, Func<bool> run)
    {
        KeyboardAccelerator accelerator = new() { Key = key, Modifiers = VirtualKeyModifiers.Control };
        accelerator.Invoked += (_, args) =>
        {
            if (ShortcutRouting.Resolve(command, navigator.Current, TextInputFocused(), DialogOpen())
                == ShortcutDecision.Run)
            {
                args.Handled = run();
            }
        };
        ShellRoot.KeyboardAccelerators.Add(accelerator);
    }

    private bool TextInputFocused() =>
        ShellRoot.XamlRoot is XamlRoot root &&
        FocusManager.GetFocusedElement(root) is TextBox or PasswordBox or RichEditBox;

    private bool FocusLibrarySearch()
    {
        if (!LibrarySearchInput.IsEnabled || LibrarySearchInput.Visibility != Visibility.Visible)
        {
            return false;
        }
        LibrarySearchInput.Focus(FocusState.Keyboard);
        // Typing then replaces the old query.
        LibrarySearchInput.FindDescendant<TextBox>()?.SelectAll();
        return true;
    }

    // Runs the button's own handler, so its guard against a second picker applies.
    private bool ImportFromKeyboard()
    {
        if (!ImportGuideButton.IsEnabled || ImportGuideButton.Visibility != Visibility.Visible)
        {
            return false;
        }
        ImportGuideClicked(ImportGuideButton, new RoutedEventArgs());
        return true;
    }

    private void LibrarySearchKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape ||
            ShortcutRouting.Resolve(ShortcutCommand.ClearLibrarySearch, navigator.Current, true, DialogOpen())
                != ShortcutDecision.Run)
        {
            return;
        }
        args.Handled = true;
        switch (ShortcutRouting.LibrarySearchEsc(LibrarySearchInput.Text.Length > 0, GameList.Items.Count))
        {
            case LibrarySearchEscape.ClearText:
                LibrarySearchInput.Text = string.Empty;
                ApplyLibrarySearch(announce: true);
                break;
            case LibrarySearchEscape.FocusFirstGame:
                (GameList.ContainerFromIndex(0) as Control)?.Focus(FocusState.Keyboard);
                break;
            default:
                AddGameButton.Focus(FocusState.Keyboard);
                break;
        }
    }
}
```

If `CommunityToolkit.WinUI`'s `FindDescendant<T>()` isn't available through
the referenced `CommunityToolkit.WinUI.Extensions` package, write a private
recursive `VisualTreeHelper` search with the same shape, and record a ruling.

- [ ] **Step 2: Wire it in and turn the reader keys on**

- In the `ShellWindow` constructor, directly after
  `ShellRoot.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;`,
  add `InitializeKeyboard();`.
- In `ShellWindow.xaml.cs`, after the TXT session's
  `ReaderActions.SetSession(session);`, add:

```csharp
                    // T16.1: TXT gets the reader keys PDF already had.
                    ReaderActions.KeysEnabled = true;
```

- In `ShellWindow.HtmlReader.cs`, after `ReaderActions.SetSession(session);`,
  add:

```csharp
        // T16.1: HTML gets the reader keys; only text size applies there.
        ReaderActions.KeysEnabled = true;
```

- In `ReaderToolbar.xaml.cs`:
  - Change the comment above `KeysEnabled` to
    `// Each session's shell code turns keys on after SetSession resets them.`
  - Change the "Ruling 1" comment in `TryRunKey` to
    `// T14.1 Ruling 1: the text size keys run even with KeysEnabled off.`

- [ ] **Step 3: Build and run the Core suite**

Run: Production build, then Core tests (host commands).
Expected: build succeeds with 0 warnings and 0 errors, and Core is green.

- [ ] **Step 4: Self-check**

- Ctrl+O runs `ImportGuideClicked`, whose `importRequested` guard stops a
  second picker (Review Focus 2).
- The Esc handler is attached with `+=`, not
  `AddHandler(..., handledEventsToo: true)` (Review Focus 5).
- Alt+Left is unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "feat(shell): add Ctrl+F, Ctrl+O and search Esc; reader keys for TXT and HTML"
```

### Task 3: HTML guides: the probe, then forwarding if needed

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (probe mode `keyboard-html-probe`)
- Modify: `tools/p1/windows_shell_install.ps1` (run the probe in the `design` group)
- Only if the probe shows native routing fails:
  - Create: `src/DesktopGuides.Core/Html/HtmlKeyForwarding.cs`
  - Create: `tests/DesktopGuides.Core.Tests/HtmlKeyForwardingTests.cs`
  - Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs`
  - Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs`

**Interfaces:**
- Consumes: Task 2's HTML `KeysEnabled`; `ReaderToolbar.TryRunKey(VirtualKey, VirtualKeyModifiers, bool)`;
  the shell's `GoBackAsync` path behind `BackAcceleratorInvoked`.
- Produces:
  - the probe result: two booleans, `nativeTextSize` and `nativeBack`, in
    the probe's result JSON;
  - only on the fallback path: `HtmlKeyForwarding.Script(string binding) → string`,
    `HtmlKeyForwarding.TryParse(string payload, out ShortcutCommand command) → bool`,
    and `HtmlReaderSession.ForwardedCommand` (`event Action<ShortcutCommand>?`).

- [ ] **Step 1: Add the probe mode**

In `windows_shell_ui_smoke.ps1`, add `'keyboard-html-probe'` to the `-Mode`
`ValidateSet`, and add this branch next to the `icons` mode. It records and
never fails on the keys themselves:

```powershell
    elseif ($Mode -eq 'keyboard-html-probe') {
        # T16.1 Task 3: do the app's keys reach it from inside an HTML page?
        Select-Element 'Text Size Game'
        [void](Wait-Status 'Game ready.')
        Open-GuideFromGame 'Static Web Guide'
        [void](Wait-Status 'Guide ready.')
        $before = (Wait-VisibleById 'TextSizeValue').Current.Name
        # Focus the page itself: a click 150 px below the reader toolbar,
        # which is always inside the guide page.
        $rect = (Wait-VisibleById 'ReaderCommands').Current.BoundingRectangle
        $pageX = [int]($rect.Left + $rect.Width / 2)
        $pageY = [int]($rect.Bottom + 150)
        [DesktopGuidesForegroundProbe]::Click($pageX, $pageY)
        Start-Sleep -Milliseconds 500
        [System.Windows.Forms.SendKeys]::SendWait('^{ADD}')
        Start-Sleep -Milliseconds 800
        $after = (Find-ById 'TextSizeValue').Current.Name
        $report.nativeTextSize = ($after -ne $before)
        [System.Windows.Forms.SendKeys]::SendWait('^0')
        Start-Sleep -Milliseconds 500
        [DesktopGuidesForegroundProbe]::Click($pageX, $pageY)
        Start-Sleep -Milliseconds 500
        [System.Windows.Forms.SendKeys]::SendWait('%{LEFT}')
        Start-Sleep -Milliseconds 1500
        $heading = Find-ById 'GameHeading'
        $report.nativeBack = [bool]($heading -and -not $heading.Current.IsOffscreen)
        $report.phases += "probe-textsize-$($report.nativeTextSize)-back-$($report.nativeBack)"
    }
```

Check the helpers and IDs it uses against the script before running:
- `Select-Element`, `Wait-Status`, `Open-GuideFromGame`, `Wait-VisibleById`
  and `Find-ById` must exist.
- `ReaderCommands` is the reader toolbar's AutomationId, and
  `[DesktopGuidesForegroundProbe]::Click(int x, int y)` exists in
  `windows_shell_foreground_probe.cs`; both were checked while writing this
  plan. `ReaderSurface` has no AutomationId, so the click is placed relative
  to the toolbar.

In `windows_shell_install.ps1`, in the `design` group, add after
`Run-ReaderDesignScenarios`:

```powershell
        Start-InstalledShell
        $report.keyboardHtmlProbe = Run-ShellSmoke 'keyboard-html-probe'
        Close-InstalledShell
```

- [ ] **Step 2: Run the probe in CI**

Commit and push, then run the **Installed group** (host commands) and
download the `design` artifact's `keyboard-html-probe.json`.

```bash
git add tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): probe the app's keys from inside an HTML guide"
git push -u origin feat/p1-t16-1-keyboard-shortcuts
```

Expected: the run passes, and the JSON has `nativeTextSize` and `nativeBack`
set. Record both values in the ledger.

- [ ] **Step 3: Decide**

- **Both true:** native routing works. Skip to Step 8. No forwarding code is
  written.
- **Either false:** continue with Step 4, the DevTools-protocol listener.

- [ ] **Step 4: Write the failing forwarding tests (fallback only)**

```csharp
// tests/DesktopGuides.Core.Tests/HtmlKeyForwardingTests.cs
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlKeyForwardingTests
{
    [Theory]
    [InlineData("Alt+ArrowLeft", ShortcutCommand.Back)]
    [InlineData("Control+Equal", ShortcutCommand.ZoomIn)]
    [InlineData("Control+Shift+Equal", ShortcutCommand.ZoomIn)]
    [InlineData("Control+NumpadAdd", ShortcutCommand.ZoomIn)]
    [InlineData("Control+Minus", ShortcutCommand.ZoomOut)]
    [InlineData("Control+NumpadSubtract", ShortcutCommand.ZoomOut)]
    [InlineData("Control+Digit0", ShortcutCommand.ZoomReset)]
    [InlineData("Control+Numpad0", ShortcutCommand.ZoomReset)]
    public void TheForwardedKeysMapToTheirCommands(string payload, ShortcutCommand expected)
    {
        Assert.True(HtmlKeyForwarding.TryParse(payload, out ShortcutCommand command));
        Assert.Equal(expected, command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Control+KeyF")]
    [InlineData("Control+KeyO")]
    [InlineData("PageDown")]
    [InlineData("Control+Home")]
    [InlineData("Control+KeyG")]
    [InlineData("Alt+ArrowRight")]
    [InlineData("alt+arrowleft")]
    public void EverythingElseIsRefused(string payload)
    {
        Assert.False(HtmlKeyForwarding.TryParse(payload, out _));
    }

    [Fact]
    public void TheScriptCallsOnlyItsBindingAndListsOnlyTheForwardedKeys()
    {
        string script = HtmlKeyForwarding.Script("desktopGuidesKey");

        Assert.Contains("desktopGuidesKey(", script);
        Assert.Contains("'keydown'", script);
        foreach (string key in HtmlKeyForwarding.Payloads)
        {
            Assert.Contains($"'{key}'", script);
        }
        Assert.DoesNotContain("PageDown", script);
        Assert.DoesNotContain("KeyG", script);
    }
}
```

Run: Core tests with `--filter "FullyQualifiedName~HtmlKeyForwarding"`.
Expected: FAIL to compile; `HtmlKeyForwarding` doesn't exist.

- [ ] **Step 5: Implement the key map (fallback only)**

```csharp
// src/DesktopGuides.Core/Html/HtmlKeyForwarding.cs
using DesktopGuides.Core.Navigation;

namespace DesktopGuides.Core.Html;

/// <summary>
/// T16.1: the app keys an HTML guide's page passes back to the app. The
/// listener runs in an isolated world, so the guide's own content still has
/// no script access, and only this fixed set ever reaches the app.
/// </summary>
public static class HtmlKeyForwarding
{
    private static readonly IReadOnlyDictionary<string, ShortcutCommand> Keys =
        new Dictionary<string, ShortcutCommand>(StringComparer.Ordinal)
        {
            ["Alt+ArrowLeft"] = ShortcutCommand.Back,
            ["Control+Equal"] = ShortcutCommand.ZoomIn,
            ["Control+Shift+Equal"] = ShortcutCommand.ZoomIn,
            ["Control+NumpadAdd"] = ShortcutCommand.ZoomIn,
            ["Control+Minus"] = ShortcutCommand.ZoomOut,
            ["Control+NumpadSubtract"] = ShortcutCommand.ZoomOut,
            ["Control+Digit0"] = ShortcutCommand.ZoomReset,
            ["Control+Numpad0"] = ShortcutCommand.ZoomReset,
        };

    public static IReadOnlyCollection<string> Payloads => (IReadOnlyCollection<string>)Keys.Keys;

    public static bool TryParse(string payload, out ShortcutCommand command) =>
        Keys.TryGetValue(payload, out command);

    // Builds "Control+Shift+Alt+<code>" from the event and reports only a known key.
    public static string Script(string binding)
    {
        string list = string.Join(",", Keys.Keys.Select(key => $"'{key}'"));
        return "(() => { const keys = new Set([" + list + "]); " +
            "document.addEventListener('keydown', e => { " +
            "const k = (e.ctrlKey ? 'Control+' : '') + (e.shiftKey ? 'Shift+' : '') + " +
            "(e.altKey ? 'Alt+' : '') + e.code; " +
            "if (keys.has(k)) { e.preventDefault(); e.stopImmediatePropagation(); " +
            binding + "(k); } }, true); })();";
    }
}
```

Run: the same filter.
Expected: PASS, 17 tests.

- [ ] **Step 6: Start the listener and pass its keys on (fallback only)**

In `HtmlReaderSession.cs`, add:

```csharp
    // T16.1: page keys the app owns. The listener lives in an isolated world;
    // the guide's own scripts stay off and can't call the binding.
    private const string KeyBinding = "desktopGuidesKey";
    private const string KeyWorld = "desktop-guides-keys";

    public event Action<ShortcutCommand>? ForwardedCommand;

    private async Task StartKeyForwardingAsync(CoreWebView2 core)
    {
        core.GetDevToolsProtocolEventReceiver("Runtime.bindingCalled")
            .DevToolsProtocolEventReceived += OnBindingCalled;
        await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
        await core.CallDevToolsProtocolMethodAsync("Runtime.addBinding",
            JsonSerializer.Serialize(new { name = KeyBinding, executionContextName = KeyWorld }));
        await core.CallDevToolsProtocolMethodAsync("Page.addScriptToEvaluateOnNewDocument",
            JsonSerializer.Serialize(new
            {
                source = HtmlKeyForwarding.Script(KeyBinding),
                worldName = KeyWorld,
                runImmediately = true
            }));
    }

    private void OnBindingCalled(CoreWebView2 sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        using JsonDocument json = JsonDocument.Parse(args.ParameterObjectAsJson);
        if (json.RootElement.GetProperty("name").GetString() != KeyBinding) return;
        // The C# side checks the payload again; the page can't widen the set.
        if (json.RootElement.GetProperty("payload").GetString() is string payload &&
            HtmlKeyForwarding.TryParse(payload, out ShortcutCommand command))
        {
            ForwardedCommand?.Invoke(command);
        }
    }
```

Add the `using DesktopGuides.Core.Html;` and `using DesktopGuides.Core.Navigation;`
directives if they're missing. Call `await StartKeyForwardingAsync(core);`
inside the existing `try` block, directly after
`core.ProcessFailed += OnProcessFailed;`, so it runs before the guide
navigates.

In `ShellWindow.HtmlReader.cs`, directly after the session is created
(`HtmlReaderSession session = new(...)`), add:

```csharp
        session.ForwardedCommand += command =>
            DispatcherQueue.TryEnqueue(() => RunForwardedKey(session, command));
```

and add this method to the partial. It needs `using DesktopGuides.Core.Navigation;`
and `using Windows.System;` at the top of `ShellWindow.HtmlReader.cs` if
they're missing:

```csharp
    // A key from inside the page runs the same command as its accelerator.
    private void RunForwardedKey(HtmlReaderSession source, ShortcutCommand command)
    {
        if (!ReferenceEquals(readerSession, source)) return;
        if (command == ShortcutCommand.Back)
        {
            if (!AppTitleBar.IsBackButtonEnabled || DialogOpen()) return;
            CancelReaderLoad();
            _ = RunNavigationAsync(GoBackAsync);
            return;
        }
        VirtualKey key = command switch
        {
            ShortcutCommand.ZoomIn => VirtualKey.Add,
            ShortcutCommand.ZoomOut => VirtualKey.Subtract,
            _ => VirtualKey.Number0
        };
        ReaderActions.TryRunKey(key, VirtualKeyModifiers.Control);
    }
```

Run: Production build.
Expected: 0 warnings and 0 errors.

- [ ] **Step 7: Run the probe again**

Commit and push, then rerun the **Installed group**.

```bash
git add src tests
git commit -m "feat(reader): forward the app's keys from inside HTML guides"
git push
```

Expected: `nativeTextSize` and `nativeBack` are both true now. The probe
names them "native" but measures the result, whatever the mechanism.

**Stop condition:** if either is still false, stop and ask the user. Report
which mechanism failed and how, and propose F6 to move focus to the toolbar
as the fallback. Don't pick it yourself.

- [ ] **Step 8: Record the result**

In the ledger, record which mechanism works (native, or the DevTools-protocol
listener) and the two values from each probe run. Task 5 turns the probe's
checks into assertions in the `keyboard` mode and removes the probe mode.

### Task 4: The Settings "Keyboard shortcuts" list

**Files:**
- Create: `src/DesktopGuides.Production/ShellWindow.Shortcuts.cs`
- Create: `src/DesktopGuides.Production/Assets/Icons/Color/ic_fluent_question_circle_24_color.svg`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (Help heading and expander after `ProviderSettings`)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (constructor)
- Modify: `src/DesktopGuides.Production/ShellWindow.Icons.cs` (one more host)
- Modify: `src/DesktopGuides.Core/Library/AppIconCatalog.cs` (`AppIcon.ShortcutsCard`)
- Modify: `src/DesktopGuides.Production/Assets/Icons/Color/SOURCE.md`
- Modify: `tests/DesktopGuides.Core.Tests/AppIconCatalogTests.cs`
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (icons mode counts)

**Interfaces:**
- Consumes: Task 1's `KeyboardShortcuts`; T11.5's `AppIconCatalog`,
  `AppIcons.Element`, `ApplyIcons()` and `iconHostsPending`.
- Produces:
  - AutomationIds `SettingsHelpHeading`, `KeyboardShortcutsExpander`, and
    `Shortcut<Command>` for each row, for example `ShortcutImportGuide`;
  - each row's automation name, from `KeyboardShortcuts.AutomationName`;
  - `AppIcon.ShortcutsCard` (glyph `E9CE`, asset
    `ic_fluent_question_circle_24_color.svg`).

- [ ] **Step 1: Update the icon tests (RED)**

In `AppIconCatalogTests.cs`:
- Rename `TheCatalogListsElevenPlaces` to `TheCatalogListsTwelvePlaces` and
  change `11` to `12`.
- Add this row to `EachPlaceKeepsItsGlyphAndGetsItsAsset`:

```csharp
    [InlineData(AppIcon.ShortcutsCard, "\uE9CE", "ic_fluent_question_circle_24_color.svg")]
```

- Rename `AssetFilesAreTheFourteenDistinctFiles` to
  `AssetFilesAreTheFifteenDistinctFiles` and change `14` to `15`.

Run: Core tests with `--filter "FullyQualifiedName~AppIconCatalog"`.
Expected: FAIL to compile; `AppIcon.ShortcutsCard` doesn't exist.

- [ ] **Step 2: Add the icon (GREEN)**

- In `AppIconCatalog.cs`, add `ShortcutsCard` at the end of `AppIcon`, and
  this table entry after `ColorfulIconsCard`:

```csharp
            // Fluent has no colour keyboard; Help's question mark suits the section.
            [AppIcon.ShortcutsCard] = ("\uE9CE", "ic_fluent_question_circle_24_color.svg"),
```

- Download the asset:
  `curl -fsSL https://raw.githubusercontent.com/microsoft/fluentui-system-icons/a347af95478f989038cccbf085978865264c048a/assets/Question%20Circle/SVG/ic_fluent_question_circle_24_color.svg -o src/DesktopGuides.Production/Assets/Icons/Color/ic_fluent_question_circle_24_color.svg`
- In `SOURCE.md`, add its row (folder `Question Circle`, its byte count
  from `wc -c`), keeping the table sorted, and update the SVG total.

Run: the same filter, then the whole Core suite.
Expected: PASS, and the suite is green.

- [ ] **Step 3: Add the Settings section**

In `ShellWindow.xaml`, directly after `<local:ProviderSettingsCard x:Name="ProviderSettings" />`:

```xml
                        <TextBlock x:Name="SettingsHelpHeading"
                                   Style="{StaticResource DesktopGuidesSettingsSectionStyle}"
                                   AutomationProperties.AutomationId="SettingsHelpHeading" />
                        <toolkit:SettingsExpander
                            x:Name="KeyboardShortcutsExpander"
                            HorizontalAlignment="Stretch"
                            AutomationProperties.AutomationId="KeyboardShortcutsExpander" />
```

Create the partial:

```csharp
// src/DesktopGuides.Production/ShellWindow.Shortcuts.cs
using CommunityToolkit.WinUI.Controls;
using DesktopGuides.Core.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T16.1: the Settings list comes from KeyboardShortcuts, so it can't drift.
    private void InitializeShortcutList()
    {
        SettingsHelpHeading.Text = KeyboardShortcuts.SectionHeading;
        KeyboardShortcutsExpander.Header = KeyboardShortcuts.ExpanderHeader;
        KeyboardShortcutsExpander.Description = KeyboardShortcuts.ExpanderDescription;
        AutomationProperties.SetName(KeyboardShortcutsExpander,
            $"{KeyboardShortcuts.ExpanderHeader}. {KeyboardShortcuts.ExpanderDescription}");
        foreach (IGrouping<ShortcutContext, KeyboardShortcut> group in
                 KeyboardShortcuts.All.GroupBy(shortcut => shortcut.Context))
        {
            TextBlock heading = new()
            {
                Text = KeyboardShortcuts.ContextHeading(group.Key),
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                Margin = new Thickness(0, 8, 0, 0)
            };
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level3);
            KeyboardShortcutsExpander.Items.Add(heading);
            foreach (KeyboardShortcut shortcut in group)
            {
                SettingsCard row = new()
                {
                    Header = shortcut.Description,
                    Content = new TextBlock { Text = shortcut.Keys }
                };
                AutomationProperties.SetAutomationId(row, $"Shortcut{shortcut.Command}");
                AutomationProperties.SetName(row, KeyboardShortcuts.AutomationName(shortcut));
                KeyboardShortcutsExpander.Items.Add(row);
            }
        }
    }
}
```

In the `ShellWindow` constructor, directly after `InitializeKeyboard();`
(Task 2), add `InitializeShortcutList();`.

In `ShellWindow.Icons.cs`'s `ApplyIcons()`, after the `ColorfulIconsSettingsCard`
line:

```csharp
        KeyboardShortcutsExpander.HeaderIcon = AppIcons.Element(Pick(AppIcon.ShortcutsCard));
```

and add `KeyboardShortcutsExpander` to the `iconHostsPending` list beside
`ColorfulIconsSettingsCard`.

In `windows_shell_ui_smoke.ps1`'s icons mode, change
`'images:11 glyphs:0'` to `'images:12 glyphs:0'` and `'images:0 glyphs:11'`
to `'images:0 glyphs:12'`. In its `design-language` mode, after
`Assert-Below 'ProviderSettingsExpander' 'SettingsGameDataHeading'`, add:

```powershell
        [void](Show-SettingsCard 'KeyboardShortcutsExpander')
        Assert-HeadingLevel 'SettingsHelpHeading' 2
        Assert-Below 'SettingsHelpHeading' 'ProviderSettingsExpander'
        Assert-Below 'KeyboardShortcutsExpander' 'SettingsHelpHeading'
```

- [ ] **Step 4: Build**

Run: Production build.
Expected: 0 warnings and 0 errors. If `BodyStrongTextBlockStyle` isn't in
the app's resources, use the app's own body-strong style from
`Styles/Typography.xaml`, and record a ruling.

- [ ] **Step 5: Commit**

```bash
git add src tests tools/p1/windows_shell_ui_smoke.ps1
git commit -m "feat(shell): list the keyboard shortcuts in Settings"
```

### Task 5: The keyboard trace, tooltip parity and README

**Files:**
- Create: `tests/DesktopGuides.Core.Tests/ReaderToolbarTooltipTests.cs`
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (mode `keyboard`; the probe mode removed)
- Modify: `tools/p1/windows_shell_install.ps1` (run `keyboard` in place of the probe)
- Modify: `README.md` ("Keyboard shortcuts" section)

**Interfaces:**
- Consumes: everything from Tasks 1 to 4; the HTML mechanism recorded in
  Task 3 Step 8.
- Produces: the `keyboard` smoke mode and its screenshots
  `keyboard.settings-shortcuts.png`; the README section.

- [ ] **Step 1: Write the tooltip parity test**

```csharp
// tests/DesktopGuides.Core.Tests/ReaderToolbarTooltipTests.cs
using System.Text.RegularExpressions;
using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

// Ruling 7: the toolbar's tooltips name the keys KeyboardShortcuts lists.
public sealed class ReaderToolbarTooltipTests
{
    [Theory]
    [InlineData("PageStart", ShortcutCommand.PageStart)]
    [InlineData("PreviousPage", ShortcutCommand.PreviousPage)]
    [InlineData("NextPage", ShortcutCommand.NextPage)]
    [InlineData("PageEnd", ShortcutCommand.PageEnd)]
    [InlineData("SmallerText", ShortcutCommand.ZoomOut)]
    [InlineData("LargerText", ShortcutCommand.ZoomIn)]
    [InlineData("ZoomOut", ShortcutCommand.ZoomOut)]
    [InlineData("ZoomIn", ShortcutCommand.ZoomIn)]
    [InlineData("GoToPage", ShortcutCommand.GoToPage)]
    [InlineData("FitToWidth", ShortcutCommand.ZoomReset)]
    [InlineData("ResetTextSize", ShortcutCommand.ZoomReset)]
    public void EachToolbarTooltipNamesItsKeys(string name, ShortcutCommand command)
    {
        string xaml = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "DesktopGuides.Production", "ReaderToolbar.xaml"));
        Match button = Regex.Match(xaml,
            $"<AppBarButton x:Name=\"{name}\"(?<attributes>[^>]*)>", RegexOptions.Singleline);
        Assert.True(button.Success, $"No AppBarButton named {name}.");
        Match tooltip = Regex.Match(button.Groups["attributes"].Value, "ToolTipService.ToolTip=\"(?<text>[^\"]*)\"");
        Assert.True(tooltip.Success, $"{name} has no tooltip.");
        Assert.EndsWith($"({KeyboardShortcuts.For(command).Keys})", tooltip.Groups["text"].Value);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DesktopGuides.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("DesktopGuides.sln not found.");
    }
}
```

Run: Core tests with `--filter "FullyQualifiedName~ReaderToolbarTooltip"`.
Expected: PASS, 11 tests. These pin existing XAML, so they pass at once.
Prove each can fail: temporarily change `"Go to page (Ctrl+G)"` to
`"Go to page"` in the XAML, see `EachToolbarTooltipNamesItsKeys(GoToPage)`
fail, then restore it.

- [ ] **Step 2: Add the keyboard mode (RED until it runs)**

In `windows_shell_ui_smoke.ps1`:
- Replace `'keyboard-html-probe'` with `'keyboard'` in the `-Mode`
  `ValidateSet`.
- Replace the `keyboard-html-probe` branch with:

```powershell
    elseif ($Mode -eq 'keyboard') {
        # T16.1: a keyboard-only trace. Each check reads app-owned state:
        # focus, automation ids and names, item status and the status sequence.
        function Send-Keys([string] $keys) {
            [System.Windows.Forms.SendKeys]::SendWait($keys)
            Start-Sleep -Milliseconds 300
        }
        function Get-StatusSequence {
            $status = (Find-RawById 'ShellContent').Current.ItemStatus
            if ($status -match '^(\d+)\|') { return [long]$Matches[1] }
            return 0
        }
        function Get-FocusedName {
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            if ($focused) { return $focused.Current.Name } else { return '' }
        }
        function Wait-FocusedName([string] $expected, [string] $step) {
            $deadline = (Get-Date).AddSeconds(10)
            do {
                if ((Get-FocusedName) -eq $expected) { return }
                Start-Sleep -Milliseconds 150
            } while ((Get-Date) -lt $deadline)
            throw "$step: expected focus on '$expected', found '$(Get-FocusedName)'."
        }
        function Get-FirstListItem([string] $listId) {
            $condition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem)
            return (Wait-VisibleById $listId).FindFirst($scope, $condition)
        }
        function Get-TopTextLine {
            $list = Wait-VisibleById 'ReaderTextLines'
            $top = $list.Current.BoundingRectangle.Top
            $condition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem)
            foreach ($row in $list.FindAll($scope, $condition)) {
                if (-not $row.Current.IsOffscreen -and $row.Current.BoundingRectangle.Top -ge $top -1 -and
                    $row.Current.Name -match '^Line (\d{4}) ') { return [int]$Matches[1] }
            }
            throw 'No visible TXT line.'
        }
        function Wait-TopTextLine([int] $expected, [string] $step) {
            $deadline = (Get-Date).AddSeconds(10)
            do {
                $line = Get-TopTextLine
                if ($line -eq $expected) { return }
                Start-Sleep -Milliseconds 150
            } while ((Get-Date) -lt $deadline)
            throw "$step: expected top line $expected, found $line."
        }
        function Click-Page {
            $rect = (Wait-VisibleById 'ReaderCommands').Current.BoundingRectangle
            [DesktopGuidesForegroundProbe]::Click([int]($rect.Left + $rect.Width / 2), [int]($rect.Bottom + 150))
            Start-Sleep -Milliseconds 500
        }

        Resize-ShellWindow 1500 720
        Select-Element 'Library'
        [void](Wait-VisibleById 'LibraryHeading')

        # Library: Ctrl+F from a game row, then Esc clears and Esc leaves.
        $firstGame = Get-FirstListItem 'GameList'
        $firstGameName = $firstGame.Current.Name
        $firstGame.SetFocus()
        Wait-FocusedName $firstGameName 'first game row'
        Send-Keys '^f'
        Wait-FocusWithin 'LibrarySearchInput'
        Send-Keys 'zzqx'
        [void](Wait-Status 'No games match.' -AllowHidden)
        Send-Keys '{ESC}'
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-SearchText 'LibrarySearchInput') -ne '' -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 150 }
        if ((Get-SearchText 'LibrarySearchInput') -ne '') { throw 'Esc did not clear the Library search.' }
        Wait-FocusWithin 'LibrarySearchInput'
        Send-Keys '{ESC}'
        Wait-FocusedName $firstGameName 'Esc in an empty search'
        $report.phases += 'keyboard-library-search'

        # Wrong context: Ctrl+O on the Library does nothing.
        $sequence = Get-StatusSequence
        Send-Keys '^o'
        Start-Sleep -Milliseconds 800
        Wait-FocusedName $firstGameName 'Ctrl+O on the Library'
        if ((Get-StatusSequence) -ne $sequence) { throw 'Ctrl+O on the Library changed the status.' }
        $report.phases += 'keyboard-wrong-context'

        # Game page: Enter opens it; Ctrl+O opens the picker; Esc returns focus.
        $game = 'Text Size Game'
        $row = Find-ByName $game
        $row.SetFocus()
        Wait-FocusedName $game 'Text Size Game row'
        Send-Keys '{ENTER}'
        [void](Wait-Name 'GameHeading' $game)
        [void](Wait-Status 'Game ready.')
        $firstGuide = Get-FirstListItem 'GuideList'
        $firstGuideName = $firstGuide.Current.Name
        $firstGuide.SetFocus()
        Wait-FocusedName $firstGuideName 'first guide row'
        Send-Keys '^o'
        $picker = Wait-FilePicker
        Send-Keys '{ESC}'
        Wait-PickerClosed $picker
        Wait-FocusedName $firstGuideName 'after the import picker closed'
        $report.phases += 'keyboard-import-picker'

        # A confirmation dialog: Esc cancels it, and focus returns to its button.
        $remove = Find-ByName 'Remove game'
        $remove.SetFocus()
        Wait-FocusedName 'Remove game' 'Remove game button'
        Send-Keys '{ENTER}'
        [void](Wait-VisibleById 'PrimaryButton')
        Send-Keys '{ESC}'
        [void](Wait-HiddenById 'PrimaryButton')
        Wait-FocusedName 'Remove game' 'after Esc cancelled the removal'
        [void](Wait-Name 'GameHeading' $game)
        $report.phases += 'keyboard-confirm-dialog'

        # TXT: Page Down lands where Next page does, Ctrl+Home returns, and
        # Ctrl+Plus / Ctrl+0 change and reset the text size.
        Open-GuideFromGame 'ASCII Map Guide'
        [void](Wait-Status 'Guide ready.')
        $start = Get-TopTextLine
        Invoke-Element (Find-ByName 'Next page')
        Start-Sleep -Milliseconds 500
        $afterNext = Get-TopTextLine
        if ($afterNext -eq $start) { throw 'Next page did not move the TXT reader.' }
        (Get-FirstListItem 'ReaderTextLines').SetFocus()
        Send-Keys '^{HOME}'
        Wait-TopTextLine $start 'Ctrl+Home'
        Send-Keys '{PGDN}'
        Wait-TopTextLine $afterNext 'Page Down'
        $size = (Wait-VisibleById 'TextSizeValue').Current.Name
        Send-Keys '^{ADD}'
        $deadline = (Get-Date).AddSeconds(5)
        while ((Find-ById 'TextSizeValue').Current.Name -eq $size -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 150 }
        if ((Find-ById 'TextSizeValue').Current.Name -eq $size) { throw 'Ctrl+Plus did not change the TXT text size.' }
        Send-Keys '^0'
        Send-Keys '%{LEFT}'
        [void](Wait-Name 'GameHeading' $game)
        $report.phases += 'keyboard-txt'

        # HTML: with focus inside the page, Ctrl+Plus and Alt+Left still work.
        Open-GuideFromGame 'Static Web Guide'
        [void](Wait-Status 'Guide ready.')
        $size = (Wait-VisibleById 'TextSizeValue').Current.Name
        Click-Page
        Send-Keys '^{ADD}'
        $deadline = (Get-Date).AddSeconds(5)
        while ((Find-ById 'TextSizeValue').Current.Name -eq $size -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 150 }
        if ((Find-ById 'TextSizeValue').Current.Name -eq $size) { throw 'Ctrl+Plus inside the HTML page did not change the text size.' }
        Click-Page
        Send-Keys '^0'
        Click-Page
        Send-Keys '%{LEFT}'
        [void](Wait-Name 'GameHeading' $game)
        $report.phases += 'keyboard-html'

        # Settings: the list shows every shortcut.
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        $expander = Show-SettingsCard 'KeyboardShortcutsExpander'
        $pattern = $expander.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        if ($pattern.Current.ExpandCollapseState -ne 'Expanded') { $pattern.Expand() }
        $expected = [ordered]@{
            ShortcutBack = 'Go back, Alt+Left'
            ShortcutFocusLibrarySearch = 'Search the library, Ctrl+F'
            ShortcutClearLibrarySearch = 'Clear the search, Esc'
            ShortcutImportGuide = 'Import a guide, Ctrl+O'
            ShortcutPreviousPage = 'Previous page (TXT, PDF), Page Up'
            ShortcutNextPage = 'Next page (TXT, PDF), Page Down'
            ShortcutPageStart = 'Go to start (TXT, PDF), Ctrl+Home'
            ShortcutPageEnd = 'Go to end (TXT, PDF), Ctrl+End'
            ShortcutGoToPage = 'Go to page (PDF), Ctrl+G'
            ShortcutZoomIn = 'Zoom in (PDF) or larger text, Ctrl+Plus'
            ShortcutZoomOut = 'Zoom out (PDF) or smaller text, Ctrl+Minus'
            ShortcutZoomReset = 'Fit to width (PDF) or reset text size, Ctrl+0'
            ShortcutCloseDialog = 'Close the dialog, Esc'
        }
        foreach ($id in $expected.Keys) { [void](Wait-Name $id $expected[$id]) }
        [void](Show-SettingsCard 'ShortcutImportGuide')
        $report.settingsScreenshot = Save-WindowScreenshot 'settings-shortcuts'
        $report.phases += 'keyboard-settings'
    }
```

Check each helper the mode uses against the script: `Find-RawById`,
`Wait-VisibleById`, `Find-ById`, `Find-ByName`, `Wait-FocusWithin`,
`Wait-Status`, `Get-SearchText`, `Wait-Name`, `Select-Element`,
`Invoke-Element`, `Open-GuideFromGame`, `Wait-FilePicker`,
`Wait-PickerClosed`, `Show-SettingsCard`, `Resize-ShellWindow` and
`Save-WindowScreenshot`. `$scope` is the script's Descendants scope. If
`Find-ByName` doesn't exist, add it beside `Find-ById` using a
`NameProperty` condition. Record any other substitution as a ruling.

In `windows_shell_install.ps1`, replace the probe lines from Task 3 with:

```powershell
        Start-InstalledShell
        $report.keyboard = Run-ShellSmoke 'keyboard'
        Close-InstalledShell
```

Run the ASCII and parse checks on both scripts (as T11.5 Task 5 did).
Expected: no non-ASCII lines and 0 parse errors.

- [ ] **Step 3: Run the trace in CI**

Commit and push, then run the **Installed group**.

```bash
git add tests tools/p1
git commit -m "test(p1): add the T16.1 keyboard trace and tooltip parity check"
git push
```

Expected: the `design` shard passes, with phases `keyboard-library-search`,
`keyboard-wrong-context`, `keyboard-import-picker`, `keyboard-confirm-dialog`, `keyboard-txt`,
`keyboard-html` and `keyboard-settings`. If a check fails, use
superpowers:systematic-debugging on the result JSON before changing
anything. The most likely suspect is TXT Page Down landing on a different
line than Next page, which means the ListView also handled the key.

Download the screenshot. Confirm the Settings list reads clearly and that
the `E9CE` glyph and the `question_circle` icon match in meaning (ruling
9). Run once more with Colorful icons on, or use the `icons` mode's
Settings screenshot, which now includes the card.

- [ ] **Step 4: Add the README section**

In `README.md`, add a "Keyboard shortcuts" section after the usage section
(or at the end if there is none):

```markdown
## Keyboard shortcuts

| Where | Keys | Does |
| --- | --- | --- |
| Everywhere | Alt+Left | Go back |
| Library | Ctrl+F | Search the library |
| Library search | Esc | Clear the search |
| Game page | Ctrl+O | Import a guide |
| Reader (TXT, PDF) | Page Up / Page Down | Previous / next page |
| Reader (TXT, PDF) | Ctrl+Home / Ctrl+End | Go to start / end |
| Reader (PDF) | Ctrl+G | Go to page |
| Reader | Ctrl+Plus / Ctrl+Minus / Ctrl+0 | Zoom (PDF) or text size (TXT, HTML) |
| Dialogs | Esc | Close the dialog |

Settings → Help → Keyboard shortcuts lists the same keys.
```

```bash
git add README.md
git commit -m "docs: list the keyboard shortcuts in the README"
```

### Task 6: Verification record, status pages and the PR

**Files:**
- Modify: `docs/p1/t16-1-keyboard-shortcuts-design.md` (status line, a new `## T16.1 verification record`)
- Modify: `docs/p1/implementation-plan.md` (status paragraph)
- Modify: `docs/progress.md` (a T16.1 row; the P1 row's open list)
- Create: `docs/p1/evidence/t16-1-keyboard-shortcuts/settings-shortcuts-light.png`, `settings-shortcuts-dark.png`

- [ ] **Step 1: Record the verification**

Add `## T16.1 verification record` to the end of the design. List:
- the Core test counts from the host run;
- the HTML probe values and the mechanism kept (Task 3);
- the CI run IDs: each `design` dispatch and the full PR run, with any
  reruns and their cause;
- the trace phases from the `keyboard` result;
- rulings 1 to 9 from this plan, one line each, plus any made during
  execution;
- **gaps for T16.2:** anything the trace showed outside these shortcuts
  (tab order, focus visuals, Narrator), listed and not fixed;
- anything not run.

Copy the Settings screenshots, light and dark, into the evidence folder.

- [ ] **Step 2: Update the status pages**

- **Design status line:** "Status: in review in PR #<n>."
- **`implementation-plan.md`:** add a T16.1 sentence to the status
  paragraph saying it is in review in #<n>, with a link to
  `t16-1-keyboard-shortcuts-design.md#t161-verification-record`.
- **`progress.md`:**
  - Add a `P1 T16.1 keyboard shortcuts` row: "In review: PR #<n>", with a
    one-line summary.
  - In the P1 row's open list, change "T16.1–T16.3 keyboard, accessibility
    audit and PDF Narrator record" to "T16.1 keyboard shortcuts (in review,
    #<n>); T16.2–T16.3 accessibility audit and PDF Narrator record".

Run: `git diff --check`
Expected: no output.

```bash
git add docs
git commit -m "docs(p1): record the T16.1 verification"
```

- [ ] **Step 3: Open the PR**

Push and open the PR against `main`. The body:
- names the target task (T16.1 keyboard shortcuts);
- gives the prerequisites and their status (T05.2, T06.1, T08.3, T10.2,
  T11.3: merged);
- states the intended outcome: Ctrl+F and Ctrl+O, Esc in the Library
  search, the reader keys for TXT and HTML (including from inside an HTML
  page), the Settings list, and focus return;
- embeds the Settings screenshots;
- lists the rulings;
- ends with the Claude Code line.

Run the full PR CI and expect it green before marking the PR ready.

## Traceability

| Requirement | Where |
| --- | --- |
| Ctrl+O on a Game page | Task 1 (routing), Task 2 (accelerator), Task 5 (trace) |
| Ctrl+F for the Library search | Task 1, Task 2, Task 5 |
| Esc in dialogs and the Library search | Task 1 (`LibrarySearchEsc`), Task 2, Task 5; PDF dialog Esc in the existing `pdf` smoke |
| Page movement in the active reader | Task 2 (TXT and HTML keys on), Task 5 (TXT); PDF in the existing `pdf` smoke |
| Unavailable elsewhere, never over typing | Task 1 (routing tests), Task 5 (wrong-context check) |
| Visible command parity | Task 2 (tooltips, `AcceleratorKey`), Task 5 (`ReaderToolbarTooltipTests`) |
| Documented shortcuts | Task 4 (Settings list), Task 5 (README) |
| TR16.1: no keyboard-only or hover-only flow | Every shortcut has a visible control (Traceability rows above) |
| TR16.2: focus returns from overlays | Ruling 4; Task 5 (import picker, Remove game dialog); existing `pdf` smoke (Go to page) |
| HTML guides | Task 3 (probe, forwarding), Task 5 (trace) |

## PR outcome

One PR, `feat/p1-t16-1-keyboard-shortcuts`. Once it merges, T16.2's audit
can start, and the TXT and HTML reader keys deferred since T08.3 and T10.2
work at last.
