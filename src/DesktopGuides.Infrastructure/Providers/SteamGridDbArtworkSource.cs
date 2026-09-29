using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public sealed class SteamGridDbArtworkSource(
    ProviderHttp http, Func<CancellationToken, Task<string?>> apiKey) : IArtworkSource
{
    private const string Api = "https://www.steamgriddb.com/api/v2/";

    public async Task<ArtworkCandidate?> FindAsync(ArtworkHints hints, CancellationToken token)
    {
        if (await apiKey(token) is not { Length: > 0 } key) return null;
        if (hints.SteamAppId is { } appId &&
            await FirstGridAsync($"grids/steam/{appId}?dimensions=600x900", key, token) is { } bySteam)
        {
            return bySteam;
        }
        using JsonDocument? search = await GetAsync(
            $"search/autocomplete/{Uri.EscapeDataString(hints.Title)}", key, token);
        long? gameId = Data(search)
            .Where(e => ReadString(e, "name") is { } name &&
                string.Equals(name.Trim(), hints.Title, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number &&
                id.TryGetInt64(out long value) && value > 0 ? value : (long?)null)
            .FirstOrDefault(id => id is not null);
        return gameId is { } found
            ? await FirstGridAsync($"grids/game/{found}?dimensions=600x900", key, token)
            : null;
    }

    public async Task TestConnectionAsync(CancellationToken token)
    {
        string key = await apiKey(token) is { Length: > 0 } saved ? saved :
            throw new ProviderException(ProviderErrorKind.NotConfigured, "Add a SteamGridDB API key to test it.");
        using JsonDocument? _ = await GetAsync("grids/steam/70?dimensions=600x900", key, token);
    }

    private async Task<ArtworkCandidate?> FirstGridAsync(string path, string key, CancellationToken token)
    {
        using JsonDocument? grids = await GetAsync(path, key, token);
        foreach (JsonElement grid in Data(grids))
        {
            if (ReadString(grid, "url") is { } url && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
                uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && ProviderHttp.AllowedHosts.Contains(uri.IdnHost))
            {
                return new ArtworkCandidate(uri, "SteamGridDB");
            }
        }
        return null;
    }

    private async Task<JsonDocument?> GetAsync(string path, string key, CancellationToken token)
    {
        ProviderResponse response = await http.SendAsync(HttpMethod.Get, new Uri(Api + path), request =>
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }, ProviderHttp.MaxJsonBytes, token);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                try { return JsonDocument.Parse(response.Body); }
                catch (JsonException) { throw new ProviderException(ProviderErrorKind.MalformedData, "SteamGridDB returned data the app couldn't read."); }
            case HttpStatusCode.NotFound:
                return null;
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new ProviderException(ProviderErrorKind.InvalidCredentials, "SteamGridDB rejected the API key.");
            default:
                throw new ProviderException(ProviderErrorKind.Unavailable, "SteamGridDB could not complete the request.");
        }
    }

    private static IEnumerable<JsonElement> Data(JsonDocument? document) =>
        document?.RootElement is { ValueKind: JsonValueKind.Object } root &&
        root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToArray()
            : [];

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
