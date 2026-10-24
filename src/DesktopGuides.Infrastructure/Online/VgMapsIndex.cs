using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace DesktopGuides.Infrastructure.Online;

/// <summary>
/// A game in the atlas: its title as searched, its label as listed ("Pac-Man
/// (Arcade)"), and the system page and anchor that hold its maps.
/// </summary>
public sealed record VgMapsGame(string Title, string Label, string SystemPath, string Anchor);

/// <summary>
/// S22 PR 5: the atlas's index, fetched when first searched, kept 30 days
/// in the cache folder, and searched locally as Steam's games are.
/// </summary>
public sealed partial class VgMapsIndex(string cacheFile, TimeProvider clock)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private (VgMapsGame Game, string Key)[] games = [];
    private DateTimeOffset fetchedUtc;
    private bool cacheRead;

    public bool IsFresh => games.Length > 0 && clock.GetUtcNow() - fetchedUtc <= MaxAge;

    /// <summary>
    /// The index's games. A game's own entry is its title; one in
    /// parentheses, "(NES)", continues the title before it. The letter
    /// headings and the "other stuff" pages outside a system aren't games.
    /// </summary>
    public static IReadOnlyList<VgMapsGame> Parse(string html)
    {
        IDocument document = new HtmlParser().ParseDocument(html);
        List<VgMapsGame> found = [];
        HashSet<(string, string)> seen = [];
        string? title = null;
        foreach (IElement link in document.QuerySelectorAll("a[href]"))
        {
            if (EntryHref().Match(link.GetAttribute("href")!) is not { Success: true } entry) continue;
            string text = Space().Replace(link.TextContent, " ").Trim();
            string system = entry.Groups["system"].Value;
            string anchor = Uri.UnescapeDataString(entry.Groups["anchor"].Value);
            if (text.Length == 0 || (anchor.Length == 1 && text == anchor)) continue;
            string label;
            if (text.StartsWith('(') && text.EndsWith(')'))
            {
                if (title is null) continue;
                label = $"{title} {text}";
            }
            else if (Variant().Match(text) is { Success: true } named)
            {
                title = named.Groups["title"].Value.Trim();
                label = text;
            }
            else
            {
                title = text;
                label = $"{text} ({system})";
            }
            string path = system + "/index.htm";
            if (seen.Add((path, anchor))) found.Add(new VgMapsGame(title, label, path, anchor));
        }
        return found;
    }

    /// <summary>Uses an index under 30 days old, else fetches one; a failed fetch keeps nothing.</summary>
    public async Task LoadAsync(Func<CancellationToken, Task<IReadOnlyList<VgMapsGame>>> fetch, bool refresh, CancellationToken token)
    {
        if (!refresh && (IsFresh || await LoadCachedAsync())) return;
        IReadOnlyList<VgMapsGame> fetched = await fetch(token);
        DateTimeOffset now = clock.GetUtcNow();
        games = await Task.Run(() => Index(fetched), token);
        fetchedUtc = now;
        await Task.Run(() => TryWrite(fetched, now), CancellationToken.None);
    }

    public IReadOnlyList<VgMapsGame> Search(string query, int max = 30)
    {
        string key = OnlineNames.Normalize(query);
        if (key.Length == 0) return [];
        string prefix = key + " ";
        string[] words = key.Split(' ').Select(word => " " + word + " ").ToArray();
        return games
            .Select((entry, order) => (entry.Game, entry.Key, Order: order, Rank: OnlineNames.Rank(entry.Key, key, prefix, words)))
            .Where(entry => entry.Rank >= 0)
            .OrderBy(entry => entry.Rank).ThenBy(entry => entry.Key.Length).ThenBy(entry => entry.Order)
            .Take(max)
            .Select(entry => entry.Game)
            .ToArray();
    }

    private async Task<bool> LoadCachedAsync()
    {
        if (!cacheRead)
        {
            cacheRead = true;
            if (await Task.Run(Read) is ({ } read, DateTimeOffset fetched))
            {
                games = read;
                fetchedUtc = fetched;
            }
        }
        return IsFresh;
    }

    private static (VgMapsGame, string)[] Index(IEnumerable<VgMapsGame> list) =>
        list.Select(game => (game, OnlineNames.Normalize(game.Title))).Where(entry => entry.Item2.Length > 0).ToArray();

    private ((VgMapsGame, string)[]? Games, DateTimeOffset Fetched) Read()
    {
        try
        {
            if (!File.Exists(cacheFile)) return (null, default);
            JsonNode? root = JsonNode.Parse(File.ReadAllText(cacheFile));
            if (root?["fetchedUtc"]?.GetValue<long>() is not long fetched || root["games"] is not JsonArray list) return (null, default);
            List<VgMapsGame> read = [];
            foreach (JsonNode? item in list)
            {
                if (item is JsonArray { Count: 4 } entry && entry[0]?.GetValue<string>() is { } title &&
                    entry[1]?.GetValue<string>() is { } label && entry[2]?.GetValue<string>() is { } path &&
                    entry[3]?.GetValue<string>() is { } anchor)
                {
                    read.Add(new VgMapsGame(title, label, path, anchor));
                }
            }
            return read.Count == 0 ? (null, default) : (Index(read), DateTimeOffset.FromUnixTimeMilliseconds(fetched));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            return (null, default);
        }
    }

    // As Steam's list: written whole and moved over, best effort.
    private void TryWrite(IReadOnlyList<VgMapsGame> list, DateTimeOffset fetched)
    {
        string temporary = cacheFile + ".tmp";
        try
        {
            JsonArray items = [];
            foreach (VgMapsGame game in list)
            {
                items.Add((JsonNode)new JsonArray((JsonNode)game.Title, (JsonNode)game.Label, (JsonNode)game.SystemPath, (JsonNode)game.Anchor));
            }
            JsonObject root = new() { ["fetchedUtc"] = fetched.ToUnixTimeMilliseconds(), ["games"] = items };
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            File.WriteAllText(temporary, root.ToJsonString());
            File.Move(temporary, cacheFile, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // "PSX/index.htm#FinalFantasyVII": a system's page, not ../Other/….
    [GeneratedRegex(@"^(?<system>[A-Za-z0-9]+)/index\.htm#(?<anchor>[^#]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryHref();

    [GeneratedRegex(@"^(?<title>.+?)\s*\((?<variant>[^()]+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex Variant();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Space();
}
