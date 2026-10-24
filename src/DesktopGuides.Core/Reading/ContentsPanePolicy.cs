namespace DesktopGuides.Core.Reading;

/// <summary>
/// T18.3: whether the Contents pane shows when a guide opens. It's as the
/// reader left it; until they've chosen, it opens for a guide of several
/// HTML pages. A narrow window keeps it closed, since there it covers the guide.
/// </summary>
public static class ContentsPanePolicy
{
    public static bool ShowOnOpen(bool? remembered, bool multiPageHtml, bool narrow) =>
        !narrow && (remembered ?? multiPageHtml);
}
