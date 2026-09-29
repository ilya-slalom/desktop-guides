using System.Security.Cryptography;
using System.Text;

namespace DesktopGuides.Infrastructure.Tests.Import;

internal sealed class ImportTestDirectory : IDisposable
{
    public ImportTestDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(),
            "desktop-guides-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(Root, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public string Write(string name, string text) => Write(name, Encoding.UTF8.GetBytes(text));

    public string Copy(string fixture, string name) =>
        Write(name, File.ReadAllBytes(P0Fixtures.Resolve(fixture)));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record FileFingerprint(string Path, string Sha256, DateTime LastWriteUtc)
{
    public static IReadOnlyList<FileFingerprint> Of(string fileOrDirectory)
    {
        IEnumerable<string> files = Directory.Exists(fileOrDirectory)
            ? Directory.EnumerateFiles(fileOrDirectory, "*", SearchOption.AllDirectories)
            : [fileOrDirectory];
        return files
            .Order(StringComparer.Ordinal)
            .Select(file => new FileFingerprint(
                file,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),
                File.GetLastWriteTimeUtc(file)))
            .ToArray();
    }
}
