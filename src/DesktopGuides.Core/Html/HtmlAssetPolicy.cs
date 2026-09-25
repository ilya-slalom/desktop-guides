using System.Security.Cryptography;

namespace DesktopGuides.Core.Html;

public sealed class HtmlAssetPolicy
{
    private readonly string root;
    private readonly Dictionary<string, AllowedAsset> assets = new(StringComparer.Ordinal);

    public HtmlAssetPolicy(string root, IEnumerable<string> allowedPaths)
    {
        this.root = Path.GetFullPath(root);
        foreach (string path in allowedPaths)
        {
            string fullPath = Path.GetFullPath(path);
            if (!IsWithinRoot(fullPath) || ContainsReparsePoint(fullPath))
            {
                throw new InvalidDataException($"HTML asset escapes its root: {path}");
            }

            string relative = Path.GetRelativePath(this.root, fullPath).Replace('\\', '/');
            string key = "/" + relative;
            byte[] hash = SHA256.HashData(File.ReadAllBytes(fullPath));
            if (!assets.TryAdd(key, new AllowedAsset(fullPath, hash)))
            {
                throw new InvalidDataException($"Duplicate HTML asset: {relative}");
            }
        }
    }

    public string? Resolve(string method, string url)
    {
        if (method != "GET" ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "guide.invalid", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 ||
            uri.UserInfo.Length > 0 ||
            uri.Query.Length > 0)
        {
            return null;
        }

        string relative = Uri.UnescapeDataString(uri.AbsolutePath);
        if (relative.Contains('\\') || relative.Contains('\0') ||
            !assets.TryGetValue(relative, out AllowedAsset? asset) ||
            !IsWithinRoot(asset.Path) ||
            ContainsReparsePoint(asset.Path))
        {
            return null;
        }

        byte[] currentHash = SHA256.HashData(File.ReadAllBytes(asset.Path));
        return CryptographicOperations.FixedTimeEquals(currentHash, asset.Sha256)
            ? asset.Path
            : null;
    }

    private bool IsWithinRoot(string path)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private bool ContainsReparsePoint(string path)
    {
        for (string current = path; !string.Equals(current, root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
             current = Path.GetDirectoryName(current)!)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private sealed record AllowedAsset(string Path, byte[] Sha256);
}
