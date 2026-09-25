using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace DesktopGuides.Core.Text;

public sealed class TextGuideDocument
{
    private const int ContextRadius = 16;
    private readonly int[] lineStarts;

    private TextGuideDocument(string text, string contentSha256, string encodingName)
    {
        Text = text;
        ContentSha256 = contentSha256;
        EncodingName = encodingName;
        List<int> starts = [0];
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }
        lineStarts = [.. starts];
        LineStarts = new ReadOnlyCollection<int>(lineStarts);
    }

    public string Text { get; }
    public string ContentSha256 { get; }
    public string EncodingName { get; }
    public IReadOnlyList<int> LineStarts { get; }

    public static TextGuideDocument Decode(byte[] bytes, int? codePage = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        bool hasUtf8Bom = bytes.Length >= 3 &&
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        ReadOnlySpan<byte> contents = hasUtf8Bom ? bytes.AsSpan(3) : bytes;
        Encoding encoding;
        if (codePage is int selectedCodePage)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            encoding = Encoding.GetEncoding(
                selectedCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        else
        {
            encoding = new UTF8Encoding(false, true);
        }

        string decoded;
        try
        {
            decoded = encoding.GetString(contents);
        }
        catch (DecoderFallbackException exception) when (codePage is null)
        {
            throw new EncodingSelectionRequiredException(exception);
        }

        string normalized = decoded.Replace("\r\n", "\n").Replace('\r', '\n');
        string sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        return new TextGuideDocument(normalized, sha256, encoding.WebName);
    }

    public int LineAtOffset(int characterOffset)
    {
        int boundedOffset = Math.Clamp(characterOffset, 0, Text.Length);
        int index = Array.BinarySearch(lineStarts, boundedOffset);
        return index >= 0 ? index : ~index - 1;
    }

    public TextLocation Capture(int characterOffset)
    {
        int boundedOffset = Math.Clamp(characterOffset, 0, Text.Length);
        int start = Math.Max(0, boundedOffset - ContextRadius);
        int end = Math.Min(Text.Length, boundedOffset + ContextRadius);
        return new TextLocation(
            1,
            ContentSha256,
            EncodingName,
            boundedOffset,
            Text[start..end],
            boundedOffset - start,
            Text.Length == 0 ? 0 : (double)boundedOffset / Text.Length);
    }

    public TextRestoreResult Restore(TextLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.SchemaVersion == 1 &&
            string.Equals(ContentSha256, location.ContentSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(EncodingName, location.EncodingName, StringComparison.OrdinalIgnoreCase))
        {
            return new TextRestoreResult(
                Math.Clamp(location.CharacterOffset, 0, Text.Length), TextRestoreKind.Exact);
        }

        if (location.SchemaVersion == 1 && !string.IsNullOrEmpty(location.ContextQuote))
        {
            int bestOffset = -1;
            long bestDistance = long.MaxValue;
            bool ambiguous = false;
            int searchFrom = 0;
            while (searchFrom < Text.Length)
            {
                int match = Text.IndexOf(location.ContextQuote, searchFrom, StringComparison.Ordinal);
                if (match < 0)
                {
                    break;
                }
                int offset = (int)Math.Clamp((long)match + location.ContextOffset, 0, Text.Length);
                long distance = Math.Abs((long)offset - location.CharacterOffset);
                if (distance < bestDistance)
                {
                    bestOffset = offset;
                    bestDistance = distance;
                    ambiguous = false;
                }
                else if (distance == bestDistance)
                {
                    ambiguous = true;
                }
                searchFrom = match + 1;
            }
            if (bestOffset >= 0 && !ambiguous)
            {
                return new TextRestoreResult(bestOffset, TextRestoreKind.Context);
            }
        }

        double fraction = double.IsFinite(location.Fraction)
            ? Math.Clamp(location.Fraction, 0, 1)
            : 0;
        int fallbackOffset = (int)Math.Round(fraction * Text.Length, MidpointRounding.AwayFromZero);
        return new TextRestoreResult(fallbackOffset, TextRestoreKind.Fraction);
    }
}

public sealed class EncodingSelectionRequiredException : Exception
{
    public EncodingSelectionRequiredException(DecoderFallbackException innerException)
        : base("The text is not valid UTF-8. Choose its original encoding.", innerException)
    {
    }
}

public sealed record TextLocation(
    int SchemaVersion,
    string ContentSha256,
    string EncodingName,
    int CharacterOffset,
    string ContextQuote,
    int ContextOffset,
    double Fraction);

public enum TextRestoreKind
{
    Exact,
    Context,
    Fraction
}

public sealed record TextRestoreResult(int CharacterOffset, TextRestoreKind Kind);
