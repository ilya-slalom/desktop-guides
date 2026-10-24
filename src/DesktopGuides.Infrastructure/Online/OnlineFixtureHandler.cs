using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesktopGuides.Infrastructure.Online;

/// <summary>
/// S22 PR 4, test runs only: the app's own requests (Steam's API, pages and
/// images) answered from the online fixture manifest, as the browser's are.
/// A route never names the key: it's left out of the match. A form POST
/// matches its body as the query. An unmatched request gets 599 and a
/// method, host and path line in unmatched.json, never its query or body.
/// </summary>
public sealed class OnlineFixtureHandler(OnlineFixtureRoutes routes, string logFolder) : HttpMessageHandler
{
    public const string UnmatchedName = "unmatched.json";
    private readonly object gate = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Uri asked = request.RequestUri!;
        string query = string.Join("&", asked.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("key=", StringComparison.Ordinal)));
        if (request.Method == HttpMethod.Post && request.Content is FormUrlEncodedContent form && query.Length == 0)
        {
            query = await form.ReadAsStringAsync(token);
        }
        Uri match = new UriBuilder(asked) { Query = query }.Uri;
        if (!routes.TryAnswer(match, out int status, out string contentType, out string? file, out string? location))
        {
            Record(request.Method, asked);
        }
        HttpResponseMessage response = new((HttpStatusCode)status)
        {
            Content = new ByteArrayContent(file is not null && File.Exists(file) ? await File.ReadAllBytesAsync(file, token) : []),
        };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? type) ? type : null;
        if (location is not null) response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private void Record(HttpMethod method, Uri uri)
    {
        lock (gate)
        {
            string path = Path.Combine(logFolder, UnmatchedName);
            JsonArray lines = File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray existing ? existing : [];
            lines.Add(new JsonObject { ["method"] = method.Method, ["host"] = uri.Host, ["path"] = uri.AbsolutePath });
            File.WriteAllText(path, lines.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
