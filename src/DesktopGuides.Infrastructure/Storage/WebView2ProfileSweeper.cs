namespace DesktopGuides.Infrastructure.Storage;

/// <summary>
/// Removes the WebView2 profile folders a crash or a slow exit left behind.
/// Only names <c>HtmlReaderSession</c> generates are touched, and a link is
/// removed as a link: its target is never entered.
/// </summary>
public static class WebView2ProfileSweeper
{
    // Returns the number of profile folders removed.
    public static int Sweep(string cacheRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);
        DirectoryInfo profiles = new(Path.Combine(cacheRoot, "WebView2"));
        int removed = 0;
        try
        {
            if (!profiles.Exists || IsLink(profiles)) return 0;
            foreach (DirectoryInfo profile in profiles.EnumerateDirectories())
            {
                if (!Guid.TryParseExact(profile.Name, "N", out _)) continue;
                try
                {
                    Delete(profile);
                    removed++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Still locked; the next startup tries again.
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // An unreadable cache never blocks startup.
        }
        return removed;
    }

    private static bool IsLink(FileSystemInfo entry) =>
        (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    private static void Delete(DirectoryInfo folder)
    {
        if (!IsLink(folder))
        {
            foreach (FileSystemInfo entry in folder.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo child)
                {
                    Delete(child);
                    continue;
                }
                if (!IsLink(entry) && (entry.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    entry.Attributes &= ~FileAttributes.ReadOnly;
                }
                entry.Delete();
            }
        }
        // A link's own entry goes; its target is untouched.
        folder.Delete();
    }
}
