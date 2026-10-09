using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Html;

// New members go at the end, so existing values keep their numbers.
public enum HtmlGuideLoadError { RuntimeMissing, NoManifest, Changed, Missing, RuntimeFailed, Crashed }

// How far the entry page got during an open: never asked for, served, or
// refused because its managed copy changed or is missing.
public enum HtmlEntryState { NotRequested, Served, Changed, Missing }

public static class HtmlGuideLoadMessages
{
    public const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    // T15.1: the startup check, shown before any web page guide is opened.
    public const string RuntimeMissingAtStartup =
        "Web page guides need the Microsoft Edge WebView2 Runtime. Text and PDF guides still open.";

    public static string For(HtmlGuideLoadError error) => error switch
    {
        HtmlGuideLoadError.RuntimeMissing => "Web page guides need the Microsoft Edge WebView2 Runtime.",
        HtmlGuideLoadError.RuntimeFailed => "Web page guides couldn't start.",
        HtmlGuideLoadError.Crashed => "This guide stopped responding.",
        HtmlGuideLoadError.Missing =>
            "This guide's file is missing from the library. Remove it, then import the original again.",
        HtmlGuideLoadError.NoManifest => "Re-import this guide to read it.",
        HtmlGuideLoadError.Changed =>
            "This guide's file changed after it was imported, so it can't be opened safely. Remove it, then import the original again.",
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

    public static GuideLoadAction ActionFor(HtmlGuideLoadError error) => error switch
    {
        HtmlGuideLoadError.RuntimeMissing => GuideLoadAction.GetRuntime,
        HtmlGuideLoadError.RuntimeFailed or HtmlGuideLoadError.Crashed => GuideLoadAction.Reopen,
        HtmlGuideLoadError.Missing or HtmlGuideLoadError.NoManifest or HtmlGuideLoadError.Changed =>
            GuideLoadAction.Remove,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    // What a failed open says about the guide's file; null leaves it as it was.
    public static GuideFileStatus? StatusFor(HtmlGuideLoadError error) => error switch
    {
        HtmlGuideLoadError.Missing => GuideFileStatus.Missing,
        HtmlGuideLoadError.NoManifest or HtmlGuideLoadError.Changed => GuideFileStatus.Damaged,
        HtmlGuideLoadError.RuntimeMissing or HtmlGuideLoadError.RuntimeFailed or HtmlGuideLoadError.Crashed => null,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static string ActionLabel(GuideLoadAction action) => action switch
    {
        GuideLoadAction.GetRuntime => "Get WebView2 Runtime",
        GuideLoadAction.Reopen => "Reopen",
        GuideLoadAction.Remove => "Remove guide",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
