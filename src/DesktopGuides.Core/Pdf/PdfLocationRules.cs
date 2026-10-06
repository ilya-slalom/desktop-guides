using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Pdf;

public sealed record PdfRestore(int PageIndex, double PageFraction, RestoreOutcome Outcome);

// A PDF position is a page and the share of that page above the top of the
// viewport. A location for other bytes of the guide keeps its page and point
// but is only approximate; one that can't be used opens at the first page.
public static class PdfLocationRules
{
    public const string ClampedReason = "That page isn't in this guide, so the nearest page is shown.";
    public const string ChangedReason = "The guide changed, so this is an approximate position.";
    public const string UnavailableReason = "This reading position can't be used with this guide.";

    public static ReaderLocation Capture(string contentSha256, int pageIndex, double pageFraction, int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, pageCount);
        if (!ValidFraction(pageFraction))
        {
            throw new ArgumentOutOfRangeException(nameof(pageFraction), pageFraction, "A page fraction is between 0 and 1.");
        }
        return new ReaderLocation(
            GuideFormat.Pdf,
            ReaderLocationCodec.CurrentVersion,
            contentSha256.ToLowerInvariant(),
            new PdfPosition(pageIndex, pageFraction),
            Math.Min(1, (pageIndex + pageFraction) / pageCount));
    }

    public static PdfRestore Restore(ReaderLocation location, string contentSha256, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
        if (location.Format != GuideFormat.Pdf ||
            location.SchemaVersion != ReaderLocationCodec.CurrentVersion ||
            location.Payload is not PdfPosition position ||
            !ValidFraction(position.PageFraction))
        {
            return new PdfRestore(0, 0, new RestoreOutcome(RestoreKind.Unavailable, UnavailableReason));
        }
        int page = Math.Clamp(position.PageIndex, 0, pageCount - 1);
        bool clamped = page != position.PageIndex;
        // A point on a page that isn't there means nothing; show its top.
        double fraction = clamped ? 0 : position.PageFraction;
        if (!string.Equals(location.ContentSha256, contentSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new PdfRestore(page, fraction, new RestoreOutcome(RestoreKind.Approximate, ChangedReason));
        }
        return clamped
            ? new PdfRestore(page, 0, new RestoreOutcome(RestoreKind.Approximate, ClampedReason))
            : new PdfRestore(page, fraction, new RestoreOutcome(RestoreKind.Exact));
    }

    // 1-based, like the page number the user types.
    public static bool IsPageInRange(int pageNumber, int pageCount) =>
        pageNumber >= 1 && pageNumber <= pageCount;

    // NaN fails both comparisons.
    private static bool ValidFraction(double fraction) => fraction is >= 0 and <= 1;
}
