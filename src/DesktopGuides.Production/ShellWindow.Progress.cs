using System.Text.Json;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    private const string ApproximateRestoreMessage =
        "Opened near your last place. The guide changed since you were here.";
    private const string UnavailableRestoreMessage =
        "Couldn't return to your last place, so the guide opened at the start.";
    private const string SaveFailedMessage = "Couldn't save your place in this guide.";

    private ProgressCoordinator? progress;
    private IProgressTracking? progressTracking;
    private readonly object progressCountsFile = new();
    // #94: taken when a save failure is first counted, so later saves don't
    // overwrite where the failed write was stuck.
    private int diagnosedFailures;
    private ProgressFailureContext? failureContext;
    private (ProgressCounts Counts, ProgressFailure? LastFailure)? writtenCounts;

    private void StartProgress(IReadingLocationStore store)
    {
        ProgressCoordinator coordinator = new(store, TimeProvider.System);
        coordinator.SaveFailed += (_, _) =>
            DispatcherQueue.TryEnqueue(() => ShowWarningStatus(SaveFailedMessage));
        coordinator.CountsChanged += (_, _) =>
            WriteProgressCountsForTest(coordinator.Counts, coordinator.LastFailure);
        progress = coordinator;
    }

    // Test gate only: counts and the latest failure's stage and type, never
    // locator text or exception messages.
    private void WriteProgressCountsForTest(ProgressCounts counts, ProgressFailure? lastFailure)
    {
        if (cacheRoot is null ||
            !TestGate.IsOpen(AppLane.Current.LocalEvent("ProgressDiagnostics", Environment.ProcessId)))
        {
            return;
        }
        string folder = Path.Combine(cacheRoot, "diagnostics");
        string path = Path.Combine(folder, $"progress-{Environment.ProcessId}.json");
        lock (progressCountsFile)
        {
            writtenCounts = (counts, lastFailure);
            if (counts.Failures != diagnosedFailures)
            {
                diagnosedFailures = counts.Failures;
                Func<RepositoryWriteTiming>? failed = repository?.LastWrite;
                RepositoryWriteTiming? write = failed?.Invoke();
                failureContext = new ProgressFailureContext(
                    write?.GateWaitMs,
                    write?.QueueWaitMs,
                    write?.WorkMs,
                    write?.ValidateMs,
                    write?.OpenMs,
                    write?.PragmaMs,
                    null,
                    ThreadPool.ThreadCount,
                    ThreadPool.PendingWorkItemCount);
                if (failed is not null && write?.WorkMs < 0)
                {
                    _ = RecordWhenFailedWriteFinishesAsync(failed, failureContext);
                }
            }
            try
            {
                Directory.CreateDirectory(folder);
                DiagnosticFile.Write(path, JsonSerializer.Serialize(
                    new ProgressCountsForTest(
                        counts.Saves,
                        counts.SkippedUnchanged,
                        counts.Failures,
                        counts.Opens,
                        counts.OpenFailures,
                        lastFailure is null ? null : new ProgressFailureForTest(
                            lastFailure.Stage,
                            lastFailure.ErrorType,
                            lastFailure.HResult,
                            lastFailure.Trigger,
                            lastFailure.CaptureMs,
                            lastFailure.WriteMs),
                        failureContext),
                    ProductionJsonContext.Default.ProgressCountsForTest));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // #94: a timed-out write runs on; how long it took tells a lock wait
    // (up to SQLite's 30 s timeout) from a brief stall. -1: still running at 40 s.
    private async Task RecordWhenFailedWriteFinishesAsync(
        Func<RepositoryWriteTiming> failed, ProgressFailureContext context)
    {
        long finished = -1;
        for (int poll = 0; poll < 160 && finished < 0; poll++)
        {
            await Task.Delay(250).ConfigureAwait(false);
            finished = failed().WorkMs;
        }
        (ProgressCounts Counts, ProgressFailure? LastFailure) latest;
        lock (progressCountsFile)
        {
            if (!ReferenceEquals(failureContext, context) || writtenCounts is null) return;
            failureContext = context with { FinishedWorkMs = finished };
            latest = writtenCounts.Value;
        }
        WriteProgressCountsForTest(latest.Counts, latest.LastFailure);
    }

    // Saves an unsaved place while the session is still alive. Never throws.
    private async Task DisposeProgressTrackingAsync()
    {
        IProgressTracking? tracking = progressTracking;
        progressTracking = null;
        if (tracking is not null)
        {
            await tracking.DisposeAsync();
        }
    }

    private void WindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            FlushProgressAsync();
        }
    }

    private async void FlushProgressAsync()
    {
        if (progressTracking is not IProgressTracking tracking)
        {
            return;
        }
        using CancellationTokenSource timeout = new(ProgressCoordinator.FlushTimeout);
        try
        {
            await tracking.FlushAsync(timeout.Token);
        }
        catch (Exception)
        {
            // The coordinator reports failures through SaveFailed.
        }
    }

    // The unmoved place the coordinator compares captures against.
    private static async Task<string?> CaptureBaselineAsync(
        IReaderSession session, CancellationToken token)
    {
        try
        {
            ReaderLocation location = await session.GetLocationAsync(token);
            return ReaderLocationCodec.Serialize(location with
            {
                EstimatedFraction = ProgressEstimate.Bound(location.EstimatedFraction),
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Once a session is showing, returns it to the guide's saved place and
    // says so when that place is approximate or lost. Returns false when a
    // newer render took over.
    private async Task<bool> OpenAtSavedPlaceAsync(
        Guide guide, IReaderSession session, int generation, string contentSha256,
        IReadOnlyCollection<string>? htmlDocuments, CancellationToken token,
        bool quiet = false, bool recordOpen = true)
    {
        string? stored;
        RestoreKind? kind = null;
        try
        {
            stored = RestoreLocatorForTest() ??
                (await repository!.GetReadingStateAsync(guide.Id, token))?.LocatorJson;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            // An unreadable reading state is a lost place, not a failed open.
            stored = null;
            kind = RestoreKind.Unavailable;
        }
        if (generation != renderGeneration)
        {
            return false;
        }
        if (stored is not null)
        {
            // Stored and override locators are untrusted; only the codec's
            // result reaches the session.
            LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
                stored, guide.Format, contentSha256, htmlDocuments);
            kind = RestoreKind.Unavailable;
            if ((decoded.Status is LocationDecodeStatus.Valid or LocationDecodeStatus.ContentChanged) &&
                decoded.Location is ReaderLocation location)
            {
                try
                {
                    HtmlOpenTimelineForTest.Mark("restore");
                    kind = (await session.RestoreLocationAsync(location, token)).Kind;
                    HtmlOpenTimelineForTest.Mark("restored", kind.ToString());
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception)
                {
                    kind = RestoreKind.Unavailable;
                }
                if (generation != renderGeneration)
                {
                    return false;
                }
            }
        }
        // T21.2: a map restores or fits without a notice.
        if (!quiet) ShowRestoreStatus(kind);
        // An exact restore is already at the stored place; any other outcome
        // compares against where the reader actually is, so an unmoved guide
        // keeps its stored locator.
        string? baseline = kind == RestoreKind.Exact
            ? stored
            : await CaptureBaselineAsync(session, token);
        if (generation != renderGeneration)
        {
            return false;
        }
        // T21.2: a map isn't reading activity, so its open isn't recorded.
        progressTracking = progress!.Track(guide.Id, session, baseline, recordOpen);
        StartContents(session, guide, contentSha256, htmlDocuments);
        HtmlOpenTimelineForTest.Mark("ready");
        return true;
    }

    private void ShowRestoreStatus(RestoreKind? kind)
    {
        if (kind == RestoreKind.Approximate)
        {
            ShowReaderNotice(ApproximateRestoreMessage);
        }
        else if (kind == RestoreKind.Unavailable)
        {
            ShowWarningStatus(UnavailableRestoreMessage);
        }
        else
        {
            AnnounceStatus("Guide ready.");
        }
    }

    // Test gate only: replaces the stored locator. The file is untrusted and
    // goes through the codec like a stored locator.
    private string? RestoreLocatorForTest()
    {
        if (dataRoot is null ||
            !TestGate.IsOpen(AppLane.Current.LocalEvent("ProgressOverride", Environment.ProcessId)))
        {
            return null;
        }
        string path = Path.Combine(dataRoot, "test", "restore-locator.json");
        try
        {
            FileInfo file = new(path);
            if (!file.Exists) return null;
            // Longer than any locator: decodes as Invalid.
            if (file.Length > ReaderLocationCodec.MaxBytes) return string.Empty;
            return File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
