namespace DesktopGuides.Core.Library;

// One Library row: the game, its guide and map counts, and the latest of
// its creation, any guide import and any guide open.
// GuideTitles are the game's own guide and map titles, in title then ID order.
public sealed record LibraryGameSummary(
    Game Game, int GuideCount, DateTimeOffset LastActivityUtc,
    IReadOnlyList<string> GuideTitles, int MapCount = 0);

// One Game-page row. State is null when the guide has no reading-state row.
public sealed record GuideSummary(Guide Guide, ReadingState? State);

// A row fact: Label is shown, AccessibleLabel is read by screen readers.
public sealed record CatalogFact(string Label, string AccessibleLabel);
