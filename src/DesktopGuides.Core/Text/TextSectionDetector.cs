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
    private const int MaxTitleLength = 50;
    // A contents block lists its codes within this many lines.
    private const int ContentsWindow = 60;
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
        List<(int Line, string Code)> all = [];
        for (int i = 0; i < lines.Count; i++)
        {
            if ((i & 255) == 0 && token.IsCancellationRequested) return null;
            foreach (Match match in SearchCode().Matches(lines[i].Text))
            {
                string code = match.Groups["code"].Value.ToUpperInvariant();
                if (!seen.TryGetValue(code, out List<int>? at)) seen[code] = at = [];
                if (at.Count > 0 && at[^1] == i) continue;
                at.Add(i);
                all.Add((i, code));
            }
        }
        // The guide's own contents block: the window of lines that holds the
        // most distinct codes. Codes it doesn't list (button names such as
        // [L1], map grid squares) aren't search codes.
        Dictionary<string, int> inWindow = new(StringComparer.Ordinal);
        HashSet<string> listed = [];
        int listedEnd = -1;
        for (int left = 0, right = 0; right < all.Count; right++)
        {
            inWindow[all[right].Code] = inWindow.GetValueOrDefault(all[right].Code) + 1;
            while (all[right].Line - all[left].Line >= ContentsWindow)
            {
                if (--inWindow[all[left].Code] == 0) inWindow.Remove(all[left].Code);
                left++;
            }
            if (inWindow.Count > listed.Count)
            {
                listed = [.. inWindow.Keys];
                listedEnd = all[right].Line;
            }
        }
        List<DetectedSection> found = [];
        if (listed.Count < MinimumSections) return found;
        foreach (string code in listed)
        {
            // The heading is a later line that reads like one: beside a rule,
            // or the code first or last on a short line. A reference mid-line
            // ("see [WLK02] for the castle") never is; no heading, no entry.
            List<int> later = seen[code].Where(i => i > listedEnd).ToList();
            int line = later.FirstOrDefault(i => BesideRule(lines, i) && Title(lines[i].Text).Length > 0, -1);
            if (line < 0) line = later.FirstOrDefault(i => CodeAtEdge(lines[i].Text), -1);
            if (line < 0) continue;
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
            if (title.Length == 0 || title.Length > MaxTitleLength || !title.Any(char.IsLetter) ||
                IsRule(title, out _) || Columns(title))
            {
                continue;
            }
            // Boxed: a rule above as well, of any length.
            bool boxed = i >= 2 && IsRule(lines[i - 2].Text, out _);
            // A heading opens a block: a blank line or the file's start comes
            // first, never a paragraph's last line or a table's previous row.
            int before = boxed ? i - 3 : i - 2;
            if (before >= 0 && lines[before].Text.Trim().Length > 0) continue;
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

    // Underlined (a rule below) or boxed (rules both sides). A rule only
    // above is a heading's own rule, and the line under it is the text.
    private static bool BesideRule(List<Line> lines, int index) =>
        index + 1 < lines.Count && IsRule(lines[index + 1].Text, out _);

    // "[WLK01] Forest" or "Forest .... [WLK01]": the code at either end of a
    // short line that isn't a sentence.
    private static bool CodeAtEdge(string line)
    {
        string trimmed = line.Trim();
        string remainder = SearchCode().Replace(trimmed, "").Trim();
        string title = Title(trimmed);
        return title.Length > 0 && title.Length <= MaxTitleLength && !EndsSentence(remainder) &&
            SearchCode().Matches(trimmed).Any(m => m.Index == 0 || m.Index + m.Length == trimmed.Length);
    }

    // Aligned columns ("Slime     10   5"): two or more gaps of two spaces.
    private static bool Columns(string text) =>
        Regex.Matches(text, @"\S\s{2,}(?=\S)").Count >= 2;

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
