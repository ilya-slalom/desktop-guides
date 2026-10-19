using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Reading;

public abstract record ReaderPosition;

public sealed record TextPosition(int CharacterOffset, string Context) : ReaderPosition;

public sealed record HtmlPosition(
    string DocumentPath,
    string? ElementId,
    string? TextQuote,
    int TextOffset,
    double ScrollFraction) : ReaderPosition;

public sealed record PdfPosition(int PageIndex, double PageFraction) : ReaderPosition;

// T21.2: a map's view. A null zoom is fitted; the centre is a fraction of
// the image. It's a convenience, never progress.
public sealed record ImagePosition(double? Zoom, double CenterX, double CenterY) : ReaderPosition
{
    public const double MaxZoom = 4;
}

public sealed record ReaderLocation(
    GuideFormat Format,
    int SchemaVersion,
    string ContentSha256,
    ReaderPosition Payload,
    double? EstimatedFraction);

public enum RestoreKind
{
    Exact,
    Context,
    Approximate,
    Unavailable
}

public sealed record RestoreOutcome(RestoreKind Kind, string? Reason = null);

public enum LocationDecodeStatus
{
    Valid,
    ContentChanged,
    UnsupportedVersion,
    Invalid
}

public sealed record LocationDecodeResult(
    LocationDecodeStatus Status,
    ReaderLocation? Location);
