using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Storage;

namespace DesktopGuides.Production;

// Test gate only (#102): the stages the latest guide open reached. The
// installer holds one gate for its whole run, so a guide reopened at startup
// is recorded too. Opens run on the UI thread, one at a time.
internal static class HtmlOpenTimelineForTest
{
    private static readonly HtmlOpenTimeline timeline = new(() => Environment.TickCount64);
    private static string? path;

    public static void Begin(string? cacheRoot)
    {
        path = cacheRoot is not null && TestGate.IsOpen(AppLane.Current.LocalEvent("HtmlOpenTimeline"))
            ? Path.Combine(cacheRoot, "diagnostics", $"html-open-{Environment.ProcessId}.json")
            : null;
        if (path is null) return;
        timeline.Begin();
        Mark("route");
    }

    public static bool IsRecording => path is not null;

    public static void Mark(string stage, string? detail = null)
    {
        if (path is null) return;
        timeline.Mark(stage, detail);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            DiagnosticFile.Write(path, timeline.ToJson());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Test diagnostics must never affect reading.
        }
    }
}
