# T06.3 Import Publication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pressing **Import** on a valid preview copies the guide into
app-owned storage, fingerprints and re-validates it, and publishes it with
its metadata, so it survives removal of the original; cancellation, failure
and crash leave no listed guide and no residue.

**Architecture:** Infrastructure gains `GuideImportPublisher`, which runs
inside a new `SqliteLibraryRepository.RunImportAsync` that holds the write
gate for the whole import. SQL stays in the repository behind a synchronous
`IImportJournal` (`Prepare`, `Publish`, `RollBack`); rollback reuses the
T15.2 reconciler through a new `RollBackImport`. Production adds a primary
**Import** button and a progress bar to `ImportGuideDialog`, and
`ShellWindow` re-renders the Game page with the new guide selected and
focused.

**Tech Stack:** .NET 10, WinUI 3, Microsoft.Data.Sqlite, PdfPig 0.1.16,
xUnit, the PowerShell 5.1 UI Automation harness.

**Spec:** `docs/p1/t06-3-import-publication-design.md`

**Target:** T06.3. **Prerequisites, all merged:** T03.2, T03.3, T15.2
(PRs #3–#5) and T06.2 (PR #17). **Next:** T06.4.

## Global Constraints

- The source is opened only with `FileAccess.Read` and `FileShare.Read`,
  and it's never written.
- Staged files are created with `FileMode.CreateNew` and flushed with
  `Flush(flushToDisk: true)`.
- Everything is written under `library/.staging/<op>/<guide>` and renamed to
  `library/content/<guide>` with `Directory.Move`. Every destination goes
  through `ILibraryPaths.GetPlannedGuideFile`.
- `ContentSha256` is lowercase hex. TXT and PDF use the SHA-256 of the
  copied bytes. HTML uses the SHA-256 of the UTF-8 text made of one line per
  file, sorted by ordinal comparison of the path:
  `<managed relative path>\t<lowercase sha>\n`.
- No schema change. The schema stays at version 3.
- Messages, verbatim:
  - Missing: "The file is no longer there. Choose it again."
  - Changed: "The file changed after it was checked. Choose it again to see
    the new version."
  - NotEnoughSpace: "There isn't enough free space to import this guide."
  - SaveFailed: "The guide couldn't be saved to your library. Nothing was
    changed."
- Messages never include the source path or the underlying exception.
- `ERROR_DISK_FULL` (112) and `ERROR_HANDLE_DISK_FULL` (39) map to
  `NotEnoughSpace`.
- Cancellation is honoured until the publish transaction starts. After that,
  the token is ignored.
- `SourceLabel` is the original file name. The source path, PDF passwords and
  preview warnings are not stored.
- Duplicates import as a second copy (T06.4 adds the choice).
- UI tests assert only what app code controls.
- PowerShell scripts stay ASCII-only.
- Never print, copy or log provider credential values.

## Rulings against the spec

Task 7 records these rulings in the design doc's verification record.

- **`PublishAsync` is public and takes a `string` title.** Production can't
  see Infrastructure internals (`InternalsVisibleTo` names only the test
  project), so the publisher and its constructor are public.
  `GuideTitle` is a static helper, not a type, so the title is a string
  checked with `GuideTitle.Create`.
- **Progress is `IProgress<ImportProgress>`,** where
  `ImportProgress(double Fraction, bool Publishing)` is a Core record. The
  dialog must disable Cancel when publication starts, and a bare `double`
  can't say that.
- **The journal is synchronous,** and `AbandonAsync` is folded into
  `RollBack`. The whole import already runs on the thread pool under the
  gate, and SQLite calls here are synchronous.
- **In-process rollback doesn't reuse the startup `Resolve` path verbatim.**
  `RollBackImport` deletes the staged tree when it exists, and otherwise
  deletes `content/<guide>`. `Directory.Move` is atomic, so while the stage
  exists, `content/<guide>` isn't this import's. This lets a pre-created
  content directory survive, where startup preflight would throw a
  conflict. Startup `Run` is unchanged.
- **The checkpoint `Copied` replaces `MidCopy`.** It fires after every file
  is copied and before verification. That is a deterministic point, and a
  change made there is exactly one made during the copy.
- **The source is re-checked before format verification,** not after. A
  file changed during the copy then reports `Changed`, not a misleading
  decode error.
- **The dialog reports success through `ImportedGuideId`.** `Hide()` makes
  `ShowAsync` return `None`, not `Primary`, so `ShellWindow` checks the ID.
- **No logging.** `src` has no logging infrastructure, and adding one is out
  of scope. The spec's "logs carry only the issue and the operation ID"
  becomes a rule for future logging.
- **The smoke publishes two guides.** The import group runs in light and
  dark, so the final state expects two TXT guides with code page 437.
- **`SourceLabel` isn't truncated.** NTFS limits a file name to 255 UTF-16
  units, which always fits the column's 255-character check, so the
  spec's truncation can't trigger.
- **The smoke shares its helpers by branching.** The existing
  `import-preview` branch becomes `import-*`, and the scenario splits inside
  it, so no helper is moved.

## Review Focus

1. **Title edited during import.** The title box is disabled while
   importing, and the stored title is the one captured on Import. Covered by
   the smoke (Task 6).
2. **Window closed mid-import.** The closing deferral cancels the import and
   waits for it. If publication had already started, the guide is kept and
   the Game page shows it. Checked in the Task 5 code review, because the
   UI harness can't close at an exact moment.
3. **Game deleted in another window during import.** The foreign-key
   failure gives `SaveFailed` with a full rollback. Test
   `GameDeletedBeforeCommitIsSaveFailed` (Task 3).
4. **A second import right after a failed one.** A failed import must leave
   the dialog usable and the library clean. Tests
   `CrashIsRolledBackAtStartup` (a later import succeeds) and the smoke.
5. **Reading-position save during import.** It waits for the gate and then
   succeeds. Test `ImportHoldsTheWriteGate` (Task 3).

## Host commands

The Mac has no `dotnet`. Build and unit-test on the Windows host over SSH,
staging a fresh copy for each run:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t06-3) { Remove-Item -Recurse -Force E:\work\desktop-guides\t06-3 }; New-Item -ItemType Directory E:\work\desktop-guides\t06-3 | Out-Null"'
COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t06-3'
```

The steps below refer to these commands by name: **Core tests**,
**Infrastructure tests**, **Production build** and **Seed build**.

```bash
s 'cd /d E:\work\desktop-guides\t06-3 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'
s 'cd /d E:\work\desktop-guides\t06-3 && dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release'
s 'cd /d E:\work\desktop-guides\t06-3 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'
s 'cd /d E:\work\desktop-guides\t06-3 && dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release'
```

Add `--filter "FullyQualifiedName~<ClassName>"` to run one test class. Stage
again before every run.

---

### Task 1: Contracts and the HTML fingerprint

**Files:**
- Modify: `src/DesktopGuides.Core/Import/ImportContracts.cs`
- Create: `src/DesktopGuides.Infrastructure/Import/GuideFingerprint.cs`
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideFingerprintTests.cs`
- Modify: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorHtmlPdfTests.cs`

**Interfaces:**
- Produces:
  - `ImportIssue.NotEnoughSpace` and `ImportIssue.SaveFailed`, appended.
  - `public readonly record struct ImportProgress(double Fraction, bool Publishing)` in `DesktopGuides.Core.Import`.
  - `HtmlImportManifest(..., IReadOnlyList<ImportWarning> Warnings, string Fingerprint)`.
  - `internal static string GuideFingerprint.OfHtml(IEnumerable<(string RelativePath, string Sha256)> files)`.
  - `GuideImportValidator` gains:
    - `internal StaticHtmlImportValidator Html`;
    - `internal Task<StaticHtmlImportPreview> PreviewHtmlAsync(ImportSource source, CancellationToken token)`;
    - `internal static GuideImportException Unreadable(string name)`;
    - `internal static GuideImportException NotPdf()`.

- [ ] **Step 1: Write the failing tests**

`GuideFingerprintTests.cs`:

```csharp
using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class GuideFingerprintTests
{
    private static readonly (string, string)[] Files =
    [
        ("guide.html", new string('a', 64)),
        ("styles/site.css", new string('b', 64)),
    ];

    [Fact]
    public void HtmlFingerprintHashesSortedPathAndHashLines() =>
        Assert.Equal(
            "4124251947a2ecaab5072f8a3d6c7e914af2b8ab1bf2920001d050d131f3759a",
            GuideFingerprint.OfHtml(Files));

    [Fact]
    public void HtmlFingerprintIgnoresInputOrder() =>
        Assert.Equal(GuideFingerprint.OfHtml(Files), GuideFingerprint.OfHtml(Files.Reverse()));
}
```

In `GuideImportValidatorHtmlPdfTests.cs`, add the following. Adjust the
`InspectAsync` call to match how the file's existing HTML tests reach the
fixture.

```csharp
    [Fact]
    public async Task HtmlManifestCarriesTheContentFingerprint()
    {
        ImportInspection inspection = await new GuideImportValidator().InspectAsync(
            P0Fixtures.Resolve("html-static/guide.html"), CancellationToken.None);

        HtmlImportManifest html = Assert.IsType<HtmlImportManifest>(
            Assert.IsType<ImportReady>(inspection).Manifest);
        Assert.Equal(
            "743c87a4c5222c99cb5de3f4ee931a63b994a54be3dc9ec5855c855dafcd3f21",
            html.Fingerprint);
    }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run **Infrastructure tests** with `--filter "FullyQualifiedName~GuideFingerprint|FullyQualifiedName~HtmlManifestCarries"`.
Expected: build error. `GuideFingerprint` and `HtmlImportManifest.Fingerprint` don't exist.

- [ ] **Step 3: Implement**

In `ImportContracts.cs`:
- Append `, string Fingerprint` as the last positional parameter of
  `HtmlImportManifest`.
- Append `NotEnoughSpace, SaveFailed,` to `ImportIssue`.
- Add:

```csharp
/// <summary>Copy progress from 0 to 1; Publishing is set once cancellation no longer applies.</summary>
public readonly record struct ImportProgress(double Fraction, bool Publishing);
```

`GuideFingerprint.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace DesktopGuides.Infrastructure.Import;

internal static class GuideFingerprint
{
    /// <summary>SHA-256 of one "path\tsha\n" line per file, in ordinal path order.</summary>
    public static string OfHtml(IEnumerable<(string RelativePath, string Sha256)> files)
    {
        StringBuilder text = new();
        foreach ((string path, string sha) in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            text.Append(path).Append('\t').Append(sha).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
```

In `GuideImportValidator.cs`:
- Add `internal StaticHtmlImportValidator Html => html;`.
- Move the body of `InspectHtmlAsync`, from the lock-check `using` through
  the whole `try`/`catch` around `html.PreviewAsync`, into
  `internal async Task<StaticHtmlImportPreview> PreviewHtmlAsync(ImportSource source, CancellationToken token)`.
  It returns `preview`. `InspectHtmlAsync` becomes:

```csharp
    private async Task<ImportInspection> InspectHtmlAsync(
        ImportSource source, string title, CancellationToken token)
    {
        StaticHtmlImportPreview preview = await PreviewHtmlAsync(source, token);
        ImportWarning[] warnings = preview.Warnings
            .Select(warning => new ImportWarning(warning.RelativePath ?? warning.RawTarget, warning.Message))
            .ToArray();
        return new ImportReady(new HtmlImportManifest(
            source, title, preview.EntryRelativePath,
            preview.Manifest.Assets.Count - 1, preview.Manifest.TotalBytes, warnings,
            GuideFingerprint.OfHtml(preview.Manifest.Assets.Select(asset => (asset.RelativePath, asset.Sha256)))));
    }
```

- Change `private static GuideImportException Unreadable(string name)` and
  `private static GuideImportException NotPdf()` to `internal static`.

- [ ] **Step 4: Run the tests and confirm they pass**

Stage, then run **Infrastructure tests** (full) and **Core tests**.
Expected: all pass. If the `743c…` pin differs, the fixture bytes aren't the
ones hashed on the Mac. Check `.gitattributes` (`tests/fixtures/p0/** -text`)
and the staged bytes before changing the pin.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Core/Import/ImportContracts.cs src/DesktopGuides.Infrastructure/Import/GuideFingerprint.cs src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs tests/DesktopGuides.Infrastructure.Tests/Import/GuideFingerprintTests.cs tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorHtmlPdfTests.cs
git commit -m "feat(p1): add import progress, new issues and the HTML fingerprint" \
  -m "Adds ImportProgress, the NotEnoughSpace and SaveFailed issues, and GuideFingerprint.OfHtml. HTML manifests now carry the fingerprint, and the HTML preview mapping is extracted so T06.3 can re-run it." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 2: Import journal, rollback and the write-gate entry point

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/FileOperationReconciler.cs`
- Create: `src/DesktopGuides.Infrastructure/Storage/ImportJournal.cs` (`IImportJournal`, `NewImportedGuide`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/ImportJournalTests.cs`

**Interfaces:**
- Consumes: `FileOperationManifest.Create(FileOperationKind, Guid, IEnumerable<Guid>)`, `OwnedGuideTree.Capture`.
- Produces:

```csharp
internal interface IImportJournal
{
    void Prepare(Guid operationId, Guid guideId);
    void Publish(NewImportedGuide guide, Action beforeCommit);
    void RollBack(Guid operationId);
}

internal sealed record NewImportedGuide(
    Guid OperationId, Guid Id, Guid GameId, string Title, GuideFormat Format,
    string PrimaryRelativePath, string ContentSha256, long ContentBytes,
    string? SourceLabel, int? TextCodePage);

// SqliteLibraryRepository
internal Task<T> RunImportAsync<T>(
    Func<IImportJournal, CancellationToken, Task<T>> work, CancellationToken token);

// FileOperationReconciler
public void RollBackImport(SqliteConnection connection, Guid operationId);
```

- [ ] **Step 1: Write the failing tests**

`ImportJournalTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class ImportJournalTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "desktop-guides-journal-" + Guid.NewGuid().ToString("N"));
    private ManagedPathResolver paths = null!;
    private SqliteLibraryRepository repository = null!;
    private Game game = null!;
    private readonly Guid operationId = Guid.NewGuid();
    private readonly Guid guideId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        paths = new ManagedPathResolver(Path.Combine(root, "app-data"));
        repository = new SqliteLibraryRepository(paths);
        await repository.InitializeAsync();
        game = await repository.AddGameAsync("Journal Game", null, null);
    }

    public async Task DisposeAsync()
    {
        await repository.DisposeAsync();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public async Task PrepareRecordsAPreparedImport()
    {
        await Run(journal => journal.Prepare(operationId, guideId));

        Assert.Equal("Import|Prepared", Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
    }

    [Fact]
    public async Task PublishAddsGuideWithEmptyStateAndRemovesTheOperation()
    {
        await Run(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Publish(Guide(), () => { });
        });

        Guide guide = Assert.Single(await repository.ListGuidesAsync(game.Id));
        Assert.Equal(guideId, guide.Id);
        Assert.Equal($"content/{guideId:N}", guide.ManagedRelativeRoot);
        Assert.Equal("guide.txt", guide.PrimaryRelativePath);
        Assert.Equal(437, guide.TextCodePage);
        Assert.Equal("notes.txt", guide.SourceLabel);
        ReadingState state = Assert.IsType<ReadingState>(await repository.GetReadingStateAsync(guideId));
        Assert.Null(state.LocatorJson);
        Assert.NotNull(await repository.GetReaderPreferencesAsync(guideId));
        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    [Fact]
    public async Task PublishFailureBeforeCommitLeavesNoGuideAndKeepsTheOperation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Publish(Guide(), () => throw new InvalidOperationException());
        }));

        Assert.Empty(await repository.ListGuidesAsync(game.Id));
        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    [Fact]
    public async Task RollBackRemovesTheStageAndKeepsContentItDoesNotOwn()
    {
        string staged = paths.GetStagedGuideRoot(operationId, guideId);
        string content = paths.GetGuideRoot(guideId);
        await Run(journal =>
        {
            journal.Prepare(operationId, guideId);
            WriteMarker(staged);
            WriteMarker(content);
            journal.RollBack(operationId);
        });

        Assert.False(Directory.Exists(Path.GetDirectoryName(staged)!));
        Assert.True(File.Exists(Path.Combine(content, "marker.txt")));
        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    [Fact]
    public async Task RollBackRemovesRenamedContent()
    {
        string content = paths.GetGuideRoot(guideId);
        await Run(journal =>
        {
            journal.Prepare(operationId, guideId);
            WriteMarker(content);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.GetStagedGuideRoot(operationId, guideId))!);
            journal.RollBack(operationId);
        });

        Assert.False(Directory.Exists(content));
        Assert.False(Directory.Exists(Path.GetDirectoryName(paths.GetStagedGuideRoot(operationId, guideId))!));
        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    [Fact]
    public async Task RunImportHoldsTheWriteGate()
    {
        using SemaphoreSlim entered = new(0);
        using SemaphoreSlim release = new(0);
        Task import = repository.RunImportAsync<bool>(async (_, _) =>
        {
            entered.Release();
            await release.WaitAsync();
            return true;
        }, CancellationToken.None);
        await entered.WaitAsync();

        Task edit = repository.UpdateGameAsync(game.Id, "Renamed", null, null);
        await Task.Delay(200);
        Assert.False(edit.IsCompleted);

        release.Release();
        await import;
        await edit;
        Assert.Equal("Renamed", (await repository.GetGameAsync(game.Id))!.Title);
    }

    private NewImportedGuide Guide() => new(
        operationId, guideId, game.Id, "Imported", GuideFormat.Txt, "guide.txt",
        new string('a', 64), 20, "notes.txt", 437);

    private Task Run(Action<IImportJournal> work) =>
        repository.RunImportAsync<bool>((journal, _) =>
        {
            work(journal);
            return Task.FromResult(true);
        }, CancellationToken.None);

    private static void WriteMarker(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "marker.txt"), "x");
    }

    private string Scalar(string sql)
    {
        using SqliteConnection connection = new($"Data Source={paths.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run **Infrastructure tests** with `--filter "FullyQualifiedName~ImportJournalTests"`.
Expected: build error. `IImportJournal`, `NewImportedGuide` and `RunImportAsync` don't exist.

- [ ] **Step 3: Implement**

`ImportJournal.cs`:

```csharp
using DesktopGuides.Core.Library;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>SQL for one import. Callers already hold the repository's write gate.</summary>
internal interface IImportJournal
{
    /// <summary>Commits a Prepared Import row owning the staged and content directories.</summary>
    void Prepare(Guid operationId, Guid guideId);

    /// <summary>Adds the guide and its empty state rows and removes the operation, in one transaction.</summary>
    void Publish(NewImportedGuide guide, Action beforeCommit);

    /// <summary>Removes the import's owned directories, then its row.</summary>
    void RollBack(Guid operationId);
}

internal sealed record NewImportedGuide(
    Guid OperationId, Guid Id, Guid GameId, string Title, GuideFormat Format,
    string PrimaryRelativePath, string ContentSha256, long ContentBytes,
    string? SourceLabel, int? TextCodePage);
```

In `FileOperationReconciler.cs`, add `RollBackImport`. Replace the
empty-root block at the end of `Resolve` with a call to the new
`RemoveIfEmpty`:

```csharp
    /// <summary>
    /// Rolls back one in-process import. The rename is atomic, so while the
    /// stage exists, content/&lt;guide&gt; isn't this import's and is kept.
    /// </summary>
    public void RollBackImport(SqliteConnection connection, Guid operationId)
    {
        JournalRow row = ReadJournalRows(connection).SingleOrDefault(candidate => candidate.Id == operationId)
            ?? throw new InvalidDataException("The import is not in the file-operation journal.");
        if (row.Kind != FileOperationKind.Import)
        {
            throw new InvalidDataException("The file operation is not an import.");
        }
        HashSet<Guid> committedGuides = ReadGuideIds(connection);
        string? operationRoot = null;
        foreach (Guid guideId in row.Manifest.GuideIds)
        {
            if (committedGuides.Contains(guideId))
            {
                throw new InvalidDataException(
                    "File-operation phase conflicts with committed guide metadata.");
            }
            string stagedPath = paths.GetStagedGuideRoot(row.Id, guideId);
            operationRoot ??= Path.GetDirectoryName(stagedPath)!;
            RequireDirectoryOrMissing(operationRoot);
            OwnedGuideTree staged = OwnedGuideTree.Capture(stagedPath);
            (staged.Exists ? staged : OwnedGuideTree.Capture(paths.GetGuideRoot(guideId))).Delete();
        }
        RemoveIfEmpty(operationRoot);
        RemoveJournalRow(connection, row.Id);
    }

    private static void RemoveIfEmpty(string? directory)
    {
        if (directory is not null && Directory.Exists(directory) &&
            !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }
```

In `SqliteLibraryRepository.cs`, add the entry point and a private nested
journal:

```csharp
    /// <summary>
    /// Runs an import under the write gate for its whole duration, so no other
    /// write sees a half-published guide.
    /// </summary>
    internal async Task<T> RunImportAsync<T>(
        Func<IImportJournal, CancellationToken, Task<T>> work, CancellationToken token)
    {
        await writeGate.WaitAsync(token);
        try
        {
            return await Task.Run(() => work(new ImportJournal(this), token), token);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private sealed class ImportJournal(SqliteLibraryRepository owner) : IImportJournal
    {
        public void Prepare(Guid operationId, Guid guideId)
        {
            using SqliteConnection connection = owner.OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO FileOperations (Id, Kind, Phase, ManifestJson, CreatedUtcMs)
                VALUES ($id, 'Import', 'Prepared', $manifest, $now)
                """;
            command.Parameters.AddWithValue("$id", operationId.ToString("N"));
            command.Parameters.AddWithValue("$manifest",
                FileOperationManifest.Create(FileOperationKind.Import, operationId, [guideId]));
            command.Parameters.AddWithValue("$now", owner.clock.GetUtcNow().ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
        }

        public void Publish(NewImportedGuide guide, Action beforeCommit)
        {
            using SqliteConnection connection = owner.OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Guides (
                    Id, GameId, Title, Format, ManagedRelativeRoot, PrimaryRelativePath,
                    ContentSha256, ContentBytes, SourceLabel, TextCodePage,
                    ImportedUtcMs, UpdatedUtcMs
                ) VALUES (
                    $id, $game, $title, $format, $root, $primary,
                    $sha, $bytes, $label, $codePage, $now, $now
                );
                INSERT INTO ReadingStates (GuideId) VALUES ($id);
                INSERT INTO ReaderPreferences (GuideId) VALUES ($id);
                """;
            string id = guide.Id.ToString("N");
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$game", guide.GameId.ToString("N"));
            insert.Parameters.AddWithValue("$title", guide.Title);
            insert.Parameters.AddWithValue("$format", guide.Format.ToString());
            insert.Parameters.AddWithValue("$root", "content/" + id);
            insert.Parameters.AddWithValue("$primary", guide.PrimaryRelativePath);
            insert.Parameters.AddWithValue("$sha", guide.ContentSha256);
            insert.Parameters.AddWithValue("$bytes", guide.ContentBytes);
            insert.Parameters.AddWithValue("$label", (object?)guide.SourceLabel ?? DBNull.Value);
            insert.Parameters.AddWithValue("$codePage", (object?)guide.TextCodePage ?? DBNull.Value);
            insert.Parameters.AddWithValue("$now", owner.clock.GetUtcNow().ToUnixTimeMilliseconds());
            insert.ExecuteNonQuery();
            using SqliteCommand remove = connection.CreateCommand();
            remove.Transaction = transaction;
            remove.CommandText = """
                DELETE FROM FileOperations
                WHERE Id = $op AND Kind = 'Import' AND Phase = 'Prepared'
                """;
            remove.Parameters.AddWithValue("$op", guide.OperationId.ToString("N"));
            if (remove.ExecuteNonQuery() != 1)
            {
                throw new InvalidDataException("The import's file operation is missing.");
            }
            beforeCommit();
            transaction.Commit();
        }

        public void RollBack(Guid operationId)
        {
            using SqliteConnection connection = owner.OpenConnection();
            new FileOperationReconciler(owner.paths).RollBackImport(connection, operationId);
        }
    }
```

If the repository's fields have other names than `paths` and `clock`, use
the existing names.

- [ ] **Step 4: Run the tests and confirm they pass**

Stage, then run **Infrastructure tests** (full).
Expected: all pass, including the existing `FileOperationReconciliationTests`.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Infrastructure/Storage/FileOperationReconciler.cs src/DesktopGuides.Infrastructure/Storage/ImportJournal.cs src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Infrastructure.Tests/ImportJournalTests.cs
git commit -m "feat(p1): add the import journal and in-process import rollback" \
  -m "RunImportAsync holds the library write gate for a whole import. The journal prepares an Import operation, publishes the guide with empty reading-state and preference rows in one transaction, and rolls back through the reconciler, keeping a content directory the import doesn't own." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 3: `GuideImportPublisher` for TXT and PDF

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/Import/PublisherHarness.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportPublisherTests.cs`

**Interfaces:**
- Consumes: from Task 1, `ImportProgress`, `ImportIssue.NotEnoughSpace`/`SaveFailed`, `GuideImportValidator.Unreadable`, `NotPdf`, `ReadPdf`, `PreviewHtmlAsync` and `Html`; from Task 2, `RunImportAsync`, `IImportJournal` and `NewImportedGuide`.
- Produces:

```csharp
internal enum ImportCheckpoint { Prepared, Copied, Verified, Renamed, InCommit }

public sealed class GuideImportPublisher
{
    public GuideImportPublisher(SqliteLibraryRepository repository, ILibraryPaths paths, GuideImportLimits? limits = null);
    internal GuideImportPublisher(SqliteLibraryRepository repository, ILibraryPaths paths, GuideImportLimits? limits,
        Func<Guid> newId, Func<string, Stream> createStagedFile,
        Action<ImportCheckpoint> checkpoint, Action<IImportJournal, Guid> rollBack);
    public Task<Guid> PublishAsync(ImportManifest manifest, Guid gameId, string title,
        IProgress<ImportProgress>? progress, CancellationToken token);
}
```

`newId` is called twice per import: the operation ID first, then the guide ID.

- [ ] **Step 1: Write the harness**

`PublisherHarness.cs`:

```csharp
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

internal sealed class PublisherHarness : IAsyncDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "desktop-guides-publish-" + Guid.NewGuid().ToString("N"));

    private PublisherHarness() => Paths = new ManagedPathResolver(Path.Combine(root, "app-data"));

    public ManagedPathResolver Paths { get; }
    public SqliteLibraryRepository Repository { get; private set; } = null!;
    public Game Game { get; private set; } = null!;
    public ImportTestDirectory Sources { get; } = new();
    public GuideImportValidator Validator { get; } = new();
    public Guid OperationId { get; private set; }
    public Guid GuideId { get; private set; }

    public static async Task<PublisherHarness> CreateAsync()
    {
        PublisherHarness harness = new();
        harness.Repository = new SqliteLibraryRepository(harness.Paths);
        await harness.Repository.InitializeAsync();
        harness.Game = await harness.Repository.AddGameAsync("Publish Game", "PC", null);
        return harness;
    }

    /// <summary>A publisher whose next import uses fresh, known IDs.</summary>
    public GuideImportPublisher Publisher(
        Action<ImportCheckpoint>? checkpoint = null,
        Func<string, Stream>? createStagedFile = null,
        Action<IImportJournal, Guid>? rollBack = null)
    {
        OperationId = Guid.NewGuid();
        GuideId = Guid.NewGuid();
        Queue<Guid> ids = new([OperationId, GuideId]);
        return new GuideImportPublisher(
            Repository, Paths, null,
            () => ids.Count > 0 ? ids.Dequeue() : Guid.NewGuid(),
            createStagedFile ?? (path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true)),
            checkpoint ?? (_ => { }),
            rollBack ?? ((journal, operationId) => journal.RollBack(operationId)));
    }

    public async Task<ImportManifest> InspectAsync(string path, int? codePage = null)
    {
        ImportInspection inspection = await Validator.InspectAsync(path, CancellationToken.None);
        return inspection switch
        {
            ImportReady ready => ready.Manifest,
            ImportNeedsTxtEncoding needs => await Validator.ResolveTxtEncodingAsync(
                needs, codePage ?? throw new InvalidOperationException("A code page is required."), CancellationToken.None),
            _ => throw new InvalidOperationException(),
        };
    }

    public Task<Guid> PublishAsync(
        GuideImportPublisher publisher, ImportManifest manifest,
        IProgress<ImportProgress>? progress = null, CancellationToken token = default) =>
        publisher.PublishAsync(manifest, Game.Id, "Imported Guide", progress, token);

    public long Count(string table)
    {
        using SqliteConnection connection = new($"Data Source={Paths.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }

    public void Execute(string sql, Guid id)
    {
        using SqliteConnection connection = new($"Data Source={Paths.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; " + sql;
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        command.ExecuteNonQuery();
    }

    /// <summary>No guide, no operation row, and nothing staged or managed.</summary>
    public void AssertNothingLeft()
    {
        Assert.Equal(0, Count("Guides"));
        Assert.Equal(0, Count("FileOperations"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Paths.StagingRoot));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Paths.ContentRoot));
    }

    public async ValueTask DisposeAsync()
    {
        await Repository.DisposeAsync();
        Sources.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

/// <summary>A staged file on a full disk.</summary>
internal sealed class DiskFullStream : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));

    public override void Write(ReadOnlySpan<byte> buffer) => Write([], 0, 0);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) =>
        ValueTask.FromException(new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)));
}
```

- [ ] **Step 2: Write the failing tests**

`GuideImportPublisherTests.cs`:

```csharp
using System.Security.Cryptography;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class GuideImportPublisherTests
{
    [Fact]
    public async Task PublishesUtf8TextGuide()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        IReadOnlyList<FileFingerprint> original = FileFingerprint.Of(source);
        ImportManifest manifest = await harness.InspectAsync(source);

        Guid id = await harness.PublishAsync(harness.Publisher(), manifest);

        Assert.Equal(harness.GuideId, id);
        Guide guide = Assert.Single(await harness.Repository.ListGuidesAsync(harness.Game.Id));
        byte[] bytes = File.ReadAllBytes(source);
        Assert.Equal(
            (id, "Imported Guide", GuideFormat.Txt, $"content/{id:N}", "guide.txt",
                Convert.ToHexStringLower(SHA256.HashData(bytes)), (long)bytes.Length, "notes.txt", (int?)null),
            (guide.Id, guide.Title, guide.Format, guide.ManagedRelativeRoot, guide.PrimaryRelativePath,
                guide.ContentSha256, guide.ContentBytes, guide.SourceLabel, guide.TextCodePage));
        Assert.Equal(bytes, File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, "guide.txt")));
        Assert.Null((await harness.Repository.GetReadingStateAsync(id))!.LocatorJson);
        Assert.NotNull(await harness.Repository.GetReaderPreferencesAsync(id));
        Assert.Equal(0, harness.Count("FileOperations"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness.Paths.StagingRoot));
        Assert.Equal(original, FileFingerprint.Of(source));
    }

    [Fact]
    public async Task PublishesLegacyTextWithTheChosenCodePage()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-legacy.txt", "legacy.txt");

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source, 437));

        Assert.Equal(437, (await harness.Repository.GetGuideAsync(id))!.TextCodePage);
    }

    [Fact]
    public async Task PublishesPdfGuide()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("pdf-short.pdf", "short.pdf");

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source));

        Guide guide = (await harness.Repository.GetGuideAsync(id))!;
        Assert.Equal((GuideFormat.Pdf, "guide.pdf", 896L), (guide.Format, guide.PrimaryRelativePath, guide.ContentBytes));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, "guide.pdf")));
    }

    [Fact]
    public async Task GuideSurvivesDeletingTheOriginal()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source));

        File.Delete(source);

        Guide guide = (await harness.Repository.GetGuideAsync(id))!;
        byte[] managed = File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, guide.PrimaryRelativePath));
        Assert.Equal(guide.ContentSha256, Convert.ToHexStringLower(SHA256.HashData(managed)));
    }

    [Fact]
    public async Task SourceRemovedBeforeCopyIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        File.Delete(source);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal((ImportIssue.Missing, "The file is no longer there. Choose it again."), (error.Issue, error.Message));
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task SourceGrownAfterPreviewIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        File.AppendAllText(source, "more");

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        Assert.Equal(
            "The file changed after it was checked. Choose it again to see the new version.", error.Message);
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task SourceRewrittenDuringCopyIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point != ImportCheckpoint.Copied) return;
            byte[] bytes = File.ReadAllBytes(source);
            bytes[0] ^= 0x20;
            File.WriteAllBytes(source, bytes);
            File.SetLastWriteTimeUtc(source, manifest.Source.LastWriteUtc.UtcDateTime.AddSeconds(2));
        });

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task LockedSourceIsUnreadable()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        using FileStream locked = new(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        harness.AssertNothingLeft();
    }

    [Theory]
    [InlineData(ImportCheckpoint.Prepared)]
    [InlineData(ImportCheckpoint.Copied)]
    [InlineData(ImportCheckpoint.Verified)]
    [InlineData(ImportCheckpoint.Renamed)]
    public async Task CancellationBeforeCommitRollsBack(ImportCheckpoint at)
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        IReadOnlyList<FileFingerprint> original = FileFingerprint.Of(source);
        ImportManifest manifest = await harness.InspectAsync(source);
        using CancellationTokenSource cancel = new();
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point == at) cancel.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.PublishAsync(publisher, manifest, token: cancel.Token));

        harness.AssertNothingLeft();
        Assert.Equal(original, FileFingerprint.Of(source));
    }

    [Fact]
    public async Task CancellationFromProgressRollsBack()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        using CancellationTokenSource cancel = new();
        SyncProgress progress = new(value =>
        {
            if (!value.Publishing) cancel.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.PublishAsync(harness.Publisher(), manifest, progress, cancel.Token));

        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task CancellationInsideTheCommitIsIgnored()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        using CancellationTokenSource cancel = new();
        List<ImportProgress> reports = [];
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point == ImportCheckpoint.InCommit) cancel.Cancel();
        });

        Guid id = await harness.PublishAsync(publisher, manifest, new SyncProgress(reports.Add), cancel.Token);

        Assert.NotNull(await harness.Repository.GetGuideAsync(id));
        Assert.Equal(new ImportProgress(1, true), reports[^1]);
        Assert.All(reports.SkipLast(1), report => Assert.True(report is { Publishing: false, Fraction: < 1 }));
    }

    [Fact]
    public async Task FullDiskIsNotEnoughSpace()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(createStagedFile: _ => new DiskFullStream()), manifest));

        Assert.Equal(
            (ImportIssue.NotEnoughSpace, "There isn't enough free space to import this guide."),
            (error.Issue, error.Message));
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task ExistingContentDirectoryIsSaveFailedAndKept()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GuideImportPublisher publisher = harness.Publisher();
        string foreign = Path.Combine(harness.Paths.GetGuideRoot(harness.GuideId), "foreign.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
        File.WriteAllText(foreign, "not ours");

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(
            (ImportIssue.SaveFailed, "The guide couldn't be saved to your library. Nothing was changed."),
            (error.Issue, error.Message));
        Assert.Equal("not ours", File.ReadAllText(foreign));
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness.Paths.StagingRoot));
        Assert.Equal(0, harness.Count("Guides") + harness.Count("FileOperations"));
    }

    [Fact]
    public async Task FailureInsideTheCommitIsSaveFailed()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point == ImportCheckpoint.InCommit) throw new InvalidOperationException("injected");
        });

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task GameDeletedBeforeCommitIsSaveFailed()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point == ImportCheckpoint.Renamed) harness.Execute("DELETE FROM Games WHERE Id = $id", harness.Game.Id);
        });

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        harness.AssertNothingLeft();
    }

    [Theory]
    [InlineData(ImportCheckpoint.Prepared)]
    [InlineData(ImportCheckpoint.Copied)]
    [InlineData(ImportCheckpoint.Verified)]
    [InlineData(ImportCheckpoint.Renamed)]
    [InlineData(ImportCheckpoint.InCommit)]
    public async Task CrashIsRolledBackAtStartup(ImportCheckpoint at)
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GuideImportPublisher crashing = harness.Publisher(
            checkpoint: point =>
            {
                if (point == at) throw new InvalidOperationException("crash");
            },
            rollBack: (_, _) => { });
        await Assert.ThrowsAsync<GuideImportException>(() => harness.PublishAsync(crashing, manifest));
        Assert.Equal(1, harness.Count("FileOperations"));

        await harness.Repository.InitializeAsync();

        Assert.Equal(1, harness.Repository.LastStartupReconciliation!.ResolvedOperationCount);
        harness.AssertNothingLeft();
        Guid later = await harness.PublishAsync(harness.Publisher(), manifest);
        Assert.NotNull(await harness.Repository.GetGuideAsync(later));
    }

    [Fact]
    public async Task FailedRollBackStillReportsTheOriginalIssue()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GuideImportPublisher publisher = harness.Publisher(
            createStagedFile: _ => new DiskFullStream(),
            rollBack: (_, _) => throw new IOException("rollback failed"));

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(ImportIssue.NotEnoughSpace, error.Issue);
        Assert.Equal(1, harness.Count("FileOperations"));
        await harness.Repository.InitializeAsync();
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task ImportHoldsTheWriteGate()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        Guid earlier = await harness.PublishAsync(harness.Publisher(), manifest);
        using ManualResetEventSlim paused = new();
        using ManualResetEventSlim resume = new();
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point != ImportCheckpoint.Copied) return;
            paused.Set();
            resume.Wait();
        });
        Task<Guid> import = harness.PublishAsync(publisher, manifest);
        Assert.True(paused.Wait(TimeSpan.FromSeconds(10)));

        Task save = harness.Repository.SaveReadingLocationAsync(earlier, "{\"one\":1}", 0.25);
        await Task.Delay(200);
        Assert.False(save.IsCompleted);

        resume.Set();
        await import;
        await save;
        Assert.Equal("{\"one\":1}", (await harness.Repository.GetReadingStateAsync(earlier))!.LocatorJson);
    }

    private sealed class SyncProgress(Action<ImportProgress> report) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => report(value);
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Stage, then run **Infrastructure tests** with `--filter "FullyQualifiedName~GuideImportPublisherTests"`.
Expected: build error. `GuideImportPublisher` and `ImportCheckpoint` don't exist.

- [ ] **Step 4: Implement**

`GuideImportPublisher.cs`. Task 4 adds HTML by replacing `ImportPlan`,
`PlanAsync`, `OpenSourceAsync`, `CopyAsync`'s last line and `VerifyAsync`.

```csharp
using System.Security.Cryptography;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;
using DesktopGuides.Core.Text;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Import;

internal enum ImportCheckpoint { Prepared, Copied, Verified, Renamed, InCommit }

/// <summary>
/// Copies a previewed guide into managed storage and publishes it. The whole
/// import holds the library write gate; any failure before the commit rolls
/// back, and a crash is rolled back by the startup reconciler.
/// </summary>
public sealed class GuideImportPublisher
{
    private const string MissingMessage = "The file is no longer there. Choose it again.";
    private const string ChangedMessage =
        "The file changed after it was checked. Choose it again to see the new version.";
    private const string NoSpaceMessage = "There isn't enough free space to import this guide.";
    private const string SaveFailedMessage =
        "The guide couldn't be saved to your library. Nothing was changed.";
    private const int BufferBytes = 81920;

    private readonly SqliteLibraryRepository repository;
    private readonly ILibraryPaths paths;
    private readonly GuideImportValidator validator;
    private readonly Func<Guid> newId;
    private readonly Func<string, Stream> createStagedFile;
    private readonly Action<ImportCheckpoint> checkpoint;
    private readonly Action<IImportJournal, Guid> rollBack;

    public GuideImportPublisher(
        SqliteLibraryRepository repository, ILibraryPaths paths, GuideImportLimits? limits = null)
        : this(repository, paths, limits, Guid.NewGuid,
            path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true),
            _ => { },
            (journal, operationId) => journal.RollBack(operationId))
    {
    }

    internal GuideImportPublisher(
        SqliteLibraryRepository repository, ILibraryPaths paths, GuideImportLimits? limits,
        Func<Guid> newId, Func<string, Stream> createStagedFile,
        Action<ImportCheckpoint> checkpoint, Action<IImportJournal, Guid> rollBack)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        validator = new GuideImportValidator(limits);
        this.newId = newId;
        this.createStagedFile = createStagedFile;
        this.checkpoint = checkpoint;
        this.rollBack = rollBack;
    }

    /// <summary>
    /// Returns the new guide's ID. Throws <see cref="GuideImportException"/>,
    /// or <see cref="OperationCanceledException"/> before publication starts.
    /// </summary>
    public Task<Guid> PublishAsync(
        ImportManifest manifest, Guid gameId, string title,
        IProgress<ImportProgress>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string validTitle = GuideTitle.Create(title);
        return repository.RunImportAsync(
            (journal, gateToken) => PublishLockedAsync(journal, manifest, gameId, validTitle, progress, gateToken),
            token);
    }

    private async Task<Guid> PublishLockedAsync(
        IImportJournal journal, ImportManifest manifest, Guid gameId, string title,
        IProgress<ImportProgress>? progress, CancellationToken token)
    {
        CheckSource(manifest.Source);
        ImportPlan plan = await PlanAsync(manifest, token);
        token.ThrowIfCancellationRequested();
        Guid operationId = newId();
        Guid guideId = newId();
        try
        {
            journal.Prepare(operationId, guideId);
        }
        catch (SqliteException)
        {
            throw new GuideImportException(ImportIssue.SaveFailed, SaveFailedMessage);
        }
        try
        {
            Pass(ImportCheckpoint.Prepared, token);
            string staged = paths.GetStagedGuideRoot(operationId, guideId);
            string fingerprint = await CopyAsync(plan, manifest.Source, guideId, staged, progress, token);
            Pass(ImportCheckpoint.Copied, token);
            CheckSource(manifest.Source);
            await VerifyAsync(manifest, plan, staged, token);
            Pass(ImportCheckpoint.Verified, token);
            Directory.Move(staged, paths.GetGuideRoot(guideId));
            Pass(ImportCheckpoint.Renamed, token);
            // Past this point cancellation no longer applies.
            progress?.Report(new ImportProgress(1, true));
            journal.Publish(
                new NewImportedGuide(
                    operationId, guideId, gameId, title, manifest.Format, plan.PrimaryRelativePath,
                    fingerprint, plan.TotalBytes, manifest.Source.FileName,
                    (manifest as TxtImportManifest)?.CodePage),
                () => checkpoint(ImportCheckpoint.InCommit));
            return guideId;
        }
        catch (Exception error)
        {
            try
            {
                rollBack(journal, operationId);
            }
            catch (Exception)
            {
                // The Prepared row stays, and the startup reconciler finishes the rollback.
            }
            if (error is OperationCanceledException or GuideImportException)
            {
                throw;
            }
            throw new GuideImportException(ImportIssue.SaveFailed, SaveFailedMessage);
        }
    }

    private void Pass(ImportCheckpoint point, CancellationToken token)
    {
        checkpoint(point);
        token.ThrowIfCancellationRequested();
    }

    private sealed record PlannedFile(string RelativePath, long ByteCount, string? Sha256);

    private sealed record ImportPlan(IReadOnlyList<PlannedFile> Files, string PrimaryRelativePath)
    {
        public long TotalBytes => Files.Sum(file => file.ByteCount);
    }

    private Task<ImportPlan> PlanAsync(ImportManifest manifest, CancellationToken token) =>
        Task.FromResult(manifest switch
        {
            TxtImportManifest => Single("guide.txt", manifest.Source),
            PdfImportManifest => Single("guide.pdf", manifest.Source),
            _ => throw new ArgumentException("Unsupported import manifest.", nameof(manifest)),
        });

    private static ImportPlan Single(string name, ImportSource source) =>
        new([new PlannedFile(name, source.ByteCount, null)], name);

    /// <summary>Copies and hashes every planned file; returns the fingerprint.</summary>
    private async Task<string> CopyAsync(
        ImportPlan plan, ImportSource source, Guid guideId, string staged,
        IProgress<ImportProgress>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(staged);
        string guideRoot = paths.GetGuideRoot(guideId);
        long total = plan.TotalBytes;
        long copied = 0;
        byte[] buffer = new byte[BufferBytes];
        List<(string RelativePath, string Sha256)> hashes = [];
        foreach (PlannedFile file in plan.Files)
        {
            string target = Path.Combine(
                staged, Path.GetRelativePath(guideRoot, paths.GetPlannedGuideFile(guideId, file.RelativePath)));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long written = 0;
            await using (Stream input = await OpenSourceAsync(plan, file, source, token))
            await using (Stream output = Create(target))
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int read = await ReadAsync(input, buffer, source.FileName, token);
                    if (read == 0)
                    {
                        break;
                    }
                    written += read;
                    if (written > file.ByteCount)
                    {
                        throw Changed();
                    }
                    hash.AppendData(buffer, 0, read);
                    await WriteAsync(output, buffer.AsMemory(0, read), token);
                    copied += read;
                    progress?.Report(new ImportProgress(
                        total == 0 ? 0 : Math.Min((double)copied / total, 0.99), false));
                }
                Flush(output);
            }
            string sha = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (written != file.ByteCount || (file.Sha256 is not null && sha != file.Sha256))
            {
                throw Changed();
            }
            hashes.Add((file.RelativePath, sha));
        }
        return hashes.Single().Sha256;
    }

    private Task<Stream> OpenSourceAsync(
        ImportPlan plan, PlannedFile file, ImportSource source, CancellationToken token) =>
        Task.FromResult(OpenSource(source));

    private static Stream OpenSource(ImportSource source)
    {
        try
        {
            return new FileStream(
                source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, useAsync: true);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw Missing();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw GuideImportValidator.Unreadable(source.FileName);
        }
    }

    private Task VerifyAsync(ImportManifest manifest, ImportPlan plan, string staged, CancellationToken token)
    {
        string primary = Path.Combine(staged, plan.PrimaryRelativePath);
        switch (manifest)
        {
            case TxtImportManifest txt:
                VerifyText(primary, txt.CodePage);
                break;
            case PdfImportManifest pdf:
                VerifyPdf(primary, pdf, token);
                break;
        }
        return Task.CompletedTask;
    }

    private static void VerifyText(string path, int? codePage)
    {
        try
        {
            TextGuideDocument.Decode(File.ReadAllBytes(path), codePage);
        }
        catch (EncodingSelectionRequiredException)
        {
            throw new GuideImportException(ImportIssue.Unreadable,
                "This text file isn't UTF-8. Save it as UTF-8 and import it again.");
        }
    }

    private static void VerifyPdf(string path, PdfImportManifest pdf, CancellationToken token)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ImportInspection staged = GuideImportValidator.ReadPdf(stream, pdf.Source, pdf.SuggestedTitle, token);
        if (staged is not ImportReady { Manifest: PdfImportManifest copy } || copy.PageCount != pdf.PageCount)
        {
            throw GuideImportValidator.NotPdf();
        }
    }

    /// <summary>The source must still have the size and write time the preview saw.</summary>
    private static void CheckSource(ImportSource source)
    {
        FileInfo file = new(source.FullPath);
        if (!file.Exists)
        {
            throw Missing();
        }
        if (file.Length != source.ByteCount || file.LastWriteTimeUtc != source.LastWriteUtc.UtcDateTime)
        {
            throw Changed();
        }
    }

    private static async Task<int> ReadAsync(Stream input, byte[] buffer, string name, CancellationToken token)
    {
        try
        {
            return await input.ReadAsync(buffer, token);
        }
        catch (IOException)
        {
            throw GuideImportValidator.Unreadable(name);
        }
    }

    private Stream Create(string path)
    {
        try
        {
            return createStagedFile(path);
        }
        catch (IOException error)
        {
            throw WriteFailed(error);
        }
    }

    private static async Task WriteAsync(Stream output, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        try
        {
            await output.WriteAsync(bytes, token);
        }
        catch (IOException error)
        {
            throw WriteFailed(error);
        }
    }

    private static void Flush(Stream output)
    {
        try
        {
            if (output is FileStream file)
            {
                file.Flush(flushToDisk: true);
            }
            else
            {
                output.Flush();
            }
        }
        catch (IOException error)
        {
            throw WriteFailed(error);
        }
    }

    // ERROR_HANDLE_DISK_FULL (39) and ERROR_DISK_FULL (112).
    private static GuideImportException WriteFailed(IOException error) =>
        (error.HResult & 0xFFFF) is 39 or 112
            ? new(ImportIssue.NotEnoughSpace, NoSpaceMessage)
            : new(ImportIssue.SaveFailed, SaveFailedMessage);

    private static GuideImportException Missing() => new(ImportIssue.Missing, MissingMessage);

    private static GuideImportException Changed() => new(ImportIssue.Changed, ChangedMessage);
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Stage, then run **Infrastructure tests** (full).
Expected: all pass. If `SourceGrownAfterPreviewIsChanged` or
`SourceRewrittenDuringCopyIsChanged` fails, check that `CheckSource`
compares `LastWriteTimeUtc` the way `GuideImportValidator` does (line 82).

- [ ] **Step 6: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs tests/DesktopGuides.Infrastructure.Tests/Import/PublisherHarness.cs tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportPublisherTests.cs
git commit -m "feat(p1): publish TXT and PDF guides into managed storage" \
  -m "GuideImportPublisher re-checks the source, stages a hashed and flushed copy, verifies it, renames it into content/<guide> and publishes the row. Cancellation, failures and crashes before the commit leave no guide, row or managed files, and the original is only read." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 4: HTML publication

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs`
- Modify: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportPublisherTests.cs`

**Interfaces:**
- Consumes: from Task 1, `HtmlImportManifest.Fingerprint`, `GuideFingerprint.OfHtml`, `GuideImportValidator.PreviewHtmlAsync` and `Html`; the existing `StaticHtmlImportPreview.CreateSource()`, `RootedStaticHtmlAssetSource.OpenReadAsync` (null when the file is gone) and `StaticHtmlImportValidator.VerifyStagedAsync`.
- Produces: no new surface. `PublishAsync` accepts `HtmlImportManifest`.

- [ ] **Step 1: Write the failing tests**

Add to `GuideImportPublisherTests`:

```csharp
    private const string HtmlStaticFingerprint = "743c87a4c5222c99cb5de3f4ee931a63b994a54be3dc9ec5855c855dafcd3f21";

    [Fact]
    public async Task PublishesStaticHtmlGuide()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string entry = CopyHtmlStatic(harness, "guide.html");
        IReadOnlyList<FileFingerprint> original = FileFingerprint.Of(harness.Sources.Root);

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));

        Guide guide = (await harness.Repository.GetGuideAsync(id))!;
        Assert.Equal(
            (GuideFormat.Html, "guide.html", HtmlStaticFingerprint, 722L, "guide.html"),
            (guide.Format, guide.PrimaryRelativePath, guide.ContentSha256, guide.ContentBytes, guide.SourceLabel));
        foreach (string file in new[] { "guide.html", "images/map.png", "styles/main.css", "styles/palette.css" })
        {
            Assert.Equal(
                File.ReadAllBytes(P0Fixtures.Resolve("html-static/" + file)),
                File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, file)));
        }
        Assert.Equal(0, harness.Count("FileOperations"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness.Paths.StagingRoot));
        Assert.Equal(original, FileFingerprint.Of(harness.Sources.Root));
    }

    [Fact]
    public async Task PercentNamedEntryPublishesAsGuideHtml()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string entry = CopyHtmlStatic(harness, "100% Walkthrough.html");

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));

        Guide guide = (await harness.Repository.GetGuideAsync(id))!;
        Assert.Equal(
            ("guide.html", "100% Walkthrough.html", HtmlStaticFingerprint),
            (guide.PrimaryRelativePath, guide.SourceLabel, guide.ContentSha256));
        Assert.True(File.Exists(harness.Paths.ResolveExistingGuideFile(id, "guide.html")));
    }

    [Fact]
    public async Task OnlyScannedFilesAreCopied()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string entry = CopyHtmlStatic(harness, "guide.html");
        harness.Sources.Write("notes.txt", "not referenced");

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));

        Assert.Equal(4, Directory.EnumerateFiles(harness.Paths.GetGuideRoot(id), "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task AssetEditedAfterPreviewIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string entry = CopyHtmlStatic(harness, "guide.html");
        ImportManifest manifest = await harness.InspectAsync(entry);
        harness.Sources.Write("styles/main.css", "body { color: red; }");

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task AssetEditedDuringCopyIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string entry = CopyHtmlStatic(harness, "guide.html");
        ImportManifest manifest = await harness.InspectAsync(entry);
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point == ImportCheckpoint.Copied) harness.Sources.Write("images/map.png", [1, 2, 3]);
        });

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        harness.AssertNothingLeft();
    }

    private static string CopyHtmlStatic(PublisherHarness harness, string entryName)
    {
        foreach (string file in new[] { "images/map.png", "styles/main.css", "styles/palette.css" })
        {
            harness.Sources.Copy("html-static/" + file, file);
        }
        return harness.Sources.Copy("html-static/guide.html", entryName);
    }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Stage, then run **Infrastructure tests** with `--filter "FullyQualifiedName~GuideImportPublisherTests"`.
Expected: the five new tests fail with `ArgumentException: Unsupported import manifest.`, and the Task 3 tests still pass.

- [ ] **Step 3: Implement**

In `GuideImportPublisher.cs`, replace `ImportPlan`, `PlanAsync` and
`OpenSourceAsync`:

```csharp
    private sealed record ImportPlan(
        IReadOnlyList<PlannedFile> Files, string PrimaryRelativePath, StaticHtmlImportPreview? Html = null)
    {
        public long TotalBytes => Files.Sum(file => file.ByteCount);
    }

    private async Task<ImportPlan> PlanAsync(ImportManifest manifest, CancellationToken token)
    {
        switch (manifest)
        {
            case TxtImportManifest:
                return Single("guide.txt", manifest.Source);
            case PdfImportManifest:
                return Single("guide.pdf", manifest.Source);
            case HtmlImportManifest html:
                // The manifest carries only counts, so scan again for the file list.
                StaticHtmlImportPreview preview = await validator.PreviewHtmlAsync(html.Source, token);
                IReadOnlyList<StaticAsset> assets = preview.Manifest.Assets;
                if (GuideFingerprint.OfHtml(assets.Select(asset => (asset.RelativePath, asset.Sha256))) != html.Fingerprint)
                {
                    throw Changed();
                }
                return new ImportPlan(
                    assets.Select(asset => new PlannedFile(asset.RelativePath, asset.ByteCount, asset.Sha256)).ToArray(),
                    preview.EntryRelativePath,
                    preview);
            default:
                throw new ArgumentException("Unsupported import manifest.", nameof(manifest));
        }
    }

    private static async Task<Stream> OpenSourceAsync(
        ImportPlan plan, PlannedFile file, ImportSource source, CancellationToken token)
    {
        if (plan.Html is null)
        {
            return OpenSource(source);
        }
        try
        {
            // Maps the managed name back to the source file, including a renamed entry.
            return await plan.Html.CreateSource().OpenReadAsync(file.RelativePath, token) ?? throw Changed();
        }
        catch (Exception error) when (error is StaticHtmlValidationException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw Changed();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw GuideImportValidator.Unreadable(source.FileName);
        }
    }
```

In `CopyAsync`, replace the last line:

```csharp
        return plan.Html is null ? hashes.Single().Sha256 : GuideFingerprint.OfHtml(hashes);
```

Replace `VerifyAsync`:

```csharp
    private async Task VerifyAsync(ImportManifest manifest, ImportPlan plan, string staged, CancellationToken token)
    {
        string primary = Path.Combine(staged, plan.PrimaryRelativePath);
        switch (manifest)
        {
            case TxtImportManifest txt:
                VerifyText(primary, txt.CodePage);
                break;
            case PdfImportManifest pdf:
                VerifyPdf(primary, pdf, token);
                break;
            case HtmlImportManifest:
                try
                {
                    // Re-hashes every source asset and every staged file.
                    await validator.Html.VerifyStagedAsync(plan.Html!, staged, token);
                }
                catch (StaticHtmlValidationException)
                {
                    throw Changed();
                }
                break;
        }
    }
```

- [ ] **Step 4: Run the tests and confirm they pass**

Stage, then run **Infrastructure tests** (full).
Expected: all pass.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportPublisherTests.cs
git commit -m "feat(p1): publish static HTML guides" \
  -m "The publisher re-scans the HTML source, rejects a fingerprint that differs from the preview, copies only the scanned files under their managed names, and verifies the stage and source before the rename. A '%'-named entry publishes as guide.html." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 5: Import button, progress and the Game page after import

No unit tests: Production has no test project, and the installed smoke in
Task 6 covers this task. TDD is skipped here, and that is called out. The
gate is a Release build of Production, then Task 6.

**Files:**
- Modify: `src/DesktopGuides.Production/ImportGuideDialog.xaml`
- Modify: `src/DesktopGuides.Production/ImportGuideDialog.xaml.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`

**Interfaces:**
- Consumes: from Task 3, `GuideImportPublisher(SqliteLibraryRepository, ILibraryPaths, GuideImportLimits? = null)` and `PublishAsync(ImportManifest, Guid, string, IProgress<ImportProgress>?, CancellationToken)`; from Task 1, `ImportProgress`.
- Produces: `ImportGuideDialog(string gameTitle, string path, IGuideImportValidator validator, Func<Task<string?>> pickFile, Func<ImportManifest, string, IProgress<ImportProgress>, CancellationToken, Task<Guid>> import)` and `internal Guid? ImportedGuideId`. Automation IDs `PrimaryButton` (the WinUI default), `ImportCopyProgress`.

- [ ] **Step 1: Dialog XAML**

On the root `ContentDialog`, add `PrimaryButtonText="Import"` and
`IsPrimaryButtonEnabled="False"`, and change `DefaultButton="None"` to
`DefaultButton="Primary"`.

After `ImportBusyText`, add:

```xml
                <ProgressBar x:Name="ImportCopyProgress"
                             Width="120"
                             Minimum="0"
                             Maximum="1"
                             Visibility="Collapsed"
                             VerticalAlignment="Center"
                             AutomationProperties.Name="Import progress"
                             AutomationProperties.AutomationId="ImportCopyProgress" />
```

- [ ] **Step 2: Dialog code**

In `ImportGuideDialog.xaml.cs`:

Add constants and fields:

```csharp
    private const string ImportFailedMessage = "The guide couldn't be imported. Try again.";
    private readonly Func<ImportManifest, string, IProgress<ImportProgress>, CancellationToken, Task<Guid>> import;
    private ImportManifest? manifest;
    private bool importing;
```

Replace the constructor's signature and add the new wiring:

```csharp
    internal ImportGuideDialog(
        string gameTitle, string path, IGuideImportValidator validator, Func<Task<string?>> pickFile,
        Func<ImportManifest, string, IProgress<ImportProgress>, CancellationToken, Task<Guid>> import)
    {
        InitializeComponent();
        Title = $"Import guide for {gameTitle}";
        this.validator = validator;
        this.pickFile = pickFile;
        this.import = import;
        firstPath = path;
        Opened += DialogOpened;
        Closing += DialogClosing;
        PrimaryButtonClick += ImportClicked;
        SecondaryButtonClick += ChooseAnotherClicked;
    }
```

Replace the `Manifest` property:

```csharp
    /// <summary>The checked file, once the preview is complete.</summary>
    internal ImportManifest? Manifest
    {
        get => manifest;
        private set
        {
            manifest = value;
            UpdateImportButton();
        }
    }

    /// <summary>The published guide. Hide() makes ShowAsync return None, so the shell checks this.</summary>
    internal Guid? ImportedGuideId { get; private set; }
```

Add, after `ChooseAnotherClicked`:

```csharp
    private void ImportClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (importing || closing || Manifest is not { } ready ||
            !GuideTitle.TryCreate(GuideTitleInput.Text, out string title))
        {
            return;
        }
        Track(ImportAsync(ready, title));
    }

    private async Task ImportAsync(ImportManifest ready, string title)
    {
        using CancellationTokenSource cancel = new();
        check = cancel;
        ShowImporting(ready.Source.FileName);
        try
        {
            ImportedGuideId = await import(ready, title, new Progress<ImportProgress>(ShowImportProgress), cancel.Token);
            if (!closing)
            {
                Hide();
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // Back to the preview, with the title and encoding unchanged.
        }
        catch (GuideImportException error)
        {
            if (!closing)
            {
                ShowMessage(InfoBarSeverity.Error, error.Message);
            }
        }
        catch (Exception)
        {
            if (!closing)
            {
                ShowMessage(InfoBarSeverity.Error, ImportFailedMessage);
            }
        }
        finally
        {
            if (ReferenceEquals(check, cancel))
            {
                check = null;
            }
            if (!closing && ImportedGuideId is null)
            {
                HideImporting();
            }
        }
    }

    private void ShowImporting(string name)
    {
        importing = true;
        ImportStatus.IsOpen = false;
        GuideTitleInput.IsEnabled = false;
        EncodingOptions.IsEnabled = false;
        IsSecondaryButtonEnabled = false;
        ImportCancel.IsEnabled = true;
        ImportBusyText.Text = $"Importing {name}…";
        ImportProgress.Visibility = Visibility.Collapsed;
        ImportCopyProgress.Value = 0;
        ImportCopyProgress.Visibility = Visibility.Visible;
        ImportBusy.Visibility = Visibility.Visible;
        UpdateImportButton();
    }

    private void HideImporting()
    {
        importing = false;
        ImportBusy.Visibility = Visibility.Collapsed;
        ImportCopyProgress.Visibility = Visibility.Collapsed;
        ImportProgress.Visibility = Visibility.Visible;
        ImportCancel.IsEnabled = true;
        GuideTitleInput.IsEnabled = true;
        EncodingOptions.IsEnabled = true;
        IsSecondaryButtonEnabled = !picking;
        UpdateImportButton();
    }

    private void ShowImportProgress(ImportProgress value)
    {
        // Progress posts to the UI thread, so a late report can arrive after the import ends.
        if (!importing)
        {
            return;
        }
        ImportCopyProgress.Value = value.Fraction;
        if (value.Publishing)
        {
            ImportCancel.IsEnabled = false;
            ImportBusyText.Text = "Saving to your library…";
        }
    }

    private void UpdateImportButton() =>
        IsPrimaryButtonEnabled = !importing && manifest is not null &&
            GuideTitle.TryCreate(GuideTitleInput.Text, out _);
```

In `ChooseAnotherAsync`, change the guard to
`if (picking || closing || importing)`, and in its `finally` change
`if (!closing)` to `if (!closing && !importing)`.

Split `ShowStatus` so an import error keeps the preview:

```csharp
    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        Manifest = null;
        needsEncoding = null;
        ImportPreview.Visibility = Visibility.Collapsed;
        ShowMessage(severity, message);
    }

    private void ShowMessage(InfoBarSeverity severity, string message)
    {
        ImportStatus.Severity = severity;
        ImportStatus.Message = message;
        AutomationProperties.SetName(ImportStatus, message);
        ImportStatus.IsOpen = true;
    }
```

At the end of `ValidateTitle`, add `UpdateImportButton();`.

- [ ] **Step 3: Shell wiring**

In `ShellWindow.xaml.cs`:

Add the field after `importValidator`:

```csharp
    private GuideImportPublisher? guidePublisher;
```

After `await repository.InitializeAsync();` in initialization, add:

```csharp
            guidePublisher = new GuideImportPublisher(repository, paths);
```

In `ImportGuideClicked`, declare `bool imported = false;` before the outer
`try`. Replace the dialog construction and the `try`/`finally` around
`ShowAsync` with:

```csharp
                GuideImportPublisher publisher = guidePublisher
                    ?? throw new InvalidOperationException("The library is not ready.");
                ImportGuideDialog dialog = new(
                    game.Title, path, importValidator, PickGuideFileAsync,
                    (manifest, title, progress, token) =>
                        publisher.PublishAsync(manifest, game.Id, title, progress, token))
                {
                    XamlRoot = Navigation.XamlRoot
                };
                DialogSurface.Apply(dialog, EffectiveMaterial);
                activeImportDialog = dialog;
                try
                {
                    await dialog.ShowAsync();
                }
                finally
                {
                    activeImportDialog = null;
                }
                // An import that reached publication is kept even if the dialog was closed.
                if (dialog.ImportedGuideId is Guid guideId && !closeRequested &&
                    navigator.Current is GameRoute shown && shown.GameId == route.GameId)
                {
                    imported = true;
                    pendingGuideFocus = guideId;
                    await RenderCurrentAsync();
                }
```

In the outer `finally`, focus the button only when nothing was imported:

```csharp
            if (!closeRequested && navigator.Current is GameRoute)
            {
                ImportGuideButton.IsEnabled = true;
                if (!imported)
                {
                    ImportGuideButton.Focus(FocusState.Programmatic);
                }
            }
```

Add `using DesktopGuides.Infrastructure.Import;` if it isn't already there.

- [ ] **Step 4: Build**

Stage, then run the **Production build** host command.
Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Production/ImportGuideDialog.xaml src/DesktopGuides.Production/ImportGuideDialog.xaml.cs src/DesktopGuides.Production/ShellWindow.xaml.cs
git commit -m "feat(p1): import from the dialog and select the new guide" \
  -m "The import dialog gains an Import button and a copy progress bar. Cancel works until publication starts, a failure keeps the preview, and success closes the dialog. The Game page then re-renders with the new guide selected and focused." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 6: Installed smoke `import-publish`

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`
- Modify: `tools/p1/windows_shell_install.ps1`
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs`

**Interfaces:**
- Consumes: from Task 5, automation IDs `PrimaryButton`, `ImportGuideDialog`, `GuideTitleInput`, `ImportEncodingCp437` and `ImportEncodingValue`; the existing helpers `Wait-SelectedGuide` and `Wait-FocusedGuide`.
- Produces: smoke mode `import-publish`; the `describe-import` field `LegacyTextGuides`.

- [ ] **Step 1: Smoke scenario**

In `windows_shell_ui_smoke.ps1`:

1. Change `'import-preview', 'provider-live', 'provider-remove')]` in the
   `Mode` `ValidateSet` to
   `'import-preview', 'import-publish', 'provider-live', 'provider-remove')]`.
2. Change `elseif ($Mode -eq 'import-preview') {` to
   `elseif ($Mode -like 'import-*') {`. Leave the shared helpers
   (`Choose-PickerFile` through `Wait-FocusedId`) where they are.
3. Wrap the existing scenario, from `Select-Element 'Import Test Game'`
   through `$report.phases += 'import-picker-cancel'`, in
   `if ($Mode -eq 'import-preview') {` … `}`, then add:

```powershell
        else {
            $importTitle = 'Imported Guide ' + [guid]::NewGuid().ToString('N').Substring(0, 8)
            Select-Element 'Import Test Game'
            [void](Wait-Name 'GameHeading' 'Import Test Game')
            [void](Wait-Status 'Game ready.')
            Click-Element (Wait-EnabledById 'ImportGuideButton')
            Choose-PickerFile 'txt-legacy.txt'
            [void](Wait-VisibleById 'ImportGuideDialog')
            [void](Wait-PresentById 'ImportEncodingCp437')
            if ((Wait-VisibleById 'PrimaryButton').Current.IsEnabled) {
                throw 'Import was enabled before an encoding was chosen.'
            }
            [void](Select-ById 'ImportEncodingCp437')
            [void](Wait-Text 'ImportEncodingValue' 'DOS (CP437)')
            Set-Text 'GuideTitleInput' $importTitle
            [void](Wait-EnabledById 'PrimaryButton')
            $report.importReadyScreenshot = Save-WindowScreenshot 'import-ready'
            $report.phases += 'import-ready'

            Invoke-Element (Wait-EnabledById 'PrimaryButton')
            [void](Wait-HiddenById 'ImportGuideDialog')
            [void](Wait-SelectedGuide $importTitle)
            Wait-FocusedGuide $importTitle
            $report.importedTitle = $importTitle
            $report.importPublishedScreenshot = Save-WindowScreenshot 'import-published'
            $report.phases += 'import-published'
        }
```

4. In the failure diagnostics, change `$Mode -eq 'import-preview'` to
   `$Mode -like 'import-*'`.

- [ ] **Step 2: Install script**

In `windows_shell_install.ps1`:

1. In `Run-ShellSmoke`'s timeout, change `$mode -eq 'import-preview'` to
   `$mode -like 'import-*'`. It stays at 120 seconds.
2. Replace `Run-ImportScenarios` with:

```powershell
function Run-ImportScenarios {
    Invoke-ShellSeed @('seed-import', $dataRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.importLight = Run-ShellSmoke 'import-preview' -ResultName 'import-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.importDark = Run-ShellSmoke 'import-preview' -ResultName 'import-dark'
        Close-InstalledShell

        $state = Invoke-ShellSeed @('describe-import', $dataRoot) | ConvertFrom-Json
        $report.importPreviewState = $state
        foreach ($name in @('Guides', 'FileOperations', 'StagingEntries', 'ContentEntries')) {
            if ($state.$name -ne 0) {
                throw "The import preview left $($state.$name) $name; expected none."
            }
        }

        Set-AppThemePreference $true
        Start-InstalledShell
        $report.importPublishLight = Run-ShellSmoke 'import-publish' -ResultName 'import-publish-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.importPublishDark = Run-ShellSmoke 'import-publish' -ResultName 'import-publish-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
    $state = Invoke-ShellSeed @('describe-import', $dataRoot) | ConvertFrom-Json
    $report.importState = $state
    $expected = [ordered]@{
        Guides = 2; FileOperations = 0; StagingEntries = 0; ContentEntries = 2; LegacyTextGuides = 2
    }
    foreach ($name in $expected.Keys) {
        if ($state.$name -ne $expected[$name]) {
            throw "After two imports, $name was $($state.$name); expected $($expected[$name])."
        }
    }
}
```

- [ ] **Step 3: Seed state**

In `DesktopGuides.ShellSeed/Program.cs`, in `describe-import`, replace
`CountRows` with a scalar helper and add `LegacyTextGuides`:

```csharp
    long Scalar(string sql)
    {
        using SqliteCommand command = importConnection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
```

```csharp
        Guides = Scalar("SELECT COUNT(*) FROM Guides"),
        FileOperations = Scalar("SELECT COUNT(*) FROM FileOperations"),
        StagingEntries = CountEntries(importPaths.StagingRoot),
        ContentEntries = CountEntries(importPaths.ContentRoot),
        LegacyTextGuides = Scalar("SELECT COUNT(*) FROM Guides WHERE Format = 'Txt' AND TextCodePage = 437"),
```

- [ ] **Step 4: Static checks**

```bash
perl -ne 'print "$ARGV:$.: non-ASCII\n" if /[^\x00-\x7F]/; close ARGV if eof' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
```

Expected: no output.

Stage, then run the **Seed build** host command, and parse both scripts on
the host:

```bash
s 'powershell -NoProfile -Command "foreach ($f in @(''E:\work\desktop-guides\t06-3\tools\p1\windows_shell_ui_smoke.ps1'',''E:\work\desktop-guides\t06-3\tools\p1\windows_shell_install.ps1'')) { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$e); if ($e) { $e; exit 1 } }; ''parsed''"'
```

Expected: the seed builds with 0 warnings, and the parse prints `parsed`.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1 tools/p1/DesktopGuides.ShellSeed/Program.cs
git commit -m "test(p1): smoke-test publishing an imported guide" \
  -m "The import-publish mode picks the legacy TXT fixture, chooses CP437, sets a unique title and presses Import, then waits for the new guide to be selected and focused. It runs in light and dark, and the final state must hold two CP437 guides with no file operation or staging residue." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 6: Installed verification**

This needs a pushed branch, so ask the user first. After approval, push
and let CI's `production-shell-ui` job run. It is the gate of record.
Alternatively, with a CI-built x64 MSIX already on the host, run
`windows_shell_install.ps1 -PackagePath <msix> -ResultDirectory E:\work\desktop-guides\t06-3-results -ImportOnly`
through an interactive scheduled task, following
`docs/p1/e2e-testing.md` (back up and restore the app data and
`%LOCALAPPDATA%\DesktopGuides\P0-WebView`).
Expected: `success: true`, phases `import-ready` and `import-published` in
both publish results, and `importState` matching the expected counts.
Copy the four screenshots (`import-publish-<theme>.import-ready.png` and
`import-publish-<theme>.import-published.png`) to
`docs/p1/evidence/t06-3-import-publication/` as `import-ready-light.png`,
`import-published-light.png`, `import-ready-dark.png` and
`import-published-dark.png`.

---

### Task 7: Documentation and verification record

**Files:**
- Modify: `docs/p1/t06-3-import-publication-design.md` (status line, `SourceLabel` row, new verification record)
- Modify: `docs/p1/implementation-plan.md` (T06.3 paragraph after the T06.1 + T06.2 paragraph, ~line 622)
- Modify: `docs/progress.md` (date line, new T06.3 row after line 18)
- Modify: `docs/p1/e2e-testing.md` (new `Import publication` checklist row after `Import preview`)
- Create: `docs/p1/evidence/t06-3-import-publication/*.png` (from Task 6 Step 6)

TDD skip: documentation only. Fill each `<…>` with the value observed in
Task 6 Step 6 (CI run ID, pass counts); do not commit a placeholder.

- [ ] **Step 1: Align the design with the Rulings and set its status**

The design must describe what shipped. In `docs/p1/t06-3-import-publication-design.md`,
edit the text each ruling contradicts: the `PublishAsync` signature (public,
`string` title, `IProgress<ImportProgress>`), the synchronous journal without
`AbandonAsync`, the rollback description, `MidCopy` → `Copied` (Testing seams
and tests 3 and 8), the step 4 re-check order, "closes with
`ContentDialogResult.Primary`" → "sets `ImportedGuideId` and hides", the
"Logs carry only…" sentence (no logging; messages still omit paths), and the
`SourceLabel` row. For the last, replace

```text
| `SourceLabel` | Original file name, truncated to 255 characters |
```

with

```text
| `SourceLabel` | The original file name, not truncated (NTFS caps names at 255 UTF-16 units) |
```

and replace the status line with

```text
Status: implemented and verified <date> on `feat/p1-t06-3-import-publication`;
see the [verification record](#t063-verification-record).
```

- [ ] **Step 2: Append the verification record to the design**

```markdown
## T06.3 verification record

- **Unit tests.** `core-tests` in CI run <run>: <n> Core and <n>
  Infrastructure passes, including `GuideImportPublisherTests` (TXT, PDF and
  HTML publication, source changes, cancellation, injected faults, crash
  points, rollback failure and the write gate), `ImportJournalTests` and
  `GuideFingerprintTests`.
- **Installed import scenario.** `production-shell-ui` in the same run, light
  and dark: Import disabled until CP437 is chosen for `txt-legacy`; Import
  closes the dialog; the Game page lists the new guide selected and focused.
  The preview runs still end with no guide or file operation, and after both
  publish runs `importState` shows 2 guides, 0 file operations, 0 staging
  entries, 2 content directories and 2 guides with code page 437.
- **Rulings.** The plan's Rulings section, plus any `Ruling:` lines from
  the execution ledger, one line each.
- **Evidence.** [import-ready (light)](../evidence/t06-3-import-publication/import-ready-light.png),
  [import-published (light)](../evidence/t06-3-import-publication/import-published-light.png),
  and the matching dark screenshots.
```

- [ ] **Step 3: Record T06.3 in the P1 implementation plan**

After the T06.1 + T06.2 paragraph in `docs/p1/implementation-plan.md`
(ending "Confirm, copying and publication remain T06.3."), add:

```markdown
The [T06.3 import publication design](t06-3-import-publication-design.md)
and [plan](t06-3-import-publication-plan.md) add the Import button,
`GuideImportPublisher` and the prepared-journal copy, verify, rename and
publish sequence for TXT, HTML and PDF. They were implemented and verified
on <date>: CI run <run> passed `core-tests`, both package builds and the
installed `import-publish` scenario in light and dark. Duplicate handling
remains T06.4.
```

- [ ] **Step 4: Update progress**

In `docs/progress.md`, set the date line to `Updated <date>.` and add after
the T06.1 + T06.2 row:

```markdown
| P1 T06.3 import publication | Implemented on `feat/p1-t06-3-import-publication`; PR pending. | Import publishes TXT, HTML and PDF guides into managed storage with a fingerprint, surviving removal of the original; cancellation, failures and crashes leave no guide or residue. CI run <run> passed the installed `import-publish` scenario in light and dark; see the [verification record](p1/t06-3-import-publication-design.md#t063-verification-record). Duplicate handling is T06.4. |
```

Also change "T06.3 adds Confirm and publication." in the T06.1 + T06.2 row to
"T06.3 adds publication."

- [ ] **Step 5: Add the E2E checklist row**

In `docs/p1/e2e-testing.md`, after the `Import preview` row:

```markdown
| Import publication | From a seeded game, pick `txt-legacy`, confirm Import is disabled until CP437 is chosen, enter a unique title and press Import. In light and dark: the dialog closes and the Game page lists the new guide selected and focused. Afterwards the library holds one guide per run with code page 437, a content directory per guide, and no file operation or staging entry. | T06.3, TR06.2, TR06.3, TR11.3 |
```

- [ ] **Step 6: Check and commit**

Run: `git diff --check`
Expected: no output.

```bash
git add docs/p1/t06-3-import-publication-design.md docs/p1/implementation-plan.md \
  docs/progress.md docs/p1/e2e-testing.md docs/p1/evidence/t06-3-import-publication
git commit -m "docs(p1): record T06.3 import publication verification"
```

---

## Traceability

| Requirement | Evidence |
| --- | --- |
| TR06.1 — the original is never written | Task 3–4 happy-path tests compare `FileFingerprint` before and after; source-change tests (`SourceRemovedBeforeCopyIsMissing`, `SourceGrownAfterPreviewIsChanged`, `SourceRewrittenDuringCopyIsChanged`, `LockedSourceIsUnreadable`, `AssetEditedAfterPreviewIsChanged`, `AssetEditedDuringCopyIsChanged`); the cancellation tests. |
| TR06.2 — publication only after validation and staging; failures remove managed files | `CancellationBeforeCommitRollsBack`, `CancellationFromProgressRollsBack`, `CancellationInsideTheCommitIsIgnored`, `FullDiskIsNotEnoughSpace`, `ExistingContentDirectoryIsSaveFailedAndKept`, `FailureInsideTheCommitIsSaveFailed`, `GameDeletedBeforeCommitIsSaveFailed`, `CrashIsRolledBackAtStartup`, `FailedRollBackStillReportsTheOriginalIssue`, `ImportJournalTests`; the smoke's zero-residue `importState`. |
| TR06.3 — fingerprints and IDs recorded | `PublishesUtf8TextGuide`, `PublishesLegacyTextWithTheChosenCodePage`, `PublishesPdfGuide`, `PublishesStaticHtmlGuide` (pinned fingerprint), `GuideSurvivesDeletingTheOriginal`, `GuideFingerprintTests`; the smoke's selected new guide. |
| TR11.3 — keyboard and UIA | The smoke drives Import by UIA and asserts the new guide is focused. |

## PR outcome

Open the PR only with the user's approval, through the GitHub MCP, against
`main`. Body:

```markdown
**Target task:** T06.3 — staged import publication.

**Prerequisites:** T03.2, T03.3, T15.2 (PRs #3–#5) and T06.1 + T06.2
(PR #17), all merged.

**Outcome:** Pressing **Import** publishes a TXT, HTML or PDF guide into
managed storage with a recorded fingerprint. The guide survives removal of
the original. Cancellation, failure and crash leave no partial guide and
no staged or managed residue. Duplicates import as a second copy until
T06.4.

**Verification:** CI run <run>: `core-tests` (<n> Core, <n>
Infrastructure), both package builds and the installed `import-publish`
scenario in light and dark. See the [verification record](docs/p1/t06-3-import-publication-design.md#t063-verification-record).

| | Light | Dark |
| --- | --- | --- |
| Ready to import | ![](docs/p1/evidence/t06-3-import-publication/import-ready-light.png) | ![](docs/p1/evidence/t06-3-import-publication/import-ready-dark.png) |
| After import | ![](docs/p1/evidence/t06-3-import-publication/import-published-light.png) | ![](docs/p1/evidence/t06-3-import-publication/import-published-dark.png) |

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

Use absolute `raw.githubusercontent.com` URLs on the branch for the images
if relative paths don't render in the PR body.
