using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public sealed partial class IgdbClient(
    ProviderHttp http,
    TwitchTokenSource tokens,
    Func<CancellationToken, Task<IgdbCredentials?>> credentials) : IGameMetadataProvider
{
    public const int MaxQueryLength = 100;
    private const int SteamSource = 1;
    private static readonly Uri GamesUri = new("https://api.igdb.com/v4/games");

    [GeneratedRegex("^[a-z0-9]{1,40}$")]
    private static partial Regex ImageId();

    public async Task<IReadOnlyList<ProviderSearchResult>> SearchAsync(string query, CancellationToken token)
    {
        string body = BuildSearchBody(query);
        using JsonDocument document = await PostAsync(body, token);
        List<ProviderSearchResult> results = [];
        foreach (JsonElement item in RequireArray(document).EnumerateArray())
        {
            try
            {
                results.Add(new ProviderSearchResult(
                    GameMetadataNormalizer.NormalizeExternalId(ReadId(item)),
                    GameMetadataNormalizer.NormalizeTitle(ReadString(item, "name")),
                    ReadReleaseDate(item)?.Year,
                    GameMetadataNormalizer.NormalizeList(ReadNames(item, "platforms")),
                    ReadType(item),
                    ReadCoverId(item) is { } id ? $"https://images.igdb.com/igdb/image/upload/t_thumb/{id}.jpg" : null));
            }
            catch (ProviderException) { }
            if (results.Count == 20) break;
        }
        return results;
    }

    public async Task<ProviderGameRecord> GetAsync(string externalId, CancellationToken token)
    {
        string id = GameMetadataNormalizer.NormalizeExternalId(externalId);
        using JsonDocument document = await PostAsync(BuildGetBody(id), token);
        JsonElement array = RequireArray(document);
        if (array.GetArrayLength() != 1 || array[0].ValueKind != JsonValueKind.Object)
        {
            throw Malformed();
        }
        JsonElement item = array[0];
        if (ReadId(item) != id) throw Malformed();
        string title = GameMetadataNormalizer.NormalizeTitle(ReadString(item, "name"));
        IEnumerable<JsonElement> companies = ReadArray(item, "involved_companies");
        GameMetadataSnapshot snapshot = new(
            GameMetadataSnapshot.CurrentSchemaVersion,
            GameMetadataNormalizer.NormalizeText(ReadString(item, "summary"), GameMetadataNormalizer.SummaryLimit),
            ReadReleaseDate(item),
            GameMetadataNormalizer.NormalizeList(ReadNames(item, "genres")),
            GameMetadataNormalizer.NormalizeList(CompanyNames(companies, "developer")),
            GameMetadataNormalizer.NormalizeList(CompanyNames(companies, "publisher")),
            GameMetadataNormalizer.NormalizeList(ReadNames(item, "platforms")),
            GameMetadataNormalizer.NormalizeProviderUrl(ReadString(item, "url")),
            ReadType(item));
        return new ProviderGameRecord(id, title, snapshot, new ArtworkHints(title, ReadSteamAppId(item), ReadCoverId(item)));
    }

    internal static string BuildSearchBody(string query)
    {
        string? trimmed = GameMetadataNormalizer.NormalizeText(query?.ReplaceLineEndings(" "), int.MaxValue);
        if (trimmed is null || trimmed.Length > MaxQueryLength)
        {
            throw new ArgumentException($"Enter 1 to {MaxQueryLength} characters to search.", nameof(query));
        }
        string escaped = trimmed.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"search \"{escaped}\"; fields name,first_release_date,game_type.type,version_parent," +
            "platforms.name,cover.image_id; limit 20;";
    }

    internal static string BuildGetBody(string externalId) =>
        "fields name,summary,url,first_release_date,game_type.type,version_parent,genres.name," +
        "platforms.name,involved_companies.developer,involved_companies.publisher," +
        "involved_companies.company.name,cover.image_id,external_games.uid," +
        "external_games.external_game_source,external_games.category; " +
        $"where id = {externalId}; limit 1;";

    private async Task<JsonDocument> PostAsync(string body, CancellationToken token)
    {
        IgdbCredentials current = await credentials(token) ??
            throw new ProviderException(ProviderErrorKind.NotConfigured, "Add IGDB credentials in Settings to search.");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string accessToken = await tokens.GetTokenAsync(current, forceRefresh: attempt > 0, token);
            ProviderResponse response = await http.SendAsync(HttpMethod.Post, GamesUri, request =>
            {
                request.Headers.Add("Client-ID", current.ClientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
            }, ProviderHttp.MaxJsonBytes, token);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    try { return JsonDocument.Parse(response.Body); }
                    catch (JsonException) { throw Malformed(); }
                case HttpStatusCode.Unauthorized when attempt == 0:
                    continue;
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw new ProviderException(ProviderErrorKind.InvalidCredentials, "IGDB rejected the client ID or secret.");
                default:
                    throw new ProviderException(ProviderErrorKind.Unavailable, "IGDB could not complete the request.");
            }
        }
        throw new ProviderException(ProviderErrorKind.InvalidCredentials, "IGDB rejected the client ID or secret.");
    }

    private static JsonElement RequireArray(JsonDocument document) =>
        document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement : throw Malformed();

    private static string? ReadId(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out JsonElement id) &&
        id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out long value)
            ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IEnumerable<JsonElement> ReadArray(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToArray()
            : [];

    private static IEnumerable<string?> ReadNames(JsonElement item, string name) =>
        ReadArray(item, name).Select(e => ReadString(e, "name"));

    private static IEnumerable<string?> CompanyNames(IEnumerable<JsonElement> companies, string role) =>
        companies
            .Where(c => c.TryGetProperty(role, out JsonElement flag) && flag.ValueKind == JsonValueKind.True)
            .Select(c => c.TryGetProperty("company", out JsonElement company) && company.ValueKind == JsonValueKind.Object
                ? ReadString(company, "name") : null);

    private static DateOnly? ReadReleaseDate(JsonElement item) =>
        item.TryGetProperty("first_release_date", out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long seconds) &&
        seconds is >= -62135596800 and <= 253402300799
            ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime)
            : null;

    private static GameTypeTag ReadType(JsonElement item) =>
        GameMetadataNormalizer.MapType(
            item.TryGetProperty("game_type", out JsonElement type) && type.ValueKind == JsonValueKind.Object
                ? ReadString(type, "type") : null,
            item.TryGetProperty("version_parent", out JsonElement parent) && parent.ValueKind == JsonValueKind.Number);

    private static string? ReadCoverId(JsonElement item) =>
        item.TryGetProperty("cover", out JsonElement cover) && cover.ValueKind == JsonValueKind.Object &&
        ReadString(cover, "image_id") is { } id && ImageId().IsMatch(id) ? id : null;

    private static string? ReadSteamAppId(JsonElement item) =>
        ReadArray(item, "external_games")
            .Where(e => IsSource(e, "external_game_source") || IsSource(e, "category"))
            .Select(e => ReadString(e, "uid"))
            .FirstOrDefault(uid => uid is { Length: >= 1 and <= 12 } && uid.All(char.IsAsciiDigit));

    private static bool IsSource(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int source) && source == SteamSource;

    private static ProviderException Malformed() =>
        new(ProviderErrorKind.MalformedData, "IGDB returned data the app couldn't read.");
}
