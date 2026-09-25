using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Storage;

public sealed class SqliteLibraryRepository : ILibraryRepository
{
    private readonly ILibraryPaths paths;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public SqliteLibraryRepository(ILibraryPaths paths, TimeProvider? clock = null)
    {
        this.paths = paths;
        this.clock = clock ?? TimeProvider.System;
    }

    public Task InitializeAsync(CancellationToken token = default) =>
        WriteAsync(Initialize, token);

    public Task<Game> AddGameAsync(
        string title, string? platform, string? notes, CancellationToken token = default)
    {
        title = RequiredText(title, 160, nameof(title));
        platform = OptionalText(platform, 80, nameof(platform));
        notes = OptionalText(notes, 2000, nameof(notes));
        return WriteAsync(() =>
        {
            Guid id = Guid.NewGuid();
            DateTimeOffset now = clock.GetUtcNow();
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Games (
                    Id, Title, Platform, Notes, CreatedUtcMs, UpdatedUtcMs
                ) VALUES ($id, $title, $platform, $notes, $now, $now)
                """;
            command.Parameters.AddWithValue("$id", id.ToString("N"));
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$platform", (object?)platform ?? DBNull.Value);
            command.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
            return new Game(id, title, platform, notes, now, now);
        }, token);
    }

    public Task<Game?> GetGameAsync(Guid gameId, CancellationToken token = default) =>
        ReadAsync<Game?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Title, Platform, Notes, CreatedUtcMs, UpdatedUtcMs
                FROM Games WHERE Id = $id
                """;
            command.Parameters.AddWithValue("$id", gameId.ToString("N"));
            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadGame(reader) : null;
        }, token);

    public Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken token = default) =>
        ReadAsync<IReadOnlyList<Game>>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Title, Platform, Notes, CreatedUtcMs, UpdatedUtcMs
                FROM Games ORDER BY Title, Id
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            List<Game> games = [];
            while (reader.Read())
            {
                games.Add(ReadGame(reader));
            }
            return games;
        }, token);

    public Task<IReadOnlyList<Guide>> ListGuidesAsync(
        Guid gameId, CancellationToken token = default) =>
        ReadAsync<IReadOnlyList<Guide>>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, GameId, Title, Format, ManagedRelativeRoot,
                       PrimaryRelativePath, ContentSha256, ContentBytes,
                       SourceLabel, TextCodePage, ImportedUtcMs, UpdatedUtcMs
                FROM Guides WHERE GameId = $gameId ORDER BY Title, Id
                """;
            command.Parameters.AddWithValue("$gameId", gameId.ToString("N"));
            using SqliteDataReader reader = command.ExecuteReader();
            List<Guide> guides = [];
            while (reader.Read())
            {
                guides.Add(ReadGuide(reader));
            }
            return guides;
        }, token);

    public Task<Guide?> GetGuideAsync(Guid guideId, CancellationToken token = default) =>
        ReadAsync<Guide?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
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
        }, token);

    public Task<ReadingState?> GetReadingStateAsync(
        Guid guideId, CancellationToken token = default) =>
        ReadAsync<ReadingState?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT GuideId, LocatorJson, EstimatedFraction,
                       LastOpenedUtcMs, CompletedUtcMs
                FROM ReadingStates WHERE GuideId = $id
                """;
            command.Parameters.AddWithValue("$id", guideId.ToString("N"));
            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read()
                ? new ReadingState(
                    Guid.ParseExact(reader.GetString(0), "N"),
                    NullableString(reader, 1),
                    reader.IsDBNull(2) ? null : reader.GetDouble(2),
                    reader.IsDBNull(3) ? null : FromUnixMilliseconds(reader.GetInt64(3)),
                    reader.IsDBNull(4) ? null : FromUnixMilliseconds(reader.GetInt64(4)))
                : null;
        }, token);

    public Task SaveReadingLocationAsync(
        Guid guideId, string locatorJson, double? estimatedFraction,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(locatorJson) ||
            System.Text.Encoding.UTF8.GetByteCount(locatorJson) > 4096)
        {
            throw new ArgumentException("A bounded reader locator is required.", nameof(locatorJson));
        }
        if (estimatedFraction is double estimate &&
            (!double.IsFinite(estimate) || estimate < 0 || estimate > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedFraction));
        }
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ReadingStates
                SET LocatorJson = $locator, EstimatedFraction = $estimate
                WHERE GuideId = $id
                """;
            command.Parameters.AddWithValue("$id", guideId.ToString("N"));
            command.Parameters.AddWithValue("$locator", locatorJson);
            command.Parameters.AddWithValue("$estimate",
                (object?)estimatedFraction ?? DBNull.Value);
            RequireUpdated(command.ExecuteNonQuery(), "reading state");
        }, token);
    }

    public Task<ReaderPreferences?> GetReaderPreferencesAsync(
        Guid guideId, CancellationToken token = default) =>
        ReadAsync<ReaderPreferences?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT GuideId, TextScale FROM ReaderPreferences WHERE GuideId = $id";
            command.Parameters.AddWithValue("$id", guideId.ToString("N"));
            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read()
                ? new ReaderPreferences(
                    Guid.ParseExact(reader.GetString(0), "N"),
                    reader.IsDBNull(1) ? null : reader.GetDouble(1))
                : null;
        }, token);

    public Task SaveReaderPreferencesAsync(
        Guid guideId, double? textScale, CancellationToken token = default)
    {
        if (textScale is double scale && (!double.IsFinite(scale) || scale < 0.75 || scale > 2))
        {
            throw new ArgumentOutOfRangeException(nameof(textScale));
        }
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ReaderPreferences SET TextScale = $scale WHERE GuideId = $id
                """;
            command.Parameters.AddWithValue("$id", guideId.ToString("N"));
            command.Parameters.AddWithValue("$scale", (object?)textScale ?? DBNull.Value);
            RequireUpdated(command.ExecuteNonQuery(), "reader preferences");
        }, token);
    }

    public Task<AppSettings> GetSettingsAsync(CancellationToken token = default) =>
        ReadAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Key, Value FROM Settings";
            using SqliteDataReader reader = command.ExecuteReader();
            ThemePreference theme = ThemePreference.System;
            Guid? lastGuide = null;
            while (reader.Read())
            {
                string key = reader.GetString(0);
                string value = reader.GetString(1);
                if (key == "Theme")
                {
                    if (!Enum.TryParse(value, out theme) ||
                        !Enum.IsDefined(theme) ||
                        theme.ToString() != value)
                    {
                        throw new InvalidDataException("Stored theme is invalid.");
                    }
                }
                else if (key == "LastActiveGuideId")
                {
                    if (!Guid.TryParseExact(value, "N", out Guid parsed))
                    {
                        throw new InvalidDataException("Stored last-guide ID is invalid.");
                    }
                    lastGuide = parsed;
                }
            }
            return new AppSettings(theme, lastGuide);
        }, token);

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default)
    {
        if (!Enum.IsDefined(settings.Theme))
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using (SqliteCommand theme = connection.CreateCommand())
            {
                theme.Transaction = transaction;
                theme.CommandText = """
                    INSERT INTO Settings (Key, Value) VALUES ('Theme', $value)
                    ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
                    """;
                theme.Parameters.AddWithValue("$value", settings.Theme.ToString());
                theme.ExecuteNonQuery();
            }
            using (SqliteCommand lastGuide = connection.CreateCommand())
            {
                lastGuide.Transaction = transaction;
                lastGuide.CommandText = settings.LastActiveGuideId.HasValue
                    ? """
                      INSERT INTO Settings (Key, Value) VALUES ('LastActiveGuideId', $value)
                      ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
                      """
                    : "DELETE FROM Settings WHERE Key = 'LastActiveGuideId'";
                if (settings.LastActiveGuideId is Guid id)
                {
                    lastGuide.Parameters.AddWithValue("$value", id.ToString("N"));
                }
                lastGuide.ExecuteNonQuery();
            }
            transaction.Commit();
        }, token);
    }

    public ValueTask DisposeAsync()
    {
        writeGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private void Initialize()
    {
        paths.EnsureCreated();
        bool existed = File.Exists(paths.DatabasePath);
        using SqliteConnection connection = OpenConnection(create: true);
        using SqliteCommand version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        long currentVersion = (long)version.ExecuteScalar()!;
        if (currentVersion == 1)
        {
            return;
        }
        if (currentVersion != 0 || existed)
        {
            throw new InvalidDataException(
                $"Library schema version {currentVersion} needs a supported migration or recovery.");
        }
        using (SqliteCommand journal = connection.CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode=WAL";
            journal.ExecuteNonQuery();
        }
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand schema = connection.CreateCommand();
        schema.Transaction = transaction;
        schema.CommandText = LibrarySchema.Version1;
        schema.ExecuteNonQuery();
        transaction.Commit();
    }

    private SqliteConnection OpenConnection(bool create = false)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            using SqliteCommand pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys=ON";
            pragma.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private async Task WriteAsync(Action action, CancellationToken token)
    {
        await writeGate.WaitAsync(token);
        try
        {
            await Task.Run(action, token);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task<T> WriteAsync<T>(Func<T> action, CancellationToken token)
    {
        await writeGate.WaitAsync(token);
        try
        {
            return await Task.Run(action, token);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private static Task<T> ReadAsync<T>(Func<T> action, CancellationToken token) =>
        Task.Run(action, token);

    private static void RequireUpdated(int count, string kind)
    {
        if (count != 1)
        {
            throw new InvalidOperationException($"Cannot update missing {kind}.");
        }
    }

    private static string RequiredText(string value, int limit, string name)
    {
        string trimmed = value?.Trim() ?? "";
        if (trimmed.Length is < 1 || trimmed.Length > limit)
        {
            throw new ArgumentException($"A {name} of 1–{limit} characters is required.", name);
        }
        return trimmed;
    }

    private static string? OptionalText(string? value, int limit, string name)
    {
        string? trimmed = value?.Trim();
        if (trimmed?.Length > limit)
        {
            throw new ArgumentException($"{name} exceeds {limit} characters.", name);
        }
        return trimmed is { Length: 0 } ? null : trimmed;
    }

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTimeOffset FromUnixMilliseconds(long value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(value);

    private static Game ReadGame(SqliteDataReader reader) =>
        new(
            Guid.ParseExact(reader.GetString(0), "N"),
            reader.GetString(1),
            NullableString(reader, 2),
            NullableString(reader, 3),
            FromUnixMilliseconds(reader.GetInt64(4)),
            FromUnixMilliseconds(reader.GetInt64(5)));

    private static Guide ReadGuide(SqliteDataReader reader)
    {
        string formatName = reader.GetString(3);
        if (!Enum.TryParse(formatName, out GuideFormat format) ||
            !Enum.IsDefined(format) || format.ToString() != formatName)
        {
            throw new InvalidDataException("Stored guide format is invalid.");
        }
        return new Guide(
            Guid.ParseExact(reader.GetString(0), "N"),
            Guid.ParseExact(reader.GetString(1), "N"),
            reader.GetString(2),
            format,
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt64(7),
            NullableString(reader, 8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            FromUnixMilliseconds(reader.GetInt64(10)),
            FromUnixMilliseconds(reader.GetInt64(11)));
    }
}
