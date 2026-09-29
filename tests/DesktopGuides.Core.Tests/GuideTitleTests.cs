using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideTitleTests
{
    [Theory]
    [InlineData("Walkthrough.txt", "Walkthrough")]
    [InlineData("  FAQ v2 .HTML", "FAQ v2")]
    [InlineData("100% Walkthrough.html", "100% Walkthrough")]
    [InlineData("archive.tar.pdf", "archive.tar")]
    [InlineData(".txt", GuideTitle.Fallback)]
    [InlineData("   .pdf", GuideTitle.Fallback)]
    public void SuggestsTheFileNameWithoutItsExtension(string fileName, string expected) =>
        Assert.Equal(expected, GuideTitle.Suggest(fileName));

    [Fact]
    public void SuggestionIsCutTo200Characters()
    {
        string suggested = GuideTitle.Suggest(new string('A', 201) + ".txt");

        Assert.Equal(new string('A', 200), suggested);
    }

    [Fact]
    public void SuggestionNeverSplitsASurrogatePair()
    {
        // 199 letters, then U+1F600 (two UTF-16 units) straddles the cut.
        string name = new string('A', 199) + "\U0001F600" + "tail.txt";

        string suggested = GuideTitle.Suggest(name);

        Assert.Equal(new string('A', 199), suggested);
        Assert.False(char.IsHighSurrogate(suggested[^1]));
    }

    [Fact]
    public void CreateTrimsAndAcceptsOneTo200Characters()
    {
        Assert.Equal("A", GuideTitle.Create("  A  "));
        Assert.Equal(200, GuideTitle.Create(" " + new string('T', 200) + " ").Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void CreateRejectsAnEmptyTitle(string? title) =>
        Assert.Throws<ArgumentException>(() => GuideTitle.Create(title));

    [Fact]
    public void CreateRejects201Characters()
    {
        Assert.Throws<ArgumentException>(() => GuideTitle.Create(new string('T', 201)));
        Assert.False(GuideTitle.TryCreate(new string('T', 201), out _));
        Assert.True(GuideTitle.TryCreate(" Guide ", out string title));
        Assert.Equal("Guide", title);
    }
}
