using System.Globalization;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

// A Game-page row. Rows are rebuilt on every render, so callers match guides
// by Guide.Id, never by row identity.
public sealed class GuideRowItem : CatalogRowItem
{
    internal GuideRowItem(GuideSummary summary, TimeProvider clock, CultureInfo culture)
        : base(
            summary.Guide.Title,
            FormatGlyph(summary.Guide.Format),
            CatalogPresentation.GuideFacts(summary, clock, culture))
    {
        Guide = summary.Guide;
        CompletedUtc = summary.State?.CompletedUtc;
    }

    internal Guide Guide { get; }

    internal DateTimeOffset? CompletedUtc { get; }

    private static string FormatGlyph(GuideFormat format) => format switch
    {
        GuideFormat.Html => "\uE774", // Globe
        GuideFormat.Pdf => "\uEA90",  // PDF
        _ => "\uE8A5"                 // Document
    };
}
