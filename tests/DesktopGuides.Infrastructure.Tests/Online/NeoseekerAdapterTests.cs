using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Online;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class NeoseekerAdapterTests
{
    private const string Host = "https://www.neoseeker.com";
    private const string Walkthrough = "example-quest/walkthrough";
    private readonly NeoseekerAdapter adapter = new();

    private GuidePageReading Read(string fixture, string path) =>
        adapter.Read(new Uri(Host + path), P3Fixtures.Read("neoseeker/" + fixture))
        ?? throw new InvalidOperationException("Not read as a guide.");

    [Theory]
    [InlineData("/chrono-trigger/faqs/260078-script.html", "260078", "")]
    [InlineData("/example-quest/walkthrough/A_Golden_Key", Walkthrough, "a-golden-key")]
    [InlineData("/example-quest/walkthrough/Episode_3", Walkthrough, "episode-3")]
    [InlineData("/example-quest/walkthrough", Walkthrough, "index")]
    public void MatchesFaqsAndWalkthroughPages(string path, string item, string page) =>
        Assert.Equal(new GuidePageMatch(SourceSite.Neoseeker, item, page), adapter.Match(new Uri(Host + path)));

    [Theory]
    [InlineData("https://www.neoseeker.com/example-quest/walkthrough/A_Golden_Key?edit")]
    [InlineData("https://www.neoseeker.com/example-quest/characters/Hero")]
    [InlineData("http://www.neoseeker.com/example-quest/walkthrough")]
    [InlineData("https://example.com/example-quest/walkthrough")]
    public void MatchesNothingElse(string url) => Assert.Null(adapter.Match(new Uri(url)));

    [Fact]
    public void ATextFaqUsesItsCanonicalIdAndPlainText()
    {
        GuidePageReading page = Read("text-faq.html", "/example-quest/faqs/260078-script.html");
        Assert.Equal(new GuidePageMatch(SourceSite.Neoseeker, "74586", ""), page.Match);
        Assert.Equal(CapturedFormat.Text, page.Format);
        Assert.Contains("The hero meets the Example Quest elder.", page.Content);
        Assert.DoesNotContain("<a", page.Content);
        Assert.DoesNotContain("another platform", page.Content);
        Assert.Equal("Example Quest Game Script", page.Title);
        Assert.Equal("Script Scribe", page.Author);
        Assert.Equal(DateTimeOffset.Parse("2004-04-12T00:00:00Z"), page.SourceUpdated);
        Assert.StartsWith("Copyright 2004 Script Scribe.", page.Notice);
        Assert.Empty(page.Pages);
    }

    // Review Focus 4.
    [Fact]
    public void OnlyTheWalkthroughsOwnPagesAreListed()
    {
        GuidePageReading page = Read("wiki-a-golden-key.html", "/example-quest/walkthrough/A_Golden_Key");
        Assert.Equal(["index", "episode-3", "a-golden-key"], page.Pages.Select(link => link.PageKey));
        Assert.Equal(new Uri(Host + "/example-quest/walkthrough"), page.Pages[0].Url);
        Assert.Equal("Chapter 12: A Golden Key", page.Pages[2].Title);
        Assert.Equal(CapturedFormat.Html, page.Format);
    }

    [Fact]
    public void AWalkthroughPageDropsAdsCommentsAndNoPrint()
    {
        GuidePageReading page = Read("wiki-a-golden-key.html", "/example-quest/walkthrough/A_Golden_Key");
        Assert.Contains("Walk to the gate on the A Golden Key page.", page.Content);
        Assert.DoesNotContain("section-ad", page.Content);
        Assert.DoesNotContain("noprint", page.Content);
        Assert.DoesNotContain("<!--", page.Content);
    }

    // Review Focus 2.
    [Fact]
    public void LazyImagesUseTheirDataSource()
    {
        GuidePageReading page = Read("wiki-a-golden-key.html", "/example-quest/walkthrough/A_Golden_Key");
        Assert.Equal(
            [new Uri("https://cdn.staticneo.com/ew/thumb/a.png"), new Uri("https://cdn.staticneo.com/ew/thumb/b.png")],
            page.Images);
    }

    [Fact]
    public void AWalkthroughReadsItsTitleDateAndNotice()
    {
        GuidePageReading page = Read("wiki-hub.html", "/example-quest/walkthrough");
        Assert.Equal("Example Quest Walkthrough", page.Title);
        Assert.Equal("Example Quest Walkthrough",
            Read("wiki-episode-3.html", "/example-quest/walkthrough/Episode_3").Title);
        Assert.Equal(DateTimeOffset.Parse("2024-03-19T14:52:48Z"), page.SourceUpdated);
        Assert.Null(page.Author);
        Assert.Equal("© Neo Era Media Inc.", page.Notice);
        Assert.Equal("index", page.Match.PageKey);
    }

    [Fact]
    public void AChangedLayoutIsNotAGuide() =>
        Assert.Null(adapter.Read(new Uri(Host + "/example-quest/walkthrough"), "<html><body><p>New</p></body></html>"));

    [Fact]
    public void AnImageLinkedToItsFilePageStaysAnImage()
    {
        GuidePageReading page = adapter.Read(new Uri(Host + "/example-quest/walkthrough/Episode_3"), """
            <html><body><article id="wiki-content"><div class="mw-parser-output">
            <p><a class="image" href="/example-quest/File:Map.png"><img src="https://cdn.staticneo.com/ew/map.png"></a>
            <a href="/example-quest/File%3AKey.png">Key</a> <a href="/example-quest/walkthrough/Episode_4">Next</a></p>
            </div></article></body></html>
            """)!;

        Assert.Contains("<img src=\"https://cdn.staticneo.com/ew/map.png\">", page.Content);
        Assert.DoesNotContain("File:", page.Content);
        Assert.DoesNotContain("File%3A", page.Content);
        Assert.Contains("Key <a href", page.Content);
        Assert.Contains("href=\"https://www.neoseeker.com/example-quest/walkthrough/Episode_4\"", page.Content);
    }

    private static readonly GuidePageMatch WalkthroughItem = new(SourceSite.Neoseeker, "example-quest/walkthrough", "");

    private static GuidePageLink Article(string path, string key) =>
        new(key, new Uri(Host + "/example-quest/" + path), null, Linked: true);

    [Fact]
    public void AWalkthroughPageListsTheSameGamesArticlesItLinksTo()
    {
        GuidePageReading page = Read("wiki-episode-3.html", "/example-quest/walkthrough/Episode_3");

        Assert.Equal(
            [("article-quests-stuck-in-a-rut", Host + "/example-quest/Quests/Stuck_in_a_Rut"),
             ("article-world-intel-gongaga", Host + "/example-quest/World_Intel/Gongaga")],
            page.Linked.Select(link => (link.PageKey, link.Url.AbsoluteUri)));
        Assert.All(page.Linked, link => Assert.True(link.Linked));
    }

    [Fact]
    public void ATextFaqListsNoArticles() =>
        Assert.Empty(Read("text-faq.html", "/example-quest/faqs/260078-script.html").Linked);

    [Fact]
    public void AnArticleIsReadAsAPageOfTheWalkthrough()
    {
        GuidePageLink link = Article("Quests/Stuck_in_a_Rut", "article-quests-stuck-in-a-rut");
        GuidePageReading page = adapter.ReadLinked(link, P3Fixtures.Read("neoseeker/wiki-article-stuck-in-a-rut.html"), WalkthroughItem)!;

        Assert.Equal(WalkthroughItem with { PageKey = "article-quests-stuck-in-a-rut" }, page.Match);
        Assert.Equal("Stuck in a Rut", page.Title);
        Assert.Equal(CapturedFormat.Html, page.Format);
        Assert.Contains("Help the farmer", page.Content);
        Assert.DoesNotContain("Advertisement", page.Content);
        Assert.DoesNotContain("File:", page.Content);
        Assert.Equal([new Uri("https://cdn.staticneo.com/ew/thumb/rut.png")], page.Images);
        Assert.Empty(page.Linked);
        Assert.Empty(page.Pages);
    }

    [Fact]
    public void ASitePageWithoutWikiContentIsNoArticle()
    {
        string html = P3Fixtures.Read("neoseeker/wiki-no-article.html");
        Assert.Null(adapter.ReadLinked(Article("faqs", "article-faqs"), html, WalkthroughItem));
        Assert.True(adapter.IsSitePage(html));
    }

    [Fact]
    public void AChallengeIsNotASitePage() =>
        Assert.False(adapter.IsSitePage("<html><body><p>Checking your browser…</p></body></html>"));
}
