using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

public sealed class LibraryGameItem : ArtworkItem
{
    internal LibraryGameItem(Game game)
        : base(
            game.Title,
            LibraryGamePresentation.Summary(game),
            null,
            LibraryGamePresentation.AccessibleName(game))
    {
        Game = game;
    }

    internal Game Game { get; }
    internal string? ArtworkRelativePath => Game.ArtworkRelativePath;
}
