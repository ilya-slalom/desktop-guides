using DesktopGuides.Core.Library;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>The game's title, artwork path and guide IDs, read inside the write gate.</summary>
internal sealed record GameForRemoval(string Title, string? ArtworkRelativePath, IReadOnlyList<Guid> GuideIds);

/// <summary>SQL for guide and game deletions. Callers already hold the repository's write gate.</summary>
internal interface IDeletionJournal
{
    Guide? GetGuide(Guid guideId);

    /// <summary>The game's title, artwork path and guide IDs, or null when it's gone.</summary>
    GameForRemoval? GetGameForRemoval(Guid gameId);

    /// <summary>True when an unfinished file operation already claims the guide.</summary>
    bool IsPending(Guid guideId);

    /// <summary>Commits a Prepared DeleteGuide row owning content/&lt;guide&gt; and .trash/&lt;op&gt;/&lt;guide&gt;.</summary>
    void Prepare(Guid operationId, Guid guideId);

    /// <summary>Commits a Prepared DeleteGame row owning each content/&lt;guide&gt; and .trash/&lt;op&gt;/&lt;guide&gt;.</summary>
    void PrepareGame(Guid operationId, IReadOnlyList<Guid> guideIds);

    /// <summary>Deletes the Guide row (cascading its state rows) and marks the operation Committed, in one transaction.</summary>
    void Commit(Guid operationId, Guid guideId, Action beforeCommit);

    /// <summary>
    /// Deletes the game (cascading its guides, their state rows and its provider
    /// link), clears a LastActiveGuideId that named one of its guides, and marks
    /// the operation Committed, in one transaction. With no guides, there is no
    /// operation and only the game is deleted.
    /// </summary>
    void CommitGame(Guid? operationId, Guid gameId, IReadOnlyList<Guid> guideIds, Action beforeCommit);

    /// <summary>Moves a Prepared deletion's trash back to content, then removes its row.</summary>
    void RollBack(Guid operationId);

    /// <summary>Deletes a Committed deletion's trash, then removes its row.</summary>
    void Finish(Guid operationId);
}
