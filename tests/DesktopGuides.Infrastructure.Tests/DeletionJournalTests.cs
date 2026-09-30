using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class DeletionJournalTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;
    private Guid gameId;
    private Guid guideId;
    private Guid otherId;
    private readonly Guid operationId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        library = await RemovalLibrary.CreateAsync();
        gameId = await library.AddGameAsync("Journal Game");
        guideId = await library.AddGuideAsync(gameId, "Removed");
        otherId = await library.AddGuideAsync(gameId, "Kept");
    }

    public async Task DisposeAsync() => await library.DisposeAsync();

    private string Content(Guid id) => library.Paths.GetGuideRoot(id);
    private string Trash(Guid id) => library.Paths.GetTrashedGuideRoot(operationId, id);
    private string OperationCount() => library.Scalar("SELECT COUNT(*) FROM FileOperations");

    [Fact]
    public async Task GetGuideReadsTheRowOrNull()
    {
        Guide? found = null;
        Guide? missing = null;

        await library.RunDeletion(journal =>
        {
            found = journal.GetGuide(guideId);
            missing = journal.GetGuide(Guid.NewGuid());
        });

        Assert.Equal("Removed", found?.Title);
        Assert.Null(missing);
    }

    [Fact]
    public async Task PrepareRecordsAPreparedDeletion()
    {
        await library.RunDeletion(journal => journal.Prepare(operationId, guideId));

        Assert.Equal("DeleteGuide|Prepared", library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
    }

    [Fact]
    public async Task CommitRemovesTheGuideAndItsStateAndMarksTheOperationCommitted()
    {
        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Commit(operationId, guideId, () => { });
        });

        Assert.Equal("0|0|0", library.RowsFor(guideId));
        Assert.Equal("1|1|1", library.RowsFor(otherId));
        Assert.Equal("Committed", library.Scalar("SELECT Phase FROM FileOperations"));
    }

    [Fact]
    public async Task CommitFailureBeforeCommitKeepsEverything()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Commit(operationId, guideId, () => throw new InvalidOperationException());
        }));

        Assert.Equal("1|1|1", library.RowsFor(guideId));
        Assert.Equal("Prepared", library.Scalar("SELECT Phase FROM FileOperations"));
    }

    [Fact]
    public async Task CommitWithoutAPreparedOperationKeepsTheGuide()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => library.RunDeletion(
            journal => journal.Commit(operationId, guideId, () => { })));

        Assert.Equal("1|1|1", library.RowsFor(guideId));
    }

    [Fact]
    public async Task CommitOfAMissingGuideKeepsThePreparedOperation()
    {
        Guid unknown = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidDataException>(() => library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, unknown);
            journal.Commit(operationId, unknown, () => { });
        }));

        Assert.Equal("Prepared", library.Scalar("SELECT Phase FROM FileOperations"));
    }

    [Fact]
    public async Task CommitClearsAMatchingLastActiveGuide()
    {
        await library.Repository.UpdateSettingsAsync(s => s with { LastActiveGuideId = guideId });

        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Commit(operationId, guideId, () => { });
        });

        Assert.Null((await library.Repository.GetSettingsAsync()).LastActiveGuideId);
    }

    [Fact]
    public async Task CommitKeepsAnotherGuidesLastActiveSetting()
    {
        await library.Repository.UpdateSettingsAsync(s => s with { LastActiveGuideId = otherId });

        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Commit(operationId, guideId, () => { });
        });

        Assert.Equal(otherId, (await library.Repository.GetSettingsAsync()).LastActiveGuideId);
    }

    [Fact]
    public async Task RollBackMovesTheTrashBackAndRemovesTheOperation()
    {
        RemovalLibrary.WriteFile(Content(guideId), "guide.txt", "bytes");

        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            Directory.CreateDirectory(Path.GetDirectoryName(Trash(guideId))!);
            Directory.Move(Content(guideId), Trash(guideId));
            journal.RollBack(operationId);
        });

        Assert.Equal("bytes", File.ReadAllText(Path.Combine(Content(guideId), "guide.txt")));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Trash(guideId))!));
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task RollBackWithoutTrashOnlyRemovesTheOperation()
    {
        RemovalLibrary.WriteFile(Content(guideId), "guide.txt", "bytes");

        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.RollBack(operationId);
        });

        Assert.True(File.Exists(Path.Combine(Content(guideId), "guide.txt")));
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task RollBackRefusesACommittedDeletion()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Commit(operationId, guideId, () => { });
            journal.RollBack(operationId);
        }));

        Assert.Equal("Committed", library.Scalar("SELECT Phase FROM FileOperations"));
    }

    [Fact]
    public async Task FinishDeletesTheTrashAndRemovesTheOperation()
    {
        RemovalLibrary.WriteFile(Content(guideId), "nested/guide.txt", "bytes");
        RemovalLibrary.WriteFile(Content(otherId), "guide.txt", "kept");

        await library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            Directory.CreateDirectory(Path.GetDirectoryName(Trash(guideId))!);
            Directory.Move(Content(guideId), Trash(guideId));
            journal.Commit(operationId, guideId, () => { });
            journal.Finish(operationId);
        });

        Assert.False(Directory.Exists(Path.GetDirectoryName(Trash(guideId))!));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(Content(otherId), "guide.txt")));
        Assert.Equal("0", OperationCount());
    }

    [Fact]
    public async Task FinishRefusesAPreparedDeletion()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => library.RunDeletion(journal =>
        {
            journal.Prepare(operationId, guideId);
            journal.Finish(operationId);
        }));

        Assert.Equal("1|1|1", library.RowsFor(guideId));
        Assert.Equal("Prepared", library.Scalar("SELECT Phase FROM FileOperations"));
    }

    [Fact]
    public async Task RunDeletionHoldsTheWriteGate()
    {
        using SemaphoreSlim entered = new(0);
        using SemaphoreSlim release = new(0);
        Task deletion = library.Repository.RunDeletionAsync(_ =>
        {
            entered.Release();
            release.Wait();
            return true;
        }, CancellationToken.None);
        await entered.WaitAsync();

        Task<Game> edit = library.Repository.AddGameAsync("Blocked", null, null);
        await Task.Delay(200);
        Assert.False(edit.IsCompleted);

        release.Release();
        await deletion;
        await edit;
    }

    [Fact]
    public async Task RunDeletionWithACancelledTokenDoesNotRunTheWork()
    {
        bool ran = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.Repository.RunDeletionAsync(
            _ => ran = true, new CancellationToken(canceled: true)));

        Assert.False(ran);
    }
}
