using System.Globalization;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Maps;

public static class MapPresentation
{
    public static string StoredName(this MapImageKind kind) => kind == MapImageKind.Png ? "map.png" : "map.jpg";

    public static string Label(this MapImageKind kind) => kind == MapImageKind.Png ? "PNG" : "JPEG";

    public static MapImageKind KindOf(Guide guide)
    {
        ArgumentNullException.ThrowIfNull(guide);
        return Path.GetExtension(guide.PrimaryRelativePath).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? MapImageKind.Png
            : MapImageKind.Jpeg;
    }

    public static CatalogFact SizeFact(int width, int height) => new(
        string.Create(CultureInfo.InvariantCulture, $"{width} × {height}"),
        string.Create(CultureInfo.InvariantCulture, $"{width} by {height} pixels"));

    // By title, ignoring case, then by ID, so two maps of one title keep their order.
    public static IReadOnlyList<GuideSummary> Sort(IEnumerable<GuideSummary> maps) =>
        [.. maps.OrderBy(map => map.Guide.Title, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            .ThenBy(map => map.Guide.Id)];
}
