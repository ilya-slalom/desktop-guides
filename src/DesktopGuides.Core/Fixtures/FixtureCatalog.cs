using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopGuides.Core.Fixtures;

public sealed record FixtureEntry(
    string Id, string Path, string Format, long Bytes, string Sha256, string Expectations);

public sealed class FixtureCatalog
{
    private readonly Dictionary<string, string> paths;

    private FixtureCatalog(List<FixtureEntry> entries, Dictionary<string, string> paths)
    {
        Entries = new ReadOnlyCollection<FixtureEntry>(entries);
        this.paths = paths;
    }

    public IReadOnlyList<FixtureEntry> Entries { get; }

    public static FixtureCatalog Load(string root)
    {
        string fullRoot = System.IO.Path.GetFullPath(root);
        string manifestText = File.ReadAllText(System.IO.Path.Combine(fullRoot, "manifest.json"));
        FixtureManifest? manifest = JsonSerializer.Deserialize<FixtureManifest>(
            manifestText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest?.SchemaVersion != 1 || manifest.Fixtures is null)
        {
            throw new InvalidDataException("Unsupported fixture manifest.");
        }

        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        foreach (FixtureEntry entry in manifest.Fixtures)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || paths.ContainsKey(entry.Id))
            {
                throw new InvalidDataException("Fixture IDs must be nonempty and unique.");
            }

            string[] components = entry.Path.Split(['/', '\\'], StringSplitOptions.None);
            if (System.IO.Path.IsPathRooted(entry.Path) ||
                components.Any(component => component is "" or "." or ".." or ":"))
            {
                throw new InvalidDataException($"Unsafe fixture path: {entry.Path}");
            }
            string filePath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine([fullRoot, .. components]));
            if (!filePath.StartsWith(fullRoot + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Fixture escapes root: {entry.Path}");
            }
            for (string? current = filePath; current is not null; current = System.IO.Path.GetDirectoryName(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"Fixture contains a link: {entry.Path}");
                }
                if (string.Equals(current, fullRoot, OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    break;
                }
            }
            byte[] contents = File.ReadAllBytes(filePath);
            if (contents.LongLength != entry.Bytes ||
                !string.Equals(
                    Convert.ToHexString(SHA256.HashData(contents)),
                    entry.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Fixture hash mismatch: {entry.Id}");
            }
            paths.Add(entry.Id, filePath);
        }

        return new FixtureCatalog(manifest.Fixtures, paths);
    }

    public string Resolve(string id) => paths[id];

    private sealed record FixtureManifest(int SchemaVersion, List<FixtureEntry> Fixtures);
}
