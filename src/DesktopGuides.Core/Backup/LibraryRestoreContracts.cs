namespace DesktopGuides.Core.Backup;

public enum LibraryRestorePhase { Copying, Checking, Extracting }

public readonly record struct LibraryRestoreProgress(
    LibraryRestorePhase Phase, long BytesDone, long BytesTotal);

public enum LibraryRestoreIssue
{
    SourceUnavailable,
    ArchiveInvalid,
    ArchiveUnsafe,
    NotEnoughSpace,
    NewerVersion,
    DatabaseInvalid,
    ReferencesInvalid,
    SwapFailed
}

public sealed class LibraryRestoreException : Exception
{
    public LibraryRestoreException(
        LibraryRestoreIssue issue, IReadOnlyList<Guid>? guideIds = null,
        IReadOnlyList<Guid>? gameIds = null, IReadOnlyList<string>? titles = null,
        long? bytesNeeded = null, Exception? inner = null)
        : base($"Library restore failed: {issue}.", inner)
    {
        Issue = issue;
        GuideIds = guideIds ?? [];
        GameIds = gameIds ?? [];
        Titles = titles ?? [];
        BytesNeeded = bytesNeeded;
    }

    public LibraryRestoreIssue Issue { get; }

    /// <summary>For <see cref="LibraryRestoreIssue.ReferencesInvalid"/>: guides whose files are missing.</summary>
    public IReadOnlyList<Guid> GuideIds { get; }

    /// <summary>For <see cref="LibraryRestoreIssue.ReferencesInvalid"/>: games whose artwork is missing.</summary>
    public IReadOnlyList<Guid> GameIds { get; }

    /// <summary>The titles of those guides and games, read from the backup before it was discarded.</summary>
    public IReadOnlyList<string> Titles { get; }

    /// <summary>For <see cref="LibraryRestoreIssue.NotEnoughSpace"/>: the free space needed.</summary>
    public long? BytesNeeded { get; }
}

/// <summary>A backup checked and extracted into staging, ready to replace the library.</summary>
public sealed record LibraryRestoreStage(
    Guid StageId, DateTimeOffset CreatedUtc, string AppVersion,
    int SchemaVersion, int Games, int Guides, long Bytes);
