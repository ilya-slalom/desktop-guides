namespace DesktopGuides.Core.Backup;

/// <summary>
/// Folders a backup may not be saved in. Uninstalling removes the app's own
/// folder, and for MSIX Windows redirects writes under %LOCALAPPDATA% and
/// %APPDATA% into the package store, which uninstall also removes.
/// </summary>
public static class BackupDestinationPolicy
{
    public const string PortableFolderName = "DesktopGuides";

    public static IReadOnlyList<string> ProtectedRoots(
        bool packaged, string dataRoot, string localAppData, string roamingAppData)
    {
        RequireFull(dataRoot, nameof(dataRoot));
        RequireFull(localAppData, nameof(localAppData));
        RequireFull(roamingAppData, nameof(roamingAppData));
        if (!packaged)
        {
            return [Path.Combine(localAppData, PortableFolderName)];
        }
        string package = Path.GetDirectoryName(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot)))!;
        return [package, localAppData, roamingAppData];
    }

    private static void RequireFull(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A full path is required.", name);
        }
    }
}
