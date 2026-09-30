namespace DesktopGuides.Core.Library;

/// <summary>The removal confirmation and status copy.</summary>
public static class GuideRemovalPresentation
{
    public static string DialogTitle(string title) => $"Remove {title}?";

    public static string DialogBody(int fileCount)
    {
        string files = fileCount == 1 ? "1 managed file" : $"{fileCount} managed files";
        return $"This removes the guide, its reading progress, and its {files} from Desktop Guides. " +
            "The original file you imported isn't affected.";
    }

    public static string Removed(string title, bool cleanupPending) => cleanupPending
        ? $"Removed {title}. Leftover files will be cleaned up the next time Desktop Guides starts."
        : $"Removed {title}.";

    public static string AlreadyRemoved(string title) => $"{title} was already removed.";

    public static string Error(GuideRemovalIssue issue, string title) => issue switch
    {
        GuideRemovalIssue.Unsafe => $"{title} can't be removed because its files were changed outside Desktop Guides.",
        GuideRemovalIssue.Failed => $"{title} couldn't be removed. The guide is unchanged. Try again.",
        GuideRemovalIssue.RestoreFailed => $"{title} couldn't be removed. Restart Desktop Guides to finish restoring it.",
        _ => throw new ArgumentOutOfRangeException(nameof(issue)),
    };
}
