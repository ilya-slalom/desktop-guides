using DesktopGuides.Core.Pdf;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfZoomTests
{
    [Fact]
    public void StepsAreFixed() =>
        Assert.Equal([50, 75, 100, 125, 150, 200, 300, 400], PdfZoom.Steps);

    [Fact]
    public void FitIsTheDefault()
    {
        Assert.True(PdfZoom.Fit.IsFit);
        Assert.True(default(PdfZoom).IsFit);
        Assert.False(new PdfZoom(125).IsFit);
    }

    [Theory]
    [InlineData(37.0, 50)]
    [InlineData(80.0, 100)]
    [InlineData(100.0, 125)]
    [InlineData(99.8, 125)]
    [InlineData(250.0, 300)]
    public void InFromFitPicksTheFirstStepAbove(double fitPercent, int expected) =>
        Assert.Equal(new PdfZoom(expected), PdfZoom.Fit.In(fitPercent));

    [Theory]
    [InlineData(80.0, 75)]
    [InlineData(100.0, 75)]
    [InlineData(100.3, 75)]
    [InlineData(450.0, 400)]
    public void OutFromFitPicksTheLastStepBelow(double fitPercent, int expected) =>
        Assert.Equal(new PdfZoom(expected), PdfZoom.Fit.Out(fitPercent));

    [Fact]
    public void FromAStepZoomMovesOneStep()
    {
        Assert.Equal(new PdfZoom(150), new PdfZoom(125).In(37));
        Assert.Equal(new PdfZoom(100), new PdfZoom(125).Out(500));
    }

    [Fact]
    public void TheEndsAreUnavailable()
    {
        Assert.False(new PdfZoom(400).CanZoomIn(80));
        Assert.Equal(new PdfZoom(400), new PdfZoom(400).In(80));
        Assert.False(new PdfZoom(50).CanZoomOut(80));
        Assert.Equal(new PdfZoom(50), new PdfZoom(50).Out(80));
        Assert.True(new PdfZoom(50).CanZoomIn(80));
        Assert.True(new PdfZoom(400).CanZoomOut(80));
    }

    [Fact]
    public void FitBeyondTheStepsCantGoFurther()
    {
        Assert.False(PdfZoom.Fit.CanZoomIn(400));
        Assert.False(PdfZoom.Fit.CanZoomIn(410));
        Assert.Equal(PdfZoom.Fit, PdfZoom.Fit.In(410));
        Assert.False(PdfZoom.Fit.CanZoomOut(50));
        Assert.False(PdfZoom.Fit.CanZoomOut(30));
    }

    [Fact]
    public void WidthForUsesTheShownPage()
    {
        // pdf-long mixes portrait and landscape pages, whose natural widths differ.
        Assert.Equal(900, PdfZoom.Fit.WidthFor(900, 612));
        Assert.Equal(900, PdfZoom.Fit.WidthFor(900, 792));
        Assert.Equal(765, new PdfZoom(125).WidthFor(900, 612));
        Assert.Equal(990, new PdfZoom(125).WidthFor(900, 792));
    }

    [Fact]
    public void LabelsNameTheZoom()
    {
        Assert.Equal("Fit width", PdfZoom.Fit.Label);
        Assert.Equal("125%", new PdfZoom(125).Label);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnInvalidFitPercentThrows(double fitPercent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfZoom.Fit.In(fitPercent));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfZoom.Fit.CanZoomOut(fitPercent));
    }

    [Theory]
    [InlineData(0.0, 612.0)]
    [InlineData(900.0, 0.0)]
    [InlineData(double.NaN, 612.0)]
    public void AnInvalidWidthThrows(double viewportWidth, double naturalPageWidth) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PdfZoom.Fit.WidthFor(viewportWidth, naturalPageWidth));
}
