using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfGuideLoadMessagesTests
{
    [Theory]
    [InlineData(PdfGuideLoadError.Missing, "This guide's file is missing from the library. Remove it, then import the original again.")]
    [InlineData(PdfGuideLoadError.Changed, "This guide's file changed after it was imported, so it can't be opened safely. Remove it, then import the original again.")]
    [InlineData(PdfGuideLoadError.Unreadable, "This guide's file can't be opened. Close any app that's using it, then open the guide again.")]
    [InlineData(PdfGuideLoadError.Damaged, "This PDF is damaged, so it can't be opened. Re-import it from the original file.")]
    [InlineData(PdfGuideLoadError.PasswordProtected, "This PDF's protection isn't supported. Remove the password and re-import it.")]
    [InlineData(PdfGuideLoadError.Failed, "This guide stopped responding.")]
    [InlineData(PdfGuideLoadError.PasswordRequired, "This PDF needs a password.")]
    [InlineData(PdfGuideLoadError.PasswordIncorrect, "That password didn't open this PDF. Try again.")]
    public void EachErrorHasItsMessage(PdfGuideLoadError error, string message) =>
        Assert.Equal(message, PdfGuideLoadMessages.For(error));

    [Theory]
    [InlineData(PdfGuideLoadError.Missing, GuideLoadAction.Remove)]
    [InlineData(PdfGuideLoadError.Changed, GuideLoadAction.Remove)]
    [InlineData(PdfGuideLoadError.Unreadable, GuideLoadAction.Reopen)]
    [InlineData(PdfGuideLoadError.Damaged, GuideLoadAction.Remove)]
    [InlineData(PdfGuideLoadError.PasswordProtected, GuideLoadAction.None)]
    [InlineData(PdfGuideLoadError.Failed, GuideLoadAction.Reopen)]
    [InlineData(PdfGuideLoadError.PasswordRequired, GuideLoadAction.None)]
    [InlineData(PdfGuideLoadError.PasswordIncorrect, GuideLoadAction.None)]
    public void EachErrorHasItsAction(PdfGuideLoadError error, GuideLoadAction action) =>
        Assert.Equal(action, PdfGuideLoadMessages.ActionFor(error));

    [Theory]
    [InlineData(PdfGuideLoadError.Missing, GuideFileStatus.Missing)]
    [InlineData(PdfGuideLoadError.Changed, GuideFileStatus.Damaged)]
    [InlineData(PdfGuideLoadError.Damaged, GuideFileStatus.Damaged)]
    [InlineData(PdfGuideLoadError.Unreadable, null)]
    [InlineData(PdfGuideLoadError.PasswordProtected, null)]
    [InlineData(PdfGuideLoadError.Failed, null)]
    [InlineData(PdfGuideLoadError.PasswordRequired, null)]
    [InlineData(PdfGuideLoadError.PasswordIncorrect, null)]
    public void EachErrorMarksTheGuideOrNot(PdfGuideLoadError error, GuideFileStatus? status) =>
        Assert.Equal(status, PdfGuideLoadMessages.StatusFor(error));

    [Fact]
    public void ChangedMatchesTheHtmlCopy() =>
        Assert.Equal(HtmlGuideLoadMessages.For(HtmlGuideLoadError.Changed),
            PdfGuideLoadMessages.For(PdfGuideLoadError.Changed));

    [Fact]
    public void EveryErrorIsCovered()
    {
        foreach (PdfGuideLoadError error in Enum.GetValues<PdfGuideLoadError>())
        {
            Assert.NotEmpty(PdfGuideLoadMessages.For(error));
            _ = PdfGuideLoadMessages.ActionFor(error);
            _ = PdfGuideLoadMessages.StatusFor(error);
        }
    }

    [Fact]
    public void UnknownErrorsThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfGuideLoadMessages.For((PdfGuideLoadError)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfGuideLoadMessages.ActionFor((PdfGuideLoadError)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfGuideLoadMessages.StatusFor((PdfGuideLoadError)99));
    }

    [Fact]
    public void ActionLabelsMatchTheHtmlLabels()
    {
        Assert.Equal("Reopen", PdfGuideLoadMessages.ActionLabel(GuideLoadAction.Reopen));
        Assert.Equal("Remove guide", PdfGuideLoadMessages.ActionLabel(GuideLoadAction.Remove));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfGuideLoadMessages.ActionLabel(GuideLoadAction.None));
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
