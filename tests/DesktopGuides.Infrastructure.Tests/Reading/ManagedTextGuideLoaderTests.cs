using DesktopGuides.Core.Library;
using DesktopGuides.Core.Text;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class ManagedTextGuideLoaderTests
{
    private static async Task<Guide> PublishLegacyAsync(PublisherHarness harness)
    {
        string source = harness.Sources.Copy("txt-legacy.txt", "legacy.txt");
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source, 437));
        return (await harness.Repository.GetGuideAsync(id))!;
    }

    private static string ManagedFile(PublisherHarness harness, Guide guide) =>
        harness.Paths.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);

    private static Task<TextGuideLoad> LoadAsync(
        PublisherHarness harness, Guide guide, CancellationToken token = default) =>
        new ManagedTextGuideLoader(harness.Paths).LoadAsync(guide, token);

    private static async Task<TextGuideLoadError> FailedAsync(PublisherHarness harness, Guide guide) =>
        Assert.IsType<TextGuideLoadFailed>(await LoadAsync(harness, guide)).Error;

    [Fact]
    public async Task LoadsAPublishedGuideWithoutTouchingAnyFile()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        string source = Path.Combine(harness.Sources.Root, "legacy.txt");
        IReadOnlyList<FileFingerprint> sourceBefore = FileFingerprint.Of(source);
        IReadOnlyList<FileFingerprint> managedBefore = FileFingerprint.Of(ManagedFile(harness, guide));

        TextGuideLoaded loaded = Assert.IsType<TextGuideLoaded>(await LoadAsync(harness, guide));

        Assert.Equal("Guide é\nItem list\n", loaded.Document.Text);
        Assert.Equal("ibm437", loaded.Document.EncodingName);
        Assert.False(loaded.ContentChanged);
        Assert.Equal(sourceBefore, FileFingerprint.Of(source));
        Assert.Equal(managedBefore, FileFingerprint.Of(ManagedFile(harness, guide)));
    }

    [Fact]
    public async Task MissingCopyIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        File.Delete(ManagedFile(harness, guide));

        Assert.Equal(TextGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task DeletedGuideFolderIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        Directory.Delete(harness.Paths.GetGuideRoot(guide.Id), recursive: true);

        Assert.Equal(TextGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task EscapingPathIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);

        Assert.Equal(TextGuideLoadError.Missing,
            await FailedAsync(harness, guide with { PrimaryRelativePath = "../escape.txt" }));
    }

    [Fact]
    public async Task CopyOverTheSizeLimitIsTooLarge()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        using (FileStream stream = new(ManagedFile(harness, guide), FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(64L * 1024 * 1024 + 1);
        }

        Assert.Equal(TextGuideLoadError.TooLarge, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task LockedCopyIsUnreadable()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        using FileStream locked = new(ManagedFile(harness, guide), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(TextGuideLoadError.Unreadable, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task NonTextGuideIsInvalidMetadataWithoutReading()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        File.Delete(ManagedFile(harness, guide));

        Assert.Equal(TextGuideLoadError.InvalidMetadata,
            await FailedAsync(harness, guide with { Format = GuideFormat.Pdf, TextCodePage = null }));
    }

    [Fact]
    public async Task CancelledLoadThrows()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LoadAsync(harness, guide, new CancellationToken(canceled: true)));
    }
}
