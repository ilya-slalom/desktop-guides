namespace DesktopGuides.Core.Html;

/// <summary>
/// Browser arguments for every HTML guide session.
/// </summary>
public static class HtmlBrowserEnvironment
{
    // Chromium's preconnect and network predictor open sockets that neither
    // CSP nor WebResourceRequested sees, so every connection Chromium makes
    // itself, loopback included, goes to a proxy that fails before sending.
    public const string Arguments = "--proxy-server=http://0.0.0.0:9 --proxy-bypass-list=<-loopback>";
}
