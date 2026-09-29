using System.Net;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class TwitchTokenSourceTests
{
    private static readonly IgdbCredentials Credentials = new("client-id", "very-secret");

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static FakeHandler Tokens(params string[] tokens)
    {
        Queue<string> queue = new(tokens);
        return new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json(
            $$"""{"access_token":"{{queue.Dequeue()}}","expires_in":3600,"token_type":"bearer"}""")));
    }

    [Fact]
    public async Task PostsClientCredentialsFormAndCachesUntilFiveMinutesBeforeExpiry()
    {
        Clock clock = new(DateTimeOffset.UnixEpoch);
        FakeHandler handler = Tokens("one", "two");
        TwitchTokenSource source = new(new ProviderHttp(handler), clock);

        Assert.Equal("one", await source.GetTokenAsync(Credentials, false, default));
        clock.Now += TimeSpan.FromMinutes(54);
        Assert.Equal("one", await source.GetTokenAsync(Credentials, false, default));
        clock.Now += TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1);
        Assert.Equal("two", await source.GetTokenAsync(Credentials, false, default));

        (HttpRequestMessage request, string? body) = handler.Requests[0];
        Assert.Equal("https://id.twitch.tv/oauth2/token", request.RequestUri!.AbsoluteUri);
        Assert.Equal("client_id=client-id&client_secret=very-secret&grant_type=client_credentials", body);
    }

    [Fact]
    public async Task ForceRefreshAndChangedCredentialsFetchANewToken()
    {
        TwitchTokenSource source = new(new ProviderHttp(Tokens("one", "two", "three")));
        Assert.Equal("one", await source.GetTokenAsync(Credentials, false, default));
        Assert.Equal("two", await source.GetTokenAsync(Credentials, true, default));
        Assert.Equal("three", await source.GetTokenAsync(Credentials with { ClientSecret = "other" }, false, default));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ProviderErrorKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderErrorKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.Forbidden, ProviderErrorKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderErrorKind.Unavailable)]
    public async Task FailuresMapToKindsWithoutTheSecret(HttpStatusCode status, ProviderErrorKind kind)
    {
        FakeHandler handler = FakeHandler.Returning(FakeHandler.Json(
            """{"status":400,"message":"invalid client secret very-secret"}""", status));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(
            () => new TwitchTokenSource(new ProviderHttp(handler)).GetTokenAsync(Credentials, false, default));
        Assert.Equal(kind, error.Kind);
        Assert.DoesNotContain("very-secret", error.ToString());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"expires_in":3600}""")]
    [InlineData("""{"access_token":"","expires_in":3600}""")]
    [InlineData("""{"access_token":"t","expires_in":0}""")]
    public async Task MalformedTokenResponseIsMalformedData(string json)
    {
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(
            () => new TwitchTokenSource(new ProviderHttp(FakeHandler.Returning(FakeHandler.Json(json))))
                .GetTokenAsync(Credentials, false, default));
        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
    }

    [Fact]
    public async Task ConcurrentCallersShareOneTokenRequest()
    {
        TaskCompletionSource<HttpResponseMessage> gate = new();
        FakeHandler handler = new(async (_, _) => await gate.Task);
        TwitchTokenSource source = new(new ProviderHttp(handler));

        Task<string> first = source.GetTokenAsync(Credentials, false, default);
        Task<string> second = source.GetTokenAsync(Credentials, false, default);
        await Task.Delay(50);
        gate.SetResult(FakeHandler.Json("""{"access_token":"shared","expires_in":3600,"token_type":"bearer"}"""));

        string[] tokens = await Task.WhenAll(first, second);
        Assert.Equal("shared", tokens[0]);
        Assert.Equal("shared", tokens[1]);
        Assert.Single(handler.Requests);
    }
}
