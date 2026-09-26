using DesktopGuides.Core.Paths;

namespace DesktopGuides.Infrastructure.Storage;

public sealed class ManagedPathResolver : ILibraryPaths
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public ManagedPathResolver(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new ArgumentException("An app-data root is required.", nameof(dataRoot));
        }

        DataRoot = Path.GetFullPath(dataRoot);
        LibraryRoot = Path.Combine(DataRoot, "library");
        ContentRoot = Path.Combine(LibraryRoot, "content");
        StagingRoot = Path.Combine(LibraryRoot, ".staging");
        TrashRoot = Path.Combine(LibraryRoot, ".trash");
        RecoveryRoot = Path.Combine(DataRoot, ".recovery");
        DatabasePath = Path.Combine(LibraryRoot, "library.sqlite");
    }

    public string DataRoot { get; }
    public string LibraryRoot { get; }
    public string ContentRoot { get; }
    public string StagingRoot { get; }
    public string TrashRoot { get; }
    public string RecoveryRoot { get; }
    public string DatabasePath { get; }

    public void EnsureCreated()
    {
        foreach (string path in new[]
                 { DataRoot, LibraryRoot, ContentRoot, StagingRoot, TrashRoot, RecoveryRoot })
        {
            RejectFilesystemLinks(path);
            Directory.CreateDirectory(path);
            RejectFilesystemLinks(path);
        }
    }

    public string GetGuideRoot(Guid guideId)
    {
        if (guideId == Guid.Empty)
        {
            throw new ArgumentException("A generated guide ID is required.", nameof(guideId));
        }

        RejectFilesystemLinks(ContentRoot);
        return Path.Combine(ContentRoot, guideId.ToString("N"));
    }

    public string GetPlannedGuideFile(Guid guideId, string relativePath)
    {
        string normalized = ManagedRelativePath.Parse(relativePath);
        string guideRoot = GetGuideRoot(guideId);
        string fullPath = Path.GetFullPath(
            Path.Combine(guideRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(guideRoot + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new InvalidDataException("Managed content path escapes its guide root.");
        }

        RejectFilesystemLinks(fullPath);
        return fullPath;
    }

    public string ResolveExistingGuideFile(Guid guideId, string relativePath)
    {
        string path = GetPlannedGuideFile(guideId, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Managed guide file is missing.", path);
        }

        RejectFilesystemLinks(path);
        return path;
    }

    private static void RejectFilesystemLinks(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)!;
        string current = root;
        foreach (string segment in Path.GetRelativePath(root, fullPath)
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Managed content path crosses a filesystem link.");
                }
            }
            catch (FileNotFoundException)
            {
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
        }
    }
}
