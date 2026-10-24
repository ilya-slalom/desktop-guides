using DesktopGuides.Infrastructure.Online;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class VgMapsSystemPageTests
{
    private static readonly Uri Page = new("https://vgmaps.com/Atlas/PSX/index.htm");
    private static readonly VgMapsGame Game = new("Online Test Game", "Online Test Game (PSX)", "PSX/index.htm", "OnlineTestGame");

    private static VgMapsGamePage? Read(VgMapsGame game) =>
        VgMapsSystemPage.Read(VgMapsClient.Decode(P3Bytes.Read("vgmaps/system.htm")), Page, game);

    // Review Focus 1.
    [Fact]
    public void TheGamesTableListsEveryMapInOrder()
    {
        VgMapsGamePage page = Read(Game)!;
        Assert.Equal("© 2026 Test Studio", page.Copyright);
        Assert.Equal(new Uri("https://vgmaps.com/Atlas/PSX/index.htm#OnlineTestGame"), page.PageUrl);
        Assert.Equal(
            [
                ("World", "https://vgmaps.com/Atlas/PSX/OnlineTestGame-World.png", 3400, 1480, 5054136L, "PNG", "Test Ripper", VgMapsSaveable.Yes),
                ("Town", "https://vgmaps.com/Atlas/PSX/OnlineTestGame-Town.png", 800, 600, 557056L, "PNG", "Second Ripper", VgMapsSaveable.Yes),
                ("Cave", "https://vgmaps.com/Atlas/PSX/OnlineTestGame-Cave.gif", 320, 240, 12288L, "GIF", "Test Ripper", VgMapsSaveable.Convert),
                ("Field", "https://vgmaps.com/Atlas/PSX/OnlineTestGame-Field.jpg", 640, 480, 81920L, "JPG", "Test Ripper", VgMapsSaveable.Yes),
                ("Intro", "https://vgmaps.com/Atlas/PSX/OnlineTestGame-Intro.swf", 640, 480, 1258291L, "SWF", "Test Ripper", VgMapsSaveable.NotSaveable),
                ("Huge World", "https://vgmaps.com/Atlas/PSX/OnlineTestGame-Huge.png", 40000, 40000, 943718400L, "PNG", "Test Ripper", VgMapsSaveable.TooLarge),
            ],
            page.Maps.Take(6).Select(map => (map.Area, map.Url.AbsoluteUri, map.Width ?? 0, map.Height ?? 0, map.Bytes ?? 0, map.Format, map.Ripper, map.Saveable)));
    }

    [Fact]
    public void AMapMissingItsSizeIsStillSaveable()
    {
        VgMapsMap sea = Read(Game)!.Maps[^1];
        Assert.Equal(("Sea", null, null, null, null, VgMapsSaveable.Yes), (sea.Area, sea.Width, sea.Height, sea.Bytes, sea.Ripper, sea.Saveable));
        Assert.Equal(7, Read(Game)!.Maps.Count);
    }

    [Fact]
    public void AnotherGamesTableIsItsOwn()
    {
        VgMapsGamePage quest = Read(new VgMapsGame("Example Quest", "Example Quest (PSX)", "PSX/index.htm", "ExampleQuest"))!;
        Assert.Equal(("© 2020 Example Studio", "Town"), (quest.Copyright, Assert.Single(quest.Maps).Area));
    }

    // Review Focus 5.
    [Fact]
    public void AGameWithoutATableHasNoPage() =>
        Assert.Null(Read(new VgMapsGame("Missing Game", "Missing Game (PSX)", "PSX/index.htm", "MissingGame")));

    // Review: a thousands separator is part of the number.
    [Fact]
    public void SizesWithSeparatorsRead()
    {
        string html = VgMapsClient.Decode(P3Bytes.Read("vgmaps/system.htm"))
            .Replace("3400 x 1480", "13,400 x 1,480", StringComparison.Ordinal)
            .Replace("544 kB", "1,544 kB", StringComparison.Ordinal);
        VgMapsGamePage page = VgMapsSystemPage.Read(html, Page, Game)!;
        Assert.Equal((13400, 1480), (page.Maps[0].Width, page.Maps[0].Height));
        Assert.Equal(1544L * 1024, page.Maps[1].Bytes);
    }

    // Review: a map with no name in its row is still a map, named by its place.
    [Fact]
    public void AMapWithoutANameIsNamedByItsPlace()
    {
        string html = VgMapsClient.Decode(P3Bytes.Read("vgmaps/system.htm"))
            .Replace(">Town</a>", "><img src=\"x.png\"></a>", StringComparison.Ordinal)
            .Replace("<p align=\"center\">Town</TD>", "<p align=\"center\"></TD>", StringComparison.Ordinal);
        Assert.Equal("Map 2", VgMapsSystemPage.Read(html, Page, Game)!.Maps[1].Area);
    }
}
