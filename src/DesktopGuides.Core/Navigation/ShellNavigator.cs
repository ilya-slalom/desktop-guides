namespace DesktopGuides.Core.Navigation;

public abstract record ShellRoute;

public sealed record LibraryRoute : ShellRoute;

public sealed record GameRoute(Guid GameId) : ShellRoute;

public sealed record ReaderRoute(Guid GuideId, Guid GameId) : ShellRoute;

public sealed record SettingsRoute : ShellRoute;

public sealed class ShellNavigator
{
    // Each entry keeps an optional anchor: the focused game on the Library,
    // the selected guide on a Game. Reader and Settings never have one.
    private readonly List<(ShellRoute Route, Guid? Anchor)> backStack = [];

    public ShellRoute Current { get; private set; } = new LibraryRoute();

    public Guid? CurrentAnchor { get; private set; }

    public bool CanGoBack => backStack.Count != 0;

    public void OpenLibrary() => Navigate(new LibraryRoute());

    public void OpenSettings() => Navigate(new SettingsRoute());

    public void OpenGame(Guid gameId)
    {
        RequireId(gameId);
        if (Current is LibraryRoute)
        {
            CurrentAnchor = gameId;
        }
        Navigate(new GameRoute(gameId));
    }

    public void OpenReader(Guid guideId, Guid gameId)
    {
        RequireId(guideId);
        RequireId(gameId);
        ReaderRoute reader = new(guideId, gameId);
        if (Current == reader)
        {
            return;
        }
        if (Current is not GameRoute game || game.GameId != gameId)
        {
            if (Current is LibraryRoute)
            {
                CurrentAnchor = gameId;
            }
            Navigate(new GameRoute(gameId));
        }
        CurrentAnchor = guideId;
        Navigate(reader);
    }

    public void SetAnchor(Guid? anchorId)
    {
        if (Current is not (LibraryRoute or GameRoute))
        {
            throw new InvalidOperationException("Only the Library and Game pages keep an anchor.");
        }
        CurrentAnchor = anchorId;
    }

    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }
        int index = backStack.Count - 1;
        (Current, CurrentAnchor) = backStack[index];
        backStack.RemoveAt(index);
        return true;
    }

    public void ResetToLibrary()
    {
        backStack.Clear();
        Current = new LibraryRoute();
        CurrentAnchor = null;
    }

    private void Navigate(ShellRoute route)
    {
        if (Current == route)
        {
            return;
        }
        backStack.Add((Current, CurrentAnchor));
        Current = route;
        CurrentAnchor = null;
    }

    private static void RequireId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A generated ID is required.");
        }
    }
}
