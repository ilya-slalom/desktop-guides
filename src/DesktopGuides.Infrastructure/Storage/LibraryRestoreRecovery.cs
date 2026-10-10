using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Infrastructure.Storage;

public enum RestoreRecoveryOutcome { None, RolledBack, PendingVerify }

/// <summary>
/// Finishes or undoes a restore from what is on disk, before the repository
/// opens. Only the session that swapped (verifyRestore) may keep a promoted
/// library; any later start rolls it back. Every step is safe to repeat.
/// </summary>
public static class LibraryRestoreRecovery
{
    public static RestoreRecoveryOutcome Run(ILibraryPaths paths, bool verifyRestore) =>
        Run(paths, verifyRestore, _ => { });

    internal static RestoreRecoveryOutcome Run(
        ILibraryPaths paths, bool verifyRestore, Action<RestoreCheckpoint> checkpoint) =>
        Guarded(() =>
        {
            RestoreMarker? marker = RestoreMarker.Read(paths);
            if (marker is null)
            {
                DeleteLeftovers(paths);
                return RestoreRecoveryOutcome.None;
            }
            switch (marker.Phase)
            {
                case RestoreMarkerPhase.Confirmed:
                    FinishComplete(paths, marker);
                    return RestoreRecoveryOutcome.None;
                case RestoreMarkerPhase.RollingBack:
                    FinishRollBack(paths, marker, checkpoint);
                    return RestoreRecoveryOutcome.RolledBack;
            }
            if (Directory.Exists(RestoreMarker.StagedLibrary(paths, marker.StageId)))
            {
                // The swap never promoted the stage: return a parked prior root.
                string prior = RestoreMarker.PriorRoot(paths, marker.StageId);
                if (marker.PriorExists && Directory.Exists(prior))
                {
                    if (Directory.Exists(paths.LibraryRoot))
                    {
                        throw new InvalidDataException("Both the live and the parked library exist.");
                    }
                    Directory.Move(prior, paths.LibraryRoot);
                }
                else if (marker.PriorExists && !Directory.Exists(paths.LibraryRoot))
                {
                    throw new InvalidDataException("The parked library is missing.");
                }
                RestoreMarker.Delete(paths);
                DeleteLeftovers(paths);
                return RestoreRecoveryOutcome.RolledBack;
            }
            // Only a promoted library that is still there can be verified.
            if (verifyRestore && Directory.Exists(paths.LibraryRoot))
            {
                return RestoreRecoveryOutcome.PendingVerify;
            }
            BeginRollBack(paths, marker, checkpoint);
            return RestoreRecoveryOutcome.RolledBack;
        });

    /// <summary>The restored library opened: keep it.</summary>
    public static void Complete(ILibraryPaths paths) => Complete(paths, _ => { });

    internal static void Complete(ILibraryPaths paths, Action<RestoreCheckpoint> checkpoint) =>
        Guarded(() =>
        {
            if (RestoreMarker.Read(paths) is not { } marker)
            {
                return 0;
            }
            if (marker.Phase == RestoreMarkerPhase.Confirmed)
            {
                FinishComplete(paths, marker);
                return 0;
            }
            // Only a promoted swap can be kept: the stage is gone and library/ is there.
            if (marker.Phase != RestoreMarkerPhase.Swapping ||
                Directory.Exists(RestoreMarker.StagedLibrary(paths, marker.StageId)) ||
                !Directory.Exists(paths.LibraryRoot))
            {
                throw new InvalidDataException("The restore wasn't promoted, so it can't be kept.");
            }
            // Confirmed first, so a crash below can only finish the cleanup.
            RestoreMarker confirmed = marker with { Phase = RestoreMarkerPhase.Confirmed };
            confirmed.Write(paths);
            checkpoint(RestoreCheckpoint.Confirmed);
            FinishComplete(paths, confirmed);
            return 0;
        });

    /// <summary>The restored library didn't open: put the prior one back.</summary>
    public static void RollBack(ILibraryPaths paths) => RollBack(paths, _ => { });

    internal static void RollBack(ILibraryPaths paths, Action<RestoreCheckpoint> checkpoint) =>
        Guarded(() =>
        {
            if (RestoreMarker.Read(paths) is not { } marker)
            {
                return 0;
            }
            // A confirmed restore is already kept: only its cleanup is left.
            if (marker.Phase == RestoreMarkerPhase.Confirmed)
            {
                FinishComplete(paths, marker);
            }
            else
            {
                BeginRollBack(paths, marker, checkpoint);
            }
            return 0;
        });

    private static void BeginRollBack(ILibraryPaths paths, RestoreMarker marker, Action<RestoreCheckpoint> checkpoint)
    {
        // Once RollingBack, a live root with no parked prior reads as returned,
        // so a promoted library must not be marked so while its prior is gone.
        if (marker.Phase == RestoreMarkerPhase.Swapping && marker.PriorExists &&
            !Directory.Exists(RestoreMarker.PriorRoot(paths, marker.StageId)))
        {
            throw new InvalidDataException("The parked library is missing.");
        }
        // RollingBack first, so a crash below never moves a returned prior root aside.
        RestoreMarker rolling = marker with { Phase = RestoreMarkerPhase.RollingBack };
        rolling.Write(paths);
        FinishRollBack(paths, rolling, checkpoint);
    }

    private static void FinishComplete(ILibraryPaths paths, RestoreMarker marker)
    {
        LibraryRestorer.DeleteTree(Path.GetDirectoryName(RestoreMarker.PriorRoot(paths, marker.StageId))!);
        LibraryRestorer.DeleteTree(LibraryRestorer.StageRoot(paths, marker.StageId));
        RestoreMarker.Delete(paths);
    }

    private static void FinishRollBack(ILibraryPaths paths, RestoreMarker marker, Action<RestoreCheckpoint> checkpoint)
    {
        string prior = RestoreMarker.PriorRoot(paths, marker.StageId);
        string unverified = RestoreMarker.Unverified(paths, marker.StageId);
        bool priorParked = marker.PriorExists && Directory.Exists(prior);
        // library/ is the promoted root while the prior is still parked, or when there was none.
        if (Directory.Exists(paths.LibraryRoot) && (priorParked || !marker.PriorExists))
        {
            ManagedPathResolver.RejectFilesystemLinks(paths.LibraryRoot);
            string staging = Path.GetDirectoryName(unverified)!;
            Directory.CreateDirectory(staging);
            ManagedPathResolver.RejectFilesystemLinks(staging);
            LibraryRestorer.DeleteTree(unverified);
            Directory.Move(paths.LibraryRoot, unverified);
        }
        checkpoint(RestoreCheckpoint.RolledAside);
        if (priorParked)
        {
            Directory.Move(prior, paths.LibraryRoot);
        }
        else if (marker.PriorExists && !Directory.Exists(paths.LibraryRoot))
        {
            throw new InvalidDataException("The parked library is missing.");
        }
        checkpoint(RestoreCheckpoint.PriorReturned);
        RestoreMarker.Delete(paths);
        DeleteLeftovers(paths);
    }

    // Only restore folders: .recovery also holds migration copies, which stay.
    // Best effort: the marker is gone, so a leftover is only wasted space.
    private static void DeleteLeftovers(ILibraryPaths paths)
    {
        try
        {
            DeleteFolders(Path.Combine(paths.DataRoot, LibraryRestorer.StagingFolderName), "*");
            DeleteFolders(paths.RecoveryRoot, "restore-*");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Never enumerate through a link: its folders aren't ours.
    private static void DeleteFolders(string parent, string pattern)
    {
        DirectoryInfo folder = new(parent);
        if (!folder.Exists || folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return;
        }
        foreach (string child in Directory.EnumerateDirectories(parent, pattern))
        {
            LibraryRestorer.DeleteTree(child);
        }
    }

    private static T Guarded<T>(Func<T> work)
    {
        try
        {
            return work();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new LibraryOpenException(LibraryOpenIssue.RestoreIncomplete, inner: error);
        }
    }
}
