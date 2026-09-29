using System.Security.Cryptography;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

if (args.Length == 4 &&
    args[0] is "hold-write-lock" or "hold-read-lock")
{
    ManagedPathResolver lockPaths = new(args[1]);
    using SqliteConnection lockConnection = new(new SqliteConnectionStringBuilder
    {
        DataSource = lockPaths.DatabasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false
    }.ToString());
    lockConnection.Open();
    using SqliteCommand lockCommand = lockConnection.CreateCommand();
    bool writeLock = args[0] == "hold-write-lock";
    if (writeLock)
    {
        lockCommand.CommandText = "BEGIN IMMEDIATE";
        lockCommand.ExecuteNonQuery();
    }
    else
    {
        lockCommand.CommandText = "PRAGMA locking_mode=EXCLUSIVE";
        if (lockCommand.ExecuteScalar() is not string mode || mode != "exclusive")
        {
            throw new InvalidOperationException("Could not enable exclusive read lock.");
        }
        lockCommand.CommandText = "SELECT COUNT(*) FROM Games";
        _ = lockCommand.ExecuteScalar();
    }
    try
    {
        File.WriteAllText(args[2], "ready");
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!File.Exists(args[3]) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }
        if (!File.Exists(args[3]))
        {
            Console.Error.WriteLine("Database-lock release timed out.");
            return 3;
        }
    }
    finally
    {
        if (writeLock)
        {
            lockCommand.CommandText = "ROLLBACK";
            lockCommand.ExecuteNonQuery();
        }
    }
    return 0;
}

if (args.Length == 3 && args[0] == "set-material")
{
    if (!Enum.TryParse(args[2], false, out WindowMaterial material) ||
        !Enum.IsDefined(material) || material.ToString() != args[2])
    {
        Console.Error.WriteLine("Material must be Mica, Acrylic, or Solid.");
        return 2;
    }
    await using SqliteLibraryRepository materialRepository =
        new(new ManagedPathResolver(args[1]));
    await materialRepository.InitializeAsync();
    await materialRepository.UpdateSettingsAsync(
        settings => settings with { WindowMaterial = material });
    return 0;
}

if (args.Length == 2 && args[0] == "invalidate-blocked-guide")
{
    ManagedPathResolver fixturePaths = new(args[1]);
    using SqliteConnection connection = new(new SqliteConnectionStringBuilder
    {
        DataSource = fixturePaths.DatabasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false,
        ForeignKeys = true
    }.ToString());
    connection.Open();
    using SqliteTransaction transaction = connection.BeginTransaction();
    using (SqliteCommand clearResume = connection.CreateCommand())
    {
        clearResume.Transaction = transaction;
        clearResume.CommandText = "DELETE FROM Settings WHERE Key = 'LastActiveGuideId'";
        clearResume.ExecuteNonQuery();
    }
    using (SqliteCommand removeGuide = connection.CreateCommand())
    {
        removeGuide.Transaction = transaction;
        removeGuide.CommandText = """
            DELETE FROM Guides
            WHERE Title = 'Blocked Write Guide'
              AND GameId = (
                  SELECT Id FROM Games WHERE Title = 'Route Test Game'
              )
            """;
        if (removeGuide.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(
                "Expected one blocked guide in the shell fixture.");
        }
    }
    transaction.Commit();
    Console.WriteLine("Cleared Resume and invalidated the displayed blocked guide.");
    return 0;
}

if (args.Length == 2 &&
    args[0] is "corrupt-reader-guide" or "restore-reader-guide")
{
    bool corrupt = args[0] == "corrupt-reader-guide";
    ManagedPathResolver fixturePaths = new(args[1]);
    using SqliteConnection connection = new(new SqliteConnectionStringBuilder
    {
        DataSource = fixturePaths.DatabasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false,
        ForeignKeys = true
    }.ToString());
    connection.Open();
    if (corrupt)
    {
        using SqliteCommand allowInvalidFormat = connection.CreateCommand();
        allowInvalidFormat.CommandText = "PRAGMA ignore_check_constraints = ON";
        allowInvalidFormat.ExecuteNonQuery();
    }
    using SqliteTransaction transaction = connection.BeginTransaction();
    if (corrupt)
    {
        using SqliteCommand clearResume = connection.CreateCommand();
        clearResume.Transaction = transaction;
        clearResume.CommandText = "DELETE FROM Settings WHERE Key = 'LastActiveGuideId'";
        clearResume.ExecuteNonQuery();
    }
    using (SqliteCommand updateGuide = connection.CreateCommand())
    {
        updateGuide.Transaction = transaction;
        updateGuide.CommandText = """
            UPDATE Guides SET Format = $newFormat
            WHERE Title = 'Route Test Guide'
              AND Format = $oldFormat
              AND GameId = (
                  SELECT Id FROM Games WHERE Title = 'Route Test Game'
              )
            """;
        updateGuide.Parameters.AddWithValue(
            "$newFormat", corrupt ? "Invalid" : "Txt");
        updateGuide.Parameters.AddWithValue(
            "$oldFormat", corrupt ? "Txt" : "Invalid");
        if (updateGuide.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(
                "Expected one route guide for the Reader render fault.");
        }
    }
    transaction.Commit();
    Console.WriteLine(corrupt
        ? "Cleared Resume and invalidated the Reader metadata read."
        : "Restored the Reader metadata fixture.");
    return 0;
}

if (args.Length != 2 ||
    args[0] is not ("seed" or "stale" or "seed-long" or "seed-second" or
        "seed-design"))
{
    Console.Error.WriteLine(
        "Usage: DesktopGuides.ShellSeed seed|stale|seed-long|seed-second|seed-design " +
        "<app-data-root> " +
        "or invalidate-blocked-guide <app-data-root> " +
        "or corrupt-reader-guide|restore-reader-guide <app-data-root> " +
        "or hold-write-lock|hold-read-lock <app-data-root> <ready-path> <release-path>");
    return 2;
}

ManagedPathResolver paths = new(args[1]);
await using SqliteLibraryRepository repository = new(paths);
await repository.InitializeAsync();

if (args[0] == "stale")
{
    AppSettings current = await repository.GetSettingsAsync();
    await repository.SaveSettingsAsync(
        current with { LastActiveGuideId = Guid.NewGuid() });
    Console.WriteLine("Stale last-guide ID set.");
    return 0;
}

if (args[0] == "seed-long")
{
    Game seededGame = (await repository.ListGamesAsync())
        .Single(game => game.Title == "Route Test Game");
    if ((await repository.ListGuidesAsync(seededGame.Id))
        .Any(guide => guide.Title == "ZZZ Focus Target Guide"))
    {
        throw new InvalidOperationException("Long-list guides were already seeded.");
    }
    long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    for (int index = 0; index < 96; index++)
    {
        await InsertGuideAsync(
            paths, seededGame.Id, Guid.NewGuid(), $"Guide {index:D3}", timestamp);
    }
    Guid tailGuideId = Guid.NewGuid();
    await InsertGuideAsync(
        paths, seededGame.Id, tailGuideId, "ZZZ Focus Target Guide", timestamp);
    AppSettings current = await repository.GetSettingsAsync();
    await repository.SaveSettingsAsync(
        current with { LastActiveGuideId = tailGuideId });
    Console.WriteLine($"Seeded virtualized tail guide {tailGuideId:N}.");
    return 0;
}

if (args[0] == "seed-second")
{
    Game secondGame = await repository.AddGameAsync(
        "Second Test Game", "Windows", null);
    Guid secondGuideId = Guid.NewGuid();
    await InsertGuideAsync(
        paths, secondGame.Id, secondGuideId, "Second Test Guide",
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    Console.WriteLine(
        $"Seeded second game {secondGame.Id:N} and guide {secondGuideId:N}.");
    return 0;
}

if (args[0] == "seed-design")
{
    Game designGame = await repository.AddGameAsync(
        "The Legend of Zelda: Tears of the Kingdom",
        "Nintendo Switch",
        "Keep the main story, shrine routes, and armor upgrades together " +
        "for quick reference while playing.");
    Guid walkthroughId = Guid.NewGuid();
    long designTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    foreach ((Guid guideId, string title) in new[]
             {
                 (walkthroughId, "Complete Story Walkthrough"),
                 (Guid.NewGuid(), "Shrine and Lightroot Checklist"),
                 (Guid.NewGuid(), "Armor Upgrade Materials")
             })
    {
        await InsertGuideAsync(
            paths, designGame.Id, guideId, title, designTimestamp);
    }
    AppSettings designSettings = await repository.GetSettingsAsync();
    await repository.SaveSettingsAsync(
        designSettings with { LastActiveGuideId = walkthroughId });
    Console.WriteLine(
        $"Seeded design-language game {designGame.Id:N} and three guides.");
    return 0;
}

Game game = await repository.AddGameAsync("Route Test Game", "Windows", null);
Guid resumeGuideId = Guid.NewGuid();
Guid blockedGuideId = Guid.NewGuid();
long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

foreach ((Guid guideId, string title) in new[]
         {
             (resumeGuideId, "Route Test Guide"),
             (blockedGuideId, "Blocked Write Guide")
         })
{
    await InsertGuideAsync(paths, game.Id, guideId, title, now);
}

AppSettings settings = await repository.GetSettingsAsync();
await repository.SaveSettingsAsync(settings with { LastActiveGuideId = resumeGuideId });
Console.WriteLine(
    $"Seeded game {game.Id:N}, Resume guide {resumeGuideId:N}, " +
    $"and blocked-write guide {blockedGuideId:N}.");
return 0;

static async Task InsertGuideAsync(
    ManagedPathResolver paths, Guid gameId, Guid guideId, string title, long now)
{
    string guideRoot = paths.GetGuideRoot(guideId);
    Directory.CreateDirectory(guideRoot);
    string content = Path.Combine(guideRoot, "guide.txt");
    await File.WriteAllTextAsync(content, "Test guide.");
    byte[] bytes = await File.ReadAllBytesAsync(content);
    string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

    using SqliteConnection connection = new(new SqliteConnectionStringBuilder
    {
        DataSource = paths.DatabasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false,
        ForeignKeys = true
    }.ToString());
    connection.Open();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO Guides (
            Id, GameId, Title, Format, ManagedRelativeRoot, PrimaryRelativePath,
            ContentSha256, ContentBytes, ImportedUtcMs, UpdatedUtcMs
        ) VALUES (
            $guide, $game, $title, 'Txt', $root, 'guide.txt',
            $hash, $bytes, $now, $now
        );
        INSERT INTO ReadingStates (GuideId) VALUES ($guide);
        INSERT INTO ReaderPreferences (GuideId) VALUES ($guide);
        """;
    command.Parameters.AddWithValue("$guide", guideId.ToString("N"));
    command.Parameters.AddWithValue("$game", gameId.ToString("N"));
    command.Parameters.AddWithValue("$title", title);
    command.Parameters.AddWithValue("$root", $"content/{guideId:N}");
    command.Parameters.AddWithValue("$hash", hash);
    command.Parameters.AddWithValue("$bytes", bytes.LongLength);
    command.Parameters.AddWithValue("$now", now);
    command.ExecuteNonQuery();
}
