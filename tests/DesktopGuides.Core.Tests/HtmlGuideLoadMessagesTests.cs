using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlGuideLoadMessagesTests
{
    [Theory]
    [InlineData(HtmlGuideLoadError.RuntimeMissing, "Web page guides need the Microsoft Edge WebView2 Runtime.")]
    [InlineData(HtmlGuideLoadError.NoManifest, "Re-import this guide to read it.")]
    [InlineData(HtmlGuideLoadError.Changed, "This guide's files have changed. Re-import it to read it.")]
    public void EachErrorHasOneSentence(HtmlGuideLoadError error, string message) =>
        Assert.Equal(message, HtmlGuideLoadMessages.For(error));

    [Fact]
    public void UndefinedErrorsThrow() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.For((HtmlGuideLoadError)99));
}
