using System.Globalization;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;

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

    // Import waits for every picked file to be checked, so none is left out.
    public static bool CanImport(bool checking, bool importing, IReadOnlyList<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        return !checking && !importing && titles.Count > 0 &&
            titles.All(title => GuideTitle.TryCreate(title, out _));
    }

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
