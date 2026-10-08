using System.Text.Json;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
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
            !TestGate.IsOpen($@"Local\DesktopGuides.Preview.ProgressDiagnostics.{Environment.ProcessId}"))
        {
            return;
        }
        string folder = Path.Combine(cacheRoot, "diagnostics");
        string path = Path.Combine(folder, $"progress-{Environment.ProcessId}.json");
        lock (progressCountsFile)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(new
                {
                    saves = counts.Saves,
                    skippedUnchanged = counts.SkippedUnchanged,
                    failures = counts.Failures,
                    opens = counts.Opens,
                    openFailures = counts.OpenFailures,
                    lastFailure = lastFailure is null ? null : new
                    {
                        stage = lastFailure.Stage,
                        type = lastFailure.ErrorType,
                        hresult = lastFailure.HResult,
                    },
                }));
                File.Move(temp, path, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
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
        string? htmlEntry, CancellationToken token)
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
                stored, guide.Format, contentSha256, htmlEntry);
            kind = RestoreKind.Unavailable;
            if ((decoded.Status is LocationDecodeStatus.Valid or LocationDecodeStatus.ContentChanged) &&
                decoded.Location is ReaderLocation location)
            {
                try
                {
                    kind = (await session.RestoreLocationAsync(location, token)).Kind;
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
        ShowRestoreStatus(kind);
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
        progressTracking = progress!.Track(guide.Id, session, baseline);
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
            !TestGate.IsOpen($@"Local\DesktopGuides.Preview.ProgressOverride.{Environment.ProcessId}"))
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
