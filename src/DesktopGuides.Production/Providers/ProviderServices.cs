using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Providers;

namespace DesktopGuides.Production.Providers;

internal sealed class ProviderServices : IDisposable
{
    public ProviderServices(string localStatePath)
    {
        Http = new ProviderHttp();
        Tokens = new TwitchTokenSource(Http);
        Thumbnails = new ProviderThumbnailLoader(Http);
        Credentials = new WindowsProviderCredentialStore(localStatePath);
    }

    public ProviderHttp Http { get; }
    public TwitchTokenSource Tokens { get; }
    public ProviderThumbnailLoader Thumbnails { get; }
    public IProviderCredentialStore Credentials { get; }

    private IgdbClient? igdb;

    public IgdbClient Igdb => igdb ??= CreateIgdb(async token => (await Credentials.LoadAsync(token)).Igdb);

    public async Task<bool> HasIgdbCredentialsAsync(CancellationToken token) =>
        (await Credentials.LoadAsync(token)).Igdb is not null;

    public ProviderGameImporter CreateImporter(ILibraryRepository repository, ManagedArtworkStore store) =>
        new(repository,
            Igdb,
            new FallbackArtworkSource(
                CreateSteamGridDb(async token => (await Credentials.LoadAsync(token)).SteamGridDbKey),
                new IgdbCoverArtworkSource()),
            store,
            Http.GetImageAsync);

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
