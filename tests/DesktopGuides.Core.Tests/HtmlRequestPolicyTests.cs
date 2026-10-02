using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlRequestPolicyTests
{
    private static readonly Guid Id = Guid.Parse("3f2a9c0e-4b7d-1a65-f08c-2e9d3b4a7c10");
    private static readonly string Origin = "https://" + GuideWebOrigin.HostFor(Id);
    private const string Hash = "0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly GuideAsset[] Assets =
    [
        new("guide.html", "guide.html", GuideAssetKind.EntryHtml, 10, Hash),
        new("styles/main.css", "styles/main.css", GuideAssetKind.StyleSheet, 10, Hash),
        new("images/map.png", "images/map.png", GuideAssetKind.Image, 10, Hash),
        new("images/photo.JPEG", "images/photo.JPEG", GuideAssetKind.Image, 10, Hash),
        new("images/anim.gif", "images/anim.gif", GuideAssetKind.Image, 10, Hash),
        new("images/pic.webp", "images/pic.webp", GuideAssetKind.Image, 10, Hash),
        new("images/vector.svg", "images/vector.svg", GuideAssetKind.Image, 10, Hash),
        new("100% Completion Guide_files/map.png", "__desktop_guides_files/map.png", GuideAssetKind.Image, 10, Hash),
    ];

    private static HtmlRequestPolicy Policy() => new(Id, Assets);

    private static HtmlDenyReason Denied(string uri, string method = "GET") =>
        Assert.IsType<HtmlDeny>(Policy().Decide(method, uri)).Reason;

    private static HtmlServe Served(string uri) =>
        Assert.IsType<HtmlServe>(Policy().Decide("GET", uri));

    [Fact]
    public void EntryUriIsTheEntryRowUnderTheGuideOrigin() =>
        Assert.Equal(Origin + "/guide.html", Policy().EntryUri.AbsoluteUri);

    [Theory]
    [InlineData("/guide.html", "guide.html", "text/html")]
    [InlineData("/styles/main.css", "styles/main.css", "text/css")]
    [InlineData("/images/map.png", "images/map.png", "image/png")]
    [InlineData("/images/photo.JPEG", "images/photo.JPEG", "image/jpeg")]
    [InlineData("/images/anim.gif", "images/anim.gif", "image/gif")]
    [InlineData("/images/pic.webp", "images/pic.webp", "image/webp")]
    public void ServesManifestRowsWithTheirContentType(string path, string relative, string type)
    {
        HtmlServe serve = Served(Origin + path);
        Assert.Equal(relative, serve.Asset.RelativePath);
        Assert.Equal(type, serve.ContentType);
    }

    [Fact]
    public void ContentTypesCarryNoCharset()
    {
        Assert.DoesNotContain("charset", Served(Origin + "/guide.html").ContentType);
        Assert.DoesNotContain("charset", Served(Origin + "/styles/main.css").ContentType);
    }

    [Theory]
    [InlineData("/100%25%20Completion%20Guide_files/map.png")]
    [InlineData("/100%%20Completion%20Guide_files/map.png")]
    public void PercentNamedCompanionFolderServesTheManagedFile(string path) =>
        Assert.Equal("__desktop_guides_files/map.png", Served(Origin + path).Asset.RelativePath);

    [Fact]
    public void DoubleEncodedCompanionFolderIsNotInTheManifest() =>
        Assert.Equal(HtmlDenyReason.NotInManifest, Denied(Origin + "/100%2525%20Completion%20Guide_files/map.png"));

    [Theory]
    [InlineData("/styles/main.css?ver=5.8")]
    [InlineData("/styles/main.css#top")]
    [InlineData("/styles/main.css?ver=5.8#top")]
    public void QueryAndFragmentAreIgnoredForLookup(string path) =>
        Assert.Equal("styles/main.css", Served(Origin + path).Asset.RequestPath);

    [Fact]
    public void HostCaseDoesNotMatterButPathCaseDoes()
    {
        Served(Origin.ToUpperInvariant().Replace("HTTPS", "https") + "/guide.html");
        Assert.Equal(HtmlDenyReason.NotInManifest, Denied(Origin + "/GUIDE.html"));
    }

    [Fact]
    public void RawDotSegmentsAreCollapsedByUriLikeABrowser() =>
        Assert.Equal("guide.html", Served(Origin + "/images/../guide.html").Asset.RequestPath);

    [Theory]
    [InlineData("/%2e%2e/guide.html", "guide.html")]
    [InlineData("/images/%2e/map.png", "images/map.png")]
    public void EncodedDotSegmentsAreCollapsedByUriLikeABrowser(string path, string requestPath) =>
        Assert.Equal(requestPath, Served(Origin + path).Asset.RequestPath);

    [Theory]
    [InlineData("HEAD")]
    [InlineData("POST")]
    [InlineData("get")]
    public void OnlyGetIsServed(string method) =>
        Assert.Equal(HtmlDenyReason.Method, Denied(Origin + "/guide.html", method));

    [Theory]
    [InlineData("http://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/guide.html")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid:8443/guide.html")]
    [InlineData("https://user@g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/guide.html")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/styles/")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/styles//main.css")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/images%5cmap.png")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/guide.html%00")]
    [InlineData("not a uri")]
    public void MalformedRequestsAreDenied(string uri) =>
        Assert.Equal(HtmlDenyReason.Malformed, Denied(uri));

    [Fact]
    public void DoubleEncodedTraversalIsNotInTheManifest() =>
        Assert.Equal(HtmlDenyReason.NotInManifest, Denied(Origin + "/%252e%252e/guide.html"));

    [Theory]
    [InlineData("https://g00000000000000000000000000000001.guide.invalid/guide.html")]
    [InlineData("https://guide.invalid/guide.html")]
    public void AnotherGuidesOriginIsCrossGuide(string uri) =>
        Assert.Equal(HtmlDenyReason.CrossGuide, Denied(uri));

    [Theory]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid.example/guide.html")]
    [InlineData("http://127.0.0.1:8765/canary.png")]
    [InlineData("https://example.com/a.png")]
    public void OtherHostsAreExternal(string uri) =>
        Assert.Equal(HtmlDenyReason.External, Denied(uri));

    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("blob:https://example.com/1")]
    [InlineData("ws://127.0.0.1:8765/")]
    public void OtherSchemesAreDenied(string uri) =>
        Assert.Equal(HtmlDenyReason.OtherScheme, Denied(uri));

    [Fact]
    public void UnsupportedFileTypesAreDenied() =>
        Assert.Equal(HtmlDenyReason.UnsupportedType, Denied(Origin + "/images/vector.svg"));

    [Fact]
    public void ManifestNeedsExactlyOneEntry()
    {
        Assert.Throws<ArgumentException>(() => new HtmlRequestPolicy(Id, Assets.Skip(1)));
        Assert.Throws<ArgumentException>(() => new HtmlRequestPolicy(Id,
            [.. Assets, new GuideAsset("other.html", "other.html", GuideAssetKind.EntryHtml, 1, Hash)]));
        Assert.Throws<ArgumentException>(() => new HtmlRequestPolicy(Id, [.. Assets, Assets[1]]));
    }

    [Fact]
    public void ServedHeadersCarryTheContentSecurityPolicy()
    {
        string headers = HtmlRequestPolicy.ServedHeaders("text/css");
        Assert.Equal(
            "Content-Type: text/css\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store\r\n" +
            "Content-Security-Policy: default-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
            "font-src 'none'; script-src 'none'; frame-src 'none'; form-action 'none'; connect-src 'none'; " +
            "object-src 'none'; base-uri 'none'",
            headers);
    }
}
