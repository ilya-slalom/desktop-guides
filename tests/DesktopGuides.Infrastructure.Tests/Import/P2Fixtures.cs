namespace DesktopGuides.Infrastructure.Tests.Import;

internal static class P2Fixtures
{
    public static string Resolve(string relative) => Path.GetFullPath(
        Path.Combine(P0Fixtures.Root, "..", "p2", relative.Replace('/', Path.DirectorySeparatorChar)));

    // Copies a fixture folder's files, keeping their relative paths.
    public static void CopyTree(string fixture, string destination)
    {
        string source = Resolve(fixture);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
