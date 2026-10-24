using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesktopGuides.Infrastructure.Online;

public sealed record SteamApp(string AppId, string Name);

/// <summary>
/// S22 PR 4b: Steam's games, kept in the cache folder for 30 days, so a
/// game can be matched to its Steam app by name. Names compare without
/// case, ™ ® ©, or punctuation; an exact match ranks first, then a name
/// that starts with the query, then one that holds all its words.
/// </summary>
public sealed class SteamAppIndex(string cacheFile, TimeProvider clock)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private (SteamApp App, string Key)[] apps = [];
    private DateTimeOffset fetchedUtc;
    private bool cacheRead;

    public bool IsLoaded => apps.Length > 0;

    /// <summary>Loaded, and fetched within 30 days.</summary>
    public bool IsFresh => IsLoaded && clock.GetUtcNow() - fetchedUtc <= MaxAge;

    /// <summary>
    /// Reads the kept list once, off the UI thread, and never fetches. A list
    /// past 30 days still names a game; <paramref name="allowStale"/> says
    /// whether that's enough.
    /// </summary>
    public async Task<bool> LoadCachedAsync(bool allowStale)
    {
        if (!cacheRead)
        {
            cacheRead = true;
            if (await Task.Run(Read) is ({ } read, DateTimeOffset fetched))
            {
                apps = read;
                fetchedUtc = fetched;
            }
        }
        return allowStale ? IsLoaded : IsFresh;
    }

    /// <summary>
    /// Uses a list under 30 days old, else fetches one; a failed fetch keeps
    /// nothing. A list that can't be written is still used.
    /// </summary>
    public async Task LoadAsync(
        Func<CancellationToken, Task<IReadOnlyList<(string AppId, string Name)>>> fetch, bool refresh, CancellationToken token)
    {
        if (!refresh && (IsFresh || await LoadCachedAsync(allowStale: false))) return;
        IReadOnlyList<(string AppId, string Name)> fetched = await fetch(token);
        DateTimeOffset now = clock.GetUtcNow();
        apps = await Task.Run(() => Index(fetched), token);
        fetchedUtc = now;
        await Task.Run(() => TryWrite(fetched, now), CancellationToken.None);
    }

    public IReadOnlyList<SteamApp> Match(string query, int max = 20)
    {
        string key = Normalize(query);
        if (key.Length == 0) return [];
        string prefix = key + " ";
        string[] words = key.Split(' ').Select(word => " " + word + " ").ToArray();
        return apps
            .Select(entry => (entry.App, entry.Key, Rank: Rank(entry.Key, key, prefix, words)))
            .Where(entry => entry.Rank >= 0)
            .OrderBy(entry => entry.Rank).ThenBy(entry => entry.Key.Length).ThenBy(entry => entry.App.Name.Length)
            .ThenBy(entry => long.TryParse(entry.App.AppId, out long id) ? id : long.MaxValue)
            .Take(max)
            .Select(entry => entry.App)
            .ToArray();
    }

    /// <summary>
    /// The game the title can only mean: its one exact match, when no other
    /// name starts with it ("Tomb Raider" might be "Tomb Raider I"); else null.
    /// </summary>
    public SteamApp? OnlyMatch(string title)
    {
        string key = Normalize(title);
        if (key.Length == 0) return null;
        string prefix = key + " ";
        SteamApp? exact = null;
        foreach ((SteamApp app, string name) in apps)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return null;
            if (name != key) continue;
            if (exact is not null) return null;
            exact = app;
        }
        return exact;
    }

    public string? NameOf(string appId) => apps.FirstOrDefault(entry => entry.App.AppId == appId).App?.Name;

    private static int Rank(string name, string key, string prefix, string[] words)
    {
        if (name == key) return 0;
        if (name.StartsWith(prefix, StringComparison.Ordinal)) return 1;
        string spaced = " " + name + " ";
        return words.All(word => spaced.Contains(word, StringComparison.Ordinal)) ? 2 : -1;
    }

    internal static string Normalize(string name)
    {
        StringBuilder key = new(name.Length);
        bool space = true;
        // The marks go first: compatibility normalizing turns ™ into "TM".
        string plain = name.Replace("™", "", StringComparison.Ordinal).Replace("®", "", StringComparison.Ordinal)
            .Replace("©", "", StringComparison.Ordinal);
        foreach (char c in plain.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                key.Append(c);
                space = false;
            }
            else if (!space)
            {
                key.Append(' ');
                space = true;
            }
        }
        return key.ToString().TrimEnd();
    }

    private static (SteamApp, string)[] Index(IEnumerable<(string AppId, string Name)> list) =>
        list.Select(app => (new SteamApp(app.AppId, app.Name), Normalize(app.Name))).Where(entry => entry.Item2.Length > 0).ToArray();

    private ((SteamApp, string)[]? Apps, DateTimeOffset Fetched) Read()
    {
        try
        {
            if (!File.Exists(cacheFile)) return (null, default);
            JsonNode? root = JsonNode.Parse(File.ReadAllText(cacheFile));
            if (root?["fetchedUtc"]?.GetValue<long>() is not long fetched || root["apps"] is not JsonArray list) return (null, default);
            List<(string, string)> read = [];
            foreach (JsonNode? item in list)
            {
                if (item is JsonArray { Count: 2 } pair && pair[0]?.GetValue<string>() is { } id && pair[1]?.GetValue<string>() is { } name)
                {
                    read.Add((id, name));
                }
            }
            return read.Count == 0 ? (null, default) : (Index(read), DateTimeOffset.FromUnixTimeMilliseconds(fetched));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            return (null, default);
        }
    }

    // Written whole, then moved over the old list, so a reader never sees
    // half of one. Best effort: a disk that's full or a file another program
    // holds leaves the list in memory, and the next start fetches it again.
    private void TryWrite(IReadOnlyList<(string AppId, string Name)> list, DateTimeOffset fetched)
    {
        string temporary = cacheFile + ".tmp";
        try
        {
            JsonArray items = [];
            foreach ((string id, string name) in list) items.Add((JsonNode)new JsonArray((JsonNode)id, (JsonNode)name));
            JsonObject root = new() { ["fetchedUtc"] = fetched.ToUnixTimeMilliseconds(), ["apps"] = items };
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
}
