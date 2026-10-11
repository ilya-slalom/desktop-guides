using System.Globalization;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

// A Game-page row. Rows are rebuilt on every render, so callers match guides
// by Guide.Id, never by row identity.
public sealed class GuideRowItem : CatalogRowItem
{
    internal GuideRowItem(
        GuideSummary summary, TimeProvider clock, CultureInfo culture,
        GuideFileStatus fileStatus = GuideFileStatus.Ok, bool colorful = false, bool highContrast = false)
        : base(
            summary.Guide.Title,
            AppIconCatalog.ForGuide(summary.Guide.Format, fileStatus != GuideFileStatus.Ok, colorful, highContrast),
            CatalogPresentation.GuideFacts(summary, clock, culture, fileStatus))
    {
        Guide = summary.Guide;
        CompletedUtc = summary.State?.CompletedUtc;
    }

    internal Guide Guide { get; }

    internal DateTimeOffset? CompletedUtc { get; }
}
