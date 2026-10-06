using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class AppDataRootTests : IDisposable
{
    private readonly string localAppData = Directory.CreateTempSubdirectory("dg-localappdata-").FullName;

    [Fact]
    public void PackagedAppUsesThePackageLocalFolder()
    {
        string packaged = Path.Combine(localAppData, "Packages", "DesktopGuides.Preview_x", "LocalState");

        Assert.Equal(packaged, AppDataRoot.Resolve(packaged: true, () => packaged, localAppData));
        Assert.False(Directory.Exists(Path.Combine(localAppData, AppDataRoot.PortableFolderName)));
    }

    [Fact]
    public void PortableAppCreatesItsFolderUnderLocalAppDataWithoutAskingForThePackageFolder()
    {
        string root = AppDataRoot.Resolve(
            packaged: false, () => throw new InvalidOperationException("No package identity."), localAppData);

        Assert.Equal(Path.Combine(localAppData, "DesktopGuides"), root);
        Assert.True(Directory.Exists(root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative\\folder")]
    public void PortableAppRejectsAMissingOrRelativeLocalAppData(string value) =>
        Assert.Throws<InvalidOperationException>(() => AppDataRoot.Resolve(packaged: false, () => "", value));

    [Fact]
    public void TestProcessHasNoPackageIdentity()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.False(AppDataRoot.HasPackageIdentity());
    }

    public void Dispose() => Directory.Delete(localAppData, recursive: true);
}
