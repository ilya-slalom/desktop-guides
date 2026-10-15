using System.Text.Json;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Html;

/// <summary>
/// T18.2: an HTML page's contents from the collect script's reply. The
/// page's own contents list (three or more same-page links in one container,
/// each to a different place further down) wins over its headings; each
/// entry is the HTML position the reader already restores. A references or
/// "Back to top" list points back up, or to one place, so it never counts.
/// </summary>
public static class HtmlOutlineRules
{
    public const int MinimumListLinks = 3;

    private sealed record Place(int Offset, string? Quote, string? Id, double Fraction);
    private sealed record Heading(int Level, string Text, Place Place);
    private sealed record Link(string Text, int Offset, int Container, int Depth, Place? Target);

    public static Outline Build(string? json, string contentSha256, string documentPath)
    {
        if (json is null) return Outline.Empty;
        List<Heading> headings = [];
        List<Link> links = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("headings", out JsonElement headingList) || headingList.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("links", out JsonElement linkList) || linkList.ValueKind != JsonValueKind.Array)
            {
                return Outline.Empty;
            }
            foreach (JsonElement item in headingList.EnumerateArray())
            {
                headings.Add(new Heading(item.GetProperty("level").GetInt32(), item.GetProperty("text").GetString() ?? "",
                    ReadPlace(item)));
            }
            foreach (JsonElement item in linkList.EnumerateArray())
            {
                JsonElement target = item.GetProperty("target");
                links.Add(new Link(item.GetProperty("text").GetString() ?? "", item.GetProperty("offset").GetInt32(),
                    item.GetProperty("container").GetInt32(), item.GetProperty("depth").GetInt32(),
                    target.ValueKind == JsonValueKind.Null ? null : ReadPlace(target)));
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // Our own script's reply in the wrong shape: no outline.
            return Outline.Empty;
        }

        // Forward links only, and each place once: a contents list leads down
        // the page to different sections.
        List<Link>? list = links.Where(link => link.Target is not null && link.Target.Offset > link.Offset)
            .GroupBy(link => link.Container)
            .Select(group => group.DistinctBy(link => link.Target!.Offset))
            .Where(group => group.Count() >= MinimumListLinks)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault()?.ToList();
        IEnumerable<OutlineItem> items = list is not null
            ? list.Select(link => Item(link.Text, link.Depth, link.Target!, contentSha256, documentPath))
            : headings.Select(heading => Item(heading.Text, heading.Level, heading.Place, contentSha256, documentPath));
        return OutlineTree.Build(OutlineSource.Document, items.Where(item => item.Title.Length > 0).ToList());
    }

    private static Place ReadPlace(JsonElement item) => new(
        item.GetProperty("offset").GetInt32(),
        Quote(item),
        item.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null,
        Math.Clamp(item.GetProperty("fraction").GetDouble(), 0, 1));

    // A quote cut through a surrogate pair can't be decoded; the entry keeps
    // its offset, which restores exactly without it.
    private static string? Quote(JsonElement item)
    {
        if (!item.TryGetProperty("quote", out JsonElement quote) || quote.ValueKind != JsonValueKind.String) return null;
        try
        {
            return quote.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static OutlineItem Item(string text, int level, Place place, string contentSha256, string documentPath) =>
        new(string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), level,
            HtmlLocationRules.Capture(contentSha256, documentPath,
                new HtmlCapture(place.Offset, place.Quote, string.IsNullOrEmpty(place.Id) ? null : place.Id, place.Fraction)),
            place.Offset);
}
