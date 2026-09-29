using System.Net;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class ProviderHttpTests
{
    private static readonly Uri Igdb = new("https://api.igdb.com/v4/games");
    private readonly List<TimeSpan> delays = [];

    private ProviderHttp Create(FakeHandler handler, TimeSpan? timeout = null) =>
        new(handler, timeout ?? TimeSpan.FromSeconds(15), (wait, _) => { delays.Add(wait); return Task.CompletedTask; });

    private static Task<ProviderResponse> Send(ProviderHttp http, Uri uri, CancellationToken token = default) =>
        http.SendAsync(HttpMethod.Post, uri, _ => { }, ProviderHttp.MaxJsonBytes, token);

    [Theory]
    [InlineData("http://api.igdb.com/v4/games")]
    [InlineData("https://evil.example/v4/games")]
    [InlineData("https://api.igdb.com:8443/v4/games")]
    public async Task RejectsHttpOtherHostsAndPortsWithoutSending(string uri)
    {
        FakeHandler handler = FakeHandler.Returning(FakeHandler.Json("[]"));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => Send(Create(handler), new Uri(uri)));
        Assert.Equal(ProviderErrorKind.Unavailable, error.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FollowsSameHostRedirectButRejectsCrossHostRedirect()
    {
        HttpResponseMessage same = new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("/v4/games?x=1", UriKind.Relative) } };
        HttpResponseMessage cross = new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://www.igdb.com/") } };
        FakeHandler handler = FakeHandler.Returning(same, cross);

        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => Send(Create(handler), Igdb));
        Assert.Equal(ProviderErrorKind.Unavailable, error.Kind);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api.igdb.com/v4/games?x=1", handler.Requests[1].Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ShortRetryAfterRetriesOnceThenSucceeds()
    {
        HttpResponseMessage busy = new(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new(TimeSpan.FromSeconds(1)) } };
        FakeHandler handler = FakeHandler.Returning(busy, FakeHandler.Json("[]"));

        ProviderResponse response = await Send(Create(handler), Igdb);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task LongOrMissingRetryAfterOrSecond429IsRateLimited()
    {
        HttpResponseMessage Busy(int? seconds) => new(HttpStatusCode.TooManyRequests)
        { Headers = { RetryAfter = seconds is null ? null : new(TimeSpan.FromSeconds(seconds.Value)) } };

        foreach (FakeHandler handler in new[]
                 { FakeHandler.Returning(Busy(3)), FakeHandler.Returning(Busy(null)), FakeHandler.Returning(Busy(1), Busy(1)) })
        {
            ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => Send(Create(handler), Igdb));
            Assert.Equal(ProviderErrorKind.RateLimited, error.Kind);
        }
    }

    [Fact]
    public async Task SlowResponseIsATimeoutButCallerCancelIsACancel()
    {
        FakeHandler slow = new(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(); });
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(
            () => Send(Create(slow, TimeSpan.FromMilliseconds(50)), Igdb));
        Assert.Equal(ProviderErrorKind.Timeout, error.Kind);

        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Send(Create(slow, TimeSpan.FromSeconds(30)), Igdb, cancel.Token));
    }

    [Fact]
    public async Task NetworkFailureIsUnavailableWithoutInnerException()
    {
        FakeHandler down = new((_, _) => throw new HttpRequestException("Bearer secret-token leaked"));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => Send(Create(down), Igdb));
        Assert.Equal(ProviderErrorKind.Unavailable, error.Kind);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("secret-token", error.ToString());
    }

    [Fact]
    public async Task BodyReadFailureIsUnavailableWithoutInnerException()
    {
        FakeHandler reset = FakeHandler.Returning(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingStream())
        });
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => Send(Create(reset), Igdb));
        Assert.Equal(ProviderErrorKind.Unavailable, error.Kind);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task OversizedBodyIsMalformed()
    {
        FakeHandler big = FakeHandler.Returning(FakeHandler.Json(new string('x', ProviderHttp.MaxJsonBytes + 1)));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => Send(Create(big), Igdb));
        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
    }

    [Fact]
    public async Task RequestsRunOneAtATime()
    {
        int active = 0, peak = 0;
        FakeHandler handler = new(async (_, _) =>
        {
            peak = Math.Max(peak, Interlocked.Increment(ref active));
            await Task.Delay(20);
            Interlocked.Decrement(ref active);
            return FakeHandler.Json("[]");
        });
        ProviderHttp http = Create(handler);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Send(http, Igdb)));
        Assert.Equal(1, peak);
    }

    [Fact]
    public void CredentialRecordsNeverPrintSecrets()
    {
        ProviderCredentials credentials = new(new IgdbCredentials("client-id", "igdb-secret"), "sgdb-key");
        Assert.DoesNotContain("igdb-secret", credentials.ToString());
        Assert.DoesNotContain("igdb-secret", credentials.Igdb!.ToString());
        Assert.DoesNotContain("sgdb-key", credentials.ToString());
    }
}

internal sealed class FailingStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Connection reset.");
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
