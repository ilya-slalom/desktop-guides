# T05.3 Stable navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Back and list changes keep the person's place by ID: Back to Library
focuses the opened game with the query intact, Back to a Game restores its
selected guide, removals select the nearest survivor, and late async work
can't move focus or selection to a stale row.

**Architecture:** `ShellNavigator` stores an optional anchor ID next to every
back-stack entry (Library → game ID, Game → guide ID). A pure
`ListAnchor.Resolve` picks the surviving row after a list change. The WinUI
shell reads the anchor when it renders an entry, writes it when the guide
selection changes, and restores Library row focus on Back.

**Tech Stack:** .NET 10, C#, xUnit, WinUI 3, Windows PowerShell 5.1 UI
Automation smoke, SQLite seed tool.

**Spec:** [t05-3-stable-navigation-design.md](t05-3-stable-navigation-design.md)

## Global Constraints

- Route records and their ID-only equality are unchanged.
- `SetAnchor` throws `InvalidOperationException` on a Reader or Settings entry.
- Restoring the Library anchor focuses the row and never sets `SelectedItem`
  (selecting a row opens the game).
- Nothing is persisted: no SQLite, settings or file changes for anchors.
- Keyboard and mouse Back (Alt+Left, XButton1) are out of scope.
- PowerShell scripts stay ASCII-only; non-ASCII strings are built from code
  points.
- UI tests assert only what app code controls: focused element, selected row,
  search text, visible rows.
- CI `production-shell-ui` (full `tools/p1/windows_shell_install.ps1`) is the
  gate for shell behaviour. Host E2E runs only to debug CI failures, following
  `docs/p1/e2e-testing.md`.
- Every commit message is shown to the user and approved before committing;
  trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Host commands run on `pcsx2-win` from `E:\work\desktop-guides\t05-3`
  (staged with the `stage` helper below).

Host helpers (run from the repo root on the Mac):

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){ s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t05-3) { Remove-Item -Recurse -Force E:\work\desktop-guides\t05-3 }; New-Item -ItemType Directory E:\work\desktop-guides\t05-3 | Out-Null"'; COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='._*' -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t05-3'; }
```

Baselines before this plan: Core 280 tests, Infrastructure 429 tests.

## Rulings carried from planning

These refine the spec; Task 6 edits the spec to match.

1. **Library focus restore runs only on Back.** Game removal keeps its
   T04.3 behaviour (focus on `AddGameButton`), which the game-actions smoke
   asserts.
2. **No Library in-place reload rule.** Nothing re-renders the Library while
   it is showing (import, rename and removal all happen on the Game page), so
   the spec's "in-place reload" bullet has no trigger and is dropped.
3. **Empty-list fallback** is `LibrarySearchInput` when enabled, else
   `AddGameButton`.
4. **Reopening the same game** without an anchor falls back to the list's
   current selection only when the list still shows that game.
5. **Stale refresh has no deterministic smoke phase.** CI has no provider
   keys, so a refresh fails at once and can't be ordered against a
   navigation. The guard is code-reviewed (Review Focus 1). The existing
   `PauseReaderMetadataReadForTestAsync` gate pattern could back a later phase.
6. **Shell wiring has no unit-testable RED.** `ShellWindow` has no test
   project. The smoke (Task 3) is written first and fails against the
   current shell; Tasks 4–5 make it pass. The local proof is a Release
   build; CI is the gate.
7. **Guide order is stable during the smoke.** Nothing writes
   `ReadingStates.LastOpenedUtcMs` yet (T12.3, T13.2), so opening a guide
   doesn't reorder the list; the seeded order Third, Second, First holds and
   the smoke covers both the next-row and the previous-row removal cases.

## Review Focus

1. **A metadata refresh finishing after the person left the game** must not
   re-render, refocus, or post "Details refreshed" on the new page.
2. **A render overtaken by a newer navigation** (fast Back, Back) must not
   focus a Library row or select a guide on the newer page; every restore
   checks `renderGeneration` and the route.
3. **Back to a Library whose query hides the anchored game** focuses the first
   visible row, or the search box when nothing matches, never the disabled
   Back button.
4. **Removing the only guide** leaves no selection and focuses
   `ImportGuideButton`; the anchor becomes `null`, not the removed ID.
5. **A guide selection change made by the shell itself** (under
   `settingGuideSelection`) must not overwrite the anchor with a transient
   `null` while `GuideList` is cleared and refilled.

Items 3–5 have smoke or Core coverage in Tasks 1–5; items 1–2 are reviewed
against the code.

## File structure

| File | Change | Responsibility |
|---|---|---|
| `src/DesktopGuides.Core/Navigation/ListAnchor.cs` | Create | Pure survivor resolution. |
| `src/DesktopGuides.Core/Navigation/ShellNavigator.cs` | Modify | Per-entry anchors. |
| `tests/DesktopGuides.Core.Tests/ListAnchorTests.cs` | Create | Resolve rules. |
| `tests/DesktopGuides.Core.Tests/ShellNavigatorTests.cs` | Modify | Anchor rules. |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | Modify | `seed-navigation`. |
| `tools/p1/windows_shell_ui_smoke.ps1` | Modify | `stable-navigation` mode; catalog, library-search, import-publish edits. |
| `tools/p1/windows_shell_install.ps1` | Modify | `Run-StableNavigationScenarios`. |
| `src/DesktopGuides.Production/ShellWindow.xaml.cs` | Modify | Anchor reads/writes, removal survivor, refresh scope, Library focus. |
| `docs/…` | Modify | Spec rulings, e2e table, traceability, follow-ups. |

---

### Task 1: ListAnchor.Resolve

**Files:**
- Create: `src/DesktopGuides.Core/Navigation/ListAnchor.cs`
- Test: `tests/DesktopGuides.Core.Tests/ListAnchorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static Guid? ListAnchor.Resolve(IReadOnlyList<Guid> previousIds, IReadOnlyList<Guid> currentIds, Guid? anchorId)` in namespace `DesktopGuides.Core.Navigation`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ListAnchorTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();
    private static readonly Guid E = Guid.NewGuid();

    [Fact]
    public void AnchorThatSurvivesIsKept()
    {
        Assert.Equal(B, ListAnchor.Resolve([A, B, C], [A, B, C], B));
    }

    [Fact]
    public void AnchorIsKeptAfterReorder()
    {
        Assert.Equal(B, ListAnchor.Resolve([A, B, C], [B, C, A], B));
    }

    [Fact]
    public void RemovedAnchorResolvesToNextRow()
    {
        Assert.Equal(C, ListAnchor.Resolve([A, B, C], [A, C], B));
    }

    [Fact]
    public void RemovedLastRowResolvesToPreviousRow()
    {
        Assert.Equal(B, ListAnchor.Resolve([A, B, C], [A, B], C));
    }

    [Fact]
    public void RemovedNeighboursResolveToNearestLaterSurvivor()
    {
        Assert.Equal(E, ListAnchor.Resolve([A, B, C, D, E], [A, E], C));
    }

    [Fact]
    public void RemovedTailResolvesToNearestEarlierSurvivor()
    {
        Assert.Equal(A, ListAnchor.Resolve([A, B, C, D], [A], C));
    }

    [Fact]
    public void EmptyCurrentListResolvesToNull()
    {
        Assert.Null(ListAnchor.Resolve([A, B], [], A));
    }

    [Fact]
    public void NullAnchorResolvesToNull()
    {
        Assert.Null(ListAnchor.Resolve([A, B], [A, B], null));
    }

    [Fact]
    public void AnchorMissingFromBothListsResolvesToNull()
    {
        Assert.Null(ListAnchor.Resolve([A, B], [A, B], C));
    }

    [Fact]
    public void AnchorOnlyInCurrentListIsKept()
    {
        Assert.Equal(C, ListAnchor.Resolve([], [A, C], C));
    }

    [Fact]
    public void NullListsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ListAnchor.Resolve(null!, [A], A));
        Assert.Throws<ArgumentNullException>(() => ListAnchor.Resolve([A], null!, A));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `stage; s 'cd /d E:\work\desktop-guides\t05-3 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
Expected: build FAIL, `error CS0103: The name 'ListAnchor' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

```csharp
namespace DesktopGuides.Core.Navigation;

public static class ListAnchor
{
    // IDs in each list are unique. Returns the anchor if it is still shown;
    // otherwise the nearest survivor by its old position, later rows first;
    // otherwise null, which tells the caller to use its own fallback.
    public static Guid? Resolve(
        IReadOnlyList<Guid> previousIds,
        IReadOnlyList<Guid> currentIds,
        Guid? anchorId)
    {
        ArgumentNullException.ThrowIfNull(previousIds);
        ArgumentNullException.ThrowIfNull(currentIds);
        if (anchorId is not Guid anchor)
        {
            return null;
        }
        HashSet<Guid> current = [.. currentIds];
        if (current.Contains(anchor))
        {
            return anchor;
        }
        int index = -1;
        for (int i = 0; i < previousIds.Count; i++)
        {
            if (previousIds[i] == anchor)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            return null;
        }
        for (int i = index + 1; i < previousIds.Count; i++)
        {
            if (current.Contains(previousIds[i]))
            {
                return previousIds[i];
            }
        }
        for (int i = index - 1; i >= 0; i--)
        {
            if (current.Contains(previousIds[i]))
            {
                return previousIds[i];
            }
        }
        return null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `stage; s 'cd /d E:\work\desktop-guides\t05-3 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
Expected: `Passed!  - Failed: 0, Passed: 291` (280 + 11).

- [ ] **Step 5: Commit** (show the message to the user first)

```bash
git add src/DesktopGuides.Core/Navigation/ListAnchor.cs tests/DesktopGuides.Core.Tests/ListAnchorTests.cs
git commit -m "feat(core): resolve a list anchor to its nearest survivor (T05.3)"
```

---

### Task 2: ShellNavigator anchors

**Files:**
- Modify: `src/DesktopGuides.Core/Navigation/ShellNavigator.cs`
- Test: `tests/DesktopGuides.Core.Tests/ShellNavigatorTests.cs` (append)

**Interfaces:**
- Consumes: nothing.
- Produces: `Guid? ShellNavigator.CurrentAnchor { get; }`;
  `void ShellNavigator.SetAnchor(Guid? anchorId)` (throws
  `InvalidOperationException` unless `Current` is `LibraryRoute` or
  `GameRoute`). `OpenGame`, `OpenReader`, `OpenLibrary`, `OpenSettings`,
  `GoBack`, `ResetToLibrary` keep their signatures.

- [ ] **Step 1: Write the failing tests** (append inside `ShellNavigatorTests`)

```csharp
    [Fact]
    public void OpenGameFromLibraryAnchorsTheLibraryOnThatGame()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();

        navigator.OpenGame(gameId);

        Assert.Null(navigator.CurrentAnchor);
        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.Equal(gameId, navigator.CurrentAnchor);
    }

    [Fact]
    public void OpenReaderAnchorsTheGameOnThatGuide()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        navigator.OpenGame(gameId);

        navigator.OpenReader(guideId, gameId);

        Assert.Null(navigator.CurrentAnchor);
        Assert.True(navigator.GoBack());
        Assert.Equal(new GameRoute(gameId), navigator.Current);
        Assert.Equal(guideId, navigator.CurrentAnchor);
    }

    [Fact]
    public void ResumeFromLibraryAnchorsTheGameAndTheLibrary()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();

        navigator.OpenReader(guideId, gameId);

        Assert.True(navigator.GoBack());
        Assert.Equal(new GameRoute(gameId), navigator.Current);
        Assert.Equal(guideId, navigator.CurrentAnchor);
        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.Equal(gameId, navigator.CurrentAnchor);
    }

    [Fact]
    public void SetAnchorIsKeptThroughSettingsAndBack()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        Guid firstGuide = Guid.NewGuid();
        Guid secondGuide = Guid.NewGuid();
        navigator.OpenGame(gameId);

        navigator.SetAnchor(firstGuide);
        navigator.SetAnchor(secondGuide);
        navigator.OpenSettings();

        Assert.True(navigator.GoBack());
        Assert.Equal(secondGuide, navigator.CurrentAnchor);
    }

    [Fact]
    public void SetAnchorCanClearTheAnchor()
    {
        ShellNavigator navigator = new();
        navigator.OpenGame(Guid.NewGuid());
        navigator.SetAnchor(Guid.NewGuid());

        navigator.SetAnchor(null);

        Assert.Null(navigator.CurrentAnchor);
    }

    [Fact]
    public void SetAnchorThrowsOnReaderAndSettings()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        navigator.OpenReader(Guid.NewGuid(), gameId);

        Assert.Throws<InvalidOperationException>(() => navigator.SetAnchor(Guid.NewGuid()));
        navigator.OpenSettings();
        Assert.Throws<InvalidOperationException>(() => navigator.SetAnchor(null));
    }

    [Fact]
    public void BackToAnotherGameRestoresItsAnchor()
    {
        ShellNavigator navigator = new();
        Guid firstGame = Guid.NewGuid();
        Guid secondGame = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        navigator.OpenGame(firstGame);
        navigator.SetAnchor(guideId);

        navigator.OpenLibrary();
        navigator.OpenGame(secondGame);

        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.Equal(secondGame, navigator.CurrentAnchor);
        Assert.True(navigator.GoBack());
        Assert.Equal(new GameRoute(firstGame), navigator.Current);
        Assert.Equal(guideId, navigator.CurrentAnchor);
    }

    [Fact]
    public void ResetToLibraryClearsEveryAnchor()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        navigator.OpenGame(gameId);
        navigator.SetAnchor(Guid.NewGuid());

        navigator.ResetToLibrary();

        Assert.Null(navigator.CurrentAnchor);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void OpeningTheCurrentRouteKeepsItsAnchor()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        navigator.OpenGame(gameId);
        navigator.SetAnchor(guideId);

        navigator.OpenGame(gameId);

        Assert.Equal(guideId, navigator.CurrentAnchor);
        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void RouteEqualityIgnoresAnchors()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        navigator.OpenGame(gameId);
        navigator.SetAnchor(Guid.NewGuid());

        Assert.Equal(new GameRoute(gameId), navigator.Current);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `stage; s 'cd /d E:\work\desktop-guides\t05-3 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
Expected: build FAIL, `error CS1061: 'ShellNavigator' does not contain a definition for 'CurrentAnchor'` (and `SetAnchor`).

- [ ] **Step 3: Write the implementation** — replace the body of
  `ShellNavigator` (route records above it are unchanged):

```csharp
public sealed class ShellNavigator
{
    // Each entry keeps an optional anchor: the focused game on the Library,
    // the selected guide on a Game. Reader and Settings never have one.
    private readonly List<(ShellRoute Route, Guid? Anchor)> backStack = [];

    public ShellRoute Current { get; private set; } = new LibraryRoute();

    public Guid? CurrentAnchor { get; private set; }

    public bool CanGoBack => backStack.Count != 0;

    public void OpenLibrary() => Navigate(new LibraryRoute());

    public void OpenSettings() => Navigate(new SettingsRoute());

    public void OpenGame(Guid gameId)
    {
        RequireId(gameId);
        if (Current is LibraryRoute)
        {
            CurrentAnchor = gameId;
        }
        Navigate(new GameRoute(gameId));
    }

    public void OpenReader(Guid guideId, Guid gameId)
    {
        RequireId(guideId);
        RequireId(gameId);
        ReaderRoute reader = new(guideId, gameId);
        if (Current == reader)
        {
            return;
        }
        if (Current is not GameRoute game || game.GameId != gameId)
        {
            if (Current is LibraryRoute)
            {
                CurrentAnchor = gameId;
            }
            Navigate(new GameRoute(gameId));
        }
        CurrentAnchor = guideId;
        Navigate(reader);
    }

    public void SetAnchor(Guid? anchorId)
    {
        if (Current is not (LibraryRoute or GameRoute))
        {
            throw new InvalidOperationException("Only the Library and Game pages keep an anchor.");
        }
        CurrentAnchor = anchorId;
    }

    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }
        int index = backStack.Count - 1;
        (Current, CurrentAnchor) = backStack[index];
        backStack.RemoveAt(index);
        return true;
    }

    public void ResetToLibrary()
    {
        backStack.Clear();
        Current = new LibraryRoute();
        CurrentAnchor = null;
    }

    private void Navigate(ShellRoute route)
    {
        if (Current == route)
        {
            return;
        }
        backStack.Add((Current, CurrentAnchor));
        Current = route;
        CurrentAnchor = null;
    }

    private static void RequireId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A generated ID is required.");
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `stage; s 'cd /d E:\work\desktop-guides\t05-3 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
Expected: `Passed!  - Failed: 0, Passed: 301` (291 + 10), existing navigator tests unchanged.

- [ ] **Step 5: Commit** (show the message to the user first)

```bash
git add src/DesktopGuides.Core/Navigation/ShellNavigator.cs tests/DesktopGuides.Core.Tests/ShellNavigatorTests.cs
git commit -m "feat(core): keep a per-entry anchor in the shell back stack (T05.3)"
```

---
### Task 3: Navigation seed and smoke (written first; RED against today's shell)

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (usage check ~386-399; new block after `seed-search` ~638-674)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (Mode ValidateSet lines 3-14; new `elseif` before `catalog` ~1598; catalog ~1692-1700; import-publish ~1961-1990)
- Modify: `tools/p1/windows_shell_install.ps1` (new function after `Run-LibrarySearchScenarios` ~835; calls at ~1343 and ~1422)

**Interfaces:**
- Consumes: nothing from Tasks 1-2 directly; it tests the shell behaviour Tasks 4-5 add.
- Produces: seed command `seed-navigation <app-data-root>`; smoke mode `stable-navigation`; install function `Run-StableNavigationScenarios`; report keys `stableNavigationLight`, `stableNavigationDark`.

Seed data (`seed-navigation`):

| Game | Guides (imported) | Matches `navigation` |
|---|---|---|
| Atlas Navigation Game | Atlas First Guide (-3 d), Atlas Second Guide (-2 d), Atlas Third Guide (-1 d) | yes |
| Beacon Navigation Game | Beacon Guide (-4 d) | yes |
| Cobalt Other Game | none | no |

Guide order on the Atlas page is Third, Second, First (newest import first).
`LastActiveGuideId` is Beacon Guide, so the Library shows `Resume Beacon Guide`.

- [ ] **Step 1: Add the seed command**

In the usage check add `"seed-navigation"` to the `args[0] is not (...)` list
and `|seed-navigation` after `seed-actions` in the usage string. Then add,
after the `seed-search` block:

```csharp
if (args[0] == "seed-navigation")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The navigation seed needs an empty library.");
    }
    const long navigationDay = 86_400_000;
    long navigationNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    Game atlas = await repository.AddGameAsync("Atlas Navigation Game", null, null);
    Game beacon = await repository.AddGameAsync("Beacon Navigation Game", null, null);
    await repository.AddGameAsync("Cobalt Other Game", null, null);
    // Import times fix the Atlas guide order: Third, Second, First.
    await InsertGuideAsync(paths, atlas.Id, Guid.NewGuid(), "Atlas First Guide", navigationNow - 3 * navigationDay);
    await InsertGuideAsync(paths, atlas.Id, Guid.NewGuid(), "Atlas Second Guide", navigationNow - 2 * navigationDay);
    await InsertGuideAsync(paths, atlas.Id, Guid.NewGuid(), "Atlas Third Guide", navigationNow - navigationDay);
    Guid beaconGuideId = Guid.NewGuid();
    await InsertGuideAsync(paths, beacon.Id, beaconGuideId, "Beacon Guide", navigationNow - 4 * navigationDay);
    AppSettings navigationSettings = await repository.GetSettingsAsync();
    await repository.SaveSettingsAsync(navigationSettings with { LastActiveGuideId = beaconGuideId });
    Console.WriteLine("Seeded three navigation games.");
    return 0;
}
```

- [ ] **Step 2: Build the seed tool**

Run: `stage; s 'cd /d E:\work\desktop-guides\t05-3 && dotnet build tools\p1\DesktopGuides.ShellSeed -c Release'`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Add the `stable-navigation` smoke mode**

Add `'stable-navigation'` to the Mode `ValidateSet` (after `'library-search'`).
Insert this branch before `elseif ($Mode -eq 'catalog')`:

```powershell
    elseif ($Mode -eq 'stable-navigation') {
        $atlas = 'Atlas Navigation Game'
        $beacon = 'Beacon Navigation Game'
        $cobalt = 'Cobalt Other Game'
        $renamedBeacon = 'Beacon Renamed Game'
        $query = 'navigation'

        function Assert-QueryKept([string] $where) {
            if ((Get-SearchText 'LibrarySearchInput') -ne $query) {
                throw "$where dropped the Library search query."
            }
        }

        function Assert-NavigationRows {
            [void](Wait-GameRow $atlas)
            [void](Wait-GameRow $beacon)
            if ((Count-GameRows $cobalt) -ne 0) {
                throw "The '$query' query showed $cobalt."
            }
        }

        function Back-ToLibrary([string] $where) {
            Go-Back
            [void](Wait-Name 'LibraryHeading' 'Library')
            [void](Wait-Status 'Library ready.')
            Assert-QueryKept $where
        }

        function Open-AtlasGame {
            Select-Element $atlas
            [void](Wait-Name 'GameHeading' $atlas)
            [void](Wait-Status 'Game ready.')
        }

        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-EnabledById 'LibrarySearchInput')
        Set-SearchQuery $query 'LibrarySearchInput'
        [void](Wait-Status '2 of 3 games match.' -AllowHidden)
        Assert-NavigationRows

        # Resume pushes Library -> Game -> Reader; Back walks the same entries.
        Invoke-Element (Wait-Name 'ResumeGuide' 'Resume Beacon Guide')
        [void](Wait-Name 'ReaderHeading' 'Beacon Guide')
        [void](Wait-Status 'Guide details ready.')
        Go-Back
        [void](Wait-Name 'GameHeading' $beacon)
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide 'Beacon Guide')
        [void](Wait-FocusedGuide 'Beacon Guide')
        Back-ToLibrary 'Back after Resume'
        Assert-NavigationRows
        [void](Wait-FocusedGameRow $beacon)
        $report.phases += 'resume-back-keeps-query-and-row'

        # Focus returns to the opened row without selecting (opening) it.
        Open-AtlasGame
        Back-ToLibrary 'Back from a game'
        [void](Wait-FocusedGameRow $atlas)
        Start-Sleep -Milliseconds 500
        [void](Wait-HiddenById 'GameHeading')
        [void](Wait-HiddenById 'ShellStatus')
        $report.libraryFocusScreenshot = Save-WindowScreenshot 'library-focus-restored'
        $report.phases += 'back-focuses-opened-row'

        Open-AtlasGame
        Open-GuideFromGame 'Atlas Second Guide'
        [void](Wait-Name 'ReaderHeading' 'Atlas Second Guide')
        [void](Wait-Status 'Guide details ready.')
        Go-Back
        [void](Wait-Name 'GameHeading' $atlas)
        [void](Wait-SelectedGuide 'Atlas Second Guide')
        [void](Wait-FocusedGuide 'Atlas Second Guide')
        $report.phases += 'reader-back-keeps-guide'

        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        Go-Back
        [void](Wait-Name 'GameHeading' $atlas)
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide 'Atlas Second Guide')
        $report.phases += 'settings-back-keeps-guide'

        # Library -> another game -> Back, Back returns to Atlas's guide.
        Press-Enter (Wait-VisibleById 'LibraryNavigation')
        [void](Wait-Name 'LibraryHeading' 'Library')
        Assert-QueryKept 'The Library navigation item'
        Select-Element $beacon
        [void](Wait-Name 'GameHeading' $beacon)
        [void](Wait-Status 'Game ready.')
        Back-ToLibrary 'Back from another game'
        [void](Wait-FocusedGameRow $beacon)
        Go-Back
        [void](Wait-Name 'GameHeading' $atlas)
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide 'Atlas Second Guide')
        $report.phases += 'other-game-back-keeps-guide'

        # Removal selects the next row, then the previous one at the end.
        Invoke-Element (Wait-Name 'RemoveSelectedGuide' 'Remove Atlas Second Guide')
        [void](Wait-VisibleById 'RemoveGuideDialog')
        Invoke-Element (Wait-EnabledById 'PrimaryButton')
        [void](Wait-HiddenById 'RemoveGuideDialog')
        [void](Wait-Status 'Removed Atlas Second Guide.')
        [void](Wait-GuideRowCount 2)
        [void](Wait-SelectedGuide 'Atlas First Guide')
        [void](Wait-FocusedGuide 'Atlas First Guide')
        $report.phases += 'remove-selects-next-guide'

        Invoke-Element (Wait-Name 'RemoveSelectedGuide' 'Remove Atlas First Guide')
        [void](Wait-VisibleById 'RemoveGuideDialog')
        Invoke-Element (Wait-EnabledById 'PrimaryButton')
        [void](Wait-HiddenById 'RemoveGuideDialog')
        [void](Wait-Status 'Removed Atlas First Guide.')
        [void](Wait-GuideRowCount 1)
        [void](Wait-SelectedGuide 'Atlas Third Guide')
        [void](Wait-FocusedGuide 'Atlas Third Guide')
        $report.phases += 'remove-last-selects-previous-guide'

        # This Library entry is the one Atlas was opened from.
        Back-ToLibrary 'Back after guide removal'
        [void](Wait-FocusedGameRow $atlas)
        $report.phases += 'remove-keeps-query'

        # A rename that leaves the query hides the row; focus takes the first row.
        Select-Element $beacon
        [void](Wait-Name 'GameHeading' $beacon)
        [void](Wait-Status 'Game ready.')
        Invoke-Element (Wait-EnabledById 'EditGameButton')
        Set-Text 'GameTitleInput' $renamedBeacon
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        [void](Wait-Name 'GameHeading' $renamedBeacon)
        [void](Wait-Status 'Game ready.')
        Back-ToLibrary 'Back after rename'
        [void](Wait-GameRow $atlas)
        if ((Count-GameRows $renamedBeacon) -ne 0) {
            throw "The '$query' query showed the renamed game."
        }
        [void](Wait-FocusedGameRow $atlas)
        $report.phases += 'rename-keeps-query-and-falls-back'

        Assert-NoRemoteConnections 'stable navigation'
        $report.phases += 'navigation-no-provider-traffic'
    }
```

- [ ] **Step 4: Replace the catalog Back workaround**

In the catalog mode, replace the block after the first `Go-Back` (from
`Focus-And-Verify 'AddGameButton'` through `[void](Wait-FocusedGameRow '')`)
and the lines after the second `Go-Back`, so the section reads:

```powershell
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        [void](Wait-FocusedGameRow $shortTitle)
        [System.Windows.Forms.SendKeys]::SendWait('{END}')
        [void](Wait-Name 'GameHeading' $lastTitle)
        [void](Wait-Status 'Game ready.')
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        [void](Wait-FocusedGameRow $lastTitle)
        $report.phases += 'catalog-keyboard'
```

- [ ] **Step 5: Extend library-search and import-publish**

In library-search, after `[void](Assert-RowFacts 'GameList' @(,$zetaMatch))`
in the `search-kept-after-back` phase, add:

```powershell
        [void](Wait-FocusedGameRow $zeta)
```

In the import branch, replace `Select-Element 'Import Test Game'` with:

```powershell
            if ($Mode -eq 'import-publish') {
                Set-SearchQuery 'Import Test' 'LibrarySearchInput'
                [void](Wait-GameRow 'Import Test Game')
            }
            Select-Element 'Import Test Game'
```

and after `$report.phases += 'import-published'` add:

```powershell
                Go-Back
                [void](Wait-Name 'LibraryHeading' 'Library')
                [void](Wait-Status 'Library ready.')
                if ((Get-SearchText 'LibrarySearchInput') -ne 'Import Test') {
                    throw 'Back after an import dropped the Library search query.'
                }
                [void](Wait-FocusedGameRow 'Import Test Game')
                $report.phases += 'query-kept-after-import'
```

Before this edit, check in `windows_shell_install.ps1` that nothing after
the `import-publish` run in the same shell session expects the Game page;
if something does, add the Back phase at the end of that run instead and
ledger the ruling.

- [ ] **Step 6: Wire the install script**

After `Run-LibrarySearchScenarios` add:

```powershell
function Run-StableNavigationScenarios {
    # The smoke renames and removes, so each theme gets a fresh seed.
    $originalTheme = Get-AppThemePreference
    try {
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Invoke-ShellSeed @('seed-navigation', $dataRoot) | Out-Null
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.stableNavigationLight = Run-ShellSmoke 'stable-navigation' -ResultName 'stable-navigation-light'
        Close-InstalledShell

        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Invoke-ShellSeed @('seed-navigation', $dataRoot) | Out-Null
        Set-AppThemePreference $false
        Start-InstalledShell
        $report.stableNavigationDark = Run-ShellSmoke 'stable-navigation' -ResultName 'stable-navigation-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}
```

Call it after `Run-LibrarySearchScenarios` in both the `$CatalogOnly` block
and the full run (the function clears `$dataRoot` itself):

```powershell
        Run-LibrarySearchScenarios
        Run-StableNavigationScenarios
```

- [ ] **Step 7: Check the scripts parse and stay ASCII**

Run:
```bash
LC_ALL=C grep -nP '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1 || echo ascii-ok
stage; s 'powershell -NoProfile -Command "foreach ($f in @(''E:\work\desktop-guides\t05-3\tools\p1\windows_shell_ui_smoke.ps1'',''E:\work\desktop-guides\t05-3\tools\p1\windows_shell_install.ps1'')) { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$e); \"$f $($e.Count)\" }"'
```
Expected: `ascii-ok`, and each file reports `0` parse errors.

- [ ] **Step 8: RED evidence**

The new phases fail against today's shell, for example
`back-focuses-opened-row` (focus stays on the disabled Back button) and
`other-game-back-keeps-guide` (no guide selected). Don't run the installed
smoke locally: CI `production-shell-ui` is the gate, and host E2E runs are
only for debugging CI failures. Record in the ledger that RED is shown by
inspection of the current `RenderCurrentAsync` (no Library focus code; Game
selection comes only from `pendingGuideFocus ?? SelectedGuide`).

- [ ] **Step 9: Commit** (show the message to the user first)

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): add stable navigation smoke and seed (T05.3)"
```

---
### Task 4: Game page anchors, removal survivor, refresh scope, queued startup

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`
  - startup render ~275
  - `GuideSelected` ~446
  - `RemoveSelectedGuideClicked` ~871-985
  - Game case of `RenderCurrentAsync` ~1323-1432
  - `RefreshMetadataAsync` queued action ~1687-1706

**Interfaces:**
- Consumes: `ShellNavigator.CurrentAnchor`, `ShellNavigator.SetAnchor(Guid?)` (Task 2); `ListAnchor.Resolve(IReadOnlyList<Guid>, IReadOnlyList<Guid>, Guid?)` (Task 1).
- Produces: `private static bool FocusIsWithin(UIElement container)` (used by Task 5 only if needed; no other consumer).

TDD note (Ruling 6): there is no unit-test harness for `ShellWindow`. The
RED for this task is the Task 3 smoke phases `reader-back-keeps-guide`,
`settings-back-keeps-guide`, `other-game-back-keeps-guide`,
`remove-selects-next-guide` and `remove-last-selects-previous-guide`; the
local GREEN check is a Release build, and CI `production-shell-ui` is the gate.

- [ ] **Step 1: Queue the startup render**

Replace `await RenderCurrentAsync();` right after `ready = true;` with:

```csharp
            ready = true;
            // Queued like every other render, so a quick first click can't be overwritten.
            await RunNavigationAsync(() => RenderCurrentAsync());
```

- [ ] **Step 2: Record the person's guide selection as the anchor**

In `GuideSelected`, after the `settingGuideSelection` early return:

```csharp
        if (navigator.Current is GameRoute)
        {
            navigator.SetAnchor(SelectedGuide?.Id);
        }
        UpdateOpenSelectedGuideAction();
```

- [ ] **Step 3: Select from the anchor in the Game render**

Replace

```csharp
                    Guid? selectedGuideId = pendingGuideFocus ??
                        SelectedGuide?.Id;
```

with

```csharp
                    // The rows shown before this render, if they belong to this game;
                    // ListAnchor.Resolve uses them to find a removed guide's survivor.
                    bool sameGameList = detailsGameId == gameRoute.GameId;
                    List<Guid> shownGuideIds = sameGameList
                        ? [.. GuideList.Items.OfType<GuideRowItem>().Select(item => item.Guide.Id)]
                        : [];
                    Guid? wantedGuideId = pendingGuideFocus ?? navigator.CurrentAnchor ??
                        (sameGameList ? SelectedGuide?.Id : null);
```

Replace

```csharp
                    GuideRowItem? selectedGuide = selectedGuideId is Guid id
                        ? guides.FirstOrDefault(item => item.Guide.Id == id)
                        : null;
```

with

```csharp
                    Guid? selectedGuideId = ListAnchor.Resolve(
                        shownGuideIds, [.. guides.Select(item => item.Guide.Id)], wantedGuideId);
                    GuideRowItem? selectedGuide = selectedGuideId is Guid id
                        ? guides.First(item => item.Guide.Id == id)
                        : null;
                    if (pendingGuideFocus is not null)
                    {
                        // Focus follows the survivor when the wanted guide is gone.
                        pendingGuideFocus = selectedGuide?.Guide.Id;
                    }
                    if (navigator.Current == gameRoute)
                    {
                        navigator.SetAnchor(selectedGuide?.Guide.Id);
                    }
```

The focus block that follows (`if (pendingGuideFocus is not null && selectedGuide is not null)`) is unchanged.

- [ ] **Step 4: Let the render pick the removal survivor**

In `RemoveSelectedGuideClicked`, delete the neighbour lines:

```csharp
        // The row that takes the removed row's place, or the previous row.
        int index = GuideList.SelectedIndex;
        Guide? neighbor = GuideAt(index + 1 < GuideList.Items.Count ? GuideList.Items[index + 1]
            : index > 0 ? GuideList.Items[index - 1] : null);
```

and change both `pendingGuideFocus = neighbor?.Id;` lines to:

```csharp
                    // The render resolves the removed guide to its nearest survivor.
                    pendingGuideFocus = guide.Id;
```

`GuideAt` stays; `SelectedGuide` and the row handlers still use it.

- [ ] **Step 5: Scope the refresh's re-render and status to its game**

Replace the queued action at the end of `RefreshMetadataAsync` with:

```csharp
        await RunNavigationAsync(async () =>
        {
            // The person left this game: no re-render, no status on another page.
            if (navigator.Current is not GameRoute current || current.GameId != gameId)
            {
                return;
            }
            if (FocusIsWithin(GuideList))
            {
                pendingGuideFocus = navigator.CurrentAnchor;
            }
            await RenderCurrentAsync();
            if (error is not null)
            {
                ShowErrorStatus(error);
            }
            else if (result!.ArtworkMissing)
            {
                ShowWarningStatus("Metadata refreshed. A new cover couldn't be downloaded.");
            }
            else
            {
                ShowTransientStatus("Metadata refreshed.");
            }
        });
```

Add next to `TryRestoreGuideFocus`:

```csharp
    private static bool FocusIsWithin(UIElement container)
    {
        if (container.XamlRoot is null)
        {
            return false;
        }
        for (DependencyObject? element = FocusManager.GetFocusedElement(container.XamlRoot) as DependencyObject;
            element is not null;
            element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, container))
            {
                return true;
            }
        }
        return false;
    }
```

(`Microsoft.UI.Xaml.Input` and `Microsoft.UI.Xaml.Media` are already imported.)

- [ ] **Step 6: Build and run the unit suites**

Run:
```bash
stage; s 'cd /d E:\work\desktop-guides\t05-3 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release && dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release'
```
Expected: `Build succeeded.` with 0 errors and no new warnings; Core 301 passed;
Infrastructure 429 passed.

- [ ] **Step 7: Commit** (show the message to the user first)

```bash
git add src/DesktopGuides.Production/ShellWindow.xaml.cs
git commit -m "feat(shell): keep the Game page's guide by anchor (T05.3)"
```

---

### Task 5: Library row focus on Back

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`
  - fields ~46-51
  - `GoBackAsync` ~392-405
  - start of `RenderCurrentAsync` ~1266
  - Library case ~1311-1320

**Interfaces:**
- Consumes: `ShellNavigator.CurrentAnchor` (Task 2).
- Produces: nothing for later tasks.

TDD note (Ruling 6): the RED is the Task 3 smoke phases
`resume-back-keeps-query-and-row`, `back-focuses-opened-row`,
`rename-keeps-query-and-falls-back`, the catalog `catalog-keyboard` edit,
library-search `search-kept-after-back`, and import-publish
`query-kept-after-import`.

- [ ] **Step 1: Add the fields**

Next to `pendingGuideFocus`:

```csharp
    // Set by Back to the Library; the next Library render focuses the anchored row.
    private bool libraryFocusPending;
    private int libraryFocusGeneration = -1;
```

- [ ] **Step 2: Request the restore on Back**

In `GoBackAsync`, before `await RenderCurrentAsync();`:

```csharp
        libraryFocusPending = navigator.Current is LibraryRoute;
```

- [ ] **Step 3: Consume the request in the render**

At the start of `RenderCurrentAsync`, after `guideFocusRenderGeneration = -1;`:

```csharp
        bool restoreLibraryFocus = libraryFocusPending;
        libraryFocusPending = false;
        libraryFocusGeneration = -1;
```

In the Library case, before `ShowTransientStatus("Library ready.");`:

```csharp
                    if (restoreLibraryFocus)
                    {
                        RestoreLibraryFocus(generation);
                    }
```

- [ ] **Step 4: Add the restore methods** (next to `TryRestoreGuideFocus`)

```csharp
    private void RestoreLibraryFocus(int generation)
    {
        libraryFocusGeneration = generation;
        GameList.UpdateLayout();
        TryRestoreLibraryFocus();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (libraryFocusGeneration == generation)
            {
                GameList.UpdateLayout();
                TryRestoreLibraryFocus();
            }
        });
    }

    // Focuses the anchored row, or the first row when the query hides it.
    // Never sets SelectedItem: selecting a Library row opens the game.
    private void TryRestoreLibraryFocus()
    {
        if (libraryFocusGeneration != renderGeneration || navigator.Current is not LibraryRoute)
        {
            return;
        }
        List<LibraryGameItem> rows = [.. GameList.Items.OfType<LibraryGameItem>()];
        if (rows.Count == 0)
        {
            libraryFocusGeneration = -1;
            Control fallback = LibrarySearchInput.IsEnabled ? LibrarySearchInput : AddGameButton;
            fallback.Focus(FocusState.Programmatic);
            return;
        }
        LibraryGameItem target = rows.FirstOrDefault(row => row.Game.Id == navigator.CurrentAnchor) ?? rows[0];
        GameList.ScrollIntoView(target);
        GameList.UpdateLayout();
        // Focus can cause another layout pass. Suspend the callback while it runs.
        libraryFocusGeneration = -1;
        if (GameList.ContainerFromItem(target) is not Control container ||
            !container.Focus(FocusState.Programmatic))
        {
            libraryFocusGeneration = renderGeneration;
        }
    }
```

The second attempt runs once through `DispatcherQueue`; if the container is
still not realized, the restore gives up quietly (spec: Error handling).

- [ ] **Step 5: Build and run the unit suites**

Run:
```bash
stage; s 'cd /d E:\work\desktop-guides\t05-3 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release && dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release'
```
Expected: `Build succeeded.` with 0 errors; Core 301 passed; Infrastructure 429 passed.

- [ ] **Step 6: Commit** (show the message to the user first)

```bash
git add src/DesktopGuides.Production/ShellWindow.xaml.cs
git commit -m "feat(shell): focus the opened game row on Back to Library (T05.3)"
```

---
### Task 6: Spec rulings, test docs and traceability

**Files:**
- Modify: `docs/p1/t05-3-stable-navigation-design.md` (Status line; Library page and Game page sections; Testing → Installed smoke)
- Modify: `docs/p1/e2e-testing.md` (scenario table ~244-255)
- Modify: `docs/p1-technical-design.md` (T05.3 bullet ~537)
- Modify: `docs/work-breakdown.md` (S05 section ~169-190)

**Interfaces:**
- Consumes: phase names from Task 3; rulings 1-7 above.
- Produces: nothing for code. `docs/progress.md` and the
  `docs/p1/implementation-plan.md` T05.3 paragraph are written after CI
  passes (verification record) and after merge ("mark merged" commit on the
  next branch), not in this task.

No TDD: documentation only.

- [ ] **Step 1: Bring the spec in line with the rulings**

In `t05-3-stable-navigation-design.md`:

- Status: `Status: design approved in brainstorming on 1 October 2026;
  implementation planned in [t05-3-stable-navigation-plan.md](t05-3-stable-navigation-plan.md).`
- Library page, first bullet: replace "After a Library render," with "After
  Back to the Library," and the names `pendingGameFocus` /
  `TryRestoreGameFocus` with `libraryFocusPending` / `TryRestoreLibraryFocus`.
  Add: "Other routes to the Library leave focus alone; after a game removal
  focus stays on Add game (T04.3)."
- Library page, second bullet: "If no row is shown, it goes to
  `LibrarySearchInput`, or to Add game when search is disabled."
- Delete the "An in-place reload" bullet and add: "Nothing re-renders the
  Library while it is showing (import, rename and removal happen on the
  Game page), so there is no in-place reload to handle."
- Game page, first bullet: replace "replacing the `selectedGuideId` field"
  with "falling back to the list's own selection only when it still shows
  this game".
- Testing → Installed smoke, Stale refresh: replace the paragraph with
  "CI has no provider keys, so a refresh fails at once and can't be
  ordered against a navigation. The guard is a Review Focus item backed by
  the route check in the queued refresh action and the generation checks;
  the verification record says so."
- Testing → Installed smoke, add: "**Guide order.** Nothing writes
  `LastOpenedUtcMs` yet, so opening a guide doesn't reorder the list and
  the smoke covers both next-row and previous-row removal."

- [ ] **Step 2: Add the e2e scenario row**

In `docs/p1/e2e-testing.md`, insert after the Library search row:

```markdown
| Stable navigation | Seed Atlas Navigation Game (Atlas Third, Second and First Guide, newest import first), Beacon Navigation Game (Beacon Guide, the Resume guide) and Cobalt Other Game. In light and dark, with the Library filtered to `navigation` (`2 of 3 games match.`): Resume Beacon Guide, Back selects and focuses Beacon Guide, Back keeps the query and rows and focuses the Beacon row; Atlas then Back focuses the Atlas row without opening it; Atlas Second Guide's Reader then Back selects and focuses it, and Settings then Back keeps it; the Library item, Beacon, Back (Beacon row focused) and Back return to Atlas with Second selected; removing Second selects and focuses First, removing First selects and focuses Third, and Back keeps the query with the Atlas row focused; renaming Beacon to Beacon Renamed Game hides it under the query and Back focuses the Atlas row; no non-loopback TCP connection. Each theme gets a fresh seed. | T05.3, TR05.1, TR11.3 |
```

Append to the Library catalog row: "Back from the Ctrl+Down game focuses its
row, and Back from the last game focuses the last row." Append to the
Library search row: "Back focuses the Zeta row." Append to the Import
publication row: "With the Library filtered to `Import Test`, Back after the
import keeps the query and focuses the Import Test Game row." Add `T05.3` to
the task column of those three rows.

- [ ] **Step 3: Note the anchors in the technical design**

Append to the T05.3 bullet in `docs/p1-technical-design.md`:

```markdown
  The navigator keeps an optional anchor ID with each back-stack entry
  (the focused game on the Library, the selected guide on a Game), and
  `ListAnchor.Resolve` picks the nearest surviving row by its old position.
```

- [ ] **Step 4: Record the follow-ups**

Under the S05 task list in `docs/work-breakdown.md`, after T05.3, add:

```markdown
  Follow-ups recorded by T05.3: keyboard and mouse Back (Alt+Left,
  XButton1), a limit on the back stack's size, guide rename selection once
  guides can be renamed, and keeping the Library query across a relaunch.
```

- [ ] **Step 5: Check the docs**

Run: `git diff --stat docs/ && grep -n "pendingGameFocus\|TryRestoreGameFocus\|in-place reload" docs/p1/t05-3-stable-navigation-design.md || echo spec-clean`
Expected: four doc files changed; `spec-clean`.

- [ ] **Step 6: Commit** (show the message to the user first)

```bash
git add docs/p1/t05-3-stable-navigation-design.md docs/p1/e2e-testing.md docs/p1-technical-design.md docs/work-breakdown.md
git commit -m "docs(p1): record T05.3 rulings, smoke phases and follow-ups"
```

---

## After the tasks

1. Final whole-branch review on the most capable model, with the Review
   Focus list above and the ledger's `Ruling:` lines.
2. With the user's OK, push and open a draft PR: target T05.3; prerequisites
   T04.2 (PR #25), T05.2 (PR #24), T06.3 (PR #19), T15.3 (PR #22), all
   merged; outcome as in the spec; light and dark `library-focus-restored`
   screenshots as absolute blob `?raw=true` URLs.
3. When CI `production-shell-ui` passes, add the T05.3 verification record
   to the spec (each phase against the exit criteria, the stale-refresh
   Review Focus note, the run ID) and the progress and implementation-plan
   rows.
