using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

public sealed class LibraryGameItem : CatalogRowItem
{
    internal LibraryGameItem(LibrarySearchMatch match)
        : base(match.Summary.Game.Title, "\uE7FC",
            CatalogPresentation.GameFacts(match.Summary, match.MatchedGuideTitle))
    {
        Game = match.Summary.Game;
    }

    internal Game Game { get; }
    internal string? ArtworkRelativePath => Game.ArtworkRelativePath;
}
