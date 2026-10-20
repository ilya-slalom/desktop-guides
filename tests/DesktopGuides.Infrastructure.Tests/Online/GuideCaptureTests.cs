using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Online;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class GuideCaptureTests : IDisposable
{
    private const string SagaUrl = "https://gamefaqs.gamespot.com/ps5/100002-example-saga/faqs/78736/";
    private static readonly Uri MapImage = new("https://gamefaqs.gamespot.com/a/faqs/36/78736-54.png");
    private readonly string root = Path.Combine(Path.GetTempPath(), "dg-capture-" + Guid.NewGuid().ToString("N"));
    private readonly Clock clock = new();

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;

        public Task Delay(TimeSpan wait, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Now += wait;
            return Task.CompletedTask;
        }
    }

    private sealed class Loader(Clock clock) : IPageLoader
    {
        public Dictionary<string, Queue<int>> Statuses { get; } = [];
        public List<(Uri Url, DateTimeOffset At)> Loads { get; } = [];
        public List<Uri> ImageLoads { get; } = [];
        public bool PageCarriesImage { get; set; } = true;
        public Action<Uri>? OnLoad { get; set; }

        public Task<PageLoad> LoadPageAsync(Uri url, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Loads.Add((url, clock.Now));
            OnLoad?.Invoke(url);
            token.ThrowIfCancellationRequested();
            clock.Now += TimeSpan.FromMilliseconds(500);
            string slug = url.Segments[^1];
            int status = Statuses.TryGetValue(slug, out Queue<int>? queue) && queue.Count > 0 ? queue.Dequeue() : 200;
            string fixture = slug == "introduction" ? "intro" : slug;
            string html = status == 299 ? "<html><body><p>Checking your browser…</p></body></html>"
                : P3Fixtures.Read($"gamefaqs/html-slug-{fixture}.html");
            Dictionary<Uri, byte[]> images = PageCarriesImage ? new() { [MapImage] = [1, 2, 3] } : [];
            return Task.FromResult(new PageLoad(status == 299 ? 200 : status, status is 200 or 299 ? html : null, images));
        }

        public Task<byte[]?> LoadImageAsync(Uri url, CancellationToken token)
        {
            ImageLoads.Add(url);
            Loads.Add((url, clock.Now));
            return Task.FromResult<byte[]?>([9]);
        }
    }

    private static GuidePageReading Saga(string fixture, string slug) =>
        new GameFaqsAdapter().Read(new Uri(SagaUrl + slug), P3Fixtures.Read($"gamefaqs/html-slug-{fixture}.html"))!;

    private CaptureDraft Draft() => new CaptureStore(root, clock).Begin(Guid.NewGuid(), Saga("cairn", "cairn"));

    private GuideCapture Capture(Loader loader) => new(new GameFaqsAdapter(), loader, clock, clock.Delay);

    [Fact]
    public async Task CapturesEveryPageAndItsImages()
    {
        Loader loader = new(clock);
        CaptureDraft draft = Draft();

        CaptureOutcome outcome = await Capture(loader).RunAsync(draft, null, CancellationToken.None);

        Assert.IsType<CaptureOutcome.Complete>(outcome);
        Assert.True(draft.IsComplete);
        Assert.Equal(["introduction", "cairn", "fishing"], draft.Captured.Select(page => page.Reading.Match.PageKey));
        Assert.All(draft.Captured, page => Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(page.ImageFiles[MapImage])));
        Assert.Empty(loader.ImageLoads);
    }

    // Review Focus 1.
    [Fact]
    public async Task ARefusedPagePausesAndResumesThere()
    {
        Loader loader = new(clock);
        loader.Statuses["cairn"] = new([429]);
        CaptureDraft draft = Draft();

        CaptureOutcome.Paused paused = Assert.IsType<CaptureOutcome.Paused>(
            await Capture(loader).RunAsync(draft, null, CancellationToken.None));

        Assert.Equal(("cairn", 2, 3, 429), (paused.Page.PageKey, paused.Number, paused.Total, paused.Status));
        Assert.Single(draft.Captured);
        Assert.IsType<CaptureOutcome.Complete>(await Capture(loader).RunAsync(draft, null, CancellationToken.None));
        Assert.True(draft.IsComplete);
    }

    [Fact]
    public async Task AChallengePageIsARefusal()
    {
        Loader loader = new(clock);
        loader.Statuses["introduction"] = new([299]);
        CaptureOutcome.Paused paused = Assert.IsType<CaptureOutcome.Paused>(
            await Capture(loader).RunAsync(Draft(), null, CancellationToken.None));
        Assert.Equal((1, 200), (paused.Number, paused.Status));
    }

    [Fact]
    public async Task PagesArePacedTwoSecondsApart()
    {
        Loader loader = new(clock);
        await Capture(loader).RunAsync(Draft(), null, CancellationToken.None);
        DateTimeOffset[] starts = loader.Loads.Select(load => load.At).ToArray();
        Assert.Equal(3, starts.Length);
        for (int i = 1; i < starts.Length; i++)
        {
            Assert.True(starts[i] - starts[i - 1] >= TimeSpan.FromSeconds(2), $"{starts[i] - starts[i - 1]}");
        }
    }

    [Fact]
    public async Task AMissingImageIsLoadedDirectlyOnce()
    {
        Loader loader = new(clock) { PageCarriesImage = false };
        CaptureDraft draft = Draft();
        await Capture(loader).RunAsync(draft, null, CancellationToken.None);
        Assert.Equal(3, loader.ImageLoads.Count);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(draft.Captured[0].ImageFiles[MapImage]));
        DateTimeOffset[] starts = loader.Loads.Select(load => load.At).ToArray();
        for (int i = 1; i < starts.Length; i++) Assert.True(starts[i] - starts[i - 1] >= TimeSpan.FromSeconds(2));
    }

    // Review Focus 2.
    [Fact]
    public async Task CancellingKeepsWhatWasCaptured()
    {
        using CancellationTokenSource cancel = new();
        Loader loader = new(clock) { OnLoad = url => { if (url.AbsoluteUri.EndsWith("fishing", StringComparison.Ordinal)) cancel.Cancel(); } };
        CaptureDraft draft = Draft();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Capture(loader).RunAsync(draft, null, cancel.Token));

        Assert.Equal(["introduction", "cairn"], draft.Captured.Select(page => page.Reading.Match.PageKey));
    }

    [Fact]
    public async Task ProgressCountsCapturedPages()
    {
        List<(int, int)> reports = [];
        await Capture(new Loader(clock)).RunAsync(Draft(), new SyncProgress(reports), CancellationToken.None);
        Assert.Equal([(1, 3), (2, 3), (3, 3)], reports);
    }

    private sealed class SyncProgress(List<(int, int)> reports) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => reports.Add(value);
    }
}
