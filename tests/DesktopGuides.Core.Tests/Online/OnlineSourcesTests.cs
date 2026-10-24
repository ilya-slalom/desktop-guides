using DesktopGuides.Core.Library;
using DesktopGuides.Core.Online;
using Xunit;

namespace DesktopGuides.Core.Tests.Online;

public sealed class OnlineSourcesTests
{
    [Fact]
    public void OnlineSourcesListGameFaqsNeoseekerSteamThenVgMaps() =>
        Assert.Equal([(SourceSite.GameFaqs, "GameFAQs"), (SourceSite.Neoseeker, "Neoseeker"), (SourceSite.Steam, "Steam"), (SourceSite.VgMaps, "VGMaps")],
            OnlineSources.Available.Select(source => (source.Site, source.Name)));

    [Theory]
    [InlineData("https://gamefaqs.gamespot.com/ps/1-x/faqs/2", SourceSite.GameFaqs)]
    [InlineData("https://www.neoseeker.com/x/walkthrough", SourceSite.Neoseeker)]
    [InlineData("https://GAMEFAQS.gamespot.com/", SourceSite.GameFaqs)]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=1", SourceSite.Steam)]
    [InlineData("https://vgmaps.com/Atlas/PSX/index.htm", SourceSite.VgMaps)]
    public void FindMatchesASourcesHosts(string url, SourceSite site) =>
        Assert.Equal(site, OnlineSources.Find(new Uri(url))?.Site);

    [Theory]
    [InlineData("http://gamefaqs.gamespot.com/ps/1-x/faqs/2")]
    [InlineData("https://example.com/")]
    [InlineData("https://neoseeker.com.evil.test/")]
    [InlineData("https://www.gamespot.com/")]
    public void FindMatchesNothingElse(string url) => Assert.Null(OnlineSources.Find(new Uri(url)));

    [Fact]
    public void TheGameFaqsStartUrlSearchesForTheTitle() =>
        Assert.Equal(new Uri("https://gamefaqs.gamespot.com/search?game=Final%20Fantasy%20VII"),
            OnlineSources.StartUrl(OnlineSources.Available[0], "Final Fantasy VII"));

    [Fact]
    public void TheNeoseekerStartUrlIsItsHome() =>
        Assert.Equal(new Uri("https://www.neoseeker.com/"), OnlineSources.StartUrl(OnlineSources.Available[1], "X"));
}
