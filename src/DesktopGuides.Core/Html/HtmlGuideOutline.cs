using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Html;

/// <summary>
/// T21.2: a guide of several pages lists every page, by its title, in
/// reading order; the open page's own outline nests beneath its entry. A
/// one-page guide keeps its own outline.
/// </summary>
public static class HtmlGuideOutline
{
    public static Outline Build(HtmlGuidePages pages, int openPage, Outline openOutline, string contentSha256)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(openOutline);
        if (pages.Count == 1) return openOutline;
        OutlineEntry[] entries = new OutlineEntry[pages.Count];
        for (int index = 0; index < pages.Count; index++)
        {
            ReaderLocation top = HtmlLocationRules.Capture(
                contentSha256, pages[index].RequestPath, new HtmlCapture(0, null, null, 0), index, pages.Count);
            IReadOnlyList<OutlineEntry> children = index == openPage
                ? [.. openOutline.Entries.Select(entry => Nest(entry, pages, openPage))]
                : [];
            entries[index] = new OutlineEntry(pages.DisplayTitles[index], 1, top, pages.OrderOf(index, 0), children);
        }
        return new Outline(OutlineSource.Document, entries);
    }

    private static OutlineEntry Nest(OutlineEntry entry, HtmlGuidePages pages, int page) =>
        entry with
        {
            Level = entry.Level + 1,
            Order = pages.OrderOf(page, entry.Order),
            Children = [.. entry.Children.Select(child => Nest(child, pages, page))],
        };
}
