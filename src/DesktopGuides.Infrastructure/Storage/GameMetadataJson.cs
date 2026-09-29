using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Storage;

internal static class GameMetadataJson
{
    public const int MaxLength = 65536;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static string Serialize(GameMetadataSnapshot snapshot)
    {
        string json = JsonSerializer.Serialize(snapshot, Options);
        if (json.Length > MaxLength)
        {
            throw new ArgumentException("The metadata snapshot is too large to store.", nameof(snapshot));
        }
        return json;
    }

    public static GameMetadataSnapshot? TryParse(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaxLength) return null;
        try
        {
            GameMetadataSnapshot? value = JsonSerializer.Deserialize<GameMetadataSnapshot>(json, Options);
            return value is { SchemaVersion: GameMetadataSnapshot.CurrentSchemaVersion } &&
                value.Genres is not null && value.Developers is not null &&
                value.Publishers is not null && value.Platforms is not null &&
                Enum.IsDefined(value.Type) && value.ArtworkSource is null or "SteamGridDB" or "IGDB"
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
