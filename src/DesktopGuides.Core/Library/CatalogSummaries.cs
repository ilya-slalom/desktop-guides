namespace DesktopGuides.Core.Library;

// One Library row: the game, its guide count, and the latest of its creation,
// any guide import and any guide open.
public sealed record LibraryGameSummary(Game Game, int GuideCount, DateTimeOffset LastActivityUtc);

// One Game-page row. State is null when the guide has no reading-state row.
public sealed record GuideSummary(Guide Guide, ReadingState? State);

// A row fact: Label is shown, AccessibleLabel is read by screen readers.
public sealed record CatalogFact(string Label, string AccessibleLabel);
