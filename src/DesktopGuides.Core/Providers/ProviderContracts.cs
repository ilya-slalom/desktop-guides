namespace DesktopGuides.Core.Providers;

public sealed record StoredArtwork(string RelativePath, string Sha256);

public interface IArtworkStore
{
    Task<StoredArtwork> StoreAsync(Guid gameId, byte[] content, CancellationToken token);
    void Delete(string relativePath);
    string? ResolveFile(string relativePath);
}

public sealed record IgdbCredentials(string ClientId, string ClientSecret)
{
    public override string ToString() => $"IgdbCredentials {{ ClientId = {ClientId}, ClientSecret = *** }}";
}

public sealed record ProviderCredentials(IgdbCredentials? Igdb, string? SteamGridDbKey)
{
    public static ProviderCredentials None { get; } = new(null, null);

    public override string ToString() =>
        $"ProviderCredentials {{ Igdb = {(Igdb is null ? "none" : "saved")}, SteamGridDbKey = {(SteamGridDbKey is null ? "none" : "saved")} }}";
}

public interface IGameMetadataProvider
{
    Task<IReadOnlyList<ProviderSearchResult>> SearchAsync(string query, CancellationToken token);
    Task<ProviderGameRecord> GetAsync(string externalId, CancellationToken token);
}

public sealed record ArtworkCandidate(Uri Url, string SourceName);

public interface IArtworkSource
{
    Task<ArtworkCandidate?> FindAsync(ArtworkHints hints, CancellationToken token);
}

public interface IProviderCredentialStore
{
    Task<ProviderCredentials> LoadAsync(CancellationToken token);
    Task SaveAsync(ProviderCredentials credentials, CancellationToken token);
    Task ClearAsync(CancellationToken token);
}
