namespace DesktopGuides.Core.Online;

public enum NewWindowAction { Ignore, OpenHere, OpenExternally }

/// <summary>S22: what Find online's browser does with a link that asks for a new window.</summary>
public static class OnlineNavigation
{
    // A source's page opens in the browser itself, as its top-level
    // navigations do, so a guide list whose links open new tabs stays here.
    // Any other web page opens in the system browser, and only on a click.
    // During a capture nothing opens.
    public static NewWindowAction ForNewWindow(Uri? url, bool capturing, bool userInitiated)
    {
        if (capturing || url is null) return NewWindowAction.Ignore;
        if (OnlineSources.Find(url) is not null) return NewWindowAction.OpenHere;
        return userInitiated && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            ? NewWindowAction.OpenExternally
            : NewWindowAction.Ignore;
    }
}
