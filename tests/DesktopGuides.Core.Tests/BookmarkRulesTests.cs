using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class BookmarkRulesTests
{
    [Theory]
    [InlineData("Chapter 3", "Chapter 3")]
    [InlineData("  The castle  ", "The castle")]
    public void ALabelIsTrimmed(string label, string expected) =>
        Assert.Equal(expected, BookmarkRules.Label(label));

    [Fact]
    public void EightyCharactersIsTheLongestLabel()
    {
        Assert.Equal(80, BookmarkRules.Label(new string('a', 80)).Length);
        Assert.Throws<ArgumentException>(() => BookmarkRules.Label(new string('a', 81)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyLabelIsRefused(string label) =>
        Assert.Throws<ArgumentException>(() => BookmarkRules.Label(label));

    // UTF-16 units, not characters: stricter than SQL's length(), so a label
    // C# accepts always fits the column.
    [Fact]
    public void SurrogatePairsCountAsTwo() =>
        Assert.Throws<ArgumentException>(() => BookmarkRules.Label(new string('a', 79) + "\U0001F5FA"));

    [Fact]
    public void ALocatorIsBoundedInUtf8Bytes()
    {
        string fits = new('a', 4096);
        Assert.Same(fits, BookmarkRules.Locator(fits));
        // 2,049 two-byte characters: 4,098 bytes in 2,049 characters.
        Assert.Throws<ArgumentException>(() => BookmarkRules.Locator(new string('é', 2049)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ABlankLocatorIsRefused(string locator) =>
        Assert.Throws<ArgumentException>(() => BookmarkRules.Locator(locator));

    [Fact]
    public void TheLimitNamesItsGuide()
    {
        Guid guide = Guid.NewGuid();
        Assert.Equal(guide, new BookmarkLimitException(guide).GuideId);
    }
}
