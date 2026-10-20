using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Online;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class GameFaqsAdapterTests
{
    private const string Host = "https://gamefaqs.gamespot.com";
    private readonly GameFaqsAdapter adapter = new();

    private GuidePageReading Read(string fixture, string url) =>
        adapter.Read(new Uri(url), P3Fixtures.Read("gamefaqs/" + fixture))
        ?? throw new InvalidOperationException("Not read as a guide.");

    [Theory]
    [InlineData("/ps/197341-final-fantasy-vii/faqs/13639", "13639", "")]
    [InlineData("/ps5/289140-assassins-creed-valhalla/faqs/78736/cairn", "78736", "cairn")]
    [InlineData("/switch/189707-zelda/faqs/74635?page=3", "74635", "page-3")]
    [InlineData("/switch/189707-zelda/faqs/74635?page=0", "74635", "page-0")]
    [InlineData("/switch/189707-zelda/faqs/74635", "74635", "")]
    public void MatchesGuideUrls(string path, string item, string page) =>
        Assert.Equal(new GuidePageMatch(SourceSite.GameFaqs, item, page), adapter.Match(new Uri(Host + path)));

    [Theory]
    [InlineData("http://gamefaqs.gamespot.com/ps/197341-final-fantasy-vii/faqs/13639")]
    [InlineData("https://gamefaqs.gamespot.com/boards/197341-final-fantasy-vii")]
    [InlineData("https://gamefaqs.gamespot.com/ps/197341-final-fantasy-vii/faqs")]
    [InlineData("https://example.com/ps/197341-final-fantasy-vii/faqs/13639")]
    public void MatchesNothingElse(string url) => Assert.Null(adapter.Match(new Uri(url)));

    // Review Focus 1.
    [Fact]
    public void ATextFaqJoinsItsChunksExactly()
    {
        GuidePageReading page = Read("text-faq.html", Host + "/ps/100001-example-quest/faqs/13639");
        Assert.Equal(CapturedFormat.Text, page.Format);
        Assert.Contains("The sword is in the\nchapel. Take it", page.Content);
        Assert.Contains("open the crypt.\n\n\n3. COPYRIGHT", page.Content);
        Assert.Contains("guard \"Old Tom\"", page.Content);
        Assert.StartsWith("EXAMPLE QUEST WALKTHROUGH\nVersion 1.2", page.Content);
        Assert.Empty(page.Pages);
        Assert.Equal(new GuidePageMatch(SourceSite.GameFaqs, "13639", ""), page.Match);
    }

    [Fact]
    public void ATextFaqReadsItsMetadata()
    {
        GuidePageReading page = Read("text-faq.html", Host + "/ps/100001-example-quest/faqs/13639");
        Assert.Equal("Example Quest Walkthrough", page.Title);
        Assert.Equal("Example Author", page.Author);
        Assert.Equal(DateTimeOffset.Parse("2003-07-29T00:00:00-07:00"), page.SourceUpdated);
        Assert.StartsWith("3. COPYRIGHT\n(c) Copyright 2003 Example Author", page.Notice);
    }

    [Fact]
    public void AnHtmlGuideListsItsPagesInOrder()
    {
        GuidePageReading page = Read("html-slug-cairn.html", Host + "/ps5/100002-example-saga/faqs/78736/cairn");
        Assert.Equal(CapturedFormat.Html, page.Format);
        Assert.Equal(["introduction", "cairn", "fishing"], page.Pages.Select(link => link.PageKey));
        Assert.Equal(new Uri(Host + "/ps5/100002-example-saga/faqs/78736/fishing"), page.Pages[2].Url);
        Assert.Equal("Fishing", page.Pages[2].Title);
        Assert.Equal("cairn", page.Match.PageKey);
        Assert.Equal("Example's Saga Walkthrough", page.Title);
        Assert.Equal("Guide Writer", page.Author);
    }

    [Fact]
    public void AnHtmlPageExcludesItsTableOfContents()
    {
        GuidePageReading page = Read("html-slug-cairn.html", Host + "/ps5/100002-example-saga/faqs/78736/cairn");
        Assert.Contains("<h2", page.Content);
        Assert.Contains("100 silver", page.Content);
        Assert.DoesNotContain("ftoc", page.Content);
        Assert.Equal([new Uri(Host + "/a/faqs/36/78736-54.png")], page.Images);
    }

    // Review Focus 3.
    [Fact]
    public void TheFirstPagedPageHasOneKey()
    {
        GuidePageReading first = Read("html-paged-0.html", Host + "/switch/100003-example-wilds/faqs/74635");
        GuidePageReading second = Read("html-paged-1.html", Host + "/switch/100003-example-wilds/faqs/74635?page=1");
        Assert.Equal(["page-0", "page-1", "page-2"], second.Pages.Select(link => link.PageKey));
        Assert.Equal("page-1", second.Match.PageKey);
        Assert.Equal("page-0", first.Match.PageKey);
        Assert.Equal(new Uri(Host + "/switch/100003-example-wilds/faqs/74635?page=2"), second.Pages[2].Url);
    }

    [Fact]
    public void AChangedLayoutIsNotAGuide() =>
        Assert.Null(adapter.Read(new Uri(Host + "/ps/100001-example-quest/faqs/13639"),
            "<html><body><div id=\"something-new\"><p>Text</p></div></body></html>"));
}
