using System.Text.Json;
using DesktopGuides.Core.Pdf;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfSessionDiagnosticsTests
{
    [Fact]
    public void JsonHoldsOnlyTheCounts()
    {
        PdfSessionDiagnostics diagnostics = new(600, 41, 3, 90_000_000, 100_663_296, 17, 8, 280, true, 12);

        using JsonDocument json = JsonDocument.Parse(diagnostics.ToJson());
        JsonElement root = json.RootElement;

        Assert.Equal(
            ["requests", "loads", "staleResults", "peakCacheBytes", "maxCacheBytes",
             "cachedPagesAtClose", "peakTextPages", "peakTextCharacters", "disposedCleanly", "evictions"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(600, root.GetProperty("requests").GetInt32());
        Assert.Equal(41, root.GetProperty("loads").GetInt32());
        Assert.Equal(3, root.GetProperty("staleResults").GetInt32());
        Assert.Equal(90_000_000, root.GetProperty("peakCacheBytes").GetInt64());
        Assert.Equal(100_663_296, root.GetProperty("maxCacheBytes").GetInt64());
        Assert.Equal(17, root.GetProperty("cachedPagesAtClose").GetInt32());
        Assert.Equal(8, root.GetProperty("peakTextPages").GetInt32());
        Assert.Equal(280, root.GetProperty("peakTextCharacters").GetInt64());
        Assert.True(root.GetProperty("disposedCleanly").GetBoolean());
        Assert.Equal(12, root.GetProperty("evictions").GetInt32());
    }
}
