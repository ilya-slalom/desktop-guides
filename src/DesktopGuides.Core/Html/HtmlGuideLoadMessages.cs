namespace DesktopGuides.Core.Html;

// New members go at the end, so existing values keep their numbers.
public enum HtmlGuideLoadError { RuntimeMissing, NoManifest, Changed, Missing, RuntimeFailed, Crashed }

// How far the entry page got during an open: never asked for, served, or
// refused because its managed copy changed or is missing.
public enum HtmlEntryState { NotRequested, Served, Changed, Missing }

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

    // The error for a first navigation that failed or timed out. Only a
    // refused entry page means the guide's files are at fault.
    public static HtmlGuideLoadError ForFailedOpen(HtmlEntryState entry, bool crashed) =>
        crashed ? HtmlGuideLoadError.Crashed : entry switch
        {
            HtmlEntryState.NotRequested => HtmlGuideLoadError.RuntimeFailed,
            HtmlEntryState.Served => HtmlGuideLoadError.Crashed,
            HtmlEntryState.Changed => HtmlGuideLoadError.Changed,
            HtmlEntryState.Missing => HtmlGuideLoadError.Missing,
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
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
