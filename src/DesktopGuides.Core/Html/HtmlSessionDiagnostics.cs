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
    private int entryNavigations;
    private string? appearanceTheme;
    private double? appearanceScale;
    private int appearanceApplications;
    private int appearanceFailures;
    private HtmlAppliedStyle? appearanceComputed;

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

    public void RecordEntryNavigation()
    {
        lock (gate) entryNavigations++;
    }

    // computed is null when the readback after the write failed.
    public void RecordAppearance(string theme, double scale, HtmlAppliedStyle? computed)
    {
        lock (gate)
        {
            appearanceTheme = theme;
            appearanceScale = scale;
            appearanceApplications++;
            appearanceComputed = computed;
        }
    }

    public void RecordAppearanceFailed()
    {
        lock (gate) appearanceFailures++;
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
                restores = new Dictionary<string, int>(restores),
                entryNavigations,
                appearance = new
                {
                    theme = appearanceTheme,
                    scale = appearanceScale,
                    applications = appearanceApplications,
                    failures = appearanceFailures,
                    bodyBackground = appearanceComputed?.BodyBackground,
                    bodyColor = appearanceComputed?.BodyColor,
                    rootZoom = appearanceComputed?.RootZoom
                }
            });
        }
    }
}
