namespace DesktopGuides.Core.Reading;

// Session estimates come from guide-driven layout, so they are bounded before
// they are serialized or stored.
public static class ProgressEstimate
{
    public static double? Bound(double? estimate) =>
        estimate is double value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : null;
}
