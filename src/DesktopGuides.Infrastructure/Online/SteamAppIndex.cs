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

    public bool IsLoaded => apps.Length > 0;

    /// <summary>Uses a list under 30 days old, else fetches one and keeps it; a failed fetch keeps nothing.</summary>
    public async Task LoadAsync(
        Func<CancellationToken, Task<IReadOnlyList<(string AppId, string Name)>>> fetch, bool refresh, CancellationToken token)
    {
        if (!refresh && Read() is { } cached)
        {
            apps = cached;
            return;
        }
        IReadOnlyList<(string AppId, string Name)> fetched = await fetch(token);
        apps = Index(fetched);
        Write(fetched);
    }

    /// <summary>Reads a list under 30 days old, if one is kept; never fetches.</summary>
    public bool LoadCached()
    {
        if (Read() is not { } cached) return false;
        apps = cached;
        return true;
    }

    public IReadOnlyList<SteamApp> Match(string query, int max = 20)
    {
        string key = Normalize(query);
        if (key.Length == 0) return [];
        string[] words = key.Split(' ');
        return apps
            .Select(entry => (entry.App, entry.Key,
                Rank: entry.Key == key ? 0
                    : entry.Key.StartsWith(key + " ", StringComparison.Ordinal) ? 1
                    : words.All(word => (" " + entry.Key + " ").Contains(" " + word + " ", StringComparison.Ordinal)) ? 2
                    : -1))
            .Where(entry => entry.Rank >= 0)
            .OrderBy(entry => entry.Rank).ThenBy(entry => entry.Key.Length).ThenBy(entry => entry.App.Name.Length)
            .ThenBy(entry => long.TryParse(entry.App.AppId, out long id) ? id : long.MaxValue)
            .Take(max)
            .Select(entry => entry.App)
            .ToArray();
    }

    /// <summary>The one game whose name is the title, or null when none or several are.</summary>
    public SteamApp? ExactSingle(string title)
    {
        string key = Normalize(title);
        SteamApp[] exact = key.Length == 0 ? [] : apps.Where(entry => entry.Key == key).Select(entry => entry.App).Take(2).ToArray();
        return exact.Length == 1 ? exact[0] : null;
    }

    public string? NameOf(string appId) => apps.FirstOrDefault(entry => entry.App.AppId == appId).App?.Name;

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

    private (SteamApp, string)[]? Read()
    {
        try
        {
            if (!File.Exists(cacheFile)) return null;
            JsonNode? root = JsonNode.Parse(File.ReadAllText(cacheFile));
            if (root?["fetchedUtc"]?.GetValue<long>() is not long fetched ||
                clock.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(fetched) > MaxAge ||
                root["apps"] is not JsonArray list)
            {
                return null;
            }
            List<(string, string)> read = [];
            foreach (JsonNode? item in list)
            {
                if (item is JsonArray { Count: 2 } pair && pair[0]?.GetValue<string>() is { } id && pair[1]?.GetValue<string>() is { } name)
                {
                    read.Add((id, name));
                }
            }
            return read.Count == 0 ? null : Index(read);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Written whole, then moved over the old list, so a reader never sees half of one.
    private void Write(IReadOnlyList<(string AppId, string Name)> list)
    {
        JsonArray items = [];
        foreach ((string id, string name) in list) items.Add((JsonNode)new JsonArray((JsonNode)id, (JsonNode)name));
        JsonObject root = new() { ["fetchedUtc"] = clock.GetUtcNow().ToUnixTimeMilliseconds(), ["apps"] = items };
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        string temporary = cacheFile + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString());
        File.Move(temporary, cacheFile, overwrite: true);
    }
}
