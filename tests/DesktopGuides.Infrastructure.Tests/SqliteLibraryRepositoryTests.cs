using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class SqliteLibraryRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PersistsTwoIndependentGuideStatesAndSettingsAcrossReopen()
    {
        using TestLibrary directory = new();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid gameId;
        await using (SqliteLibraryRepository repository =
            new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await repository.InitializeAsync();
            Game game = await repository.AddGameAsync("  Space Quest  ", null, null);
            gameId = game.Id;
            Assert.Equal("Space Quest", game.Title);
            Assert.Equal(Now, game.CreatedUtc);
            InsertGuide(directory.Paths.DatabasePath, first, gameId);
            InsertGuide(directory.Paths.DatabasePath, second, gameId);
            SetCompleted(directory.Paths.DatabasePath, second);

            await repository.SaveReadingLocationAsync(first, "{\"one\":1}", 0.25);
            await repository.SaveReadingLocationAsync(second, "{\"two\":2}", 0.75);
            await repository.SaveReaderPreferencesAsync(first, 1.25);
            await repository.SaveSettingsAsync(new AppSettings(ThemePreference.Dark, second));
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();

        Assert.Equal("Space Quest", (await reopened.GetGameAsync(gameId))?.Title);
        Assert.Single(await reopened.ListGamesAsync());
        Assert.Equal(2, (await reopened.ListGuidesAsync(gameId)).Count);
        Assert.Equal(first, (await reopened.GetGuideAsync(first))?.Id);
        Assert.Equal("{\"one\":1}", (await reopened.GetReadingStateAsync(first))?.LocatorJson);
        Assert.Equal(0.25, (await reopened.GetReadingStateAsync(first))?.EstimatedFraction);
        Assert.Equal("{\"two\":2}", (await reopened.GetReadingStateAsync(second))?.LocatorJson);
        Assert.Equal(0.75, (await reopened.GetReadingStateAsync(second))?.EstimatedFraction);
        Assert.Null((await reopened.GetReadingStateAsync(first))?.CompletedUtc);
        Assert.Equal(Now, (await reopened.GetReadingStateAsync(second))?.CompletedUtc);
        Assert.Equal(Now, (await reopened.GetGuideAsync(first))?.ImportedUtc);
        Assert.Equal(1.25, (await reopened.GetReaderPreferencesAsync(first))?.TextScale);
        Assert.Null((await reopened.GetReaderPreferencesAsync(second))?.TextScale);
        Assert.Equal(new AppSettings(ThemePreference.Dark, second), await reopened.GetSettingsAsync());

        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, (long)version.ExecuteScalar()!);
        using SqliteCommand index = connection.CreateCommand();
        index.CommandText = """
            SELECT name FROM sqlite_schema
            WHERE type = 'index' AND name = 'IX_ReadingStates_LastOpenedUtcMs'
            """;
        Assert.Equal("IX_ReadingStates_LastOpenedUtcMs", index.ExecuteScalar());
        using SqliteCommand orphan = connection.CreateCommand();
        orphan.CommandText = "INSERT INTO ReadingStates (GuideId) VALUES ($id)";
        orphan.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        Assert.Throws<SqliteException>(() => orphan.ExecuteNonQuery());
    }

    [Fact]
    public async Task RejectsInvalidMetadataAndUnknownGuidePosition()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.AddGameAsync("  ", null, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.SaveReadingLocationAsync(Guid.NewGuid(), "{}", 1.1));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.SaveReadingLocationAsync(Guid.NewGuid(), "{}", 0.5));
    }

    [Fact]
    public async Task RejectsGuideRootThatIsNotBoundToItsId()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Example", null, null);

        Assert.Throws<SqliteException>(() =>
            InsertGuide(directory.Paths.DatabasePath, Guid.NewGuid(), game.Id,
                "content/wrong-guide"));
        Assert.Empty(await repository.ListGuidesAsync(game.Id));
    }

    [Fact]
    public async Task RecoversEmptyVersionZeroDatabaseAfterInterruptedSchemaCreation()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand journal = connection.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode=WAL";
            journal.ExecuteNonQuery();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand abandoned = connection.CreateCommand();
            abandoned.Transaction = transaction;
            abandoned.CommandText = "CREATE TABLE Abandoned (Id INTEGER)";
            abandoned.ExecuteNonQuery();
            transaction.Rollback();
        }

        Assert.True(File.Exists(directory.Paths.DatabasePath));
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Recovered", null, null);
        Assert.Equal("Recovered", (await repository.GetGameAsync(game.Id))?.Title);
    }

    [Fact]
    public async Task PreservesPopulatedUnknownVersionZeroDatabase()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE UnknownData (Value TEXT NOT NULL);
                INSERT INTO UnknownData (Value) VALUES ('keep this');
                """;
            command.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
        using SqliteConnection reopened = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand verify = reopened.CreateCommand();
        verify.CommandText = "SELECT Value FROM UnknownData";
        Assert.Equal("keep this", verify.ExecuteScalar());
    }

    [Fact]
    public async Task PreservesEmptyVersionZeroDatabaseWithAnotherApplicationId()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA application_id = 4242";
            command.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
        using SqliteConnection reopened = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand verify = reopened.CreateCommand();
        verify.CommandText = "PRAGMA application_id";
        Assert.Equal(4242L, (long)verify.ExecuteScalar()!);
    }

    [Fact]
    public async Task UpgradesPopulatedVersionOneWithConsistentRecoveryCopy()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionOne(directory, gameId, guideId);

        // Keep a writer open with a recently committed WAL change during backup.
        using SqliteConnection active = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using (SqliteCommand latest = active.CreateCommand())
        {
            latest.CommandText = "UPDATE Games SET Notes = 'latest WAL note' WHERE Id = $id";
            latest.Parameters.AddWithValue("$id", gameId.ToString("N"));
            Assert.Equal(1, latest.ExecuteNonQuery());
        }
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        Assert.Equal("Older game", (await repository.GetGameAsync(gameId))?.Title);
        Assert.Equal("latest WAL note", (await repository.GetGameAsync(gameId))?.Notes);
        Assert.Equal(guideId, (await repository.GetGuideAsync(guideId))?.Id);
        Assert.Equal("{\"old\":true}",
            (await repository.GetReadingStateAsync(guideId))?.LocatorJson);
        Assert.Equal(Now, (await repository.GetReadingStateAsync(guideId))?.CompletedUtc);
        Assert.Equal(1.25, (await repository.GetReaderPreferencesAsync(guideId))?.TextScale);
        Assert.Equal(new AppSettings(ThemePreference.Dark, guideId),
            await repository.GetSettingsAsync());

        using SqliteCommand version = active.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, (long)version.ExecuteScalar()!);
        using SqliteCommand index = active.CreateCommand();
        index.CommandText = """
            SELECT name FROM sqlite_schema
            WHERE type = 'index' AND name = 'IX_ReadingStates_LastOpenedUtcMs'
            """;
        Assert.Equal("IX_ReadingStates_LastOpenedUtcMs", index.ExecuteScalar());

        string backupPath = Assert.Single(
            Directory.GetFiles(directory.Paths.RecoveryRoot, "*.sqlite"));
        using SqliteConnection backup = OpenWithForeignKeys(backupPath);
        using SqliteCommand backupVersion = backup.CreateCommand();
        backupVersion.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, (long)backupVersion.ExecuteScalar()!);
        using SqliteCommand backupGuide = backup.CreateCommand();
        backupGuide.CommandText = "SELECT Id FROM Guides WHERE Id = $id";
        backupGuide.Parameters.AddWithValue("$id", guideId.ToString("N"));
        Assert.Equal(guideId.ToString("N"), backupGuide.ExecuteScalar());
        using SqliteCommand backupNote = backup.CreateCommand();
        backupNote.CommandText = "SELECT Notes FROM Games WHERE Id = $id";
        backupNote.Parameters.AddWithValue("$id", gameId.ToString("N"));
        Assert.Equal("latest WAL note", backupNote.ExecuteScalar());

        await repository.InitializeAsync();
        Assert.Single(Directory.GetFiles(directory.Paths.RecoveryRoot, "*.sqlite"));
    }

    [Fact]
    public async Task FailedMigrationRetainsVersionOneDataAndRecoveryCopy()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionOne(directory, gameId, guideId);

        await using (SqliteLibraryRepository failing = new(
            directory.Paths, null, version =>
            {
                if (version == 2)
                {
                    throw new IOException("Injected after the v2 index was created.");
                }
            }))
        {
            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
                () => failing.InitializeAsync());
            Assert.Contains("recovery copy:", error.Message);
            Assert.IsType<IOException>(error.InnerException);
        }

        using (SqliteConnection original = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand version = original.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            Assert.Equal(1L, (long)version.ExecuteScalar()!);
            using SqliteCommand index = original.CreateCommand();
            index.CommandText = """
                SELECT name FROM sqlite_schema
                WHERE type = 'index' AND name = 'IX_ReadingStates_LastOpenedUtcMs'
                """;
            Assert.Null(index.ExecuteScalar());
            using SqliteCommand game = original.CreateCommand();
            game.CommandText = "SELECT Title FROM Games WHERE Id = $id";
            game.Parameters.AddWithValue("$id", gameId.ToString("N"));
            Assert.Equal("Older game", game.ExecuteScalar());
        }

        string backupPath = Assert.Single(
            Directory.GetFiles(directory.Paths.RecoveryRoot, "*.sqlite"));
        using (SqliteConnection backup = OpenWithForeignKeys(backupPath))
        {
            using SqliteCommand version = backup.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            Assert.Equal(1L, (long)version.ExecuteScalar()!);
            using SqliteCommand guide = backup.CreateCommand();
            guide.CommandText = "SELECT Id FROM Guides WHERE Id = $id";
            guide.Parameters.AddWithValue("$id", guideId.ToString("N"));
            Assert.Equal(guideId.ToString("N"), guide.ExecuteScalar());
        }

        await using SqliteLibraryRepository retry = new(directory.Paths);
        await retry.InitializeAsync();
        Assert.Equal(guideId, (await retry.GetGuideAsync(guideId))?.Id);
    }

    [Fact]
    public async Task RejectsNewerSchemaWithoutReplacingItsData()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand data = connection.CreateCommand();
            data.CommandText = """
                CREATE TABLE FutureData (Value TEXT NOT NULL);
                INSERT INTO FutureData (Value) VALUES ('keep this');
                PRAGMA user_version = 99;
                """;
            data.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository repository = new(directory.Paths);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => repository.InitializeAsync());
        Assert.Contains("Update Desktop Guides", error.Message);
        using SqliteConnection original = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand verify = original.CreateCommand();
        verify.CommandText = "SELECT Value FROM FutureData";
        Assert.Equal("keep this", verify.ExecuteScalar());
        Assert.Empty(Directory.GetFiles(directory.Paths.RecoveryRoot));
    }

    [Fact]
    public async Task RejectsOrphanedVersionOneRowsBeforeMigration()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionOne(directory, gameId, guideId);
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_keys=OFF";
            foreignKeys.ExecuteNonQuery();
            using SqliteCommand orphan = connection.CreateCommand();
            orphan.CommandText = "INSERT INTO ReadingStates (GuideId) VALUES ($id)";
            orphan.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            orphan.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository repository = new(directory.Paths);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => repository.InitializeAsync());
        Assert.Contains("orphaned records", error.Message);
        using SqliteConnection original = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand version = original.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, (long)version.ExecuteScalar()!);
        Assert.Empty(Directory.GetFiles(directory.Paths.RecoveryRoot));
    }

    [Fact]
    public async Task RejectsIncompleteCurrentSchemaWithoutReinitializing()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Keep", null, null);
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand drop = connection.CreateCommand();
            drop.CommandText = "DROP INDEX IX_ReadingStates_LastOpenedUtcMs";
            drop.ExecuteNonQuery();
        }

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => repository.InitializeAsync());
        Assert.Contains("schema is incomplete", error.Message);
        Assert.Equal("Keep", (await repository.GetGameAsync(game.Id))?.Title);
    }

    [Fact]
    public async Task RejectsLinkedDatabaseBeforeOpeningExternalVersionOneData()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        string outside = Path.Combine(directory.Root, "outside.sqlite");
        using (SqliteConnection connection = OpenWithForeignKeys(outside))
        {
            using SqliteCommand schema = connection.CreateCommand();
            schema.CommandText = LibrarySchema.Version1;
            schema.ExecuteNonQuery();
        }

        File.CreateSymbolicLink(directory.Paths.DatabasePath, outside);
        try
        {
            await using SqliteLibraryRepository repository = new(directory.Paths);
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                repository.GetGameAsync(Guid.NewGuid()));
            Assert.Empty(Directory.GetFiles(directory.Paths.RecoveryRoot));
        }
        finally
        {
            File.Delete(directory.Paths.DatabasePath);
        }

        using SqliteConnection original = OpenWithForeignKeys(outside);
        using SqliteCommand version = original.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, (long)version.ExecuteScalar()!);
    }

    [Fact]
    public async Task RejectsAlteredVersionOneConstraintsBeforeBackup()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        string alteredSchema = LibrarySchema.Version1.Replace(
            "length(Title) BETWEEN 1 AND 160", "length(Title) <= 160",
            StringComparison.Ordinal);
        Assert.NotEqual(LibrarySchema.Version1, alteredSchema);
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand schema = connection.CreateCommand();
            schema.CommandText = alteredSchema;
            schema.ExecuteNonQuery();
            using SqliteCommand game = connection.CreateCommand();
            game.CommandText = """
                INSERT INTO Games (Id, Title, CreatedUtcMs, UpdatedUtcMs)
                VALUES ($id, 'Keep', $now, $now)
                """;
            game.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            game.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
            game.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
        Assert.Empty(Directory.GetFiles(directory.Paths.RecoveryRoot));

        using SqliteConnection original = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand verify = original.CreateCommand();
        verify.CommandText = "SELECT Title FROM Games";
        Assert.Equal("Keep", verify.ExecuteScalar());
        verify.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, (long)verify.ExecuteScalar()!);
    }

    [Fact]
    public async Task RejectsRenamedVersionTwoColumnBeforeRepositoryReads()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        await repository.AddGameAsync("Keep", null, "Note");
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games RENAME COLUMN Notes TO Memo";
            alter.ExecuteNonQuery();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
        using SqliteConnection original = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand verify = original.CreateCommand();
        verify.CommandText = "SELECT Title FROM Games";
        Assert.Equal("Keep", verify.ExecuteScalar());
        Assert.Empty(Directory.GetFiles(directory.Paths.RecoveryRoot));
    }

    private static void InsertGuide(
        string databasePath, Guid guideId, Guid gameId, string? managedRoot = null)
    {
        using SqliteConnection connection = OpenWithForeignKeys(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Guides (
                Id, GameId, Title, Format, ManagedRelativeRoot, PrimaryRelativePath,
                ContentSha256, ContentBytes, SourceLabel, TextCodePage,
                ImportedUtcMs, UpdatedUtcMs
            ) VALUES (
                $id, $game, 'Walkthrough', 'Txt', $root, 'guide.txt',
                $hash, 100, NULL, NULL, $now, $now
            );
            INSERT INTO ReadingStates (GuideId) VALUES ($id);
            INSERT INTO ReaderPreferences (GuideId) VALUES ($id);
            """;
        command.Parameters.AddWithValue("$id", guideId.ToString("N"));
        command.Parameters.AddWithValue("$game", gameId.ToString("N"));
        command.Parameters.AddWithValue(
            "$root", managedRoot ?? "content/" + guideId.ToString("N"));
        command.Parameters.AddWithValue("$hash", new string('a', 64));
        command.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private static void CreatePopulatedVersionOne(
        TestLibrary directory, Guid gameId, Guid guideId)
    {
        directory.Paths.EnsureCreated();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand journal = connection.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode=WAL";
            journal.ExecuteNonQuery();
            using SqliteCommand schema = connection.CreateCommand();
            schema.CommandText = LibrarySchema.Version1;
            schema.ExecuteNonQuery();
            using SqliteCommand game = connection.CreateCommand();
            game.CommandText = """
                INSERT INTO Games (Id, Title, CreatedUtcMs, UpdatedUtcMs)
                VALUES ($id, 'Older game', $now, $now)
                """;
            game.Parameters.AddWithValue("$id", gameId.ToString("N"));
            game.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
            game.ExecuteNonQuery();
        }

        InsertGuide(directory.Paths.DatabasePath, guideId, gameId);
        using SqliteConnection reopened = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand state = reopened.CreateCommand();
        state.CommandText = """
            UPDATE ReadingStates
            SET LocatorJson = '{"old":true}', CompletedUtcMs = $now
            WHERE GuideId = $id;
            UPDATE ReaderPreferences SET TextScale = 1.25 WHERE GuideId = $id;
            INSERT INTO Settings (Key, Value) VALUES
                ('Theme', 'Dark'), ('LastActiveGuideId', $id);
            """;
        state.Parameters.AddWithValue("$id", guideId.ToString("N"));
        state.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
        state.ExecuteNonQuery();
    }

    private static SqliteConnection OpenWithForeignKeys(string databasePath)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString());
        connection.Open();
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void SetCompleted(string databasePath, Guid guideId)
    {
        using SqliteConnection connection = OpenWithForeignKeys(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ReadingStates SET CompletedUtcMs = $now WHERE GuideId = $id
            """;
        command.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$id", guideId.ToString("N"));
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestLibrary : IDisposable
    {
        public TestLibrary()
        {
            Root = Path.Combine(Path.GetTempPath(), "desktop-guides-db-" + Guid.NewGuid());
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
