using DesktopGuides.Core.Html;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlGuideLoadMessagesTests
{
    [Theory]
    [InlineData(HtmlGuideLoadError.RuntimeMissing, "Web page guides need the Microsoft Edge WebView2 Runtime.")]
    [InlineData(HtmlGuideLoadError.RuntimeFailed, "Web page guides couldn't start.")]
    [InlineData(HtmlGuideLoadError.Crashed, "This guide stopped responding.")]
    [InlineData(HtmlGuideLoadError.Missing, "This guide's file is missing from the library.")]
    [InlineData(HtmlGuideLoadError.NoManifest, "Re-import this guide to read it.")]
    [InlineData(HtmlGuideLoadError.Changed, "This guide's files have changed. Re-import it to read it.")]
    public void EachErrorHasOneSentence(HtmlGuideLoadError error, string message) =>
        Assert.Equal(message, HtmlGuideLoadMessages.For(error));

    [Theory]
    [InlineData(HtmlGuideLoadError.RuntimeMissing, HtmlGuideLoadAction.GetRuntime)]
    [InlineData(HtmlGuideLoadError.RuntimeFailed, HtmlGuideLoadAction.Reopen)]
    [InlineData(HtmlGuideLoadError.Crashed, HtmlGuideLoadAction.Reopen)]
    [InlineData(HtmlGuideLoadError.Missing, HtmlGuideLoadAction.None)]
    [InlineData(HtmlGuideLoadError.NoManifest, HtmlGuideLoadAction.None)]
    [InlineData(HtmlGuideLoadError.Changed, HtmlGuideLoadAction.None)]
    public void EachErrorHasOneAction(HtmlGuideLoadError error, HtmlGuideLoadAction action) =>
        Assert.Equal(action, HtmlGuideLoadMessages.ActionFor(error));

    [Theory]
    [InlineData(HtmlGuideLoadAction.GetRuntime, "Get WebView2 Runtime")]
    [InlineData(HtmlGuideLoadAction.Reopen, "Reopen")]
    public void EachActionHasALabel(HtmlGuideLoadAction action, string label) =>
        Assert.Equal(label, HtmlGuideLoadMessages.ActionLabel(action));

    [Fact]
    public void NoActionHasNoLabel() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.ActionLabel(HtmlGuideLoadAction.None));

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
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.ActionLabel((HtmlGuideLoadAction)99));
    }
}
