using DesktopGuides.Core.Html;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class ManagedHtmlAssetReaderTests
{
    private static async Task<(Guid Id, IReadOnlyList<GuideAsset> Assets)> PublishAsync(PublisherHarness harness)
    {
        foreach (string file in new[] { "images/map.png", "styles/main.css", "styles/palette.css" })
        {
            harness.Sources.Copy("html-static/" + file, file);
        }
        string entry = harness.Sources.Copy("html-static/guide.html", "guide.html");
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));
        return (id, await harness.Repository.GetGuideAssetsAsync(id));
    }

    private static string Managed(PublisherHarness harness, Guid id, GuideAsset asset) =>
        harness.Paths.ResolveExistingGuideFile(id, asset.RelativePath);

    [Fact]
    public async Task ServesEveryPublishedRowWithItsManagedBytes()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        ManagedHtmlAssetReader reader = new(harness.Paths, id);

        foreach (GuideAsset asset in assets)
        {
            HtmlAssetRead read = reader.Read(asset);
            Assert.Equal(HtmlAssetReadStatus.Served, read.Status);
            Assert.Equal(File.ReadAllBytes(Managed(harness, id, asset)), read.Bytes);
        }
    }

    [Fact]
    public async Task SameLengthEditIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        GuideAsset css = assets.First(asset => asset.Kind == GuideAssetKind.StyleSheet);
        byte[] bytes = File.ReadAllBytes(Managed(harness, id, css));
        bytes[0] ^= 0x20;
        File.WriteAllBytes(Managed(harness, id, css), bytes);

        Assert.Equal(HtmlAssetReadStatus.Changed, new ManagedHtmlAssetReader(harness.Paths, id).Read(css).Status);
    }

    [Fact]
    public async Task LengthChangeIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        GuideAsset css = assets.First(asset => asset.Kind == GuideAssetKind.StyleSheet);
        File.AppendAllText(Managed(harness, id, css), " ");

        Assert.Equal(HtmlAssetReadStatus.Changed, new ManagedHtmlAssetReader(harness.Paths, id).Read(css).Status);
    }

    [Fact]
    public async Task DeletedFileIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        GuideAsset image = assets.First(asset => asset.Kind == GuideAssetKind.Image);
        File.Delete(Managed(harness, id, image));

        Assert.Equal(HtmlAssetReadStatus.Missing, new ManagedHtmlAssetReader(harness.Paths, id).Read(image).Status);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("")]
    public async Task RowsThatEscapeTheGuideRootAreMissing(string relativePath)
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        GuideAsset escaping = assets[0] with { RelativePath = relativePath };

        Assert.Equal(HtmlAssetReadStatus.Missing, new ManagedHtmlAssetReader(harness.Paths, id).Read(escaping).Status);
    }

    [Fact]
    public async Task ReadingLeavesTheManagedCopyUntouched()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        IReadOnlyList<FileFingerprint> before = FileFingerprint.Of(harness.Paths.GetGuideRoot(id));

        foreach (GuideAsset asset in assets) new ManagedHtmlAssetReader(harness.Paths, id).Read(asset);

        Assert.Equal(before, FileFingerprint.Of(harness.Paths.GetGuideRoot(id)));
    }
}
