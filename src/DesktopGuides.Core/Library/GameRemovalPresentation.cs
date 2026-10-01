namespace DesktopGuides.Core.Library;

/// <summary>The game removal confirmation, hint and status copy.</summary>
public static class GameRemovalPresentation
{
    public const string DialogBody = "This removes the game and its details from Desktop Guides.";

    public const string GuidesFirst = "Remove this game's guides first.";

    public static string DialogTitle(string title) => $"Remove {title}?";

    public static string Removed(string title) => $"Removed {title}.";

    public static string AlreadyRemoved(string title) => $"{title} was already removed.";

    public static string HasGuides(string title) => $"{title} has guides now, so it wasn't removed.";

    public static string Failed(string title) =>
        $"{title} couldn't be removed. The game is unchanged. Try again.";
}
