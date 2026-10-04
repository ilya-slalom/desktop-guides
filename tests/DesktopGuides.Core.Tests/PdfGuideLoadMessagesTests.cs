using DesktopGuides.Core.Html;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfGuideLoadMessagesTests
{
    [Theory]
    [InlineData(PdfGuideLoadError.Missing, "This guide's file is missing from the library.")]
    [InlineData(PdfGuideLoadError.Changed, "This guide's files have changed. Re-import it to read it.")]
    [InlineData(PdfGuideLoadError.Unreadable, "This guide's file can't be opened. Close any app that's using it, then open the guide again.")]
    [InlineData(PdfGuideLoadError.Damaged, "This PDF is damaged, so it can't be opened. Re-import it from the original file.")]
    [InlineData(PdfGuideLoadError.PasswordProtected, "This PDF now needs a password, which isn't supported. Remove the password and re-import it.")]
    [InlineData(PdfGuideLoadError.Failed, "This guide stopped responding.")]
    public void EachErrorHasItsMessage(PdfGuideLoadError error, string message) =>
        Assert.Equal(message, PdfGuideLoadMessages.For(error));

    [Theory]
    [InlineData(PdfGuideLoadError.Missing, HtmlGuideLoadAction.None)]
    [InlineData(PdfGuideLoadError.Changed, HtmlGuideLoadAction.None)]
    [InlineData(PdfGuideLoadError.Unreadable, HtmlGuideLoadAction.Reopen)]
    [InlineData(PdfGuideLoadError.Damaged, HtmlGuideLoadAction.None)]
    [InlineData(PdfGuideLoadError.PasswordProtected, HtmlGuideLoadAction.None)]
    [InlineData(PdfGuideLoadError.Failed, HtmlGuideLoadAction.Reopen)]
    public void EachErrorHasItsAction(PdfGuideLoadError error, HtmlGuideLoadAction action) =>
        Assert.Equal(action, PdfGuideLoadMessages.ActionFor(error));

    [Fact]
    public void EveryErrorIsCovered()
    {
        foreach (PdfGuideLoadError error in Enum.GetValues<PdfGuideLoadError>())
        {
            Assert.NotEmpty(PdfGuideLoadMessages.For(error));
            _ = PdfGuideLoadMessages.ActionFor(error);
        }
    }

    [Fact]
    public void UnknownErrorsThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfGuideLoadMessages.For((PdfGuideLoadError)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfGuideLoadMessages.ActionFor((PdfGuideLoadError)99));
    }

    [Fact]
    public void ReopenHasALabelAndNoneHasNone()
    {
        Assert.Equal("Reopen", PdfGuideLoadMessages.ActionLabel(HtmlGuideLoadAction.Reopen));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfGuideLoadMessages.ActionLabel(HtmlGuideLoadAction.None));
    }

    [Fact]
    public void MissingAndUnreadableUseTheTextWording()
    {
        Assert.Equal(TextGuideLoadMessages.For(TextGuideLoadError.Missing),
            PdfGuideLoadMessages.For(PdfGuideLoadError.Missing));
        Assert.Equal(TextGuideLoadMessages.For(TextGuideLoadError.Unreadable),
            PdfGuideLoadMessages.For(PdfGuideLoadError.Unreadable));
    }
}
