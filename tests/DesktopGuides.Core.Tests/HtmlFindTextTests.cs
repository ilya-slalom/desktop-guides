using DesktopGuides.Core.Html;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlFindTextTests
{
    [Fact]
    public void ParsesTextAndBreaks()
    {
        HtmlFindText? text = HtmlFindText.Parse("""{"text":"BossHydra","breaks":[4],"hidden":[]}""");

        Assert.NotNull(text);
        Assert.Equal("BossHydra", text.Text);
        Assert.Equal([4], text.Breaks);
    }

    [Fact]
    public void HiddenTextIsMaskedAndOffsetsKeep()
    {
        HtmlFindText text = HtmlFindText.Parse(
            """{"text":"Open Secret door","breaks":[],"hidden":[[5,11]]}""")!;

        Assert.Equal("Open \0\0\0\0\0\0 door", text.Text);
        Assert.Empty(TextMatcher.FindAll(text.Text, new FindQuery("secret")));
        Assert.Equal(new TextMatch(12, 4), Assert.Single(TextMatcher.FindAll(text.Text, new FindQuery("door"))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"breaks":[],"hidden":[]}""")]
    [InlineData("""{"text":"abc","breaks":[4],"hidden":[]}""")]
    [InlineData("""{"text":"abc","breaks":[2,1],"hidden":[]}""")]
    [InlineData("""{"text":"abc","breaks":[],"hidden":[[2,1]]}""")]
    [InlineData("""{"text":"abc","breaks":[],"hidden":[[0,4]]}""")]
    [InlineData("""{"text":"abc","breaks":[],"hidden":[[0]]}""")]
    [InlineData("""{"text":"abc","breaks":["1"],"hidden":[]}""")]
    [InlineData("not json")]
    public void MalformedRepliesAreRefused(string? json) =>
        Assert.Null(HtmlFindText.Parse(json));
}
