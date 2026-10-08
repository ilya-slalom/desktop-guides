# T20.1 Library Export Archive Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The library can be exported to a versioned, checksummed `.zip`. The
archive holds a consistent database snapshot and every referenced guide and
artwork file, and nothing else. Cancellation and failures leave no file
behind, and T20.2's restore can reuse the format and verifier.

**Architecture:**
- **Core:** gains `LibraryArchiveManifest`, a strict JSON codec for
  `manifest.json` written with `Utf8JsonWriter` and parsed with
  `JsonDocument`, and the export contracts.
- **Infrastructure:**
  - `SqliteLibraryRepository` gains `RunExportAsync`, which holds the write
    gate, and a shared `BackupDatabase` helper.
  - `LibraryArchivePlan` reads a snapshot and lists every referenced file
    with its recorded size and hash.
  - `LibraryArchiveVerifier` checks a ZIP against its manifest.
  - `LibraryExporter` runs the flow: destination check, gate, recovery,
    snapshot, plan, streamed write, verification, then rename.

**Tech Stack:** .NET 10, Microsoft.Data.Sqlite, System.IO.Compression,
System.Text.Json (`Utf8JsonWriter` and `JsonDocument` only), xUnit.

**Spec:** `docs/p1/t20-1-library-export-design.md`

**Target:** T20.1. **Prerequisites:** T03.2 (PR #4), T04.4 (PR #14), T06.3
(PR #19) and T15.2 (PR #5), all merged.

## Global Constraints

- No schema change. The schema stays at version 4.
- The archive is a `.zip` with these entries:
  - `manifest.json` first;
  - `library/library.sqlite`;
  - `library/content/<guideId>/<path>`;
  - `library/artwork/<gameId>/<sha256>.<ext>`;
  - nothing else.
- Manifest fields, verbatim: `format` = `desktop-guides-library`,
  `formatVersion` = `1`, `exportId` (Guid `N`), `createdUtc` (ISO 8601 UTC,
  seconds, `Z`), `app.version`, `app.build`, `schemaVersion`,
  `counts.games`, `counts.guides`, `counts.files`, `counts.bytes`, and
  `entries[].path`, `entries[].bytes`, `entries[].sha256`.
- Entries are sorted ordinally by path, with no directory entries and no
  duplicates. Every entry's last-write time is `createdUtc`.
- `counts.files` and `counts.bytes` cover every entry except the manifest.
- Compression: entries ending in `.pdf`, `.png`, `.jpg`, `.jpeg`, `.webp` or
  `.gif` (any case) use `CompressionLevel.NoCompression`. Every other entry
  uses `CompressionLevel.Optimal`.
- Limits: 250,000 entries and a 64 MiB manifest.
- Excluded:
  - `providers.bin`, `providers.bin.tmp`, `.recovery/` and
    `library.session.lock`;
  - `.staging/`, `.trash/` and `.artwork-staging/`;
  - the database sidecars;
  - the cache root;
  - any file the database doesn't reference.
- The database snapshot goes to
  `%TEMP%\desktop-guides-export-<exportId>.sqlite` (`Path.GetTempPath()`).
  The temporary archive is `<destination>.<exportId>.tmp`.
- No reflection-based JSON (`JsonSerializer`) in new product code. Test code may use it.
- `LibraryExportIssue` values, verbatim: `DestinationNotAllowed`,
  `DestinationUnavailable`, `DestinationExists`, `RecoveryIncomplete`,
  `DatabaseInvalid`, `ManagedFilesDamaged`, `LibraryTooLarge`, `WriteFailed`,
  `VerificationFailed`.
- No Production or UI change. T20.2 wires the exporter.
- Infrastructure tests that touch NTFS links return early unless
  `OperatingSystem.IsWindows()`, as the existing tests do.

## Rulings against the spec

None yet. Record any in Task 7's verification record.

## Review Focus

1. **The destination folder is the data root's parent, or a sibling of
   `library/`.** "Inside a protected root" must be decided by whole path
   segment. `C:\DataX` is not inside `C:\Data`, but `C:\Data\library` is.
   Test `DestinationRulesCompareWholeSegments` (Task 5).
2. **Two `GuideAssets` rows share one `RelativePath`.** The archive must hold
   that file once, and the fingerprint must still be computed over every row,
   as import computed it. Test `SharedRelativePathIsArchivedOnce` (Task 4).
3. **The destination already exists and `overwrite` is true, but a stale
   `.tmp` from a crashed export sits beside it.** The new export must use its
   own `exportId` name and leave the stale file alone. Test
   `StaleTempFromAnotherExportIsLeftAlone` (Task 5).
4. **An empty library**, with no games and no guides. The export must
   succeed with the database as its only entry. Test
   `EmptyLibraryExportsTheDatabaseOnly` (Task 5).
5. **A guide path with non-ASCII characters**, such as Guide B's companion
   folder. It must round-trip through the ZIP's UTF-8 names and the verifier.
   This is covered by the HTML guide in `ExportFixture` and the contents test
   (Task 5).

## Host commands

The Mac has no dotnet, so every build and test runs on `pcsx2-win`. Stage the
working tree as a zip, not a tar stream: Windows `tar` decodes UTF-8 names
with the OEM code page (see `docs/p1/e2e-testing.md`).

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  python3 - <<'PY'
import subprocess, zipfile
files = subprocess.run(['git', 'ls-files', '-co', '--exclude-standard', '-z'],
                       capture_output=True, check=True).stdout.decode().split('\0')
with zipfile.ZipFile('/tmp/claude/t20-1.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    for f in files:
        if f and not f.startswith(('.claude/', '.superpowers/')):
            z.write(f)
PY
  scp -q /tmp/claude/t20-1.zip pcsx2-win:E:/work/desktop-guides/t20-1.zip
  s 'powershell -NoProfile -Command "$d = ''E:\work\desktop-guides\t20-1''; if (Test-Path $d) { Remove-Item -Recurse -Force $d }; Expand-Archive E:\work\desktop-guides\t20-1.zip $d"'
}
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t20-1 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
- **Infrastructure tests:** `stage && s 'cd /d E:\work\desktop-guides\t20-1 && dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release'`
- **One class:** append `--filter "FullyQualifiedName~<Class>"` inside the
  quoted command.
- **Production build** (a compile check only):
  `stage && s 'cd /d E:\work\desktop-guides\t20-1 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'`

---

### Task 1: Core archive manifest and export contracts

**Files:**
- Create: `src/DesktopGuides.Core/Backup/LibraryArchiveManifest.cs`
- Create: `src/DesktopGuides.Core/Backup/LibraryExportContracts.cs`
- Test: `tests/DesktopGuides.Core.Tests/LibraryArchiveManifestTests.cs`

**Interfaces:**
- Consumes: `DesktopGuides.Core.Paths.ManagedRelativePath.Parse(string)`, which
  throws `InvalidDataException` for an unsafe path.
- Produces:
  - `LibraryArchiveEntry(string Path, long Bytes, string Sha256)`
  - `LibraryArchiveManifest(Guid ExportId, DateTimeOffset CreatedUtc, string AppVersion, string Build, int SchemaVersion, int Games, int Guides, IReadOnlyList<LibraryArchiveEntry> Entries)`
    with the constants `Format`, `FormatVersion`, `EntryName`, `DatabasePath`,
    `MaxEntries` and `MaxManifestBytes`, plus `long TotalBytes`,
    `static byte[] Write(LibraryArchiveManifest)`,
    `static LibraryArchiveManifest Parse(ReadOnlySpan<byte>)` and
    `static void ValidatePath(string)`.
  - `LibraryExportPhase`, `LibraryExportProgress(Phase, BytesDone, BytesTotal)`,
    `LibraryExportResult(Path, Games, Guides, Files, Bytes, Sha256)`,
    `LibraryExportIssue` and `LibraryExportException(issue, guideIds?, gameIds?, inner?)`
    with `Issue`, `GuideIds` and `GameIds`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using DesktopGuides.Core.Backup;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class LibraryArchiveManifestTests
{
    private static readonly string GuideId = new('1', 32);
    private static readonly string GameId = new('2', 32);
    private static readonly string Hash = new('a', 64);
    private static readonly string ContentPath = $"library/content/{GuideId}/guide.txt";
    private static readonly string ArtworkPath = $"library/artwork/{GameId}/{new string('b', 64)}.png";

    private static LibraryArchiveManifest Sample(params LibraryArchiveEntry[] extra) => new(
        Guid.Parse("0f3c9a1e5b7d4c2a8e6f1b3d5a7c9e0f"),
        new DateTimeOffset(2026, 10, 8, 7, 12, 0, TimeSpan.Zero),
        "1.0.0.0", "msix", 4, 1, 1,
        [
            new(LibraryArchiveManifest.DatabasePath, 4096, Hash),
            new(ContentPath, 12345, Hash),
            new(ArtworkPath, 77, new string('b', 64)),
            .. extra
        ]);

    private static string Json(LibraryArchiveManifest manifest) =>
        Encoding.UTF8.GetString(LibraryArchiveManifest.Write(manifest));

    private static InvalidDataException Rejects(string json) =>
        Assert.Throws<InvalidDataException>(() => LibraryArchiveManifest.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void WriteThenParseRoundTrips()
    {
        LibraryArchiveManifest parsed = LibraryArchiveManifest.Parse(LibraryArchiveManifest.Write(Sample()));

        Assert.Equal(Sample().ExportId, parsed.ExportId);
        Assert.Equal(Sample().CreatedUtc, parsed.CreatedUtc);
        Assert.Equal(("1.0.0.0", "msix", 4, 1, 1),
            (parsed.AppVersion, parsed.Build, parsed.SchemaVersion, parsed.Games, parsed.Guides));
        Assert.Equal(
            new[] { ArtworkPath, ContentPath, LibraryArchiveManifest.DatabasePath },
            parsed.Entries.Select(entry => entry.Path));
        Assert.Equal(4096 + 12345 + 77, parsed.TotalBytes);
    }

    [Fact]
    public void WriteIsDeterministicAndSortsEntries()
    {
        LibraryArchiveManifest reversed = Sample() with { Entries = Sample().Entries.Reverse().ToArray() };

        Assert.Equal(LibraryArchiveManifest.Write(Sample()), LibraryArchiveManifest.Write(reversed));
    }

    [Fact]
    public void WriteRecordsFileAndByteCounts()
    {
        string json = Json(Sample());

        Assert.Contains("\"files\": 3", json);
        Assert.Contains($"\"bytes\": {4096 + 12345 + 77}", json);
        Assert.Contains("\"createdUtc\": \"2026-10-08T07:12:00Z\"", json);
    }

    [Fact]
    public void NonAsciiPathsRoundTrip()
    {
        string path = $"library/content/{GuideId}/Café – v2_files/b.png";
        LibraryArchiveManifest parsed = LibraryArchiveManifest.Parse(
            LibraryArchiveManifest.Write(Sample(new LibraryArchiveEntry(path, 3, Hash))));

        Assert.Contains(parsed.Entries, entry => entry.Path == path);
    }

    [Theory]
    [InlineData("\"desktop-guides-library\"", "\"other-archive\"", "isn't a Desktop Guides")]
    [InlineData("\"formatVersion\": 1", "\"formatVersion\": 2", "version 2")]
    [InlineData("\"schemaVersion\": 4,", "", "schemaVersion")]
    [InlineData("\"files\": 3", "\"files\": 4", "counts")]
    [InlineData("\"bytes\": 12345", "\"bytes\": -12345", "negative size")]
    [InlineData("0f3c9a1e5b7d4c2a8e6f1b3d5a7c9e0f", "not-a-guid", "malformed value")]
    public void ParseRejectsAChangedField(string from, string to, string reason)
    {
        string json = Json(Sample());
        Assert.Contains(from, json);

        Assert.Contains(reason, Rejects(json.Replace(from, to)).Message);
    }

    [Theory]
    [InlineData("guide.txt", "../guide.txt")]
    [InlineData("guide.txt", "a//guide.txt")]
    [InlineData("guide.txt", "a\\\\guide.txt")]
    [InlineData("guide.txt", "c:guide.txt")]
    [InlineData("guide.txt", "guide.txt.")]
    public void ParseRejectsAnUnsafeContentPath(string from, string to)
    {
        string json = Json(Sample());

        Assert.Contains("unsafe entry path", Rejects(json.Replace(from, to)).Message);
    }

    [Theory]
    [InlineData("library/artwork/", "library/elsewhere/")]
    [InlineData("library/artwork/", "/library/artwork/")]
    [InlineData("library/artwork/", "artwork/")]
    [InlineData(".png", ".gif")]
    public void ParseRejectsAPathOfTheWrongShape(string from, string to)
    {
        string json = Json(Sample());

        Assert.Contains("unsafe entry path", Rejects(json.Replace(from, to)).Message);
    }

    [Fact]
    public void ParseRejectsAnUppercaseHash()
    {
        string json = Json(Sample());

        Assert.Contains("malformed hash", Rejects(json.Replace(new string('b', 64) + "\"", new string('B', 64) + "\"")).Message);
    }

    [Fact]
    public void ParseRejectsADuplicatePath()
    {
        string json = Json(Sample(new LibraryArchiveEntry($"library/content/{GuideId}/other.txt", 1, Hash)));

        Assert.Contains("out of order or duplicated",
            Rejects(json.Replace($"{GuideId}/other.txt", $"{GuideId}/guide.txt")).Message);
    }

    [Fact]
    public void ParseRejectsAManifestWithoutTheDatabase()
    {
        string json = Json(Sample());

        Assert.Contains("no database entry",
            Rejects(json.Replace(LibraryArchiveManifest.DatabasePath, $"library/content/{GuideId}/zz.txt")).Message);
    }

    [Fact]
    public void ParseRejectsNonJsonAndAnOversizedManifest()
    {
        Assert.Contains("valid JSON", Rejects("not json").Message);
        InvalidDataException large = Assert.Throws<InvalidDataException>(() =>
            LibraryArchiveManifest.Parse(new byte[LibraryArchiveManifest.MaxManifestBytes + 1]));
        Assert.Contains("too large", large.Message);
    }

    [Fact]
    public void WriteRejectsWhatParseWouldReject()
    {
        Assert.Throws<InvalidDataException>(() => LibraryArchiveManifest.Write(
            Sample() with { Entries = [new($"library/content/{GuideId}/guide.txt", 1, Hash)] }));
        Assert.Throws<InvalidDataException>(() => LibraryArchiveManifest.Write(
            Sample() with
            {
                Entries = Enumerable.Range(0, LibraryArchiveManifest.MaxEntries)
                    .Select(i => new LibraryArchiveEntry($"library/content/{GuideId}/{i:D6}.txt", 0, Hash))
                    .Append(new LibraryArchiveEntry(LibraryArchiveManifest.DatabasePath, 0, Hash))
                    .ToArray()
            }));
    }

    [Fact]
    public void ExportExceptionCarriesItsIssueAndIds()
    {
        Guid guide = Guid.NewGuid();
        LibraryExportException error = new(LibraryExportIssue.ManagedFilesDamaged, [guide]);

        Assert.Equal(LibraryExportIssue.ManagedFilesDamaged, error.Issue);
        Assert.Equal(new[] { guide }, error.GuideIds);
        Assert.Empty(error.GameIds);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Core tests with `--filter "FullyQualifiedName~LibraryArchiveManifestTests"`.
Expected: a build failure, `The type or namespace name 'Backup' does not exist in the namespace 'DesktopGuides.Core'`.

- [ ] **Step 3: Write the contracts**

`src/DesktopGuides.Core/Backup/LibraryExportContracts.cs`:

```csharp
namespace DesktopGuides.Core.Backup;

public enum LibraryExportPhase { Preparing, Writing, Verifying }

public readonly record struct LibraryExportProgress(
    LibraryExportPhase Phase, long BytesDone, long BytesTotal);

/// <summary>The saved archive: its counts, size in bytes and SHA-256.</summary>
public sealed record LibraryExportResult(
    string Path, int Games, int Guides, int Files, long Bytes, string Sha256);

public enum LibraryExportIssue
{
    DestinationNotAllowed,
    DestinationUnavailable,
    DestinationExists,
    RecoveryIncomplete,
    DatabaseInvalid,
    ManagedFilesDamaged,
    LibraryTooLarge,
    WriteFailed,
    VerificationFailed
}

public sealed class LibraryExportException : Exception
{
    public LibraryExportException(
        LibraryExportIssue issue, IReadOnlyList<Guid>? guideIds = null,
        IReadOnlyList<Guid>? gameIds = null, Exception? inner = null)
        : base($"Library export failed: {issue}.", inner)
    {
        Issue = issue;
        GuideIds = guideIds ?? [];
        GameIds = gameIds ?? [];
    }

    public LibraryExportIssue Issue { get; }

    /// <summary>For <see cref="LibraryExportIssue.ManagedFilesDamaged"/>: the affected guides.</summary>
    public IReadOnlyList<Guid> GuideIds { get; }

    /// <summary>For <see cref="LibraryExportIssue.ManagedFilesDamaged"/>: games with damaged artwork.</summary>
    public IReadOnlyList<Guid> GameIds { get; }
}
```

- [ ] **Step 4: Write the manifest codec**

`src/DesktopGuides.Core/Backup/LibraryArchiveManifest.cs`:

```csharp
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
```

`ParseRejectsAnUnsafeContentPath` replaces only the content entry's file
name, so the entry stays in sorted order. `Validate` checks paths before
order, so each case fails on its path, not its order. In `"a\\\\guide.txt"`,
the C# literal is the JSON text `a\\guide.txt`, which decodes to one
backslash.

- [ ] **Step 5: Run the tests to verify they pass**

Run: Core tests with `--filter "FullyQualifiedName~LibraryArchiveManifestTests"`.
Expected: PASS, 25 tests (10 facts and 15 theory cases).

- [ ] **Step 6: Run the whole Core suite**

Run: Core tests.
Expected: PASS, with 906 plus the new tests and no failures.

- [ ] **Step 7: Commit**

```bash
git add src/DesktopGuides.Core/Backup tests/DesktopGuides.Core.Tests/LibraryArchiveManifestTests.cs
git commit -m "feat(core): library archive manifest and export contracts"
```

### Task 2: Repository export gate and shared backup helper

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`.
  Change `CreateRecoveryCopy` (about lines 768–794), and add `BackupTo` and
  `RunExportAsync` next to `RunImportAsync` (about line 897).
- Test: `tests/DesktopGuides.Infrastructure.Tests/LibraryExportRepositoryTests.cs`

**Interfaces:**
- Consumes: the existing private `ValidateDatabase(SqliteConnection, SqliteTransaction?, int)`,
  `OpenConnection()` and `writeGate`.
- Produces:
  - `internal static void SqliteLibraryRepository.BackupTo(SqliteConnection source, string path, int version)`:
    creates `path` (which must not exist), backs up into it, validates it,
    and deletes it and its sidecars on any failure.
  - `internal Task<T> SqliteLibraryRepository.RunExportAsync<T>(Func<SqliteConnection, CancellationToken, Task<T>> work, CancellationToken token)`:
    holds the write gate, opens a connection to the live database, and passes
    it and the token to `work`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibraryExportRepositoryTests : IAsyncLifetime
{
    private RemovalLibrary library = null!;

    public async Task InitializeAsync() => library = await RemovalLibrary.CreateAsync();

    public async Task DisposeAsync() => await library.DisposeAsync();

    private SqliteConnection Live()
    {
        SqliteConnection connection = new($"Data Source={library.Paths.DatabasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private string TempDatabase() => Path.Combine(library.Root, $"copy-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public async Task RunExportAsyncHoldsTheWriteGate()
    {
        TaskCompletionSource entered = new();
        TaskCompletionSource release = new();
        Task<int> export = library.Repository.RunExportAsync(async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }, CancellationToken.None);
        await entered.Task;

        Task<Game> add = library.Repository.AddGameAsync("Waiting", null, null);
        await Task.Delay(200);
        Assert.False(add.IsCompleted);

        release.SetResult();
        Assert.Equal(1, await export);
        Assert.Equal("Waiting", (await add).Title);
    }

    [Fact]
    public async Task RunExportAsyncPassesAnOpenConnectionAndTheToken()
    {
        using CancellationTokenSource source = new();

        (System.Data.ConnectionState state, bool sameToken) = await library.Repository.RunExportAsync(
            (connection, token) => Task.FromResult((connection.State, token == source.Token)), source.Token);

        Assert.Equal(System.Data.ConnectionState.Open, state);
        Assert.True(sameToken);
    }

    [Fact]
    public async Task RunExportAsyncCancelsTheWaitForTheGate()
    {
        TaskCompletionSource release = new();
        Task<bool> holder = library.Repository.RunExportAsync(async (_, _) =>
        {
            await release.Task;
            return true;
        }, CancellationToken.None);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            library.Repository.RunExportAsync((_, _) => Task.FromResult(true), cancelled.Token));

        release.SetResult();
        Assert.True(await holder);
    }

    [Fact]
    public async Task BackupToWritesAValidatedCopy()
    {
        Guid game = await library.AddGameAsync("Copied");
        await library.AddGuideAsync(game, "Guide");
        string copy = TempDatabase();

        using (SqliteConnection live = Live())
        {
            SqliteLibraryRepository.BackupTo(live, copy, 4);
        }

        // A WAL copy can't be opened read-only once its -shm is gone; this only reads.
        using SqliteConnection opened = new($"Data Source={copy};Pooling=False");
        opened.Open();
        using SqliteCommand count = opened.CreateCommand();
        count.CommandText = "SELECT (SELECT COUNT(*) FROM Games) || '|' || (SELECT COUNT(*) FROM Guides)";
        Assert.Equal("1|1", count.ExecuteScalar());
    }

    [Fact]
    public void BackupToDeletesTheCopyWhenValidationFails()
    {
        string copy = TempDatabase();

        using (SqliteConnection live = Live())
        {
            Assert.Throws<InvalidDataException>(() => SqliteLibraryRepository.BackupTo(live, copy, 3));
        }

        Assert.False(File.Exists(copy));
        Assert.False(File.Exists(copy + "-wal"));
        Assert.False(File.Exists(copy + "-shm"));
    }

    [Fact]
    public void BackupToRefusesAnExistingFile()
    {
        string copy = TempDatabase();
        File.WriteAllText(copy, "keep me");

        using (SqliteConnection live = Live())
        {
            Assert.Throws<IOException>(() => SqliteLibraryRepository.BackupTo(live, copy, 4));
        }

        Assert.Equal("keep me", File.ReadAllText(copy));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryExportRepositoryTests"`.
Expected: a build failure, `'SqliteLibraryRepository' does not contain a definition for 'RunExportAsync'`.

- [ ] **Step 3: Extract `BackupTo` from `CreateRecoveryCopy`**

Replace the body of `CreateRecoveryCopy` and add `BackupTo` and
`DeleteDatabaseFiles` below it:

```csharp
    private string CreateRecoveryCopy(SqliteConnection connection, int version)
    {
        string path = Path.Combine(paths.RecoveryRoot,
            $"library-v{version}-{clock.GetUtcNow():yyyyMMddHHmmss}-{Guid.NewGuid():N}.sqlite");
        BackupTo(connection, path, version);
        return path;
    }

    /// <summary>
    /// Writes a consistent copy of the open database to a new file and checks
    /// it as a library of <paramref name="version"/>. A plain file copy of a
    /// live WAL database is not consistent. The file must not exist; it and
    /// its sidecars are deleted again if anything fails.
    /// </summary>
    internal static void BackupTo(SqliteConnection source, string path, int version)
    {
        // Reserve the name without opening an existing file or link.
        using (FileStream created = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
        }
        try
        {
            using SqliteConnection backup = new(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());
            backup.Open();
            source.BackupDatabase(backup);
            ValidateDatabase(backup, null, version);
        }
        catch
        {
            DeleteDatabaseFiles(path);
            throw;
        }
    }

    internal static void DeleteDatabaseFiles(string path)
    {
        foreach (string suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            File.Delete(path + suffix);
        }
    }
```

- [ ] **Step 4: Add `RunExportAsync` after `RunImportAsync`**

```csharp
    /// <summary>
    /// Runs an export under the write gate for its whole duration, with a
    /// connection to the live database, so no write can land between the
    /// snapshot and the last file copied. The token cancels the wait and is
    /// passed to the work.
    /// </summary>
    internal async Task<T> RunExportAsync<T>(
        Func<SqliteConnection, CancellationToken, Task<T>> work, CancellationToken token)
    {
        await writeGate.WaitAsync(token);
        try
        {
            return await Task.Run(async () =>
            {
                using SqliteConnection connection = OpenConnection();
                return await work(connection, token);
            }, token);
        }
        finally
        {
            writeGate.Release();
        }
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryExportRepositoryTests"`.
Expected: PASS, 6 tests.

- [ ] **Step 6: Run the whole Infrastructure suite**

Run: Infrastructure tests.
Expected: PASS, 546 tests (540 plus 6). The migration recovery-copy tests in
`SqliteLibraryRepositoryTests` must still pass.

- [ ] **Step 7: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Infrastructure.Tests/LibraryExportRepositoryTests.cs
git commit -m "feat(storage): export write gate and shared database backup"
```

### Task 3: Archive verifier

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Storage/LibraryArchiveVerifier.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/LibraryArchiveVerifierTests.cs`

**Interfaces:**
- Consumes: `LibraryArchiveManifest.Parse`, `.EntryName` and
  `.MaxManifestBytes` (Task 1).
- Produces: `internal static LibraryArchiveManifest LibraryArchiveVerifier.Verify(Stream zip, CancellationToken token)`.
  It throws `InvalidDataException` unless:
  - the first entry is `manifest.json`;
  - the other entries are exactly the manifest's entries, in manifest order;
  - every length and SHA-256 matches.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO.Compression;
using System.Security.Cryptography;
using DesktopGuides.Core.Backup;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibraryArchiveVerifierTests
{
    private static readonly string GuidePath = $"library/content/{new string('1', 32)}/guide.txt";
    private static readonly (string, byte[]) Database = (LibraryArchiveManifest.DatabasePath, "database"u8.ToArray());
    private static readonly (string, byte[]) Guide = (GuidePath, "guide text"u8.ToArray());

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static (string, byte[]) Manifest(params (string Path, byte[] Bytes)[] files) =>
        (LibraryArchiveManifest.EntryName, LibraryArchiveManifest.Write(new LibraryArchiveManifest(
            Guid.NewGuid(), new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero), "1.0.0.0", "msix", 4, 1, 1,
            files.Select(file => new LibraryArchiveEntry(file.Path, file.Bytes.Length, Sha(file.Bytes))).ToArray())));

    private static MemoryStream Zip(params (string Name, byte[] Bytes)[] entries)
    {
        MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] bytes) in entries)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void Rejected(MemoryStream zip) =>
        Assert.Throws<InvalidDataException>(() => LibraryArchiveVerifier.Verify(zip, CancellationToken.None));

    [Fact]
    public void AnArchiveThatMatchesItsManifestPasses()
    {
        using MemoryStream zip = Zip(Manifest(Database, Guide), Guide, Database);

        LibraryArchiveManifest manifest = LibraryArchiveVerifier.Verify(zip, CancellationToken.None);

        Assert.Equal(new[] { GuidePath, LibraryArchiveManifest.DatabasePath }, manifest.Entries.Select(entry => entry.Path));
    }

    [Fact]
    public void ATruncatedArchiveIsRejected()
    {
        byte[] whole = Zip(Manifest(Database, Guide), Guide, Database).ToArray();

        Rejected(new MemoryStream(whole[..(whole.Length / 2)]));
    }

    [Fact]
    public void AnEntryMissingFromTheManifestIsRejected() =>
        Rejected(Zip(Manifest(Database), Guide, Database));

    [Fact]
    public void AnEntryMissingFromTheArchiveIsRejected() =>
        Rejected(Zip(Manifest(Database, Guide), Database));

    [Fact]
    public void AManifestThatIsNotTheFirstEntryIsRejected() =>
        Rejected(Zip(Guide, Manifest(Database, Guide), Database));

    [Fact]
    public void AnEntryWhoseBytesDoNotMatchItsHashIsRejected() =>
        Rejected(Zip(Manifest(Database, Guide), (GuidePath, "guide texT"u8.ToArray()), Database));

    [Fact]
    public void EntriesOutOfManifestOrderAreRejected() =>
        Rejected(Zip(Manifest(Database, Guide), Database, Guide));

    [Fact]
    public void ACancelledVerifyStops()
    {
        using MemoryStream zip = Zip(Manifest(Database, Guide), Guide, Database);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => LibraryArchiveVerifier.Verify(zip, cancelled.Token));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryArchiveVerifierTests"`.
Expected: a build failure, `The name 'LibraryArchiveVerifier' does not exist in the current context`.

- [ ] **Step 3: Write the verifier**

```csharp
using System.IO.Compression;
using System.Security.Cryptography;
using DesktopGuides.Core.Backup;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>
/// Checks a library archive exactly against its manifest: the manifest
/// first, then every listed entry in order, each with its length and SHA-256.
/// Export runs it before the rename; restore runs it before staging.
/// </summary>
internal static class LibraryArchiveVerifier
{
    private const int BufferBytes = 81920;

    public static LibraryArchiveManifest Verify(Stream zip, CancellationToken token)
    {
        using ZipArchive archive = new(zip, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count == 0 ||
            archive.Entries[0].FullName != LibraryArchiveManifest.EntryName)
        {
            throw new InvalidDataException("The archive's first entry isn't its manifest.");
        }
        LibraryArchiveManifest manifest = LibraryArchiveManifest.Parse(ReadManifest(archive.Entries[0]));
        if (archive.Entries.Count != manifest.Entries.Count + 1)
        {
            throw new InvalidDataException("The archive's entries don't match its manifest.");
        }
        byte[] buffer = new byte[BufferBytes];
        for (int index = 0; index < manifest.Entries.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            LibraryArchiveEntry expected = manifest.Entries[index];
            ZipArchiveEntry actual = archive.Entries[index + 1];
            if (actual.FullName != expected.Path || actual.Length != expected.Bytes)
            {
                throw new InvalidDataException($"The archive entry {actual.FullName} doesn't match its manifest.");
            }
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long read = 0;
            using (Stream stream = actual.Open())
            {
                int count;
                while ((count = stream.Read(buffer)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    read += count;
                    hash.AppendData(buffer, 0, count);
                }
            }
            if (read != expected.Bytes ||
                Convert.ToHexStringLower(hash.GetHashAndReset()) != expected.Sha256)
            {
                throw new InvalidDataException($"The archive entry {expected.Path} doesn't match its hash.");
            }
        }
        return manifest;
    }

    private static byte[] ReadManifest(ZipArchiveEntry entry)
    {
        if (entry.Length > LibraryArchiveManifest.MaxManifestBytes)
        {
            throw new InvalidDataException("The manifest is too large.");
        }
        using Stream stream = entry.Open();
        using MemoryStream copy = new();
        byte[] buffer = new byte[BufferBytes];
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            copy.Write(buffer, 0, count);
            if (copy.Length > LibraryArchiveManifest.MaxManifestBytes)
            {
                throw new InvalidDataException("The manifest is too large.");
            }
        }
        return copy.ToArray();
    }
}
```

`ZipArchive` throws `InvalidDataException` for a truncated or corrupt
archive, so the truncation test needs no extra code.

- [ ] **Step 4: Run the tests to verify they pass**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryArchiveVerifierTests"`.
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Storage/LibraryArchiveVerifier.cs tests/DesktopGuides.Infrastructure.Tests/LibraryArchiveVerifierTests.cs
git commit -m "feat(storage): library archive verifier"
```

### Task 4: Export fixture and archive plan

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/ManagedPathResolver.cs`:
  `private static void RejectFilesystemLinks` becomes `internal static`.
- Create: `src/DesktopGuides.Infrastructure/Storage/LibraryArchivePlan.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/ExportFixture.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/LibraryArchivePlanTests.cs`

**Interfaces:**
- Consumes:
  - `SqliteLibraryRepository.BackupTo` (Task 2);
  - `LibraryArchiveManifest.ValidatePath` and `.MaxEntries` (Task 1);
  - `LibraryExportException` and `LibraryExportIssue` (Task 1);
  - `ILibraryPaths.ResolveExistingGuideFile`, `ManagedArtworkStore.ResolveFile`
    and `GuideFingerprint.OfHtml`.
- Produces:
  - `internal sealed record PlannedArchiveFile(string ArchivePath, string SourcePath, long Bytes, string Sha256, Guid? GuideId, Guid? GameId)`
  - `internal sealed record LibraryArchivePlan(int Games, int Guides, IReadOnlyList<PlannedArchiveFile> Files)`
    with `static LibraryArchivePlan Read(string snapshotPath, ILibraryPaths paths, int maxEntries = LibraryArchiveManifest.MaxEntries)`.
    `Files` is sorted ordinally by `ArchivePath`.
  - Test fixture `ExportFixture` with:
    - `Library`, `Store`, `LinkedGame`, `PlainGame`, `TxtGuide`, `HtmlGuide` and `PdfGuide`;
    - `const string HtmlFolder`;
    - `Content(Guid)`, `ArtworkFile()` and `Snapshot()`;
    - `AddHtmlGuideAsync(Guid game, string title, params (string Request, string Relative, GuideAssetKind Kind, byte[] Bytes)[] files)`;
    - `ExpectedArchivePaths()`.

- [ ] **Step 1: Write the fixture**

`tests/DesktopGuides.Infrastructure.Tests/ExportFixture.cs`:

```csharp
using System.Security.Cryptography;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Tests;

/// <summary>
/// A populated library for the export tests: a linked game with IGDB
/// metadata and artwork, a plain game, and a TXT, an HTML and a PDF guide,
/// plus reading state, a preference and a setting.
/// </summary>
internal sealed class ExportFixture : IAsyncDisposable
{
    public const string HtmlFolder = "Café – v2_files";

    private ExportFixture(RemovalLibrary library)
    {
        Library = library;
        Store = new ManagedArtworkStore(library.Paths);
    }

    public RemovalLibrary Library { get; }
    public ManagedArtworkStore Store { get; }
    public Guid LinkedGame { get; private set; }
    public Guid PlainGame { get; private set; }
    public Guid TxtGuide { get; private set; }
    public Guid HtmlGuide { get; private set; }
    public Guid PdfGuide { get; private set; }
    public string ArtworkRelativePath { get; private set; } = "";

    public static async Task<ExportFixture> CreateAsync()
    {
        ExportFixture fixture = new(await RemovalLibrary.CreateAsync());
        await fixture.PopulateAsync();
        return fixture;
    }

    private async Task PopulateAsync()
    {
        Guid linked = Guid.NewGuid();
        StoredArtwork cover = await Store.StoreAsync(linked, Artwork.TestImages.Png(4, 4), default);
        ArtworkRelativePath = cover.RelativePath;
        LinkedGame = (await Library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            linked, "Linked Game", "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, "900500", DateTimeOffset.UnixEpoch),
            GameMetadataJsonTests.Sample(), cover.RelativePath))).Id;
        PlainGame = await Library.AddGameAsync("Plain Game");
        TxtGuide = await AddFileGuideAsync(LinkedGame, "Walkthrough", GuideFormat.Txt, "guide.txt",
            "Step one.\nStep two.\n"u8.ToArray());
        PdfGuide = await AddFileGuideAsync(PlainGame, "Manual", GuideFormat.Pdf, "manual.pdf",
            "%PDF-1.4 export fixture"u8.ToArray());
        HtmlGuide = await AddHtmlGuideAsync(LinkedGame, "Maps",
            ("guide.html", "guide.html", GuideAssetKind.EntryHtml, "<html>maps</html>"u8.ToArray()),
            ($"{HtmlFolder}/style.css", $"{HtmlFolder}/style.css", GuideAssetKind.StyleSheet, "body{}"u8.ToArray()),
            ($"{HtmlFolder}/b.png", $"{HtmlFolder}/b.png", GuideAssetKind.Image, Artwork.TestImages.Png(2, 2)));
        Library.Execute($$"""
            UPDATE ReadingStates SET LocatorJson = '{"format":"Txt"}', EstimatedFraction = 0.5,
                LastOpenedUtcMs = 1000 WHERE GuideId = '{{TxtGuide:N}}';
            UPDATE ReaderPreferences SET TextScale = 1.25 WHERE GuideId = '{{HtmlGuide:N}}';
            INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('Theme', 'Dark');
            """);
    }

    public string Content(Guid guide) => Library.Paths.GetGuideRoot(guide);

    public string ArtworkFile() => Store.ResolveFile(ArtworkRelativePath)!;

    /// <summary>A validated copy of the live database, as export takes it.</summary>
    public string Snapshot()
    {
        string path = Path.Combine(Library.Root, $"snapshot-{Guid.NewGuid():N}.sqlite");
        using SqliteConnection live = new($"Data Source={Library.Paths.DatabasePath};Pooling=False");
        live.Open();
        SqliteLibraryRepository.BackupTo(live, path, 4);
        return path;
    }

    public IReadOnlyList<string> ExpectedArchivePaths() =>
        new[]
        {
            "library/" + ArtworkRelativePath,
            $"library/content/{TxtGuide:N}/guide.txt",
            $"library/content/{PdfGuide:N}/manual.pdf",
            $"library/content/{HtmlGuide:N}/guide.html",
            $"library/content/{HtmlGuide:N}/{HtmlFolder}/style.css",
            $"library/content/{HtmlGuide:N}/{HtmlFolder}/b.png",
        }.OrderBy(path => path, StringComparer.Ordinal).ToArray();

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public async Task<Guid> AddFileGuideAsync(
        Guid game, string title, GuideFormat format, string name, byte[] bytes)
    {
        Guid operation = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        await Library.Repository.RunImportAsync<bool>((journal, _) =>
        {
            journal.Prepare(operation, id);
            Write(id, name, bytes);
            journal.Publish(new NewImportedGuide(
                operation, id, game, title, format, name, Sha(bytes), bytes.Length, name, null), () => { });
            return Task.FromResult(true);
        }, CancellationToken.None);
        return id;
    }

    public async Task<Guid> AddHtmlGuideAsync(
        Guid game, string title, params (string Request, string Relative, GuideAssetKind Kind, byte[] Bytes)[] files)
    {
        Guid operation = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        GuideAsset[] assets = files
            .Select(file => new GuideAsset(file.Request, file.Relative, file.Kind, file.Bytes.Length, Sha(file.Bytes)))
            .ToArray();
        await Library.Repository.RunImportAsync<bool>((journal, _) =>
        {
            journal.Prepare(operation, id);
            foreach ((_, string relative, _, byte[] bytes) in files)
            {
                Write(id, relative, bytes);
            }
            journal.Publish(new NewImportedGuide(
                operation, id, game, title, GuideFormat.Html, files[0].Relative,
                GuideFingerprint.OfHtml(assets.Select(asset => (asset.RelativePath, asset.Sha256))),
                assets.Sum(asset => asset.ByteCount), title + ".html", null, assets), () => { });
            return Task.FromResult(true);
        }, CancellationToken.None);
        return id;
    }

    private void Write(Guid guide, string relative, byte[] bytes)
    {
        string path = Path.Combine(Library.Paths.GetGuideRoot(guide), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    public ValueTask DisposeAsync() => Library.DisposeAsync();
}
```

The `$$` raw string keeps the JSON braces literal; `{{…}}` interpolates.

- [ ] **Step 2: Write the failing plan tests**

`tests/DesktopGuides.Infrastructure.Tests/LibraryArchivePlanTests.cs`:

```csharp
using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Html;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibraryArchivePlanTests : IAsyncLifetime
{
    private ExportFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await ExportFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private LibraryArchivePlan Plan(int maxEntries = LibraryArchiveManifest.MaxEntries) =>
        LibraryArchivePlan.Read(fixture.Snapshot(), fixture.Library.Paths, maxEntries);

    private LibraryExportException Damaged() =>
        Assert.Throws<LibraryExportException>(() => Plan());

    [Fact]
    public void ThePlanListsEveryReferencedFileWithItsRecordedSizeAndHash()
    {
        LibraryArchivePlan plan = Plan();

        Assert.Equal((2, 3), (plan.Games, plan.Guides));
        Assert.Equal(fixture.ExpectedArchivePaths(), plan.Files.Select(file => file.ArchivePath));
        foreach (PlannedArchiveFile file in plan.Files)
        {
            Assert.Equal(file.Bytes, new FileInfo(file.SourcePath).Length);
            Assert.Equal(file.Sha256,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file.SourcePath))));
        }
        Assert.Equal(fixture.LinkedGame, plan.Files.Single(file => file.ArchivePath.Contains("/artwork/")).GameId);
    }

    [Fact]
    public void StrayFilesAreNotPlanned()
    {
        RemovalLibrary.WriteFile(fixture.Content(fixture.TxtGuide), "stray.txt", "not referenced");
        RemovalLibrary.WriteFile(Path.Combine(fixture.Library.Paths.ContentRoot, Guid.NewGuid().ToString("N")), "x.txt", "orphan");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.ArtworkFile())!, new string('c', 64) + ".png"), "old cover");

        Assert.Equal(fixture.ExpectedArchivePaths(), Plan().Files.Select(file => file.ArchivePath));
    }

    [Fact]
    public void AMissingGuideFileIsDamaged()
    {
        File.Delete(Path.Combine(fixture.Content(fixture.TxtGuide), "guide.txt"));

        LibraryExportException error = Damaged();

        Assert.Equal(LibraryExportIssue.ManagedFilesDamaged, error.Issue);
        Assert.Equal(new[] { fixture.TxtGuide }, error.GuideIds);
        Assert.Empty(error.GameIds);
    }

    [Fact]
    public void AFileOfTheWrongSizeIsDamaged()
    {
        File.AppendAllText(Path.Combine(fixture.Content(fixture.PdfGuide), "manual.pdf"), "x");

        Assert.Equal(new[] { fixture.PdfGuide }, Damaged().GuideIds);
    }

    [Fact]
    public void HtmlRowsThatDisagreeWithTheGuideAreDamaged()
    {
        fixture.Library.Execute($"UPDATE Guides SET ContentBytes = ContentBytes + 1 WHERE Id = '{fixture.HtmlGuide:N}'");

        Assert.Equal(new[] { fixture.HtmlGuide }, Damaged().GuideIds);
    }

    [Fact]
    public void MissingArtworkIsDamaged()
    {
        File.Delete(fixture.ArtworkFile());

        LibraryExportException error = Damaged();

        Assert.Empty(error.GuideIds);
        Assert.Equal(new[] { fixture.LinkedGame }, error.GameIds);
    }

    [Fact]
    public void EveryDamagedGuideIsListed()
    {
        File.Delete(Path.Combine(fixture.Content(fixture.TxtGuide), "guide.txt"));
        File.Delete(Path.Combine(fixture.Content(fixture.PdfGuide), "manual.pdf"));

        Assert.Equal(new[] { fixture.TxtGuide, fixture.PdfGuide }.OrderBy(id => id), Damaged().GuideIds);
    }

    [Fact]
    public async Task SharedRelativePathIsArchivedOnce()
    {
        Guid shared = await fixture.AddHtmlGuideAsync(fixture.PlainGame, "Shared",
            ("guide.html", "guide.html", GuideAssetKind.EntryHtml, "<html/>"u8.ToArray()),
            ("a.png", "a.png", GuideAssetKind.Image, "png"u8.ToArray()),
            ("img/a.png", "a.png", GuideAssetKind.Image, "png"u8.ToArray()));

        LibraryArchivePlan plan = Plan();

        Assert.Single(plan.Files, file => file.ArchivePath == $"library/content/{shared:N}/a.png");
    }

    [Fact]
    public void TooManyEntriesFailsWithLibraryTooLarge()
    {
        LibraryExportException error = Assert.Throws<LibraryExportException>(() => Plan(maxEntries: 3));

        Assert.Equal(LibraryExportIssue.LibraryTooLarge, error.Issue);
    }

    [Fact]
    public void AJunctionedGuideFolderIsDamaged()
    {
        if (!OperatingSystem.IsWindows()) return;
        string folder = fixture.Content(fixture.TxtGuide);
        string moved = Path.Combine(fixture.Library.Root, "moved-guide");
        Directory.Move(folder, moved);
        RemovalLibrary.CreateJunction(folder, moved);
        try
        {
            Assert.Equal(new[] { fixture.TxtGuide }, Damaged().GuideIds);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void ALinkedGuideFileIsDamaged()
    {
        if (!OperatingSystem.IsWindows()) return;
        string file = Path.Combine(fixture.Content(fixture.TxtGuide), "guide.txt");
        string moved = Path.Combine(fixture.Library.Root, "moved-guide.txt");
        File.Move(file, moved);
        File.CreateSymbolicLink(file, moved);
        try
        {
            Assert.Equal(new[] { fixture.TxtGuide }, Damaged().GuideIds);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ALinkedArtworkFileIsDamaged()
    {
        if (!OperatingSystem.IsWindows()) return;
        string file = fixture.ArtworkFile();
        string moved = Path.Combine(fixture.Library.Root, "moved-cover.png");
        File.Move(file, moved);
        File.CreateSymbolicLink(file, moved);
        try
        {
            Assert.Equal(new[] { fixture.LinkedGame }, Damaged().GameIds);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
```

`EveryDamagedGuideIsListed` orders the IDs with `Guid`'s comparer, which is
how the plan orders them.

- [ ] **Step 3: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryArchivePlanTests"`.
Expected: a build failure, `The type or namespace name 'LibraryArchivePlan' could not be found`.

- [ ] **Step 4: Make the link check reusable**

In `ManagedPathResolver.cs`, change `private static void RejectFilesystemLinks(string path)`
to `internal static void RejectFilesystemLinks(string path)`. Change nothing else.

- [ ] **Step 5: Write the plan**

`src/DesktopGuides.Infrastructure/Storage/LibraryArchivePlan.cs`:

```csharp
using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Paths;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Import;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>One file to archive, with what the database recorded for it.</summary>
internal sealed record PlannedArchiveFile(
    string ArchivePath, string SourcePath, long Bytes, string Sha256, Guid? GuideId, Guid? GameId);

/// <summary>
/// Every live file a database snapshot references, each resolved without
/// links and checked for its recorded size. Hashes are checked while the
/// exporter streams the files. Unreferenced files are never listed.
/// </summary>
internal sealed record LibraryArchivePlan(int Games, int Guides, IReadOnlyList<PlannedArchiveFile> Files)
{
    private sealed record GuideRow(Guid Id, string Format, string Primary, string Sha256, long Bytes);

    private sealed record AssetRow(string RelativePath, long Bytes, string Sha256);

    public static LibraryArchivePlan Read(
        string snapshotPath, ILibraryPaths paths, int maxEntries = LibraryArchiveManifest.MaxEntries)
    {
        // The snapshot keeps the live database's WAL mode, which can't be opened
        // read-only once its -shm is gone. Nothing here writes, so the file's
        // bytes don't change before the exporter hashes it.
        using SqliteConnection snapshot = new(new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        snapshot.Open();
        List<(Guid Id, string? Artwork)> games = Query(snapshot,
            "SELECT Id, ArtworkRelativePath FROM Games ORDER BY Id",
            reader => (Guid.ParseExact(reader.GetString(0), "N"), reader.IsDBNull(1) ? null : reader.GetString(1)));
        List<GuideRow> guides = Query(snapshot,
            "SELECT Id, Format, PrimaryRelativePath, ContentSha256, ContentBytes FROM Guides ORDER BY Id",
            reader => new GuideRow(Guid.ParseExact(reader.GetString(0), "N"), reader.GetString(1),
                reader.GetString(2), reader.GetString(3), reader.GetInt64(4)));
        ILookup<Guid, AssetRow> assets = Query(snapshot,
            "SELECT GuideId, RelativePath, ByteCount, Sha256 FROM GuideAssets ORDER BY GuideId, RequestPath",
            reader => (Guid.ParseExact(reader.GetString(0), "N"),
                new AssetRow(reader.GetString(1), reader.GetInt64(2), reader.GetString(3))))
            .ToLookup(row => row.Item1, row => row.Item2);

        List<PlannedArchiveFile> files = [];
        SortedSet<Guid> damagedGuides = [];
        SortedSet<Guid> damagedGames = [];
        foreach (GuideRow guide in guides)
        {
            if (!TryPlanGuide(guide, assets[guide.Id].ToArray(), paths, files))
            {
                damagedGuides.Add(guide.Id);
            }
        }
        ManagedArtworkStore store = new(paths);
        foreach ((Guid id, string? artwork) in games)
        {
            if (artwork is not null && !TryPlanArtwork(id, artwork, store, files))
            {
                damagedGames.Add(id);
            }
        }
        if (damagedGuides.Count > 0 || damagedGames.Count > 0)
        {
            throw new LibraryExportException(
                LibraryExportIssue.ManagedFilesDamaged, damagedGuides.ToArray(), damagedGames.ToArray());
        }
        if (files.Count + 1 > maxEntries)
        {
            throw new LibraryExportException(LibraryExportIssue.LibraryTooLarge);
        }
        return new LibraryArchivePlan(games.Count, guides.Count,
            files.OrderBy(file => file.ArchivePath, StringComparer.Ordinal).ToArray());
    }

    private static bool TryPlanGuide(
        GuideRow guide, AssetRow[] assets, ILibraryPaths paths, List<PlannedArchiveFile> files)
    {
        AssetRow[] planned;
        if (guide.Format == "Html")
        {
            // Import recorded the fingerprint and size over every row, so they're checked the same way.
            if (assets.Length == 0 ||
                assets.Sum(asset => asset.Bytes) != guide.Bytes ||
                GuideFingerprint.OfHtml(assets.Select(asset => (asset.RelativePath, asset.Sha256))) != guide.Sha256 ||
                !assets.Any(asset => asset.RelativePath == guide.Primary))
            {
                return false;
            }
            IGrouping<string, AssetRow>[] byPath = assets.GroupBy(asset => asset.RelativePath, StringComparer.Ordinal).ToArray();
            if (byPath.Any(group => group.Select(asset => (asset.Bytes, asset.Sha256)).Distinct().Count() != 1))
            {
                return false;
            }
            planned = byPath.Select(group => group.First()).ToArray();
        }
        else
        {
            planned = [new AssetRow(guide.Primary, guide.Bytes, guide.Sha256)];
        }

        List<PlannedArchiveFile> resolved = [];
        foreach (AssetRow asset in planned)
        {
            string archivePath = $"library/content/{guide.Id:N}/{asset.RelativePath}";
            try
            {
                LibraryArchiveManifest.ValidatePath(archivePath);
                string source = paths.ResolveExistingGuideFile(guide.Id, asset.RelativePath);
                if (new FileInfo(source).Length != asset.Bytes)
                {
                    return false;
                }
                resolved.Add(new PlannedArchiveFile(archivePath, source, asset.Bytes, asset.Sha256, guide.Id, null));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return false;
            }
        }
        files.AddRange(resolved);
        return true;
    }

    private static bool TryPlanArtwork(
        Guid gameId, string relativePath, ManagedArtworkStore store, List<PlannedArchiveFile> files)
    {
        string archivePath = "library/" + relativePath;
        try
        {
            LibraryArchiveManifest.ValidatePath(archivePath);
            if (store.ResolveFile(relativePath) is not { } source)
            {
                return false;
            }
            ManagedPathResolver.RejectFilesystemLinks(source);
            // Artwork is content-addressed: its name is its SHA-256.
            files.Add(new PlannedArchiveFile(archivePath, source, new FileInfo(source).Length,
                Path.GetFileNameWithoutExtension(source), null, gameId));
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<T> Query<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> read)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        List<T> rows = [];
        while (reader.Read())
        {
            rows.Add(read(reader));
        }
        return rows;
    }
}
```

`FileNotFoundException` and `DirectoryNotFoundException` are both
`IOException`s, so a missing file or folder counts as damaged.
`ResolveExistingGuideFile` throws `InvalidDataException` for a link anywhere
in the path.

- [ ] **Step 6: Run the tests to verify they pass**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryArchivePlanTests"`.
Expected: PASS, 12 tests. The three link tests pass trivially off Windows, but
the suite only runs on `pcsx2-win` and CI.

- [ ] **Step 7: Run the whole Infrastructure suite**

Run: Infrastructure tests.
Expected: PASS, with no failures. `ManagedPathResolverTests` are unchanged.

- [ ] **Step 8: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Storage/LibraryArchivePlan.cs src/DesktopGuides.Infrastructure/Storage/ManagedPathResolver.cs tests/DesktopGuides.Infrastructure.Tests/ExportFixture.cs tests/DesktopGuides.Infrastructure.Tests/LibraryArchivePlanTests.cs
git commit -m "feat(storage): library archive plan from a database snapshot"
```

### Task 5: Library exporter

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Storage/LibraryExporter.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/LibraryExporterTests.cs`

**Interfaces:**
- Consumes:
  - `RunExportAsync`, `BackupTo` and `DeleteDatabaseFiles` (Task 2);
  - `LibraryArchiveVerifier.Verify` (Task 3);
  - `LibraryArchivePlan.Read` and `PlannedArchiveFile` (Task 4);
  - `LibraryArchiveManifest` and the export contracts (Task 1);
  - `FileOperationReconciler.Run(SqliteConnection)` and `LibrarySchema.CurrentVersion`.
- Produces:
  - `public sealed record LibraryExportOptions(string AppVersion, string Build, IReadOnlyList<string> ProtectedRoots)`
  - `internal enum ExportCheckpoint { GateHeld, Snapshotted, Planned, EntryWritten, Written, BeforeRename }`
  - `public LibraryExporter(SqliteLibraryRepository repository, ILibraryPaths paths, LibraryExportOptions options, TimeProvider? clock = null)`
  - `internal LibraryExporter(SqliteLibraryRepository repository, ILibraryPaths paths, LibraryExportOptions options, TimeProvider? clock, Action<ExportCheckpoint> checkpoint, Func<Guid>? newId = null)`
  - `public Task<LibraryExportResult> ExportAsync(string destination, bool overwrite, IProgress<LibraryExportProgress>? progress, CancellationToken token)`

- [ ] **Step 1: Write the failing tests (part 1: contents, consistency, recovery, damage)**

`tests/DesktopGuides.Infrastructure.Tests/LibraryExporterTests.cs`:

```csharp
using System.IO.Compression;
using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibraryExporterTests : IAsyncLifetime
{
    private static readonly Guid ExportId = Guid.Parse("0f3c9a1e5b7d4c2a8e6f1b3d5a7c9e0f");
    private ExportFixture fixture = null!;
    private string output = null!;

    public async Task InitializeAsync()
    {
        fixture = await ExportFixture.CreateAsync();
        output = Path.Combine(fixture.Library.Root, "out");
        Directory.CreateDirectory(output);
    }

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Recorder : IProgress<LibraryExportProgress>
    {
        public List<LibraryExportProgress> Reports { get; } = [];
        public void Report(LibraryExportProgress value) { lock (Reports) Reports.Add(value); }
    }

    private LibraryExporter Exporter(
        Action<ExportCheckpoint>? checkpoint = null, TimeProvider? clock = null, params string[] protectedRoots) =>
        new(fixture.Library.Repository, fixture.Library.Paths,
            new LibraryExportOptions("1.0.0.0", "msix", protectedRoots),
            clock, checkpoint ?? (_ => { }), () => ExportId);

    private string Destination(string name = "backup.zip") => Path.Combine(output, name);

    private Task<LibraryExportResult> Export(LibraryExporter? exporter = null, string? destination = null,
        bool overwrite = false, CancellationToken token = default) =>
        (exporter ?? Exporter()).ExportAsync(destination ?? Destination(), overwrite, null, token);

    private static IReadOnlyList<string> EntryNames(string zip)
    {
        using ZipArchive archive = ZipFile.OpenRead(zip);
        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }

    private IReadOnlyList<string> ExpectedEntryNames() =>
        new[] { LibraryArchiveManifest.EntryName }
            .Concat(fixture.ExpectedArchivePaths().Append(LibraryArchiveManifest.DatabasePath)
                .OrderBy(path => path, StringComparer.Ordinal))
            .ToArray();

    private string Scalar(string zip, string sql)
    {
        string copy = Path.Combine(fixture.Library.Root, $"read-{Guid.NewGuid():N}.sqlite");
        using (ZipArchive archive = ZipFile.OpenRead(zip))
        {
            archive.GetEntry(LibraryArchiveManifest.DatabasePath)!.ExtractToFile(copy);
        }
        using SqliteConnection connection = new($"Data Source={copy};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    private void AssertNothingLeft()
    {
        Assert.Empty(Directory.EnumerateFiles(output, "*.tmp"));
        Assert.Empty(Directory.EnumerateFiles(Path.GetTempPath(), $"desktop-guides-export-{ExportId:N}.sqlite*"));
    }

    private async Task<LibraryExportException> Fails(LibraryExportIssue issue, LibraryExporter? exporter = null,
        string? destination = null, bool overwrite = false)
    {
        LibraryExportException error = await Assert.ThrowsAsync<LibraryExportException>(() =>
            Export(exporter, destination, overwrite));
        Assert.Equal(issue, error.Issue);
        return error;
    }

    [Fact]
    public async Task TheArchiveHoldsExactlyTheReferencedEntriesAndVerifies()
    {
        LibraryExportResult result = await Export();

        Assert.Equal(Destination(), result.Path);
        Assert.Equal(ExpectedEntryNames(), EntryNames(result.Path));
        Assert.Equal((2, 3, 7), (result.Games, result.Guides, result.Files));
        Assert.Equal(new FileInfo(result.Path).Length, result.Bytes);
        await using FileStream stream = File.OpenRead(result.Path);
        Assert.Equal(ExportId, LibraryArchiveVerifier.Verify(stream, CancellationToken.None).ExportId);
        AssertNothingLeft();
    }

    [Fact]
    public async Task TheSnapshotHoldsTheLibraryRows()
    {
        string zip = (await Export()).Path;

        Assert.Equal("2|3|3", Scalar(zip,
            "SELECT (SELECT COUNT(*) FROM Games) || '|' || (SELECT COUNT(*) FROM Guides) || '|' || (SELECT COUNT(*) FROM GuideAssets)"));
        Assert.Equal("0.5", Scalar(zip, $"SELECT EstimatedFraction FROM ReadingStates WHERE GuideId = '{fixture.TxtGuide:N}'"));
        Assert.Equal("1.25", Scalar(zip, $"SELECT TextScale FROM ReaderPreferences WHERE GuideId = '{fixture.HtmlGuide:N}'"));
        Assert.Equal("Dark", Scalar(zip, "SELECT Value FROM Settings WHERE Key = 'Theme'"));
        Assert.Equal("1", Scalar(zip, "SELECT COUNT(*) FROM Games WHERE MetadataJson IS NOT NULL AND ArtworkRelativePath IS NOT NULL"));
    }

    [Fact]
    public async Task NothingOutsideTheReferencedLibraryIsArchived()
    {
        ManagedPathResolver paths = fixture.Library.Paths;
        RemovalLibrary.WriteFile(paths.DataRoot, "providers.bin", "credentials");
        RemovalLibrary.WriteFile(paths.DataRoot, "providers.bin.tmp", "credentials");
        RemovalLibrary.WriteFile(paths.DataRoot, "library.session.lock", "");
        RemovalLibrary.WriteFile(paths.RecoveryRoot, "library-v3-old.sqlite", "old");
        RemovalLibrary.WriteFile(paths.StagingRoot, $"{Guid.NewGuid():N}/{Guid.NewGuid():N}/x.txt", "staged");
        RemovalLibrary.WriteFile(paths.TrashRoot, $"{Guid.NewGuid():N}/{Guid.NewGuid():N}/x.txt", "trashed");
        RemovalLibrary.WriteFile(paths.ArtworkStagingRoot, $"{Guid.NewGuid():N}.tmp", "staged cover");
        RemovalLibrary.WriteFile(paths.DataRoot, "Cache/WebView2/profile/x.bin", "profile");
        RemovalLibrary.WriteFile(paths.DataRoot, "Cache/diagnostics/html-session.json", "{}");
        RemovalLibrary.WriteFile(fixture.Content(fixture.TxtGuide), "stray.txt", "not referenced");

        string zip = (await Export()).Path;

        Assert.Equal(ExpectedEntryNames(), EntryNames(zip));
    }

    [Fact]
    public async Task AnEditDuringTheExportWaitsAndIsNotInTheSnapshot()
    {
        Task<Game>? late = null;
        LibraryExporter exporter = Exporter(point =>
        {
            if (point == ExportCheckpoint.GateHeld) late = fixture.Library.Repository.AddGameAsync("Late", null, null);
        });

        string zip = (await Export(exporter)).Path;

        Assert.Equal("2", Scalar(zip, "SELECT COUNT(*) FROM Games"));
        Assert.Equal("Late", (await late!).Title);
        Assert.Equal("3", fixture.Library.Scalar("SELECT COUNT(*) FROM Games"));
    }

    [Fact]
    public async Task ALeftoverPreparedImportIsReconciledBeforeTheSnapshot()
    {
        Guid operation = Guid.NewGuid();
        Guid guide = Guid.NewGuid();
        await fixture.Library.Repository.RunImportAsync<bool>((journal, _) =>
        {
            journal.Prepare(operation, guide);
            return Task.FromResult(true);
        }, CancellationToken.None);
        RemovalLibrary.WriteFile(fixture.Library.Paths.GetStagedGuideRoot(operation, guide), "x.txt", "half copied");

        string zip = (await Export()).Path;

        Assert.Equal("0", Scalar(zip, "SELECT COUNT(*) FROM FileOperations"));
        Assert.Equal("0", fixture.Library.Scalar("SELECT COUNT(*) FROM FileOperations"));
        Assert.False(Directory.Exists(fixture.Library.Paths.GetStagedGuideRoot(operation, guide)));
    }

    [Fact]
    public async Task AnOperationRecoveryCannotFinishFailsTheExport()
    {
        fixture.Library.Execute($"""
            INSERT INTO FileOperations (Id, Kind, Phase, ManifestJson, CreatedUtcMs)
            VALUES ('{Guid.NewGuid():N}', 'Import', 'Prepared', 'not json', 0)
            """);

        await Fails(LibraryExportIssue.RecoveryIncomplete);

        Assert.False(File.Exists(Destination()));
        AssertNothingLeft();
    }

    [Fact]
    public async Task AGuideFileChangedInPlaceIsDamaged()
    {
        string file = Path.Combine(fixture.Content(fixture.TxtGuide), "guide.txt");
        File.WriteAllText(file, File.ReadAllText(file).Replace("one", "One"));

        LibraryExportException error = await Fails(LibraryExportIssue.ManagedFilesDamaged);

        Assert.Equal(new[] { fixture.TxtGuide }, error.GuideIds);
        Assert.False(File.Exists(Destination()));
        AssertNothingLeft();
    }

    [Fact]
    public async Task ArtworkWhoseBytesDoNotMatchItsNameIsDamaged()
    {
        byte[] bytes = File.ReadAllBytes(fixture.ArtworkFile());
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(fixture.ArtworkFile(), bytes);

        LibraryExportException error = await Fails(LibraryExportIssue.ManagedFilesDamaged);

        Assert.Equal(new[] { fixture.LinkedGame }, error.GameIds);
        AssertNothingLeft();
    }

    [Fact]
    public async Task DestinationRulesCompareWholeSegments()
    {
        ManagedPathResolver paths = fixture.Library.Paths;
        string package = Path.Combine(fixture.Library.Root, "package");
        Directory.CreateDirectory(Path.Combine(package, "LocalCache"));
        string sibling = paths.DataRoot + "X";
        Directory.CreateDirectory(sibling);
        LibraryExporter exporter = Exporter(protectedRoots: package);

        await Fails(LibraryExportIssue.DestinationNotAllowed, exporter, Path.Combine(paths.DataRoot, "backup.zip"));
        await Fails(LibraryExportIssue.DestinationNotAllowed, exporter, Path.Combine(paths.LibraryRoot, "backup.zip"));
        await Fails(LibraryExportIssue.DestinationNotAllowed, exporter, Path.Combine(package, "LocalCache", "backup.zip"));
        Assert.True(File.Exists((await Export(exporter, Path.Combine(sibling, "backup.zip"))).Path));
    }

    [Fact]
    public async Task TheDestinationMustBeAZipPathInAnExistingFolder()
    {
        await Fails(LibraryExportIssue.DestinationUnavailable, destination: Path.Combine(output, "missing", "backup.zip"));
        await Fails(LibraryExportIssue.DestinationUnavailable, destination: Destination("backup.txt"));
        await Fails(LibraryExportIssue.DestinationUnavailable, destination: "backup.zip");
        AssertNothingLeft();
    }

    [Fact]
    public async Task AnExistingDestinationIsKeptWithoutOverwriteAndReplacedWithIt()
    {
        File.WriteAllText(Destination(), "older backup");

        await Fails(LibraryExportIssue.DestinationExists);
        Assert.Equal("older backup", File.ReadAllText(Destination()));

        await Export(overwrite: true);
        Assert.Equal(ExpectedEntryNames(), EntryNames(Destination()));
    }

    [Theory]
    [InlineData("GateHeld")]
    [InlineData("Snapshotted")]
    [InlineData("Planned")]
    [InlineData("EntryWritten")]
    [InlineData("Written")]
    [InlineData("BeforeRename")]
    public async Task CancellingAtAnyStageLeavesNothingAndReleasesTheGate(string stage)
    {
        ExportCheckpoint target = Enum.Parse<ExportCheckpoint>(stage);
        using CancellationTokenSource cancel = new();
        LibraryExporter exporter = Exporter(point =>
        {
            if (point == target) cancel.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Export(exporter, token: cancel.Token));

        Assert.False(File.Exists(Destination()));
        AssertNothingLeft();
        Assert.Equal("After", (await fixture.Library.Repository.AddGameAsync("After", null, null)).Title);
    }

    [Fact]
    public async Task CancellingWhileWaitingForTheGateLeavesNothing()
    {
        TaskCompletionSource release = new();
        Task<bool> holder = fixture.Library.Repository.RunExportAsync(async (_, _) =>
        {
            await release.Task;
            return true;
        }, CancellationToken.None);
        using CancellationTokenSource cancel = new();

        Task<LibraryExportResult> waiting = Export(token: cancel.Token);
        await Task.Delay(100);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.SetResult();
        await holder;
        Assert.False(File.Exists(Destination()));
        AssertNothingLeft();
    }

    [Fact]
    public async Task ACorruptedTemporaryArchiveFailsVerification()
    {
        LibraryExporter exporter = Exporter(point =>
        {
            if (point != ExportCheckpoint.Written) return;
            string temp = Directory.EnumerateFiles(output, "*.tmp").Single();
            using FileStream stream = new(temp, FileMode.Open, FileAccess.ReadWrite);
            stream.Position = stream.Length / 2;
            stream.WriteByte((byte)(stream.ReadByte() ^ 0xFF));
        });

        await Fails(LibraryExportIssue.VerificationFailed, exporter);

        Assert.False(File.Exists(Destination()));
        AssertNothingLeft();
    }

    [Fact]
    public async Task TwoExportsWithTheSameClockAreByteIdentical()
    {
        FixedClock clock = new(new DateTimeOffset(2026, 10, 8, 7, 12, 34, 500, TimeSpan.Zero));

        string first = (await Export(Exporter(clock: clock), Destination("first.zip"))).Path;
        string second = (await Export(Exporter(clock: clock), Destination("second.zip"))).Path;

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        using ZipArchive archive = ZipFile.OpenRead(first);
        Assert.All(archive.Entries, entry => Assert.Equal(new DateTime(2026, 10, 8, 7, 12, 34), entry.LastWriteTime.DateTime));
    }

    [Fact]
    public async Task StaleTempFromAnotherExportIsLeftAlone()
    {
        string stale = Destination($"backup.zip.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(stale, "crashed export");

        await Export();

        Assert.Equal("crashed export", File.ReadAllText(stale));
        File.Delete(stale);
    }

    [Fact]
    public async Task EmptyLibraryExportsTheDatabaseOnly()
    {
        await using RemovalLibrary empty = await RemovalLibrary.CreateAsync();
        LibraryExporter exporter = new(empty.Repository, empty.Paths,
            new LibraryExportOptions("1.0.0.0", "portable", []), null, _ => { }, () => ExportId);

        LibraryExportResult result = await exporter.ExportAsync(Destination("empty.zip"), false, null, default);

        Assert.Equal(new[] { LibraryArchiveManifest.EntryName, LibraryArchiveManifest.DatabasePath }, EntryNames(result.Path));
        Assert.Equal((0, 0, 1), (result.Games, result.Guides, result.Files));
    }

    [Fact]
    public async Task ProgressRunsThroughEachPhaseToTheTotal()
    {
        Recorder recorder = new();

        await Exporter().ExportAsync(Destination(), false, recorder, default);

        Assert.Equal(
            new[] { LibraryExportPhase.Preparing, LibraryExportPhase.Writing, LibraryExportPhase.Verifying },
            recorder.Reports.Select(report => report.Phase).Distinct());
        LibraryExportProgress written = recorder.Reports.Last(report => report.Phase == LibraryExportPhase.Writing);
        Assert.True(written.BytesTotal > 0);
        Assert.Equal(written.BytesTotal, written.BytesDone);
    }
}
```

The cancellation theory takes the checkpoint's name as a string, because
`ExportCheckpoint` is internal and xUnit test methods must be public.

- [ ] **Step 2: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryExporterTests"`.
Expected: a build failure, `The type or namespace name 'LibraryExporter' could not be found`.

- [ ] **Step 3: Write the exporter**

`src/DesktopGuides.Infrastructure/Storage/LibraryExporter.cs`:

```csharp
using System.IO.Compression;
using System.Security.Cryptography;
using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Paths;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Storage;

/// <param name="ProtectedRoots">Folders an archive may not be saved in, in addition to the data root.</param>
public sealed record LibraryExportOptions(string AppVersion, string Build, IReadOnlyList<string> ProtectedRoots);

internal enum ExportCheckpoint { GateHeld, Snapshotted, Planned, EntryWritten, Written, BeforeRename }

/// <summary>
/// Exports the library to a .zip: a database snapshot and every file it
/// references, each checked against its recorded SHA-256. The write gate is
/// held from recovery until the last entry is written, so the archive
/// describes one state. Nothing appears at the destination until the
/// finished archive has been verified; a cancelled or failed export leaves
/// no file behind.
/// </summary>
public sealed class LibraryExporter
{
    private const int BufferBytes = 81920;
    private const long ProgressStepBytes = 1024 * 1024;
    private static readonly string[] StoredExtensions = [".pdf", ".png", ".jpg", ".jpeg", ".webp", ".gif"];

    private sealed record ArchiveItem(LibraryArchiveEntry Entry, string SourcePath, Guid? GuideId, Guid? GameId);

    private readonly SqliteLibraryRepository repository;
    private readonly ILibraryPaths paths;
    private readonly LibraryExportOptions options;
    private readonly TimeProvider clock;
    private readonly Action<ExportCheckpoint> checkpoint;
    private readonly Func<Guid> newId;

    public LibraryExporter(
        SqliteLibraryRepository repository, ILibraryPaths paths, LibraryExportOptions options,
        TimeProvider? clock = null)
        : this(repository, paths, options, clock, _ => { })
    {
    }

    internal LibraryExporter(
        SqliteLibraryRepository repository, ILibraryPaths paths, LibraryExportOptions options,
        TimeProvider? clock, Action<ExportCheckpoint> checkpoint, Func<Guid>? newId = null)
    {
        this.repository = repository;
        this.paths = paths;
        this.options = options;
        this.clock = clock ?? TimeProvider.System;
        this.checkpoint = checkpoint;
        this.newId = newId ?? Guid.NewGuid;
    }

    public async Task<LibraryExportResult> ExportAsync(
        string destination, bool overwrite, IProgress<LibraryExportProgress>? progress, CancellationToken token)
    {
        string target = CheckDestination(destination, overwrite);
        Guid exportId = newId();
        DateTimeOffset created = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
        string temp = $"{target}.{exportId:N}.tmp";
        string snapshot = Path.Combine(Path.GetTempPath(), $"desktop-guides-export-{exportId:N}.sqlite");
        progress?.Report(new LibraryExportProgress(LibraryExportPhase.Preparing, 0, 0));
        try
        {
            LibraryArchiveManifest manifest = await repository.RunExportAsync(
                (connection, work) => Task.FromResult(
                    WriteUnderGate(connection, exportId, created, snapshot, temp, progress, work)),
                token);
            string sha256 = Verify(temp, manifest, progress, token);
            checkpoint(ExportCheckpoint.BeforeRename);
            token.ThrowIfCancellationRequested();
            Rename(temp, target, overwrite);
            return new LibraryExportResult(
                target, manifest.Games, manifest.Guides, manifest.Entries.Count, new FileInfo(target).Length, sha256);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
        finally
        {
            TryDeleteDatabase(snapshot);
        }
    }

    private LibraryArchiveManifest WriteUnderGate(
        SqliteConnection connection, Guid exportId, DateTimeOffset created, string snapshot, string temp,
        IProgress<LibraryExportProgress>? progress, CancellationToken token)
    {
        checkpoint(ExportCheckpoint.GateHeld);
        token.ThrowIfCancellationRequested();
        Recover(connection);
        Snapshot(connection, snapshot);
        checkpoint(ExportCheckpoint.Snapshotted);
        token.ThrowIfCancellationRequested();

        LibraryArchivePlan plan = LibraryArchivePlan.Read(snapshot, paths);
        (long databaseBytes, string databaseSha) = HashSnapshot(snapshot, token);
        ArchiveItem[] items = plan.Files
            .Select(file => new ArchiveItem(
                new LibraryArchiveEntry(file.ArchivePath, file.Bytes, file.Sha256), file.SourcePath, file.GuideId, file.GameId))
            .Append(new ArchiveItem(
                new LibraryArchiveEntry(LibraryArchiveManifest.DatabasePath, databaseBytes, databaseSha), snapshot, null, null))
            .OrderBy(item => item.Entry.Path, StringComparer.Ordinal)
            .ToArray();
        LibraryArchiveManifest manifest = new(
            exportId, created, options.AppVersion, options.Build, LibrarySchema.CurrentVersion,
            plan.Games, plan.Guides, items.Select(item => item.Entry).ToArray());
        byte[] manifestJson;
        try
        {
            manifestJson = LibraryArchiveManifest.Write(manifest);
        }
        catch (InvalidDataException error)
        {
            // The plan already capped the entry count; only very long paths get here.
            throw new LibraryExportException(LibraryExportIssue.LibraryTooLarge, inner: error);
        }
        checkpoint(ExportCheckpoint.Planned);
        token.ThrowIfCancellationRequested();

        WriteArchive(temp, created, manifestJson, items, manifest.TotalBytes, progress, token);
        checkpoint(ExportCheckpoint.Written);
        token.ThrowIfCancellationRequested();
        return manifest;
    }

    private void Recover(SqliteConnection connection)
    {
        try
        {
            new FileOperationReconciler(paths).Run(connection);
            using SqliteCommand count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM FileOperations";
            if ((long)count.ExecuteScalar()! != 0)
            {
                throw new InvalidDataException("File operations remain after recovery.");
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new LibraryExportException(LibraryExportIssue.RecoveryIncomplete, inner: error);
        }
    }

    private static void Snapshot(SqliteConnection connection, string snapshot)
    {
        try
        {
            SqliteLibraryRepository.BackupTo(connection, snapshot, LibrarySchema.CurrentVersion);
        }
        catch (Exception error) when (error is InvalidDataException or SqliteException)
        {
            throw new LibraryExportException(LibraryExportIssue.DatabaseInvalid, inner: error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LibraryExportException(LibraryExportIssue.WriteFailed, inner: error);
        }
    }

    private void WriteArchive(
        string temp, DateTimeOffset created, byte[] manifestJson, ArchiveItem[] items, long totalBytes,
        IProgress<LibraryExportProgress>? progress, CancellationToken token)
    {
        SortedSet<Guid> damagedGuides = [];
        SortedSet<Guid> damagedGames = [];
        long done = 0;
        long reported = 0;
        byte[] buffer = new byte[BufferBytes];
        try
        {
            using FileStream file = new(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using (ZipArchive zip = new(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                ZipArchiveEntry manifestEntry = zip.CreateEntry(LibraryArchiveManifest.EntryName, CompressionLevel.Optimal);
                manifestEntry.LastWriteTime = created;
                using (Stream output = manifestEntry.Open())
                {
                    output.Write(manifestJson);
                }
                foreach (ArchiveItem item in items)
                {
                    token.ThrowIfCancellationRequested();
                    ZipArchiveEntry entry = zip.CreateEntry(item.Entry.Path, LevelFor(item.Entry.Path));
                    entry.LastWriteTime = created;
                    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long copied = 0;
                    bool readable = true;
                    using (Stream output = entry.Open())
                    using (FileStream? input = TryOpenSource(item.SourcePath))
                    {
                        readable = input is not null;
                        while (input is not null)
                        {
                            token.ThrowIfCancellationRequested();
                            // Only a failed read marks the file damaged; a failed write
                            // (a full disk) is the archive's fault and fails the export.
                            int count = TryRead(input, buffer);
                            if (count < 0) { readable = false; break; }
                            if (count == 0) break;
                            hash.AppendData(buffer, 0, count);
                            output.Write(buffer, 0, count);
                            copied += count;
                            done += count;
                            if (done - reported >= ProgressStepBytes)
                            {
                                reported = done;
                                progress?.Report(new LibraryExportProgress(LibraryExportPhase.Writing, done, totalBytes));
                            }
                        }
                    }
                    if (!readable || copied != item.Entry.Bytes ||
                        Convert.ToHexStringLower(hash.GetHashAndReset()) != item.Entry.Sha256)
                    {
                        if (item.GuideId is { } guide) damagedGuides.Add(guide);
                        else if (item.GameId is { } game) damagedGames.Add(game);
                        else throw new InvalidDataException("The database snapshot changed while it was archived.");
                    }
                    progress?.Report(new LibraryExportProgress(LibraryExportPhase.Writing, done, totalBytes));
                    checkpoint(ExportCheckpoint.EntryWritten);
                }
            }
            file.Flush(flushToDisk: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new LibraryExportException(LibraryExportIssue.WriteFailed, inner: error);
        }
        if (damagedGuides.Count > 0 || damagedGames.Count > 0)
        {
            throw new LibraryExportException(
                LibraryExportIssue.ManagedFilesDamaged, damagedGuides.ToArray(), damagedGames.ToArray());
        }
    }

    private static string Verify(
        string temp, LibraryArchiveManifest manifest, IProgress<LibraryExportProgress>? progress, CancellationToken token)
    {
        progress?.Report(new LibraryExportProgress(LibraryExportPhase.Verifying, 0, manifest.TotalBytes));
        try
        {
            using FileStream stream = new(temp, FileMode.Open, FileAccess.Read, FileShare.None);
            if (LibraryArchiveVerifier.Verify(stream, token).ExportId != manifest.ExportId)
            {
                throw new InvalidDataException("The archive's manifest isn't the one this export wrote.");
            }
            stream.Position = 0;
            string sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
            progress?.Report(new LibraryExportProgress(LibraryExportPhase.Verifying, manifest.TotalBytes, manifest.TotalBytes));
            return sha256;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new LibraryExportException(LibraryExportIssue.VerificationFailed, inner: error);
        }
    }

    private string CheckDestination(string destination, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination) ||
            !destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new LibraryExportException(LibraryExportIssue.DestinationUnavailable);
        }
        string full = Path.GetFullPath(destination);
        string? folder = Path.GetDirectoryName(full);
        if (folder is null || !Directory.Exists(folder) || Directory.Exists(full))
        {
            throw new LibraryExportException(LibraryExportIssue.DestinationUnavailable);
        }
        List<string> candidates = [folder];
        if (new DirectoryInfo(folder).ResolveLinkTarget(returnFinalTarget: true) is { } linked)
        {
            candidates.Add(linked.FullName);
        }
        IEnumerable<string> roots = options.ProtectedRoots.Append(paths.DataRoot);
        if (candidates.Any(candidate => roots.Any(root => IsInside(candidate, root))))
        {
            throw new LibraryExportException(LibraryExportIssue.DestinationNotAllowed);
        }
        if (!overwrite && File.Exists(full))
        {
            throw new LibraryExportException(LibraryExportIssue.DestinationExists);
        }
        return full;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or below it, by whole segment.</summary>
    private static bool IsInside(string path, string root)
    {
        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void Rename(string temp, string target, bool overwrite)
    {
        try
        {
            File.Move(temp, target, overwrite);
        }
        catch (IOException error) when (!overwrite && File.Exists(target))
        {
            throw new LibraryExportException(LibraryExportIssue.DestinationExists, inner: error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LibraryExportException(LibraryExportIssue.WriteFailed, inner: error);
        }
    }

    private static FileStream? TryOpenSource(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The bytes read, or -1 if the source can't be read.</summary>
    private static int TryRead(FileStream input, byte[] buffer)
    {
        try
        {
            return input.Read(buffer);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private static CompressionLevel LevelFor(string path) =>
        StoredExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            ? CompressionLevel.NoCompression
            : CompressionLevel.Optimal;

    private static (long Bytes, string Sha256) HashSnapshot(string path, CancellationToken token)
    {
        try
        {
            return HashFile(path, token);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LibraryExportException(LibraryExportIssue.WriteFailed, inner: error);
        }
    }

    private static (long Bytes, string Sha256) HashFile(string path, CancellationToken token)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[BufferBytes];
        long bytes = 0;
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, count);
            bytes += count;
        }
        return (bytes, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDatabase(string path)
    {
        try { SqliteLibraryRepository.DeleteDatabaseFiles(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
```

Notes for the implementer:
- `IsInside` compares case-insensitively, because the app runs on Windows,
  where paths are case-insensitive.
- `DeleteDatabaseFiles` comes from Task 2.
- A damaged source file is collected, and the scan continues. The partly
  written entry stays in the temporary archive, which is deleted.
- The `ExportId` check in `Verify` stops a stale `.tmp` with a valid manifest
  from passing for this export.

- [ ] **Step 4: Run the tests to verify they pass**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryExporterTests"`.
Expected: PASS, 23 tests (17 facts and 6 theory cases).

- [ ] **Step 5: Run the whole Infrastructure suite and the Production build**

Run: Infrastructure tests, then the Production build.
Expected: every test passes, and the Production build succeeds with no new
warnings. Production doesn't use the exporter yet; the build only shows
that nothing else broke.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Storage/LibraryExporter.cs tests/DesktopGuides.Infrastructure.Tests/LibraryExporterTests.cs
git commit -m "feat(storage): library exporter with verify-then-rename"
```

### Task 6: Export measurement on the host

**Files:**
- Create: `tests/DesktopGuides.Infrastructure.Tests/LibraryExportMeasurement.cs`

**Interfaces:**
- Consumes: `ExportFixture.AddFileGuideAsync` (made public in Task 4's
  fixture) and `.AddHtmlGuideAsync`, `LibraryExporter`'s internal
  constructor, and `ExportCheckpoint` (Task 5).
- Produces: a measurement test that does nothing unless `DG_EXPORT_MEASURE`
  names an output file. It writes a JSON record that Task 7 copies into the
  verification record.

This task measures and does not change behavior, so it has no failing-first
step. Say so in the ledger.

- [ ] **Step 1: Write the measurement**

```csharp
using System.Diagnostics;
using System.Text.Json;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

/// <summary>
/// Sizes and timings for a representative library. Runs only when
/// DG_EXPORT_MEASURE names an output file, so CI skips it.
/// </summary>
public sealed class LibraryExportMeasurement
{
    [Fact]
    public async Task MeasureARepresentativeLibrary()
    {
        if (Environment.GetEnvironmentVariable("DG_EXPORT_MEASURE") is not { Length: > 0 } outputPath) return;
        await using ExportFixture fixture = await ExportFixture.CreateAsync();
        Random random = new(20261008);
        byte[] Bytes(int length) { byte[] bytes = new byte[length]; random.NextBytes(bytes); return bytes; }
        byte[] Text(int length) => System.Text.Encoding.ASCII.GetBytes(
            string.Concat(Enumerable.Repeat("Go north. Take the key. Open the door.\n", length / 39 + 1))[..length]);

        for (int index = 0; index < 20; index++)
        {
            await fixture.AddFileGuideAsync(fixture.PlainGame, $"Text {index}", GuideFormat.Txt, "guide.txt", Text(200_000));
        }
        for (int index = 0; index < 5; index++)
        {
            (string, string, GuideAssetKind, byte[])[] files =
            [
                ("guide.html", "guide.html", GuideAssetKind.EntryHtml, Text(150_000)),
                ("style.css", "style.css", GuideAssetKind.StyleSheet, Text(20_000)),
                .. Enumerable.Range(0, 50).Select(image =>
                    ($"images/{image}.png", $"images/{image}.png", GuideAssetKind.Image, Bytes(40_000)))
            ];
            await fixture.AddHtmlGuideAsync(fixture.LinkedGame, $"Web {index}", files);
        }
        foreach (int megabytes in new[] { 20, 20, 20, 100 })
        {
            await fixture.AddFileGuideAsync(fixture.PlainGame, $"Manual {megabytes}", GuideFormat.Pdf, "manual.pdf",
                Bytes(megabytes * 1_000_000));
        }

        Stopwatch total = Stopwatch.StartNew();
        TimeSpan gateHeld = TimeSpan.Zero;
        TimeSpan written = TimeSpan.Zero;
        LibraryExporter exporter = new(fixture.Library.Repository, fixture.Library.Paths,
            new LibraryExportOptions("1.0.0.0", "msix", []), null, point =>
            {
                if (point == ExportCheckpoint.GateHeld) gateHeld = total.Elapsed;
                if (point == ExportCheckpoint.Written) written = total.Elapsed;
            });
        string destination = Path.Combine(fixture.Library.Root, "measure.zip");
        LibraryExportResult result = await exporter.ExportAsync(destination, false, null, default);
        total.Stop();

        long libraryBytes = Directory.EnumerateFiles(fixture.Library.Paths.LibraryRoot, "*", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith("-wal") && !file.EndsWith("-shm"))
            .Sum(file => new FileInfo(file).Length);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(new
        {
            result.Games, result.Guides, result.Files, archiveBytes = result.Bytes, libraryBytes,
            gateHeldMs = (long)(written - gateHeld).TotalMilliseconds,
            totalMs = total.ElapsedMilliseconds,
            machine = Environment.MachineName
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

Reflection-based `JsonSerializer` is fine here: it's test-only code, which the
Global Constraints rule doesn't cover.

- [ ] **Step 2: Run it on the host**

Run:

```bash
stage && s 'cd /d E:\work\desktop-guides\t20-1 && set DG_EXPORT_MEASURE=E:\work\desktop-guides\t20-1-measure.json&& dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryExportMeasurement" && type E:\work\desktop-guides\t20-1-measure.json'
```

Expected: PASS and a JSON record with `games` 2, `guides` 32, about 175 MB
of library data, and the archive size and timings. Copy the record to
`docs/p1/evidence/t20-1-library-export/measurement.json`.

- [ ] **Step 3: Confirm CI skips it**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryExportMeasurement"` and no variable set.
Expected: PASS in under a second; the test returns at once.

- [ ] **Step 4: Commit**

```bash
git add tests/DesktopGuides.Infrastructure.Tests/LibraryExportMeasurement.cs docs/p1/evidence/t20-1-library-export/measurement.json
git commit -m "test(storage): measure export size and gate time on a representative library"
```

### Task 7: Documentation and verification record

**Files:**
- Modify: `docs/p1-technical-design.md`, section 2 (*Live layout and path
  rule*) and section 9 (T20.1 and T20.2).
- Modify: `docs/p1/t20-1-library-export-design.md`: the status line and a
  verification record.
- Modify: `docs/p1/implementation-plan.md`: a T20.1 sentence above the M5
  table.
- Create: `docs/p1/evidence/t20-1-library-export/` (Task 6 already added
  `measurement.json`).

Documentation only, with no behavior to test first. Say so in the ledger.

- [ ] **Step 1: Correct the live layout in `p1-technical-design.md` section 2**

Replace, from `For the packaged app, use` through `Tests inject a temporary parent.`:

```markdown
The data root is `ApplicationData.Current.LocalFolder.Path` for the packaged
app and `%LOCALAPPDATA%\DesktopGuides` for the portable build. It holds:

- `library/`;
- `.recovery/`, the pre-migration database copies;
- `library.session.lock`;
- the DPAPI-protected `providers.bin`.

The live `library/` child contains:

- `library.sqlite`;
- `content/<guide-id>/`;
- `artwork/<game-id>/<sha256>.<ext>`, the content-addressed provider artwork
  referenced by `Games.ArtworkRelativePath`;
- `.staging/<operation-id>/`, `.trash/<operation-id>/` and
  `.artwork-staging/`.

Provider metadata lives in `Games` columns. T20.2 adds the restore marker and
the staging it needs to replace the whole library without renaming a
directory into itself. Transient WebView2 profiles and diagnostics go under
the cache root, outside backup scope: `LocalCacheFolder`, or
`%LOCALAPPDATA%\DesktopGuides\Cache` for the portable build. Tests inject a
temporary parent.
```

- [ ] **Step 2: Update section 9**

Replace the whole T20.1 bullet, from `- **T20.1** Define a versioned ZIP manifest`
through `removes that temporary output.`:

```markdown
- **T20.1** Export a versioned ZIP. It holds:
  - `manifest.json`, with the format and schema versions, the export ID and
    time, and each entry's relative path, byte length and SHA-256;
  - a `BackupDatabase` snapshot;
  - every guide and artwork file the snapshot references.

  Hold the library write gate from recovery to the last entry:
  - run the file-operation reconciler, and fail if any operation remains;
  - write the snapshot to `%TEMP%`;
  - fail, naming each guide or game, if a referenced file is missing, is a
    link, or doesn't match its recorded size or hash.

  Exclude credentials, `.recovery/`, staging and trash, the database
  sidecars, the cache root and every unreferenced file. Write a temporary
  sibling of the destination, verify it against its manifest, then rename it.
  Cancellation or failure removes it. See the
  [T20.1 design](p1/t20-1-library-export-design.md).
```

In the T20.2 bullet, replace `` `GameMetadataLinks` artwork reference `` with
`` `Games.ArtworkRelativePath` artwork reference ``.

- [ ] **Step 3: Write the verification record**

In `docs/p1/t20-1-library-export-design.md`:
- Replace the status line with
  `Status: implemented on \`feat/p1-t20-1-library-export\`; see the verification record.`
- Append a `## T20.1 verification record` section with:
  - **Unit tests:** the Core and Infrastructure pass counts from the host run
    in Step 5, and the new test classes with what each covers;
  - **Measurement:** the numbers from
    `evidence/t20-1-library-export/measurement.json`: archive size, library
    size, gate time and total time. Say whether the gate time argues for
    hard-link pinning (the design's out-of-scope item);
  - **Rulings:** every `Ruling:` line from the execution ledger, or "None";
  - **Not run:** an installed smoke, because T20.1 has no UI.

- [ ] **Step 4: Add the implementation-plan sentence**

In `docs/p1/implementation-plan.md`, directly above the M5 table, add:

```markdown
T20.1 is implemented on `feat/p1-t20-1-library-export`; see the
[design](t20-1-library-export-design.md#t201-verification-record).
```

- [ ] **Step 5: Run both suites on the host**

Run: Core tests, then Infrastructure tests.
Expected: both PASS, with Core at 906 plus Task 1's 25 tests and
Infrastructure at 540 plus the new tests (6 + 8 + 12 + 23 + 1 = 50). Record
the exact counts in Step 3's record.

- [ ] **Step 6: Commit**

```bash
git add docs/p1-technical-design.md docs/p1/t20-1-library-export-design.md docs/p1/implementation-plan.md docs/p1/evidence/t20-1-library-export
git commit -m "docs(p1): T20.1 verification record and corrected storage layout"
```

## Traceability

- **T20.1's exit:**
  - a consistent snapshot under one write gate:
    `AnEditDuringTheExportWaitsAndIsNotInTheSnapshot` and
    `RunExportAsyncHoldsTheWriteGate`;
  - provider snapshots and managed artwork: `TheSnapshotHoldsTheLibraryRows`
    and `TheArchiveHoldsExactlyTheReferencedEntriesAndVerifies`;
  - clean cancellation: `CancellingAtAnyStageLeavesNothingAndReleasesTheGate`
    and `CancellingWhileWaitingForTheGateLeavesNothing`;
  - verified by checksums: `AGuideFileChangedInPlaceIsDamaged`,
    `ACorruptedTemporaryArchiveFailsVerification` and
    `LibraryArchiveVerifierTests`.
- **TR20.2:** `NothingOutsideTheReferencedLibraryIsArchived`.
- **TR20.1** stays with T20.2, which reuses `LibraryArchiveVerifier` and
  `LibraryArchiveManifest.Parse`.

## PR outcome

- **Target task:** T20.1.
- **Prerequisites:** T03.2 (PR #4), T04.4 (PR #14), T06.3 (PR #19) and T15.2
  (PR #5), all merged.
- **Outcome:** the library exports to a versioned, checksummed `.zip` holding
  a consistent database snapshot and every referenced guide and artwork file,
  and nothing else. Cancellation and failures leave no file behind. The
  format and verifier are ready for T20.2's restore. There is no UI change,
  so there is no screenshot.
