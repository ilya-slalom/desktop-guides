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
        Assert.Equal(1L, (long)version.ExecuteScalar()!);
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
