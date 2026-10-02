using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>SQL for one import. Callers already hold the repository's write gate.</summary>
internal interface IImportJournal
{
    /// <summary>Commits a Prepared Import row owning the staged and content directories.</summary>
    void Prepare(Guid operationId, Guid guideId);

    /// <summary>Adds the guide and its empty state rows and removes the operation, in one transaction.</summary>
    void Publish(NewImportedGuide guide, Action beforeCommit);

    /// <summary>Removes the import's owned directories, then its row.</summary>
    void RollBack(Guid operationId);

    /// <summary>The oldest guide in the game with this format and content hash, or null.</summary>
    Guid? FindGuide(Guid gameId, GuideFormat format, string contentSha256);
}

internal sealed record NewImportedGuide(
    Guid OperationId, Guid Id, Guid GameId, string Title, GuideFormat Format,
    string PrimaryRelativePath, string ContentSha256, long ContentBytes,
    string? SourceLabel, int? TextCodePage, IReadOnlyList<GuideAsset>? Assets = null);
