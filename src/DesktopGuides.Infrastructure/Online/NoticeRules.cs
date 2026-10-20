using System.Text.RegularExpressions;

namespace DesktopGuides.Infrastructure.Online;

// S22: how a notice treats a saved copy; Restrictive prompts before saving.
public enum NoticeKind { Unknown, Permissive, Restrictive }

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

    /// <summary>
    /// Permissive when the notice allows personal use; Restrictive when it
    /// forbids electronic or any-form copies (a forbidding phrase within 80
    /// characters of the copy wording); otherwise Unknown. Forbidding posting
    /// on other sites is about hosting, not saving, so it stays Unknown.
    /// </summary>
    public static NoticeKind Classify(string? notice)
    {
        if (string.IsNullOrWhiteSpace(notice)) return NoticeKind.Unknown;
        if (PersonalUse().IsMatch(notice)) return NoticeKind.Permissive;
        int[] forbids = Forbidding().Matches(notice).Select(match => match.Index).ToArray();
        int[] copies = CopyWording().Matches(notice).Select(match => match.Index).ToArray();
        return forbids.Any(forbid => copies.Any(copy => Math.Abs(copy - forbid) <= 80))
            ? NoticeKind.Restrictive
            : NoticeKind.Unknown;
    }

    [GeneratedRegex(@"personal use|private use|personal,\s*private", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PersonalUse();

    [GeneratedRegex(@"may not|cannot|can not|must not|not be|prohibited|forbidden", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Forbidding();

    [GeneratedRegex(@"electronic|any form|in any way|any means|stored|saved", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CopyWording();

    [GeneratedRegex(@"\(c\)|©|copyright\s*(\(c\)|©)?\s*\d{4}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoticeLine();
}
