using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Html;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibraryArchivePlanTests : IAsyncLifetime
{
    private ExportFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await ExportFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private LibraryArchivePlan Plan(int maxEntries = LibraryArchiveManifest.MaxEntries) =>
        LibraryArchivePlan.Read(fixture.Snapshot(), fixture.Library.Paths, maxEntries);

    private LibraryExportException Damaged() =>
        Assert.Throws<LibraryExportException>(() => Plan());

    [Fact]
    public void ThePlanListsEveryReferencedFileWithItsRecordedSizeAndHash()
    {
        LibraryArchivePlan plan = Plan();

        Assert.Equal((2, 3), (plan.Games, plan.Guides));
        Assert.Equal(fixture.ExpectedArchivePaths(), plan.Files.Select(file => file.ArchivePath));
        foreach (PlannedArchiveFile file in plan.Files)
        {
            Assert.Equal(file.Bytes, new FileInfo(file.SourcePath).Length);
            Assert.Equal(file.Sha256,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file.SourcePath))));
        }
        Assert.Equal(fixture.LinkedGame, plan.Files.Single(file => file.ArchivePath.Contains("/artwork/")).GameId);
    }

    [Fact]
    public void StrayFilesAreNotPlanned()
    {
        RemovalLibrary.WriteFile(fixture.Content(fixture.TxtGuide), "stray.txt", "not referenced");
        RemovalLibrary.WriteFile(Path.Combine(fixture.Library.Paths.ContentRoot, Guid.NewGuid().ToString("N")), "x.txt", "orphan");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.ArtworkFile())!, new string('c', 64) + ".png"), "old cover");

        Assert.Equal(fixture.ExpectedArchivePaths(), Plan().Files.Select(file => file.ArchivePath));
    }

    [Fact]
    public void AMissingGuideFileIsDamaged()
    {
        File.Delete(Path.Combine(fixture.Content(fixture.TxtGuide), "guide.txt"));

        LibraryExportException error = Damaged();

        Assert.Equal(LibraryExportIssue.ManagedFilesDamaged, error.Issue);
        Assert.Equal(new[] { fixture.TxtGuide }, error.GuideIds);
        Assert.Empty(error.GameIds);
    }

    [Fact]
    public void AFileOfTheWrongSizeIsDamaged()
    {
        File.AppendAllText(Path.Combine(fixture.Content(fixture.PdfGuide), "manual.pdf"), "x");

        Assert.Equal(new[] { fixture.PdfGuide }, Damaged().GuideIds);
    }

    [Fact]
    public void HtmlRowsThatDisagreeWithTheGuideAreDamaged()
    {
        fixture.Library.Execute($"UPDATE Guides SET ContentBytes = ContentBytes + 1 WHERE Id = '{fixture.HtmlGuide:N}'");

        Assert.Equal(new[] { fixture.HtmlGuide }, Damaged().GuideIds);
    }

    [Fact]
    public void MissingArtworkIsDamaged()
    {
        File.Delete(fixture.ArtworkFile());

        LibraryExportException error = Damaged();

        Assert.Empty(error.GuideIds);
        Assert.Equal(new[] { fixture.LinkedGame }, error.GameIds);
    }

    [Fact]
    public void EveryDamagedGuideIsListed()
    {
        File.Delete(Path.Combine(fixture.Content(fixture.TxtGuide), "guide.txt"));
        File.Delete(Path.Combine(fixture.Content(fixture.PdfGuide), "manual.pdf"));

        Assert.Equal(new[] { fixture.TxtGuide, fixture.PdfGuide }.OrderBy(id => id), Damaged().GuideIds);
    }

    [Fact]
    public async Task SharedRelativePathIsArchivedOnce()
    {
        Guid shared = await fixture.AddHtmlGuideAsync(fixture.PlainGame, "Shared",
            ("guide.html", "guide.html", GuideAssetKind.EntryHtml, "<html/>"u8.ToArray()),
            ("a.png", "a.png", GuideAssetKind.Image, "png"u8.ToArray()),
            ("img/a.png", "a.png", GuideAssetKind.Image, "png"u8.ToArray()));

        LibraryArchivePlan plan = Plan();

        Assert.Single(plan.Files, file => file.ArchivePath == $"library/content/{shared:N}/a.png");
    }

    [Fact]
    public void TooManyEntriesFailsWithLibraryTooLarge()
    {
        LibraryExportException error = Assert.Throws<LibraryExportException>(() => Plan(maxEntries: 3));

        Assert.Equal(LibraryExportIssue.LibraryTooLarge, error.Issue);
    }

    [Fact]
    public void AJunctionedGuideFolderIsDamaged()
    {
        if (!OperatingSystem.IsWindows()) return;
        string folder = fixture.Content(fixture.TxtGuide);
        string moved = Path.Combine(fixture.Library.Root, "moved-guide");
        Directory.Move(folder, moved);
        RemovalLibrary.CreateJunction(folder, moved);
        try
        {
            Assert.Equal(new[] { fixture.TxtGuide }, Damaged().GuideIds);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void ALinkedGuideFileIsDamaged()
    {
        if (!OperatingSystem.IsWindows()) return;
        string file = Path.Combine(fixture.Content(fixture.TxtGuide), "guide.txt");
        string moved = Path.Combine(fixture.Library.Root, "moved-guide.txt");
        File.Move(file, moved);
        File.CreateSymbolicLink(file, moved);
        try
        {
            Assert.Equal(new[] { fixture.TxtGuide }, Damaged().GuideIds);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ALinkedArtworkFileIsDamaged()
    {
        if (!OperatingSystem.IsWindows()) return;
        string file = fixture.ArtworkFile();
        string moved = Path.Combine(fixture.Library.Root, "moved-cover.png");
        File.Move(file, moved);
        File.CreateSymbolicLink(file, moved);
        try
        {
            Assert.Equal(new[] { fixture.LinkedGame }, Damaged().GameIds);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
