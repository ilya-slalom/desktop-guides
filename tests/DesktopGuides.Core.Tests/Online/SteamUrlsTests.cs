using DesktopGuides.Core.Online;
using DesktopGuides.Core.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests.Online;

public sealed class SteamUrlsTests
{
    [Theory]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=3285627728")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=3285627728&searchtext=x")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails?id=3285627728")]
    [InlineData("http://STEAMCOMMUNITY.com/sharedfiles/filedetails/?id=3285627728")]
    public void AGuideUrlNamesItsFile(string url) =>
        Assert.Equal("3285627728", SteamUrls.GuideId(new Uri(url)));

    [Theory]
    [InlineData("https://steamcommunity.com/workshop/filedetails/?id=1")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=abc")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=123456789012345678901")]
    [InlineData("https://example.com/sharedfiles/filedetails/?id=1")]
    public void OtherUrlsNameNoGuide(string url) => Assert.Null(SteamUrls.GuideId(new Uri(url)));

    [Theory]
    [InlineData("https://store.steampowered.com/app/620/Portal_2/")]
    [InlineData("https://steamcommunity.com/app/620/guides/")]
    [InlineData("https://steamcommunity.com/app/620")]
    public void AStoreOrGuidesUrlNamesItsApp(string url) => Assert.Equal("620", SteamUrls.AppId(new Uri(url)));

    [Theory]
    [InlineData("https://store.steampowered.com/app/abc/")]
    [InlineData("https://example.com/app/620/")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=1")]
    public void OtherUrlsNameNoApp(string url) => Assert.Null(SteamUrls.AppId(new Uri(url)));

    [Fact]
    public void AGuideHasOneUrl() =>
        Assert.Equal(new Uri("https://steamcommunity.com/sharedfiles/filedetails/?id=42"), SteamUrls.Guide("42"));

    [Fact]
    public void TheKeyNeverPrints()
    {
        string printed = new ProviderCredentials(null, null, "SECRETKEY123").ToString();
        Assert.Contains("SteamWebApiKey = saved", printed);
        Assert.DoesNotContain("SECRETKEY123", printed);
    }
}
