using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Text;

public static class TextGuideLoadMessages
{
    public static string For(TextGuideLoadError error) => error switch
    {
        TextGuideLoadError.Missing =>
            "This guide's file is missing from the library. Remove it, then import the original again.",
        TextGuideLoadError.TooLarge => "This guide is larger than the 64 MB limit for text files.",
        TextGuideLoadError.Unreadable =>
            "This guide's file can't be opened. Close any app that's using it, then open the guide again.",
        TextGuideLoadError.InvalidMetadata => "This guide's saved details are damaged, so it can't be opened.",
        TextGuideLoadError.NotUtf8 => "This guide isn't valid UTF-8 text, so it can't be opened.",
        TextGuideLoadError.Undecodable => "This guide can't be read with its saved encoding.",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };

    public static GuideLoadAction ActionFor(TextGuideLoadError error) => error switch
    {
        TextGuideLoadError.Missing or TextGuideLoadError.InvalidMetadata => GuideLoadAction.Remove,
        TextGuideLoadError.TooLarge or TextGuideLoadError.Unreadable or TextGuideLoadError.NotUtf8 or
            TextGuideLoadError.Undecodable => GuideLoadAction.None,
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };

    // What a failed open says about the guide's file; null leaves it as it was.
    public static GuideFileStatus? StatusFor(TextGuideLoadError error) => error switch
    {
        TextGuideLoadError.Missing => GuideFileStatus.Missing,
        TextGuideLoadError.InvalidMetadata => GuideFileStatus.Damaged,
        TextGuideLoadError.TooLarge or TextGuideLoadError.Unreadable or TextGuideLoadError.NotUtf8 or
            TextGuideLoadError.Undecodable => null,
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };
}
