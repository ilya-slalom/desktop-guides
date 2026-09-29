namespace DesktopGuides.Core.Providers;

public static class GameMetadataPresentation
{
    public static string TypeLabel(GameTypeTag type) => type switch
    {
        GameTypeTag.MainGame => "Main game",
        _ => type.ToString(),
    };

    public static string ResultSummary(ProviderSearchResult result) =>
        result.ReleaseYear is { } year ? $"{TypeLabel(result.Type)}, {year}" : TypeLabel(result.Type);

    public static string? PlatformSummary(IReadOnlyList<string> platforms) => platforms.Count switch
    {
        0 => null,
        <= 3 => string.Join(", ", platforms),
        _ => $"{string.Join(", ", platforms.Take(3))} and {platforms.Count - 3} more",
    };

    public static IReadOnlyList<string> Attribution(GameMetadataSnapshot snapshot) => snapshot.ArtworkSource switch
    {
        "IGDB" => ["Metadata and artwork from IGDB"],
        "SteamGridDB" => ["Metadata from IGDB", "Artwork from SteamGridDB"],
        _ => ["Metadata from IGDB"],
    };

    public static string? Companies(GameMetadataSnapshot snapshot)
    {
        List<string> parts = [];
        if (snapshot.Developers.Count > 0) parts.Add($"Developed by {string.Join(", ", snapshot.Developers)}");
        if (snapshot.Publishers.Count > 0) parts.Add($"Published by {string.Join(", ", snapshot.Publishers)}");
        return parts.Count == 0 ? null : string.Join(". ", parts) + ".";
    }
}
