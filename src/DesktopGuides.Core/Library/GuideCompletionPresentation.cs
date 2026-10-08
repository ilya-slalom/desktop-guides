namespace DesktopGuides.Core.Library;

/// <summary>The completion choice's labels, name, and status copy.</summary>
public static class GuideCompletionPresentation
{
    public const string InProgressLabel = "In progress";
    public const string CompleteLabel = "Complete";
    // The text the Reader already shows for a missing guide.
    public const string Removed = "This guide is no longer in your library.";

    // Only a stored completion time is complete; the estimate plays no part.
    public static bool IsComplete(DateTimeOffset? completedUtc) => completedUtc is not null;

    public static string ChoiceName(string title) => $"Completion for {title}";

    public static string Announcement(string title, DateTimeOffset? completedUtc) =>
        IsComplete(completedUtc) ? $"{title} marked complete." : $"{title} marked in progress.";

    public static string SaveFailed(string title) =>
        $"Couldn't update completion for {title}. Try again.";
}
