using System.Security.Cryptography;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class ManagedPdfGuideLoaderTests
{
    private static async Task<(Guide Guide, string Source)> PublishAsync(PublisherHarness harness, string fixture = "pdf-access.pdf")
    {
        string source = harness.Sources.Copy(fixture, "guide.pdf");
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source));
        return ((await harness.Repository.GetGuideAsync(id))!, source);
    }

    private const string WrongAttempt = "wrong-7Q2x";

    private static async Task<Guide> PublishLockedAsync(PublisherHarness harness)
    {
        string source = harness.Sources.Copy("pdf-locked.pdf", "guide.pdf");
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source, password: "guide"));
        return (await harness.Repository.GetGuideAsync(id))!;
    }

    private static ManagedPdfGuideLoader Loader(PublisherHarness harness) => new(harness.Paths);

    private static string ManagedFile(PublisherHarness harness, Guide guide) =>
        harness.Paths.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);

    private static Task<PdfGuideLoad> LoadAsync(PublisherHarness harness, Guide guide) =>
        new ManagedPdfGuideLoader(harness.Paths).LoadAsync(guide, CancellationToken.None);

    private static async Task<PdfGuideLoadError> FailedAsync(PublisherHarness harness, Guide guide) =>
        Assert.IsType<PdfGuideLoadFailed>(await LoadAsync(harness, guide)).Error;

    [Fact]
    public async Task LoadsAPublishedGuide()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);

        PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(await LoadAsync(harness, guide));
        using PdfPageTextSource text = loaded.Text;

        Assert.Equal(ManagedFile(harness, guide), loaded.FilePath);
        Assert.Equal(1, text.PageCount);
        Assert.Contains("Tagged guide paragraph for Narrator",
            (await text.GetPageTextAsync(0, CancellationToken.None)).Text);
    }

    [Fact]
    public async Task LoadsAfterTheOriginalIsDeleted()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, string source) = await PublishAsync(harness);
        File.Delete(source);

        PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(await LoadAsync(harness, guide));
        loaded.Text.Dispose();
    }

    [Fact]
    public async Task DisposingTheTextReleasesTheManagedCopy()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(await LoadAsync(harness, guide));
        Assert.Throws<IOException>(() => File.Delete(loaded.FilePath));

        loaded.Text.Dispose();

        File.Delete(loaded.FilePath);
    }

    [Fact]
    public async Task MissingFileIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        File.Delete(ManagedFile(harness, guide));

        Assert.Equal(PdfGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task DeletedGuideFolderIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        Directory.Delete(harness.Paths.GetGuideRoot(guide.Id), recursive: true);

        Assert.Equal(PdfGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task FolderInPlaceOfTheFileIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        string file = ManagedFile(harness, guide);
        File.Delete(file);
        Directory.CreateDirectory(file);

        Assert.Equal(PdfGuideLoadError.Changed, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task GuideFolderThatIsALinkIsChanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        string root = harness.Paths.GetGuideRoot(guide.Id);
        string elsewhere = Path.Combine(Path.GetTempPath(), "desktop-guides-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.Move(root, elsewhere);
            RemovalLibrary.CreateJunction(root, elsewhere);

            Assert.Equal(PdfGuideLoadError.Changed, await FailedAsync(harness, guide));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root);
            if (Directory.Exists(elsewhere)) Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public async Task APathOutsideTheGuideIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);

        Assert.Equal(PdfGuideLoadError.Changed,
            await FailedAsync(harness, guide with { PrimaryRelativePath = "../escape.pdf" }));
    }

    [Fact]
    public async Task TruncatedBytesAreDamaged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness, "pdf-short.pdf");
        string file = ManagedFile(harness, guide);
        File.WriteAllBytes(file, File.ReadAllBytes(file)[..400]);

        Assert.Equal(PdfGuideLoadError.Damaged, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task GarbageBytesAreDamaged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        File.WriteAllBytes(ManagedFile(harness, guide), Enumerable.Repeat((byte)'x', 4096).ToArray());

        Assert.Equal(PdfGuideLoadError.Damaged, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task AnEmptyFileIsDamaged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        File.WriteAllBytes(ManagedFile(harness, guide), []);

        Assert.Equal(PdfGuideLoadError.Damaged, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task AnEncryptedCopyNeedsAPassword()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        File.Copy(P0Fixtures.Resolve("pdf-locked.pdf"), ManagedFile(harness, guide), overwrite: true);

        Assert.Equal(PdfGuideLoadError.PasswordRequired, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task AWrongPasswordIsIncorrect()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLockedAsync(harness);

        PdfGuideLoad load = await Loader(harness).LoadAsync(guide, WrongAttempt, CancellationToken.None);

        // The failure holds only an error code, so the attempt has nowhere to be echoed.
        Assert.Equal(PdfGuideLoadError.PasswordIncorrect, Assert.IsType<PdfGuideLoadFailed>(load).Error);
        // The managed copy is closed again: it can be opened for writing.
        // Only Windows locks a file that is open elsewhere.
        if (OperatingSystem.IsWindows())
        {
            using FileStream exclusive = new(ManagedFile(harness, guide), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }

    [Fact]
    public async Task TheRightPasswordGivesThePageText()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLockedAsync(harness);

        PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(
            await Loader(harness).LoadAsync(guide, "guide", CancellationToken.None));
        using PdfPageTextSource text = loaded.Text;

        Assert.Equal(1, text.PageCount);
        Assert.Contains("Locked guide secret page", (await text.GetPageTextAsync(0, CancellationToken.None)).Text);
    }

    [Fact]
    public async Task AChangedLockedCopyIsStillChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLockedAsync(harness);
        string managed = ManagedFile(harness, guide);
        File.Delete(managed);
        Directory.CreateDirectory(managed);

        PdfGuideLoad load = await Loader(harness).LoadAsync(guide, "guide", CancellationToken.None);

        Assert.Equal(PdfGuideLoadError.Changed, Assert.IsType<PdfGuideLoadFailed>(load).Error);
    }

    [Fact]
    public async Task AFailedLoadLeavesTheFileClosed()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        string file = ManagedFile(harness, guide);
        File.Copy(P0Fixtures.Resolve("pdf-locked.pdf"), file, overwrite: true);
        await FailedAsync(harness, guide);

        File.Delete(file);
    }

    [Fact]
    public async Task ADamagedLoadLeavesTheFileClosed()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        string file = ManagedFile(harness, guide);
        File.WriteAllBytes(file, File.ReadAllBytes(file)[..64]);
        Assert.Equal(PdfGuideLoadError.Damaged, await FailedAsync(harness, guide));

        File.Delete(file);
    }

    [Fact]
    public async Task ACancelledLoadThrows()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ManagedPdfGuideLoader(harness.Paths).LoadAsync(guide, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task LoadedHashIsTheManagedCopysSha256()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);

        PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(await LoadAsync(harness, guide));
        using PdfPageTextSource text = loaded.Text;

        string expected = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(ManagedFile(harness, guide))));
        Assert.Equal(expected, loaded.ContentSha256);
        Assert.Equal(guide.ContentSha256, loaded.ContentSha256, ignoreCase: true);
    }

    [Fact]
    public async Task BytesAppendedPastEofChangeTheHashButStillLoad()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        string managed = ManagedFile(harness, guide);
        File.SetAttributes(managed, FileAttributes.Normal);
        File.AppendAllText(managed, "\n% changed\n");

        PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(await LoadAsync(harness, guide));
        using PdfPageTextSource text = loaded.Text;

        Assert.NotEqual(guide.ContentSha256, loaded.ContentSha256, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, text.PageCount);
    }

    [Fact]
    public async Task HashReadFailureIsUnreadable()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        string managed = ManagedFile(harness, guide);
        // Padding past %%EOF keeps the locked byte beyond the header check's
        // buffered first read.
        File.SetAttributes(managed, FileAttributes.Normal);
        File.AppendAllText(managed, "\n%" + new string(' ', 64 * 1024) + "\n");
        using FileStream locker = new(managed, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        // A byte-range lock past the header makes the hash's read fail with an
        // IOException after the header check has passed.
        locker.Lock(locker.Length - 1, 1);

        Assert.Equal(PdfGuideLoadError.Unreadable, await FailedAsync(harness, guide));
    }
}
