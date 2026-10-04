namespace DesktopGuides.Core.Pdf;

// Where the reader is on the shown PDF page: the share of the page above
// the top of the viewport. A resize or re-render keeps it; a new page starts
// at its top unless a restore asked for a point. The session uses it on the
// UI thread only; it isn't thread-safe.
public sealed class PdfPagePosition
{
    // The scroller snaps offsets to physical pixels, so its echo of the
    // app's own scroll can be a little off.
    private const double EchoTolerance = 1;
    private int? pendingPage;
    private double pendingFraction;
    private double appliedOffset = double.NaN;
    private double appliedImage = double.NaN;
    private double appliedViewport = double.NaN;

    public int Page { get; private set; }
    public double Fraction { get; private set; }

    // The session is about to request this page: 0 for a turn, the saved
    // point for a restore.
    public void Target(int page, double fraction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        if (fraction is not (>= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "A page fraction is between 0 and 1.");
        }
        pendingPage = page;
        pendingFraction = fraction;
    }

    public void Shown(int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        if (page == pendingPage)
        {
            Fraction = pendingFraction;
            pendingPage = null;
        }
        else if (page != Page)
        {
            Fraction = 0;
        }
        Page = page;
    }

    // The bottom clamp limits the offset, never the stored point, so a
    // taller window returns to it.
    public double OffsetFor(double imageHeight, double viewportHeight)
    {
        double offset = Usable(imageHeight, viewportHeight)
            ? Math.Clamp(Fraction * imageHeight, 0, Math.Max(0, imageHeight - viewportHeight))
            : 0;
        appliedOffset = offset;
        appliedImage = imageHeight;
        appliedViewport = viewportHeight;
        return offset;
    }

    // A scroll came to rest. Only a user's scroll moves the point: the echo
    // of an applied offset doesn't, nor does the scroller's own clamp after
    // a layout change, which the next layout event re-applies over.
    public bool Scrolled(double offset, double imageHeight, double viewportHeight)
    {
        if (!Usable(imageHeight, viewportHeight) || !double.IsFinite(offset) ||
            imageHeight <= viewportHeight ||
            !Near(imageHeight, appliedImage) || !Near(viewportHeight, appliedViewport) ||
            Near(offset, appliedOffset))
        {
            return false;
        }
        Fraction = Math.Clamp(offset / imageHeight, 0, 1);
        appliedOffset = offset;
        return true;
    }

    private static bool Usable(double imageHeight, double viewportHeight) =>
        double.IsFinite(imageHeight) && double.IsFinite(viewportHeight) &&
        imageHeight > 0 && viewportHeight > 0;

    // False when either side is NaN, so nothing is near an offset not yet applied.
    private static bool Near(double a, double b) => Math.Abs(a - b) <= EchoTolerance;
}
