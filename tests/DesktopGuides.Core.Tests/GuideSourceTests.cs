using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideSourceTests
{
    [Fact]
    public void ACompleteSourceIsValidAndNotPartial()
    {
        GuideSource source = Source();
        source.Validate();
        Assert.False(source.IsPartial);
    }

    [Fact]
    public void APartialSourceHasFewerCapturedPagesThanTotal()
    {
        Assert.True(Source() with { CapturedPages = 37, TotalPages = 80 } is { IsPartial: true });
        (Source() with { CapturedPages = 37, TotalPages = 80 }).Validate();
        Assert.Throws<ArgumentException>(() => (Source() with { CapturedPages = 80, TotalPages = 80 }).Validate());
        Assert.Throws<ArgumentException>(() => (Source() with { CapturedPages = 3 }).Validate());
    }

    [Theory]
    [InlineData("", "https://example.test/g")]
    [InlineData("id", "")]
    public void AnEmptyIdOrUrlIsInvalid(string id, string url) =>
        Assert.Throws<ArgumentException>(() => (Source() with { ItemId = id, Url = url }).Validate());

    [Fact]
    public void LimitsMatchTheSchema()
    {
        Assert.Throws<ArgumentException>(() => (Source() with { ItemId = new string('a', 201) }).Validate());
        Assert.Throws<ArgumentException>(() => (Source() with { Url = new string('a', 2049) }).Validate());
        Assert.Throws<ArgumentException>(() => (Source() with { Author = "" }).Validate());
        Assert.Throws<ArgumentException>(() => (Source() with { Notice = new string('a', 16385) }).Validate());
        Assert.Throws<ArgumentException>(() => (Source() with { SourceTitle = new string('a', 513) }).Validate());
    }

    [Fact]
    public void SiteNamesAreStable() =>
        Assert.Equal(["GameFaqs", "Neoseeker", "Steam", "VgMaps"], Enum.GetNames<SourceSite>());

    private static GuideSource Source() => new(
        SourceSite.GameFaqs, "13639",
        "https://gamefaqs.gamespot.com/ps/197341-final-fantasy-vii/faqs/13639",
        "Author", "(c) Author", "Walkthrough",
        DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_000), null);
}
