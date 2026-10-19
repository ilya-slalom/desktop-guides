using DesktopGuides.Core.Maps;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class MapSessionDiagnosticsTests
{
    [Fact]
    public void MapDiagnosticsKeepTheirFieldNames()
    {
        string json = new MapSessionDiagnostics(420, true, false, true, 1, 12, 2048, 12_582_912, 100_663_296, 0.25, 0.09, 7728, 5152).ToJson();
        Assert.Contains("\"openMs\":420", json);
        Assert.Contains("\"tilesReady\":true", json);
        Assert.Contains("\"maxSurfaceSide\":2048", json);
        Assert.Contains("\"centerY\":5152", json);
    }
}
