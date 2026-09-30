using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideRemovalPresentationTests
{
    [Fact]
    public void DialogTitleNamesTheGuide() =>
        Assert.Equal("Remove Walkthrough?", GuideRemovalPresentation.DialogTitle("Walkthrough"));

    [Theory]
    [InlineData(0, "0 managed files")]
    [InlineData(1, "1 managed file")]
    [InlineData(3, "3 managed files")]
    public void DialogBodyCountsManagedFiles(int count, string files) =>
        Assert.Equal(
            $"This removes the guide, its reading progress, and its {files} from Desktop Guides. The original file you imported isn't affected.",
            GuideRemovalPresentation.DialogBody(count));

    [Theory]
    [InlineData(false, "Removed Walkthrough.")]
    [InlineData(true, "Removed Walkthrough. Leftover files will be cleaned up the next time Desktop Guides starts.")]
    public void RemovedMentionsPendingCleanup(bool cleanupPending, string expected) =>
        Assert.Equal(expected, GuideRemovalPresentation.Removed("Walkthrough", cleanupPending));

    [Fact]
    public void AlreadyRemovedNamesTheGuide() =>
        Assert.Equal("Walkthrough was already removed.", GuideRemovalPresentation.AlreadyRemoved("Walkthrough"));

    [Theory]
    [InlineData(GuideRemovalIssue.Unsafe, "Walkthrough can't be removed because its files were changed outside Desktop Guides.")]
    [InlineData(GuideRemovalIssue.Failed, "Walkthrough couldn't be removed. The guide is unchanged. Try again.")]
    [InlineData(GuideRemovalIssue.RestoreFailed, "Walkthrough couldn't be removed. Restart Desktop Guides to finish restoring it.")]
    public void ErrorDescribesEachIssue(GuideRemovalIssue issue, string expected) =>
        Assert.Equal(expected, GuideRemovalPresentation.Error(issue, "Walkthrough"));

    [Fact]
    public void ErrorRejectsAnUnknownIssue() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GuideRemovalPresentation.Error((GuideRemovalIssue)99, "Walkthrough"));

    [Fact]
    public void ExceptionCarriesItsIssueAndCause()
    {
        IOException cause = new("locked");

        GuideRemovalException error = new(GuideRemovalIssue.Failed, cause);

        Assert.Equal(GuideRemovalIssue.Failed, error.Issue);
        Assert.Same(cause, error.InnerException);
    }
}
