# T04.2 Game Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Renaming a game keeps it bound to its Game ID, with its provider
link and snapshot, artwork, Guide IDs, reading state, Resume, and the
selected guide intact. A game with no guides can be removed from the Game
page.

**Architecture:** Core gains `EmptyGameRemoval`, a `GameRemover` that
removes the row and then deletes the artwork best effort, and
`GameRemovalPresentation`. The repository gains `RemoveEmptyGameAsync`,
which deletes a game row in one write transaction only while the game has no
guides. The Game page gains **Remove game**, a guides-first hint, and a
confirmation dialog. Rename gets tests, not new code.

**Tech Stack:** .NET 10, WinUI 3, Microsoft.Data.Sqlite, xUnit, the
PowerShell 5.1 UI Automation harness.

**Spec:** `docs/p1/t04-2-game-actions-design.md`

**Target:** T04.2 (TR03.1, TR04.3). **Prerequisites:** T04.1 (PR #11),
T04.4 (PR #14), T05.4 (PR #16) and T11.1 (PR #6), all merged.

## Global Constraints

- No schema change and no new package.
- Removal covers only games with no guides. It never touches guide files,
  the trash journal or `FileOperations`.
- The delete is `DELETE FROM Games WHERE Id = $id AND NOT EXISTS (SELECT 1
  FROM Guides WHERE GameId = $id)`. `Guides.GameId` cascades on delete, so
  the guard is what keeps a guide from being removed with its game.
- Rename writes only `Title`, `Platform`, `Notes` and `UpdatedUtcMs`.
- Copy, verbatim:
  - dialog title: `Remove {title}?`;
  - dialog body: `This removes the game and its details from Desktop Guides.`;
  - hint: `Remove this game's guides first.`;
  - statuses: `Removed {title}.`, `{title} was already removed.`,
    `{title} has guides now, so it wasn't removed.` and
    `{title} couldn't be removed. The game is unchanged. Try again.`;
  - buttons: `Remove game`; in the dialog, `Remove` and `Cancel`.
- AutomationIds: `RemoveGameButton`, `RemoveGameHint`, `RemoveGameDialog`,
  `RemoveGameMessage`.
- Styles: `DesktopGuidesSecondaryActionButtonStyle` for the button,
  `DesktopGuidesSecondaryBodyStyle` for the hint.
- Cancel is the dialog's default button, so Enter and Escape both cancel.
- UI tests assert only what app code controls.
- PowerShell scripts stay ASCII-only.
- Never print, copy or log provider credential values. `describe-actions`
  prints none.

## Rulings against the spec

Task 5 records these rulings in the design's verification record.

1. **Task 1 adds `RemoveEmptyGameAsync` to `SqliteLibraryRepository` as a
   stub that throws `NotSupportedException`,** so the build stays green.
   Task 2 replaces it through TDD. Nothing calls it before Task 3.
2. **The Core types live in `GameRemovalContracts.cs`,** beside
   `GuideRemovalContracts.cs`. The spec names only the folder.
3. **`GameRemover` deletes artwork only on `Removed`,** even if a future
   repository returned a path with another outcome.
4. **`EditGameClicked`'s `finally` focuses Edit game when it re-enables
   it.** The button is disabled while the dialog is open, so WinUI cannot
   return focus to it, and the spec wants focus on Edit game after a save.
   Cancel gets the same focus.
5. **The smoke opens Beta Route Guide and comes back with Back to game to
   select it,** because selecting a row opens the Reader (the `remove-*`
   precedent).
6. **The Game page tracks the loaded guide count in `loadedGameGuideCount`
   (`int?`).** It is null while the page loads, so Remove game stays
   disabled until the count is known.
7. **While the hint is hidden, the button's HelpText is the empty string.**
8. **During a game removal, Edit game, Refresh metadata, Import guide and
   Remove game are disabled.** The empty game has no guide rows to disable.
9. **After `HasGuides`, focus moves to Edit game,** because Remove game is
   then disabled.
10. **After `Removed` or `NotFound`, the removal status is shown after the
    Library render's `Library ready.`,** so `Wait-Status 'Removed Empty
    Linked Game.'` finds it.
11. **The smoke cancels with Enter as well as Escape,** to pin Cancel as the
    default button.
12. **`RefreshMetadataAsync` calls `UpdateRemoveGameAction` when it starts
    and ends,** so Remove game is disabled while a refresh runs.
13. **The install script gains `-GameActionsOnly`** for host debugging. CI
    still runs the full script.
14. **If the game is gone before the dialog opens, the flow finishes as
    `NotFound`,** with the status titled from the Game page heading.
15. **`loadedGameGuideCount` is reset to null at the top of every render,**
    so a page that is loading, or a route other than the Game page, never
    leaves Remove game enabled from an earlier count.
16. **The smoke's `Test-BackEnabled` replaces an "assert Back unavailable"
    helper,** and is checked both ways: enabled on the Game page before the
    removal as a positive control, disabled after it. A check that passed
    because the button was never found would prove nothing.
17. **The `design-language` mode asserts that `RemoveGameButton` and
    `RemoveGameHint` stay inside the window at the wide and narrow widths,**
    because the header gains a fourth column.
18. **`EditGameClicked` focuses Edit game only when the Game page still
    shows the same game,** so a close or a navigation during the save does
    not pull focus to a hidden button.
19. **`game-actions*` joins the 120-second smoke timeout group,** beside
    `catalog*` and `import-*`, because each mode spans several renders.
20. **`describe-actions` reads the database read-only and never opens the
    repository,** because `InitializeAsync` sweeps unreferenced artwork and
    would hide a leftover folder from a failed artwork delete.
21. **Checks beyond the spec:**
    - `describe-actions` also prints `LastActiveGuideId`, and the install
      script requires Beta Route Guide's ID, pinning Resume across rename
      and restart.
    - The install script checks the state after the persisted run too.
    - `game-actions-persisted` also checks `GameCover` and `ReaderGameName`.
    - The smoke checks the dialog title with a `Wait-VisibleName` helper
      over `Find-ByName`, since the title has no AutomationId.
    - The new modes do not pass `-ExpectedResumeGuide`, whose
      `ValidateSet` rejects `Beta Route Guide`; they name it themselves.
22. **The Game details card scrolls so the guide list keeps a row.** CI run
    36813711960 failed `game-actions-light` with "Expected a visible guide
    list.": in the 768 × 519 CI window the wrapped title, the
    Remove game hint and the details card filled the page's Auto rows and
    left the guide list no height. A user with a short window would have
    seen no guides.
    - `GamePageLayout.DetailsMaxHeight` caps `GameMetadataSurface` at what
      the header, the Guides row and the row gaps leave, minus a 96 px guide
      list minimum. The card has a floor of 48 px.
    - The card's content sits in the `GameMetadataScroll` ScrollViewer.
      `GamePanel`, `GameHeader` and `GameGuidesHeader` recompute the cap on
      SizeChanged.
    - The smoke's `Assert-GuideListUsable` adds the
      `guide-list-keeps-a-row` phase. The CI run above is its RED evidence;
      no host run reproduced it.
    - `Wait-VisibleById` and `Wait-Name` scroll the card one step per poll
      toward an element that is offscreen only because it is below the
      card's viewport.
    - Cost if wrong: on very short windows the details need scrolling.
    - From the final review: opening a different game resets the card's
      scroll to the top (`next-game-details-at-top`), and `design-language`
      checks that a short card is uncapped at 1024 × 720, since the
      scrolling waits would otherwise hide a cap that is too tight.

## Review Focus

1. **Closing the window while the Remove game dialog is open.** The dialog
   must close as Cancel and remove nothing. The flow reuses
   `activeRemoveDialog`, which the close handler already hides, and returns
   unless the result is `Primary`. Checked by the final review.
2. **Pressing Remove game while a metadata refresh for that game runs.**
   The button must be disabled until the refresh ends (ruling 12).
   Checked by the final review.
3. **Back after a removal.** `navigator.ResetToLibrary()` clears the back
   stack, so Back cannot reach the removed page. Covered by the smoke's
   `Test-BackEnabled` checks: enabled on the Game page before the removal,
   disabled on the Library after it (ruling 16).
4. **A search query typed before opening the game.** The Library render
   reapplies the kept query after the removal. Covered by the smoke: it
   filters with `Empty` before the removal and expects the query kept and
   the no-results view after it.
5. **Activating Remove game twice quickly.** The `gameRemoveRequested`
   guard must show one dialog only. Checked by the final review.

## Host commands

Builds and tests run on `pcsx2-win` over SSH:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){ s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t04-2) { Remove-Item -Recurse -Force E:\work\desktop-guides\t04-2 }; New-Item -ItemType Directory E:\work\desktop-guides\t04-2 | Out-Null"'; COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t04-2'; }
```

- Core tests: `stage && s 'cd /d E:\work\desktop-guides\t04-2 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
- Infrastructure tests: the same with
  `tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj`.
- One class: append `--filter "FullyQualifiedName~<Class>"`.
- Production build: `dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64`
- Seed build: `dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release`
- PowerShell parse check: scp a temporary `.ps1` that runs
  `[System.Management.Automation.Language.Parser]::ParseFile` over
  `windows_shell_ui_smoke.ps1` and `windows_shell_install.ps1`, and run it
  with `-File`. Inline `-Command` breaks under SSH quoting.

Redirect long test output to a file and read its tail.

---

## Task 1: Core removal types, remover and copy

**Files:**
- Create: `src/DesktopGuides.Core/Library/GameRemovalContracts.cs`
- Create: `src/DesktopGuides.Core/Library/GameRemover.cs`
- Create: `src/DesktopGuides.Core/Library/GameRemovalPresentation.cs`
- Modify: `src/DesktopGuides.Core/Library/ILibraryRepository.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (stub only, ruling 1)
- Modify: `tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs`
- Create: `tests/DesktopGuides.Core.Tests/GameRemoverTests.cs`
- Create: `tests/DesktopGuides.Core.Tests/GameRemovalPresentationTests.cs`

**Interfaces:**
- Produces:
  - `enum EmptyGameRemovalOutcome { Removed, NotFound, HasGuides }`;
  - `record EmptyGameRemoval(EmptyGameRemovalOutcome Outcome, string? ArtworkRelativePath)`;
  - `Task<EmptyGameRemoval> ILibraryRepository.RemoveEmptyGameAsync(Guid gameId, CancellationToken token = default)`;
  - `GameRemover(ILibraryRepository repository, IArtworkStore artwork)` with
    `Task<EmptyGameRemovalOutcome> RemoveAsync(Guid gameId, CancellationToken token = default)`;
  - `static class GameRemovalPresentation` with `DialogTitle(string)`,
    `DialogBody`, `GuidesFirst`, `Removed(string)`, `AlreadyRemoved(string)`,
    `HasGuides(string)` and `Failed(string)`.

- [ ] **Step 1: Write the failing tests**

In `ImporterFakes.cs`, add to `FakeRepository`:

```csharp
    public EmptyGameRemoval NextRemoval { get; set; } = new(EmptyGameRemovalOutcome.NotFound, null);
    public List<Guid> RemoveCalls { get; } = [];

    public Task<EmptyGameRemoval> RemoveEmptyGameAsync(Guid gameId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        RemoveCalls.Add(gameId);
        return Task.FromResult(NextRemoval);
    }
```

and change `FakeStore.Delete` to:

```csharp
    public Exception? DeleteFailure { get; set; }

    public void Delete(string relativePath)
    {
        if (DeleteFailure is not null) throw DeleteFailure;
        Deleted.Add(relativePath);
        Files.Remove(relativePath);
    }
```

`tests/DesktopGuides.Core.Tests/GameRemoverTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Tests.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GameRemoverTests
{
    private const string Artwork = "artwork/0123456789abcdef0123456789abcdef/cover.png";
    private readonly FakeRepository repository = new();
    private readonly FakeStore store = new();

    private GameRemover Remover() => new(repository, store);

    [Fact]
    public async Task ARemovedGameLosesItsArtwork()
    {
        Guid id = Guid.NewGuid();
        repository.NextRemoval = new(EmptyGameRemovalOutcome.Removed, Artwork);

        EmptyGameRemovalOutcome outcome = await Remover().RemoveAsync(id);

        Assert.Equal(EmptyGameRemovalOutcome.Removed, outcome);
        Assert.Equal([id], repository.RemoveCalls);
        Assert.Equal([Artwork], store.Deleted);
    }

    [Fact]
    public async Task ARemovedGameWithoutArtworkDeletesNothing()
    {
        repository.NextRemoval = new(EmptyGameRemovalOutcome.Removed, null);

        Assert.Equal(EmptyGameRemovalOutcome.Removed, await Remover().RemoveAsync(Guid.NewGuid()));
        Assert.Empty(store.Deleted);
    }

    [Theory]
    [InlineData(EmptyGameRemovalOutcome.NotFound)]
    [InlineData(EmptyGameRemovalOutcome.HasGuides)]
    public async Task AGameThatWasNotRemovedKeepsItsFiles(EmptyGameRemovalOutcome outcome)
    {
        repository.NextRemoval = new(outcome, Artwork);

        Assert.Equal(outcome, await Remover().RemoveAsync(Guid.NewGuid()));
        Assert.Empty(store.Deleted);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task AFailedArtworkDeleteStillReportsRemoved(Type failure)
    {
        repository.NextRemoval = new(EmptyGameRemovalOutcome.Removed, Artwork);
        store.DeleteFailure = (Exception)Activator.CreateInstance(failure)!;

        Assert.Equal(EmptyGameRemovalOutcome.Removed, await Remover().RemoveAsync(Guid.NewGuid()));
    }
}
```

`tests/DesktopGuides.Core.Tests/GameRemovalPresentationTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GameRemovalPresentationTests
{
    [Fact]
    public void DialogTitleNamesTheGame() =>
        Assert.Equal("Remove Zelda?", GameRemovalPresentation.DialogTitle("Zelda"));

    [Fact]
    public void DialogBodyDescribesTheRemoval() =>
        Assert.Equal(
            "This removes the game and its details from Desktop Guides.",
            GameRemovalPresentation.DialogBody);

    [Fact]
    public void GuidesFirstExplainsTheDisabledAction() =>
        Assert.Equal("Remove this game's guides first.", GameRemovalPresentation.GuidesFirst);

    [Fact]
    public void StatusLinesNameTheGame()
    {
        Assert.Equal("Removed Zelda.", GameRemovalPresentation.Removed("Zelda"));
        Assert.Equal("Zelda was already removed.", GameRemovalPresentation.AlreadyRemoved("Zelda"));
        Assert.Equal(
            "Zelda has guides now, so it wasn't removed.", GameRemovalPresentation.HasGuides("Zelda"));
        Assert.Equal(
            "Zelda couldn't be removed. The game is unchanged. Try again.",
            GameRemovalPresentation.Failed("Zelda"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: the Core tests, filtered to `GameRemoverTests` and then
`GameRemovalPresentationTests`.
Expected: the build fails: `EmptyGameRemoval`, `EmptyGameRemovalOutcome`,
`GameRemover` and `GameRemovalPresentation` are not defined.

- [ ] **Step 3: Write the minimal implementation**

`src/DesktopGuides.Core/Library/GameRemovalContracts.cs`:

```csharp
namespace DesktopGuides.Core.Library;

public enum EmptyGameRemovalOutcome { Removed, NotFound, HasGuides }

/// <summary>The artwork path is set only for Removed, and only when the game had artwork.</summary>
public sealed record EmptyGameRemoval(
    EmptyGameRemovalOutcome Outcome, string? ArtworkRelativePath);
```

In `ILibraryRepository.cs`, after `UpdateGameAsync`:

```csharp
    // Deletes the game only while it has no guides.
    Task<EmptyGameRemoval> RemoveEmptyGameAsync(
        Guid gameId, CancellationToken token = default);
```

`src/DesktopGuides.Core/Library/GameRemover.cs`:

```csharp
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Core.Library;

/// <summary>Removes a game without guides, then deletes its artwork best effort.</summary>
public sealed class GameRemover(ILibraryRepository repository, IArtworkStore artwork)
{
    public async Task<EmptyGameRemovalOutcome> RemoveAsync(
        Guid gameId, CancellationToken token = default)
    {
        EmptyGameRemoval removal = await repository.RemoveEmptyGameAsync(gameId, token);
        if (removal is { Outcome: EmptyGameRemovalOutcome.Removed, ArtworkRelativePath: { } path })
        {
            try
            {
                artwork.Delete(path);
            }
            // The row is gone, so the next startup sweep deletes the file.
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return removal.Outcome;
    }
}
```

`src/DesktopGuides.Core/Library/GameRemovalPresentation.cs`:

```csharp
namespace DesktopGuides.Core.Library;

/// <summary>The game removal confirmation, hint and status copy.</summary>
public static class GameRemovalPresentation
{
    public const string DialogBody = "This removes the game and its details from Desktop Guides.";

    public const string GuidesFirst = "Remove this game's guides first.";

    public static string DialogTitle(string title) => $"Remove {title}?";

    public static string Removed(string title) => $"Removed {title}.";

    public static string AlreadyRemoved(string title) => $"{title} was already removed.";

    public static string HasGuides(string title) => $"{title} has guides now, so it wasn't removed.";

    public static string Failed(string title) =>
        $"{title} couldn't be removed. The game is unchanged. Try again.";
}
```

In `SqliteLibraryRepository.cs`, after `UpdateGameAsync` (ruling 1):

```csharp
    public Task<EmptyGameRemoval> RemoveEmptyGameAsync(
        Guid gameId, CancellationToken token = default) =>
        throw new NotSupportedException();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the same filtered Core tests, then the full Core and Infrastructure
suites and the Production build.
Expected: all PASS; the Production build succeeds with no new warnings.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Core/Library src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Core.Tests
git commit -m "feat(core): add the empty-game remover and its copy"
```

## Task 2: Repository removal and rename tests

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/GameRenameTests.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/EmptyGameRemovalTests.cs`

**Interfaces:**
- Consumes: `EmptyGameRemoval`, `EmptyGameRemovalOutcome`, `GameRemover`
  and the `RemoveEmptyGameAsync` signature from Task 1.
- Produces: the real `SqliteLibraryRepository.RemoveEmptyGameAsync`.

Both classes build on `RemovalLibrary` (a real library on disk, with
`RestartAsync`, `AddGameAsync`, `AddGuideAsync` and `RowsFor`).
`GameMetadataJsonTests.Sample()` and `GameMetadataJson` are internal and
visible to the tests.

- [ ] **Step 1: Write the failing tests**

`tests/DesktopGuides.Infrastructure.Tests/GameRenameTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class GameRenameTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;
    private Game before = null!;
    private Guid alphaId;
    private Guid betaId;

    public async Task InitializeAsync()
    {
        library = await RemovalLibrary.CreateAsync();
        Guid id = Guid.NewGuid();
        StoredArtwork cover = await new ManagedArtworkStore(library.Paths)
            .StoreAsync(id, Artwork.TestImages.Png(4, 4), default);
        await library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            id, "Linked Rename Game", "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, "900100", DateTimeOffset.UnixEpoch.AddDays(1)),
            GameMetadataJsonTests.Sample(), cover.RelativePath));
        alphaId = await library.AddGuideAsync(id, "Alpha Route Guide");
        betaId = await library.AddGuideAsync(id, "Beta Route Guide");
        await library.Repository.SaveReadingLocationAsync(alphaId, "{\"offset\":12}", 0.45);
        await library.Repository.UpdateSettingsAsync(s => s with { LastActiveGuideId = betaId });
        // Read back, so the comparison uses stored precision.
        before = (await library.Repository.GetGameAsync(id))!;
    }

    public async Task DisposeAsync() => await library.DisposeAsync();

    [Fact]
    public async Task RenameKeepsTheGameBoundToItsId()
    {
        Game renamed = await library.Repository.UpdateGameAsync(
            before.Id, "Renamed Linked Game", before.Platform, before.Notes);

        Assert.Equal(before.Id, renamed.Id);
        await AssertRenamedAndKeptAsync();
    }

    [Fact]
    public async Task RenameSurvivesARestart()
    {
        await library.Repository.UpdateGameAsync(
            before.Id, "Renamed Linked Game", before.Platform, before.Notes);

        await library.RestartAsync();

        await AssertRenamedAndKeptAsync();
    }

    private async Task AssertRenamedAndKeptAsync()
    {
        Game game = (await library.Repository.GetGameAsync(before.Id))!;
        Assert.Equal("Renamed Linked Game", game.Title);
        Assert.Equal(before.Link, game.Link);
        Assert.Equal(GameMetadataJson.Serialize(before.Metadata!), GameMetadataJson.Serialize(game.Metadata!));
        Assert.Equal(before.ArtworkRelativePath, game.ArtworkRelativePath);
        Assert.NotNull(new ManagedArtworkStore(library.Paths).ResolveFile(game.ArtworkRelativePath!));
        Assert.Equal(before.CreatedUtc, game.CreatedUtc);

        Assert.Equal(
            new[] { alphaId, betaId }.Order(),
            (await library.Repository.ListGuidesAsync(before.Id)).Select(guide => guide.Id).Order());
        ReadingState alpha = (await library.Repository.GetReadingStateAsync(alphaId))!;
        Assert.Equal("{\"offset\":12}", alpha.LocatorJson);
        Assert.Equal(0.45, alpha.EstimatedFraction);
        Assert.Equal(betaId, (await library.Repository.GetSettingsAsync()).LastActiveGuideId);

        LibraryGameSummary summary = Assert.Single(await library.Repository.ListGameSummariesAsync());
        Assert.Equal((before.Id, "Renamed Linked Game", 2), (summary.Game.Id, summary.Game.Title, summary.GuideCount));
    }
}
```

`tests/DesktopGuides.Infrastructure.Tests/EmptyGameRemovalTests.cs`:

```csharp
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class EmptyGameRemovalTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;
    private ManagedArtworkStore store = null!;

    public async Task InitializeAsync()
    {
        library = await RemovalLibrary.CreateAsync();
        store = new ManagedArtworkStore(library.Paths);
    }

    public async Task DisposeAsync() => await library.DisposeAsync();

    private async Task<Game> AddLinkedAsync(string externalId)
    {
        Guid id = Guid.NewGuid();
        StoredArtwork cover = await store.StoreAsync(id, Artwork.TestImages.Png(4, 4), default);
        return await library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            id, "Empty Linked Game", "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, externalId, DateTimeOffset.UnixEpoch),
            GameMetadataJsonTests.Sample(), cover.RelativePath));
    }

    private GameRemover Remover() => new(library.Repository, store);

    [Fact]
    public async Task AManualGameWithoutGuidesIsRemoved()
    {
        Guid id = await library.AddGameAsync("Empty Manual Game");

        EmptyGameRemoval removal = await library.Repository.RemoveEmptyGameAsync(id);

        Assert.Equal(new EmptyGameRemoval(EmptyGameRemovalOutcome.Removed, null), removal);
        Assert.Null(await library.Repository.GetGameAsync(id));
    }

    [Fact]
    public async Task ALinkedGameIsRemovedWithItsArtworkPathAndCanBeAddedAgain()
    {
        Game game = await AddLinkedAsync("900200");

        EmptyGameRemoval removal = await library.Repository.RemoveEmptyGameAsync(game.Id);

        Assert.Equal(new EmptyGameRemoval(EmptyGameRemovalOutcome.Removed, game.ArtworkRelativePath), removal);
        Assert.Null(await library.Repository.FindLinkedGameAsync(ProviderGameLink.Igdb, "900200"));
        Game again = await AddLinkedAsync("900200");
        Assert.NotEqual(game.Id, again.Id);
    }

    [Fact]
    public async Task AGameWithAGuideIsKeptWithItsGuideAndState()
    {
        Guid id = await library.AddGameAsync("Guided Game");
        Guid guideId = await library.AddGuideAsync(id, "Walkthrough");
        await library.Repository.SaveReadingLocationAsync(guideId, "{\"offset\":3}", 0.45);

        EmptyGameRemoval removal = await library.Repository.RemoveEmptyGameAsync(id);

        Assert.Equal(new EmptyGameRemoval(EmptyGameRemovalOutcome.HasGuides, null), removal);
        Assert.NotNull(await library.Repository.GetGameAsync(id));
        Assert.Equal("1|1|1", library.RowsFor(guideId));
        Assert.Equal(0.45, (await library.Repository.GetReadingStateAsync(guideId))!.EstimatedFraction);
    }

    [Fact]
    public async Task AnUnknownGameIsNotFound() =>
        Assert.Equal(
            new EmptyGameRemoval(EmptyGameRemovalOutcome.NotFound, null),
            await library.Repository.RemoveEmptyGameAsync(Guid.NewGuid()));

    [Fact]
    public async Task AnEmptyIdIsRejected() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => library.Repository.RemoveEmptyGameAsync(Guid.Empty));

    [Fact]
    public async Task OtherGamesAreUntouched()
    {
        Guid removed = await library.AddGameAsync("Removed Game");
        Guid kept = await library.AddGameAsync("Kept Game");
        Guid keptGuide = await library.AddGuideAsync(kept, "Kept Guide");

        await library.Repository.RemoveEmptyGameAsync(removed);

        Assert.Equal([kept], (await library.Repository.ListGamesAsync()).Select(game => game.Id));
        Assert.Equal("1|1|1", library.RowsFor(keptGuide));
    }

    [Fact]
    public async Task TheRemoverDeletesTheArtworkFileAndItsFolder()
    {
        Game game = await AddLinkedAsync("900300");
        string file = store.ResolveFile(game.ArtworkRelativePath!)!;

        Assert.Equal(EmptyGameRemovalOutcome.Removed, await Remover().RemoveAsync(game.Id));

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
    }

    [Fact]
    public async Task ArtworkThatCannotBeDeletedIsSweptAtTheNextStart()
    {
        Game game = await AddLinkedAsync("900400");
        string file = store.ResolveFile(game.ArtworkRelativePath!)!;
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(EmptyGameRemovalOutcome.Removed, await Remover().RemoveAsync(game.Id));
            Assert.True(File.Exists(file));
        }

        await library.RestartAsync();

        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task AGuidePublishedIntoARemovedGameFailsAndLeavesNothing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        Assert.Equal(
            EmptyGameRemovalOutcome.Removed,
            (await harness.Repository.RemoveEmptyGameAsync(harness.Game.Id)).Outcome);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        harness.AssertNothingLeft();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: the Infrastructure tests, filtered to `GameRenameTests`, then to
`EmptyGameRemovalTests`.
Expected: `GameRenameTests` PASS, because rename already keeps the ID
(the spec's "proof, not new UI"). Read the output to confirm both tests
ran. `EmptyGameRemovalTests` FAIL with `NotSupportedException` from the
stub, except `AnEmptyIdIsRejected`, which fails because a
`NotSupportedException` is not an `ArgumentException`. If a rename test
fails, the failure is a real defect: fix it in `UpdateGameAsync` and
ledger it.

- [ ] **Step 3: Write the minimal implementation**

Replace the stub in `SqliteLibraryRepository.cs`:

```csharp
    public Task<EmptyGameRemoval> RemoveEmptyGameAsync(
        Guid gameId, CancellationToken token = default)
    {
        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("A game ID is required.", nameof(gameId));
        }
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            string? artwork;
            using (SqliteCommand read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = """
                    SELECT ArtworkRelativePath,
                           (SELECT COUNT(*) FROM Guides WHERE GameId = $id)
                    FROM Games WHERE Id = $id
                    """;
                read.Parameters.AddWithValue("$id", gameId.ToString("N"));
                using SqliteDataReader reader = read.ExecuteReader();
                if (!reader.Read())
                {
                    return new EmptyGameRemoval(EmptyGameRemovalOutcome.NotFound, null);
                }
                if (reader.GetInt64(1) > 0)
                {
                    return new EmptyGameRemoval(EmptyGameRemovalOutcome.HasGuides, null);
                }
                artwork = NullableString(reader, 0);
            }

            // Guides cascade on delete; the guard keeps them out of reach.
            using SqliteCommand delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM Games
                WHERE Id = $id AND NOT EXISTS (SELECT 1 FROM Guides WHERE GameId = $id)
                """;
            delete.Parameters.AddWithValue("$id", gameId.ToString("N"));
            RequireUpdated(delete.ExecuteNonQuery(), "game");
            transaction.Commit();
            return new EmptyGameRemoval(EmptyGameRemovalOutcome.Removed, artwork);
        }, token);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the filtered `EmptyGameRemovalTests` and `GameRenameTests`, then the
full Infrastructure and Core suites.
Expected: all PASS.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Infrastructure.Tests/GameRenameTests.cs tests/DesktopGuides.Infrastructure.Tests/EmptyGameRemovalTests.cs
git commit -m "feat(storage): remove a game only while it has no guides"
```

## Task 3: Game page Remove game action

TDD skip: this task is XAML and event glue. The removal rules and copy are
covered by Tasks 1 and 2, and Task 4's installed smoke is this task's gate.

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (Game header,
  around lines 222–245)
- Create: `src/DesktopGuides.Production/RemoveGameDialog.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`

**Interfaces:**
- Consumes: `GameRemover`, `EmptyGameRemovalOutcome` and
  `GameRemovalPresentation` from Task 1; the real `RemoveEmptyGameAsync`
  from Task 2.
- Produces, for Task 4: the AutomationIds `RemoveGameButton`,
  `RemoveGameHint`, `RemoveGameDialog` and `RemoveGameMessage`; the
  button's HelpText; the statuses in Global Constraints; focus on Remove
  game after Cancel and on Add game after a removal.

- [ ] **Step 1: Add the button and the hint to the Game header**

In `ShellWindow.xaml`, wrap the header `Grid` (the one holding
`GameHeading`, `RefreshMetadataButton` and `EditGameButton`) in a
`StackPanel`, add a fourth column, and add the button and the hint. The
`StackPanel` takes the header's place in row 0 of the Game panel grid:

```xml
                <StackPanel Spacing="{StaticResource DesktopGuidesSpacing4}">
                    <Grid ColumnSpacing="{StaticResource DesktopGuidesSpacing16}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <TextBlock x:Name="GameHeading"
                                   Style="{StaticResource DesktopGuidesPageTitleStyle}"
                                   AutomationProperties.AutomationId="GameHeading" />
                        <Button x:Name="RefreshMetadataButton"
                                Grid.Column="1"
                                Content="Refresh metadata"
                                Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"
                                VerticalAlignment="Center"
                                Visibility="Collapsed"
                                Click="RefreshMetadataClicked"
                                AutomationProperties.AutomationId="RefreshMetadataButton" />
                        <Button x:Name="EditGameButton"
                                Grid.Column="2"
                                Content="Edit game"
                                Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"
                                VerticalAlignment="Center"
                                Click="EditGameClicked"
                                AutomationProperties.AutomationId="EditGameButton" />
                        <Button x:Name="RemoveGameButton"
                                Grid.Column="3"
                                Content="Remove game"
                                Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"
                                VerticalAlignment="Center"
                                IsEnabled="False"
                                Click="RemoveGameClicked"
                                AutomationProperties.AutomationId="RemoveGameButton" />
                    </Grid>
                    <TextBlock x:Name="RemoveGameHint"
                               Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                               HorizontalAlignment="Right"
                               TextWrapping="Wrap"
                               Visibility="Collapsed"
                               AutomationProperties.AutomationId="RemoveGameHint" />
                </StackPanel>
```

- [ ] **Step 2: Add the confirmation dialog**

Create `src/DesktopGuides.Production/RemoveGameDialog.cs`:

```csharp
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

/// <summary>Confirms removing a game without guides. Cancel is the default, so Enter and Escape both cancel.</summary>
internal static class RemoveGameDialog
{
    public static ContentDialog Create(string title, XamlRoot root)
    {
        TextBlock message = new()
        {
            Text = GameRemovalPresentation.DialogBody,
            Style = (Style)Application.Current.Resources["DesktopGuidesBodyStyle"],
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(message, "RemoveGameMessage");
        ContentDialog dialog = new()
        {
            Title = GameRemovalPresentation.DialogTitle(title),
            Content = message,
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        AutomationProperties.SetAutomationId(dialog, "RemoveGameDialog");
        return dialog;
    }
}
```

Match `RemoveGuideDialog.cs` line for line where the two agree.

- [ ] **Step 3: Wire the action in the code-behind**

In `ShellWindow.xaml.cs`:

1. After `private ContentDialog? activeRemoveDialog;`, add:

```csharp
    private GameRemover? gameRemover;
    private bool gameRemoveRequested;
    // Null while the Game page loads, so Remove game stays disabled until the count is known.
    private int? loadedGameGuideCount;
```

2. In the constructor, after `ArtworkListLoader.NameRows(GuideList);`, add:

```csharp
        RemoveGameHint.Text = GameRemovalPresentation.GuidesFirst;
```

3. In `InitializeCoreAsync`, after `guideRemover = new GuideRemover(repository, paths);`, add:

```csharp
            gameRemover = new GameRemover(repository, artwork);
```

4. Add the helper next to `UpdateOpenSelectedGuideAction`:

```csharp
    private void UpdateRemoveGameAction()
    {
        bool hasGuides = loadedGameGuideCount > 0;
        RemoveGameButton.IsEnabled = loadedGameGuideCount == 0 && !closeRequested &&
            !importRequested && !gameEditorRequested && !removeRequested &&
            !gameRemoveRequested && refreshCancel is null;
        RemoveGameHint.Visibility = hasGuides ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetHelpText(
            RemoveGameButton, hasGuides ? GameRemovalPresentation.GuidesFirst : string.Empty);
    }
```

5. In `RenderCurrentAsync`, the top block hides the panels. Edit the lines
   that read (the pair is unique; line 519 has the first line alone):

```csharp
        RemoveSelectedGuideButton.Visibility = Visibility.Collapsed;
        AppTitleBar.IsBackButtonEnabled = navigator.CanGoBack;
```

to:

```csharp
        RemoveSelectedGuideButton.Visibility = Visibility.Collapsed;
        loadedGameGuideCount = null;
        UpdateRemoveGameAction();
        AppTitleBar.IsBackButtonEnabled = navigator.CanGoBack;
```

6. In the `GameRoute` case of the same method, edit:

```csharp
                    EditGameButton.IsEnabled = true;
                    ImportGuideButton.IsEnabled = !importRequested;
                    GuideRowItem? selectedGuide = selectedGuideId is Guid id
```

to:

```csharp
                    EditGameButton.IsEnabled = true;
                    ImportGuideButton.IsEnabled = !importRequested;
                    loadedGameGuideCount = guides.Count;
                    UpdateRemoveGameAction();
                    GuideRowItem? selectedGuide = selectedGuideId is Guid id
```

7. Replace the `finally` block of `EditGameClicked` (ruling 4 and ruling
   18):

```csharp
        finally
        {
            gameEditorRequested = false;
            if (!closeRequested && navigator.Current is GameRoute shown)
            {
                EditGameButton.IsEnabled = true;
                // The button was disabled while the dialog was open, so WinUI could not return focus to it.
                if (shown.GameId == route.GameId)
                {
                    EditGameButton.Focus(FocusState.Programmatic);
                }
            }
            if (!closeRequested)
            {
                UpdateRemoveGameAction();
            }
        }
```

8. At the end of the `finally` blocks of `ImportGuideClicked` (after its
   `if (!closeRequested && navigator.Current is GameRoute) { ... }` block)
   and `RemoveSelectedGuideClicked` (after its
   `if (!closeRequested && navigator.Current is GameRoute shown && ...)`
   block), add:

```csharp
            if (!closeRequested)
            {
                UpdateRemoveGameAction();
            }
```

   Each of those flows can render the Game page while its own flag is
   still set, so the button is recomputed once the flag clears.

9. In `RefreshMetadataAsync` (ruling 12), change:

```csharp
        refreshCancel = cancel;
        RefreshMetadataButton.IsEnabled = false;
```

to:

```csharp
        refreshCancel = cancel;
        RefreshMetadataButton.IsEnabled = false;
        UpdateRemoveGameAction();
```

and its `finally` from:

```csharp
            refreshCancel = null;
            // Re-enable here: the user may have moved to another game, which won't re-render.
            if (!closeRequested) RefreshMetadataButton.IsEnabled = true;
```

to:

```csharp
            refreshCancel = null;
            // Re-enable here: the user may have moved to another game, which won't re-render.
            if (!closeRequested)
            {
                RefreshMetadataButton.IsEnabled = true;
                UpdateRemoveGameAction();
            }
```

10. Add the handler after `ShowRemovalError`:

```csharp
    private async void RemoveGameClicked(object sender, RoutedEventArgs args)
    {
        if (gameRemoveRequested || closeRequested || navigator.Current is not GameRoute route)
        {
            return;
        }
        gameRemoveRequested = true;
        string shownTitle = GameHeading.Text;
        EditGameButton.IsEnabled = false;
        RefreshMetadataButton.IsEnabled = false;
        ImportGuideButton.IsEnabled = false;
        RemoveGameButton.IsEnabled = false;
        bool rendered = false;
        try
        {
            await RunNavigationAsync(async () =>
            {
                if (closeRequested ||
                    navigator.Current is not GameRoute current ||
                    current.GameId != route.GameId)
                {
                    return;
                }
                GameRemover remover = gameRemover
                    ?? throw new InvalidOperationException("The library is not ready.");
                Game? game;
                try
                {
                    game = await RequireRepository().GetGameAsync(route.GameId);
                }
                catch (Exception)
                {
                    if (!closeRequested)
                    {
                        ShowErrorStatus(GameRemovalPresentation.Failed(shownTitle));
                    }
                    return;
                }
                if (closeRequested)
                {
                    return;
                }
                string title = game?.Title ?? shownTitle;
                EmptyGameRemovalOutcome outcome;
                if (game is null)
                {
                    // Removed elsewhere before the dialog (ruling 14).
                    outcome = EmptyGameRemovalOutcome.NotFound;
                }
                else
                {
                    ContentDialog dialog = RemoveGameDialog.Create(game.Title, Navigation.XamlRoot);
                    DialogSurface.Apply(dialog, EffectiveMaterial);
                    activeRemoveDialog = dialog;
                    ContentDialogResult choice;
                    try
                    {
                        choice = await dialog.ShowAsync();
                    }
                    finally
                    {
                        activeRemoveDialog = null;
                    }
                    if (choice != ContentDialogResult.Primary || closeRequested)
                    {
                        return;
                    }
                    try
                    {
                        outcome = await remover.RemoveAsync(game.Id);
                    }
                    catch (Exception)
                    {
                        if (!closeRequested)
                        {
                            ShowErrorStatus(GameRemovalPresentation.Failed(title));
                        }
                        return;
                    }
                    if (closeRequested)
                    {
                        return;
                    }
                }
                rendered = true;
                if (outcome == EmptyGameRemovalOutcome.HasGuides)
                {
                    await RenderCurrentAsync();
                    ShowWarningStatus(GameRemovalPresentation.HasGuides(title));
                    EditGameButton.Focus(FocusState.Programmatic);
                    return;
                }
                // Clearing the back stack keeps Back from reaching the removed page.
                navigator.ResetToLibrary();
                await RenderCurrentAsync();
                ShowTransientStatus(outcome == EmptyGameRemovalOutcome.Removed
                    ? GameRemovalPresentation.Removed(title)
                    : GameRemovalPresentation.AlreadyRemoved(title));
                AddGameButton.Focus(FocusState.Programmatic);
            });
        }
        finally
        {
            gameRemoveRequested = false;
            if (!closeRequested)
            {
                bool restore = !rendered &&
                    navigator.Current is GameRoute shown && shown.GameId == route.GameId;
                if (restore)
                {
                    EditGameButton.IsEnabled = true;
                    ImportGuideButton.IsEnabled = !importRequested;
                    RefreshMetadataButton.IsEnabled = refreshCancel is null;
                }
                UpdateRemoveGameAction();
                if (restore)
                {
                    RemoveGameButton.Focus(FocusState.Programmatic);
                }
            }
        }
    }
```

The close handler already hides `activeRemoveDialog`, so closing the window
while this dialog is open resolves it as Cancel (Review Focus 1). The Library
render ends with `Library ready.`; the removal status comes after it (ruling
10).

- [ ] **Step 4: Build and run the suites**

Run: the Production build, then the full Core and Infrastructure suites.
Expected: the build succeeds with no new warnings, and both suites PASS.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Production/ShellWindow.xaml src/DesktopGuides.Production/ShellWindow.xaml.cs src/DesktopGuides.Production/RemoveGameDialog.cs
git commit -m "feat(shell): remove a game without guides from the Game page"
```

## Task 4: Seed, installed smoke and verification

TDD note: the smoke is written before the run that exercises Task 3. Step 6
is its RED/GREEN gate: the CI run fails if Task 3's AutomationIds, copy,
enabled states or focus are wrong, or if the rename loses anything bound to
the Game ID.

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (a new two-argument
  block beside `describe-import`; arg validation and usage around line
  310; a new block before `seed-import`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`
- Modify: `tools/p1/windows_shell_install.ps1`

**Interfaces:**
- Consumes: Task 3's AutomationIds, copy and focus targets (Global
  Constraints and Task 3's Produces).
- Produces: the seed modes `seed-actions` and `describe-actions`; the smoke
  modes `game-actions` and `game-actions-persisted`; the result names
  `game-actions-light`, `game-actions-dark` and `game-actions-persisted`;
  the screenshots `remove-game-hint` and `remove-game-confirm`; the install
  switch `-GameActionsOnly`.

- [ ] **Step 1: Add the `seed-actions` and `describe-actions` modes**

In `Program.cs`, after the `describe-import` block, add:

```csharp
if (args.Length == 2 && args[0] == "describe-actions")
{
    // Read-only, without InitializeAsync: its artwork sweep would hide a
    // removed game's leftover artwork folder (ruling 20).
    ManagedPathResolver actionsPaths = new(args[1]);
    using SqliteConnection actionsConnection = new(new SqliteConnectionStringBuilder
    {
        DataSource = actionsPaths.DatabasePath,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString());
    actionsConnection.Open();
    List<T> Rows<T>(string sql, Func<SqliteDataReader, T> read)
    {
        using SqliteCommand command = actionsConnection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        List<T> rows = [];
        while (reader.Read())
        {
            rows.Add(read(reader));
        }
        return rows;
    }
    var actionsGames = Rows(
        "SELECT Id, Title, ProviderGameId, ArtworkRelativePath FROM Games ORDER BY Title",
        reader => new
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            ExternalId = reader.IsDBNull(2) ? null : reader.GetString(2),
            ArtworkRelativePath = reader.IsDBNull(3) ? null : reader.GetString(3),
        });
    var actionsGuides = Rows(
        "SELECT GameId, Id FROM Guides",
        reader => (GameId: reader.GetString(0), GuideId: reader.GetString(1)));
    ManagedArtworkStore actionsStore = new(actionsPaths);
    string[] artworkFolders = Directory.Exists(actionsPaths.ArtworkRoot)
        ? [.. Directory.EnumerateDirectories(actionsPaths.ArtworkRoot).Select(folder => Path.GetFileName(folder))]
        : [];
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        GameCount = actionsGames.Count,
        Games = actionsGames.Select(row => new
        {
            row.Id,
            row.Title,
            row.ExternalId,
            row.ArtworkRelativePath,
            ArtworkExists = row.ArtworkRelativePath is { } path && actionsStore.ResolveFile(path) is not null,
            GuideIds = actionsGuides.Where(guide => guide.GameId == row.Id).Select(guide => guide.GuideId),
        }),
        ArtworkFolders = artworkFolders,
        ReadingStates = Rows(
            "SELECT GuideId, EstimatedFraction, LastOpenedUtcMs FROM ReadingStates",
            reader => new
            {
                GuideId = reader.GetString(0),
                EstimatedFraction = reader.IsDBNull(1) ? (double?)null : reader.GetDouble(1),
                LastOpenedUtcMs = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2),
            }),
        LastActiveGuideId = Rows(
            "SELECT Value FROM Settings WHERE Key = 'LastActiveGuideId'",
            reader => reader.GetString(0)).FirstOrDefault(),
    }));
    return 0;
}
```

Extend the main-mode check and the usage text:

```csharp
if (args.Length != 2 ||
    args[0] is not ("seed" or "stale" or "seed-long" or "seed-second" or
        "seed-design" or "seed-catalog" or "seed-facts" or "seed-search" or "seed-import" or
        "seed-actions"))
{
    Console.Error.WriteLine(
        "Usage: DesktopGuides.ShellSeed seed|stale|seed-long|seed-second|seed-design|seed-catalog|seed-facts|seed-search|seed-import|seed-actions " +
        "<app-data-root> " +
        "or seed-linked-game|describe-providers|describe-import|describe-actions <app-data-root> " +
```

(the remaining usage lines stay as they are). Before the `seed-import`
block, add:

```csharp
if (args[0] == "seed-actions")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The game actions seed needs an empty library.");
    }
    long actionsNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    DateTime actionsYesterday = TimeZoneInfo.ConvertTime(
        DateTimeOffset.FromUnixTimeMilliseconds(actionsNow), TimeZoneInfo.Local).Date.AddDays(-1).AddHours(12);
    long alphaOpened = new DateTimeOffset(
        actionsYesterday, TimeZoneInfo.Local.GetUtcOffset(actionsYesterday)).ToUnixTimeMilliseconds();
    ManagedArtworkStore actionsArtwork = new(paths);
    async Task<Guid> AddActionsGameAsync(string title, string externalId, byte[] cover)
    {
        Guid id = Guid.NewGuid();
        StoredArtwork stored = await actionsArtwork.StoreAsync(id, cover, CancellationToken.None);
        await repository.AddLinkedGameAsync(new NewLinkedGame(
            id, title, "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, externalId, DateTimeOffset.UtcNow),
            new GameMetadataSnapshot(
                GameMetadataSnapshot.CurrentSchemaVersion,
                "A seeded summary for the game actions check.",
                null, [], [], [], [], null, GameTypeTag.MainGame),
            stored.RelativePath), CancellationToken.None);
        return id;
    }

    Guid renameGameId = await AddActionsGameAsync(
        "Linked Rename Game", "900100", SolidPng(60, 90, 0x2E, 0x5E, 0x8C));
    Guid emptyGameId = await AddActionsGameAsync(
        "Empty Linked Game", "900101", SolidPng(60, 90, 0x8C, 0x4A, 0x2E));
    Guid alphaId = Guid.NewGuid();
    Guid betaId = Guid.NewGuid();
    await InsertGuideAsync(paths, renameGameId, alphaId, "Alpha Route Guide", actionsNow);
    await InsertGuideAsync(paths, renameGameId, betaId, "Beta Route Guide", actionsNow);
    // No writer for reading state exists yet (T12.3, T13.2), so set it here.
    ExecuteSql(paths, $"""
        UPDATE ReadingStates SET EstimatedFraction = 0.45, LastOpenedUtcMs = {alphaOpened}
            WHERE GuideId = '{alphaId:N}';
        """);
    AppSettings actionsSettings = await repository.GetSettingsAsync();
    await repository.SaveSettingsAsync(actionsSettings with { LastActiveGuideId = betaId });
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        RenameGameId = renameGameId.ToString("N"),
        EmptyGameId = emptyGameId.ToString("N"),
        AlphaGuideId = alphaId.ToString("N"),
        BetaGuideId = betaId.ToString("N"),
        AlphaLastOpenedUtcMs = alphaOpened,
    }));
    return 0;
}
```

The names avoid the top-level `game`, `settings`, `now` and `paths`
locals declared later in the file, which a nested declaration would
conflict with (CS0136).

- [ ] **Step 2: Add the smoke modes**

In `windows_shell_ui_smoke.ps1`:

1. Add `'game-actions', 'game-actions-persisted'` to the `-Mode`
   `ValidateSet`, after `'provider-live', 'provider-remove'`. Do not pass
   `-ExpectedResumeGuide`: its `ValidateSet` allows only the default seed's
   guides, so the modes name `Beta Route Guide` themselves.

2. Before `elseif ($Mode -eq 'long-list')`, add:

```powershell
    elseif ($Mode -like 'game-actions*') {
        $renameTitle = 'Linked Rename Game'
        $renamed = 'Renamed Linked Game'
        $emptyTitle = 'Empty Linked Game'
        $hint = "Remove this game's guides first."
        $summary = 'A seeded summary for the game actions check.'

        # The title-bar Back button stays visible on the Library, disabled.
        function Test-BackEnabled {
            $back = Find-ById 'PART_BackButton'
            return [bool]($back -and -not $back.Current.IsOffscreen -and $back.Current.IsEnabled)
        }

        # Finds the dialog title whether UIA names the dialog or its title text.
        function Wait-VisibleName([string] $name) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $element = Find-ByName $name
                if ($element -and -not $element.Current.IsOffscreen) {
                    return $element
                }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            throw "Expected a visible element named '$name'."
        }

        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        if ($Mode -eq 'game-actions') {
            $before = (Wait-GameRow $renameTitle).Current.HelpText
            Select-Element $renameTitle
            [void](Wait-Name 'GameHeading' $renameTitle)
            [void](Wait-Status 'Game ready.')
            $remove = Wait-VisibleById 'RemoveGameButton'
            if ($remove.Current.IsEnabled) {
                throw 'Remove game was enabled for a game with guides.'
            }
            if ($remove.Current.HelpText -ne $hint) {
                throw "Remove game's HelpText was '$($remove.Current.HelpText)'."
            }
            [void](Wait-Name 'RemoveGameHint' $hint)
            $report.hintScreenshot = Save-WindowScreenshot 'remove-game-hint'
            $report.phases += 'remove-disabled-with-guides'

            # Selecting a guide opens it (ruling 5), so come back to the game
            # with it selected before renaming.
            Open-GuideFromGame 'Beta Route Guide'
            [void](Wait-Name 'ReaderHeading' 'Beta Route Guide')
            [void](Wait-Status 'Guide details ready.')
            Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
            [void](Wait-Name 'GameHeading' $renameTitle)
            [void](Wait-Status 'Game ready.')
            [void](Wait-SelectedGuide 'Beta Route Guide')
            Wait-FocusedGuide 'Beta Route Guide'
            Invoke-Element (Wait-EnabledById 'EditGameButton')
            Set-Text 'GameTitleInput' $renamed
            Press-Enter (Wait-VisibleById 'GameTitleInput')
            [void](Wait-Name 'GameHeading' $renamed)
            [void](Wait-Status 'Game ready.')
            [void](Wait-SelectedGuide 'Beta Route Guide')
            Wait-FocusedId 'EditGameButton'
            [void](Wait-Name 'GameSummary' $summary)
            $report.phases += 'rename-keeps-selection'

            Go-Back
            [void](Wait-Name 'LibraryHeading' 'Library')
            [void](Wait-Status 'Library ready.')
            $after = (Wait-GameRow $renamed).Current.HelpText
            if ($after -ne $before) {
                throw "The renamed row's facts were '$after'; before the rename they were '$before'."
            }
            if ((Count-GameRows $renameTitle) -ne 0) {
                throw "A row still had the old title '$renameTitle'."
            }
            $report.phases += 'rename-library-row'

            # A query that only the empty game matches; the Library must
            # reapply it after the removal (Review Focus 4).
            Set-SearchQuery 'Empty' 'LibrarySearchInput'
            [void](Wait-Status '1 of 2 games match.' -AllowHidden)
            [void](Wait-GameRow $emptyTitle)
            if ((Count-GameRows $renamed) -ne 0) {
                throw "The query 'Empty' still listed '$renamed'."
            }
            Select-Element $emptyTitle
            [void](Wait-Name 'GameHeading' $emptyTitle)
            [void](Wait-Status 'Game ready.')
            $remove = Wait-EnabledById 'RemoveGameButton'
            if ($remove.Current.HelpText) {
                throw "Remove game's HelpText was '$($remove.Current.HelpText)' for a game without guides."
            }
            Assert-Absent 'RemoveGameHint'
            if (-not (Test-BackEnabled)) {
                throw 'Back was unavailable on the Game page before the removal.'
            }
            Invoke-Element $remove
            [void](Wait-VisibleById 'RemoveGameDialog')
            [void](Wait-VisibleName "Remove $emptyTitle?")
            [void](Wait-Name 'RemoveGameMessage' 'This removes the game and its details from Desktop Guides.')
            [void](Wait-Name 'PrimaryButton' 'Remove')
            [void](Wait-Name 'CloseButton' 'Cancel')
            $report.dialogScreenshot = Save-WindowScreenshot 'remove-game-confirm'
            $report.phases += 'remove-confirm'

            [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
            [void](Wait-HiddenById 'RemoveGameDialog')
            Wait-FocusedId 'RemoveGameButton'
            [void](Wait-Name 'GameHeading' $emptyTitle)
            $report.phases += 'remove-escape-cancels'

            # Cancel is the default button (ruling 11), so Enter cancels too.
            Invoke-Element (Wait-EnabledById 'RemoveGameButton')
            [void](Wait-VisibleById 'RemoveGameDialog')
            Wait-FocusedId 'CloseButton'
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            [void](Wait-HiddenById 'RemoveGameDialog')
            Wait-FocusedId 'RemoveGameButton'
            [void](Wait-Name 'GameHeading' $emptyTitle)
            $report.phases += 'remove-enter-cancels'

            Invoke-Element (Wait-EnabledById 'RemoveGameButton')
            [void](Wait-VisibleById 'RemoveGameDialog')
            Invoke-Element (Wait-EnabledById 'PrimaryButton')
            [void](Wait-HiddenById 'RemoveGameDialog')
            [void](Wait-Name 'LibraryHeading' 'Library')
            [void](Wait-Status "Removed $emptyTitle.")
            if ((Get-SearchText 'LibrarySearchInput') -ne 'Empty') {
                throw "The Library query was '$(Get-SearchText 'LibrarySearchInput')' after the removal."
            }
            [void](Wait-Name 'LibraryNoResults' 'No games or guides match "Empty".')
            [void](Wait-HiddenById 'GameList')
            Wait-FocusedId 'AddGameButton'
            if (Test-BackEnabled) {
                throw 'Back was available after the removal.'
            }
            Set-SearchQuery '' 'LibrarySearchInput'
            [void](Wait-GameRow $renamed)
            if ((Count-GameRows $emptyTitle) -ne 0) {
                throw "The removed game '$emptyTitle' is still listed."
            }
            $report.phases += 'removed'
        }
        else {
            [void](Wait-GameRow $renamed)
            Invoke-Element (Wait-Name 'ResumeGuide' 'Resume Beta Route Guide')
            [void](Wait-Name 'ReaderHeading' 'Beta Route Guide')
            [void](Wait-Name 'ReaderGameName' $renamed)
            [void](Wait-Status 'Guide details ready.')
            $report.phases += 'persisted-resume'

            Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
            [void](Wait-Name 'GameHeading' $renamed)
            [void](Wait-Status 'Game ready.')
            [void](Wait-SelectedGuide 'Beta Route Guide')
            $report.phases += 'persisted-selection'

            $alpha = (Wait-GuideRow 'Alpha Route Guide').Current.HelpText
            if ($alpha -notlike '*about 45 percent*') {
                throw "Alpha Route Guide's facts were '$alpha'."
            }
            [void](Wait-Name 'GameSummary' $summary)
            [void](Wait-VisibleById 'GameCover')
            $report.phases += 'persisted-facts'

            Assert-NoRemoteConnections 'game page'
            $report.phases += 'persisted-no-provider-traffic'
        }
    }
```

3. In the failure-inspection `catch` near the end, add
   `-or $Mode -like 'game-actions*'` to the mode condition, and add
   `'RemoveGameButton', 'RemoveGameHint', 'RemoveGameDialog',
   'RemoveGameMessage'` to the ID list after `'RemoveSelectedGuide'`.

4. In the `design-language` mode (ruling 17), after
   `Assert-InsideWindow 'EditGameButton'` in both the wide block (around
   line 1171) and the narrow block (around line 1178), add:

```powershell
        Assert-InsideWindow 'RemoveGameButton'
        Assert-InsideWindow 'RemoveGameHint'
```

   The design game has three guides, so the hint is visible at both widths.

- [ ] **Step 3: Run the modes from the install script**

In `windows_shell_install.ps1`:

1. After `[switch] $ImportOnly,`, add `[switch] $GameActionsOnly,`
   (ruling 13).

2. In `Run-ShellSmoke`, change the timeout line (ruling 19) to:

```powershell
        elseif ($mode -like 'catalog*' -or $mode -like 'import-*' -or $mode -like 'game-actions*') { 120 }
```

3. After `Run-RemovalScenarios`, add:

```powershell
function Assert-GameActionsState([string] $label, $seed) {
    $state = Invoke-ShellSeed @('describe-actions', $dataRoot) | ConvertFrom-Json
    if ($state.GameCount -ne 1) {
        throw "$label, the library had $($state.GameCount) games; expected 1."
    }
    $game = @($state.Games)[0]
    if ($game.Id -ne $seed.RenameGameId -or $game.Title -ne 'Renamed Linked Game' -or
        $game.ExternalId -ne '900100' -or -not $game.ArtworkExists) {
        throw "$label, the remaining game was $($game | ConvertTo-Json -Compress)."
    }
    if (@($state.ArtworkFolders) -contains $seed.EmptyGameId) {
        throw "$label, the removed game's artwork folder remains."
    }
    $guides = @(@($game.GuideIds) | Sort-Object)
    $expected = @(@($seed.AlphaGuideId, $seed.BetaGuideId) | Sort-Object)
    if (($guides -join ',') -ne ($expected -join ',')) {
        throw "$label, the guide IDs were $($guides -join ', ')."
    }
    $alpha = @($state.ReadingStates | Where-Object { $_.GuideId -eq $seed.AlphaGuideId })
    if ($alpha.Count -ne 1 -or $alpha[0].EstimatedFraction -ne 0.45 -or
        $alpha[0].LastOpenedUtcMs -ne $seed.AlphaLastOpenedUtcMs) {
        throw "$label, Alpha Route Guide's reading state was $($alpha | ConvertTo-Json -Compress)."
    }
    if ($state.LastActiveGuideId -ne $seed.BetaGuideId) {
        throw "$label, the Resume guide was $($state.LastActiveGuideId)."
    }
    return $state
}

function Run-GameActionsScenarios {
    $originalTheme = Get-AppThemePreference
    try {
        $seed = Invoke-ShellSeed @('seed-actions', $dataRoot) | ConvertFrom-Json
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.gameActionsLight = Run-ShellSmoke 'game-actions' -ResultName 'game-actions-light'
        Close-InstalledShell
        $report.gameActionsLightState = Assert-GameActionsState 'After the light run' $seed

        Start-InstalledShell
        $report.gameActionsPersisted = Run-ShellSmoke 'game-actions-persisted'
        Close-InstalledShell
        $report.gameActionsPersistedState = Assert-GameActionsState 'After the relaunch' $seed

        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        $seed = Invoke-ShellSeed @('seed-actions', $dataRoot) | ConvertFrom-Json
        Set-AppThemePreference $false
        Start-InstalledShell
        $report.gameActionsDark = Run-ShellSmoke 'game-actions' -ResultName 'game-actions-dark'
        Close-InstalledShell
        $report.gameActionsDarkState = Assert-GameActionsState 'After the dark run' $seed
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}
```

4. After the `if ($ImportOnly) { ... }` block, add:

```powershell
    if ($GameActionsOnly) {
        Run-GameActionsScenarios
        $report.success = $true
        return
    }
```

5. In the full run, between `Run-RemovalScenarios` and the wipe before
   `Run-ProviderScenarios`, add:

```powershell
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-GameActionsScenarios
```

- [ ] **Step 4: Build and parse**

Run: the seed build and the Production build; the PowerShell parse check
over both scripts; then the full Core and Infrastructure suites.
Expected: both builds succeed with no new warnings, both scripts parse
with no errors, and both suites PASS.

- [ ] **Step 5: Check the scripts are ASCII**

Run: `LC_ALL=C grep -n '[^ -~	]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1`
Expected: no output.

- [ ] **Step 6: Commit, then verify installed**

Commit (show the message in chat first):

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): smoke-test the Game page rename and remove actions"
```

Then **ask the user before pushing.** After their OK, push the branch and
watch `production-shell-ui`.
Expected: the job passes; `game-actions-light` and `game-actions-dark`
list every phase from `remove-disabled-with-guides` to `removed`;
`game-actions-persisted` lists every phase from `persisted-resume` to
`persisted-no-provider-traffic`; the three `describe-actions` checks pass;
`design-language` passes with the new asserts. If `Wait-FocusedId
'CloseButton'` fails, Cancel is not the dialog's initial focus: treat it
as a defect against the spec and debug it (superpowers:systematic-debugging);
do not drop the check.

Download the `production-shell-ui` artifact
(`gh run download <run-id> -n production-shell-ui -D <tmp-dir>`) and copy
`game-actions-light.remove-game-hint.png`,
`game-actions-dark.remove-game-hint.png`,
`game-actions-light.remove-game-confirm.png` and
`game-actions-dark.remove-game-confirm.png` into
`docs/p1/evidence/t04-2-game-actions/` as `remove-game-hint-light.png`,
`remove-game-hint-dark.png`, `remove-game-confirm-light.png` and
`remove-game-confirm-dark.png`. Read each one to confirm it shows the
intended view.

## Task 5: Verification record and traceability docs

TDD skip: documentation only. The gate is the grep in Step 3 and the CI run
from Task 4.

`<run-id>` below is the passing `production-shell-ui` run from Task 4
Step 6, and `<n>` is each suite's pass count from the last full run. Both
come from real output; Step 3 fails if any is left in.

**Files:**
- Modify: `docs/p1/t04-2-game-actions-design.md` (status line; a new
  verification record at the end)
- Modify: `docs/p1/implementation-plan.md` (a T04.2 paragraph after the
  T05.2 paragraph, around line 680)
- Modify: `docs/p1/e2e-testing.md` (a Game actions row after the Library
  search row, around line 250)
- Modify: `docs/progress.md` (a T04.2 row after the T05.2 row)
- Add: the four PNGs in `docs/p1/evidence/t04-2-game-actions/` (Task 4
  Step 6)

**Interfaces:**
- Consumes: Task 4's run ID, phase names and screenshots; the suite counts
  from Tasks 2–4; the rulings above plus any made during implementation.
- Produces: the anchor `t04-2-game-actions-design.md#t042-verification-record`,
  which `progress.md` links to.

- [ ] **Step 1: Write the verification record**

In `t04-2-game-actions-design.md`, replace the status lines with:

```markdown
Status: implemented on `feat/p1-t04-2-game-actions`; verified by CI run
<run-id>. Prerequisites T04.1 (PR #11), T04.4 (PR #14), T05.4 (PR #16) and
T11.1 (PR #6) are merged.
```

Append:

```markdown
## T04.2 verification record

- **Unit tests.** On `pcsx2-win`, Infrastructure <n>/<n> and Core <n>/<n>
  passed. The new tests are:
  - `GameRemoverTests`: artwork deleted only on `Removed`, nothing deleted
    for `HasGuides`, `NotFound` or a game without artwork, and `Removed`
    kept when the artwork delete throws;
  - `GameRemovalPresentationTests`: the dialog title and body, the hint,
    and each status line;
  - `GameRenameTests`: a rename keeps the provider link, snapshot, artwork
    path, `CreatedUtc`, Guide IDs, reading state and `LastActiveGuideId`,
    and the summary shows the new title under the same Game ID, before and
    after reopening the repository;
  - `EmptyGameRemovalTests`: manual and linked games removed, the provider
    game addable again, `HasGuides` leaving the game, guide and reading
    state intact, `NotFound`, `Guid.Empty`, other games untouched, the
    artwork file and folder deleted, a locked artwork file swept at the
    next start, and a publication into a removed game leaving nothing.
- **Installed.** CI run [<run-id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run-id>)
  passed `production-shell-ui`:
  - `game-actions`, light and dark: phases `remove-disabled-with-guides`,
    `rename-keeps-selection`, `rename-library-row`, `remove-confirm`,
    `remove-escape-cancels`, `remove-enter-cancels` and `removed`;
  - `game-actions-persisted`: phases `persisted-resume`,
    `persisted-selection`, `persisted-facts` and
    `persisted-no-provider-traffic`, with zero non-loopback connections;
  - `describe-actions` after each run: one game with the original ID,
    provider game ID and artwork; no artwork folder for the removed game;
    the seeded Guide IDs, Alpha's reading state and Beta as Resume;
  - `design-language`: Remove game and its hint inside the window at both
    widths.
- **Rulings.** Rulings 1–21 in the
  [plan](t04-2-game-actions-plan.md#rulings-against-the-spec), plus any
  made during implementation, each with what it costs if wrong.
- **Evidence.**
  - [Guides-first hint, light](evidence/t04-2-game-actions/remove-game-hint-light.png)
  - [Guides-first hint, dark](evidence/t04-2-game-actions/remove-game-hint-dark.png)
  - [Remove confirmation, light](evidence/t04-2-game-actions/remove-game-confirm-light.png)
  - [Remove confirmation, dark](evidence/t04-2-game-actions/remove-game-confirm-dark.png)
```

List the implementation rulings by name; "plus any made" is replaced by
the list, or by "none were needed".

- [ ] **Step 2: Update the traceability docs**

In `implementation-plan.md`, after the T05.2 paragraph, add:

```markdown
T04.2 is implemented on `feat/p1-t04-2-game-actions`; see the
[design and verification record](t04-2-game-actions-design.md). Renaming a
game through Edit game keeps its Game ID, provider link and snapshot,
artwork, Guide IDs, reading state, Resume and selected guide, after a
refresh and after a restart. Remove game removes only a game without
guides; for a game with guides it is disabled, with the visible hint
`Remove this game's guides first.`. The confirmation defaults to Cancel.
After a removal the Library is shown with an empty back stack, and the
artwork is deleted best effort, with the startup sweep removing anything
left. CI run <run-id> passed `game-actions` in light and dark and
`game-actions-persisted`.
```

The T04.2 table row (line 709) stays as is.

In `e2e-testing.md`, after the Library search row, add:

```markdown
| Game actions | Seed Linked Rename Game, provider-linked with artwork and the guides Alpha Route Guide (about 45%, opened yesterday) and Beta Route Guide (Resume), and Empty Linked Game, provider-linked with artwork and no guides. In light and dark: Linked Rename Game shows Remove game disabled with `Remove this game's guides first.`; renaming it to Renamed Linked Game keeps Beta selected, focus on Edit game and the summary, and its Library facts are unchanged. With the Library filtered to `Empty`, Empty Linked Game's Remove game opens `Remove Empty Linked Game?`; Escape and Enter cancel with focus back on Remove game; Remove shows the Library with `Removed Empty Linked Game.`, the `Empty` query kept and its no-results view, focus on Add game and Back disabled. `describe-actions` finds one game with its original ID, artwork, Guide IDs, reading state and Resume, and no artwork folder for the removed game. After a relaunch, Resume opens Beta under the new name, Back keeps Beta selected, Alpha still reads about 45 percent, and no non-loopback TCP connection is made. | T04.2, TR03.1, TR04.3, TR11.3 |
```

In `progress.md`, after the T05.2 row, add:

```markdown
| P1 T04.2 game actions | Implemented on `feat/p1-t04-2-game-actions`; PR open. | Renaming a game keeps its ID, provider link, artwork, guides, reading state, Resume and selection, after a refresh and a restart. Remove game removes a game without guides after a confirmation; for a game with guides it is disabled with a visible hint. CI run [<run-id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run-id>) passed `game-actions`; see the [verification record](p1/t04-2-game-actions-design.md#t042-verification-record). |
```

`work-breakdown.md` (line 143) and `initial-design.md` need no change: the
task's scope is unchanged. Confirm with
`grep -n "T04.2" docs/work-breakdown.md docs/initial-design.md`.

- [ ] **Step 3: Check and commit**

Run: `grep -n "<run-id>\|<n>" docs/progress.md docs/p1/implementation-plan.md docs/p1/e2e-testing.md docs/p1/t04-2-game-actions-design.md`
Expected: no output.

Run: `git status --short docs/p1/evidence/t04-2-game-actions`
Expected: the four PNGs, untracked.

Commit (show the message in chat first):

```bash
git add docs/progress.md docs/p1/implementation-plan.md docs/p1/e2e-testing.md docs/p1/t04-2-game-actions-design.md docs/p1/evidence/t04-2-game-actions
git commit -m "docs(p1): record T04.2 game actions verification"
```
