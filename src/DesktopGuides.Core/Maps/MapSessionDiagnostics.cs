using System.Text.Json;

namespace DesktopGuides.Core.Maps;

/// <summary>
/// T21.2: test-only facts from one map session. No paths and no titles.
/// MaxSurfaceSide is the largest side of any bitmap it decoded; OpenMs runs
/// from opening to the first frame with the map drawn.
/// </summary>
public sealed record MapSessionDiagnostics(
    long OpenMs,
    bool FromCache,
    bool Small,
    bool TilesReady,
    int Level,
    int TilesShown,
    int MaxSurfaceSide,
    long PeakTileBytes,
    long MaxTileBytes,
    double Zoom,
    double Fit,
    double CenterX,
    double CenterY)
{
    public string ToJson() => JsonSerializer.Serialize(this, CoreJsonContext.Default.MapSessionDiagnostics);
}
