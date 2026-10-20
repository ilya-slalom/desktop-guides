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

    // S22: saving a guide again.
    Guide? GetGuide(Guid guideId);

    /// <summary>True while a file operation still claims the guide.</summary>
    bool IsPending(Guid guideId);

    /// <summary>Commits a Prepared ReplaceGuide row owning the stage, the content and the trash.</summary>
    void PrepareReplace(Guid operationId, Guid guideId);

    /// <summary>
    /// Points the guide at its new copy, rewrites its assets, pages and
    /// source, and commits the operation, in one transaction.
    /// </summary>
    void PublishReplacement(ReplacedGuide guide, Action beforeCommit);

    /// <summary>Puts the old copy back and removes the stage, then the row.</summary>
    void RollBackReplace(Guid operationId);

    /// <summary>Deletes a committed replacement's old copy and stage, then its row.</summary>
    void FinishReplace(Guid operationId);
}

/// <summary>
/// A guide's new copy. <paramref name="ClearPlace"/> drops the place,
/// bookmarks and reader settings, for a copy in another format.
/// </summary>
internal sealed record ReplacedGuide(
    Guid OperationId, Guid Id, GuideFormat Format, string PrimaryRelativePath, string ContentSha256,
    long ContentBytes, string? SourceLabel, int? TextCodePage, GuideSource Source, bool ClearPlace,
    IReadOnlyList<GuideAsset>? Assets = null, IReadOnlyList<GuidePage>? Pages = null,
    int? PixelWidth = null, int? PixelHeight = null);

internal sealed record NewImportedGuide(
    Guid OperationId, Guid Id, Guid GameId, string Title, GuideFormat Format,
    string PrimaryRelativePath, string ContentSha256, long ContentBytes,
    string? SourceLabel, int? TextCodePage, IReadOnlyList<GuideAsset>? Assets = null,
    IReadOnlyList<GuidePage>? Pages = null, int? PixelWidth = null, int? PixelHeight = null,
    GuideSource? Source = null);
