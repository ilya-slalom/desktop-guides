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
}
