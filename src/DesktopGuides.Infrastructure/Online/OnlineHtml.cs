using AngleSharp.Dom;

namespace DesktopGuides.Infrastructure.Online;

// S22: URL handling shared by the adapters and the offline copy.
internal static class OnlineHtml
{
    /// <summary>An image's address: its src, or data-src when src is a lazy-load placeholder.</summary>
    public static Uri? ImageUrl(IElement image, Uri baseUrl)
    {
        string? source = image.GetAttribute("src");
        if (source is null || source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            source = image.GetAttribute("data-src");
        }
        return Web(baseUrl, source);
    }

    /// <summary>
    /// Makes the content's links and image addresses absolute, so it reads
    /// the same once it leaves its page (whose base may differ from its URL).
    /// </summary>
    public static void Absolutize(IElement root, Uri baseUrl)
    {
        foreach (IElement link in root.QuerySelectorAll("a[href]").Prepend(root).Where(e => e.HasAttribute("href")))
        {
            string href = link.GetAttribute("href")!;
            if (href.StartsWith('#') || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;
            if (Web(baseUrl, href) is { } target) link.SetAttribute("href", target.AbsoluteUri);
        }
        foreach (IElement image in root.QuerySelectorAll("img").Prepend(root).Where(e => e.LocalName == "img"))
        {
            foreach (string name in new[] { "src", "data-src" })
            {
                if (image.GetAttribute(name) is { } value && !value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                    Web(baseUrl, value) is { } target)
                {
                    image.SetAttribute(name, target.AbsoluteUri);
                }
            }
        }
    }

    public static Uri? Web(Uri baseUrl, string? reference) =>
        reference is not null && Uri.TryCreate(baseUrl, reference.Trim(), out Uri? resolved) &&
        (resolved.Scheme == Uri.UriSchemeHttps || resolved.Scheme == Uri.UriSchemeHttp) ? resolved : null;
}
