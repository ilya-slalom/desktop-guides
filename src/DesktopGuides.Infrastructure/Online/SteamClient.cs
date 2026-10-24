using System.Globalization;
using System.Net;
using System.Text.Json;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;

namespace DesktopGuides.Infrastructure.Online;

/// <summary>A Steam Community guide as the Web API describes it; the author's name only when it could be looked up.</summary>
public sealed record SteamGuideSummary(
    string FileId, string AppId, string Title, string? Author, string Summary, Uri? Preview, DateTimeOffset? Updated);

public sealed record SteamGuidePage(IReadOnlyList<SteamGuideSummary> Items, int Total);

/// <summary>
/// S22 PR 4: the three Steam Web API calls Find online makes, one at a time
/// through <see cref="ProviderHttp"/>. Searching needs the user's key, sent
/// only to the API as Steam requires; a guide's details need none. Errors
/// are <see cref="ProviderException"/>s whose messages never hold the key.
/// </summary>
public sealed class SteamClient(ProviderHttp http, Func<CancellationToken, Task<string?>> key)
{
    public const int PageSize = 20;
    private const string Api = "https://api.steampowered.com";

    public static string UserAgent { get; } =
        $"DesktopGuides/{typeof(SteamClient).Assembly.GetName().Version?.ToString(3) ?? "0"} (+https://github.com/ilya-slalom/desktop-guides)";

    public static void Identify(HttpRequestMessage request) => request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

    /// <summary>A page of the app's guides matching the text, ranked by Steam's text search.</summary>
    public async Task<SteamGuidePage> QueryGuidesAsync(string appId, string text, int page, CancellationToken token)
    {
        string apiKey = await RequireKeyAsync(token);
        Uri uri = new($"{Api}/IPublishedFileService/QueryFiles/v1/?key={Uri.EscapeDataString(apiKey)}" +
            $"&appid={Uri.EscapeDataString(appId)}&search_text={Uri.EscapeDataString(text)}" +
            $"&filetype=10&return_details=true&return_short_description=true&numperpage={PageSize}&page={page}&query_type=12");
        using JsonDocument json = await GetJsonAsync(HttpMethod.Get, uri, null, token);
        JsonElement response = Property(json.RootElement, "response");
        int total = response.TryGetProperty("total", out JsonElement count) && count.TryGetInt32(out int n) ? n : 0;
        List<SteamGuideSummary> items = [];
        Dictionary<string, string> creators = new(StringComparer.Ordinal);
        if (response.TryGetProperty("publishedfiledetails", out JsonElement files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement file in files.EnumerateArray())
            {
                if (Summary(file, "consumer_appid", "short_description") is not { } item) continue;
                items.Add(item);
                if (Text(file, "creator") is { } creator) creators[item.FileId] = creator;
            }
        }
        if (items.Count == 0) return new SteamGuidePage([], total);
        IReadOnlyDictionary<string, string> names = await AuthorsAsync(apiKey, creators.Values.Distinct(), token);
        return new SteamGuidePage(
            items.Select(item => item with
            {
                Author = creators.TryGetValue(item.FileId, out string? creator) && names.TryGetValue(creator, out string? name) ? name : null,
            }).ToArray(),
            total);
    }

    /// <summary>
    /// A guide's details; null when Steam has no such visible guide, or
    /// when there's no key: Steam answers a guide asked without one as missing.
    /// </summary>
    public async Task<SteamGuideSummary?> GetDetailsAsync(string fileId, CancellationToken token)
    {
        if (await key(token) is not { Length: > 0 } apiKey) return null;
        using JsonDocument json = await GetJsonAsync(
            HttpMethod.Post, new Uri($"{Api}/ISteamRemoteStorage/GetPublishedFileDetails/v1/"),
            () => new FormUrlEncodedContent([new("key", apiKey), new("itemcount", "1"), new("publishedfileids[0]", fileId)]),
            token);
        JsonElement response = Property(json.RootElement, "response");
        if (!response.TryGetProperty("publishedfiledetails", out JsonElement files) || files.ValueKind != JsonValueKind.Array ||
            files.GetArrayLength() == 0)
        {
            return null;
        }
        return Summary(files[0], "consumer_app_id", "description");
    }

    /// <summary>One search, so a key Steam refuses fails here rather than at the first real search.</summary>
    public async Task TestKeyAsync(CancellationToken token)
    {
        string apiKey = await RequireKeyAsync(token);
        using JsonDocument _ = await GetJsonAsync(HttpMethod.Get,
            new Uri($"{Api}/IPublishedFileService/QueryFiles/v1/?key={Uri.EscapeDataString(apiKey)}&appid=620&filetype=10&numperpage=1"),
            null, token);
    }

    private async Task<string> RequireKeyAsync(CancellationToken token) => await key(token) is { Length: > 0 } value ? value
        : throw new ProviderException(ProviderErrorKind.NotConfigured, "Steam search needs your Steam Web API key.");

    private async Task<IReadOnlyDictionary<string, string>> AuthorsAsync(
        string apiKey, IEnumerable<string> steamIds, CancellationToken token)
    {
        // A name is a nicety: a failed lookup leaves the authors unnamed.
        try
        {
            string ids = string.Join(",", steamIds);
            using JsonDocument json = await GetJsonAsync(HttpMethod.Get,
                new Uri($"{Api}/ISteamUser/GetPlayerSummaries/v2/?key={Uri.EscapeDataString(apiKey)}&steamids={ids}"), null, token);
            Dictionary<string, string> names = new(StringComparer.Ordinal);
            if (Property(json.RootElement, "response").TryGetProperty("players", out JsonElement players) &&
                players.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement player in players.EnumerateArray())
                {
                    if (Text(player, "steamid") is { } id && Text(player, "personaname") is { } name) names[id] = name;
                }
            }
            return names;
        }
        catch (ProviderException error) when (error.Kind != ProviderErrorKind.RateLimited)
        {
            return new Dictionary<string, string>();
        }
    }

    private static SteamGuideSummary? Summary(JsonElement file, string appProperty, string summaryProperty)
    {
        if (Number(file, "result") is not 1 || Text(file, "publishedfileid") is not { } id || Text(file, "title") is not { } title)
        {
            return null;
        }
        Uri? preview = Text(file, "preview_url") is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed : null;
        DateTimeOffset? updated = Number(file, "time_updated") is { } seconds and > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        return new SteamGuideSummary(id, Text(file, appProperty) ?? "", title, null, Text(file, summaryProperty) ?? "", preview, updated);
    }

    // The form is made for each attempt: a request disposes its content,
    // and a retry or a redirect sends a new request.
    private async Task<JsonDocument> GetJsonAsync(HttpMethod method, Uri uri, Func<HttpContent>? content, CancellationToken token)
    {
        ProviderResponse response = await http.SendAsync(method, uri, request =>
        {
            Identify(request);
            if (content is not null) request.Content = content();
        }, ProviderHttp.MaxJsonBytes, token);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                break;
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new ProviderException(ProviderErrorKind.InvalidCredentials, "Steam didn't accept the key. Check it in Settings.");
            case HttpStatusCode.TooManyRequests:
                throw new ProviderException(ProviderErrorKind.RateLimited, "Steam is limiting requests. Try again in a few minutes.");
            default:
                throw new ProviderException(ProviderErrorKind.Unavailable,
                    $"Steam's API answered {(int)response.StatusCode}.");
        }
        try
        {
            return JsonDocument.Parse(response.Body);
        }
        catch (JsonException)
        {
            throw new ProviderException(ProviderErrorKind.MalformedData, "Steam's API answered with something unreadable.");
        }
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new ProviderException(ProviderErrorKind.MalformedData, "Steam's API answered with something unreadable.");

    // Steam writes IDs as strings in one call and numbers in another.
    private static string? Text(JsonElement element, string name) =>
        !element.TryGetProperty(name, out JsonElement value) ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : value.ValueKind == JsonValueKind.Number ? value.GetRawText()
        : null;

    private static long? Number(JsonElement element, string name) =>
        Text(element, name) is { } text && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : null;
}
