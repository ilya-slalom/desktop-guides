using System.Globalization;
using System.Text.RegularExpressions;

namespace DesktopGuides.Core.Packaging;

/// <summary>
/// The names that depend on the package lane: the single-instance key, the
/// named events the shell and the installed tests share, the activation pipe
/// and the window title. Preview keeps the development names; Public is the
/// released app, so both can be installed side by side.
/// </summary>
public sealed partial record AppLaneNames(string Prefix, string WindowTitle)
{
    public const string PortableInstanceKey = "DesktopGuides.Portable.Main";

    public static AppLaneNames Preview { get; } = new("DesktopGuides.Preview", "Desktop Guides Preview");

    public static AppLaneNames Public { get; } = new("DesktopGuides", "Desktop Guides");

    public static AppLaneNames For(bool publicLane) => publicLane ? Public : Preview;

    public string InstanceKey => $"{Prefix}.Main";

    public string LocalEvent(string name) => $@"Local\{Prefix}.{Segment(name)}";

    public string LocalEvent(string name, int processId) =>
        string.Create(CultureInfo.InvariantCulture, $@"Local\{Prefix}.{Segment(name)}.{processId}");

    public string PipeName(string name, int processId) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}.{Segment(name)}.{processId}");

    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex SegmentPattern();

    private static string Segment(string name) =>
        SegmentPattern().IsMatch(name)
            ? name
            : throw new ArgumentException("A lane name is one alphanumeric segment.", nameof(name));
}
