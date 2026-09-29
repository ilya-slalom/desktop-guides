using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DesktopGuides.Core.Paths;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Artwork;

public sealed partial class ManagedArtworkStore(ILibraryPaths paths) : IArtworkStore
{
    [GeneratedRegex("^artwork/([0-9a-f]{32})/([0-9a-f]{64}\\.(?:png|jpg|webp))$")]
    private static partial Regex CanonicalPath();

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex GameFolder();

    [GeneratedRegex("^[0-9a-f]{64}\\.(?:png|jpg|webp)$")]
    private static partial Regex ArtworkFile();

    [GeneratedRegex("^[0-9a-f]{32}\\.tmp$")]
    private static partial Regex StagedFile();

    public async Task<StoredArtwork> StoreAsync(Guid gameId, byte[] content, CancellationToken token)
    {
        ArtworkInfo info = ArtworkValidator.Validate(content);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        string relativePath = $"artwork/{gameId:N}/{sha256}.{info.Extension}";
        string target = ResolvePath(relativePath)!;
        string staged = Path.Combine(paths.ArtworkStagingRoot, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (FileStream stream = new(staged, FileMode.CreateNew, FileAccess.Write))
            {
                await stream.WriteAsync(content, token);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            string folder = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(folder);
            if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("The artwork folder is a link and was not used.");
            }
            if (!File.Exists(target)) File.Move(staged, target);
            return new StoredArtwork(relativePath, sha256);
        }
        finally
        {
            File.Delete(staged);
        }
    }

    public void Delete(string relativePath)
    {
        if (ResolvePath(relativePath) is not { } file) return;
        try
        {
            File.Delete(file);
            string folder = Path.GetDirectoryName(file)!;
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public string? ResolveFile(string relativePath) =>
        ResolvePath(relativePath) is { } file && File.Exists(file) ? file : null;

    internal int Sweep(IReadOnlySet<string> referenced)
    {
        int review = 0;
        foreach (string entry in Directory.EnumerateFileSystemEntries(paths.ArtworkStagingRoot))
        {
            if (StagedFile().IsMatch(Path.GetFileName(entry)) && File.Exists(entry)) TryDelete(entry);
            else review++;
        }
        foreach (string entry in Directory.EnumerateFileSystemEntries(paths.ArtworkRoot))
        {
            string name = Path.GetFileName(entry);
            if (!GameFolder().IsMatch(name) || !Directory.Exists(entry) ||
                File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
            {
                review++;
                continue;
            }
            foreach (string file in Directory.EnumerateFileSystemEntries(entry))
            {
                string fileName = Path.GetFileName(file);
                if (!ArtworkFile().IsMatch(fileName) || !File.Exists(file)) review++;
                else if (!referenced.Contains($"artwork/{name}/{fileName}")) TryDelete(file);
            }
            if (!Directory.EnumerateFileSystemEntries(entry).Any()) Directory.Delete(entry);
        }
        return review;
    }

    private string? ResolvePath(string relativePath) =>
        CanonicalPath().Match(relativePath) is { Success: true } match
            ? Path.Combine(paths.ArtworkRoot, match.Groups[1].Value, match.Groups[2].Value)
            : null;

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
