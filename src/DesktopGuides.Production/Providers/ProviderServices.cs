using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;

namespace DesktopGuides.Production.Providers;

internal sealed class ProviderServices : IDisposable
{
    public ProviderServices(string localStatePath)
    {
        Http = new ProviderHttp();
        Tokens = new TwitchTokenSource(Http);
        Credentials = new WindowsProviderCredentialStore(localStatePath);
    }

    public ProviderHttp Http { get; }
    public TwitchTokenSource Tokens { get; }
    public IProviderCredentialStore Credentials { get; }

    public IgdbClient CreateIgdb(Func<CancellationToken, Task<IgdbCredentials?>> credentials) =>
        new(Http, Tokens, credentials);

    public SteamGridDbArtworkSource CreateSteamGridDb(Func<CancellationToken, Task<string?>> apiKey) =>
        new(Http, apiKey);

    public void Dispose()
    {
        Tokens.Dispose();
        Http.Dispose();
    }
}
