using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class AppCacheRootTests : IDisposable
{
    private readonly string localAppData = Directory.CreateTempSubdirectory("dg-localappdata-").FullName;

    [Fact]
    public void PackagedAppUsesThePackageCacheFolder()
    {
        string packaged = Path.Combine(localAppData, "Packages", "DesktopGuides.Preview_x", "LocalCache");

        Assert.Equal(packaged, AppCacheRoot.Resolve(packaged: true, () => packaged, localAppData));
        Assert.False(Directory.Exists(Path.Combine(localAppData, AppDataRoot.PortableFolderName)));
    }

    [Fact]
    public void PortableAppCreatesCacheUnderItsDataFolder()
    {
        string root = AppCacheRoot.Resolve(
            packaged: false, () => throw new InvalidOperationException("No package identity."), localAppData);

        Assert.Equal(Path.Combine(localAppData, "DesktopGuides", "Cache"), root);
        Assert.True(Directory.Exists(root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative\\folder")]
    public void PortableAppRejectsAMissingOrRelativeLocalAppData(string value) =>
        Assert.Throws<InvalidOperationException>(() => AppCacheRoot.Resolve(packaged: false, () => "", value));

    public void Dispose() => Directory.Delete(localAppData, recursive: true);
}
