namespace DesktopGuides.Infrastructure.Storage;

internal sealed class OwnedGuideTree
{
    private readonly List<string> files;
    private readonly List<string> directories;

    private OwnedGuideTree(List<string> files, List<string> directories)
    {
        this.files = files;
        this.directories = directories;
    }

    public bool Exists => directories.Count != 0;

    public static OwnedGuideTree Capture(string root)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(root);
        }
        catch (FileNotFoundException)
        {
            return new OwnedGuideTree([], []);
        }
        catch (DirectoryNotFoundException)
        {
            return new OwnedGuideTree([], []);
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            (attributes & FileAttributes.Directory) == 0)
        {
            throw new InvalidDataException("Expected managed guide directory is unsafe.");
        }

        List<string> files = [];
        List<string> directories = [root];
        for (int index = 0; index < directories.Count; index++)
        {
            foreach (string child in Directory.EnumerateFileSystemEntries(directories[index]))
            {
                FileAttributes childAttributes = File.GetAttributes(child);
                if ((childAttributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Managed guide directory contains a link.");
                }
                if ((childAttributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(child);
                }
                else
                {
                    files.Add(child);
                }
            }
        }
        return new OwnedGuideTree(files, directories);
    }

    public void Delete()
    {
        foreach (string file in files)
        {
            File.Delete(file);
        }
        for (int index = directories.Count - 1; index >= 0; index--)
        {
            Directory.Delete(directories[index]);
        }
    }
}
