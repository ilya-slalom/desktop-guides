using System.Diagnostics;
using System.Text.Json;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Maps;
using DesktopGuides.Infrastructure.Maps;

namespace DesktopGuides.Production;

/// <summary>A cache being made: the overview's path first, then the whole cache.</summary>
internal sealed record MapCacheBuild(Task<string> Overview, Task Complete);

// T21.2: builds large maps' caches one at a time, off the UI thread. An
// import queues its maps at the back; an open moves its map to the front.
internal sealed class MapCacheQueue(MapTileStore store, string cacheRoot)
{
    private readonly object gate = new();
    private readonly LinkedList<Job> waiting = new();
    private readonly Dictionary<string, Job> jobs = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource cancel = new();
    private readonly List<Timing> diagnostics = [];
    // A prune waits for the build in progress, so it never deletes it.
    private readonly SemaphoreSlim work = new(1, 1);
    private bool running;
    private Job? current;

    // The open map's hash, which a trim never drops.
    public string? OpenHash { get; set; }

    public MapCacheBuild Ensure(Guide map, string sourcePath, bool front)
    {
        ArgumentNullException.ThrowIfNull(map);
        MapPyramid pyramid = new(map.PixelWidth ?? throw new ArgumentException("A map has a size.", nameof(map)),
            map.PixelHeight ?? throw new ArgumentException("A map has a size.", nameof(map)));
        if (pyramid.Small) throw new ArgumentException("A small map has no cache.", nameof(map));
        string sha = map.ContentSha256;
        lock (gate)
        {
            if (jobs.TryGetValue(sha, out Job? existing))
            {
                if (front && existing.Node.List is not null)
                {
                    waiting.Remove(existing.Node);
                    waiting.AddFirst(existing.Node);
                }
                return existing.Build;
            }
        }
        if (store.IsComplete(sha, pyramid))
        {
            return new MapCacheBuild(Task.FromResult(store.OverviewPath(sha)), Task.CompletedTask);
        }
        lock (gate)
        {
            if (jobs.TryGetValue(sha, out Job? raced)) return raced.Build;
            Job job = new(sha, sourcePath, pyramid);
            job.Node = front ? waiting.AddFirst(job) : waiting.AddLast(job);
            jobs[sha] = job;
            if (!running)
            {
                running = true;
                _ = Task.Run(RunAsync);
            }
            return job.Build;
        }
    }

    public void CancelAll() => cancel.Cancel();

    // Stops the matching builds, queued or running, and waits until the
    // running one has let go of the map's file, so the map can be removed.
    public async Task StopAsync(Func<string, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        Task? stopping = null;
        lock (gate)
        {
            foreach (Job job in waiting.Where(job => matches(job.Sha)).ToList())
            {
                waiting.Remove(job.Node);
                jobs.Remove(job.Sha);
                job.Cancel();
            }
            if (current is Job building && matches(building.Sha))
            {
                building.Stop.Cancel();
                stopping = building.Done.Task;
            }
        }
        if (stopping is not null) await stopping.ConfigureAwait(false);
    }

    // Removes caches no guide has, between builds.
    public async Task PruneAsync(Func<Task<IReadOnlySet<string>>> liveHashes)
    {
        ArgumentNullException.ThrowIfNull(liveHashes);
        await work.WaitAsync(cancel.Token).ConfigureAwait(false);
        try
        {
            IReadOnlySet<string> live = await liveHashes().ConfigureAwait(false);
            await Task.Run(() => store.Prune(live)).ConfigureAwait(false);
        }
        finally
        {
            work.Release();
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            Job job;
            lock (gate)
            {
                if (waiting.First is not LinkedListNode<Job> first || cancel.IsCancellationRequested)
                {
                    foreach (Job left in waiting) left.Cancel();
                    waiting.Clear();
                    running = false;
                    return;
                }
                job = first.Value;
                waiting.RemoveFirst();
                current = job;
            }
            await work.WaitAsync().ConfigureAwait(false);
            try
            {
                await BuildAsync(job);
            }
            finally
            {
                work.Release();
            }
            lock (gate)
            {
                jobs.Remove(job.Sha);
                current = null;
            }
            job.Done.TrySetResult();
        }
    }

    private async Task BuildAsync(Job job)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token, job.Stop.Token);
        CancellationToken token = linked.Token;
        Stopwatch clock = Stopwatch.StartNew();
        try
        {
            using MapTileBuild build = store.BeginBuild(job.Sha);
            await WicMapTileBuilder.WriteOverviewAsync(job.SourcePath, job.Pyramid, build.OverviewPath, token);
            long overviewMs = clock.ElapsedMilliseconds;
            job.Overview.TrySetResult(build.OverviewPath);
            int tiles = await WicMapTileBuilder.WriteTilesAsync(job.SourcePath, job.Pyramid, build.TilePath, token);
            build.Commit(job.Pyramid);
            job.Complete.TrySetResult();
            long bytes = store.Trim(new HashSet<string>(StringComparer.Ordinal) { job.Sha, OpenHash ?? job.Sha });
            Record(new Timing(job.Sha, overviewMs, clock.ElapsedMilliseconds - overviewMs, tiles, bytes, null));
        }
        catch (OperationCanceledException)
        {
            job.Cancel();
        }
        catch (Exception error)
        {
            job.Overview.TrySetException(error);
            job.Complete.TrySetException(error);
            Record(new Timing(job.Sha, 0, 0, 0, 0, error.GetType().Name));
        }
    }

    // Test-only build timings: no paths and no titles.
    private void Record(Timing build)
    {
        if (!TestGate.IsOpen(AppLane.Current.LocalEvent("MapDiagnostics", Environment.ProcessId))) return;
        try
        {
            string folder = Path.Combine(cacheRoot, "diagnostics");
            Directory.CreateDirectory(folder);
            lock (diagnostics)
            {
                diagnostics.Add(build);
                using FileStream file = new(Path.Combine(folder, "map-cache.json"), FileMode.Create, FileAccess.Write);
                using Utf8JsonWriter writer = new(file);
                writer.WriteStartObject();
                writer.WriteStartArray("builds");
                foreach (Timing timing in diagnostics)
                {
                    writer.WriteStartObject();
                    writer.WriteString("sha", timing.Sha);
                    writer.WriteNumber("overviewMs", timing.OverviewMs);
                    writer.WriteNumber("tilesMs", timing.TilesMs);
                    writer.WriteNumber("tiles", timing.Tiles);
                    writer.WriteNumber("bytes", timing.Bytes);
                    if (timing.Failed is string failed) writer.WriteString("failed", failed);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Diagnostics never fail a build.
        }
    }

    private sealed record Timing(string Sha, long OverviewMs, long TilesMs, int Tiles, long Bytes, string? Failed);

    private sealed class Job(string sha, string sourcePath, MapPyramid pyramid)
    {
        public string Sha { get; } = sha;
        public string SourcePath { get; } = sourcePath;
        public MapPyramid Pyramid { get; } = pyramid;
        public TaskCompletionSource<string> Overview { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<Job> Node { get; set; } = null!;
        public CancellationTokenSource Stop { get; } = new();
        // Set once the build has ended and closed the map's file.
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MapCacheBuild Build => new(Overview.Task, Complete.Task);

        public void Cancel()
        {
            Overview.TrySetCanceled();
            Complete.TrySetCanceled();
        }
    }
}
