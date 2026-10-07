using System.Globalization;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextSizeStepsTests
{
    [Fact]
    public void StepsAreFixed() =>
        Assert.Equal([0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0], TextSizeSteps.Steps);

    [Fact]
    public void TheDefaultIsAHundredPercent() =>
        Assert.Equal(1.0, TextSizeSteps.Default);

    [Theory]
    [InlineData(null, 1.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 1.0)]
    [InlineData(0.74, 1.0)]
    [InlineData(2.01, 1.0)]
    [InlineData(5.0, 1.0)]
    [InlineData(0.75, 0.75)]
    [InlineData(2.0, 2.0)]
    [InlineData(1.25, 1.25)]
    [InlineData(1.3, 1.3)]
    public void NormalizeKeepsOnlyAStoredValueInRange(double? stored, double expected) =>
        Assert.Equal(expected, TextSizeSteps.Normalize(stored));

    [Theory]
    [InlineData(0.75, 0.9)]
    [InlineData(1.0, 1.1)]
    [InlineData(1.1, 1.25)]
    [InlineData(1.75, 2.0)]
    [InlineData(1.3, 1.5)]
    [InlineData(0.8, 0.9)]
    [InlineData(1.004, 1.1)]
    [InlineData(0.996, 1.1)]
    public void LargerMovesToTheNextStepUp(double scale, double expected) =>
        Assert.Equal(expected, TextSizeSteps.Larger(scale));

    [Theory]
    [InlineData(2.0, 1.75)]
    [InlineData(1.1, 1.0)]
    [InlineData(1.0, 0.9)]
    [InlineData(0.9, 0.75)]
    [InlineData(1.3, 1.25)]
    [InlineData(1.004, 0.9)]
    [InlineData(0.996, 0.9)]
    public void SmallerMovesToTheNextStepDown(double scale, double expected) =>
        Assert.Equal(expected, TextSizeSteps.Smaller(scale));

    [Theory]
    [InlineData(2.0)]
    [InlineData(1.998)]
    public void LargerStopsAtTheTop(double scale)
    {
        Assert.Equal(scale, TextSizeSteps.Larger(scale));
        Assert.False(TextSizeSteps.CanLarger(scale));
        Assert.True(TextSizeSteps.CanSmaller(scale));
    }

    [Theory]
    [InlineData(0.75)]
    [InlineData(0.752)]
    public void SmallerStopsAtTheBottom(double scale)
    {
        Assert.Equal(scale, TextSizeSteps.Smaller(scale));
        Assert.False(TextSizeSteps.CanSmaller(scale));
        Assert.True(TextSizeSteps.CanLarger(scale));
    }

    [Theory]
    [InlineData(0.75, "75%")]
    [InlineData(1.0, "100%")]
    [InlineData(1.1, "110%")]
    [InlineData(1.25, "125%")]
    [InlineData(2.0, "200%")]
    [InlineData(1.3, "130%")]
    [InlineData(1.004, "100%")]
    public void LabelIsTheRoundedPercent(double scale, string expected) =>
        Assert.Equal(expected, TextSizeSteps.Label(scale));

    [Fact]
    public void LabelIgnoresTheCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("125%", TextSizeSteps.Label(1.25));
            Assert.Equal("Text size 125%.", TextSizeSteps.Status(1.25));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void StatusNamesTheSize() =>
        Assert.Equal("Text size 110%.", TextSizeSteps.Status(1.1));

    [Fact]
    public void SaveFailedExplainsTheError() =>
        Assert.Equal(
            "Could not save the text size: database is locked",
            TextSizeSteps.SaveFailed("database is locked"));
}
