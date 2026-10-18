using System.Globalization;

namespace DesktopGuides.Core.Html;

// T21.2: the address a browser recorded when it saved a page, and the key
// two addresses of one page share.
public static class SavedPageUrl
{
    private const string Marker = "<!-- saved from url=(";

    // Edge and Chrome write "<!-- saved from url=(NNNN)URL -->" at the top
    // of a saved page; NNNN is the address's length.
    public static Uri? FromComment(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        int start = prefix.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        int digits = start + Marker.Length;
        int close = prefix.IndexOf(')', digits);
        if (close - digits is < 1 or > 5 ||
            !int.TryParse(prefix.AsSpan(digits, close - digits), NumberStyles.None, CultureInfo.InvariantCulture, out int length) ||
            length < 1 || close + 1 + length > prefix.Length)
        {
            return null;
        }
        return Parse(prefix.Substring(close + 1, length));
    }

    // Only an absolute http or https address names a page.
    public static Uri? Parse(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https" && uri.Host.Length > 0
            ? uri
            : null;

    // The scheme, the host's case, the fragment and escaping don't count.
    public static string Key(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.GetComponents(
            UriComponents.Host | UriComponents.Port | UriComponents.PathAndQuery, UriFormat.Unescaped);
    }

    public static bool SameHost(Uri a, Uri b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return string.Equals(a.IdnHost, b.IdnHost, StringComparison.OrdinalIgnoreCase);
    }
}
