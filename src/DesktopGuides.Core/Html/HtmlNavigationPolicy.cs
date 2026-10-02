namespace DesktopGuides.Core.Html;

public enum HtmlNavigationKind { Entry, SameDocument, External, Deny }

public sealed record HtmlNavigation(HtmlNavigationKind Kind, Uri? ExternalUri = null);

public static class HtmlNavigationPolicy
{
    private static readonly HtmlNavigation Denied = new(HtmlNavigationKind.Deny);

    public static HtmlNavigation Classify(string uri, Uri entry, bool entryNavigated, bool userInitiated)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? target)) return Denied;
        if (Uri.Compare(target, entry, UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
                UriFormat.UriEscaped, StringComparison.Ordinal) == 0)
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
}
