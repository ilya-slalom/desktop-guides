namespace DesktopGuides.Core.Navigation;

public static class ListAnchor
{
    // IDs in each list are unique. Returns the anchor if it is still shown;
    // otherwise the nearest survivor by its old position, later rows first;
    // otherwise null, which tells the caller to use its own fallback.
    public static Guid? Resolve(
        IReadOnlyList<Guid> previousIds,
        IReadOnlyList<Guid> currentIds,
        Guid? anchorId)
    {
        ArgumentNullException.ThrowIfNull(previousIds);
        ArgumentNullException.ThrowIfNull(currentIds);
        if (anchorId is not Guid anchor)
        {
            return null;
        }
        HashSet<Guid> current = [.. currentIds];
        if (current.Contains(anchor))
        {
            return anchor;
        }
        int index = -1;
        for (int i = 0; i < previousIds.Count; i++)
        {
            if (previousIds[i] == anchor)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            return null;
        }
        for (int i = index + 1; i < previousIds.Count; i++)
        {
            if (current.Contains(previousIds[i]))
            {
                return previousIds[i];
            }
        }
        for (int i = index - 1; i >= 0; i--)
        {
            if (current.Contains(previousIds[i]))
            {
                return previousIds[i];
            }
        }
        return null;
    }
}
