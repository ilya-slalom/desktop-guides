using System.Text.RegularExpressions;

namespace DesktopGuides.Core.Text;

public readonly record struct DetectedSection(int Offset, int Level, string Title);

/// <summary>
/// T18.2: a TXT guide's sections, from its own markup. Search codes are the
/// strongest signal, then underlined or boxed headings, then numbered ones.
/// The first signal that finds three sections, not all in the guide's first
/// tenth, wins; anything less, or running out of time, is no outline.
/// </summary>
public static partial class TextSectionDetector
{
    public const int MinimumSections = 3;
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);
    private const int MaxHeadingLength = 80;
    private const int MaxTitleLength = 60;
    private static readonly char[] Decoration = " \t=-*#~|_.:[]{}".ToCharArray();

    // [WLK01], {A12}, [1.0]: letters then digits, or a dotted number.
    [GeneratedRegex(@"[\[{](?<code>[A-Za-z]{1,6}[0-9]{1,4}[A-Za-z]?|[0-9]{1,2}(?:\.[0-9]{1,2}){1,3})[\]}]")]
    private static partial Regex SearchCode();

    [GeneratedRegex(@"^\s{0,3}(?<number>[0-9]{1,2}(?:\.[0-9]{1,2}){0,3})\.?\s+(?<title>\p{L}.*)$")]
    private static partial Regex NumberedHeading();

    // A dot leader and what follows it: "Basics ........ 12".
    [GeneratedRegex(@"\.{3,}.*$")]
    private static partial Regex Leader();

    private readonly record struct Line(int Start, string Text);

    private delegate List<DetectedSection>? Signal(List<Line> lines, CancellationToken token);

    public static IReadOnlyList<DetectedSection> Detect(string text, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<Line> lines = Split(text);
        foreach (Signal signal in new Signal[] { SearchCodes, Underlined, Numbered })
        {
            if (token.IsCancellationRequested) return [];
            List<DetectedSection>? found = signal(lines, token);
            // Out of time: no outline, never a partial one.
            if (found is null) return [];
            if (found.Count >= MinimumSections && found[^1].Offset >= text.Length / 10) return found;
        }
        return [];
    }

    private static List<DetectedSection>? SearchCodes(List<Line> lines, CancellationToken token)
    {
        Dictionary<string, List<int>> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < lines.Count; i++)
        {
            if ((i & 255) == 0 && token.IsCancellationRequested) return null;
            foreach (Match match in SearchCode().Matches(lines[i].Text))
            {
                string code = match.Groups["code"].Value.ToUpperInvariant();
                if (!seen.TryGetValue(code, out List<int>? at)) seen[code] = at = [];
                if (at.Count == 0 || at[^1] != i) at.Add(i);
            }
        }
        List<DetectedSection> found = [];
        foreach ((string code, List<int> at) in seen)
        {
            // Listed once: a contents line with no section behind it.
            if (at.Count < 2) continue;
            // The first occurrence is the contents block; the heading is the
            // first later line that reads like one, not a cross-reference.
            int line = at.Skip(1).Where(i => HeadingLike(SearchCode().Replace(lines[i].Text, "")))
                .DefaultIfEmpty(at[1]).First();
            found.Add(new DetectedSection(lines[line].Start, CodeLevel(code), Title(lines[line].Text)));
        }
        found.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return found;
    }

    private static List<DetectedSection>? Underlined(List<Line> lines, CancellationToken token)
    {
        List<DetectedSection> found = [];
        for (int i = 1; i < lines.Count; i++)
        {
            if ((i & 255) == 0 && token.IsCancellationRequested) return null;
            if (!IsRule(lines[i].Text, out char rule)) continue;
            string title = lines[i - 1].Text.Trim();
            if (title.Length == 0 || title.Length > MaxHeadingLength || !title.Any(char.IsLetter) ||
                IsRule(title, out _))
            {
                continue;
            }
            // Boxed: a rule above as well, of any length.
            bool boxed = i >= 2 && IsRule(lines[i - 2].Text, out _);
            int ruleLength = lines[i].Text.Trim().Length;
            if (!boxed && Math.Abs(ruleLength - title.Length) > Math.Max(3, title.Length / 4)) continue;
            found.Add(new DetectedSection(lines[i - 1].Start, boxed || rule == '=' ? 1 : 2, Title(title)));
        }
        return found;
    }

    private static List<DetectedSection>? Numbered(List<Line> lines, CancellationToken token)
    {
        Dictionary<string, List<DetectedSection>> byKey = new(StringComparer.Ordinal);
        List<string> order = [];
        for (int i = 0; i < lines.Count; i++)
        {
            if ((i & 255) == 0 && token.IsCancellationRequested) return null;
            string text = lines[i].Text;
            if (text.Trim().Length > MaxHeadingLength) continue;
            Match match = NumberedHeading().Match(text);
            if (!match.Success) continue;
            string raw = Leader().Replace(match.Groups["title"].Value, "").Trim();
            string title = Title(raw);
            if (title.Length == 0 || title.Length > MaxTitleLength || EndsSentence(raw)) continue;
            string number = match.Groups["number"].Value;
            string key = number + " " + title.ToUpperInvariant();
            if (!byKey.TryGetValue(key, out List<DetectedSection>? at))
            {
                byKey[key] = at = [];
                order.Add(key);
            }
            at.Add(new DetectedSection(lines[i].Start, NumberLevel(number), title));
        }
        // A heading listed in the contents comes twice: keep the body's.
        List<DetectedSection> found = order.Select(key => byKey[key] is { Count: > 1 } at ? at[1] : byKey[key][0]).ToList();
        found.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return found;
    }

    private static bool IsRule(string line, out char rule)
    {
        string trimmed = line.Trim();
        char first = trimmed.Length > 0 ? trimmed[0] : '\0';
        rule = first;
        return trimmed.Length >= 3 && "=-~*#_".Contains(first) && trimmed.All(c => c == first);
    }

    private static bool HeadingLike(string remainder)
    {
        string trimmed = remainder.Trim();
        return trimmed.Length <= MaxHeadingLength && trimmed.Any(char.IsLetter) && !EndsSentence(trimmed);
    }

    // "for the castle." is a sentence; a dot leader ("....") isn't.
    private static bool EndsSentence(string text) =>
        text.Length >= 2 && text[^1] == '.' && char.IsLetterOrDigit(text[^2]);

    private static int CodeLevel(string code) => char.IsDigit(code[0]) ? NumberLevel(code) : 1;

    // "2" and "2.0" are level 1, "2.1" level 2, "2.1.3" level 3.
    private static int NumberLevel(string number)
    {
        string[] parts = number.Split('.');
        int depth = parts.Length;
        if (depth > 1 && parts[^1].TrimStart('0').Length == 0) depth--;
        return depth;
    }

    private static string Title(string line)
    {
        string text = Leader().Replace(SearchCode().Replace(line, " "), "");
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(Decoration);
    }

    private static List<Line> Split(string text)
    {
        List<Line> lines = [];
        int start = 0;
        while (true)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0)
            {
                lines.Add(new Line(start, text[start..]));
                return lines;
            }
            lines.Add(new Line(start, text[start..end]));
            start = end + 1;
        }
    }
}
