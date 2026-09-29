using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ArtworkLoadTicketsTests
{
    private sealed class Container;

    [Fact]
    public void AnIssuedTicketIsCurrent()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container row = new();
        ArtworkLoadTicket ticket = tickets.Issue(row);
        Assert.True(tickets.IsCurrent(row, ticket));
        Assert.False(ticket.IsCancelled);
        Assert.False(ticket.Token.IsCancellationRequested);
    }

    [Fact]
    public void ASupersededTicketIsNotCurrentAndIsCancelled()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container row = new();
        ArtworkLoadTicket first = tickets.Issue(row);
        ArtworkLoadTicket second = tickets.Issue(row);
        Assert.False(tickets.IsCurrent(row, first));
        Assert.True(first.IsCancelled);
        Assert.True(first.Token.IsCancellationRequested);
        Assert.True(tickets.IsCurrent(row, second));
    }

    [Fact]
    public void AReleasedTicketIsNotCurrentAndIsCancelled()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container row = new();
        ArtworkLoadTicket ticket = tickets.Issue(row);
        tickets.Release(row);
        Assert.False(tickets.IsCurrent(row, ticket));
        Assert.True(ticket.IsCancelled);
    }

    [Fact]
    public void ReleaseAllCancelsEveryTicket()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container first = new();
        Container second = new();
        ArtworkLoadTicket a = tickets.Issue(first);
        ArtworkLoadTicket b = tickets.Issue(second);
        tickets.ReleaseAll();
        Assert.False(tickets.IsCurrent(first, a));
        Assert.False(tickets.IsCurrent(second, b));
        Assert.True(a.IsCancelled);
        Assert.True(b.IsCancelled);
    }

    [Fact]
    public void ContainersAreIndependent()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container first = new();
        Container second = new();
        ArtworkLoadTicket a = tickets.Issue(first);
        ArtworkLoadTicket b = tickets.Issue(second);
        tickets.Release(second);
        Assert.True(tickets.IsCurrent(first, a));
        Assert.False(tickets.IsCurrent(second, b));
    }

    [Fact]
    public void ATicketIsNotCurrentForAnotherContainer()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container first = new();
        Container second = new();
        ArtworkLoadTicket a = tickets.Issue(first);
        tickets.Issue(second);
        Assert.False(tickets.IsCurrent(second, a));
    }

    [Fact]
    public void ACancelledTokenStaysReadable()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container row = new();
        ArtworkLoadTicket ticket = tickets.Issue(row);
        tickets.ReleaseAll();
        Assert.Throws<OperationCanceledException>(() => ticket.Token.ThrowIfCancellationRequested());
    }
}
