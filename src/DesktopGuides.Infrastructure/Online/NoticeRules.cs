using System.Text.RegularExpressions;

namespace DesktopGuides.Infrastructure.Online;

// S22: a guide's own copyright notice, found in its text.
public static partial class NoticeRules
{
    private const int MaxLines = 30;
    private const int MaxCharacters = 16384;

    /// <summary>
    /// The paragraph holding the text's first notice line ("(c)", "©", or
    /// "copyright" and a year), up to 30 lines; null when there is none. A
    /// contents line such as "3. Copyright" is not a notice.
    /// </summary>
    public static string? Find(string text)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        int hit = Array.FindIndex(lines, line => NoticeLine().IsMatch(line));
        if (hit < 0) return null;
        int start = hit;
        while (start > 0 && !string.IsNullOrWhiteSpace(lines[start - 1]) && hit - start < MaxLines - 1) start--;
        int end = hit;
        while (end + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[end + 1]) && end - start < MaxLines - 1) end++;
        string notice = string.Join("\n", lines[start..(end + 1)]).Trim();
        return notice.Length > MaxCharacters ? notice[..MaxCharacters] : notice;
    }

    [GeneratedRegex(@"\(c\)|©|copyright\s*(\(c\)|©)?\s*\d{4}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoticeLine();
}
