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
    string GetGuideRoot(Guid guideId);
    string GetPlannedGuideFile(Guid guideId, string relativePath);
    string ResolveExistingGuideFile(Guid guideId, string relativePath);
}
