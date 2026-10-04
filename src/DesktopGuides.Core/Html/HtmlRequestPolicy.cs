namespace DesktopGuides.Core.Html;

public enum HtmlDenyReason
{
    Method, CrossGuide, External, OtherScheme, Malformed, NotInManifest, UnsupportedType, HashMismatch, FileMissing,
    UnimportedPage
}

public abstract record HtmlRequestDecision;
public sealed record HtmlServe(GuideAsset Asset, string ContentType) : HtmlRequestDecision;
public sealed record HtmlDeny(HtmlDenyReason Reason) : HtmlRequestDecision;

/// <summary>
/// Decides every WebView2 request for one guide. A served decision carries
/// the row; the caller opens the row's RelativePath, never the request path.
/// </summary>
public sealed class HtmlRequestPolicy
{
    public const string ContentSecurityPolicy =
        "default-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
        "font-src 'none'; script-src 'none'; frame-src 'none'; form-action 'none'; " +
        "connect-src 'none'; object-src 'none'; base-uri 'none'";

    public const string DeniedHeaders = "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff";

    private readonly Dictionary<string, GuideAsset> assets = new(StringComparer.Ordinal);
    private readonly string host;

    public HtmlRequestPolicy(Guid guideId, IEnumerable<GuideAsset> assets)
    {
        host = GuideWebOrigin.HostFor(guideId);
        foreach (GuideAsset asset in assets)
        {
            if (!this.assets.TryAdd(asset.RequestPath, asset))
            {
                throw new ArgumentException("Guide assets repeat a request path.", nameof(assets));
            }
        }
        GuideAsset[] entries = [.. this.assets.Values.Where(asset => asset.Kind == GuideAssetKind.EntryHtml)];
        if (entries.Length != 1)
        {
            throw new ArgumentException("A guide needs exactly one entry document.", nameof(assets));
        }
        GuideId = guideId;
        Entry = entries[0];
        EntryUri = GuideWebOrigin.EntryUri(guideId, Entry.RequestPath);
    }

    public Guid GuideId { get; }
    public GuideAsset Entry { get; }
    public Uri EntryUri { get; }

    public HtmlRequestDecision Decide(string method, string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? target)) return Deny(HtmlDenyReason.Malformed);
        if (target.Scheme is not ("http" or "https")) return Deny(HtmlDenyReason.OtherScheme);
        if (!string.Equals(target.Host, host, StringComparison.Ordinal))
        {
            return Deny(GuideWebOrigin.IsGuideHost(target.Host) ? HtmlDenyReason.CrossGuide : HtmlDenyReason.External);
        }
        // The query is ignored: the scanner drops it when it records a reference.
        if (target.Scheme != "https" || target.Port != 443 || target.UserInfo.Length != 0)
        {
            return Deny(HtmlDenyReason.Malformed);
        }
        if (!string.Equals(method, "GET", StringComparison.Ordinal)) return Deny(HtmlDenyReason.Method);
        string path = Uri.UnescapeDataString(target.AbsolutePath);
        if (!path.StartsWith('/')) return Deny(HtmlDenyReason.Malformed);
        string requestPath = path[1..];
        if (requestPath.Contains('\\') || requestPath.Contains('\0') ||
            requestPath.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return Deny(HtmlDenyReason.Malformed);
        }
        if (!assets.TryGetValue(requestPath, out GuideAsset? asset)) return Deny(HtmlDenyReason.NotInManifest);
        return ContentTypeFor(asset.RelativePath) is { } type
            ? new HtmlServe(asset, type)
            : Deny(HtmlDenyReason.UnsupportedType);
    }

    public static string ServedHeaders(string contentType) =>
        $"Content-Type: {contentType}\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store\r\n" +
        $"Content-Security-Policy: {ContentSecurityPolicy}";

    // No charset: legacy guides keep their own meta charset or BOM.
    private static string? ContentTypeFor(string relativePath) =>
        Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => null
        };

    private static HtmlDeny Deny(HtmlDenyReason reason) => new(reason);
}
