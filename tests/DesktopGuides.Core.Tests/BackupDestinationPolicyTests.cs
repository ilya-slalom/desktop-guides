using DesktopGuides.Core.Backup;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class BackupDestinationPolicyTests
{
    private const string Local = @"C:\Users\reader\AppData\Local";
    private const string Roaming = @"C:\Users\reader\AppData\Roaming";

    [Fact]
    public void PackagedProtectsThePackageFolderAndBothAppDataFolders()
    {
        IReadOnlyList<string> roots = BackupDestinationPolicy.ProtectedRoots(
            true, Local + @"\Packages\DesktopGuides.Preview_abc\LocalState\", Local, Roaming);

        Assert.Equal([Local + @"\Packages\DesktopGuides.Preview_abc", Local, Roaming], roots);
    }

    [Fact]
    public void PortableProtectsOnlyItsOwnFolder()
    {
        IReadOnlyList<string> roots = BackupDestinationPolicy.ProtectedRoots(
            false, Local + @"\DesktopGuides", Local, Roaming);

        Assert.Equal([Local + @"\DesktopGuides"], roots);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"relative\folder")]
    public void APathThatIsntFullIsRejected(string local)
    {
        Assert.Throws<ArgumentException>(() =>
            BackupDestinationPolicy.ProtectedRoots(false, Local + @"\DesktopGuides", local, Roaming));
    }
}
