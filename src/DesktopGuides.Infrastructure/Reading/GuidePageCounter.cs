using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Infrastructure.Reading;

/// <summary>
/// T21.2: matches in a guide's saved pages, for search across a guide of
/// several pages. The counts come from each page's saved text, so they can
/// differ a little from what the open page's live search finds; the open
/// page's live count wins.
/// </summary>
public static class GuidePageCounter
{
    // The reader runs pages with scripts off, so <noscript> content is
    // shown, and counted.
    public static string TextOf(byte[] html)
    {
        ArgumentNullException.ThrowIfNull(html);
        using MemoryStream stream = new(html, writable: false);
        IDocument document = new HtmlParser().ParseDocument(stream);
        foreach (IElement hidden in document.QuerySelectorAll("script, style, template").ToArray())
        {
            hidden.Remove();
        }
        return document.Body?.TextContent ?? string.Empty;
    }

    // One count per page; a page that can't be read counts nothing. A
    // cancelled count is no count, never a partial one.
    public static int[]? Count(
        IReadOnlyList<GuideAsset> pages, Func<GuideAsset, HtmlAssetRead> read, FindQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(query);
        int[] counts = new int[pages.Count];
        for (int index = 0; index < pages.Count; index++)
        {
            if (token.IsCancellationRequested) return null;
            HtmlAssetRead page = read(pages[index]);
            counts[index] = page.Status == HtmlAssetReadStatus.Served && page.Bytes is byte[] bytes
                ? TextMatcher.FindAll(TextOf(bytes), query, TextMatcher.MaxMatches).Count
                : 0;
        }
        return token.IsCancellationRequested ? null : counts;
    }
}
