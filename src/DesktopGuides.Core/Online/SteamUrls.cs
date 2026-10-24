using System.Text.RegularExpressions;

namespace DesktopGuides.Core.Online;

/// <summary>S22 PR 4: the Steam addresses Find online reads: a guide's, and a game's store or guides page.</summary>
public static partial class SteamUrls
{
    private const string Community = "steamcommunity.com";
    private const string Store = "store.steampowered.com";

    /// <summary>The published file ID a guide page names, or null.</summary>
    public static string? GuideId(Uri url)
    {
        if (!IsWeb(url) || !url.Host.Equals(Community, StringComparison.OrdinalIgnoreCase) ||
            url.AbsolutePath.TrimEnd('/') != "/sharedfiles/filedetails")
        {
            return null;
        }
        return FileId().Match(url.Query) is { Success: true } id ? id.Groups["id"].Value : null;
    }

    /// <summary>The app ID a store page or a community app page names, or null.</summary>
    public static string? AppId(Uri url)
    {
        if (!IsWeb(url) ||
            !(url.Host.Equals(Community, StringComparison.OrdinalIgnoreCase) || url.Host.Equals(Store, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        return AppPath().Match(url.AbsolutePath) is { Success: true } app ? app.Groups["app"].Value : null;
    }

    public static Uri Guide(string fileId) => new($"https://{Community}/sharedfiles/filedetails/?id={fileId}");

    private static bool IsWeb(Uri url) => url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp;

    // Published file IDs are 64-bit: at most 20 digits.
    [GeneratedRegex(@"^\?(?:[^&]*&)*id=(?<id>\d{1,20})(?:&|$)", RegexOptions.CultureInvariant)]
    private static partial Regex FileId();

    [GeneratedRegex(@"^/app/(?<app>\d{1,10})(?:/|$)", RegexOptions.CultureInvariant)]
    private static partial Regex AppPath();
}
