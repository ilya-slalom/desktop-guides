using DesktopGuides.Core.Providers;

namespace DesktopGuides.Core.Library;

/// <summary>Removes a game without guides, then deletes its artwork best effort.</summary>
public sealed class GameRemover(ILibraryRepository repository, IArtworkStore artwork)
{
    public async Task<EmptyGameRemovalOutcome> RemoveAsync(
        Guid gameId, CancellationToken token = default)
    {
        EmptyGameRemoval removal = await repository.RemoveEmptyGameAsync(gameId, token);
        if (removal is { Outcome: EmptyGameRemovalOutcome.Removed, ArtworkRelativePath: { } path })
        {
            try
            {
                artwork.Delete(path);
            }
            // The row is gone, so the next startup sweep deletes the file.
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return removal.Outcome;
    }
}
