using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

public sealed class LibraryGameItem : CatalogRowItem
{
    internal LibraryGameItem(LibrarySearchMatch match, int attentionCount = 0)
        // Fluent has no colour game controller, so the placeholder stays a glyph.
        : base(match.Summary.Game.Title, AppIconChoice.FromGlyph("\uE7FC"),
            CatalogPresentation.GameFacts(match.Summary, match.MatchedGuideTitle, attentionCount))
    {
        Game = match.Summary.Game;
    }

    internal Game Game { get; }
    internal string? ArtworkRelativePath => Game.ArtworkRelativePath;
}
