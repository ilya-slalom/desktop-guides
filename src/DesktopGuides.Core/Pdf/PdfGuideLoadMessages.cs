using DesktopGuides.Core.Html;

namespace DesktopGuides.Core.Pdf;

// New members go at the end, so existing values keep their numbers.
public enum PdfGuideLoadError { Missing, Changed, Unreadable, Damaged, PasswordProtected, Failed, PasswordRequired, PasswordIncorrect }

// Actions reuse HtmlGuideLoadAction because the Reader's error surface is
// typed to it; a PDF error only ever offers None or Reopen.
public static class PdfGuideLoadMessages
{
    public static string For(PdfGuideLoadError error) => error switch
    {
        PdfGuideLoadError.Missing => "This guide's file is missing from the library.",
        PdfGuideLoadError.Changed => "This guide's files have changed. Re-import it to read it.",
        PdfGuideLoadError.Unreadable =>
            "This guide's file can't be opened. Close any app that's using it, then open the guide again.",
        PdfGuideLoadError.Damaged => "This PDF is damaged, so it can't be opened. Re-import it from the original file.",
        PdfGuideLoadError.PasswordProtected =>
            "This PDF's protection isn't supported. Remove the password and re-import it.",
        PdfGuideLoadError.Failed => "This guide stopped responding.",
        PdfGuideLoadError.PasswordRequired => "This PDF needs a password.",
        PdfGuideLoadError.PasswordIncorrect => "That password didn't open this PDF. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static HtmlGuideLoadAction ActionFor(PdfGuideLoadError error) => error switch
    {
        PdfGuideLoadError.Unreadable or PdfGuideLoadError.Failed => HtmlGuideLoadAction.Reopen,
        PdfGuideLoadError.Missing or PdfGuideLoadError.Changed or PdfGuideLoadError.Damaged or
            PdfGuideLoadError.PasswordProtected or PdfGuideLoadError.PasswordRequired or
            PdfGuideLoadError.PasswordIncorrect => HtmlGuideLoadAction.None,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static string ActionLabel(HtmlGuideLoadAction action) => HtmlGuideLoadMessages.ActionLabel(action);
}
