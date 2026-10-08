namespace DesktopGuides.Core.Backup;

public enum LibraryExportPhase { Preparing, Writing, Verifying }

public readonly record struct LibraryExportProgress(
    LibraryExportPhase Phase, long BytesDone, long BytesTotal);

/// <summary>The saved archive: its counts, size in bytes and SHA-256.</summary>
public sealed record LibraryExportResult(
    string Path, int Games, int Guides, int Files, long Bytes, string Sha256);

public enum LibraryExportIssue
{
    DestinationNotAllowed,
    DestinationUnavailable,
    DestinationExists,
    RecoveryIncomplete,
    DatabaseInvalid,
    ManagedFilesDamaged,
    LibraryTooLarge,
    WriteFailed,
    VerificationFailed
}

public sealed class LibraryExportException : Exception
{
    public LibraryExportException(
        LibraryExportIssue issue, IReadOnlyList<Guid>? guideIds = null,
        IReadOnlyList<Guid>? gameIds = null, Exception? inner = null)
        : base($"Library export failed: {issue}.", inner)
    {
        Issue = issue;
        GuideIds = guideIds ?? [];
        GameIds = gameIds ?? [];
    }

    public LibraryExportIssue Issue { get; }

    /// <summary>For <see cref="LibraryExportIssue.ManagedFilesDamaged"/>: the affected guides.</summary>
    public IReadOnlyList<Guid> GuideIds { get; }

    /// <summary>For <see cref="LibraryExportIssue.ManagedFilesDamaged"/>: games with damaged artwork.</summary>
    public IReadOnlyList<Guid> GameIds { get; }
}
