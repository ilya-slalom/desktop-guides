# T05.2 Library Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Library gets a title search over game and guide titles. It
ignores case, accents and width, and shows distinct loading, empty-library
and no-results views, with a visible Clear search action.

**Architecture:** Core gains `LibrarySearchMatch` and a static
`LibrarySearch` that filters the summaries the Library already loads, in
memory. `LibraryGameSummary` gains `GuideTitles`. The repository fills it
from a second statement in the same read transaction.
`CatalogPresentation.GameFacts` appends `Guide: {title}` for guide-only
matches. The shell caches the summaries, filters them on a 200 ms debounce,
and switches between four exclusive views.

**Tech Stack:** .NET 10, WinUI 3, Microsoft.Data.Sqlite, xUnit, the
PowerShell 5.1 UI Automation harness.

**Spec:** `docs/p1/t05-2-library-search-design.md`

**Target:** T05.2 (TR05.1, TR05.2). **Prerequisite:** T05.1 (PR #23),
merged.

## Global Constraints

- No schema change and no new package.
- Search reads only the summaries already loaded. It never opens guide
  files, never queries SQLite per keystroke, and never calls a provider.
- Matching: `CultureInfo.InvariantCulture.CompareInfo.IndexOf(title,
  query.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace |
  CompareOptions.IgnoreWidth) >= 0`. A null or blank query matches
  everything.
- Filtering keeps the T05.1 activity order.
- `GuideTitles` is in `Title`, then `Id` order (ordinal SQLite collation).
- Copy, verbatim:
  - box: Name `Search library`, placeholder `Search games and guides`;
  - button: `Clear search`;
  - loading: `Loading library…`;
  - no results: `No games or guides match "{query}".` and
    `Check the spelling or clear the search.`;
  - guide fact: `Guide: {title}`;
  - announcements: `{n} of {total} games match.` and `No games match.`.
- AutomationIds: `LibrarySearchInput`, `LibrarySearchClear`,
  `LibraryLoading`, `LibraryNoResults`.
- Unread guides say `Not started`; `0%` appears only after a real estimate.
- UI tests assert only what app code controls.
- PowerShell scripts stay ASCII-only. Build non-ASCII strings from `[char]`
  code points.
- Never print, copy or log provider credential values.

## Rulings against the spec

Task 5 records these rulings in the design's verification record.

1. **Task 1 passes `[]` for `GuideTitles` from the repository,** so the
   build stays green. Task 2 replaces it with real titles through TDD.
2. **Every `TextChanged` restarts the debounce, not only `UserInput`.**
   It is unclear which reason UIA `SetValue` reports. Applying is skipped
   when the trimmed text equals the last applied query, so redundant
   events cost nothing.
3. **The loading view shows only on the first load,** while no summaries
   are cached. Later renders keep the current view until the new
   summaries arrive, which avoids a flicker after every import.
4. **The render applies the filter without an announcement** and still
   ends with `Library ready.`, so every existing `Wait-Status` stays
   valid. Only typing, Enter and Clear announce.
5. **The debounce tick does nothing unless the Library is the current
   route,** and each render stops the timer first.
6. **A render error collapses the loading view,** so the spinner never
   lingers next to the error status.
7. **The smoke AutomationIds sit on TextBlocks** (`LibraryLoading`,
   `LibraryNoResults`). StackPanel and Border have no UIA peer.
   `LibraryLoadingState` and `LibraryNoResultsState` stay as `x:Name`s.
8. **`Wait-Status 'Library ready.'` also asserts the loading view is gone,**
   which implements "every mode that waits for Library ready".
9. **The `catalog` mode's Tab steps gain one Tab,** because the search box
   now sits between Add game and the first row.
10. **`POKEMON` is typed with real keystrokes;** the non-ASCII queries go
    through ValuePattern, because `SendKeys` cannot type them reliably.
11. **Clear search shows only when the query isn't blank and the view is
    no-results or the list.** The loading and empty views never show it,
    because the box is disabled there.

## Review Focus

1. **A query typed before the first load finishes.** The box is disabled
   while loading, so no query can exist yet; the first apply uses the
   box's text anyway. Covered by the empty-mode disabled check.
2. **A title containing the query only after folding** (`Pokémon` vs
   `POKEMON`, `ＸＩ` vs `XI`). Covered by `LibrarySearchTests` and the
   smoke's fullwidth query.
3. **Removing or importing while a query is active.** The render reapplies
   the kept query. Covered by the smoke's Back step (steps 7–8).
4. **A game whose title and guide both match.** The row must not name the
   guide. Covered by `AGameTitleMatchNamesNoGuide`.
5. **Stale artwork loads after filtering.** `ApplyLibrarySearch` calls
   `gameArtwork.CancelAll()` before replacing the items. Checked by the
   final review; the smoke seed has no artwork.

## Host commands

Builds and tests run on `pcsx2-win` over SSH:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){ s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t05-2) { Remove-Item -Recurse -Force E:\work\desktop-guides\t05-2 }; New-Item -ItemType Directory E:\work\desktop-guides\t05-2 | Out-Null"'; COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t05-2'; }
```

- Core tests: `stage && s 'cd /d E:\work\desktop-guides\t05-2 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
- Infrastructure tests: the same with
  `tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj`.
- One class: append `--filter "FullyQualifiedName~<Class>"`.
- Production build: `dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64`
- Seed build: `dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release`
- PowerShell parse check: scp a temporary `.ps1` that runs
  `[System.Management.Automation.Language.Parser]::ParseFile` over both
  scripts, and run it with `-File`. Inline `-Command` breaks under SSH
  quoting.

---

## Task 1: Core search and facts

**Files:**
- Modify: `src/DesktopGuides.Core/Library/CatalogSummaries.cs`
- Create: `src/DesktopGuides.Core/Library/LibrarySearch.cs`
- Modify: `src/DesktopGuides.Core/Library/CatalogPresentation.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (construction site only, ruling 1)
- Modify: `src/DesktopGuides.Production/LibraryGameItem.cs`,
  `src/DesktopGuides.Production/ShellWindow.xaml.cs` (construction only)
- Create: `tests/DesktopGuides.Core.Tests/LibrarySearchTests.cs`
- Modify: `tests/DesktopGuides.Core.Tests/CatalogPresentationTests.cs`

**Interfaces:**
- Produces:
  - `record LibraryGameSummary(Game Game, int GuideCount, DateTimeOffset LastActivityUtc, IReadOnlyList<string> GuideTitles)`;
  - `record LibrarySearchMatch(LibraryGameSummary Summary, string? MatchedGuideTitle)`;
  - `static bool LibrarySearch.Matches(string title, string? query)`;
  - `static IReadOnlyList<LibrarySearchMatch> LibrarySearch.Filter(IReadOnlyList<LibraryGameSummary> games, string? query)`;
  - `CatalogPresentation.GameFacts(LibraryGameSummary summary, string? matchedGuideTitle = null)`;
  - `LibraryGameItem(LibrarySearchMatch match)`.

- [ ] **Step 1: Write the failing tests**

`tests/DesktopGuides.Core.Tests/LibrarySearchTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class LibrarySearchTests
{
    [Theory]
    [InlineData("The Legend of Zelda", "zELDA")]
    [InlineData("The Legend of Zelda", "legend OF")]
    [InlineData("Pokémon Crystal", "pokemon")]
    [InlineData("Pokemon Crystal", "Pokémon")]
    [InlineData("Ōkami HD", "OKAMI")]
    [InlineData("FF Tactics", "ＦＦ")]
    [InlineData("ＦＦ Tactics", "ff")]
    [InlineData("ドラクエXI", "ドラクエ")]
    [InlineData("Ведьмак 3", "ВЕДЬМАК")]
    [InlineData("The Legend of Zelda", "  zelda  ")]
    public void MatchingIgnoresCaseAccentsAndWidth(string title, string query)
    {
        Assert.True(LibrarySearch.Matches(title, query));
    }

    [Theory]
    [InlineData("The Legend of Zelda", "Mario")]
    [InlineData("Pokémon Crystal", "pokemons")]
    [InlineData("ドラクエXI", "ドラゴン")]
    public void ADifferentTitleDoesNotMatch(string title, string query)
    {
        Assert.False(LibrarySearch.Matches(title, query));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankQueryMatchesEverything(string? query)
    {
        Assert.True(LibrarySearch.Matches("Anything", query));
    }

    [Fact]
    public void AGameTitleMatchNamesNoGuide()
    {
        LibraryGameSummary zelda = Summary("Zelda", "Zelda Walkthrough");

        LibrarySearchMatch match = Assert.Single(LibrarySearch.Filter([zelda], "zelda"));

        Assert.Same(zelda, match.Summary);
        Assert.Null(match.MatchedGuideTitle);
    }

    [Fact]
    public void AGuideOnlyMatchNamesTheFirstMatchingGuide()
    {
        LibraryGameSummary game = Summary("Zeta", "Achievements", "Main Walkthrough", "Side Walkthrough");

        LibrarySearchMatch match = Assert.Single(LibrarySearch.Filter([game], "walkthrough"));

        Assert.Equal("Main Walkthrough", match.MatchedGuideTitle);
    }

    [Fact]
    public void FilteringKeepsTheInputOrderAndDropsNonMatches()
    {
        LibraryGameSummary first = Summary("Zeta Quest");
        LibraryGameSummary other = Summary("Mario");
        LibraryGameSummary last = Summary("Alpha", "Quest Maps");

        IReadOnlyList<LibrarySearchMatch> matches = LibrarySearch.Filter([first, other, last], "quest");

        Assert.Equal([first, last], matches.Select(match => match.Summary));
    }

    [Fact]
    public void ABlankQueryKeepsEveryGameWithoutAGuide()
    {
        LibraryGameSummary first = Summary("Zeta", "Guide");
        LibraryGameSummary second = Summary("Alpha");

        IReadOnlyList<LibrarySearchMatch> matches = LibrarySearch.Filter([first, second], " ");

        Assert.Equal([first, second], matches.Select(match => match.Summary));
        Assert.All(matches, match => Assert.Null(match.MatchedGuideTitle));
    }

    [Fact]
    public void AnEmptyLibraryHasNoMatches()
    {
        Assert.Empty(LibrarySearch.Filter([], "zelda"));
    }

    private static LibraryGameSummary Summary(string title, params string[] guides) =>
        new(new Game(Guid.NewGuid(), title, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null),
            guides.Length, DateTimeOffset.UnixEpoch, guides);
}
```

In `CatalogPresentationTests.cs`, add `, []` to the two
`new LibraryGameSummary(GameWith(...), guides, Now)` calls, and add:

```csharp
[Fact]
public void AGuideMatchEndsTheGameFacts()
{
    LibraryGameSummary summary = new(GameWith("PC"), 2, Now, ["Complete Walkthrough", "Maps"]);

    IReadOnlyList<CatalogFact> facts = CatalogPresentation.GameFacts(summary, "Complete Walkthrough");

    Assert.Equal(["PC", "Manual", "2 guides", "Guide: Complete Walkthrough"], Labels(facts));
    Assert.Equal("PC, Manual, 2 guides, Guide: Complete Walkthrough", CatalogPresentation.AccessibleText(facts));
}

[Fact]
public void NoGuideMatchLeavesTheFactsUnchanged()
{
    LibraryGameSummary summary = new(GameWith("PC"), 2, Now, ["Complete Walkthrough", "Maps"]);

    Assert.Equal(["PC", "Manual", "2 guides"], Labels(CatalogPresentation.GameFacts(summary, null)));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Core tests with `--filter "FullyQualifiedName~LibrarySearchTests|FullyQualifiedName~CatalogPresentationTests"`.
Expected: build FAIL — `LibrarySearch` and `LibrarySearchMatch` do not
exist, and `LibraryGameSummary` has no four-argument constructor.

- [ ] **Step 3: Write the minimal implementation**

In `CatalogSummaries.cs`:

```csharp
public sealed record LibraryGameSummary(
    Game Game, int GuideCount, DateTimeOffset LastActivityUtc,
    IReadOnlyList<string> GuideTitles);
```

`src/DesktopGuides.Core/Library/LibrarySearch.cs`:

```csharp
using System.Globalization;

namespace DesktopGuides.Core.Library;

public sealed record LibrarySearchMatch(LibraryGameSummary Summary, string? MatchedGuideTitle);

public static class LibrarySearch
{
    private const CompareOptions Options =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;

    public static bool Matches(string title, string? query)
    {
        string trimmed = query?.Trim() ?? string.Empty;
        return trimmed.Length == 0
            || CultureInfo.InvariantCulture.CompareInfo.IndexOf(title, trimmed, Options) >= 0;
    }

    public static IReadOnlyList<LibrarySearchMatch> Filter(
        IReadOnlyList<LibraryGameSummary> games, string? query)
    {
        List<LibrarySearchMatch> matches = [];
        foreach (LibraryGameSummary game in games)
        {
            if (Matches(game.Game.Title, query))
            {
                matches.Add(new(game, null));
                continue;
            }

            string? guide = game.GuideTitles.FirstOrDefault(title => Matches(title, query));
            if (guide is not null)
            {
                matches.Add(new(game, guide));
            }
        }

        return matches;
    }
}
```

In `CatalogPresentation.GameFacts`, add the parameter
`string? matchedGuideTitle = null` and, after the count label:

```csharp
if (matchedGuideTitle is not null)
{
    labels.Add($"Guide: {matchedGuideTitle}");
}
```

In `SqliteLibraryRepository.ListGameSummariesAsync`, pass `[]` as the
fourth argument (ruling 1).

`LibraryGameItem`:

```csharp
internal LibraryGameItem(LibrarySearchMatch match)
    : base(match.Summary.Game.Title, "\uE7FC",
        CatalogPresentation.GameFacts(match.Summary, match.MatchedGuideTitle))
{
    Game = match.Summary.Game;
}
```

In the Library case of `ShellWindow.RenderCurrentAsync`, build the items as
`summaries.Select(summary => new LibraryGameItem(new LibrarySearchMatch(summary, null)))`.
Task 3 replaces this.

- [ ] **Step 4: Run the tests to verify they pass**

Run: the same filtered Core tests, then the full Core and Infrastructure
suites and the Production build.
Expected: all PASS; the Production build succeeds with no new warnings.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Core src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs src/DesktopGuides.Production/LibraryGameItem.cs src/DesktopGuides.Production/ShellWindow.xaml.cs tests/DesktopGuides.Core.Tests
git commit -m "feat(core): filter Library summaries by game and guide title"
```

## Task 2: Repository guide titles

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (`ListGameSummariesAsync`, around line 257)
- Modify: `tests/DesktopGuides.Infrastructure.Tests/LibrarySummaryTests.cs`

**Interfaces:**
- Consumes: the four-argument `LibraryGameSummary` from Task 1.
- Produces: `ListGameSummariesAsync` fills `GuideTitles` with each game's
  own guide titles in `Title`, then `Id` order, or `[]`.

- [ ] **Step 1: Write the failing tests**

Add to `LibrarySummaryTests`:

```csharp
[Fact]
public async Task GuideTitlesAreEachGamesOwnInTitleOrder()
{
    Guid a = await GameAt("A", Base + 2 * Day);
    Guid b = await GameAt("B", Base + Day);
    await GameAt("C", Base);
    await GuideAt(a, "Walkthrough", Base);
    await GuideAt(a, "Achievements", Base);
    await GuideAt(a, "Maps", Base);
    await GuideAt(b, "Other", Base);

    IReadOnlyList<LibraryGameSummary> summaries = await library.Repository.ListGameSummariesAsync();

    Assert.Equal(["A", "B", "C"], summaries.Select(summary => summary.Game.Title));
    Assert.Equal(["Achievements", "Maps", "Walkthrough"], summaries[0].GuideTitles);
    Assert.Equal(["Other"], summaries[1].GuideTitles);
    Assert.Empty(summaries[2].GuideTitles);
}
```

In `RemovingAGuideUpdatesTheCountAndTheOrder`, append:

```csharp
Assert.Equal(["Kept"], summaries.Single(summary => summary.Game.Title == "Older").GuideTitles);
```

In `ListingNeverReadsGuideContent`, replace the first assertion with:

```csharp
LibraryGameSummary summary = Assert.Single(await library.Repository.ListGameSummariesAsync());
Assert.Equal(1, summary.GuideCount);
Assert.Equal(["Guide"], summary.GuideTitles);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibrarySummaryTests"`.
Expected: the three changed tests FAIL, because `GuideTitles` is empty
(ruling 1); every other summary test passes.

- [ ] **Step 3: Write the minimal implementation**

Replace the body of `ListGameSummariesAsync`'s lambda, keeping the existing
SQL text and comment unchanged:

```csharp
using SqliteConnection connection = OpenConnection();
using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
List<(Game Game, int Count, DateTimeOffset Activity)> rows = [];
using (SqliteCommand command = connection.CreateCommand())
{
    command.Transaction = transaction;
    command.CommandText = """
        ...the existing T05.1 summary statement, unchanged...
        """;
    using SqliteDataReader reader = command.ExecuteReader();
    while (reader.Read())
    {
        rows.Add((ReadGame(reader), reader.GetInt32(11), FromUnixMilliseconds(reader.GetInt64(12))));
    }
}

Dictionary<Guid, List<string>> titles = [];
using (SqliteCommand command = connection.CreateCommand())
{
    command.Transaction = transaction;
    command.CommandText = "SELECT GameId, Title FROM Guides ORDER BY GameId, Title, Id";
    using SqliteDataReader reader = command.ExecuteReader();
    while (reader.Read())
    {
        Guid gameId = Guid.ParseExact(reader.GetString(0), "N");
        if (!titles.TryGetValue(gameId, out List<string>? list))
        {
            titles[gameId] = list = [];
        }
        list.Add(reader.GetString(1));
    }
}

transaction.Commit();
return rows.Select(row => new LibraryGameSummary(
        row.Game, row.Count, row.Activity,
        titles.TryGetValue(row.Game.Id, out List<string>? list) ? list : []))
    .ToList();
```

Extend the comment above the method with one sentence: "Guide titles come
from a second statement in the same read transaction, so they match the
counts."

- [ ] **Step 4: Run the tests to verify they pass**

Run: the full Infrastructure suite.
Expected: PASS, including every existing summary test.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Infrastructure.Tests/LibrarySummaryTests.cs
git commit -m "feat(storage): list each game's guide titles with its summary"
```

## Task 3: Library search UI

TDD skip: this task is XAML and event glue. The matching and facts logic
is covered by Task 1, and Task 4's installed smoke is this task's gate.

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (Library panel,
  around lines 90–155)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`

**Interfaces:**
- Consumes: `LibrarySearch.Filter`, `LibrarySearchMatch` and
  `LibraryGameItem(LibrarySearchMatch)` from Task 1; `GuideTitles` from
  Task 2.
- Produces, for Task 4: the AutomationIds `LibrarySearchInput`,
  `LibrarySearchClear`, `LibraryLoading` and `LibraryNoResults`, and the
  announcements `{n} of {total} games match.` and `No games match.`.

- [ ] **Step 1: Add the search row and the views to the XAML**

Add a fifth `RowDefinition Height="Auto"` before the `*` row of the
Library panel grid, so there are four Auto rows and then `*`. After
`ResumeButton`, add:

```xml
<Grid Grid.Row="3" ColumnSpacing="{StaticResource DesktopGuidesSpacing8}">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <AutoSuggestBox x:Name="LibrarySearchInput"
                    PlaceholderText="Search games and guides"
                    QueryIcon="Find"
                    IsEnabled="False"
                    TextChanged="LibrarySearchTextChanged"
                    QuerySubmitted="LibrarySearchSubmitted"
                    AutomationProperties.Name="Search library"
                    AutomationProperties.AutomationId="LibrarySearchInput" />
    <Button x:Name="LibrarySearchClear"
            Grid.Column="1"
            Content="Clear search"
            Visibility="Collapsed"
            Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"
            Click="LibrarySearchClearClicked"
            AutomationProperties.AutomationId="LibrarySearchClear" />
</Grid>
```

Change the views grid to `Grid.Row="4"`. Give `LibraryEmptyState` and
`GameList` `Visibility="Collapsed"`, and add these two siblings before
`LibraryEmptyState`:

```xml
<StackPanel x:Name="LibraryLoadingState"
            Style="{StaticResource DesktopGuidesBusyRowStyle}"
            VerticalAlignment="Top"
            Visibility="Collapsed">
    <ProgressRing x:Name="LibraryProgress"
                  Width="20"
                  Height="20"
                  IsActive="False"
                  AutomationProperties.AccessibilityView="Raw" />
    <TextBlock Text="Loading library…"
               VerticalAlignment="Center"
               AutomationProperties.AutomationId="LibraryLoading" />
</StackPanel>
<Border x:Name="LibraryNoResultsState"
        Style="{StaticResource DesktopGuidesEmptyStateStyle}"
        Visibility="Collapsed">
    <StackPanel Spacing="{StaticResource DesktopGuidesSpacing8}">
        <FontIcon Glyph="&#xE721;"
                  FontSize="24"
                  HorizontalAlignment="Left"
                  Foreground="{ThemeResource DesktopGuidesAccentBrush}"
                  AutomationProperties.AccessibilityView="Raw" />
        <TextBlock x:Name="LibraryNoResults"
                   Style="{StaticResource DesktopGuidesEmptyTitleStyle}"
                   TextWrapping="Wrap"
                   AutomationProperties.AutomationId="LibraryNoResults" />
        <TextBlock Text="Check the spelling or clear the search."
                   Style="{StaticResource DesktopGuidesSecondaryBodyStyle}" />
    </StackPanel>
</Border>
```

- [ ] **Step 2: Add the view switch and the filter to the code-behind**

Fields, next to `statusDismissTimer`:

```csharp
private readonly DispatcherQueueTimer librarySearchTimer;
private IReadOnlyList<LibraryGameSummary>? librarySummaries;
private string appliedLibraryQuery = string.Empty;
```

In the constructor, after the `statusDismissTimer` setup:

```csharp
librarySearchTimer = DispatcherQueue.CreateTimer();
librarySearchTimer.Interval = TimeSpan.FromMilliseconds(200);
librarySearchTimer.IsRepeating = false;
librarySearchTimer.Tick += (_, _) =>
{
    if (navigator.Current is LibraryRoute)
    {
        ApplyLibrarySearch(announce: true);
    }
};
```

Members:

```csharp
private enum LibraryView { Loading, Empty, NoResults, List }

// Exactly one Library view is visible. Search is usable only when there
// are games to search.
private void ShowLibraryView(LibraryView view)
{
    LibraryLoadingState.Visibility = view == LibraryView.Loading ? Visibility.Visible : Visibility.Collapsed;
    LibraryProgress.IsActive = view == LibraryView.Loading;
    LibraryEmptyState.Visibility = view == LibraryView.Empty ? Visibility.Visible : Visibility.Collapsed;
    LibraryNoResultsState.Visibility = view == LibraryView.NoResults ? Visibility.Visible : Visibility.Collapsed;
    GameList.Visibility = view == LibraryView.List ? Visibility.Visible : Visibility.Collapsed;
    bool searchable = view is LibraryView.NoResults or LibraryView.List;
    LibrarySearchInput.IsEnabled = searchable;
    LibrarySearchClear.Visibility = searchable && appliedLibraryQuery.Length > 0
        ? Visibility.Visible
        : Visibility.Collapsed;
}

// Filters the cached summaries in memory; never reads SQLite or guide files.
private void ApplyLibrarySearch(bool announce)
{
    librarySearchTimer.Stop();
    if (librarySummaries is null)
    {
        return;
    }

    appliedLibraryQuery = LibrarySearchInput.Text.Trim();
    IReadOnlyList<LibrarySearchMatch> matches = LibrarySearch.Filter(librarySummaries, appliedLibraryQuery);
    gameArtwork.CancelAll();
    GameList.ItemsSource = matches.Select(match => new LibraryGameItem(match)).ToList();
    LibraryNoResults.Text = $"No games or guides match \"{appliedLibraryQuery}\".";
    ShowLibraryView(
        librarySummaries.Count == 0 ? LibraryView.Empty
        : matches.Count == 0 ? LibraryView.NoResults
        : LibraryView.List);
    if (announce && appliedLibraryQuery.Length > 0)
    {
        ShowTransientStatus(matches.Count == 0
            ? "No games match."
            : $"{matches.Count} of {librarySummaries.Count} games match.");
    }
}

// Every change restarts the debounce (ruling 2); an unchanged query is skipped.
private void LibrarySearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
{
    librarySearchTimer.Stop();
    if (sender.Text.Trim() != appliedLibraryQuery)
    {
        librarySearchTimer.Start();
    }
}

private void LibrarySearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
    ApplyLibrarySearch(announce: true);

private void LibrarySearchClearClicked(object sender, RoutedEventArgs e)
{
    LibrarySearchInput.Text = string.Empty;
    ApplyLibrarySearch(announce: true);
    LibrarySearchInput.Focus(FocusState.Programmatic);
}
```

Replace the `case LibraryRoute:` body in `RenderCurrentAsync` with:

```csharp
LibraryPanel.Visibility = Visibility.Visible;
librarySearchTimer.Stop();
if (librarySummaries is null)
{
    ShowLibraryView(LibraryView.Loading);
}
ShowBusyStatus("Loading library…");
IReadOnlyList<LibraryGameSummary> games = await library.ListGameSummariesAsync();
AppSettings settings = await library.GetSettingsAsync();
Guide? resume = settings.LastActiveGuideId is Guid lastId
    ? await library.GetGuideAsync(lastId)
    : null;
if (generation != renderGeneration)
{
    return false;
}
librarySummaries = games;
ApplyLibrarySearch(announce: false);
resumeGuideId = resume?.Id;
ResumeButton.Visibility =
    resume is null ? Visibility.Collapsed : Visibility.Visible;
if (resume is not null)
{
    ResumeButton.Content = $"Resume {resume.Title}";
}
ShowTransientStatus("Library ready.");
break;
```

In the render's `catch`, before `ShowErrorStatus`, collapse the spinner
(ruling 6):

```csharp
LibraryLoadingState.Visibility = Visibility.Collapsed;
LibraryProgress.IsActive = false;
```

- [ ] **Step 3: Build and run the suites**

Run: the Production build, then the full Core and Infrastructure suites.
Expected: the build succeeds with no new warnings, and both suites PASS.

- [ ] **Step 4: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Production/ShellWindow.xaml src/DesktopGuides.Production/ShellWindow.xaml.cs
git commit -m "feat(shell): search the Library with loading and no-results views"
```

## Task 4: Seed, installed smoke and verification

TDD note: the smoke is written before the run that exercises Task 3. Step 6
is its RED/GREEN gate: the CI run fails if Task 3's AutomationIds, views
or announcements are wrong.

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (arg validation
  and usage around line 310; new block before `seed-import`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`
- Modify: `tools/p1/windows_shell_install.ps1`

**Interfaces:**
- Consumes: Task 3's AutomationIds and copy (Global Constraints).
- Produces: the seed mode `seed-search`, the smoke mode `library-search`,
  and the result names `library-search-light` and `library-search-dark`
  with screenshots `search-results` and `no-results`.

- [ ] **Step 1: Add the `seed-search` seed**

Add `"seed-search"` to the accepted modes and to the usage string. Before
`if (args[0] == "seed-import")`, add:

```csharp
if (args[0] == "seed-search")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The search seed needs an empty library.");
    }
    const long searchDay = 86_400_000;
    long searchNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    // Titles exercise case, accent, width and non-Latin matching. Created
    // times fix the Library order: Zeta (its guide, -1 day), then the rest.
    (string Title, int DaysAgo)[] searchTitles =
    [
        ("Zeta Archive Game", 5),
        ("Pok\u00e9mon Crystal", 2),
        ("\u014ckami HD", 3),
        ("\u30c9\u30e9\u30b4\u30f3\u30af\u30a8\u30b9\u30c8XI", 4)
    ];
    List<string> searchSql = [];
    Guid zetaSearchId = Guid.Empty;
    foreach ((string title, int daysAgo) in searchTitles)
    {
        Game game = await repository.AddGameAsync(title, null, null);
        long created = searchNow - daysAgo * searchDay;
        searchSql.Add($"UPDATE Games SET CreatedUtcMs = {created}, UpdatedUtcMs = {created} WHERE Id = '{game.Id:N}';");
        if (zetaSearchId == Guid.Empty)
        {
            zetaSearchId = game.Id;
        }
    }
    await InsertGuideAsync(paths, zetaSearchId, Guid.NewGuid(), "Complete Walkthrough", searchNow - searchDay);
    ExecuteSql(paths, string.Join('\n', searchSql));

    string[] searchOrder = [.. (await repository.ListGameSummariesAsync()).Select(entry => entry.Game.Title)];
    if (!searchOrder.SequenceEqual(searchTitles.Select(entry => entry.Title)))
    {
        throw new InvalidOperationException("The search seed did not read back in activity order.");
    }
    Console.WriteLine("Seeded four search games.");
    return 0;
}
```

The expected read-back order is the declaration order: Zeta (−1 day via
its guide), Pokémon (−2), Ōkami (−3), then ドラゴンクエストXI (−4).

- [ ] **Step 2: Add the smoke helpers**

Replace `Set-SearchQuery` with a shared pattern lookup, keeping the Add
game callers working through the default id:

```powershell
    # The AutoSuggestBox exposes its text through its inner edit box.
    function Get-SearchPattern([string] $id) {
        $box = Wait-VisibleById $id
        $pattern = $null
        if (-not $box.TryGetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
            $edit = $box.FindFirst($scope,
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Edit))
            if (-not $edit) { throw "The search box '$id' has no editable text." }
            $pattern = $edit.GetCurrentPattern(
                [System.Windows.Automation.ValuePattern]::Pattern)
        }
        return $pattern
    }

    function Set-SearchQuery([string] $query, [string] $id = 'GameSearchInput') {
        (Get-SearchPattern $id).SetValue($query)
    }

    function Get-SearchText([string] $id) {
        return (Get-SearchPattern $id).Current.Value
    }

    function Search-Library([string] $query, [string] $status, $rows) {
        Set-SearchQuery $query 'LibrarySearchInput'
        [void](Wait-Status $status -AllowHidden)
        [void](Assert-RowFacts 'GameList' $rows)
    }
```

After `Wait-FocusedId`, add:

```powershell
    # Focus in an AutoSuggestBox lands on its inner edit box.
    function Wait-FocusWithin([string] $id) {
        $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $element = [System.Windows.Automation.AutomationElement]::FocusedElement
            while ($element) {
                if ($element.Current.AutomationId -eq $id) { return }
                $element = $walker.GetParent($element)
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected keyboard focus inside '$id'."
    }
```

After `Assert-Absent`, move the `catalog` mode's connection check into:

```powershell
    function Assert-NoRemoteConnections([string] $view) {
        $loopback = @('127.0.0.1', '::1', '0.0.0.0', '::')
        $remote = @(Get-NetTCPConnection -OwningProcess $ProcessId -ErrorAction SilentlyContinue |
            Where-Object { $_.State -ne 'Listen' -and $_.RemoteAddress -notin $loopback })
        $report.remoteConnections = $remote.Count
        if ($remote.Count -gt 0) {
            throw "The $view opened $($remote.Count) non-loopback connection(s)."
        }
    }
```

and replace the inline block in `catalog` with
`Assert-NoRemoteConnections 'catalog'`.

In `Wait-Status`, as the first statement inside
`if ($message -in $expected -and $sequence -gt $script:lastStatusSequence)`,
add (ruling 8):

```powershell
                if ($message -eq 'Library ready.') { Assert-Absent 'LibraryLoading' }
```

- [ ] **Step 3: Update the existing modes**

- `empty`: after the Resume check, add:

  ```powershell
        $search = Find-ById 'LibrarySearchInput'
        if (-not $search -or $search.Current.IsEnabled) {
            throw 'An empty library left search enabled.'
        }
        Assert-Absent 'LibrarySearchClear'
  ```

- `catalog`: after each of the two
  `Focus-And-Verify 'AddGameButton'; SendWait('{TAB}')` pairs, insert
  (ruling 9):

  ```powershell
        [void](Wait-FocusWithin 'LibrarySearchInput')
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
  ```

- Add `'library-search'` to the `-Mode` ValidateSet, after
  `'catalog-facts'`.

- [ ] **Step 4: Add the `library-search` mode**

After the `catalog-facts` block:

```powershell
    elseif ($Mode -eq 'library-search') {
        # Built from code points: Windows PowerShell 5.1 reads this file as ANSI.
        $zeta = 'Zeta Archive Game'
        $pokemon = 'Pok' + [char]0x00E9 + 'mon Crystal'
        $okami = [string][char]0x014C + 'kami HD'
        $dragonPrefix = [string]::new([char[]]@(0x30C9, 0x30E9, 0x30B4, 0x30F3))
        $dragon = $dragonPrefix + [string]::new([char[]]@(0x30AF, 0x30A8, 0x30B9, 0x30C8)) + 'XI'
        $fullwidthXi = [string]::new([char[]]@(0xFF38, 0xFF29))
        $zetaRow = @($zeta, 'Manual, 1 guide')
        $zetaMatch = @($zeta, 'Manual, 1 guide, Guide: Complete Walkthrough')
        $pokemonRow = @($pokemon, 'Manual, No guides')
        $okamiRow = @($okami, 'Manual, No guides')
        $dragonRow = @($dragon, 'Manual, No guides')
        $allRows = @($zetaRow, $pokemonRow, $okamiRow, $dragonRow)

        [void](Wait-Name 'LibraryHeading' 'Library')
        $report.libraryRows = Assert-RowFacts 'GameList' $allRows
        Assert-Absent 'LibrarySearchClear'

        # Real keystrokes for the ASCII query; ValuePattern for the rest (Ruling 10).
        $box = Wait-EnabledById 'LibrarySearchInput'
        $box.SetFocus()
        [void](Wait-FocusWithin 'LibrarySearchInput')
        [System.Windows.Forms.SendKeys]::SendWait('POKEMON')
        [void](Wait-Status '1 of 4 games match.' -AllowHidden)
        [void](Assert-RowFacts 'GameList' @(,$pokemonRow))
        [void](Wait-VisibleById 'LibrarySearchClear')
        $report.phases += 'search-case-accent'

        Search-Library 'okami' '1 of 4 games match.' @(,$okamiRow)
        Search-Library $dragonPrefix '1 of 4 games match.' @(,$dragonRow)
        Search-Library $fullwidthXi '1 of 4 games match.' @(,$dragonRow)
        $report.phases += 'search-non-ascii'

        Search-Library 'walkthrough' '1 of 4 games match.' @(,$zetaMatch)
        $report.phases += 'search-guide-title'
        [void](Wait-HiddenById 'ShellStatus')
        Save-WindowScreenshot 'search-results'

        Set-SearchQuery 'zzzz' 'LibrarySearchInput'
        [void](Wait-Status 'No games match.' -AllowHidden)
        [void](Wait-Name 'LibraryNoResults' 'No games or guides match "zzzz".')
        Wait-HiddenById 'GameList'
        [void](Wait-VisibleById 'LibrarySearchClear')
        $report.phases += 'search-no-results'
        [void](Wait-HiddenById 'ShellStatus')
        Save-WindowScreenshot 'no-results'

        Invoke-Element (Wait-VisibleById 'LibrarySearchClear')
        [void](Assert-RowFacts 'GameList' $allRows)
        [void](Wait-FocusWithin 'LibrarySearchInput')
        Wait-HiddenById 'LibrarySearchClear'
        if ((Get-SearchText 'LibrarySearchInput') -ne '') {
            throw 'Clear search left text in the box.'
        }
        $report.phases += 'search-clear'

        Search-Library 'walkthrough' '1 of 4 games match.' @(,$zetaMatch)
        Select-Element $zeta
        [void](Wait-Name 'GameHeading' $zeta)
        [void](Wait-Status 'Game ready.')
        [void](Assert-RowFacts 'GuideList' @(,@('Complete Walkthrough', 'Text (TXT), Not started')))
        $report.phases += 'search-not-started'

        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        if ((Get-SearchText 'LibrarySearchInput') -ne 'walkthrough') {
            throw 'Back to the Library dropped the search query.'
        }
        [void](Assert-RowFacts 'GameList' @(,$zetaMatch))
        $report.phases += 'search-kept-after-back'

        Assert-NoRemoteConnections 'library search'
        $report.phases += 'search-no-provider-traffic'
    }
```

- [ ] **Step 5: Run the mode from the install script**

In `windows_shell_install.ps1`, after `Run-CatalogFactsScenarios`, add:

```powershell
function Run-LibrarySearchScenarios {
    Invoke-ShellSeed @('seed-search', $dataRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.librarySearchLight = Run-ShellSmoke 'library-search' -ResultName 'library-search-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.librarySearchDark = Run-ShellSmoke 'library-search' -ResultName 'library-search-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}
```

In the `$CatalogOnly` block, fix the indentation and run the new scenarios
after a wipe:

```powershell
    if ($CatalogOnly) {
        Run-CatalogScenarios
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Run-CatalogFactsScenarios
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Run-LibrarySearchScenarios
        $report.success = $true
        return
    }
```

In the full run, after `Run-CatalogFactsScenarios`, add:

```powershell
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-LibrarySearchScenarios
```

- [ ] **Step 6: Build, parse and verify installed**

Run: the seed build and the Production build; the PowerShell parse check
over both scripts; then the full Core and Infrastructure suites.
Expected: both builds succeed, both scripts parse with no errors, and both
suites PASS.

Commit (show the message in chat first):

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): smoke-test Library search and its views"
```

Then **ask the user before pushing.** After their OK, push the branch and
watch `production-shell-ui`.
Expected: the job passes; `library-search-light` and `library-search-dark`
list every phase from `search-case-accent` to `search-no-provider-traffic`;
`empty` and `catalog` pass with the new checks. Download the
`search-results` and `no-results` screenshots for light and dark into
`docs/p1/evidence/t05-2-library-search/` as `results-light.png`,
`results-dark.png`, `no-results-light.png` and `no-results-dark.png`, and
read each one to confirm it shows the intended view.

## Task 5: Documentation and verification record

TDD skip: documentation only.

**Files:**
- Modify: `docs/p1/t05-2-library-search-design.md` (status line and a new
  verification record)
- Modify: `docs/p1/implementation-plan.md` (after the T05.1 paragraph)
- Modify: `docs/p1/e2e-testing.md` (after the Catalog facts row)
- Modify: `docs/progress.md` (the T05.1 row and a new T05.2 row)
- Add: `docs/p1/evidence/t05-2-library-search/*.png` (from Task 4)

**Interfaces:**
- Consumes: the CI run ID, the test counts and the screenshots from
  Task 4.

- [ ] **Step 1: Update the design**

Set the status to: `Status: implemented on
feat/p1-t05-2-library-search; verified by CI run <run-id>. Prerequisite
T05.1 (PR #23) is merged.` Append a `## T05.2 verification record`
section in the T05.1 record's shape:

- **Unit tests.** On `pcsx2-win`, Infrastructure `<n>/<n>` and Core
  `<n>/<n>` passed. Name the new tests: `LibrarySearchTests` (case,
  accents both ways, width both ways, Japanese and Cyrillic, padded,
  blank, non-matches, game-title and guide-only matches, order, empty
  input); `CatalogPresentationTests` (`Guide: {title}` in the facts and
  the spoken text, and its absence); `LibrarySummaryTests` (each game's
  own guide titles in title order, after a removal, and after the content
  directory is deleted).
- **Installed.** CI run `<run-id>` passed `production-shell-ui`, with
  `library-search` in light and dark, every phase listed; `empty` with
  the disabled search box; `catalog` with the extra Tab.
- **Rulings.** Rulings 1–11 in the
  [plan](t05-2-library-search-plan.md#rulings-against-the-spec), plus any
  made during implementation.
- **Evidence.** Links to the four screenshots.

Fill in the real counts and run ID; never leave the placeholders.

- [ ] **Step 2: Update the traceability docs**

After the T05.1 paragraph in `implementation-plan.md`:

```markdown
T05.2 is implemented on `feat/p1-t05-2-library-search`; see the
[design and verification record](t05-2-library-search-design.md). The
Library search box filters games whose title, or one of whose guide
titles, contains the query, ignoring case, accents and width. A guide-only
match names the guide in the row's facts. The Library shows exactly one of
loading, empty, no-results and list views, with a visible Clear search.
Search filters the loaded summaries in memory; it never opens guide files
or calls a provider. CI run <run-id> passed `library-search` in light and
dark.
```

After the Catalog facts row in `e2e-testing.md`:

```markdown
| Library search | Seed Zeta Archive Game with the unread guide Complete Walkthrough, Pokémon Crystal, Ōkami HD and ドラゴンクエストXI. In light and dark: typed `POKEMON`, then `okami`, `ドラゴン` and fullwidth `ＸＩ`, each leave one row and announce `1 of 4 games match.`; `walkthrough` leaves Zeta with HelpText `Manual, 1 guide, Guide: Complete Walkthrough`; `zzzz` shows `No games or guides match "zzzz".` and hides `GameList`; Clear search restores all four rows, empties and focuses the box and hides itself; Zeta's guide reads `Not started`, and Back keeps `walkthrough` applied; no non-loopback TCP connection. `empty` shows search disabled, and every Library-ready wait asserts the loading view is gone. | T05.2, TR05.1, TR05.2, TR11.3 |
```

In `progress.md`, change the T05.1 row's status to
`Merged through [PR #23](https://github.com/ilya-slalom/desktop-guides/pull/23) on 30 September 2026, merge commit `d94df92`.`
and add after it:

```markdown
| P1 T05.2 Library search | Implemented on `feat/p1-t05-2-library-search`; PR open. | The Library search box filters games by their own or their guides' titles, ignoring case, accents and width, and names the matching guide. Loading, empty-library and no-results views are distinct, with a visible Clear search. Search reads only loaded metadata. CI run [<run-id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run-id>) passed `library-search`; see the [verification record](p1/t05-2-library-search-design.md#t052-verification-record). |
```

`work-breakdown.md` and `initial-design.md` need no change: T05.2's
wording there already matches.

- [ ] **Step 3: Check and commit**

Run: `grep -n "<run-id>\|<n>" docs/progress.md docs/p1/implementation-plan.md docs/p1/t05-2-library-search-design.md`
Expected: no output.

Commit (show the message in chat first):

```bash
git add docs/progress.md docs/p1/implementation-plan.md docs/p1/e2e-testing.md docs/p1/t05-2-library-search-design.md docs/p1/evidence/t05-2-library-search
git commit -m "docs(p1): record T05.2 Library search verification"
```
