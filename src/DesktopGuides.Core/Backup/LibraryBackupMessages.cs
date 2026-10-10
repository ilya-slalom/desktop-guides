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

    private static string Plural(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? "" : "s")}");
}
