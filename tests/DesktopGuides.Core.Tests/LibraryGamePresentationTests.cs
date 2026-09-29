using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class LibraryGamePresentationTests
{
    private static Game GameWith(string title, string? platform) => new(
        Guid.NewGuid(), title, platform, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void SummaryIsTheTrimmedPlatform()
    {
        Assert.Equal("Nintendo Switch", LibraryGamePresentation.Summary(GameWith("Zelda", "  Nintendo Switch ")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SummaryIsNullWhenThePlatformIsBlank(string? platform)
    {
        Assert.Null(LibraryGamePresentation.Summary(GameWith("Zelda", platform)));
    }

    [Theory]
    [InlineData("ゼルダの伝説")]
    [InlineData("كتالوج الألعاب")]
    [InlineData("Catalog D Größe Überfall Äpfel")]
    public void AccessibleNameIsTheFullTitle(string title)
    {
        Assert.Equal(title, LibraryGamePresentation.AccessibleName(GameWith(title, "PC")));
    }

    [Fact]
    public void AccessibleNameKeepsATitleAtTheLengthLimit()
    {
        string title = "Catalog A " + new string('W', 150);
        Assert.Equal(GameDetails.TitleLimit, title.Length);
        Assert.Equal(title, LibraryGamePresentation.AccessibleName(GameWith(title, "PC")));
    }
}
