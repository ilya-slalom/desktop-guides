using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibraryExportRepositoryTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;

    public async Task InitializeAsync() => library = await RemovalLibrary.CreateAsync();

    public async Task DisposeAsync() => await library.DisposeAsync();

    private SqliteConnection Live()
    {
        SqliteConnection connection = new($"Data Source={library.Paths.DatabasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private string TempDatabase() => Path.Combine(library.Root, $"copy-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public async Task RunExportAsyncHoldsTheWriteGate()
    {
        TaskCompletionSource entered = new();
        TaskCompletionSource release = new();
        Task<int> export = library.Repository.RunExportAsync(async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }, CancellationToken.None);
        await entered.Task;

        Task<Game> add = library.Repository.AddGameAsync("Waiting", null, null);
        await Task.Delay(200);
        Assert.False(add.IsCompleted);

        release.SetResult();
        Assert.Equal(1, await export);
        Assert.Equal("Waiting", (await add).Title);
    }

    [Fact]
    public async Task RunExportAsyncPassesAnOpenConnectionAndTheToken()
    {
        using CancellationTokenSource source = new();

        (System.Data.ConnectionState state, bool sameToken) = await library.Repository.RunExportAsync(
            (connection, token) => Task.FromResult((connection.State, token == source.Token)), source.Token);

        Assert.Equal(System.Data.ConnectionState.Open, state);
        Assert.True(sameToken);
    }

    [Fact]
    public async Task RunExportAsyncCancelsTheWaitForTheGate()
    {
        TaskCompletionSource release = new();
        Task<bool> holder = library.Repository.RunExportAsync(async (_, _) =>
        {
            await release.Task;
            return true;
        }, CancellationToken.None);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            library.Repository.RunExportAsync((_, _) => Task.FromResult(true), cancelled.Token));

        release.SetResult();
        Assert.True(await holder);
    }

    [Fact]
    public async Task BackupToWritesAValidatedCopy()
    {
        Guid game = await library.AddGameAsync("Copied");
        await library.AddGuideAsync(game, "Guide");
        string copy = TempDatabase();

        using (SqliteConnection live = Live())
        {
            SqliteLibraryRepository.BackupTo(live, copy, 4);
        }

        // A WAL copy can't be opened read-only once its -shm is gone; this only reads.
        using SqliteConnection opened = new($"Data Source={copy};Pooling=False");
        opened.Open();
        using SqliteCommand count = opened.CreateCommand();
        count.CommandText = "SELECT (SELECT COUNT(*) FROM Games) || '|' || (SELECT COUNT(*) FROM Guides)";
        Assert.Equal("1|1", count.ExecuteScalar());
    }

    [Fact]
    public void BackupToDeletesTheCopyWhenValidationFails()
    {
        string copy = TempDatabase();

        using (SqliteConnection live = Live())
        {
            Assert.Throws<InvalidDataException>(() => SqliteLibraryRepository.BackupTo(live, copy, 3));
        }

        Assert.False(File.Exists(copy));
        Assert.False(File.Exists(copy + "-wal"));
        Assert.False(File.Exists(copy + "-shm"));
    }

    [Fact]
    public void BackupToRefusesAnExistingFile()
    {
        string copy = TempDatabase();
        File.WriteAllText(copy, "keep me");

        using (SqliteConnection live = Live())
        {
            Assert.Throws<IOException>(() => SqliteLibraryRepository.BackupTo(live, copy, 4));
        }

        Assert.Equal("keep me", File.ReadAllText(copy));
    }
}
