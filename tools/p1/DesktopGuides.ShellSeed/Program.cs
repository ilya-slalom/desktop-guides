using System.Security.Cryptography;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

if (args.Length != 2 || args[0] is not ("seed" or "stale"))
{
    Console.Error.WriteLine("Usage: DesktopGuides.ShellSeed seed|stale <app-data-root>");
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
Guid guideId = Guid.NewGuid();
string guideRoot = paths.GetGuideRoot(guideId);
Directory.CreateDirectory(guideRoot);
string content = Path.Combine(guideRoot, "guide.txt");
await File.WriteAllTextAsync(content, "Test guide.");
byte[] bytes = await File.ReadAllBytesAsync(content);
string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

using (SqliteConnection connection = new(new SqliteConnectionStringBuilder
{
    DataSource = paths.DatabasePath,
    Mode = SqliteOpenMode.ReadWrite,
    Pooling = false,
    ForeignKeys = true
}.ToString()))
{
    connection.Open();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO Guides (
            Id, GameId, Title, Format, ManagedRelativeRoot, PrimaryRelativePath,
            ContentSha256, ContentBytes, ImportedUtcMs, UpdatedUtcMs
        ) VALUES (
            $guide, $game, 'Route Test Guide', 'Txt', $root, 'guide.txt',
            $hash, $bytes, $now, $now
        );
        INSERT INTO ReadingStates (GuideId) VALUES ($guide);
        INSERT INTO ReaderPreferences (GuideId) VALUES ($guide);
        """;
    command.Parameters.AddWithValue("$guide", guideId.ToString("N"));
    command.Parameters.AddWithValue("$game", game.Id.ToString("N"));
    command.Parameters.AddWithValue("$root", $"content/{guideId:N}");
    command.Parameters.AddWithValue("$hash", hash);
    command.Parameters.AddWithValue("$bytes", bytes.LongLength);
    command.Parameters.AddWithValue("$now", now);
    command.ExecuteNonQuery();
}

AppSettings settings = await repository.GetSettingsAsync();
await repository.SaveSettingsAsync(settings with { LastActiveGuideId = guideId });
Console.WriteLine($"Seeded game {game.Id:N} and guide {guideId:N}.");
return 0;
