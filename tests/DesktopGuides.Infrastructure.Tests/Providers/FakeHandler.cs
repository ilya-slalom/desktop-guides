using System.Net;

namespace DesktopGuides.Infrastructure.Tests.Providers;

internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public static FakeHandler Returning(params HttpResponseMessage[] responses)
    {
        Queue<HttpResponseMessage> queue = new(responses);
        return new FakeHandler((_, _) => Task.FromResult(queue.Dequeue()));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken token)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
        Requests.Add((request, body));
        return await respond(request, token);
    }
}
