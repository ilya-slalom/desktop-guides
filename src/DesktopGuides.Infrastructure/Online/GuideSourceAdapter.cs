using DesktopGuides.Core.Library;

namespace DesktopGuides.Infrastructure.Online;

// S22: how a guide page from a supported site is recognised and read. An
// adapter only parses HTML it is given; it never fetches anything.
public enum CapturedFormat { Text, Html }

/// <summary>A guide page's site item and its page within the guide; "" for a one-page guide.</summary>
public sealed record GuidePageMatch(SourceSite Site, string ItemId, string PageKey);

public sealed record GuidePageLink(string PageKey, Uri Url, string? Title);

/// <summary>
/// One page as read: <paramref name="Content"/> is text for
/// <see cref="CapturedFormat.Text"/> and HTML for <see cref="CapturedFormat.Html"/>.
/// <paramref name="Pages"/> lists the guide's own pages in order, empty for
/// a one-page guide; <paramref name="Images"/> is every image the content uses.
/// </summary>
public sealed record GuidePageReading(
    GuidePageMatch Match,
    Uri Url,
    CapturedFormat Format,
    string Title,
    string? Author,
    string? Notice,
    DateTimeOffset? SourceUpdated,
    IReadOnlyList<GuidePageLink> Pages,
    string Content,
    IReadOnlyList<Uri> Images);

public interface IGuideSourceAdapter
{
    SourceSite Site { get; }

    /// <summary>The guide page this URL names, or null.</summary>
    GuidePageMatch? Match(Uri url);

    /// <summary>The page read as a guide, or null when it isn't one this adapter can read.</summary>
    GuidePageReading? Read(Uri url, string html);
}
