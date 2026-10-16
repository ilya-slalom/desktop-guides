namespace DesktopGuides.Core.Library;

public interface IBookmarkStore
{
    // Oldest first; the reader sorts them into reading order.
    Task<IReadOnlyList<Bookmark>> ListBookmarksAsync(
        Guid guideId, CancellationToken token = default);

    // Throws BookmarkLimitException at MaxPerGuide, and
    // InvalidOperationException when the guide is gone.
    Task<Bookmark> AddBookmarkAsync(
        Guid guideId, string locatorJson, string label, CancellationToken token = default);

    // Null when the bookmark is gone.
    Task<Bookmark?> RenameBookmarkAsync(
        Guid bookmarkId, string label, CancellationToken token = default);

    // False when it was already gone.
    Task<bool> DeleteBookmarkAsync(Guid bookmarkId, CancellationToken token = default);

    // Undo: puts a deleted bookmark back with its id and times, under the
    // same limit and guide checks as an add.
    Task RestoreBookmarkAsync(Bookmark bookmark, CancellationToken token = default);
}
