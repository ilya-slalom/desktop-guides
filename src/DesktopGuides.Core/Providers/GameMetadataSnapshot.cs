namespace DesktopGuides.Core.Providers;

public enum GameTypeTag { MainGame, Remaster, Remake, Port, Edition, Expansion, Bundle, Other }

public sealed record GameMetadataSnapshot(
    int SchemaVersion,
    string? Summary,
    DateOnly? FirstReleaseDate,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Developers,
    IReadOnlyList<string> Publishers,
    IReadOnlyList<string> Platforms,
    string? ProviderUrl,
    GameTypeTag Type,
    string? ArtworkSource = null)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record ProviderGameLink(string Provider, string ExternalId, DateTimeOffset RetrievedUtc)
{
    public const string Igdb = "igdb";
}

public sealed record ArtworkHints(string Title, string? SteamAppId, string? IgdbCoverImageId);

public sealed record ProviderGameRecord(
    string ExternalId, string Title, GameMetadataSnapshot Snapshot, ArtworkHints Hints);

public sealed record ProviderSearchResult(
    string ExternalId,
    string Title,
    int? ReleaseYear,
    IReadOnlyList<string> Platforms,
    GameTypeTag Type,
    string? ThumbnailUrl);
