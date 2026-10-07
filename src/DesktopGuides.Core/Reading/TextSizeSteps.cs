using System.Globalization;

namespace DesktopGuides.Core.Reading;

// T14.1: TXT and HTML text size moves through fixed steps, as PdfZoom does,
// so the value shown and stored is exact and repeated steps don't drift.
public static class TextSizeSteps
{
    public const double Default = 1.0;
    public const double Min = 0.75;
    public const double Max = 2.0;

    // In percent: a value this close to a step counts as that step, so it
    // isn't offered as a step that changes nothing.
    private const double Tolerance = 0.5;
    private static readonly int[] percents = [75, 90, 100, 110, 125, 150, 175, 200];

    public static IReadOnlyList<double> Steps { get; } =
        Array.AsReadOnly(percents.Select(percent => percent / 100.0).ToArray());

    // A value between steps (an older or hand-edited database) is kept.
    public static double Normalize(double? stored) =>
        stored is double value && double.IsFinite(value) && value >= Min && value <= Max
            ? value
            : Default;

    public static double Larger(double scale)
    {
        double current = scale * 100;
        foreach (int step in percents)
        {
            if (step > current + Tolerance)
            {
                return step / 100.0;
            }
        }
        return scale;
    }

    public static double Smaller(double scale)
    {
        double current = scale * 100;
        for (int index = percents.Length - 1; index >= 0; index--)
        {
            if (percents[index] < current - Tolerance)
            {
                return percents[index] / 100.0;
            }
        }
        return scale;
    }

    public static bool CanLarger(double scale) => Larger(scale) != scale;

    public static bool CanSmaller(double scale) => Smaller(scale) != scale;

    public static string Label(double scale) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)Math.Round(scale * 100, MidpointRounding.AwayFromZero)}%");

    public static string Status(double scale) => $"Text size {Label(scale)}.";

    public static string SaveFailed(string message) =>
        $"Could not save the text size: {message}";
}
