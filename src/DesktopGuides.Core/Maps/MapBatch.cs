using System.Globalization;
using DesktopGuides.Core.Import;

namespace DesktopGuides.Core.Maps;

// T21.2: the Import maps dialog's words, testable without WinUI.
public static class MapBatch
{
    public const int MaxFiles = 100;

    public static string ImportLabel(int ready) => ready switch
    {
        0 => "Import maps",
        1 => "Import 1 map",
        _ => string.Create(CultureInfo.CurrentCulture, $"Import {ready} maps"),
    };

    public static string Summary(int imported, IReadOnlyList<(string Title, string Reason)> failed)
    {
        ArgumentNullException.ThrowIfNull(failed);
        string done = imported switch
        {
            0 => "No maps were imported.",
            1 => "Imported 1 map.",
            _ => string.Create(CultureInfo.CurrentCulture, $"Imported {imported} maps."),
        };
        if (failed.Count == 0)
        {
            return done;
        }
        string reasons = string.Join("; ", failed.Select(failure => $"{failure.Title}: {failure.Reason}"));
        return string.Create(CultureInfo.CurrentCulture, $"{done} {failed.Count} couldn't be imported: {reasons}");
    }

    public static string Facts(MapImportManifest map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return string.Create(CultureInfo.InvariantCulture, $"{map.Kind.Label()} · {map.PixelWidth} × {map.PixelHeight} · ") +
            ImportPresentation.FormatSize(map.Source.ByteCount);
    }
}
