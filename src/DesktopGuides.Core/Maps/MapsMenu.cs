using System.Globalization;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Maps;

/// <summary>A row of the reader's Maps pane.</summary>
public sealed record MapsMenuItem(Guid Id, string Title, string Facts, int PixelWidth, int PixelHeight);

// T21.2 PR c: the game's maps for the reader's Maps pane, by title.
public static class MapsMenu
{
    public static IReadOnlyList<MapsMenuItem> Items(IEnumerable<GuideSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        return [.. MapPresentation.Sort(summaries.Where(summary => summary.Guide.Format == GuideFormat.Image))
            .Select(summary => summary.Guide)
            .Select(map => new MapsMenuItem(
                map.Id,
                map.Title,
                string.Create(CultureInfo.InvariantCulture,
                    $"{MapPresentation.KindOf(map).Label()} · {map.PixelWidth} × {map.PixelHeight}"),
                map.PixelWidth ?? 1,
                map.PixelHeight ?? 1))];
    }

    // The open map's row in the map reader, else the first.
    public static int FirstFocus(IReadOnlyList<MapsMenuItem> items, Guid? openGuideId)
    {
        ArgumentNullException.ThrowIfNull(items);
        for (int index = 0; index < items.Count; index++)
        {
            if (items[index].Id == openGuideId) return index;
        }
        return 0;
    }
}
