using DesktopGuides.Core.Paths;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ManagedRelativePathTests
{
    [Theory]
    [InlineData("guide.html")]
    [InlineData("styles/main guide.css")]
    [InlineData("images/map.v2.png")]
    [InlineData("images/🗺️.png")]
    public void AllowsNormalizedRelativePaths(string path)
    {
        Assert.Equal(path, ManagedRelativePath.Parse(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../secret.txt")]
    [InlineData("styles/../secret.txt")]
    [InlineData("styles//main.css")]
    [InlineData("styles/./main.css")]
    [InlineData("styles\\main.css")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("//server/share.txt")]
    [InlineData("styles/%2fsecret.css")]
    [InlineData("styles/%5csecret.css")]
    [InlineData("styles/CON.txt")]
    [InlineData("styles/COM¹.txt")]
    [InlineData("styles/LPT².txt")]
    [InlineData("styles/NUL .txt")]
    [InlineData("styles/COM0.txt")]
    [InlineData("styles/trailing.")]
    public void RejectsEscapesAndAmbiguousWindowsNames(string path)
    {
        Assert.Throws<InvalidDataException>(() => ManagedRelativePath.Parse(path));
    }

    [Fact]
    public void RejectsUnpairedSurrogate()
    {
        string malformed = "images/" + new string((char)0xD800, 1) + ".png";
        Assert.Throws<InvalidDataException>(() => ManagedRelativePath.Parse(malformed));
    }
}
