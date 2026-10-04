using System.Text.Json;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Html;

/// <summary>
/// Test-only counts of one session's decisions. Holds request paths, reasons
/// and counts, never external URLs or guide text.
/// </summary>
public sealed class HtmlSessionDiagnostics
{
    private readonly object gate = new();
    private readonly SortedSet<string> served = new(StringComparer.Ordinal);
    private readonly Dictionary<(HtmlDenyReason Reason, string Context), int> denied = [];
    private readonly SortedDictionary<string, int> restores = new(StringComparer.Ordinal);
    private int rejectedCaptures;

    public void RecordServed(string requestPath)
    {
        lock (gate) served.Add(requestPath);
    }

    public void RecordDenied(HtmlDenyReason reason, string context)
    {
        lock (gate) denied[(reason, context)] = denied.GetValueOrDefault((reason, context)) + 1;
    }

    public void RecordRejectedCapture()
    {
        lock (gate) rejectedCaptures++;
    }

    public void RecordRestore(RestoreKind kind)
    {
        lock (gate) restores[kind.ToString()] = restores.GetValueOrDefault(kind.ToString()) + 1;
    }

    public string ToJson(Guid guideId)
    {
        lock (gate)
        {
            return JsonSerializer.Serialize(new
            {
                guideId = guideId.ToString("N"),
                served = served.ToArray(),
                denied = denied
                    .OrderBy(pair => pair.Key.Reason.ToString(), StringComparer.Ordinal)
                    .ThenBy(pair => pair.Key.Context, StringComparer.Ordinal)
                    .Select(pair => new { reason = pair.Key.Reason.ToString(), context = pair.Key.Context, count = pair.Value })
                    .ToArray(),
                rejectedCaptures,
                restores = new Dictionary<string, int>(restores)
            });
        }
    }
}
