namespace DesktopGuides.Core.Html;

/// <summary>
/// Each guide gets its own origin, so a URL into another guide is
/// cross-guide, never external. ".invalid" never resolves.
/// </summary>
public static class GuideWebOrigin
{
    public const string HostSuffix = ".guide.invalid";
    private const string P0Host = "guide.invalid";

    public static string HostFor(Guid guideId)
    {
        if (guideId == Guid.Empty)
        {
            throw new ArgumentException("A guide origin needs a guide ID.", nameof(guideId));
        }
        return "g" + guideId.ToString("N") + HostSuffix;
    }

    public static Uri OriginFor(Guid guideId) => new("https://" + HostFor(guideId) + "/");

    public static Uri EntryUri(Guid guideId, string requestPath) =>
        new("https://" + HostFor(guideId) + "/" +
            string.Join('/', requestPath.Split('/').Select(Uri.EscapeDataString)));

    public static bool IsGuideHost(string host) =>
        host.EndsWith(HostSuffix, StringComparison.OrdinalIgnoreCase) ||
        host.Equals(P0Host, StringComparison.OrdinalIgnoreCase);
}
