using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class FileOperationReconciliationTests
{
    [Fact]
    public async Task PreparedImportRemovesOnlyNamedStageAndRetainsUnknownDirectories()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid operationId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        string staged = StagedRoot(directory.Paths, operationId, guideId);
        WriteMarker(staged);
        string unknownStage = Path.Combine(directory.Paths.StagingRoot, "unknown-stage");
        string unknownContent = Path.Combine(directory.Paths.ContentRoot, "unknown-content");
        WriteMarker(unknownStage);
        WriteMarker(unknownContent);
        InsertOperation(directory.Paths.DatabasePath, operationId, "Import", "Prepared",
            Manifest("Import", operationId, guideId));

        await repository.InitializeAsync();

        Assert.False(Directory.Exists(staged));
        Assert.False(Directory.Exists(Path.GetDirectoryName(staged)!));
        Assert.True(Directory.Exists(unknownStage));
        Assert.True(Directory.Exists(unknownContent));
        Assert.Equal(0, OperationCount(directory.Paths.DatabasePath));
        Assert.Equal(
            new StartupReconciliationReport(1, 2),
            repository.LastStartupReconciliation);
        await repository.InitializeAsync();
        Assert.True(Directory.Exists(unknownStage));
        Assert.Equal(
            new StartupReconciliationReport(0, 2),
            repository.LastStartupReconciliation);
    }

    [Fact]
    public async Task PreparedImportRemovesUnpublishedMovedContent()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid operationId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        WriteMarker(directory.Paths.GetGuideRoot(guideId));
        InsertOperation(directory.Paths.DatabasePath, operationId, "Import", "Prepared",
            Manifest("Import", operationId, guideId));

        await repository.InitializeAsync();

        Assert.False(Directory.Exists(directory.Paths.GetGuideRoot(guideId)));
        Assert.Equal(0, OperationCount(directory.Paths.DatabasePath));
    }

    [Fact]
    public async Task PreparedDeletionRestoresMovedGuideAndKeepsItsMetadata()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Keep", null, null);
        Guid operationId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        InsertGuide(directory.Paths.DatabasePath, game.Id, guideId);
        string content = directory.Paths.GetGuideRoot(guideId);
        string trash = TrashedRoot(directory.Paths, operationId, guideId);
        WriteMarker(content);
        Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        Directory.Move(content, trash);
        InsertOperation(directory.Paths.DatabasePath, operationId, "DeleteGuide", "Prepared",
            Manifest("DeleteGuide", operationId, guideId));

        await repository.InitializeAsync();

        Assert.True(File.Exists(Path.Combine(content, "marker.txt")));
        Assert.False(Directory.Exists(trash));
        Assert.Equal(guideId, (await repository.GetGuideAsync(guideId))?.Id);
        Assert.NotNull(await repository.GetReadingStateAsync(guideId));
        Assert.NotNull(await repository.GetReaderPreferencesAsync(guideId));
        Assert.Equal(0, OperationCount(directory.Paths.DatabasePath));
        Assert.Equal(
            new StartupReconciliationReport(1, 0),
            repository.LastStartupReconciliation);
    }

    [Fact]
    public async Task PreparedGameDeletionRestoresOnlyMovedGuidesAndCanRetry()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Keep", null, null);
        Guid operationId = Guid.NewGuid();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        InsertGuide(directory.Paths.DatabasePath, game.Id, first);
        InsertGuide(directory.Paths.DatabasePath, game.Id, second);
        string firstContent = directory.Paths.GetGuideRoot(first);
        string secondContent = directory.Paths.GetGuideRoot(second);
        string firstTrash = TrashedRoot(directory.Paths, operationId, first);
        WriteMarker(firstContent);
        WriteMarker(secondContent);
        Directory.CreateDirectory(Path.GetDirectoryName(firstTrash)!);
        Directory.Move(firstContent, firstTrash);
        InsertOperation(directory.Paths.DatabasePath, operationId, "DeleteGame", "Prepared",
            Manifest("DeleteGame", operationId, first, second));

        await repository.InitializeAsync();
        await repository.InitializeAsync();

        Assert.True(File.Exists(Path.Combine(firstContent, "marker.txt")));
        Assert.True(File.Exists(Path.Combine(secondContent, "marker.txt")));
        Assert.False(Directory.Exists(firstTrash));
        Assert.Equal(2, (await repository.ListGuidesAsync(game.Id)).Count);
        Assert.Equal(0, OperationCount(directory.Paths.DatabasePath));
    }

    [Fact]
    public async Task CommittedDeletionRemovesOnlyNamedTrashAndLeavesUnknownSibling()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid operationId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        string trash = TrashedRoot(directory.Paths, operationId, guideId);
        string unknown = Path.Combine(Path.GetDirectoryName(trash)!, "unknown");
        WriteMarker(trash);
        WriteMarker(unknown);
        InsertOperation(directory.Paths.DatabasePath, operationId, "DeleteGuide", "Committed",
            Manifest("DeleteGuide", operationId, guideId));

        await repository.InitializeAsync();

        Assert.False(Directory.Exists(trash));
        Assert.True(File.Exists(Path.Combine(unknown, "marker.txt")));
        Assert.Equal(0, OperationCount(directory.Paths.DatabasePath));
        Assert.Equal(
            new StartupReconciliationReport(1, 1),
            repository.LastStartupReconciliation);
    }

    [Fact]
    public async Task MalformedManifestStopsRecoveryBeforeAnyOwnedTreeIsRemoved()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid goodOperation = Guid.NewGuid();
        Guid goodGuide = Guid.NewGuid();
        string goodStage = StagedRoot(directory.Paths, goodOperation, goodGuide);
        WriteMarker(goodStage);
        InsertOperation(directory.Paths.DatabasePath, goodOperation, "Import", "Prepared",
            Manifest("Import", goodOperation, goodGuide), createdUtcMs: 1);

        Guid badOperation = Guid.NewGuid();
        Guid badGuide = Guid.NewGuid();
        string outside = Path.Combine(directory.Root, "outside");
        WriteMarker(outside);
        string malicious = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            guideIds = new[] { badGuide.ToString("N") },
            ownedPaths = new[] { "../../outside" }
        });
        InsertOperation(directory.Paths.DatabasePath, badOperation, "Import", "Prepared",
            malicious, createdUtcMs: 2);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());

        Assert.True(File.Exists(Path.Combine(goodStage, "marker.txt")));
        Assert.True(File.Exists(Path.Combine(outside, "marker.txt")));
        Assert.Equal(2, OperationCount(directory.Paths.DatabasePath));
    }

    [Fact]
    public async Task NestedLinkStopsCleanupWithoutFollowingTheTarget()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid operationId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        string staged = StagedRoot(directory.Paths, operationId, guideId);
        WriteMarker(staged);
        string outside = Path.Combine(directory.Root, "outside.txt");
        File.WriteAllText(outside, "keep");
        File.CreateSymbolicLink(Path.Combine(staged, "linked.txt"), outside);
        InsertOperation(directory.Paths.DatabasePath, operationId, "Import", "Prepared",
            Manifest("Import", operationId, guideId));

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());

        Assert.Equal("keep", File.ReadAllText(outside));
        Assert.True(Directory.Exists(staged));
        Assert.Equal(1, OperationCount(directory.Paths.DatabasePath));
    }

    [Fact]
    public async Task ConflictingPreparedDeletionLeavesContentAndTrashForReview()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Keep", null, null);
        Guid operationId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        InsertGuide(directory.Paths.DatabasePath, game.Id, guideId);
        string content = directory.Paths.GetGuideRoot(guideId);
        string trash = TrashedRoot(directory.Paths, operationId, guideId);
        WriteMarker(content);
        WriteMarker(trash);
        InsertOperation(directory.Paths.DatabasePath, operationId, "DeleteGuide", "Prepared",
            Manifest("DeleteGuide", operationId, guideId));

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());

        Assert.True(Directory.Exists(content));
        Assert.True(Directory.Exists(trash));
        Assert.Equal(1, OperationCount(directory.Paths.DatabasePath));
    }

    [Theory]
    [InlineData("future-version")]
    [InlineData("extra-field")]
    [InlineData("duplicate-guide")]
    public async Task RejectsUnsupportedManifestShapesWithoutCleanup(string mutation)
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid operationId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        string staged = StagedRoot(directory.Paths, operationId, guideId);
        WriteMarker(staged);
        JsonNode manifest = JsonNode.Parse(Manifest("Import", operationId, guideId))!;
        switch (mutation)
        {
            case "future-version":
                manifest["schemaVersion"] = 2;
                break;
            case "extra-field":
                manifest["unexpected"] = true;
                break;
            case "duplicate-guide":
                manifest["guideIds"]!.AsArray().Add(guideId.ToString("N"));
                break;
        }
        InsertOperation(directory.Paths.DatabasePath, operationId, "Import", "Prepared",
            manifest.ToJsonString());

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());

        Assert.True(File.Exists(Path.Combine(staged, "marker.txt")));
        Assert.Equal(1, OperationCount(directory.Paths.DatabasePath));
    }

    [Fact]
    public async Task OverlappingOperationsAreRejectedBeforeCleanup()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid firstOperation = Guid.NewGuid();
        Guid secondOperation = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        string firstStage = StagedRoot(directory.Paths, firstOperation, guideId);
        string secondStage = StagedRoot(directory.Paths, secondOperation, guideId);
        WriteMarker(firstStage);
        WriteMarker(secondStage);
        InsertOperation(directory.Paths.DatabasePath, firstOperation, "Import", "Prepared",
            Manifest("Import", firstOperation, guideId), createdUtcMs: 1);
        InsertOperation(directory.Paths.DatabasePath, secondOperation, "Import", "Prepared",
            Manifest("Import", secondOperation, guideId), createdUtcMs: 2);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());

        Assert.True(Directory.Exists(firstStage));
        Assert.True(Directory.Exists(secondStage));
        Assert.Equal(2, OperationCount(directory.Paths.DatabasePath));
    }

    [Fact]
    public async Task FileDeletionFailureRetainsJournalForRetry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid operationId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        string staged = StagedRoot(directory.Paths, operationId, guideId);
        WriteMarker(staged);
        string marker = Path.Combine(staged, "marker.txt");
        InsertOperation(directory.Paths.DatabasePath, operationId, "Import", "Prepared",
            Manifest("Import", operationId, guideId));
        using (FileStream locked = new(marker, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => repository.InitializeAsync());
            Assert.Equal(1, OperationCount(directory.Paths.DatabasePath));
            Assert.Null(repository.LastStartupReconciliation);
        }

        await repository.InitializeAsync();

        Assert.False(Directory.Exists(staged));
        Assert.Equal(0, OperationCount(directory.Paths.DatabasePath));
    }

    [Fact]
    public async Task NestedWindowsJunctionIsRetainedWithoutVisitingTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid operationId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        string staged = StagedRoot(directory.Paths, operationId, guideId);
        WriteMarker(staged);
        string outside = Path.Combine(directory.Root, "outside-directory");
        WriteMarker(outside);
        string junction = Path.Combine(staged, "junction");
        ProcessStartInfo start = new("cmd.exe", $"/c mklink /J \"{junction}\" \"{outside}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (Process process = Process.Start(start)!)
        {
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        }
        InsertOperation(directory.Paths.DatabasePath, operationId, "Import", "Prepared",
            Manifest("Import", operationId, guideId));
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
            Assert.True(File.Exists(Path.Combine(outside, "marker.txt")));
            Assert.Equal(1, OperationCount(directory.Paths.DatabasePath));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    private static string StagedRoot(ManagedPathResolver paths, Guid operation, Guid guide) =>
        Path.Combine(paths.StagingRoot, operation.ToString("N"), guide.ToString("N"));

    private static string TrashedRoot(ManagedPathResolver paths, Guid operation, Guid guide) =>
        Path.Combine(paths.TrashRoot, operation.ToString("N"), guide.ToString("N"));

    private static void WriteMarker(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "marker.txt"), "keep");
    }

    private static string Manifest(string kind, Guid operationId, params Guid[] guideIds)
    {
        string[] ids = guideIds.Select(id => id.ToString("N"))
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        string[] paths = ids.SelectMany(id => kind == "Import"
            ? new[] { $".staging/{operationId:N}/{id}", $"content/{id}" }
            : new[] { $"content/{id}", $".trash/{operationId:N}/{id}" })
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            guideIds = ids,
            ownedPaths = paths
        });
    }

    private static void InsertOperation(
        string databasePath, Guid operationId, string kind, string phase,
        string manifest, long createdUtcMs = 1)
    {
        using SqliteConnection connection = Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FileOperations (Id, Kind, Phase, ManifestJson, CreatedUtcMs)
            VALUES ($id, $kind, $phase, $manifest, $created)
            """;
        command.Parameters.AddWithValue("$id", operationId.ToString("N"));
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$phase", phase);
        command.Parameters.AddWithValue("$manifest", manifest);
        command.Parameters.AddWithValue("$created", createdUtcMs);
        command.ExecuteNonQuery();
    }

    private static void InsertGuide(string databasePath, Guid gameId, Guid guideId)
    {
        using SqliteConnection connection = Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Guides (
                Id, GameId, Title, Format, ManagedRelativeRoot, PrimaryRelativePath,
                ContentSha256, ContentBytes, ImportedUtcMs, UpdatedUtcMs
            ) VALUES (
                $id, $game, 'Walkthrough', 'Txt', $root, 'guide.txt',
                $hash, 4, $now, $now
            );
            INSERT INTO ReadingStates (GuideId) VALUES ($id);
            INSERT INTO ReaderPreferences (GuideId) VALUES ($id);
            """;
        command.Parameters.AddWithValue("$id", guideId.ToString("N"));
        command.Parameters.AddWithValue("$game", gameId.ToString("N"));
        command.Parameters.AddWithValue("$root", $"content/{guideId:N}");
        command.Parameters.AddWithValue("$hash", new string('a', 64));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private static long OperationCount(string databasePath)
    {
        using SqliteConnection connection = Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM FileOperations";
        return (long)command.ExecuteScalar()!;
    }

    private static SqliteConnection Open(string databasePath)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private sealed class TestLibrary : IDisposable
    {
        public TestLibrary()
        {
            Root = Path.Combine(Path.GetTempPath(), "desktop-guides-reconcile-" + Guid.NewGuid());
            Paths = new ManagedPathResolver(Path.Combine(Root, "app-data"));
        }

        public string Root { get; }
        public ManagedPathResolver Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
