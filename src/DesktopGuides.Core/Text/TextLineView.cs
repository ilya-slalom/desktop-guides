using System.Text;

namespace DesktopGuides.Core.Text;

/// <summary>How one TXT guide line is shown: a tab expands to the next
/// 8-column stop and a C0 control character other than tab, or DEL, shows
/// as one space. A column is one UTF-16 code unit. The document's text and
/// offsets are never changed.</summary>
public static class TextLineView
{
    public const int TabWidth = 8;

    public static string DisplayText(TextGuideDocument document, int line)
    {
        (int start, int end) = Bounds(document, line);
        ReadOnlySpan<char> source = document.Text.AsSpan(start, end - start);
        if (source.IndexOfAnyInRange('\0', '\x1F') < 0 && !source.Contains('\x7F'))
        {
            return source.ToString();
        }
        StringBuilder shown = new(DisplayColumns(document, line));
        foreach (char character in source)
        {
            if (character == '\t')
            {
                shown.Append(' ', TabWidth - shown.Length % TabWidth);
            }
            else
            {
                shown.Append(character < ' ' || character == '\x7F' ? ' ' : character);
            }
        }
        return shown.ToString();
    }

    public static int DisplayColumns(TextGuideDocument document, int line)
    {
        (int start, int end) = Bounds(document, line);
        int columns = 0;
        for (int index = start; index < end; index++)
        {
            columns += Width(document.Text[index], columns);
        }
        return columns;
    }

    /// <summary>The offset in <see cref="TextGuideDocument.Text"/> shown at
    /// <paramref name="column"/>, clamped to the line. A column inside an
    /// expanded tab maps to the tab; a column past the end maps to the
    /// line's end.</summary>
    public static int SourceOffset(TextGuideDocument document, int line, int column)
    {
        (int start, int end) = Bounds(document, line);
        int columns = 0;
        for (int index = start; index < end; index++)
        {
            columns += Width(document.Text[index], columns);
            if (column < columns)
            {
                return index;
            }
        }
        return end;
    }

    private static int Width(char character, int column) =>
        character == '\t' ? TabWidth - column % TabWidth : 1;

    private static (int Start, int End) Bounds(TextGuideDocument document, int line)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyList<int> starts = document.LineStarts;
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, starts.Count);
        int end = line + 1 < starts.Count ? starts[line + 1] - 1 : document.Text.Length;
        return (starts[line], end);
    }
}
