using System.Runtime.InteropServices;
using DesktopGuides.Core.Import;
using DesktopGuides.Infrastructure.Import;
using Xunit;
using Xunit.Abstractions;

namespace DesktopGuides.Infrastructure.Tests.Import;

[CollectionDefinition(nameof(CloudSyncRootImportTests), DisableParallelization = true)]
public sealed class CloudSyncRootCollection;

/// <summary>
/// Host check for #18. It runs only when DG_CLOUD_SYNC_ROOT names a folder
/// inside a OneDrive or other Cloud Files sync root, and writes a small guide
/// there that syncs until the test deletes it. Expose mode makes the sync
/// root's reparse points visible, as a placeholder-aware process sees them.
/// </summary>
[Collection(nameof(CloudSyncRootImportTests))]
public sealed class CloudSyncRootImportTests(ITestOutputHelper output)
{
    private const sbyte DisguisePlaceholders = 1;
    private const sbyte ExposePlaceholders = 2;

    [DllImport("ntdll.dll")]
    private static extern sbyte RtlSetProcessPlaceholderCompatibilityMode(sbyte mode);

    [Theory]
    [InlineData(DisguisePlaceholders)]
    [InlineData(ExposePlaceholders)]
    public async Task HtmlInACloudSyncRootIsReady(sbyte mode)
    {
        string? syncRoot = Environment.GetEnvironmentVariable("DG_CLOUD_SYNC_ROOT");
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(syncRoot)) return;
        string root = Path.Combine(syncRoot.Trim(), "desktop-guides-cloud-" + Guid.NewGuid().ToString("N"));
        sbyte previous = RtlSetProcessPlaceholderCompatibilityMode(mode);
        Assert.True(previous > 0, $"The placeholder mode could not be set: {previous}.");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "guide_files"));
            string entry = Path.Combine(root, "guide.html");
            File.WriteAllText(entry, "<p>Route</p><img src=\"guide_files/map.png\">");
            File.WriteAllBytes(Path.Combine(root, "guide_files", "map.png"), [1, 2, 3]);
            output.WriteLine($"sync root: {File.GetAttributes(syncRoot.Trim())}; entry: {File.GetAttributes(entry)}");

            ImportInspection result = await new GuideImportValidator().InspectAsync(entry, CancellationToken.None);

            HtmlImportManifest manifest = Assert.IsType<HtmlImportManifest>(Assert.IsType<ImportReady>(result).Manifest);
            Assert.Equal(1, manifest.AssetCount);
            Assert.Empty(manifest.Warnings);
        }
        finally
        {
            RtlSetProcessPlaceholderCompatibilityMode(previous);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
