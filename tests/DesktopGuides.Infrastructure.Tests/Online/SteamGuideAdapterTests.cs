using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Online;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class SteamGuideAdapterTests
{
    private static readonly Uri Url = new("https://steamcommunity.com/sharedfiles/filedetails/?id=4242");
    private readonly SteamGuideAdapter adapter = new();

    [Fact]
    public void AGuidePageIsOneHtmlPage()
    {
        GuidePageReading page = adapter.Read(Url, P3Fixtures.Read("steam/guide.html"))!;

        Assert.Equal(new GuidePageMatch(SourceSite.Steam, "4242", ""), page.Match);
        Assert.Equal(CapturedFormat.Html, page.Format);
        Assert.Equal(("Example Quest Field Notes", "Test Author"), (page.Title, page.Author));
        Assert.Empty(page.Pages);
        Assert.Equal(["The village", "The forest", "The castle"],
            System.Text.RegularExpressions.Regex.Matches(page.Content, "<h2>(.*?)</h2>").Select(m => m.Groups[1].Value));
        Assert.Contains("Notes for every chapter of Example Quest.", page.Content);
        Assert.Equal(
            [new Uri("https://images.steamusercontent.com/ugc/2/FULLA/"), new Uri("https://images.steamusercontent.com/ugc/3/FULLB/"),
             new Uri("https://images.steamusercontent.com/ugc/9/OTHER/")],
            page.Images);
    }

    [Fact]
    public void ImagesAreTheirFullSizeAndLinksLeaveTheLinkFilter()
    {
        string content = adapter.Read(Url, P3Fixtures.Read("steam/guide.html"))!.Content;
        Assert.Contains("src=\"https://images.steamusercontent.com/ugc/3/FULLB/\"", content);
        Assert.DoesNotContain("modalContentLink", content);
        Assert.DoesNotContain("?imw=", content);
        Assert.Contains("href=\"https://example.com/map\"", content);
        Assert.DoesNotContain("linkfilter", content);
        // An emoticon is its text; it isn't an image of the guide.
        Assert.Contains(":steamhappy:", content);
        Assert.DoesNotContain("emoticon", content);
    }

    // Review Focus 3.
    [Fact]
    public void APageWithoutSectionsIsNoGuide() =>
        Assert.Null(adapter.Read(Url, P3Fixtures.Read("steam/guide-no-sections.html")));

    [Theory]
    [InlineData("https://steamcommunity.com/app/620/guides/")]
    [InlineData("https://www.neoseeker.com/x/walkthrough")]
    public void OnlyGuideUrlsMatch(string url) => Assert.Null(adapter.Match(new Uri(url)));

    // Review: a YouTube embed is an empty div Steam's script fills; offline it's a link.
    [Fact]
    public void AVideoBecomesALinkToIt() =>
        Assert.Contains("<a href=\"https://www.youtube.com/watch?v=jpXJ5cC1eCQ\">Video on YouTube</a>",
            adapter.Read(Url, P3Fixtures.Read("steam/guide.html"))!.Content);

    // Review: a thumbnail can link to another guide; the link stays and the image is the thumbnail.
    [Fact]
    public void AThumbnailLinkingElsewhereKeepsItsLink()
    {
        string content = adapter.Read(Url, P3Fixtures.Read("steam/guide.html"))!.Content;
        Assert.Contains("href=\"https://steamcommunity.com/sharedfiles/filedetails/?id=777&amp;insideModal=1\"", content);
        Assert.Contains("src=\"https://images.steamusercontent.com/ugc/9/OTHER/\"", content);
        Assert.DoesNotContain("akamaihd", content);
    }

    // Review: the source review's attribution links the author's profile.
    [Fact]
    public void TheAuthorsProfileIsRead() =>
        Assert.Equal(new Uri("https://steamcommunity.com/id/testauthor"),
            adapter.Read(Url, P3Fixtures.Read("steam/guide.html"))!.AuthorUrl);

    [Theory]
    [InlineData("9 Jul, 2024 @ 9:51am", 2024, 7, 9, 9, 51)]
    [InlineData("3 Mar @ 4:05pm", 2026, 3, 3, 16, 5)]
    [InlineData("Dec 1, 2023 @ 12:00pm", 2023, 12, 1, 12, 0)]
    public void APageDateReadsAsUtc(string text, int year, int month, int day, int hour, int minute) =>
        Assert.Equal(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero),
            SteamGuideAdapter.PageDate(text, 2026));

    [Fact]
    public void TheLastPageDateIsTheUpdate() =>
        Assert.Equal(new DateTimeOffset(DateTimeOffset.UtcNow.Year, 3, 3, 16, 5, 0, TimeSpan.Zero),
            adapter.Read(Url, P3Fixtures.Read("steam/guide.html"))!.SourceUpdated);
}
