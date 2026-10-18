using System.Globalization;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Reading;

// T19.1: a stored bookmark with its decoded place. A locator that doesn't
// decode leaves Location null: a damaged bookmark, offered only Delete.
// Stale: saved against other content, so it is followed but isn't "here".
public sealed record BookmarkItem(Bookmark Bookmark, ReaderLocation? Location, long Order, bool Stale)
{
    public bool IsDamaged => Location is null;
}

public static class BookmarkList
{
    public const string DamagedLabel = "Damaged bookmark";
    public const string FallbackLabel = "Bookmark";
    public const string Added = "Bookmark added.";
    public const string AlreadyBookmarked = "This place is already bookmarked.";
    public const string Deleted = "Bookmark deleted.";
    public const string UndoLabel = "Undo";
    public const string Full = "This guide has 500 bookmarks. Delete one to add another.";
    public const string SaveFailed = "Couldn't save the bookmark.";
    public const string Shifted = "This bookmark's place may have shifted.";
    public const string NotFound = "This bookmark's place couldn't be found.";
    public static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(10);

    // Reading order, the older first at one place; damaged bookmarks last.
    public static IReadOnlyList<BookmarkItem> Arrange(
        IEnumerable<Bookmark> bookmarks,
        Func<string, LocationDecodeResult> decode,
        Func<ReaderLocation, long> orderOf)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        ArgumentNullException.ThrowIfNull(decode);
        ArgumentNullException.ThrowIfNull(orderOf);
        List<BookmarkItem> items = [];
        foreach (Bookmark bookmark in bookmarks)
        {
            LocationDecodeResult decoded = decode(bookmark.LocatorJson);
            ReaderLocation? location =
                decoded.Status is LocationDecodeStatus.Valid or LocationDecodeStatus.ContentChanged
                    ? decoded.Location
                    : null;
            long order = long.MaxValue;
            if (location is not null)
            {
                try
                {
                    order = orderOf(location);
                }
                catch (Exception)
                {
                    // A place the reader can't order is as good as damaged.
                    location = null;
                }
            }
            items.Add(new BookmarkItem(
                bookmark, location, order, decoded.Status == LocationDecodeStatus.ContentChanged));
        }
        return items
            .OrderBy(item => item.IsDamaged)
            .ThenBy(item => item.Order)
            .ThenBy(item => item.Bookmark.CreatedUtc)
            .ThenBy(item => item.Bookmark.Id)
            .ToArray();
    }

    // The bookmark already at this place, if one is.
    public static BookmarkItem? At(IReadOnlyList<BookmarkItem> items, long order) =>
        items.FirstOrDefault(item => !item.IsDamaged && !item.Stale && item.Order == order);

    public static string DefaultLabel(ReaderLocation place, Outline outline, long order)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(outline);
        if (OutlineTree.EntryAt(outline, order) is OutlineEntry entry &&
            FirstWords(entry.Title) is { Length: > 0 } title)
        {
            return title;
        }
        string words = place.Payload switch
        {
            PdfPosition pdf => string.Create(CultureInfo.InvariantCulture, $"Page {pdf.PageIndex + 1}"),
            TextPosition text => FirstWords(text.Context),
            HtmlPosition html => FirstWords(html.TextQuote ?? string.Empty),
            _ => string.Empty
        };
        return words.Length > 0 ? words : FallbackLabel;
    }

    // Whitespace collapsed, then cut at the last word that fits.
    public static string FirstWords(string text, int limit = BookmarkRules.MaxLabelLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        string collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= limit) return collapsed;
        int space = collapsed.LastIndexOf(' ', limit);
        string cut = space > 0 ? collapsed[..space] : collapsed[..limit];
        return char.IsHighSurrogate(cut[^1]) ? cut[..^1] : cut;
    }

    public static string DisplayLabel(BookmarkItem item) =>
        item.IsDamaged ? DamagedLabel : item.Bookmark.Label;

    public static string AccessibleName(BookmarkItem item) => AccessibleName(item, null);

    // T21.2: in a guide of several pages, the page's title follows the label.
    public static string AccessibleName(BookmarkItem item, string? pageTitle)
    {
        if (item.Location is not ReaderLocation place) return DamagedLabel;
        string label = pageTitle is null ? item.Bookmark.Label : $"{item.Bookmark.Label}, {pageTitle}";
        if (place.Payload is PdfPosition pdf)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{label}, page {pdf.PageIndex + 1}");
        }
        if (place.EstimatedFraction is double fraction && double.IsFinite(fraction))
        {
            int percent = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100, MidpointRounding.AwayFromZero);
            return string.Create(CultureInfo.InvariantCulture, $"{label}, {percent}% through");
        }
        return label;
    }

    public static string? MessageFor(RestoreKind kind) => kind switch
    {
        RestoreKind.Approximate => Shifted,
        RestoreKind.Unavailable => NotFound,
        _ => null
    };
}
