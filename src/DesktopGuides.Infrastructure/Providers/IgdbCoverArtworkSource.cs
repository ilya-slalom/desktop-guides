using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public sealed class IgdbCoverArtworkSource : IArtworkSource
{
    public Task<ArtworkCandidate?> FindAsync(ArtworkHints hints, CancellationToken token) =>
        Task.FromResult(hints.IgdbCoverImageId is { } id
            ? new ArtworkCandidate(new Uri($"https://images.igdb.com/igdb/image/upload/t_cover_big/{id}.jpg"), "IGDB")
            : null);
}
