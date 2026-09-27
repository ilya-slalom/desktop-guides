namespace DesktopGuides.Infrastructure.Import;

/// <summary>
/// Reads url() arguments from CSS text emitted by AngleSharp.Css. It does not
/// resolve or decode a target; the scanner rejects unsafe escape syntax.
/// </summary>
internal static class CssUrlReferences
{
    public static IEnumerable<string> Extract(string css)
    {
        for (int index = 0; index < css.Length;)
        {
            if (css[index] == '/' && index + 1 < css.Length &&
                css[index + 1] == '*')
            {
                int end = css.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? css.Length : end + 2;
                continue;
            }
            if (css[index] is '\'' or '"')
            {
                index = SkipString(css, index);
                continue;
            }
            if (!IsIdentifierCharacter(css[index]))
            {
                index++;
                continue;
            }

            int start = index;
            while (index < css.Length && IsIdentifierCharacter(css[index]))
            {
                index++;
            }
            if (!css.AsSpan(start, index - start).Equals(
                    "url".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            int cursor = index;
            while (cursor < css.Length && char.IsWhiteSpace(css[cursor]))
            {
                cursor++;
            }
            if (cursor >= css.Length || css[cursor] != '(')
            {
                continue;
            }
            if (TryReadArgument(css, cursor + 1, out string? argument,
                    out int after))
            {
                index = after;
                if (!string.IsNullOrWhiteSpace(argument))
                {
                    yield return argument;
                }
            }
        }
    }

    private static bool TryReadArgument(
        string css,
        int start,
        out string? argument,
        out int after)
    {
        argument = null;
        int cursor = start;
        while (cursor < css.Length && char.IsWhiteSpace(css[cursor]))
        {
            cursor++;
        }
        if (cursor >= css.Length)
        {
            after = css.Length;
            return false;
        }
        if (css[cursor] is '\'' or '"')
        {
            char quote = css[cursor++];
            int valueStart = cursor;
            while (cursor < css.Length)
            {
                if (css[cursor] == '\\')
                {
                    cursor = Math.Min(cursor + 2, css.Length);
                    continue;
                }
                if (css[cursor] == quote)
                {
                    argument = css[valueStart..cursor];
                    cursor++;
                    while (cursor < css.Length && char.IsWhiteSpace(css[cursor]))
                    {
                        cursor++;
                    }
                    if (cursor < css.Length && css[cursor] == ')')
                    {
                        after = cursor + 1;
                        return true;
                    }
                    after = cursor;
                    return false;
                }
                cursor++;
            }
            after = css.Length;
            return false;
        }

        int unquotedStart = cursor;
        while (cursor < css.Length && css[cursor] != ')')
        {
            if (css[cursor] is '\'' or '"')
            {
                after = cursor + 1;
                return false;
            }
            cursor++;
        }
        if (cursor >= css.Length)
        {
            after = css.Length;
            return false;
        }
        argument = css[unquotedStart..cursor].Trim();
        after = cursor + 1;
        return true;
    }

    private static int SkipString(string css, int start)
    {
        char quote = css[start];
        for (int index = start + 1; index < css.Length; index++)
        {
            if (css[index] == '\\')
            {
                index++;
            }
            else if (css[index] == quote)
            {
                return index + 1;
            }
        }
        return css.Length;
    }

    private static bool IsIdentifierCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is '-' or '_';
}
