using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Infrastructure.Tests.FaultInjection;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Restore;

public sealed class LibraryRestoreRecoveryTests : IAsyncLifetime
{
    private RestoreFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await RestoreFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private ManagedPathResolver Paths => fixture.Target.Paths;

    private async Task<(LibraryRestoreStage Stage, IReadOnlyDictionary<string, string> Before)> PrepareAsync(bool populated)
    {
        LibraryRestoreStage stage = await fixture.StageAsync();
        if (populated)
        {
            await fixture.AddPriorGameAsync();
        }
        else
        {
            await fixture.ClearTargetAsync();
        }
        return (stage, fixture.LiveEntries());
    }

    private void AssertRolledBackTo(IReadOnlyDictionary<string, string> before)
    {
        Assert.Equal(before, fixture.LiveEntries());
        Assert.False(File.Exists(fixture.MarkerPath));
        fixture.AssertNoStage();
        Assert.Empty(Directory.Exists(Paths.RecoveryRoot)
            ? Directory.EnumerateDirectories(Paths.RecoveryRoot, "restore-*")
            : []);
    }

    // Every file and folder under the data root, with each file's size.
    private string[] DataTree() =>
        new DirectoryInfo(Paths.DataRoot).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            .Select(entry => Path.GetRelativePath(Paths.DataRoot, entry.FullName) +
                             (entry is FileInfo file ? $":{file.Length}" : "/"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    [Theory]
    [InlineData(nameof(RestoreCheckpoint.MarkerWritten), true)]
    [InlineData(nameof(RestoreCheckpoint.MarkerWritten), false)]
    [InlineData(nameof(RestoreCheckpoint.PriorMoved), true)]
    [InlineData(nameof(RestoreCheckpoint.PriorMoved), false)]
    [InlineData(nameof(RestoreCheckpoint.Promoted), true)]
    [InlineData(nameof(RestoreCheckpoint.Promoted), false)]
    public async Task AnInterruptedSwapRollsBackOnTheNextStart(string checkpoint, bool populated)
    {
        RestoreCheckpoint point = Enum.Parse<RestoreCheckpoint>(checkpoint);
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(populated);

        await Assert.ThrowsAsync<InjectedFault>(() =>
            fixture.Restorer(FaultFixture.FaultAt(point)).ReplaceAsync(stage, CancellationToken.None));
        RestoreRecoveryOutcome outcome = LibraryRestoreRecovery.Run(Paths, verifyRestore: false);

        Assert.Equal(RestoreRecoveryOutcome.RolledBack, outcome);
        AssertRolledBackTo(before);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AConfirmedRestoreReplacesTheLibraryAndCleansUp(bool populated)
    {
        (LibraryRestoreStage stage, _) = await PrepareAsync(populated);

        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Assert.Equal(RestoreRecoveryOutcome.PendingVerify, LibraryRestoreRecovery.Run(Paths, verifyRestore: true));
        await fixture.Target.RestartAsync();
        LibraryRestoreRecovery.Complete(Paths);

        IReadOnlyDictionary<string, string> source = LibrarySnapshot.Capture(fixture.Source.Library.Paths).Entries;
        Assert.Equal(source, fixture.LiveEntries());
        Assert.False(File.Exists(fixture.MarkerPath));
        fixture.AssertNoStage();
        Assert.Empty(Directory.EnumerateDirectories(Paths.RecoveryRoot, "restore-*"));
    }

    [Fact]
    public async Task APromotedRestoreFoundByALaterStartRollsBack()
    {
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);

        Assert.Equal(RestoreRecoveryOutcome.RolledBack, LibraryRestoreRecovery.Run(Paths, verifyRestore: false));

        AssertRolledBackTo(before);
    }

    [Fact]
    public async Task APromotedRestoreRollsBackWithoutAStagingFolder()
    {
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Directory.Delete(fixture.StagingParent, recursive: true);

        Assert.Equal(RestoreRecoveryOutcome.RolledBack, LibraryRestoreRecovery.Run(Paths, verifyRestore: false));

        AssertRolledBackTo(before);
    }

    [Fact]
    public async Task ARestoredLibraryThatWontOpenRollsBack()
    {
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Assert.Equal(RestoreRecoveryOutcome.PendingVerify, LibraryRestoreRecovery.Run(Paths, verifyRestore: true));
        File.WriteAllBytes(Paths.DatabasePath, Enumerable.Repeat((byte)0x5A, 4096).ToArray());

        await Assert.ThrowsAsync<LibraryOpenException>(() => fixture.Target.RestartAsync());
        LibraryRestoreRecovery.RollBack(Paths);
        await fixture.Target.RestartAsync();

        AssertRolledBackTo(before);
    }

    [Fact]
    public async Task ACrashWhileCompletingKeepsTheRestoredLibrary()
    {
        (LibraryRestoreStage stage, _) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        LibraryRestoreRecovery.Run(Paths, verifyRestore: true);
        await fixture.Target.RestartAsync();

        Assert.Throws<InjectedFault>(() =>
            LibraryRestoreRecovery.Complete(Paths, FaultFixture.FaultAt(RestoreCheckpoint.Confirmed)));
        RestoreRecoveryOutcome outcome = LibraryRestoreRecovery.Run(Paths, verifyRestore: false);

        Assert.Equal(RestoreRecoveryOutcome.None, outcome);
        Assert.Equal("2", fixture.Target.Scalar("SELECT COUNT(*) FROM Games"));
        Assert.False(File.Exists(fixture.MarkerPath));
        Assert.Empty(Directory.EnumerateDirectories(Paths.RecoveryRoot, "restore-*"));
    }

    [Fact]
    public async Task CompletingAnUnfinishedRollbackIsIncomplete()
    {
        (LibraryRestoreStage stage, _) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Assert.Throws<InjectedFault>(() =>
            LibraryRestoreRecovery.Run(Paths, verifyRestore: false, FaultFixture.FaultAt(RestoreCheckpoint.RolledAside)));

        LibraryOpenException error = Assert.Throws<LibraryOpenException>(() => LibraryRestoreRecovery.Complete(Paths));

        Assert.Equal(LibraryOpenIssue.RestoreIncomplete, error.Issue);
        Assert.Equal(new RestoreMarker(stage.StageId, true, RestoreMarkerPhase.RollingBack), RestoreMarker.Read(Paths));
        Assert.True(Directory.Exists(RestoreMarker.PriorRoot(Paths, stage.StageId)));
    }

    [Fact]
    public async Task CompletingAnUnpromotedSwapIsIncompleteAndChangesNothing()
    {
        (LibraryRestoreStage stage, _) = await PrepareAsync(true);
        await Assert.ThrowsAsync<InjectedFault>(() =>
            fixture.Restorer(FaultFixture.FaultAt(RestoreCheckpoint.PriorMoved)).ReplaceAsync(stage, CancellationToken.None));
        string[] tree = DataTree();
        string marker = File.ReadAllText(fixture.MarkerPath);

        LibraryOpenException error = Assert.Throws<LibraryOpenException>(() => LibraryRestoreRecovery.Complete(Paths));

        Assert.Equal(LibraryOpenIssue.RestoreIncomplete, error.Issue);
        Assert.Equal(marker, File.ReadAllText(fixture.MarkerPath));
        Assert.Equal(tree, DataTree());
        Assert.True(Directory.Exists(RestoreMarker.StagedLibrary(Paths, stage.StageId)));
        Assert.True(Directory.Exists(RestoreMarker.PriorRoot(Paths, stage.StageId)));
    }

    [Fact]
    public async Task RollingBackAConfirmedRestoreKeepsTheRestoredLibrary()
    {
        (LibraryRestoreStage stage, _) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Assert.Equal(RestoreRecoveryOutcome.PendingVerify, LibraryRestoreRecovery.Run(Paths, verifyRestore: true));
        await fixture.Target.RestartAsync();
        Assert.Throws<InjectedFault>(() =>
            LibraryRestoreRecovery.Complete(Paths, FaultFixture.FaultAt(RestoreCheckpoint.Confirmed)));

        LibraryRestoreRecovery.RollBack(Paths);

        Assert.Equal("2", fixture.Target.Scalar("SELECT COUNT(*) FROM Games"));
        Assert.False(File.Exists(fixture.MarkerPath));
        fixture.AssertNoStage();
        Assert.Empty(Directory.EnumerateDirectories(Paths.RecoveryRoot, "restore-*"));
    }

    [Fact]
    public async Task AMissingPromotedLibraryRollsBackEvenWhenVerifying()
    {
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Directory.Delete(Paths.LibraryRoot, recursive: true);

        Assert.Equal(RestoreRecoveryOutcome.RolledBack, LibraryRestoreRecovery.Run(Paths, verifyRestore: true));

        AssertRolledBackTo(before);
    }

    [Theory]
    [InlineData(nameof(RestoreCheckpoint.RolledAside), true)]
    [InlineData(nameof(RestoreCheckpoint.RolledAside), false)]
    [InlineData(nameof(RestoreCheckpoint.PriorReturned), true)]
    [InlineData(nameof(RestoreCheckpoint.PriorReturned), false)]
    public async Task ACrashDuringRollbackFinishesOnTheNextStart(string checkpoint, bool populated)
    {
        RestoreCheckpoint point = Enum.Parse<RestoreCheckpoint>(checkpoint);
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(populated);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);

        Assert.Throws<InjectedFault>(() =>
            LibraryRestoreRecovery.Run(Paths, verifyRestore: false, FaultFixture.FaultAt(point)));
        RestoreRecoveryOutcome outcome = LibraryRestoreRecovery.Run(Paths, verifyRestore: false);

        Assert.Equal(RestoreRecoveryOutcome.RolledBack, outcome);
        AssertRolledBackTo(before);
    }

    [Fact]
    public async Task ACrashAfterThePriorRootReturnsKeepsIt()
    {
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Assert.Throws<InjectedFault>(() =>
            LibraryRestoreRecovery.Run(Paths, verifyRestore: false, FaultFixture.FaultAt(RestoreCheckpoint.PriorReturned)));

        // Twice more: each start must leave the returned prior root in place.
        LibraryRestoreRecovery.Run(Paths, verifyRestore: false);
        LibraryRestoreRecovery.Run(Paths, verifyRestore: false);

        AssertRolledBackTo(before);
    }

    [Fact]
    public async Task AMissingParkedLibraryIsIncomplete()
    {
        (LibraryRestoreStage stage, _) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Directory.Delete(RestoreMarker.PriorRoot(Paths, stage.StageId), recursive: true);
        Directory.Delete(Paths.LibraryRoot, recursive: true);

        LibraryOpenException error = Assert.Throws<LibraryOpenException>(() =>
            LibraryRestoreRecovery.Run(Paths, verifyRestore: false));

        Assert.Equal(LibraryOpenIssue.RestoreIncomplete, error.Issue);
        Assert.True(File.Exists(fixture.MarkerPath));
    }

    [Fact]
    public async Task AMissingParkedLibraryBehindAPromotedRestoreIsIncomplete()
    {
        (LibraryRestoreStage stage, _) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Directory.Delete(RestoreMarker.PriorRoot(Paths, stage.StageId), recursive: true);
        IReadOnlyDictionary<string, string> restored = fixture.LiveEntries();

        LibraryOpenException error = Assert.Throws<LibraryOpenException>(() =>
            LibraryRestoreRecovery.Run(Paths, verifyRestore: false));

        Assert.Equal(LibraryOpenIssue.RestoreIncomplete, error.Issue);
        Assert.Equal(new RestoreMarker(stage.StageId, true, RestoreMarkerPhase.Swapping), RestoreMarker.Read(Paths));
        Assert.Equal(restored, fixture.LiveEntries());
    }

    [Fact]
    public async Task AMissingParkedLibraryBeforePromotionIsIncomplete()
    {
        (LibraryRestoreStage stage, _) = await PrepareAsync(true);
        await Assert.ThrowsAsync<InjectedFault>(() =>
            fixture.Restorer(FaultFixture.FaultAt(RestoreCheckpoint.PriorMoved)).ReplaceAsync(stage, CancellationToken.None));
        Directory.Delete(RestoreMarker.PriorRoot(Paths, stage.StageId), recursive: true);

        LibraryOpenException error = Assert.Throws<LibraryOpenException>(() =>
            LibraryRestoreRecovery.Run(Paths, verifyRestore: false));

        Assert.Equal(LibraryOpenIssue.RestoreIncomplete, error.Issue);
        Assert.True(File.Exists(fixture.MarkerPath));
        Assert.True(Directory.Exists(RestoreMarker.StagedLibrary(Paths, stage.StageId)));
    }

    [Fact]
    public void AMalformedMarkerIsIncomplete()
    {
        File.WriteAllText(fixture.MarkerPath, "not json");

        LibraryOpenException error = Assert.Throws<LibraryOpenException>(() =>
            LibraryRestoreRecovery.Run(Paths, verifyRestore: false));

        Assert.Equal(LibraryOpenIssue.RestoreIncomplete, error.Issue);
    }

    [Fact]
    public async Task WithoutAMarkerLeftoversAreDeletedAndMigrationCopiesKept()
    {
        await fixture.StageAsync();
        string parked = Path.Combine(Paths.RecoveryRoot, $"restore-{Guid.NewGuid():N}", "library");
        Directory.CreateDirectory(parked);
        string migrationCopy = Path.Combine(Paths.RecoveryRoot, "library-v3-20261010000000-x.sqlite");
        File.WriteAllText(migrationCopy, "copy");

        Assert.Equal(RestoreRecoveryOutcome.None, LibraryRestoreRecovery.Run(Paths, verifyRestore: false));

        fixture.AssertNoStage();
        Assert.False(Directory.Exists(Path.GetDirectoryName(parked)));
        Assert.True(File.Exists(migrationCopy));
    }

    [Fact]
    public void LeftoverCleanupDoesntFollowAJunctionedRecoveryFolder()
    {
        if (!OperatingSystem.IsWindows()) return;
        string outside = Path.Combine(fixture.Target.Root, "outside");
        string kept = Path.Combine(outside, "restore-x");
        Directory.CreateDirectory(kept);
        Directory.Delete(Paths.RecoveryRoot, recursive: true);
        RemovalLibrary.CreateJunction(Paths.RecoveryRoot, outside);

        RestoreRecoveryOutcome outcome;
        try
        {
            outcome = LibraryRestoreRecovery.Run(Paths, verifyRestore: false);
        }
        finally
        {
            Directory.Delete(Paths.RecoveryRoot);
        }

        Assert.Equal(RestoreRecoveryOutcome.None, outcome);
        Assert.True(Directory.Exists(kept));
    }
}
