using DesktopGuides.Core.Library;
using DesktopGuides.Core.Maps;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class MapsMenuTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

    private static GuideSummary Txt(string title) => new(new Guide(
        Guid.NewGuid(), Guid.Empty, title, GuideFormat.Txt, "content/x", "guide.txt",
        new string('a', 64), 10, null, null, At, At), null);

    private static GuideSummary Map(string title, int width, int height) => new(new Guide(
        Guid.NewGuid(), Guid.Empty, title, GuideFormat.Image, "content/x", "map.png",
        new string('b', 64), 10, null, null, At, At, width, height), null);

    [Fact]
    public void TheMenuListsOnlyMapsByTitle()
    {
        IReadOnlyList<MapsMenuItem> items = MapsMenu.Items([Txt("Walkthrough"), Map("catacombs", 64, 48), Map("Castle floor 1", 640, 480)]);
        Assert.Equal(["Castle floor 1", "catacombs"], items.Select(item => item.Title));
        Assert.Equal("PNG · 640 × 480", items[0].Facts);
        Assert.Equal((640, 480), (items[0].PixelWidth, items[0].PixelHeight));
    }

    // Review Focus 1.
    [Fact]
    public void AGameWithOneMapHasAMenu() =>
        Assert.Single(MapsMenu.Items([Txt("Walkthrough"), Map("castle", 640, 480)]));

    [Fact]
    public void AGameWithNoMapsHasNone() =>
        Assert.Empty(MapsMenu.Items([Txt("Walkthrough")]));

    [Fact]
    public void FocusStartsOnTheOpenMap()
    {
        IReadOnlyList<MapsMenuItem> items = MapsMenu.Items([Map("a", 10, 10), Map("b", 10, 10)]);
        Assert.Equal(1, MapsMenu.FirstFocus(items, items[1].Id));
        Assert.Equal(0, MapsMenu.FirstFocus(items, Guid.NewGuid()));
        Assert.Equal(0, MapsMenu.FirstFocus(items, null));
    }
}
