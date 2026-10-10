namespace DesktopGuides.Core.Library;

// Why InitializeAsync stopped. Every issue except RestoreIncomplete leaves the library unchanged.
public enum LibraryOpenIssue { Damaged, Missing, NewerVersion, MigrationFailed, Locked, NoAccess, DiskFull, RestoreIncomplete }

// The message is the user-facing body; technical detail stays in InnerException.
public sealed class LibraryOpenException(
    LibraryOpenIssue issue, string? recoveryCopyPath = null, Exception? inner = null)
    : Exception(LibraryOpenMessages.For(issue).Body, inner)
{
    public LibraryOpenIssue Issue { get; } = issue;
    public string? RecoveryCopyPath { get; } = recoveryCopyPath;
}

public sealed record LibraryOpenMessage(string Title, string Body);

public static class LibraryOpenMessages
{
    private const string Unchanged = "Desktop Guides stopped before changing anything";

    public static LibraryOpenMessage For(LibraryOpenIssue issue) => issue switch
    {
        LibraryOpenIssue.Damaged => new("Library can't be read",
            $"Your library database can't be read. {Unchanged}. Your guide files are still in the data folder."),
        LibraryOpenIssue.Missing => new("Library database is missing",
            $"Your library database is missing, but your guide files are still in the data folder. {Unchanged}. Restore library.sqlite, then try again."),
        LibraryOpenIssue.NewerVersion => new("Update Desktop Guides",
            $"Your library was saved by a newer version of Desktop Guides. {Unchanged}. Update Desktop Guides, then try again."),
        LibraryOpenIssue.MigrationFailed => new("Library upgrade failed",
            $"Your library couldn't be upgraded. {Unchanged} and kept a copy of your library from before the upgrade."),
        LibraryOpenIssue.Locked => new("Library is in use",
            $"Another program may be using your library. {Unchanged}. Close that program, then try again."),
        LibraryOpenIssue.NoAccess => new("Library can't be accessed",
            $"Desktop Guides doesn't have permission to read or change your library. {Unchanged}. Check the data folder's permissions, then try again."),
        LibraryOpenIssue.DiskFull => new("Not enough disk space",
            $"There isn't enough free disk space to open your library. {Unchanged}. Free up some space, then try again."),
        LibraryOpenIssue.RestoreIncomplete => new("Restore didn't finish",
            "A restore didn't finish and Desktop Guides couldn't put your previous library back. Your previous library is in the .recovery folder inside the data folder. Close other programs that might be using it, then try again."),
        _ => throw new ArgumentOutOfRangeException(nameof(issue))
    };

    public static string DataFolder(string path) => $"Data folder: {path}";

    public static string RecoveryCopy(string path) => $"Copy from before the upgrade: {path}";
}
