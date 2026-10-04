namespace DesktopGuides.Core.Html;

public enum HtmlNavigationKind { Entry, SameDocument, External, Deny }

public sealed record HtmlNavigation(HtmlNavigationKind Kind, Uri? ExternalUri = null);

public static class HtmlNavigationPolicy
{
    private static readonly HtmlNavigation Denied = new(HtmlNavigationKind.Deny);

    public static HtmlNavigation Classify(string uri, Uri entry, bool entryNavigated, bool userInitiated)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? target)) return Denied;
        if (IsEntryDocument(target, entry))
        {
            bool fragment = target.Fragment.Length > 0;
            if (!entryNavigated && !fragment) return new(HtmlNavigationKind.Entry);
            if (entryNavigated && fragment) return new(HtmlNavigationKind.SameDocument);
            return Denied;
        }
        // Only a person's click may raise the bar; redirects and refreshes can't.
        if (target.Scheme is "http" or "https" && userInitiated &&
            target.UserInfo.Length == 0 && !GuideWebOrigin.IsGuideHost(target.Host))
        {
            return new(HtmlNavigationKind.External, target);
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
