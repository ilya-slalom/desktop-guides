namespace DesktopGuides.Core.Text;

public enum ReflowKind { Prose, Preserved }

// A run of display characters that came from one run of source characters.
public sealed record ReflowSegment(int DisplayStart, int SourceStart, int Length);

// T19.2: one block of the reflow view. [Start, End) is its source range,
// trailing blank lines included; Text is what it shows.
public sealed record ReflowBlock(
    ReflowKind Kind, int Start, int End, string Text, IReadOnlyList<ReflowSegment> Segments)
{
    // A joining space maps to the end of the line before it.
    public int SourceOffsetAt(int displayIndex)
    {
        ReflowSegment segment = SegmentAtDisplay(Math.Clamp(displayIndex, 0, Text.Length));
        return segment.SourceStart + Math.Min(Math.Max(displayIndex - segment.DisplayStart, 0), segment.Length);
    }

    // A source offset past a segment (trailing spaces, the line end, blank
    // lines) maps to that segment's end.
    public int DisplayIndexOf(int sourceOffset)
    {
        ReflowSegment segment = Segments[0];
        foreach (ReflowSegment candidate in Segments)
        {
            if (candidate.SourceStart > sourceOffset) break;
            segment = candidate;
        }
        return segment.DisplayStart + Math.Clamp(sourceOffset - segment.SourceStart, 0, segment.Length);
    }

    private ReflowSegment SegmentAtDisplay(int displayIndex)
    {
        ReflowSegment segment = Segments[0];
        foreach (ReflowSegment candidate in Segments)
        {
            if (candidate.DisplayStart > displayIndex) break;
            segment = candidate;
        }
        return segment;
    }
}

public sealed record ReflowPlan(IReadOnlyList<ReflowBlock> Blocks)
{
    // The block whose source range holds the offset.
    public int BlockAt(int sourceOffset)
    {
        int low = 0;
        int high = Blocks.Count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (Blocks[middle].Start <= sourceOffset) low = middle;
            else high = middle - 1;
        }
        return low;
    }
}

// T19.2: splits a TXT guide into prose, which the reflow view wraps, and
// preserved blocks, which keep their layout. Doubtful paragraphs are
// preserved. It only reads the document (TR19.1).
public static class TextReflowPlanner
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);
    // Plain punctuation, including the typographic quotes, dashes and ellipsis
    // that guides copied from word processors carry.
    private const string ProsePunctuation = ".,;:'\"!?()-\u2018\u2019\u201C\u201D\u2013\u2014\u2026";
    // Characters that draw a layout; never prose.
    private const string LayoutCharacters = "|\\_";
    private const int MinimumWidthSample = 30;

    // Null when the guide has no prose, is empty, or the token ends first.
    public static ReflowPlan? Plan(
        TextGuideDocument document, IReadOnlyList<DetectedSection> headings, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(headings);
        try
        {
            return Build(document, headings, token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static ReflowPlan? Build(
        TextGuideDocument document, IReadOnlyList<DetectedSection> headings, CancellationToken token)
    {
        string text = document.Text;
        List<(int Start, int Length)> lines = Lines(document, token);
        int? width = TypicalWidth(text, lines, token);
        if (width is not int typical) return null;

        List<List<int>> paragraphs = [];
        List<int>? current = null;
        for (int i = 0; i < lines.Count; i++)
        {
            if (i % 256 == 0) token.ThrowIfCancellationRequested();
            if (IsBlank(text, lines[i]))
            {
                current = null;
                continue;
            }
            if (current is null)
            {
                current = [];
                paragraphs.Add(current);
            }
            current.Add(i);
        }
        if (paragraphs.Count == 0) return null;

        HashSet<int> headingOffsets = [.. headings.Select(h => h.Offset)];
        List<ReflowBlock> blocks = [];
        for (int p = 0; p < paragraphs.Count; p++)
        {
            List<int> paragraph = paragraphs[p];
            int start = p == 0 ? 0 : lines[paragraph[0]].Start;
            int end = p + 1 < paragraphs.Count ? lines[paragraphs[p + 1][0]].Start : text.Length;
            blocks.Add(IsProse(text, lines, paragraph, typical, headingOffsets)
                ? ProseBlock(text, lines, paragraph, start, end)
                : PreservedBlock(text, lines, paragraph, start, end));
        }
        return blocks.Any(b => b.Kind == ReflowKind.Prose) ? new ReflowPlan(blocks) : null;
    }

    // Each line without its newline; the empty line after a final newline is left out.
    private static List<(int Start, int Length)> Lines(TextGuideDocument document, CancellationToken token)
    {
        string text = document.Text;
        IReadOnlyList<int> starts = document.LineStarts;
        List<(int, int)> lines = [];
        for (int i = 0; i < starts.Count; i++)
        {
            if (i % 256 == 0) token.ThrowIfCancellationRequested();
            int start = starts[i];
            int next = i + 1 < starts.Count ? starts[i + 1] - 1 : text.Length;
            if (start == text.Length && i > 0) break;
            lines.Add((start, Math.Max(next - start, 0)));
        }
        return lines;
    }

    // The 75th percentile of the trimmed lengths of texty, unindented lines
    // of 30 or more characters; null with fewer than three.
    private static int? TypicalWidth(string text, List<(int Start, int Length)> lines, CancellationToken token)
    {
        List<int> lengths = [];
        for (int i = 0; i < lines.Count; i++)
        {
            if (i % 256 == 0) token.ThrowIfCancellationRequested();
            ReadOnlySpan<char> line = Line(text, lines[i]);
            int trimmed = line.TrimEnd().Length;
            if (trimmed >= MinimumWidthSample && !IsIndented(line) && IsTexty(line)) lengths.Add(trimmed);
        }
        if (lengths.Count < 3) return null;
        lengths.Sort();
        return lengths[(int)(0.75 * (lengths.Count - 1))];
    }

    private static bool IsProse(
        string text, List<(int Start, int Length)> lines, List<int> paragraph, int typical, HashSet<int> headings)
    {
        (int firstStart, int firstLength) = lines[paragraph[0]];
        if (headings.Any(offset => offset >= firstStart && offset <= firstStart + firstLength)) return false;
        double minimum = 0.6 * typical;
        for (int k = 0; k < paragraph.Count; k++)
        {
            ReadOnlySpan<char> line = Line(text, lines[paragraph[k]]);
            if (IsIndented(line) || !IsTexty(line) || HasAlignmentGap(line.Trim())) return false;
            // A list keeps its items apart.
            if (k > 0 && IsListItem(line)) return false;
            bool last = k == paragraph.Count - 1;
            if ((!last || paragraph.Count == 1) && line.TrimEnd().Length < minimum) return false;
        }
        return true;
    }

    // Lines joined with one space; each line's trimmed text is one segment.
    private static ReflowBlock ProseBlock(
        string text, List<(int Start, int Length)> lines, List<int> paragraph, int start, int end)
    {
        System.Text.StringBuilder display = new();
        List<ReflowSegment> segments = [];
        foreach (int index in paragraph)
        {
            (int lineStart, int _) = lines[index];
            ReadOnlySpan<char> line = Line(text, lines[index]).TrimEnd();
            if (display.Length > 0) display.Append(' ');
            segments.Add(new ReflowSegment(display.Length, lineStart, line.Length));
            display.Append(line);
        }
        return new ReflowBlock(ReflowKind.Prose, start, end, display.ToString(), segments);
    }

    // The paragraph's own text, from its first line to the end of its last.
    private static ReflowBlock PreservedBlock(
        string text, List<(int Start, int Length)> lines, List<int> paragraph, int start, int end)
    {
        int from = lines[paragraph[0]].Start;
        (int lastStart, int lastLength) = lines[paragraph[^1]];
        int length = lastStart + lastLength - from;
        return new ReflowBlock(
            ReflowKind.Preserved, start, end, text.Substring(from, length), [new ReflowSegment(0, from, length)]);
    }

    private static ReadOnlySpan<char> Line(string text, (int Start, int Length) line) =>
        text.AsSpan(line.Start, line.Length);

    private static bool IsBlank(string text, (int Start, int Length) line) => Line(text, line).IsWhiteSpace();

    private static bool IsIndented(ReadOnlySpan<char> line) => line.Length > 0 && (line[0] == ' ' || line[0] == '\t');

    // Mostly letters and digits. Layout characters, control characters, box
    // drawing and rules (three or more of one symbol) are never prose; other
    // symbols, such as HP/MP or 50%, are allowed up to a tenth of the line.
    private static bool IsTexty(ReadOnlySpan<char> line)
    {
        ReadOnlySpan<char> trimmed = line.Trim();
        if (trimmed.IsEmpty) return false;
        int words = 0;
        int other = 0;
        int run = 0;
        char previous = '\0';
        foreach (char c in trimmed)
        {
            run = c == previous ? run + 1 : 1;
            previous = c;
            if (char.IsLetterOrDigit(c))
            {
                words++;
                continue;
            }
            if (char.IsControl(c) || LayoutCharacters.Contains(c) || c is >= '\u2500' and <= '\u259F') return false;
            if (run >= 3 && c is not ('.' or '!' or '?' or ' ')) return false;
            if (c != ' ' && !ProsePunctuation.Contains(c)) other++;
        }
        return words >= 0.6 * trimmed.Length && other <= 0.1 * trimmed.Length;
    }

    // Column alignment: three or more spaces, or two that don't follow the end
    // of a sentence (older guides put two spaces after one).
    private static bool HasAlignmentGap(ReadOnlySpan<char> line)
    {
        for (int i = 1; i + 1 < line.Length; i++)
        {
            if (line[i] != ' ' || line[i + 1] != ' ') continue;
            bool third = i + 2 < line.Length && line[i + 2] == ' ';
            if (third || ".!?:".IndexOf(line[i - 1]) < 0) return true;
            i++;
        }
        return false;
    }

    // "- item", "* item", "+ item", "• item", "1. item" or "1) item".
    private static bool IsListItem(ReadOnlySpan<char> line)
    {
        if (line.Length >= 2 && line[0] is '-' or '*' or '+' or '\u2022' && line[1] == ' ') return true;
        int digits = 0;
        while (digits < line.Length && digits < 4 && char.IsAsciiDigit(line[digits])) digits++;
        return digits is > 0 and < 4 && digits + 1 < line.Length &&
            line[digits] is '.' or ')' && line[digits + 1] == ' ';
    }
}
