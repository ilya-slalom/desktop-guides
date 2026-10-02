using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Storage;

public sealed class SqliteLibraryRepository : ILibraryRepository
{
    private sealed record SchemaObject(string Type, string Name, string Table, string Sql);

    private static readonly Lazy<IReadOnlyDictionary<int, IReadOnlyList<SchemaObject>>> ExpectedSchemas =
        new(() => LibrarySchema.Migrations.ToDictionary(
            migration => migration.Version,
            migration => BuildExpectedSchema(migration.Version)));

    private const string GameColumns = """
        Id, Title, Platform, Notes, CreatedUtcMs, UpdatedUtcMs,
        ProviderName, ProviderGameId, MetadataJson, MetadataRetrievedUtcMs, ArtworkRelativePath
        """;

    private readonly ILibraryPaths paths;
    private readonly TimeProvider clock;
    private readonly Action<int>? migrationCheckpoint;
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public SqliteLibraryRepository(ILibraryPaths paths, TimeProvider? clock = null)
    {
        this.paths = paths;
        this.clock = clock ?? TimeProvider.System;
    }

    internal SqliteLibraryRepository(
        ILibraryPaths paths, TimeProvider? clock, Action<int> migrationCheckpoint)
        : this(paths, clock)
    {
        this.migrationCheckpoint = migrationCheckpoint;
    }

    public StartupReconciliationReport? LastStartupReconciliation { get; private set; }

    public Task InitializeAsync(CancellationToken token = default) =>
        WriteAsync(Initialize, token);

    public Task<Game> AddGameAsync(
        string title, string? platform, string? notes, CancellationToken token = default)
    {
        GameDetails details = GameDetails.Create(title, platform, notes);
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
            command.Parameters.AddWithValue("$title", details.Title);
            command.Parameters.AddWithValue(
                "$platform", (object?)details.Platform ?? DBNull.Value);
            command.Parameters.AddWithValue("$notes", (object?)details.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
            return new Game(id, details.Title, details.Platform, details.Notes, now, now);
        }, token);
    }

    public Task<Game> UpdateGameAsync(
        Guid gameId, string title, string? platform, string? notes,
        CancellationToken token = default)
    {
        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("A game ID is required.", nameof(gameId));
        }
        GameDetails details = GameDetails.Create(title, platform, notes);
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            Game game = GetGame(connection, null, gameId)
                ?? throw new InvalidOperationException("This game is no longer in the library.");

            DateTimeOffset now = clock.GetUtcNow();
            using SqliteCommand update = connection.CreateCommand();
            update.CommandText = """
                UPDATE Games
                SET Title = $title, Platform = $platform, Notes = $notes,
                    UpdatedUtcMs = $updated
                WHERE Id = $id
                """;
            update.Parameters.AddWithValue("$id", gameId.ToString("N"));
            update.Parameters.AddWithValue("$title", details.Title);
            update.Parameters.AddWithValue(
                "$platform", (object?)details.Platform ?? DBNull.Value);
            update.Parameters.AddWithValue("$notes", (object?)details.Notes ?? DBNull.Value);
            update.Parameters.AddWithValue("$updated", now.ToUnixTimeMilliseconds());
            RequireUpdated(update.ExecuteNonQuery(), "game");
            return game with
            {
                Title = details.Title,
                Platform = details.Platform,
                Notes = details.Notes,
                UpdatedUtc = now
            };
        }, token);
    }

    public Task<Game?> GetGameAsync(Guid gameId, CancellationToken token = default) =>
        ReadAsync<Game?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            return GetGame(connection, null, gameId);
        }, token);

    public Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken token = default) =>
        ReadAsync<IReadOnlyList<Game>>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {GameColumns}
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

    public Task<Game?> FindLinkedGameAsync(
        string provider, string externalId, CancellationToken token = default) =>
        ReadAsync<Game?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            return FindLinkedGame(connection, null, provider, externalId);
        }, token);

    public Task<Game> AddLinkedGameAsync(NewLinkedGame game, CancellationToken token = default)
    {
        GameDetails details = GameDetails.Create(game.Title, game.Platform, null);
        string metadataJson = GameMetadataJson.Serialize(game.Metadata);
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            if (FindLinkedGame(connection, transaction, game.Link.Provider, game.Link.ExternalId)
                is { } existing)
            {
                throw new DuplicateProviderLinkException(existing.Id);
            }
            DateTimeOffset now = clock.GetUtcNow();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Games (
                    Id, Title, Platform, Notes, CreatedUtcMs, UpdatedUtcMs,
                    ProviderName, ProviderGameId, MetadataJson, MetadataRetrievedUtcMs,
                    ArtworkRelativePath
                ) VALUES ($id, $title, $platform, NULL, $now, $now,
                    $provider, $externalId, $json, $retrieved, $artwork)
                """;
            command.Parameters.AddWithValue("$id", game.Id.ToString("N"));
            command.Parameters.AddWithValue("$title", details.Title);
            command.Parameters.AddWithValue("$platform", (object?)details.Platform ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$provider", game.Link.Provider);
            command.Parameters.AddWithValue("$externalId", game.Link.ExternalId);
            command.Parameters.AddWithValue("$json", metadataJson);
            command.Parameters.AddWithValue("$retrieved", game.Link.RetrievedUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$artwork", (object?)game.ArtworkRelativePath ?? DBNull.Value);
            try { command.ExecuteNonQuery(); }
            catch (SqliteException error) when (error.SqliteExtendedErrorCode == 2067)
            {
                transaction.Rollback();
                Guid existingId = FindLinkedGame(connection, null, game.Link.Provider, game.Link.ExternalId)?.Id
                    ?? throw new InvalidDataException("The provider link conflicted but no game holds it.", error);
                throw new DuplicateProviderLinkException(existingId);
            }
            transaction.Commit();
            return new Game(game.Id, details.Title, details.Platform, null, now, now,
                game.Link, game.Metadata, game.ArtworkRelativePath);
        }, token);
    }

    public Task<Game> UpdateGameMetadataAsync(
        Guid gameId, GameMetadataSnapshot metadata, DateTimeOffset retrievedUtc,
        string? artworkRelativePath, CancellationToken token = default)
    {
        string metadataJson = GameMetadataJson.Serialize(metadata);
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            Game current = GetGame(connection, transaction, gameId) ??
                throw new KeyNotFoundException("The game no longer exists.");
            if (current.Link is null)
            {
                throw new InvalidOperationException("Only linked games have provider metadata.");
            }
            DateTimeOffset now = clock.GetUtcNow();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Games SET MetadataJson = $json, MetadataRetrievedUtcMs = $retrieved,
                    ArtworkRelativePath = $artwork, UpdatedUtcMs = $now
                WHERE Id = $id
                """;
            command.Parameters.AddWithValue("$id", gameId.ToString("N"));
            command.Parameters.AddWithValue("$json", metadataJson);
            command.Parameters.AddWithValue("$retrieved", retrievedUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$artwork", (object?)artworkRelativePath ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
            transaction.Commit();
            return current with
            {
                Metadata = metadata,
                Link = current.Link with { RetrievedUtc = retrievedUtc },
                ArtworkRelativePath = artworkRelativePath,
                UpdatedUtc = now
            };
        }, token);
    }

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

    // A game's last activity is the latest of its creation, any guide import
    // and any guide open. ReadingStates is keyed by GuideId, so the second
    // join adds at most one row per guide and COUNT stays exact. Guide titles
    // come from a second statement in the same read transaction, so they
    // match the counts.
    public Task<IReadOnlyList<LibraryGameSummary>> ListGameSummariesAsync(
        CancellationToken token = default) =>
        ReadAsync<IReadOnlyList<LibraryGameSummary>>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
            List<(Game Game, int Count, DateTimeOffset Activity)> rows = [];
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT g.Id, g.Title, g.Platform, g.Notes, g.CreatedUtcMs, g.UpdatedUtcMs,
                           g.ProviderName, g.ProviderGameId, g.MetadataJson,
                           g.MetadataRetrievedUtcMs, g.ArtworkRelativePath,
                           COUNT(gu.Id),
                           MAX(g.CreatedUtcMs,
                               COALESCE(MAX(gu.ImportedUtcMs), 0),
                               COALESCE(MAX(rs.LastOpenedUtcMs), 0)) AS LastActivityUtcMs
                    FROM Games g
                    LEFT JOIN Guides gu ON gu.GameId = g.Id
                    LEFT JOIN ReadingStates rs ON rs.GuideId = gu.Id
                    GROUP BY g.Id
                    ORDER BY LastActivityUtcMs DESC, g.Title, g.Id
                    """;
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add((ReadGame(reader), reader.GetInt32(11), FromUnixMilliseconds(reader.GetInt64(12))));
                }
            }

            Dictionary<Guid, List<string>> titles = [];
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT GameId, Title FROM Guides ORDER BY GameId, Title, Id";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    Guid gameId = Guid.ParseExact(reader.GetString(0), "N");
                    if (!titles.TryGetValue(gameId, out List<string>? list))
                    {
                        titles[gameId] = list = [];
                    }
                    list.Add(reader.GetString(1));
                }
            }

            transaction.Commit();
            return rows.Select(row => new LibraryGameSummary(
                    row.Game, row.Count, row.Activity,
                    titles.TryGetValue(row.Game.Id, out List<string>? list) ? list : []))
                .ToList();
        }, token);

    public Task<IReadOnlyList<GuideSummary>> ListGuideSummariesAsync(
        Guid gameId, CancellationToken token = default) =>
        ReadAsync<IReadOnlyList<GuideSummary>>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT gu.Id, gu.GameId, gu.Title, gu.Format, gu.ManagedRelativeRoot,
                       gu.PrimaryRelativePath, gu.ContentSha256, gu.ContentBytes,
                       gu.SourceLabel, gu.TextCodePage, gu.ImportedUtcMs, gu.UpdatedUtcMs,
                       rs.GuideId, rs.LocatorJson, rs.EstimatedFraction,
                       rs.LastOpenedUtcMs, rs.CompletedUtcMs
                FROM Guides gu
                LEFT JOIN ReadingStates rs ON rs.GuideId = gu.Id
                WHERE gu.GameId = $gameId
                ORDER BY MAX(gu.ImportedUtcMs, COALESCE(rs.LastOpenedUtcMs, 0)) DESC,
                         gu.Title, gu.Id
                """;
            command.Parameters.AddWithValue("$gameId", gameId.ToString("N"));
            using SqliteDataReader reader = command.ExecuteReader();
            List<GuideSummary> summaries = [];
            while (reader.Read())
            {
                summaries.Add(new GuideSummary(
                    ReadGuide(reader), reader.IsDBNull(12) ? null : ReadReadingState(reader, 12)));
            }
            return summaries;
        }, token);

    public Task<Guide?> GetGuideAsync(Guid guideId, CancellationToken token = default) =>
        ReadAsync<Guide?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            return GetGuide(connection, guideId);
        }, token);

    public Task<IReadOnlyList<GuideAsset>> GetGuideAssetsAsync(Guid guideId, CancellationToken token = default) =>
        ReadAsync<IReadOnlyList<GuideAsset>>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT RequestPath, RelativePath, Kind, ByteCount, Sha256
                FROM GuideAssets WHERE GuideId = $id ORDER BY RequestPath
                """;
            command.Parameters.AddWithValue("$id", guideId.ToString("N"));
            using SqliteDataReader reader = command.ExecuteReader();
            List<GuideAsset> assets = [];
            while (reader.Read())
            {
                assets.Add(new GuideAsset(
                    reader.GetString(0), reader.GetString(1),
                    Enum.Parse<GuideAssetKind>(reader.GetString(2)),
                    reader.GetInt64(3), reader.GetString(4)));
            }
            return assets;
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

    private const string FindGuideSql = """
        SELECT Id, GameId, Title, Format, ManagedRelativeRoot,
               PrimaryRelativePath, ContentSha256, ContentBytes,
               SourceLabel, TextCodePage, ImportedUtcMs, UpdatedUtcMs
        FROM Guides
        WHERE GameId = $gameId AND Format = $format AND ContentSha256 = $sha
        ORDER BY ImportedUtcMs, Id
        LIMIT 1
        """;

    /// <summary>The oldest guide in the game with this format and content hash, or null.</summary>
    public Task<Guide?> FindGuideByFingerprintAsync(
        Guid gameId, GuideFormat format, string contentSha256, CancellationToken token = default) =>
        ReadAsync<Guide?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            return FindGuide(connection, gameId, format, contentSha256);
        }, token);

    private static Guide? FindGuide(
        SqliteConnection connection, Guid gameId, GuideFormat format, string contentSha256)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = FindGuideSql;
        command.Parameters.AddWithValue("$gameId", gameId.ToString("N"));
        command.Parameters.AddWithValue("$format", format.ToString());
        command.Parameters.AddWithValue("$sha", contentSha256);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadGuide(reader) : null;
    }

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
            return reader.Read() ? ReadReadingState(reader, 0) : null;
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
            return ReadSettings(connection, null);
        }, token);

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default)
    {
        ValidateSettings(settings);
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            WriteSettings(connection, transaction, settings);
            transaction.Commit();
        }, token);
    }

    public Task<AppSettings> UpdateSettingsAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            AppSettings updated = update(ReadSettings(connection, transaction));
            ValidateSettings(updated);
            WriteSettings(connection, transaction, updated);
            transaction.Commit();
            return updated;
        }, token);
    }

    private static void ValidateSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Theme) || !Enum.IsDefined(settings.WindowMaterial))
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }
    }

    private static AppSettings ReadSettings(
        SqliteConnection connection, SqliteTransaction? transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Key, Value FROM Settings";
        using SqliteDataReader reader = command.ExecuteReader();
        ThemePreference theme = ThemePreference.System;
        Guid? lastGuide = null;
        WindowMaterial material = WindowMaterial.Mica;
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
            else if (key == "WindowMaterial")
            {
                // An unknown material, such as one from a newer build, falls back to Mica.
                if (!Enum.TryParse(value, out material) ||
                    !Enum.IsDefined(material) ||
                    material.ToString() != value)
                {
                    material = WindowMaterial.Mica;
                }
            }
        }
        return new AppSettings(theme, lastGuide, material);
    }

    private static void WriteSettings(
        SqliteConnection connection, SqliteTransaction transaction, AppSettings settings)
    {
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
        using (SqliteCommand material = connection.CreateCommand())
        {
            material.Transaction = transaction;
            material.CommandText = """
                INSERT INTO Settings (Key, Value) VALUES ('WindowMaterial', $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
                """;
            material.Parameters.AddWithValue("$value", settings.WindowMaterial.ToString());
            material.ExecuteNonQuery();
        }
    }

    public ValueTask DisposeAsync()
    {
        writeGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private void Initialize()
    {
        LastStartupReconciliation = null;
        paths.EnsureCreated();
        using SqliteConnection connection = OpenConnection(create: true);
        MigrateOrValidate(connection);
        StartupReconciliationReport report = new FileOperationReconciler(paths).Run(connection);
        int artworkReview = new ManagedArtworkStore(paths).Sweep(ReadArtworkReferences(connection));
        LastStartupReconciliation = report with { ArtworkReviewCount = artworkReview };
    }

    private static HashSet<string> ReadArtworkReferences(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ArtworkRelativePath FROM Games WHERE ArtworkRelativePath IS NOT NULL";
        using SqliteDataReader reader = command.ExecuteReader();
        HashSet<string> references = new(StringComparer.Ordinal);
        while (reader.Read()) references.Add(reader.GetString(0));
        return references;
    }

    private void MigrateOrValidate(SqliteConnection connection)
    {
        using SqliteCommand version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        long currentVersion = (long)version.ExecuteScalar()!;
        if (currentVersion > LibrarySchema.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Library schema version {currentVersion} is newer than this app supports. Update Desktop Guides.");
        }
        if (currentVersion == LibrarySchema.CurrentVersion)
        {
            ValidateDatabase(connection, null, LibrarySchema.CurrentVersion);
            return;
        }
        if (currentVersion < 0 ||
            (currentVersion == 0 && !IsRecoverableEmptyDatabase(connection)))
        {
            throw new InvalidDataException(
                $"Library schema version {currentVersion} needs a supported migration or recovery.");
        }

        if (currentVersion > 0)
        {
            ValidateDatabase(connection, null, (int)currentVersion);
        }

        string? recoveryCopy = currentVersion > 0
            ? CreateRecoveryCopy(connection, (int)currentVersion)
            : null;
        try
        {
            using (SqliteCommand journal = connection.CreateCommand())
            {
                journal.CommandText = "PRAGMA journal_mode=WAL";
                journal.ExecuteNonQuery();
            }
            using SqliteTransaction transaction = connection.BeginTransaction();
            foreach ((int nextVersion, string sql) in LibrarySchema.Migrations)
            {
                if (nextVersion <= currentVersion)
                {
                    continue;
                }
                using SqliteCommand migration = connection.CreateCommand();
                migration.Transaction = transaction;
                migration.CommandText = sql;
                migration.ExecuteNonQuery();
                migrationCheckpoint?.Invoke(nextVersion);
            }
            ValidateDatabase(connection, transaction, LibrarySchema.CurrentVersion);
            transaction.Commit();
        }
        catch (Exception error) when (recoveryCopy is not null)
        {
            throw new InvalidDataException(
                $"Library upgrade failed. Pre-upgrade recovery copy: {recoveryCopy}",
                error);
        }
    }

    private string CreateRecoveryCopy(SqliteConnection connection, int version)
    {
        string path = Path.Combine(paths.RecoveryRoot,
            $"library-v{version}-{clock.GetUtcNow():yyyyMMddHHmmss}-{Guid.NewGuid():N}.sqlite");
        // Reserve the generated name without opening an existing file or link.
        using (FileStream created = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
        }
        try
        {
            using SqliteConnection backup = new(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());
            backup.Open();
            connection.BackupDatabase(backup);
            ValidateDatabase(backup, null, version);
            return path;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private static void ValidateDatabase(
        SqliteConnection connection, SqliteTransaction? transaction, int expectedVersion)
    {
        using SqliteCommand check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "PRAGMA user_version";
        if ((long)check.ExecuteScalar()! != expectedVersion)
        {
            throw new InvalidDataException("Library database schema version is inconsistent.");
        }
        check.CommandText = "PRAGMA integrity_check";
        using (SqliteDataReader integrity = check.ExecuteReader())
        {
            if (!integrity.Read() ||
                !string.Equals(integrity.GetString(0), "ok", StringComparison.Ordinal) ||
                integrity.Read())
            {
                throw new InvalidDataException("Library database failed its integrity check.");
            }
        }
        check.CommandText = "PRAGMA foreign_key_check";
        using (SqliteDataReader foreignKeys = check.ExecuteReader())
        {
            if (foreignKeys.Read())
            {
                throw new InvalidDataException("Library database contains orphaned records.");
            }
        }
        if (!ExpectedSchemas.Value.TryGetValue(expectedVersion, out IReadOnlyList<SchemaObject>? expectedSchema))
        {
            throw new InvalidDataException("Unsupported library schema version.");
        }
        if (!ReadSchema(connection, transaction).SequenceEqual(expectedSchema))
        {
            throw new InvalidDataException("Library database schema is incomplete or altered.");
        }
    }

    private static IReadOnlyList<SchemaObject> BuildExpectedSchema(int version)
    {
        using SqliteConnection reference = new(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:",
            Pooling = false
        }.ToString());
        reference.Open();
        using SqliteCommand migration = reference.CreateCommand();
        foreach ((int nextVersion, string sql) in LibrarySchema.Migrations)
        {
            if (nextVersion > version) break;
            migration.CommandText = sql;
            migration.ExecuteNonQuery();
        }
        return ReadSchema(reference, null);
    }

    private static IReadOnlyList<SchemaObject> ReadSchema(
        SqliteConnection connection, SqliteTransaction? transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT type, name, tbl_name, sql FROM sqlite_schema
            WHERE sql IS NOT NULL AND name NOT GLOB 'sqlite_*'
            ORDER BY type, name
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        List<SchemaObject> objects = [];
        while (reader.Read())
        {
            objects.Add(new SchemaObject(
                reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3)));
        }
        return objects;
    }

    private static bool IsRecoverableEmptyDatabase(SqliteConnection connection)
    {
        using SqliteCommand check = connection.CreateCommand();
        check.CommandText = "PRAGMA application_id";
        if ((long)check.ExecuteScalar()! != 0)
        {
            return false;
        }

        check.CommandText = "PRAGMA integrity_check(1)";
        if (check.ExecuteScalar() is not string result ||
            !string.Equals(result, "ok", StringComparison.Ordinal))
        {
            return false;
        }

        check.CommandText = "SELECT 1 FROM sqlite_schema LIMIT 1";
        return check.ExecuteScalar() is null;
    }

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
        public Guid? FindGuide(Guid gameId, GuideFormat format, string contentSha256)
        {
            using SqliteConnection connection = owner.OpenConnection();
            return SqliteLibraryRepository.FindGuide(connection, gameId, format, contentSha256)?.Id;
        }

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
            foreach (GuideAsset asset in guide.Assets ?? [])
            {
                using SqliteCommand row = connection.CreateCommand();
                row.Transaction = transaction;
                row.CommandText = """
                    INSERT INTO GuideAssets (GuideId, RequestPath, RelativePath, Kind, ByteCount, Sha256)
                    VALUES ($id, $request, $relative, $kind, $bytes, $sha)
                    """;
                row.Parameters.AddWithValue("$id", id);
                row.Parameters.AddWithValue("$request", asset.RequestPath);
                row.Parameters.AddWithValue("$relative", asset.RelativePath);
                row.Parameters.AddWithValue("$kind", asset.Kind.ToString());
                row.Parameters.AddWithValue("$bytes", asset.ByteCount);
                row.Parameters.AddWithValue("$sha", asset.Sha256);
                row.ExecuteNonQuery();
            }
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

        public bool IsPending(Guid guideId)
        {
            using SqliteConnection connection = owner.OpenConnection();
            return FileOperationReconciler.ClaimsGuide(connection, guideId);
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

    private SqliteConnection OpenConnection(bool create = false)
    {
        paths.ValidateDatabasePath();
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

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTimeOffset FromUnixMilliseconds(long value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(value);

    private static Game? FindLinkedGame(
        SqliteConnection connection, SqliteTransaction? transaction, string provider, string externalId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {GameColumns} FROM Games
            WHERE ProviderName = $provider AND ProviderGameId = $externalId
            """;
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$externalId", externalId);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadGame(reader) : null;
    }

    private static Game? GetGame(
        SqliteConnection connection, SqliteTransaction? transaction, Guid gameId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {GameColumns} FROM Games WHERE Id = $id";
        command.Parameters.AddWithValue("$id", gameId.ToString("N"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadGame(reader) : null;
    }

    private static Game ReadGame(SqliteDataReader reader)
    {
        string? provider = NullableString(reader, 6);
        ProviderGameLink? link = provider is null
            ? null
            : new ProviderGameLink(
                provider,
                reader.GetString(7),
                FromUnixMilliseconds(reader.IsDBNull(9) ? 0 : reader.GetInt64(9)));
        return new Game(
            Guid.ParseExact(reader.GetString(0), "N"),
            reader.GetString(1),
            NullableString(reader, 2),
            NullableString(reader, 3),
            FromUnixMilliseconds(reader.GetInt64(4)),
            FromUnixMilliseconds(reader.GetInt64(5)),
            link,
            GameMetadataJson.TryParse(NullableString(reader, 8)),
            NullableString(reader, 10));
    }

    private static ReadingState ReadReadingState(SqliteDataReader reader, int first) => new(
        Guid.ParseExact(reader.GetString(first), "N"),
        NullableString(reader, first + 1),
        reader.IsDBNull(first + 2) ? null : reader.GetDouble(first + 2),
        reader.IsDBNull(first + 3) ? null : FromUnixMilliseconds(reader.GetInt64(first + 3)),
        reader.IsDBNull(first + 4) ? null : FromUnixMilliseconds(reader.GetInt64(first + 4)));

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
