# T21.2 image maps PR c (the Maps button and pop-up) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A reader partway through a walkthrough glances at a map without
leaving the guide. Every reader's toolbar gains **Maps** when the game has
a map, and Alt+M opens it too. It lists the game's maps by title, and
hovering, focusing or clicking a name shows that map, shrunk to fit,
beside the list. Pointer movement never closes the list or the map.
**Open map** opens it on its own. After this PR, image maps are listed as
supported.

**Architecture:**
- **Core** gets:
  - the shortcut: `ShortcutCommand.ShowMaps` (Alt+M, in the Reader);
  - `MapsMenu`: the list's items, from the game's guide summaries;
  - `MapPreview`: the preview's decode size and the pop-up's size within
    the window.
- **Production** gets:
  - a `MapsPane` control: the list and the preview side by side, inside
    one `Flyout` on a new toolbar button, **Maps**. Light dismiss closes
    it on a click outside, Esc or the button. Pointer movement never
    does, because the map is inside the flyout, not a second pop-up.
  - **Preview copies:**
    - a large map's preview is its cache's overview (PR b), at most 2048
      pixels, built at import or on demand;
    - a small map's is its own file, decoded to at most 2048 pixels.
  - **The shell:**
    - loads the game's maps when a reader opens;
    - shows Maps when there are any;
    - opens a map from the pane as any guide switch does, so the text
      guide's place is saved and Back returns to the game page.

**Tech Stack:** .NET 10, xUnit, WinUI 3 (`Flyout`, `ListView`),
PowerShell UI Automation.

**Spec:** [t21-2-image-maps-design.md](t21-2-image-maps-design.md)
(approved 19 October 2026), section "The Maps button and pop-up" and the
`maps-popup` and `maps-keyboard` rows of "Tests".

## Global Constraints

- **The Maps button:**
  - an `AppBarButton`, primary, `x:Name="ShowMaps"`, AutomationId
    `ShowMaps`;
  - label "Maps", tooltip "Maps (Alt+M)", the map glyph `\uE826`;
  - after the reader's own commands, with `DynamicOverflowOrder="2"`;
  - visible on every reader (TXT, HTML, PDF and a map) when the game has
    at least one map, and collapsed otherwise.
- **The pane** is the flyout's whole content:
  - a list of maps, AutomationId `MapsList`, named "Maps", 280 DIPs wide,
    by title as `MapPresentation.Sort`;
  - beside it, the preview:
    - an `Image`, AutomationId `MapsPreviewImage`, named with the shown
      map's title;
    - the map's title as a heading;
    - its facts ("PNG · 640 × 480");
    - **Open map**, AutomationId `OpenMapFromMaps`.
  - Before any map is shown, the preview area says "Point to a map to see
    it.".
- **The pane's size:**
  - `MapPreview.PaneSize(windowWidth, windowHeight)` is the window less
    48 DIPs on each side, within 1200 × 900, and at least 520 × 320;
  - the preview fits the rest, beside the list, keeping the map's aspect.
- **What shows a map:** the pointer entering a row, keyboard focus on a
  row, or a click on a row. Each swaps the preview. Nothing clears it.
  When the pointer leaves, the last map stays.
- **What closes the pane:**
  - Esc;
  - a click outside it (the flyout's light dismiss);
  - the Maps button, or Alt+M, again;
  - **Open map** or Enter on a row, which also open that map.

  `LightDismissOverlayMode` is `Off`, so the guide stays visible beside
  it.
- **Opening a map from the pane** calls the shell's `OpenGuideAsync(mapId)`.
  - The current reader closes as on any guide switch, saving its place.
  - Back from the map returns to the game page.
  - Opening the map that's already open closes the pane and does
    nothing else.
- **The keyboard:**
  - Alt+M opens the pane with focus on the first row (or the open map's
    row, in the map reader), and closes it when it's open;
  - Up and Down move through the rows, and the focused row's map shows;
  - Enter opens the focused map;
  - Esc closes, and focus returns to the reader view.
- **Previews:**
  - **Decode width:** `MapPreview.DecodeWidth(w, h)`, at most 2048 on the
    longer side, or 0 (natural size) when the map is already that small.
  - **Large maps** (`!MapPyramid.Small`): the overview from
    `MapCacheQueue.Ensure(map, path, front: true).Overview`. While it's
    building, the preview says "Preparing the map…".
  - **Small maps:** the file itself, decoded to `DecodeWidth`.
  - **Kept in memory:** the pane keeps the last 8 previews it showed.
  - **A preview that fails** says "This map can't be shown." The pane
    stays open.
- **Copy:** "Maps", "Maps (Alt+M)", "Point to a map to see it.",
  "Preparing the map…", "This map can't be shown.", "Open map". Shortcut
  description: "Show the game's maps".
- **No reading activity:** showing a preview saves no place and records
  no open. Only **Open map** opens a map.
- **Shell rules:** no `cd` in local shell commands. PowerShell stays
  ASCII-only and runs under 5.1 and 7.
- **Visual direction:** the app's existing Fluent styles; the flyout uses
  the default `FlyoutPresenter` with its size constraints lifted. No new
  styles.

## Review Focus

1. **A game with one map** shows Maps, and its single row works with
   hover, the keyboard and Open map. Pinned by Task 1's
   `AGameWithOneMapHasAMenu`.
2. **A map added or removed while a guide is open** (another window
   isn't possible, but an import from the game page then Back to the
   guide is) shows the current list the next time the pane opens: the
   list is reloaded on each opening (Task 3). Pinned by Task 4's
   `maps-popup`, which opens the pane twice, after `maps-reader` removed
   two maps.
3. **A very wide or very tall map** (the 20000 × 400 strip) fits the
   preview area without overflowing the pane. Pinned by Task 1's
   `AStripFitsThePreviewByWidth`.
4. **A small window** (the CI window, about 770 × 520) still shows the
   list and a usable preview. Pinned by Task 1's
   `ASmallWindowKeepsTheMinimumPane`.
5. **Opening from the pane while the text guide is mid-scroll** saves the
   guide's place as Back would. Pinned by Task 4's `maps-popup`, which
   checks the TXT place after opening a map and coming back.

## Host commands

As in the [PR a plan](t21-2-image-maps-import-plan.md#host-commands), with
the staging folder `E:\work\desktop-guides\t21-2p` and logs
`E:\work\t21-2p-*.log`.

Installed runs: `gh workflow run windows-ci.yml --ref feat/p2-t21-2-image-maps-popup -f shell-scope=navigation -f shell-lane=msix` (and `portable`).

---

### Task 1: Core: the shortcut, the menu and the preview sizes

**Files:**
- Modify: `src/DesktopGuides.Core/Navigation/KeyboardShortcuts.cs` (`ShortcutCommand.ShowMaps`; the row `(Reader, "Alt+M", ShowMaps, "Show the game's maps")`, after Show bookmarks)
- Create: `src/DesktopGuides.Core/Maps/MapsMenu.cs`, `src/DesktopGuides.Core/Maps/MapPreview.cs`
- Test: `tests/DesktopGuides.Core.Tests/KeyboardShortcutsTests.cs`, `ShortcutRoutingTests.cs`, `MapsMenuTests.cs` (new), `MapPreviewTests.cs` (new), `ShellNavigatorTests.cs`

**Interfaces:**
- Produces:
  - `ShortcutCommand.ShowMaps`;
  - `public sealed record MapsMenuItem(Guid Id, string Title, string Facts, int PixelWidth, int PixelHeight)`;
  - `MapsMenu.Items(IEnumerable<GuideSummary> summaries) -> IReadOnlyList<MapsMenuItem>`:
    the `Image` guides only, in `MapPresentation.Sort` order, with
    `Facts` = `MapPresentation.Facts`-style "PNG · 640 × 480" (kind and
    size, no file size);
  - `MapsMenu.FirstFocus(IReadOnlyList<MapsMenuItem> items, Guid? openGuideId) -> int`:
    the open map's row, or 0;
  - `MapPreview.DecodeWidth(int width, int height) -> int`: 0 when the
    longer side is at most 2048, or the width that makes the longer side
    2048;
  - `MapPreview.PaneSize(double windowWidth, double windowHeight) -> (double Width, double Height)`;
  - `MapPreview.ImageSize(double areaWidth, double areaHeight, int mapWidth, int mapHeight) -> (double Width, double Height)`,
    the largest size with the map's aspect that fits the area.

- [ ] **Step 1: Write the failing tests.**

```csharp
    // KeyboardShortcutsTests.TheTableMatchesTheSpec gains, after Show bookmarks:
    //   (ShortcutContext.Reader, "Alt+M", ShortcutCommand.ShowMaps, "Show the game's maps"),
    // ShortcutRoutingTests: ShowMaps runs in the Reader and nowhere else, and not while a dialog is open.

    [Fact]
    public void TheMenuListsOnlyMapsByTitle()
    {
        IReadOnlyList<MapsMenuItem> items = MapsMenu.Items([Txt("Walkthrough"), Map("catacombs", 64, 48), Map("Castle floor 1", 640, 480)]);
        Assert.Equal(["Castle floor 1", "catacombs"], items.Select(item => item.Title));
        Assert.Equal("PNG · 640 × 480", items[0].Facts);
    }

    // Review Focus 1.
    [Fact]
    public void AGameWithOneMapHasAMenu() =>
        Assert.Single(MapsMenu.Items([Txt("Walkthrough"), Map("castle", 640, 480)]));

    [Fact]
    public void FocusStartsOnTheOpenMap()
    {
        IReadOnlyList<MapsMenuItem> items = MapsMenu.Items([Map("a", 10, 10), Map("b", 10, 10)]);
        Assert.Equal(1, MapsMenu.FirstFocus(items, items[1].Id));
        Assert.Equal(0, MapsMenu.FirstFocus(items, Guid.NewGuid()));
        Assert.Equal(0, MapsMenu.FirstFocus(items, null));
    }

    [Theory]
    [InlineData(2048, 1000, 0)]
    [InlineData(15456, 10304, 2048)]
    [InlineData(400, 20000, 41)]
    public void APreviewDecodesToAtMost2048(int w, int h, int width) =>
        Assert.Equal(width, MapPreview.DecodeWidth(w, h));

    [Fact]
    public void ThePaneFillsALargeWindowUpToItsCap() =>
        Assert.Equal((1200.0, 900.0), MapPreview.PaneSize(2560, 1400));

    // Review Focus 4.
    [Fact]
    public void ASmallWindowKeepsTheMinimumPane()
    {
        Assert.Equal((672.0, 424.0), MapPreview.PaneSize(768, 520));
        Assert.Equal((520.0, 320.0), MapPreview.PaneSize(400, 300));
    }

    // Review Focus 3.
    [Fact]
    public void AStripFitsThePreviewByWidth() =>
        Assert.Equal((800.0, 16.0), MapPreview.ImageSize(800, 600, 20000, 400));

    [Fact]
    public void ASmallMapIsShrunkToFitOrShownWhole() =>
        Assert.Equal((400.0, 300.0), MapPreview.ImageSize(800, 300, 640, 480));

    // ShellNavigatorTests: Back from a map opened while a guide was open is the game page.
    [Fact]
    public void BackFromAMapOpenedFromAGuideIsTheGamePage()
    {
        ShellNavigator navigator = new();
        Guid game = Guid.NewGuid(), guide = Guid.NewGuid(), map = Guid.NewGuid();
        navigator.OpenGame(game);
        navigator.OpenReader(guide, game);
        navigator.OpenReader(map, game);
        Assert.True(navigator.GoBack());
        Assert.Equal(new GameRoute(game), navigator.Current);
    }
```

  `Txt(title)` and `Map(title, w, h)` are local helpers that build
  `GuideSummary` values the way `CatalogPresentationTests` does. If
  `ShellNavigator`'s constructor needs arguments, follow its existing
  tests.
- [ ] **Step 2: Run them.** `t DesktopGuides.Core.Tests "FullyQualifiedName~Shortcut|FullyQualifiedName~MapsMenu|FullyQualifiedName~MapPreview|FullyQualifiedName~ShellNavigator"`.
  Expected: compile errors, and the shortcut table failing.
- [ ] **Step 3: Implement.** The pane size is
  `Clamp(window − 96, minimum, cap)` per side. `ImageSize` scales by
  `min(areaW / w, areaH / h)` and never above 1: a small map isn't
  enlarged.
- [ ] **Step 4: Run them, then the whole Core suite.** Expected: all
  pass. If `NoTwoShortcutsShareKeysWhereBothCanRun` objects, Alt+M is
  taken: stop and rule.
- [ ] **Step 5: Commit.** "feat(p2): the Maps shortcut, menu and preview sizes".

### Task 2: Production: the Maps pane and its previews

**Files:**
- Create: `src/DesktopGuides.Production/MapsPane.xaml` and `.xaml.cs`
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml` and `.xaml.cs` (the `ShowMaps` button and its `Flyout`)

**Interfaces:**
- Consumes: `MapsMenu`, `MapsMenuItem`, `MapPreview` (Task 1); `MapCacheQueue.Ensure`, `MapPyramid` (PR b).
- Produces:
  - `MapsPane : UserControl`, with:
    - `void Show(IReadOnlyList<MapsMenuItem> items, Guid? openGuideId)`,
      which fills the list and focuses `MapsMenu.FirstFocus`;
    - `event EventHandler<Guid>? OpenRequested`;
    - `Func<MapsMenuItem, CancellationToken, Task<ImageSource>>? LoadPreview`,
      set by the shell.
  - `ReaderToolbar`:
    - `void SetMaps(bool any)`, which shows or collapses `ShowMaps`;
    - `MapsPane Maps { get; }`;
    - `bool MapsOpen`, `void OpenMaps()`, `void CloseMaps()`, and
      `event EventHandler? MapsOpening` (raised before the flyout shows,
      so the shell can load the list).
- **Behaviour:**
  - A row's `PointerEntered`, `GotFocus` and `ItemClick` show its
    preview. A newer request wins: a slower preview that finishes later
    is dropped.
  - Enter on a row and **Open map** raise `OpenRequested` and close the
    flyout.
  - The flyout's presenter `MaxWidth` and `MaxHeight` are lifted, and its
    content is sized from `MapPreview.PaneSize` of the window each time it
    opens.
  - `LightDismissOverlayMode="Off"`, `ShowMode="Standard"`.
  - When the flyout closes, focus returns to the reader view. The
    toolbar's existing restore of focus after a command is reused if it
    fits.

- [ ] **Step 1: Build the pane and the button.** No unit tests here: the
  behaviour is pinned by Task 4's installed modes, and its arithmetic by
  Task 1. `build` → 0 warnings, 0 errors.
- [ ] **Step 2: Commit.** "feat(p2): the Maps pane on the reader toolbar".

### Task 3: The shell: the game's maps, Alt+M and opening a map

**Files:**
- Create: `src/DesktopGuides.Production/ShellWindow.Maps.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.Keyboard.cs` (`AddShortcut(VirtualKey.M, ShortcutCommand.ShowMaps, ToggleMapsFromKeyboard, VirtualKeyModifiers.Menu)`)
- Modify: each reader's open path (`OpenTextGuideAsync`'s caller, `OpenHtmlGuideAsync`, `OpenPdfGuideAsync`, `OpenMapAsync`), or the one place after `ReaderActions.SetSession`, to call `RefreshMapsButtonAsync(gameId)`

**Interfaces:**
- Consumes: Task 1 and Task 2's members; `library.ListGuideSummariesAsync(gameId)`; `OpenGuideAsync(Guid)`.
- **Behaviour:**
  - **Opening a reader:**
    - after `SetSession`, the shell loads the game's summaries;
    - it calls `ReaderActions.SetMaps(MapsMenu.Items(...).Count > 0)`;
    - a newer render cancels an older load.
  - **`MapsOpening`** reloads the list. A map imported or removed on the
    game page since the reader opened shows (Review Focus 2). Then
    `Maps.Show(items, readerGuideId)`.
  - **Previews:**
    - `LoadPreview` resolves the map's file with `libraryPaths`;
    - a large map waits for `mapCache.Ensure(map, path, front: true).Overview`
      and loads that file into a `BitmapImage`;
    - a small map decodes its file with `DecodePixelWidth = MapPreview.DecodeWidth`;
    - both read through a `FileStream` closed after `SetSourceAsync`,
      so no handle stays on the file.
  - **`OpenRequested`:**
    - if the id is the open guide, nothing more happens;
    - otherwise `await OpenGuideAsync(id)`. The guide switch closes the
      reader, which flushes its place.
  - **Alt+M** toggles the pane when the reader is shown and Maps is
    visible. Otherwise it does nothing and isn't handled.
  - Leaving the reader closes the pane.

- [ ] **Step 1: Implement and build.** `build` → 0 warnings, 0 errors.
- [ ] **Step 2: Run Core and Infrastructure.** Expected: all pass.
- [ ] **Step 3: Commit.** "feat(p2): Maps on every reader, and Alt+M".

### Task 4: The installed `maps-popup` and `maps-keyboard` modes

**Files:**
- Modify: `tools/p1/windows_shell_foreground_probe.cs` (make `MoveTo` public as `Hover(int x, int y)`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the two modes, in the top-level mode chain, and in the `ValidateSet`)
- Modify: `tools/p1/windows_shell_install.ps1` (run both after `maps-reader` in `Run-MapReaderScenario`, each in its own launch with the `ProgressDiagnostics` gate)

**Interfaces:**
- The library is `seed-maps-reader`'s, after `maps-reader`: "Walkthrough"
  (TXT), "big map" (with its cache) and "castle floor 1" (small).
- **`maps-popup`:**
  1. Open "Walkthrough" and page down twice. Assert the top line moved
     off the first line; if the fixture is too short to page, give
     `seed-maps-reader`'s walkthrough a longer TXT fixture. Wait 1.5 s,
     then record the top line and the progress diagnostics' save count.
  2. Click `ShowMaps`. Assert:
     - `MapsList` is visible with the rows "big map" and "castle floor 1";
     - the preview reads "Point to a map to see it.".
  3. Hover "castle floor 1". Wait for `MapsPreviewImage` named "castle
     floor 1".
  4. Hover "big map". Wait for it to be named "big map".
  5. Move the pointer onto the image's centre, across it to its right
     edge, then outside both, over the guide's text. After each move,
     assert `MapsList` is still visible and the image is still named "big
     map". Save the screenshot `maps-popup`.
  6. Press Esc. Assert:
     - `MapsList` is hidden;
     - the top line and the save count are unchanged (no place saved
       while glancing).
  7. Click `ShowMaps` again, which reloads the list. Click "castle floor
     1", then `OpenMapFromMaps`. Assert:
     - `MapImageView` is named "castle floor 1";
     - `ShowMaps` is visible on the map reader too.
  8. Go Back. Assert `GameHeading` is "Map Reader Game".
  9. Open "Walkthrough" again. Assert its top line is step 1's (the
     place saved on the switch, Review Focus 5).
- **`maps-keyboard`:**
  1. Open "Walkthrough", then press Alt+M. Assert focus is within
     `MapsList` on "big map", and the preview is named "big map".
  2. Press Down. Assert focus is on "castle floor 1", and the preview
     follows.
  3. Press Esc. Assert `MapsList` is hidden and focus is within the
     reader.
  4. Press Alt+M, Down, Enter. Assert `MapImageView` is named "castle
     floor 1".
  5. Press Alt+M. Assert focus is on "castle floor 1", the open map's
     row. Press Alt+M again, and assert the pane closed.
  6. Go Back.
- **After both,** `Run-MapReaderScenario`'s `describe-maps` checks run as
  today:
  - "castle floor 1" now has a place (it was opened), with no estimate
    and no open time;
  - Resume is still "Walkthrough".

- [ ] **Step 1: Write both modes and the scenario lines.** Parse both
  scripts on the host and compile the probe.
- [ ] **Step 2: Dispatch the `navigation` scope** on both lanes.
  Expected: both new modes and every existing mode pass. Debug any
  failure with systematic-debugging; a code fault gets a test that
  fails first where one can be written.
- [ ] **Step 3: Copy the screenshot** `maps-popup.png` from the MSIX
  artifact into `docs/p2/evidence/ci/t21-2/`, and look at it.
- [ ] **Step 4: Commit.** "test(p2): the installed maps-popup and maps-keyboard modes".

### Task 5: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t21-2-image-maps-design.md` (Status; a "PR c verification record")
- Modify: `docs/p2/results.md` (#137's row)
- Modify: `docs/p2/implementation-plan.md`, `docs/progress.md`, and wherever multi-page HTML was listed as supported in #135's records, to list image maps as supported

- [ ] **Step 1: The full suites and the build** on the host: Core,
  Infrastructure, the Production build with 0 warnings, and the script
  parse check.
- [ ] **Step 2: The final review.** Run the whole-branch review first.
  Its fixes land before the records.
- [ ] **Step 3: The records.**
  - **The design's Status:** add "PR b merged (#137). PR c (the Maps
    pop-up) in review; image maps are supported."
  - **The "PR c verification record":** the tests, the `navigation`
    dispatches on both lanes, the screenshot, the review, and every
    ruling, including this plan's deviations.
  - **`results.md`:** #137's row: 2026-10-19, `068c610`, T21.2 maps
    (PR b), its final PR run 39233920937, Core 1498, Infra 917, "Zoom,
    pan, levels and tiles, and the view place. Two removal defects found
    by the PR runs, fixed before merge." Check the counts against the run
    before writing them.
  - **The status pages:** T21.2 complete: multi-page HTML and image maps
    are supported (#133–#138). The `progress.md` "Next" names the next
    P2 task from `implementation-plan.md`.
- [ ] **Step 4: Commit, push, open the PR, and wait for its run.**
  - **Commit:** "docs(p2): record image maps PR c".
  - **The PR:** "T21.2 image maps PR c: the Maps button and pop-up",
    based on `main`. It names:
    - the task;
    - the prerequisites: image maps PR a (#136) and PR b (#137), both
      merged;
    - the outcome.

    It includes the screenshot, the dispatches, every ruling and the
    deferred minors.
  - **Expected:** every job passes. Record the run in the PR, then stop
    for the user's merge decision.

## Deviations from the design

1. **The list and the map share one flyout,** rather than a pop-up
   beside a separate list. Pointer movement can't close either, since
   there is nothing for the pointer to leave. Light dismiss gives Esc and
   click outside, and a second Maps click closes it.
2. **A small map's preview is its own file,** decoded to at most 2048
   pixels, rather than a stored copy. It's at most 4096 on a side, so it
   decodes quickly, and nothing extra is kept on disk. A large map's
   preview is its PR b overview, which an import already builds.
3. **The pane keeps its last 8 previews in memory** for its lifetime, a
   limit the design didn't name.
4. **Both installed modes run in `Run-MapReaderScenario`,** on the
   library `maps-reader` leaves.

## Traceability

| Design item | Task |
| --- | --- |
| Maps on every reader's toolbar when the game has a map; the map reader too | 2, 3 |
| A list of the game's maps, by title | 1, 2 |
| Hover or focus shows the map beside the list, shrunk to fit; another name swaps it | 1, 2, 4 |
| Pointer movement never closes; the last map stays; Esc, the button, a click outside, or opening closes | 2, 4 |
| Clicking a name shows it; Open map opens it; the guide's place is saved; Back to the game page | 1, 3, 4 |
| Alt+M, Up and Down, Enter, Esc; the image's name is the title | 1, 3, 4 |
| Preview copies at most 2048, shown at once | 1, 3 |
| `maps-popup`, `maps-keyboard` | 4 |
| Image maps listed as supported | 5 |
