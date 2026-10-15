using System.Globalization;

namespace DesktopGuides.Core.Reading;

// T18.1: the find bar's note under the count, for pages search couldn't read.
public static class FindResultText
{
    private const int ListedPages = 3;

    public static string? Note(FindResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        List<string> parts = [];
        if (result.UnsearchablePages > 0)
        {
            parts.Add(result.UnsearchablePages == 1
                ? "1 image-only page"
                : $"{result.UnsearchablePages} image-only pages");
        }
        IReadOnlyList<int> unreadable = result.UnreadablePages;
        if (unreadable.Count == 1)
        {
            parts.Add($"Couldn't search page {unreadable[0]}");
        }
        else if (unreadable.Count > 1)
        {
            string listed = string.Join(", ",
                unreadable.Take(ListedPages).Select(page => page.ToString(CultureInfo.InvariantCulture)));
            parts.Add(unreadable.Count > ListedPages
                ? $"Couldn't search pages {listed} and {unreadable.Count - ListedPages} more"
                : $"Couldn't search pages {listed}");
        }
        return parts.Count == 0 ? null : string.Join(" \u00B7 ", parts);
    }
}
