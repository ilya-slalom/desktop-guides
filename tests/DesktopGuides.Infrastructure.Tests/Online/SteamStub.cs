using System.Net;
using System.Text;

namespace DesktopGuides.Infrastructure.Tests.Online;

// S22 PR 4: answers Steam requests by host and path, and records each one.
internal sealed class SteamStub : HttpMessageHandler
{
    public Dictionary<string, Func<HttpRequestMessage, (HttpStatusCode Status, byte[] Body)>> Routes { get; } = [];
    public List<(HttpMethod Method, Uri Uri, string? Body, DateTimeOffset At, string UserAgent)> Requests { get; } = [];
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    public TimeSpan? RetryAfter { get; set; }

    public void Json(string path, string fixture, HttpStatusCode status = HttpStatusCode.OK) =>
        Routes[path] = _ => (status, Encoding.UTF8.GetBytes(P3Fixtures.Read("steam/" + fixture)));

    public void Status(string path, HttpStatusCode status) => Routes[path] = _ => (status, []);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
        Requests.Add((request.Method, request.RequestUri!, body, Clock(), request.Headers.UserAgent.ToString()));
        string key = request.RequestUri!.Host + request.RequestUri.AbsolutePath;
        (HttpStatusCode status, byte[] bytes) = Routes.TryGetValue(key, out var route) ? route(request) : (HttpStatusCode.NotFound, []);
        HttpResponseMessage response = new(status) { Content = new ByteArrayContent(bytes) };
        if (status == HttpStatusCode.TooManyRequests && RetryAfter is { } wait)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
        }
        return response;
    }
}
