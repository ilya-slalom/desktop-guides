using System.Net;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Online;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class VgMapsSaverTests : IAsyncLifetime
{
    private static readonly Uri Page = new("https://vgmaps.com/Atlas/PSX/index.htm");
    private readonly SteamStub stub = new();
    private readonly Clock clock = new();
    private PublisherHarness harness = null!;
    private int conversions;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public Task Delay(TimeSpan wait, CancellationToken token)
        {
            Now += wait;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConverter(Action onConvert) : IMapImageConverter
    {
        public Task<byte[]> ToPngAsync(byte[] gif, CancellationToken token)
        {
            onConvert();
            return Task.FromResult(MapBytes.WholePng(32, 24));
        }
    }

    public async Task InitializeAsync()
    {
        harness = await PublisherHarness.CreateAsync();
        stub.Clock = () => clock.Now;
        stub.Routes["vgmaps.com/Atlas/PSX/OnlineTestGame-World.png"] = _ => (HttpStatusCode.OK, MapBytes.WholePng(34, 14));
        stub.Routes["vgmaps.com/Atlas/PSX/OnlineTestGame-Town.png"] = _ => (HttpStatusCode.OK, MapBytes.WholePng(8, 6));
        stub.Routes["vgmaps.com/Atlas/PSX/OnlineTestGame-Cave.gif"] = _ => (HttpStatusCode.OK, P3Bytes.Read("vgmaps/map.gif"));
    }

    public async Task DisposeAsync() => await harness.DisposeAsync();

    private VgMapsGamePage GamePage() => VgMapsSystemPage.Read(VgMapsClient.Decode(P3Bytes.Read("vgmaps/system.htm")), Page,
        new VgMapsGame("Online Test Game", "Online Test Game (PSX)", "PSX/index.htm", "OnlineTestGame"))!;

    private VgMapsSaver Saver() => new(
        new VgMapsClient(new ProviderHttp(stub), clock.Delay), harness.Repository, harness.Publisher(), harness.Validator,
        new FakeConverter(() => conversions++), Path.Combine(harness.Sources.Root, "vgmaps-work"), clock);

    private Task<VgMapsSaveResult> SaveAsync(params string[] areas)
    {
        VgMapsGamePage page = GamePage();
        return Saver().SaveAsync(harness.Game.Id, page, page.Maps.Where(map => areas.Contains(map.Area)).ToArray(), null, CancellationToken.None);
    }

    [Fact]
    public async Task ChosenMapsAreSavedOneAtATimeWithTheirSource()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(new VgMapsSaveResult(2, 0, 0), await SaveAsync("World", "Town"));

        Assert.True(stub.Requests[1].At - stub.Requests[0].At >= TimeSpan.FromSeconds(1));
        IReadOnlyList<Guide> maps = (await harness.Repository.ListGuidesAsync(harness.Game.Id)).Where(guide => guide.Format == GuideFormat.Image).ToList();
        Assert.Equal(["Town", "World"], maps.Select(map => map.Title).Order());
        Guide world = maps.Single(map => map.Title == "World");
        GuideSource source = (await harness.Repository.GetGuideSourceAsync(world.Id))!;
        Assert.Equal(
            (SourceSite.VgMaps, "PSX/OnlineTestGame-World.png", "https://vgmaps.com/Atlas/PSX/index.htm#OnlineTestGame",
             "Test Ripper", "© 2026 Test Studio", "Online Test Game: World"),
            (source.Site, source.ItemId, source.Url, source.Author, source.Notice, source.SourceTitle));
    }

    // Review Focus 3.
    [Fact]
    public async Task AGifIsSavedAsPng()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(new VgMapsSaveResult(1, 0, 0), await SaveAsync("Cave"));
        Assert.Equal(1, conversions);
        Guide cave = (await harness.Repository.ListGuidesAsync(harness.Game.Id)).Single(guide => guide.Title == "Cave");
        Assert.Equal((32, 24), (cave.PixelWidth, cave.PixelHeight));
    }

    // Review Focus 4.
    [Fact]
    public async Task SavingAgainKeepsUnchangedMapsAndReplacesAChangedOne()
    {
        if (!OperatingSystem.IsWindows()) return;
        await SaveAsync("World", "Town");
        Assert.Equal(new VgMapsSaveResult(0, 0, 2), await SaveAsync("World", "Town"));
        stub.Routes["vgmaps.com/Atlas/PSX/OnlineTestGame-World.png"] = _ => (HttpStatusCode.OK, MapBytes.WholePng(40, 20));
        Assert.Equal(new VgMapsSaveResult(0, 1, 1), await SaveAsync("World", "Town"));
        Assert.Equal(2, (await harness.Repository.ListGuidesAsync(harness.Game.Id)).Count(guide => guide.Format == GuideFormat.Image));
    }

    [Fact]
    public async Task ALimitPartWayKeepsTheMapsBeforeItAndNamesTheMap()
    {
        if (!OperatingSystem.IsWindows()) return;
        stub.Status("vgmaps.com/Atlas/PSX/OnlineTestGame-Town.png", HttpStatusCode.TooManyRequests);
        VgMapsSaveException error = await Assert.ThrowsAsync<VgMapsSaveException>(() => SaveAsync("World", "Town", "Cave"));
        Assert.Equal(("map 2 of 3", ProviderErrorKind.RateLimited), (error.Step, error.Kind));
        Assert.Equal(1, error.Saved);
        Assert.Equal(["World"], (await harness.Repository.ListGuidesAsync(harness.Game.Id)).Select(guide => guide.Title));
        Assert.DoesNotContain(stub.Requests, request => request.Uri.AbsolutePath.EndsWith(".gif", StringComparison.Ordinal));
    }

    // Review Focus 2.
    [Fact]
    public async Task AnUnsaveableMapIsRefusedBeforeAnyRequest()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync("Intro"));
        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync("Huge World"));
        Assert.Empty(stub.Requests);
    }
}
