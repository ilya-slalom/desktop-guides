namespace DesktopGuides.Core.Library;

/// <summary>What the confirmation names: the guide and its managed-file count.</summary>
public sealed record GuideRemovalPreview(Guid GuideId, Guid GameId, string Title, int FileCount);

public enum GuideRemovalOutcome { Removed, NotFound }

/// <summary>CleanupPending is true only for Removed: startup deletes the leftover trash.</summary>
public sealed record GuideRemovalResult(GuideRemovalOutcome Outcome, bool CleanupPending);

public enum GuideRemovalIssue { Unsafe, Failed, RestoreFailed }

public sealed class GuideRemovalException(GuideRemovalIssue issue, Exception? inner = null)
    : Exception(null, inner)
{
    public GuideRemovalIssue Issue { get; } = issue;
}
