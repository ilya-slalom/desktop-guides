using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class LibraryOpenMessagesTests
{
    [Theory]
    [InlineData(LibraryOpenIssue.Damaged, "Library can't be read",
        "Your library database can't be read. Desktop Guides stopped before changing anything. Your guide files are still in the data folder.")]
    [InlineData(LibraryOpenIssue.Missing, "Library database is missing",
        "Your library database is missing, but your guide files are still in the data folder. Desktop Guides stopped before changing anything. Restore library.sqlite, then try again.")]
    [InlineData(LibraryOpenIssue.NewerVersion, "Update Desktop Guides",
        "Your library was saved by a newer version of Desktop Guides. Desktop Guides stopped before changing anything. Update Desktop Guides, then try again.")]
    [InlineData(LibraryOpenIssue.MigrationFailed, "Library upgrade failed",
        "Your library couldn't be upgraded. Desktop Guides stopped before changing anything and kept a copy of your library from before the upgrade.")]
    [InlineData(LibraryOpenIssue.Locked, "Library is in use",
        "Another program may be using your library. Desktop Guides stopped before changing anything. Close that program, then try again.")]
    [InlineData(LibraryOpenIssue.NoAccess, "Library can't be accessed",
        "Desktop Guides doesn't have permission to read or change your library. Desktop Guides stopped before changing anything. Check the data folder's permissions, then try again.")]
    [InlineData(LibraryOpenIssue.DiskFull, "Not enough disk space",
        "There isn't enough free disk space to open your library. Desktop Guides stopped before changing anything. Free up some space, then try again.")]
    public void EachIssueHasItsTitleAndBody(LibraryOpenIssue issue, string title, string body) =>
        Assert.Equal(new LibraryOpenMessage(title, body), LibraryOpenMessages.For(issue));

    [Fact]
    public void EveryBodySaysNothingWasChanged()
    {
        // A failed restore is the one case where the library may have changed.
        foreach (LibraryOpenIssue issue in Enum.GetValues<LibraryOpenIssue>()
                     .Where(issue => issue != LibraryOpenIssue.RestoreIncomplete))
        {
            Assert.Contains(
                "Desktop Guides stopped before changing anything",
                LibraryOpenMessages.For(issue).Body);
        }
    }

    [Fact]
    public void AnUnfinishedRestoreNamesTheRecoveryFolder()
    {
        LibraryOpenMessage message = LibraryOpenMessages.For(LibraryOpenIssue.RestoreIncomplete);

        Assert.Equal("Restore didn't finish", message.Title);
        Assert.Equal(
            "A restore didn't finish and Desktop Guides couldn't put your previous library back. Your previous library is in the .recovery folder inside the data folder. Close other programs that might be using it, then try again.",
            message.Body);
    }

    [Fact]
    public void AnUnknownIssueThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LibraryOpenMessages.For((LibraryOpenIssue)99));

    [Fact]
    public void DataFolderNamesThePath() =>
        Assert.Equal(@"Data folder: C:\Data", LibraryOpenMessages.DataFolder(@"C:\Data"));

    [Fact]
    public void RecoveryCopyNamesThePath() =>
        Assert.Equal(
            @"Copy from before the upgrade: C:\Data\.recovery\library.sqlite",
            LibraryOpenMessages.RecoveryCopy(@"C:\Data\.recovery\library.sqlite"));

    [Fact]
    public void ExceptionCarriesItsIssuePathAndCause()
    {
        IOException cause = new("injected");

        LibraryOpenException error = new(LibraryOpenIssue.MigrationFailed, @"C:\copy.sqlite", cause);

        Assert.Equal(LibraryOpenIssue.MigrationFailed, error.Issue);
        Assert.Equal(@"C:\copy.sqlite", error.RecoveryCopyPath);
        Assert.Same(cause, error.InnerException);
        Assert.Equal(LibraryOpenMessages.For(LibraryOpenIssue.MigrationFailed).Body, error.Message);
    }

    [Fact]
    public void ExceptionDefaultsHaveNoPathOrCause()
    {
        LibraryOpenException error = new(LibraryOpenIssue.Missing);

        Assert.Null(error.RecoveryCopyPath);
        Assert.Null(error.InnerException);
    }
}
