namespace DesktopGuides.Core.Library;

// S22: the sites a guide can be saved from. Stored by name, so the names
// are fixed.
public enum SourceSite { GameFaqs, Neoseeker, Steam, VgMaps }

/// <summary>
/// Where a saved guide or map came from. A saved item is found again by
/// its site and <paramref name="ItemId"/>. A partial save records the pages
/// it captured and the pages the guide has.
/// </summary>
public sealed record GuideSource(
    SourceSite Site,
    string ItemId,
    string Url,
    string? Author,
    string? Notice,
    string? SourceTitle,
    DateTimeOffset SavedUtc,
    DateTimeOffset? SourceUpdatedUtc,
    int? CapturedPages = null,
    int? TotalPages = null)
{
    public bool IsPartial => CapturedPages is not null;

    /// <summary>Throws for anything schema 9's GuideSources checks refuse.</summary>
    public void Validate()
    {
        Require(Enum.IsDefined(Site), nameof(Site));
        Require(Length(ItemId, 1, 200), nameof(ItemId));
        Require(Length(Url, 1, 2048), nameof(Url));
        Require(Author is null || Length(Author, 1, 200), nameof(Author));
        Require(Notice is null || Notice.Length <= 16384, nameof(Notice));
        Require(SourceTitle is null || Length(SourceTitle, 1, 512), nameof(SourceTitle));
        Require(SavedUtc.ToUnixTimeMilliseconds() >= 0, nameof(SavedUtc));
        Require(SourceUpdatedUtc is null || SourceUpdatedUtc.Value.ToUnixTimeMilliseconds() >= 0,
            nameof(SourceUpdatedUtc));
        Require((CapturedPages is null) == (TotalPages is null), nameof(TotalPages));
        Require(CapturedPages is null || (CapturedPages >= 1 && TotalPages >= 2 && CapturedPages < TotalPages),
            nameof(CapturedPages));
    }

    private static bool Length(string? value, int min, int max) =>
        value is not null && value.Length >= min && value.Length <= max;

    private static void Require(bool valid, string name)
    {
        if (!valid) throw new ArgumentException($"The guide source's {name} is invalid.", name);
    }
}
