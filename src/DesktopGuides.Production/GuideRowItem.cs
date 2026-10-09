using System.Globalization;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

// A Game-page row. Rows are rebuilt on every render, so callers match guides
// by Guide.Id, never by row identity.
public sealed class GuideRowItem : CatalogRowItem
{
    internal GuideRowItem(
        GuideSummary summary, TimeProvider clock, CultureInfo culture,
        GuideFileStatus fileStatus = GuideFileStatus.Ok)
        : base(
            summary.Guide.Title,
            fileStatus == GuideFileStatus.Ok ? FormatGlyph(summary.Guide.Format) : "\uE7BA", // Warning
            CatalogPresentation.GuideFacts(summary, clock, culture, fileStatus))
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
