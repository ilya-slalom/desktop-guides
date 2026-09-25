using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlAssetPolicyTests
{
    [Fact]
    public void AllowsOnlyManifestAssetsWithinTheSelectedGuideRoot()
    {
        using AssetDirectory assets = new();
        HtmlAssetPolicy policy = assets.CreatePolicy();

        Assert.Equal(assets.GuidePath, policy.Resolve("GET", "https://guide.invalid/guide.html"));
        Assert.Equal(assets.StylePath, policy.Resolve("GET", "https://guide.invalid/styles/main.css"));
        Assert.Null(policy.Resolve("GET", "https://guide.invalid/unlisted.html"));
    }

    [Theory]
    [InlineData("POST", "https://guide.invalid/guide.html")]
    [InlineData("GET", "http://guide.invalid/guide.html")]
    [InlineData("GET", "https://elsewhere.invalid/guide.html")]
    [InlineData("GET", "https://guide.invalid:444/guide.html")]
    [InlineData("GET", "https://guide.invalid/../outside.html")]
    [InlineData("GET", "https://guide.invalid/%2e%2e/outside.html")]
    [InlineData("GET", "file:///C:/Windows/win.ini")]
    [InlineData("GET", "https://guide.invalid/guide.html?remote=true")]
    public void RejectsRequestsOutsideTheReadOnlyAssetScope(string method, string url)
    {
        using AssetDirectory assets = new();
        Assert.Null(assets.CreatePolicy().Resolve(method, url));
    }

    [Fact]
    public void RejectsAnAssetChangedAfterManifestVerification()
    {
        using AssetDirectory assets = new();
        HtmlAssetPolicy policy = assets.CreatePolicy();
        File.WriteAllText(assets.StylePath, "changed");

        Assert.Null(policy.Resolve("GET", "https://guide.invalid/styles/main.css"));
    }

    [Fact]
    public void RejectsAnAssetReachedThroughADirectoryLink()
    {
        using AssetDirectory assets = new();
        string outside = Path.Combine(Path.GetTempPath(), "desktop-guides-html-outside-" + Guid.NewGuid());
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "secret.html"), "Guide text");
            string linked = Path.Combine(assets.Root, "linked");
            Directory.CreateSymbolicLink(linked, outside);

            Assert.Throws<InvalidDataException>(() =>
                new HtmlAssetPolicy(assets.Root, [Path.Combine(linked, "secret.html")]));
        }
        finally
        {
            Directory.Delete(outside, true);
        }
    }

    private sealed class AssetDirectory : IDisposable
    {
        public AssetDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "desktop-guides-html-" + Guid.NewGuid());
            Directory.CreateDirectory(Path.Combine(Root, "styles"));
            GuidePath = Path.Combine(Root, "guide.html");
            StylePath = Path.Combine(Root, "styles", "main.css");
            File.WriteAllText(GuidePath, "<h1>Guide</h1>");
            File.WriteAllText(StylePath, "h1 {color: blue}");
            File.WriteAllText(Path.Combine(Root, "unlisted.html"), "not allowed");
        }

        public string Root { get; }
        public string GuidePath { get; }
        public string StylePath { get; }

        public HtmlAssetPolicy CreatePolicy() => new(Root, [GuidePath, StylePath]);

        public void Dispose() => Directory.Delete(Root, true);
    }
}
