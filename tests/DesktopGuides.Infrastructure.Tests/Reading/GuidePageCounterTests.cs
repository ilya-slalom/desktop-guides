using System.Text;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class GuidePageCounterTests
{
    private const string Hash = "0000000000000000000000000000000000000000000000000000000000000000";

    private static GuideAsset Page(string path) => new(path, path, GuideAssetKind.Page, 1, Hash);

    private static Func<GuideAsset, HtmlAssetRead> Reads(Dictionary<string, string> pages) =>
        asset => new HtmlAssetRead(HtmlAssetReadStatus.Served, Encoding.UTF8.GetBytes(pages[asset.RequestPath]));

    [Fact]
    public void PagesAreCountedInOrder()
    {
        GuideAsset[] pages = [Page("a.html"), Page("b.html"), Page("c.html")];
        Dictionary<string, string> text = new()
        {
            ["a.html"] = "<p>ford ford</p>", ["b.html"] = "<p>road</p>", ["c.html"] = "<p>Ford</p>",
        };

        Assert.Equal([2, 0, 1], GuidePageCounter.Count(pages, Reads(text), new FindQuery("ford"), CancellationToken.None));
    }

    [Fact]
    public void ScriptsAndStylesDontCount() =>
        Assert.Equal([1], GuidePageCounter.Count([Page("a.html")], Reads(new()
        {
            ["a.html"] = "<script>ford</script><style>.ford{}</style><template>ford</template><p>ford</p>",
        }), new FindQuery("ford"), CancellationToken.None));

    // Review Focus 3.
    [Fact]
    public void AnUnreadablePageCountsNothing()
    {
        GuideAsset[] pages = [Page("a.html"), Page("b.html")];
        int[]? counts = GuidePageCounter.Count(pages,
            asset => asset.RequestPath == "b.html"
                ? new HtmlAssetRead(HtmlAssetReadStatus.Changed)
                : new HtmlAssetRead(HtmlAssetReadStatus.Served, Encoding.UTF8.GetBytes("<p>ford</p>")),
            new FindQuery("ford"), CancellationToken.None);

        Assert.Equal([1, 0], counts);
    }

    [Fact]
    public void ACancelledCountIsNoCount() =>
        Assert.Null(GuidePageCounter.Count([Page("a.html")], Reads(new() { ["a.html"] = "<p>ford</p>" }),
            new FindQuery("ford"), new CancellationToken(canceled: true)));
}
