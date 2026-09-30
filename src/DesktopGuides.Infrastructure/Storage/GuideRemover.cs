using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Infrastructure.Storage;

internal enum RemovalCheckpoint { Prepared, Moved, InCommit, Committed }

/// <summary>
/// Removes a guide and its owned files. The removal holds the library write
/// gate; content/&lt;guide&gt; moves to .trash/&lt;op&gt;/&lt;guide&gt; under a
/// journaled DeleteGuide operation, so any failure before the commit moves it
/// back, and a crash is finished by the startup reconciler.
/// </summary>
public sealed class GuideRemover
{
    private readonly SqliteLibraryRepository repository;
    private readonly ILibraryPaths paths;
    private readonly Action<RemovalCheckpoint> checkpoint;

    public GuideRemover(SqliteLibraryRepository repository, ILibraryPaths paths)
        : this(repository, paths, _ => { })
    {
    }

    internal GuideRemover(
        SqliteLibraryRepository repository, ILibraryPaths paths, Action<RemovalCheckpoint> checkpoint)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
    }

    /// <summary>The guide and its file count, or null when it's already gone.</summary>
    public async Task<GuideRemovalPreview?> DescribeAsync(Guid guideId, CancellationToken token = default)
    {
        Guide? guide = await repository.GetGuideAsync(guideId, token);
        if (guide is null)
        {
            return null;
        }
        OwnedGuideTree content = await Task.Run(() => Capture(paths.GetGuideRoot(guide.Id)), token);
        return new GuideRemovalPreview(guide.Id, guide.GameId, guide.Title, content.FileCount);
    }

    /// <summary>
    /// Removes the guide. The token only cancels the wait for the write gate;
    /// once the deletion is journaled it runs to an outcome.
    /// </summary>
    public Task<GuideRemovalResult> RemoveAsync(Guid guideId, CancellationToken token = default) =>
        repository.RunDeletionAsync(journal => Remove(journal, guideId), token);

    private GuideRemovalResult Remove(IDeletionJournal journal, Guid guideId)
    {
        if (journal.GetGuide(guideId) is null)
        {
            return new GuideRemovalResult(GuideRemovalOutcome.NotFound, false);
        }
        if (journal.IsPending(guideId))
        {
            // A second deletion would strand the first one's row, and startup
            // would then refuse to open the library.
            throw new GuideRemovalException(GuideRemovalIssue.RestoreFailed);
        }
        string contentPath = paths.GetGuideRoot(guideId);
        OwnedGuideTree content = Capture(contentPath);
        Guid operationId = Guid.NewGuid();
        try
        {
            journal.Prepare(operationId, guideId);
        }
        catch (Exception error)
        {
            // Nothing has changed yet, so there's nothing to roll back.
            throw new GuideRemovalException(GuideRemovalIssue.Failed, error);
        }
        try
        {
            checkpoint(RemovalCheckpoint.Prepared);
            if (content.Exists)
            {
                string trashPath = paths.GetTrashedGuideRoot(operationId, guideId);
                Directory.CreateDirectory(Path.GetDirectoryName(trashPath)!);
                Directory.Move(contentPath, trashPath);
            }
            checkpoint(RemovalCheckpoint.Moved);
            journal.Commit(operationId, guideId, () => checkpoint(RemovalCheckpoint.InCommit));
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
                throw new GuideRemovalException(
                    GuideRemovalIssue.RestoreFailed, new AggregateException(error, rollBackError));
            }
            throw new GuideRemovalException(GuideRemovalIssue.Failed, error);
        }
        try
        {
            checkpoint(RemovalCheckpoint.Committed);
            journal.Finish(operationId);
            return new GuideRemovalResult(GuideRemovalOutcome.Removed, false);
        }
        catch (Exception)
        {
            // The Committed row stays, so the next startup deletes the trash.
            return new GuideRemovalResult(GuideRemovalOutcome.Removed, true);
        }
    }

    private static OwnedGuideTree Capture(string root)
    {
        try
        {
            return OwnedGuideTree.Capture(root);
        }
        catch (InvalidDataException error)
        {
            throw new GuideRemovalException(GuideRemovalIssue.Unsafe, error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new GuideRemovalException(GuideRemovalIssue.Failed, error);
        }
    }
}
