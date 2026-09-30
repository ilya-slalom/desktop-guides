# T15.3 Guide Deletion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Someone can remove a guide from its game after a confirmation that
names it and its managed-file count. Removal deletes the guide's metadata,
reading state, reader preferences and owned files. Every failure leaves the
whole guide, or no guide plus one journaled trash entry that startup finishes.

**Architecture:** Core gains the removal contracts and their copy
(`GuideRemovalPresentation`). `SqliteLibraryRepository.RunDeletionAsync`
hands an internal `IDeletionJournal` to its caller while holding the write
gate. `FileOperationReconciler` gains `RollBackDeletion` and
`FinishDeletion`, so an in-process rollback and a startup rollback follow the
same rules. `GuideRemover` captures `content/<id>` with `OwnedGuideTree`,
journals a `DeleteGuide` operation, moves the tree to `.trash/<op>/<id>`,
commits the row deletion, then deletes the trash. The Game page gains a
**Remove guide** header button and a `RemoveGuideDialog`.

**Tech Stack:** .NET 10, WinUI 3, Microsoft.Data.Sqlite, xUnit, the
PowerShell 5.1 UI Automation harness.

**Spec:** `docs/p1/t15-3-guide-deletion-design.md`

**Target:** T15.3. **Prerequisites:** T06.3 (PR #19, merged as `494cb02`)
and T15.2 (PR #5, merged).

## Global Constraints

- No schema change. The schema stays at version 3. `ReadingStates` and
  `ReaderPreferences` go through the existing `ON DELETE CASCADE`.
- The journal row is `Kind = 'DeleteGuide'`, written with
  `FileOperationManifest.Create(FileOperationKind.DeleteGuide, operationId, [guideId])`.
- Only `content/<id>` and `.trash/<op>/<id>` (plus an empty `.trash/<op>`)
  are ever moved or deleted. The original imported file is never touched.
- `CleanupPending` is true only for `Removed`.
- Cancellation stops only the wait for the write gate. Once `Prepare` runs,
  removal runs to an outcome.
- Messages, verbatim, with a straight ASCII apostrophe:
  - Dialog title: `Remove {title}?`
  - Dialog body: `This removes the guide, its reading progress, and its {N} managed files from Desktop Guides. The original file you imported isn't affected.`
    with `1 managed file` when N is 1.
  - Removed: `Removed {title}.`
  - Removed, cleanup pending: `Removed {title}. Leftover files will be cleaned up the next time Desktop Guides starts.`
  - NotFound: `{title} was already removed.`
  - Unsafe: `{title} can't be removed because its files were changed outside Desktop Guides.`
  - Failed: `{title} couldn't be removed. The guide is unchanged. Try again.`
  - RestoreFailed: `{title} couldn't be removed. Restart Desktop Guides to finish restoring it.`
- Dialog buttons: Primary **Remove**, Close **Cancel**, and
  `DefaultButton = Close`. No custom danger style.
- Automation IDs: `RemoveSelectedGuide` (name `Remove {title}`),
  `RemoveGuideDialog` and `RemoveGuideMessage`.
- UI tests assert only what app code controls.
- PowerShell scripts stay ASCII-only.
- Never print, copy or log provider credential values.

## Rulings against the spec

Task 6 records these rulings in the design's verification record.

1. **The header button follows the Open button's visibility rule.**
   Selecting a guide opens it, so the smoke opens the guide, goes back to the
   game, and then invokes **Remove guide** on the restored selection. The
   user chose to keep the header button as specified.
2. **`Commit` also clears a matching `LastActiveGuideId`** in the same
   transaction. The Library home already ignores a missing guide, so this
   only keeps the setting from pointing at a deleted row.
3. **The failure tests hold files open with `FileShare.Read`.** On NTFS this
   blocks both renaming the parent directory and deleting the file. The first
   red run on `pcsx2-win` confirms that. If a rename isn't blocked, the
   RestoreFailed test falls back to creating a file at `content/<id>` inside
   the checkpoint, so rollback's capture throws.
4. **A failing `Prepare` throws `Failed` without calling `RollBack`,** because
   no row or file has changed.
5. **Capture errors map by type:** `InvalidDataException` (from
   `OwnedGuideTree`) is `Unsafe`, and `IOException` or
   `UnauthorizedAccessException` is `Failed`.
6. **The `Moved` checkpoint fires even when `content/<id>` was missing,** so
   the fault Theory covers the same three points for every guide.
7. **The `Committed` checkpoint and `Finish` share one try.** A fault at
   `Committed` is a cleanup fault, and returns `CleanupPending`.
8. **The messages live in Core's `GuideRemovalPresentation`,** so they're
   unit-tested. Production has no test project, so Task 4 is gated by the
   Production build and by the Task 5 installed smoke. This is the plan's
   only TDD skip for code.
9. **The smoke reads the remaining row's name** instead of hard-coding it.
10. **The smoke removes the Imported guide, which is the last row.** "Copied
    Guide…" sorts before "Imported Guide…", so this exercises the
    previous-row rule. The spec's smoke table says "the first guide".
11. **The fault Theory takes checkpoint names as strings,** because
    `RemovalCheckpoint` is internal and a public Theory can't take it as a
    parameter (CS0051).
12. **`RollBackDeletion` and `FinishDeletion` loop over the manifest's
    guides,** so T04.3's `DeleteGame` can reuse them unchanged.

## Review Focus

1. **Another program holds a content file open** (an editor, an antivirus
   scan). The move fails, and removal must report `Failed` with the guide
   intact. Test `ContentHeldOpenFailsAndKeepsTheGuide` (Task 3).
2. **The removed guide is the Library's Resume target.** The setting must
   not keep pointing at it. Test `CommitClearsAMatchingLastActiveGuide`
   (Task 2).
3. **The guide is removed between the dialog opening and Remove** (a second
   window). Remove must return `NotFound` and the list must reload. Test
   `RemoveOfAnUnknownGuideReturnsNotFound` (Task 3). The shell's NotFound
   branch is checked in code review (Task 4).
4. **The window closes while the dialog is open or removal is running.** The
   dialog must hide and the navigation queue must drain without touching
   disposed UI. Checked in code review against the close handler (Task 4).
5. **The last guide in a game is removed.** The empty state must show and
   **Import guide** must take focus. Checked in code review (Task 4).

## Host commands

The Mac has no dotnet, so every build and test runs on `pcsx2-win`:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t15-3) { Remove-Item -Recurse -Force E:\work\desktop-guides\t15-3 }; New-Item -ItemType Directory E:\work\desktop-guides\t15-3 | Out-Null"'
  COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t15-3'
}
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t15-3 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
- **Infrastructure tests:** `stage && s 'cd /d E:\work\desktop-guides\t15-3 && dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release'`
- **One class:** append `--filter "FullyQualifiedName~<Class>"` inside the
  quoted command.
- **Production build:** `stage && s 'cd /d E:\work\desktop-guides\t15-3 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'`
- **Seed build:** `stage && s 'cd /d E:\work\desktop-guides\t15-3 && dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release'`

---

### Task 1: Core removal contracts and copy

**Files:**
- Create: `src/DesktopGuides.Core/Library/GuideRemovalContracts.cs`
- Create: `src/DesktopGuides.Core/Library/GuideRemovalPresentation.cs`
- Test: `tests/DesktopGuides.Core.Tests/GuideRemovalPresentationTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces, in namespace `DesktopGuides.Core.Library`:
  - `sealed record GuideRemovalPreview(Guid GuideId, Guid GameId, string Title, int FileCount)`
  - `enum GuideRemovalOutcome { Removed, NotFound }`
  - `sealed record GuideRemovalResult(GuideRemovalOutcome Outcome, bool CleanupPending)`
  - `enum GuideRemovalIssue { Unsafe, Failed, RestoreFailed }`
  - `sealed class GuideRemovalException(GuideRemovalIssue issue, Exception? inner = null)`
    with `GuideRemovalIssue Issue`
  - `static class GuideRemovalPresentation` with `DialogTitle(string)`,
    `DialogBody(int)`, `Removed(string, bool)`, `AlreadyRemoved(string)` and
    `Error(GuideRemovalIssue, string)`

- [ ] **Step 1: Write the failing tests**

`tests/DesktopGuides.Core.Tests/GuideRemovalPresentationTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideRemovalPresentationTests
{
    [Fact]
    public void DialogTitleNamesTheGuide() =>
        Assert.Equal("Remove Walkthrough?", GuideRemovalPresentation.DialogTitle("Walkthrough"));

    [Theory]
    [InlineData(0, "0 managed files")]
    [InlineData(1, "1 managed file")]
    [InlineData(3, "3 managed files")]
    public void DialogBodyCountsManagedFiles(int count, string files) =>
        Assert.Equal(
            $"This removes the guide, its reading progress, and its {files} from Desktop Guides. The original file you imported isn't affected.",
            GuideRemovalPresentation.DialogBody(count));

    [Theory]
    [InlineData(false, "Removed Walkthrough.")]
    [InlineData(true, "Removed Walkthrough. Leftover files will be cleaned up the next time Desktop Guides starts.")]
    public void RemovedMentionsPendingCleanup(bool cleanupPending, string expected) =>
        Assert.Equal(expected, GuideRemovalPresentation.Removed("Walkthrough", cleanupPending));

    [Fact]
    public void AlreadyRemovedNamesTheGuide() =>
        Assert.Equal("Walkthrough was already removed.", GuideRemovalPresentation.AlreadyRemoved("Walkthrough"));

    [Theory]
    [InlineData(GuideRemovalIssue.Unsafe, "Walkthrough can't be removed because its files were changed outside Desktop Guides.")]
    [InlineData(GuideRemovalIssue.Failed, "Walkthrough couldn't be removed. The guide is unchanged. Try again.")]
    [InlineData(GuideRemovalIssue.RestoreFailed, "Walkthrough couldn't be removed. Restart Desktop Guides to finish restoring it.")]
    public void ErrorDescribesEachIssue(GuideRemovalIssue issue, string expected) =>
        Assert.Equal(expected, GuideRemovalPresentation.Error(issue, "Walkthrough"));

    [Fact]
    public void ErrorRejectsAnUnknownIssue() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GuideRemovalPresentation.Error((GuideRemovalIssue)99, "Walkthrough"));

    [Fact]
    public void ExceptionCarriesItsIssueAndCause()
    {
        IOException cause = new("locked");

        GuideRemovalException error = new(GuideRemovalIssue.Failed, cause);

        Assert.Equal(GuideRemovalIssue.Failed, error.Issue);
        Assert.Same(cause, error.InnerException);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: Core tests with `--filter "FullyQualifiedName~GuideRemovalPresentationTests"`.
Expected: build fails with CS0246/CS0103 for `GuideRemovalPresentation`,
`GuideRemovalIssue` and `GuideRemovalException`.

- [ ] **Step 3: Implement**

`src/DesktopGuides.Core/Library/GuideRemovalContracts.cs`:

```csharp
namespace DesktopGuides.Core.Library;

/// <summary>What the confirmation names: the guide and its managed-file count.</summary>
public sealed record GuideRemovalPreview(Guid GuideId, Guid GameId, string Title, int FileCount);

public enum GuideRemovalOutcome { Removed, NotFound }

/// <summary>CleanupPending is true only for Removed: startup deletes the leftover trash.</summary>
public sealed record GuideRemovalResult(GuideRemovalOutcome Outcome, bool CleanupPending);

public enum GuideRemovalIssue { Unsafe, Failed, RestoreFailed }

public sealed class GuideRemovalException(GuideRemovalIssue issue, Exception? inner = null)
    : Exception(null, inner)
{
    public GuideRemovalIssue Issue { get; } = issue;
}
```

`src/DesktopGuides.Core/Library/GuideRemovalPresentation.cs`:

```csharp
namespace DesktopGuides.Core.Library;

/// <summary>The removal confirmation and status copy.</summary>
public static class GuideRemovalPresentation
{
    public static string DialogTitle(string title) => $"Remove {title}?";

    public static string DialogBody(int fileCount)
    {
        string files = fileCount == 1 ? "1 managed file" : $"{fileCount} managed files";
        return $"This removes the guide, its reading progress, and its {files} from Desktop Guides. " +
            "The original file you imported isn't affected.";
    }

    public static string Removed(string title, bool cleanupPending) => cleanupPending
        ? $"Removed {title}. Leftover files will be cleaned up the next time Desktop Guides starts."
        : $"Removed {title}.";

    public static string AlreadyRemoved(string title) => $"{title} was already removed.";

    public static string Error(GuideRemovalIssue issue, string title) => issue switch
    {
        GuideRemovalIssue.Unsafe => $"{title} can't be removed because its files were changed outside Desktop Guides.",
        GuideRemovalIssue.Failed => $"{title} couldn't be removed. The guide is unchanged. Try again.",
        GuideRemovalIssue.RestoreFailed => $"{title} couldn't be removed. Restart Desktop Guides to finish restoring it.",
        _ => throw new ArgumentOutOfRangeException(nameof(issue)),
    };
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: Core tests (whole project).
Expected: all pass, including the 12 new cases.

- [ ] **Step 5: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Core/Library/GuideRemovalContracts.cs src/DesktopGuides.Core/Library/GuideRemovalPresentation.cs tests/DesktopGuides.Core.Tests/GuideRemovalPresentationTests.cs
git commit -m "feat(core): add guide removal contracts and copy" -m "Add the removal preview, result, issue and exception types, and the confirmation and status messages for T15.3." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Deletion journal

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Storage/DeletionJournal.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (extract `GetGuide(connection, id)` from `GetGuideAsync` at line 254; add
  `RunDeletionAsync` and a nested `DeletionJournal` after `ImportJournal`,
  around line 820)
- Modify: `src/DesktopGuides.Infrastructure/Storage/FileOperationReconciler.cs`
  (add `RollBackDeletion`, `FinishDeletion` and `ReadDeletion` after
  `RollBackImport`)
- Create: `tests/DesktopGuides.Infrastructure.Tests/RemovalLibrary.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/DeletionJournalTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces:
  - `internal interface IDeletionJournal` with `Guide? GetGuide(Guid)`,
    `void Prepare(Guid operationId, Guid guideId)`,
    `void Commit(Guid operationId, Guid guideId, Action beforeCommit)`,
    `void RollBack(Guid operationId)` and `void Finish(Guid operationId)`.
  - `internal Task<T> SqliteLibraryRepository.RunDeletionAsync<T>(Func<IDeletionJournal, T> work, CancellationToken token)`.
  - `public void FileOperationReconciler.RollBackDeletion(SqliteConnection, Guid)`
    and `FinishDeletion(SqliteConnection, Guid)`.
  - Test harness `RemovalLibrary` (used again by Task 3):
    `static Task<RemovalLibrary> CreateAsync()`, `string Root`,
    `ManagedPathResolver Paths`, `SqliteLibraryRepository Repository`,
    `Task<Guid> AddGameAsync(string title)`,
    `Task<Guid> AddGuideAsync(Guid gameId, string title)`,
    `Task RunDeletion(Action<IDeletionJournal> work)`,
    `static void WriteFile(string directory, string relative, string text)`,
    `static void CreateJunction(string link, string target)`,
    `string Scalar(string sql)` and `string RowsFor(Guid guideId)`, which
    returns the `Guides|ReadingStates|ReaderPreferences` counts, such as `1|1|1`.

- [ ] **Step 1: Write the test harness**

`tests/DesktopGuides.Infrastructure.Tests/RemovalLibrary.cs`:

```csharp
using System.Diagnostics;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

/// <summary>A real library on disk for the deletion tests.</summary>
internal sealed class RemovalLibrary : IAsyncDisposable
{
    private RemovalLibrary(string root)
    {
        Root = root;
        Paths = new ManagedPathResolver(Path.Combine(root, "app-data"));
        Repository = new SqliteLibraryRepository(Paths);
    }

    public string Root { get; }
    public ManagedPathResolver Paths { get; }
    public SqliteLibraryRepository Repository { get; private set; }

    public static async Task<RemovalLibrary> CreateAsync()
    {
        RemovalLibrary library = new(Path.Combine(
            Path.GetTempPath(), "desktop-guides-removal-" + Guid.NewGuid().ToString("N")));
        await library.Repository.InitializeAsync();
        return library;
    }

    /// <summary>Opens a fresh repository on the same files, as the next app start does.</summary>
    public async Task RestartAsync()
    {
        await Repository.DisposeAsync();
        Repository = new SqliteLibraryRepository(Paths);
        await Repository.InitializeAsync();
    }

    public async Task<Guid> AddGameAsync(string title) =>
        (await Repository.AddGameAsync(title, null, null)).Id;

    public async Task<Guid> AddGuideAsync(Guid gameId, string title)
    {
        Guid operation = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        await Repository.RunImportAsync<bool>((journal, _) =>
        {
            journal.Prepare(operation, id);
            journal.Publish(
                new NewImportedGuide(operation, id, gameId, title, GuideFormat.Txt, "guide.txt",
                    new string('a', 64), 20, null, null),
                () => { });
            return Task.FromResult(true);
        }, CancellationToken.None);
        return id;
    }

    public Task RunDeletion(Action<IDeletionJournal> work) =>
        Repository.RunDeletionAsync(journal =>
        {
            work(journal);
            return true;
        }, CancellationToken.None);

    public static void WriteFile(string directory, string relative, string text)
    {
        string path = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public static void CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(target);
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    public string Scalar(string sql)
    {
        using SqliteConnection connection = new($"Data Source={Paths.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    public string RowsFor(Guid guideId)
    {
        string id = guideId.ToString("N");
        return Scalar($"""
            SELECT (SELECT COUNT(*) FROM Guides WHERE Id = '{id}') || '|' ||
                   (SELECT COUNT(*) FROM ReadingStates WHERE GuideId = '{id}') || '|' ||
                   (SELECT COUNT(*) FROM ReaderPreferences WHERE GuideId = '{id}')
            """);
    }

    public async ValueTask DisposeAsync()
    {
        await Repository.DisposeAsync();
        // Tests that create a junction delete it in a finally block first.
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/DesktopGuides.Infrastructure.Tests/DeletionJournalTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class DeletionJournalTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;
    private Guid gameId;
    private Guid guideId;
    private Guid otherId;
    private readonly Guid operationId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        library = await RemovalLibrary.CreateAsync();
        gameId = await library.AddGameAsync("Journal Game");
        guideId = await library.AddGuideAsync(gameId, "Removed");
        otherId = await library.AddGuideAsync(gameId, "Kept");
    }

    public async Task DisposeAsync() => await library.DisposeAsync();

    private string Content(Guid id) => library.Paths.GetGuideRoot(id);
    private string Trash(Guid id) => library.Paths.GetTrashedGuideRoot(operationId, id);
    private string OperationCount() => library.Scalar("SELECT COUNT(*) FROM FileOperations");

    [Fact]
    public async Task GetGuideReadsTheRowOrNull()
    {
        Guide? found = null;
        Guide? missing = null;

        await library.RunDeletion(journal =>
        {
            found = journal.GetGuide(guideId);
            missing = journal.GetGuide(Guid.NewGuid());
        });

        Assert.Equal("Removed", found?.Title);
        Assert.Null(missing);
    }

    [Fact]
    public async Task PrepareRecordsAPreparedDeletion()
    {
        await library.RunDeletion(journal => journal.Prepare(operationId, guideId));

        Assert.Equal("DeleteGuide|Prepared", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
    }

    [Fact]
    public async Task CommitRemovesTheGuideAndItsStateAndMarksTheOperationCommitted()
    {
        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Commit(operationId, guideId, () => { });
        });

        Assert.Equal("0|0|0", library.RowsFor(guideId));
        Assert.Equal("1|1|1", library.RowsFor(otherId));
        Assert.Equal("Committed", library.Scalar("SELECT Phase FROM FileOperations"));
    }

    [Fact]
    public async Task CommitFailureBeforeCommitKeepsEverything()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Commit(operationId, guideId, () => throw new InvalidOperationException());
        }));

        Assert.Equal("1|1|1", library.RowsFor(guideId));
        Assert.Equal("Prepared", library.Scalar("SELECT Phase FROM FileOperations"));
    }

    [Fact]
    public async Task CommitWithoutAPreparedOperationKeepsTheGuide()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => library.RunDeletion(
            journal => journal.Commit(operationId, guideId, () => { })));

        Assert.Equal("1|1|1", library.RowsFor(guideId));
    }

    [Fact]
    public async Task CommitOfAMissingGuideKeepsThePreparedOperation()
    {
        Guid unknown = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidDataException>(() => library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, unknown);
            journal.Commit(operationId, unknown, () => { });
        }));

        Assert.Equal("Prepared", library.Scalar("SELECT Phase FROM FileOperations"));
    }

    [Fact]
    public async Task CommitClearsAMatchingLastActiveGuide()
    {
        await library.Repository.UpdateSettingsAsync(s => s with { LastActiveGuideId = guideId });

        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Commit(operationId, guideId, () => { });
        });

        Assert.Null((await library.Repository.GetSettingsAsync()).LastActiveGuideId);
    }

    [Fact]
    public async Task CommitKeepsAnotherGuidesLastActiveSetting()
    {
        await library.Repository.UpdateSettingsAsync(s => s with { LastActiveGuideId = otherId });

        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Commit(operationId, guideId, () => { });
        });

        Assert.Equal(otherId, (await library.Repository.GetSettingsAsync()).LastActiveGuideId);
    }

    [Fact]
    public async Task RollBackMovesTheTrashBackAndRemovesTheOperation()
    {
        RemovalLibrary.WriteFile(Content(guideId), "guide.txt", "bytes");

        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            Directory.CreateDirectory(Path.GetDirectoryName(Trash(guideId))!);
            Directory.Move(Content(guideId), Trash(guideId));
            journal.RollBack(operationId);
        });

        Assert.Equal("bytes", File.ReadAllText(Path.Combine(Content(guideId), "guide.txt")));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Trash(guideId))!));
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task RollBackWithoutTrashOnlyRemovesTheOperation()
    {
        RemovalLibrary.WriteFile(Content(guideId), "guide.txt", "bytes");

        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.RollBack(operationId);
        });

        Assert.True(File.Exists(Path.Combine(Content(guideId), "guide.txt")));
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task RollBackRefusesACommittedDeletion()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Commit(operationId, guideId, () => { });
            journal.RollBack(operationId);
        }));

        Assert.Equal("Committed", library.Scalar("SELECT Phase FROM FileOperations"));
    }

    [Fact]
    public async Task FinishDeletesTheTrashAndRemovesTheOperation()
    {
        RemovalLibrary.WriteFile(Content(guideId), "nested/guide.txt", "bytes");
        RemovalLibrary.WriteFile(Content(otherId), "guide.txt", "kept");

        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            Directory.CreateDirectory(Path.GetDirectoryName(Trash(guideId))!);
            Directory.Move(Content(guideId), Trash(guideId));
            journal.Commit(operationId, guideId, () => { });
            journal.Finish(operationId);
        });

        Assert.False(Directory.Exists(Path.GetDirectoryName(Trash(guideId))!));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(Content(otherId), "guide.txt")));
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task FinishRefusesAPreparedDeletion()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Finish(operationId);
        }));

        Assert.Equal("1|1|1", library.RowsFor(guideId));
        Assert.Equal("Prepared", library.Scalar("SELECT Phase FROM FileOperations"));
    }

    [Fact]
    public async Task RunDeletionHoldsTheWriteGate()
    {
        using SemaphoreSlim entered = new(0);
        using SemaphoreSlim release = new(0);
        Task deletion = library.Repository.RunDeletionAsync(_ =>
        {
            entered.Release();
            release.Wait();
            return true;
        }, CancellationToken.None);
        await entered.WaitAsync();

        Task<Game> edit = library.Repository.AddGameAsync("Blocked", null, null);
        await Task.Delay(200);
        Assert.False(edit.IsCompleted);

        release.Release();
        await deletion;
        await edit;
    }

    [Fact]
    public async Task RunDeletionWithACancelledTokenDoesNotRunTheWork()
    {
        bool ran = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.Repository.RunDeletionAsync(
            _ => ran = true, new CancellationToken(canceled: true)));

        Assert.False(ran);
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~DeletionJournalTests"`.
Expected: build fails with CS0246 for `IDeletionJournal` and CS1061 for
`RunDeletionAsync`.

- [ ] **Step 4: Implement the journal interface**

`src/DesktopGuides.Infrastructure/Storage/DeletionJournal.cs`:

```csharp
using DesktopGuides.Core.Library;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>SQL for one guide deletion. Callers already hold the repository's write gate.</summary>
internal interface IDeletionJournal
{
    Guide? GetGuide(Guid guideId);

    /// <summary>Commits a Prepared DeleteGuide row owning content/&lt;guide&gt; and .trash/&lt;op&gt;/&lt;guide&gt;.</summary>
    void Prepare(Guid operationId, Guid guideId);

    /// <summary>Deletes the Guide row (cascading its state rows) and marks the operation Committed, in one transaction.</summary>
    void Commit(Guid operationId, Guid guideId, Action beforeCommit);

    /// <summary>Moves a Prepared deletion's trash back to content, then removes its row.</summary>
    void RollBack(Guid operationId);

    /// <summary>Deletes a Committed deletion's trash, then removes its row.</summary>
    void Finish(Guid operationId);
}
```

- [ ] **Step 5: Extract `GetGuide` in the repository**

Replace `GetGuideAsync` (line 254) with:

```csharp
    public Task<Guide?> GetGuideAsync(Guid guideId, CancellationToken token = default) =>
        ReadAsync<Guide?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            return GetGuide(connection, guideId);
        }, token);

    private static Guide? GetGuide(SqliteConnection connection, Guid guideId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, GameId, Title, Format, ManagedRelativeRoot,
                   PrimaryRelativePath, ContentSha256, ContentBytes,
                   SourceLabel, TextCodePage, ImportedUtcMs, UpdatedUtcMs
            FROM Guides WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$id", guideId.ToString("N"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadGuide(reader) : null;
    }
```

- [ ] **Step 6: Add `RunDeletionAsync` and the nested journal**

After the `ImportJournal` class, add:

```csharp
    /// <summary>
    /// Runs a deletion under the write gate for its whole duration. The token
    /// only cancels the wait for the gate: once the work starts, it runs to an
    /// outcome, so a journal row is never abandoned half-way.
    /// </summary>
    internal async Task<T> RunDeletionAsync<T>(Func<IDeletionJournal, T> work, CancellationToken token)
    {
        await writeGate.WaitAsync(token);
        try
        {
            return await Task.Run(() => work(new DeletionJournal(this)), CancellationToken.None);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private sealed class DeletionJournal(SqliteLibraryRepository owner) : IDeletionJournal
    {
        public Guide? GetGuide(Guid guideId)
        {
            using SqliteConnection connection = owner.OpenConnection();
            return SqliteLibraryRepository.GetGuide(connection, guideId);
        }

        public void Prepare(Guid operationId, Guid guideId)
        {
            using SqliteConnection connection = owner.OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO FileOperations (Id, Kind, Phase, ManifestJson, CreatedUtcMs)
                VALUES ($id, 'DeleteGuide', 'Prepared', $manifest, $now)
                """;
            command.Parameters.AddWithValue("$id", operationId.ToString("N"));
            command.Parameters.AddWithValue("$manifest",
                FileOperationManifest.Create(FileOperationKind.DeleteGuide, operationId, [guideId]));
            command.Parameters.AddWithValue("$now", owner.clock.GetUtcNow().ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
        }

        public void Commit(Guid operationId, Guid guideId, Action beforeCommit)
        {
            using SqliteConnection connection = owner.OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            int Execute(string sql)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$guide", guideId.ToString("N"));
                command.Parameters.AddWithValue("$op", operationId.ToString("N"));
                return command.ExecuteNonQuery();
            }
            // Cascaded ReadingStates and ReaderPreferences deletes aren't counted.
            if (Execute("DELETE FROM Guides WHERE Id = $guide") != 1)
            {
                throw new InvalidDataException("The guide to delete is missing.");
            }
            Execute("DELETE FROM Settings WHERE Key = 'LastActiveGuideId' AND Value = $guide");
            if (Execute("""
                UPDATE FileOperations SET Phase = 'Committed'
                WHERE Id = $op AND Kind = 'DeleteGuide' AND Phase = 'Prepared'
                """) != 1)
            {
                throw new InvalidDataException("The deletion's file operation is missing.");
            }
            beforeCommit();
            transaction.Commit();
        }

        public void RollBack(Guid operationId)
        {
            using SqliteConnection connection = owner.OpenConnection();
            new FileOperationReconciler(owner.paths).RollBackDeletion(connection, operationId);
        }

        public void Finish(Guid operationId)
        {
            using SqliteConnection connection = owner.OpenConnection();
            new FileOperationReconciler(owner.paths).FinishDeletion(connection, operationId);
        }
    }
```

- [ ] **Step 7: Add the reconciler methods**

After `RollBackImport` in `FileOperationReconciler.cs`, add:

```csharp
    /// <summary>
    /// Rolls back one in-process deletion that hasn't committed: every trashed
    /// guide goes back to content, under the same rules as startup recovery.
    /// </summary>
    public void RollBackDeletion(SqliteConnection connection, Guid operationId)
    {
        JournalRow row = ReadDeletion(connection, operationId, FileOperationPhase.Prepared);
        HashSet<Guid> committedGuides = ReadGuideIds(connection);
        string? operationRoot = null;
        foreach (Guid guideId in row.Manifest.GuideIds)
        {
            if (!committedGuides.Contains(guideId))
            {
                throw new InvalidDataException(
                    "File-operation phase conflicts with committed guide metadata.");
            }
            string contentPath = paths.GetGuideRoot(guideId);
            string trashPath = paths.GetTrashedGuideRoot(row.Id, guideId);
            operationRoot ??= Path.GetDirectoryName(trashPath)!;
            RequireDirectoryOrMissing(operationRoot);
            OwnedGuideTree content = OwnedGuideTree.Capture(contentPath);
            OwnedGuideTree trash = OwnedGuideTree.Capture(trashPath);
            if (content.Exists && trash.Exists)
            {
                throw new InvalidDataException(
                    "Prepared deletion has conflicting content and trash directories.");
            }
            if (trash.Exists)
            {
                Directory.Move(trashPath, contentPath);
            }
        }
        RemoveIfEmpty(operationRoot);
        RemoveJournalRow(connection, row.Id);
    }

    /// <summary>Deletes a committed deletion's trash, then its journal row.</summary>
    public void FinishDeletion(SqliteConnection connection, Guid operationId)
    {
        JournalRow row = ReadDeletion(connection, operationId, FileOperationPhase.Committed);
        HashSet<Guid> committedGuides = ReadGuideIds(connection);
        string? operationRoot = null;
        foreach (Guid guideId in row.Manifest.GuideIds)
        {
            if (committedGuides.Contains(guideId))
            {
                throw new InvalidDataException(
                    "File-operation phase conflicts with committed guide metadata.");
            }
            string trashPath = paths.GetTrashedGuideRoot(row.Id, guideId);
            operationRoot ??= Path.GetDirectoryName(trashPath)!;
            RequireDirectoryOrMissing(operationRoot);
            if (OwnedGuideTree.Capture(paths.GetGuideRoot(guideId)).Exists)
            {
                throw new InvalidDataException("Committed deletion still has a content directory.");
            }
            OwnedGuideTree.Capture(trashPath).Delete();
        }
        RemoveIfEmpty(operationRoot);
        RemoveJournalRow(connection, row.Id);
    }

    private static JournalRow ReadDeletion(
        SqliteConnection connection, Guid operationId, FileOperationPhase phase)
    {
        JournalRow row = ReadJournalRows(connection).SingleOrDefault(candidate => candidate.Id == operationId)
            ?? throw new InvalidDataException("The deletion is not in the file-operation journal.");
        if (row.Kind == FileOperationKind.Import || row.Phase != phase)
        {
            throw new InvalidDataException("The file operation is not a deletion in the expected phase.");
        }
        return row;
    }
```

- [ ] **Step 8: Run the tests to see them pass**

Run: Infrastructure tests (whole project).
Expected: all pass, including the 15 new `DeletionJournalTests`. The
existing `FileOperationReconciliationTests` and `ImportJournalTests` pass
unchanged.

- [ ] **Step 9: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Infrastructure/Storage/DeletionJournal.cs src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs src/DesktopGuides.Infrastructure/Storage/FileOperationReconciler.cs tests/DesktopGuides.Infrastructure.Tests/RemovalLibrary.cs tests/DesktopGuides.Infrastructure.Tests/DeletionJournalTests.cs
git commit -m "feat(storage): add the guide deletion journal" -m "RunDeletionAsync holds the write gate around an IDeletionJournal. Commit deletes the guide row, its cascaded state and a matching LastActiveGuideId, and marks the DeleteGuide operation Committed. RollBack and Finish share the reconciler's rules through new RollBackDeletion and FinishDeletion methods." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: Guide remover

Two TDD cycles: `DescribeAsync` first, then `RemoveAsync`.

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/OwnedGuideTree.cs` (add
  `FileCount` after `Exists`, line 14)
- Create: `src/DesktopGuides.Infrastructure/Storage/GuideRemover.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/OwnedGuideTreeTests.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/GuideRemoverTests.cs`

**Interfaces:**
- Consumes: the Task 1 contracts; `IDeletionJournal` and `RunDeletionAsync`
  from Task 2; the `RemovalLibrary` harness from Task 2.
- Produces:
  - `public sealed class GuideRemover` with
    `public GuideRemover(SqliteLibraryRepository repository, ILibraryPaths paths)`,
    `internal GuideRemover(SqliteLibraryRepository, ILibraryPaths, Action<RemovalCheckpoint>)`,
    `Task<GuideRemovalPreview?> DescribeAsync(Guid guideId, CancellationToken token = default)`
    and `Task<GuideRemovalResult> RemoveAsync(Guid guideId, CancellationToken token = default)`.
  - `internal enum RemovalCheckpoint { Prepared, Moved, InCommit, Committed }`.
  - `OwnedGuideTree.FileCount`.

- [ ] **Step 1: Write the failing Describe tests**

`tests/DesktopGuides.Infrastructure.Tests/OwnedGuideTreeTests.cs`:

```csharp
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class OwnedGuideTreeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "desktop-guides-tree-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public void FileCountCountsNestedFiles()
    {
        RemovalLibrary.WriteFile(root, "guide.txt", "a");
        RemovalLibrary.WriteFile(root, "images/map.png", "b");
        RemovalLibrary.WriteFile(root, "images/deep/key.png", "c");

        Assert.Equal(3, OwnedGuideTree.Capture(root).FileCount);
    }

    [Fact]
    public void FileCountIsZeroForAMissingRoot() =>
        Assert.Equal(0, OwnedGuideTree.Capture(root).FileCount);
}
```

`tests/DesktopGuides.Infrastructure.Tests/GuideRemoverTests.cs`, with the
Describe tests first:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class GuideRemoverTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;
    private Guid gameId;
    private Guid guideId;
    private Guid siblingId;
    private Guid otherGameGuideId;

    public async Task InitializeAsync()
    {
        library = await RemovalLibrary.CreateAsync();
        gameId = await library.AddGameAsync("Removal Game");
        guideId = await library.AddGuideAsync(gameId, "Walkthrough");
        siblingId = await library.AddGuideAsync(gameId, "Sibling");
        otherGameGuideId = await library.AddGuideAsync(await library.AddGameAsync("Other Game"), "Other");
        RemovalLibrary.WriteFile(Content(guideId), "guide.txt", "walkthrough");
        RemovalLibrary.WriteFile(Content(guideId), "images/map.png", "map");
        RemovalLibrary.WriteFile(Content(guideId), "images/deep/key.png", "key");
        RemovalLibrary.WriteFile(Content(siblingId), "guide.txt", "sibling");
        RemovalLibrary.WriteFile(Content(otherGameGuideId), "guide.txt", "other");
    }

    public async Task DisposeAsync() => await library.DisposeAsync();

    private string Content(Guid id) => library.Paths.GetGuideRoot(id);

    private GuideRemover Remover(Action<RemovalCheckpoint>? checkpoint = null) =>
        new(library.Repository, library.Paths, checkpoint ?? (_ => { }));

    [Fact]
    public async Task DescribeNamesTheGuideAndCountsItsFiles()
    {
        GuideRemovalPreview? preview = await Remover().DescribeAsync(guideId);

        Assert.Equal(new GuideRemovalPreview(guideId, gameId, "Walkthrough", 3), preview);
    }

    [Fact]
    public async Task DescribeCountsZeroFilesWhenTheContentIsMissing()
    {
        Directory.Delete(Content(guideId), true);

        GuideRemovalPreview? preview = await Remover().DescribeAsync(guideId);

        Assert.Equal(0, preview?.FileCount);
    }

    [Fact]
    public async Task DescribeReturnsNullForAnUnknownGuide() =>
        Assert.Null(await Remover().DescribeAsync(Guid.NewGuid()));

    [Fact]
    public async Task DescribeRefusesALinkInsideTheTree()
    {
        if (!OperatingSystem.IsWindows()) return;
        string link = Path.Combine(Content(guideId), "link");
        RemovalLibrary.CreateJunction(link, Path.Combine(library.Root, "outside"));
        try
        {
            GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(
                () => Remover().DescribeAsync(guideId));

            Assert.Equal(GuideRemovalIssue.Unsafe, error.Issue);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task DescribeRefusesAFileInPlaceOfTheDirectory()
    {
        Directory.Delete(Content(guideId), true);
        File.WriteAllText(Content(guideId), "not a directory");

        GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(
            () => Remover().DescribeAsync(guideId));

        Assert.Equal(GuideRemovalIssue.Unsafe, error.Issue);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~OwnedGuideTreeTests|FullyQualifiedName~GuideRemoverTests"`.
Expected: build fails with CS1061 for `FileCount` and CS0246 for
`GuideRemover` and `RemovalCheckpoint`.

- [ ] **Step 3: Implement `FileCount` and `DescribeAsync`**

In `OwnedGuideTree.cs`, after `Exists`:

```csharp
    public int FileCount => files.Count;
```

`src/DesktopGuides.Infrastructure/Storage/GuideRemover.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Infrastructure.Storage;

internal enum RemovalCheckpoint { Prepared, Moved, InCommit, Committed }

/// <summary>
/// Removes a guide and its owned files. The removal holds the library write
/// gate; content/&lt;guide&gt; moves to .trash/&lt;op&gt;/&lt;guide&gt; under a
/// journaled DeleteGuide operation, so any failure before the commit moves it
/// back, and a crash is finished by the startup reconciler.
/// </summary>
public sealed class GuideRemover
{
    private readonly SqliteLibraryRepository repository;
    private readonly ILibraryPaths paths;
    private readonly Action<RemovalCheckpoint> checkpoint;

    public GuideRemover(SqliteLibraryRepository repository, ILibraryPaths paths)
        : this(repository, paths, _ => { })
    {
    }

    internal GuideRemover(
        SqliteLibraryRepository repository, ILibraryPaths paths, Action<RemovalCheckpoint> checkpoint)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
    }

    /// <summary>The guide and its file count, or null when it's already gone.</summary>
    public async Task<GuideRemovalPreview?> DescribeAsync(Guid guideId, CancellationToken token = default)
    {
        Guide? guide = await repository.GetGuideAsync(guideId, token);
        if (guide is null)
        {
            return null;
        }
        OwnedGuideTree content = await Task.Run(() => Capture(paths.GetGuideRoot(guide.Id)), token);
        return new GuideRemovalPreview(guide.Id, guide.GameId, guide.Title, content.FileCount);
    }

    private static OwnedGuideTree Capture(string root)
    {
        try
        {
            return OwnedGuideTree.Capture(root);
        }
        catch (InvalidDataException error)
        {
            throw new GuideRemovalException(GuideRemovalIssue.Unsafe, error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new GuideRemovalException(GuideRemovalIssue.Failed, error);
        }
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: the same filter.
Expected: 7 passed (on a non-Windows host the junction test returns early).

- [ ] **Step 5: Write the failing Remove tests**

Add to `GuideRemoverTests`:

```csharp
    private string OperationCount() => library.Scalar("SELECT COUNT(*) FROM FileOperations");

    private static int TrashEntries(RemovalLibrary library) =>
        Directory.EnumerateFileSystemEntries(library.Paths.TrashRoot).Count();

    private void AssertGuideIntact()
    {
        Assert.Equal("1|1|1", library.RowsFor(guideId));
        Assert.Equal("walkthrough", File.ReadAllText(Path.Combine(Content(guideId), "guide.txt")));
        Assert.Equal("key", File.ReadAllText(Path.Combine(Content(guideId), "images/deep/key.png")));
        Assert.Equal("0", OperationCount());
        Assert.Equal(0, TrashEntries(library));
    }

    private void AssertNeighboursKept()
    {
        Assert.Equal("1|1|1", library.RowsFor(siblingId));
        Assert.Equal("1|1|1", library.RowsFor(otherGameGuideId));
        Assert.Equal("sibling", File.ReadAllText(Path.Combine(Content(siblingId), "guide.txt")));
        Assert.Equal("other", File.ReadAllText(Path.Combine(Content(otherGameGuideId), "guide.txt")));
    }

    [Fact]
    public async Task RemoveDeletesTheGuideItsStateAndItsFiles()
    {
        List<RemovalCheckpoint> seen = [];

        GuideRemovalResult result = await Remover(seen.Add).RemoveAsync(guideId);

        Assert.Equal(new GuideRemovalResult(GuideRemovalOutcome.Removed, false), result);
        Assert.Equal(
            [RemovalCheckpoint.Prepared, RemovalCheckpoint.Moved, RemovalCheckpoint.InCommit, RemovalCheckpoint.Committed],
            seen);
        Assert.Equal("0|0|0", library.RowsFor(guideId));
        Assert.False(Directory.Exists(Content(guideId)));
        Assert.Equal(0, TrashEntries(library));
        Assert.Equal("0", OperationCount());
        AssertNeighboursKept();
    }

    [Fact]
    public async Task RemoveOfABrokenGuideDeletesItsRows()
    {
        Directory.Delete(Content(guideId), true);

        GuideRemovalResult result = await Remover().RemoveAsync(guideId);

        Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
        Assert.Equal("0|0|0", library.RowsFor(guideId));
        Assert.Equal(0, TrashEntries(library));
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task RemoveOfAnUnknownGuideReturnsNotFound()
    {
        List<RemovalCheckpoint> seen = [];

        GuideRemovalResult result = await Remover(seen.Add).RemoveAsync(Guid.NewGuid());

        Assert.Equal(new GuideRemovalResult(GuideRemovalOutcome.NotFound, false), result);
        Assert.Empty(seen);
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task RemoveRefusesALinkBeforeJournaling()
    {
        if (!OperatingSystem.IsWindows()) return;
        string outside = Path.Combine(library.Root, "outside");
        string link = Path.Combine(Content(guideId), "link");
        RemovalLibrary.CreateJunction(link, outside);
        RemovalLibrary.WriteFile(outside, "keep.txt", "outside");
        List<RemovalCheckpoint> seen = [];
        try
        {
            GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(
                () => Remover(seen.Add).RemoveAsync(guideId));

            Assert.Equal(GuideRemovalIssue.Unsafe, error.Issue);
            Assert.Empty(seen);
            Assert.Equal("1|1|1", library.RowsFor(guideId));
            Assert.Equal("0", OperationCount());
            Assert.Equal("outside", File.ReadAllText(Path.Combine(outside, "keep.txt")));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task AFaultBeforeTheCommitRestoresTheGuide(string point)
    {
        RemovalCheckpoint fault = Enum.Parse<RemovalCheckpoint>(point);

        GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(() => Remover(reached =>
        {
            if (reached == fault) throw new InvalidOperationException("fault");
        }).RemoveAsync(guideId));

        Assert.Equal(GuideRemovalIssue.Failed, error.Issue);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        AssertGuideIntact();
        AssertNeighboursKept();
    }

    [Fact]
    public async Task ContentHeldOpenFailsAndKeepsTheGuide()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (new FileStream(Path.Combine(Content(guideId), "images/map.png"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(
                () => Remover().RemoveAsync(guideId));

            Assert.Equal(GuideRemovalIssue.Failed, error.Issue);
        }
        AssertGuideIntact();
    }

    [Fact]
    public async Task AFailedRestoreKeepsThePreparedRowForStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileStream? held = null;
        try
        {
            GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(() => Remover(reached =>
            {
                if (reached != RemovalCheckpoint.Moved) return;
                string trashed = Directory.EnumerateFiles(library.Paths.TrashRoot, "map.png", SearchOption.AllDirectories).Single();
                held = new FileStream(trashed, FileMode.Open, FileAccess.Read, FileShare.Read);
                throw new InvalidOperationException("fault");
            }).RemoveAsync(guideId));

            Assert.Equal(GuideRemovalIssue.RestoreFailed, error.Issue);
            Assert.Equal("DeleteGuide|Prepared", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
            Assert.Equal("1|1|1", library.RowsFor(guideId));
        }
        finally
        {
            held?.Dispose();
        }

        await library.RestartAsync();

        AssertGuideIntact();
    }

    [Fact]
    public async Task AFailedCleanupStillRemovesTheGuide()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileStream? held = null;
        GuideRemovalResult result;
        try
        {
            result = await Remover(reached =>
            {
                if (reached != RemovalCheckpoint.Committed) return;
                string trashed = Directory.EnumerateFiles(library.Paths.TrashRoot, "map.png", SearchOption.AllDirectories).Single();
                held = new FileStream(trashed, FileMode.Open, FileAccess.Read, FileShare.Read);
            }).RemoveAsync(guideId);

            Assert.Equal(new GuideRemovalResult(GuideRemovalOutcome.Removed, true), result);
            Assert.Equal("0|0|0", library.RowsFor(guideId));
            Assert.Equal("DeleteGuide|Committed", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
            string operationRoot = Assert.Single(Directory.EnumerateFileSystemEntries(library.Paths.TrashRoot));
            Assert.Equal(guideId.ToString("N"), Path.GetFileName(Assert.Single(Directory.EnumerateFileSystemEntries(operationRoot))));
            AssertNeighboursKept();
        }
        finally
        {
            held?.Dispose();
        }

        await library.RestartAsync();

        Assert.Equal(0, TrashEntries(library));
        Assert.Equal("0", OperationCount());
        AssertNeighboursKept();
    }

    [Fact]
    public async Task RemoveWithACancelledTokenWritesNothing()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Remover().RemoveAsync(guideId, new CancellationToken(canceled: true)));

        AssertGuideIntact();
    }
```

- [ ] **Step 6: Run the tests to see them fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~GuideRemoverTests"`.
Expected: build fails with CS1061 for `RemoveAsync`.

- [ ] **Step 7: Implement `RemoveAsync`**

Add to `GuideRemover`:

```csharp
    /// <summary>
    /// Removes the guide. The token only cancels the wait for the write gate;
    /// once the deletion is journaled it runs to an outcome.
    /// </summary>
    public Task<GuideRemovalResult> RemoveAsync(Guid guideId, CancellationToken token = default) =>
        repository.RunDeletionAsync(journal => Remove(journal, guideId), token);

    private GuideRemovalResult Remove(IDeletionJournal journal, Guid guideId)
    {
        if (journal.GetGuide(guideId) is null)
        {
            return new GuideRemovalResult(GuideRemovalOutcome.NotFound, false);
        }
        string contentPath = paths.GetGuideRoot(guideId);
        OwnedGuideTree content = Capture(contentPath);
        Guid operationId = Guid.NewGuid();
        try
        {
            journal.Prepare(operationId, guideId);
        }
        catch (Exception error)
        {
            // Nothing has changed yet, so there's nothing to roll back.
            throw new GuideRemovalException(GuideRemovalIssue.Failed, error);
        }
        try
        {
            checkpoint(RemovalCheckpoint.Prepared);
            if (content.Exists)
            {
                string trashPath = paths.GetTrashedGuideRoot(operationId, guideId);
                Directory.CreateDirectory(Path.GetDirectoryName(trashPath)!);
                Directory.Move(contentPath, trashPath);
            }
            checkpoint(RemovalCheckpoint.Moved);
            journal.Commit(operationId, guideId, () => checkpoint(RemovalCheckpoint.InCommit));
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
                throw new GuideRemovalException(
                    GuideRemovalIssue.RestoreFailed, new AggregateException(error, rollBackError));
            }
            throw new GuideRemovalException(GuideRemovalIssue.Failed, error);
        }
        try
        {
            checkpoint(RemovalCheckpoint.Committed);
            journal.Finish(operationId);
            return new GuideRemovalResult(GuideRemovalOutcome.Removed, false);
        }
        catch (Exception)
        {
            // The Committed row stays, so the next startup deletes the trash.
            return new GuideRemovalResult(GuideRemovalOutcome.Removed, true);
        }
    }
```

- [ ] **Step 8: Run the tests to see them pass**

Run: Infrastructure tests (whole project).
Expected: all pass, including 2 `OwnedGuideTreeTests` and 16
`GuideRemoverTests` cases. On this first Windows run, confirm that
`AFailedRestoreKeepsThePreparedRowForStartup` fails the rollback move
because of the held file. If NTFS allows the move, apply ruling 3's
fallback: in the checkpoint, create a file at `content/<id>` instead of
holding the trashed file, and delete it before `RestartAsync`.

- [ ] **Step 9: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Infrastructure/Storage/OwnedGuideTree.cs src/DesktopGuides.Infrastructure/Storage/GuideRemover.cs tests/DesktopGuides.Infrastructure.Tests/OwnedGuideTreeTests.cs tests/DesktopGuides.Infrastructure.Tests/GuideRemoverTests.cs
git commit -m "feat(storage): remove guides through the trash journal" -m "GuideRemover describes a guide's managed-file count and removes it: the content directory moves to .trash under a DeleteGuide operation, the rows are deleted in one transaction, and the trash is deleted. Failures before the commit move the files back; a failed restore or cleanup is left to startup recovery." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: Game page Remove guide

TDD skip (ruling 8): Production has no test project. The gate is the
Production build here, and the installed smoke in Task 5.

**Files:**
- Create: `src/DesktopGuides.Production/RemoveGuideDialog.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (the Guides header
  grid, around line 266)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`: fields (38–61),
  library setup (226), close handler (272), `UpdateOpenSelectedGuideAction`
  (479), the render's collapse block (963), and a new handler after
  `ImportGuideClicked` (805)

**Interfaces:**
- Consumes: `GuideRemover`, `GuideRemovalPreview`, `GuideRemovalResult`,
  `GuideRemovalOutcome`, `GuideRemovalException` and
  `GuideRemovalPresentation` from Tasks 1 and 3.
- Produces: the automation surface Task 5 drives: `RemoveSelectedGuide`
  (name `Remove {title}`), `RemoveGuideDialog`, `RemoveGuideMessage`, and
  the dialog's `PrimaryButton` and `CloseButton`.

- [ ] **Step 1: Add the dialog**

`src/DesktopGuides.Production/RemoveGuideDialog.cs`:

```csharp
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

/// <summary>Confirms a guide removal. Cancel is the default, so Enter and Escape both cancel.</summary>
internal static class RemoveGuideDialog
{
    public static ContentDialog Create(GuideRemovalPreview preview, XamlRoot root)
    {
        TextBlock message = new()
        {
            Text = GuideRemovalPresentation.DialogBody(preview.FileCount),
            Style = (Style)Application.Current.Resources["DesktopGuidesBodyStyle"],
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(message, "RemoveGuideMessage");
        ContentDialog dialog = new()
        {
            Title = GuideRemovalPresentation.DialogTitle(preview.Title),
            Content = message,
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        AutomationProperties.SetAutomationId(dialog, "RemoveGuideDialog");
        return dialog;
    }
}
```

- [ ] **Step 2: Add the header button**

In `ShellWindow.xaml`, give the Guides header grid a fourth column, put
the new button in column 2 and move **Import guide** to column 3:

```xml
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
```

```xml
                    <Button x:Name="RemoveSelectedGuideButton"
                            Grid.Column="2"
                            Content="Remove guide"
                            Visibility="Collapsed"
                            Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"
                            Click="RemoveSelectedGuideClicked"
                            AutomationProperties.AutomationId="RemoveSelectedGuide" />
                    <Button x:Name="ImportGuideButton"
                            Grid.Column="3"
```

(The rest of the `ImportGuideButton` element is unchanged.)

- [ ] **Step 3: Wire the shell**

Fields, next to `guidePublisher`, `importRequested` and `activeImportDialog`:

```csharp
    private GuideRemover? guideRemover;
    private bool removeRequested;
    private ContentDialog? activeRemoveDialog;
```

Library setup, after `guidePublisher = new GuideImportPublisher(repository, paths);`:

```csharp
        guideRemover = new GuideRemover(repository, paths);
```

Close handler, after `activeImportDialog?.Hide();`:

```csharp
        activeRemoveDialog?.Hide();
```

Render collapse block, after `OpenSelectedGuideButton.Visibility = Visibility.Collapsed;`:

```csharp
        RemoveSelectedGuideButton.Visibility = Visibility.Collapsed;
```

`UpdateOpenSelectedGuideAction` becomes:

```csharp
    private void UpdateOpenSelectedGuideAction()
    {
        if (GuideList.IsEnabled &&
            navigator.Current is GameRoute game &&
            GuideList.SelectedItem is Guide guide &&
            guide.GameId == game.GameId)
        {
            AutomationProperties.SetName(
                OpenSelectedGuideButton, $"Open {guide.Title}");
            AutomationProperties.SetName(
                RemoveSelectedGuideButton, $"Remove {guide.Title}");
            OpenSelectedGuideButton.Visibility = Visibility.Visible;
            RemoveSelectedGuideButton.Visibility = Visibility.Visible;
        }
        else
        {
            OpenSelectedGuideButton.Visibility = Visibility.Collapsed;
            RemoveSelectedGuideButton.Visibility = Visibility.Collapsed;
        }
    }
```

- [ ] **Step 4: Add the handler**

After `ImportGuideClicked`:

```csharp
    private async void RemoveSelectedGuideClicked(object sender, RoutedEventArgs args)
    {
        if (removeRequested || closeRequested ||
            navigator.Current is not GameRoute route ||
            GuideList.SelectedItem is not Guide guide ||
            guide.GameId != route.GameId)
        {
            return;
        }
        removeRequested = true;
        GuideList.IsEnabled = false;
        OpenSelectedGuideButton.IsEnabled = false;
        RemoveSelectedGuideButton.IsEnabled = false;
        ImportGuideButton.IsEnabled = false;
        // The row that takes the removed row's place, or the previous row.
        int index = GuideList.SelectedIndex;
        Guide? neighbor = (index + 1 < GuideList.Items.Count ? GuideList.Items[index + 1]
            : index > 0 ? GuideList.Items[index - 1] : null) as Guide;
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
                GuideRemover remover = guideRemover
                    ?? throw new InvalidOperationException("The library is not ready.");
                GuideRemovalPreview? preview;
                try
                {
                    preview = await remover.DescribeAsync(guide.Id);
                }
                catch (Exception error)
                {
                    ShowRemovalError(error, guide.Title);
                    return;
                }
                if (closeRequested)
                {
                    return;
                }
                if (preview is null)
                {
                    rendered = true;
                    pendingGuideFocus = neighbor?.Id;
                    await RenderCurrentAsync();
                    ShowTransientStatus(GuideRemovalPresentation.AlreadyRemoved(guide.Title));
                    return;
                }
                ContentDialog dialog = RemoveGuideDialog.Create(preview, Navigation.XamlRoot);
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
                GuideRemovalResult result;
                try
                {
                    result = await remover.RemoveAsync(guide.Id);
                }
                catch (Exception error)
                {
                    ShowRemovalError(error, guide.Title);
                    return;
                }
                if (!closeRequested && navigator.Current is GameRoute shown && shown.GameId == route.GameId)
                {
                    rendered = true;
                    pendingGuideFocus = neighbor?.Id;
                    await RenderCurrentAsync();
                }
                ShowTransientStatus(result.Outcome == GuideRemovalOutcome.NotFound
                    ? GuideRemovalPresentation.AlreadyRemoved(guide.Title)
                    : GuideRemovalPresentation.Removed(guide.Title, result.CleanupPending));
            });
        }
        finally
        {
            removeRequested = false;
            OpenSelectedGuideButton.IsEnabled = true;
            RemoveSelectedGuideButton.IsEnabled = true;
            if (!closeRequested && navigator.Current is GameRoute shown && shown.GameId == route.GameId)
            {
                if (!rendered)
                {
                    GuideList.IsEnabled = true;
                    ImportGuideButton.IsEnabled = !importRequested;
                    UpdateOpenSelectedGuideAction();
                    RemoveSelectedGuideButton.Focus(FocusState.Programmatic);
                }
                else if (GuideList.Items.Count == 0)
                {
                    ImportGuideButton.Focus(FocusState.Programmatic);
                }
            }
        }
    }

    private void ShowRemovalError(Exception error, string title)
    {
        if (!closeRequested)
        {
            ShowErrorStatus(GuideRemovalPresentation.Error(
                error is GuideRemovalException removal ? removal.Issue : GuideRemovalIssue.Failed, title));
        }
    }
```

The render re-enables the list and **Import guide**, selects
`pendingGuideFocus` and focuses its row, so a rendered removal needs no
restore beyond the empty-state focus.

- [ ] **Step 5: Build**

Run: Production build.
Expected: build succeeded, 0 errors, and no new warnings in the changed files.

- [ ] **Step 6: Review against the Review Focus**

Read the handler once more for items 3–5. NotFound from `DescribeAsync`
and from `RemoveAsync` both re-render. The close handler hides
`activeRemoveDialog`, and `ShowAsync` then returns `None`, so nothing is
removed. `RemoveAsync` isn't cancellable once it starts, so the drained
queue waits for it. An empty list focuses **Import guide**.

- [ ] **Step 7: Commit** (show the message in chat first)

```bash
git add src/DesktopGuides.Production/RemoveGuideDialog.cs src/DesktopGuides.Production/ShellWindow.xaml src/DesktopGuides.Production/ShellWindow.xaml.cs
git commit -m "feat(shell): remove the selected guide from the Game page" -m "Add a Remove guide header button and a confirmation dialog that names the guide and its managed-file count, with Cancel as the default. After removal the neighbouring guide is selected and focused, or Import guide when none is left." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```


---

### Task 5: Installed smoke for removal

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (the `describe-import` block, ~lines 160–167)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (Mode `ValidateSet` ~line 12, the top-level helpers after `Wait-FocusedGuide` ~line 459, the nested `Wait-FocusedId` ~line 1592, a new branch before `long-list` ~line 1726, the catch diagnostics ~line 2270)
- Modify: `tools/p1/windows_shell_install.ps1` (after `Run-ImportScenarios` ~line 855, and the two `Run-ImportScenarios` calls ~lines 1186 and 1251)

**Interfaces:**
- Consumes:
  - the Task 4 automation IDs `RemoveSelectedGuide` (name `Remove {title}`),
    `RemoveGuideDialog` and `RemoveGuideMessage`, and the dialog's
    `PrimaryButton` and `CloseButton`;
  - the Task 1 copy `Removed {title}.` and the one-file dialog body;
  - `$report.importPublishLight.importedTitle` from `Run-ImportScenarios`,
    which leaves "Copied Guide…" and "Imported Guide…" in Import Test Game.
- Produces:
  - `describe-import` fields `TrashEntries`, `ReadingStates` and
    `ReaderPreferences`;
  - smoke modes `remove-guide-cancel` and `remove-guide`;
  - results `remove-cancel-dark` and `remove-light`, with the screenshots
    `remove-confirm` and `removed`;
  - report fields `removeCancelState` and `removeState`.

TDD skip: the smoke is the test. Its first real run is the Step 6
installed verification.

- [ ] **Step 1: Seed state fields**

In `describe-import`, add three fields after `ContentEntries`:

```csharp
        TrashEntries = CountEntries(importPaths.TrashRoot),
        ReadingStates = Scalar("SELECT COUNT(*) FROM ReadingStates"),
        ReaderPreferences = Scalar("SELECT COUNT(*) FROM ReaderPreferences"),
```

`CountEntries` returns 0 when `.trash` doesn't exist, so an empty trash and
a missing trash both read 0.

Run: Seed build.
Expected: build succeeded, 0 errors.

- [ ] **Step 2: Smoke modes and shared helpers**

Add `'remove-guide-cancel', 'remove-guide'` after
`'import-duplicate-open'` in the Mode `ValidateSet`.

Move `Wait-FocusedId` out of the `import-*` branch: delete the nested copy
(~line 1592) and add this after the top-level `Wait-FocusedGuide`, with
`Get-GuideRowNames`:

```powershell
    function Wait-FocusedId([string] $id) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            if ($focused -and $focused.Current.AutomationId -eq $id) { return }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected keyboard focus on '$id'."
    }

    function Get-GuideRowNames {
        $list = Find-ById 'GuideList'
        if (-not $list -or $list.Current.IsOffscreen) {
            throw 'Expected a visible guide list.'
        }
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        $names = @($list.FindAll($scope, $condition) | ForEach-Object { $_.Current.Name })
        return ,$names
    }

    function Wait-GuideRowCount([int] $expected) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $names = Get-GuideRowNames
            if ($names.Count -eq $expected) { return ,$names }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected $expected guide rows, found $($names.Count): $($names -join ', ')."
    }
```

The import branch's calls to `Wait-FocusedId` keep working, because the
top-level helper is in scope there.

- [ ] **Step 3: The removal branch**

Before `elseif ($Mode -eq 'long-list')`, add:

```powershell
    elseif ($Mode -like 'remove-*') {
        if (-not $ExpectedGuideTitle) {
            throw "$Mode needs -ExpectedGuideTitle."
        }
        $title = $ExpectedGuideTitle
        Select-Element 'Import Test Game'
        [void](Wait-Name 'GameHeading' 'Import Test Game')
        [void](Wait-Status 'Game ready.')

        # Selecting a guide opens it (Ruling 1), so come back to the game
        # with it selected before removing it.
        Open-GuideFromGame $title
        [void](Wait-Name 'ReaderHeading' $title)
        [void](Wait-Status 'Guide details ready.')
        Press-Enter (Wait-Name 'ReaderBackToGame' 'Back to game')
        [void](Wait-Name 'GameHeading' 'Import Test Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-SelectedGuide $title)
        [void](Wait-GuideRowCount 2)

        Invoke-Element (Wait-Name 'RemoveSelectedGuide' "Remove $title")
        [void](Wait-VisibleById 'RemoveGuideDialog')
        [void](Wait-Name 'RemoveGuideMessage' ("This removes the guide, its reading progress, and its 1 managed file " +
            "from Desktop Guides. The original file you imported isn't affected."))
        [void](Wait-Name 'PrimaryButton' 'Remove')
        [void](Wait-Name 'CloseButton' 'Cancel')
        $report.removeConfirmScreenshot = Save-WindowScreenshot 'remove-confirm'
        $report.phases += 'remove-confirm'

        if ($Mode -eq 'remove-guide-cancel') {
            Invoke-Element (Wait-EnabledById 'CloseButton')
            [void](Wait-HiddenById 'RemoveGuideDialog')
            Wait-FocusedId 'RemoveSelectedGuide'
            [void](Wait-GuideRowCount 2)
            [void](Wait-SelectedGuide $title)
            $report.phases += 'remove-cancelled'
        }
        else {
            Invoke-Element (Wait-EnabledById 'PrimaryButton')
            [void](Wait-HiddenById 'RemoveGuideDialog')
            [void](Wait-Status "Removed $title.")
            $remaining = Wait-GuideRowCount 1
            if ($remaining[0] -eq $title) {
                throw "The removed guide '$title' is still listed."
            }
            [void](Wait-SelectedGuide $remaining[0])
            Wait-FocusedGuide $remaining[0]
            $report.remainingTitle = $remaining[0]
            $report.removedScreenshot = Save-WindowScreenshot 'removed'
            $report.phases += 'removed'
        }
    }
```

`Wait-SelectedGuide` returns the row, hence the `[void]`. The dialog
buttons are found by their WinUI automation IDs `PrimaryButton` and
`CloseButton`, as the import and game-editor branches already do.

- [ ] **Step 4: Failure diagnostics**

In the catch block, add `-or $Mode -like 'remove-*'` to the mode condition,
and add `'RemoveGuideDialog', 'RemoveGuideMessage', 'RemoveSelectedGuide'`
to the ID list after `'ImportBusyText'`.

- [ ] **Step 5: Install-script runs**

After `Run-ImportScenarios`, add:

```powershell
function Assert-RemovalState([string] $label, $expected) {
    $state = Invoke-ShellSeed @('describe-import', $dataRoot) | ConvertFrom-Json
    foreach ($name in $expected.Keys) {
        if ($state.$name -ne $expected[$name]) {
            throw "$label, $name was $($state.$name); expected $($expected[$name])."
        }
    }
    return $state
}

function Run-RemovalScenarios {
    $title = $report.importPublishLight.importedTitle
    if (-not $title) {
        throw 'Guide removal needs the title from the light import-publish run.'
    }
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $false
        Start-InstalledShell
        $report.removeCancelDark = Run-ShellSmoke 'remove-guide-cancel' `
            -ResultName 'remove-cancel-dark' -ExpectedGuideTitle $title
        Close-InstalledShell

        $report.removeCancelState = Assert-RemovalState 'After Cancel' ([ordered]@{
            Guides = 2; FileOperations = 0; StagingEntries = 0; ContentEntries = 2
            TrashEntries = 0; ReadingStates = 2; ReaderPreferences = 2; LegacyTextGuides = 2
        })

        Set-AppThemePreference $true
        Start-InstalledShell
        $report.removeLight = Run-ShellSmoke 'remove-guide' `
            -ResultName 'remove-light' -ExpectedGuideTitle $title
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
    $report.removeState = Assert-RemovalState 'After removal' ([ordered]@{
        Guides = 1; FileOperations = 0; StagingEntries = 0; ContentEntries = 1
        TrashEntries = 0; ReadingStates = 1; ReaderPreferences = 1; LegacyTextGuides = 1
    })
}
```

Call `Run-RemovalScenarios` right after both `Run-ImportScenarios` calls:
inside `if ($ImportOnly) { ... }` before `$report.success = $true`, and in
the full run before the `Get-ChildItem ... | Remove-Item` that precedes
`Run-ProviderScenarios`.

- [ ] **Step 6: Static checks**

```bash
perl -ne 'print "$ARGV:$.: non-ASCII\n" if /[^\x00-\x7F]/; close ARGV if eof' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
```

Expected: no output.

Stage, then parse both scripts on the host:

```bash
s 'powershell -NoProfile -Command "foreach ($f in @(''E:\work\desktop-guides\t15-3\tools\p1\windows_shell_ui_smoke.ps1'',''E:\work\desktop-guides\t15-3\tools\p1\windows_shell_install.ps1'')) { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$e); if ($e) { $e; exit 1 } }; ''parsed''"'
```

Expected: `parsed`.

- [ ] **Step 7: Commit**

Show the message in chat first.

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): smoke-test guide removal" \
  -m "After the import group, a dark remove-guide-cancel run opens the Remove guide dialog for the imported guide, checks it names the guide and 1 managed file, and cancels: focus returns to Remove guide and the library is unchanged. A light remove-guide run removes it: the status confirms, the remaining guide is selected and focused, and the library has 1 guide, 1 content directory, 1 reading state, 1 preferences row and no trash or file operation. describe-import now reports trash entries, reading states and reader preferences." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 8: Installed verification**

This needs a pushed branch, so ask the user first. After approval, push
and let CI's `production-shell-ui` job run. It is the gate of record.

Alternatively, with a CI-built x64 MSIX already on the host, run
`windows_shell_install.ps1 -PackagePath <msix> -ResultDirectory E:\work\desktop-guides\t15-3-results -ImportOnly`
through an interactive scheduled task. Follow `docs/p1/e2e-testing.md`:
back up and restore the app data and `%LOCALAPPDATA%\DesktopGuides\P0-WebView`.

Expected:
- `success: true`;
- phases `remove-confirm` and `remove-cancelled` in `remove-cancel-dark`;
- phases `remove-confirm` and `removed` in `remove-light`, and
  `remainingTitle` starting with `Copied Guide`;
- `removeCancelState` 2/0/0/2/0/2/2/2 and `removeState` 1/0/0/1/0/1/1/1 for
  Guides, FileOperations, StagingEntries, ContentEntries, TrashEntries,
  ReadingStates, ReaderPreferences and LegacyTextGuides.

Copy the screenshots to `docs/p1/evidence/t15-3-guide-deletion/`:

| From | To |
| --- | --- |
| `remove-cancel-dark.remove-confirm.png` | `remove-confirm-dark.png` |
| `remove-light.remove-confirm.png` | `remove-confirm-light.png` |
| `remove-light.removed.png` | `removed-light.png` |

---

### Task 6: Documentation and verification record

**Files:**
- Modify: `docs/p1/t15-3-guide-deletion-design.md` (status line, new verification record)
- Modify: `docs/p1/implementation-plan.md` (after the T06.4 paragraph ending "and a light Open existing.", ~line 647)
- Modify: `docs/progress.md` (the "Updated" line, a new T15.3 row after the T06.4 row)
- Modify: `docs/p1/e2e-testing.md` (a new `Guide removal` row after `Duplicate import`, ~line 251)
- Create: `docs/p1/evidence/t15-3-guide-deletion/` (the three PNGs from Task 5 Step 8)

**Interfaces:**
- Consumes:
  - the CI run ID and conclusion from Task 5 Step 8;
  - the Core and Infrastructure test totals from the last host runs;
  - the three evidence PNGs;
  - every `Ruling:` line in the executor's ledger.

TDD skip: this task changes documentation only. The gate is the placeholder
grep, `git diff --check` and a read-through.

Fill every `<…>` below from the observed run before committing. Never
commit a placeholder.

- [ ] **Step 1: Spec**

In `t15-3-guide-deletion-design.md`:

- Replace the status line's first sentence with:
  `Status: implemented on \`feat/p1-t15-3-guide-deletion\`; verified by CI run <run id>.`
  Keep the prerequisite sentence.
- In Installed smoke, change the Light row's "Select the first guide" to
  "Select the imported guide (the last row)". This follows Ruling 10.
- Append:

```markdown
## T15.3 verification record

- **Unit tests.** On `pcsx2-win`, Infrastructure <n>/<n> and Core <n>/<n>
  passed. The new tests are:
  - `GuideRemovalPresentationTests`: the dialog title and body, and every
    outcome and issue message;
  - `OwnedGuideTreeTests`: `FileCount` for a nested tree and a missing root;
  - `DeletionJournalTests`: Prepare, Commit (including the cascade and the
    matching `LastActiveGuideId`), the Commit guards, RollBack, Finish, the
    write gate and a cancelled gate wait;
  - `GuideRemoverTests`: Describe, the happy path, the broken and unknown
    guides, a link, faults at `Prepared`, `Moved` and `InCommit`, a held
    content file, a failed restore and a failed cleanup (each finished by
    a restarted repository's startup recovery), and cancellation.
- **Installed.** CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>)
  passed `production-shell-ui`. After the import group:
  - the dark `remove-guide-cancel` run showed the dialog naming the
    imported guide and 1 managed file, and Cancel returned focus to
    **Remove guide** with the library unchanged;
  - the light `remove-guide` run removed that guide. The status read
    "Removed {title}." and the remaining guide was selected and focused.

  The final state was 1 guide, 0 file operations, 0 staging entries, 1
  content directory, 0 trash entries, 1 reading state and 1 preferences row.
- **Rulings.** Rulings 1–12 in the [plan](t15-3-guide-deletion-plan.md#rulings-against-the-spec),
  plus <the ledger rulings made during implementation, each on one line,
  or "none">.
- **Evidence.**
  - [Confirmation, dark](evidence/t15-3-guide-deletion/remove-confirm-dark.png)
  - [Confirmation, light](evidence/t15-3-guide-deletion/remove-confirm-light.png)
  - [Removed, light](evidence/t15-3-guide-deletion/removed-light.png)
```

- [ ] **Step 2: Implementation plan and E2E catalogue**

In `docs/p1/implementation-plan.md`, after the T06.4 paragraph, add:

```markdown
T15.3 is implemented on `feat/p1-t15-3-guide-deletion`; see the
[design and verification record](t15-3-guide-deletion-design.md). The Game
page's **Remove guide** confirms with the guide's title and managed-file
count. `GuideRemover` journals a `DeleteGuide` operation, moves
`content/<id>` to `.trash/<op>/<id>`, deletes the guide row with its state
and preferences, then deletes the trash. A failure before the commit
restores the guide, and a failure after it leaves a journaled trash entry
that startup deletes. CI run <run id> passed the installed removal group:
a dark Cancel and a light removal.
```

In `docs/p1/e2e-testing.md`, after the `Duplicate import` row, add:

```markdown
| Guide removal | After Duplicate import, open the imported guide, go back and invoke Remove guide. The `RemoveGuideDialog` names the guide and 1 managed file. In dark, Cancel returns focus to Remove guide and the library is unchanged. In light, Remove closes the dialog, the status reads "Removed {title}.", and the remaining guide is selected and focused. Afterwards the library has one guide, one content directory, one reading state, one preferences row, and no trash entry, file operation or staging entry. | T15.3, TR15.1, TR15.2, TR11.3 |
```

- [ ] **Step 3: Progress**

In `docs/progress.md`:

- Keep the "Updated" date line current.
- After the T06.4 row, add:

```markdown
| P1 T15.3 guide deletion | Implemented on `feat/p1-t15-3-guide-deletion`; PR open. | A guide can be removed from its game after a confirmation naming it and its managed-file count. Removal deletes its metadata, reading state, preferences and managed files; every failure leaves the guide intact or a journaled trash entry that startup finishes. CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>) passed the installed removal group; see the [verification record](p1/t15-3-guide-deletion-design.md#t153-verification-record). |
```

After the PR opens, change "PR open" to the linked PR number in a
follow-up commit on the same branch.

- [ ] **Step 4: Check and commit**

```bash
grep -n '<run id>\|<n>/<n>\|<the ledger' docs/p1/t15-3-guide-deletion-design.md docs/p1/implementation-plan.md docs/progress.md docs/p1/e2e-testing.md
git diff --check
```

Expected: no output from either command.

Show the message in chat first.

```bash
git add docs/p1/t15-3-guide-deletion-design.md docs/p1/implementation-plan.md docs/progress.md docs/p1/e2e-testing.md docs/p1/evidence/t15-3-guide-deletion
git commit -m "docs(p1): record T15.3 guide deletion verification" \
  -m "Mark the T15.3 design implemented and add its verification record: unit tests, CI run <run id>, rulings and three screenshots. Add the T15.3 paragraph to the implementation plan, a Guide removal row to the E2E catalogue and a T15.3 row to progress." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Traceability

| Requirement | Evidence in this plan |
| --- | --- |
| TR15.1: cleanup targets only app-owned paths | Task 3's Unsafe tests (a link, a file in place of the directory) throw before any journal row; the fault Theory and `ContentHeldOpenFailsAndKeepsTheGuide` show only `content/<id>` and `.trash/<op>/<id>` change; the failed-cleanup test leaves only the known trash entry and its `Committed` row; Task 2's RollBack and Finish tests touch only the manifest's `content/<id>` and `.trash/<op>/<id>` |
| TR15.2: deletion removes metadata, state, preferences and owned files; cancel leaves all four | Task 2's Commit cascade test; Task 3's happy path (with a sibling guide and another game untouched); Task 5's `removeState` after `remove-guide` and `removeCancelState` after `remove-guide-cancel` |
| TR11.3: installed UI Automation | Task 5 drives Remove guide, Cancel and Remove by automation ID in dark and light |

## PR outcome

- **Target task:** T15.3.
- **Prerequisites:** T06.3 (PR #19) and T15.2 (PR #5), both merged. T06.4
  (PR #20, merged) provides the two-guide import group the smoke builds on.
- **Outcome:** a guide can be removed from its game after a confirmation that
  names it and its file count. Removal deletes its metadata, state,
  preferences and managed files, and every failure leaves the guide intact or
  a journaled trash entry that startup finishes. The PR includes the dark
  and light confirmation screenshots and the light result.
