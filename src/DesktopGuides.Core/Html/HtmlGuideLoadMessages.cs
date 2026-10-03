namespace DesktopGuides.Core.Html;

public enum HtmlGuideLoadError { RuntimeMissing, NoManifest, Changed }

public static class HtmlGuideLoadMessages
{
    public static string For(HtmlGuideLoadError error) => error switch
    {
        HtmlGuideLoadError.RuntimeMissing => "Web page guides need the Microsoft Edge WebView2 Runtime.",
        HtmlGuideLoadError.NoManifest => "Re-import this guide to read it.",
        HtmlGuideLoadError.Changed => "This guide's files have changed. Re-import it to read it.",
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };
}
