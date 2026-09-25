using DesktopGuides.Core.Paths;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ManagedRelativePathTests
{
    [Theory]
    [InlineData("guide.html")]
    [InlineData("styles/main guide.css")]
    [InlineData("images/map.v2.png")]
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
    [InlineData("styles/trailing.")]
    public void RejectsEscapesAndAmbiguousWindowsNames(string path)
    {
        Assert.Throws<InvalidDataException>(() => ManagedRelativePath.Parse(path));
    }
}
