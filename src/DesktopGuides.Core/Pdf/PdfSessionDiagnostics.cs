using System.Text.Json;

namespace DesktopGuides.Core.Pdf;

/// <summary>
/// Test-only counts from one PDF session. No guide text and no paths.
/// </summary>
public sealed record PdfSessionDiagnostics(
    int Requests,
    int Loads,
    int StaleResults,
    long PeakCacheBytes,
    long MaxCacheBytes,
    int CachedPagesAtClose,
    int PeakTextPages,
    long PeakTextCharacters,
    bool DisposedCleanly,
    int Evictions,
    bool AbandonedExtraction)
{
    public string ToJson() => JsonSerializer.Serialize(new
    {
        requests = Requests,
        loads = Loads,
        staleResults = StaleResults,
        peakCacheBytes = PeakCacheBytes,
        maxCacheBytes = MaxCacheBytes,
        cachedPagesAtClose = CachedPagesAtClose,
        peakTextPages = PeakTextPages,
        peakTextCharacters = PeakTextCharacters,
        disposedCleanly = DisposedCleanly,
        evictions = Evictions,
        abandonedExtraction = AbandonedExtraction
    });
}
