namespace DesktopGuides.Infrastructure.Import;

public enum StaticHtmlValidationIssue
{
    UnsafePath,
    CaseCollision,
    SourceChanged,
    StageChanged
}

public sealed class StaticHtmlValidationException(
    StaticHtmlValidationIssue issue,
    string message) : IOException(message)
{
    public StaticHtmlValidationIssue Issue { get; } = issue;
}

public sealed record StaticHtmlPreviewWarning(
    StaticReferenceStatus Status,
    string SourceRelativePath,
    string RawTarget,
    string? RelativePath,
    string Message);

public sealed class StaticHtmlImportPreview
{
    internal StaticHtmlImportPreview(
        string sourceRoot,
        string sourceEntryFileName,
        string entryRelativePath,
        StaticHtmlManifest manifest,
        IReadOnlyList<StaticHtmlPreviewWarning> warnings)
    {
        SourceRoot = sourceRoot;
        SourceEntryFileName = sourceEntryFileName;
        EntryRelativePath = entryRelativePath;
        Manifest = manifest;
        Warnings = warnings;
    }

    internal string SourceRoot { get; }
    internal string SourceEntryFileName { get; }
    public string EntryRelativePath { get; }
    public StaticHtmlManifest Manifest { get; }
    public IReadOnlyList<StaticHtmlPreviewWarning> Warnings { get; }
}
