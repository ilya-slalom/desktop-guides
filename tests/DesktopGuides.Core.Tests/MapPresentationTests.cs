using DesktopGuides.Core.Library;
using DesktopGuides.Core.Maps;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class MapPresentationTests
{
    private static Guide MapGuide(Guid id, string title, string path = "map.png") => new(
        id, Guid.Empty, title, GuideFormat.Image, "content/x", path,
        "hash", 1, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 10, 10);

    private static GuideSummary Map(Guid id, string title) => new(MapGuide(id, title), null);

    // Review Focus 4.
    [Fact]
    public void MapsSortByTitleThenId()
    {
        Guid a = Guid.Parse("00000000000000000000000000000001");
        Guid b = Guid.Parse("00000000000000000000000000000002");
        GuideSummary[] maps = [Map(b, "castle"), Map(a, "Castle"), Map(a, "Catacombs")];
        Assert.Equal([(a, "Castle"), (b, "castle"), (a, "Catacombs")],
            MapPresentation.Sort(maps).Select(m => (m.Guide.Id, m.Guide.Title)));
    }

    [Theory]
    [InlineData("map.png", MapImageKind.Png)]
    [InlineData("map.jpg", MapImageKind.Jpeg)]
    public void AMapsKindComesFromItsStoredName(string path, MapImageKind kind) =>
        Assert.Equal(kind, MapPresentation.KindOf(MapGuide(Guid.Empty, "m", path)));

    [Fact]
    public void KindsHaveStoredNamesAndLabels()
    {
        Assert.Equal(("map.png", "PNG"), (MapImageKind.Png.StoredName(), MapImageKind.Png.Label()));
        Assert.Equal(("map.jpg", "JPEG"), (MapImageKind.Jpeg.StoredName(), MapImageKind.Jpeg.Label()));
    }
}
