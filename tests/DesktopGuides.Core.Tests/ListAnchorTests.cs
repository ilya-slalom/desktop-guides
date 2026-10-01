using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ListAnchorTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();
    private static readonly Guid E = Guid.NewGuid();

    [Fact]
    public void AnchorThatSurvivesIsKept()
    {
        Assert.Equal(B, ListAnchor.Resolve([A, B, C], [A, B, C], B));
    }

    [Fact]
    public void AnchorIsKeptAfterReorder()
    {
        Assert.Equal(B, ListAnchor.Resolve([A, B, C], [B, C, A], B));
    }

    [Fact]
    public void RemovedAnchorResolvesToNextRow()
    {
        Assert.Equal(C, ListAnchor.Resolve([A, B, C], [A, C], B));
    }

    [Fact]
    public void RemovedLastRowResolvesToPreviousRow()
    {
        Assert.Equal(B, ListAnchor.Resolve([A, B, C], [A, B], C));
    }

    [Fact]
    public void RemovedNeighboursResolveToNearestLaterSurvivor()
    {
        Assert.Equal(E, ListAnchor.Resolve([A, B, C, D, E], [A, E], C));
    }

    [Fact]
    public void RemovedTailResolvesToNearestEarlierSurvivor()
    {
        Assert.Equal(A, ListAnchor.Resolve([A, B, C, D], [A], C));
    }

    [Fact]
    public void EmptyCurrentListResolvesToNull()
    {
        Assert.Null(ListAnchor.Resolve([A, B], [], A));
    }

    [Fact]
    public void NullAnchorResolvesToNull()
    {
        Assert.Null(ListAnchor.Resolve([A, B], [A, B], null));
    }

    [Fact]
    public void AnchorMissingFromBothListsResolvesToNull()
    {
        Assert.Null(ListAnchor.Resolve([A, B], [A, B], C));
    }

    [Fact]
    public void AnchorOnlyInCurrentListIsKept()
    {
        Assert.Equal(C, ListAnchor.Resolve([], [A, C], C));
    }

    [Fact]
    public void NullListsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ListAnchor.Resolve(null!, [A], A));
        Assert.Throws<ArgumentNullException>(() => ListAnchor.Resolve([A], null!, A));
    }
}
