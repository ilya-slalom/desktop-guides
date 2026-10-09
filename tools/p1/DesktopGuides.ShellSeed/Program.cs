using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

if (args.Length is 4 or 5 &&
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
        // The holder outlasts a caller's 30 s SQLite timeout when asked to.
        int holdSeconds = args.Length == 5 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 30;
        DateTime deadline = DateTime.UtcNow.AddSeconds(holdSeconds);
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

if (args.Length == 2 && args[0] == "describe-theme")
{
    await using SqliteLibraryRepository themeRepository =
        new(new ManagedPathResolver(args[1]));
    await themeRepository.InitializeAsync();
    Console.WriteLine((await themeRepository.GetSettingsAsync()).Theme);
    return 0;
}

if (args.Length == 2 && args[0] == "seed-linked-game")
{
    ManagedPathResolver linkedPaths = new(args[1]);
    await using SqliteLibraryRepository linkedRepository = new(linkedPaths);
    await linkedRepository.InitializeAsync();
    Guid gameId = Guid.NewGuid();
    StoredArtwork cover = await new ManagedArtworkStore(linkedPaths)
        .StoreAsync(gameId, SolidPng(60, 90, 0x2E, 0x5E, 0x8C), CancellationToken.None);
    GameMetadataSnapshot snapshot = new(
        GameMetadataSnapshot.CurrentSchemaVersion,
        "A seeded summary that the offline check reads back.",
        new DateOnly(2004, 11, 16),
        ["Shooter"],
        ["Seed Developer"],
        ["Seed Publisher"],
        ["PC (Microsoft Windows)", "PlayStation 2"],
        "https://www.igdb.com/games/seeded-linked-game",
        GameTypeTag.MainGame,
        "SteamGridDB");
    await linkedRepository.AddLinkedGameAsync(new NewLinkedGame(
        gameId, "Seeded Linked Game", null,
        new ProviderGameLink(ProviderGameLink.Igdb, "900001", DateTimeOffset.UtcNow),
        snapshot, cover.RelativePath), CancellationToken.None);
    Console.WriteLine($"Seeded linked game {gameId:N}.");
    return 0;
}

if (args.Length == 2 && args[0] == "describe-providers")
{
    ManagedPathResolver describePaths = new(args[1]);
    await using SqliteLibraryRepository describeRepository = new(describePaths);
    await describeRepository.InitializeAsync();
    ManagedArtworkStore store = new(describePaths);
    var games = (await describeRepository.ListGamesAsync()).Select(game => new
    {
        game.Title,
        game.Platform,
        game.Notes,
        Provider = game.Link?.Provider,
        ExternalId = game.Link?.ExternalId,
        HasMetadata = game.Metadata is not null,
        ArtworkSource = game.Metadata?.ArtworkSource,
        game.ArtworkRelativePath,
        ArtworkExists = game.ArtworkRelativePath is { } path && store.ResolveFile(path) is not null,
    });
    int CountFiles(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count()
        : 0;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Games = games,
        ArtworkFiles = CountFiles(describePaths.ArtworkRoot),
        StagingFiles = CountFiles(describePaths.ArtworkStagingRoot),
        CredentialBlobExists = File.Exists(Path.Combine(args[1], "providers.bin")),
    }));
    return 0;
}

if (args.Length == 2 && args[0] == "describe-import")
{
    ManagedPathResolver importPaths = new(args[1]);
    using SqliteConnection importConnection = new(new SqliteConnectionStringBuilder
    {
        DataSource = importPaths.DatabasePath,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString());
    importConnection.Open();
    long Scalar(string sql)
    {
        using SqliteCommand command = importConnection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
    int CountEntries(string root) => Directory.Exists(root)
        ? Directory.EnumerateFileSystemEntries(root).Count()
        : 0;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Guides = Scalar("SELECT COUNT(*) FROM Guides"),
        FileOperations = Scalar("SELECT COUNT(*) FROM FileOperations"),
        StagingEntries = CountEntries(importPaths.StagingRoot),
        ContentEntries = CountEntries(importPaths.ContentRoot),
        TrashEntries = CountEntries(importPaths.TrashRoot),
        ReadingStates = Scalar("SELECT COUNT(*) FROM ReadingStates"),
        ReaderPreferences = Scalar("SELECT COUNT(*) FROM ReaderPreferences"),
        LegacyTextGuides = Scalar("SELECT COUNT(*) FROM Guides WHERE Format = 'Txt' AND TextCodePage = 437"),
    }));
    return 0;
}

if (args.Length == 2 && args[0] == "describe-actions")
{
    // Read-only, without InitializeAsync: its artwork sweep would hide a
    // removed game's leftover artwork folder (ruling 20).
    ManagedPathResolver actionsPaths = new(args[1]);
    using SqliteConnection actionsConnection = new(new SqliteConnectionStringBuilder
    {
        DataSource = actionsPaths.DatabasePath,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString());
    actionsConnection.Open();
    List<T> Rows<T>(string sql, Func<SqliteDataReader, T> read)
    {
        using SqliteCommand command = actionsConnection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        List<T> rows = [];
        while (reader.Read())
        {
            rows.Add(read(reader));
        }
        return rows;
    }
    var actionsGames = Rows(
        "SELECT Id, Title, ProviderGameId, ArtworkRelativePath FROM Games ORDER BY Title",
        reader => new
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            ExternalId = reader.IsDBNull(2) ? null : reader.GetString(2),
            ArtworkRelativePath = reader.IsDBNull(3) ? null : reader.GetString(3),
        });
    var actionsGuides = Rows(
        "SELECT GameId, Id FROM Guides",
        reader => (GameId: reader.GetString(0), GuideId: reader.GetString(1)));
    ManagedArtworkStore actionsStore = new(actionsPaths);
    string[] artworkFolders = Directory.Exists(actionsPaths.ArtworkRoot)
        ? [.. Directory.EnumerateDirectories(actionsPaths.ArtworkRoot).Select(folder => Path.GetFileName(folder))]
        : [];
    string[] contentDirectories = Directory.Exists(actionsPaths.ContentRoot)
        ? [.. Directory.EnumerateDirectories(actionsPaths.ContentRoot).Select(folder => Path.GetFileName(folder))]
        : [];
    int trashEntries = Directory.Exists(actionsPaths.TrashRoot)
        ? Directory.EnumerateFileSystemEntries(actionsPaths.TrashRoot).Count()
        : 0;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        GameCount = actionsGames.Count,
        Games = actionsGames.Select(row => new
        {
            row.Id,
            row.Title,
            row.ExternalId,
            row.ArtworkRelativePath,
            ArtworkExists = row.ArtworkRelativePath is { } path && actionsStore.ResolveFile(path) is not null,
            GuideIds = actionsGuides.Where(guide => guide.GameId == row.Id).Select(guide => guide.GuideId),
        }),
        ArtworkFolders = artworkFolders,
        ContentDirectories = contentDirectories,
        TrashEntries = trashEntries,
        FileOperations = Rows("SELECT COUNT(*) FROM FileOperations", reader => reader.GetInt64(0)).Single(),
        ReadingStates = Rows(
            "SELECT GuideId, EstimatedFraction, LastOpenedUtcMs FROM ReadingStates",
            reader => new
            {
                GuideId = reader.GetString(0),
                EstimatedFraction = reader.IsDBNull(1) ? (double?)null : reader.GetDouble(1),
                LastOpenedUtcMs = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2),
            }),
        LastActiveGuideId = Rows(
            "SELECT Value FROM Settings WHERE Key = 'LastActiveGuideId'",
            reader => reader.GetString(0)).FirstOrDefault(),
    }));
    return 0;
}

if (args.Length == 3 && args[0] == "check-igdb-fields")
{
    Dictionary<string, string> labelled = File.ReadLines(args[1])
        .Select(line => line.Split(':', 2))
        .Where(parts => parts.Length == 2)
        .GroupBy(parts => parts[0].Trim().ToLowerInvariant())
        .ToDictionary(group => group.Key, group => group.Last()[1].Trim());
    if (!labelled.TryGetValue("igdb client id", out string? clientId) ||
        !labelled.TryGetValue("igdb client secret", out string? clientSecret))
    {
        Console.Error.WriteLine("The IGDB credential file has no client id or client secret line.");
        return 2;
    }
    SortedSet<string> fields = [];
    foreach (string fixture in Directory.EnumerateFiles(args[2], "igdb-*.json"))
    {
        CollectFieldPaths(JsonNode.Parse(File.ReadAllText(fixture)), string.Empty, fields);
    }
    using ProviderHttp http = new();
    using TwitchTokenSource tokens = new(http);
    string token = await tokens.GetTokenAsync(
        new IgdbCredentials(clientId, clientSecret), false, CancellationToken.None);
    string body = $"fields {string.Join(",", fields)}; search \"Half-Life\"; limit 1;";
    ProviderResponse response = await http.SendAsync(
        HttpMethod.Post, new Uri("https://api.igdb.com/v4/games"), request =>
        {
            request.Headers.Add("Client-ID", clientId);
            request.Headers.Authorization = new("Bearer", token);
            request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
        }, ProviderHttp.MaxJsonBytes, CancellationToken.None);
    if (response.StatusCode != HttpStatusCode.OK ||
        JsonNode.Parse(response.Body) is not JsonArray { Count: > 0 })
    {
        Console.Error.WriteLine(
            $"IGDB rejected the fixture fields (HTTP {(int)response.StatusCode}): " +
            Encoding.UTF8.GetString(response.Body));
        return 1;
    }
    Console.WriteLine($"IGDB accepted {fields.Count} fixture fields: {string.Join(",", fields)}");
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

if (args.Length == 3 && args[0] == "seed-txt-reader")
{
    ManagedPathResolver textPaths = new(args[1]);
    await using SqliteLibraryRepository textRepository = new(textPaths);
    await textRepository.InitializeAsync();
    if ((await textRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The TXT reader seed needs an empty library.");
    }
    string fixtures = Path.GetFullPath(args[2]);
    byte[] Fixture(string relative) =>
        File.ReadAllBytes(Path.Combine(fixtures, relative.Replace('/', Path.DirectorySeparatorChar)));
    long textNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    Game textGame = await textRepository.AddGameAsync("Text Reader Game", null, null);
    await InsertTextGuideAsync(textPaths, textGame.Id, Guid.NewGuid(), "ASCII Map Guide", textNow,
        Fixture("p0/txt-ascii.txt"));
    await InsertTextGuideAsync(textPaths, textGame.Id, Guid.NewGuid(), "Long Text Guide", textNow,
        Fixture("p0/generated/txt-long.txt"));
    await InsertTextGuideAsync(textPaths, textGame.Id, Guid.NewGuid(), "Numbered Lines Guide", textNow,
        Fixture("p1/txt-numbered.txt"));
    Guid missingGuideId = Guid.NewGuid();
    await InsertTextGuideAsync(textPaths, textGame.Id, missingGuideId, "Missing File Guide", textNow,
        Fixture("p0/txt-ascii.txt"));
    File.Delete(Path.Combine(textPaths.GetGuideRoot(missingGuideId), "guide.txt"));
    await InsertGuideAsync(textPaths, textGame.Id, Guid.NewGuid(), "Web Page Guide", textNow,
        "Html", "guide.html");
    Console.WriteLine("Seeded the TXT reader game with five guides.");
    return 0;
}

if (args.Length == 3 && args[0] == "seed-html-reader")
{
    ManagedPathResolver htmlPaths = new(args[1]);
    await using SqliteLibraryRepository htmlRepository = new(htmlPaths);
    await htmlRepository.InitializeAsync();
    if ((await htmlRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The HTML reader seed needs an empty library.");
    }
    string canaries = Path.Combine(Path.GetFullPath(args[2]), "p1", "html-canary");
    Game htmlGame = await htmlRepository.AddGameAsync("Web Reader Game", null, null);
    GuideImportValidator validator = new();
    GuideImportPublisher publisher = new(htmlRepository, htmlPaths);
    string staging = Path.Combine(Path.GetTempPath(), "desktop-guides-html-seed-" + Guid.NewGuid().ToString("N"));
    try
    {
        // Copies one canary guide, points its cross-guide references at the
        // other guide's origin, and imports it as a user would.
        async Task<Guid> PublishCanaryAsync(string guide, string title, Guid? otherGuide)
        {
            string source = Path.Combine(canaries, guide);
            string target = Path.Combine(staging, guide);
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string copy = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy);
            }
            // Guide B is a "Save Page As, Complete" export named after its page title.
            string entry = Directory.GetFiles(target, "*.html").Single();
            if (otherGuide is Guid other)
            {
                File.WriteAllText(entry, File.ReadAllText(entry).Replace(
                    "__GUIDE_B_ORIGIN__", GuideWebOrigin.OriginFor(other).AbsoluteUri.TrimEnd('/'),
                    StringComparison.Ordinal));
            }
            ImportInspection inspection = await validator.InspectAsync(entry, CancellationToken.None);
            if (inspection is not ImportReady ready)
            {
                throw new InvalidOperationException($"The {guide} canary guide failed the import preview: {inspection}.");
            }
            return await publisher.PublishAsync(
                ready.Manifest, htmlGame.Id, title, false, null, CancellationToken.None);
        }

        Guid guideB = await PublishCanaryAsync("b", "Canary Guide B", null);
        Guid guideA = await PublishCanaryAsync("a", "Canary Guide A", guideB);
        // The runtime-missing pass checks that TXT guides still open.
        await InsertTextGuideAsync(htmlPaths, htmlGame.Id, Guid.NewGuid(), "Plain Text Guide",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            File.ReadAllBytes(Path.Combine(Path.GetFullPath(args[2]), "p0", "txt-ascii.txt")));
        Guide seededB = (await htmlRepository.GetGuideAsync(guideB))!;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            guideA = guideA.ToString("N"),
            guideB = guideB.ToString("N"),
            // The runtime-missing pass deletes it to show the Missing error.
            guideBEntry = htmlPaths.ResolveExistingGuideFile(guideB, seededB.PrimaryRelativePath),
        }));
    }
    finally
    {
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
    }
    return 0;
}

if (args.Length == 3 && args[0] == "seed-html-position")
{
    ManagedPathResolver positionPaths = new(args[1]);
    await using SqliteLibraryRepository positionRepository = new(positionPaths);
    await positionRepository.InitializeAsync();
    if ((await positionRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The HTML position seed needs an empty library.");
    }
    string fixtures = Path.Combine(Path.GetFullPath(args[2]), "p1");
    Game positionGame = await positionRepository.AddGameAsync("Web Position Game", null, null);
    GuideImportValidator positionValidator = new();
    GuideImportPublisher positionPublisher = new(positionRepository, positionPaths);
    async Task<Guid> PublishLongAsync(string folder, string title)
    {
        ImportInspection inspection = await positionValidator.InspectAsync(
            Path.Combine(fixtures, folder, "guide.html"), CancellationToken.None);
        if (inspection is not ImportReady ready)
        {
            throw new InvalidOperationException($"The {folder} guide failed the import preview: {inspection}.");
        }
        return await positionPublisher.PublishAsync(
            ready.Manifest, positionGame.Id, title, false, null, CancellationToken.None);
    }
    Guid guideLong = await PublishLongAsync("html-long", "Long Web Guide");
    Guid guideChanged = await PublishLongAsync("html-long-changed", "Changed Long Web Guide");
    // T14.3: an image-only guide, whose place comes back by fraction only.
    Guid guidePictures = await PublishLongAsync("html-pictures", "Picture Web Guide");
    // A header that stays on screen, as wiki saves have.
    Guid guideFixedHeader = await PublishLongAsync("html-fixed-header", "Fixed Header Web Guide");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        guideLong = guideLong.ToString("N"),
        guideChanged = guideChanged.ToString("N"),
        guidePictures = guidePictures.ToString("N"),
        guideFixedHeader = guideFixedHeader.ToString("N")
    }));
    return 0;
}

if (args.Length == 3 && args[0] == "seed-html-theme")
{
    // One styled guide, imported as a user would. T09.2's passes set its
    // stored theme and scale with set-html-appearance.
    ManagedPathResolver themePaths = new(args[1]);
    await using SqliteLibraryRepository themeLibrary = new(themePaths);
    await themeLibrary.InitializeAsync();
    if ((await themeLibrary.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The HTML theme seed needs an empty library.");
    }
    string themeEntry = Path.Combine(Path.GetFullPath(args[2]), "p1", "html-theme", "guide.html");
    Game themeGame = await themeLibrary.AddGameAsync("Web Theme Game", null, null);
    ImportInspection themeInspection = await new GuideImportValidator().InspectAsync(themeEntry, CancellationToken.None);
    if (themeInspection is not ImportReady themeReady)
    {
        throw new InvalidOperationException($"The theme guide failed the import preview: {themeInspection}.");
    }
    Guid themeGuide = await new GuideImportPublisher(themeLibrary, themePaths).PublishAsync(
        themeReady.Manifest, themeGame.Id, "Theme Web Guide", false, null, CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(new { guide = themeGuide.ToString("N") }));
    return 0;
}

if (args.Length == 5 && args[0] == "set-html-appearance")
{
    if (!Enum.TryParse(args[3], false, out ThemePreference storedTheme) ||
        !Enum.IsDefined(storedTheme) || storedTheme.ToString() != args[3])
    {
        Console.Error.WriteLine("Theme must be System, Light, or Dark.");
        return 2;
    }
    double? storedScale = args[4] == "default"
        ? null
        : double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
    Guid appearanceGuide = Guid.ParseExact(args[2], "N");
    await using SqliteLibraryRepository appearanceLibrary = new(new ManagedPathResolver(args[1]));
    await appearanceLibrary.InitializeAsync();
    await appearanceLibrary.UpdateSettingsAsync(settings => settings with { Theme = storedTheme });
    await appearanceLibrary.SaveReaderPreferencesAsync(appearanceGuide, storedScale);
    // The save is an UPDATE: a guide without a preferences row would keep no scale.
    if ((await appearanceLibrary.GetReaderPreferencesAsync(appearanceGuide))?.TextScale != storedScale)
    {
        throw new InvalidOperationException("The guide's text scale did not save.");
    }
    return 0;
}

if (args.Length == 3 && args[0] == "seed-pdf-reader")
{
    ManagedPathResolver pdfPaths = new(args[1]);
    await using SqliteLibraryRepository pdfRepository = new(pdfPaths);
    await pdfRepository.InitializeAsync();
    if ((await pdfRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The PDF reader seed needs an empty library.");
    }
    string pdfFixtures = Path.Combine(Path.GetFullPath(args[2]), "p0");
    // P8: imports read a temp copy of the fixtures, so the installer can
    // delete the originals before pdf-offline. The TXT guide is inserted
    // directly and needs no original.
    string originals = Path.Combine(
        Path.GetTempPath(), $"desktop-guides-pdf-originals-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path.Combine(originals, "generated"));
    foreach (string fixture in new[]
    {
        "pdf-access.pdf", "pdf-scan.pdf", "pdf-short.pdf", "pdf-locked.pdf",
        Path.Combine("generated", "pdf-long.pdf"),
    })
    {
        File.Copy(Path.Combine(pdfFixtures, fixture), Path.Combine(originals, fixture));
    }
    Game pdfGame = await pdfRepository.AddGameAsync("PDF Reader Game", null, null);
    GuideImportValidator pdfValidator = new();
    GuideImportPublisher pdfPublisher = new(pdfRepository, pdfPaths);

    // Imports a P0 fixture as a user would. The Reader opens the managed copy.
    async Task<Guid> PublishPdfAsync(string fixture, string title, bool allowDuplicate)
    {
        ImportInspection inspection = await pdfValidator.InspectAsync(
            Path.Combine(originals, fixture), CancellationToken.None);
        if (inspection is not ImportReady ready)
        {
            throw new InvalidOperationException($"The {fixture} fixture failed the import preview: {inspection}.");
        }
        return await pdfPublisher.PublishAsync(
            ready.Manifest, pdfGame.Id, title, allowDuplicate, null, CancellationToken.None);
    }

    // A locked fixture goes through the password step, as the dialog does.
    // "guide" is the fixture's public test password.
    async Task<Guid> PublishLockedPdfAsync(string fixture, string title, string password)
    {
        ImportInspection inspection = await pdfValidator.InspectAsync(
            Path.Combine(originals, fixture), CancellationToken.None);
        if (inspection is not ImportNeedsPdfPassword needs)
        {
            throw new InvalidOperationException(
                $"The {fixture} fixture didn't ask for a password: {inspection.GetType().Name}.");
        }
        PdfImportManifest manifest = await pdfValidator.ResolvePdfPasswordAsync(
            needs, password, CancellationToken.None);
        return await pdfPublisher.PublishAsync(
            manifest, pdfGame.Id, title, false, null, CancellationToken.None);
    }

    async Task<string> ManagedCopyAsync(Guid id) =>
        pdfPaths.ResolveExistingGuideFile(id, (await pdfRepository.GetGuideAsync(id))!.PrimaryRelativePath);

    await PublishPdfAsync("pdf-access.pdf", "Tagged PDF Guide", false);
    await PublishPdfAsync("pdf-scan.pdf", "Scanned PDF Guide", false);
    Guid pdfLong = await PublishPdfAsync(Path.Combine("generated", "pdf-long.pdf"), "Long PDF Guide", false);
    Guid pdfLocked = await PublishLockedPdfAsync("pdf-locked.pdf", "Locked PDF Guide", "guide");
    Guid damaged = await PublishPdfAsync("pdf-short.pdf", "Damaged PDF Guide", false);
    Guid missing = await PublishPdfAsync("pdf-short.pdf", "Missing PDF Guide", true);
    // Cuts the managed copy before its cross-reference table, as a failed
    // copy might.
    using (FileStream copy = new(await ManagedCopyAsync(damaged), FileMode.Open, FileAccess.Write))
    {
        copy.SetLength(400);
    }
    File.Delete(await ManagedCopyAsync(missing));
    // TXT guides still open after the PDF errors.
    await InsertTextGuideAsync(pdfPaths, pdfGame.Id, Guid.NewGuid(), "Plain Text Guide",
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        File.ReadAllBytes(Path.Combine(pdfFixtures, "txt-ascii.txt")));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        pdfLong = pdfLong.ToString("N"),
        pdfLocked = pdfLocked.ToString("N"),
        originals,
    }));
    return 0;
}

if (args.Length == 3 && args[0] == "seed-progress")
{
    ManagedPathResolver progressPaths = new(args[1]);
    await using SqliteLibraryRepository progressRepository = new(progressPaths);
    await progressRepository.InitializeAsync();
    if ((await progressRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The progress seed needs an empty library.");
    }
    string fixtures = Path.GetFullPath(args[2]);
    Game progressGame = await progressRepository.AddGameAsync("Progress Game", null, null);
    GuideImportValidator progressValidator = new();
    GuideImportPublisher progressPublisher = new(progressRepository, progressPaths);
    async Task<Guid> PublishAsync(string relative, string title)
    {
        ImportInspection inspection = await progressValidator.InspectAsync(
            Path.Combine(fixtures, relative), CancellationToken.None);
        if (inspection is not ImportReady ready)
        {
            throw new InvalidOperationException($"The {relative} fixture failed the import preview: {inspection}.");
        }
        return await progressPublisher.PublishAsync(
            ready.Manifest, progressGame.Id, title, false, null, CancellationToken.None);
    }
    Guid numbered = Guid.NewGuid();
    await InsertTextGuideAsync(progressPaths, progressGame.Id, numbered, "Numbered Lines Guide",
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        File.ReadAllBytes(Path.Combine(fixtures, "p1", "txt-numbered.txt")));
    Guid web = await PublishAsync(Path.Combine("p1", "html-long", "guide.html"), "Long Web Guide");
    Guid pdf = await PublishAsync(Path.Combine("p0", "generated", "pdf-long.pdf"), "Long PDF Guide");
    Guid unopened = Guid.NewGuid();
    await InsertTextGuideAsync(progressPaths, progressGame.Id, unopened, "Unopened Guide",
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        File.ReadAllBytes(Path.Combine(fixtures, "p1", "txt-numbered.txt")));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        numbered = numbered.ToString("N"),
        web = web.ToString("N"),
        pdf = pdf.ToString("N"),
        unopened = unopened.ToString("N")
    }));
    return 0;
}

if (args.Length == 3 && args[0] == "seed-text-size")
{
    // T14.1: two TXT guides, an HTML guide and a PDF guide in one game.
    ManagedPathResolver sizePaths = new(args[1]);
    await using SqliteLibraryRepository sizeRepository = new(sizePaths);
    await sizeRepository.InitializeAsync();
    if ((await sizeRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The text size seed needs an empty library.");
    }
    string fixtures = Path.GetFullPath(args[2]);
    Game sizeGame = await sizeRepository.AddGameAsync("Text Size Game", null, null);
    GuideImportValidator sizeValidator = new();
    GuideImportPublisher sizePublisher = new(sizeRepository, sizePaths);
    async Task<Guid> PublishAsync(string relative, string title)
    {
        ImportInspection inspection = await sizeValidator.InspectAsync(
            Path.Combine(fixtures, relative), CancellationToken.None);
        if (inspection is not ImportReady ready)
        {
            throw new InvalidOperationException($"The {relative} fixture failed the import preview: {inspection}.");
        }
        return await sizePublisher.PublishAsync(
            ready.Manifest, sizeGame.Id, title, false, null, CancellationToken.None);
    }
    long sizeNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    Guid ascii = Guid.NewGuid();
    await InsertTextGuideAsync(sizePaths, sizeGame.Id, ascii, "ASCII Map Guide", sizeNow,
        File.ReadAllBytes(Path.Combine(fixtures, "p0", "txt-ascii.txt")));
    Guid utf8 = Guid.NewGuid();
    await InsertTextGuideAsync(sizePaths, sizeGame.Id, utf8, "UTF-8 Guide", sizeNow,
        File.ReadAllBytes(Path.Combine(fixtures, "p0", "txt-utf8.txt")));
    Guid web = await PublishAsync(Path.Combine("p0", "html-static", "guide.html"), "Static Web Guide");
    Guid pdf = await PublishAsync(Path.Combine("p0", "generated", "pdf-long.pdf"), "Long PDF Guide");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ascii = ascii.ToString("N"),
        utf8 = utf8.ToString("N"),
        web = web.ToString("N"),
        pdf = pdf.ToString("N")
    }));
    return 0;
}

if (args.Length >= 3 && args[0] == "describe-text-scales")
{
    // Each guide's stored TextScale, or null when it has none.
    ManagedPathResolver scalePaths = new(args[1]);
    await using SqliteLibraryRepository scaleRepository = new(scalePaths);
    await scaleRepository.InitializeAsync();
    Dictionary<string, double?> scales = [];
    foreach (string id in args.Skip(2))
    {
        ReaderPreferences? preferences = await scaleRepository.GetReaderPreferencesAsync(Guid.Parse(id));
        scales[id] = preferences?.TextScale;
    }
    Console.WriteLine(JsonSerializer.Serialize(scales));
    return 0;
}

if (args.Length == 2 && args[0] == "clear-reading-locations")
{
    // Puts every guide back at its start between passes that share a data folder.
    ExecuteSql(
        new ManagedPathResolver(args[1]),
        "UPDATE ReadingStates SET LocatorJson = NULL, EstimatedFraction = NULL");
    return 0;
}

if (args.Length == 2 && args[0] == "describe-progress")
{
    // The stored reading state behind each Progress Game row, newest first.
    ManagedPathResolver describePaths = new(args[1]);
    await using SqliteLibraryRepository describeRepository = new(describePaths);
    await describeRepository.InitializeAsync();
    Game describeGame = (await describeRepository.ListGamesAsync())
        .Single(game => game.Title == "Progress Game");
    var describeRows = (await describeRepository.ListGuideSummariesAsync(describeGame.Id))
        .Select(summary => new
        {
            title = summary.Guide.Title,
            estimate = summary.State?.EstimatedFraction,
            openedUtcMs = summary.State?.LastOpenedUtc?.ToUnixTimeMilliseconds(),
            completedUtcMs = summary.State?.CompletedUtc?.ToUnixTimeMilliseconds(),
        });
    Console.WriteLine(JsonSerializer.Serialize(describeRows));
    return 0;
}

if (args.Length == 2 && args[0] == "change-progress-copies")
{
    // Edits the managed copies, never the fixtures: every TXT line loses its
    // saved context, and the PDF gains bytes past %%EOF.
    ManagedPathResolver changePaths = new(args[1]);
    await using SqliteLibraryRepository changeRepository = new(changePaths);
    await changeRepository.InitializeAsync();
    Game changeGame = (await changeRepository.ListGamesAsync())
        .Single(game => game.Title == "Progress Game");
    IReadOnlyList<GuideSummary> changeGuides =
        await changeRepository.ListGuideSummariesAsync(changeGame.Id);
    string ManagedCopy(string title)
    {
        Guide guide = changeGuides.Single(summary => summary.Guide.Title == title).Guide;
        string copy = changePaths.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);
        File.SetAttributes(copy, FileAttributes.Normal);
        return copy;
    }
    string numberedCopy = ManagedCopy("Numbered Lines Guide");
    string numberedText = File.ReadAllText(numberedCopy);
    if (!numberedText.Contains("Numbered guide text.", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The Numbered guide copy has no line to edit.");
    }
    File.WriteAllText(numberedCopy,
        numberedText.Replace("Numbered guide text.", "Edited guide text.", StringComparison.Ordinal),
        new UTF8Encoding(false));
    File.AppendAllText(ManagedCopy("Long PDF Guide"), "\n% changed\n");
    return 0;
}

if (args.Length != 2 ||
    args[0] is not ("seed" or "stale" or "seed-long" or "seed-second" or
        "seed-design" or "seed-catalog" or "seed-facts" or "seed-search" or "seed-import" or
        "seed-actions" or "seed-navigation"))
{
    Console.Error.WriteLine(
        "Usage: DesktopGuides.ShellSeed seed|stale|seed-long|seed-second|seed-design|seed-catalog|seed-facts|seed-search|seed-import|seed-actions|seed-navigation " +
        "<app-data-root> " +
        "or seed-linked-game|describe-providers|describe-import|describe-actions|describe-progress|describe-theme <app-data-root> " +
        "or seed-txt-reader|seed-html-reader|seed-html-position|seed-html-theme|seed-pdf-reader|seed-progress <app-data-root> <fixtures-root> " +
        "or set-html-appearance <app-data-root> <guide-id> <System|Light|Dark> <scale|default> " +
        "or check-igdb-fields <igdb-credential-file> <fixture-dir> " +
        "or invalidate-blocked-guide <app-data-root> " +
        "or clear-reading-locations <app-data-root> " +
        "or change-progress-copies <app-data-root> " +
        "or corrupt-reader-guide|restore-reader-guide <app-data-root> " +
        "or hold-write-lock|hold-read-lock <app-data-root> <ready-path> <release-path> [hold-seconds]");
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

if (args[0] == "seed-catalog")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The catalog seed needs an empty library.");
    }
    ManagedArtworkStore catalogArt = new(paths);
    int nextExternalId = 950000;

    async Task AddWithArtwork(string title, string? platform, byte shade)
    {
        Guid id = Guid.NewGuid();
        StoredArtwork stored = await catalogArt.StoreAsync(
            id, SolidPng(60, 90, shade, (byte)(255 - shade), 0x80), CancellationToken.None);
        GameMetadataSnapshot snapshot = new(
            GameMetadataSnapshot.CurrentSchemaVersion, null, null, [], [], [], [], null,
            GameTypeTag.MainGame, "SteamGridDB");
        await repository.AddLinkedGameAsync(new NewLinkedGame(
            id, title, platform,
            new ProviderGameLink(ProviderGameLink.Igdb, (nextExternalId++).ToString(), DateTimeOffset.UtcNow),
            snapshot, stored.RelativePath), CancellationToken.None);
    }

    string longTitle = "Catalog A " + new string('W', 150);
    await AddWithArtwork(longTitle, "PC", 0x20);
    await AddWithArtwork("Catalog B Short", "Windows", 0x40);

    await AddWithArtwork("Catalog C Corrupt Art", "PC", 0x60);
    for (int i = 0; i < 10; i++)
    {
        await AddWithArtwork($"Catalog C Missing Art {i}", "PC", 0x70);
    }
    foreach (Game seeded in await repository.ListGamesAsync())
    {
        if (seeded.ArtworkRelativePath is not { } relative) continue;
        if (seeded.Title == "Catalog C Corrupt Art")
        {
            File.WriteAllBytes(catalogArt.ResolveFile(relative)!, [0, 1, 2, 3]);
        }
        else if (seeded.Title.StartsWith("Catalog C Missing Art ", StringComparison.Ordinal))
        {
            catalogArt.Delete(relative);
        }
    }

    await repository.AddGameAsync("Catalog D Größe Überfall Äpfel", "PlayStation 5", null);
    for (int i = 0; i < 484; i++)
    {
        string title = $"Catalog Game {i:D3}";
        string? platform = i % 2 == 0 ? "Windows" : null;
        if (i % 3 == 0) await AddWithArtwork(title, platform, (byte)(i % 200));
        else await repository.AddGameAsync(title, platform, null);
    }
    await repository.AddGameAsync("كتالوج الألعاب", "PC", null);
    await AddWithArtwork("ゼルダの伝説", "Nintendo Switch", 0xA0);

    // One shared creation time, so activity order is title order and the
    // catalog smoke's head, tail and keyboard checks still hold.
    long catalogCreated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    ExecuteSql(paths, $"UPDATE Games SET CreatedUtcMs = {catalogCreated}, UpdatedUtcMs = {catalogCreated}");

    IReadOnlyList<Game> catalog = await repository.ListGamesAsync();
    string[] expectedHead = [longTitle, "Catalog B Short", "Catalog C Corrupt Art", "Catalog C Missing Art 0"];
    if (catalog.Count != 500 ||
        !catalog.Take(4).Select(seeded => seeded.Title).SequenceEqual(expectedHead) ||
        catalog[^1].Title != "ゼルダの伝説" ||
        catalog.Count(seeded => seeded.Title.StartsWith("Catalog C Missing Art ", StringComparison.Ordinal) &&
            seeded.ArtworkRelativePath is { } path && catalogArt.ResolveFile(path) is null) != 10)
    {
        throw new InvalidOperationException("The catalog seed did not read back in the expected order.");
    }
    if (!(await repository.ListGameSummariesAsync()).Select(entry => entry.Game.Title)
            .SequenceEqual(catalog.Select(seeded => seeded.Title)))
    {
        throw new InvalidOperationException("The catalog seed's activity order differs from its title order.");
    }
    Console.WriteLine("Seeded 500 catalog games.");
    return 0;
}

if (args[0] == "seed-facts")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The facts seed needs an empty library.");
    }
    const long factsDay = 86_400_000;
    long factsNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    DateTime factsLocalDate = TimeZoneInfo.ConvertTime(
        DateTimeOffset.FromUnixTimeMilliseconds(factsNow), TimeZoneInfo.Local).Date;
    long LocalMs(DateTime local) =>
        new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUnixTimeMilliseconds();
    long openedToday = Math.Max(factsNow - 60_000, LocalMs(factsLocalDate));
    long openedYesterday = LocalMs(factsLocalDate.AddDays(-1).AddHours(12));
    static string N(Guid value) => value.ToString("N");

    Guid factsGameId = Guid.NewGuid();
    await repository.AddLinkedGameAsync(new NewLinkedGame(
        factsGameId, "Facts Test Game", "PC",
        new ProviderGameLink(ProviderGameLink.Igdb, "960000", DateTimeOffset.UtcNow),
        new GameMetadataSnapshot(
            GameMetadataSnapshot.CurrentSchemaVersion, null, null, [], [], [], [], null,
            GameTypeTag.MainGame),
        null), CancellationToken.None);
    Game zetaGame = await repository.AddGameAsync("Zeta Archive Game", "Windows", null);
    Game emptyGame = await repository.AddGameAsync("Empty Test Game", null, null);

    Guid checklistId = Guid.NewGuid();
    Guid storyId = Guid.NewGuid();
    Guid mapId = Guid.NewGuid();
    Guid upgradeId = Guid.NewGuid();
    Guid notesId = Guid.NewGuid();
    foreach ((Guid guideId, string title) in new[]
             {
                 (checklistId, "Achievement Checklist"),
                 (storyId, "Main Story Walkthrough"),
                 (mapId, "Collectibles Map"),
                 (upgradeId, "Weapon Upgrade Guide")
             })
    {
        await InsertGuideAsync(paths, factsGameId, guideId, title, factsNow - 15 * factsDay);
    }
    await InsertGuideAsync(paths, zetaGame.Id, notesId, "Recent Notes", factsNow - 30 * factsDay);

    // No writer for reading state exists yet (T12.3, T13.2), so set it here.
    // Html and Pdf rows keep TXT content: the smoke never opens them.
    ExecuteSql(paths, $"""
        UPDATE Games SET CreatedUtcMs = {factsNow - 15 * factsDay}, UpdatedUtcMs = {factsNow - 15 * factsDay}
            WHERE Id = '{N(factsGameId)}';
        UPDATE Games SET CreatedUtcMs = {factsNow - 30 * factsDay}, UpdatedUtcMs = {factsNow - 30 * factsDay}
            WHERE Id = '{N(zetaGame.Id)}';
        UPDATE Games SET CreatedUtcMs = {factsNow - 5 * factsDay}, UpdatedUtcMs = {factsNow - 5 * factsDay}
            WHERE Id = '{N(emptyGame.Id)}';
        UPDATE Guides SET Format = 'Html' WHERE Id = '{N(storyId)}';
        UPDATE Guides SET Format = 'Pdf' WHERE Id = '{N(mapId)}';
        UPDATE ReadingStates SET LastOpenedUtcMs = {openedToday} WHERE GuideId = '{N(storyId)}';
        UPDATE ReadingStates SET EstimatedFraction = 0.45, LastOpenedUtcMs = {openedYesterday}
            WHERE GuideId = '{N(mapId)}';
        UPDATE ReadingStates SET EstimatedFraction = 0.8, LastOpenedUtcMs = {factsNow - 10 * factsDay},
            CompletedUtcMs = {factsNow - 10 * factsDay} WHERE GuideId = '{N(upgradeId)}';
        UPDATE ReadingStates SET LastOpenedUtcMs = {factsNow} WHERE GuideId = '{N(notesId)}';
        """);

    string[] factsGames = [.. (await repository.ListGameSummariesAsync()).Select(entry => entry.Game.Title)];
    string[] factsGuides = [.. (await repository.ListGuideSummariesAsync(factsGameId)).Select(entry => entry.Guide.Title)];
    if (!factsGames.SequenceEqual(new[] { "Zeta Archive Game", "Facts Test Game", "Empty Test Game" }) ||
        !factsGuides.SequenceEqual(new[]
        {
            "Main Story Walkthrough", "Collectibles Map", "Weapon Upgrade Guide", "Achievement Checklist"
        }))
    {
        throw new InvalidOperationException("The facts seed did not read back in activity order.");
    }
    Console.WriteLine($"Seeded facts game {factsGameId:N} with four guides.");
    return 0;
}

if (args[0] == "seed-search")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The search seed needs an empty library.");
    }
    const long searchDay = 86_400_000;
    long searchNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    // Titles exercise case, accent, width and non-Latin matching. Created
    // times fix the Library order: Zeta (its guide, -1 day), then the rest.
    (string Title, int DaysAgo)[] searchTitles =
    [
        ("Zeta Archive Game", 5),
        ("Pok\u00e9mon Crystal", 2),
        ("\u014ckami HD", 3),
        ("\u30c9\u30e9\u30b4\u30f3\u30af\u30a8\u30b9\u30c8XI", 4)
    ];
    List<string> searchSql = [];
    Guid zetaSearchId = Guid.Empty;
    foreach ((string title, int daysAgo) in searchTitles)
    {
        Game searchGame = await repository.AddGameAsync(title, null, null);
        long created = searchNow - daysAgo * searchDay;
        searchSql.Add($"UPDATE Games SET CreatedUtcMs = {created}, UpdatedUtcMs = {created} WHERE Id = '{searchGame.Id:N}';");
        if (zetaSearchId == Guid.Empty)
        {
            zetaSearchId = searchGame.Id;
        }
    }
    await InsertGuideAsync(paths, zetaSearchId, Guid.NewGuid(), "Complete Walkthrough", searchNow - searchDay);
    ExecuteSql(paths, string.Join('\n', searchSql));

    string[] searchOrder = [.. (await repository.ListGameSummariesAsync()).Select(entry => entry.Game.Title)];
    if (!searchOrder.SequenceEqual(searchTitles.Select(entry => entry.Title)))
    {
        throw new InvalidOperationException("The search seed did not read back in activity order.");
    }
    Console.WriteLine("Seeded four search games.");
    return 0;
}

if (args[0] == "seed-navigation")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The navigation seed needs an empty library.");
    }
    const long navigationDay = 86_400_000;
    long navigationNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    Game atlas = await repository.AddGameAsync("Atlas Navigation Game", null, null);
    Game beacon = await repository.AddGameAsync("Beacon Navigation Game", null, null);
    await repository.AddGameAsync("Cobalt Other Game", null, null);
    // Import times fix the Atlas guide order: Third, Second, First.
    await InsertGuideAsync(paths, atlas.Id, Guid.NewGuid(), "Atlas First Guide", navigationNow - 3 * navigationDay);
    await InsertGuideAsync(paths, atlas.Id, Guid.NewGuid(), "Atlas Second Guide", navigationNow - 2 * navigationDay);
    await InsertGuideAsync(paths, atlas.Id, Guid.NewGuid(), "Atlas Third Guide", navigationNow - navigationDay);
    Guid beaconGuideId = Guid.NewGuid();
    await InsertGuideAsync(paths, beacon.Id, beaconGuideId, "Beacon Guide", navigationNow - 4 * navigationDay);
    AppSettings navigationSettings = await repository.GetSettingsAsync();
    await repository.SaveSettingsAsync(navigationSettings with { LastActiveGuideId = beaconGuideId });
    Console.WriteLine("Seeded three navigation games.");
    return 0;
}

if (args[0] == "seed-actions")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The game actions seed needs an empty library.");
    }
    long actionsNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    DateTime actionsYesterday = TimeZoneInfo.ConvertTime(
        DateTimeOffset.FromUnixTimeMilliseconds(actionsNow), TimeZoneInfo.Local).Date.AddDays(-1).AddHours(12);
    long alphaOpened = new DateTimeOffset(
        actionsYesterday, TimeZoneInfo.Local.GetUtcOffset(actionsYesterday)).ToUnixTimeMilliseconds();
    ManagedArtworkStore actionsArtwork = new(paths);
    async Task<Guid> AddActionsGameAsync(string title, string externalId, byte[] cover)
    {
        Guid id = Guid.NewGuid();
        StoredArtwork stored = await actionsArtwork.StoreAsync(id, cover, CancellationToken.None);
        await repository.AddLinkedGameAsync(new NewLinkedGame(
            id, title, "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, externalId, DateTimeOffset.UtcNow),
            new GameMetadataSnapshot(
                GameMetadataSnapshot.CurrentSchemaVersion,
                "A seeded summary for the game actions check.",
                null, [], [], [], [], null, GameTypeTag.MainGame),
            stored.RelativePath), CancellationToken.None);
        return id;
    }

    Guid renameGameId = await AddActionsGameAsync(
        "Linked Rename Game", "900100", SolidPng(60, 90, 0x2E, 0x5E, 0x8C));
    Guid emptyGameId = await AddActionsGameAsync(
        "Empty Linked Game", "900101", SolidPng(60, 90, 0x8C, 0x4A, 0x2E));
    Guid guidedGameId = await AddActionsGameAsync(
        "Guided Remove Game", "900102", SolidPng(60, 90, 0x3C, 0x7A, 0x4E));
    Guid guidedWalkthroughId = Guid.NewGuid();
    Guid guidedMapId = Guid.NewGuid();
    Guid alphaId = Guid.NewGuid();
    Guid betaId = Guid.NewGuid();
    await InsertGuideAsync(paths, renameGameId, alphaId, "Alpha Route Guide", actionsNow);
    await InsertGuideAsync(paths, renameGameId, betaId, "Beta Route Guide", actionsNow);
    await InsertGuideAsync(paths, guidedGameId, guidedWalkthroughId, "Guided Walkthrough", actionsNow);
    // Never opened in the smoke, so no WebView2 state is needed (ruling 7).
    await InsertGuideAsync(
        paths, guidedGameId, guidedMapId, "Guided Map Guide", actionsNow,
        "Html", "index.html", "images/map.png");
    // No writer for reading state exists yet (T12.3, T13.2), so set it here.
    ExecuteSql(paths, $"""
        UPDATE ReadingStates SET EstimatedFraction = 0.45, LastOpenedUtcMs = {alphaOpened}
            WHERE GuideId = '{alphaId:N}';
        UPDATE ReadingStates SET EstimatedFraction = 0.2 WHERE GuideId = '{guidedWalkthroughId:N}';
        """);
    AppSettings actionsSettings = await repository.GetSettingsAsync();
    await repository.SaveSettingsAsync(actionsSettings with { LastActiveGuideId = guidedWalkthroughId });
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        RenameGameId = renameGameId.ToString("N"),
        EmptyGameId = emptyGameId.ToString("N"),
        AlphaGuideId = alphaId.ToString("N"),
        BetaGuideId = betaId.ToString("N"),
        AlphaLastOpenedUtcMs = alphaOpened,
        GuidedGameId = guidedGameId.ToString("N"),
        GuidedGuideIds = new[] { guidedWalkthroughId.ToString("N"), guidedMapId.ToString("N") },
    }));
    return 0;
}

if (args[0] == "seed-import")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The import seed needs an empty library.");
    }
    Game importGame = await repository.AddGameAsync("Import Test Game", "PC", null);
    Console.WriteLine($"Seeded import game {importGame.Id:N}.");
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
    ManagedPathResolver paths, Guid gameId, Guid guideId, string title, long now,
    string format = "Txt", string primaryPath = "guide.txt", params string[] assetPaths)
{
    string guideRoot = paths.GetGuideRoot(guideId);
    Directory.CreateDirectory(guideRoot);
    string content = Path.Combine(guideRoot, primaryPath);
    await File.WriteAllTextAsync(content, "Test guide.");
    foreach (string asset in assetPaths)
    {
        string assetPath = Path.Combine(guideRoot, asset);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        await File.WriteAllTextAsync(assetPath, "Test asset.");
    }
    byte[] bytes = await File.ReadAllBytesAsync(content);
    InsertGuideRow(paths, gameId, guideId, title, now, format, primaryPath, bytes, codePage: null);
}

static async Task InsertTextGuideAsync(
    ManagedPathResolver paths, Guid gameId, Guid guideId, string title, long now,
    byte[] content)
{
    string guideRoot = paths.GetGuideRoot(guideId);
    Directory.CreateDirectory(guideRoot);
    await File.WriteAllBytesAsync(Path.Combine(guideRoot, "guide.txt"), content);
    InsertGuideRow(paths, gameId, guideId, title, now, "Txt", "guide.txt", content, codePage: null);
}

static void InsertGuideRow(
    ManagedPathResolver paths, Guid gameId, Guid guideId, string title, long now,
    string format, string primaryPath, byte[] bytes, int? codePage)
{
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
            ContentSha256, ContentBytes, TextCodePage, ImportedUtcMs, UpdatedUtcMs
        ) VALUES (
            $guide, $game, $title, $format, $root, $primary,
            $hash, $bytes, $codePage, $now, $now
        );
        INSERT INTO ReadingStates (GuideId) VALUES ($guide);
        INSERT INTO ReaderPreferences (GuideId) VALUES ($guide);
        """;
    command.Parameters.AddWithValue("$guide", guideId.ToString("N"));
    command.Parameters.AddWithValue("$game", gameId.ToString("N"));
    command.Parameters.AddWithValue("$title", title);
    command.Parameters.AddWithValue("$root", $"content/{guideId:N}");
    command.Parameters.AddWithValue("$format", format);
    command.Parameters.AddWithValue("$primary", primaryPath);
    command.Parameters.AddWithValue("$hash", hash);
    command.Parameters.AddWithValue("$bytes", bytes.LongLength);
    command.Parameters.AddWithValue("$codePage", codePage is int page ? page : DBNull.Value);
    command.Parameters.AddWithValue("$now", now);
    command.ExecuteNonQuery();
}

static void ExecuteSql(ManagedPathResolver paths, string sql)
{
    using SqliteConnection connection = new(new SqliteConnectionStringBuilder
    {
        DataSource = paths.DatabasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false,
        ForeignKeys = true
    }.ToString());
    connection.Open();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
}

static void CollectFieldPaths(JsonNode? node, string prefix, SortedSet<string> fields)
{
    switch (node)
    {
        case JsonArray array:
            foreach (JsonNode? item in array) CollectFieldPaths(item, prefix, fields);
            break;
        case JsonObject item:
            foreach ((string name, JsonNode? value) in item)
            {
                string path = prefix.Length == 0 ? name : $"{prefix}.{name}";
                // Expanded objects and object arrays become dotted paths
                // such as platforms.name; scalars and ID arrays are leaves.
                bool expanded = value is JsonObject ||
                    (value is JsonArray items && items.Any(entry => entry is JsonObject));
                if (expanded)
                {
                    CollectFieldPaths(value, path, fields);
                }
                else
                {
                    fields.Add(path);
                }
            }
            break;
    }
}

// A valid, decodable RGB PNG of one colour, so the offline check needs no
// provider image.
static byte[] SolidPng(int width, int height, byte red, byte green, byte blue)
{
    byte[] raw = new byte[height * (1 + width * 3)];
    for (int row = 0; row < height; row++)
    {
        int offset = row * (1 + width * 3);
        for (int x = 0; x < width; x++)
        {
            raw[offset + 1 + x * 3] = red;
            raw[offset + 2 + x * 3] = green;
            raw[offset + 3 + x * 3] = blue;
        }
    }
    using MemoryStream compressed = new();
    using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
    {
        zlib.Write(raw);
    }
    byte[] header = new byte[13];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
    header[8] = 8; // bit depth
    header[9] = 2; // colour type: RGB
    using MemoryStream png = new();
    png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    WriteChunk(png, "IHDR", header);
    WriteChunk(png, "IDAT", compressed.ToArray());
    WriteChunk(png, "IEND", []);
    return png.ToArray();
}

static void WriteChunk(Stream stream, string type, byte[] data)
{
    Span<byte> length = stackalloc byte[4];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
    stream.Write(length);
    byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
    stream.Write(typed);
    Span<byte> crc = stackalloc byte[4];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
    stream.Write(crc);
}

static uint Crc32(byte[] bytes)
{
    uint crc = 0xFFFFFFFF;
    foreach (byte value in bytes)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
    }
    return ~crc;
}
