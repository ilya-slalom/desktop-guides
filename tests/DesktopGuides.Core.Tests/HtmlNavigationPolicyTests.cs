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

    // A "Save Page As, Complete" export is named after the page title.
    private const string Title = "Canary Guide B (PS1) - Walkthrough's 100% Caf\u00E9 \u2013 v2";
    private static readonly Uri TitledEntry = GuideWebOrigin.EntryUri(Id, Title + ".html");

    // Chromium resolves a relative self-link with these sub-delims left literal.
    private static string LiteralSubDelims(Uri uri) =>
        uri.AbsoluteUri.Replace("%28", "(").Replace("%29", ")").Replace("%27", "'");

    private static HtmlNavigationKind TitledKind(string uri, bool navigated) =>
        HtmlNavigationPolicy.Classify(uri, TitledEntry, navigated, userInitiated: false).Kind;

    [Fact]
    public void TitledEntryIsRecognisedInItsEscapedForm()
    {
        Assert.Equal(HtmlNavigationKind.Entry, TitledKind(TitledEntry.AbsoluteUri, navigated: false));
        Assert.Equal(HtmlNavigationKind.SameDocument, TitledKind(TitledEntry.AbsoluteUri + "#sec", navigated: true));
    }

    [Fact]
    public void TitledEntryIsRecognisedWithLiteralSubDelims()
    {
        string literal = LiteralSubDelims(TitledEntry);
        Assert.Contains("(PS1)", literal, StringComparison.Ordinal);
        Assert.Contains("Walkthrough's", literal, StringComparison.Ordinal);
        Assert.Equal(HtmlNavigationKind.Entry, TitledKind(literal, navigated: false));
        Assert.Equal(HtmlNavigationKind.SameDocument, TitledKind(literal + "#sec", navigated: true));
        Assert.Equal(HtmlNavigationKind.Deny, TitledKind(literal, navigated: true));
        Assert.Equal(HtmlNavigationKind.Deny, TitledKind(literal + "#sec", navigated: false));
        Assert.Equal(HtmlNavigationKind.Deny, TitledKind(literal + "?page=2", navigated: false));
    }

    [Theory]
    [InlineData("Canary Guide B (PS1) - Walkthrough's 100% Caf\u00E8 \u2013 v2.html")]
    [InlineData("Canary Guide B (PS1) - Walkthrough's 100% Caf\u00E9 \u2014 v2.html")]
    [InlineData("Canary Guide B [PS1) - Walkthrough's 100% Caf\u00E9 \u2013 v2.html")]
    public void SiblingFileDifferingByOneEscapedCharacterIsNotTheEntry(string sibling)
    {
        Uri other = GuideWebOrigin.EntryUri(Id, sibling);
        Assert.Equal(HtmlNavigationKind.Deny, TitledKind(other.AbsoluteUri, navigated: false));
        Assert.Equal(HtmlNavigationKind.Deny, TitledKind(LiteralSubDelims(other), navigated: false));
        Assert.Equal(HtmlNavigationKind.Deny, TitledKind(other.AbsoluteUri + "#sec", navigated: true));
    }

    [Fact]
    public void DoubleEncodedTitledEntryIsNotTheEntry() =>
        Assert.Equal(HtmlNavigationKind.Deny,
            TitledKind(TitledEntry.AbsoluteUri.Replace("%28", "%2528"), navigated: false));

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
