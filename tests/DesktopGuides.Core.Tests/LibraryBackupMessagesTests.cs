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
}
