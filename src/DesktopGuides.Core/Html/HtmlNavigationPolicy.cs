namespace DesktopGuides.Core.Html;

public enum HtmlNavigationKind { Entry, SameDocument, External, Deny, Unavailable, Page }

public sealed record HtmlNavigation(
    HtmlNavigationKind Kind, Uri? ExternalUri = null, int? PageIndex = null, string? Fragment = null);

public static class HtmlNavigationPolicy
{
    private static readonly HtmlNavigation Denied = new(HtmlNavigationKind.Deny);

    // T21.2: document is the page the view shows, or is about to show. A
    // person's click on another of the guide's pages, by either address,
    // is a Page move the session makes itself.
    public static HtmlNavigation Classify(
        string uri, Uri document, bool documentNavigated, bool userInitiated, HtmlGuidePages? pages = null)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? target)) return Denied;
        if (IsEntryDocument(target, document))
        {
            bool fragment = target.Fragment.Length > 0;
            if (!documentNavigated && !fragment) return new(HtmlNavigationKind.Entry);
            if (documentNavigated && fragment) return new(HtmlNavigationKind.SameDocument);
            return Denied;
        }
        if (pages is not null && userInitiated && target.UserInfo.Length == 0 && pages.IndexOf(target) is int page)
        {
            return new(HtmlNavigationKind.Page, PageIndex: page, Fragment: target.Fragment.Length > 0 ? target.Fragment : null);
        }
        // Only a person's click may raise the bar; redirects and refreshes can't.
        if (target.Scheme is "http" or "https" && userInitiated &&
            target.UserInfo.Length == 0 && !GuideWebOrigin.IsGuideHost(target.Host))
        {
            return new(HtmlNavigationKind.External, target);
        }
        // Another page of this guide's origin wasn't imported. A person is
        // told; anything else is cancelled silently.
        if (target.Scheme is "http" or "https" && userInitiated && target.UserInfo.Length == 0 &&
            string.Equals(target.Host, document.Host, StringComparison.OrdinalIgnoreCase))
        {
            return new(HtmlNavigationKind.Unavailable);
        }
        return Denied;
    }

    // Chromium may report a self-link with sub-delims such as ( ) ' left
    // literal, so paths are compared decoded, as the request policy does.
    // The fragment is ignored.
    public static bool IsEntryDocument(Uri target, Uri entry) =>
        Uri.Compare(target, entry, UriComponents.SchemeAndServer,
            UriFormat.UriEscaped, StringComparison.Ordinal) == 0 &&
        string.Equals(target.Query, entry.Query, StringComparison.Ordinal) &&
        string.Equals(Uri.UnescapeDataString(target.AbsolutePath), Uri.UnescapeDataString(entry.AbsolutePath),
            StringComparison.Ordinal);
}
