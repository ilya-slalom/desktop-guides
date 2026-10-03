using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideWebOriginTests
{
    private static readonly Guid Id = Guid.Parse("3f2a9c0e-4b7d-1a65-f08c-2e9d3b4a7c10");

    [Fact]
    public void HostIsTheGuideIdUnderTheInvalidSuffix()
    {
        Assert.Equal("g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid", GuideWebOrigin.HostFor(Id));
        Assert.Equal("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/", GuideWebOrigin.OriginFor(Id).AbsoluteUri);
    }

    [Fact]
    public void EveryGuideHasItsOwnOrigin() =>
        Assert.NotEqual(GuideWebOrigin.HostFor(Guid.NewGuid()), GuideWebOrigin.HostFor(Guid.NewGuid()));

    [Fact]
    public void EmptyIdHasNoOrigin() =>
        Assert.Throws<ArgumentException>(() => GuideWebOrigin.HostFor(Guid.Empty));

    [Theory]
    [InlineData("guide.html", "/guide.html")]
    [InlineData("100% Completion Guide_files/map.png", "/100%25%20Completion%20Guide_files/map.png")]
    [InlineData("a:b#c?.html", "/a%3Ab%23c%3F.html")]
    public void EntryUriEscapesEachSegment(string requestPath, string expectedPath)
    {
        Uri entry = GuideWebOrigin.EntryUri(Id, requestPath);
        Assert.Equal(GuideWebOrigin.HostFor(Id), entry.Host);
        Assert.Equal(expectedPath, entry.AbsolutePath);
        Assert.Equal("", entry.Query);
        Assert.Equal("", entry.Fragment);
    }

    [Theory]
    [InlineData("g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid", true)]
    [InlineData("G00000000000000000000000000000001.GUIDE.INVALID", true)]
    [InlineData("guide.invalid", true)]
    [InlineData("g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid.example", false)]
    [InlineData("example.com", false)]
    [InlineData("notguide.invalid", false)]
    public void RecognizesGuideHosts(string host, bool expected) =>
        Assert.Equal(expected, GuideWebOrigin.IsGuideHost(host));
}
