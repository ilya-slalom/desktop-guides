namespace DesktopGuides.Core.Text;

public static class TextLineMetrics
{
    private const int LinesPerCheck = 4096;

    /// <summary>The widest line in display columns. Scans every line, so
    /// callers run it off the UI thread.</summary>
    public static int MaxColumns(TextGuideDocument document, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(document);
        int widest = 0;
        int lines = document.LineStarts.Count;
        for (int line = 0; line < lines; line++)
        {
            if (line % LinesPerCheck == 0)
            {
                token.ThrowIfCancellationRequested();
            }
            widest = Math.Max(widest, TextLineView.DisplayColumns(document, line));
        }
        return widest;
    }
}
