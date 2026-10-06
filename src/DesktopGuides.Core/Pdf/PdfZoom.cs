using System.Globalization;

namespace DesktopGuides.Core.Pdf;

/// <summary>
/// The PDF Reader's zoom: fit width (null), or a percent of the shown page's
/// natural width. It lasts only while the guide is open.
/// </summary>
public readonly record struct PdfZoom(int? Percent)
{
    // Within half a percent counts as equal, so a step a hair above the fit
    // percent isn't offered as a zoom that changes nothing.
    private const double Tolerance = 0.5;
    private static readonly int[] steps = [50, 75, 100, 125, 150, 200, 300, 400];

    public static PdfZoom Fit => default;
    public static IReadOnlyList<int> Steps { get; } = Array.AsReadOnly(steps);
    public bool IsFit => Percent is null;
    public string Label => Percent is int percent
        ? string.Create(CultureInfo.InvariantCulture, $"{percent}%")
        : "Fit width";

    // fitPercent is the viewport width over the shown page's natural width, × 100.
    public PdfZoom In(double fitPercent) => Above(fitPercent) is int step ? new PdfZoom(step) : this;
    public PdfZoom Out(double fitPercent) => Below(fitPercent) is int step ? new PdfZoom(step) : this;
    public bool CanZoomIn(double fitPercent) => Above(fitPercent) is not null;
    public bool CanZoomOut(double fitPercent) => Below(fitPercent) is not null;

    public double WidthFor(double viewportWidth, double naturalPageWidth)
    {
        Positive(viewportWidth, nameof(viewportWidth));
        Positive(naturalPageWidth, nameof(naturalPageWidth));
        return Percent is int percent ? naturalPageWidth * percent / 100 : viewportWidth;
    }

    private int? Above(double fitPercent)
    {
        double current = Effective(fitPercent);
        foreach (int step in steps)
        {
            if (step > current + Tolerance) return step;
        }
        return null;
    }

    private int? Below(double fitPercent)
    {
        double current = Effective(fitPercent);
        for (int index = steps.Length - 1; index >= 0; index--)
        {
            if (steps[index] < current - Tolerance) return steps[index];
        }
        return null;
    }

    private double Effective(double fitPercent)
    {
        Positive(fitPercent, nameof(fitPercent));
        return Percent ?? fitPercent;
    }

    private static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Must be finite and positive.");
        }
    }
}
