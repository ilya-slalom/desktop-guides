namespace DesktopGuides.Core.Library;

public enum EmptyGameRemovalOutcome { Removed, NotFound, HasGuides }

/// <summary>The artwork path is set only for Removed, and only when the game had artwork.</summary>
public sealed record EmptyGameRemoval(
    EmptyGameRemovalOutcome Outcome, string? ArtworkRelativePath);
