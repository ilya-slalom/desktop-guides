namespace DesktopGuides.Core.Navigation;

public abstract record ShellRoute;

public sealed record LibraryRoute : ShellRoute;

public sealed record GameRoute(Guid GameId) : ShellRoute;

public sealed record ReaderRoute(Guid GuideId, Guid GameId) : ShellRoute;

public sealed record SettingsRoute : ShellRoute;

public sealed class ShellNavigator
{
    private readonly List<ShellRoute> backStack = [];

    public ShellRoute Current { get; private set; } = new LibraryRoute();

    public bool CanGoBack => backStack.Count != 0;

    public void OpenLibrary() => Navigate(new LibraryRoute());

    public void OpenSettings() => Navigate(new SettingsRoute());

    public void OpenGame(Guid gameId)
    {
        RequireId(gameId);
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
            Navigate(new GameRoute(gameId));
        }
        Navigate(reader);
    }

    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }
        int index = backStack.Count - 1;
        Current = backStack[index];
        backStack.RemoveAt(index);
        return true;
    }

    public void ResetToLibrary()
    {
        backStack.Clear();
        Current = new LibraryRoute();
    }

    private void Navigate(ShellRoute route)
    {
        if (Current == route)
        {
            return;
        }
        backStack.Add(Current);
        Current = route;
    }

    private static void RequireId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A generated ID is required.");
        }
    }
}
