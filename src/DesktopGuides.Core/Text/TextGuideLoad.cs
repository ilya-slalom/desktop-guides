namespace DesktopGuides.Core.Text;

/// <summary>The result of opening a TXT guide's managed copy.</summary>
public abstract record TextGuideLoad;

/// <summary>The copy decoded. <paramref name="ContentChanged"/> is true when it no
/// longer matches the fingerprint recorded at import.</summary>
public sealed record TextGuideLoaded(TextGuideDocument Document, bool ContentChanged) : TextGuideLoad;

public sealed record TextGuideLoadFailed(TextGuideLoadError Error) : TextGuideLoad;

public enum TextGuideLoadError
{
    Missing,
    TooLarge,
    Unreadable,
    InvalidMetadata,
    NotUtf8,
    Undecodable,
}
