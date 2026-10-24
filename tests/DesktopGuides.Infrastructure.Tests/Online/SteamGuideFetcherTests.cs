using System.Net;
using System.Text;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Online;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class SteamGuideFetcherTests : IAsyncLifetime
{
    private const string Page = "steamcommunity.com/sharedfiles/filedetails/";
    private const string Details = "api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
    private readonly SteamStub stub = new();
    private readonly Clock clock = new();
    private PublisherHarness harness = null!;
    private CaptureStore store = null!;

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

    public async Task InitializeAsync()
    {
        harness = await PublisherHarness.CreateAsync();
        store = new CaptureStore(Path.Combine(harness.Sources.Root, "captures"), clock);
        stub.Clock = () => clock.Now;
        stub.Json(Details, "details.json");
        stub.Routes[Page] = _ => (HttpStatusCode.OK, Encoding.UTF8.GetBytes(P3Fixtures.Read("steam/guide.html")));
        foreach (string image in new[] { "images.steamusercontent.com/ugc/2/FULLA/", "images.steamusercontent.com/ugc/3/FULLB/" })
        {
            stub.Routes[image] = _ => (HttpStatusCode.OK, MapBytes.WholePng(4, 4));
        }
    }

    public async Task DisposeAsync() => await harness.DisposeAsync();

    private SteamGuideFetcher Fetcher()
    {
        ProviderHttp http = new(stub);
        return new SteamGuideFetcher(new SteamClient(http, () => null), http, store, clock, clock.Delay);
    }

    private OnlineGuideSaver Saver() =>
        new(harness.Repository, harness.Publisher(), harness.Validator, Path.Combine(harness.Sources.Root, "work"), clock);

    [Fact]
    public async Task TheGuideAndItsImagesLoadOneAtATime()
    {
        CaptureDraft draft = await Fetcher().FetchAsync(harness.Game.Id, "4242", null, CancellationToken.None);

        Assert.Equal(
            [Page, Details, "images.steamusercontent.com/ugc/2/FULLA/", "images.steamusercontent.com/ugc/3/FULLB/"],
            stub.Requests.Select(r => r.Uri.Host + r.Uri.AbsolutePath));
        Assert.All(stub.Requests.Zip(stub.Requests.Skip(1)), pair => Assert.True(pair.Second.At - pair.First.At >= TimeSpan.FromSeconds(1)));
        CapturedPage page = Assert.Single(draft.Captured);
        Assert.Equal(2, page.ImageFiles.Count);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1720543880), page.Reading.SourceUpdated);
        Assert.True(draft.IsComplete);
    }

    // Review Focus 1.
    [Fact]
    public async Task ALimitPartWayLeavesNoDraftAndNoGuide()
    {
        stub.Status("images.steamusercontent.com/ugc/3/FULLB/", HttpStatusCode.TooManyRequests);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(
            () => Fetcher().FetchAsync(harness.Game.Id, "4242", null, CancellationToken.None));
        Assert.Equal(ProviderErrorKind.RateLimited, error.Kind);
        Assert.Empty(store.ListForGame(harness.Game.Id));
        Assert.Empty(await harness.Repository.ListGuidesAsync(harness.Game.Id));
    }

    [Fact]
    public async Task AnImageThatFailsIsLeftForThePlaceholder()
    {
        stub.Status("images.steamusercontent.com/ugc/3/FULLB/", HttpStatusCode.NotFound);
        CaptureDraft draft = await Fetcher().FetchAsync(harness.Game.Id, "4242", null, CancellationToken.None);
        Assert.Single(Assert.Single(draft.Captured).ImageFiles);
    }

    [Fact]
    public async Task APageTheAdapterCannotReadSavesNothing()
    {
        stub.Routes[Page] = _ => (HttpStatusCode.OK, Encoding.UTF8.GetBytes(P3Fixtures.Read("steam/guide-no-sections.html")));
        Assert.Equal(ProviderErrorKind.MalformedData, (await Assert.ThrowsAsync<ProviderException>(
            () => Fetcher().FetchAsync(harness.Game.Id, "4242", null, CancellationToken.None))).Kind);
        Assert.Empty(store.ListForGame(harness.Game.Id));
    }

    [Fact]
    public async Task APageOverFourMegabytesIsRefused()
    {
        stub.Routes[Page] = _ => (HttpStatusCode.OK, new byte[4 * 1024 * 1024 + 1]);
        Assert.Equal(ProviderErrorKind.MalformedData, (await Assert.ThrowsAsync<ProviderException>(
            () => Fetcher().FetchAsync(harness.Game.Id, "4242", null, CancellationToken.None))).Kind);
    }

    // Review Focus 4.
    [Fact]
    public async Task ASavedGuideIsCreditedAndSavingAgainReplacesIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid id = Assert.IsType<OnlineSaveResult.Added>(await Saver().SaveAsync(
            await Fetcher().FetchAsync(harness.Game.Id, "4242", null, CancellationToken.None), false, null, CancellationToken.None)).GuideId;

        GuideSource source = (await harness.Repository.GetGuideSourceAsync(id))!;
        Assert.Equal((SourceSite.Steam, "4242", "https://steamcommunity.com/sharedfiles/filedetails/?id=4242", "Test Author"),
            (source.Site, source.ItemId, source.Url, source.Author));
        Assert.Contains(await harness.Repository.GetGuideAssetsAsync(id), asset => asset.Kind == GuideAssetKind.StyleSheet);

        Assert.Equal(new OnlineSaveResult.UpToDate(id), await Saver().SaveAsync(
            await Fetcher().FetchAsync(harness.Game.Id, "4242", null, CancellationToken.None), false, null, CancellationToken.None));
        stub.Routes[Page] = _ => (HttpStatusCode.OK, Encoding.UTF8.GetBytes(
            P3Fixtures.Read("steam/guide.html").Replace("Bring a torch.", "Bring two torches.", StringComparison.Ordinal)));
        Assert.Equal(new OnlineSaveResult.Replaced(id), await Saver().SaveAsync(
            await Fetcher().FetchAsync(harness.Game.Id, "4242", null, CancellationToken.None), false, null, CancellationToken.None));
    }
}
