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
            return JsonSerializer.Serialize(new HtmlSessionDiagnosticsJson(
                guideId.ToString("N"),
                served.ToArray(),
                denied
                    .OrderBy(pair => pair.Key.Reason.ToString(), StringComparer.Ordinal)
                    .ThenBy(pair => pair.Key.Context, StringComparer.Ordinal)
                    .Select(pair => new HtmlDeniedJson(pair.Key.Reason.ToString(), pair.Key.Context, pair.Value))
                    .ToArray(),
                rejectedCaptures,
                new Dictionary<string, int>(restores),
                entryNavigations,
                new HtmlAppearanceJson(
                    appearanceTheme,
                    appearanceScale,
                    appearanceApplications,
                    appearanceFailures,
                    appearanceComputed?.BodyBackground,
                    appearanceComputed?.BodyColor,
                    appearanceComputed?.RootZoom)),
                CoreJsonContext.Default.HtmlSessionDiagnosticsJson);
        }
    }
}

internal sealed record HtmlSessionDiagnosticsJson(
    string GuideId, string[] Served, HtmlDeniedJson[] Denied, int RejectedCaptures,
    Dictionary<string, int> Restores, int EntryNavigations, HtmlAppearanceJson Appearance);

internal sealed record HtmlDeniedJson(string Reason, string Context, int Count);

internal sealed record HtmlAppearanceJson(
    string? Theme, double? Scale, int Applications, int Failures,
    string? BodyBackground, string? BodyColor, string? RootZoom);
