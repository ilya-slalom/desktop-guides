using System.Globalization;

namespace DesktopGuides.Core.Backup;

public static class LibraryBackupMessages
{
    public const string ExportCanceled = "Export canceled.";
    public const string ExportReminder =
        "Your library lives in app data, and uninstalling the app removes it. Export a backup from Settings.";
    public const string GoToExport = "Go to Export";
    public const string PickerFailed = "Couldn't open the file dialog. Try again.";

    private const double KiB = 1024;
    private const double MiB = KiB * 1024;
    private const double GiB = MiB * 1024;

    public static string SuggestedFileName(DateTime localDate) =>
        string.Create(CultureInfo.InvariantCulture, $"DesktopGuides-backup-{localDate:yyyy-MM-dd}.zip");

    public static string Counts(int games, int guides) =>
        $"{Plural(games, "game")}, {Plural(guides, "guide")}";

    public static string Size(long bytes) => bytes switch
    {
        < (long)MiB => string.Create(CultureInfo.InvariantCulture,
            $"{Math.Max(1L, (long)Math.Ceiling(bytes / KiB))} KB"),
        < (long)GiB => string.Create(CultureInfo.InvariantCulture,
            $"{Math.Round(bytes / MiB, MidpointRounding.AwayFromZero):0} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / GiB:0.0} GB")
    };

    /// <summary>Up to three titles, then "and N more".</summary>
    public static string Titles(IReadOnlyList<string> titles) => titles.Count switch
    {
        0 => "",
        1 => titles[0],
        2 or 3 => $"{string.Join(", ", titles.Take(titles.Count - 1))} and {titles[^1]}",
        _ => $"{string.Join(", ", titles.Take(3))} and {titles.Count - 3} more"
    };

    public static string ExportSaved(string fileName, int games, int guides, long bytes) =>
        $"Backup saved: {fileName} ({Counts(games, guides)}, {Size(bytes)}).";

    public static string ExportPhase(LibraryExportPhase phase) => phase switch
    {
        LibraryExportPhase.Preparing => "Preparing backup…",
        LibraryExportPhase.Writing => "Writing backup…",
        LibraryExportPhase.Verifying => "Checking backup…",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static string ExportFailed(LibraryExportIssue issue, IReadOnlyList<string> titles) => issue switch
    {
        LibraryExportIssue.DestinationNotAllowed =>
            "Choose a folder outside the app's data, such as Documents or a USB drive. Backups saved in app data are removed when the app is uninstalled.",
        LibraryExportIssue.DestinationUnavailable => "That folder isn't available. Choose another folder.",
        LibraryExportIssue.DestinationExists => "A file with that name appeared while saving. Try again.",
        LibraryExportIssue.RecoveryIncomplete =>
            "An earlier change to the library didn't finish. Restart the app, then try again.",
        LibraryExportIssue.DatabaseInvalid =>
            "The library database couldn't be copied. Restart the app, then try again.",
        LibraryExportIssue.ManagedFilesDamaged => titles.Count == 0
            ? "Some guide files are missing or damaged. Remove or re-import those guides, then try again."
            : $"Some guide files are missing or damaged: {Titles(titles)}. Remove or re-import them, then try again.",
        LibraryExportIssue.LibraryTooLarge => "The library is too large to back up in one file.",
        LibraryExportIssue.WriteFailed =>
            "The backup couldn't be written. Check the drive has free space, then try again.",
        LibraryExportIssue.VerificationFailed =>
            "The saved backup didn't match the library, so it was deleted. Try again, or choose another drive.",
        _ => throw new ArgumentOutOfRangeException(nameof(issue))
    };

    public const string ReplaceTitle = "Replace your library?";
    public const string RestoreKept = "The backup couldn't be opened, so your previous library was kept.";
    public const string RestoreCanceled = "Restore canceled.";

    public static string RestorePhase(LibraryRestorePhase phase) => phase switch
    {
        LibraryRestorePhase.Copying => "Copying backup…",
        LibraryRestorePhase.Checking => "Checking backup…",
        LibraryRestorePhase.Extracting => "Unpacking backup…",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static string BackupMade(DateTime localCreated, string appVersion) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Backup made: {localCreated:d MMMM yyyy, HH:mm}, by version {appVersion}");

    public static string BackupHolds(int games, int guides, long bytes) =>
        $"Backup holds: {Counts(games, guides)}, {Size(bytes)}";

    public static string LibraryHas(int games, int guides) => $"This library has: {Counts(games, guides)}";

    public static string ReplaceBody(int currentGames, int currentGuides, int games, int guides) =>
        currentGames == 0 && currentGuides == 0
            ? $"The backup's {Pair(games, guides)} will be restored."
            : $"Your {Pair(currentGames, currentGuides)}, with their reading progress, will be replaced by the backup's {Pair(games, guides)}. This can't be undone. To keep the current library, export it first.";

    public static string Restored(int games, int guides) => $"Library restored: {Counts(games, guides)}.";

    public static string RestoreFailed(LibraryRestoreIssue issue, IReadOnlyList<string> titles, long? bytesNeeded) => issue switch
    {
        LibraryRestoreIssue.SourceUnavailable =>
            "The backup file couldn't be read. Check it's still there, then try again.",
        LibraryRestoreIssue.ArchiveInvalid => "This file isn't a Desktop Guides backup, or it's damaged.",
        LibraryRestoreIssue.ArchiveUnsafe =>
            "This backup contains file names that aren't allowed, so it wasn't opened.",
        LibraryRestoreIssue.NotEnoughSpace => bytesNeeded is long needed
            ? $"There isn't enough free space to restore this backup. It needs {Size(needed)}."
            : "There isn't enough free space to restore this backup.",
        LibraryRestoreIssue.NewerVersion =>
            "This backup is from a newer version of Desktop Guides. Update the app, then try again.",
        LibraryRestoreIssue.DatabaseInvalid => "The library database in this backup is damaged.",
        LibraryRestoreIssue.ReferencesInvalid => titles.Count == 0
            ? "This backup is missing some guide or artwork files."
            : $"This backup is missing files for: {Titles(titles)}.",
        LibraryRestoreIssue.SwapFailed =>
            "The library couldn't be replaced. Close other programs that might be using it, then try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(issue))
    };

    private static string Pair(int games, int guides) =>
        $"{Plural(games, "game")} and {Plural(guides, "guide")}";

    private static string Plural(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? "" : "s")}");
}
