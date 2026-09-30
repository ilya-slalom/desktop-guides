using System.Globalization;

namespace DesktopGuides.Core.Library;

public sealed record LibrarySearchMatch(LibraryGameSummary Summary, string? MatchedGuideTitle);

public static class LibrarySearch
{
    private const CompareOptions Options =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;

    public static bool Matches(string title, string? query)
    {
        string trimmed = query?.Trim() ?? string.Empty;
        return trimmed.Length == 0
            || CultureInfo.InvariantCulture.CompareInfo.IndexOf(title, trimmed, Options) >= 0;
    }

    public static IReadOnlyList<LibrarySearchMatch> Filter(
        IReadOnlyList<LibraryGameSummary> games, string? query)
    {
        List<LibrarySearchMatch> matches = [];
        foreach (LibraryGameSummary game in games)
        {
            if (Matches(game.Game.Title, query))
            {
                matches.Add(new(game, null));
                continue;
            }

            string? guide = game.GuideTitles.FirstOrDefault(title => Matches(title, query));
            if (guide is not null)
            {
                matches.Add(new(game, guide));
            }
        }

        return matches;
    }
}
