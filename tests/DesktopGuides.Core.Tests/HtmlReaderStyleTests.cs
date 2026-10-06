using System.Globalization;
using System.Text.Json;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlReaderStyleTests
{
    [Theory]
    [InlineData(0.74, 0.75)]
    [InlineData(0.75, 0.75)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.5)]
    [InlineData(2.0, 2.0)]
    [InlineData(2.01, 2.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 1.0)]
    public void ClampScaleBoundsTheStoredScale(double stored, double expected) =>
        Assert.Equal(expected, HtmlReaderStyle.ClampScale(stored));

    [Fact]
    public void LightOnlySetsZeroSpecificityDefaults() =>
        Assert.Equal(
            "html { zoom: 1 !important; }\n" +
            ":where(html) { color-scheme: light; background-color: #FFFFFF; color: #000000; }",
            HtmlReaderStyle.Css(ReaderTheme.Light, 1.0));

    [Fact]
    public void DarkForcesThePaletteAndKeepsImages() =>
        Assert.Equal(
            "html { zoom: 1.5 !important; }\n" +
            ":root { color-scheme: dark !important; }\n" +
            "html, body { background-color: #1E1E1E !important; color: #E6E6E6 !important; }\n" +
            "body * { background-color: transparent !important; color: inherit !important; " +
            "border-color: #5A5A5A !important; text-shadow: none !important; }\n" +
            "a:link, a:visited, a:link *, a:visited * { color: #8AB4F8 !important; }\n" +
            "mark { background-color: #5C4B00 !important; }",
            HtmlReaderStyle.Css(ReaderTheme.Dark, 1.5));

    [Fact]
    public void HighContrastOnlyScales() =>
        Assert.Equal("html { zoom: 0.75 !important; }", HtmlReaderStyle.Css(ReaderTheme.HighContrast, 0.75));

    [Fact]
    public void CssClampsTheScale() =>
        Assert.StartsWith("html { zoom: 2 !important; }", HtmlReaderStyle.Css(ReaderTheme.Dark, 9));

    [Fact]
    public void ZoomIgnoresTheCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.StartsWith("html { zoom: 1.25 !important; }", HtmlReaderStyle.Css(ReaderTheme.Light, 1.25));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(ReaderTheme.Light)]
    [InlineData(ReaderTheme.Dark)]
    [InlineData(ReaderTheme.HighContrast)]
    public void NoThemeCanMakeARequest(ReaderTheme theme)
    {
        string css = HtmlReaderStyle.Css(theme, 1.0);
        foreach (string token in new[] { "url(", "@import", "@font-face", "font-family" })
        {
            Assert.DoesNotContain(token, css, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(ReaderTheme.Light, "#FFFFFF")]
    [InlineData(ReaderTheme.Dark, "#1E1E1E")]
    [InlineData(ReaderTheme.HighContrast, null)]
    public void PageColorMatchesThePage(ReaderTheme theme, string? expected) =>
        Assert.Equal(expected, HtmlReaderStyle.PageColor(theme));

    [Fact]
    public void WriteScriptCarriesTheCssAsAJsonLiteral()
    {
        string script = HtmlReaderStyle.WriteScript(ReaderTheme.Dark, 1.5);
        string literal = JsonSerializer.Serialize(HtmlReaderStyle.Css(ReaderTheme.Dark, 1.5));

        Assert.Equal(
            "(() => { const css = " + literal + "; " +
            "let style = document.querySelector(\"style#desktop-guides-style\"); " +
            "if (!style) { style = document.createElement(\"style\"); style.id = \"desktop-guides-style\"; " +
            "(document.head || document.documentElement).appendChild(style); } " +
            "style.textContent = css; return true; })()",
            script);
    }

    [Fact]
    public void ParseAppliedReadsTheComputedValues() =>
        Assert.Equal(
            new HtmlAppliedStyle("rgb(30, 30, 30)", "rgb(230, 230, 230)", "1.5"),
            HtmlReaderStyle.ParseApplied(
                """{"bodyBackground":"rgb(30, 30, 30)","bodyColor":"rgb(230, 230, 230)","rootZoom":"1.5"}"""));

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("not json")]
    [InlineData("""{"bodyBackground":"rgb(0, 0, 0)","bodyColor":"rgb(0, 0, 0)"}""")]
    [InlineData("""{"bodyBackground":1,"bodyColor":"rgb(0, 0, 0)","rootZoom":"1"}""")]
    public void ParseAppliedRejectsAnythingElse(string? reply) =>
        Assert.Null(HtmlReaderStyle.ParseApplied(reply));
}
