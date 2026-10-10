using DesktopGuides.Core.Backup;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class LibraryBackupMessagesTests
{
    [Fact]
    public void TheSuggestedNameCarriesTheLocalDate() =>
        Assert.Equal("DesktopGuides-backup-2026-10-08.zip",
            LibraryBackupMessages.SuggestedFileName(new DateTime(2026, 10, 8, 23, 59, 0)));

    [Theory]
    [InlineData(1, 1, "1 game, 1 guide")]
    [InlineData(12, 40, "12 games, 40 guides")]
    [InlineData(0, 0, "0 games, 0 guides")]
    public void CountsUseSingularAndPlural(int games, int guides, string expected) =>
        Assert.Equal(expected, LibraryBackupMessages.Counts(games, guides));

    [Theory]
    [InlineData(0L, "1 KB")]
    [InlineData(1500L, "2 KB")]
    [InlineData(310L * 1024 * 1024, "310 MB")]
    [InlineData(1536L * 1024 * 1024, "1.5 GB")]
    public void SizesAreRoundedForReading(long bytes, string expected) =>
        Assert.Equal(expected, LibraryBackupMessages.Size(bytes));

    [Theory]
    [InlineData(new[] { "A" }, "A")]
    [InlineData(new[] { "A", "B" }, "A and B")]
    [InlineData(new[] { "A", "B", "C" }, "A, B and C")]
    [InlineData(new[] { "A", "B", "C", "D", "E" }, "A, B, C and 2 more")]
    public void TitlesNameAtMostThree(string[] titles, string expected) =>
        Assert.Equal(expected, LibraryBackupMessages.Titles(titles));

    [Fact]
    public void TheSavedMessageNamesTheFileCountsAndSize() =>
        Assert.Equal("Backup saved: DesktopGuides-backup-2026-10-10.zip (12 games, 40 guides, 310 MB).",
            LibraryBackupMessages.ExportSaved(
                "DesktopGuides-backup-2026-10-10.zip", 12, 40, 310L * 1024 * 1024));

    [Fact]
    public void EveryExportIssueHasAMessage()
    {
        foreach (LibraryExportIssue issue in Enum.GetValues<LibraryExportIssue>())
        {
            Assert.False(string.IsNullOrWhiteSpace(LibraryBackupMessages.ExportFailed(issue, [])));
        }
    }

    [Fact]
    public void AProtectedFolderSaysWhy() =>
        Assert.Equal(
            "Choose a folder outside the app's data, such as Documents or a USB drive. Backups saved in app data are removed when the app is uninstalled.",
            LibraryBackupMessages.ExportFailed(LibraryExportIssue.DestinationNotAllowed, []));

    [Fact]
    public void DamagedFilesNameTheGuides() =>
        Assert.Equal(
            "Some guide files are missing or damaged: Maps, Manual, Walkthrough and 1 more. Remove or re-import them, then try again.",
            LibraryBackupMessages.ExportFailed(
                LibraryExportIssue.ManagedFilesDamaged, ["Maps", "Manual", "Walkthrough", "Notes"]));

    [Fact]
    public void DamagedFilesWithoutTitlesStillSayWhatToDo() =>
        Assert.Equal(
            "Some guide files are missing or damaged. Remove or re-import those guides, then try again.",
            LibraryBackupMessages.ExportFailed(LibraryExportIssue.ManagedFilesDamaged, []));

    [Fact]
    public void EveryPhaseHasALabel()
    {
        foreach (LibraryExportPhase phase in Enum.GetValues<LibraryExportPhase>())
        {
            Assert.EndsWith("…", LibraryBackupMessages.ExportPhase(phase));
        }
    }

    [Fact]
    public void EveryRestoreIssueHasAMessage()
    {
        foreach (LibraryRestoreIssue issue in Enum.GetValues<LibraryRestoreIssue>())
        {
            Assert.False(string.IsNullOrWhiteSpace(LibraryBackupMessages.RestoreFailed(issue, [], null)));
        }
    }

    [Fact]
    public void ANewerBackupSaysToUpdate() =>
        Assert.Equal("This backup is from a newer version of Desktop Guides. Update the app, then try again.",
            LibraryBackupMessages.RestoreFailed(LibraryRestoreIssue.NewerVersion, [], null));

    [Fact]
    public void MissingFilesNameTheGuides() =>
        Assert.Equal("This backup is missing files for: Maps and Linked Game.",
            LibraryBackupMessages.RestoreFailed(LibraryRestoreIssue.ReferencesInvalid, ["Maps", "Linked Game"], null));

    [Fact]
    public void NotEnoughSpaceSaysHowMuch() =>
        Assert.Equal("There isn't enough free space to restore this backup. It needs 1.5 GB.",
            LibraryBackupMessages.RestoreFailed(LibraryRestoreIssue.NotEnoughSpace, [], 1536L * 1024 * 1024));

    [Fact]
    public void TheStagedDetailsReadAsSentences()
    {
        // The shell passes the creation time already converted to local time.
        Assert.Equal("Backup made: 8 October 2026, 16:12, by version 1.0.0.0",
            LibraryBackupMessages.BackupMade(new DateTime(2026, 10, 8, 16, 12, 0), "1.0.0.0"));
        Assert.Equal("Backup holds: 12 games, 40 guides, 310 MB",
            LibraryBackupMessages.BackupHolds(12, 40, 310L * 1024 * 1024));
        Assert.Equal("This library has: 9 games, 31 guides", LibraryBackupMessages.LibraryHas(9, 31));
    }

    [Fact]
    public void ReplacingAPopulatedLibraryWarns() =>
        Assert.Equal(
            "Your 9 games and 31 guides, with their reading progress, will be replaced by the backup's 12 games and 40 guides. This can't be undone. To keep the current library, export it first.",
            LibraryBackupMessages.ReplaceBody(9, 31, 12, 40));

    [Fact]
    public void ReplacingAnEmptyLibraryDoesnt() =>
        Assert.Equal("The backup's 12 games and 40 guides will be restored.",
            LibraryBackupMessages.ReplaceBody(0, 0, 12, 40));

    [Fact]
    public void TheRestoredMessageCountsTheLibrary() =>
        Assert.Equal("Library restored: 1 game, 2 guides.", LibraryBackupMessages.Restored(1, 2));
}
