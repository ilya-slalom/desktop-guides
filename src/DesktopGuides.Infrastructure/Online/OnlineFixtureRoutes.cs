using System.Text.Json;

namespace DesktopGuides.Infrastructure.Online;

internal sealed record OnlineFixtureRoute(string Url, int Status, string? File, string ContentType, string? Location = null);

internal sealed record OnlineFixtureManifest(List<OnlineFixtureRoute> Routes);

/// <summary>
/// S22, test runs only: the answers the online browsers give instead of the
/// network. The manifest is read again when it changes, so a test can make
/// a page refuse and then recover. Unmatched addresses answer 599.
/// </summary>
public sealed class OnlineFixtureRoutes
{
    private readonly string manifestPath;
    private readonly string folder;
    private readonly object gate = new();
    private DateTime readUtc;
    private Dictionary<string, OnlineFixtureRoute> routes = [];

    private OnlineFixtureRoutes(string manifestPath)
    {
        this.manifestPath = Path.GetFullPath(manifestPath);
        folder = Path.GetDirectoryName(this.manifestPath)!;
    }

    public static OnlineFixtureRoutes Load(string manifestPath)
    {
        OnlineFixtureRoutes loaded = new(manifestPath);
        loaded.Refresh();
        return loaded;
    }

    public bool TryAnswer(Uri url, out int status, out string contentType, out string? filePath) =>
        TryAnswer(url, out status, out contentType, out filePath, out _);

    /// <summary>As above, with a redirect's target, for a route that answers 3xx.</summary>
    public bool TryAnswer(Uri url, out int status, out string contentType, out string? filePath, out string? location)
    {
        lock (gate)
        {
            if (File.GetLastWriteTimeUtc(manifestPath) != readUtc) Refresh();
            if (routes.TryGetValue(url.AbsoluteUri, out OnlineFixtureRoute? route))
            {
                status = route.Status;
                contentType = route.ContentType;
                filePath = route.File is null ? null : Resolve(route.File);
                location = route.Location;
                return true;
            }
        }
        status = 599;
        contentType = "text/plain";
        filePath = null;
        location = null;
        return false;
    }

    private void Refresh()
    {
        DateTime written = File.GetLastWriteTimeUtc(manifestPath);
        OnlineFixtureManifest manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath), InfrastructureJsonContext.Default.OnlineFixtureManifest)
            ?? throw new InvalidDataException("The online fixture manifest is empty.");
        Dictionary<string, OnlineFixtureRoute> next = new(StringComparer.Ordinal);
        foreach (OnlineFixtureRoute route in manifest.Routes)
        {
            if (route.File is not null) Resolve(route.File);
            next[new Uri(route.Url).AbsoluteUri] = route;
        }
        routes = next;
        readUtc = written;
    }

    // A fixture file stays inside the manifest's folder.
    private string Resolve(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(folder, relative));
        if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("An online fixture file is outside the fixture folder.");
        }
        return path;
    }
}
