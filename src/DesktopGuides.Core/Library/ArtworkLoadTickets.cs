namespace DesktopGuides.Core.Library;

public sealed class ArtworkLoadTicket
{
    // The source is never disposed. A decode that finishes after the ticket is
    // cancelled may still read Token, and a disposed source would throw.
    private readonly CancellationTokenSource source = new();

    public CancellationToken Token => source.Token;

    public bool IsCancelled => source.IsCancellationRequested;

    internal void Cancel() => source.Cancel();
}

// Pairs each list container with the artwork load it is waiting for. A decoded
// image may be applied only while its ticket is still the container's current one,
// so a recycled container never shows another item's artwork.
public sealed class ArtworkLoadTickets<TContainer> where TContainer : class
{
    private readonly Dictionary<TContainer, ArtworkLoadTicket> current =
        new(ReferenceEqualityComparer.Instance);

    public ArtworkLoadTicket Issue(TContainer container)
    {
        Release(container);
        ArtworkLoadTicket ticket = new();
        current[container] = ticket;
        return ticket;
    }

    public void Release(TContainer container)
    {
        if (current.Remove(container, out ArtworkLoadTicket? ticket)) ticket.Cancel();
    }

    public void ReleaseAll()
    {
        foreach (ArtworkLoadTicket ticket in current.Values) ticket.Cancel();
        current.Clear();
    }

    public bool IsCurrent(TContainer container, ArtworkLoadTicket ticket) =>
        !ticket.IsCancelled &&
        current.TryGetValue(container, out ArtworkLoadTicket? active) &&
        ReferenceEquals(active, ticket);
}
