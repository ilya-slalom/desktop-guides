using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DesktopGuides.Infrastructure.Providers;

// T17.2 D: answers provider requests from a recorded fixture folder for the
// fake-provider CI lane. It never opens a socket: an unmatched request gets
// 599 and a { method, host, path } line in unmatched.json, never its query
// or headers, which can hold keys.
public sealed class FixtureProviderHandler : HttpMessageHandler
{
    public const string ManifestName = "manifest.json";
    public const string UnmatchedName = "unmatched.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly string folder;
    private readonly IReadOnlyList<FixtureRoute> routes;
    private readonly int[] used;
    private readonly object gate = new();

    public FixtureProviderHandler(string folder)
    {
        this.folder = Path.GetFullPath(folder);
        string manifest = File.ReadAllText(Path.Combine(this.folder, ManifestName));
        routes = JsonSerializer.Deserialize<FixtureManifest>(manifest, Options)?.Routes ?? [];
        foreach (FixtureRoute route in routes)
        {
            if (route.Body is string body && Path.GetFileName(body) != body)
            {
                throw new InvalidDataException($"Fixture body '{body}' must be a file name in the fixture folder.");
            }
        }
        used = new int[routes.Count];
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri uri = request.RequestUri ?? throw new InvalidOperationException("A fixture request needs a URI.");
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        string searched = Uri.UnescapeDataString(uri.Query) + "\n" + body;
        int index = -1;
        lock (gate)
        {
            for (int i = 0; i < routes.Count; i++)
            {
                FixtureRoute route = routes[i];
                if (route.Times is int times && used[i] >= times) continue;
                if (!string.Equals(route.Method, request.Method.Method, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(route.Host, uri.Host, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(route.Path, uri.AbsolutePath, StringComparison.Ordinal) ||
                    (route.Contains is string contains && !searched.Contains(contains, StringComparison.Ordinal)))
                {
                    continue;
                }
                used[i]++;
                index = i;
                break;
            }
            if (index < 0) RecordUnmatched(request.Method.Method, uri);
        }
        if (index < 0) return new HttpResponseMessage((HttpStatusCode)599) { RequestMessage = request };

        FixtureRoute match = routes[index];
        if (match.DelayMilliseconds > 0) await Task.Delay(match.DelayMilliseconds, cancellationToken);
        HttpResponseMessage response = new((HttpStatusCode)match.Status) { RequestMessage = request };
        response.Content = match.Body is string file
            ? new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(folder, file), cancellationToken))
            : new ByteArrayContent([]);
        if (match.RetryAfterSeconds is int seconds)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }
        return response;
    }

    // Called under the gate.
    private void RecordUnmatched(string method, Uri uri)
    {
        string path = Path.Combine(folder, UnmatchedName);
        List<UnmatchedRequest> entries = File.Exists(path)
            ? JsonSerializer.Deserialize<List<UnmatchedRequest>>(File.ReadAllText(path), Options) ?? []
            : [];
        entries.Add(new UnmatchedRequest(method, uri.Host, uri.AbsolutePath));
        File.WriteAllText(path, JsonSerializer.Serialize(entries, Options));
    }

    private sealed record FixtureManifest(IReadOnlyList<FixtureRoute> Routes);

    private sealed record FixtureRoute(
        string Method, string Host, string Path, string? Contains, int Status, string? Body,
        int? RetryAfterSeconds, int DelayMilliseconds, int? Times);

    private sealed record UnmatchedRequest(string Method, string Host, string Path);
}
