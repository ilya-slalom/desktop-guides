using System.Text;

namespace DesktopGuides.Infrastructure.Import;

/// <summary>
/// Decodes a local stylesheet's BOM or leading @charset before CSS parsing.
/// Without either signal, the preview retains its UTF-8 fallback.
/// </summary>
internal static class CssTextDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return DecodeStrict(StrictUtf8, bytes[3..]);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return DecodeStrict(
                new UnicodeEncoding(false, false, true), bytes[2..]);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return DecodeStrict(
                new UnicodeEncoding(true, false, true), bytes[2..]);
        }
        if (TryReadCharset(bytes, out string? label))
        {
            return DecodeStrict(ResolveEncoding(label!), bytes);
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static bool TryReadCharset(
        ReadOnlySpan<byte> bytes,
        out string? label)
    {
        label = null;
        ReadOnlySpan<byte> prefix = "@charset \""u8;
        if (!bytes.StartsWith(prefix))
        {
            return false;
        }
        int labelEnd = bytes[prefix.Length..].IndexOf((byte)'"');
        if (labelEnd < 0 || labelEnd > 64 ||
            bytes.Length <= prefix.Length + labelEnd + 1 ||
            bytes[prefix.Length + labelEnd + 1] != (byte)';')
        {
            return false;
        }
        ReadOnlySpan<byte> rawLabel = bytes.Slice(prefix.Length, labelEnd);
        if (rawLabel.IsEmpty)
        {
            throw new InvalidDataException(
                "CSS encoding declaration is invalid.");
        }
        foreach (byte character in rawLabel)
        {
            if (character is < 0x21 or > 0x7E)
            {
                throw new InvalidDataException(
                    "CSS encoding declaration is invalid.");
            }
        }
        label = Encoding.ASCII.GetString(rawLabel);
        return true;
    }

    private static Encoding ResolveEncoding(string label)
    {
        if (label.Equals("iso-8859-1", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("latin1", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("latin-1", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("us-ascii", StringComparison.OrdinalIgnoreCase))
        {
            label = "windows-1252";
        }
        Encoding? encoding = CodePagesEncodingProvider.Instance
            .GetEncoding(label);
        if (encoding is null)
        {
            try
            {
                encoding = Encoding.GetEncoding(label);
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException(
                    $"CSS encoding '{label}' is unsupported.", error);
            }
        }
        if (encoding.CodePage is 1200 or 1201 or 12000 or 12001)
        {
            throw new InvalidDataException(
                "CSS UTF-16/UTF-32 encoding requires a byte-order mark.");
        }
        Encoding strict = (Encoding)encoding.Clone();
        strict.DecoderFallback = DecoderFallback.ExceptionFallback;
        return strict;
    }

    private static string DecodeStrict(
        Encoding encoding,
        ReadOnlySpan<byte> bytes)
    {
        try
        {
            return encoding.GetString(bytes);
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException(
                "CSS encoding could not decode the stylesheet.", error);
        }
    }
}
