using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Pdf;

// New members go at the end, so existing values keep their numbers.
public enum PdfGuideLoadError { Missing, Changed, Unreadable, Damaged, PasswordProtected, Failed, PasswordRequired, PasswordIncorrect }

// Action labels come from HtmlGuideLoadMessages so every format reads alike.
public static class PdfGuideLoadMessages
{
    public static string For(PdfGuideLoadError error) => error switch
    {
        PdfGuideLoadError.Missing =>
            "This guide's file is missing from the library. Remove it, then import the original again.",
        PdfGuideLoadError.Changed =>
            "This guide's file changed after it was imported, so it can't be opened safely. Remove it, then import the original again.",
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

    public static GuideLoadAction ActionFor(PdfGuideLoadError error) => error switch
    {
        PdfGuideLoadError.Unreadable or PdfGuideLoadError.Failed => GuideLoadAction.Reopen,
        PdfGuideLoadError.Missing or PdfGuideLoadError.Changed or PdfGuideLoadError.Damaged =>
            GuideLoadAction.Remove,
        PdfGuideLoadError.PasswordProtected or PdfGuideLoadError.PasswordRequired or
            PdfGuideLoadError.PasswordIncorrect => GuideLoadAction.None,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    // What a failed open says about the guide's file; null leaves it as it was.
    public static GuideFileStatus? StatusFor(PdfGuideLoadError error) => error switch
    {
        PdfGuideLoadError.Missing => GuideFileStatus.Missing,
        PdfGuideLoadError.Changed or PdfGuideLoadError.Damaged => GuideFileStatus.Damaged,
        PdfGuideLoadError.Unreadable or PdfGuideLoadError.PasswordProtected or PdfGuideLoadError.Failed or
            PdfGuideLoadError.PasswordRequired or PdfGuideLoadError.PasswordIncorrect => null,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static string ActionLabel(GuideLoadAction action) => HtmlGuideLoadMessages.ActionLabel(action);
}
