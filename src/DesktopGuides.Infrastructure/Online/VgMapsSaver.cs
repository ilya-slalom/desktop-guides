using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;

namespace DesktopGuides.Infrastructure.Online;

/// <summary>Converts a GIF's first frame to PNG; the platform supplies it.</summary>
public interface IMapImageConverter
{
    Task<byte[]> ToPngAsync(byte[] gif, CancellationToken token);
}

public sealed record VgMapsSaveResult(int Added, int Replaced, int UpToDate);

/// <summary>A save that stopped at a map; the maps before it are saved.</summary>
public sealed class VgMapsSaveException(ProviderErrorKind? kind, string step, int saved, Exception inner)
    : Exception($"Saving maps stopped at {step}.", inner)
{
    public ProviderErrorKind? Kind { get; } = kind;
    public string Step { get; } = step;
    public int Saved { get; } = saved;
}

/// <summary>
/// S22 PR 5: saves chosen maps, one at a time, as image maps of the game.
/// Each is checked and published on its own through the image-map import,
/// or replaces the map saved before from the same address; an unchanged
/// one is up to date. A failure stops the run at that map.
/// </summary>
public sealed class VgMapsSaver(
    VgMapsClient client, SqliteLibraryRepository repository, GuideImportPublisher publisher,
    GuideImportValidator validator, IMapImageConverter converter, string workRoot, TimeProvider clock)
{
    public async Task<VgMapsSaveResult> SaveAsync(
        Guid gameId, VgMapsGamePage page, IReadOnlyList<VgMapsMap> maps,
        IProgress<(int Done, int Total)>? progress, CancellationToken token)
    {
        if (maps.FirstOrDefault(map => map.Saveable is VgMapsSaveable.NotSaveable or VgMapsSaveable.TooLarge) is { } refused)
        {
            throw new ArgumentException($"{refused.Area} can't be saved.", nameof(maps));
        }
        int added = 0, replaced = 0, upToDate = 0;
        for (int index = 0; index < maps.Count; index++)
        {
            VgMapsMap map = maps[index];
            string step = $"map {index + 1} of {maps.Count}";
            try
            {
                switch (await SaveOneAsync(gameId, page, map, token))
                {
                    case OnlineSaveResult.Added:
                        added++;
                        break;
                    case OnlineSaveResult.UpToDate:
                        upToDate++;
                        break;
                    default:
                        replaced++;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new VgMapsSaveException((error as ProviderException)?.Kind, step, added + replaced + upToDate, error);
            }
            progress?.Report((index + 1, maps.Count));
        }
        return new VgMapsSaveResult(added, replaced, upToDate);
    }

    private async Task<OnlineSaveResult> SaveOneAsync(Guid gameId, VgMapsGamePage page, VgMapsMap map, CancellationToken token)
    {
        byte[] bytes = await client.GetMapAsync(map.Url, token);
        string extension = ".png";
        if (map.Saveable == VgMapsSaveable.Convert) bytes = await converter.ToPngAsync(bytes, token);
        else if (map.Format is "JPG" or "JPEG") extension = ".jpg";

        string work = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string file = Path.Combine(work, "map" + extension);
            await File.WriteAllBytesAsync(file, bytes, token);
            ImportManifest manifest = await validator.InspectAsync(file, token) switch
            {
                // A map past the limits throws here, with the import's own message.
                ImportReady ready => ready.Manifest,
                _ => throw new GuideImportException(ImportIssue.Unreadable, "The map couldn't be read as an image."),
            };
            string itemId = ItemId(map.Url);
            GuideSource source = new(
                SourceSite.VgMaps, itemId, page.PageUrl.AbsoluteUri, Cut(map.Ripper, 200), page.Copyright,
                Cut($"{page.Game.Title}: {map.Area}", 512), clock.GetUtcNow(), null);
            if (await repository.FindSourcedGuideAsync(gameId, SourceSite.VgMaps, itemId, token) is { } saved)
            {
                return await publisher.ReplaceAsync(manifest, saved.Id, source, allowFormatChange: true, null, token) switch
                {
                    GuideReplaceOutcome.UpToDate => new OnlineSaveResult.UpToDate(saved.Id),
                    _ => new OnlineSaveResult.Replaced(saved.Id),
                };
            }
            Guid id = await publisher.PublishAsync(manifest, gameId, Cut(map.Area, 200)!, allowDuplicate: true, null, token, source);
            return new OnlineSaveResult.Added(id);
        }
        finally
        {
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        }
    }

    // The path under /Atlas/ ("PSX/FinalFantasyVII-World.png") names a map,
    // within GuideSources' 200 characters; a longer one is named by its hash.
    internal static string ItemId(Uri map)
    {
        string path = Uri.UnescapeDataString(map.AbsolutePath);
        string item = path.StartsWith("/Atlas/", StringComparison.Ordinal) ? path["/Atlas/".Length..] : path.TrimStart('/');
        return item.Length is > 0 and <= 200 ? item
            : "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(map.AbsoluteUri)));
    }

    private static string? Cut(string? text, int length) => text is { Length: > 0 } value && value.Length > length
        ? value[..length].TrimEnd() : text;
}
