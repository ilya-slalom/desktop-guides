using DesktopGuides.Core.Pdf;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfPagePositionTests
{
    // A 1,000 px tall page in a 400 px viewport scrolls 600 px.
    private static PdfPagePosition ScrolledTo(int page, double offset)
    {
        PdfPagePosition position = new();
        position.Shown(page);
        position.OffsetFor(1000, 400);
        Assert.True(position.Scrolled(offset, 1000, 400));
        return position;
    }

    [Fact]
    public void ANewPageStartsAtItsTop()
    {
        PdfPagePosition position = ScrolledTo(0, 300);
        position.Target(1, 0);

        position.Shown(1);

        Assert.Equal(1, position.Page);
        Assert.Equal(0, position.Fraction);
        Assert.Equal(0, position.OffsetFor(1000, 400));
    }

    [Fact]
    public void ARestoreTargetAppliesItsFractionOnce()
    {
        PdfPagePosition position = new();
        position.Target(5, 0.4);

        position.Shown(5);
        Assert.Equal(0.4, position.Fraction);
        Assert.Equal(400, position.OffsetFor(1000, 400));

        position.Shown(6);
        position.Shown(5);
        Assert.Equal(0, position.Fraction);
    }

    [Fact]
    public void AnotherPageShownFirstKeepsThePendingTarget()
    {
        PdfPagePosition position = new();
        position.Target(5, 0.4);

        position.Shown(3);
        Assert.Equal(0, position.Fraction);

        position.Shown(5);
        Assert.Equal(0.4, position.Fraction);
    }

    [Fact]
    public void AReRenderKeepsThePoint()
    {
        PdfPagePosition position = ScrolledTo(2, 300);

        position.Shown(2);

        Assert.Equal(0.3, position.Fraction, 6);
    }

    [Fact]
    public void ATargetOnTheShownPageReplacesItsPoint()
    {
        PdfPagePosition position = ScrolledTo(4, 500);
        position.Target(4, 0.2);

        position.Shown(4);

        Assert.Equal(0.2, position.Fraction);
    }

    [Fact]
    public void AWidthChangeReappliesTheFraction()
    {
        PdfPagePosition position = ScrolledTo(2, 300);

        Assert.Equal(600, position.OffsetFor(2000, 400), 6);
    }

    [Fact]
    public void TheBottomClampDoesntReduceTheFraction()
    {
        PdfPagePosition position = new();
        position.Target(0, 0.8);
        position.Shown(0);

        Assert.Equal(600, position.OffsetFor(1000, 400));
        Assert.Equal(0.8, position.Fraction);
        Assert.Equal(2400, position.OffsetFor(3000, 400));
    }

    [Fact]
    public void TheEchoOfAnAppliedOffsetIsIgnored()
    {
        PdfPagePosition position = new();
        position.Target(0, 0.3);
        position.Shown(0);
        double applied = position.OffsetFor(1000, 400);

        Assert.False(position.Scrolled(applied + 0.5, 1000, 400));
        Assert.Equal(0.3, position.Fraction);
    }

    [Fact]
    public void AUserScrollMovesThePointOnce()
    {
        PdfPagePosition position = ScrolledTo(0, 500);

        Assert.Equal(0.5, position.Fraction);
        Assert.False(position.Scrolled(500, 1000, 400));
    }

    [Fact]
    public void AScrollAfterTheLayoutChangedIsIgnored()
    {
        PdfPagePosition position = ScrolledTo(0, 500);

        // The window shrank: the scroller clamped its own offset.
        Assert.False(position.Scrolled(400, 800, 400));
        Assert.False(position.Scrolled(400, 1000, 600));
        Assert.Equal(0.5, position.Fraction);
    }

    [Fact]
    public void AScrollBeforeAnyOffsetIsIgnored()
    {
        PdfPagePosition position = new();
        position.Shown(0);

        Assert.False(position.Scrolled(100, 1000, 400));
        Assert.Equal(0, position.Fraction);
    }

    [Fact]
    public void APageWithNothingToScrollIgnoresScrolls()
    {
        PdfPagePosition position = new();
        position.Shown(0);

        Assert.Equal(0, position.OffsetFor(300, 400));
        Assert.False(position.Scrolled(10, 300, 400));
        Assert.Equal(0, position.Fraction);
    }

    [Theory]
    [InlineData(double.NaN, 400)]
    [InlineData(double.PositiveInfinity, 400)]
    [InlineData(0, 400)]
    [InlineData(-1, 400)]
    [InlineData(1000, double.NaN)]
    [InlineData(1000, 0)]
    [InlineData(1000, -1)]
    public void ABadHeightGivesTheTopAndNoScroll(double imageHeight, double viewportHeight)
    {
        PdfPagePosition position = new();
        position.Target(0, 0.5);
        position.Shown(0);

        Assert.Equal(0, position.OffsetFor(imageHeight, viewportHeight));
        Assert.False(position.Scrolled(100, imageHeight, viewportHeight));
        Assert.Equal(0.5, position.Fraction);
    }

    [Fact]
    public void ANonFiniteOffsetIsIgnored()
    {
        PdfPagePosition position = ScrolledTo(0, 300);

        Assert.False(position.Scrolled(double.NaN, 1000, 400));
        Assert.Equal(0.3, position.Fraction, 6);
    }

    [Theory]
    [InlineData(-1, 0.0)]
    [InlineData(0, -0.1)]
    [InlineData(0, 1.1)]
    [InlineData(0, double.NaN)]
    public void ABadTargetIsRejected(int page, double fraction) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPagePosition().Target(page, fraction));
}
