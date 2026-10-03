using System.Text.Json;
using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlSessionDiagnosticsTests
{
    [Fact]
    public void CountsDistinctServedPathsAndDeniedReasonsByContext()
    {
        Guid id = Guid.NewGuid();
        HtmlSessionDiagnostics diagnostics = new();
        diagnostics.RecordServed("styles/main.css");
        diagnostics.RecordServed("guide.html");
        diagnostics.RecordServed("guide.html");
        diagnostics.RecordDenied(HtmlDenyReason.External, "Image");
        diagnostics.RecordDenied(HtmlDenyReason.External, "Image");
        diagnostics.RecordDenied(HtmlDenyReason.CrossGuide, "Image");

        using JsonDocument json = JsonDocument.Parse(diagnostics.ToJson(id));

        Assert.Equal(id.ToString("N"), json.RootElement.GetProperty("guideId").GetString());
        Assert.Equal(["guide.html", "styles/main.css"],
            json.RootElement.GetProperty("served").EnumerateArray().Select(e => e.GetString()));
        JsonElement[] denied = [.. json.RootElement.GetProperty("denied").EnumerateArray()];
        Assert.Equal(2, denied.Length);
        Assert.Equal(("CrossGuide", "Image", 1),
            (denied[0].GetProperty("reason").GetString(), denied[0].GetProperty("context").GetString(),
             denied[0].GetProperty("count").GetInt32()));
        Assert.Equal(("External", "Image", 2),
            (denied[1].GetProperty("reason").GetString(), denied[1].GetProperty("context").GetString(),
             denied[1].GetProperty("count").GetInt32()));
    }
}
