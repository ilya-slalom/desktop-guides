using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public static class ProviderCredentialBlob
{
    public const int MaxBytes = 4096;
    public const int MaxClientIdLength = 64;
    public const int MaxSecretLength = 128;
    private const int Version = 1;

    public static ProviderCredentials Normalize(string? clientId, string? clientSecret, string? steamGridDbKey)
    {
        string? id = Field(clientId, MaxClientIdLength, "IGDB client ID");
        string? secret = Field(clientSecret, MaxSecretLength, "IGDB client secret");
        string? key = Field(steamGridDbKey, MaxSecretLength, "SteamGridDB API key");
        if ((id is null) != (secret is null))
        {
            throw new ArgumentException(id is null
                ? "Enter the IGDB client ID with the secret."
                : "Enter the IGDB client secret with the client ID.");
        }
        return new ProviderCredentials(id is null ? null : new IgdbCredentials(id, secret!), key);
    }

    public static byte[] Format(ProviderCredentials credentials)
    {
        ProviderCredentials valid = Normalize(
            credentials.Igdb?.ClientId, credentials.Igdb?.ClientSecret, credentials.SteamGridDbKey);
        JsonObject root = new() { ["v"] = Version };
        if (valid.Igdb is { } igdb)
        {
            root["igdb"] = new JsonObject { ["clientId"] = igdb.ClientId, ["clientSecret"] = igdb.ClientSecret };
        }
        if (valid.SteamGridDbKey is { } key) root["steamGridDbKey"] = key;
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    public static ProviderCredentials Parse(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || data.Length > MaxBytes) return ProviderCredentials.None;
        try
        {
            using JsonDocument document = JsonDocument.Parse(data.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("v", out JsonElement version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int v) || v != Version)
            {
                return ProviderCredentials.None;
            }
            string? id = null, secret = null;
            if (root.TryGetProperty("igdb", out JsonElement igdb))
            {
                if (igdb.ValueKind != JsonValueKind.Object) return ProviderCredentials.None;
                id = ReadString(igdb, "clientId");
                secret = ReadString(igdb, "clientSecret");
            }
            return Normalize(id, secret, ReadString(root, "steamGridDbKey"));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            return ProviderCredentials.None;
        }
    }

    private static string? ReadString(JsonElement item, string name) =>
        !item.TryGetProperty(name, out JsonElement value) ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : throw new InvalidOperationException("Credential field has the wrong type.");

    private static string? Field(string? raw, int maxLength, string name)
    {
        string? trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"The {name} can be at most {maxLength} characters.");
        }
        if (trimmed.Any(c => c is < '!' or > '~'))
        {
            throw new ArgumentException($"The {name} can contain only letters, digits and punctuation, with no spaces.");
        }
        return trimmed;
    }
}
