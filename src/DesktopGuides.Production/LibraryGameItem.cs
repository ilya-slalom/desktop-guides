using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

public sealed partial class LibraryGameItem : CatalogRowItem
{
    internal LibraryGameItem(
        LibrarySearchMatch match, int attentionCount = 0, bool colorful = false, bool highContrast = false)
        : base(match.Summary.Game.Title, AppIconCatalog.ForGame(colorful, highContrast),
            CatalogPresentation.GameFacts(match.Summary, match.MatchedGuideTitle, attentionCount))
    {
        Game = match.Summary.Game;
    }

    internal Game Game { get; }
    internal string? ArtworkRelativePath => Game.ArtworkRelativePath;
}
