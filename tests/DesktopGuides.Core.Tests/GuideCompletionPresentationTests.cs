using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideCompletionPresentationTests
{
    private static readonly DateTimeOffset Finished =
        new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LabelsNameBothStates()
    {
        Assert.Equal("In progress", GuideCompletionPresentation.InProgressLabel);
        Assert.Equal("Complete", GuideCompletionPresentation.CompleteLabel);
    }

    [Fact]
    public void NoStoredTimeIsInProgress() =>
        Assert.False(GuideCompletionPresentation.IsComplete(null));

    [Fact]
    public void AStoredTimeIsComplete() =>
        Assert.True(GuideCompletionPresentation.IsComplete(Finished));

    [Fact]
    public void ChoiceNameNamesTheGuide() =>
        Assert.Equal("Completion for Walkthrough", GuideCompletionPresentation.ChoiceName("Walkthrough"));

    [Fact]
    public void AnnouncementForComplete() =>
        Assert.Equal("Walkthrough marked complete.",
            GuideCompletionPresentation.Announcement("Walkthrough", Finished));

    [Fact]
    public void AnnouncementForInProgress() =>
        Assert.Equal("Walkthrough marked in progress.",
            GuideCompletionPresentation.Announcement("Walkthrough", null));

    [Fact]
    public void SaveFailedOffersARetry() =>
        Assert.Equal("Couldn't update completion for Walkthrough. Try again.",
            GuideCompletionPresentation.SaveFailed("Walkthrough"));

    [Fact]
    public void RemovedMatchesTheReaderMessage() =>
        Assert.Equal("This guide is no longer in your library.", GuideCompletionPresentation.Removed);

    [Fact]
    public void TitlesAreKeptVerbatim()
    {
        string title = "Ōkami 大神 <b>&amp;</b> " + new string('x', 300);
        Assert.Equal($"Completion for {title}", GuideCompletionPresentation.ChoiceName(title));
        Assert.Equal($"{title} marked complete.", GuideCompletionPresentation.Announcement(title, Finished));
        Assert.Equal($"Couldn't update completion for {title}. Try again.",
            GuideCompletionPresentation.SaveFailed(title));
    }
}
