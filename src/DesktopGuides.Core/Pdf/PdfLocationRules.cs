using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Pdf;

public sealed record PdfRestore(int PageIndex, RestoreOutcome Outcome);

// T10.1 restores by page only. Fraction restore and changed-byte matching
// are T10.3, so a location for other bytes is Unavailable here.
public static class PdfLocationRules
{
    public const string ClampedReason = "That page isn't in this guide, so the nearest page is shown.";
    public const string UnavailableReason = "This reading position can't be used with this guide.";

    public static ReaderLocation Capture(string contentSha256, int pageIndex, int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, pageCount);
        return new ReaderLocation(
            GuideFormat.Pdf,
            ReaderLocationCodec.CurrentVersion,
            contentSha256.ToLowerInvariant(),
            new PdfPosition(pageIndex, 0),
            (pageIndex + 1.0) / pageCount);
    }

    public static PdfRestore Restore(ReaderLocation location, string contentSha256, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
        if (location.Format != GuideFormat.Pdf ||
            location.SchemaVersion != ReaderLocationCodec.CurrentVersion ||
            location.Payload is not PdfPosition position ||
            !double.IsFinite(position.PageFraction) ||
            !string.Equals(location.ContentSha256, contentSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new PdfRestore(-1, new RestoreOutcome(RestoreKind.Unavailable, UnavailableReason));
        }
        int page = Math.Clamp(position.PageIndex, 0, pageCount - 1);
        return page == position.PageIndex
            ? new PdfRestore(page, new RestoreOutcome(RestoreKind.Exact))
            : new PdfRestore(page, new RestoreOutcome(RestoreKind.Approximate, ClampedReason));
    }
}
