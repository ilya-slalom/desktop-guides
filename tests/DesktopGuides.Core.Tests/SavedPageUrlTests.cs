using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class SavedPageUrlTests
{
    [Fact]
    public void TheCommentGivesTheAddressItsLengthNames() =>
        Assert.Equal(new Uri("https://guides.example/wiki/Sample_Quest"), SavedPageUrl.FromComment(
            "<!DOCTYPE html>\n<!-- saved from url=(0040)https://guides.example/wiki/Sample_Quest -->\n<html>"));

    [Theory]
    [InlineData("<html><head>")]
    [InlineData("<!-- saved from url=(0099)https://guides.example/short -->")]
    [InlineData("<!-- saved from url=(abcd)https://guides.example/ -->")]
    [InlineData("<!-- saved from url=(0016)file:///C:/x.htm -->")]
    public void ABrokenOrMissingCommentGivesNoAddress(string prefix) =>
        Assert.Null(SavedPageUrl.FromComment(prefix));

    [Theory]
    [InlineData("mailto:someone@guides.example")]
    [InlineData("/wiki/Relative")]
    [InlineData("")]
    [InlineData(null)]
    public void OnlyWebAddressesParse(string? value) => Assert.Null(SavedPageUrl.Parse(value));

    [Fact]
    public void AddressesOfOnePageShareAKey()
    {
        string key = SavedPageUrl.Key(new Uri("https://guides.example/wiki/Sample_Quest:Chapter_2"));

        Assert.Equal(key, SavedPageUrl.Key(new Uri("http://Guides.Example/wiki/Sample_Quest%3AChapter_2#top")));
        Assert.Equal(key, SavedPageUrl.Key(new Uri("https://guides.example:443/wiki/Sample_Quest:Chapter_2")));
    }

    [Fact]
    public void ThePathCaseAndTheQueryStillCount()
    {
        string key = SavedPageUrl.Key(new Uri("https://guides.example/faqs/1?page=2"));

        Assert.NotEqual(key, SavedPageUrl.Key(new Uri("https://guides.example/faqs/1?page=3")));
        Assert.NotEqual(key, SavedPageUrl.Key(new Uri("https://guides.example/FAQS/1?page=2")));
        Assert.NotEqual(key, SavedPageUrl.Key(new Uri("https://guides.example:8443/faqs/1?page=2")));
    }

    [Fact]
    public void HostsCompareWithoutCase()
    {
        Assert.True(SavedPageUrl.SameHost(new Uri("https://Guides.Example/a"), new Uri("http://guides.example/b")));
        Assert.False(SavedPageUrl.SameHost(new Uri("https://guides.example/a"), new Uri("https://other.example/a")));
    }
}
