using System.Globalization;
using System.Text.Json;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Html;

public sealed record HtmlAppliedStyle(string BodyBackground, string BodyColor, string RootZoom);

/// <summary>
/// The app's fixed style for HTML guides. It's built from constants and a
/// clamped scale only, so no page data enters it, and it names no URL or
/// font, so it can't make a request.
/// </summary>
public static class HtmlReaderStyle
{
    public const string ElementId = "desktop-guides-style";
    public const double MinScale = 0.75;
    public const double MaxScale = 2.0;

    // Zero specificity: any rule the page sets wins.
    private const string Light =
        ":where(html) { color-scheme: light; background-color: #FFFFFF; color: #000000; }";

    // Background images, media, layout and fonts stay as authored.
    private const string Dark =
        ":root { color-scheme: dark !important; }\n" +
        "html, body { background-color: #1E1E1E !important; color: #E6E6E6 !important; }\n" +
        "body * { background-color: transparent !important; color: inherit !important; " +
        "border-color: #5A5A5A !important; text-shadow: none !important; }\n" +
        "a:link, a:visited, a:link *, a:visited * { color: #8AB4F8 !important; }\n" +
        "mark { background-color: #5C4B00 !important; }";

    // A stored scale can predate the range check; a bad one reads as 1.
    public static double ClampScale(double scale) =>
        double.IsFinite(scale) ? Math.Clamp(scale, MinScale, MaxScale) : 1.0;

    // High contrast sets no colors: WebView2 applies Windows' forced colors.
    public static string Css(ReaderTheme theme, double scale)
    {
        string zoom = "html { zoom: " +
            ClampScale(scale).ToString("0.###", CultureInfo.InvariantCulture) + " !important; }";
        return theme switch
        {
            ReaderTheme.Light => zoom + "\n" + Light,
            ReaderTheme.Dark => zoom + "\n" + Dark,
            _ => zoom
        };
    }

    // The WebView's color before the first paint; null keeps its default.
    public static string? PageColor(ReaderTheme theme) => theme switch
    {
        ReaderTheme.Light => "#FFFFFF",
        ReaderTheme.Dark => "#1E1E1E",
        _ => null
    };

    // Creates the element on the first write and rewrites it after that.
    // The lookup names the tag, so a page element that has the same id is
    // never emptied.
    public static string WriteScript(ReaderTheme theme, double scale) =>
        "(() => { const css = " + JsonSerializer.Serialize(Css(theme, scale)) + "; " +
        "let style = document.querySelector(\"style#" + ElementId + "\"); " +
        "if (!style) { style = document.createElement(\"style\"); style.id = \"" + ElementId + "\"; " +
        "(document.head || document.documentElement).appendChild(style); } " +
        "style.textContent = css; return true; })()";

    // Test diagnostics: what the page computed after a write.
    public const string ReadbackScript =
        "(() => { const body = getComputedStyle(document.body || document.documentElement); " +
        "const root = getComputedStyle(document.documentElement); " +
        "return { bodyBackground: body.backgroundColor, bodyColor: body.color, rootZoom: root.zoom }; })()";

    public static HtmlAppliedStyle? ParseApplied(string? json)
    {
        if (json is null) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   Text(root, "bodyBackground") is string background &&
                   Text(root, "bodyColor") is string color &&
                   Text(root, "rootZoom") is string zoom
                ? new HtmlAppliedStyle(background, color, zoom)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
