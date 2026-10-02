using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Text;

public sealed record TextRestore(int Line, RestoreOutcome Outcome);

// Maps a TXT document to the T12.1 locator: a line's start offset plus the
// text that follows it, so a changed guide can still find the same place.
public static class TextLocator
{
    public const int ContextLength = 128;
    public const string ApproximateReason = "The guide changed, so this is an approximate position.";
    public const string UnavailableReason = "This reading position can't be used with this guide.";

    public static ReaderLocation Capture(TextGuideDocument document, int line)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyList<int> starts = document.LineStarts;
        string text = document.Text;
        int offset = starts[Math.Clamp(line, 0, starts.Count - 1)];
        int length = Math.Min(ContextLength, text.Length - offset);
        // The codec rejects NUL, and the JSON writer rejects a lone high surrogate.
        int nul = text.IndexOf('\0', offset, length);
        if (nul >= 0)
        {
            length = nul - offset;
        }
        if (length > 0 && char.IsHighSurrogate(text[offset + length - 1]))
        {
            length--;
        }
        return new ReaderLocation(
            GuideFormat.Txt,
            ReaderLocationCodec.CurrentVersion,
            document.ContentSha256.ToLowerInvariant(),
            new TextPosition(offset, text.Substring(offset, length)),
            text.Length == 0 ? 0 : (double)offset / text.Length);
    }

    public static TextRestore Restore(TextGuideDocument document, ReaderLocation location)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(location);
        if (location.Format != GuideFormat.Txt ||
            location.Payload is not TextPosition position ||
            location.SchemaVersion != ReaderLocationCodec.CurrentVersion)
        {
            return Unavailable();
        }

        string text = document.Text;
        if (string.Equals(location.ContentSha256, document.ContentSha256, StringComparison.OrdinalIgnoreCase) &&
            position.CharacterOffset >= 0 && position.CharacterOffset <= text.Length)
        {
            return new(document.LineAtOffset(position.CharacterOffset), new(RestoreKind.Exact));
        }
        if (NearestMatch(text, position) is int match)
        {
            return new(document.LineAtOffset(match), new(RestoreKind.Context));
        }
        if (location.EstimatedFraction is double fraction && double.IsFinite(fraction))
        {
            int offset = (int)Math.Round(Math.Clamp(fraction, 0, 1) * text.Length);
            return new(document.LineAtOffset(offset), new(RestoreKind.Approximate, ApproximateReason));
        }
        return Unavailable();
    }

    // The match nearest the saved offset, or null when there is none or two
    // are equally near. Matches arrive in order, so the scan stops once they
    // are past the offset and moving away from it.
    private static int? NearestMatch(string text, TextPosition position)
    {
        string context = position.Context;
        if (context.Length == 0)
        {
            return null;
        }
        int? best = null;
        long bestDistance = long.MaxValue;
        bool tie = false;
        for (int index = text.IndexOf(context, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(context, index + 1, StringComparison.Ordinal))
        {
            long distance = Math.Abs((long)index - position.CharacterOffset);
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
                tie = false;
            }
            else if (distance == bestDistance)
            {
                tie = true;
            }
            else if (index > position.CharacterOffset)
            {
                break;
            }
        }
        return tie ? null : best;
    }

    private static TextRestore Unavailable() =>
        new(0, new RestoreOutcome(RestoreKind.Unavailable, UnavailableReason));
}
