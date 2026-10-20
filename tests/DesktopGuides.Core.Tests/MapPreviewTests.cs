using DesktopGuides.Core.Maps;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class MapPreviewTests
{
    [Theory]
    [InlineData(2048, 1000, 0)]
    [InlineData(15456, 10304, 2048)]
    [InlineData(400, 20000, 41)]
    public void APreviewDecodesToAtMost2048(int w, int h, int width) =>
        Assert.Equal(width, MapPreview.DecodeWidth(w, h));

    // The pane gets the space below and right of the Maps button.
    [Fact]
    public void ThePaneFillsTheSpaceLeftUpToItsCap() =>
        Assert.Equal((1200.0, 900.0), MapPreview.PaneSize(2400, 1300));

    // Review Focus 4: the CI window leaves about 680 × 310 below the toolbar,
    // and the pane must stay inside it.
    [Fact]
    public void ASmallWindowsPaneStaysInsideIt()
    {
        Assert.Equal((632.0, 262.0), MapPreview.PaneSize(680, 310));
        Assert.Equal((0.0, 0.0), MapPreview.PaneSize(20, 30));
    }

    // Review Focus 3.
    [Fact]
    public void AStripFitsThePreviewByWidth() =>
        Assert.Equal((800.0, 16.0), MapPreview.ImageSize(800, 600, 20000, 400));

    [Fact]
    public void ASmallMapIsShrunkToFitOrShownWhole()
    {
        Assert.Equal((400.0, 300.0), MapPreview.ImageSize(800, 300, 640, 480));
        Assert.Equal((64.0, 48.0), MapPreview.ImageSize(800, 600, 64, 48));
    }
}
