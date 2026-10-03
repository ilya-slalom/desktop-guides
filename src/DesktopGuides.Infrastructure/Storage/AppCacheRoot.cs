namespace DesktopGuides.Infrastructure.Storage;

// The MSIX build keeps disposable data in the package's LocalCache. The
// portable build has no package identity, so it uses a Cache folder beside
// its library under %LOCALAPPDATA%\DesktopGuides.
public static class AppCacheRoot
{
    public const string PortableFolderName = "Cache";

    public static string Resolve(bool packaged, Func<string> packagedCacheFolder, string localAppData)
    {
        if (packaged) return packagedCacheFolder();
        string root = Path.Combine(
            AppDataRoot.Resolve(false, static () => throw new InvalidOperationException(), localAppData),
            PortableFolderName);
        Directory.CreateDirectory(root);
        return root;
    }
}
