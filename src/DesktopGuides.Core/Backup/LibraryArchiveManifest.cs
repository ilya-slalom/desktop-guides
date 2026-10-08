using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Core.Backup;

public sealed record LibraryArchiveEntry(string Path, long Bytes, string Sha256);

/// <summary>
/// manifest.json of a library export. Written and parsed without reflection;
/// Parse rejects anything Write would not produce.
/// </summary>
public sealed partial record LibraryArchiveManifest(
    Guid ExportId, DateTimeOffset CreatedUtc, string AppVersion, string Build,
    int SchemaVersion, int Games, int Guides, IReadOnlyList<LibraryArchiveEntry> Entries)
{
    public const string Format = "desktop-guides-library";
    public const int FormatVersion = 1;
    public const string EntryName = "manifest.json";
    public const string DatabasePath = "library/library.sqlite";
    public const int MaxEntries = 250_000;
    public const int MaxManifestBytes = 64 * 1024 * 1024;
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    [GeneratedRegex("^library/content/[0-9a-f]{32}/(.+)\\z")]
    private static partial Regex ContentPath();

    [GeneratedRegex("^library/artwork/[0-9a-f]{32}/[0-9a-f]{64}\\.(?:png|jpg|webp)\\z")]
    private static partial Regex ArtworkPath();

    [GeneratedRegex("^[0-9a-f]{64}\\z")]
    private static partial Regex Sha256Hex();

    public long TotalBytes => Entries.Sum(entry => entry.Bytes);

    public static byte[] Write(LibraryArchiveManifest manifest)
    {
        LibraryArchiveEntry[] entries = manifest.Entries
            .OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
        LibraryArchiveManifest sorted = manifest with { Entries = entries };
        Validate(sorted);
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter json = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("format", Format);
            json.WriteNumber("formatVersion", FormatVersion);
            json.WriteString("exportId", sorted.ExportId.ToString("N"));
            json.WriteString("createdUtc",
                sorted.CreatedUtc.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture));
            json.WriteStartObject("app");
            json.WriteString("version", sorted.AppVersion);
            json.WriteString("build", sorted.Build);
            json.WriteEndObject();
            json.WriteNumber("schemaVersion", sorted.SchemaVersion);
            json.WriteStartObject("counts");
            json.WriteNumber("games", sorted.Games);
            json.WriteNumber("guides", sorted.Guides);
            json.WriteNumber("files", entries.Length);
            json.WriteNumber("bytes", sorted.TotalBytes);
            json.WriteEndObject();
            json.WriteStartArray("entries");
            foreach (LibraryArchiveEntry entry in entries)
            {
                json.WriteStartObject();
                json.WriteString("path", entry.Path);
                json.WriteNumber("bytes", entry.Bytes);
                json.WriteString("sha256", entry.Sha256);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        if (buffer.WrittenCount > MaxManifestBytes)
        {
            throw new InvalidDataException("The manifest is too large.");
        }
        return buffer.WrittenSpan.ToArray();
    }

    public static LibraryArchiveManifest Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > MaxManifestBytes)
        {
            throw new InvalidDataException("The manifest is too large.");
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = RequireKind(document.RootElement, JsonValueKind.Object, "the manifest");
            if (Text(root, "format") != Format)
            {
                throw new InvalidDataException("The archive isn't a Desktop Guides library archive.");
            }
            int formatVersion = Number(root, "formatVersion");
            if (formatVersion != FormatVersion)
            {
                throw new InvalidDataException($"The archive format version {formatVersion} isn't supported.");
            }
            Guid exportId = Guid.ParseExact(Text(root, "exportId"), "N");
            DateTimeOffset created = DateTimeOffset.ParseExact(
                Text(root, "createdUtc"), TimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            JsonElement app = Property(root, "app", JsonValueKind.Object);
            JsonElement counts = Property(root, "counts", JsonValueKind.Object);
            JsonElement items = Property(root, "entries", JsonValueKind.Array);
            if (items.GetArrayLength() > MaxEntries)
            {
                throw new InvalidDataException("The manifest lists too many entries.");
            }
            List<LibraryArchiveEntry> entries = new(items.GetArrayLength());
            foreach (JsonElement item in items.EnumerateArray())
            {
                RequireKind(item, JsonValueKind.Object, "an entry");
                entries.Add(new LibraryArchiveEntry(Text(item, "path"), LongNumber(item, "bytes"), Text(item, "sha256")));
            }
            LibraryArchiveManifest manifest = new(
                exportId, created, Text(app, "version"), Text(app, "build"),
                Number(root, "schemaVersion"), Number(counts, "games"), Number(counts, "guides"), entries);
            Validate(manifest);
            if (Number(counts, "files") != entries.Count || LongNumber(counts, "bytes") != manifest.TotalBytes)
            {
                throw new InvalidDataException("The manifest counts don't match its entries.");
            }
            return manifest;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The manifest isn't valid JSON.", error);
        }
        catch (FormatException error)
        {
            throw new InvalidDataException("The manifest has a malformed value.", error);
        }
    }

    /// <summary>Throws unless the path is the database, a content file or an artwork file.</summary>
    public static void ValidatePath(string path)
    {
        if (path == DatabasePath || ArtworkPath().IsMatch(path))
        {
            return;
        }
        Match content = ContentPath().Match(path);
        try
        {
            if (content.Success && ManagedRelativePath.Parse(content.Groups[1].Value) is not null)
            {
                return;
            }
        }
        catch (InvalidDataException)
        {
        }
        throw new InvalidDataException($"The manifest has an unsafe entry path: {path}.");
    }

    private static void Validate(LibraryArchiveManifest manifest)
    {
        if (manifest.ExportId == Guid.Empty ||
            manifest.AppVersion.Length is 0 or > 64 || manifest.Build.Length is 0 or > 64 ||
            manifest.SchemaVersion < 1 || manifest.Games < 0 || manifest.Guides < 0)
        {
            throw new InvalidDataException("The manifest has a malformed value.");
        }
        if (manifest.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException("The manifest lists too many entries.");
        }
        foreach (LibraryArchiveEntry entry in manifest.Entries)
        {
            ValidatePath(entry.Path);
            if (entry.Bytes < 0)
            {
                throw new InvalidDataException($"The manifest has a negative size for {entry.Path}.");
            }
            if (!Sha256Hex().IsMatch(entry.Sha256))
            {
                throw new InvalidDataException($"The manifest has a malformed hash for {entry.Path}.");
            }
        }
        for (int index = 1; index < manifest.Entries.Count; index++)
        {
            if (string.CompareOrdinal(manifest.Entries[index - 1].Path, manifest.Entries[index].Path) >= 0)
            {
                throw new InvalidDataException("The manifest entries are out of order or duplicated.");
            }
        }
        if (!manifest.Entries.Any(entry => entry.Path == DatabasePath))
        {
            throw new InvalidDataException("The manifest has no database entry.");
        }
    }

    private static JsonElement RequireKind(JsonElement element, JsonValueKind kind, string what) =>
        element.ValueKind == kind
            ? element
            : throw new InvalidDataException($"The manifest has a malformed value for {what}.");

    private static JsonElement Property(JsonElement owner, string name, JsonValueKind kind) =>
        owner.TryGetProperty(name, out JsonElement value)
            ? RequireKind(value, kind, name)
            : throw new InvalidDataException($"The manifest is missing {name}.");

    private static string Text(JsonElement owner, string name) =>
        Property(owner, name, JsonValueKind.String).GetString()!;

    private static int Number(JsonElement owner, string name) =>
        Property(owner, name, JsonValueKind.Number).TryGetInt32(out int value)
            ? value
            : throw new InvalidDataException($"The manifest has a malformed value for {name}.");

    private static long LongNumber(JsonElement owner, string name) =>
        Property(owner, name, JsonValueKind.Number).TryGetInt64(out long value)
            ? value
            : throw new InvalidDataException($"The manifest has a malformed value for {name}.");
}
