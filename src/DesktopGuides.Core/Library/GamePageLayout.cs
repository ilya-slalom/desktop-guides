namespace DesktopGuides.Core.Library;

/// <summary>Splits the Game page height so the guide list never collapses.</summary>
public static class GamePageLayout
{
    /// <summary>One guide row (a 60 px tile with 8 px padding) plus a little room.</summary>
    public const double MinGuideListHeight = 96;

    /// <summary>Enough of the details card to show that it scrolls.</summary>
    public const double MinDetailsHeight = 48;

    /// <summary>
    /// T21.2: a row of guides and a row of maps, the Maps header, and the two
    /// gaps between them.
    /// </summary>
    public static double ListAreaMinHeight(double mapsHeaderHeight, double listSpacing) =>
        2 * MinGuideListHeight + mapsHeaderHeight + 2 * listSpacing;

    /// <summary>
    /// The tallest the details card may be once the header, the Guides row and
    /// the three row gaps are placed, leaving the list area its minimum.
    /// </summary>
    public static double DetailsMaxHeight(
        double panelHeight, double headerHeight, double guidesHeaderHeight, double rowSpacing,
        double listAreaMinHeight = MinGuideListHeight)
    {
        if (!double.IsFinite(panelHeight) || panelHeight <= 0)
        {
            return double.PositiveInfinity;
        }

        var available = panelHeight - headerHeight - guidesHeaderHeight - 3 * rowSpacing;
        return Math.Max(MinDetailsHeight, available - listAreaMinHeight);
    }
}
