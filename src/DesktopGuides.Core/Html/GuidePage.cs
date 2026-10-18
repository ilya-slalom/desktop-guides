namespace DesktopGuides.Core.Html;

/// <summary>
/// T21.2: one page of an HTML guide, in reading order. Order 0 is the entry
/// page; a one-page guide has only it. The address and title are what the
/// saved page recorded, when it did.
/// </summary>
public sealed record GuidePage(int Order, string RequestPath, string? OriginalUrl, string? Title);

public static class PageTitles
{
    private static readonly string[] Separators = [" - ", " – ", " — ", " | ", " · ", " _ ", " :: "];

    // Removes the suffix every title shares, such as " — StrategyWiki | …".
    // The cut starts at a separator, so a word is never split; one page, or
    // a cut that would empty a title, keeps the titles whole.
    public static IReadOnlyList<string> WithoutSharedSuffix(IReadOnlyList<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        if (titles.Count < 2) return titles;
        string first = titles[0];
        int common = first.Length;
        foreach (string title in titles.Skip(1))
        {
            int length = 0;
            while (length < common && length < title.Length && first[^(length + 1)] == title[^(length + 1)])
            {
                length++;
            }
            common = length;
        }
        string shared = first[^common..];
        int cut = Separators
            .Select(separator => shared.IndexOf(separator, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (cut < 0) return titles;
        int remove = common - cut;
        string[] trimmed = titles.Select(title => title[..^remove].TrimEnd()).ToArray();
        return trimmed.Any(title => title.Length == 0) ? titles : trimmed;
    }
}
