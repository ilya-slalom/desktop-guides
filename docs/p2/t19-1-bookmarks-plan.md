# T19.1 PR a (Bookmark storage and backup) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The library stores bookmarks: a `Bookmarks` table at schema
version 5, repository methods to list, add, rename, delete and restore
them, the 500-per-guide limit, and backup and restore coverage. No UI;
PR b adds the Bookmarks tab.

**Architecture:**
- **Core** gets the `Bookmark` record, `BookmarkRules` (the limits and
  label trimming), `BookmarkLimitException` and an `IBookmarkStore`
  interface that `ILibraryRepository` inherits, like
  `IReadingLocationStore`.
- **Infrastructure** adds `LibrarySchema.Version5` and implements the
  store in `SqliteLibraryRepository` under the existing write gate.
- **Backup needs no production change:** export snapshots the whole
  database, and restore already checks integrity (including `CHECK`
  constraints), foreign keys and the exact schema. The tests pin that.

**Tech Stack:** .NET 10, Microsoft.Data.Sqlite, xUnit.

**Spec:** [t19-1-bookmarks-design.md](t19-1-bookmarks-design.md)
(approved 16 October 2026) and
[section 5 of the P2 design](../p2-technical-design.md#5-s19-bookmarks-t191).

## Global Constraints

- **The table** is `Bookmarks`: `Id` (32-character GUID, `N` format),
  `GuideId` (`REFERENCES Guides(Id) ON DELETE CASCADE`), `LocatorJson`
  (1 to 4096 characters), `Label` (1 to 80 characters), `CreatedUtcMs`
  and `UpdatedUtcMs` (both at least 0), with an index on `GuideId`.
- **Exact limits in C#,** checked before the write: the locator is at
  most 4096 UTF-8 bytes (`ReaderLocationCodec.MaxBytes`); the label is 1
  to 80 characters after trimming.
- **At most 500 bookmarks per guide,** counted in the same transaction as
  the insert. No trigger.
- **Bookmarks never touch** `ReadingStates`, completion or
  `ReaderPreferences`.
- **One forward migration** takes version 4 to 5. `MigrateOrValidate`
  already writes a recovery copy and runs it in one transaction.
- **Times** are whole milliseconds, so a returned `Bookmark` equals the
  one read back.
- **Shell rules:** no `cd` in local shell commands.

## Review Focus

1. **A guide removed while a bookmark is added or restored** (the undo
   after the guide was deleted). The add must fail with "This guide is no
   longer in the library.", not a raw SQLite error. Pinned by Task 3's
   `AddingToAMissingGuideSaysSo` and `RestoringToAMissingGuideSaysSo`.
2. **An undo that would make 501 bookmarks** (one deleted, another added,
   then Undo). The restore obeys the limit. Pinned by Task 3's
   `RestoreObeysTheLimit`.
3. **A label of only spaces, or one with surrogate pairs near 80.** C#
   counts UTF-16 units, so it is stricter than SQL's character count and
   never stores what SQL would refuse. Pinned by Task 1's label cases.
4. **A pre-P2 backup restored into this app.** It must restore, then
   migrate on the next open with its data and no bookmarks. Pinned by
   Task 4's `AVersionFourBackupRestoresAndMigrates`.
5. **A hand-edited backup with an oversized label** (the `CHECK` bypassed).
   Restore must refuse it, not import it. Pinned by Task 4's
   `AnOversizedBookmarkIsInvalid`.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t19-1a) { Remove-Item -Recurse -Force E:\work\desktop-guides\t19-1a }; New-Item -ItemType Directory E:\work\desktop-guides\t19-1a | Out-Null"'
  git -C /Users/ilya.lissoboi/work/desktop-guides ls-files -z -co --exclude-standard | grep -z -v -E '^(\.claude/|az2aws)' | COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --null -cf - -T - | s 'tar -xf - -C E:\work\desktop-guides\t19-1a'
  s 'cd /d E:\work\desktop-guides\t19-1a && python tools\p0\make_fixtures.py > E:\work\t19-1a-fixtures.log 2>&1'
}
```

- **Tests:** `stage && s 'cd /d E:\work\desktop-guides\t19-1a && dotnet test tests\<project> --filter "<filter>" -nologo > E:\work\t19-1a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" /c:"[FAIL]" E:\work\t19-1a-test.log'`
- **Production build:** `s 'cd /d E:\work\desktop-guides\t19-1a && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo -warnaserror > E:\work\t19-1a-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t19-1a-build.log'`
- The Infrastructure suite needs Windows paths and the generated
  fixtures, so it runs on the host. `stage` generates the fixtures.

---
### Task 1: The bookmark contract (Core)

**Files:**
- Create: `src/DesktopGuides.Core/Library/Bookmark.cs`
- Create: `src/DesktopGuides.Core/Library/IBookmarkStore.cs`
- Modify: `src/DesktopGuides.Core/Library/ILibraryRepository.cs` (inherit `IBookmarkStore`)
- Modify: `tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs` (stubs)
- Test: `tests/DesktopGuides.Core.Tests/BookmarkRulesTests.cs`

**Interfaces:**
- Produces:
  - `sealed record Bookmark(Guid Id, Guid GuideId, string LocatorJson, string Label, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc)`
  - `static class BookmarkRules { const int MaxPerGuide = 500; const int MaxLabelLength = 80; const int MaxLocatorBytes = ReaderLocationCodec.MaxBytes; static string Label(string label); static string Locator(string locatorJson); }`
  - `sealed class BookmarkLimitException(Guid guideId) : InvalidOperationException` with `Guid GuideId`
  - `interface IBookmarkStore` with `ListBookmarksAsync`, `AddBookmarkAsync`, `RenameBookmarkAsync`, `DeleteBookmarkAsync`, `RestoreBookmarkAsync` (signatures in Step 3)

- [ ] **Step 1: Write the failing tests**

```csharp
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class BookmarkRulesTests
{
    [Theory]
    [InlineData("Chapter 3", "Chapter 3")]
    [InlineData("  The castle  ", "The castle")]
    public void ALabelIsTrimmed(string label, string expected) =>
        Assert.Equal(expected, BookmarkRules.Label(label));

    [Fact]
    public void EightyCharactersIsTheLongestLabel()
    {
        Assert.Equal(80, BookmarkRules.Label(new string('a', 80)).Length);
        Assert.Throws<ArgumentException>(() => BookmarkRules.Label(new string('a', 81)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyLabelIsRefused(string label) =>
        Assert.Throws<ArgumentException>(() => BookmarkRules.Label(label));

    // UTF-16 units, not characters: stricter than SQL's length(), so a label
    // C# accepts always fits the column.
    [Fact]
    public void SurrogatePairsCountAsTwo() =>
        Assert.Throws<ArgumentException>(() => BookmarkRules.Label(new string('a', 79) + "\U0001F5FA"));

    [Fact]
    public void ALocatorIsBoundedInUtf8Bytes()
    {
        string fits = new('a', 4096);
        Assert.Same(fits, BookmarkRules.Locator(fits));
        // 2,049 two-byte characters: 4,098 bytes in 2,049 characters.
        Assert.Throws<ArgumentException>(() => BookmarkRules.Locator(new string('é', 2049)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ABlankLocatorIsRefused(string locator) =>
        Assert.Throws<ArgumentException>(() => BookmarkRules.Locator(locator));

    [Fact]
    public void TheLimitNamesItsGuide()
    {
        Guid guide = Guid.NewGuid();
        Assert.Equal(guide, new BookmarkLimitException(guide).GuideId);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-1a && dotnet test tests\DesktopGuides.Core.Tests --filter "FullyQualifiedName~BookmarkRulesTests" -nologo > E:\work\t19-1a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" E:\work\t19-1a-test.log'`
Expected: FAIL to compile, with `error CS0103` or `CS0246` for `BookmarkRules`.

- [ ] **Step 3: Write the contract**

`src/DesktopGuides.Core/Library/Bookmark.cs`:

```csharp
using System.Text;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Library;

// T19.1: a named place in one guide. The locator stays text, so a bookmark
// whose locator no longer decodes is still listed, as a damaged bookmark.
public sealed record Bookmark(
    Guid Id, Guid GuideId, string LocatorJson, string Label,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

public static class BookmarkRules
{
    public const int MaxPerGuide = 500;
    public const int MaxLabelLength = 80;
    public const int MaxLocatorBytes = ReaderLocationCodec.MaxBytes;

    // Trimmed, 1 to 80 UTF-16 units: never longer than the column allows.
    public static string Label(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        string trimmed = label.Trim();
        if (trimmed.Length is 0 or > MaxLabelLength)
        {
            throw new ArgumentException(
                $"A bookmark label is 1 to {MaxLabelLength} characters.", nameof(label));
        }
        return trimmed;
    }

    public static string Locator(string locatorJson)
    {
        if (string.IsNullOrWhiteSpace(locatorJson) ||
            Encoding.UTF8.GetByteCount(locatorJson) > MaxLocatorBytes)
        {
            throw new ArgumentException("A bounded reader locator is required.", nameof(locatorJson));
        }
        return locatorJson;
    }
}

public sealed class BookmarkLimitException(Guid guideId)
    : InvalidOperationException($"This guide has {BookmarkRules.MaxPerGuide} bookmarks.")
{
    public Guid GuideId { get; } = guideId;
}
```

`src/DesktopGuides.Core/Library/IBookmarkStore.cs`:

```csharp
namespace DesktopGuides.Core.Library;

public interface IBookmarkStore
{
    // Oldest first; the reader sorts them into reading order.
    Task<IReadOnlyList<Bookmark>> ListBookmarksAsync(
        Guid guideId, CancellationToken token = default);

    // Throws BookmarkLimitException at MaxPerGuide, and
    // InvalidOperationException when the guide is gone.
    Task<Bookmark> AddBookmarkAsync(
        Guid guideId, string locatorJson, string label, CancellationToken token = default);

    // Null when the bookmark is gone.
    Task<Bookmark?> RenameBookmarkAsync(
        Guid bookmarkId, string label, CancellationToken token = default);

    // False when it was already gone.
    Task<bool> DeleteBookmarkAsync(Guid bookmarkId, CancellationToken token = default);

    // Undo: puts a deleted bookmark back with its id and times, under the
    // same limit and guide checks as an add.
    Task RestoreBookmarkAsync(Bookmark bookmark, CancellationToken token = default);
}
```

`ILibraryRepository.cs`: change the declaration to
`public interface ILibraryRepository : IReadingLocationStore, IGuideCompletionStore, IBookmarkStore, IAsyncDisposable`.

`ImporterFakes.cs`: after the `SetGuideCompletionAsync` stub, add:

```csharp
    public Task<IReadOnlyList<Bookmark>> ListBookmarksAsync(Guid guideId, CancellationToken token = default) => throw new NotSupportedException();
    public Task<Bookmark> AddBookmarkAsync(Guid guideId, string locatorJson, string label, CancellationToken token = default) => throw new NotSupportedException();
    public Task<Bookmark?> RenameBookmarkAsync(Guid bookmarkId, string label, CancellationToken token = default) => throw new NotSupportedException();
    public Task<bool> DeleteBookmarkAsync(Guid bookmarkId, CancellationToken token = default) => throw new NotSupportedException();
    public Task RestoreBookmarkAsync(Bookmark bookmark, CancellationToken token = default) => throw new NotSupportedException();
```

`SqliteLibraryRepository` doesn't compile until Task 3. To keep each task
green, add the same five `NotSupportedException` stubs to it now, in a
`// T19.1 bookmarks` block after `SetGuideCompletionAsync`. Task 3
replaces them.

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, then the whole Core suite (`--filter` removed).
Expected: `BookmarkRulesTests` passes 10/10, and Core passes in full
(1265 + 10 = 1275).

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core/Library/Bookmark.cs src/DesktopGuides.Core/Library/IBookmarkStore.cs src/DesktopGuides.Core/Library/ILibraryRepository.cs src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Core.Tests/BookmarkRulesTests.cs tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): the bookmark contract and its limits"
```

---
### Task 2: Schema version 5 and its migration

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/LibrarySchema.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `LibrarySchema.Version5` and `CurrentVersion = 5`; the
  `Bookmarks` table and `IX_Bookmarks_GuideId` that Task 3 writes and
  Task 4's backups carry. A private test helper
  `CreatePopulatedVersionFour(TestLibrary, Guid gameId, Guid guideId)`.

- [ ] **Step 1: Write the failing tests.** Beside
  `UpgradesPopulatedVersionThreeWithAnEmptyAssetTable`:

```csharp
    [Fact]
    public async Task UpgradesPopulatedVersionFourWithAnEmptyBookmarkTable()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionFour(directory, gameId, guideId);

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        Assert.Equal(5L, ReadUserVersion(directory.Paths.DatabasePath));
        Assert.NotNull(await repository.GetGuideAsync(guideId));
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT count(*) FROM Bookmarks";
        Assert.Equal(0L, (long)count.ExecuteScalar()!);
    }

    [Fact]
    public async Task FailedVersionFiveMigrationStaysAtVersionFour()
    {
        using TestLibrary directory = new();
        CreatePopulatedVersionFour(directory, Guid.NewGuid(), Guid.NewGuid());

        await using (SqliteLibraryRepository failing = new(directory.Paths, null, version =>
        {
            if (version == 5) throw new IOException("Injected after the v5 table was created.");
        }))
        {
            LibraryOpenException error = await Assert.ThrowsAsync<LibraryOpenException>(() => failing.InitializeAsync());
            Assert.Equal(LibraryOpenIssue.MigrationFailed, error.Issue);
        }

        Assert.Equal(4L, ReadUserVersion(directory.Paths.DatabasePath));
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand table = connection.CreateCommand();
        table.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'Bookmarks'";
        Assert.Equal(0L, (long)table.ExecuteScalar()!);
    }
```

  And the helper, beside `CreatePopulatedVersionThree`:

```csharp
    private static void CreatePopulatedVersionFour(TestLibrary directory, Guid gameId, Guid guideId)
    {
        CreatePopulatedVersionThree(directory, gameId, guideId);
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = LibrarySchema.Version4;
        command.ExecuteNonQuery();
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-1a && dotnet test tests\DesktopGuides.Infrastructure.Tests --filter "FullyQualifiedName~VersionFour|FullyQualifiedName~VersionFive" -nologo > E:\work\t19-1a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"[FAIL]" E:\work\t19-1a-test.log'`
Expected: both FAIL. The upgrade reads user_version 4, not 5, and the
injected checkpoint for version 5 never runs, so the open succeeds.

- [ ] **Step 3: Add the migration.** In `LibrarySchema.cs`, after
  `Version4`:

```csharp
    // T19.1: a guide's named places. Removing the guide removes them.
    public const string Version5 = """
        CREATE TABLE Bookmarks (
            Id TEXT PRIMARY KEY CHECK (length(Id) = 32),
            GuideId TEXT NOT NULL REFERENCES Guides(Id) ON DELETE CASCADE,
            LocatorJson TEXT NOT NULL CHECK (length(LocatorJson) BETWEEN 1 AND 4096),
            Label TEXT NOT NULL CHECK (length(Label) BETWEEN 1 AND 80),
            CreatedUtcMs INTEGER NOT NULL CHECK (CreatedUtcMs >= 0),
            UpdatedUtcMs INTEGER NOT NULL CHECK (UpdatedUtcMs >= 0)
        );
        CREATE INDEX IX_Bookmarks_GuideId ON Bookmarks(GuideId);
        PRAGMA user_version = 5;
        """;
```

  Set `CurrentVersion = 5`, and append `(5, Version5)` to `Migrations`.

- [ ] **Step 4: Move the tests that mean "the current version".** These
  assert the version after an upgrade or a fresh create, so they follow
  `CurrentVersion` from now on:
  - `SqliteLibraryRepositoryTests.cs` lines 112 and 596:
    `Assert.Equal(4L, (long)version.ExecuteScalar()!);` becomes
    `Assert.Equal((long)LibrarySchema.CurrentVersion, (long)version.ExecuteScalar()!);`
  - lines 1048 (`UpgradesPopulatedVersionTwoAndKeepsGames`) and 1254
    (`UpgradesPopulatedVersionThreeWithAnEmptyAssetTable`):
    `Assert.Equal(4L, ReadUserVersion(...))` becomes
    `Assert.Equal((long)LibrarySchema.CurrentVersion, ReadUserVersion(...))`
  - `Restore/LibraryRestorerStageTests.cs` line 35: the tuple's `4`
    becomes `LibrarySchema.CurrentVersion`. Add
    `using DesktopGuides.Infrastructure.Storage;` if the file lacks it.

  `FailedVersionFourMigrationStaysAtVersionThree` keeps its literals: it
  is about version 4 specifically.

- [ ] **Step 5: Run the tests to verify they pass**

Run: the Step 2 command, then the whole Infrastructure suite.
Expected: the two new tests pass, and the suite passes in full
(812 + 2 = 814).

- [ ] **Step 6: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Infrastructure/Storage/LibrarySchema.cs tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs tests/DesktopGuides.Infrastructure.Tests/Restore/LibraryRestorerStageTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): schema version 5 adds the Bookmarks table"
```

---
### Task 3: The bookmark store in `SqliteLibraryRepository`

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (replace Task 1's stubs)
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`
  (a `// T19.1 bookmarks` block, using the file's `TestLibrary`,
  `InsertGuide`, `OpenWithForeignKeys` and `AdjustableTimeProvider`)

**Interfaces:**
- Consumes: Task 1's `Bookmark`, `BookmarkRules`, `BookmarkLimitException`
  and `IBookmarkStore`; Task 2's `Bookmarks` table.
- Produces: the five `IBookmarkStore` methods, which PR b's pane calls.

- [ ] **Step 1: Write the failing tests**

```csharp
    // T19.1 bookmarks

    private static async Task<(SqliteLibraryRepository Repository, Guid Guide)> BookmarkLibraryAsync(
        TestLibrary directory, TimeProvider clock)
    {
        SqliteLibraryRepository repository = new(directory.Paths, clock);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Bookmarks", null, null);
        Guid guide = Guid.NewGuid();
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);
        return (repository, guide);
    }

    private static void InsertBookmarks(string databasePath, Guid guideId, int count)
    {
        using SqliteConnection connection = OpenWithForeignKeys(databasePath);
        using SqliteTransaction transaction = connection.BeginTransaction();
        for (int i = 0; i < count; i++)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Bookmarks (Id, GuideId, LocatorJson, Label, CreatedUtcMs, UpdatedUtcMs)
                VALUES ($id, $guide, '{}', 'Seeded', 0, 0)
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$guide", guideId.ToString("N"));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    [Fact]
    public async Task BookmarksListOldestFirstAsAdded()
    {
        using TestLibrary directory = new();
        AdjustableTimeProvider clock = new(Now);
        (SqliteLibraryRepository repository, Guid guide) = await BookmarkLibraryAsync(directory, clock);
        await using (repository)
        {
            Bookmark first = await repository.AddBookmarkAsync(guide, "{\"page\":4}", "  Boss: Dragon ");
            clock.Now = Now.AddMinutes(1);
            Bookmark second = await repository.AddBookmarkAsync(guide, "{\"page\":2}", "Page 2");

            Assert.Equal("Boss: Dragon", first.Label);
            Assert.Equal((guide, Now, Now), (first.GuideId, first.CreatedUtc, first.UpdatedUtc));
            Assert.Equal([first, second], await repository.ListBookmarksAsync(guide));
            Assert.Empty(await repository.ListBookmarksAsync(Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task ARenameKeepsTheCreationTime()
    {
        using TestLibrary directory = new();
        AdjustableTimeProvider clock = new(Now);
        (SqliteLibraryRepository repository, Guid guide) = await BookmarkLibraryAsync(directory, clock);
        await using (repository)
        {
            Bookmark added = await repository.AddBookmarkAsync(guide, "{}", "Old");
            clock.Now = Now.AddHours(1);

            Bookmark? renamed = await repository.RenameBookmarkAsync(added.Id, " New ");

            Assert.Equal(added with { Label = "New", UpdatedUtc = Now.AddHours(1) }, renamed);
            Assert.Equal([renamed!], await repository.ListBookmarksAsync(guide));
            Assert.Null(await repository.RenameBookmarkAsync(Guid.NewGuid(), "Gone"));
        }
    }

    [Fact]
    public async Task DeleteRemovesOnlyThatBookmarkAndRestorePutsItBack()
    {
        using TestLibrary directory = new();
        (SqliteLibraryRepository repository, Guid guide) = await BookmarkLibraryAsync(directory, new FixedTimeProvider(Now));
        await using (repository)
        {
            Bookmark kept = await repository.AddBookmarkAsync(guide, "{}", "Kept");
            Bookmark deleted = await repository.AddBookmarkAsync(guide, "{\"page\":9}", "Deleted");

            Assert.True(await repository.DeleteBookmarkAsync(deleted.Id));
            Assert.False(await repository.DeleteBookmarkAsync(deleted.Id));
            Assert.Equal([kept], await repository.ListBookmarksAsync(guide));

            await repository.RestoreBookmarkAsync(deleted);

            // Both share the fixed time, so the list order between them is by id.
            Assert.Equal(new[] { kept, deleted }.OrderBy(b => b.Id), (await repository.ListBookmarksAsync(guide)).OrderBy(b => b.Id));
        }
    }

    [Fact]
    public async Task TheFiveHundredAndFirstBookmarkIsRefused()
    {
        using TestLibrary directory = new();
        (SqliteLibraryRepository repository, Guid guide) = await BookmarkLibraryAsync(directory, new FixedTimeProvider(Now));
        await using (repository)
        {
            Game other = await repository.AddGameAsync("Other", null, null);
            Guid otherGuide = Guid.NewGuid();
            InsertGuide(directory.Paths.DatabasePath, otherGuide, other.Id);
            InsertBookmarks(directory.Paths.DatabasePath, guide, BookmarkRules.MaxPerGuide);

            BookmarkLimitException error = await Assert.ThrowsAsync<BookmarkLimitException>(
                () => repository.AddBookmarkAsync(guide, "{}", "One more"));

            Assert.Equal(guide, error.GuideId);
            Assert.Equal(BookmarkRules.MaxPerGuide, (await repository.ListBookmarksAsync(guide)).Count);
            Assert.NotNull(await repository.AddBookmarkAsync(otherGuide, "{}", "Another guide's"));
        }
    }

    [Fact]
    public async Task RestoreObeysTheLimit()
    {
        using TestLibrary directory = new();
        (SqliteLibraryRepository repository, Guid guide) = await BookmarkLibraryAsync(directory, new FixedTimeProvider(Now));
        await using (repository)
        {
            Bookmark deleted = await repository.AddBookmarkAsync(guide, "{}", "Deleted");
            Assert.True(await repository.DeleteBookmarkAsync(deleted.Id));
            InsertBookmarks(directory.Paths.DatabasePath, guide, BookmarkRules.MaxPerGuide);

            await Assert.ThrowsAsync<BookmarkLimitException>(() => repository.RestoreBookmarkAsync(deleted));
        }
    }

    [Fact]
    public async Task AddingToAMissingGuideSaysSo()
    {
        using TestLibrary directory = new();
        (SqliteLibraryRepository repository, _) = await BookmarkLibraryAsync(directory, new FixedTimeProvider(Now));
        await using (repository)
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddBookmarkAsync(Guid.NewGuid(), "{}", "Lost"));
            Assert.Equal("This guide is no longer in the library.", error.Message);
        }
    }

    [Fact]
    public async Task RestoringToAMissingGuideSaysSo()
    {
        using TestLibrary directory = new();
        (SqliteLibraryRepository repository, Guid guide) = await BookmarkLibraryAsync(directory, new FixedTimeProvider(Now));
        await using (repository)
        {
            Bookmark bookmark = await repository.AddBookmarkAsync(guide, "{}", "Undo me");
            Assert.True(await repository.DeleteBookmarkAsync(bookmark.Id));

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.RestoreBookmarkAsync(bookmark with { GuideId = Guid.NewGuid() }));
            Assert.Equal("This guide is no longer in the library.", error.Message);
        }
    }

    [Fact]
    public async Task BookmarksGoWithTheirGuideAndLeaveItsStateAlone()
    {
        using TestLibrary directory = new();
        (SqliteLibraryRepository repository, Guid guide) = await BookmarkLibraryAsync(directory, new FixedTimeProvider(Now));
        await using (repository)
        {
            await repository.SaveReadingLocationAsync(guide, "{\"at\":1}", 0.5);
            await repository.AddBookmarkAsync(guide, "{\"at\":2}", "Elsewhere");

            ReadingState? state = await repository.GetReadingStateAsync(guide);
            Assert.Equal(("{\"at\":1}", 0.5), (state?.LocatorJson, state?.EstimatedFraction));

            using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = $"DELETE FROM Guides WHERE Id = '{guide:N}'";
            delete.ExecuteNonQuery();

            Assert.Empty(await repository.ListBookmarksAsync(guide));
        }
    }

    [Fact]
    public async Task OutOfBoundsInputIsRefusedBeforeTheWrite()
    {
        using TestLibrary directory = new();
        (SqliteLibraryRepository repository, Guid guide) = await BookmarkLibraryAsync(directory, new FixedTimeProvider(Now));
        await using (repository)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => repository.AddBookmarkAsync(guide, new string('é', 2049), "Too long a locator"));
            await Assert.ThrowsAsync<ArgumentException>(
                () => repository.AddBookmarkAsync(guide, "{}", new string('a', 81)));
            await Assert.ThrowsAsync<ArgumentException>(
                () => repository.RenameBookmarkAsync(Guid.NewGuid(), "   "));
            Assert.Empty(await repository.ListBookmarksAsync(guide));
        }
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-1a && dotnet test tests\DesktopGuides.Infrastructure.Tests --filter "FullyQualifiedName~Bookmark|FullyQualifiedName~ARenameKeepsTheCreationTime|FullyQualifiedName~RestoreObeysTheLimit|FullyQualifiedName~MissingGuideSaysSo|FullyQualifiedName~OutOfBoundsInput" -nologo > E:\work\t19-1a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"[FAIL]" E:\work\t19-1a-test.log'`
Expected: the 9 new tests FAIL with `NotSupportedException` from Task 1's
stubs. The filter also matches Task 2's
`UpgradesPopulatedVersionFourWithAnEmptyBookmarkTable`, which still
passes.

- [ ] **Step 3: Implement the store.** Replace the stubs:

```csharp
    // T19.1 bookmarks

    private const string BookmarkColumns =
        "Id, GuideId, LocatorJson, Label, CreatedUtcMs, UpdatedUtcMs";

    public Task<IReadOnlyList<Bookmark>> ListBookmarksAsync(
        Guid guideId, CancellationToken token = default) =>
        ReadAsync<IReadOnlyList<Bookmark>>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {BookmarkColumns} FROM Bookmarks
                WHERE GuideId = $guide ORDER BY CreatedUtcMs, Id
                """;
            command.Parameters.AddWithValue("$guide", guideId.ToString("N"));
            using SqliteDataReader reader = command.ExecuteReader();
            List<Bookmark> bookmarks = [];
            while (reader.Read()) bookmarks.Add(ReadBookmark(reader));
            return bookmarks;
        }, token);

    public Task<Bookmark> AddBookmarkAsync(
        Guid guideId, string locatorJson, string label, CancellationToken token = default)
    {
        string locator = BookmarkRules.Locator(locatorJson);
        string name = BookmarkRules.Label(label);
        return WriteAsync(() =>
        {
            // Whole milliseconds, so the returned record equals the stored one.
            DateTimeOffset now = FromUnixMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
            Bookmark bookmark = new(Guid.NewGuid(), guideId, locator, name, now, now);
            InsertBookmark(bookmark);
            return bookmark;
        }, token);
    }

    public Task<Bookmark?> RenameBookmarkAsync(
        Guid bookmarkId, string label, CancellationToken token = default)
    {
        string name = BookmarkRules.Label(label);
        return WriteAsync<Bookmark?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Bookmarks SET Label = $label, UpdatedUtcMs = $now WHERE Id = $id
                """;
            update.Parameters.AddWithValue("$id", bookmarkId.ToString("N"));
            update.Parameters.AddWithValue("$label", name);
            update.Parameters.AddWithValue("$now", clock.GetUtcNow().ToUnixTimeMilliseconds());
            if (update.ExecuteNonQuery() != 1) return null;
            using SqliteCommand read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = $"SELECT {BookmarkColumns} FROM Bookmarks WHERE Id = $id";
            read.Parameters.AddWithValue("$id", bookmarkId.ToString("N"));
            Bookmark renamed;
            using (SqliteDataReader reader = read.ExecuteReader())
            {
                reader.Read();
                renamed = ReadBookmark(reader);
            }
            transaction.Commit();
            return renamed;
        }, token);
    }

    public Task<bool> DeleteBookmarkAsync(Guid bookmarkId, CancellationToken token = default) =>
        WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Bookmarks WHERE Id = $id";
            command.Parameters.AddWithValue("$id", bookmarkId.ToString("N"));
            return command.ExecuteNonQuery() == 1;
        }, token);

    public Task RestoreBookmarkAsync(Bookmark bookmark, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        if (bookmark.Id == Guid.Empty || bookmark.CreatedUtc.ToUnixTimeMilliseconds() < 0 ||
            bookmark.UpdatedUtc.ToUnixTimeMilliseconds() < 0)
        {
            throw new ArgumentException("A stored bookmark is required.", nameof(bookmark));
        }
        Bookmark restored = bookmark with
        {
            LocatorJson = BookmarkRules.Locator(bookmark.LocatorJson),
            Label = BookmarkRules.Label(bookmark.Label)
        };
        return WriteAsync(() => InsertBookmark(restored), token);
    }

    // Counts and inserts in one transaction, under the write gate.
    private void InsertBookmark(Bookmark bookmark)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using (SqliteCommand count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = """
                SELECT (SELECT COUNT(*) FROM Guides WHERE Id = $guide),
                       (SELECT COUNT(*) FROM Bookmarks WHERE GuideId = $guide)
                """;
            count.Parameters.AddWithValue("$guide", bookmark.GuideId.ToString("N"));
            using SqliteDataReader reader = count.ExecuteReader();
            reader.Read();
            if (reader.GetInt64(0) == 0)
            {
                throw new InvalidOperationException("This guide is no longer in the library.");
            }
            if (reader.GetInt64(1) >= BookmarkRules.MaxPerGuide)
            {
                throw new BookmarkLimitException(bookmark.GuideId);
            }
        }
        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            INSERT INTO Bookmarks ({BookmarkColumns})
            VALUES ($id, $guide, $locator, $label, $created, $updated)
            """;
        insert.Parameters.AddWithValue("$id", bookmark.Id.ToString("N"));
        insert.Parameters.AddWithValue("$guide", bookmark.GuideId.ToString("N"));
        insert.Parameters.AddWithValue("$locator", bookmark.LocatorJson);
        insert.Parameters.AddWithValue("$label", bookmark.Label);
        insert.Parameters.AddWithValue("$created", bookmark.CreatedUtc.ToUnixTimeMilliseconds());
        insert.Parameters.AddWithValue("$updated", bookmark.UpdatedUtc.ToUnixTimeMilliseconds());
        insert.ExecuteNonQuery();
        transaction.Commit();
    }

    private static Bookmark ReadBookmark(SqliteDataReader reader) => new(
        Guid.ParseExact(reader.GetString(0), "N"),
        Guid.ParseExact(reader.GetString(1), "N"),
        reader.GetString(2),
        reader.GetString(3),
        FromUnixMilliseconds(reader.GetInt64(4)),
        FromUnixMilliseconds(reader.GetInt64(5)));
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, then the whole Infrastructure suite.
Expected: the filter's 10 tests pass, and the suite passes in full
(814 + 9 = 823).

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): store, rename, delete and restore bookmarks"
```

---
### Task 4: Backup and restore coverage

**Files:**
- Modify: `tests/DesktopGuides.Infrastructure.Tests/ExportFixture.cs` (one bookmark)
- Modify: `tests/DesktopGuides.Infrastructure.Tests/Restore/RestoreFixture.cs`
  (`RewriteDatabase` takes an optional manifest edit)
- Test: `tests/DesktopGuides.Infrastructure.Tests/LibraryExporterTests.cs`,
  `Restore/LibraryRestorerValidationTests.cs`,
  `Restore/LibraryRestorerSwapTests.cs`

**Interfaces:**
- Consumes: Task 3's `AddBookmarkAsync`; Task 2's schema.
- Produces: no production code. These tests pin the design's claim that
  export, restore validation and migration already cover the table.

These are mostly characterisation tests: the behaviour comes from Tasks 2
and 3 and the existing export and restore code. Only the snapshot test
fails first (the fixture has no bookmark yet). The two refusal tests and
the migration test are expected to pass when written; if one fails, the
design's claim is wrong. Stop and ledger a ruling, and don't weaken the
test.

- [ ] **Step 1: Write the snapshot test, and watch it fail.** In
  `LibraryExporterTests.cs`, after `TheSnapshotHoldsTheLibraryRows`:

```csharp
    [Fact]
    public async Task TheSnapshotHoldsTheBookmarks()
    {
        string zip = (await Export()).Path;

        Assert.Equal("Boss fight|{\"format\":\"Txt\"}", Scalar(zip,
            $"SELECT Label || '|' || LocatorJson FROM Bookmarks WHERE GuideId = '{fixture.TxtGuide:N}'"));
    }
```

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-1a && dotnet test tests\DesktopGuides.Infrastructure.Tests --filter "FullyQualifiedName~TheSnapshotHoldsTheBookmarks" -nologo > E:\work\t19-1a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"[FAIL]" E:\work\t19-1a-test.log'`
Expected: FAIL. The query finds no row, because the fixture has no
bookmark yet.

- [ ] **Step 2: Give the export fixture a bookmark.** In
  `ExportFixture.PopulateAsync`, after the `Library.Execute(...)` block:

```csharp
        await Library.Repository.AddBookmarkAsync(TxtGuide, "{\"format\":\"Txt\"}", "Boss fight");
```

Run: the Step 1 command. Expected: PASS.

- [ ] **Step 3: Let `RewriteDatabase` edit the manifest.** In
  `RestoreFixture.cs`, replace the method's signature and its call to
  `Rewrite`:

```csharp
    /// <summary>A self-consistent backup whose database was changed by <paramref name="sql"/>.</summary>
    public string RewriteDatabase(
        string sql, Func<LibraryArchiveManifest, LibraryArchiveManifest>? editManifest = null) => Rewrite(files =>
    {
        // (body unchanged)
    }, editManifest: editManifest);
```

- [ ] **Step 4: Add the refusal tests.** In
  `LibraryRestorerValidationTests.cs`, beside
  `ADanglingForeignKeyIsInvalid`:

```csharp
    [Fact]
    public async Task ADanglingBookmarkIsInvalid() =>
        await Refused(fixture.RewriteDatabase(
                $"PRAGMA foreign_keys = OFF; INSERT INTO Bookmarks (Id, GuideId, LocatorJson, Label, CreatedUtcMs, UpdatedUtcMs) VALUES ('{Guid.NewGuid():N}', '{Guid.NewGuid():N}', '{{}}', 'Lost', 0, 0);"),
            LibraryRestoreIssue.DatabaseInvalid);

    // A hand-edited backup can bypass the CHECK; integrity_check reports it.
    [Fact]
    public async Task AnOversizedBookmarkIsInvalid() =>
        await Refused(fixture.RewriteDatabase(
                "PRAGMA ignore_check_constraints = ON; UPDATE Bookmarks SET Label = printf('%.90c', 'x');"),
            LibraryRestoreIssue.DatabaseInvalid);
```

- [ ] **Step 5: Add the version 4 backup test.** In
  `LibraryRestorerSwapTests.cs` (add `using DesktopGuides.Core.Backup;`
  and `using DesktopGuides.Infrastructure.Storage;` if missing):

```csharp
    // A backup from before P2 restores as it is, and the next open migrates it.
    [Fact]
    public async Task AVersionFourBackupRestoresAndMigrates()
    {
        string backup = fixture.RewriteDatabase(
            "DROP TABLE Bookmarks; PRAGMA user_version = 4;",
            manifest => manifest with { SchemaVersion = 4 });
        LibraryRestoreStage stage = await fixture.StageAsync(backup);

        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        await fixture.Target.RestartAsync();

        Assert.Equal(LibrarySchema.CurrentVersion.ToString(), fixture.Target.Scalar("PRAGMA user_version"));
        Assert.Equal("0", fixture.Target.Scalar("SELECT COUNT(*) FROM Bookmarks"));
        Assert.Equal("3", fixture.Target.Scalar("SELECT COUNT(*) FROM Guides"));
    }
```

  Dropping the table also drops its index, so the database matches
  version 4's schema exactly.

  **If `RestartAsync` refuses the library** because the restore marker
  still says `Swapping`, confirm the swap first, the way startup does
  (`LibraryRestoreRecovery`), and ledger the ruling.

- [ ] **Step 6: Run the new tests, then the suite**

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-1a && dotnet test tests\DesktopGuides.Infrastructure.Tests --filter "FullyQualifiedName~TheSnapshotHoldsTheBookmarks|FullyQualifiedName~DanglingBookmark|FullyQualifiedName~OversizedBookmark|FullyQualifiedName~AVersionFourBackup" -nologo > E:\work\t19-1a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"[FAIL]" E:\work\t19-1a-test.log'`, then the whole Infrastructure suite.
Expected: the 4 tests pass, and the suite passes in full (823 + 4 = 827).
The fixture's new bookmark mustn't change any other test. If one counts
rows that now include it, ledger the change.

- [ ] **Step 7: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tests/DesktopGuides.Infrastructure.Tests/ExportFixture.cs tests/DesktopGuides.Infrastructure.Tests/LibraryExporterTests.cs tests/DesktopGuides.Infrastructure.Tests/Restore
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(p2): backups carry bookmarks, and a pre-P2 backup migrates"
```

---
### Task 5: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t19-1-bookmarks-design.md` (Status; a "PR a
  verification record")
- Modify: `docs/p2/t18-2-contents-design.md` (Status: PR b merged as #119)
- Modify: `docs/p2/results.md` (the #119 row)
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md`

- [ ] **Step 1: The production build and the full suites.** Run the
  Production build (expected: 0 warnings, 0 errors), and Core (1275) and
  Infrastructure (827) on the host.
- [ ] **Step 2: The final review.** Run the whole-branch review first.
  Its fixes land before the records.
- [ ] **Step 3: The records.**
  - **T18.2's Status:** "Approved in conversation, 15 October 2026. PR a
    merged as #118 and PR b as #119; T18.2 and M8 are complete."
  - **T19.1's Status:** "Approved in conversation, 16 October 2026. PR a
    (storage and backup) implemented on `feat/p2-t19-1-bookmarks`." The
    "PR a verification record" lists the tests per task, the suite
    counts, the PR run, the review, and every ruling.
  - **`results.md`:** #119's row: 2026-10-16, `ff10219`, T18.2 (PR b),
    run 38766036799, Core 1265, Infra 808, linking the T18.2 design's PR b
    verification record. The note: "HTML and PDF contents; T18.2
    complete. One re-run, of four jobs outside this change: the toolbar
    install fixture, the arm64 window, the system Open dialog (#122) and
    a 'Loading guide…' timeout (#102)."
  - **The status pages:** "M8 complete (T18.2: #118 and #119). M9 in
    progress: T19.1 PR a (bookmark storage and backup) in review."
- [ ] **Step 4: Commit, push, open the PR, and wait for its run.**
  - **Commit:** "docs(p2): record T19.1 PR a, and T18.2 complete".
  - **The PR:** "T19.1 PR a: bookmark storage and backup", based on
    `main`. It names the task, the prerequisite (T18.2, merged with #119)
    and the outcome (stored bookmarks with no UI yet), plus the run,
    every ruling and the deferred minors. No UI change, so no
    screenshot.
  - **Expected:** every job passes. Record the run in the PR, then stop
    for the user's merge decision.

## Traceability

| Design item | Task |
| --- | --- |
| The `Bookmarks` table, schema version 5, one forward migration | 2 |
| The cascade with the guide; no effect on reading state | 2, 3 |
| Exact byte and label limits in C# | 1, 3 |
| List, add, rename, delete, restore (undo) under the write lock | 1, 3 |
| At most 500 per guide, counted in the add's transaction | 3 |
| Export carries the table with no change | 4 |
| Restore refuses a dangling or oversized row | 4 |
| A version 4 backup restores, then migrates with no bookmarks | 4 |
| Tests that hard-code version 4 move to the current version | 2 |
