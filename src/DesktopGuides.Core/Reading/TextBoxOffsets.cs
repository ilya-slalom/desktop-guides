namespace DesktopGuides.Core.Reading;

// T18.1: WinUI's TextBox keeps each line break as one '\r', so a "\r\n"
// before an offset is one character fewer in the box.
public static class TextBoxOffsets
{
    public static (int Start, int Length) Map(string text, TextMatch match)
    {
        ArgumentNullException.ThrowIfNull(text);
        int start = ToTextBox(text, match.Start);
        return (start, ToTextBox(text, match.Start + match.Length) - start);
    }

    private static int ToTextBox(string text, int offset)
    {
        int pairs = 0;
        for (int i = 0; i + 1 < offset && i + 1 < text.Length; i++)
        {
            if (text[i] == '\r' && text[i + 1] == '\n') pairs++;
        }
        return offset - pairs;
    }
}
