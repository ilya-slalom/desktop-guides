using System.Runtime.InteropServices;

namespace DesktopGuides.Infrastructure.Storage;

// The MSIX build keeps its data in the package's LocalState. The portable build
// has no package identity, so it uses %LOCALAPPDATA%\DesktopGuides instead.
public static class AppDataRoot
{
    public const string PortableFolderName = "DesktopGuides";
    private const int AppModelErrorNoPackage = 15700;

    public static bool HasPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
    }

    public static string Resolve(bool packaged, Func<string> packagedLocalFolder, string localAppData)
    {
        if (packaged) return packagedLocalFolder();
        if (string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathFullyQualified(localAppData))
        {
            throw new InvalidOperationException("The local application data folder is unavailable.");
        }
        string root = Path.Combine(localAppData, PortableFolderName);
        Directory.CreateDirectory(root);
        return root;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
}
