using DesktopGuides.Core.Backup;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Restore;

public sealed class LibraryRestorerValidationTests : IAsyncLifetime
{
    private RestoreFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await RestoreFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private async Task<LibraryRestoreException> Refused(string backup, LibraryRestoreIssue issue)
    {
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();
        LibraryRestoreException error = await Assert.ThrowsAsync<LibraryRestoreException>(
            () => fixture.StageAsync(backup));
        Assert.Equal(issue, error.Issue);
        fixture.AssertNoStage();
        Assert.Equal(before, fixture.LiveEntries());
        return error;
    }

    [Fact]
    public async Task ANewerSchemaSaysUpdate() =>
        await Refused(fixture.RewriteDatabase("PRAGMA user_version = 99;"), LibraryRestoreIssue.NewerVersion);

    [Fact]
    public async Task ASchemaOlderThanAnyExportIsInvalid() =>
        await Refused(fixture.RewriteDatabase("PRAGMA user_version = 3;"), LibraryRestoreIssue.DatabaseInvalid);

    [Fact]
    public async Task ADamagedDatabaseIsInvalid()
    {
        string backup = fixture.Rewrite(files =>
        {
            int index = files.FindIndex(file => file.Name == LibraryArchiveManifest.DatabasePath);
            byte[] damaged = files[index].Bytes.ToArray();
            Array.Fill(damaged, (byte)0x5A, 4096, Math.Min(4096, damaged.Length - 4096));
            files[index] = files[index] with { Bytes = damaged };
        });

        await Refused(backup, LibraryRestoreIssue.DatabaseInvalid);
    }

    [Fact]
    public async Task ADanglingForeignKeyIsInvalid() =>
        await Refused(fixture.RewriteDatabase(
                $"PRAGMA foreign_keys = OFF; UPDATE Guides SET GameId = '{Guid.NewGuid():N}' WHERE Id = '{fixture.Source.PdfGuide:N}';"),
            LibraryRestoreIssue.DatabaseInvalid);

    [Fact]
    public async Task AnAlteredSchemaIsInvalid() =>
        await Refused(fixture.RewriteDatabase("CREATE TABLE Extra (Id INTEGER);"), LibraryRestoreIssue.DatabaseInvalid);

    [Fact]
    public async Task AnUnfinishedFileOperationIsInvalid() =>
        await Refused(fixture.RewriteDatabase(
                $"INSERT INTO FileOperations (Id, Kind, Phase, ManifestJson, CreatedUtcMs) VALUES ('{Guid.NewGuid():N}', 'Import', 'Prepared', '{{}}', 0);"),
            LibraryRestoreIssue.DatabaseInvalid);

    [Fact]
    public async Task MissingArtworkNamesItsGame()
    {
        string backup = fixture.Rewrite(files => files.RemoveAll(file => file.Name.StartsWith("library/artwork/", StringComparison.Ordinal)));

        LibraryRestoreException error = await Refused(backup, LibraryRestoreIssue.ReferencesInvalid);

        Assert.Equal([fixture.Source.LinkedGame], error.GameIds);
        Assert.Empty(error.GuideIds);
        Assert.Equal(["Linked Game"], error.Titles);
    }

    [Fact]
    public async Task AMissingGuideFileNamesItsGuide()
    {
        string backup = fixture.Rewrite(files => files.RemoveAll(file => file.Name.EndsWith("/manual.pdf", StringComparison.Ordinal)));

        LibraryRestoreException error = await Refused(backup, LibraryRestoreIssue.ReferencesInvalid);

        Assert.Equal([fixture.Source.PdfGuide], error.GuideIds);
        Assert.Equal(["Manual"], error.Titles);
    }

    [Fact]
    public async Task AFileTheDatabaseDoesntReferenceIsInvalid() =>
        await Refused(fixture.Rewrite(files => files.Add(new ArchiveFile(
                $"library/content/{fixture.Source.TxtGuide:N}/stray.txt", "stray"u8.ToArray()))),
            LibraryRestoreIssue.ReferencesInvalid);

    [Fact]
    public async Task AFileWhoseContentDisagreesWithTheDatabaseIsInvalid()
    {
        // Self-consistent archive, but guide.txt no longer matches the hash the database recorded.
        string backup = fixture.Rewrite(files =>
        {
            int index = files.FindIndex(file => file.Name.EndsWith("/guide.txt", StringComparison.Ordinal));
            byte[] changed = files[index].Bytes.ToArray();
            changed[0] ^= 0xFF;
            files[index] = files[index] with { Bytes = changed };
        });

        LibraryRestoreException error = await Refused(backup, LibraryRestoreIssue.ReferencesInvalid);

        Assert.Equal([fixture.Source.TxtGuide], error.GuideIds);
    }

    [Fact]
    public async Task HtmlAssetsThatDontSumToTheGuideAreInvalid() =>
        await Refused(fixture.RewriteDatabase(
                $"UPDATE Guides SET ContentBytes = ContentBytes + 1 WHERE Id = '{fixture.Source.HtmlGuide:N}';"),
            LibraryRestoreIssue.ReferencesInvalid);
}
