using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class ImportJournalTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "desktop-guides-journal-" + Guid.NewGuid().ToString("N"));
    private ManagedPathResolver paths = null!;
    private SqliteLibraryRepository repository = null!;
    private Game game = null!;
    private readonly Guid operationId = Guid.NewGuid();
    private readonly Guid guideId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        paths = new ManagedPathResolver(Path.Combine(root, "app-data"));
        repository = new SqliteLibraryRepository(paths);
        await repository.InitializeAsync();
        game = await repository.AddGameAsync("Journal Game", null, null);
    }

    public async Task DisposeAsync()
    {
        await repository.DisposeAsync();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public async Task PrepareRecordsAPreparedImport()
    {
        await Run(journal => journal.Prepare(operationId, guideId));

        Assert.Equal("Import|Prepared", Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
    }

    [Fact]
    public async Task PublishAddsGuideWithEmptyStateAndRemovesTheOperation()
    {
        await Run(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Publish(Guide(), () => { });
        });

        Guide guide = Assert.Single(await repository.ListGuidesAsync(game.Id));
        Assert.Equal(guideId, guide.Id);
        Assert.Equal($"content/{guideId:N}", guide.ManagedRelativeRoot);
        Assert.Equal("guide.txt", guide.PrimaryRelativePath);
        Assert.Equal(437, guide.TextCodePage);
        Assert.Equal("notes.txt", guide.SourceLabel);
        ReadingState state = Assert.IsType<ReadingState>(await repository.GetReadingStateAsync(guideId));
        Assert.Null(state.LocatorJson);
        Assert.NotNull(await repository.GetReaderPreferencesAsync(guideId));
        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    [Fact]
    public async Task PublishFailureBeforeCommitLeavesNoGuideAndKeepsTheOperation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Publish(Guide(), () => throw new InvalidOperationException());
        }));

        Assert.Empty(await repository.ListGuidesAsync(game.Id));
        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    [Fact]
    public async Task RollBackRemovesTheStageAndKeepsContentItDoesNotOwn()
    {
        string staged = paths.GetStagedGuideRoot(operationId, guideId);
        string content = paths.GetGuideRoot(guideId);
        await Run(journal =>
        {
            journal.Prepare(operationId, guideId);
            WriteMarker(staged);
            WriteMarker(content);
            journal.RollBack(operationId);
        });

        Assert.False(Directory.Exists(Path.GetDirectoryName(staged)!));
        Assert.True(File.Exists(Path.Combine(content, "marker.txt")));
        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    [Fact]
    public async Task RollBackRemovesRenamedContent()
    {
        string content = paths.GetGuideRoot(guideId);
        await Run(journal =>
        {
            journal.Prepare(operationId, guideId);
            WriteMarker(content);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.GetStagedGuideRoot(operationId, guideId))!);
            journal.RollBack(operationId);
        });

        Assert.False(Directory.Exists(content));
        Assert.False(Directory.Exists(Path.GetDirectoryName(paths.GetStagedGuideRoot(operationId, guideId))!));
        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    [Fact]
    public async Task RunImportHoldsTheWriteGate()
    {
        using SemaphoreSlim entered = new(0);
        using SemaphoreSlim release = new(0);
        Task import = repository.RunImportAsync<bool>(async (_, _) =>
        {
            entered.Release();
            await release.WaitAsync();
            return true;
        }, CancellationToken.None);
        await entered.WaitAsync();

        Task edit = repository.UpdateGameAsync(game.Id, "Renamed", null, null);
        await Task.Delay(200);
        Assert.False(edit.IsCompleted);

        release.Release();
        await import;
        await edit;
        Assert.Equal("Renamed", (await repository.GetGameAsync(game.Id))!.Title);
    }

    private static readonly string Hash = new('b', 64);

    [Fact]
    public async Task FingerprintLookupFindsTheGuideInTheSameGame()
    {
        Guid id = await PublishGuide(game.Id, GuideFormat.Txt, Hash);

        Guide? found = await repository.FindGuideByFingerprintAsync(game.Id, GuideFormat.Txt, Hash);

        Assert.Equal(id, found?.Id);
    }

    [Fact]
    public async Task FingerprintLookupIgnoresOtherGamesFormatsAndHashes()
    {
        Game other = await repository.AddGameAsync("Other Game", null, null);
        await PublishGuide(other.Id, GuideFormat.Txt, Hash);
        await PublishGuide(game.Id, GuideFormat.Pdf, Hash);
        await PublishGuide(game.Id, GuideFormat.Txt, new string('c', 64));

        Assert.Null(await repository.FindGuideByFingerprintAsync(game.Id, GuideFormat.Txt, Hash));
    }

    [Fact]
    public async Task FingerprintLookupReturnsTheOldestMatch()
    {
        await PublishGuide(game.Id, GuideFormat.Txt, Hash);
        Guid older = await PublishGuide(game.Id, GuideFormat.Txt, Hash);
        Scalar($"UPDATE Guides SET ImportedUtcMs = 1 WHERE Id = '{older:N}'");

        Guide? found = await repository.FindGuideByFingerprintAsync(game.Id, GuideFormat.Txt, Hash);

        Assert.Equal(older, found?.Id);
    }

    [Fact]
    public async Task JournalLookupUsesTheSameMatch()
    {
        Guid id = await PublishGuide(game.Id, GuideFormat.Txt, Hash);
        Guid? found = null;
        Guid? otherFormat = Guid.Empty;

        await Run(journal =>
        {
            found = journal.FindGuide(game.Id, GuideFormat.Txt, Hash);
            otherFormat = journal.FindGuide(game.Id, GuideFormat.Pdf, Hash);
        });

        Assert.Equal(id, found);
        Assert.Null(otherFormat);
    }

    private NewImportedGuide Guide() => new(
        operationId, guideId, game.Id, "Imported", GuideFormat.Txt, "guide.txt",
        new string('a', 64), 20, "notes.txt", 437);

    private async Task<Guid> PublishGuide(Guid gameId, GuideFormat format, string sha)
    {
        Guid operation = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        string primary = format == GuideFormat.Pdf ? "guide.pdf" : "guide.txt";
        await Run(journal =>
        {
            journal.Prepare(operation, id);
            journal.Publish(
                new NewImportedGuide(operation, id, gameId, "Imported", format, primary, sha, 20, "notes", null),
                () => { });
        });
        return id;
    }

    private Task Run(Action<IImportJournal> work) =>
        repository.RunImportAsync<bool>((journal, _) =>
        {
            work(journal);
            return Task.FromResult(true);
        }, CancellationToken.None);

    private static void WriteMarker(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "marker.txt"), "x");
    }

    private string Scalar(string sql)
    {
        using SqliteConnection connection = new($"Data Source={paths.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }
}
