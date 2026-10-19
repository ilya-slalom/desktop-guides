using DesktopGuides.Core.Maps;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class MapViewportTests
{
    [Fact]
    public void AMapOpensFittedToTheView()
    {
        MapViewport view = MapViewport.Fitted(15456, 10304, 1600, 900);
        Assert.Equal(900.0 / 10304, view.Fit, 9);
        Assert.True(view.IsFitted);
        Assert.False(view.CanZoomOut);
        Assert.True(view.CanZoomIn);
        Assert.Equal((7728.0, 5152.0), (view.CenterX, view.CenterY));
    }

    [Fact]
    public void ASmallMapFitsAtNoMoreThanFourTimes()
    {
        MapViewport view = MapViewport.Fitted(64, 48, 1600, 900);
        Assert.Equal(ImagePosition.MaxZoom, view.Fit);
        Assert.False(view.CanZoomIn);
    }

    [Fact]
    public void ZoomAboutAPointKeepsThatPointStill()
    {
        MapViewport view = MapViewport.Fitted(4000, 4000, 1000, 1000).ZoomBy(2, 250, 250);
        // The image point under (250, 250) was (1000, 1000); it still is.
        Assert.Equal(0.5, view.Zoom, 9);
        Assert.Equal(1000, view.CenterX - (500 - 250) / view.Zoom, 6);
        Assert.Equal(1000, view.CenterY - (500 - 250) / view.Zoom, 6);
    }

    [Fact]
    public void ZoomStopsAtFourTimes()
    {
        MapViewport view = MapViewport.Fitted(1000, 1000, 1000, 1000);
        for (int i = 0; i < 20; i++) view = view.ZoomIn();
        Assert.Equal(ImagePosition.MaxZoom, view.Zoom);
        Assert.False(view.CanZoomIn);
    }

    [Fact]
    public void AZoomStepIsAQuarter() =>
        Assert.Equal(0.125 * 1.25, MapViewport.Fitted(8000, 8000, 1000, 1000).ZoomIn().Zoom, 9);

    [Fact]
    public void ZoomingOutPastFitFits() =>
        Assert.True(MapViewport.Fitted(8000, 8000, 1000, 1000).ZoomIn().ZoomOut().ZoomOut().IsFitted);

    [Fact]
    public void AZoomWithinOnePercentOfFitIsFitted() =>
        Assert.True(MapViewport.Fitted(8000, 8000, 1000, 1000).ZoomBy(1.009, 500, 500).IsFitted);

    [Fact]
    public void PanningStopsAtTheEdge()
    {
        MapViewport view = MapViewport.Fitted(8000, 8000, 1000, 1000).ZoomBy(8, 500, 500).PanBy(-100000, 0);
        Assert.Equal(8000 - 500 / view.Zoom, view.CenterX, 6);
        Assert.Equal(500 / view.Zoom, view.PanBy(100000, 0).CenterX, 6);
    }

    [Fact]
    public void AnArrowStepPansATenthOfTheView()
    {
        MapViewport view = MapViewport.Fitted(8000, 8000, 1000, 1000).ZoomBy(8, 500, 500);
        Assert.Equal(view.CenterX + 100 / view.Zoom, view.PanStep(1, 0).CenterX, 6);
        Assert.Equal(view.CenterY - 100 / view.Zoom, view.PanStep(0, -1).CenterY, 6);
    }

    // Review Focus 5.
    [Fact]
    public void AStripFitsByWidthAndCentresVertically()
    {
        MapViewport view = MapViewport.Fitted(20000, 400, 2000, 1000).PanBy(0, 5000);
        Assert.Equal(0.1, view.Fit, 9);
        Assert.Equal(200, view.CenterY, 6);
    }

    // Review Focus 1.
    [Fact]
    public void ResizeKeepsTheCentreAndClampsToTheNewFit()
    {
        MapViewport zoomed = MapViewport.Fitted(8000, 8000, 1000, 1000).ZoomBy(8, 500, 500).PanBy(100, 100);
        MapViewport resized = zoomed.Resize(2000, 2000);
        Assert.Equal((zoomed.CenterX, zoomed.CenterY), (resized.CenterX, resized.CenterY));
        Assert.True(MapViewport.Fitted(8000, 8000, 1000, 1000).Resize(4000, 4000).IsFitted);
        Assert.Equal(0.5, MapViewport.Fitted(8000, 8000, 1000, 1000).ZoomBy(2, 500, 500).Resize(4000, 4000).Zoom, 9);
    }

    [Fact]
    public void AFittedPlaceRestoresFitted() =>
        Assert.True(MapViewport.FromPosition(new ImagePosition(null, 0.2, 0.9), 8000, 8000, 1000, 1000).IsFitted);

    [Fact]
    public void APlaceRoundTrips()
    {
        MapViewport view = MapViewport.Fitted(8000, 6000, 1000, 800).ZoomBy(4, 300, 200);
        Assert.Equal(view, MapViewport.FromPosition(view.ToPosition(), 8000, 6000, 1000, 800));
        Assert.Null(MapViewport.Fitted(8000, 6000, 1000, 800).ToPosition().Zoom);
    }

    [Fact]
    public void ARestoredZoomBelowTheNewFitFits() =>
        Assert.True(MapViewport.FromPosition(new ImagePosition(0.05, 0.5, 0.5), 8000, 8000, 1000, 1000).IsFitted);

    [Theory]
    [InlineData(1.0, 0)]
    [InlineData(0.6, 0)]
    [InlineData(0.5, 1)]
    [InlineData(0.3, 1)]
    [InlineData(0.01, 3)]   // the overview, with 3 tiled levels
    public void TheLevelHasAtLeastTheZoomsDetail(double zoom, int level)
    {
        MapViewport view = MapViewport.Fitted(16000, 16000, 100, 100).ZoomBy(zoom / (100.0 / 16000), 50, 50);
        Assert.Equal(level, view.Level(levelCount: 3));
    }

    [Fact]
    public void VisibleTilesAreClippedToTheGrid()
    {
        MapViewport view = MapViewport.Fitted(4096, 4096, 1024, 1024).ZoomBy(4, 0, 0);
        Assert.Equal((0, 0, 2, 2), view.VisibleTiles(level: 0, tileSize: 512));
    }

    [Fact]
    public void VisibleTilesAtALowerLevelUseItsGrid()
    {
        // Fitted at 0.25: level 2 is 1024 × 1024, two tiles of 512 a side.
        MapViewport view = MapViewport.Fitted(4096, 4096, 1024, 1024);
        Assert.Equal((0, 0, 1, 1), view.VisibleTiles(level: 2, tileSize: 512));
    }

    // Final review: on a 4K view, zoomed just past a level's halfway, the
    // visible tiles must fit the tile budget, or they evict each other.
    [Fact]
    public void ATilePlanOnA4kViewFitsTheBudget()
    {
        MapViewport view = MapViewport.Fitted(32768, 32768, 3840, 2160).ZoomBy(0.126 / (2160.0 / 32768), 1920, 1080);
        MapTilePlan plan = view.PlanTiles(levelCount: 4, tileSize: 512, maxTiles: 96);
        Assert.True(plan.Count <= 96, $"{plan.Count} tiles");
        Assert.Equal(3, plan.Level);   // level 2 would need about 160
    }

    [Fact]
    public void ATilePlanKeepsItsMarginWhenItFits()
    {
        MapViewport view = MapViewport.Fitted(4096, 4096, 1024, 1024).ZoomBy(4, 0, 0);
        Assert.Equal(new MapTilePlan(0, 0, 0, 2, 2), view.PlanTiles(levelCount: 3, tileSize: 512, maxTiles: 96));
    }

    [Fact]
    public void ATilePlanDropsItsMarginBeforeItsDetail()
    {
        // At zoom 1, a 1024 view shows 2 × 2 tiles; the margin would make 3 × 3.
        MapViewport view = MapViewport.Fitted(4096, 4096, 1024, 1024).ZoomBy(4, 512, 512);
        MapTilePlan plan = view.PlanTiles(levelCount: 3, tileSize: 512, maxTiles: 6);
        Assert.Equal(0, plan.Level);
        Assert.True(plan.Count <= 6, $"{plan.Count} tiles");
    }

    [Fact]
    public void AnOverviewPlanHasNoTiles()
    {
        MapTilePlan plan = MapViewport.Fitted(16000, 16000, 100, 100).PlanTiles(levelCount: 3, tileSize: 512, maxTiles: 96);
        Assert.Equal(3, plan.Level);
        Assert.Equal(0, plan.Count);
    }
}
