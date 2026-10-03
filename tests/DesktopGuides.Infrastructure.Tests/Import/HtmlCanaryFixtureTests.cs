using System.Text;
using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class HtmlCanaryFixtureTests
{
    private static readonly string CanaryRoot =
        Path.Combine(Path.GetDirectoryName(P0Fixtures.Root)!, "p1", "html-canary");

    // Guide B is a "Save Page As, Complete" export named after its page title.
    private const string TitleB = "Canary Guide B (PS1) - Walkthrough's 100% Caf\u00E9 \u2013 v2";

    [Fact]
    public async Task CanaryGuidesPublishOnlyTheirOwnLocalFiles()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guid b = await harness.PublishAsync(
            harness.Publisher(), await harness.InspectAsync(CopyCanary(harness, "b", null)));
        Guid a = await harness.PublishAsync(
            harness.Publisher(), await harness.InspectAsync(CopyCanary(harness, "a", b)));

        Assert.Equal(new[] { "guide.html", "images/a.png", "style.css" }, await RequestPathsAsync(harness, a));
        Assert.Equal(
            new[] { TitleB + "_files/b.png", TitleB + "_files/style.css", "guide.html" },
            await RequestPathsAsync(harness, b));
        string entry = File.ReadAllText(harness.Paths.ResolveExistingGuideFile(a, "guide.html"));
        Assert.Contains(GuideWebOrigin.OriginFor(b).Host, entry, StringComparison.Ordinal);
        Assert.DoesNotContain("__GUIDE_B_ORIGIN__", entry, StringComparison.Ordinal);
    }

    private static async Task<string[]> RequestPathsAsync(PublisherHarness harness, Guid guideId) =>
        (await harness.Repository.GetGuideAssetsAsync(guideId)).Select(asset => asset.RequestPath).ToArray();

    private static string CopyCanary(PublisherHarness harness, string guide, Guid? otherGuide)
    {
        string source = Path.Combine(CanaryRoot, guide);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file).Replace(Path.DirectorySeparatorChar, '/');
            byte[] bytes = File.ReadAllBytes(file);
            if (relative == "guide.html" && otherGuide is Guid other)
            {
                bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(
                    "__GUIDE_B_ORIGIN__", GuideWebOrigin.OriginFor(other).AbsoluteUri.TrimEnd('/'),
                    StringComparison.Ordinal));
            }
            harness.Sources.Write($"{guide}/{relative}", bytes);
        }
        string entry = Assert.Single(Directory.GetFiles(source, "*.html"));
        return Path.Combine(harness.Sources.Root, guide, Path.GetFileName(entry));
    }
}
