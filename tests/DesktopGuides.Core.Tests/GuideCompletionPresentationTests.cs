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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyAStoredTimeIsComplete(bool stored) =>
        Assert.Equal(stored, GuideCompletionPresentation.IsComplete(stored ? Finished : null));

    [Fact]
    public void ChoiceNameNamesTheGuide() =>
        Assert.Equal("Completion for Walkthrough", GuideCompletionPresentation.ChoiceName("Walkthrough"));

    [Theory]
    [InlineData(true, "Walkthrough marked complete.")]
    [InlineData(false, "Walkthrough marked in progress.")]
    public void AnnouncementNamesTheNewState(bool complete, string expected) =>
        Assert.Equal(expected,
            GuideCompletionPresentation.Announcement("Walkthrough", complete ? Finished : null));

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
