namespace DesktopGuides.Core.Reading;

/// <summary>
/// T21.2: search across a guide of several pages. Within the open page a
/// step never wraps; past its ends, it goes to the nearest other page with
/// a match, wrapping around the guide. The count adds the other pages'
/// saved-text counts to the open page's live count.
/// </summary>
public static class GuideFind
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    public static int? StepInPage(IReadOnlyList<TextMatch> matches, int? current, int fromOffset, FindDirection direction)
    {
        ArgumentNullException.ThrowIfNull(matches);
        if (current is int at)
        {
            int next = direction == FindDirection.Next ? at + 1 : at - 1;
            return next >= 0 && next < matches.Count ? next : null;
        }
        if (direction == FindDirection.Next)
        {
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Start >= fromOffset) return i;
            }
            return null;
        }
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            if (matches[i].Start < fromOffset) return i;
        }
        return null;
    }

    public static int? NextPage(IReadOnlyList<int> counts, int openPage, FindDirection direction)
    {
        ArgumentNullException.ThrowIfNull(counts);
        int step = direction == FindDirection.Next ? 1 : -1;
        for (int offset = 1; offset < counts.Count; offset++)
        {
            int page = ((openPage + step * offset) % counts.Count + counts.Count) % counts.Count;
            if (counts[page] > 0) return page;
        }
        return null;
    }

    // indexInPage is 0-based; Index is 1-based across the guide.
    public static (int Index, int Total) Count(IReadOnlyList<int> counts, int openPage, int liveCount, int indexInPage)
    {
        ArgumentNullException.ThrowIfNull(counts);
        int before = counts.Take(openPage).Sum();
        int others = counts.Sum() - counts[openPage];
        return (before + indexInPage + 1, others + liveCount);
    }
}
