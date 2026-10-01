namespace DesktopGuides.Core.Library;

public enum EmptyGameRemovalOutcome { Removed, NotFound, HasGuides }

/// <summary>The artwork path is set only for Removed, and only when the game had artwork.</summary>
public sealed record EmptyGameRemoval(
    EmptyGameRemovalOutcome Outcome, string? ArtworkRelativePath);

/// <summary>What the confirmation states: the game, its guide count and its managed-file count.</summary>
public sealed record GameRemovalPreview(Guid GameId, string Title, int GuideCount, int FileCount);

public enum GameRemovalOutcome { Removed, NotFound, CountChanged }

/// <summary>
/// CleanupPending is true only for Removed: startup deletes the leftover trash.
/// Current is the fresh preview, set only for CountChanged.
/// </summary>
public sealed record GameRemovalResult(
    GameRemovalOutcome Outcome, bool CleanupPending, GameRemovalPreview? Current);

public enum GameRemovalIssue { Unsafe, Failed, RestoreFailed }

public sealed class GameRemovalException(GameRemovalIssue issue, Exception? inner = null)
    : Exception(null, inner)
{
    public GameRemovalIssue Issue { get; } = issue;
}
