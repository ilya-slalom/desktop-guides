using System.Text;

namespace DesktopGuides.Infrastructure.Import;

internal enum CssDeclarationReferenceKind
{
    Image,
    CustomPropertyImage,
    VariableUse
}

internal readonly record struct CssDeclarationReference(
    CssDeclarationReferenceKind Kind,
    string Value,
    string? CustomProperty);

/// <summary>
/// Reads image URLs and var() uses from CSS declaration values, including
/// nested rules that the CSS parser may omit. Function-name escapes are
/// decoded; target text stays unchanged for path checks.
/// </summary>
internal static class CssUrlReferences
{
    public static IEnumerable<CssDeclarationReference> ExtractDeclarations(
        string css,
        bool inlineStyle = false)
    {
        int blockDepth = inlineStyle ? 1 : 0;
        int parenthesisDepth = 0;
        int declarationStart = 0;
        bool inDeclarationValue = false;
        bool inAtRulePrelude = false;
        string? customProperty = null;
        Stack<ImageSetFrame> imageSets = new();
        for (int index = 0; index < css.Length;)
        {
            if (css[index] == '/' && index + 1 < css.Length &&
                css[index + 1] == '*')
            {
                int end = css.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? css.Length : end + 2;
                continue;
            }
            if (char.IsWhiteSpace(css[index]))
            {
                index++;
                continue;
            }
            if (css[index] is '\'' or '"')
            {
                int after = SkipString(css, index);
                if (inDeclarationValue &&
                    imageSets.TryPeek(out ImageSetFrame? imageSet) &&
                    imageSet.Depth == parenthesisDepth &&
                    imageSet.ExpectsImage &&
                    after > index + 1 &&
                    css[after - 1] == css[index])
                {
                    imageSet.ExpectsImage = false;
                    yield return Image(
                        css[(index + 1)..(after - 1)], customProperty);
                }
                index = after;
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
                    parenthesisDepth = 0;
                    imageSets.Clear();
                    index++;
                    declarationStart = index;
                    customProperty = null;
                    continue;
                case '}':
                    blockDepth = Math.Max(0, blockDepth - 1);
                    inDeclarationValue = false;
                    inAtRulePrelude = false;
                    parenthesisDepth = 0;
                    imageSets.Clear();
                    index++;
                    declarationStart = index;
                    customProperty = null;
                    continue;
                case ';' when parenthesisDepth == 0:
                    inDeclarationValue = false;
                    inAtRulePrelude = false;
                    index++;
                    declarationStart = index;
                    customProperty = null;
                    continue;
                case ':' when blockDepth > 0 && !inAtRulePrelude &&
                    !inDeclarationValue:
                    customProperty = ReadCustomPropertyName(
                        css, declarationStart, index);
                    inDeclarationValue = true;
                    index++;
                    continue;
                case '(':
                    MarkImageSlot(imageSets, parenthesisDepth);
                    parenthesisDepth++;
                    index++;
                    continue;
                case ')':
                    if (imageSets.TryPeek(out ImageSetFrame? closing) &&
                        closing.Depth == parenthesisDepth)
                    {
                        imageSets.Pop();
                    }
                    parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
                    index++;
                    continue;
                case ',':
                    if (imageSets.TryPeek(out ImageSetFrame? option) &&
                        option.Depth == parenthesisDepth)
                    {
                        option.ExpectsImage = true;
                    }
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
            bool isImageSet = true;
            bool isWebkitImageSet = true;
            bool isVar = true;
            while (TryReadIdentifierCharacter(css, ref index,
                out char character))
            {
                char lower = char.ToLowerInvariant(character);
                isUrl &= Matches("url", identifierLength, lower);
                isImageSet &= Matches("image-set", identifierLength, lower);
                isWebkitImageSet &= Matches(
                    "-webkit-image-set", identifierLength, lower);
                isVar &= Matches("var", identifierLength, lower);
                identifierLength++;
            }
            if (identifierLength == 0)
            {
                MarkImageSlot(imageSets, parenthesisDepth);
                index++;
                continue;
            }
            MarkImageSlot(imageSets, parenthesisDepth);
            if ((!isUrl || identifierLength != 3) &&
                (!isImageSet || identifierLength != 9) &&
                (!isWebkitImageSet || identifierLength != 17) &&
                (!isVar || identifierLength != 3))
            {
                continue;
            }
            int cursor = SkipTrivia(css, index);
            if (cursor >= css.Length || css[cursor] != '(')
            {
                index = cursor;
                continue;
            }
            if (isUrl && identifierLength == 3)
            {
                bool valid = TryReadArgument(
                    css, cursor + 1, out string? argument, out int after);
                index = after;
                if (valid && !string.IsNullOrWhiteSpace(argument))
                {
                    yield return Image(argument, customProperty);
                }
                continue;
            }
            if (isVar && identifierLength == 3)
            {
                string? name = ReadVariableName(css, cursor + 1);
                if (name is not null)
                {
                    yield return new CssDeclarationReference(
                        CssDeclarationReferenceKind.VariableUse,
                        name, customProperty);
                }
                parenthesisDepth++;
                index = cursor + 1;
                continue;
            }
            parenthesisDepth++;
            imageSets.Push(new ImageSetFrame(parenthesisDepth));
            index = cursor + 1;
        }
    }

    private static CssDeclarationReference Image(
        string target,
        string? customProperty) => new(
            customProperty is null
                ? CssDeclarationReferenceKind.Image
                : CssDeclarationReferenceKind.CustomPropertyImage,
            target, customProperty);

    private static string? ReadCustomPropertyName(
        string css,
        int start,
        int colon)
    {
        int cursor = SkipTrivia(css, start);
        if (cursor >= colon || css[cursor] is not ('-' or '\\'))
        {
            return null;
        }
        string? name = ReadIdentifier(css, ref cursor);
        return name is not null && name.StartsWith("--", StringComparison.Ordinal) &&
            name.Length > 2 && SkipTrivia(css, cursor) == colon
            ? name : null;
    }

    private static string? ReadVariableName(string css, int start)
    {
        int cursor = SkipTrivia(css, start);
        if (cursor >= css.Length || css[cursor] is not ('-' or '\\'))
        {
            return null;
        }
        string? name = ReadIdentifier(css, ref cursor);
        if (name is null || !name.StartsWith("--", StringComparison.Ordinal) ||
            name.Length < 3)
        {
            return null;
        }
        cursor = SkipTrivia(css, cursor);
        return cursor < css.Length && (css[cursor] is ',' or ')')
            ? name : null;
    }

    private static string? ReadIdentifier(string css, ref int cursor)
    {
        StringBuilder name = new();
        while (TryReadIdentifierCharacter(css, ref cursor,
            out char character))
        {
            name.Append(character);
        }
        return name.Length > 0 ? name.ToString() : null;
    }

    private sealed class ImageSetFrame(int depth)
    {
        public int Depth { get; } = depth;
        public bool ExpectsImage { get; set; } = true;
    }

    private static bool Matches(string expected, int index, char actual) =>
        index < expected.Length && expected[index] == actual;

    private static void MarkImageSlot(
        Stack<ImageSetFrame> imageSets,
        int parenthesisDepth)
    {
        if (imageSets.TryPeek(out ImageSetFrame? imageSet) &&
            imageSet.Depth == parenthesisDepth)
        {
            imageSet.ExpectsImage = false;
        }
    }

    private static int SkipTrivia(string css, int index)
    {
        while (index < css.Length)
        {
            if (char.IsWhiteSpace(css[index]))
            {
                index++;
            }
            else if (css[index] == '/' && index + 1 < css.Length &&
                css[index + 1] == '*')
            {
                int end = css.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? css.Length : end + 2;
            }
            else
            {
                break;
            }
        }
        return index;
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
                after = SkipString(css, cursor);
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
