using System.Text.Json;
using DesktopGuides.Core.Maps;

namespace DesktopGuides.Infrastructure.Maps;

/// <summary>
/// T21.2: large maps' overviews and tiles, on disk under the app's cache
/// folder, keyed by the map file's hash. A cache is built in a .building
/// folder and moved into place only after its manifest is written, so an
/// interrupted build is never taken for a complete one. It's capped, least
/// recently opened first out, pruned of hashes no guide has, and never
/// backed up.
/// </summary>
public sealed class MapTileStore(string cacheRoot, long maxBytes = MapTileStore.MaxBytes)
{
    public const long MaxBytes = 2_147_483_648;
    private const string Manifest = "map-cache.json";
    private const string Building = ".building";
    private const int Version = 1;

    private readonly string root = Path.Combine(
        !string.IsNullOrWhiteSpace(cacheRoot) ? cacheRoot : throw new ArgumentException("A cache root is required.", nameof(cacheRoot)),
        "maps");

    public string Folder(string sha256) => Path.Combine(root, Checked(sha256));

    public string OverviewPath(string sha256) => Path.Combine(Folder(sha256), "overview.png");

    public string TilePath(string sha256, MapTileKey key) => TileIn(Folder(sha256), key);

    public bool IsComplete(string sha256, MapPyramid pyramid)
    {
        ArgumentNullException.ThrowIfNull(pyramid);
        string folder = Folder(sha256);
        try
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, Manifest)));
            JsonElement value = manifest.RootElement;
            (int Width, int Height) overview = pyramid.OverviewSize;
            if (Int(value, "version") != Version || Int(value, "width") != pyramid.Width ||
                Int(value, "height") != pyramid.Height || Int(value, "tileSize") != pyramid.TileSize ||
                Int(value, "levels") != pyramid.Levels || Int(value, "overviewWidth") != overview.Width ||
                Int(value, "overviewHeight") != overview.Height)
            {
                return false;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return false;
        }
        return File.Exists(Path.Combine(folder, "overview.png")) &&
            Enumerable.Range(0, pyramid.Levels).All(level => Directory.Exists(Path.Combine(folder, $"L{level}")));
    }

    public MapTileBuild BeginBuild(string sha256)
    {
        string folder = Folder(sha256);
        string building = folder + Building;
        if (Directory.Exists(building)) Directory.Delete(building, recursive: true);
        Directory.CreateDirectory(building);
        return new MapTileBuild(building, folder);
    }

    // Opening a map makes it the most recently used.
    public void Touch(string sha256)
    {
        string manifest = Path.Combine(Folder(sha256), Manifest);
        try
        {
            if (File.Exists(manifest)) File.SetLastWriteTimeUtc(manifest, DateTime.UtcNow);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A cache that can't be touched is only older than it should be.
        }
    }

    // Drops the least recently opened caches, never a kept one, until the
    // total is within the cap. Returns the bytes left.
    public long Trim(IReadOnlySet<string> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        if (!Directory.Exists(root)) return 0;
        List<(string Folder, string Sha, DateTime Used, long Bytes)> caches = [];
        foreach (string folder in Directory.EnumerateDirectories(root))
        {
            string name = Path.GetFileName(folder);
            if (!IsHash(name)) continue;
            string manifest = Path.Combine(folder, Manifest);
            DateTime used = File.Exists(manifest) ? File.GetLastWriteTimeUtc(manifest) : DateTime.MinValue;
            caches.Add((folder, name, used, SizeOf(folder)));
        }
        long total = caches.Sum(cache => cache.Bytes);
        foreach (var cache in caches.OrderBy(cache => cache.Used))
        {
            if (total <= maxBytes) break;
            if (keep.Contains(cache.Sha)) continue;
            if (TryDelete(cache.Folder)) total -= cache.Bytes;
        }
        return total;
    }

    // Removes every cache no guide's hash names, and every unfinished build.
    public void Prune(IReadOnlySet<string> liveHashes)
    {
        ArgumentNullException.ThrowIfNull(liveHashes);
        if (!Directory.Exists(root)) return;
        foreach (string folder in Directory.EnumerateDirectories(root))
        {
            string name = Path.GetFileName(folder);
            if (name.EndsWith(Building, StringComparison.Ordinal) || !liveHashes.Contains(name))
            {
                TryDelete(folder);
            }
        }
    }

    internal static string TileIn(string folder, MapTileKey key) =>
        Path.Combine(folder, $"L{key.Level}", $"{key.Column}_{key.Row}.png");

    internal static void WriteManifest(string folder, MapPyramid pyramid)
    {
        (int width, int height) = pyramid.OverviewSize;
        using FileStream file = new(Path.Combine(folder, Manifest), FileMode.Create, FileAccess.Write);
        using Utf8JsonWriter writer = new(file);
        writer.WriteStartObject();
        writer.WriteNumber("version", Version);
        writer.WriteNumber("width", pyramid.Width);
        writer.WriteNumber("height", pyramid.Height);
        writer.WriteNumber("tileSize", pyramid.TileSize);
        writer.WriteNumber("levels", pyramid.Levels);
        writer.WriteNumber("overviewWidth", width);
        writer.WriteNumber("overviewHeight", height);
        writer.WriteEndObject();
    }

    private static int Int(JsonElement value, string name) => value.GetProperty(name).GetInt32();

    private static long SizeOf(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A tile still open in the reader; the next prune or trim tries again.
            return false;
        }
    }

    private static bool IsHash(string value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Checked(string sha256) =>
        IsHash(sha256) ? sha256 : throw new ArgumentException("A map cache is named by a SHA-256 hash.", nameof(sha256));
}

/// <summary>A cache being written; disposed without Commit, it's deleted.</summary>
public sealed class MapTileBuild : IDisposable
{
    private readonly string building;
    private readonly string target;
    private readonly HashSet<int> levels = [];
    private bool committed;

    internal MapTileBuild(string building, string target)
    {
        this.building = building;
        this.target = target;
    }

    public string OverviewPath => Path.Combine(building, "overview.png");

    public string TilePath(MapTileKey key)
    {
        if (levels.Add(key.Level)) Directory.CreateDirectory(Path.Combine(building, $"L{key.Level}"));
        return MapTileStore.TileIn(building, key);
    }

    public void Commit(MapPyramid pyramid)
    {
        ArgumentNullException.ThrowIfNull(pyramid);
        ObjectDisposedException.ThrowIf(committed, this);
        for (int level = 0; level < pyramid.Levels; level++)
        {
            Directory.CreateDirectory(Path.Combine(building, $"L{level}"));
        }
        MapTileStore.WriteManifest(building, pyramid);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.Move(building, target);
        committed = true;
    }

    public void Dispose()
    {
        if (committed || !Directory.Exists(building)) return;
        try
        {
            Directory.Delete(building, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Prune removes it at the next start.
        }
    }
}
