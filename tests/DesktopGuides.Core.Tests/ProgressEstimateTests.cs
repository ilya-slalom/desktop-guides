using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ProgressEstimateTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(1.0, 1.0)]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    public void FiniteEstimateIsClampedToTheUnitRange(double estimate, double expected) =>
        Assert.Equal(expected, ProgressEstimate.Bound(estimate));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteEstimateIsDropped(double estimate) =>
        Assert.Null(ProgressEstimate.Bound(estimate));

    [Fact]
    public void MissingEstimateStaysMissing() => Assert.Null(ProgressEstimate.Bound(null));
}
