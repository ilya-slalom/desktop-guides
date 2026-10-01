namespace DesktopGuides.Core.Library;

/// <summary>The game removal confirmation and status copy.</summary>
public static class GameRemovalPresentation
{
    public static string DialogTitle(string title, int guideCount) => guideCount switch
    {
        0 => $"Remove {title}?",
        1 => $"Remove {title} and its guide?",
        _ => $"Remove {title} and its {guideCount} guides?",
    };

    public static string DialogBody(int guideCount, int fileCount)
    {
        if (guideCount == 0)
        {
            return "This removes the game and its details from Desktop Guides.";
        }
        string files = fileCount == 1 ? "1 managed file" : $"{fileCount} managed files";
        return guideCount == 1
            ? $"This removes the game, its guide with its reading progress, and its {files} from Desktop Guides. " +
                "The original file you imported isn't affected."
            : $"This removes the game, its {guideCount} guides with their reading progress, and their {files} from Desktop Guides. " +
                "The original files you imported aren't affected.";
    }

    public static string CountChanged(int guideCount) =>
        $"The number of guides changed. It's now {guideCount}.";

    public static string Removed(string title, bool cleanupPending) => cleanupPending
        ? $"Removed {title}. Leftover files will be cleaned up the next time Desktop Guides starts."
        : $"Removed {title}.";

    public static string AlreadyRemoved(string title) => $"{title} was already removed.";

    public static string Error(GameRemovalIssue issue, string title) => issue switch
    {
        GameRemovalIssue.Unsafe => $"{title} can't be removed because a guide's files were changed outside Desktop Guides.",
        GameRemovalIssue.Failed => $"{title} couldn't be removed. The game is unchanged. Try again.",
        GameRemovalIssue.RestoreFailed => $"{title} couldn't be removed. Restart Desktop Guides to finish restoring it.",
        _ => throw new ArgumentOutOfRangeException(nameof(issue)),
    };
}
