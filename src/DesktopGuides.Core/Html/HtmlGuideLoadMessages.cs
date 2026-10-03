namespace DesktopGuides.Core.Html;

// New members go at the end, so existing values keep their numbers.
public enum HtmlGuideLoadError { RuntimeMissing, NoManifest, Changed, Missing, RuntimeFailed, Crashed }

// The one thing the Reader offers to do about an error.
public enum HtmlGuideLoadAction { None, GetRuntime, Reopen }

public static class HtmlGuideLoadMessages
{
    public const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    public static string For(HtmlGuideLoadError error) => error switch
    {
        HtmlGuideLoadError.RuntimeMissing => "Web page guides need the Microsoft Edge WebView2 Runtime.",
        HtmlGuideLoadError.RuntimeFailed => "Web page guides couldn't start.",
        HtmlGuideLoadError.Crashed => "This guide stopped responding.",
        HtmlGuideLoadError.Missing => "This guide's file is missing from the library.",
        HtmlGuideLoadError.NoManifest => "Re-import this guide to read it.",
        HtmlGuideLoadError.Changed => "This guide's files have changed. Re-import it to read it.",
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static HtmlGuideLoadAction ActionFor(HtmlGuideLoadError error) => error switch
    {
        HtmlGuideLoadError.RuntimeMissing => HtmlGuideLoadAction.GetRuntime,
        HtmlGuideLoadError.RuntimeFailed or HtmlGuideLoadError.Crashed => HtmlGuideLoadAction.Reopen,
        HtmlGuideLoadError.Missing or HtmlGuideLoadError.NoManifest or HtmlGuideLoadError.Changed =>
            HtmlGuideLoadAction.None,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static string ActionLabel(HtmlGuideLoadAction action) => action switch
    {
        HtmlGuideLoadAction.GetRuntime => "Get WebView2 Runtime",
        HtmlGuideLoadAction.Reopen => "Reopen",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
