using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Maps;
using DesktopGuides.Infrastructure.Import;

namespace DesktopGuides.Production;

// T21.2: a large map's cache is built after its import and dropped when no
// guide has its hash. The startup prune also covers a restore, which starts
// the library again.
public sealed partial class ShellWindow
{
    private async Task<Guid> PublishAndQueueMapAsync(
        GuideImportPublisher publisher, ImportManifest manifest, Guid gameId, string title,
        bool allowDuplicate, IProgress<ImportProgress>? progress, CancellationToken token)
    {
        Guid guideId = await publisher.PublishAsync(manifest, gameId, title, allowDuplicate, progress, token);
        if (manifest is MapImportManifest map && !new MapPyramid(map.PixelWidth, map.PixelHeight).Small)
        {
            QueueMapCache(guideId);
        }
        return guideId;
    }

    private async void QueueMapCache(Guid guideId)
    {
        try
        {
            if (repository is null || libraryPaths is null || mapCache is null) return;
            Guide? map = await repository.GetGuideAsync(guideId);
            if (map is null) return;
            string path = libraryPaths.ResolveExistingGuideFile(map.Id, map.PrimaryRelativePath);
            MapCacheBuild build = mapCache.Ensure(map, path, front: false);
            // Opening the map reports a failed build; here it's only observed.
            await build.Complete.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
        }
        catch (Exception)
        {
            // The map still opens; its cache is built then.
        }
    }

    private async void PruneMapCacheSoon()
    {
        try
        {
            if (repository is not { } library || mapCache is not { } queue) return;
            await queue.PruneAsync(() => library.ListMapHashesAsync());
        }
        catch (Exception)
        {
            // A cache left behind is pruned at the next start.
        }
    }
}
