using DesktopGuides.Infrastructure.Tests.FaultInjection;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class LibrarySnapshotTests : IAsyncLifetime
{
    private ExportFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await ExportFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private LibrarySnapshot Capture() => LibrarySnapshot.Capture(fixture.Library.Paths);

    [Fact]
    public void TwoCapturesOfAnUnchangedLibraryAreEqual()
    {
        LibrarySnapshot first = Capture();

        SnapshotAssert.Unchanged(first, Capture());
        Assert.Contains($"db:Guides/{fixture.TxtGuide:N}", first.Entries.Keys);
        Assert.Contains($"db:GuideAssets/{fixture.HtmlGuide:N}/guide.html", first.Entries.Keys);
        Assert.Contains($"fs:library/content/{fixture.TxtGuide:N}/guide.txt", first.Entries.Keys);
        Assert.Contains("db:Settings/Theme", first.Entries.Keys);
    }

    [Fact]
    public void ARowChangeIsReportedByItsKey()
    {
        LibrarySnapshot before = Capture();
        fixture.Library.Execute($"UPDATE ReadingStates SET EstimatedFraction = 0.75 WHERE GuideId = '{fixture.TxtGuide:N}'");

        SnapshotDiff diff = LibrarySnapshot.Diff(before, Capture());

        SnapshotAssert.Exactly(diff, [], [], [$"db:ReadingStates/{fixture.TxtGuide:N}"]);
    }

    [Fact]
    public void FilesAndEmptyFoldersAreKeyedByRelativePath()
    {
        LibrarySnapshot before = Capture();
        string content = fixture.Content(fixture.TxtGuide);
        File.WriteAllText(Path.Combine(content, "guide.txt"), "changed");
        Directory.CreateDirectory(Path.Combine(fixture.Library.Paths.StagingRoot, "empty"));
        File.Delete(Path.Combine(fixture.Content(fixture.PdfGuide), "manual.pdf"));

        SnapshotDiff diff = LibrarySnapshot.Diff(before, Capture());

        SnapshotAssert.Exactly(diff,
            ["fs:library/.staging/empty"],
            [$"fs:library/content/{fixture.PdfGuide:N}/manual.pdf"],
            [$"fs:library/content/{fixture.TxtGuide:N}/guide.txt"]);
    }

    [Fact]
    public void TheDatabaseFilesAreNotPartOfTheTree()
    {
        Assert.DoesNotContain(Capture().Entries.Keys, key => key.StartsWith("fs:library/library.sqlite", StringComparison.Ordinal));
    }

    [Fact]
    public void ALinkIsRecordedAndNotFollowed()
    {
        if (!OperatingSystem.IsWindows()) return;
        string outside = Path.Combine(fixture.Library.Root, "outside");
        RemovalLibrary.WriteFile(outside, "secret.txt", "secret");
        string link = Path.Combine(fixture.Library.Paths.StagingRoot, "linked");
        RemovalLibrary.CreateJunction(link, outside);
        try
        {
            LibrarySnapshot snapshot = Capture();

            Assert.Equal("link", snapshot.Entries["fs:library/.staging/linked"]);
            Assert.DoesNotContain("fs:library/.staging/linked/secret.txt", snapshot.Entries.Keys);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public void KeysContainingFindsEveryKeyOfAGuide()
    {
        string[] keys = Capture().KeysContaining(fixture.TxtGuide).ToArray();

        Assert.Contains($"db:Guides/{fixture.TxtGuide:N}", keys);
        Assert.Contains($"db:ReadingStates/{fixture.TxtGuide:N}", keys);
        Assert.Contains($"db:ReaderPreferences/{fixture.TxtGuide:N}", keys);
        Assert.Contains($"fs:library/content/{fixture.TxtGuide:N}", keys);
        Assert.Contains($"fs:library/content/{fixture.TxtGuide:N}/guide.txt", keys);
    }
}
