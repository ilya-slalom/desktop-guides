# T04.3 Game Removal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** **Remove game** works for every game: a count-confirmed dialog, then one recoverable removal of the game, its guides, their state and content, its provider link and its artwork, through the T15.3 deletion journal.

**Architecture:** A new Infrastructure `GameRemover` replaces T04.2's Core one. It holds the repository write gate, writes the existing v1 `DeleteGame` manifest, moves each `content/<guide>` into `.trash/<op>/<guide>`, then deletes the game row (cascading guides and state) in one transaction, and deletes the artwork best effort. A game without guides skips the journal. The shell's dialog states the guide and file counts and re-prompts when the count changed.

**Tech Stack:** WinUI 3, .NET 10, xUnit, Microsoft.Data.Sqlite (schema v3), the PowerShell 5.1 UIA smoke harness.

**Spec:** [t04-3-game-removal-design.md](t04-3-game-removal-design.md)

**Target:** T04.3 (TR04.1, TR04.2). **Prerequisites:** T04.2 (PR #25), T04.4 (PR #14) and T15.3 (PR #22), all merged.

## Global Constraints

- Schema stays at version 3; no migration. `DeleteGame` uses the existing v1 manifest (1 to 10,000 guide IDs).
- Originals the person imported are never touched; only `content/<guide>`, `.trash/<op>`, and the game's managed artwork are deleted.
- Cancel changes neither database records nor files.
- The confirmation is the native `ContentDialog` with **Cancel** as `DefaultButton`; no checkbox, no typed title, no custom danger style.
- UIA IDs: `RemoveGameButton`, `RemoveGameDialog`, `RemoveGameMessage` keep their IDs; `RemoveGameCountChanged` is new; `RemoveGameHint` is removed.
- Copy is exactly the spec's presentation and message tables.
- PowerShell scripts stay ASCII-only and parse under Windows PowerShell 5.1.
- Infrastructure tests run on `pcsx2-win` (real NTFS); Windows-only tests start with `if (!OperatingSystem.IsWindows()) return;`.
- CI `production-shell-ui` is the gate of record for the installed smoke. Pushes need the user's explicit OK, each one.

## Rulings against the spec

1. **Build sequencing.** Task 1 changes `GameRemovalPresentation`, so `DesktopGuides.Production` doesn't compile from Task 1 until Task 3. The Core and Infrastructure test suites stay green at every commit; the Production build is checked in Task 3. Cost if wrong: a bisect across Tasks 1–2 can't build the app.
2. **No `GameMetadataLinks` table.** The provider link and snapshot are columns on `Games`, so the happy path asserts them through the `Games` row and `FindLinkedGameAsync` returning null. Cost if wrong: none; the spec's table name is a naming slip.
3. **`DescribeAsync` uses public reads outside the gate** (`GetGameAsync`, `ListGuidesAsync`), as `GuideRemover.DescribeAsync` does. `RemoveAsync` re-reads inside the gate, so a stale preview only causes `CountChanged`.
4. **The no-guides path raises `InCommit`** as its `beforeCommit`, so a test can fault the empty-game commit. No other checkpoint fires on that path.
5. **Failed and RestoreFailed don't re-render the page.** Nothing changed in the database, so the rows already shown are correct; the shell restores the buttons. The spec's "re-renders from the database" is satisfied by the unchanged rows. Cost if wrong: a page that went stale during the dialog stays stale until the next navigation.
6. **Smoke order.** The guided removal runs first in `game-actions`, with Guided Walkthrough seeded as the Resume guide. The later `Open-GuideFromGame` step sets Resume to Beta again, so the existing `LastActive is Beta` check still holds.
7. **The seed's HTML guide is never opened**, so the smoke needs no WebView2 state for it.
8. **`RemoveGameCountChanged` is added to the dialog only when `changed`** rather than collapsed, so `Assert-Absent` works without a visibility check.
9. **A negative `expectedGuideCount` throws `ArgumentOutOfRangeException`.**
10. **`ContentDirectories`** in `describe-actions` lists the guide IDs whose `content/<guide>` exists; it is the spec's per-guide content report.
11. **The shell is tested only by the installed smoke.** There is no WinUI unit-test harness, and host E2E runs are reserved for debugging CI failures, so Task 3's shell changes first run in CI with Task 4's phases. Task 3's local gate is the Production build. Cost if wrong: a shell defect costs one CI round trip.

## Review Focus

1. **Closing the window while the dialog is open or while `RemoveAsync` runs.** Expect: either the whole game or nothing, with startup finishing any journaled row. Owned by Task 2's crash-point tests; the shell adds no new state.
2. **A guide's file held open by a reader after reading** (WebView2 or the PDF renderer). Expect `Failed` with every guide restored. Pinned by `ContentHeldOpenFailsAndRestoresEveryGuide` in Task 2.
3. **More than 10,000 guides.** Expect `Failed` and nothing changed: `FileOperationManifest.Create` rejects the list inside `PrepareGame`, before any move. Pinned by `APrepareFailureChangesNothing` in Task 2, which faults `PrepareGame` through the same catch.
4. **Double activation and Enter on the re-prompted dialog.** Expect: the button is disabled while the flow runs and Enter cancels. Pinned in Task 3 by the existing `gameRemoveRequested` guard and `DefaultButton = Close` on every dialog the loop opens.
5. **Retrying after `RestoreFailed`.** Expect `RestoreFailed` again with nothing changed, and startup restoring the game. Pinned by `RemoveAfterAFailedRestoreLeavesTheGameToStartup` in Task 2.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){ s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t04-3) { Remove-Item -Recurse -Force E:\work\desktop-guides\t04-3 }; New-Item -ItemType Directory E:\work\desktop-guides\t04-3 | Out-Null"'; COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='._*' -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t04-3'; }
```

- Core tests: `stage && s 'cd /d E:\work\desktop-guides\t04-3 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
- Infrastructure tests: the same with `tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj`.
- One class: append `--filter "FullyQualifiedName~<Class>"`.
- Production build: `s 'cd /d E:\work\desktop-guides\t04-3 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'`
- Seed build: `s 'cd /d E:\work\desktop-guides\t04-3 && dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release'`
- Baseline before Task 1: Core 276 passed, Infrastructure 407 passed.

---

### Task 1: Core contracts and presentation

**Files:**
- Modify: `src/DesktopGuides.Core/Library/GameRemovalContracts.cs`
- Modify: `src/DesktopGuides.Core/Library/GameRemovalPresentation.cs`
- Test: `tests/DesktopGuides.Core.Tests/GameRemovalPresentationTests.cs` (rewrite)

**Interfaces:**
- Produces: `GameRemovalPreview(Guid GameId, string Title, int GuideCount, int FileCount)`; `GameRemovalOutcome { Removed, NotFound, CountChanged }`; `GameRemovalResult(GameRemovalOutcome Outcome, bool CleanupPending, GameRemovalPreview? Current)`; `GameRemovalIssue { Unsafe, Failed, RestoreFailed }`; `GameRemovalException(GameRemovalIssue issue, Exception? inner = null)` with `Issue`.
- Produces: `GameRemovalPresentation.DialogTitle(string title, int guideCount)`, `DialogBody(int guideCount, int fileCount)`, `CountChanged(int guideCount)`, `Removed(string title, bool cleanupPending)`, `AlreadyRemoved(string title)`, `Error(GameRemovalIssue issue, string title)`.
- `EmptyGameRemoval` and `EmptyGameRemovalOutcome` stay until Task 2 deletes them.

- [ ] **Step 1: Write the failing tests**

Replace `tests/DesktopGuides.Core.Tests/GameRemovalPresentationTests.cs` with:

```csharp
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GameRemovalPresentationTests
{
    [Theory]
    [InlineData(0, "Remove Halo?")]
    [InlineData(1, "Remove Halo and its guide?")]
    [InlineData(3, "Remove Halo and its 3 guides?")]
    public void TheTitleStatesTheGuideCount(int guides, string expected) =>
        Assert.Equal(expected, GameRemovalPresentation.DialogTitle("Halo", guides));

    [Fact]
    public void TheBodyForAGameWithoutGuidesIsT042sCopy() =>
        Assert.Equal(
            "This removes the game and its details from Desktop Guides.",
            GameRemovalPresentation.DialogBody(0, 0));

    [Theory]
    [InlineData(1, 1, "This removes the game, its guide with its reading progress, and its 1 managed file from Desktop Guides. The original file you imported isn't affected.")]
    [InlineData(1, 4, "This removes the game, its guide with its reading progress, and its 4 managed files from Desktop Guides. The original file you imported isn't affected.")]
    [InlineData(2, 1, "This removes the game, its 2 guides with their reading progress, and their 1 managed file from Desktop Guides. The original files you imported aren't affected.")]
    [InlineData(2, 3, "This removes the game, its 2 guides with their reading progress, and their 3 managed files from Desktop Guides. The original files you imported aren't affected.")]
    public void TheBodyStatesTheGuideAndFileCounts(int guides, int files, string expected) =>
        Assert.Equal(expected, GameRemovalPresentation.DialogBody(guides, files));

    [Fact]
    public void CountChangedStatesTheNewCount() =>
        Assert.Equal("The number of guides changed. It's now 3.", GameRemovalPresentation.CountChanged(3));

    [Fact]
    public void TheStatusLinesNameTheGame()
    {
        Assert.Equal("Removed Halo.", GameRemovalPresentation.Removed("Halo", false));
        Assert.Equal(
            "Removed Halo. Leftover files will be cleaned up the next time Desktop Guides starts.",
            GameRemovalPresentation.Removed("Halo", true));
        Assert.Equal("Halo was already removed.", GameRemovalPresentation.AlreadyRemoved("Halo"));
    }

    [Theory]
    [InlineData(GameRemovalIssue.Unsafe, "Halo can't be removed because a guide's files were changed outside Desktop Guides.")]
    [InlineData(GameRemovalIssue.Failed, "Halo couldn't be removed. The game is unchanged. Try again.")]
    [InlineData(GameRemovalIssue.RestoreFailed, "Halo couldn't be removed. Restart Desktop Guides to finish restoring it.")]
    public void EachIssueHasItsMessage(GameRemovalIssue issue, string expected) =>
        Assert.Equal(expected, GameRemovalPresentation.Error(issue, "Halo"));

    [Fact]
    public void AnUnknownIssueIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GameRemovalPresentation.Error((GameRemovalIssue)99, "Halo"));
}
```

Check the namespace of the existing file before replacing it and keep it.

- [ ] **Step 2: Run the tests to verify they fail**

Run: Core tests with `--filter "FullyQualifiedName~GameRemovalPresentationTests"`.
Expected: build FAIL: `GameRemovalIssue` not found, no `DialogTitle` overload taking 2 arguments.

- [ ] **Step 3: Add the contracts**

Append to `src/DesktopGuides.Core/Library/GameRemovalContracts.cs` (keep the two existing types for now):

```csharp

/// <summary>What the confirmation states: the game, its guide count and its managed-file count.</summary>
public sealed record GameRemovalPreview(Guid GameId, string Title, int GuideCount, int FileCount);

public enum GameRemovalOutcome { Removed, NotFound, CountChanged }

/// <summary>
/// CleanupPending is true only for Removed: startup deletes the leftover trash.
/// Current is the fresh preview, set only for CountChanged.
/// </summary>
public sealed record GameRemovalResult(
    GameRemovalOutcome Outcome, bool CleanupPending, GameRemovalPreview? Current);

public enum GameRemovalIssue { Unsafe, Failed, RestoreFailed }

public sealed class GameRemovalException(GameRemovalIssue issue, Exception? inner = null)
    : Exception(null, inner)
{
    public GameRemovalIssue Issue { get; } = issue;
}
```

- [ ] **Step 4: Rewrite the presentation**

Replace `src/DesktopGuides.Core/Library/GameRemovalPresentation.cs` with:

```csharp
namespace DesktopGuides.Core.Library;

/// <summary>The game removal confirmation and status copy.</summary>
public static class GameRemovalPresentation
{
    public static string DialogTitle(string title, int guideCount) => guideCount switch
    {
        0 => $"Remove {title}?",
        1 => $"Remove {title} and its guide?",
        _ => $"Remove {title} and its {guideCount} guides?",
    };

    public static string DialogBody(int guideCount, int fileCount)
    {
        if (guideCount == 0)
        {
            return "This removes the game and its details from Desktop Guides.";
        }
        string files = fileCount == 1 ? "1 managed file" : $"{fileCount} managed files";
        return guideCount == 1
            ? $"This removes the game, its guide with its reading progress, and its {files} from Desktop Guides. " +
                "The original file you imported isn't affected."
            : $"This removes the game, its {guideCount} guides with their reading progress, and their {files} from Desktop Guides. " +
                "The original files you imported aren't affected.";
    }

    public static string CountChanged(int guideCount) =>
        $"The number of guides changed. It's now {guideCount}.";

    public static string Removed(string title, bool cleanupPending) => cleanupPending
        ? $"Removed {title}. Leftover files will be cleaned up the next time Desktop Guides starts."
        : $"Removed {title}.";

    public static string AlreadyRemoved(string title) => $"{title} was already removed.";

    public static string Error(GameRemovalIssue issue, string title) => issue switch
    {
        GameRemovalIssue.Unsafe => $"{title} can't be removed because a guide's files were changed outside Desktop Guides.",
        GameRemovalIssue.Failed => $"{title} couldn't be removed. The game is unchanged. Try again.",
        GameRemovalIssue.RestoreFailed => $"{title} couldn't be removed. Restart Desktop Guides to finish restoring it.",
        _ => throw new ArgumentOutOfRangeException(nameof(issue)),
    };
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: Core tests with `--filter "FullyQualifiedName~GameRemovalPresentationTests"`.
Expected: PASS, 14 tests (3 + 1 + 4 + 1 + 1 + 3 + 1).

Then the full Core suite.
Expected: PASS. The count is 276 minus the old presentation tests plus the new ones; record the number in the ledger.

If the Core suite references `GuidesFirst`, `HasGuides` or the one-argument `DialogTitle` anywhere else, the build fails here; those uses are only in Production (fixed in Task 3) and `GameRemoverTests` (Core). `GameRemoverTests` doesn't use the presentation, so the Core suite compiles.

- [ ] **Step 6: Commit**

Show the message in chat first, then:

```bash
git add src/DesktopGuides.Core/Library/GameRemovalContracts.cs src/DesktopGuides.Core/Library/GameRemovalPresentation.cs tests/DesktopGuides.Core.Tests/GameRemovalPresentationTests.cs
git commit -m "feat(core): add counted game removal contracts and copy

- Add GameRemovalPreview, GameRemovalResult, GameRemovalOutcome,
  GameRemovalIssue and GameRemovalException.
- State the guide and file counts in the dialog title and body.
- Replace GuidesFirst and HasGuides with CountChanged and Error.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Deletion journal and the Infrastructure game remover

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Storage/GameRemover.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/GuideRemover.cs` (add `MovedGuide` to `RemovalCheckpoint`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/DeletionJournal.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (journal members; delete `RemoveEmptyGameAsync`, lines 112–157)
- Modify: `src/DesktopGuides.Core/Library/ILibraryRepository.cs` (delete `RemoveEmptyGameAsync` and its comment, lines 14–16)
- Modify: `src/DesktopGuides.Core/Library/GameRemovalContracts.cs` (delete `EmptyGameRemovalOutcome` and `EmptyGameRemoval`)
- Modify: `tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs` (delete `NextRemoval`, `RemoveCalls` and `RemoveEmptyGameAsync`, lines 55–62)
- Delete: `src/DesktopGuides.Core/Library/GameRemover.cs`, `tests/DesktopGuides.Core.Tests/GameRemoverTests.cs`, `tests/DesktopGuides.Infrastructure.Tests/EmptyGameRemovalTests.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/GameRemovalTests.cs` (new)

**Interfaces:**
- Consumes (Task 1): `GameRemovalPreview`, `GameRemovalOutcome`, `GameRemovalResult`, `GameRemovalIssue`, `GameRemovalException`.
- Produces: `public sealed class GameRemover` in `DesktopGuides.Infrastructure.Storage`:
  - `public GameRemover(SqliteLibraryRepository repository, ILibraryPaths paths, IArtworkStore artwork)`
  - `internal GameRemover(SqliteLibraryRepository repository, ILibraryPaths paths, IArtworkStore artwork, Action<RemovalCheckpoint> checkpoint)`
  - `public Task<GameRemovalPreview?> DescribeAsync(Guid gameId, CancellationToken token = default)`
  - `public Task<GameRemovalResult> RemoveAsync(Guid gameId, int expectedGuideCount, CancellationToken token = default)`
- Produces: `RemovalCheckpoint.MovedGuide`; `IDeletionJournal.GetGameForRemoval`, `PrepareGame`, `CommitGame`; `internal sealed record GameForRemoval(string Title, string? ArtworkRelativePath, IReadOnlyList<Guid> GuideIds)`.
- Removes: `ILibraryRepository.RemoveEmptyGameAsync`, `EmptyGameRemoval`, `EmptyGameRemovalOutcome`, Core `GameRemover`. Production still references the Core `GameRemover` until Task 3 (ruling 1).

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Infrastructure.Tests/GameRemovalTests.cs`:

```csharp
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Infrastructure.Tests.Import;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class GameRemovalTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;
    private ManagedArtworkStore store = null!;
    private Game game = null!;
    private Guid walkthroughId;
    private Guid mapsId;
    private Guid otherGuideId;
    private string artworkFile = null!;
    private string otherArtworkFile = null!;
    private byte[] otherArtwork = null!;

    public async Task InitializeAsync()
    {
        library = await RemovalLibrary.CreateAsync();
        store = new ManagedArtworkStore(library.Paths);
        game = await AddLinkedAsync("Guided Game", "900500");
        walkthroughId = await library.AddGuideAsync(game.Id, "Walkthrough");
        mapsId = await library.AddGuideAsync(game.Id, "Maps");
        Game other = await AddLinkedAsync("Other Game", "900600");
        otherGuideId = await library.AddGuideAsync(other.Id, "Other");
        RemovalLibrary.WriteFile(Content(walkthroughId), "guide.txt", "walkthrough");
        RemovalLibrary.WriteFile(Content(mapsId), "index.html", "maps");
        RemovalLibrary.WriteFile(Content(mapsId), "images/map.png", "map");
        RemovalLibrary.WriteFile(Content(mapsId), "images/deep/key.png", "key");
        RemovalLibrary.WriteFile(Content(otherGuideId), "guide.txt", "other");
        artworkFile = store.ResolveFile(game.ArtworkRelativePath!)!;
        otherArtworkFile = store.ResolveFile(other.ArtworkRelativePath!)!;
        otherArtwork = File.ReadAllBytes(otherArtworkFile);
    }

    public async Task DisposeAsync() => await library.DisposeAsync();

    private async Task<Game> AddLinkedAsync(string title, string externalId)
    {
        Guid id = Guid.NewGuid();
        StoredArtwork cover = await store.StoreAsync(id, Artwork.TestImages.Png(4, 4), default);
        return await library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            id, title, "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, externalId, DateTimeOffset.UnixEpoch),
            GameMetadataJsonTests.Sample(), cover.RelativePath));
    }

    private string Content(Guid id) => library.Paths.GetGuideRoot(id);

    private GameRemover Remover(Action<RemovalCheckpoint>? checkpoint = null) =>
        new(library.Repository, library.Paths, store, checkpoint ?? (_ => { }));

    private string GameRows(Guid id) => library.Scalar($"SELECT COUNT(*) FROM Games WHERE Id = '{id:N}'");

    private string OperationCount() => library.Scalar("SELECT COUNT(*) FROM FileOperations");

    private int TrashEntries() =>
        Directory.EnumerateFileSystemEntries(library.Paths.TrashRoot).Count();

    private static Action<RemovalCheckpoint> FaultAt(RemovalCheckpoint fault) => reached =>
    {
        if (reached == fault) throw new InvalidOperationException("fault");
    };

    private void HoldTrashedMap(out FileStream held)
    {
        string trashed = Directory.EnumerateFiles(library.Paths.TrashRoot, "map.png", SearchOption.AllDirectories).Single();
        held = new FileStream(trashed, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private void AssertGameIntact()
    {
        Assert.Equal("1", GameRows(game.Id));
        Assert.Equal("1|1|1", library.RowsFor(walkthroughId));
        Assert.Equal("1|1|1", library.RowsFor(mapsId));
        Assert.Equal("walkthrough", File.ReadAllText(Path.Combine(Content(walkthroughId), "guide.txt")));
        Assert.Equal("maps", File.ReadAllText(Path.Combine(Content(mapsId), "index.html")));
        Assert.Equal("key", File.ReadAllText(Path.Combine(Content(mapsId), "images/deep/key.png")));
        Assert.True(File.Exists(artworkFile));
        Assert.Equal("0", OperationCount());
        Assert.Equal(0, TrashEntries());
    }

    private async Task AssertOtherGameKeptAsync()
    {
        Assert.Equal("1|1|1", library.RowsFor(otherGuideId));
        Assert.Equal("other", File.ReadAllText(Path.Combine(Content(otherGuideId), "guide.txt")));
        Assert.Equal(otherArtwork, File.ReadAllBytes(otherArtworkFile));
        Assert.NotNull(await library.Repository.FindLinkedGameAsync(ProviderGameLink.Igdb, "900600"));
    }

    // Describe

    [Fact]
    public async Task DescribeCountsTheGuidesAndTheirFiles() =>
        Assert.Equal(
            new GameRemovalPreview(game.Id, "Guided Game", 2, 4),
            await Remover().DescribeAsync(game.Id));

    [Fact]
    public async Task DescribeCountsZeroFilesForMissingContent()
    {
        Directory.Delete(Content(mapsId), true);

        Assert.Equal(
            new GameRemovalPreview(game.Id, "Guided Game", 2, 1),
            await Remover().DescribeAsync(game.Id));
    }

    [Fact]
    public async Task DescribeReturnsNullForAnUnknownGame() =>
        Assert.Null(await Remover().DescribeAsync(Guid.NewGuid()));

    [Fact]
    public async Task InvalidArgumentsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Remover().DescribeAsync(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => Remover().RemoveAsync(Guid.Empty, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Remover().RemoveAsync(game.Id, -1));
        AssertGameIntact();
    }

    [Fact]
    public async Task DescribeRefusesALinkInAnyGuidesTree()
    {
        if (!OperatingSystem.IsWindows()) return;
        string link = Path.Combine(Content(mapsId), "link");
        RemovalLibrary.CreateJunction(link, Path.Combine(library.Root, "outside"));
        try
        {
            GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
                () => Remover().DescribeAsync(game.Id));

            Assert.Equal(GameRemovalIssue.Unsafe, error.Issue);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    // Remove

    [Fact]
    public async Task RemoveDeletesTheGameItsGuidesAndTheirFiles()
    {
        List<RemovalCheckpoint> seen = [];

        GameRemovalResult result = await Remover(seen.Add).RemoveAsync(game.Id, 2);

        Assert.Equal(new GameRemovalResult(GameRemovalOutcome.Removed, false, null), result);
        Assert.Equal(
            [RemovalCheckpoint.Prepared, RemovalCheckpoint.MovedGuide, RemovalCheckpoint.MovedGuide,
             RemovalCheckpoint.Moved, RemovalCheckpoint.InCommit, RemovalCheckpoint.Committed],
            seen);
        Assert.Equal("0", GameRows(game.Id));
        Assert.Null(await library.Repository.FindLinkedGameAsync(ProviderGameLink.Igdb, "900500"));
        Assert.Equal("0|0|0", library.RowsFor(walkthroughId));
        Assert.Equal("0|0|0", library.RowsFor(mapsId));
        Assert.False(Directory.Exists(Content(walkthroughId)));
        Assert.False(Directory.Exists(Content(mapsId)));
        Assert.False(File.Exists(artworkFile));
        Assert.False(Directory.Exists(Path.GetDirectoryName(artworkFile)));
        Assert.Equal(0, TrashEntries());
        Assert.Equal("0", OperationCount());
        await AssertOtherGameKeptAsync();
    }

    [Fact]
    public async Task RemoveClearsAResumeGuideThatWasTheGames()
    {
        await library.Repository.SaveSettingsAsync(
            await library.Repository.GetSettingsAsync() with { LastActiveGuideId = mapsId });

        await Remover().RemoveAsync(game.Id, 2);

        Assert.Null((await library.Repository.GetSettingsAsync()).LastActiveGuideId);
    }

    [Fact]
    public async Task RemoveKeepsAResumeGuideFromAnotherGame()
    {
        await library.Repository.SaveSettingsAsync(
            await library.Repository.GetSettingsAsync() with { LastActiveGuideId = otherGuideId });

        await Remover().RemoveAsync(game.Id, 2);

        Assert.Equal(otherGuideId, (await library.Repository.GetSettingsAsync()).LastActiveGuideId);
    }

    [Fact]
    public async Task AManualGameWithoutGuidesIsRemovedWithoutAnOperation()
    {
        Guid id = await library.AddGameAsync("Empty Manual Game");
        List<RemovalCheckpoint> seen = [];

        GameRemovalResult result = await Remover(seen.Add).RemoveAsync(id, 0);

        Assert.Equal(new GameRemovalResult(GameRemovalOutcome.Removed, false, null), result);
        Assert.Equal([RemovalCheckpoint.InCommit], seen);
        Assert.Null(await library.Repository.GetGameAsync(id));
        AssertGameIntact();
        await AssertOtherGameKeptAsync();
    }

    [Fact]
    public async Task ALinkedGameWithoutGuidesLosesItsArtworkAndCanBeAddedAgain()
    {
        Game empty = await AddLinkedAsync("Empty Linked Game", "900700");
        string file = store.ResolveFile(empty.ArtworkRelativePath!)!;

        Assert.Equal(GameRemovalOutcome.Removed, (await Remover().RemoveAsync(empty.Id, 0)).Outcome);

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
        Assert.Null(await library.Repository.FindLinkedGameAsync(ProviderGameLink.Igdb, "900700"));
        Game again = await AddLinkedAsync("Empty Linked Game", "900700");
        Assert.NotEqual(empty.Id, again.Id);
    }

    [Fact]
    public async Task ArtworkThatCannotBeDeletedIsSweptAtTheNextStart()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (new FileStream(artworkFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(
                new GameRemovalResult(GameRemovalOutcome.Removed, false, null),
                await Remover().RemoveAsync(game.Id, 2));
            Assert.True(File.Exists(artworkFile));
        }

        await library.RestartAsync();

        Assert.False(File.Exists(artworkFile));
        await AssertOtherGameKeptAsync();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task AChangedCountReturnsTheFreshPreviewAndRemovesNothing(int expected)
    {
        GameRemovalResult result = await Remover().RemoveAsync(game.Id, expected);

        Assert.Equal(
            new GameRemovalResult(GameRemovalOutcome.CountChanged, false,
                new GameRemovalPreview(game.Id, "Guided Game", 2, 4)),
            result);
        AssertGameIntact();
    }

    [Fact]
    public async Task ACountChangedByAGuideRemovalIsReportedThenRemoved()
    {
        await new GuideRemover(library.Repository, library.Paths).RemoveAsync(mapsId);

        Assert.Equal(
            new GameRemovalResult(GameRemovalOutcome.CountChanged, false,
                new GameRemovalPreview(game.Id, "Guided Game", 1, 1)),
            await Remover().RemoveAsync(game.Id, 2));
        Assert.Equal(GameRemovalOutcome.Removed, (await Remover().RemoveAsync(game.Id, 1)).Outcome);
        Assert.Equal("0", GameRows(game.Id));
    }

    [Fact]
    public async Task AGuideWithMissingContentIsRemoved()
    {
        Directory.Delete(Content(mapsId), true);
        List<RemovalCheckpoint> seen = [];

        Assert.Equal(GameRemovalOutcome.Removed, (await Remover(seen.Add).RemoveAsync(game.Id, 2)).Outcome);

        Assert.Single(seen, point => point == RemovalCheckpoint.MovedGuide);
        Assert.Equal("0|0|0", library.RowsFor(mapsId));
        Assert.Equal(0, TrashEntries());
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task RemoveOfAnUnknownGameReturnsNotFound()
    {
        Assert.Equal(
            new GameRemovalResult(GameRemovalOutcome.NotFound, false, null),
            await Remover().RemoveAsync(Guid.NewGuid(), 0));

        AssertGameIntact();
    }

    [Fact]
    public async Task RemoveRefusesALinkBeforeJournaling()
    {
        if (!OperatingSystem.IsWindows()) return;
        string link = Path.Combine(Content(mapsId), "link");
        RemovalLibrary.CreateJunction(link, Path.Combine(library.Root, "outside"));
        try
        {
            GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
                () => Remover().RemoveAsync(game.Id, 2));

            Assert.Equal(GameRemovalIssue.Unsafe, error.Issue);
            Assert.Equal("0", OperationCount());
        }
        finally
        {
            Directory.Delete(link);
        }
        AssertGameIntact();
    }

    [Fact]
    public async Task AGuideClaimedByAnUnfinishedOperationIsLeftAlone()
    {
        await library.RunDeletion(journal => journal.Prepare(Guid.NewGuid(), mapsId));

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
            () => Remover().RemoveAsync(game.Id, 2));

        Assert.Equal(GameRemovalIssue.RestoreFailed, error.Issue);
        Assert.Equal("1", GameRows(game.Id));
        Assert.Equal("1|1|1", library.RowsFor(walkthroughId));
        Assert.Equal("1|1|1", library.RowsFor(mapsId));
        Assert.Equal("1", OperationCount());
        Assert.True(Directory.Exists(Content(walkthroughId)));
    }

    [Fact]
    public async Task AGuidePublishedIntoARemovedGameFailsAndLeavesNothing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GameRemover remover = new(harness.Repository, harness.Paths, new ManagedArtworkStore(harness.Paths));
        Assert.Equal(GameRemovalOutcome.Removed, (await remover.RemoveAsync(harness.Game.Id, 0)).Outcome);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task RemoveWithACancelledTokenWritesNothing()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Remover().RemoveAsync(game.Id, 2, new CancellationToken(canceled: true)));

        AssertGameIntact();
    }

    // Crash points

    [Theory]
    [InlineData("Prepared")]
    [InlineData("MovedGuide")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task AFaultBeforeTheCommitRestoresEveryGuide(string point)
    {
        // MovedGuide faults at the first of the two moves.
        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
            () => Remover(FaultAt(Enum.Parse<RemovalCheckpoint>(point))).RemoveAsync(game.Id, 2));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        AssertGameIntact();
        await AssertOtherGameKeptAsync();
    }

    [Fact]
    public async Task APrepareFailureChangesNothing()
    {
        library.Execute("""
            CREATE TRIGGER RefuseOperations BEFORE INSERT ON FileOperations
            BEGIN SELECT RAISE(ABORT, 'fault'); END
            """);

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
            () => Remover().RemoveAsync(game.Id, 2));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        Assert.IsType<SqliteException>(error.InnerException);
        AssertGameIntact();
    }

    [Fact]
    public async Task ContentHeldOpenFailsAndRestoresEveryGuide()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (new FileStream(Path.Combine(Content(mapsId), "images/map.png"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
                () => Remover().RemoveAsync(game.Id, 2));

            Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        }
        AssertGameIntact();
    }

    [Fact]
    public async Task AFailedRestoreKeepsThePreparedRowForStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileStream? held = null;
        try
        {
            GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() => Remover(reached =>
            {
                if (reached != RemovalCheckpoint.Moved) return;
                HoldTrashedMap(out FileStream stream);
                held = stream;
                throw new InvalidOperationException("fault");
            }).RemoveAsync(game.Id, 2));

            Assert.Equal(GameRemovalIssue.RestoreFailed, error.Issue);
            Assert.Equal("DeleteGame|Prepared", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
            Assert.Equal("1", GameRows(game.Id));
        }
        finally
        {
            held?.Dispose();
        }

        await library.RestartAsync();

        AssertGameIntact();
    }

    [Fact]
    public async Task RemoveAfterAFailedRestoreLeavesTheGameToStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileStream? held = null;
        try
        {
            await Assert.ThrowsAsync<GameRemovalException>(() => Remover(reached =>
            {
                if (reached != RemovalCheckpoint.Moved) return;
                HoldTrashedMap(out FileStream stream);
                held = stream;
                throw new InvalidOperationException("fault");
            }).RemoveAsync(game.Id, 2));
        }
        finally
        {
            held?.Dispose();
        }

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
            () => Remover().RemoveAsync(game.Id, 2));

        Assert.Equal(GameRemovalIssue.RestoreFailed, error.Issue);
        Assert.Equal("DeleteGame|Prepared", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
        Assert.Equal("1", GameRows(game.Id));

        await library.RestartAsync();

        AssertGameIntact();
    }

    [Fact]
    public async Task AFailedCleanupStillRemovesTheGame()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileStream? held = null;
        try
        {
            GameRemovalResult result = await Remover(reached =>
            {
                if (reached != RemovalCheckpoint.Committed) return;
                HoldTrashedMap(out FileStream stream);
                held = stream;
            }).RemoveAsync(game.Id, 2);

            Assert.Equal(new GameRemovalResult(GameRemovalOutcome.Removed, true, null), result);
            Assert.Equal("0", GameRows(game.Id));
            Assert.Equal("0|0|0", library.RowsFor(mapsId));
            Assert.Equal("DeleteGame|Committed", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
            Assert.Single(Directory.EnumerateFileSystemEntries(library.Paths.TrashRoot));
            Assert.False(File.Exists(artworkFile));
        }
        finally
        {
            held?.Dispose();
        }

        await library.RestartAsync();

        Assert.Equal(0, TrashEntries());
        Assert.Equal("0", OperationCount());
        await AssertOtherGameKeptAsync();
    }

    [Fact]
    public async Task TheCommitGuardKeepsAGameWhoseGuidesChanged()
    {
        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() => Remover(reached =>
        {
            if (reached == RemovalCheckpoint.Moved)
            {
                library.Execute($"UPDATE Guides SET GameId = '{game.Id:N}' WHERE Id = '{otherGuideId:N}'");
            }
        }).RemoveAsync(game.Id, 2));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        Assert.IsType<InvalidDataException>(error.InnerException);
        AssertGameIntact();
        Assert.Equal("1|1|1", library.RowsFor(otherGuideId));
    }

    [Fact]
    public async Task AFaultWhileCommittingAGameWithoutGuidesKeepsIt()
    {
        Guid id = await library.AddGameAsync("Empty Manual Game");

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
            () => Remover(FaultAt(RemovalCheckpoint.InCommit)).RemoveAsync(id, 0));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        Assert.NotNull(await library.Repository.GetGameAsync(id));
    }
}
```

`Artwork.TestImages`, `GameMetadataJsonTests.Sample()` and `PublisherHarness` are the existing helpers `EmptyGameRemovalTests` used.

- [ ] **Step 2: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~GameRemovalTests"`.
Expected: build FAIL: `GameRemover` takes 2 arguments (the Core one) or isn't found in `DesktopGuides.Infrastructure.Storage`, and `RemovalCheckpoint.MovedGuide` doesn't exist.

- [ ] **Step 3: Add the checkpoint and the journal members**

In `src/DesktopGuides.Infrastructure/Storage/GuideRemover.cs`, replace the enum line:

```csharp
internal enum RemovalCheckpoint { Prepared, MovedGuide, Moved, InCommit, Committed }
```

Replace `src/DesktopGuides.Infrastructure/Storage/DeletionJournal.cs` with:

```csharp
using DesktopGuides.Core.Library;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>The game's title, artwork path and guide IDs, read inside the write gate.</summary>
internal sealed record GameForRemoval(string Title, string? ArtworkRelativePath, IReadOnlyList<Guid> GuideIds);

/// <summary>SQL for guide and game deletions. Callers already hold the repository's write gate.</summary>
internal interface IDeletionJournal
{
    Guide? GetGuide(Guid guideId);

    /// <summary>The game's title, artwork path and guide IDs, or null when it's gone.</summary>
    GameForRemoval? GetGameForRemoval(Guid gameId);

    /// <summary>True when an unfinished file operation already claims the guide.</summary>
    bool IsPending(Guid guideId);

    /// <summary>Commits a Prepared DeleteGuide row owning content/&lt;guide&gt; and .trash/&lt;op&gt;/&lt;guide&gt;.</summary>
    void Prepare(Guid operationId, Guid guideId);

    /// <summary>Commits a Prepared DeleteGame row owning each content/&lt;guide&gt; and .trash/&lt;op&gt;/&lt;guide&gt;.</summary>
    void PrepareGame(Guid operationId, IReadOnlyList<Guid> guideIds);

    /// <summary>Deletes the Guide row (cascading its state rows) and marks the operation Committed, in one transaction.</summary>
    void Commit(Guid operationId, Guid guideId, Action beforeCommit);

    /// <summary>
    /// Deletes the game (cascading its guides, their state rows and its provider
    /// link), clears a LastActiveGuideId that named one of its guides, and marks
    /// the operation Committed, in one transaction. With no guides, there is no
    /// operation and only the game is deleted.
    /// </summary>
    void CommitGame(Guid? operationId, Guid gameId, IReadOnlyList<Guid> guideIds, Action beforeCommit);

    /// <summary>Moves a Prepared deletion's trash back to content, then removes its row.</summary>
    void RollBack(Guid operationId);

    /// <summary>Deletes a Committed deletion's trash, then removes its row.</summary>
    void Finish(Guid operationId);
}
```

In `SqliteLibraryRepository.cs`, inside `private sealed class DeletionJournal`, add after `GetGuide`:

```csharp
        public GameForRemoval? GetGameForRemoval(Guid gameId)
        {
            using SqliteConnection connection = owner.OpenConnection();
            string title;
            string? artwork;
            using (SqliteCommand read = connection.CreateCommand())
            {
                read.CommandText = "SELECT Title, ArtworkRelativePath FROM Games WHERE Id = $id";
                read.Parameters.AddWithValue("$id", gameId.ToString("N"));
                using SqliteDataReader reader = read.ExecuteReader();
                if (!reader.Read())
                {
                    return null;
                }
                title = reader.GetString(0);
                artwork = NullableString(reader, 1);
            }
            return new GameForRemoval(title, artwork, GuideIdsOf(connection, null, gameId));
        }

        private static List<Guid> GuideIdsOf(SqliteConnection connection, SqliteTransaction? transaction, Guid gameId)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT Id FROM Guides WHERE GameId = $game ORDER BY Id";
            command.Parameters.AddWithValue("$game", gameId.ToString("N"));
            using SqliteDataReader reader = command.ExecuteReader();
            List<Guid> ids = [];
            while (reader.Read())
            {
                ids.Add(Guid.ParseExact(reader.GetString(0), "N"));
            }
            return ids;
        }
```

After `Prepare`, add:

```csharp
        public void PrepareGame(Guid operationId, IReadOnlyList<Guid> guideIds)
        {
            using SqliteConnection connection = owner.OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO FileOperations (Id, Kind, Phase, ManifestJson, CreatedUtcMs)
                VALUES ($id, 'DeleteGame', 'Prepared', $manifest, $now)
                """;
            command.Parameters.AddWithValue("$id", operationId.ToString("N"));
            command.Parameters.AddWithValue("$manifest",
                FileOperationManifest.Create(FileOperationKind.DeleteGame, operationId, guideIds));
            command.Parameters.AddWithValue("$now", owner.clock.GetUtcNow().ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
        }
```

After `Commit`, add:

```csharp
        public void CommitGame(Guid? operationId, Guid gameId, IReadOnlyList<Guid> guideIds, Action beforeCommit)
        {
            if (operationId is null && guideIds.Count != 0)
            {
                throw new ArgumentException("Guides are only deleted under a file operation.", nameof(operationId));
            }
            using SqliteConnection connection = owner.OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            int Execute(string sql)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$game", gameId.ToString("N"));
                command.Parameters.AddWithValue("$op", (object?)operationId?.ToString("N") ?? DBNull.Value);
                return command.ExecuteNonQuery();
            }
            // The remover holds the write gate, so this only keeps the delete exact.
            if (!GuideIdsOf(connection, transaction, gameId).Order().SequenceEqual(guideIds.Order()))
            {
                throw new InvalidDataException("The game's guides changed.");
            }
            Execute("""
                DELETE FROM Settings
                WHERE Key = 'LastActiveGuideId' AND Value IN (SELECT Id FROM Guides WHERE GameId = $game)
                """);
            // Cascaded Guides, ReadingStates and ReaderPreferences deletes aren't counted.
            if (Execute("DELETE FROM Games WHERE Id = $game") != 1)
            {
                throw new InvalidDataException("The game to delete is missing.");
            }
            if (operationId is not null && Execute("""
                UPDATE FileOperations SET Phase = 'Committed'
                WHERE Id = $op AND Kind = 'DeleteGame' AND Phase = 'Prepared'
                """) != 1)
            {
                throw new InvalidDataException("The deletion's file operation is missing.");
            }
            beforeCommit();
            transaction.Commit();
        }
```

- [ ] **Step 4: Write the remover**

Create `src/DesktopGuides.Infrastructure/Storage/GameRemover.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>
/// Removes a game with its guides, their state and owned files, its provider
/// link and its artwork. The removal holds the library write gate; each
/// content/&lt;guide&gt; moves to .trash/&lt;op&gt;/&lt;guide&gt; under one
/// journaled DeleteGame operation, so any failure before the commit moves them
/// all back, and a crash is finished by the startup reconciler. A game without
/// guides needs no operation. The artwork is deleted after the commit, and the
/// startup sweep deletes it if that fails.
/// </summary>
public sealed class GameRemover
{
    private readonly SqliteLibraryRepository repository;
    private readonly ILibraryPaths paths;
    private readonly IArtworkStore artwork;
    private readonly Action<RemovalCheckpoint> checkpoint;

    public GameRemover(SqliteLibraryRepository repository, ILibraryPaths paths, IArtworkStore artwork)
        : this(repository, paths, artwork, _ => { })
    {
    }

    internal GameRemover(
        SqliteLibraryRepository repository, ILibraryPaths paths, IArtworkStore artwork,
        Action<RemovalCheckpoint> checkpoint)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.artwork = artwork ?? throw new ArgumentNullException(nameof(artwork));
        this.checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
    }

    /// <summary>The game with its guide and file counts, or null when it's already gone.</summary>
    public async Task<GameRemovalPreview?> DescribeAsync(Guid gameId, CancellationToken token = default)
    {
        RequireId(gameId);
        Game? game = await repository.GetGameAsync(gameId, token);
        if (game is null)
        {
            return null;
        }
        IReadOnlyList<Guide> guides = await repository.ListGuidesAsync(gameId, token);
        List<OwnedGuideTree> trees = await Task.Run(
            () => CaptureAll(guides.Select(guide => guide.Id).ToList()), token);
        return Preview(gameId, game.Title, trees);
    }

    /// <summary>
    /// Removes the game when it still has expectedGuideCount guides; otherwise
    /// returns CountChanged with a fresh preview. The token only cancels the
    /// wait for the write gate; once the deletion is journaled it runs to an outcome.
    /// </summary>
    public Task<GameRemovalResult> RemoveAsync(
        Guid gameId, int expectedGuideCount, CancellationToken token = default)
    {
        RequireId(gameId);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedGuideCount);
        return repository.RunDeletionAsync(journal => Remove(journal, gameId, expectedGuideCount), token);
    }

    private GameRemovalResult Remove(IDeletionJournal journal, Guid gameId, int expectedGuideCount)
    {
        GameForRemoval? game = journal.GetGameForRemoval(gameId);
        if (game is null)
        {
            return new GameRemovalResult(GameRemovalOutcome.NotFound, false, null);
        }
        List<OwnedGuideTree> trees = CaptureAll(game.GuideIds);
        if (game.GuideIds.Count != expectedGuideCount)
        {
            return new GameRemovalResult(
                GameRemovalOutcome.CountChanged, false, Preview(gameId, game.Title, trees));
        }
        if (game.GuideIds.Count == 0)
        {
            try
            {
                journal.CommitGame(null, gameId, [], () => checkpoint(RemovalCheckpoint.InCommit));
            }
            catch (Exception error)
            {
                throw new GameRemovalException(GameRemovalIssue.Failed, error);
            }
            DeleteArtwork(game.ArtworkRelativePath);
            return new GameRemovalResult(GameRemovalOutcome.Removed, false, null);
        }
        if (game.GuideIds.Any(journal.IsPending))
        {
            // A second deletion would strand the first one's row, and startup
            // would then refuse to open the library.
            throw new GameRemovalException(GameRemovalIssue.RestoreFailed);
        }
        Guid operationId = Guid.NewGuid();
        try
        {
            journal.PrepareGame(operationId, game.GuideIds);
        }
        catch (Exception error)
        {
            // Nothing has changed yet, so there's nothing to roll back.
            throw new GameRemovalException(GameRemovalIssue.Failed, error);
        }
        try
        {
            checkpoint(RemovalCheckpoint.Prepared);
            for (int index = 0; index < game.GuideIds.Count; index++)
            {
                if (!trees[index].Exists)
                {
                    continue;
                }
                Guid guideId = game.GuideIds[index];
                string trashPath = paths.GetTrashedGuideRoot(operationId, guideId);
                Directory.CreateDirectory(Path.GetDirectoryName(trashPath)!);
                Directory.Move(paths.GetGuideRoot(guideId), trashPath);
                checkpoint(RemovalCheckpoint.MovedGuide);
            }
            checkpoint(RemovalCheckpoint.Moved);
            journal.CommitGame(operationId, gameId, game.GuideIds, () => checkpoint(RemovalCheckpoint.InCommit));
        }
        catch (Exception error)
        {
            try
            {
                journal.RollBack(operationId);
            }
            catch (Exception rollBackError)
            {
                // The Prepared row stays, so the next startup moves the trash back.
                throw new GameRemovalException(
                    GameRemovalIssue.RestoreFailed, new AggregateException(error, rollBackError));
            }
            throw new GameRemovalException(GameRemovalIssue.Failed, error);
        }
        bool cleanupPending = false;
        try
        {
            checkpoint(RemovalCheckpoint.Committed);
            journal.Finish(operationId);
        }
        catch (Exception)
        {
            // The Committed row stays, so the next startup deletes the trash.
            cleanupPending = true;
        }
        DeleteArtwork(game.ArtworkRelativePath);
        return new GameRemovalResult(GameRemovalOutcome.Removed, cleanupPending, null);
    }

    private void DeleteArtwork(string? relativePath)
    {
        if (relativePath is null)
        {
            return;
        }
        try
        {
            artwork.Delete(relativePath);
        }
        // The row is gone, so the next startup sweep deletes the file.
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void RequireId(Guid gameId)
    {
        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("A game ID is required.", nameof(gameId));
        }
    }

    private static GameRemovalPreview Preview(Guid gameId, string title, List<OwnedGuideTree> trees) =>
        new(gameId, title, trees.Count, trees.Sum(tree => tree.FileCount));

    private List<OwnedGuideTree> CaptureAll(IReadOnlyList<Guid> guideIds) =>
        guideIds.Select(id => Capture(paths.GetGuideRoot(id))).ToList();

    private static OwnedGuideTree Capture(string root)
    {
        try
        {
            return OwnedGuideTree.Capture(root);
        }
        catch (InvalidDataException error)
        {
            throw new GameRemovalException(GameRemovalIssue.Unsafe, error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new GameRemovalException(GameRemovalIssue.Failed, error);
        }
    }
}
```

- [ ] **Step 5: Retire T04.2's empty-game path**

- Delete `src/DesktopGuides.Core/Library/GameRemover.cs`, `tests/DesktopGuides.Core.Tests/GameRemoverTests.cs` and `tests/DesktopGuides.Infrastructure.Tests/EmptyGameRemovalTests.cs` with `git rm`.
- Delete `RemoveEmptyGameAsync` and its `// Deletes the game only while it has no guides.` comment from `ILibraryRepository.cs`.
- Delete `RemoveEmptyGameAsync` (lines 112–157) from `SqliteLibraryRepository.cs`.
- Delete `NextRemoval`, `RemoveCalls` and `RemoveEmptyGameAsync` from `ImporterFakes.cs`.
- Delete `EmptyGameRemovalOutcome` and `EmptyGameRemoval` (and the blank line and doc comment above them) from `GameRemovalContracts.cs`, so the file starts with `namespace DesktopGuides.Core.Library;` followed by the Task 1 types.

Run: `grep -rn "EmptyGameRemoval\|RemoveEmptyGameAsync\|NextRemoval\|RemoveCalls" src tests tools`
Expected: only `src/DesktopGuides.Production/ShellWindow.xaml.cs` matches (fixed in Task 3), or no match.

- [ ] **Step 6: Run the tests to verify they pass**

Run: Infrastructure tests with `--filter "FullyQualifiedName~GameRemovalTests"`.
Expected: PASS, 30 tests.

Then both full suites.
Expected: Core PASS (Task 1's count minus `GameRemoverTests`); Infrastructure PASS (407 minus `EmptyGameRemovalTests`' 9, plus 30). Record both counts in the ledger. `GuideRemoverTests`, `DeletionJournalTests`, `FileOperationReconciliationTests` and the import tests pass unchanged.

- [ ] **Step 7: Commit**

Show the message in chat first, then:

```bash
git add -A src/DesktopGuides.Core src/DesktopGuides.Infrastructure tests
git commit -m "feat(storage): remove a game with its guides through the deletion journal

- Add an Infrastructure GameRemover that moves every guide's content to
  the trash under one DeleteGame operation, then deletes the game in one
  transaction and its artwork best effort.
- Return CountChanged with a fresh preview when the guide count differs.
- Add GetGameForRemoval, PrepareGame and CommitGame to the deletion
  journal, and the MovedGuide checkpoint.
- Retire RemoveEmptyGameAsync, EmptyGameRemoval and the Core GameRemover;
  their cases move to GameRemovalTests.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: Shell dialog and remove flow

**Files:**
- Modify: `src/DesktopGuides.Production/RemoveGameDialog.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (line 109; line 260; `UpdateRemoveGameAction` at 518; `RemoveGameClicked` at 1001)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (delete the `RemoveGameHint` TextBlock, lines 260–265)

**Interfaces:**
- Consumes (Task 1): `GameRemovalPreview`, `GameRemovalResult`, `GameRemovalOutcome`, `GameRemovalIssue`, `GameRemovalException`, `GameRemovalPresentation.DialogTitle(string, int)`, `DialogBody(int, int)`, `CountChanged(int)`, `Removed(string, bool)`, `AlreadyRemoved(string)`, `Error(GameRemovalIssue, string)`.
- Consumes (Task 2): `new GameRemover(SqliteLibraryRepository, ILibraryPaths, IArtworkStore)`, `DescribeAsync(Guid)`, `RemoveAsync(Guid, int)`.
- Produces: `RemoveGameDialog.Create(GameRemovalPreview preview, bool countChanged, XamlRoot root)`; UIA IDs `RemoveGameDialog`, `RemoveGameMessage`, `RemoveGameCountChanged`.

The shell has no unit-test harness; its behavior is pinned by Task 4's installed smoke phases, which run in CI (ruling 11). This task's gate is the Production build plus green Core and Infrastructure suites.

- [ ] **Step 1: Rewrite the dialog**

Replace `src/DesktopGuides.Production/RemoveGameDialog.cs` with:

```csharp
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

/// <summary>
/// Confirms removing a game, stating its guide and file counts. Cancel is the
/// default, so Enter and Escape both cancel.
/// </summary>
internal static class RemoveGameDialog
{
    public static ContentDialog Create(GameRemovalPreview preview, bool countChanged, XamlRoot root)
    {
        StackPanel content = new() { Spacing = 12 };
        if (countChanged)
        {
            content.Children.Add(Line(
                GameRemovalPresentation.CountChanged(preview.GuideCount), "RemoveGameCountChanged"));
        }
        content.Children.Add(Line(
            GameRemovalPresentation.DialogBody(preview.GuideCount, preview.FileCount), "RemoveGameMessage"));
        ContentDialog dialog = new()
        {
            Title = GameRemovalPresentation.DialogTitle(preview.Title, preview.GuideCount),
            Content = content,
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        AutomationProperties.SetAutomationId(dialog, "RemoveGameDialog");
        return dialog;
    }

    private static TextBlock Line(string text, string automationId)
    {
        TextBlock line = new()
        {
            Text = text,
            Style = (Style)Application.Current.Resources["DesktopGuidesBodyStyle"],
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(line, automationId);
        return line;
    }
}
```

- [ ] **Step 2: Enable the button with guides and drop the hint**

In `ShellWindow.xaml`, delete the `RemoveGameHint` element:

```xml
                    <TextBlock x:Name="RemoveGameHint"
                               Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                               HorizontalAlignment="Right"
                               TextWrapping="Wrap"
                               Visibility="Collapsed"
                               AutomationProperties.AutomationId="RemoveGameHint" />
```

The enclosing `StackPanel` stays, holding only the action `Grid`.

In `ShellWindow.xaml.cs`:
- Delete line 109: `RemoveGameHint.Text = GameRemovalPresentation.GuidesFirst;`
- Line 260 becomes `gameRemover = new GameRemover(repository, paths, artwork);` (now the `DesktopGuides.Infrastructure.Storage` type, already imported).
- Replace `UpdateRemoveGameAction` with:

```csharp
    private void UpdateRemoveGameAction()
    {
        RemoveGameButton.IsEnabled = loadedGameGuideCount is not null && !closeRequested &&
            !importRequested && !gameEditorRequested && !removeRequested &&
            !gameRemoveRequested && refreshCancel is null;
    }
```

- [ ] **Step 3: Count, confirm and re-prompt in the remove flow**

In `RemoveGameClicked`, replace everything inside the `RunNavigationAsync` lambda from `GameRemover remover = gameRemover` to the lambda's closing `AddGameButton.Focus(FocusState.Programmatic);` with:

```csharp
                GameRemover remover = gameRemover
                    ?? throw new InvalidOperationException("The library is not ready.");
                GameRemovalPreview? preview;
                try
                {
                    preview = await remover.DescribeAsync(route.GameId);
                }
                catch (Exception error)
                {
                    if (!closeRequested)
                    {
                        ShowErrorStatus(GameRemovalError(error, shownTitle));
                    }
                    return;
                }
                if (closeRequested)
                {
                    return;
                }
                // A null preview means it was removed elsewhere before the dialog (T04.2 ruling 14).
                string title = preview?.Title ?? shownTitle;
                GameRemovalResult? result = null;
                bool countChanged = false;
                while (preview is not null)
                {
                    title = preview.Title;
                    ContentDialog dialog = RemoveGameDialog.Create(preview, countChanged, Navigation.XamlRoot);
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
                        result = await remover.RemoveAsync(preview.GameId, preview.GuideCount);
                    }
                    catch (Exception error)
                    {
                        // Nothing changed in the database, so the page still shows the game.
                        if (!closeRequested)
                        {
                            ShowErrorStatus(GameRemovalError(error, title));
                        }
                        return;
                    }
                    if (closeRequested)
                    {
                        return;
                    }
                    if (result.Outcome != GameRemovalOutcome.CountChanged)
                    {
                        break;
                    }
                    // Show the dialog again with the fresh counts.
                    preview = result.Current!;
                    countChanged = true;
                }
                rendered = true;
                // Clearing the back stack keeps Back from reaching the removed page.
                navigator.ResetToLibrary();
                await RenderCurrentAsync();
                ShowTransientStatus(result is { Outcome: GameRemovalOutcome.Removed }
                    ? GameRemovalPresentation.Removed(title, result.CleanupPending)
                    : GameRemovalPresentation.AlreadyRemoved(title));
                AddGameButton.Focus(FocusState.Programmatic);
```

Add after `RemoveGameClicked`:

```csharp
    private static string GameRemovalError(Exception error, string title) =>
        GameRemovalPresentation.Error(
            error is GameRemovalException removal ? removal.Issue : GameRemovalIssue.Failed, title);
```

The `finally` block is unchanged: when nothing rendered, it restores Edit, Import and Refresh, calls `UpdateRemoveGameAction` and focuses Remove game.

- [ ] **Step 4: Verify nothing references the retired members**

Run: `grep -rn "RemoveGameHint\|GuidesFirst\|HasGuides\|EmptyGameRemoval\|ShowWarningStatus(GameRemoval" src`
Expected: no match.

- [ ] **Step 5: Build and run the suites**

Run: the Production build, then the Core and Infrastructure suites.
Expected: build succeeds with 0 errors and no new warnings; both suites PASS with Task 2's counts.

- [ ] **Step 6: Commit**

Show the message in chat first, then:

```bash
git add src/DesktopGuides.Production/RemoveGameDialog.cs src/DesktopGuides.Production/ShellWindow.xaml src/DesktopGuides.Production/ShellWindow.xaml.cs
git commit -m "feat(shell): remove a game with its guides after a counted confirmation

- Enable Remove game once the guide list loads, and drop RemoveGameHint.
- State the guide and file counts in the dialog, and show it again with
  the fresh counts when they changed.
- Map each removal issue to its status message.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: Seed, installed smoke and CI

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`describe-actions` at about line 174; `seed-actions` at about line 670; `InsertGuideAsync` at about line 755)
- Modify: `tools/p1/windows_shell_install.ps1` (`Assert-GameActionsState` at about line 936)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (game-actions block at about line 2075; design-language at about lines 1243 and 1253; failure ID list at about line 2819)
- Create: `docs/p1/evidence/t04-3-game-removal/remove-game-with-guides-confirm-light.png` and `-dark.png` (from CI artifacts)

**Interfaces:**
- Consumes (Task 3): UIA `RemoveGameButton`, `RemoveGameDialog`, `RemoveGameMessage`, `RemoveGameCountChanged`, `PrimaryButton`, `CloseButton`, `ResumeGuide`, `AddGameButton`; the dialog title "Remove Guided Remove Game and its 2 guides?" and body for 2 guides and 3 files; status "Removed Guided Remove Game.".
- Produces: `seed-actions` JSON gains `GuidedGameId` and `GuidedGuideIds` (array of N-format IDs). `describe-actions` JSON gains `ContentDirectories` (array of guide IDs), `TrashEntries` (int) and `FileOperations` (int).

The smoke phases are this task's tests, and Task 3's (ruling 11). They first run in CI after the push.

- [ ] **Step 1: Let the seed write an HTML guide with an asset**

Replace `InsertGuideAsync`'s signature and file-writing lines with:

```csharp
static async Task InsertGuideAsync(
    ManagedPathResolver paths, Guid gameId, Guid guideId, string title, long now,
    string format = "Txt", string primaryPath = "guide.txt", params string[] assetPaths)
{
    string guideRoot = paths.GetGuideRoot(guideId);
    Directory.CreateDirectory(guideRoot);
    string content = Path.Combine(guideRoot, primaryPath);
    await File.WriteAllTextAsync(content, "Test guide.");
    foreach (string asset in assetPaths)
    {
        string assetPath = Path.Combine(guideRoot, asset);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        await File.WriteAllTextAsync(assetPath, "Test asset.");
    }
    byte[] bytes = await File.ReadAllBytesAsync(content);
```

In its SQL, replace `'Txt', $root, 'guide.txt',` with `$format, $root, $primary,` and add:

```csharp
    command.Parameters.AddWithValue("$format", format);
    command.Parameters.AddWithValue("$primary", primaryPath);
```

Existing five-argument calls are unchanged.

- [ ] **Step 2: Seed Guided Remove Game**

In `seed-actions`, after the `emptyGameId` line, add:

```csharp
    Guid guidedGameId = await AddActionsGameAsync(
        "Guided Remove Game", "900102", SolidPng(60, 90, 0x3C, 0x7A, 0x4E));
    Guid guidedWalkthroughId = Guid.NewGuid();
    Guid guidedMapId = Guid.NewGuid();
```

After the Beta `InsertGuideAsync` line, add:

```csharp
    await InsertGuideAsync(paths, guidedGameId, guidedWalkthroughId, "Guided Walkthrough", actionsNow);
    // Never opened in the smoke, so no WebView2 state is needed (ruling 7).
    await InsertGuideAsync(
        paths, guidedGameId, guidedMapId, "Guided Map Guide", actionsNow,
        "Html", "index.html", "images/map.png");
```

Add a second statement to the `ExecuteSql` block, inside the same raw string:

```sql
        UPDATE ReadingStates SET EstimatedFraction = 0.2 WHERE GuideId = '{guidedWalkthroughId:N}';
```

Change the Resume guide to Guided Walkthrough (ruling 6):

```csharp
    await repository.SaveSettingsAsync(actionsSettings with { LastActiveGuideId = guidedWalkthroughId });
```

Add to the output object:

```csharp
        GuidedGameId = guidedGameId.ToString("N"),
        GuidedGuideIds = new[] { guidedWalkthroughId.ToString("N"), guidedMapId.ToString("N") },
```

- [ ] **Step 3: Report content, trash and file operations**

In `describe-actions`, after the `artworkFolders` local, add:

```csharp
    string[] contentDirectories = Directory.Exists(actionsPaths.ContentRoot)
        ? [.. Directory.EnumerateDirectories(actionsPaths.ContentRoot).Select(folder => Path.GetFileName(folder))]
        : [];
    int trashEntries = Directory.Exists(actionsPaths.TrashRoot)
        ? Directory.EnumerateFileSystemEntries(actionsPaths.TrashRoot).Count()
        : 0;
```

Add to the output object, after `ArtworkFolders`:

```csharp
        ContentDirectories = contentDirectories,
        TrashEntries = trashEntries,
        FileOperations = Rows("SELECT COUNT(*) FROM FileOperations", reader => reader.GetInt64(0)).Single(),
```

Run: the seed build.
Expected: build succeeds with 0 errors.

- [ ] **Step 4: Check the guided game is gone after each run**

In `Assert-GameActionsState`, before `return $state`, add:

```powershell
    if (@($state.ArtworkFolders) -contains $seed.GuidedGameId) {
        throw "$label, the guided game's artwork folder remains."
    }
    $removedGuides = @($seed.GuidedGuideIds)
    $leftContent = @(@($state.ContentDirectories) | Where-Object { $removedGuides -contains $_ })
    if ($leftContent.Count -ne 0) {
        throw "$label, removed guide content remains: $($leftContent -join ', ')."
    }
    $leftStates = @(@($state.ReadingStates) | Where-Object { $removedGuides -contains $_.GuideId })
    if ($leftStates.Count -ne 0) {
        throw "$label, removed guides' reading states remain."
    }
    foreach ($kept in @($seed.AlphaGuideId, $seed.BetaGuideId)) {
        if (@($state.ContentDirectories) -notcontains $kept) {
            throw "$label, guide $kept lost its content."
        }
    }
    if ($state.TrashEntries -ne 0 -or $state.FileOperations -ne 0) {
        throw "$label, $($state.TrashEntries) trash entries and $($state.FileOperations) file operations remain."
    }
```

The existing `GameCount -ne 1` check covers the guided game's rows, and the `LastActiveGuideId -ne BetaGuideId` check covers a Resume guide naming a removed guide.

- [ ] **Step 5: Add the guided removal phases to the smoke**

In the game-actions block, replace `$hint = "Remove this game's guides first."` with:

```powershell
        $guidedTitle = 'Guided Remove Game'
```

Inside `if ($Mode -eq 'game-actions') {`, insert before `$before = (Wait-GameRow $renameTitle).Current.HelpText`:

```powershell
            # The guided game goes first, while it is still the Resume guide's game (ruling 6).
            [void](Wait-Name 'ResumeGuide' 'Resume Guided Walkthrough')
            [void](Wait-GameRow $guidedTitle)
            Select-Element $guidedTitle
            [void](Wait-Name 'GameHeading' $guidedTitle)
            [void](Wait-Status 'Game ready.')
            [void](Wait-GuideRowCount 2)
            Invoke-Element (Wait-EnabledById 'RemoveGameButton')
            [void](Wait-VisibleById 'RemoveGameDialog')
            [void](Wait-VisibleName "Remove $guidedTitle and its 2 guides?")
            [void](Wait-Name 'RemoveGameMessage' ('This removes the game, its 2 guides with their reading progress, ' +
                "and their 3 managed files from Desktop Guides. The original files you imported aren't affected."))
            Assert-Absent 'RemoveGameCountChanged'
            Wait-FocusedId 'CloseButton'
            $report.guidedDialogScreenshot = Save-WindowScreenshot 'remove-game-with-guides-confirm'
            [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
            [void](Wait-HiddenById 'RemoveGameDialog')
            Wait-FocusedId 'RemoveGameButton'
            [void](Wait-Name 'GameHeading' $guidedTitle)
            [void](Wait-GuideRowCount 2)
            $report.phases += 'remove-with-guides-cancel'

            Invoke-Element (Wait-EnabledById 'RemoveGameButton')
            [void](Wait-VisibleById 'RemoveGameDialog')
            Invoke-Element (Wait-EnabledById 'PrimaryButton')
            [void](Wait-HiddenById 'RemoveGameDialog')
            [void](Wait-Name 'LibraryHeading' 'Library')
            [void](Wait-Status "Removed $guidedTitle.")
            Wait-FocusedId 'AddGameButton'
            Assert-Absent 'ResumeGuide'
            [void](Wait-GameRow $renameTitle)
            if ((Count-GameRows $guidedTitle) -ne 0) {
                throw "The removed game '$guidedTitle' is still listed."
            }
            $report.phases += 'remove-with-guides'

```

Replace the `remove-disabled-with-guides` phase (from `$remove = Wait-VisibleById 'RemoveGameButton'` through `$report.phases += 'remove-disabled-with-guides'`) with:

```powershell
            $remove = Wait-EnabledById 'RemoveGameButton'
            if ($remove.Current.HelpText) {
                throw "Remove game's HelpText was '$($remove.Current.HelpText)' for a game with guides."
            }
            Assert-Absent 'RemoveGameHint'
            $report.phases += 'remove-enabled-with-guides'
```

Delete `Assert-Absent 'RemoveGameHint'` from the empty-game phase (about line 2185); the element no longer exists, and the guided phase above already asserts it once.

In the persisted branch, after `[void](Wait-GameRow $renamed)`, add:

```powershell
            if ((Count-GameRows $guidedTitle) -ne 0) {
                throw "The removed game '$guidedTitle' came back."
            }
```

In design-language, delete both `Assert-InsideWindow 'RemoveGameHint'` lines.

In the failure ID list, replace `'RemoveGameHint'` with `'RemoveGameCountChanged'`.

Run: `grep -n "RemoveGameHint\|\$hint" tools/p1/windows_shell_ui_smoke.ps1`
Expected: exactly one match, the `Assert-Absent 'RemoveGameHint'` in `remove-enabled-with-guides`.

- [ ] **Step 6: Check the scripts**

Run: `LC_ALL=C grep -n '[^ -~	]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1`
Expected: no output.

Run the PowerShell 5.1 parse check: scp a temporary `.ps1` that calls `[System.Management.Automation.Language.Parser]::ParseFile` on both scripts under `E:\work\desktop-guides\t04-3` and prints the error count, then run it with `powershell -NoProfile -File`.
Expected: `0` errors for each script.

Run: the seed build, the Production build, and both suites.
Expected: all succeed; suite counts unchanged from Task 3.

- [ ] **Step 7: Commit**

Show the message in chat first, then:

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_install.ps1 tools/p1/windows_shell_ui_smoke.ps1
git commit -m "test(p1): cover removing a game with its guides in the installed smoke

- Seed Guided Remove Game with a TXT guide and a nested HTML guide, as
  the Resume game.
- Cancel, then remove it in the game-actions smoke, in light and dark.
- Check that its rows, content, artwork, trash and file operations are
  gone after each run, and that it stays gone after a relaunch.
- Replace remove-disabled-with-guides with remove-enabled-with-guides
  and drop the RemoveGameHint asserts.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 8: Push and run CI (needs the user's OK)**

Ask the user for approval to push `feat/p1-t04-3-game-removal`. After the OK, push, open the PR as a draft (Task 5 fills in its body), and watch the run.
Expected: every job succeeds, including `production-shell-ui` with phases `remove-with-guides-cancel`, `remove-with-guides` and `remove-enabled-with-guides` in both the light and dark reports.

On a `production-shell-ui` failure, use superpowers:systematic-debugging on the uploaded artifacts first. A host E2E run follows `docs/p1/e2e-testing.md`'s backup and cleanup rules.

- [ ] **Step 9: Copy the evidence screenshots**

Download the `production-shell-ui` artifact and copy the light and dark `remove-game-with-guides-confirm` screenshots to `docs/p1/evidence/t04-3-game-removal/remove-game-with-guides-confirm-light.png` and `-dark.png`. Look at both: the title states 2 guides, the body states 3 managed files, and Cancel is the highlighted default.

### Task 5: Traceability docs and the PR

**Files:**
- Modify: `docs/p1/t04-3-game-removal-design.md` (status line; append a verification record)
- Modify: `docs/p1/implementation-plan.md` (a T04.3 paragraph after the T04.2 paragraph at about line 683)
- Modify: `docs/p1/e2e-testing.md:251` (Game actions row)
- Modify: `docs/progress.md` (a T04.3 row after the T04.2 row at line 24)
- Modify: `docs/p1/t04-2-game-actions-design.md` (supersede note under its title)

**Interfaces:**
- Consumes: the green CI run ID from Task 4 Step 8, the evidence paths from Task 4 Step 9, the final suite counts and the PR number.

No code; no TDD cycle. Each step quotes the run ID and counts from real output.

- [ ] **Step 1: Write the verification record**

Change the design's status line to `Status: implemented on \`feat/p1-t04-3-game-removal\`; verified by CI run <run-id> on <date>.`, filling both from Task 4 Step 8. Append:

```markdown
## T04.3 verification record

- Core tests: <core-count> passed on `pcsx2-win` (Release, x64).
- Infrastructure tests: <infra-count> passed on `pcsx2-win`, including the
  30 `GameRemovalTests`.
- CI run [<run-id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run-id>):
  every job passed. `production-shell-ui` ran `remove-with-guides-cancel`,
  `remove-with-guides` and `remove-enabled-with-guides` in light and dark,
  and `describe-actions` found no Guided Remove Game rows, content, artwork,
  trash entries or file operations after each run and after the relaunch.
- Screenshots: [light](evidence/t04-3-game-removal/remove-game-with-guides-confirm-light.png)
  and [dark](evidence/t04-3-game-removal/remove-game-with-guides-confirm-dark.png).
- Not exercised in the installed smoke: the changed-count re-prompt
  (ruling 2), a failed restore and a pending cleanup. Infrastructure tests
  cover them.
```

The `<…>` markers here are values copied from Task 4's output while doing this step, not placeholders left in the committed file; Step 6 checks that none remain.

- [ ] **Step 2: Add the M2 paragraph**

After the T04.2 paragraph in `docs/p1/implementation-plan.md`, add:

```markdown
T04.3 is implemented on `feat/p1-t04-3-game-removal` (PR #<pr>). Remove game
works for every game: the confirmation states the guide and managed-file
counts, and one Remove deletes the game, its guides, state, preferences,
provider link, artwork and content through the T15.3 deletion journal as a
`DeleteGame` operation. A changed count shows the dialog again rather than
deleting. See the [design and verification record](t04-3-game-removal-design.md).
```

Leave row 732 unchanged; its text already states T04.3's scope.

- [ ] **Step 3: Update the Game actions E2E row**

In `docs/p1/e2e-testing.md:251`:
- Add to the seed: `Guided Remove Game, provider-linked with artwork and two guides (Guided Walkthrough, TXT and the Resume guide; Guided Map Guide, HTML with an image)`, and change `Beta Route Guide (Resume)` to `Beta Route Guide`.
- Insert first in the light/dark sequence: `Guided Remove Game's Remove game opens \`Remove Guided Remove Game and its 2 guides?\` stating 3 managed files; Escape keeps both guides with focus on Remove game; Remove shows the Library with \`Removed Guided Remove Game.\`, no Resume and focus on Add game.`
- Replace `Linked Rename Game shows Remove game disabled with \`Remove this game's guides first.\`` with `Linked Rename Game shows Remove game enabled`.
- Add to the `describe-actions` sentence: `and no rows, content, artwork, trash entries or file operations for Guided Remove Game`. After the relaunch, add `Guided Remove Game is still gone`.
- Add `T04.3, TR04.1, TR04.2` to the traces column.

Note that Beta is still the Resume guide after the relaunch: `Open-GuideFromGame 'Beta Route Guide'` restores it during the run (ruling 6).

- [ ] **Step 4: Add the progress row**

After the T04.2 row in `docs/progress.md`, add:

```markdown
| P1 T04.3 game removal | Implemented on `feat/p1-t04-3-game-removal`, [PR #<pr>](https://github.com/ilya-slalom/desktop-guides/pull/<pr>). | Remove game removes a game with its guides after a confirmation that states the guide and managed-file counts, through the deletion journal; Cancel and a changed count change nothing. CI run [<run-id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run-id>) passed `game-actions`; see the [verification record](p1/t04-3-game-removal-design.md#t043-verification-record). |
```

- [ ] **Step 5: Mark the T04.2 rule superseded**

Under the T04.2 design's title, add:

```markdown
> Superseded in part by [T04.3](t04-3-game-removal-design.md): Remove game is
> enabled for a game with guides, and `RemoveGameHint` is removed. The rest
> of this design is as shipped.
```

- [ ] **Step 6: Check for leftovers and commit**

Run: `grep -n '<run-id>\|<pr>\|<core-count>\|<infra-count>\|<date>' docs/p1/t04-3-game-removal-design.md docs/p1/implementation-plan.md docs/p1/e2e-testing.md docs/progress.md`
Expected: no output.

Show the message in chat first, then:

```bash
git add docs/p1/t04-3-game-removal-design.md docs/p1/implementation-plan.md docs/p1/e2e-testing.md docs/progress.md docs/p1/t04-2-game-actions-design.md docs/p1/evidence/t04-3-game-removal
git commit -m "docs(p1): record T04.3 verification

- Add the verification record and evidence screenshots.
- Add T04.3 to the M2 notes, the Game actions E2E row and progress.
- Note in the T04.2 design which rules T04.3 supersedes.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 7: Push and fill in the PR (needs the user's OK)**

Ask for approval to push. After the OK, push and update the draft PR's body:

```markdown
## Target task
T04.3: count-confirmed removal of a game with its guides, through the T15.3 trash protocol.

## Prerequisites
- T04.2 game actions: merged (PR #25)
- T04.4 metadata providers: merged (PR #14)
- T15.3 deletion journal: merged (PR #22)

## Outcome
Remove game works for every game. The confirmation states the guide and managed-file counts; one Remove deletes the game, its guides, reading state, preferences, provider link, artwork and content, and nothing else. Cancel and a changed count change nothing. A crash at any point leaves the whole game or no game, and startup finishes the rest.

## Screenshots
| Light | Dark |
| --- | --- |
| ![light](https://github.com/ilya-slalom/desktop-guides/blob/<head-sha>/docs/p1/evidence/t04-3-game-removal/remove-game-with-guides-confirm-light.png?raw=true) | ![dark](https://github.com/ilya-slalom/desktop-guides/blob/<head-sha>/docs/p1/evidence/t04-3-game-removal/remove-game-with-guides-confirm-dark.png?raw=true) |

## Verification
- Core <core-count>, Infrastructure <infra-count> on `pcsx2-win`.
- CI run <run-id>: all jobs green, including `production-shell-ui`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

Fill `<head-sha>` with the pushed commit. Leave the PR in draft until the final review is done.
