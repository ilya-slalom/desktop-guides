namespace DesktopGuides.Infrastructure.Storage;

/// <summary>
/// Test gates only: a diagnostic file written to a temporary file and
/// moved, so installed tests never read half a file.
/// </summary>
public static class DiagnosticFile
{
    // On Windows the replace fails while a test has the file open to read
    // it. A dropped write would leave the file stale until the next one,
    // which may never come, so the replace is tried again for a moment.
    private const int Attempts = 20;
    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(25);

    public static void Write(string path, string contents) => Write(path, contents, beforeRetry: null);

    // Tests release the block here instead of racing the retries.
    internal static void Write(string path, string contents, Action? beforeRetry)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, contents);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception error) when (attempt < Attempts && error is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(Wait);
                beforeRetry?.Invoke();
            }
        }
    }
}
