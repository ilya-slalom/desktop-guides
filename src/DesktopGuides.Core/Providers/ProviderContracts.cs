namespace DesktopGuides.Core.Providers;

public sealed record StoredArtwork(string RelativePath, string Sha256);

public interface IArtworkStore
{
    Task<StoredArtwork> StoreAsync(Guid gameId, byte[] content, CancellationToken token);
    void Delete(string relativePath);
    string? ResolveFile(string relativePath);
}
