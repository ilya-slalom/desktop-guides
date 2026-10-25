using System.Net;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public sealed record ProviderResponse(HttpStatusCode StatusCode, byte[] Body);

public sealed class ProviderHttp : IDisposable
{
    public const int MaxJsonBytes = 1024 * 1024;
    public const int MaxImageBytes = 5 * 1024 * 1024;
    private const int MaxRedirects = 3;

    // Recorded in Task 1 from the terms and a live grid query.
    public static IReadOnlySet<string> AllowedHosts { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "id.twitch.tv", "api.igdb.com", "images.igdb.com", "www.steamgriddb.com", "cdn2.steamgriddb.com",
        // S22 PR 4: Steam's Web API, its guide pages and their images.
        "api.steampowered.com", "steamcommunity.com", "images.steamusercontent.com",
        // S22 PR 5: The Video Game Atlas.
        "vgmaps.com"
    };

    private readonly HttpClient client;
    private readonly TimeSpan requestTimeout;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly SemaphoreSlim gate = new(1, 1);

    public ProviderHttp(HttpMessageHandler? handler = null)
        : this(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false },
            TimeSpan.FromSeconds(15), Task.Delay)
    {
    }

    internal ProviderHttp(
        HttpMessageHandler handler, TimeSpan requestTimeout, Func<TimeSpan, CancellationToken, Task> delay)
    {
        client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        this.requestTimeout = requestTimeout;
        this.delay = delay;
    }

    public async Task<ProviderResponse> SendAsync(
        HttpMethod method, Uri uri, Action<HttpRequestMessage> configure, int maxBytes, CancellationToken token)
    {
        RequireAllowed(uri);
        await gate.WaitAsync(token);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                (HttpStatusCode status, byte[] body, TimeSpan? retryAfter) =
                    await SendFollowingRedirectsAsync(method, uri, configure, maxBytes, token);
                if (status != HttpStatusCode.TooManyRequests) return new ProviderResponse(status, body);
                if (attempt > 0 || retryAfter is not { } wait || wait > TimeSpan.FromSeconds(2))
                {
                    throw new ProviderException(ProviderErrorKind.RateLimited, $"{uri.Host} is busy.");
                }
                await delay(wait, token);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// S22 PR 5: streams a large file (a map) to <paramref name="path"/>. The
    /// request timeout covers the headers and each wait for more of the body,
    /// not the whole download, so a big file on a slow line still arrives.
    /// Returns the status; only a 200 writes the file, and a failure deletes it.
    /// </summary>
    public async Task<HttpStatusCode> DownloadAsync(
        Uri uri, string path, Action<HttpRequestMessage> configure, long maxBytes, CancellationToken token)
    {
        RequireAllowed(uri);
        await gate.WaitAsync(token);
        try
        {
            Uri current = uri;
            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                using CancellationTokenSource timer = CancellationTokenSource.CreateLinkedTokenSource(token);
                timer.CancelAfter(requestTimeout);
                try
                {
                    using HttpRequestMessage request = new(HttpMethod.Get, current);
                    configure(request);
                    using HttpResponseMessage response = await client.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, timer.Token);
                    if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                    {
                        Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
                        if (next.Scheme != Uri.UriSchemeHttps || !string.Equals(next.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
                        {
                            throw Unavailable(uri, "redirected to another host");
                        }
                        current = next;
                        continue;
                    }
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        throw new ProviderException(ProviderErrorKind.RateLimited, $"{uri.Host} is busy.");
                    }
                    if (response.StatusCode != HttpStatusCode.OK) return response.StatusCode;
                    if (response.Content.Headers.ContentLength > maxBytes) throw TooLarge(uri);
                    await CopyLimitedAsync(response.Content, path, maxBytes, uri, timer, token);
                    return HttpStatusCode.OK;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    TryDelete(path);
                    throw new ProviderException(ProviderErrorKind.Timeout, $"{uri.Host} took too long to respond.");
                }
                catch (Exception error) when (error is HttpRequestException or IOException)
                {
                    TryDelete(path);
                    throw Unavailable(uri, "could not be reached");
                }
                catch
                {
                    TryDelete(path);
                    throw;
                }
            }
            throw Unavailable(uri, "redirected too many times");
        }
        finally
        {
            gate.Release();
        }
    }

    // Each read restarts the silence timer.
    private async Task CopyLimitedAsync(
        HttpContent content, string path, long maxBytes, Uri uri, CancellationTokenSource timer, CancellationToken token)
    {
        await using Stream source = await content.ReadAsStreamAsync(timer.Token);
        await using FileStream target = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        byte[] chunk = new byte[81920];
        long written = 0;
        while (true)
        {
            timer.CancelAfter(requestTimeout);
            int read = await source.ReadAsync(chunk, timer.Token);
            if (read == 0) return;
            written += read;
            if (written > maxBytes) throw TooLarge(uri);
            await target.WriteAsync(chunk.AsMemory(0, read), token);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async Task<byte[]> GetImageAsync(Uri uri, CancellationToken token)
    {
        ProviderResponse response = await SendAsync(HttpMethod.Get, uri, _ => { }, MaxImageBytes, token);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, $"{uri.Host} did not return the image.");
        }
        return response.Body;
    }

    private async Task<(HttpStatusCode, byte[], TimeSpan?)> SendFollowingRedirectsAsync(
        HttpMethod method, Uri uri, Action<HttpRequestMessage> configure, int maxBytes, CancellationToken token)
    {
        Uri current = uri;
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            using CancellationTokenSource timer = CancellationTokenSource.CreateLinkedTokenSource(token);
            timer.CancelAfter(requestTimeout);
            try
            {
                using HttpRequestMessage request = new(method, current);
                configure(request);
                using HttpResponseMessage response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timer.Token);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (next.Scheme != Uri.UriSchemeHttps || !string.Equals(next.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        throw Unavailable(uri, "redirected to another host");
                    }
                    current = next;
                    continue;
                }
                byte[] body = await ReadLimitedAsync(response.Content, maxBytes, uri, timer.Token);
                return (response.StatusCode, body, RetryAfter(response));
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new ProviderException(ProviderErrorKind.Timeout, $"{uri.Host} took too long to respond.");
            }
            catch (Exception error) when (error is HttpRequestException or IOException)
            {
                // IOException covers a connection that drops while the body is read.
                throw Unavailable(uri, "could not be reached");
            }
        }
        throw Unavailable(uri, "redirected too many times");
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maxBytes, Uri uri, CancellationToken token)
    {
        if (content.Headers.ContentLength > maxBytes) throw TooLarge(uri);
        await using Stream stream = await content.ReadAsStreamAsync(token);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > maxBytes) throw TooLarge(uri);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    // A date already past (server clock ahead of ours) means retry now.
    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta ??
        (response.Headers.RetryAfter?.Date is { } date
            ? TimeSpan.FromTicks(Math.Max(0, (date - DateTimeOffset.UtcNow).Ticks))
            : null);

    private static void RequireAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !AllowedHosts.Contains(uri.IdnHost))
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, "The app does not contact that address.");
        }
    }

    private static ProviderException Unavailable(Uri uri, string reason) =>
        new(ProviderErrorKind.Unavailable, $"{uri.Host} {reason}.");

    private static ProviderException TooLarge(Uri uri) =>
        new(ProviderErrorKind.MalformedData, $"{uri.Host} returned more data than the app accepts.");

    public void Dispose()
    {
        client.Dispose();
        gate.Dispose();
    }
}
