using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlNavigationPolicyTests
{
    private static readonly Guid Id = Guid.Parse("3f2a9c0e-4b7d-1a65-f08c-2e9d3b4a7c10");
    private static readonly Uri Entry = GuideWebOrigin.EntryUri(Id, "guide.html");

    private static HtmlNavigationKind Kind(string uri, bool navigated = true, bool user = true) =>
        HtmlNavigationPolicy.Classify(uri, Entry, navigated, user).Kind;

    [Fact]
    public void FirstEntryNavigationIsAllowedOnce()
    {
        Assert.Equal(HtmlNavigationKind.Entry, Kind(Entry.AbsoluteUri, navigated: false, user: false));
        Assert.Equal(HtmlNavigationKind.Deny, Kind(Entry.AbsoluteUri, navigated: true));
    }

    [Fact]
    public void FragmentsOfTheEntryAreSameDocumentAfterItLoads()
    {
        Assert.Equal(HtmlNavigationKind.SameDocument, Kind(Entry.AbsoluteUri + "#details"));
        Assert.Equal(HtmlNavigationKind.Deny, Kind(Entry.AbsoluteUri + "#details", navigated: false));
    }

    [Fact]
    public void UserInitiatedWebsiteLinksAreExternal()
    {
        HtmlNavigation navigation = HtmlNavigationPolicy.Classify(
            "HTTPS://Example.com/desktop-guides-canary", Entry, true, true);
        Assert.Equal(HtmlNavigationKind.External, navigation.Kind);
        Assert.Equal("https://example.com/desktop-guides-canary", navigation.ExternalUri!.AbsoluteUri);
        Assert.Equal(HtmlNavigationKind.External, Kind("http://example.org/page?x=1"));
    }

    [Theory]
    [InlineData("https://example.com/redirected")]
    [InlineData("http://127.0.0.1:8765/refresh")]
    public void NonUserInitiatedWebsiteNavigationIsDenied(string uri) =>
        Assert.Equal(HtmlNavigationKind.Deny, Kind(uri, user: false));

    [Theory]
    [InlineData("https://example.com@evil.example/")]
    [InlineData("https://g00000000000000000000000000000001.guide.invalid/guide.html")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/other.html")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/guide.html?page=2")]
    [InlineData("https://guide.invalid/guide.html")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JAVASCRIPT:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("file:///C:/guide.html")]
    [InlineData("blob:https://example.com/1")]
    [InlineData("steam://run/1")]
    [InlineData("about:blank")]
    [InlineData("not a uri")]
    public void EverythingElseIsDenied(string uri) =>
        Assert.Equal(HtmlNavigationKind.Deny, Kind(uri));
}
