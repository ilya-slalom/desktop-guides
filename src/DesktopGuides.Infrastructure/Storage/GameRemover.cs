using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>
/// Removes a game with its guides, their state and owned files, its provider
/// link and its artwork. The removal holds the library write gate; each
/// content/&lt;guide&gt; moves to .trash/&lt;op&gt;/&lt;guide&gt; under one
/// journaled DeleteGame operation, so any failure before the commit moves them
/// all back, and a crash is finished by the startup reconciler. A game without
/// guides needs no operation. The artwork is deleted after the commit, and the
/// startup sweep deletes it if that fails.
/// </summary>
public sealed class GameRemover
{
    private readonly SqliteLibraryRepository repository;
    private readonly ILibraryPaths paths;
    private readonly IArtworkStore artwork;
    private readonly Action<RemovalCheckpoint> checkpoint;

    public GameRemover(SqliteLibraryRepository repository, ILibraryPaths paths, IArtworkStore artwork)
        : this(repository, paths, artwork, _ => { })
    {
    }

    internal GameRemover(
        SqliteLibraryRepository repository, ILibraryPaths paths, IArtworkStore artwork,
        Action<RemovalCheckpoint> checkpoint)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.artwork = artwork ?? throw new ArgumentNullException(nameof(artwork));
        this.checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
    }

    /// <summary>The game with its guide and file counts, or null when it's already gone.</summary>
    public async Task<GameRemovalPreview?> DescribeAsync(Guid gameId, CancellationToken token = default)
    {
        RequireId(gameId);
        Game? game = await repository.GetGameAsync(gameId, token);
        if (game is null)
        {
            return null;
        }
        IReadOnlyList<Guide> guides = await repository.ListGuidesAsync(gameId, token);
        List<OwnedGuideTree> trees = await Task.Run(
            () => CaptureAll(guides.Select(guide => guide.Id).ToList()), token);
        return Preview(gameId, game.Title, trees);
    }

    /// <summary>
    /// Removes the game when it still has expectedGuideCount guides; otherwise
    /// returns CountChanged with a fresh preview. The token only cancels the
    /// wait for the write gate; once the deletion is journaled it runs to an outcome.
    /// </summary>
    public Task<GameRemovalResult> RemoveAsync(
        Guid gameId, int expectedGuideCount, CancellationToken token = default)
    {
        RequireId(gameId);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedGuideCount);
        return repository.RunDeletionAsync(journal => Remove(journal, gameId, expectedGuideCount), token);
    }

    private GameRemovalResult Remove(IDeletionJournal journal, Guid gameId, int expectedGuideCount)
    {
        GameForRemoval? game = journal.GetGameForRemoval(gameId);
        if (game is null)
        {
            return new GameRemovalResult(GameRemovalOutcome.NotFound, false, null);
        }
        List<OwnedGuideTree> trees = CaptureAll(game.GuideIds);
        if (game.GuideIds.Count != expectedGuideCount)
        {
            return new GameRemovalResult(
                GameRemovalOutcome.CountChanged, false, Preview(gameId, game.Title, trees));
        }
        if (game.GuideIds.Count == 0)
        {
            try
            {
                journal.CommitGame(null, gameId, [], () => checkpoint(RemovalCheckpoint.InCommit));
            }
            catch (Exception error)
            {
                throw new GameRemovalException(GameRemovalIssue.Failed, error);
            }
            DeleteArtwork(game.ArtworkRelativePath);
            return new GameRemovalResult(GameRemovalOutcome.Removed, false, null);
        }
        if (game.GuideIds.Any(journal.IsPending))
        {
            // A second deletion would strand the first one's row, and startup
            // would then refuse to open the library.
            throw new GameRemovalException(GameRemovalIssue.RestoreFailed);
        }
        Guid operationId = Guid.NewGuid();
        try
        {
            journal.PrepareGame(operationId, game.GuideIds);
        }
        catch (Exception error)
        {
            // Nothing has changed yet, so there's nothing to roll back.
            throw new GameRemovalException(GameRemovalIssue.Failed, error);
        }
        try
        {
            checkpoint(RemovalCheckpoint.Prepared);
            for (int index = 0; index < game.GuideIds.Count; index++)
            {
                if (!trees[index].Exists)
                {
                    continue;
                }
                Guid guideId = game.GuideIds[index];
                string trashPath = paths.GetTrashedGuideRoot(operationId, guideId);
                Directory.CreateDirectory(Path.GetDirectoryName(trashPath)!);
                Directory.Move(paths.GetGuideRoot(guideId), trashPath);
                checkpoint(RemovalCheckpoint.MovedGuide);
            }
            checkpoint(RemovalCheckpoint.Moved);
            journal.CommitGame(operationId, gameId, game.GuideIds, () => checkpoint(RemovalCheckpoint.InCommit));
        }
        catch (Exception error)
        {
            try
            {
                journal.RollBack(operationId);
            }
            catch (Exception rollBackError)
            {
                // The Prepared row stays, so the next startup moves the trash back.
                throw new GameRemovalException(
                    GameRemovalIssue.RestoreFailed, new AggregateException(error, rollBackError));
            }
            throw new GameRemovalException(GameRemovalIssue.Failed, error);
        }
        bool cleanupPending = false;
        try
        {
            checkpoint(RemovalCheckpoint.Committed);
            journal.Finish(operationId);
        }
        catch (Exception)
        {
            // The Committed row stays, so the next startup deletes the trash.
            cleanupPending = true;
        }
        DeleteArtwork(game.ArtworkRelativePath);
        return new GameRemovalResult(GameRemovalOutcome.Removed, cleanupPending, null);
    }

    private void DeleteArtwork(string? relativePath)
    {
        if (relativePath is null)
        {
            return;
        }
        try
        {
            artwork.Delete(relativePath);
        }
        // The row is gone, so the next startup sweep deletes the file.
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void RequireId(Guid gameId)
    {
        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("A game ID is required.", nameof(gameId));
        }
    }

    private static GameRemovalPreview Preview(Guid gameId, string title, List<OwnedGuideTree> trees) =>
        new(gameId, title, trees.Count, trees.Sum(tree => tree.FileCount));

    private List<OwnedGuideTree> CaptureAll(IReadOnlyList<Guid> guideIds) =>
        guideIds.Select(id => Capture(paths.GetGuideRoot(id))).ToList();

    private static OwnedGuideTree Capture(string root)
    {
        try
        {
            return OwnedGuideTree.Capture(root);
        }
        catch (InvalidDataException error)
        {
            throw new GameRemovalException(GameRemovalIssue.Unsafe, error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new GameRemovalException(GameRemovalIssue.Failed, error);
        }
    }
}
