using System.Net;
using System.Text.Json;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public sealed class TwitchTokenSource(ProviderHttp http, TimeProvider? clock = null) : IDisposable
{
    private static readonly Uri TokenUri = new("https://id.twitch.tv/oauth2/token");
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private (IgdbCredentials Credentials, string Token, DateTimeOffset RefreshAfter)? cached;

    public async Task<string> GetTokenAsync(IgdbCredentials credentials, bool forceRefresh, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!forceRefresh && cached is { } entry && entry.Credentials == credentials &&
                time.GetUtcNow() < entry.RefreshAfter)
            {
                return entry.Token;
            }
            cached = null;
            ProviderResponse response = await http.SendAsync(HttpMethod.Post, TokenUri, request =>
                request.Content = new FormUrlEncodedContent(
                [
                    new("client_id", credentials.ClientId),
                    new("client_secret", credentials.ClientSecret),
                    new("grant_type", "client_credentials")
                ]), ProviderHttp.MaxJsonBytes, token);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderException(ProviderErrorKind.InvalidCredentials, "Twitch rejected the IGDB client ID or secret.");
            }
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new ProviderException(ProviderErrorKind.Unavailable, "Twitch could not issue an IGDB token.");
            }
            (string accessToken, int expiresIn) = Parse(response.Body);
            cached = (credentials, accessToken, time.GetUtcNow() + TimeSpan.FromSeconds(expiresIn) - ExpiryMargin);
            return accessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private static (string, int) Parse(byte[] body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("access_token", out JsonElement accessToken) &&
                accessToken.ValueKind == JsonValueKind.String && accessToken.GetString() is { Length: > 0 } value &&
                root.TryGetProperty("expires_in", out JsonElement expires) &&
                expires.TryGetInt32(out int seconds) && seconds > 0)
            {
                return (value, seconds);
            }
        }
        catch (JsonException) { }
        throw new ProviderException(ProviderErrorKind.MalformedData, "Twitch returned a token response the app couldn't read.");
    }

    public void Dispose()
    {
        gate.Dispose();
    }
}
