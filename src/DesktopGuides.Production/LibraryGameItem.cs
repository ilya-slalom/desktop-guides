using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

public sealed class LibraryGameItem : CatalogRowItem
{
    internal LibraryGameItem(LibrarySearchMatch match, int attentionCount = 0)
        : base(match.Summary.Game.Title, "\uE7FC",
            CatalogPresentation.GameFacts(match.Summary, match.MatchedGuideTitle, attentionCount))
    {
        Game = match.Summary.Game;
    }

    internal Game Game { get; }
    internal string? ArtworkRelativePath => Game.ArtworkRelativePath;
}
