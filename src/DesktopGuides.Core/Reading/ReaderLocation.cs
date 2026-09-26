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
