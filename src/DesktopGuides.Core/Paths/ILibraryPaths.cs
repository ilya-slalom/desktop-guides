namespace DesktopGuides.Core.Paths;

public interface ILibraryPaths
{
    string DataRoot { get; }
    string LibraryRoot { get; }
    string ContentRoot { get; }
    string StagingRoot { get; }
    string TrashRoot { get; }
    string RecoveryRoot { get; }
    string DatabasePath { get; }

    void EnsureCreated();
    void ValidateDatabasePath();
    string GetGuideRoot(Guid guideId);
    string GetStagedGuideRoot(Guid operationId, Guid guideId);
    string GetTrashedGuideRoot(Guid operationId, Guid guideId);
    string GetPlannedGuideFile(Guid guideId, string relativePath);
    string ResolveExistingGuideFile(Guid guideId, string relativePath);
}
