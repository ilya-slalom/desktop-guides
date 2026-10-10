using DesktopGuides.Core.Backup;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Restore;

public sealed class LibraryRestorerSwapTests : IAsyncLifetime
{
    private RestoreFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await RestoreFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Fact]
    public async Task ReplaceParksThePriorLibraryAndPromotesTheStage()
    {
        await fixture.AddPriorGameAsync();
        LibraryRestoreStage stage = await fixture.StageAsync();

        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);

        Assert.Equal("2", fixture.Target.Scalar("SELECT COUNT(*) FROM Games"));
        string prior = RestoreMarker.PriorRoot(fixture.Target.Paths, stage.StageId);
        Assert.True(File.Exists(Path.Combine(prior, "library.sqlite")));
        Assert.Equal(new RestoreMarker(stage.StageId, true, RestoreMarkerPhase.Swapping),
            RestoreMarker.Read(fixture.Target.Paths));
    }

    [Fact]
    public async Task ReplacingNoLibraryRecordsThatThereWasNone()
    {
        LibraryRestoreStage stage = await fixture.StageAsync();
        await fixture.ClearTargetAsync();

        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);

        Assert.True(File.Exists(fixture.Target.Paths.DatabasePath));
        Assert.False(RestoreMarker.Read(fixture.Target.Paths)!.PriorExists);
    }

    [Fact]
    public async Task AMarkerRoundTrips()
    {
        RestoreMarker marker = new(Guid.NewGuid(), true, RestoreMarkerPhase.RollingBack);

        marker.Write(fixture.Target.Paths);

        Assert.Equal(marker, RestoreMarker.Read(fixture.Target.Paths));
        Assert.Empty(Directory.EnumerateFiles(fixture.Target.Paths.DataRoot, "restore.marker.*.tmp"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"stageId\":\"x\",\"priorExists\":true,\"phase\":\"Swapping\"}")]
    public void AMalformedMarkerIsInvalid(string text)
    {
        File.WriteAllText(fixture.MarkerPath, text);

        Assert.Throws<InvalidDataException>(() => RestoreMarker.Read(fixture.Target.Paths));
    }

    [Fact]
    public async Task AMissingStageFailsBeforeAnythingMoves()
    {
        LibraryRestoreStage stage = await fixture.StageAsync();
        fixture.Restorer().DiscardStage(stage);
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();

        LibraryRestoreException error = await Assert.ThrowsAsync<LibraryRestoreException>(
            () => fixture.Restorer().ReplaceAsync(stage, CancellationToken.None));

        Assert.Equal(LibraryRestoreIssue.SwapFailed, error.Issue);
        Assert.Equal(before, fixture.LiveEntries());
        Assert.False(File.Exists(fixture.MarkerPath));
    }

    [Fact]
    public async Task ALibraryHeldOpenFailsTheSwapAndChangesNothing()
    {
        if (!OperatingSystem.IsWindows()) return;
        LibraryRestoreStage stage = await fixture.StageAsync();
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();

        LibraryRestoreException error;
        using (new FileStream(fixture.Target.Paths.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            error = await Assert.ThrowsAsync<LibraryRestoreException>(
                () => fixture.Restorer().ReplaceAsync(stage, CancellationToken.None));
        }

        Assert.Equal(LibraryRestoreIssue.SwapFailed, error.Issue);
        Assert.Equal(before, fixture.LiveEntries());
        Assert.False(File.Exists(fixture.MarkerPath));
        Assert.True(Directory.Exists(RestoreMarker.StagedLibrary(fixture.Target.Paths, stage.StageId)));
    }

    [Fact]
    public async Task AJunctionedRecoveryFolderFailsTheSwap()
    {
        if (!OperatingSystem.IsWindows()) return;
        LibraryRestoreStage stage = await fixture.StageAsync();
        string outside = Path.Combine(fixture.Target.Root, "outside");
        Directory.CreateDirectory(outside);
        Directory.Delete(fixture.Target.Paths.RecoveryRoot, recursive: true);
        RemovalLibrary.CreateJunction(fixture.Target.Paths.RecoveryRoot, outside);

        LibraryRestoreException error;
        try
        {
            error = await Assert.ThrowsAsync<LibraryRestoreException>(
                () => fixture.Restorer().ReplaceAsync(stage, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(fixture.Target.Paths.RecoveryRoot);
        }

        Assert.Equal(LibraryRestoreIssue.SwapFailed, error.Issue);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.False(File.Exists(fixture.MarkerPath));
    }

    [Fact]
    public async Task AJunctionedStageFailsTheSwap()
    {
        if (!OperatingSystem.IsWindows()) return;
        LibraryRestoreStage stage = await fixture.StageAsync();
        string staged = RestoreMarker.StagedLibrary(fixture.Target.Paths, stage.StageId);
        string moved = Path.Combine(fixture.Target.Root, "moved-stage");
        Directory.Move(staged, moved);
        RemovalLibrary.CreateJunction(staged, moved);
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();

        LibraryRestoreException error;
        try
        {
            error = await Assert.ThrowsAsync<LibraryRestoreException>(
                () => fixture.Restorer().ReplaceAsync(stage, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(staged);
        }

        Assert.Equal(LibraryRestoreIssue.SwapFailed, error.Issue);
        Assert.Equal(before, fixture.LiveEntries());
    }

    [Fact]
    public async Task AFailedPromotionPutsThePriorLibraryBack()
    {
        if (!OperatingSystem.IsWindows()) return;
        await fixture.AddPriorGameAsync();
        Guid stageId = Guid.Empty;
        FileStream? held = null;
        LibraryRestorer restorer = fixture.Restorer(checkpoint =>
        {
            if (checkpoint != RestoreCheckpoint.PriorMoved) return;
            held = new FileStream(Path.Combine(RestoreMarker.StagedLibrary(fixture.Target.Paths, stageId), "library.sqlite"),
                FileMode.Open, FileAccess.Read, FileShare.None);
        });
        LibraryRestoreStage stage = await restorer.StageAsync(fixture.Backup, null, CancellationToken.None);
        stageId = stage.StageId;
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();

        LibraryRestoreException error;
        try
        {
            error = await Assert.ThrowsAsync<LibraryRestoreException>(
                () => restorer.ReplaceAsync(stage, CancellationToken.None));
        }
        finally
        {
            held?.Dispose();
        }

        Assert.Equal(LibraryRestoreIssue.SwapFailed, error.Issue);
        Assert.Equal(before, fixture.LiveEntries());
        Assert.False(File.Exists(fixture.MarkerPath));
        Assert.True(Directory.Exists(RestoreMarker.StagedLibrary(fixture.Target.Paths, stageId)));
    }

    [Fact]
    public async Task AFailedRollbackKeepsTheMarkerAndParkedLibrary()
    {
        if (!OperatingSystem.IsWindows()) return;
        await fixture.AddPriorGameAsync();
        Guid stageId = Guid.Empty;
        List<FileStream> locks = [];
        LibraryRestorer restorer = fixture.Restorer(checkpoint =>
        {
            if (checkpoint != RestoreCheckpoint.PriorMoved) return;
            foreach (string folder in new[]
                     {
                         RestoreMarker.StagedLibrary(fixture.Target.Paths, stageId),
                         RestoreMarker.PriorRoot(fixture.Target.Paths, stageId)
                     })
            {
                locks.Add(new FileStream(Path.Combine(folder, "library.sqlite"),
                    FileMode.Open, FileAccess.Read, FileShare.None));
            }
        });
        LibraryRestoreStage stage = await restorer.StageAsync(fixture.Backup, null, CancellationToken.None);
        stageId = stage.StageId;

        try
        {
            LibraryRestoreException error = await Assert.ThrowsAsync<LibraryRestoreException>(
                () => restorer.ReplaceAsync(stage, CancellationToken.None));

            Assert.Equal(LibraryRestoreIssue.SwapFailed, error.Issue);
        }
        finally
        {
            foreach (FileStream held in locks) held.Dispose();
        }

        Assert.Equal(new RestoreMarker(stageId, true, RestoreMarkerPhase.Swapping),
            RestoreMarker.Read(fixture.Target.Paths));
        Assert.False(Directory.Exists(fixture.Target.Paths.LibraryRoot));
        Assert.True(Directory.Exists(RestoreMarker.PriorRoot(fixture.Target.Paths, stageId)));
        Assert.True(Directory.Exists(RestoreMarker.StagedLibrary(fixture.Target.Paths, stageId)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnExistingMarkerRefusesTheSwapAndIsLeftAlone(bool malformed)
    {
        LibraryRestoreStage stage = await fixture.StageAsync();
        if (malformed)
        {
            File.WriteAllText(fixture.MarkerPath, "not json");
        }
        else
        {
            new RestoreMarker(Guid.NewGuid(), true, RestoreMarkerPhase.Confirmed).Write(fixture.Target.Paths);
        }
        string markerBefore = File.ReadAllText(fixture.MarkerPath);
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();

        LibraryRestoreException error = await Assert.ThrowsAsync<LibraryRestoreException>(
            () => fixture.Restorer().ReplaceAsync(stage, CancellationToken.None));

        Assert.Equal(LibraryRestoreIssue.SwapFailed, error.Issue);
        Assert.Equal(markerBefore, File.ReadAllText(fixture.MarkerPath));
        Assert.Equal(before, fixture.LiveEntries());
        Assert.True(Directory.Exists(RestoreMarker.StagedLibrary(fixture.Target.Paths, stage.StageId)));
    }
}
