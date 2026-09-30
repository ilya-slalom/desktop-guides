using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class OwnedGuideTreeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "desktop-guides-tree-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public void FileCountCountsNestedFiles()
    {
        RemovalLibrary.WriteFile(root, "guide.txt", "a");
        RemovalLibrary.WriteFile(root, "images/map.png", "b");
        RemovalLibrary.WriteFile(root, "images/deep/key.png", "c");

        Assert.Equal(3, OwnedGuideTree.Capture(root).FileCount);
    }

    [Fact]
    public void FileCountIsZeroForAMissingRoot() =>
        Assert.Equal(0, OwnedGuideTree.Capture(root).FileCount);
}
