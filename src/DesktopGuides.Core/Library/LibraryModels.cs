using DesktopGuides.Core.Providers;

namespace DesktopGuides.Core.Library;

public enum GuideFormat
{
    Txt,
    Html,
    Pdf
}

public enum ThemePreference
{
    System,
    Light,
    Dark
}

public sealed record Game(
    Guid Id,
    string Title,
    string? Platform,
    string? Notes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    ProviderGameLink? Link = null,
    GameMetadataSnapshot? Metadata = null,
    string? ArtworkRelativePath = null);

public sealed record Guide(
    Guid Id,
    Guid GameId,
    string Title,
    GuideFormat Format,
    string ManagedRelativeRoot,
    string PrimaryRelativePath,
    string ContentSha256,
    long ContentBytes,
    string? SourceLabel,
    int? TextCodePage,
    DateTimeOffset ImportedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record ReadingState(
    Guid GuideId,
    string? LocatorJson,
    double? EstimatedFraction,
    DateTimeOffset? LastOpenedUtc,
    DateTimeOffset? CompletedUtc);

public sealed record ReaderPreferences(Guid GuideId, double? TextScale);

public enum WindowMaterial
{
    Mica,
    Acrylic,
    Solid
}

public sealed record AppSettings(
    ThemePreference Theme,
    Guid? LastActiveGuideId,
    WindowMaterial WindowMaterial = WindowMaterial.Mica,
    bool ExportReminderShown = false);

// MissingGuides compares by value, so reports stay comparable in tests.
public sealed record StartupReconciliationReport(
    int ResolvedOperationCount,
    int ReviewOrphanCount,
    int ArtworkReviewCount = 0,
    IReadOnlyList<MissingGuideFile>? MissingGuides = null,
    int UnreadableGuideCount = 0)
{
    public IReadOnlyList<MissingGuideFile> MissingGuides { get; init; } = MissingGuides ?? [];

    public bool Equals(StartupReconciliationReport? other) =>
        other is not null &&
        ResolvedOperationCount == other.ResolvedOperationCount &&
        ReviewOrphanCount == other.ReviewOrphanCount &&
        ArtworkReviewCount == other.ArtworkReviewCount &&
        UnreadableGuideCount == other.UnreadableGuideCount &&
        MissingGuides.SequenceEqual(other.MissingGuides);

    public override int GetHashCode() => HashCode.Combine(
        ResolvedOperationCount, ReviewOrphanCount, ArtworkReviewCount,
        UnreadableGuideCount, MissingGuides.Count);
}

public sealed record NewLinkedGame(
    Guid Id,
    string Title,
    string? Platform,
    ProviderGameLink Link,
    GameMetadataSnapshot Metadata,
    string? ArtworkRelativePath);

public sealed class DuplicateProviderLinkException(Guid existingGameId)
    : Exception("This game is already in the library.")
{
    public Guid ExistingGameId { get; } = existingGameId;
}
