namespace DesktopGuides.Infrastructure.Import;

/// <summary>
/// Reads url() arguments from CSS declaration values in source text, including
/// nested rules that the CSS parser may omit. It decodes escapes in the function
/// name, but leaves targets unchanged for the scanner's path checks.
/// </summary>
internal static class CssUrlReferences
{
    public static IEnumerable<string> ExtractDeclarations(
        string css,
        bool inlineStyle = false)
    {
        int blockDepth = inlineStyle ? 1 : 0;
        bool inDeclarationValue = false;
        bool inAtRulePrelude = false;
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
            switch (css[index])
            {
                case '@' when !inDeclarationValue:
                    inAtRulePrelude = true;
                    index++;
                    continue;
                case '{':
                    blockDepth++;
                    inDeclarationValue = false;
                    inAtRulePrelude = false;
                    index++;
                    continue;
                case '}':
                    blockDepth = Math.Max(0, blockDepth - 1);
                    inDeclarationValue = false;
                    inAtRulePrelude = false;
                    index++;
                    continue;
                case ';':
                    inDeclarationValue = false;
                    inAtRulePrelude = false;
                    index++;
                    continue;
                case ':' when blockDepth > 0 && !inAtRulePrelude:
                    inDeclarationValue = true;
                    index++;
                    continue;
            }
            if (!inDeclarationValue)
            {
                index++;
                continue;
            }

            int identifierLength = 0;
            bool isUrl = true;
            while (TryReadIdentifierCharacter(css, ref index,
                out char character))
            {
                isUrl &= identifierLength < 3 &&
                    char.ToLowerInvariant(character) == "url"[identifierLength];
                identifierLength++;
            }
            if (identifierLength == 0)
            {
                index++;
                continue;
            }
            if (!isUrl || identifierLength != 3)
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

    private static bool TryReadIdentifierCharacter(
        string css,
        ref int index,
        out char character)
    {
        if (index >= css.Length)
        {
            character = default;
            return false;
        }
        if (IsIdentifierCharacter(css[index]))
        {
            character = css[index++];
            return true;
        }
        if (css[index] != '\\' || index + 1 >= css.Length ||
            css[index + 1] is '\r' or '\n' or '\f')
        {
            character = default;
            return false;
        }
        index++;
        int value = 0;
        int digits = 0;
        while (index < css.Length && digits < 6 &&
            Uri.IsHexDigit(css[index]))
        {
            value = value * 16 + HexValue(css[index++]);
            digits++;
        }
        if (digits == 0)
        {
            character = css[index++];
            return true;
        }
        if (index < css.Length && char.IsWhiteSpace(css[index]))
        {
            index++;
        }
        character = value <= char.MaxValue ? (char)value : '\0';
        return true;
    }

    private static int HexValue(char character) =>
        character <= '9' ? character - '0' :
        char.ToLowerInvariant(character) - 'a' + 10;

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
