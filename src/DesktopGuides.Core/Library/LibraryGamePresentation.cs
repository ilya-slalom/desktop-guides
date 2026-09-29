namespace DesktopGuides.Core.Library;

// Row strings for a Library game. The accessible name stays the title, because
// Library automation and screen-reader users identify rows by title.
public static class LibraryGamePresentation
{
    public static string? Summary(Game game) =>
        string.IsNullOrWhiteSpace(game.Platform) ? null : game.Platform.Trim();

    public static string AccessibleName(Game game) => game.Title;
}
