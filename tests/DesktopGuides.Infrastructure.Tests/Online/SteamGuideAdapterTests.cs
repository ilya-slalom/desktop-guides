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
            [new Uri("https://images.steamusercontent.com/ugc/2/FULLA/"), new Uri("https://images.steamusercontent.com/ugc/3/FULLB/")],
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
}
