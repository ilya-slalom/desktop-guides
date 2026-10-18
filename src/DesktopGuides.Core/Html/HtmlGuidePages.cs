namespace DesktopGuides.Core.Html;

/// <summary>
/// T21.2: a guide's pages in reading order, found by the address the reader
/// serves them at or by the website address each page recorded.
/// </summary>
public sealed class HtmlGuidePages
{
    private readonly GuidePage[] pages;
    private readonly Uri[] addresses;
    private readonly Dictionary<string, int> byOriginal = new(StringComparer.Ordinal);

    public HtmlGuidePages(Guid guideId, IReadOnlyList<GuidePage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0 || pages.Where((page, index) => page.Order != index).Any())
        {
            throw new ArgumentException("A guide's pages run from its entry, page 0, in order.", nameof(pages));
        }
        this.pages = [.. pages];
        DisplayTitles = PageTitles.WithoutSharedSuffix(
            [.. pages.Select(page => page.Title ?? Path.GetFileNameWithoutExtension(page.RequestPath))]);
        addresses = [.. pages.Select(page => GuideWebOrigin.EntryUri(guideId, page.RequestPath))];
        foreach (GuidePage page in pages)
        {
            if (SavedPageUrl.Parse(page.OriginalUrl) is Uri original)
            {
                byOriginal.TryAdd(SavedPageUrl.Key(original), page.Order);
            }
        }
    }

    public int Count => pages.Length;

    // T21.2: each page's title for the contents and bookmarks, without the
    // suffix every page shares; a page without one is named by its file.
    public IReadOnlyList<string> DisplayTitles { get; }

    // T21.2: a place's reading order across pages, so the same offset on
    // two pages is two places.
    public long OrderOf(int pageIndex, long textOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, pages.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(textOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(textOffset, uint.MaxValue);
        return ((long)pageIndex << 32) | textOffset;
    }

    public GuidePage this[int index] => pages[index];

    public IReadOnlyList<string> DocumentPaths => [.. pages.Select(page => page.RequestPath)];

    public Uri AddressOf(int index) => addresses[index];

    public int? IndexOf(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        for (int index = 0; index < addresses.Length; index++)
        {
            if (HtmlNavigationPolicy.IsEntryDocument(target, addresses[index])) return index;
        }
        return SavedPageUrl.Parse(target.AbsoluteUri) is Uri web &&
            byOriginal.TryGetValue(SavedPageUrl.Key(web), out int found)
                ? found
                : null;
    }

    public int? IndexOfDocument(string documentPath)
    {
        int index = Array.FindIndex(pages, page => string.Equals(page.RequestPath, documentPath, StringComparison.Ordinal));
        return index >= 0 ? index : null;
    }

    // Previous and Next page stop at the first and last pages.
    public int Step(int current, int delta) => Math.Clamp(current + delta, 0, pages.Length - 1);
}
