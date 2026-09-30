using System.Security.Cryptography;
using System.Text;

namespace DesktopGuides.Infrastructure.Import;

internal static class GuideFingerprint
{
    /// <summary>SHA-256 of one "path\tsha\n" line per file, in ordinal path order.</summary>
    public static string OfHtml(IEnumerable<(string RelativePath, string Sha256)> files)
    {
        StringBuilder text = new();
        foreach ((string path, string sha) in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            text.Append(path).Append('\t').Append(sha).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>SHA-256 of a whole file's bytes.</summary>
    public static string OfBytes(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>SHA-256 from the current position, checking the token between 81,920-byte reads.</summary>
    public static string OfStream(Stream stream, CancellationToken token)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int read = stream.Read(buffer);
            if (read == 0)
            {
                break;
            }
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
