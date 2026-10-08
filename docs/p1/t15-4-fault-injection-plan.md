# T15.4 Fault-Injection Matrix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every import and removal phase gets a fault-injection cell that
compares exact database rows and app-owned files before and after. The cells
cover in-process failure, crash then restart, cancellation, failed rollback,
and the NTFS link and name cases. Small defects the matrix finds are fixed.

**Architecture:**
- **Snapshot:** a test helper, `LibrarySnapshot`, captures every row of the
  seven library tables plus the whole data-root tree, and diffs two
  snapshots.
- **Fixture:** `FaultFixture` builds on T20.1's `ExportFixture` and adds a
  bystander game, an HTML import source and factories for publishers and
  removers with hooks.
- **Seams:** production code gains only test seams: new checkpoints and
  `rollBack`/`finish` hooks in both removers.
- **Fixes:** two small defects: a raw exception from a junctioned guide
  folder, and an artwork delete that follows a link.

**Tech Stack:** .NET 10, Microsoft.Data.Sqlite, xUnit.

**Spec:** `docs/p1/t15-4-fault-injection-design.md`

**Target:** T15.4. **Prerequisites:** T04.3 (PR #26), T06.3 (PR #19), T15.2
(PR #5) and T15.3 (PR #22), all merged.

## Global Constraints

- No change to any protocol's steps or to the journal format.
  `FileOperations.Kind` stays `Import`, `DeleteGuide` or `DeleteGame`.
- Seams are `internal`, and their defaults keep today's behavior exactly.
- New checkpoint values, verbatim:
  - `ImportCheckpoint.MovedToContent` and `ImportCheckpoint.Published`;
  - `RemovalCheckpoint.TrashCreated` and `RemovalCheckpoint.BeforeArtworkDelete`.
- The injected fault type is `InjectedFault : Exception`, defined in the test
  project.
- Snapshot tables, verbatim: `Games`, `Guides`, `GuideAssets`,
  `ReadingStates`, `ReaderPreferences`, `Settings`, `FileOperations`.
- Snapshot keys:
  - rows: `db:<Table>/<key>`;
  - files: `fs:<data-root-relative path with forward slashes>`.

  `library/library.sqlite` and its `-wal`, `-shm` and `-journal` files are
  skipped.
- NTFS cells return early unless `OperatingSystem.IsWindows()`, as the
  existing tests do.
- Fixes need a cell that fails first. A cell that passes on today's code is
  characterization and is listed in the verification record.

## Rulings against the spec

- **The held-content fix.** The spec's defect 3 says "Hold the guide that
  moves first". To exercise the partial-move rollback, the held file must
  be in the guide that moves second: the first guide moves, and then the
  second move fails. Task 7 holds the second guide in move order.
- **The import subject.** The spec describes a three-file HTML guide. The
  matrix uses the existing `html-static` P0 fixture, which has four files
  (`guide.html`, `images/map.png`, `styles/main.css`, `styles/palette.css`).
- **The game commit failure.** The spec's "existing trigger" blocks
  `FileOperations` inserts, which is a `Prepare` failure. The real
  commit-failure cell uses a `BEFORE DELETE ON Games` trigger instead.
- **`Published`.** To keep a throw at this checkpoint from rolling back a
  committed import, it fires after the publish `try` block, not inside it.
  Its throw cell therefore expects the fault to escape with the guide
  published.

## Review Focus

1. **A bystander guide in the same game.** Removing one guide must leave its
   sibling's rows, files and preferences exactly as they were. The exact
   diff in every guide-removal cell checks this, because the sibling HTML
   guide is in the subject game (Task 4).
2. **`LastActiveGuideId` points at the removed guide.** The setting must be
   gone after a commit and back after a rollback. The fixture sets it to the
   subject guide, and the exact diffs check both outcomes (Task 4).
3. **A restart after a crash in a game with two guides, one already
   moved.** Recovery must move the moved guide back and leave the unmoved
   one alone. Cell `Crash` at `MovedGuide` (Task 5).
4. **The artwork sweep after a commit.** It must delete only the removed
   game's file, not the bystander's. Cell `BeforeArtworkDelete` (Task 5).
5. **Cancellation arriving after a removal is journaled.** It must not
   abandon the operation. Cell `CancelAfterPrepared` (Tasks 4 and 5).

## Host commands

The Mac has no dotnet, so every build and test runs on `pcsx2-win`. Stage
the working tree as a zip, which keeps non-ASCII names. Include the
gitignored generated P0 fixtures, because the PDF tests read them:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  python3 - <<'PY'
import glob, subprocess, zipfile
files = subprocess.run(['git', 'ls-files', '-co', '--exclude-standard', '-z'],
                       capture_output=True, check=True).stdout.decode().split('\0')
files += glob.glob('tests/fixtures/p0/generated/*')
with zipfile.ZipFile('/tmp/claude/t15-4.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    for f in files:
        if f and not f.startswith(('.claude/', '.superpowers/')):
            z.write(f)
PY
  scp -q /tmp/claude/t15-4.zip pcsx2-win:E:/work/desktop-guides/t15-4.zip
  s "powershell -NoProfile -Command \"\$d = 'E:\\work\\desktop-guides\\t15-4'; if (Test-Path \$d) { Remove-Item -Recurse -Force \$d }; Expand-Archive E:\\work\\desktop-guides\\t15-4.zip \$d\""
}
infra(){ s "cd /d E:\\work\\desktop-guides\\t15-4 && dotnet test tests\\DesktopGuides.Infrastructure.Tests\\DesktopGuides.Infrastructure.Tests.csproj -c Release $*"; }
core(){ s "cd /d E:\\work\\desktop-guides\\t15-4 && dotnet test tests\\DesktopGuides.Core.Tests\\DesktopGuides.Core.Tests.csproj -c Release $*"; }
```

Put these in a script file and call it, rather than passing them to
`bash -c`. A `--filter` takes one expression without `|`, because `cmd` on the
host would read the pipe.

---

### Task 1: Library snapshot and diff

**Files:**
- Create: `tests/DesktopGuides.Infrastructure.Tests/FaultInjection/LibrarySnapshot.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/FaultInjection/LibrarySnapshotTests.cs`

**Interfaces:**
- Consumes:
  - `ManagedPathResolver` (`DataRoot`, `DatabasePath`);
  - T20.1's `ExportFixture` (`Library`, `TxtGuide`, `Content(Guid)`) and
    `RemovalLibrary` (`Execute`, `CreateJunction`).
- Produces:
  - `internal sealed class LibrarySnapshot` with
    `static LibrarySnapshot Capture(ManagedPathResolver paths)`,
    `IReadOnlyDictionary<string, string> Entries`,
    `IEnumerable<string> KeysContaining(Guid id)` and
    `static SnapshotDiff Diff(LibrarySnapshot before, LibrarySnapshot after)`;
  - `internal sealed record SnapshotDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Changed)`
    with `bool IsEmpty`;
  - `internal static class SnapshotAssert` with
    `Unchanged(LibrarySnapshot before, LibrarySnapshot after)` and
    `Exactly(SnapshotDiff actual, IEnumerable<string> added, IEnumerable<string> removed, IEnumerable<string>? changed = null)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DesktopGuides.Infrastructure.Tests.FaultInjection;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class LibrarySnapshotTests : IAsyncLifetime
{
    private ExportFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await ExportFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private LibrarySnapshot Capture() => LibrarySnapshot.Capture(fixture.Library.Paths);

    [Fact]
    public void TwoCapturesOfAnUnchangedLibraryAreEqual()
    {
        LibrarySnapshot first = Capture();

        SnapshotAssert.Unchanged(first, Capture());
        Assert.Contains($"db:Guides/{fixture.TxtGuide:N}", first.Entries.Keys);
        Assert.Contains($"db:GuideAssets/{fixture.HtmlGuide:N}/guide.html", first.Entries.Keys);
        Assert.Contains($"fs:library/content/{fixture.TxtGuide:N}/guide.txt", first.Entries.Keys);
        Assert.Contains("db:Settings/Theme", first.Entries.Keys);
    }

    [Fact]
    public void ARowChangeIsReportedByItsKey()
    {
        LibrarySnapshot before = Capture();
        fixture.Library.Execute($"UPDATE ReadingStates SET EstimatedFraction = 0.75 WHERE GuideId = '{fixture.TxtGuide:N}'");

        SnapshotDiff diff = LibrarySnapshot.Diff(before, Capture());

        SnapshotAssert.Exactly(diff, [], [], [$"db:ReadingStates/{fixture.TxtGuide:N}"]);
    }

    [Fact]
    public void FilesAndEmptyFoldersAreKeyedByRelativePath()
    {
        LibrarySnapshot before = Capture();
        string content = fixture.Content(fixture.TxtGuide);
        File.WriteAllText(Path.Combine(content, "guide.txt"), "changed");
        Directory.CreateDirectory(Path.Combine(fixture.Library.Paths.StagingRoot, "empty"));
        File.Delete(Path.Combine(fixture.Content(fixture.PdfGuide), "manual.pdf"));

        SnapshotDiff diff = LibrarySnapshot.Diff(before, Capture());

        SnapshotAssert.Exactly(diff,
            ["fs:library/.staging/empty"],
            [$"fs:library/content/{fixture.PdfGuide:N}/manual.pdf"],
            [$"fs:library/content/{fixture.TxtGuide:N}/guide.txt"]);
    }

    [Fact]
    public void TheDatabaseFilesAreNotPartOfTheTree()
    {
        Assert.DoesNotContain(Capture().Entries.Keys, key => key.StartsWith("fs:library/library.sqlite", StringComparison.Ordinal));
    }

    [Fact]
    public void ALinkIsRecordedAndNotFollowed()
    {
        if (!OperatingSystem.IsWindows()) return;
        string outside = Path.Combine(fixture.Library.Root, "outside");
        RemovalLibrary.WriteFile(outside, "secret.txt", "secret");
        string link = Path.Combine(fixture.Library.Paths.StagingRoot, "linked");
        RemovalLibrary.CreateJunction(link, outside);
        try
        {
            LibrarySnapshot snapshot = Capture();

            Assert.Equal("link", snapshot.Entries["fs:library/.staging/linked"]);
            Assert.DoesNotContain("fs:library/.staging/linked/secret.txt", snapshot.Entries.Keys);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public void KeysContainingFindsEveryKeyOfAGuide()
    {
        string[] keys = Capture().KeysContaining(fixture.TxtGuide).ToArray();

        Assert.Contains($"db:Guides/{fixture.TxtGuide:N}", keys);
        Assert.Contains($"db:ReadingStates/{fixture.TxtGuide:N}", keys);
        Assert.Contains($"db:ReaderPreferences/{fixture.TxtGuide:N}", keys);
        Assert.Contains($"fs:library/content/{fixture.TxtGuide:N}", keys);
        Assert.Contains($"fs:library/content/{fixture.TxtGuide:N}/guide.txt", keys);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `infra --filter FullyQualifiedName~LibrarySnapshotTests`.
Expected: a build failure, `The type or namespace name 'LibrarySnapshot' could not be found`.

- [ ] **Step 3: Write the snapshot**

```csharp
using System.Globalization;
using System.Security.Cryptography;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

/// <summary>
/// Every row of the library tables and every entry under the data root,
/// keyed so two captures can be diffed. Links are recorded, not followed.
/// </summary>
internal sealed class LibrarySnapshot
{
    private static readonly (string Table, string[] Key)[] Tables =
    [
        ("Games", ["Id"]),
        ("Guides", ["Id"]),
        ("GuideAssets", ["GuideId", "RequestPath"]),
        ("ReadingStates", ["GuideId"]),
        ("ReaderPreferences", ["GuideId"]),
        ("Settings", ["Key"]),
        ("FileOperations", ["Id"]),
    ];

    private LibrarySnapshot(IReadOnlyDictionary<string, string> entries) => Entries = entries;

    public IReadOnlyDictionary<string, string> Entries { get; }

    public static LibrarySnapshot Capture(ManagedPathResolver paths)
    {
        SortedDictionary<string, string> entries = new(StringComparer.Ordinal);
        using (SqliteConnection connection = new($"Data Source={paths.DatabasePath};Pooling=False"))
        {
            connection.Open();
            foreach ((string table, string[] key) in Tables)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM {table}";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string id = string.Join("/", key.Select(column => Convert.ToString(reader[column], CultureInfo.InvariantCulture)));
                    entries[$"db:{table}/{id}"] = string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index =>
                        $"{reader.GetName(index)}={(reader.IsDBNull(index) ? "null" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))}"));
                }
            }
        }
        // The \\?\ prefix lets reserved names such as CON be listed and read.
        string prefix = OperatingSystem.IsWindows() ? @"\\?\" : "";
        string root = prefix + Path.GetFullPath(paths.DataRoot);
        Walk(root, root, prefix + Path.GetFullPath(paths.DatabasePath), entries);
        return new LibrarySnapshot(entries);
    }

    public IEnumerable<string> KeysContaining(Guid id) =>
        Entries.Keys.Where(key => key.Contains(id.ToString("N"), StringComparison.Ordinal));

    public static SnapshotDiff Diff(LibrarySnapshot before, LibrarySnapshot after) => new(
        after.Entries.Keys.Except(before.Entries.Keys).Order(StringComparer.Ordinal).ToArray(),
        before.Entries.Keys.Except(after.Entries.Keys).Order(StringComparer.Ordinal).ToArray(),
        before.Entries.Keys.Intersect(after.Entries.Keys)
            .Where(key => before.Entries[key] != after.Entries[key]).Order(StringComparer.Ordinal).ToArray());

    private static void Walk(string root, string directory, string database, SortedDictionary<string, string> entries)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            if (path.StartsWith(database, StringComparison.OrdinalIgnoreCase))
            {
                continue; // library.sqlite and its sidecars are compared as rows.
            }
            // Both carry the same device prefix, so the relative part is a plain suffix.
            string key = "fs:" + path[(root.Length + 1)..].Replace('\\', '/');
            FileAttributes attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                entries[key] = "link";
            }
            else if (attributes.HasFlag(FileAttributes.Directory))
            {
                entries[key] = "dir";
                Walk(root, path, database, entries);
            }
            else
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                entries[key] = $"file {stream.Length} {Convert.ToHexStringLower(SHA256.HashData(stream))}";
            }
        }
    }
}

internal sealed record SnapshotDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Changed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    public override string ToString() =>
        $"added: [{string.Join(", ", Added)}]\nremoved: [{string.Join(", ", Removed)}]\nchanged: [{string.Join(", ", Changed)}]";
}

internal static class SnapshotAssert
{
    public static void Unchanged(LibrarySnapshot before, LibrarySnapshot after)
    {
        SnapshotDiff diff = LibrarySnapshot.Diff(before, after);
        Assert.True(diff.IsEmpty, "Expected no change, got\n" + diff);
    }

    public static void Exactly(
        SnapshotDiff actual, IEnumerable<string> added, IEnumerable<string> removed, IEnumerable<string>? changed = null)
    {
        SnapshotDiff expected = new(
            added.Order(StringComparer.Ordinal).ToArray(),
            removed.Order(StringComparer.Ordinal).ToArray(),
            (changed ?? []).Order(StringComparer.Ordinal).ToArray());
        bool same = expected.Added.SequenceEqual(actual.Added) &&
                    expected.Removed.SequenceEqual(actual.Removed) &&
                    expected.Changed.SequenceEqual(actual.Changed);
        Assert.True(same, $"Expected\n{expected}\nGot\n{actual}");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `infra --filter FullyQualifiedName~LibrarySnapshotTests`.
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add tests/DesktopGuides.Infrastructure.Tests/FaultInjection
git commit -m "test(storage): library snapshot and diff for the fault matrix"
```

### Task 2: Fault fixture, seams and new checkpoints

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs`:
  the `ImportCheckpoint` enum (line 12), the rename at about line 115, and
  the end of the publish `try` (about lines 120–128).
- Modify: `src/DesktopGuides.Infrastructure/Storage/GuideRemover.cs`: the
  `RemovalCheckpoint` enum, the internal constructor, and `Remove`.
- Modify: `src/DesktopGuides.Infrastructure/Storage/GameRemover.cs`: the
  internal constructor, `Remove` and `DeleteArtwork`.
- Create: `tests/DesktopGuides.Infrastructure.Tests/FaultInjection/FaultFixture.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/FaultInjection/CheckpointOrderTests.cs`

**Interfaces:**
- Consumes:
  - `LibrarySnapshot` (Task 1);
  - `ExportFixture` (`Library`, `Store`, `LinkedGame`, `TxtGuide`,
    `HtmlGuide`, `PlainGame`, `PdfGuide` and `AddFileGuideAsync`);
  - `ImportTestDirectory.Copy` and `GuideImportValidator.InspectAsync`.
- Produces:
  - `ImportCheckpoint { Prepared, Copied, Verified, MovedToContent, Renamed, InCommit, Published }`
  - `RemovalCheckpoint { Prepared, TrashCreated, MovedGuide, Moved, InCommit, Committed, BeforeArtworkDelete }`
  - `internal GuideRemover(SqliteLibraryRepository repository, ILibraryPaths paths, Action<RemovalCheckpoint> checkpoint, Action<IDeletionJournal, Guid>? rollBack = null, Action<IDeletionJournal, Guid>? finish = null)`
  - `internal GameRemover(SqliteLibraryRepository repository, ILibraryPaths paths, IArtworkStore artwork, Action<RemovalCheckpoint> checkpoint, Action<IDeletionJournal, Guid>? rollBack = null, Action<IDeletionJournal, Guid>? finish = null)`
  - Test types:
    - `InjectedFault : Exception`;
    - `FaultFixture` with:
      - `Export`, `Library`, `Paths`;
      - `SubjectGame`, `SubjectGuide`, `SiblingGuide`, `BystanderGame`,
        `BystanderGuide`;
      - `HtmlManifest`, `ImportGuideId`;
      - `Publisher(...)`, `ImportAsync(...)`, `GuideRemover(...)`,
        `GameRemover(...)`;
      - `Capture()`, `RestartAsync()`;
      - static `FaultAt<T>(T)`, `CancelAt<T>(T, CancellationTokenSource)`,
        `SkipDeletionRollBack`, `FailDeletionRollBack`, `SkipFinish` and
        `SkipImportRollBack`.

- [ ] **Step 1: Write the fixture**

`tests/DesktopGuides.Infrastructure.Tests/FaultInjection/FaultFixture.cs`:

```csharp
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Infrastructure.Tests.Import;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

internal sealed class InjectedFault() : Exception("Injected fault.");

/// <summary>
/// T20.1's populated library plus a bystander game, an HTML import source,
/// and publishers and removers with fault hooks. The subject game holds the
/// TXT guide (the guide-removal subject, also LastActiveGuideId) and its HTML
/// sibling, with artwork. The bystander game has its own artwork, guide,
/// reading state and preference; no cell may touch it.
/// </summary>
internal sealed class FaultFixture : IAsyncDisposable
{
    private FaultFixture(ExportFixture export) => Export = export;

    public ExportFixture Export { get; }
    public RemovalLibrary Library => Export.Library;
    public ManagedPathResolver Paths => Export.Library.Paths;
    public Guid SubjectGame => Export.LinkedGame;
    public Guid SubjectGuide => Export.TxtGuide;
    public Guid SiblingGuide => Export.HtmlGuide;
    public Guid BystanderGame { get; private set; }
    public Guid BystanderGuide { get; private set; }
    public ImportTestDirectory Sources { get; } = new();
    public ImportManifest HtmlManifest { get; private set; } = null!;
    public Guid ImportGuideId { get; private set; }

    public static async Task<FaultFixture> CreateAsync()
    {
        FaultFixture fixture = new(await ExportFixture.CreateAsync());
        Guid bystander = Guid.NewGuid();
        StoredArtwork cover = await fixture.Export.Store.StoreAsync(bystander, Artwork.TestImages.Png(6, 6), default);
        fixture.BystanderGame = (await fixture.Library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            bystander, "Bystander Game", "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, "900700", DateTimeOffset.UnixEpoch),
            GameMetadataJsonTests.Sample(), cover.RelativePath))).Id;
        fixture.BystanderGuide = await fixture.Export.AddFileGuideAsync(
            fixture.BystanderGame, "Bystander Guide", GuideFormat.Txt, "guide.txt", "bystander"u8.ToArray());
        fixture.Library.Execute($$"""
            UPDATE ReadingStates SET EstimatedFraction = 0.25 WHERE GuideId = '{{fixture.BystanderGuide:N}}';
            UPDATE ReaderPreferences SET TextScale = 1.5 WHERE GuideId = '{{fixture.BystanderGuide:N}}';
            INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('LastActiveGuideId', '{{fixture.SubjectGuide:N}}');
            """);
        foreach (string file in new[] { "images/map.png", "styles/main.css", "styles/palette.css" })
        {
            fixture.Sources.Copy("html-static/" + file, file);
        }
        string entry = fixture.Sources.Copy("html-static/guide.html", "guide.html");
        fixture.HtmlManifest = ((ImportReady)await new GuideImportValidator().InspectAsync(entry, CancellationToken.None)).Manifest;
        return fixture;
    }

    /// <summary>A publisher whose next import uses fresh, known IDs.</summary>
    public GuideImportPublisher Publisher(
        Action<ImportCheckpoint>? checkpoint = null,
        Func<string, Stream>? createStagedFile = null,
        Action<IImportJournal, Guid>? rollBack = null)
    {
        Guid operationId = Guid.NewGuid();
        ImportGuideId = Guid.NewGuid();
        Queue<Guid> ids = new([operationId, ImportGuideId]);
        return new GuideImportPublisher(
            Library.Repository, Paths, null,
            () => ids.Count > 0 ? ids.Dequeue() : Guid.NewGuid(),
            createStagedFile ?? (path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true)),
            checkpoint ?? (_ => { }),
            rollBack ?? ((journal, operation) => journal.RollBack(operation)));
    }

    public Task<Guid> ImportAsync(GuideImportPublisher publisher, CancellationToken token = default) =>
        publisher.PublishAsync(HtmlManifest, SubjectGame, "Imported Guide", false, null, token);

    public GuideRemover GuideRemover(
        Action<RemovalCheckpoint>? checkpoint = null,
        Action<IDeletionJournal, Guid>? rollBack = null,
        Action<IDeletionJournal, Guid>? finish = null) =>
        new(Library.Repository, Paths, checkpoint ?? (_ => { }), rollBack, finish);

    public GameRemover GameRemover(
        Action<RemovalCheckpoint>? checkpoint = null,
        Action<IDeletionJournal, Guid>? rollBack = null,
        Action<IDeletionJournal, Guid>? finish = null) =>
        new(Library.Repository, Paths, Export.Store, checkpoint ?? (_ => { }), rollBack, finish);

    public LibrarySnapshot Capture() => LibrarySnapshot.Capture(Paths);

    /// <summary>A fresh repository on the same files: startup recovery runs.</summary>
    public Task RestartAsync() => Library.RestartAsync();

    public static Action<T> FaultAt<T>(T point) where T : struct, Enum => reached =>
    {
        if (EqualityComparer<T>.Default.Equals(reached, point)) throw new InjectedFault();
    };

    public static Action<T> CancelAt<T>(T point, CancellationTokenSource source) where T : struct, Enum => reached =>
    {
        if (EqualityComparer<T>.Default.Equals(reached, point)) source.Cancel();
    };

    public static Action<IDeletionJournal, Guid> SkipDeletionRollBack => (_, _) => { };
    public static Action<IDeletionJournal, Guid> FailDeletionRollBack => (_, _) => throw new InjectedFault();
    public static Action<IDeletionJournal, Guid> SkipFinish => (_, _) => { };
    public static Action<IImportJournal, Guid> SkipImportRollBack => (_, _) => { };

    public async ValueTask DisposeAsync()
    {
        Sources.Dispose();
        await Export.DisposeAsync();
    }
}
```

- [ ] **Step 2: Write the failing order tests**

`tests/DesktopGuides.Infrastructure.Tests/FaultInjection/CheckpointOrderTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class CheckpointOrderTests : IAsyncLifetime
{
    private FaultFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await FaultFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Fact]
    public async Task ImportFiresEveryCheckpointInOrder()
    {
        List<ImportCheckpoint> seen = [];

        await fixture.ImportAsync(fixture.Publisher(seen.Add));

        Assert.Equal(
            new[]
            {
                ImportCheckpoint.Prepared, ImportCheckpoint.Copied, ImportCheckpoint.Verified,
                ImportCheckpoint.MovedToContent, ImportCheckpoint.Renamed, ImportCheckpoint.InCommit,
                ImportCheckpoint.Published,
            },
            seen);
    }

    [Fact]
    public async Task GuideRemovalFiresEveryCheckpointInOrder()
    {
        List<RemovalCheckpoint> seen = [];

        await fixture.GuideRemover(seen.Add).RemoveAsync(fixture.SubjectGuide);

        Assert.Equal(
            new[]
            {
                RemovalCheckpoint.Prepared, RemovalCheckpoint.TrashCreated, RemovalCheckpoint.Moved,
                RemovalCheckpoint.InCommit, RemovalCheckpoint.Committed,
            },
            seen);
    }

    [Fact]
    public async Task GameRemovalFiresEveryCheckpointInOrder()
    {
        List<RemovalCheckpoint> seen = [];

        await fixture.GameRemover(seen.Add).RemoveAsync(fixture.SubjectGame, 2);

        Assert.Equal(
            new[]
            {
                RemovalCheckpoint.Prepared, RemovalCheckpoint.TrashCreated, RemovalCheckpoint.MovedGuide,
                RemovalCheckpoint.MovedGuide, RemovalCheckpoint.Moved, RemovalCheckpoint.InCommit,
                RemovalCheckpoint.Committed, RemovalCheckpoint.BeforeArtworkDelete,
            },
            seen);
    }

    [Fact]
    public async Task RemoversCallTheirRollBackAndFinishHooks()
    {
        List<Guid> rolledBack = [];
        await Assert.ThrowsAsync<GuideRemovalException>(() => fixture.GuideRemover(
            FaultFixture.FaultAt(RemovalCheckpoint.Moved), rollBack: (_, operation) => rolledBack.Add(operation))
            .RemoveAsync(fixture.SubjectGuide));
        Assert.Single(rolledBack);
        Assert.Equal($"{rolledBack[0]:N}|Prepared",
            fixture.Library.Scalar("SELECT Id || '|' || Phase FROM FileOperations"));

        await fixture.RestartAsync();
        List<Guid> finished = [];
        GuideRemovalResult result = await fixture.GuideRemover(finish: (_, operation) => finished.Add(operation))
            .RemoveAsync(fixture.SubjectGuide);
        Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
        Assert.Equal($"{finished.Single():N}|Committed",
            fixture.Library.Scalar("SELECT Id || '|' || Phase FROM FileOperations"));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `infra --filter FullyQualifiedName~CheckpointOrderTests`.
Expected: a build failure: `'ImportCheckpoint' does not contain a definition for 'MovedToContent'`, the
same for `TrashCreated` and `BeforeArtworkDelete`, and no `GuideRemover`
constructor that takes `rollBack`.

- [ ] **Step 4: Add the import checkpoints**

In `GuideImportPublisher.cs`, change the enum:

```csharp
internal enum ImportCheckpoint { Prepared, Copied, Verified, MovedToContent, Renamed, InCommit, Published }
```

After `Directory.Move(staged, paths.GetGuideRoot(guideId));`, add:

```csharp
            Pass(ImportCheckpoint.MovedToContent, token);
```

At the end of the publish `try`, delete `return guideId;`, which follows
`journal.Publish(…);`. After the closing brace of that `try`/`catch` (every
path through the `catch` throws), add:

```csharp
        // After the commit: a fault here must never reach the rollback above.
        checkpoint(ImportCheckpoint.Published);
        return guideId;
```

- [ ] **Step 5: Add the removal checkpoints and hooks**

In `GuideRemover.cs`, change the enum:

```csharp
internal enum RemovalCheckpoint { Prepared, TrashCreated, MovedGuide, Moved, InCommit, Committed, BeforeArtworkDelete }
```

Add the fields and replace the internal constructor:

```csharp
    private readonly Action<IDeletionJournal, Guid> rollBack;
    private readonly Action<IDeletionJournal, Guid> finish;

    internal GuideRemover(
        SqliteLibraryRepository repository, ILibraryPaths paths, Action<RemovalCheckpoint> checkpoint,
        Action<IDeletionJournal, Guid>? rollBack = null, Action<IDeletionJournal, Guid>? finish = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        this.rollBack = rollBack ?? ((journal, operationId) => journal.RollBack(operationId));
        this.finish = finish ?? ((journal, operationId) => journal.Finish(operationId));
    }
```

In `Remove`:
- after `Directory.CreateDirectory(Path.GetDirectoryName(trashPath)!);`, add
  `checkpoint(RemovalCheckpoint.TrashCreated);`;
- replace `journal.RollBack(operationId);` with `rollBack(journal, operationId);`;
- replace `journal.Finish(operationId);` with `finish(journal, operationId);`.

In `GameRemover.cs`, add the same two fields, and replace the internal
constructor with:

```csharp
    internal GameRemover(
        SqliteLibraryRepository repository, ILibraryPaths paths, IArtworkStore artwork,
        Action<RemovalCheckpoint> checkpoint,
        Action<IDeletionJournal, Guid>? rollBack = null, Action<IDeletionJournal, Guid>? finish = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.artwork = artwork ?? throw new ArgumentNullException(nameof(artwork));
        this.checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        this.rollBack = rollBack ?? ((journal, operationId) => journal.RollBack(operationId));
        this.finish = finish ?? ((journal, operationId) => journal.Finish(operationId));
    }
```

In `GameRemover.Remove`, declare `bool trashCreated = false;` just before the
`for` loop. In the loop, after `Directory.CreateDirectory(Path.GetDirectoryName(trashPath)!);`, add:

```csharp
                if (!trashCreated)
                {
                    trashCreated = true;
                    checkpoint(RemovalCheckpoint.TrashCreated);
                }
```

Then:
- replace `journal.RollBack(operationId);` with `rollBack(journal, operationId);`;
- replace `journal.Finish(operationId);` with `finish(journal, operationId);`.

In `DeleteArtwork`, after the `relativePath is null` return and before the
`try`, add:

```csharp
        checkpoint(RemovalCheckpoint.BeforeArtworkDelete);
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `infra --filter FullyQualifiedName~CheckpointOrderTests`.
Expected: PASS, 4 tests.

- [ ] **Step 7: Run the whole Infrastructure suite**

Run: `infra`.
Expected: PASS. The defaults keep every existing remover and publisher test
unchanged.

- [ ] **Step 8: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs src/DesktopGuides.Infrastructure/Storage/GuideRemover.cs src/DesktopGuides.Infrastructure/Storage/GameRemover.cs tests/DesktopGuides.Infrastructure.Tests/FaultInjection
git commit -m "test(storage): fault fixture, removal hooks and new protocol checkpoints"
```

### Task 3: Import matrix

**Files:**
- Test: `tests/DesktopGuides.Infrastructure.Tests/FaultInjection/ImportFaultMatrixTests.cs`

**Interfaces:**
- Consumes:
  - `FaultFixture` (`Publisher`, `ImportAsync`, `ImportGuideId`, `Capture`,
    `RestartAsync`, `FaultAt`, `CancelAt` and `SkipImportRollBack`), from
    Task 2;
  - `LibrarySnapshot` and `SnapshotAssert` (Task 1);
  - `DiskFullStream` (`Import/PublisherHarness.cs`).
- Produces: none.

Most cells pass against Task 2's code, because the protocol is already
correct. Each passing cell is characterization: list it in the ledger. Only
a cell that fails gets a fix, under the spec's defect rule.

- [ ] **Step 1: Write the matrix**

```csharp
using DesktopGuides.Core.Import;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class ImportFaultMatrixTests : IAsyncLifetime
{
    private FaultFixture fixture = null!;
    private LibrarySnapshot before = null!;

    public async Task InitializeAsync()
    {
        fixture = await FaultFixture.CreateAsync();
        before = fixture.Capture();
    }

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private static ImportCheckpoint Point(string name) => Enum.Parse<ImportCheckpoint>(name);

    /// <summary>The new guide's 7 rows and 7 file-tree entries were added, and nothing else changed.</summary>
    private void AssertPublished()
    {
        LibrarySnapshot after = fixture.Capture();
        string id = fixture.ImportGuideId.ToString("N");
        string[] added = after.KeysContaining(fixture.ImportGuideId).ToArray();
        Assert.Equal(
            new[]
            {
                $"db:GuideAssets/{id}/guide.html", $"db:GuideAssets/{id}/images/map.png",
                $"db:GuideAssets/{id}/styles/main.css", $"db:GuideAssets/{id}/styles/palette.css",
                $"db:Guides/{id}", $"db:ReaderPreferences/{id}", $"db:ReadingStates/{id}",
                $"fs:library/content/{id}", $"fs:library/content/{id}/guide.html",
                $"fs:library/content/{id}/images", $"fs:library/content/{id}/images/map.png",
                $"fs:library/content/{id}/styles", $"fs:library/content/{id}/styles/main.css",
                $"fs:library/content/{id}/styles/palette.css",
            },
            added.Order(StringComparer.Ordinal));
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, after), added, []);
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("Copied")]
    [InlineData("Verified")]
    [InlineData("MovedToContent")]
    [InlineData("Renamed")]
    [InlineData("InCommit")]
    public async Task ThrowBeforeTheCommitLeavesNoTrace(string checkpoint)
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() =>
            fixture.ImportAsync(fixture.Publisher(FaultFixture.FaultAt(Point(checkpoint)))));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("Copied")]
    [InlineData("Verified")]
    [InlineData("MovedToContent")]
    [InlineData("Renamed")]
    [InlineData("InCommit")]
    public async Task CrashBeforeTheCommitIsUndoneAtStartup(string checkpoint)
    {
        await Assert.ThrowsAsync<GuideImportException>(() => fixture.ImportAsync(
            fixture.Publisher(FaultFixture.FaultAt(Point(checkpoint)), rollBack: FaultFixture.SkipImportRollBack)));

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ThrowAfterTheCommitKeepsThePublishedGuide()
    {
        await Assert.ThrowsAsync<InjectedFault>(() =>
            fixture.ImportAsync(fixture.Publisher(FaultFixture.FaultAt(ImportCheckpoint.Published))));

        AssertPublished();
    }

    [Fact]
    public async Task CrashAfterTheCommitKeepsThePublishedGuideAtStartup()
    {
        await Assert.ThrowsAsync<InjectedFault>(() => fixture.ImportAsync(fixture.Publisher(
            FaultFixture.FaultAt(ImportCheckpoint.Published), rollBack: FaultFixture.SkipImportRollBack)));

        await fixture.RestartAsync();

        AssertPublished();
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("Copied")]
    [InlineData("Verified")]
    [InlineData("MovedToContent")]
    [InlineData("Renamed")]
    public async Task CancelBeforePublishingRollsBack(string checkpoint)
    {
        using CancellationTokenSource cancel = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.ImportAsync(
            fixture.Publisher(FaultFixture.CancelAt(Point(checkpoint), cancel)), cancel.Token));

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("InCommit")]
    [InlineData("Published")]
    public async Task CancelOncePublishingStartsIsIgnored(string checkpoint)
    {
        using CancellationTokenSource cancel = new();

        Guid id = await fixture.ImportAsync(
            fixture.Publisher(FaultFixture.CancelAt(Point(checkpoint), cancel)), cancel.Token);

        Assert.Equal(fixture.ImportGuideId, id);
        AssertPublished();
    }

    /// <summary>The first staged file writes normally; the second hits a full disk.</summary>
    private static Func<string, Stream> FullDiskOnSecondFile()
    {
        int calls = 0;
        return path => ++calls == 1
            ? new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true)
            : new DiskFullStream();
    }

    [Fact]
    public async Task ACopyFaultAfterTheFirstFileLeavesNoTrace()
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() =>
            fixture.ImportAsync(fixture.Publisher(createStagedFile: FullDiskOnSecondFile())));

        Assert.Equal(ImportIssue.NotEnoughSpace, error.Issue);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ACrashMidCopyIsUndoneAtStartup()
    {
        await Assert.ThrowsAsync<GuideImportException>(() => fixture.ImportAsync(fixture.Publisher(
            createStagedFile: FullDiskOnSecondFile(), rollBack: FaultFixture.SkipImportRollBack)));
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(fixture.Paths.StagingRoot));

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("FileOperations")]
    [InlineData("Guides")]
    public async Task ARefusedDatabaseInsertLeavesNoTrace(string table)
    {
        fixture.Library.Execute($"""
            CREATE TRIGGER RefuseInsert BEFORE INSERT ON {table}
            BEGIN SELECT RAISE(ABORT, 'fault'); END
            """);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() =>
            fixture.ImportAsync(fixture.Publisher()));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task AFailedRollBackKeepsTheRowAndStartupFinishesIt()
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() => fixture.ImportAsync(
            fixture.Publisher(FaultFixture.FaultAt(ImportCheckpoint.Copied), rollBack: (_, _) => throw new InjectedFault())));
        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        Assert.Equal("Import|Prepared", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }
}
```

- [ ] **Step 2: Run the matrix**

Run: `infra --filter FullyQualifiedName~ImportFaultMatrixTests`.
Expected: PASS, 26 tests (21 theory cases and 5 facts). A failing cell is a
finding: debug it with superpowers:systematic-debugging. Fix it in this task
if the fix is local, test first. Otherwise record it in the ledger for
Task 7's tracking issues, and mark the cell `Skip = "tracked in #<issue>"`
once the issue exists.

- [ ] **Step 3: Commit**

```bash
git add tests/DesktopGuides.Infrastructure.Tests/FaultInjection/ImportFaultMatrixTests.cs
git commit -m "test(import): fault-injection matrix for publication"
```

### Task 4: Guide-removal matrix

**Files:**
- Test: `tests/DesktopGuides.Infrastructure.Tests/FaultInjection/GuideRemovalFaultMatrixTests.cs`

**Interfaces:**
- Consumes:
  - `FaultFixture` (`GuideRemover`, `SubjectGuide`, `Capture`, `RestartAsync`,
    `FaultAt`, `CancelAt`, `SkipDeletionRollBack`, `FailDeletionRollBack`
    and `SkipFinish`), from Task 2;
  - `SqliteLibraryRepository.RunExportAsync`, used to hold the write gate;
  - Task 1's snapshot types.
- Produces: none.

The subject is the TXT guide, whose content is `guide.txt`. It is also
`LastActiveGuideId`. Its HTML sibling in the same game, and the bystander
game, must stay untouched in every cell.

- [ ] **Step 1: Write the matrix**

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class GuideRemovalFaultMatrixTests : IAsyncLifetime
{
    private FaultFixture fixture = null!;
    private LibrarySnapshot before = null!;

    public async Task InitializeAsync()
    {
        fixture = await FaultFixture.CreateAsync();
        before = fixture.Capture();
    }

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private static RemovalCheckpoint Point(string name) => Enum.Parse<RemovalCheckpoint>(name);

    private string[] RemovedKeys() =>
        before.KeysContaining(fixture.SubjectGuide).Append("db:Settings/LastActiveGuideId").ToArray();

    private void AssertRemoved() =>
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, fixture.Capture()), [], RemovedKeys());

    /// <summary>Removed, plus the guide's files under .trash/op and its Committed row.</summary>
    private void AssertCleanupPending()
    {
        LibrarySnapshot after = fixture.Capture();
        string row = after.Entries.Keys.Single(key => key.StartsWith("db:FileOperations/", StringComparison.Ordinal));
        string op = row["db:FileOperations/".Length..];
        Assert.Contains("Phase=Committed", after.Entries[row]);
        string guide = fixture.SubjectGuide.ToString("N");
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, after),
            [row, $"fs:library/.trash/{op}", $"fs:library/.trash/{op}/{guide}", $"fs:library/.trash/{op}/{guide}/guide.txt"],
            RemovedKeys());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("TrashCreated")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task ThrowBeforeTheCommitRestoresTheGuide(string checkpoint)
    {
        GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(() =>
            fixture.GuideRemover(FaultFixture.FaultAt(Point(checkpoint))).RemoveAsync(fixture.SubjectGuide));

        Assert.Equal(GuideRemovalIssue.Failed, error.Issue);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("TrashCreated")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task CrashBeforeTheCommitIsRestoredAtStartup(string checkpoint)
    {
        await Assert.ThrowsAsync<GuideRemovalException>(() => fixture.GuideRemover(
            FaultFixture.FaultAt(Point(checkpoint)), rollBack: FaultFixture.SkipDeletionRollBack)
            .RemoveAsync(fixture.SubjectGuide));

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("TrashCreated")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task AFailedRollBackKeepsThePreparedRowForStartup(string checkpoint)
    {
        GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(() => fixture.GuideRemover(
            FaultFixture.FaultAt(Point(checkpoint)), rollBack: FaultFixture.FailDeletionRollBack)
            .RemoveAsync(fixture.SubjectGuide));
        Assert.Equal(GuideRemovalIssue.RestoreFailed, error.Issue);
        Assert.Equal("DeleteGuide|Prepared", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ThrowAtCommittedLeavesOnlyAKnownTrashEntry()
    {
        GuideRemovalResult result = await fixture.GuideRemover(FaultFixture.FaultAt(RemovalCheckpoint.Committed))
            .RemoveAsync(fixture.SubjectGuide);

        Assert.Equal(new GuideRemovalResult(GuideRemovalOutcome.Removed, true), result);
        AssertCleanupPending();
        await fixture.RestartAsync();
        AssertRemoved();
    }

    [Fact]
    public async Task CrashAfterTheCommitIsFinishedAtStartup()
    {
        await fixture.GuideRemover(finish: FaultFixture.SkipFinish).RemoveAsync(fixture.SubjectGuide);
        AssertCleanupPending();

        await fixture.RestartAsync();

        AssertRemoved();
    }

    [Fact]
    public async Task ARefusedDeleteRestoresTheGuide()
    {
        fixture.Library.Execute("""
            CREATE TRIGGER RefuseDelete BEFORE DELETE ON Guides
            BEGIN SELECT RAISE(ABORT, 'fault'); END
            """);

        GuideRemovalException error = await Assert.ThrowsAsync<GuideRemovalException>(() =>
            fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide));

        Assert.Equal(GuideRemovalIssue.Failed, error.Issue);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ANormalRemovalRemovesExactlyTheGuide()
    {
        Assert.Equal(new GuideRemovalResult(GuideRemovalOutcome.Removed, false),
            await fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide));

        AssertRemoved();
    }

    [Fact]
    public async Task CancelBeforeTheCallChangesNothing()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide, cancel.Token));

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task CancelWhileAnotherOperationHoldsTheGateChangesNothing()
    {
        TaskCompletionSource release = new();
        Task<bool> holder = fixture.Library.Repository.RunExportAsync(async (_, _) =>
        {
            await release.Task;
            return true;
        }, CancellationToken.None);
        using CancellationTokenSource cancel = new();

        Task<GuideRemovalResult> waiting = fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide, cancel.Token);
        await Task.Delay(100);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.SetResult();
        await holder;
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task CancelAfterPreparedStillRemoves()
    {
        using CancellationTokenSource cancel = new();

        GuideRemovalResult result = await fixture.GuideRemover(FaultFixture.CancelAt(RemovalCheckpoint.Prepared, cancel))
            .RemoveAsync(fixture.SubjectGuide, cancel.Token);

        Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
        AssertRemoved();
    }
}
```

- [ ] **Step 2: Run the matrix**

Run: `infra --filter FullyQualifiedName~GuideRemovalFaultMatrixTests`.
Expected: PASS, 19 tests (12 theory cases and 7 facts). Treat a failing cell
as in Task 3, Step 2.

- [ ] **Step 3: Commit**

```bash
git add tests/DesktopGuides.Infrastructure.Tests/FaultInjection/GuideRemovalFaultMatrixTests.cs
git commit -m "test(storage): fault-injection matrix for guide removal"
```

### Task 5: Game-removal matrix

**Files:**
- Test: `tests/DesktopGuides.Infrastructure.Tests/FaultInjection/GameRemovalFaultMatrixTests.cs`

**Interfaces:**
- Consumes:
  - `FaultFixture` (`GameRemover`, `SubjectGame`, `SubjectGuide`,
    `SiblingGuide`, `Export.Store`, `Library.Repository`, `Capture`,
    `RestartAsync`, `FaultAt`, `CancelAt`, `SkipDeletionRollBack`,
    `FailDeletionRollBack` and `SkipFinish`), from Task 2;
  - `GameMetadataJsonTests.Sample()` and `Artwork.TestImages.Png`.
- Produces: none.

The subject is the linked game, with its TXT and HTML guides and its
artwork. `LastActiveGuideId` points at its TXT guide, so a commit removes
that setting.

- [ ] **Step 1: Write the matrix**

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class GameRemovalFaultMatrixTests : IAsyncLifetime
{
    private FaultFixture fixture = null!;
    private LibrarySnapshot before = null!;

    public async Task InitializeAsync()
    {
        fixture = await FaultFixture.CreateAsync();
        before = fixture.Capture();
    }

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private static RemovalCheckpoint Point(string name) => Enum.Parse<RemovalCheckpoint>(name);

    private Task<GameRemovalResult> Remove(GameRemover remover, CancellationToken token = default) =>
        remover.RemoveAsync(fixture.SubjectGame, 2, token);

    private bool IsArtwork(string key) =>
        key.StartsWith($"fs:library/artwork/{fixture.SubjectGame:N}", StringComparison.Ordinal);

    private string[] RemovedKeys() => before.KeysContaining(fixture.SubjectGame)
        .Concat(before.KeysContaining(fixture.SubjectGuide))
        .Concat(before.KeysContaining(fixture.SiblingGuide))
        .Append("db:Settings/LastActiveGuideId")
        .Distinct().ToArray();

    private void AssertRemoved() =>
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, fixture.Capture()), [], RemovedKeys());

    /// <summary>Removed, plus both guides' files under .trash/op and the Committed row.</summary>
    private void AssertCleanupPending()
    {
        LibrarySnapshot after = fixture.Capture();
        string row = after.Entries.Keys.Single(key => key.StartsWith("db:FileOperations/", StringComparison.Ordinal));
        string op = row["db:FileOperations/".Length..];
        Assert.Contains("Phase=Committed", after.Entries[row]);
        IEnumerable<string> trashed = before.Entries.Keys
            .Where(key => key.StartsWith("fs:library/content/", StringComparison.Ordinal) &&
                (key.Contains(fixture.SubjectGuide.ToString("N")) || key.Contains(fixture.SiblingGuide.ToString("N"))))
            .Select(key => $"fs:library/.trash/{op}/" + key["fs:library/content/".Length..]);
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, after),
            trashed.Append(row).Append($"fs:library/.trash/{op}"), RemovedKeys());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("TrashCreated")]
    [InlineData("MovedGuide")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task ThrowBeforeTheCommitRestoresTheGame(string checkpoint)
    {
        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() =>
            Remove(fixture.GameRemover(FaultFixture.FaultAt(Point(checkpoint)))));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("TrashCreated")]
    [InlineData("MovedGuide")]
    [InlineData("Moved")]
    [InlineData("InCommit")]
    public async Task CrashBeforeTheCommitIsRestoredAtStartup(string checkpoint)
    {
        await Assert.ThrowsAsync<GameRemovalException>(() => Remove(fixture.GameRemover(
            FaultFixture.FaultAt(Point(checkpoint)), rollBack: FaultFixture.SkipDeletionRollBack)));

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task AFailedRollBackKeepsThePreparedRowForStartup()
    {
        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() => Remove(fixture.GameRemover(
            FaultFixture.FaultAt(RemovalCheckpoint.Moved), rollBack: FaultFixture.FailDeletionRollBack)));
        Assert.Equal(GameRemovalIssue.RestoreFailed, error.Issue);

        await fixture.RestartAsync();

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ThrowAtCommittedLeavesOnlyAKnownTrashEntry()
    {
        GameRemovalResult result = await Remove(fixture.GameRemover(FaultFixture.FaultAt(RemovalCheckpoint.Committed)));

        Assert.Equal((GameRemovalOutcome.Removed, true), (result.Outcome, result.CleanupPending));
        AssertCleanupPending();
        await fixture.RestartAsync();
        AssertRemoved();
    }

    [Fact]
    public async Task CrashAfterTheCommitIsFinishedAtStartup()
    {
        await Remove(fixture.GameRemover(finish: FaultFixture.SkipFinish));
        AssertCleanupPending();

        await fixture.RestartAsync();

        AssertRemoved();
    }

    [Fact]
    public async Task AFaultBeforeTheArtworkDeleteIsSweptAtStartup()
    {
        await Assert.ThrowsAsync<InjectedFault>(() =>
            Remove(fixture.GameRemover(FaultFixture.FaultAt(RemovalCheckpoint.BeforeArtworkDelete))));
        SnapshotAssert.Exactly(LibrarySnapshot.Diff(before, fixture.Capture()), [],
            RemovedKeys().Where(key => !IsArtwork(key)));

        await fixture.RestartAsync();

        AssertRemoved();
    }

    [Fact]
    public async Task ARefusedDeleteRestoresTheGame()
    {
        fixture.Library.Execute("""
            CREATE TRIGGER RefuseDelete BEFORE DELETE ON Games
            BEGIN SELECT RAISE(ABORT, 'fault'); END
            """);

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() => Remove(fixture.GameRemover()));

        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task ANormalRemovalRemovesExactlyTheGame()
    {
        GameRemovalResult result = await Remove(fixture.GameRemover());

        Assert.Equal((GameRemovalOutcome.Removed, false), (result.Outcome, result.CleanupPending));
        AssertRemoved();
    }

    [Fact]
    public async Task CancelBeforeTheCallChangesNothing()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Remove(fixture.GameRemover(), cancel.Token));

        SnapshotAssert.Unchanged(before, fixture.Capture());
    }

    [Fact]
    public async Task CancelAfterPreparedStillRemoves()
    {
        using CancellationTokenSource cancel = new();

        GameRemovalResult result = await Remove(
            fixture.GameRemover(FaultFixture.CancelAt(RemovalCheckpoint.Prepared, cancel)), cancel.Token);

        Assert.Equal(GameRemovalOutcome.Removed, result.Outcome);
        AssertRemoved();
    }

    /// <summary>A linked game with artwork and no guides, added after the shared snapshot.</summary>
    private async Task<(Guid Game, LibrarySnapshot Before)> AddEmptyGameAsync()
    {
        Guid id = Guid.NewGuid();
        StoredArtwork cover = await fixture.Export.Store.StoreAsync(id, Artwork.TestImages.Png(3, 3), default);
        await fixture.Library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            id, "Empty Game", "PC", new ProviderGameLink(ProviderGameLink.Igdb, "900800", DateTimeOffset.UnixEpoch),
            GameMetadataJsonTests.Sample(), cover.RelativePath));
        return (id, fixture.Capture());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AGameWithoutGuidesSurvivesAFaultInItsCommit(bool restart)
    {
        (Guid game, LibrarySnapshot start) = await AddEmptyGameAsync();

        GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(() =>
            fixture.GameRemover(FaultFixture.FaultAt(RemovalCheckpoint.InCommit)).RemoveAsync(game, 0));
        Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        if (restart) await fixture.RestartAsync();

        SnapshotAssert.Unchanged(start, fixture.Capture());
    }

    [Fact]
    public async Task AGameWithoutGuidesHasItsArtworkSweptAfterAFault()
    {
        (Guid game, LibrarySnapshot start) = await AddEmptyGameAsync();

        await Assert.ThrowsAsync<InjectedFault>(() =>
            fixture.GameRemover(FaultFixture.FaultAt(RemovalCheckpoint.BeforeArtworkDelete)).RemoveAsync(game, 0));
        await fixture.RestartAsync();

        SnapshotAssert.Exactly(LibrarySnapshot.Diff(start, fixture.Capture()), [], start.KeysContaining(game));
    }

    [Fact]
    public async Task AGameWithoutGuidesIsRemovedExactly()
    {
        (Guid game, LibrarySnapshot start) = await AddEmptyGameAsync();

        await fixture.GameRemover().RemoveAsync(game, 0);

        SnapshotAssert.Exactly(LibrarySnapshot.Diff(start, fixture.Capture()), [], start.KeysContaining(game));
    }
}
```

- [ ] **Step 2: Run the matrix**

Run: `infra --filter FullyQualifiedName~GameRemovalFaultMatrixTests`.
Expected: PASS, 22 tests (12 theory cases and 10 facts). Treat a failing cell
as in Task 3, Step 2.

- [ ] **Step 3: Commit**

```bash
git add tests/DesktopGuides.Infrastructure.Tests/FaultInjection/GameRemovalFaultMatrixTests.cs
git commit -m "test(storage): fault-injection matrix for game removal"
```

### Task 6: NTFS cells and the two link fixes

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/GuideRemover.cs`: `DescribeAsync`,
  `Remove` and a new `GuideRoot`.
- Modify: `src/DesktopGuides.Infrastructure/Storage/GameRemover.cs`: `CaptureAll`
  and a new `GuideRoot`.
- Modify: `src/DesktopGuides.Infrastructure/Artwork/ManagedArtworkStore.cs`: `Delete`.
- Test: `tests/DesktopGuides.Infrastructure.Tests/FaultInjection/NtfsFaultTests.cs`

**Interfaces:**
- Consumes:
  - `FaultFixture` (Task 2);
  - `RemovalLibrary.CreateJunction` and `WriteFile`;
  - `ManagedPathResolver.RejectFilesystemLinks` (internal since T20.1);
  - `IImportJournal.Prepare` and `IDeletionJournal.Prepare`, through
    `RunImportAsync` and `RunDeletionAsync`.
- Produces: no new API. `GuideRemover` and `GameRemover` now return
  `Unsafe` for a junctioned guide root, and `ManagedArtworkStore.Delete`
  never deletes through a link.

Every cell returns early off Windows, and removes the links it creates in a
`finally`.

- [ ] **Step 1: Write the cells**

```csharp
using System.Diagnostics;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class NtfsFaultTests : IAsyncLifetime
{
    private FaultFixture fixture = null!;
    private string outside = null!;
    private string sentinel = null!;

    public async Task InitializeAsync()
    {
        fixture = await FaultFixture.CreateAsync();
        outside = Path.Combine(fixture.Library.Root, "outside");
        sentinel = Path.Combine(outside, "sentinel.txt");
        RemovalLibrary.WriteFile(outside, "sentinel.txt", "keep me");
    }

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private void AssertSentinelIntact() => Assert.Equal("keep me", File.ReadAllText(sentinel));

    private static void HardLink(string link, string target)
    {
        using Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /H \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
    }

    [Fact]
    public async Task AJunctionedStagingOperationIsRefusedAtStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid operation = Guid.NewGuid();
        await fixture.Library.Repository.RunImportAsync<bool>((journal, _) =>
        {
            journal.Prepare(operation, Guid.NewGuid());
            return Task.FromResult(true);
        }, CancellationToken.None);
        string link = Path.Combine(fixture.Paths.StagingRoot, operation.ToString("N"));
        RemovalLibrary.CreateJunction(link, outside);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RestartAsync());

            AssertSentinelIntact();
            Assert.Equal("Import|Prepared", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task AJunctionedTrashOperationIsRefusedAtStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid operation = Guid.NewGuid();
        await fixture.Library.RunDeletion(journal => journal.Prepare(operation, fixture.SubjectGuide));
        string link = Path.Combine(fixture.Paths.TrashRoot, operation.ToString("N"));
        RemovalLibrary.CreateJunction(link, outside);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RestartAsync());

            AssertSentinelIntact();
            Assert.Equal("DeleteGuide|Prepared", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task AJunctionedCommittedTrashGuideIsRefusedAtStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        await fixture.GuideRemover(finish: FaultFixture.SkipFinish).RemoveAsync(fixture.SubjectGuide);
        string operation = fixture.Library.Scalar("SELECT Id FROM FileOperations");
        string trashed = Path.Combine(fixture.Paths.TrashRoot, operation, fixture.SubjectGuide.ToString("N"));
        Directory.Move(trashed, Path.Combine(outside, "moved"));
        RemovalLibrary.CreateJunction(trashed, outside);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RestartAsync());

            AssertSentinelIntact();
            Assert.Equal("DeleteGuide|Committed", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
        }
        finally
        {
            Directory.Delete(trashed);
        }
    }

    [Fact]
    public async Task AJunctionedGuideFolderIsUnsafeToRemove()
    {
        if (!OperatingSystem.IsWindows()) return;
        string content = fixture.Paths.GetGuideRoot(fixture.SubjectGuide);
        Directory.Move(content, Path.Combine(outside, "guide"));
        RemovalLibrary.CreateJunction(content, Path.Combine(outside, "guide"));
        try
        {
            LibrarySnapshot start = fixture.Capture();
            GuideRemover guides = fixture.GuideRemover();
            GameRemover games = fixture.GameRemover();

            Assert.Equal(GuideRemovalIssue.Unsafe, (await Assert.ThrowsAsync<GuideRemovalException>(
                () => guides.DescribeAsync(fixture.SubjectGuide))).Issue);
            Assert.Equal(GuideRemovalIssue.Unsafe, (await Assert.ThrowsAsync<GuideRemovalException>(
                () => guides.RemoveAsync(fixture.SubjectGuide))).Issue);
            Assert.Equal(GameRemovalIssue.Unsafe, (await Assert.ThrowsAsync<GameRemovalException>(
                () => games.RemoveAsync(fixture.SubjectGame, 2))).Issue);

            SnapshotAssert.Unchanged(start, fixture.Capture());
            AssertSentinelIntact();
        }
        finally
        {
            Directory.Delete(content);
        }
    }

    [Fact]
    public async Task AHardLinkedGuideFileKeepsTheOutsideFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        string file = Path.Combine(fixture.Paths.GetGuideRoot(fixture.SubjectGuide), "guide.txt");
        File.Delete(file);
        HardLink(file, sentinel);

        GuideRemovalResult result = await fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide);

        Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
        AssertSentinelIntact();
    }

    [Fact]
    public async Task ReservedNamesInARemovedGuideReachAConsistentOutcome()
    {
        if (!OperatingSystem.IsWindows()) return;
        string device = @"\\?\" + fixture.Paths.GetGuideRoot(fixture.SubjectGuide);
        File.WriteAllText(Path.Combine(device, "CON"), "reserved");
        File.WriteAllText(Path.Combine(device, "x."), "trailing dot");
        try
        {
            GuideRemovalResult result = await fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide);
            await fixture.RestartAsync();

            Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
            Assert.Equal("0", fixture.Library.Scalar($"SELECT COUNT(*) FROM Guides WHERE Id = '{fixture.SubjectGuide:N}'"));
            string operations = fixture.Library.Scalar("SELECT COUNT(*) || '|' || IFNULL(MAX(Phase), '') FROM FileOperations");
            Assert.Contains(operations, new[] { "0|", "1|Committed" });
            AssertSentinelIntact();
        }
        finally
        {
            // Plain Win32 paths can't delete these names; remove them wherever they ended up.
            foreach (string leftover in Directory.EnumerateFiles(@"\\?\" + fixture.Paths.LibraryRoot, "*", SearchOption.AllDirectories)
                         .Where(path => Path.GetFileName(path) is "CON" or "x."))
            {
                File.Delete(leftover);
            }
        }
    }

    [Fact]
    public async Task UnknownTrashFoldersAreKeptForReview()
    {
        if (!OperatingSystem.IsWindows()) return;
        RemovalLibrary.WriteFile(Path.Combine(fixture.Paths.TrashRoot, "not-a-guid"), "x.txt", "unknown");
        RemovalLibrary.WriteFile(Path.Combine(fixture.Paths.TrashRoot, Guid.NewGuid().ToString("N").ToUpperInvariant()), "y.txt", "unknown");

        await fixture.RestartAsync();

        Assert.True(File.Exists(Path.Combine(fixture.Paths.TrashRoot, "not-a-guid", "x.txt")));
        Assert.Single(Directory.EnumerateDirectories(fixture.Paths.TrashRoot), path => Path.GetFileName(path).Any(char.IsUpper) && Path.GetFileName(path).Length == 32);
        Assert.True(fixture.Library.Repository.LastStartupReconciliation!.ReviewOrphanCount >= 2);
    }

    [Fact]
    public async Task AJunctionedArtworkFolderKeepsTheOutsideFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        string artwork = fixture.Export.ArtworkFile();
        string folder = Path.GetDirectoryName(artwork)!;
        string outsideCover = Path.Combine(outside, Path.GetFileName(artwork));
        File.Copy(artwork, outsideCover);
        Directory.Delete(folder, true);
        RemovalLibrary.CreateJunction(folder, outside);
        try
        {
            GameRemovalResult result = await fixture.GameRemover().RemoveAsync(fixture.SubjectGame, 2);

            Assert.Equal(GameRemovalOutcome.Removed, result.Outcome);
            Assert.True(File.Exists(outsideCover));
            AssertSentinelIntact();
        }
        finally
        {
            Directory.Delete(folder);
        }
    }
}
```

- [ ] **Step 2: Run the cells to see which fail**

Run: `infra --filter FullyQualifiedName~NtfsFaultTests`.
Expected: `AJunctionedGuideFolderIsUnsafeToRemove` fails, because
`InvalidDataException` escapes instead of `GuideRemovalException`.
`AJunctionedArtworkFolderKeepsTheOutsideFile` fails if `Delete` follows the
junction (`outsideCover` deleted). Record the other cells' results:
- a pass is characterization;
- any other failure is a finding for Task 3, Step 2's rule. For example, a
  reserved name that makes cleanup throw raw, or a junction that recovery
  follows.

- [ ] **Step 3: Fix 1, a junctioned guide root maps to `Unsafe`**

In `GuideRemover.cs`, add:

```csharp
    /// <summary>The guide's content root; a link there is unsafe, not a crash.</summary>
    private string GuideRoot(Guid guideId)
    {
        try
        {
            return paths.GetGuideRoot(guideId);
        }
        catch (InvalidDataException error)
        {
            throw new GuideRemovalException(GuideRemovalIssue.Unsafe, error);
        }
    }
```

Then:
- in `DescribeAsync`, replace `Capture(paths.GetGuideRoot(guide.Id))` with
  `Capture(GuideRoot(guide.Id))`;
- in `Remove`, replace `string contentPath = paths.GetGuideRoot(guideId);`
  with `string contentPath = GuideRoot(guideId);`.

In `GameRemover.cs`, add the same `GuideRoot` method, throwing
`GameRemovalException(GameRemovalIssue.Unsafe, error)`, and change
`CaptureAll` to:

```csharp
    private List<OwnedGuideTree> CaptureAll(IReadOnlyList<Guid> guideIds) =>
        guideIds.Select(id => Capture(GuideRoot(id))).ToList();
```

- [ ] **Step 4: Fix 2, the artwork delete never follows a link**

Only if Step 2 showed `outsideCover` deleted. In `ManagedArtworkStore.cs`,
add `using DesktopGuides.Infrastructure.Storage;` and replace `Delete` with:

```csharp
    public void Delete(string relativePath)
    {
        if (ResolvePath(relativePath) is not { } file) return;
        try
        {
            // A linked artwork folder may point outside the library; leave it
            // for the startup sweep to count for review.
            ManagedPathResolver.RejectFilesystemLinks(file);
            File.Delete(file);
            string folder = Path.GetDirectoryName(file)!;
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (InvalidDataException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
```

If Step 2 showed that the outside file survived, skip this step. Record the
cell as characterization, and the probable defect as disproved.

- [ ] **Step 5: Run the cells and the whole suite**

Run: `infra --filter FullyQualifiedName~NtfsFaultTests`, then `infra`.
Expected: the NTFS cells PASS (8 tests), and the whole suite passes.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Storage/GuideRemover.cs src/DesktopGuides.Infrastructure/Storage/GameRemover.cs src/DesktopGuides.Infrastructure/Artwork/ManagedArtworkStore.cs tests/DesktopGuides.Infrastructure.Tests/FaultInjection/NtfsFaultTests.cs
git commit -m "fix(storage): unsafe junctioned guide roots and linked artwork deletes"
```

### Task 7: Deterministic held-content test, tracking issues and docs

**Files:**
- Modify: `tests/DesktopGuides.Infrastructure.Tests/GameRemovalTests.cs`:
  `ContentHeldOpenFailsAndRestoresEveryGuide`.
- Modify: `docs/p1/t15-4-fault-injection-design.md`: the status line and a
  verification record.
- Modify: `docs/p1/implementation-plan.md`: a T15.4 sentence above the M5
  table.

**Interfaces:**
- Consumes: `GameRemovalTests.Remover(Action<RemovalCheckpoint>?)` and
  `RemovalCheckpoint.MovedGuide`.
- Produces: none.

- [ ] **Step 1: Make the held-content test always exercise the partial move**

Guides move in `ORDER BY Id` order. That's SQLite's binary collation over the
lowercase `N` strings, which is ordinal. Replace the test with:

```csharp
    [Fact]
    public async Task ContentHeldOpenFailsAndRestoresEveryGuide()
    {
        if (!OperatingSystem.IsWindows()) return;
        // Hold a file in the guide that moves second, so the first one is
        // moved and has to come back.
        bool walkthroughFirst = string.CompareOrdinal(walkthroughId.ToString("N"), mapsId.ToString("N")) < 0;
        string held = walkthroughFirst
            ? Path.Combine(Content(mapsId), "images/map.png")
            : Path.Combine(Content(walkthroughId), "guide.txt");
        List<RemovalCheckpoint> seen = [];
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            GameRemovalException error = await Assert.ThrowsAsync<GameRemovalException>(
                () => Remover(seen.Add).RemoveAsync(game.Id, 2));

            Assert.Equal(GameRemovalIssue.Failed, error.Issue);
        }
        Assert.Equal(1, seen.Count(point => point == RemovalCheckpoint.MovedGuide));
        AssertGameIntact();
    }
```

- [ ] **Step 2: Run it**

Run: `infra --filter FullyQualifiedName~GameRemovalTests`.
Expected: PASS. The `MovedGuide` count of 1 shows that the partial-move path
ran. Before this change, the count was 0 whenever the held guide sorted
first. This is a test-only change, so no product code fails first; say so in
the ledger.

- [ ] **Step 3: Open the tracking issues**

With the GitHub MCP `issue_write` tool (method `create`), open one issue per
recorded defect. At least this one:

- Title: `Import rolls back when its empty staging folder can't be deleted`
- Body: After `Directory.Move` publishes a guide into `content/`, the
  publisher deletes the now-empty `.staging/<op>`. If that delete fails (for
  example, an indexer has a handle open), the exception reaches the publish
  `catch` and rolls back an otherwise complete import. Found in T06.3;
  pinned by the T15.4 `MovedToContent` cells. A fix would make the cleanup
  best-effort, which changes the import protocol's failure handling. That
  is out of T15.4's scope.

Add an issue for each further finding the ledger recorded under the spec's
"record" rule. Note each number for Step 4.

- [ ] **Step 4: Write the verification record**

In `docs/p1/t15-4-fault-injection-design.md`:
- Replace the status line with
  `Status: implemented on \`feat/p1-t15-4-fault-injection\`; see the verification record.`
- Append `## T15.4 verification record`, with:
  - **Unit tests:** the Core and Infrastructure pass counts from Step 6, and
    the cell count per class (`LibrarySnapshotTests` 6,
    `CheckpointOrderTests` 4, `ImportFaultMatrixTests` 26,
    `GuideRemovalFaultMatrixTests` 19, `GameRemovalFaultMatrixTests` 22,
    `NtfsFaultTests` 8);
  - **Characterization:** the cells that passed on the code before their
    task, from the ledger;
  - **Fixes:** the junctioned guide root that now maps to `Unsafe`; the
    artwork delete that no longer follows a link (or "disproved", per
    Task 6, Step 4); and the deterministic held-content test;
  - **Tracking issues:** the numbers from Step 3;
  - **Rulings:** every `Ruling:` line from the ledger;
  - **Not run:** an installed smoke, because there is no UI.

- [ ] **Step 5: Add the implementation-plan sentence**

In `docs/p1/implementation-plan.md`, directly above the M5 table and after
the T20.1 sentence, add:

```markdown
T15.4 is implemented on `feat/p1-t15-4-fault-injection`; see the
[design](t15-4-fault-injection-design.md#t154-verification-record).
```

- [ ] **Step 6: Run both suites on the host**

Run: `core`, then `infra`.
Expected: both PASS. Core is unchanged at 932. Infrastructure is 593 plus the
85 new cells (6 + 4 + 26 + 19 + 22 + 8), so 678, plus any regression tests
added for findings. Record the exact counts in Step 4's record.

- [ ] **Step 7: Commit**

```bash
git add tests/DesktopGuides.Infrastructure.Tests/GameRemovalTests.cs docs/p1/t15-4-fault-injection-design.md docs/p1/implementation-plan.md
git commit -m "docs(p1): T15.4 verification record; deterministic held-content test"
```

## Traceability

- **The technical design's eight scenarios:**
  - copy failure: `ACopyFaultAfterTheFirstFileLeavesNoTrace` and
    `ACrashMidCopyIsUndoneAtStartup`;
  - crash after stage: `CrashBeforeTheCommitIsUndoneAtStartup` at `Copied`
    and `Verified`;
  - crash after rename: the same theory at `MovedToContent` and `Renamed`;
  - failed database commit: `ARefusedDatabaseInsertLeavesNoTrace` and both
    `ARefusedDelete…` cells;
  - failed trash move: the throw cells at `Prepared` and `TrashCreated`;
  - cancelled removal: the `Cancel…` cells;
  - committed removal: `ANormalRemoval…` and the `Committed` cells;
  - startup recovery: every `Crash…` cell.
- **TR04.1 and TR04.2:** `GameRemovalFaultMatrixTests` and
  `AJunctionedArtworkFolderKeepsTheOutsideFile`.
- **TR06.2:** `ImportFaultMatrixTests`.
- **TR15.1:** `NtfsFaultTests`, plus the exact diff, which covers the
  bystander, in every cell.
- **TR15.2:** `GuideRemovalFaultMatrixTests`.

## PR outcome

- **Target task:** T15.4.
- **Prerequisites:** T04.3 (PR #26), T06.3 (PR #19), T15.2 (PR #5) and T15.3
  (PR #22), all merged.
- **Outcome:** a fault-injection matrix of 85 cells covers every import and
  removal phase. Each cell compares exact rows and app-owned files, across
  throw, crash and restart, cancellation, failed rollback and NTFS links
  and names. Small defects found are fixed with tests; the rest are
  tracked. There is no UI change, so there is no screenshot.
