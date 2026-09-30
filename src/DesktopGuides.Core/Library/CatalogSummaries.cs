namespace DesktopGuides.Core.Library;

// One Library row: the game, its guide count, and the latest of its creation,
// any guide import and any guide open.
// GuideTitles are the game's own guide titles, in title then ID order.
public sealed record LibraryGameSummary(
    Game Game, int GuideCount, DateTimeOffset LastActivityUtc,
    IReadOnlyList<string> GuideTitles);

// One Game-page row. State is null when the guide has no reading-state row.
public sealed record GuideSummary(Guide Guide, ReadingState? State);

// A row fact: Label is shown, AccessibleLabel is read by screen readers.
public sealed record CatalogFact(string Label, string AccessibleLabel);
