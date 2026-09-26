using System.Security.Cryptography;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

if (args.Length == 4 && args[0] == "hold-write-lock")
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
    lockCommand.CommandText = "BEGIN IMMEDIATE";
    lockCommand.ExecuteNonQuery();
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
            Console.Error.WriteLine("Write-lock release timed out.");
            return 3;
        }
    }
    finally
    {
        lockCommand.CommandText = "ROLLBACK";
        lockCommand.ExecuteNonQuery();
    }
    return 0;
}

if (args.Length != 2 || args[0] is not ("seed" or "stale"))
{
    Console.Error.WriteLine(
        "Usage: DesktopGuides.ShellSeed seed|stale <app-data-root> " +
        "or hold-write-lock <app-data-root> <ready-path> <release-path>");
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
    command.Parameters.AddWithValue("$game", game.Id.ToString("N"));
    command.Parameters.AddWithValue("$title", title);
    command.Parameters.AddWithValue("$root", $"content/{guideId:N}");
    command.Parameters.AddWithValue("$hash", hash);
    command.Parameters.AddWithValue("$bytes", bytes.LongLength);
    command.Parameters.AddWithValue("$now", now);
    command.ExecuteNonQuery();
}

AppSettings settings = await repository.GetSettingsAsync();
await repository.SaveSettingsAsync(settings with { LastActiveGuideId = resumeGuideId });
Console.WriteLine(
    $"Seeded game {game.Id:N}, Resume guide {resumeGuideId:N}, " +
    $"and blocked-write guide {blockedGuideId:N}.");
return 0;
