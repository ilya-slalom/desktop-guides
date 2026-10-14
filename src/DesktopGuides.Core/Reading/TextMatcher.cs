using System.Text;

namespace DesktopGuides.Core.Reading;

public readonly record struct TextMatch(int Start, int Length);

// T18.1: case-insensitive (ordinal, invariant), with whitespace runs in the
// text and the query matching one space. Ranges are in the source text.
public static class TextMatcher
{
    public const int MaxMatches = 10_000;

    public static IReadOnlyList<TextMatch> FindAll(string text, FindQuery query, int maxMatches = MaxMatches)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        // folded[i] came from source[starts[i]..ends[i]).
        StringBuilder folded = new(text.Length);
        List<int> starts = new(text.Length);
        List<int> ends = new(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                int runStart = index;
                while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
                folded.Append(' ');
                starts.Add(runStart);
                ends.Add(index);
            }
            else
            {
                folded.Append(char.ToUpperInvariant(text[index]));
                starts.Add(index);
                ends.Add(index + 1);
                index++;
            }
        }
        string haystack = folded.ToString();
        string needle = query.Text.ToUpperInvariant();
        List<TextMatch> matches = [];
        int at = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0 && matches.Count < maxMatches)
        {
            int start = starts[at];
            matches.Add(new TextMatch(start, ends[at + needle.Length - 1] - start));
            at = haystack.IndexOf(needle, at + 1, StringComparison.Ordinal);
        }
        return matches;
    }

    // The match to show next. With no current match, the first one at or
    // after fromOffset (Next), or the last one before it (Previous),
    // wrapping at the ends.
    public static int? Step(IReadOnlyList<TextMatch> matches, int? current, int fromOffset, FindDirection direction)
    {
        if (matches.Count == 0) return null;
        if (current is int at)
        {
            return direction == FindDirection.Next
                ? (at + 1) % matches.Count
                : (at - 1 + matches.Count) % matches.Count;
        }
        if (direction == FindDirection.Next)
        {
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Start >= fromOffset) return i;
            }
            return 0;
        }
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            if (matches[i].Start < fromOffset) return i;
        }
        return matches.Count - 1;
    }

    internal static string Collapse(string text)
    {
        StringBuilder collapsed = new(text.Length);
        bool space = false;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!space) collapsed.Append(' ');
                space = true;
            }
            else
            {
                collapsed.Append(character);
                space = false;
            }
        }
        return collapsed.ToString();
    }
}
