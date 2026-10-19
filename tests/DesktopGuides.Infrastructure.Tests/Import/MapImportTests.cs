using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Maps;
using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class MapImportTests : IDisposable
{
    private readonly ImportTestDirectory files = new();
    private FakeMapDecoder decoder = new(decodes: true);

    public void Dispose() => files.Dispose();

    private GuideImportValidator Validator() => new(null, decoder);

    private string Write(string name, byte[] bytes) => files.Write(name, bytes);

    private static MapImportManifest Map(ImportInspection inspection) =>
        Assert.IsType<MapImportManifest>(Assert.IsType<ImportReady>(inspection).Manifest);

    [Fact]
    public async Task APngIsAMap()
    {
        string path = Write("castle-floor-1.png", MapBytes.Png(640, 480, padding: 100));
        MapImportManifest map = Map(await Validator().InspectAsync(path, default));
        Assert.Equal(("castle floor 1", MapImageKind.Png, 640, 480), (map.SuggestedTitle, map.Kind, map.PixelWidth, map.PixelHeight));
        Assert.Equal(GuideFingerprint.OfBytes(File.ReadAllBytes(path)), map.Fingerprint);
        Assert.Equal(new MapImageSize(MapImageKind.Png, 640, 480), decoder.Calls.Single());
    }

    [Fact]
    public async Task AJpegNamedPngIsAJpeg() =>
        Assert.Equal(MapImageKind.Jpeg,
            Map(await Validator().InspectAsync(Write("chart.png", MapBytes.Jpeg(0xC0, 320, 200)), default)).Kind);

    // Review Focus 3.
    [Theory]
    [InlineData("MAP.PNG")]
    [InlineData("Chart.JPG")]
    [InlineData("chart.jpeg")]
    public async Task UpperCaseExtensionsImportAsMaps(string name) =>
        Map(await Validator().InspectAsync(
            Write(name, name.EndsWith("PNG", StringComparison.Ordinal) ? MapBytes.Png(8, 8) : MapBytes.Jpeg(0xC0, 8, 8)), default));

    [Theory]
    [InlineData(40000, 40000, "huge.png is 40,000 × 40,000 pixels. A map can be at most 32,768 pixels on a side.")]
    [InlineData(20000, 20000, "huge.png is 20,000 × 20,000 pixels. A map can have at most 268 million pixels.")]
    public async Task ADecompressionBombIsRefusedFromItsHeader(int w, int h, string message)
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => Validator().InspectAsync(Write("huge.png", MapBytes.Png(w, h)), default));
        Assert.Equal((ImportIssue.TooLarge, message), (error.Issue, error.Message));
        Assert.Empty(decoder.Calls);
    }

    [Fact]
    public async Task AFileOverTheByteLimitIsRefused()
    {
        GuideImportValidator small = new(new GuideImportLimits(MaxMapBytes: 64), decoder);
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => small.InspectAsync(Write("big.png", MapBytes.Png(8, 8, padding: 100)), default));
        Assert.Equal(ImportIssue.TooLarge, error.Issue);
        Assert.StartsWith("big.png is larger than the ", error.Message, StringComparison.Ordinal);
        Assert.EndsWith(" limit for maps.", error.Message, StringComparison.Ordinal);
    }

    // WIC decodes a truncated PNG, filling the missing rows (39177728062).
    [Fact]
    public async Task ATruncatedMapIsRefusedEvenIfItDecodes()
    {
        byte[] whole = MapBytes.WholePng(64, 64);
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => Validator().InspectAsync(Write("cut.png", whole[..(whole.Length / 2)]), default));
        Assert.Equal((ImportIssue.Unreadable, "cut.png can't be read as an image. It may be damaged or incomplete."),
            (error.Issue, error.Message));
        Assert.Empty(decoder.Calls);
    }

    [Fact]
    public async Task AnImageThatDoesntDecodeIsRefused()
    {
        decoder = new FakeMapDecoder(decodes: false);
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => Validator().InspectAsync(Write("broken.png", MapBytes.WholePng(64, 64)), default));
        Assert.Equal((ImportIssue.Unreadable, "broken.png can't be read as an image. It may be damaged or incomplete."),
            (error.Issue, error.Message));
    }

    [Theory]
    [InlineData("chart.png", "chart.png isn't a PNG or JPEG image.")]
    [InlineData("chart.gif", "chart.gif isn't a TXT, HTML, PDF, PNG or JPEG file.")]
    public async Task OtherImagesAreUnsupported(string name, string message)
    {
        byte[] gif = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0];
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => Validator().InspectAsync(Write(name, gif), default));
        Assert.Equal((ImportIssue.Unsupported, message), (error.Issue, error.Message));
    }

    [Fact]
    public async Task AMapWithoutADecoderIsAWiringFault() =>
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new GuideImportValidator().InspectAsync(Write("a.png", MapBytes.Png(8, 8)), default));

    [Fact]
    public async Task AMapPublishesAsItsOwnFile()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Write("castle-floor-1.png", MapBytes.Png(640, 480, padding: 50));

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source));

        Guide guide = (await harness.Repository.GetGuideAsync(id))!;
        Assert.Equal((GuideFormat.Image, 640, 480, "map.png", "castle-floor-1.png"),
            (guide.Format, guide.PixelWidth, guide.PixelHeight, guide.PrimaryRelativePath, guide.SourceLabel));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, "map.png")));
    }

    [Fact]
    public async Task AJpegPublishesAsMapJpg()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Write("chart.png", MapBytes.Jpeg(0xC0, 320, 200, padding: 20));

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source));

        Assert.Equal("map.jpg", (await harness.Repository.GetGuideAsync(id))!.PrimaryRelativePath);
    }

    // Review Focus 4.
    [Fact]
    public async Task TheSameTitleIsNotADuplicate()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string first = harness.Sources.Write("a/castle.png", MapBytes.Png(640, 480, padding: 1));
        string second = harness.Sources.Write("b/castle.png", MapBytes.Png(640, 480, padding: 2));
        string again = harness.Sources.Write("c/castle.png", File.ReadAllBytes(first));
        GuideImportPublisher publisher = new(harness.Repository, harness.Paths);

        await publisher.PublishAsync(await harness.InspectAsync(first), harness.Game.Id, "castle", false, null, default);
        await publisher.PublishAsync(await harness.InspectAsync(second), harness.Game.Id, "castle", false, null, default);
        ImportManifest repeat = await harness.InspectAsync(again);
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() =>
            publisher.PublishAsync(repeat, harness.Game.Id, "castle", false, null, default));

        Assert.Equal(ImportIssue.Duplicate, error.Issue);
        Assert.Equal(2, (await harness.Repository.ListGuidesAsync(harness.Game.Id)).Count);
    }

    [Fact]
    public async Task AMapChangedAfterInspectionIsRefused()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Write("castle.png", MapBytes.Png(640, 480, padding: 20));
        ImportManifest manifest = await harness.InspectAsync(source);
        DateTime written = File.GetLastWriteTimeUtc(source);
        byte[] jpeg = MapBytes.Jpeg(0xC0, 640, 480, padding: (int)new FileInfo(source).Length - 27);
        Assert.Equal(new FileInfo(source).Length, jpeg.Length);
        File.WriteAllBytes(source, jpeg);
        File.SetLastWriteTimeUtc(source, written);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        harness.AssertNothingLeft();
    }
}
