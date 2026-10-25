using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Online;

/// <summary>A site Find online can browse, and the hosts its pages come from.</summary>
public sealed record OnlineSource(SourceSite Site, string Name, IReadOnlyList<string> Hosts, Uri Home);

/// <summary>
/// S22: the sources this build includes. Each is a build switch
/// (IncludeGameFaqs, IncludeNeoseeker, IncludeSteam, IncludeVgMaps), so a build can leave one out.
/// </summary>
public static class OnlineSources
{
    public static IReadOnlyList<OnlineSource> Available { get; } = Build();

    private static OnlineSource[] Build()
    {
        List<OnlineSource> sources = [];
#if SOURCE_GAMEFAQS
        sources.Add(new(SourceSite.GameFaqs, "GameFAQs", ["gamefaqs.gamespot.com"], new Uri("https://gamefaqs.gamespot.com/")));
#endif
#if SOURCE_NEOSEEKER
        sources.Add(new(SourceSite.Neoseeker, "Neoseeker", ["www.neoseeker.com"], new Uri("https://www.neoseeker.com/")));
#endif
#if SOURCE_STEAM
        sources.Add(new(SourceSite.Steam, "Steam", ["steamcommunity.com"], new Uri("https://steamcommunity.com/")));
#endif
#if SOURCE_VGMAPS
        sources.Add(new(SourceSite.VgMaps, "VGMaps", ["vgmaps.com"], new Uri("https://vgmaps.com/Atlas/index.htm")));
#endif
        return sources.ToArray();
    }

    /// <summary>The included source an https URL belongs to, or null.</summary>
    public static OnlineSource? Find(Uri url) =>
        url.Scheme != Uri.UriSchemeHttps ? null
        : Available.FirstOrDefault(source => source.Hosts.Any(host => host.Equals(url.Host, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Where browsing starts. GameFAQs' search page is one a person opens
    /// here; the app itself never fetches it.
    /// </summary>
    public static Uri StartUrl(OnlineSource source, string gameTitle) => source.Site switch
    {
        SourceSite.GameFaqs => new Uri("https://gamefaqs.gamespot.com/search?game=" + Uri.EscapeDataString(gameTitle.Trim())),
        _ => source.Home,
    };
}
