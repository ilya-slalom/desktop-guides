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

    [Fact]
    public void ParseRejectsSizesThatOverflowTheTotal()
    {
        string json = Json(Sample())
            .Replace("\"bytes\": 4096", "\"bytes\": 9223372036854775807")
            .Replace("\"bytes\": 12345", "\"bytes\": 9223372036854775807");

        Assert.Contains("malformed value", Rejects(json).Message);
    }
}
