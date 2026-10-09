using DesktopGuides.Core.Library;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Storage;

// Translates a failure raised while opening the library. Anything without an
// issue is rethrown unchanged by the caller and keeps the generic status.
internal static class LibraryOpenErrors
{
    private const int DiskFull = 112;
    private const int HandleDiskFull = 39;

    public static LibraryOpenException? Map(Exception error)
    {
        LibraryOpenIssue? issue = error switch
        {
            LibraryOpenException => null,
            SqliteException sqlite => (sqlite.SqliteErrorCode & 0xFF) switch
            {
                11 or 26 => LibraryOpenIssue.Damaged,
                5 or 6 => LibraryOpenIssue.Locked,
                3 or 8 or 14 or 23 => LibraryOpenIssue.NoAccess,
                13 => LibraryOpenIssue.DiskFull,
                _ => null
            },
            InvalidDataException => LibraryOpenIssue.Damaged,
            UnauthorizedAccessException => LibraryOpenIssue.NoAccess,
            IOException io when (io.HResult & 0xFFFF) is DiskFull or HandleDiskFull => LibraryOpenIssue.DiskFull,
            _ => null
        };
        return issue is { } mapped ? new LibraryOpenException(mapped, null, error) : null;
    }
}
