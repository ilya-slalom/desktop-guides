using System.Text.Json;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Html;

/// <summary>
/// T18.1: an HTML page's searchable text, as the find script reads it: the
/// position walk's text, so offsets are locator offsets. Hidden text is
/// masked, and the matcher skips it, as it renders; breaks are block edges.
/// </summary>
public sealed record HtmlFindText(string Text, IReadOnlyList<int> Breaks)
{
    public static HtmlFindText? Parse(string? json)
    {
        if (json is null) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("text", out JsonElement textValue) || textValue.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("breaks", out JsonElement breaksValue) || breaksValue.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("hidden", out JsonElement hiddenValue) || hiddenValue.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            char[] text = textValue.GetString()!.ToCharArray();
            List<int> breaks = [];
            foreach (JsonElement item in breaksValue.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out int at) ||
                    at < 0 || at > text.Length || (breaks.Count > 0 && at < breaks[^1]))
                {
                    return null;
                }
                breaks.Add(at);
            }
            foreach (JsonElement item in hiddenValue.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2 ||
                    item[0].ValueKind != JsonValueKind.Number || item[1].ValueKind != JsonValueKind.Number ||
                    !item[0].TryGetInt32(out int start) || !item[1].TryGetInt32(out int end) ||
                    start < 0 || end < start || end > text.Length)
                {
                    return null;
                }
                Array.Fill(text, TextMatcher.Masked, start, end - start);
            }
            return new HtmlFindText(new string(text), breaks);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
