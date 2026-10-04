using DesktopGuides.Core.Pdf;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfRasterBudgetTests
{
    private const double Letter = 792.0 / 612.0;

    [Theory]
    [InlineData(1000, 1024)]
    [InlineData(1024, 1024)]
    [InlineData(1025, 1088)]
    [InlineData(65, 128)]
    [InlineData(1, 64)]
    [InlineData(0.5, 64)]
    [InlineData(10000, 4096)]
    public void RoundsUpToMultiplesOf64WithinTheClamp(double display, int expected) =>
        Assert.Equal(expected, PdfRasterBudget.WidthFor(display, Letter, PdfRasterBudget.MaxBytes).Width);

    [Fact]
    public void TallPagesAreHalvedUntilTheyFit()
    {
        // 4096 x 40960 x 4 and 2048 x 20480 x 4 exceed 96 MiB; 1024 x 10240 x 4 fits.
        Assert.Equal(1024, PdfRasterBudget.WidthFor(4096, 10, PdfRasterBudget.MaxBytes).Width);
    }

    [Fact]
    public void HalvingMayGoBelow64()
    {
        // 64 x 256 x 4 = 65,536 > 16,384; 32 x 128 x 4 = 16,384 fits.
        Assert.Equal(32, PdfRasterBudget.WidthFor(64, 4, 16_384).Width);
    }

    [Fact]
    public void ExtremeAspectRatioIsTooLarge()
    {
        PdfRasterWidth width = PdfRasterBudget.WidthFor(1000, 1e9, PdfRasterBudget.MaxBytes);

        Assert.True(width.IsTooLarge);
        Assert.Equal(PdfRasterWidth.PageTooLarge, width);
    }

    [Theory]
    [InlineData(double.NaN, Letter)]
    [InlineData(double.PositiveInfinity, Letter)]
    [InlineData(0, Letter)]
    [InlineData(-5, Letter)]
    [InlineData(1000, double.NaN)]
    [InlineData(1000, double.PositiveInfinity)]
    [InlineData(1000, 0)]
    [InlineData(1000, -1)]
    public void InvalidInputsAreTooLarge(double display, double ratio) =>
        Assert.True(PdfRasterBudget.WidthFor(display, ratio, PdfRasterBudget.MaxBytes).IsTooLarge);

    [Fact]
    public void NonPositiveCapIsTooLarge() =>
        Assert.True(PdfRasterBudget.WidthFor(1000, Letter, 0).IsTooLarge);

    [Fact]
    public void AFittingWidthIsNeverOverTheCap()
    {
        foreach (double ratio in new[] { 0.01, 0.5, Letter, 3, 25, 400 })
        {
            PdfRasterWidth width = PdfRasterBudget.WidthFor(3000, ratio, PdfRasterBudget.MaxBytes);
            if (width.IsTooLarge) continue;
            Assert.True((double)width.Width * Math.Ceiling(width.Width * ratio) * 4 <= PdfRasterBudget.MaxBytes);
        }
    }
}
