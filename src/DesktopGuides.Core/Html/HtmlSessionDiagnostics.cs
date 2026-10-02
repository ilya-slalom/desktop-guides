using System.Text.Json;

namespace DesktopGuides.Core.Html;

/// <summary>
/// Test-only counts of one session's decisions. Holds request paths and
/// reasons, never external URLs or guide text.
/// </summary>
public sealed class HtmlSessionDiagnostics
{
    private readonly object gate = new();
    private readonly SortedSet<string> served = new(StringComparer.Ordinal);
    private readonly Dictionary<(HtmlDenyReason Reason, string Context), int> denied = [];

    public void RecordServed(string requestPath)
    {
        lock (gate) served.Add(requestPath);
    }

    public void RecordDenied(HtmlDenyReason reason, string context)
    {
        lock (gate) denied[(reason, context)] = denied.GetValueOrDefault((reason, context)) + 1;
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
                    .ToArray()
            });
        }
    }
}
