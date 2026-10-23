using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Online;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class CaptureStoreTests : IDisposable
{
    private const string SagaUrl = "https://gamefaqs.gamespot.com/ps5/100002-example-saga/faqs/78736/";
    private readonly string root = Path.Combine(Path.GetTempPath(), "dg-captures-" + Guid.NewGuid().ToString("N"));
    private readonly Clock clock = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static GuidePageReading Saga(string fixture, string slug) =>
        new GameFaqsAdapter().Read(new Uri(SagaUrl + slug), P3Fixtures.Read($"gamefaqs/html-slug-{fixture}.html"))!;

    [Fact]
    public void ADraftResumesAfterARestart()
    {
        Guid game = Guid.NewGuid();
        CaptureDraft draft = new CaptureStore(root, clock).Begin(game, Saga("cairn", "cairn"));
        string image = draft.SaveImage(new Uri("https://gamefaqs.gamespot.com/a/faqs/36/78736-54.png"), [1, 2, 3]);
        draft.Add(Saga("cairn", "cairn"),
            new Dictionary<Uri, string> { [new Uri("https://gamefaqs.gamespot.com/a/faqs/36/78736-54.png")] = image });

        CaptureDraft found = new CaptureStore(root, clock).Find(game, SourceSite.GameFaqs, "78736")!;

        Assert.Equal(draft.Id, found.Id);
        Assert.Equal(["introduction", "cairn", "fishing"], found.Pages.Select(page => page.PageKey));
        CapturedPage captured = Assert.Single(found.Captured);
        Assert.Equal("cairn", captured.Reading.Match.PageKey);
        Assert.Equal(Saga("cairn", "cairn").Content, captured.Reading.Content);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(captured.ImageFiles.Single().Value));
        Assert.Equal(["introduction", "fishing"], found.Remaining.Select(page => page.PageKey));
        Assert.False(found.IsComplete);
    }

    [Fact]
    public void CapturingEveryPageCompletesTheDraftInTheGuidesOrder()
    {
        CaptureDraft draft = new CaptureStore(root, clock).Begin(Guid.NewGuid(), Saga("fishing", "fishing"));
        draft.Add(Saga("fishing", "fishing"), new Dictionary<Uri, string>());
        draft.Add(Saga("intro", "introduction"), new Dictionary<Uri, string>());
        draft.Add(Saga("cairn", "cairn"), new Dictionary<Uri, string>());
        Assert.True(draft.IsComplete);
        Assert.Equal(["introduction", "cairn", "fishing"], draft.Captured.Select(page => page.Reading.Match.PageKey));
    }

    [Fact]
    public void ADraftForAnotherGameIsSeparate()
    {
        CaptureStore store = new(root, clock);
        store.Begin(Guid.NewGuid(), Saga("cairn", "cairn"));
        Assert.Null(store.Find(Guid.NewGuid(), SourceSite.GameFaqs, "78736"));
    }

    [Fact]
    public void SweepRemovesOnlyStaleDrafts()
    {
        CaptureStore store = new(root, clock);
        Guid oldGame = Guid.NewGuid();
        Guid newGame = Guid.NewGuid();
        store.Begin(oldGame, Saga("cairn", "cairn"));
        clock.Now = clock.Now.AddDays(7).AddMinutes(1);
        store.Begin(newGame, Saga("cairn", "cairn"));

        Assert.Equal(1, store.Sweep());

        Assert.Null(store.Find(oldGame, SourceSite.GameFaqs, "78736"));
        Assert.NotNull(store.Find(newGame, SourceSite.GameFaqs, "78736"));
    }

    [Fact]
    public void ACorruptDraftIsIgnoredAndSwept()
    {
        CaptureStore store = new(root, clock);
        Guid game = Guid.NewGuid();
        CaptureDraft draft = store.Begin(game, Saga("cairn", "cairn"));
        File.WriteAllText(Path.Combine(root, draft.Id.ToString("N"), "capture.json"), "{ broken");

        Assert.Null(store.Find(game, SourceSite.GameFaqs, "78736"));
        Assert.Equal(1, store.Sweep());
        Assert.Empty(Directory.EnumerateDirectories(root));
    }

    [Fact]
    public void ALongPageListIsCutAt200()
    {
        GuidePageReading many = Saga("cairn", "cairn") with
        {
            Pages = Enumerable.Range(0, 250).Select(n => new GuidePageLink($"p{n}", new Uri($"{SagaUrl}p{n}"), null)).ToArray(),
            Match = new GuidePageMatch(SourceSite.GameFaqs, "78736", "p0"),
        };
        CaptureDraft draft = new CaptureStore(root, clock).Begin(Guid.NewGuid(), many);
        foreach (GuidePageLink link in draft.Pages.ToArray())
        {
            draft.Add(many with { Match = many.Match with { PageKey = link.PageKey }, Url = link.Url }, new Dictionary<Uri, string>());
        }
        Assert.Equal(200, draft.Pages.Count);
        Assert.Empty(draft.Remaining);
        Assert.False(draft.IsComplete);
    }

    [Fact]
    public void DiscardRemovesTheDraft()
    {
        CaptureStore store = new(root, clock);
        Guid game = Guid.NewGuid();
        store.Begin(game, Saga("cairn", "cairn")).Discard();
        Assert.Null(store.Find(game, SourceSite.GameFaqs, "78736"));
        Assert.Empty(Directory.EnumerateDirectories(root));
    }

    [Fact]
    public void ListForGameReturnsItsDraftsNewestFirst()
    {
        CaptureStore store = new(root, clock);
        Guid game = Guid.NewGuid();
        CaptureDraft older = store.Begin(game, Saga("cairn", "cairn"));
        clock.Now = clock.Now.AddMinutes(1);
        GuidePageReading other = new NeoseekerAdapter().Read(
            new Uri("https://www.neoseeker.com/example-quest/walkthrough"), P3Fixtures.Read("neoseeker/wiki-hub.html"))!;
        CaptureDraft newer = store.Begin(game, other);
        store.Begin(Guid.NewGuid(), Saga("cairn", "cairn"));

        Assert.Equal([newer.Id, older.Id], store.ListForGame(game).Select(draft => draft.Id));
    }

    // Final review: startup sweeps drafts; a locked one must not stop the library opening.
    [Fact]
    public void SweepSkipsADraftItCantDelete()
    {
        if (!OperatingSystem.IsWindows()) return;
        CaptureStore store = new(root, clock);
        CaptureDraft locked = store.Begin(Guid.NewGuid(), Saga("cairn", "cairn"));
        CaptureDraft stale = store.Begin(Guid.NewGuid(), Saga("cairn", "cairn"));
        clock.Now = clock.Now.AddDays(8);
        using FileStream held = new(Path.Combine(root, locked.Id.ToString("N"), "capture.json"),
            FileMode.Open, FileAccess.Read, FileShare.None);

        int removed = store.Sweep();

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(Path.Combine(root, stale.Id.ToString("N"))));
    }

    private static GuidePageLink Linked(string key) => new(key, new Uri($"{SagaUrl}{key}"), null, Linked: true);

    [Fact]
    public void FoundArticlesFollowTheGuidesPagesUpTo200()
    {
        GuidePageReading many = Saga("cairn", "cairn") with
        {
            Pages = Enumerable.Range(0, 199).Select(n => new GuidePageLink($"p{n}", new Uri($"{SagaUrl}p{n}"), null)).ToArray(),
            Match = new GuidePageMatch(SourceSite.GameFaqs, "78736", "p0"),
        };
        CaptureDraft draft = new CaptureStore(root, clock).Begin(Guid.NewGuid(), many);

        draft.Extend([Linked("a1"), Linked("a2"), Linked("p3"), Linked("a1")]);
        draft.Extend([Linked("a2"), Linked("a3")]);

        Assert.Equal(200, draft.Pages.Count);
        Assert.Equal("a1", draft.Pages[^1].PageKey);
        Assert.True(draft.Pages[^1].Linked);
        Assert.Equal(202, draft.TotalPages);
        Assert.False(draft.IsComplete);
    }

    [Fact]
    public void ADroppedArticleIsNotFoundAgain()
    {
        CaptureDraft draft = new CaptureStore(root, clock).Begin(Guid.NewGuid(), Saga("cairn", "cairn"));
        draft.Extend([Linked("a1")]);
        draft.Drop("a1");
        draft.Extend([Linked("a1")]);

        Assert.Equal(["introduction", "cairn", "fishing"], draft.Pages.Select(page => page.PageKey));
        Assert.Equal(3, draft.TotalPages);
        CaptureDraft found = new CaptureStore(root, clock).Find(draft.GameId, SourceSite.GameFaqs, "78736")!;
        found.Extend([Linked("a1")]);
        Assert.Equal(3, found.Pages.Count);
    }

    [Fact]
    public void ADraftSavedBeforeArticlesStillLoads()
    {
        CaptureDraft draft = new CaptureStore(root, clock).Begin(Guid.NewGuid(), Saga("cairn", "cairn"));
        string file = Path.Combine(root, draft.Id.ToString("N"), "capture.json");
        string json = File.ReadAllText(file);
        json = System.Text.RegularExpressions.Regex.Replace(json, ",\\s*\"Linked\":\\s*false", "");
        json = System.Text.RegularExpressions.Regex.Replace(json, ",\\s*\"(Cut|Dropped)\":\\s*(null|\\[\\])", "");
        Assert.DoesNotContain("Linked", json);
        File.WriteAllText(file, json);

        CaptureDraft found = new CaptureStore(root, clock).Find(draft.GameId, SourceSite.GameFaqs, "78736")!;
        Assert.Equal(3, found.Pages.Count);
        Assert.All(found.Pages, page => Assert.False(page.Linked));
    }
}
