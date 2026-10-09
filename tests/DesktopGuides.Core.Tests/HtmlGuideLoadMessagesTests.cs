using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlGuideLoadMessagesTests
{
    [Theory]
    [InlineData(HtmlGuideLoadError.RuntimeMissing, "Web page guides need the Microsoft Edge WebView2 Runtime.")]
    [InlineData(HtmlGuideLoadError.RuntimeFailed, "Web page guides couldn't start.")]
    [InlineData(HtmlGuideLoadError.Crashed, "This guide stopped responding.")]
    [InlineData(HtmlGuideLoadError.Missing, "This guide's file is missing from the library. Remove it, then import the original again.")]
    [InlineData(HtmlGuideLoadError.NoManifest, "Re-import this guide to read it.")]
    [InlineData(HtmlGuideLoadError.Changed, "This guide's file changed after it was imported, so it can't be opened safely. Remove it, then import the original again.")]
    public void EachErrorHasOneSentence(HtmlGuideLoadError error, string message) =>
        Assert.Equal(message, HtmlGuideLoadMessages.For(error));

    [Theory]
    [InlineData(HtmlGuideLoadError.RuntimeMissing, GuideLoadAction.GetRuntime)]
    [InlineData(HtmlGuideLoadError.RuntimeFailed, GuideLoadAction.Reopen)]
    [InlineData(HtmlGuideLoadError.Crashed, GuideLoadAction.Reopen)]
    [InlineData(HtmlGuideLoadError.Missing, GuideLoadAction.Remove)]
    [InlineData(HtmlGuideLoadError.NoManifest, GuideLoadAction.Remove)]
    [InlineData(HtmlGuideLoadError.Changed, GuideLoadAction.Remove)]
    public void EachErrorHasOneAction(HtmlGuideLoadError error, GuideLoadAction action) =>
        Assert.Equal(action, HtmlGuideLoadMessages.ActionFor(error));

    // A stalled or failed first navigation is only "changed" when the entry
    // page itself failed its hash check.
    [Theory]
    [InlineData(HtmlEntryState.NotRequested, false, HtmlGuideLoadError.RuntimeFailed)]
    [InlineData(HtmlEntryState.Served, false, HtmlGuideLoadError.Crashed)]
    [InlineData(HtmlEntryState.Changed, false, HtmlGuideLoadError.Changed)]
    [InlineData(HtmlEntryState.Missing, false, HtmlGuideLoadError.Missing)]
    [InlineData(HtmlEntryState.NotRequested, true, HtmlGuideLoadError.Crashed)]
    [InlineData(HtmlEntryState.Changed, true, HtmlGuideLoadError.Crashed)]
    public void AFailedOpenNamesWhatFailed(HtmlEntryState entry, bool crashed, HtmlGuideLoadError error) =>
        Assert.Equal(error, HtmlGuideLoadMessages.ForFailedOpen(entry, crashed));

    [Theory]
    [InlineData(GuideLoadAction.GetRuntime, "Get WebView2 Runtime")]
    [InlineData(GuideLoadAction.Reopen, "Reopen")]
    [InlineData(GuideLoadAction.Remove, "Remove guide")]
    public void EachActionHasALabel(GuideLoadAction action, string label) =>
        Assert.Equal(label, HtmlGuideLoadMessages.ActionLabel(action));

    [Theory]
    [InlineData(HtmlGuideLoadError.Missing, GuideFileStatus.Missing)]
    [InlineData(HtmlGuideLoadError.NoManifest, GuideFileStatus.Damaged)]
    [InlineData(HtmlGuideLoadError.Changed, GuideFileStatus.Damaged)]
    [InlineData(HtmlGuideLoadError.RuntimeMissing, null)]
    [InlineData(HtmlGuideLoadError.RuntimeFailed, null)]
    [InlineData(HtmlGuideLoadError.Crashed, null)]
    public void EachErrorMarksTheGuideOrNot(HtmlGuideLoadError error, GuideFileStatus? status) =>
        Assert.Equal(status, HtmlGuideLoadMessages.StatusFor(error));

    [Fact]
    public void NoActionHasNoLabel() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.ActionLabel(GuideLoadAction.None));

    [Fact]
    public void MissingMatchesTheTextGuideCopy() =>
        Assert.Equal(TextGuideLoadMessages.For(TextGuideLoadError.Missing),
            HtmlGuideLoadMessages.For(HtmlGuideLoadError.Missing));

    [Fact]
    public void RuntimeDownloadUrlIsMicrosoftsHttpsPage()
    {
        Uri url = new(HtmlGuideLoadMessages.RuntimeDownloadUrl, UriKind.Absolute);
        Assert.Equal(Uri.UriSchemeHttps, url.Scheme);
        Assert.Equal("developer.microsoft.com", url.Host);
    }

    [Fact]
    public void UndefinedErrorsThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.For((HtmlGuideLoadError)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.ActionFor((HtmlGuideLoadError)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.StatusFor((HtmlGuideLoadError)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.ActionLabel((GuideLoadAction)99));
    }
}
