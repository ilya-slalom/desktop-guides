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
    DateTimeOffset UpdatedUtc);

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

public sealed record AppSettings(ThemePreference Theme, Guid? LastActiveGuideId);
