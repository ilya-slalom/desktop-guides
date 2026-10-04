using System.Text.Json;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Reading;
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

    [Fact]
    public void CountsRejectedCapturesAndRestoresByKind()
    {
        HtmlSessionDiagnostics diagnostics = new();
        diagnostics.RecordRejectedCapture();
        diagnostics.RecordRejectedCapture();
        diagnostics.RecordRestore(RestoreKind.Exact);
        diagnostics.RecordRestore(RestoreKind.Exact);
        diagnostics.RecordRestore(RestoreKind.Approximate);
        diagnostics.RecordDenied(HtmlDenyReason.UnimportedPage, "Navigation");

        using JsonDocument json = JsonDocument.Parse(diagnostics.ToJson(Guid.NewGuid()));

        Assert.Equal(2, json.RootElement.GetProperty("rejectedCaptures").GetInt32());
        JsonElement restores = json.RootElement.GetProperty("restores");
        Assert.Equal(["Approximate", "Exact"], restores.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, restores.GetProperty("Exact").GetInt32());
        Assert.Equal(1, restores.GetProperty("Approximate").GetInt32());
        JsonElement denied = json.RootElement.GetProperty("denied")[0];
        Assert.Equal("UnimportedPage", denied.GetProperty("reason").GetString());
        Assert.Equal("Navigation", denied.GetProperty("context").GetString());
    }

    [Fact]
    public void ANewSessionHasNoPositionCounts()
    {
        using JsonDocument json = JsonDocument.Parse(new HtmlSessionDiagnostics().ToJson(Guid.NewGuid()));
        Assert.Equal(0, json.RootElement.GetProperty("rejectedCaptures").GetInt32());
        Assert.Empty(json.RootElement.GetProperty("restores").EnumerateObject());
    }
}
