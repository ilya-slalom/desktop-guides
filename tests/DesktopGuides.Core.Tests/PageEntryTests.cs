using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PageEntryTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("200", 200)]
    [InlineData(" 150 ", 150)]
    [InlineData("007", 7)]
    public void InRangeWholeNumbersParse(string text, int expected)
    {
        Assert.True(PageEntry.TryParse(text, 200, out int page));
        Assert.Equal(expected, page);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("201")]
    [InlineData("-1")]
    [InlineData("+3")]
    [InlineData("1.5")]
    [InlineData("1,000")]
    [InlineData("1e2")]
    [InlineData("99999999999")]
    [InlineData("three")]
    public void EverythingElseIsRefused(string? text)
    {
        Assert.False(PageEntry.TryParse(text, 200, out int page));
        Assert.Equal(0, page);
    }

    [Fact]
    public void NoPagesRefusesEverything() =>
        Assert.False(PageEntry.TryParse("1", 0, out _));

    [Fact]
    public void TheMessageNamesTheRange()
    {
        Assert.Equal("Enter a page from 1 to 200.", PageEntry.RangeMessage(200));
        Assert.Equal("Enter a page from 1 to 1.", PageEntry.RangeMessage(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PageEntry.RangeMessage(0));
    }
}
