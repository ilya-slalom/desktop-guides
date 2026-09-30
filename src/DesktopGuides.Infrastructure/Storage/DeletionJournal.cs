using DesktopGuides.Core.Library;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>SQL for one guide deletion. Callers already hold the repository's write gate.</summary>
internal interface IDeletionJournal
{
    Guide? GetGuide(Guid guideId);

    /// <summary>Commits a Prepared DeleteGuide row owning content/&lt;guide&gt; and .trash/&lt;op&gt;/&lt;guide&gt;.</summary>
    void Prepare(Guid operationId, Guid guideId);

    /// <summary>Deletes the Guide row (cascading its state rows) and marks the operation Committed, in one transaction.</summary>
    void Commit(Guid operationId, Guid guideId, Action beforeCommit);

    /// <summary>Moves a Prepared deletion's trash back to content, then removes its row.</summary>
    void RollBack(Guid operationId);

    /// <summary>Deletes a Committed deletion's trash, then removes its row.</summary>
    void Finish(Guid operationId);
}
