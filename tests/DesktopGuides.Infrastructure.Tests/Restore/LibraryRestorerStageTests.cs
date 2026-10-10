using DesktopGuides.Core.Backup;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Infrastructure.Tests.FaultInjection;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Restore;

public sealed class LibraryRestorerStageTests : IAsyncLifetime
{
    private RestoreFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await RestoreFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private async Task<LibraryRestoreException> Refused(
        string backup, LibraryRestoreIssue issue, Func<string, long>? freeBytes = null)
    {
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();
        LibraryRestoreException error = await Assert.ThrowsAsync<LibraryRestoreException>(
            () => fixture.StageAsync(backup, freeBytes: freeBytes));
        Assert.Equal(issue, error.Issue);
        fixture.AssertNoStage();
        Assert.Equal(before, fixture.LiveEntries());
        return error;
    }

    [Fact]
    public async Task AStagedBackupHasItsCountsAndLeavesTheLibraryAlone()
    {
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();

        LibraryRestoreStage stage = await fixture.StageAsync();

        Assert.Equal((2, 3, "1.0.0.0", 4), (stage.Games, stage.Guides, stage.AppVersion, stage.SchemaVersion));
        string library = Path.Combine(LibraryRestorer.StageRoot(fixture.Target.Paths, stage.StageId), "library");
        Assert.True(File.Exists(Path.Combine(library, "library.sqlite")));
        Assert.True(File.Exists(Path.Combine(library, "content", fixture.Source.TxtGuide.ToString("N"), "guide.txt")));
        Assert.Equal(before, fixture.LiveEntries());
    }

    [Fact]
    public async Task DiscardDeletesTheStage()
    {
        LibraryRestoreStage stage = await fixture.StageAsync();

        fixture.Restorer().DiscardStage(stage);

        fixture.AssertNoStage();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardKeepsAStageThatAMarkerMayName(bool malformed)
    {
        LibraryRestoreStage stage = await fixture.StageAsync();
        if (malformed)
        {
            File.WriteAllText(fixture.MarkerPath, "not json");
        }
        else
        {
            new RestoreMarker(stage.StageId, true, RestoreMarkerPhase.Swapping).Write(fixture.Target.Paths);
        }

        fixture.Restorer().DiscardStage(stage);

        Assert.True(Directory.Exists(RestoreMarker.StagedLibrary(fixture.Target.Paths, stage.StageId)));
    }

    [Fact]
    public async Task AMissingFileIsUnavailable() =>
        await Refused(Path.Combine(fixture.Target.Root, "nowhere.zip"), LibraryRestoreIssue.SourceUnavailable);

    [Fact]
    public async Task AFileThatIsntAZipIsInvalid() =>
        await Refused(fixture.WriteBytes("not a zip"u8.ToArray()), LibraryRestoreIssue.ArchiveInvalid);

    [Fact]
    public async Task ATruncatedZipIsInvalid()
    {
        byte[] bytes = File.ReadAllBytes(fixture.Backup);
        await Refused(fixture.WriteBytes(bytes[..(bytes.Length / 2)]), LibraryRestoreIssue.ArchiveInvalid);
    }

    [Fact]
    public async Task AChangedFileIsInvalid()
    {
        string backup = fixture.Rewrite(files =>
        {
            int index = files.FindIndex(file => file.Name.EndsWith("/guide.txt", StringComparison.Ordinal));
            byte[] changed = files[index].Bytes.ToArray();
            changed[0] ^= 0xFF;
            files[index] = files[index] with { Bytes = changed };
        }, rewriteManifest: false);

        await Refused(backup, LibraryRestoreIssue.ArchiveInvalid);
    }

    [Fact]
    public async Task AnEntryTheManifestDoesntListIsInvalid() =>
        await Refused(fixture.Rewrite(files => files.Add(new ArchiveFile(
                $"library/content/{fixture.Source.TxtGuide:N}/extra.txt", "extra"u8.ToArray())),
            rewriteManifest: false),
            LibraryRestoreIssue.ArchiveInvalid);

    [Fact]
    public async Task AListedEntryMissingFromTheArchiveIsInvalid() =>
        await Refused(fixture.Rewrite(files => files.RemoveAll(file => file.Name.EndsWith("/manual.pdf", StringComparison.Ordinal)),
            rewriteManifest: false),
            LibraryRestoreIssue.ArchiveInvalid);

    [Fact]
    public async Task AnEntryLongerThanItsManifestSizeIsInvalid() =>
        await Refused(fixture.Rewrite(_ => { }, editManifest: manifest => manifest with
        {
            Entries = manifest.Entries
                .Select(entry => entry.Path.EndsWith("/guide.txt", StringComparison.Ordinal)
                    ? entry with { Bytes = entry.Bytes - 1 }
                    : entry)
                .ToArray()
        }), LibraryRestoreIssue.ArchiveInvalid);

    [Theory]
    [InlineData("library/content/../../evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("/library/library.sqlite")]
    [InlineData("library/content/0123456789abcdef0123456789abcdef/..\\..\\evil.txt")]
    public async Task ANameOutsideTheLibraryIsUnsafe(string name) =>
        await Refused(fixture.Rewrite(files => files.Add(new ArchiveFile(name, "x"u8.ToArray())),
            rewriteManifest: false),
            LibraryRestoreIssue.ArchiveUnsafe);

    [Theory]
    [InlineData("Notes.txt", "notes.txt")]
    [InlineData("caf\u00e9.txt", "cafe\u0301.txt")]
    public async Task NamesThatCollideOnDiskAreUnsafe(string first, string second) =>
        await Refused(fixture.Rewrite(files =>
        {
            files.Add(new ArchiveFile($"library/content/{fixture.Source.TxtGuide:N}/{first}", "a"u8.ToArray()));
            files.Add(new ArchiveFile($"library/content/{fixture.Source.TxtGuide:N}/{second}", "b"u8.ToArray()));
        }), LibraryRestoreIssue.ArchiveUnsafe);

    [Fact]
    public async Task NotEnoughSpaceForTheCopyStopsBeforeCopying()
    {
        LibraryRestoreException error = await Refused(fixture.Backup, LibraryRestoreIssue.NotEnoughSpace, _ => 0);

        Assert.True(error.BytesNeeded > new FileInfo(fixture.Backup).Length);
    }

    [Fact]
    public async Task NotEnoughSpaceToUnpackStopsBeforeExtracting()
    {
        int calls = 0;
        await Refused(fixture.Backup, LibraryRestoreIssue.NotEnoughSpace,
            _ => ++calls == 1 ? long.MaxValue : 0);
    }

    [Theory]
    [InlineData("Copied")]
    [InlineData("Extracted")]
    public async Task CancellationLeavesNoStage(string checkpoint)
    {
        RestoreCheckpoint point = Enum.Parse<RestoreCheckpoint>(checkpoint);
        using CancellationTokenSource cancel = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.StageAsync(checkpoint: FaultFixture.CancelAt(point, cancel), token: cancel.Token));

        fixture.AssertNoStage();
    }
}
