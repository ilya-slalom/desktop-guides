using System.Text.Json;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Infrastructure.Online;

internal sealed record CapturedEntry(GuidePageReading Reading, Dictionary<string, string> Images);

internal sealed record CaptureFile(
    int SchemaVersion, Guid Id, Guid GameId, SourceSite Site, string ItemId,
    List<GuidePageLink> Pages, int TotalPages, List<CapturedEntry> Captured, DateTimeOffset TouchedUtc);

/// <summary>
/// S22: unfinished captures, kept outside the library under the cache root
/// until they are saved or discarded, and swept after 7 days untouched.
/// </summary>
public sealed class CaptureStore(string root, TimeProvider clock)
{
    internal const int MaxPages = 200;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    public CaptureDraft Begin(Guid gameId, GuidePageReading first)
    {
        List<GuidePageLink> pages = first.Pages.Count > 0
            ? first.Pages.ToList()
            : [new GuidePageLink(first.Match.PageKey, first.Url, null)];
        CaptureFile file = new(1, Guid.NewGuid(), gameId, first.Match.Site, first.Match.ItemId,
            pages.Take(MaxPages).ToList(), pages.Count, [], clock.GetUtcNow());
        CaptureDraft draft = new(this, Path.Combine(root, file.Id.ToString("N")), file);
        Directory.CreateDirectory(draft.Folder);
        draft.Write();
        return draft;
    }

    /// <summary>The latest draft for this game's site item, or null; a damaged draft is skipped.</summary>
    public CaptureDraft? Find(Guid gameId, SourceSite site, string itemId) =>
        Drafts().Where(draft => draft.File is { } file && file.GameId == gameId && file.Site == site && file.ItemId == itemId)
            .OrderByDescending(draft => draft.File!.TouchedUtc)
            .Select(draft => new CaptureDraft(this, draft.Folder, draft.File!))
            .FirstOrDefault();

    /// <summary>Deletes drafts untouched for 7 days, and damaged ones; returns how many.</summary>
    public int Sweep()
    {
        DateTimeOffset cutoff = clock.GetUtcNow() - MaxAge;
        int removed = 0;
        foreach ((string folder, CaptureFile? file) in Drafts())
        {
            if (file is null || file.TouchedUtc < cutoff)
            {
                Directory.Delete(folder, recursive: true);
                removed++;
            }
        }
        return removed;
    }

    internal DateTimeOffset Now => clock.GetUtcNow();

    private IEnumerable<(string Folder, CaptureFile? File)> Drafts()
    {
        if (!Directory.Exists(root)) yield break;
        foreach (string folder in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) continue;
            yield return (folder, Load(Path.Combine(folder, "capture.json")));
        }
    }

    private static CaptureFile? Load(string path)
    {
        try
        {
            CaptureFile? file = JsonSerializer.Deserialize(File.ReadAllText(path), InfrastructureJsonContext.Default.CaptureFile);
            return file is { SchemaVersion: 1, Pages: not null, Captured: not null } ? file : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>One unfinished capture: the guide's pages, and those captured so far, in the guide's order.</summary>
public sealed class CaptureDraft
{
    private readonly CaptureStore store;
    private CaptureFile file;

    internal CaptureDraft(CaptureStore store, string folder, CaptureFile file)
    {
        this.store = store;
        Folder = folder;
        this.file = file;
    }

    internal string Folder { get; }

    public Guid Id => file.Id;
    public Guid GameId => file.GameId;
    public GuidePageMatch Item => new(file.Site, file.ItemId, "");
    public IReadOnlyList<GuidePageLink> Pages => file.Pages;

    /// <summary>The guide's page count; more than <see cref="Pages"/> when it was cut at 200.</summary>
    public int TotalPages => file.TotalPages;

    public IReadOnlyList<CapturedPage> Captured => file.Pages
        .Select(page => file.Captured.FirstOrDefault(entry => entry.Reading.Match.PageKey == page.PageKey))
        .OfType<CapturedEntry>()
        .Select(entry => new CapturedPage(entry.Reading, entry.Images.ToDictionary(
            image => new Uri(image.Key), image => Path.Combine(Folder, image.Value.Replace('/', Path.DirectorySeparatorChar)))))
        .ToArray();

    public IReadOnlyList<GuidePageLink> Remaining =>
        file.Pages.Where(page => file.Captured.All(entry => entry.Reading.Match.PageKey != page.PageKey)).ToArray();

    /// <summary>Every page of the guide is captured; a guide cut at 200 pages never is.</summary>
    public bool IsComplete => file.TotalPages == file.Pages.Count && Remaining.Count == 0;

    /// <summary>Stores an image's bytes in the draft and returns the file's path.</summary>
    public string SaveImage(Uri url, ReadOnlySpan<byte> bytes)
    {
        string named = Path.GetExtension(url.AbsolutePath).ToLowerInvariant();
        string extension = named is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".avif" or ".bmp"
            ? named : ".img";
        string relative = $"images/{Guid.NewGuid():N}{extension}";
        string path = Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes.ToArray());
        Touch();
        return path;
    }

    /// <summary>Adds or replaces a captured page; its images must be files from <see cref="SaveImage"/>.</summary>
    public void Add(GuidePageReading page, IReadOnlyDictionary<Uri, string> imageFiles)
    {
        if (page.Match.Site != file.Site || page.Match.ItemId != file.ItemId)
        {
            throw new ArgumentException("The page belongs to another guide.", nameof(page));
        }
        Dictionary<string, string> images = imageFiles.ToDictionary(
            image => image.Key.AbsoluteUri,
            image => Path.GetRelativePath(Folder, image.Value).Replace(Path.DirectorySeparatorChar, '/'));
        if (images.Values.Any(relative => relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)))
        {
            throw new ArgumentException("Images must be saved through the draft.", nameof(imageFiles));
        }
        List<CapturedEntry> captured = file.Captured.Where(entry => entry.Reading.Match.PageKey != page.Match.PageKey).ToList();
        captured.Add(new CapturedEntry(page, images));
        file = file with { Captured = captured };
        Touch();
    }

    public void Discard()
    {
        if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
    }

    private void Touch()
    {
        file = file with { TouchedUtc = store.Now };
        Write();
    }

    internal void Write()
    {
        string path = Path.Combine(Folder, "capture.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(file, InfrastructureJsonContext.Default.CaptureFile));
        File.Move(temporary, path, overwrite: true);
    }
}
