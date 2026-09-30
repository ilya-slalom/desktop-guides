using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

public sealed class LibraryGameItem : CatalogRowItem
{
    internal LibraryGameItem(LibraryGameSummary summary)
        : base(summary.Game.Title, "\uE7FC", CatalogPresentation.GameFacts(summary))
    {
        Game = summary.Game;
    }

    internal Game Game { get; }
    internal string? ArtworkRelativePath => Game.ArtworkRelativePath;
}
