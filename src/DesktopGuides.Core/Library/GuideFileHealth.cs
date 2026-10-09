namespace DesktopGuides.Core.Library;

public enum GuideFileStatus
{
    Ok,
    Missing,
    Damaged
}

public sealed record MissingGuideFile(Guid GuideId, Guid GameId);

// What this session knows about each guide's managed file: startup reports
// the missing ones, and opening a guide marks it damaged or clears it. Each
// entry keeps its game so the Library can count guides that need attention.
public sealed class GuideFileHealth
{
    private readonly Dictionary<Guid, (Guid GameId, GuideFileStatus Status)> entries = [];

    public GuideFileStatus this[Guid guideId] =>
        entries.TryGetValue(guideId, out var entry) ? entry.Status : GuideFileStatus.Ok;

    public void Mark(Guid guideId, Guid gameId, GuideFileStatus status)
    {
        if (status == GuideFileStatus.Ok)
        {
            entries.Remove(guideId);
            return;
        }
        entries[guideId] = (gameId, status);
    }

    public void Forget(Guid guideId) => entries.Remove(guideId);

    public int CountForGame(Guid gameId) => entries.Values.Count(entry => entry.GameId == gameId);

    public void Reset(IEnumerable<MissingGuideFile> missing)
    {
        entries.Clear();
        foreach (MissingGuideFile file in missing)
        {
            entries[file.GuideId] = (file.GameId, GuideFileStatus.Missing);
        }
    }
}

public static class GuideFilePresentation
{
    public static string StatusLabel(GuideFileStatus status) => status switch
    {
        GuideFileStatus.Missing => "File missing",
        GuideFileStatus.Damaged => "File damaged",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static string Attention(int count) =>
        count == 1 ? "1 guide needs attention" : $"{count} guides need attention";

    public static string Unreadable(int count) => count == 1
        ? "1 guide record couldn't be read and is hidden. Other guides open normally."
        : $"{count} guide records couldn't be read and are hidden. Other guides open normally.";
}
