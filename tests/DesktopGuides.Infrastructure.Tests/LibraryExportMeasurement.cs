using System.Diagnostics;
using System.Text.Json;
using DesktopGuides.Core.Backup;
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
