namespace DesktopGuides.Core.Text;

public static class TextGuideLoadMessages
{
    public static string For(TextGuideLoadError error) => error switch
    {
        TextGuideLoadError.Missing => "This guide's file is missing from the library.",
        TextGuideLoadError.TooLarge => "This guide is larger than the 64 MB limit for text files.",
        TextGuideLoadError.Unreadable =>
            "This guide's file can't be opened. Close any app that's using it, then open the guide again.",
        TextGuideLoadError.InvalidMetadata => "This guide's saved details are damaged, so it can't be opened.",
        TextGuideLoadError.NotUtf8 => "This guide isn't valid UTF-8 text, so it can't be opened.",
        TextGuideLoadError.Undecodable => "This guide can't be read with its saved encoding.",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };
}
