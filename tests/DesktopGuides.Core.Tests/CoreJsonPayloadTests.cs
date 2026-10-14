using DesktopGuides.Core.Html;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

// T17.4: the exact JSON each payload has today, so moving to source
// generation can't change a byte.
public sealed class CoreJsonPayloadTests
{
    private static readonly Guid Id = Guid.Parse("0123456789abcdef0123456789abcdef");

    [Fact]
    public void PdfDiagnosticsJsonIsUnchanged()
    {
        PdfSessionDiagnostics diagnostics = new(600, 41, 3, 90_000_000, 100_663_296, 17, 8, 280, true, 12, true);

        Assert.Equal(
            """{"requests":600,"loads":41,"staleResults":3,"peakCacheBytes":90000000,"maxCacheBytes":100663296,"cachedPagesAtClose":17,"peakTextPages":8,"peakTextCharacters":280,"disposedCleanly":true,"evictions":12,"abandonedExtraction":true}""",
            diagnostics.ToJson());
    }

    [Fact]
    public void EmptyHtmlDiagnosticsJsonIsUnchanged()
    {
        Assert.Equal(
            """{"guideId":"0123456789abcdef0123456789abcdef","served":[],"denied":[],"rejectedCaptures":0,"restores":{},"entryNavigations":0,"appearance":{"theme":null,"scale":null,"applications":0,"failures":0,"bodyBackground":null,"bodyColor":null,"rootZoom":null}}""",
            new HtmlSessionDiagnostics().ToJson(Id));
    }

    [Fact]
    public void HtmlDiagnosticsJsonIsUnchanged()
    {
        HtmlSessionDiagnostics diagnostics = new();
        diagnostics.RecordServed("/b.html");
        diagnostics.RecordServed("/a.html");
        diagnostics.RecordDenied(HtmlDenyReason.External, "Navigation");
        diagnostics.RecordDenied(HtmlDenyReason.External, "Navigation");
        diagnostics.RecordRejectedCapture();
        diagnostics.RecordRestore(RestoreKind.Exact);
        diagnostics.RecordEntryNavigation();
        diagnostics.RecordAppearance("dark", 1.5, new HtmlAppliedStyle("rgb(0, 0, 0)", "rgb(255, 255, 255)", "1.5"));

        Assert.Equal(
            """{"guideId":"0123456789abcdef0123456789abcdef","served":["/a.html","/b.html"],"denied":[{"reason":"External","context":"Navigation","count":2}],"rejectedCaptures":1,"restores":{"Exact":1},"entryNavigations":1,"appearance":{"theme":"dark","scale":1.5,"applications":1,"failures":0,"bodyBackground":"rgb(0, 0, 0)","bodyColor":"rgb(255, 255, 255)","rootZoom":"1.5"}}""",
            diagnostics.ToJson(Id));
    }

    [Fact]
    public void FindArgsJsonIsUnchangedAndEscapesMarkup()
    {
        HtmlPosition position = new("index.html", "intro", "Chapter \"1\" <b>", 42, 0.25);

        Assert.Equal(
            """{"offset":42,"quote":"Chapter \u00221\u0022 \u003Cb\u003E","id":"intro"}""",
            HtmlLocationRules.FindArgs(position));
    }

    [Fact]
    public void FindArgsJsonKeepsNulls()
    {
        HtmlPosition position = new("index.html", null, null, 0, 0);

        Assert.Equal("""{"offset":0,"quote":null,"id":null}""", HtmlLocationRules.FindArgs(position));
    }
}
